using NzbWebDAV.Streams;

namespace NzbWebDAV.Tests.Streams;

public class AdaptiveBodyBatchSizerTests
{
    [Fact]
    public void TwoNonAdjacentStarvedInEight_NarrowsFourToTwoThenTwoToOne()
    {
        var sizer = new AdaptiveBodyBatchSizer(4);
        // S-R-R-R-S-R-R-R → two starved in eight → 4→2
        ObservePattern(sizer, "SRRRSRRR");
        Assert.Equal(2, sizer.Current);

        // Same pattern again after window clear → 2→1
        ObservePattern(sizer, "SRRRSRRR");
        Assert.Equal(1, sizer.Current);
    }

    [Fact]
    public void OneStarvedInEight_DoesNotNarrow()
    {
        var sizer = new AdaptiveBodyBatchSizer(4);
        ObservePattern(sizer, "SRRRRRRR");
        Assert.Equal(4, sizer.Current);
    }

    [Fact]
    public void SixteenConsecutiveReady_RecoversOneStepAtATime()
    {
        var sizer = new AdaptiveBodyBatchSizer(4);
        ObservePattern(sizer, "SRRRSRRR"); // 4→2
        ObservePattern(sizer, "SRRRSRRR"); // 2→1
        Assert.Equal(1, sizer.Current);

        ObservePattern(sizer, new string('R', 16));
        Assert.Equal(2, sizer.Current);

        ObservePattern(sizer, new string('R', 16));
        Assert.Equal(4, sizer.Current);
    }

    [Fact]
    public void FifteenReadyThenStarved_DoesNotRecover()
    {
        var sizer = new AdaptiveBodyBatchSizer(4);
        ObservePattern(sizer, "SRRRSRRR"); // 4→2
        ObservePattern(sizer, new string('R', 15) + "S");
        Assert.Equal(2, sizer.Current);
    }

    [Fact]
    public void AlternatingObservations_ResizeOnlyOncePerClearedWindow()
    {
        var sizer = new AdaptiveBodyBatchSizer(4);
        // Alternating fills the window with four starved → one narrow, then clear.
        ObservePattern(sizer, "SRSRSRSR");
        Assert.Equal(2, sizer.Current);

        // Next eight alternating → one more narrow, not a flap within the window.
        ObservePattern(sizer, "SRSRSRSR");
        Assert.Equal(1, sizer.Current);

        // Further starvation at floor must not flap.
        ObservePattern(sizer, "SRSRSRSR");
        Assert.Equal(1, sizer.Current);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MaximumSizesStayWithinBounds(int maximum)
    {
        var sizer = new AdaptiveBodyBatchSizer(maximum);
        Assert.Equal(maximum, sizer.Current);

        // Drive starvation until the floor.
        for (var i = 0; i < 8; i++)
            ObservePattern(sizer, "SRRRSRRR");
        Assert.Equal(1, sizer.Current);

        // Recover past the configured maximum must clamp.
        for (var i = 0; i < 8; i++)
            ObservePattern(sizer, new string('R', 16));
        Assert.Equal(maximum, sizer.Current);
        Assert.InRange(sizer.Current, 1, maximum);
    }

    private static void ObservePattern(AdaptiveBodyBatchSizer sizer, string pattern)
    {
        foreach (var c in pattern)
        {
            var ready = c is 'R' or 'r';
            sizer.Observe(ready);
        }
    }
}
