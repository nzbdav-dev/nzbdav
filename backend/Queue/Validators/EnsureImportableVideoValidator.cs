using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Streams;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Queue.Validators;

public class EnsureImportableVideoValidator(DavDatabaseClient dbClient, UsenetStreamingClient usenetClient, ConfigManager configManager)
{
    public async Task ThrowIfValidationFailsAsync(List<DavItem>? existingVideoFiles, CancellationToken ct = default)
    {
        Log.Debug("Starting enhanced video content validation...");

        if (!await IsValidAsync(existingVideoFiles, ct))
        {
            Log.Error("Video validation FAILED - No importable videos found. Throwing NoVideoFilesFoundException.");
            throw new NoVideoFilesFoundException("No importable videos found.");
        }

        Log.Debug("Video validation PASSED - Valid video content found.");
    }

    private async Task<bool> IsValidAsync(List<DavItem>? existingVideoFiles, CancellationToken ct)
    {
        var useChangeTracker = existingVideoFiles == null;
        var videoFiles = existingVideoFiles ?? dbClient.Ctx.ChangeTracker.Entries<DavItem>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .Where(x => x.Type != DavItem.ItemType.Directory)
            .Where(x => FilenameUtil.IsVideoFile(x.Name))
            .ToList();

        if (videoFiles.Count == 0)
        {
            Log.Warning("No video files found by filename extension - this should cause validation to fail");
            return false;
        }

        Log.Debug("Found {VideoFileCount} potential video files, validating content with ffprobe...", videoFiles.Count);

        // Log the files we found for debugging
        foreach (var file in videoFiles)
        {
            Log.Debug("Found potential video file: {FileName} (Type: {ItemType}, Size: {FileSize} bytes, Extension: {Extension})",
                file.Name, file.Type, file.FileSize, Path.GetExtension(file.Name).ToLowerInvariant());
        }

        // Check each video file with ffprobe to ensure it's actually valid video content
        var validVideoCount = 0;
        foreach (var videoFile in videoFiles)
        {
            try
            {
                Log.Debug("Validating video content for file: {FileName}", videoFile.Name);
                var isValid = await ValidateVideoContentAsync(videoFile, ct);

                string[]? segmentIds = null;

                switch (videoFile.Type)
                {
                    case DavItem.ItemType.NzbFile:
                        var nzbFile = useChangeTracker ?
                            dbClient.Ctx.ChangeTracker.Entries<DavNzbFile>()
                                .Where(x => x.State == EntityState.Added)
                                .Select(x => x.Entity)
                                .FirstOrDefault(x => x.Id == videoFile.Id) :
                            await dbClient.GetNzbFileAsync(videoFile.Id, ct);

                        if (nzbFile != null)
                        {
                            segmentIds = nzbFile.SegmentIds;
                        }
                        else
                        {
                            Log.Warning("Could not find NZB file data for {FilePath} (ID: {Id})", videoFile.Path, videoFile.Id);
                            isValid = false;
                        }
                        break;
                    case DavItem.ItemType.RarFile:
                        {
                            var rarFile = useChangeTracker ?
                                dbClient.Ctx.ChangeTracker.Entries<DavRarFile>()
                                    .Where(x => x.State == EntityState.Added)
                                    .Select(x => x.Entity)
                                    .FirstOrDefault(x => x.Id == videoFile.Id) :
                                await dbClient.Ctx.RarFiles.Where(x => x.Id == videoFile.Id).FirstOrDefaultAsync(ct);

                            if (rarFile != null)
                            {
                                segmentIds = rarFile.GetSegmentIds();
                            }
                            else
                            {
                                Log.Warning("Could not find RAR file data for {FilePath} (ID: {Id})", videoFile.Path, videoFile.Id);
                                isValid = false;
                            }

                            break;
                        }
                    default:
                        segmentIds = null;
                        break;
                }

                if (segmentIds != null)
                {
                    // for queue item processing, we want things to be fast, so we'll sample 5% of the segments
                    var samplePercentage = 5;
                    var thresholdPercentage = configManager.GetNzbSegmentThresholdPercentage();

                    try
                    {
                        var articlesArePresent = await usenetClient.CheckNzbFileHealth(segmentIds, samplePercentage, thresholdPercentage, ct);
                        if (!articlesArePresent)
                        {
                            Log.Warning("Missing usenet articles detected for {FilePath}: {Message}", videoFile.Path, "NZB file is missing articles");
                            isValid = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        // note: we don't consider this case invalid because it indicates a connection or
                        //       other transient issue.
                        Log.Error(ex, "Error checking NZB file health");
                    }

                    if (isValid)
                    {
                        validVideoCount++;
                        Log.Debug("Video file validation PASSED: {FileName}", videoFile.Name);
                    }
                    else
                    {
                        Log.Warning("Video file validation FAILED: {FileName} - not valid media content", videoFile.Name);
                    }
                }
                else
                {
                    Log.Warning("Could not find segment IDs for {FilePath} (ID: {Id})", videoFile.Path, videoFile.Id);
                }
            }
            catch (UsenetArticleNotFoundException ex)
            {
                Log.Warning("Missing usenet articles for video file: {FileName} - {Message}", videoFile.Name, ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error validating video file: {FileName} - treating as INVALID due to validation failure", videoFile.Name);
            }
        }

        var hasValidVideos = validVideoCount > 0;

        if (hasValidVideos)
        {
            Log.Debug("Video validation PASSED: {ValidCount}/{TotalCount} files contain valid video content",
                validVideoCount, videoFiles.Count);
        }
        else
        {
            Log.Error("Video validation FAILED: {ValidCount}/{TotalCount} files contain valid video content - no importable videos found",
                validVideoCount, videoFiles.Count);
        }

        return hasValidVideos;
    }

    private async Task<bool> ValidateVideoContentAsync(DavItem videoFile, CancellationToken ct)
    {
        try
        {
            Stream? stream = await ExtractStream(videoFile, ct);
            if (stream == null)
            {
                return false;
            }

            // Use FFMpegCore to analyze the stream for better accuracy
            var isValid = await FfprobeUtil.IsValidMediaStreamAsync(stream, videoFile.Name, false, ct);
            await stream.DisposeAsync();

            return isValid;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error validating video content for {FileName}", videoFile.Name);
            return false;
        }
    }

    private async Task<DavNzbFile?> GetNzbFileAsync(DavItem videoFile, CancellationToken ct)
    {
        // first, check the uncommited file changes
        var uncommittedNzbFile = dbClient.Ctx.ChangeTracker.Entries<DavNzbFile>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .FirstOrDefault(x => x.Id == videoFile.Id);

        return uncommittedNzbFile ?? await dbClient.GetNzbFileAsync(videoFile.Id, ct);
    }

    private async Task<DavRarFile?> GetRarFileAsync(DavItem videoFile, CancellationToken ct)
    {
        // first, check the uncommited file changes
        var uncommittedRarFile = dbClient.Ctx.ChangeTracker.Entries<DavRarFile>()
            .Where(x => x.State == EntityState.Added)
            .Select(x => x.Entity)
            .FirstOrDefault(x => x.Id == videoFile.Id);

        return uncommittedRarFile ?? await dbClient.Ctx.RarFiles.Where(x => x.Id == videoFile.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<Stream?> ExtractStream(DavItem videoFile, CancellationToken ct)
    {
        if (videoFile.Type == DavItem.ItemType.NzbFile)
        {
            var nzbFile = await GetNzbFileAsync(videoFile, ct);
            if (nzbFile == null)
            {
                Log.Warning("Could not find NZB file data for {FileName} (ID: {Id})", videoFile.Name, videoFile.Id);
                throw new NoVideoFilesFoundException(string.Format("Could not find NZB file data for {FileName} (ID: {Id})", videoFile.Name, videoFile.Id));
            }
            return usenetClient.GetFileStream(nzbFile.SegmentIds, videoFile.FileSize!.Value, 1); // Use 1 connection for validation
        }
        else if (videoFile.Type == DavItem.ItemType.RarFile)
        {
            var rarFile = await GetRarFileAsync(videoFile, ct);
            if (rarFile == null)
            {
                Log.Warning("Could not find RAR file data for {FileName} (ID: {Id})", videoFile.Name, videoFile.Id);
                throw new NoVideoFilesFoundException(string.Format("Could not find RAR file data for {FileName} (ID: {Id})", videoFile.Name, videoFile.Id));
            }
            return new RarFileStream(rarFile.RarParts, usenetClient, 1); // Use 1 connection for validation
        }

        Log.Debug("Skipping validation for unsupported file type: {FileName} (Type: {ItemType})", videoFile.Name, videoFile.Type);
        return null;
    }
}