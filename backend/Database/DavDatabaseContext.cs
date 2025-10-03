using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Interceptors;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Database;

public sealed class DavDatabaseContext() : DbContext(Options.Value)
{
    public static string ConfigPath => Environment.GetEnvironmentVariable("CONFIG_PATH") ?? "/config";

    private static readonly Lazy<DbContextOptions<DavDatabaseContext>> Options = new(() =>
    {
        var databaseFilePath = Path.Join(ConfigPath, "db.sqlite");
        return new DbContextOptionsBuilder<DavDatabaseContext>()
            .UseSqlite($"Data Source={databaseFilePath}")
            .AddInterceptors(new SqliteForeignKeyEnabler())
            .Options;
    });

    // database sets
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<DavItem> Items => Set<DavItem>();
    public DbSet<DavNzbFile> NzbFiles => Set<DavNzbFile>();
    public DbSet<DavRarFile> RarFiles => Set<DavRarFile>();
    public DbSet<QueueItem> QueueItems => Set<QueueItem>();
    public DbSet<HistoryItem> HistoryItems => Set<HistoryItem>();
    public DbSet<ConfigItem> ConfigItems => Set<ConfigItem>();
    public DbSet<IntegrityCheckRun> IntegrityCheckRuns => Set<IntegrityCheckRun>();
    public DbSet<IntegrityCheckFileResult> IntegrityCheckFileResults => Set<IntegrityCheckFileResult>();

    // tables
    protected override void OnModelCreating(ModelBuilder b)
    {
        // Account
        b.Entity<Account>(e =>
        {
            e.ToTable("Accounts");
            e.HasKey(i => new { i.Type, i.Username });

            e.Property(i => i.Type)
                .HasConversion<int>()
                .IsRequired();

            e.Property(i => i.Username)
                .IsRequired()
                .HasMaxLength(255);

            e.Property(i => i.PasswordHash)
                .IsRequired();

            e.Property(i => i.RandomSalt)
                .IsRequired();
        });

        // DavItem
        b.Entity<DavItem>(e =>
        {
            e.ToTable("DavItems");
            e.HasKey(i => i.Id);

            e.Property(i => i.Id)
                .ValueGeneratedNever();

            e.Property(i => i.CreatedAt)
                .ValueGeneratedNever()
                .IsRequired();

            e.Property(i => i.Name)
                .IsRequired()
                .HasMaxLength(255);

            e.Property(i => i.Type)
                .HasConversion<int>()
                .IsRequired();

            e.Property(i => i.Path)
                .IsRequired();

            e.Property(i => i.IdPrefix)
                .IsRequired();

            e.HasOne(i => i.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(i => i.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(i => new { i.ParentId, i.Name })
                .IsUnique();

            e.HasIndex(i => new { i.IdPrefix, i.Type });
        });

        // DavNzbFile
        b.Entity<DavNzbFile>(e =>
        {
            e.ToTable("DavNzbFiles");
            e.HasKey(f => f.Id);

            e.Property(i => i.Id)
                .ValueGeneratedNever();

            e.Property(f => f.SegmentIds)
                .HasConversion(new ValueConverter<string[], string>
                (
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>()
                ))
                .HasColumnType("TEXT") // store raw JSON
                .IsRequired();

            e.HasOne(f => f.DavItem)
                .WithOne()
                .HasForeignKey<DavNzbFile>(f => f.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DavRarFile
        b.Entity<DavRarFile>(e =>
        {
            e.ToTable("DavRarFiles");
            e.HasKey(f => f.Id);

            e.Property(i => i.Id)
                .ValueGeneratedNever();

            e.Property(f => f.RarParts)
                .HasConversion(new ValueConverter<DavRarFile.RarPart[], string>
                (
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<DavRarFile.RarPart[]>(v, (JsonSerializerOptions?)null)
                         ?? Array.Empty<DavRarFile.RarPart>()
                ))
                .HasColumnType("TEXT") // store raw JSON
                .IsRequired();

            e.HasOne(f => f.DavItem)
                .WithOne()
                .HasForeignKey<DavRarFile>(f => f.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // QueueItem
        b.Entity<QueueItem>(e =>
        {
            e.ToTable("QueueItems");
            e.HasKey(i => i.Id);

            e.Property(i => i.Id)
                .ValueGeneratedNever();

            e.Property(i => i.CreatedAt)
                .ValueGeneratedNever()
                .IsRequired();

            e.Property(i => i.FileName)
                .IsRequired();

            e.Property(i => i.NzbContents)
                .IsRequired();

            e.Property(i => i.NzbFileSize)
                .IsRequired();

            e.Property(i => i.TotalSegmentBytes)
                .IsRequired();

            e.Property(i => i.Category)
                .IsRequired();

            e.Property(i => i.Priority)
                .HasConversion<int>()
                .IsRequired();

            e.Property(i => i.PostProcessing)
                .HasConversion<int>()
                .IsRequired();

            e.Property(i => i.PauseUntil)
                .ValueGeneratedNever();

            e.HasIndex(i => new { i.FileName })
                .IsUnique();

            e.Property(i => i.JobName)
                .IsRequired();

            e.HasIndex(i => new { i.Priority })
                .IsUnique(false);

            e.HasIndex(i => new { i.CreatedAt })
                .IsUnique(false);

            e.HasIndex(i => new { i.Category })
                .IsUnique(false);

            e.HasIndex(i => new { i.Priority, i.CreatedAt })
                .IsUnique(false);

            e.HasIndex(i => new { i.Category, i.Priority, i.CreatedAt })
                .IsUnique(false);
        });

        // HistoryItem
        b.Entity<HistoryItem>(e =>
        {
            e.ToTable("HistoryItems");
            e.HasKey(i => i.Id);

            e.Property(i => i.Id)
                .ValueGeneratedNever();

            e.Property(i => i.CreatedAt)
                .ValueGeneratedNever()
                .IsRequired();

            e.Property(i => i.FileName)
                .IsRequired();

            e.Property(i => i.JobName)
                .IsRequired();

            e.Property(i => i.Category)
                .IsRequired();

            e.Property(i => i.DownloadStatus)
                .HasConversion<int>()
                .IsRequired();

            e.Property(i => i.TotalSegmentBytes)
                .IsRequired();

            e.Property(i => i.DownloadTimeSeconds)
                .IsRequired();

            e.Property(i => i.FailMessage)
                .IsRequired(false);

            e.Property(i => i.DownloadDirId)
                .IsRequired(false);

            e.HasIndex(i => new { i.CreatedAt })
                .IsUnique(false);

            e.HasIndex(i => new { i.Category })
                .IsUnique(false);

            e.HasIndex(i => new { i.Category, i.CreatedAt })
                .IsUnique(false);

            e.HasIndex(i => new { i.Category, i.DownloadDirId })
                .IsUnique(false);
        });

        // ConfigItem
        b.Entity<ConfigItem>(e =>
        {
            e.ToTable("ConfigItems");
            e.HasKey(i => i.ConfigName);
            e.Property(i => i.ConfigValue)
                .IsRequired();
        });

        // IntegrityCheckRun
        b.Entity<IntegrityCheckRun>(e =>
        {
            e.ToTable("IntegrityCheckRuns");

            // Primary key
            e.HasKey(i => i.RunId);

            // Required properties with constraints
            e.Property(i => i.RunId)
                .IsRequired()
                .HasMaxLength(50);

            e.Property(i => i.StartTime)
                .IsRequired();

            e.Property(i => i.RunType)
                .HasConversion<int>()
                .IsRequired();

            e.Property(i => i.CorruptFileAction)
                .HasConversion<int>()
                .IsRequired();

            e.Property(i => i.Status)
                .HasConversion<int>()
                .IsRequired();

            // Optional properties with constraints
            e.Property(i => i.EndTime)
                .IsRequired(false);

            e.Property(i => i.ScanDirectory)
                .HasMaxLength(500)
                .IsRequired(false);

            e.Property(i => i.CurrentFile)
                .HasMaxLength(500)
                .IsRequired(false);

            e.Property(i => i.ProgressPercentage)
                .IsRequired(false);

            // Required properties without additional constraints
            e.Property(i => i.MaxFilesToCheck)
                .IsRequired();

            e.Property(i => i.Mp4DeepScan)
                .IsRequired();

            e.Property(i => i.AutoMonitor)
                .IsRequired();

            e.Property(i => i.UnmonitorValidatedFiles)
                .IsRequired();

            e.Property(i => i.DirectDeletionFallback)
                .IsRequired();

            e.Property(i => i.NzbSegmentSamplePercentage)
                .IsRequired();

            e.Property(i => i.NzbSegmentThresholdPercentage)
                .IsRequired();

            e.Property(i => i.ValidFiles)
                .IsRequired();

            e.Property(i => i.CorruptFiles)
                .IsRequired();

            e.Property(i => i.TotalFiles)
                .IsRequired();

            e.Property(i => i.IsRunning)
                .IsRequired();
        });

        // IntegrityCheckFileResult
        b.Entity<IntegrityCheckFileResult>(e =>
        {
            e.ToTable("IntegrityCheckFileResults");

            // Primary key
            e.HasKey(i => i.Id);

            // Required properties with constraints
            e.Property(i => i.RunId)
                .IsRequired()
                .HasMaxLength(50);

            e.Property(i => i.FileId)
                .IsRequired()
                .HasMaxLength(50);

            e.Property(i => i.FilePath)
                .IsRequired()
                .HasMaxLength(500);

            e.Property(i => i.FileName)
                .IsRequired()
                .HasMaxLength(255);

            e.Property(i => i.Status)
                .HasConversion<int>()
                .IsRequired();

            e.Property(i => i.LastChecked)
                .IsRequired();

            e.Property(i => i.IsLibraryFile)
                .IsRequired();

            // Optional properties
            e.Property(i => i.ErrorMessage)
                .HasMaxLength(1000)
                .IsRequired(false);

            e.Property(i => i.ActionTaken)
                .HasConversion<int>()
                .IsRequired(false);

            // Foreign key relationship
            e.HasOne(f => f.Run)
                .WithMany()
                .HasForeignKey(f => f.RunId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes for performance
            e.HasIndex(i => i.RunId);
            e.HasIndex(i => i.FileId);
        });
    }
}