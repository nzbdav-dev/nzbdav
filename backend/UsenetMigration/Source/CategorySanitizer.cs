namespace NzbWebDAV.UsenetMigration.Source;

/// <summary>
/// Matches Altmount's inline category sanitizer
/// (internal/importer/processor.go:513-520, duplicated in queue_handlers.go).
///
/// The entire transform: backslashes → forward slashes; trim leading/trailing
/// slashes; if any path segment is "." or "..", blank the WHOLE category. No
/// case-folding, no character stripping, no length limits, no whitespace
/// handling.
/// </summary>
public static class CategorySanitizer
{
    public static string Sanitize(string? category)
    {
        if (string.IsNullOrEmpty(category)) return "";

        var s = category.Replace('\\', '/').Trim('/');
        foreach (var part in s.Split('/'))
        {
            if (part is ".." or ".")
                return "";
        }

        return s;
    }
}
