using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.UsenetMigration;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Interceptors;
using NzbWebDAV.Database.MigrationHelpers;

namespace NzbWebDAV.Tests.UsenetMigration;

/// <summary>
/// Spins up isolated, migrated temp SQLite databases for the migration runner
/// tests — one for the migration DB (behind <see cref="UsenetMigrationStore"/>'s
/// <c>ContextFactory</c> seam) and one for the live NzbDAV DB (behind the runners'
/// <c>DavContextFactory</c> seam). Because everything goes through explicit options
/// and injected factories, these tests never touch <c>CONFIG_PATH</c> and run in
/// parallel. The Dav options mirror <see cref="Database.DavDatabaseClientTests"/>.
/// </summary>
internal sealed class MigrationTestHarness : IAsyncDisposable
{
    private readonly string _migPath;
    private readonly string _davPath;

    public DbContextOptions<UsenetMigrationDbContext> MigOptions { get; }
    public DbContextOptions<DavDatabaseContext> DavOptions { get; }
    public UsenetMigrationStore Store { get; }

    public Func<UsenetMigrationDbContext> MigFactory => () => new UsenetMigrationDbContext(MigOptions);
    public Func<DavDatabaseContext> DavFactory => () => new DavDatabaseContext(DavOptions);

    public UsenetMigrationDbContext Mig() => new(MigOptions);
    public DavDatabaseContext Dav() => new(DavOptions);

    private MigrationTestHarness()
    {
        _migPath = Path.Combine(Path.GetTempPath(), $"altmig-tests-{Guid.NewGuid():N}.db");
        _davPath = Path.Combine(Path.GetTempPath(), $"altmig-dav-{Guid.NewGuid():N}.sqlite");

        MigOptions = new DbContextOptionsBuilder<UsenetMigrationDbContext>()
            .UseSqlite($"Data Source={_migPath}")
            .Options;

        DavOptions = new DbContextOptionsBuilder<DavDatabaseContext>()
            .UseSqlite($"Data Source={_davPath}")
            .AddInterceptors(new SqliteForeignKeyEnabler())
            .ReplaceService<IMigrationsSqlGenerator, SqliteMigrationsSqlGenerator<SqliteMigrationsSqlGenerator>>()
            .Options;

        Store = new UsenetMigrationStore { ContextFactory = MigFactory };
    }

    public static async Task<MigrationTestHarness> CreateAsync()
    {
        var harness = new MigrationTestHarness();
        await using (var mig = harness.Mig())
            await mig.Database.MigrateAsync();
        await using (var dav = harness.Dav())
            await dav.Database.MigrateAsync();
        return harness;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        TryDelete(_migPath);
        TryDelete(_davPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // best effort — temp files.
        }
    }
}
