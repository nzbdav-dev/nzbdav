using NzbWebDAV.UsenetMigration.Symlinks;
using NzbWebDAV.WebDav;

namespace NzbWebDAV.Tests.UsenetMigration;

/// <summary>
/// Verifies the pure four-way classification of a library symlink and that rewrite
/// targets are computed with NzbDAV's own GetTargetPath implementation.
/// </summary>
public class SymlinkPlannerClassifyTests
{
    private const string MountDir = "/mnt/nzbdav";

    private static Dictionary<string, List<CorrelatedFile>> Index(params CorrelatedFile[] files)
    {
        var index = new Dictionary<string, List<CorrelatedFile>>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var basename = System.IO.Path.GetFileName(f.VirtualPath).ToLowerInvariant();
            if (!index.TryGetValue(basename, out var list))
                index[basename] = list = new List<CorrelatedFile>();
            list.Add(f);
        }
        return index;
    }

    [Fact]
    public void Rewrite_WhenCorrelatedAndMatched_TargetEqualsGetTargetPath()
    {
        var davId = Guid.NewGuid();
        var index = Index(new CorrelatedFile
        {
            VirtualPath = "tv/Show.S01/Show.S01E01.mkv",
            StoreRef = "/nzbs/tv/1-Show.nzbz",
            ReleaseFileId = 1,
            NewDavItemId = davId,
            MatchMethod = "exact",
        });

        var c = SymlinkPlanner.Classify("/mnt/altmount/tv/Show.S01/Show.S01E01.mkv", index, MountDir);

        Assert.Equal("rewrite", c.Status);
        Assert.Equal(DatabaseStoreSymlinkFile.GetTargetPath(davId, MountDir, '/'), c.NewTarget);
        Assert.Equal("exact", c.MatchMethod);
        Assert.Equal("/nzbs/tv/1-Show.nzbz", c.StoreRef);
    }

    [Fact]
    public void Orphan_WhenCorrelatedButNotMatched()
    {
        var index = Index(new CorrelatedFile
        {
            VirtualPath = "tv/Show.S01/Show.S01E01.mkv",
            StoreRef = "/nzbs/tv/1-Show.nzbz",
            ReleaseFileId = 1,
            // NewDavItemId null ⇒ not migrated/matched
        });

        var c = SymlinkPlanner.Classify("/mnt/altmount/tv/Show.S01/Show.S01E01.mkv", index, MountDir);

        Assert.Equal("orphan", c.Status);
        Assert.Null(c.NewTarget);
        Assert.Equal("/nzbs/tv/1-Show.nzbz", c.StoreRef);
    }

    [Fact]
    public void NotAltmount_WhenNoCorrelation()
    {
        var c = SymlinkPlanner.Classify("/mnt/other/random/file.mkv", Index(), MountDir);
        Assert.Equal("not-altmount", c.Status);
    }

    [Theory]
    [InlineData("/mnt/nzbdav/.ids/a/b/c/d/e/1234")] // under mount dir
    [InlineData("/somewhere/.ids/a/b/c/d/e/1234")]  // .ids path anywhere
    public void AlreadyNzbdav_WhenTargetIsInNzbdav(string target)
    {
        var c = SymlinkPlanner.Classify(target, Index(), MountDir);
        Assert.Equal("already-nzbdav", c.Status);
    }

    [Fact]
    public void Correlation_PrefersLongestSuffix()
    {
        var shallow = Guid.NewGuid();
        var deep = Guid.NewGuid();
        var index = Index(
            new CorrelatedFile { VirtualPath = "file.mkv", StoreRef = "s1", ReleaseFileId = 1, NewDavItemId = shallow, MatchMethod = "exact" },
            new CorrelatedFile { VirtualPath = "tv/Show/file.mkv", StoreRef = "s2", ReleaseFileId = 2, NewDavItemId = deep, MatchMethod = "exact" });

        var c = SymlinkPlanner.Classify("/mnt/altmount/tv/Show/file.mkv", index, MountDir);

        Assert.Equal("rewrite", c.Status);
        Assert.Equal("s2", c.StoreRef); // the deeper, more specific correlation
    }
}
