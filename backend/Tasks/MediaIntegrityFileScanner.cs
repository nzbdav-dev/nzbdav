using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Utils;
using Serilog;

namespace NzbWebDAV.Tasks;

public class MediaIntegrityFileScanner
{
    private readonly string? _scanDirectory;
    private readonly int _maxFiles;
    private readonly int _recheckEligibilityDays;

    public MediaIntegrityFileScanner(
        ConfigManager configManager,
        IntegrityCheckRunParameters runParams
    )
    {
        _scanDirectory = runParams.ScanDirectory ?? configManager.GetLibraryDir();
        _maxFiles = runParams.MaxFilesToCheck;
        _recheckEligibilityDays = configManager.GetIntegrityCheckRecheckEligibilityDays();
    }

    public async Task<List<IntegrityCheckItem>> GetIntegrityCheckItemsAsync(CancellationToken ct)
    {
        List<IntegrityCheckItem> checkItems;
        if (!string.IsNullOrEmpty(_scanDirectory) && Directory.Exists(_scanDirectory))
        {
            // Use specified directory for scanning - resolve symlinks to DavItems
            checkItems = await GetLibraryIntegrityCheckItemsAsync(_scanDirectory, _maxFiles, ct);
        }
        else
        {
            // Fallback to internal files for non-library usage
            var davItems = await GetDavItemsToCheckAsync(_maxFiles, ct);
            checkItems = davItems.Select(item => new IntegrityCheckItem
            {
                DavItem = item,
                LibraryFilePath = null // No library file path for internal items
            }).ToList();
        }

        // Apply max files limit from parameters for internal files only
        // (Library files are already limited during scan)
        if (string.IsNullOrEmpty(_scanDirectory) && checkItems.Count > _maxFiles)
        {
            checkItems = checkItems.Take(_maxFiles).ToList();
        }

        return checkItems;
    }

    private async Task<List<DavItem>> GetDavItemsToCheckAsync(int maxFiles, CancellationToken ct)
    {
        var cutoffTime = DateTime.UtcNow.AddDays(-_recheckEligibilityDays);

        await using var dbContext = new DavDatabaseContext();
        var dbClient = new DavDatabaseClient(dbContext);

        // Get all media files (NzbFile and RarFile types) that haven't been checked recently
        var query = dbClient.Ctx.Items
            .Where(item => item.Type == DavItem.ItemType.NzbFile || item.Type == DavItem.ItemType.RarFile)
            .Where(item => item.FileSize > 0); // Only check files with actual content

        // Filter out recently checked files using the IntegrityCheckFileResults table
        var recentlyCheckedFileIds = await dbClient.Ctx.IntegrityCheckFileResults
            .Where(r => r.LastChecked > cutoffTime && !r.IsLibraryFile)
            .Select(r => r.FileId)
            .Distinct()
            .ToHashSetAsync(ct);

        if (recentlyCheckedFileIds.Any())
        {
            query = query.Where(item => !recentlyCheckedFileIds.Contains(item.Id.ToString()));
        }

        return await query.Take(maxFiles).ToListAsync(ct);
    }

    private async Task<List<IntegrityCheckItem>> GetLibraryIntegrityCheckItemsAsync(string libraryDir, int maxFiles, CancellationToken ct)
    {
        var checkItems = new List<IntegrityCheckItem>();
        var cutoffTime = DateTime.UtcNow.AddDays(-_recheckEligibilityDays);

        try
        {
            // Get media files in library directory recursively (use enumerable for efficiency)
            var allFiles = Directory.EnumerateFiles(libraryDir, "*", SearchOption.AllDirectories)
                .Where(FilenameUtil.IsVideoFile);

            Log.Information("Scanning library directory for media files...");

            // Get recently checked library files from the new table
            await using var dbContext = new DavDatabaseContext();
            var dbClient = new DavDatabaseClient(dbContext);
            var recentlyCheckedPaths = await dbClient.Ctx.IntegrityCheckFileResults
                .Where(r => r.LastChecked > cutoffTime && r.IsLibraryFile)
                .Select(r => r.FilePath)
                .Distinct()
                .ToHashSetAsync(ct);

            if (recentlyCheckedPaths.Any())
            {
                allFiles = allFiles.Where(filePath => !recentlyCheckedPaths.Contains(filePath));
            }

            var totalProcessed = 0;

            // Resolve symlinks to DavItems and filter out recently checked files
            foreach (var filePath in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                totalProcessed++;

                try
                {
                    // Resolve symlink to get the target path (should be in .ids directory)
                    var fileInfo = new FileInfo(filePath);
                    if (!fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        Log.Debug("Skipping non-symlink file: {FilePath}", filePath);
                        continue; // Not a symlink, skip
                    }

                    var targetPath = fileInfo.ResolveLinkTarget(true)?.FullName;
                    if (string.IsNullOrEmpty(targetPath))
                    {
                        Log.Warning("Could not resolve symlink target for: {FilePath}", filePath);
                        continue;
                    }

                    // Extract the GUID from the target path (should be the filename in .ids directory)
                    var targetFileName = Path.GetFileName(targetPath);
                    if (!Guid.TryParse(targetFileName, out var davItemId))
                    {
                        Log.Debug("Target path does not contain valid GUID: {TargetPath}", targetPath);
                        continue;
                    }

                    // Look up the DavItem by ID - first check if it exists at all
                    await using var lookupDbContext = new DavDatabaseContext();
                    var lookupDbClient = new DavDatabaseClient(lookupDbContext);
                    var anyDavItem = await lookupDbClient.Ctx.Items
                        .Where(item => item.Id == davItemId)
                        .FirstOrDefaultAsync(ct);

                    if (anyDavItem == null)
                    {
                        // DavItem doesn't exist - likely imported outside nzbdav or deleted
                        Log.Debug("Skipping {FilePath}: DavItem {DavItemId} not found (file may have been imported outside nzbdav)",
                            Path.GetFileName(filePath), davItemId);
                        continue;
                    }

                    // Check if it's a streamable file type (NZB or RAR)
                    if (anyDavItem.Type != DavItem.ItemType.NzbFile && anyDavItem.Type != DavItem.ItemType.RarFile)
                    {
                        Log.Debug("Skipping {FilePath}: DavItem is {ItemType} (only NZB and RAR files supported for streaming integrity check)",
                            Path.GetFileName(filePath), anyDavItem.Type);
                        continue;
                    }

                    // Valid streamable file found
                    checkItems.Add(new IntegrityCheckItem
                    {
                        DavItem = anyDavItem,
                        LibraryFilePath = filePath
                    });
                    Log.Debug("Added {FilePath} for integrity check (DavItem: {DavItemPath}, Type: {ItemType})",
                        Path.GetFileName(filePath), anyDavItem.Path, anyDavItem.Type);

                    // Limit the number of files per run
                    if (checkItems.Count >= maxFiles)
                    {
                        Log.Information("Reached file limit of {MaxFiles}, stopping scan", maxFiles);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error processing library file: {FilePath}", filePath);
                }
            }

            var skippedCount = totalProcessed - checkItems.Count + recentlyCheckedPaths.Count;
            Log.Information("Library scan complete: {ResolvedCount} files ready for integrity check, {SkippedCount} files skipped",
                checkItems.Count, skippedCount);

            if (skippedCount > 0)
            {
                Log.Information("Skipped files are either: files checked recently, files imported outside nzbdav, deleted DavItems, or unsupported file types");
            }

            return checkItems;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error scanning library directory: {LibraryDir}", libraryDir);
            return checkItems;
        }
    }
}
