using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.Database;

/// <summary>
/// Dedicated store for migrations from Usenet-backed sources into NzbDAV. Lives in its own
/// SQLite file (<c>usenet-migration.db</c>) so migration state and schema changes
/// remain isolated from <see cref="DavDatabaseContext"/>. Uses the same separate-context
/// lifecycle as <see cref="MetricsDbContext"/>.
/// </summary>
public sealed class UsenetMigrationDbContext : DbContext
{
    public static string DatabaseFilePath => Path.Join(DavDatabaseContext.ConfigPath, "usenet-migration.db");

    private static readonly Lazy<DbContextOptions<UsenetMigrationDbContext>> Options = new(() =>
        new DbContextOptionsBuilder<UsenetMigrationDbContext>()
            .UseSqlite($"Data Source={DatabaseFilePath}")
            .Options
    );

    public UsenetMigrationDbContext() : base(Options.Value)
    {
    }

    internal UsenetMigrationDbContext(DbContextOptions<UsenetMigrationDbContext> options) : base(options)
    {
    }

    public DbSet<MigrationSessionState> SessionState => Set<MigrationSessionState>();
    public DbSet<MigrationPreferences> Preferences => Set<MigrationPreferences>();
    public DbSet<MigrationCategoryMap> CategoryMap => Set<MigrationCategoryMap>();
    public DbSet<MigrationRelease> Releases => Set<MigrationRelease>();
    public DbSet<MigrationReleaseFile> ReleaseFiles => Set<MigrationReleaseFile>();
    public DbSet<MigrationSubmission> Submissions => Set<MigrationSubmission>();
    public DbSet<MigrationRun> MigrationRuns => Set<MigrationRun>();
    public DbSet<MigratedRelease> MigratedReleases => Set<MigratedRelease>();
    public DbSet<MigratedFile> MigratedFiles => Set<MigratedFile>();
    public DbSet<MigrationSymlinkRewrite> SymlinkRewrites => Set<MigrationSymlinkRewrite>();
    public DbSet<MigrationScanError> ScanErrors => Set<MigrationScanError>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MigrationSessionState>(e =>
        {
            e.ToTable("SessionState", t => t.HasCheckConstraint("CK_SessionState_Singleton", "Id = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Status).IsRequired();
        });

        b.Entity<MigrationPreferences>(e =>
        {
            e.ToTable("MigrationPreferences",
                t => t.HasCheckConstraint("CK_MigrationPreferences_Singleton", "Id = 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        b.Entity<MigrationCategoryMap>(e =>
        {
            e.ToTable("CategoryMap");
            e.HasKey(x => x.AltmountCategory);
            e.Property(x => x.AltmountCategory).ValueGeneratedNever();
            e.Property(x => x.Action).IsRequired();
            e.Property(x => x.DiscoveredBy).IsRequired();
        });

        b.Entity<MigrationRelease>(e =>
        {
            e.ToTable("Releases");
            e.HasKey(x => x.StoreRef);
            e.Property(x => x.StoreRef).ValueGeneratedNever();
            e.Property(x => x.StoreBasename).IsRequired();
            e.Property(x => x.SubmitFileName).IsRequired();
            e.Property(x => x.QueueFileName).IsRequired();
            e.Property(x => x.JobName).IsRequired();
            e.Property(x => x.Verdict).IsRequired();
            e.Property(x => x.VerdictReasons).IsRequired();

            e.HasIndex(x => new { x.Verdict, x.Included });
            e.HasIndex(x => x.TargetCategory);
            e.HasIndex(x => x.JobName);
            e.HasIndex(x => x.CollisionGroupKey);
            e.HasIndex(x => new { x.TargetCategory, x.QueueFileName }); // Speeds queue-key collision scans.
        });

        b.Entity<MigrationReleaseFile>(e =>
        {
            e.ToTable("ReleaseFiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.StoreRef).IsRequired();
            e.Property(x => x.MetaPath).IsRequired();
            e.Property(x => x.VirtualPath).IsRequired();
            e.Property(x => x.FileName).IsRequired();
            e.Property(x => x.NormalisedName).IsRequired();

            e.HasOne<MigrationRelease>()
                .WithMany()
                .HasForeignKey(x => x.StoreRef)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.StoreRef);
            e.HasIndex(x => x.NormalisedName);
        });

        b.Entity<MigrationSubmission>(e =>
        {
            e.ToTable("Submissions");
            e.HasKey(x => x.StoreRef);
            e.Property(x => x.StoreRef).ValueGeneratedNever();
            e.Property(x => x.State).IsRequired();

            e.HasOne<MigrationRelease>()
                .WithMany()
                .HasForeignKey(x => x.StoreRef)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.State);
        });

        b.Entity<MigrationRun>(e =>
        {
            e.ToTable("MigrationRuns");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceType).IsRequired();
            e.Property(x => x.Status).IsRequired();
            e.HasIndex(x => x.StartedAt);
        });

        b.Entity<MigratedRelease>(e =>
        {
            e.ToTable("MigratedReleases");
            e.HasKey(x => x.Id);
            e.Property(x => x.SourceType).IsRequired();
            e.Property(x => x.SourceReleaseId).IsRequired();
            e.HasIndex(x => new { x.SourceType, x.SourceReleaseId }).IsUnique();
            e.HasIndex(x => x.LastRunId);
            e.HasIndex(x => x.NzoId);
        });

        b.Entity<MigratedFile>(e =>
        {
            e.ToTable("MigratedFiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.VirtualPath).IsRequired();
            e.Property(x => x.NormalisedRelativePath).IsRequired();
            e.Property(x => x.NormalisedName).IsRequired();
            e.Property(x => x.MatchMethod).IsRequired();

            e.HasOne<MigratedRelease>()
                .WithMany()
                .HasForeignKey(x => x.MigratedReleaseId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.MigratedReleaseId, x.VirtualPath }).IsUnique();
            e.HasIndex(x => x.DavItemId);
            e.HasIndex(x => x.NzbBlobId);
        });

        b.Entity<MigrationSymlinkRewrite>(e =>
        {
            e.ToTable("SymlinkRewrites");
            e.HasKey(x => x.Id);
            e.Property(x => x.SymlinkPath).IsRequired();
            e.Property(x => x.OldTarget).IsRequired();
            e.Property(x => x.Status).IsRequired();

            e.HasIndex(x => x.Status);
        });

        b.Entity<MigrationScanError>(e =>
        {
            e.ToTable("ScanErrors");
            e.HasKey(x => x.Id);
        });
    }
}
