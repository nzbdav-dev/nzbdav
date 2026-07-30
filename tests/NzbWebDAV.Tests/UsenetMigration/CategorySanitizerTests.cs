using NzbWebDAV.UsenetMigration.Source;

namespace NzbWebDAV.Tests.UsenetMigration;

/// <summary>
/// Verifies parity with Altmount's inline category sanitizer, including the
/// ".."/"." rule that blanks the entire category.
/// </summary>
public class CategorySanitizerTests
{
    [Theory]
    [InlineData("tv", "tv")]
    [InlineData(@"tv\anime", "tv/anime")]
    [InlineData("/tv/", "tv")]
    [InlineData("//movies//", "movies")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Sanitize_HappyPaths(string? input, string expected)
    {
        Assert.Equal(expected, CategorySanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("tv/../secret")]
    [InlineData("./tv")]
    [InlineData("..")]
    [InlineData(".")]
    public void Sanitize_TraversalSegment_BlanksWholeCategory(string input)
    {
        Assert.Equal("", CategorySanitizer.Sanitize(input));
    }

    [Fact]
    public void Sanitize_NoCaseFoldingOrCharStripping()
    {
        // The Go transform does none of these — preserve exactly.
        Assert.Equal("TV Shows", CategorySanitizer.Sanitize("TV Shows"));
    }
}
