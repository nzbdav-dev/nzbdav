using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Triage;

namespace NzbWebDAV.Tests.UsenetMigration;

public class CostEstimatorTests
{
    private static NzbFileEntry File(string subject, params long[] segBytes)
    {
        var f = new NzbFileEntry { Subject = subject };
        for (var i = 0; i < segBytes.Length; i++)
            f.Segments.Add(new NzbSeg { Id = $"seg{i}", Number = i + 1, Bytes = segBytes[i] });
        return f;
    }

    [Fact]
    public void NonRarRelease_LazyEqualsEager_FirstSegmentPerFile()
    {
        var store = new NzbStore
        {
            Files =
            {
                File("Movie [01/02] - \"movie.mkv\" yEnc (1/3)", 700_000, 650_000, 100_000),
                File("Movie [02/02] - \"movie.nfo\" yEnc (1/1)", 5_000),
            },
        };

        var est = CostEstimator.Estimate(store);
        Assert.False(est.IsRarRelease);
        Assert.Equal(705_000, est.EstFetchBytesLazy);          // 700000 + 5000
        Assert.Equal(est.EstFetchBytesLazy, est.EstFetchBytesEager);
        Assert.Equal(1_455_000, est.TotalBytes);
        Assert.Equal(2, est.NzbFileCount);
        Assert.Equal(4, est.SegmentCount);
    }

    [Fact]
    public void RarRelease_EagerAddsLastSegmentOfArchiveFiles()
    {
        var store = new NzbStore
        {
            Files = { File("Rel [01/01] - \"file.part01.rar\" yEnc (1/3)", 700_000, 650_000, 120_000) },
        };

        var est = CostEstimator.Estimate(store);
        Assert.True(est.IsRarRelease);
        Assert.Equal(700_000, est.EstFetchBytesLazy);
        Assert.Equal(820_000, est.EstFetchBytesEager); // + last segment 120000
    }

    [Fact]
    public void SingleSegmentArchive_EagerEqualsLazy_NoDoubleCount()
    {
        var store = new NzbStore
        {
            Files = { File("Rel [01/01] - \"file.7z\" yEnc (1/1)", 700_000) },
        };

        var est = CostEstimator.Estimate(store);
        Assert.True(est.IsRarRelease);
        Assert.Equal(est.EstFetchBytesLazy, est.EstFetchBytesEager);
    }

    [Fact]
    public void FilesWithoutSegments_ContributeNothingToFetchCost()
    {
        var store = new NzbStore { Files = { File("Empty [01/01] - \"x.bin\" yEnc") } };
        var est = CostEstimator.Estimate(store);
        Assert.Equal(0, est.EstFetchBytesLazy);
        Assert.Equal(0, est.SegmentCount);
    }
}
