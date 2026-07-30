using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.UsenetMigration.Symlinks;

namespace NzbWebDAV.Tests.UsenetMigration;

public class SymlinkRestoreServiceTests
{
    private sealed class FakeSymlinkOps : ISymlinkOps
    {
        public readonly Dictionary<string, string> Links = new(StringComparer.Ordinal);

        public string? ReadLink(string libraryRoot, string path) => Links.GetValueOrDefault(path);
        public void CreateOrReplaceSymlink(string libraryRoot, string path, string target) => Links[path] = target;
    }

    [Fact]
    public async Task Restore_LeavesDriftedAndOutOfRootLinksUntouched()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), $"altmig-library-{Guid.NewGuid():N}");
        var backupDir = Path.Combine(Path.GetTempPath(), $"altmig-backups-{Guid.NewGuid():N}");
        var inRoot = Path.Combine(root, "movie.mkv");
        var outsideRoot = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.mkv");
        var archiveName = "altmount-symlink-backup-20260720-120000.tar.gz";
        var archivePath = Path.Combine(backupDir, archiveName);
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "linked";
            s.SymlinkLibraryRoot = root;
            s.SymlinkBackupDir = backupDir;
        });
        await SymlinkBackup.WriteAsync(archivePath,
        [
            new(inRoot, "/alt/original.mkv", "/nzbdav/expected.mkv"),
            new(outsideRoot, "/alt/outside.mkv", "/nzbdav/outside.mkv"),
        ]);
        var ops = new FakeSymlinkOps
        {
            Links =
            {
                [inRoot] = "/nzbdav/changed-after-rewrite.mkv",
                [outsideRoot] = "/nzbdav/outside.mkv",
            },
        };

        var result = await new SymlinkRestoreService(h.Store) { Ops = ops }.RestoreAsync(archiveName);

        Assert.Equal(0, result.Restored);
        Assert.Equal(2, result.Failed);
        Assert.Equal("/nzbdav/changed-after-rewrite.mkv", ops.Links[inRoot]);
        Assert.Equal("/nzbdav/outside.mkv", ops.Links[outsideRoot]);
        Assert.Contains(result.Issues, i => i.Path == inRoot && i.Reason.Contains("changed after rewriting"));
        Assert.Contains(result.Issues, i => i.Path == outsideRoot && i.Reason.Contains("outside"));

        Directory.Delete(backupDir, recursive: true);
    }

    [Fact]
    public async Task Restore_LegacyArchiveUsesCurrentPlanForDriftGuard()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), $"altmig-library-{Guid.NewGuid():N}");
        var backupDir = Path.Combine(Path.GetTempPath(), $"altmig-backups-{Guid.NewGuid():N}");
        var link = Path.Combine(root, "episode.mkv");
        var archiveName = "altmount-symlink-backup-20260720-120001.tar.gz";
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "linked";
            s.SymlinkLibraryRoot = root;
            s.SymlinkBackupDir = backupDir;
        });
        await using (var ctx = h.Mig())
        {
            ctx.SymlinkRewrites.Add(new MigrationSymlinkRewrite
            {
                SymlinkPath = link,
                OldTarget = "/alt/original.mkv",
                NewTarget = "/nzbdav/replacement.mkv",
                Status = "applied",
                UpdatedAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }
        await SymlinkBackup.WriteAsync(
            Path.Combine(backupDir, archiveName),
            [new SymlinkBackup.Entry(link, "/alt/original.mkv")]);
        var ops = new FakeSymlinkOps { Links = { [link] = "/nzbdav/replacement.mkv" } };

        var result = await new SymlinkRestoreService(h.Store) { Ops = ops }.RestoreAsync(archiveName);

        Assert.Equal(1, result.Restored);
        Assert.Equal(1, result.Requeued);
        Assert.Equal("/alt/original.mkv", ops.Links[link]);
        await using var verify = h.Mig();
        Assert.Equal("rewrite", (await verify.SymlinkRewrites.SingleAsync()).Status);

        Directory.Delete(backupDir, recursive: true);
    }

    [Fact]
    public async Task Restore_CurrentArchiveRecreatesMissingRewritePlanRow()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), $"altmig-library-{Guid.NewGuid():N}");
        var backupDir = Path.Combine(Path.GetTempPath(), $"altmig-backups-{Guid.NewGuid():N}");
        var link = Path.Combine(root, "movie.mkv");
        var archiveName = "altmount-symlink-backup-20260720-120002.tar.gz";
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "linked";
            s.SymlinkLibraryRoot = root;
            s.SymlinkBackupDir = backupDir;
        });
        await SymlinkBackup.WriteAsync(
            Path.Combine(backupDir, archiveName),
            [new SymlinkBackup.Entry(link, "/alt/original.mkv", "/nzbdav/replacement.mkv")]);
        var ops = new FakeSymlinkOps { Links = { [link] = "/nzbdav/replacement.mkv" } };

        var result = await new SymlinkRestoreService(h.Store) { Ops = ops }.RestoreAsync(archiveName);

        Assert.Equal(1, result.Restored);
        Assert.Equal(1, result.Requeued);
        await using var verify = h.Mig();
        var row = await verify.SymlinkRewrites.SingleAsync();
        Assert.Equal(link, row.SymlinkPath);
        Assert.Equal("/alt/original.mkv", row.OldTarget);
        Assert.Equal("/nzbdav/replacement.mkv", row.NewTarget);
        Assert.Equal("rewrite", row.Status);

        Directory.Delete(backupDir, recursive: true);
    }

    [Theory]
    [InlineData("../altmount-symlink-backup-20260720.tar.gz")]
    [InlineData("other.tar.gz")]
    [InlineData("")]
    public void ResolveArchivePath_RejectsUntrustedNames(string fileName)
    {
        Assert.Throws<InvalidDataException>(() =>
            SymlinkRestoreService.ResolveArchivePath(Path.GetTempPath(), fileName));
    }
}
