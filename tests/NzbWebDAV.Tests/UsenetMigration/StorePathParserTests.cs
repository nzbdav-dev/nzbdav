using NzbWebDAV.UsenetMigration.Source;

namespace NzbWebDAV.Tests.UsenetMigration;

public class StorePathParserTests
{
    [Fact]
    public void CategorisedStore_WithQueueId_ParsesAllParts()
    {
        var p = StorePathParser.Parse("/config/.nzbs/tv/1041-Show.S01E01.HDTV.nzbz");
        Assert.NotNull(p);
        Assert.Equal("tv", p!.Category);
        Assert.False(p.IsUncategorised);
        Assert.False(p.IsFailed);
        Assert.Equal(1041, p.QueueId);
        Assert.Equal("1041-Show.S01E01.HDTV", p.StoreBasename);
        Assert.Equal("Show.S01E01.HDTV", p.NzbBasename);
    }

    [Fact]
    public void UncategorisedStore_AtNzbsRoot_IsUncategorised()
    {
        // A store directly under .nzbs/ has no category.
        var p = StorePathParser.Parse("/config/.nzbs/Some.Release.nzbz");
        Assert.NotNull(p);
        Assert.Equal("", p!.Category);
        Assert.True(p.IsUncategorised);
    }

    [Fact]
    public void FailedSibling_IsFlagged()
    {
        var p = StorePathParser.Parse("/config/.nzbs/failed/Rejected.Release.nzbz");
        Assert.NotNull(p);
        Assert.True(p.IsFailed);
    }

    [Fact]
    public void NoQueueIdPrefix_KeepsWholeBasename()
    {
        // processor.go only prefixes {queueID}- when queueID > 0.
        var p = StorePathParser.Parse("/config/.nzbs/movies/Some.Movie.2024.nzbz");
        Assert.NotNull(p);
        Assert.Null(p!.QueueId);
        Assert.Equal("Some.Movie.2024", p.NzbBasename);
    }

    [Fact]
    public void BasenameWithDashesButNoQueueId_SplitsOnlyLeadingDigits()
    {
        // Left of the first '-' is non-numeric ⇒ not a queueID; keep whole basename.
        var p = StorePathParser.Parse("/config/.nzbs/tv/Show-Name.S01E01.nzbz");
        Assert.NotNull(p);
        Assert.Null(p!.QueueId);
        Assert.Equal("Show-Name.S01E01", p.NzbBasename);
    }

    [Fact]
    public void QueueIdWithDashesInBasename_SplitsOnFirstDashOnly()
    {
        var p = StorePathParser.Parse("/config/.nzbs/tv/2299-Show.S01E01-GRP.nzbz");
        Assert.NotNull(p);
        Assert.Equal(2299, p!.QueueId);
        Assert.Equal("Show.S01E01-GRP", p.NzbBasename);
    }

    [Fact]
    public void NestedCategoryPath_PreservedRelativeToNzbs()
    {
        var p = StorePathParser.Parse("/config/.nzbs/tv/anime/900-Show.nzbz");
        Assert.NotNull(p);
        Assert.Equal("tv/anime", p!.Category);
    }

    [Fact]
    public void NonStoreOrNoNzbsDir_ReturnsNull()
    {
        Assert.Null(StorePathParser.Parse("/config/other/Show.nzbz"));
        Assert.Null(StorePathParser.Parse("/config/.nzbs/tv/Show.meta"));
        Assert.Null(StorePathParser.Parse(""));
    }

    [Fact]
    public void WindowsSeparators_Normalised()
    {
        var p = StorePathParser.Parse(@"C:\config\.nzbs\tv\5-Show.nzbz");
        Assert.NotNull(p);
        Assert.Equal("tv", p!.Category);
        Assert.Equal("Show", p.NzbBasename);
    }
}
