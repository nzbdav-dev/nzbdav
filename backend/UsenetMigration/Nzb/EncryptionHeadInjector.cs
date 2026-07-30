using System.Text;
using System.Text.RegularExpressions;
using NzbWebDAV.UsenetMigration.Model;

namespace NzbWebDAV.UsenetMigration.Nzb;

/// <summary>
/// Matches Altmount's <c>injectEncryptionMeta</c>
/// (internal/api/file_handlers.go:349). <c>BuildNZB</c> emits no
/// <c>&lt;head&gt;</c>, so this inserts cipher/password/salt <c>&lt;meta&gt;</c>
/// tags after the opening <c>&lt;nzb&gt;</c> tag when the release is encrypted or
/// carries a password.
///
/// <c>&lt;meta type="password"&gt;</c> is the field NzbDAV consumes; cipher/salt
/// are carried for fidelity. The insertion is literal so a password containing
/// <c>$</c> survives.
/// </summary>
public static partial class EncryptionHeadInjector
{
    /// <summary>
    /// Matches Altmount's <c>convertEncryptionToString</c>. RCLONE→"rclone",
    /// HEADERS→"headers", everything else→"none". This preserves Altmount's
    /// behavior of mapping AES to "none".
    /// </summary>
    public static string EncryptionToString(AltmountEncryption encryption) => encryption switch
    {
        AltmountEncryption.Rclone => "rclone",
        AltmountEncryption.Headers => "headers",
        _ => "none",
    };

    /// <summary>
    /// Returns <paramref name="nzbContent"/> with an encryption <c>&lt;head&gt;</c>
    /// injected, or unchanged when the release is unencrypted and password-free.
    /// </summary>
    public static string Inject(string nzbContent, AltmountFileMetadata metadata)
    {
        if (metadata.Encryption == AltmountEncryption.None && string.IsNullOrEmpty(metadata.Password))
            return nzbContent;

        var head = new StringBuilder();
        head.Append("  <head>\n");
        if (metadata.Encryption != AltmountEncryption.None)
        {
            head.Append("    <meta type=\"cipher\">");
            GoXmlEscaper.Append(head, EncryptionToString(metadata.Encryption));
            head.Append("</meta>\n");
        }

        if (!string.IsNullOrEmpty(metadata.Password))
        {
            head.Append("    <meta type=\"password\">");
            GoXmlEscaper.Append(head, metadata.Password);
            head.Append("</meta>\n");
        }

        if (!string.IsNullOrEmpty(metadata.Salt))
        {
            head.Append("    <meta type=\"salt\">");
            GoXmlEscaper.Append(head, metadata.Salt);
            head.Append("</meta>\n");
        }

        head.Append("  </head>\n");
        var headText = head.ToString();

        // Insert the head after the opening <nzb ...> tag (and any trailing newline).
        // MatchEvaluator inserts the head literally — unlike a $-substitution
        // replacement, a "$" in the password cannot be interpreted.
        return NzbOpenTagRegex().Replace(nzbContent, m => m.Value + headText, 1);
    }

    public static byte[] Inject(byte[] nzbContent, AltmountFileMetadata metadata)
    {
        var text = Encoding.UTF8.GetString(nzbContent);
        return Encoding.UTF8.GetBytes(Inject(text, metadata));
    }

    [GeneratedRegex("<nzb[^>]*>\n?")]
    private static partial Regex NzbOpenTagRegex();
}
