using NzbWebDAV.UsenetMigration.Triage;

namespace NzbWebDAV.Tests.UsenetMigration;

public class CollisionDetectorTests
{
    private static CollisionCandidate C(string storeRef, string cat, string queueFileName, string jobName, string submit)
        => new()
        {
            StoreRef = storeRef,
            TargetCategory = cat,
            QueueFileName = queueFileName,
            JobName = jobName,
            SubmitFileName = submit,
        };

    [Fact]
    public void Pass1_TwoStoresShareQueueKey_BothRed_NeitherAutoPicked()
    {
        // Distinct .nzbz stores sharing a basename produce the same queue key.
        var a = C("/nzbs/tv/1041-Show.S01E01.HDTV.nzbz", "tv", "Show.S01E01.HDTV.nzb", "Show.S01E01.HDTV", "Show.S01E01.HDTV");
        var b = C("/nzbs/tv/2299-Show.S01E01.HDTV.nzbz", "tv", "Show.S01E01.HDTV.nzb", "Show.S01E01.HDTV", "Show.S01E01.HDTV");

        var result = CollisionDetector.Detect(new[] { a, b });

        foreach (var candidate in new[] { a, b })
        {
            var findings = result.FindingsByStoreRef[candidate.StoreRef];
            Assert.Contains(findings, f => f.Reason == VerdictReason.QueueKeyCollision);
        }

        // Each records the other as a sibling — never a winner.
        Assert.Contains(b.StoreRef, result.FindingsByStoreRef[a.StoreRef].Single().SiblingStoreRefs);
        Assert.Contains(a.StoreRef, result.FindingsByStoreRef[b.StoreRef].Single().SiblingStoreRefs);
    }

    [Fact]
    public void Pass2_DistinctFileNamesSameJobName_AllAmber_NotRed()
    {
        // ':', '*', and '?' sanitize to one JobName but distinct FileNames.
        var a = C("/nzbs/tv/1-a.nzbz", "tv", "Release.Name:Subtitle.nzb", "Release.Name_Subtitle", "Release.Name:Subtitle");
        var b = C("/nzbs/tv/2-b.nzbz", "tv", "Release.Name*Subtitle.nzb", "Release.Name_Subtitle", "Release.Name*Subtitle");
        var c = C("/nzbs/tv/3-c.nzbz", "tv", "Release.Name?Subtitle.nzb", "Release.Name_Subtitle", "Release.Name?Subtitle");

        var result = CollisionDetector.Detect(new[] { a, b, c });

        foreach (var cand in new[] { a, b, c })
        {
            var findings = result.FindingsByStoreRef[cand.StoreRef];
            Assert.Contains(findings, f => f.Reason == VerdictReason.MountFolderCollision);
            Assert.DoesNotContain(findings, f => f.Reason == VerdictReason.QueueKeyCollision);
        }
    }

    [Fact]
    public void NoCollisions_ProducesNoFindings()
    {
        var a = C("/nzbs/tv/1-a.nzbz", "tv", "A.nzb", "A", "A");
        var b = C("/nzbs/tv/2-b.nzbz", "tv", "B.nzb", "B", "B");
        var result = CollisionDetector.Detect(new[] { a, b });
        Assert.Empty(result.FindingsByStoreRef);
    }

    [Fact]
    public void DifferentCategories_SameFileName_DoNotCollide()
    {
        var a = C("/nzbs/tv/1-x.nzbz", "tv", "X.nzb", "X", "X");
        var b = C("/nzbs/movies/2-x.nzbz", "movies", "X.nzb", "X", "X");
        var result = CollisionDetector.Detect(new[] { a, b });
        Assert.Empty(result.FindingsByStoreRef);
    }

    [Fact]
    public void Pass4_ReportsJobNameDivergenceRate()
    {
        var a = C("/nzbs/tv/1-a.nzbz", "tv", "A.nzb", "A", "A");            // identity
        var b = C("/nzbs/tv/2-b.nzbz", "tv", "B_x.nzb", "B_x", "B:x");      // diverges
        var result = CollisionDetector.Detect(new[] { a, b });
        Assert.Equal(2, result.TotalReleases);
        Assert.Equal(1, result.JobNameDivergentReleases);
        Assert.Equal(0.5, result.JobNameDivergenceRate);
    }

    [Fact]
    public void Pass3_AgainstExistingContent_FlagsQueueItemRedAndMountFolderAmber()
    {
        var evict = C("/nzbs/tv/1-live.nzbz", "tv", "Live.nzb", "Live", "Live");
        var folder = C("/nzbs/tv/2-exists.nzbz", "tv", "Exists.nzb", "Exists", "Exists");
        var clean = C("/nzbs/tv/3-clean.nzbz", "tv", "Clean.nzb", "Clean", "Clean");

        var findings = CollisionDetector.DetectAgainstExisting(
            new[] { evict, folder, clean },
            queueItemExists: (cat, qfn) => cat == "tv" && qfn == "Live.nzb",
            davItemExists: (cat, job) => cat == "tv" && job == "Exists");

        Assert.Contains(findings[evict.StoreRef], f => f.Reason == VerdictReason.CollidesWithExistingQueueItem);
        Assert.Contains(findings[folder.StoreRef], f => f.Reason == VerdictReason.MountFolderExists);
        Assert.False(findings.ContainsKey(clean.StoreRef));
    }
}
