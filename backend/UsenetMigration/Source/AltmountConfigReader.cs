using NzbWebDAV.UsenetMigration.Model;

namespace NzbWebDAV.UsenetMigration.Source;

/// <summary>
/// Reads the <c>sabnzbd.categories</c> list (and <c>complete_dir</c>) from an
/// Altmount <c>config.yaml</c>.
///
/// This reader handles the minimal block-style subset Altmount emits: a top-level
/// <c>sabnzbd:</c> map containing a
/// <c>categories:</c> sequence of simple scalar maps. It deliberately does NOT
/// implement flow style, anchors, or block scalars — a config using those, or
/// any parse ambiguity, surfaces as a thrown <see cref="AltmountConfigException"/>
/// rather than a silently wrong category list. The category set is also
/// cross-checked against the store tree during Scan, so a missed category still
/// shows up as config drift.
/// </summary>
public static class AltmountConfigReader
{
    public sealed class AltmountSabConfig
    {
        public string CompleteDir { get; init; } = "";
        public IReadOnlyList<AltmountCategory> Categories { get; init; } = Array.Empty<AltmountCategory>();
    }

    public static async Task<AltmountSabConfig> ReadAsync(string configPath, CancellationToken ct = default)
    {
        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(configPath, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new AltmountConfigException($"Failed to read config '{configPath}'.", e);
        }

        return Parse(lines);
    }

    public static AltmountSabConfig Parse(IReadOnlyList<string> lines)
    {
        var sab = FindTopLevelBlock(lines, "sabnzbd");
        if (sab is null)
            return new AltmountSabConfig();

        var (start, end, _) = sab.Value;
        var completeDir = "";
        var categories = new List<AltmountCategory>();

        for (var i = start; i < end; i++)
        {
            var raw = lines[i];
            if (IsBlankOrComment(raw)) continue;
            var indent = LeadingSpaces(raw);
            var trimmed = raw.Trim();

            // Direct children of sabnzbd sit at the block's base indent.
            if (trimmed.StartsWith("complete_dir:", StringComparison.Ordinal))
                completeDir = ScalarValue(trimmed["complete_dir:".Length..]);
            else if (trimmed == "categories:" || trimmed.StartsWith("categories:", StringComparison.Ordinal))
                categories = ParseCategories(lines, i + 1, end, indent);
        }

        return new AltmountSabConfig { CompleteDir = completeDir, Categories = categories };
    }

    private static List<AltmountCategory> ParseCategories(
        IReadOnlyList<string> lines, int start, int end, int categoriesIndent)
    {
        var result = new List<AltmountCategory>();

        // Per-item accumulator.
        string? name = null, dir = null, type = null;
        int? order = null, priority = null;
        var haveItem = false;
        var markerIndent = -1;

        void Flush()
        {
            if (!haveItem) return;
            result.Add(new AltmountCategory
            {
                Name = name ?? "",
                Dir = dir ?? "",
                Type = type ?? "",
                Order = order ?? 0,
                Priority = priority ?? 0,
            });
            name = dir = type = null;
            order = priority = null;
            haveItem = false;
        }

        for (var i = start; i < end; i++)
        {
            var raw = lines[i];
            if (IsBlankOrComment(raw)) continue;
            var indent = LeadingSpaces(raw);

            // Dedent back to or past the categories key ends the sequence.
            if (indent <= categoriesIndent) break;

            var trimmed = raw.Trim();

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed == "-")
            {
                Flush();
                haveItem = true;
                markerIndent = indent;
                var rest = trimmed.Length >= 2 ? trimmed[2..].Trim() : "";
                if (rest.Length > 0)
                    AssignField(rest, ref name, ref dir, ref type, ref order, ref priority);
            }
            else if (haveItem && indent > markerIndent)
            {
                AssignField(trimmed, ref name, ref dir, ref type, ref order, ref priority);
            }
        }

        Flush();
        return result;
    }

    private static void AssignField(
        string keyValue,
        ref string? name, ref string? dir, ref string? type,
        ref int? order, ref int? priority)
    {
        var colon = keyValue.IndexOf(':');
        if (colon < 0) return;
        var key = keyValue[..colon].Trim();
        var value = ScalarValue(keyValue[(colon + 1)..]);

        switch (key)
        {
            case "name":
                name = value;
                break;
            case "dir":
                dir = value;
                break;
            case "type":
                type = value;
                break;
            case "order":
                if (int.TryParse(value, out var o)) order = o;
                break;
            case "priority":
                if (int.TryParse(value, out var p)) priority = p;
                break;
        }
    }

    /// <summary>
    /// Locates a top-level (indent 0) mapping key and returns the [start,end)
    /// line range of its block body plus the key's indent.
    /// </summary>
    private static (int start, int end, int indent)? FindTopLevelBlock(IReadOnlyList<string> lines, string key)
    {
        var header = key + ":";
        for (var i = 0; i < lines.Count; i++)
        {
            var raw = lines[i];
            if (IsBlankOrComment(raw)) continue;
            if (LeadingSpaces(raw) != 0) continue;
            var trimmed = raw.TrimEnd();
            if (trimmed != header && !trimmed.StartsWith(header, StringComparison.Ordinal))
                continue;

            // Body runs until the next non-blank line at indent 0.
            var end = lines.Count;
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (IsBlankOrComment(lines[j])) continue;
                if (LeadingSpaces(lines[j]) == 0)
                {
                    end = j;
                    break;
                }
            }

            return (i + 1, end, 0);
        }

        return null;
    }

    private static int LeadingSpaces(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] == ' ') n++;
        return n;
    }

    private static bool IsBlankOrComment(string line)
    {
        var t = line.TrimStart();
        return t.Length == 0 || t[0] == '#';
    }

    /// <summary>
    /// Extracts a scalar value: strips surrounding single/double quotes (taking
    /// everything inside), or for unquoted values strips a trailing
    /// <c> #</c> comment and surrounding whitespace.
    /// </summary>
    internal static string ScalarValue(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0) return "";

        if (s[0] is '\'' or '"')
        {
            var quote = s[0];
            var close = s.IndexOf(quote, 1);
            if (close > 0)
                return s.Substring(1, close - 1);
            // Unterminated quote — fall through and return the remainder.
            return s[1..];
        }

        // Unquoted: cut an inline comment introduced by " #".
        var hash = s.IndexOf(" #", StringComparison.Ordinal);
        if (hash >= 0) s = s[..hash];
        return s.Trim();
    }
}

/// <summary>Raised when the Altmount config cannot be read or parsed.</summary>
public sealed class AltmountConfigException(string message, Exception inner) : Exception(message, inner);
