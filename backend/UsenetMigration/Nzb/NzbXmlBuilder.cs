using System.Globalization;
using System.Text;
using NzbWebDAV.UsenetMigration.Model;

namespace NzbWebDAV.UsenetMigration.Nzb;

/// <summary>
/// Matches Altmount's <c>internal/nzb/builder.go</c> <c>BuildNZB</c>. Renders an
/// <see cref="NzbStore"/> as NZB 1.1 XML, byte-identical to Altmount's output.
///
/// Altmount's builder emits a <c>&lt;!DOCTYPE&gt;</c> line and no
/// <c>&lt;head&gt;</c>; <see cref="EncryptionHeadInjector"/> adds the encryption
/// metadata separately.
/// </summary>
public static class NzbXmlBuilder
{
    private const string Header =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<!DOCTYPE nzb PUBLIC \"-//newzBin//DTD NZB 1.1//EN\" \"http://www.newzbin.com/DTD/nzb/nzb-1.1.dtd\">\n";

    public static string BuildString(NzbStore store)
    {
        var sb = new StringBuilder();
        sb.Append(Header);
        sb.Append("<nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\">\n");
        foreach (var f in store.Files)
        {
            sb.Append("  <file poster=\"");
            GoXmlEscaper.Append(sb, f.Poster);
            sb.Append("\" date=\"");
            sb.Append(f.Date.ToString(CultureInfo.InvariantCulture));
            sb.Append("\" subject=\"");
            GoXmlEscaper.Append(sb, f.Subject);
            sb.Append("\">\n    <groups>\n");
            foreach (var g in f.Groups)
            {
                sb.Append("      <group>");
                GoXmlEscaper.Append(sb, g);
                sb.Append("</group>\n");
            }

            sb.Append("    </groups>\n    <segments>\n");
            foreach (var s in f.Segments)
            {
                sb.Append("      <segment bytes=\"");
                sb.Append(s.Bytes.ToString(CultureInfo.InvariantCulture));
                sb.Append("\" number=\"");
                sb.Append(s.Number.ToString(CultureInfo.InvariantCulture));
                sb.Append("\">");
                GoXmlEscaper.Append(sb, s.Id);
                sb.Append("</segment>\n");
            }

            sb.Append("    </segments>\n  </file>\n");
        }

        sb.Append("</nzb>\n");
        return sb.ToString();
    }

    /// <summary>Renders as UTF-8 bytes, matching Go's <c>[]byte</c> output.</summary>
    public static byte[] Build(NzbStore store) => Encoding.UTF8.GetBytes(BuildString(store));
}
