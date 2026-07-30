using System.Text;

namespace NzbWebDAV.UsenetMigration.Nzb;

/// <summary>
/// Byte-exact replica of Go's <c>encoding/xml.EscapeText</c>. Altmount builds
/// its NZB XML with that function, so regenerated NZBs use the same escaping —
/// notably <c>"</c>→<c>&amp;#34;</c>, <c>'</c>→<c>&amp;#39;</c>, and tab/LF/CR →
/// <c>&amp;#x9;</c>/<c>&amp;#xA;</c>/<c>&amp;#xD;</c>, which differ from .NET's
/// built-in XML writers.
/// </summary>
public static class GoXmlEscaper
{
    public static void Append(StringBuilder sb, string s)
    {
        foreach (var rune in s.EnumerateRunes())
        {
            switch (rune.Value)
            {
                case '"':
                    sb.Append("&#34;");
                    break;
                case '\'':
                    sb.Append("&#39;");
                    break;
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '\t':
                    sb.Append("&#x9;");
                    break;
                case '\n':
                    sb.Append("&#xA;");
                    break;
                case '\r':
                    sb.Append("&#xD;");
                    break;
                default:
                    if (IsInCharacterRange(rune.Value))
                        sb.Append(rune.ToString());
                    else
                        sb.Append('�');
                    break;
            }
        }
    }

    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        Append(sb, s);
        return sb.ToString();
    }

    // Mirrors Go's xml.isInCharacterRange.
    private static bool IsInCharacterRange(int r) =>
        r == 0x09 ||
        r == 0x0A ||
        r == 0x0D ||
        (r >= 0x20 && r <= 0xD7FF) ||
        (r >= 0xE000 && r <= 0xFFFD) ||
        (r >= 0x10000 && r <= 0x10FFFF);
}
