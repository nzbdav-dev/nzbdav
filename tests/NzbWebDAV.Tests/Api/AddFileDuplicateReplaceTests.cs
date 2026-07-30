using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Api.SabControllers.AddFile;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Interceptors;
using NzbWebDAV.Database.MigrationHelpers;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Queue;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Services.StreamTrace;
using NzbWebDAV.Tests.Database;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Tests.Api;

[Collection(nameof(ConfigPathCollection))]
public sealed class AddFileDuplicateReplaceTests : IAsyncLifetime
{
    private readonly string _configRoot =
        Path.Combine(Path.GetTempPath(), $"nzbdav-addfile-cfg-{Guid.NewGuid():N}");
    private string? _previousConfigPath;
    private DbContextOptions<DavDatabaseContext> _options = null!;
    private DavDatabaseContext _context = null!;
    private DavDatabaseClient _dbClient = null!;
    private QueueManager _queueManager = null!;
    private ConfigManager _configManager = null!;
    private WebsocketManager _websocketManager = null!;

    public async Task InitializeAsync()
    {
        _previousConfigPath = Environment.GetEnvironmentVariable("CONFIG_PATH");
        Directory.CreateDirectory(_configRoot);
        Environment.SetEnvironmentVariable("CONFIG_PATH", _configRoot);

        _options = new DbContextOptionsBuilder<DavDatabaseContext>()
            .UseSqlite($"Data Source={DavDatabaseContext.DatabaseFilePath}")
            .AddInterceptors(new SqliteForeignKeyEnabler())
            .ReplaceService<
                IMigrationsSqlGenerator,
                SqliteMigrationsSqlGenerator<SqliteMigrationsSqlGenerator>>()
            .Options;
        _context = new DavDatabaseContext(_options);
        await _context.Database.MigrateAsync();
        _dbClient = new DavDatabaseClient(_context);

        _configManager = new ConfigManager();
        _configManager.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.UsenetProviders,
                ConfigValue = JsonSerializer.Serialize(new UsenetProviderConfig()),
            },
        ]);

        _websocketManager = new WebsocketManager();
        var usenet = new UsenetStreamingClient(
            _configManager,
            _websocketManager,
            new ProviderUsageTracker(),
            new MetricsWriter(),
            new ProviderBytesTracker(),
            new StreamTraceBuffer(100),
            new ActiveReadRegistry());
        _queueManager = new QueueManager(
            usenet,
            _configManager,
            _websocketManager,
            new ProviderUsageTracker(),
            new WatchdogLog(),
            new QueueItemSourceTracker(),
            new BenchmarkGate(),
            startLoop: false);
    }

    public async Task DisposeAsync()
    {
        _queueManager.Dispose();
        await _context.DisposeAsync();
        Environment.SetEnvironmentVariable("CONFIG_PATH", _previousConfigPath);
        try { Directory.Delete(_configRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task AddFileAsync_ReplacesExistingQueueItemWithSameCategoryAndFileName()
    {
        var existingId = Guid.NewGuid();
        const string fileName = "Sliders.S01E01.part.1.Pilot.nzb";
        const string category = "tv";

        _context.QueueItems.Add(new QueueItem
        {
            Id = existingId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            FileName = fileName,
            JobName = "Sliders.S01E01.part.1.Pilot",
            NzbFileSize = 10,
            TotalSegmentBytes = 10,
            Category = category,
            Priority = QueueItem.PriorityOption.Normal,
            PostProcessing = QueueItem.PostProcessingOption.None,
        });
        _context.NzbNames.Add(new NzbName { Id = existingId, FileName = fileName });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var controller = CreateController();
        var response = await controller.AddFileAsync(CreateRequest(fileName, category));

        Assert.True(response.Status);
        Assert.Single(response.NzoIds);
        var newId = Guid.Parse(response.NzoIds[0]);
        Assert.NotEqual(existingId, newId);

        Assert.Null(await _context.QueueItems.AsNoTracking()
            .SingleOrDefaultAsync(q => q.Id == existingId));
        var replacement = await _context.QueueItems.AsNoTracking()
            .SingleAsync(q => q.Category == category && q.FileName == fileName);
        Assert.Equal(newId, replacement.Id);

        // Watch-folder create reads the new QueueItem from the request change tracker.
        Assert.Contains(_context.ChangeTracker.Entries<QueueItem>(),
            e => e.Entity.Id == newId);
        Assert.NotNull(BlobStore.ReadBlob(newId));
    }

    [Fact]
    public async Task AddFileAsync_RetriesSaveAfterUniqueConflictInsertedBetweenPreCheckAndSave()
    {
        const string fileName = "Sliders.S01E01.part.2.Pilot.nzb";
        const string category = "tv";
        var conflictingId = Guid.NewGuid();

        var controller = CreateController();
        controller.AfterDuplicatePreCheckHook = async () =>
        {
            await using var raceCtx = new DavDatabaseContext(_options);
            raceCtx.QueueItems.Add(new QueueItem
            {
                Id = conflictingId,
                CreatedAt = DateTime.UtcNow,
                FileName = fileName,
                JobName = "Sliders.S01E01.part.2.Pilot",
                NzbFileSize = 10,
                TotalSegmentBytes = 10,
                Category = category,
                Priority = QueueItem.PriorityOption.Normal,
                PostProcessing = QueueItem.PostProcessingOption.None,
            });
            raceCtx.NzbNames.Add(new NzbName { Id = conflictingId, FileName = fileName });
            await raceCtx.SaveChangesAsync();
        };

        var response = await controller.AddFileAsync(CreateRequest(fileName, category));

        Assert.True(response.Status);
        var newId = Guid.Parse(Assert.Single(response.NzoIds));
        Assert.NotEqual(conflictingId, newId);

        Assert.Null(await _context.QueueItems.AsNoTracking()
            .SingleOrDefaultAsync(q => q.Id == conflictingId));
        Assert.Equal(newId, (await _context.QueueItems.AsNoTracking()
            .SingleAsync(q => q.Category == category && q.FileName == fileName)).Id);
        Assert.NotNull(BlobStore.ReadBlob(newId));
        Assert.Contains(_context.ChangeTracker.Entries<QueueItem>(),
            e => e.Entity.Id == newId);
    }

    [Fact]
    public async Task AddFileAsync_NoReplace_KeepsExistingQueueItem()
    {
        var existingId = Guid.NewGuid();
        const string fileName = "Sliders.S01E01.part.3.Pilot.nzb";
        const string category = "tv";
        await SeedQueueItemAsync(existingId, fileName, category);

        var controller = CreateController();
        var error = await Assert.ThrowsAsync<BadHttpRequestException>(() =>
            controller.AddFileAsync(CreateRequest(
                fileName, category, replaceExisting: false)));

        Assert.Contains("already exists", error.Message);
        Assert.Equal(existingId, (await _context.QueueItems.AsNoTracking()
            .SingleAsync(q => q.Category == category && q.FileName == fileName)).Id);
    }

    [Fact]
    public async Task AddFileAsync_NoReplace_KeepsQueueItemInsertedAfterPreCheck()
    {
        var conflictingId = Guid.NewGuid();
        const string fileName = "Sliders.S01E01.part.5.Pilot.nzb";
        const string category = "tv";
        var controller = CreateController();
        controller.AfterDuplicatePreCheckHook = async () =>
        {
            await using var raceContext = new DavDatabaseContext(_options);
            raceContext.QueueItems.Add(CreateQueueItem(conflictingId, fileName, category));
            raceContext.NzbNames.Add(new NzbName { Id = conflictingId, FileName = fileName });
            await raceContext.SaveChangesAsync();
        };

        var error = await Assert.ThrowsAsync<BadHttpRequestException>(() =>
            controller.AddFileAsync(CreateRequest(
                fileName, category, replaceExisting: false)));

        Assert.Contains("already exists", error.Message);
        Assert.Equal(conflictingId, (await _context.QueueItems.AsNoTracking()
            .SingleAsync(q => q.Category == category && q.FileName == fileName)).Id);
    }

    [Fact]
    public async Task AddFileAsync_UsesCallerAssignedNzoId()
    {
        var assignedId = Guid.NewGuid();
        const string fileName = "Sliders.S01E01.part.4.Pilot.nzb";

        var response = await CreateController().AddFileAsync(
            CreateRequest(fileName, "tv", nzoId: assignedId));

        Assert.Equal(assignedId.ToString(), Assert.Single(response.NzoIds));
        Assert.True(await _context.QueueItems.AsNoTracking().AnyAsync(q => q.Id == assignedId));
    }

    [Fact]
    public void IsCategoryFileNameUniqueViolation_DetectsSqliteConstraintMessage()
    {
        var sqlite = new Microsoft.Data.Sqlite.SqliteException(
            "SQLite Error 19: 'UNIQUE constraint failed: QueueItems.Category, QueueItems.FileName'.",
            19);
        var update = new DbUpdateException("save failed", sqlite);
        Assert.True(AddFileController.IsCategoryFileNameUniqueViolation(update));
    }

    private AddFileController CreateController()
    {
        var controller = new AddFileController(
            new DefaultHttpContext(),
            _dbClient,
            _queueManager,
            _configManager,
            _websocketManager)
        {
            FreshContextFactory = () => new DavDatabaseContext(_options),
        };
        return controller;
    }

    private async Task SeedQueueItemAsync(Guid id, string fileName, string category)
    {
        _context.QueueItems.Add(CreateQueueItem(id, fileName, category));
        _context.NzbNames.Add(new NzbName { Id = id, FileName = fileName });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private static QueueItem CreateQueueItem(Guid id, string fileName, string category) =>
        new()
        {
            Id = id,
            CreatedAt = DateTime.UtcNow,
            FileName = fileName,
            JobName = Path.GetFileNameWithoutExtension(fileName),
            NzbFileSize = 10,
            TotalSegmentBytes = 10,
            Category = category,
            Priority = QueueItem.PriorityOption.Normal,
            PostProcessing = QueueItem.PostProcessingOption.None,
        };

    private static AddFileRequest CreateRequest(
        string fileName,
        string category,
        Guid? nzoId = null,
        bool replaceExisting = true)
    {
        var nzb = """
            <?xml version="1.0" encoding="utf-8"?>
            <nzb xmlns="http://www.newzbin.com/DTD/2003/nzb">
              <file subject="test">
                <groups><group>alt.binaries.test</group></groups>
                <segments>
                  <segment bytes="100" number="1">seg@example.com</segment>
                </segments>
              </file>
            </nzb>
            """;
        return new AddFileRequest
        {
            NzoId = nzoId,
            ReplaceExistingQueueItem = replaceExisting,
            FileName = fileName,
            ContentType = "application/x-nzb",
            NzbFileStream = new MemoryStream(Encoding.UTF8.GetBytes(nzb)),
            Category = category,
            Priority = QueueItem.PriorityOption.Normal,
            PostProcessing = QueueItem.PostProcessingOption.None,
            CancellationToken = CancellationToken.None,
        };
    }
}
