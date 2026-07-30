using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.UsenetMigration.Symlinks;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Tests.UsenetMigration;

/// <summary>
/// Verifies the conservative matcher: exact leaf-name match, then a
/// single-leaf fallback for the deobfuscation-divergence case, and orphan otherwise.
/// </summary>
public class SymlinkMatcherTests
{
    private static MatchableFile File(long id, string basename, long? size = null, string? path = null) =>
        new()
        {
            ReleaseFileId = id,
            NormalisedName = MatchKey.ForLeaf(basename),
            NormalisedRelativePath = path is null ? "" : MatchKey.ForRelativePath(path),
            FileSize = size,
        };

    private static ReleaseLeaf Leaf(Guid id, string name, long? size = null, string? path = null) =>
        new()
        {
            DavItemId = id,
            Name = name,
            Path = path ?? "",
            FileSize = size,
            IdentityMethod = "nzb-blob-id",
        };

    [Fact]
    public async Task LoadLeaves_FallsBackToHistoryItemId_ForOlderRows()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var nzoId = Guid.NewGuid();
        var davItemId = Guid.NewGuid();
        await using (var dav = h.Dav())
        {
            dav.Items.Add(DavItem.New(
                davItemId, DavItem.ContentFolder, "Movie.mkv", 1_000,
                DavItem.ItemType.UsenetFile, DavItem.ItemSubType.NzbFile,
                null, null, historyItemId: nzoId, fileBlobId: null));
            await dav.SaveChangesAsync();
        }

        var leaves = await SymlinkMatcher.LoadLeavesAsync(nzoId, h.DavFactory);

        var leaf = Assert.Single(leaves);
        Assert.Equal(davItemId, leaf.DavItemId);
        Assert.Equal("history-item-id", leaf.IdentityMethod);
    }

    [Fact]
    public void ExactMatch_ByNormalisedName()
    {
        var leafId = Guid.NewGuid();
        var files = new[] { File(1, "Movie.Name.2024.mkv") };
        var leaves = new[] { Leaf(leafId, "movie.name.2024.MKV") }; // differs only by case

        var result = SymlinkMatcher.Match(files, leaves);

        Assert.Single(result);
        Assert.Equal(leafId, result[0].DavItemId);
        Assert.Equal("exact", result[0].MatchMethod);
    }

    [Fact]
    public void SingleLeafFallback_WhenNamesDiverge_AndBothSidesSingular()
    {
        // Altmount deobfuscated; NzbDAV kept the obfuscated leaf name.
        var leafId = Guid.NewGuid();
        var files = new[] { File(1, "The.Real.Movie.Name.2024.mkv") };
        var leaves = new[] { Leaf(leafId, "abc123def456.mkv") };

        var result = SymlinkMatcher.Match(files, leaves);

        Assert.Equal(leafId, result[0].DavItemId);
        Assert.Equal("single-leaf-fallback", result[0].MatchMethod);
    }

    [Fact]
    public void NoFallback_WhenMultipleLeaves_AndNoExactMatch_IsOrphan()
    {
        var files = new[] { File(1, "wanted.mkv") };
        var leaves = new[]
        {
            Leaf(Guid.NewGuid(), "other1.mkv"),
            Leaf(Guid.NewGuid(), "other2.mkv"),
        };

        var result = SymlinkMatcher.Match(files, leaves);

        Assert.Null(result[0].DavItemId);
        Assert.Null(result[0].MatchMethod);
    }

    [Fact]
    public void NoFallback_WhenMultipleFiles_AvoidsAliasingOntoOneLeaf()
    {
        // 2 files, 1 leaf: exact match still works for the matching name; the other
        // is an orphan (never aliased onto the single leaf).
        var leafId = Guid.NewGuid();
        var files = new[] { File(1, "present.mkv"), File(2, "absent.mkv") };
        var leaves = new[] { Leaf(leafId, "present.mkv") };

        var result = SymlinkMatcher.Match(files, leaves);

        var present = result.Single(r => r.ReleaseFileId == 1);
        var absent = result.Single(r => r.ReleaseFileId == 2);
        Assert.Equal(leafId, present.DavItemId);
        Assert.Equal("exact", present.MatchMethod);
        Assert.Null(absent.DavItemId);
    }

    [Fact]
    public void AmbiguousKey_TwoLeavesNormaliseAlike_NotExactMatched()
    {
        // Two leaves collapse to the same normalised key ⇒ no arbitrary pick. With
        // >1 leaf the fallback also does not fire ⇒ orphan.
        var files = new[] { File(1, "Dup.Name.mkv") };
        var leaves = new[]
        {
            Leaf(Guid.NewGuid(), "Dup.Name.mkv"),
            Leaf(Guid.NewGuid(), "dup.name.MKV"),
        };

        var result = SymlinkMatcher.Match(files, leaves);

        Assert.Null(result[0].DavItemId);
    }

    [Fact]
    public void UniqueSizeFallback_WhenNamesDiffer()
    {
        var leafId = Guid.NewGuid();
        var files = new[]
        {
            File(1, "deobfuscated.mkv", 1_000),
            File(2, "subtitle.srt", 50),
        };
        var leaves = new[]
        {
            Leaf(leafId, "abc123.mkv", 1_000),
            Leaf(Guid.NewGuid(), "different.srt", 75),
        };

        var result = SymlinkMatcher.Match(files, leaves);

        var matched = result.Single(r => r.ReleaseFileId == 1);
        Assert.Equal(leafId, matched.DavItemId);
        Assert.Equal("unique-size", matched.MatchMethod);
        Assert.Null(result.Single(r => r.ReleaseFileId == 2).DavItemId);
    }

    [Fact]
    public void DuplicateSizes_AreNotMatchedBySize()
    {
        var files = new[] { File(1, "one.mkv", 1_000), File(2, "two.mkv", 1_000) };
        var leaves = new[]
        {
            Leaf(Guid.NewGuid(), "a.mkv", 1_000),
            Leaf(Guid.NewGuid(), "b.mkv", 1_000),
        };

        var result = SymlinkMatcher.Match(files, leaves);

        Assert.All(result, match => Assert.Null(match.DavItemId));
    }

    [Fact]
    public void DuplicateSourceNames_CannotReuseOneDavItem()
    {
        var files = new[] { File(1, "same.mkv"), File(2, "same.mkv") };
        var leaves = new[] { Leaf(Guid.NewGuid(), "same.mkv") };

        var result = SymlinkMatcher.Match(files, leaves);

        Assert.All(result, match => Assert.Null(match.DavItemId));
    }

    [Fact]
    public void RelativePathMatch_NormalizesSeparatorsCaseAndDotSegments()
    {
        var leafId = Guid.NewGuid();
        var files = new[]
        {
            File(1, "wrong-name.mkv", path: @"TV\Show\Season 01\.\Episode.mkv"),
        };
        var leaves = new[]
        {
            Leaf(leafId, "Episode.mkv", path: "/content/tv/show/season 01/episode.mkv"),
        };

        var result = SymlinkMatcher.Match(files, leaves);

        Assert.Equal(leafId, result[0].DavItemId);
        Assert.Equal("relative-path", result[0].MatchMethod);
    }
}
