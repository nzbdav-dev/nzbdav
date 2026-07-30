using System.Xml;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.SabControllers.GetQueue;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Extensions;
using NzbWebDAV.Queue;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Api.SabControllers.AddFile;

public class AddFileController(
    HttpContext httpContext,
    DavDatabaseClient dbClient,
    QueueManager queueManager,
    ConfigManager configManager,
    WebsocketManager websocketManager
) : SabApiController.BaseController(httpContext, configManager)
{
    private static readonly XmlReaderSettings XmlSettings = new()
    {
        Async = true,
        DtdProcessing = DtdProcessing.Ignore
    };

    /// <summary>
    /// Creates a short-lived context for conflict removal without flushing
    /// pending Added entities on the request-scoped context. Tests can override
    /// this to target the same temporary database as the request context.
    /// </summary>
    internal Func<DavDatabaseContext> FreshContextFactory { get; set; } = static () => new DavDatabaseContext();

    /// <summary>
    /// Test hook invoked after the duplicate pre-check and before the blob is written,
    /// so the UNIQUE retry path can be exercised without a real concurrent request.
    /// </summary>
    internal Func<Task>? AfterDuplicatePreCheckHook { get; set; }

    public async Task<AddFileResponse> AddFileAsync(AddFileRequest request)
    {
        var id = request.NzoId ?? Guid.NewGuid();
        var category = StringUtil.EmptyToNull(request.Category)
                       ?? configManager.GetManualUploadCategory();

        await HandleExistingQueueItemAsync(
                request.FileName,
                category,
                request.ReplaceExistingQueueItem,
                request.CancellationToken)
            .ConfigureAwait(false);
        if (AfterDuplicatePreCheckHook is not null)
            await AfterDuplicatePreCheckHook().ConfigureAwait(false);

        await using var sourceStream = request.NzbFileStream;
        QueueItem? queueItem;
        try
        {
            var prepared = await NzbStreamUtil.OpenMaybeCompressedAsync(
                    sourceStream, request.CancellationToken)
                .ConfigureAwait(false);
            await using var nzbInputStream = prepared.Stream;
            try
            {
                // Store normalized XML so every downstream parser remains
                // compression-agnostic.
                await BlobStore.WriteBlob(id, nzbInputStream, request.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException exception) when (prepared.IsGzip)
            {
                throw new BadHttpRequestException("The uploaded gzip NZB is invalid.", exception);
            }

            // backup the nzb file if enabled
            if (configManager.IsNzbBackupEnabled())
            {
                var backupLocation = configManager.GetNzbBackupLocation();
                if (backupLocation != null)
                {
                    await BackupNzbAsync(id, request.FileName, category, backupLocation);
                }
            }

            // compute the total segment bytes
            await using var nzbFileStream = BlobStore.ReadBlob(id)!;
            long totalSegmentBytes;
            try
            {
                totalSegmentBytes = ComputeTotalSegmentBytes(nzbFileStream);
            }
            catch (XmlException exception) when (prepared.IsGzip)
            {
                throw new BadHttpRequestException(
                    "The uploaded gzip file does not contain a valid NZB document.",
                    exception);
            }

            // create the queue item record
            queueItem = new QueueItem
            {
                Id = id,
                CreatedAt = DateTime.Now,
                FileName = request.FileName,
                JobName = FilenameUtil.GetJobName(request.FileName),
                NzbFileSize = nzbFileStream.Length,
                TotalSegmentBytes = totalSegmentBytes,
                Category = category,
                Priority = request.Priority,
                PostProcessing = request.PostProcessing,
                PauseUntil = request.PauseUntil,
                IndexerName = request.IndexerName,
                ContentGroupKey = request.ContentGroupKey,
            };

            // record the original NZB filename so it can be served at download time
            var nzbName = new NzbName
            {
                Id = id,
                FileName = request.FileName
            };

            // save — never Clear() the change tracker here: WebDAV watch-folder create
            // reads the new QueueItem from the tracker after AddFileAsync returns.
            dbClient.Ctx.QueueItems.Add(queueItem);
            dbClient.Ctx.NzbNames.Add(nzbName);
            try
            {
                await dbClient.Ctx.SaveChangesAsync(request.CancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (
                request.ReplaceExistingQueueItem && IsCategoryFileNameUniqueViolation(ex))
            {
                // TOCTOU: another insert landed after our pre-check. Remove via a fresh
                // context so this request context's pending Added entities are not flushed
                // by RemoveQueueItemsAsync's inner SaveChangesAsync, then retry once.
                await RemoveConflictingQueueItemViaFreshContextAsync(
                        request.FileName, category, request.CancellationToken)
                    .ConfigureAwait(false);
                await dbClient.Ctx.SaveChangesAsync(request.CancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (IsCategoryFileNameUniqueViolation(ex))
            {
                throw new BadHttpRequestException(
                    $"A queue item named '{request.FileName}' already exists in category '{category}'.",
                    ex);
            }

            _ = DavDatabaseContext.RcloneVfsForget(["/nzbs"]);
        }
        catch
        {
            // Delete partial or unreferenced blobs after ingest/database failures.
            BlobStore.Delete(id);
            throw;
        }

        // inform the frontend that a new item was added to the queue
        var message = GetQueueResponse.QueueSlot.FromQueueItem(queueItem).ToJson();
        _ = websocketManager.SendMessage(WebsocketTopic.QueueItemAdded, message);

        // awaken the queue if it is sleeping
        queueManager.AwakenQueue(request.PauseUntil);

        // return response
        return new AddFileResponse()
        {
            Status = true,
            NzoIds = [queueItem.Id.ToString()],
        };
    }

    private async Task HandleExistingQueueItemAsync(
        string fileName,
        string category,
        bool replaceExisting,
        CancellationToken ct)
    {
        var existingId = await dbClient.Ctx.QueueItems.AsNoTracking()
            .Where(q => q.Category == category && q.FileName == fileName)
            .Select(q => (Guid?)q.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (existingId is null) return;

        if (!replaceExisting)
            throw new BadHttpRequestException(
                $"A queue item named '{fileName}' already exists in category '{category}'.");

        var wasInProgress = queueManager.FindInProgressQueueItem(existingId.Value) is not null;
        Log.Warning(
            "Replacing existing queue item {QueueItemId} ({FileName} in {Category}) on re-add{InProgressSuffix}",
            existingId.Value,
            fileName,
            category,
            wasInProgress ? "; cancelling in-progress download" : "");

        await queueManager.RemoveQueueItemsAsync([existingId.Value], dbClient, ct).ConfigureAwait(false);
        _ = websocketManager.SendMessage(WebsocketTopic.QueueItemRemoved, existingId.Value.ToString());
        _ = DavDatabaseContext.RcloneVfsForget(["/nzbs"]);
    }

    private async Task RemoveConflictingQueueItemViaFreshContextAsync(
        string fileName,
        string category,
        CancellationToken ct)
    {
        await using var freshCtx = FreshContextFactory();
        var freshClient = new DavDatabaseClient(freshCtx);
        var conflictingId = await freshCtx.QueueItems.AsNoTracking()
            .Where(q => q.Category == category && q.FileName == fileName)
            .Select(q => (Guid?)q.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (conflictingId is null) return;

        var wasInProgress = queueManager.FindInProgressQueueItem(conflictingId.Value) is not null;
        Log.Warning(
            "Replacing existing queue item {QueueItemId} ({FileName} in {Category}) after UNIQUE conflict on re-add{InProgressSuffix}",
            conflictingId.Value,
            fileName,
            category,
            wasInProgress ? "; cancelling in-progress download" : "");

        await queueManager.RemoveQueueItemsAsync([conflictingId.Value], freshClient, ct).ConfigureAwait(false);
        _ = websocketManager.SendMessage(WebsocketTopic.QueueItemRemoved, conflictingId.Value.ToString());
        _ = DavDatabaseContext.RcloneVfsForget(["/nzbs"]);
    }

    internal static bool IsCategoryFileNameUniqueViolation(DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is not SqliteException sqlite) continue;
            if (sqlite.SqliteErrorCode is not 19) continue; // SQLITE_CONSTRAINT

            var message = sqlite.Message;
            if (message.Contains("IX_QueueItems_Category_FileName", StringComparison.OrdinalIgnoreCase))
                return true;
            if (message.Contains("QueueItems.Category", StringComparison.OrdinalIgnoreCase)
                && message.Contains("QueueItems.FileName", StringComparison.OrdinalIgnoreCase))
                return true;
            if (message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                && message.Contains("Category", StringComparison.OrdinalIgnoreCase)
                && message.Contains("FileName", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    protected override async Task<IActionResult> Handle()
    {
        var request = await AddFileRequest.New(httpContext, configManager).ConfigureAwait(false);
        return Ok(await AddFileAsync(request).ConfigureAwait(false));
    }

    private static async Task BackupNzbAsync(Guid id, string fileName, string category, string backupLocation)
    {
        try
        {
            ValidateBackupCategory(category);

            var backupRoot = Path.GetFullPath(backupLocation);
            var backupRootPrefix = Path.EndsInDirectorySeparator(backupRoot)
                ? backupRoot
                : backupRoot + Path.DirectorySeparatorChar;
            if (!Directory.Exists(backupRoot))
                Directory.CreateDirectory(backupRoot);

            var destDir = Path.GetFullPath(Path.Combine(backupRootPrefix, category));
            if (!destDir.StartsWith(backupRootPrefix, StringComparison.Ordinal))
                throw new ArgumentException("The NZB backup category must stay within the configured directory.");
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            var destDirPrefix = Path.EndsInDirectorySeparator(destDir)
                ? destDir
                : destDir + Path.DirectorySeparatorChar;
            var leafName = Path.GetFileName(fileName);
            var baseName = Path.GetFileNameWithoutExtension(leafName);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = id.ToString();
            var ext = Path.GetExtension(leafName);
            if (string.IsNullOrEmpty(ext)) ext = ".nzb";

            var destPath = Path.GetFullPath(Path.Combine(destDirPrefix, $"{baseName}{ext}"));
            if (!destPath.StartsWith(destDirPrefix, StringComparison.Ordinal))
                throw new ArgumentException("The NZB backup file must stay within its category directory.");
            var counter = 2;
            while (System.IO.File.Exists(destPath))
            {
                destPath = Path.GetFullPath(Path.Combine(destDirPrefix, $"{baseName} ({counter}){ext}"));
                if (!destPath.StartsWith(destDirPrefix, StringComparison.Ordinal))
                    throw new ArgumentException("The NZB backup file must stay within its category directory.");
                counter++;
            }

            await using var src = BlobStore.ReadBlob(id);
            await using var dst = System.IO.File.Create(destPath);
            await src!.CopyToAsync(dst);
        }
        catch (Exception e)
        {
            throw new Exception($"Could not save nzb to `{backupLocation}`", e);
        }
    }

    private static void ValidateBackupCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category) ||
            Path.IsPathRooted(category) ||
            category is "." or ".." ||
            category.Contains('/') ||
            category.Contains('\\') ||
            category.Contains('\0'))
        {
            throw new ArgumentException("The NZB backup category must be a single directory name.", nameof(category));
        }
    }

    private static long ComputeTotalSegmentBytes(Stream stream)
    {
        long totalBytes = 0;
        using var reader = XmlReader.Create(stream, XmlSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "segment") continue;
            var bytesAttr = reader.GetAttribute("bytes");
            if (bytesAttr != null && long.TryParse(bytesAttr, out var bytes))
            {
                totalBytes += bytes;
            }
        }

        return totalBytes;
    }
}
