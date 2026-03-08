using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Services;
using NzbWebDAV.Utils;

namespace backend.Tests.Services;

[Collection(nameof(ContentIndexDatabaseCollection))]
public sealed class ContentIndexRecoveryServiceTests
{
    private readonly ContentIndexDatabaseFixture _fixture;

    public ContentIndexRecoveryServiceTests(ContentIndexDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartupRecovery_RestoresAllContent_WhenDatabaseComesUpEmpty()
    {
        var configManager = _fixture.CreateConfigManager();
        var expectedItemId = Guid.NewGuid();

        await using (var dbContext = await _fixture.ResetAndCreateMigratedContextAsync())
        {
            var movieDirectory = CreateDirectory("movies", DavItem.ContentFolder);
            var movieFile = CreateNzbFile(expectedItemId, movieDirectory, "Example.mkv");

            dbContext.Items.AddRange(movieDirectory, movieFile);
            dbContext.NzbFiles.Add(new DavNzbFile
            {
                Id = movieFile.Id,
                SegmentIds = ["segment-1", "segment-2"],
            });

            await dbContext.SaveChangesAsync();
        }

        await _fixture.RecreateDatabaseAsync();

        var recoveryService = new ContentIndexRecoveryService(configManager);
        await recoveryService.StartAsync(CancellationToken.None);

        await using var restoredContext = await _fixture.CreateMigratedContextAsync();
        Assert.Equal(
            2,
            await restoredContext.Items.CountAsync(x =>
                x.Path.StartsWith(ContentPathUtil.ForwardSlashPrefix) || x.Path.StartsWith(ContentPathUtil.BackslashPrefix))
        );
        Assert.Equal(["segment-1", "segment-2"], (await restoredContext.NzbFiles.SingleAsync()).SegmentIds);
    }

    [Fact]
    public async Task StartupRecovery_RestoresMissingMetadata_ForExistingContentItem()
    {
        var configManager = _fixture.CreateConfigManager();
        var expectedItemId = Guid.NewGuid();

        await using (var dbContext = await _fixture.ResetAndCreateMigratedContextAsync())
        {
            var movieDirectory = CreateDirectory("movies", DavItem.ContentFolder);
            var movieFile = CreateNzbFile(expectedItemId, movieDirectory, "Example.mkv");

            dbContext.Items.AddRange(movieDirectory, movieFile);
            dbContext.NzbFiles.Add(new DavNzbFile
            {
                Id = movieFile.Id,
                SegmentIds = ["segment-1", "segment-2"],
            });

            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = await _fixture.CreateMigratedContextAsync())
        {
            await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM DavNzbFiles WHERE Id = {0}", expectedItemId);
        }

        var recoveryService = new ContentIndexRecoveryService(configManager);
        await recoveryService.StartAsync(CancellationToken.None);

        await using var restoredContext = await _fixture.CreateMigratedContextAsync();
        Assert.Single(await restoredContext.NzbFiles.Where(x => x.Id == expectedItemId).ToListAsync());
    }

    [Fact]
    public async Task StartupRecovery_RestoresLinkedMissingItems_WhenDatabaseIsPartiallyMissing()
    {
        var linkedItemId = Guid.NewGuid();
        var missingDirectoryId = Guid.NewGuid();

        await using (var dbContext = await _fixture.ResetAndCreateMigratedContextAsync())
        {
            var tvDirectory = CreateDirectory("tv", DavItem.ContentFolder);
            var tvFile = CreateNzbFile(Guid.NewGuid(), tvDirectory, "Existing.mkv");
            var movieDirectory = CreateDirectory(missingDirectoryId, "movies", DavItem.ContentFolder);
            var movieFile = CreateNzbFile(linkedItemId, movieDirectory, "Missing.mkv");

            dbContext.Items.AddRange(tvDirectory, tvFile, movieDirectory, movieFile);
            dbContext.NzbFiles.AddRange(
                new DavNzbFile { Id = tvFile.Id, SegmentIds = ["tv-segment"] },
                new DavNzbFile { Id = movieFile.Id, SegmentIds = ["movie-segment"] }
            );

            await dbContext.SaveChangesAsync();
        }

        var libraryPath = _fixture.CreateLibraryDirectory();
        var configManager = _fixture.CreateConfigManager(libraryPath);
        await File.WriteAllTextAsync(
            Path.Join(libraryPath, "Missing.strm"),
            $"http://localhost:3000/view/.ids/{linkedItemId}.mkv?downloadKey=test&extension=mkv"
        );

        await using (var dbContext = await _fixture.CreateMigratedContextAsync())
        {
            await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM DavItems WHERE Id = {0}", linkedItemId);
            await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM DavItems WHERE Id = {0}", missingDirectoryId);
        }

        var recoveryService = new ContentIndexRecoveryService(configManager);
        await recoveryService.StartAsync(CancellationToken.None);

        await using var restoredContext = await _fixture.CreateMigratedContextAsync();
        var restoredPaths = await restoredContext.Items
            .Where(x => x.Path.StartsWith(ContentPathUtil.ForwardSlashPrefix) || x.Path.StartsWith(ContentPathUtil.BackslashPrefix))
            .Select(x => ContentPathUtil.NormalizeSeparators(x.Path))
            .ToListAsync();

        Assert.Contains("/content/movies", restoredPaths);
        Assert.Contains("/content/movies/Missing.mkv", restoredPaths);
        Assert.Single(await restoredContext.NzbFiles.Where(x => x.Id == linkedItemId).ToListAsync());
    }

    [Fact]
    public async Task StartupRecovery_IgnoresUnsupportedSnapshotVersion()
    {
        var configManager = _fixture.CreateConfigManager();

        await _fixture.ResetAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(ContentIndexSnapshotStore.SnapshotFilePath)!);
        await File.WriteAllTextAsync(
            ContentIndexSnapshotStore.SnapshotFilePath,
            """{"Version":999,"GeneratedAtUtc":"2026-03-08T00:00:00+00:00","Items":[],"NzbFiles":[],"RarFiles":[],"MultipartFiles":[]}"""
        );

        await _fixture.RecreateDatabaseAsync();

        var recoveryService = new ContentIndexRecoveryService(configManager);
        await recoveryService.StartAsync(CancellationToken.None);

        await using var restoredContext = await _fixture.CreateMigratedContextAsync();
        Assert.Equal(
            0,
            await restoredContext.Items.CountAsync(x =>
                x.Path.StartsWith(ContentPathUtil.ForwardSlashPrefix) || x.Path.StartsWith(ContentPathUtil.BackslashPrefix))
        );
    }

    [Fact]
    public async Task StartupRecovery_FallsBackToBackupSnapshot_WhenPrimarySnapshotIsCorrupt()
    {
        var configManager = _fixture.CreateConfigManager();
        var expectedItemId = Guid.NewGuid();

        await using (var dbContext = await _fixture.ResetAndCreateMigratedContextAsync())
        {
            var movieDirectory = CreateDirectory("movies", DavItem.ContentFolder);
            var movieFile = CreateNzbFile(expectedItemId, movieDirectory, "Example.mkv");

            dbContext.Items.AddRange(movieDirectory, movieFile);
            dbContext.NzbFiles.Add(new DavNzbFile
            {
                Id = movieFile.Id,
                SegmentIds = ["segment-1"],
            });

            await dbContext.SaveChangesAsync();
        }

        await File.WriteAllTextAsync(ContentIndexSnapshotStore.SnapshotFilePath, "not-json");
        await _fixture.RecreateDatabaseAsync();

        var recoveryService = new ContentIndexRecoveryService(configManager);
        await recoveryService.StartAsync(CancellationToken.None);

        await using var restoredContext = await _fixture.CreateMigratedContextAsync();
        Assert.Single(await restoredContext.NzbFiles.Where(x => x.Id == expectedItemId).ToListAsync());
    }

    [Fact]
    public async Task StartupRecovery_DoesNotBringBackDeletedItems_WhenSnapshotWasUpdatedAfterDeletion()
    {
        var configManager = _fixture.CreateConfigManager();
        var deletedItemId = Guid.NewGuid();

        await using (var dbContext = await _fixture.ResetAndCreateMigratedContextAsync())
        {
            var movieDirectory = CreateDirectory("movies", DavItem.ContentFolder);
            var movieFile = CreateNzbFile(deletedItemId, movieDirectory, "Deleted.mkv");

            dbContext.Items.AddRange(movieDirectory, movieFile);
            dbContext.NzbFiles.Add(new DavNzbFile
            {
                Id = movieFile.Id,
                SegmentIds = ["segment-1"],
            });

            await dbContext.SaveChangesAsync();
            dbContext.Items.Remove(movieFile);
            await dbContext.SaveChangesAsync();
        }

        await _fixture.RecreateDatabaseAsync();

        var recoveryService = new ContentIndexRecoveryService(configManager);
        await recoveryService.StartAsync(CancellationToken.None);

        await using var restoredContext = await _fixture.CreateMigratedContextAsync();
        Assert.DoesNotContain(await restoredContext.Items.Select(x => x.Id).ToListAsync(), x => x == deletedItemId);
    }

    [Fact]
    public async Task SnapshotWriter_PreservesLastKnownGoodSnapshot_WhenMetadataRowsDisappear()
    {
        var expectedItemId = Guid.NewGuid();

        await using (var dbContext = await _fixture.ResetAndCreateMigratedContextAsync())
        {
            var movieDirectory = CreateDirectory("movies", DavItem.ContentFolder);
            var movieFile = CreateNzbFile(expectedItemId, movieDirectory, "Example.mkv");

            dbContext.Items.AddRange(movieDirectory, movieFile);
            dbContext.NzbFiles.Add(new DavNzbFile
            {
                Id = movieFile.Id,
                SegmentIds = ["segment-1", "segment-2"],
            });

            await dbContext.SaveChangesAsync();
        }

        var originalSnapshot = await File.ReadAllTextAsync(ContentIndexSnapshotStore.SnapshotFilePath);

        await using (var dbContext = await _fixture.CreateMigratedContextAsync())
        {
            dbContext.NzbFiles.Remove(await dbContext.NzbFiles.SingleAsync(x => x.Id == expectedItemId));
            await dbContext.SaveChangesAsync();
        }

        var snapshotAfterCorruption = await File.ReadAllTextAsync(ContentIndexSnapshotStore.SnapshotFilePath);
        Assert.Equal(originalSnapshot, snapshotAfterCorruption);
    }

    private static DavItem CreateDirectory(string name, DavItem parent)
    {
        return CreateDirectory(Guid.NewGuid(), name, parent);
    }

    private static DavItem CreateDirectory(Guid id, string name, DavItem parent)
    {
        return DavItem.New(id, parent, name, null, DavItem.ItemType.Directory, null, null);
    }

    private static DavItem CreateNzbFile(Guid id, DavItem parent, string name)
    {
        return DavItem.New(id, parent, name, 1024, DavItem.ItemType.NzbFile, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
}

public sealed class ContentIndexDatabaseFixture : IAsyncLifetime
{
    private readonly string _configPath = Path.Join(Path.GetTempPath(), "nzbdav-tests", "content-index-recovery");

    public ContentIndexDatabaseFixture()
    {
        Environment.SetEnvironmentVariable("CONFIG_PATH", _configPath);
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_configPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return ResetAsync();
    }

    public async Task<DavDatabaseContext> ResetAndCreateMigratedContextAsync()
    {
        await ResetAsync();
        return await CreateMigratedContextAsync();
    }

    public async Task<DavDatabaseContext> CreateMigratedContextAsync()
    {
        Directory.CreateDirectory(_configPath);
        var dbContext = new DavDatabaseContext();
        await dbContext.Database.MigrateAsync();
        return dbContext;
    }

    public async Task RecreateDatabaseAsync()
    {
        await EnsureCleanDatabaseAsync(deleteSnapshots: false);
    }

    public async Task ResetAsync()
    {
        await EnsureCleanDatabaseAsync(deleteSnapshots: true);
        DeleteDirectoryIfExists(Path.Join(_configPath, "library"));
    }

    public string CreateLibraryDirectory()
    {
        var libraryPath = Path.Join(_configPath, "library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(libraryPath);
        return libraryPath;
    }

    public ConfigManager CreateConfigManager(string? libraryPath = null)
    {
        var configManager = new ConfigManager();
        var items = new List<ConfigItem>
        {
            new() { ConfigName = "rclone.mount-dir", ConfigValue = "/mnt/nzbdav" }
        };

        if (libraryPath != null)
        {
            items.Add(new ConfigItem
            {
                ConfigName = "media.library-dir",
                ConfigValue = libraryPath
            });
        }

        configManager.UpdateValues(items);
        return configManager;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private async Task EnsureCleanDatabaseAsync(bool deleteSnapshots)
    {
        Directory.CreateDirectory(_configPath);

        await using var dbContext = new DavDatabaseContext();
        await dbContext.Database.MigrateAsync();
        await dbContext.HealthCheckResults.ExecuteDeleteAsync();
        await dbContext.HealthCheckStats.ExecuteDeleteAsync();
        await dbContext.QueueNzbContents.ExecuteDeleteAsync();
        await dbContext.QueueItems.ExecuteDeleteAsync();
        await dbContext.BlobCleanupItems.ExecuteDeleteAsync();
        await dbContext.HistoryItems.ExecuteDeleteAsync();
        await dbContext.Items
            .Where(x => x.Path.StartsWith(ContentPathUtil.ForwardSlashPrefix) || x.Path.StartsWith(ContentPathUtil.BackslashPrefix))
            .ExecuteDeleteAsync();

        if (!deleteSnapshots) return;

        DeleteIfExists(ContentIndexSnapshotStore.SnapshotFilePath);
        DeleteIfExists(ContentIndexSnapshotStore.BackupSnapshotFilePath);
    }
}

[CollectionDefinition(nameof(ContentIndexDatabaseCollection), DisableParallelization = true)]
public sealed class ContentIndexDatabaseCollection : ICollectionFixture<ContentIndexDatabaseFixture>
{
}
