using NzbWebDAV.UsenetMigration.Naming;
using NzbWebDAV.Utils;

namespace NzbWebDAV.Tests.UsenetMigration;

/// <summary>
/// Verifies the leaf-matching normalization. The key must apply
/// NzbDAV's own SanitizeComponent transform (so it agrees with how leaf DavItems
/// are named), case-fold, and — critically — be independent of the runtime-mutable
/// WindowsSafePaths global so a scan-time value cannot drift from a
/// plan-time recompute.
/// </summary>
public class MatchKeyTests
{
    [Theory]
    [InlineData("Movie.Name.2024.mkv", "movie.name.2024.mkv")]
    [InlineData("MOVIE.NAME.2024.MKV", "movie.name.2024.mkv")] // case-folded
    [InlineData("Episode:Subtitle.mkv", "episode_subtitle.mkv")] // colon sanitized like SanitizeComponent
    public void ForLeaf_NormalisesLikeSanitizeComponentAndCaseFolds(string input, string expected)
    {
        Assert.Equal(expected, MatchKey.ForLeaf(input));
    }

    [Fact]
    public void ForLeaf_IsPinnedToWindowsSafe_IgnoresMutableGlobal()
    {
        // The stored key must be deterministic regardless of the live global. Flip
        // it to the non-default and assert the key is unchanged. Restore in finally
        // because the flag is process-wide.
        var original = PathSanitizer.IsWindowsSafePathsEnabled;
        try
        {
            const string name = "Episode:Subtitle.mkv";

            PathSanitizer.SetWindowsSafePathsEnabled(true);
            var whenOn = MatchKey.ForLeaf(name);

            PathSanitizer.SetWindowsSafePathsEnabled(false);
            var whenOff = MatchKey.ForLeaf(name);

            Assert.Equal(whenOn, whenOff);
            Assert.Equal("episode_subtitle.mkv", whenOff); // windows-safe form, always
        }
        finally
        {
            PathSanitizer.SetWindowsSafePathsEnabled(original);
        }
    }

    [Fact]
    public void ForLeaf_CaseOnlyDifferenceCollapsesToOneKey()
    {
        // Absorbs obfuscation/deobfuscation case changes.
        Assert.Equal(MatchKey.ForLeaf("Show.S01E01.mkv"), MatchKey.ForLeaf("show.s01e01.MKV"));
    }
}
