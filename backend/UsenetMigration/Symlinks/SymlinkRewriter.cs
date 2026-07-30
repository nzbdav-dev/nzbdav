using Microsoft.EntityFrameworkCore;
using Serilog;

namespace NzbWebDAV.UsenetMigration.Symlinks;

public sealed class RewriteSummary
{
    public int Applied { get; init; }
    public int Failed { get; init; }

    /// <summary>Path of the restore tarball written before any change.</summary>
    public string? BackupPath { get; init; }
}

/// <summary>
/// Applies the Step 6 plan. It is the only component that mutates the arr/Plex
/// library, and it does so with three guarantees:
/// <list type="number">
/// <item><b>Backup first.</b> A restore tarball of every to-be-changed symlink's prior
///   state is written before the first rewrite.</item>
/// <item><b>Retarget only, never delete.</b> Each rewrite replaces a symlink's target
///   via <see cref="ISymlinkOps"/>, which removes only the link inode; migrated content
///   and any real (non-symlink) file are never touched. Orphan / not-altmount /
///   already-nzbdav rows are never loaded, so they are untouched by construction.</item>
/// <item><b>Drift-guarded and idempotent.</b> A row is rewritten only if the on-disk
///   target still equals the planned <c>OldTarget</c>; a symlink already pointing at
///   <c>NewTarget</c> is a no-op success, so re-running apply is safe.</item>
/// </list>
/// </summary>
public sealed class SymlinkRewriter(UsenetMigrationStore store)
{
    /// <summary>Test seam for filesystem symlink operations; production uses the real FS.</summary>
    internal ISymlinkOps Ops { get; set; } = RealSymlinkOps.Instance;

    /// <summary>Test seam for the current time, so backup filenames are deterministic.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    public async Task<RewriteSummary> ApplyAsync(CancellationToken ct = default)
    {
        var session = await store.GetSessionAsync(ct).ConfigureAwait(false);
        var libraryRoot = session.SymlinkLibraryRoot
                          ?? throw new InvalidOperationException("Applying symlinks requires Library Root to be configured.");
        var backupDir = session.SymlinkBackupDir
                        ?? throw new InvalidOperationException("Applying symlinks requires SymlinkBackupDir to be set.");

        await using var ctx = store.NewContext();
        var rows = await ctx.SymlinkRewrites
            .Where(r => r.Status == "rewrite" && r.NewTarget != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return new RewriteSummary();

        // 1) Backup FIRST — the prior state of every row we might change.
        var backupPath = BuildBackupPath(backupDir, UtcNow());
        await SymlinkBackup.WriteAsync(
            backupPath,
            rows.Select(r => new SymlinkBackup.Entry(r.SymlinkPath, r.OldTarget, r.NewTarget)).ToList(),
            ct).ConfigureAwait(false);

        // 2) Retarget each, drift-guarded and idempotent.
        int applied = 0, failed = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var current = Ops.ReadLink(libraryRoot, row.SymlinkPath);
                if (current is null)
                {
                    Fail(row, "No symlink present at apply time.");
                    failed++;
                }
                else if (PathsEqual(current, row.NewTarget!))
                {
                    // Already retargeted — idempotent no-op.
                    row.Status = "applied";
                    row.Error = null;
                    applied++;
                }
                else if (!PathsEqual(current, row.OldTarget))
                {
                    Fail(row, $"Symlink target changed since plan (now '{current}'); left untouched.");
                    failed++;
                }
                else
                {
                    Ops.CreateOrReplaceSymlink(libraryRoot, row.SymlinkPath, row.NewTarget!);
                    row.Status = "applied";
                    row.Error = null;
                    applied++;
                }
            }
            catch (Exception e)
            {
                Fail(row, e.Message);
                failed++;
            }

            row.UpdatedAt = UtcNow();
        }

        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        Log.Information(
            "Symlink apply: {Applied} applied, {Failed} failed. Backup at {BackupPath}",
            applied, failed, backupPath);

        return new RewriteSummary { Applied = applied, Failed = failed, BackupPath = backupPath };
    }

    private static void Fail(Database.Models.UsenetMigration.MigrationSymlinkRewrite row, string error)
    {
        row.Status = "failed";
        row.Error = error;
    }

    private static string BuildBackupPath(string backupDir, DateTime createdAt)
    {
        var stem = $"altmount-symlink-backup-{createdAt:yyyyMMdd-HHmmss}";
        var path = Path.Combine(backupDir, $"{stem}.tar.gz");
        for (var suffix = 2; File.Exists(path); suffix++)
            path = Path.Combine(backupDir, $"{stem}-{suffix}.tar.gz");
        return path;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            a.Replace('\\', '/').TrimEnd('/'),
            b.Replace('\\', '/').TrimEnd('/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
