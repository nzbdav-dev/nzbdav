using NzbWebDAV.UsenetMigration.Naming;

namespace NzbWebDAV.Tests.UsenetMigration;

/// <summary>
/// Verifies that migration naming uses NzbDAV's own transforms instead of assuming
/// <c>basename == JobName</c>. Each case runs the raw store basename through
/// AddFileRequest.ResolveFileName and FilenameUtil.GetJobName via NzbDavNaming,
/// then asserts the mount-folder name NzbDAV produces.
/// </summary>
public class NzbDavNamingTests
{
    [Theory]
    // basename, expected QueueFileName, expected JobName
    [InlineData("Some.Movie.2024.1080p.BluRay.x264-GRP", "Some.Movie.2024.1080p.BluRay.x264-GRP.nzb", "Some.Movie.2024.1080p.BluRay.x264-GRP")]
    [InlineData("Show.S01E01.HDTV{{hunter2}}", "Show.S01E01.HDTV{{hunter2}}.nzb", "Show.S01E01.HDTV")]
    [InlineData("Release.With.Colon:Subtitle", "Release.With.Colon:Subtitle.nzb", "Release.With.Colon_Subtitle")]
    [InlineData("Movie.Name.2024.", "Movie.Name.2024..nzb", "Movie.Name.2024")]
    [InlineData("Already.Named.nzb", "Already.Named.nzb", "Already.Named")]
    [InlineData("CON", "CON.nzb", "_CON")]
    public void Fixtures_MatchRealNzbDavTransforms(string basename, string expectedQueueFileName, string expectedJobName)
    {
        Assert.Equal(expectedQueueFileName, NzbDavNaming.QueueFileName(basename));
        Assert.Equal(expectedJobName, NzbDavNaming.JobName(basename));
    }

    [Fact]
    public void PasswordMarker_StripsIntoJobName_NotIdentity()
    {
        // Queue filenames and job names are not identical in the general case.
        var basename = "Show.S01E01.HDTV{{hunter2}}";
        Assert.NotEqual(basename, NzbDavNaming.JobName(basename));
    }

    [Fact]
    public void SanitizeIsManyToOne_DistinctFileNamesCollapseToOneJobName()
    {
        // Three distinct FileNames can resolve to one mount folder.
        var a = "Release.Name:Subtitle";
        var b = "Release.Name*Subtitle";
        var c = "Release.Name?Subtitle";

        Assert.NotEqual(NzbDavNaming.QueueFileName(a), NzbDavNaming.QueueFileName(b));
        Assert.NotEqual(NzbDavNaming.QueueFileName(b), NzbDavNaming.QueueFileName(c));

        Assert.Equal("Release.Name_Subtitle", NzbDavNaming.JobName(a));
        Assert.Equal("Release.Name_Subtitle", NzbDavNaming.JobName(b));
        Assert.Equal("Release.Name_Subtitle", NzbDavNaming.JobName(c));
    }
}
