using NzbWebDAV.UsenetMigration.Source;

namespace NzbWebDAV.Tests.UsenetMigration;

public class AltmountConfigReaderTests
{
    // Mirrors the shape of Altmount's config.sample.yaml sabnzbd block.
    private static readonly string[] SampleConfig =
    {
        "webdav:",
        "  port: 8080",
        "sabnzbd:",
        "  enabled: true",
        "  complete_dir: '/' # Base virtual directory",
        "  categories: # Download categories",
        "    - name: 'Default' # System default category",
        "      order: 0",
        "      priority: -100",
        "      dir: 'complete'",
        "      type: ''",
        "    - name: 'movies'",
        "      order: 1",
        "      priority: -100",
        "      dir: 'movies'",
        "      type: 'radarr'",
        "    - name: 'tv'",
        "      order: 2",
        "      dir: 'tv'",
        "      type: 'sonarr'",
        "health:",
        "  enabled: true",
    };

    [Fact]
    public void Parse_ExtractsAllCategoriesWithFields()
    {
        var cfg = AltmountConfigReader.Parse(SampleConfig);

        Assert.Equal("/", cfg.CompleteDir);
        Assert.Equal(3, cfg.Categories.Count);

        Assert.Equal("Default", cfg.Categories[0].Name);
        Assert.Equal("complete", cfg.Categories[0].Dir);
        Assert.Equal(-100, cfg.Categories[0].Priority);
        Assert.Equal("", cfg.Categories[0].Type);

        Assert.Equal("movies", cfg.Categories[1].Name);
        Assert.Equal("radarr", cfg.Categories[1].Type);

        Assert.Equal("tv", cfg.Categories[2].Name);
        Assert.Equal("sonarr", cfg.Categories[2].Type);
    }

    [Fact]
    public void Parse_StopsAtNextTopLevelKey_DoesNotBleedIntoHealth()
    {
        var cfg = AltmountConfigReader.Parse(SampleConfig);
        Assert.DoesNotContain(cfg.Categories, c => c.Name == "enabled");
    }

    [Fact]
    public void Parse_NoSabnzbdBlock_ReturnsEmpty()
    {
        var cfg = AltmountConfigReader.Parse(new[] { "webdav:", "  port: 8080" });
        Assert.Empty(cfg.Categories);
    }

    [Theory]
    [InlineData("'quoted'", "quoted")]
    [InlineData("\"dquoted\"", "dquoted")]
    [InlineData("bare # comment", "bare")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("", "")]
    public void ScalarValue_HandlesQuotesAndComments(string input, string expected)
    {
        Assert.Equal(expected, AltmountConfigReader.ScalarValue(input));
    }
}
