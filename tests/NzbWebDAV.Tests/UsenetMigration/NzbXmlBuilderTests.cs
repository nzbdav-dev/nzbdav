using NzbWebDAV.UsenetMigration.Model;
using NzbWebDAV.UsenetMigration.Nzb;

namespace NzbWebDAV.Tests.UsenetMigration;

public class NzbXmlBuilderTests
{
    private static NzbStore SampleStore() => new()
    {
        Files =
        {
            new NzbFileEntry
            {
                Subject = "Cool.Release [1/1]",
                Poster = "poster@example.com",
                Date = 1700000000,
                Groups = { "alt.binaries.test" },
                Segments = { new NzbSeg { Id = "abc@host", Number = 1, Bytes = 700000 } },
            },
        },
    };

    [Fact]
    public void Build_MatchesAltmountStructure_IncludingDoctypeAndLfLineEndings()
    {
        var xml = NzbXmlBuilder.BuildString(SampleStore());

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n", xml);
        Assert.Contains("<!DOCTYPE nzb PUBLIC \"-//newzBin//DTD NZB 1.1//EN\" \"http://www.newzbin.com/DTD/nzb/nzb-1.1.dtd\">\n", xml);
        Assert.Contains("<nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\">\n", xml);
        Assert.Contains("  <file poster=\"poster@example.com\" date=\"1700000000\" subject=\"Cool.Release [1/1]\">\n", xml);
        Assert.Contains("      <group>alt.binaries.test</group>\n", xml);
        Assert.Contains("      <segment bytes=\"700000\" number=\"1\">abc@host</segment>\n", xml);
        Assert.EndsWith("</nzb>\n", xml);
        Assert.DoesNotContain("\r\n", xml); // Go writes LF only
    }

    [Fact]
    public void Build_EscapesLikeGoXmlEscapeText()
    {
        var store = new NzbStore
        {
            Files =
            {
                new NzbFileEntry
                {
                    Subject = "a\"b'c&d<e>f",
                    Poster = "p",
                    Date = 1,
                    Groups = { "g" },
                    Segments = { new NzbSeg { Id = "id", Number = 1, Bytes = 1 } },
                },
            },
        };

        var xml = NzbXmlBuilder.BuildString(store);
        // Go escapes " and ' numerically; & < > as named entities.
        Assert.Contains("subject=\"a&#34;b&#39;c&amp;d&lt;e&gt;f\"", xml);
    }
}
