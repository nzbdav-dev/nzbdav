using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Triage;

namespace NzbWebDAV.Tests.UsenetMigration;

public class TriageClassifierTests
{
    private static AltmountFileMetadata Meta(
        AltmountFileStatus status = AltmountFileStatus.Healthy,
        AltmountEncryption enc = AltmountEncryption.None,
        string password = "",
        bool nested = false,
        bool clips = false) => new()
        {
            StoreRef = "/config/.nzbs/tv/1-x.nzbz",
            Status = status,
            Encryption = enc,
            Password = password,
            HasNestedSources = nested,
            HasClipBoundaries = clips,
        };

    private static TriageInput Input(
        bool hasStoreRef = true,
        StoreAvailability store = StoreAvailability.Ok,
        IReadOnlyList<AltmountFileMetadata>? metas = null,
        bool categoryMapped = true,
        bool jobNameDiverges = false,
        bool filenamePasswordMarker = false) => new()
        {
            HasStoreRef = hasStoreRef,
            Store = store,
            Metas = metas ?? new[] { Meta() },
            CategoryMapped = categoryMapped,
            JobNameDiverges = jobNameDiverges,
            FilenamePasswordMarker = filenamePasswordMarker,
        };

    [Fact]
    public void CleanHealthyRelease_IsGreen_NoReasons()
    {
        var reasons = TriageClassifier.Classify(Input());
        Assert.Empty(reasons);
        Assert.Equal(Verdict.Green, VerdictReason.VerdictFor(reasons));
    }

    [Fact]
    public void V1Metadata_NoStoreRef_IsRed_AndNothingElse()
    {
        var reasons = TriageClassifier.Classify(Input(hasStoreRef: false));
        Assert.Equal(new[] { VerdictReason.NoStoreRef }, reasons);
        Assert.Equal(Verdict.Red, VerdictReason.VerdictFor(reasons));
    }

    [Theory]
    [InlineData(StoreAvailability.Missing, VerdictReason.StoreMissing)]
    [InlineData(StoreAvailability.Corrupt, VerdictReason.StoreCorrupt)]
    [InlineData(StoreAvailability.Empty, VerdictReason.StoreEmpty)]
    public void StoreLevelFailures_ShortCircuitToSingleRedReason(StoreAvailability store, string expected)
    {
        var reasons = TriageClassifier.Classify(Input(store: store));
        Assert.Equal(new[] { expected }, reasons);
    }

    [Fact]
    public void AllFilesCorrupted_IsRedStatusCorrupted()
    {
        var metas = new[] { Meta(AltmountFileStatus.Corrupted), Meta(AltmountFileStatus.Corrupted) };
        var reasons = TriageClassifier.Classify(Input(metas: metas));
        Assert.Equal(new[] { VerdictReason.StatusCorrupted }, reasons);
    }

    [Fact]
    public void SomeFilesCorrupted_IsAmber_ReplacingRemovedStatusDegraded()
    {
        var metas = new[] { Meta(AltmountFileStatus.Healthy), Meta(AltmountFileStatus.Corrupted) };
        var reasons = TriageClassifier.Classify(Input(metas: metas));
        Assert.Contains(VerdictReason.SomeFilesCorrupted, reasons);
        Assert.Equal(Verdict.Amber, VerdictReason.VerdictFor(reasons));
    }

    [Fact]
    public void UnmappedCategory_IsRed_AndBlocksReview()
    {
        var reasons = TriageClassifier.Classify(Input(categoryMapped: false));
        Assert.Contains(VerdictReason.CategoryUnmapped, reasons);
        Assert.True(VerdictReason.BlocksReview(VerdictReason.CategoryUnmapped));
    }

    [Fact]
    public void EncryptionPasswordNestedClips_AllSurfaceAsAmberReasons()
    {
        var metas = new[]
        {
            Meta(enc: AltmountEncryption.Rclone, password: "pw", nested: true, clips: true),
        };
        var reasons = TriageClassifier.Classify(Input(metas: metas));
        Assert.Contains(VerdictReason.Encrypted, reasons);
        Assert.Contains(VerdictReason.Password, reasons);
        Assert.Contains(VerdictReason.NestedSources, reasons);
        Assert.Contains(VerdictReason.ClipBoundaries, reasons);
        Assert.Equal(Verdict.Amber, VerdictReason.VerdictFor(reasons));
    }

    [Fact]
    public void MultipleReasons_VerdictIsMaxSeverity()
    {
        // job_name_diverges (Amber) + category_unmapped (Red) produces Red.
        var reasons = TriageClassifier.Classify(Input(categoryMapped: false, jobNameDiverges: true));
        Assert.Contains(VerdictReason.JobNameDiverges, reasons);
        Assert.Contains(VerdictReason.CategoryUnmapped, reasons);
        Assert.Equal(Verdict.Red, VerdictReason.VerdictFor(reasons));
    }

    [Fact]
    public void FilenamePasswordMarker_IsAmber()
    {
        var reasons = TriageClassifier.Classify(Input(filenamePasswordMarker: true));
        Assert.Contains(VerdictReason.FilenamePasswordMarker, reasons);
    }
}
