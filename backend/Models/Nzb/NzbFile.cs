using System.Text.RegularExpressions;

namespace NzbWebDAV.Models.Nzb;

public class NzbFile
{
    public required string Subject { get; init; }
    public List<NzbSegment> Segments { get; } = [];

    public string[] GetSegmentIds()
    {
        return Segments
            .Select(x => x.MessageId)
            .ToArray();
    }

    public long GetTotalYencodedSize()
    {
        return Segments
            .Select(x => x.Bytes)
            .Sum();
    }

    public string GetSubjectFileName()
    {
        return GetFirstValidNonEmptyFilename(
            TryParseSubjectFilename1,
            TryParseSubjectFilename2
        );
    }

    private string TryParseSubjectFilename1()
    {
        // The most common format is when filename appears in double quotes
        // example: `[1/8] - "file.mkv" yEnc 12345 (1/54321)`
        var match = Regex.Match(Subject, "\\\"(.*)\\\"");
        return match.Success ? match.Groups[1].Value : "";
    }

    private string TryParseSubjectFilename2()
    {
        // Otherwise, use sabnzbd's regex
        // https://github.com/sabnzbd/sabnzbd/blob/b6b0d10367fd4960bad73edd1d3812cafa7fc002/sabnzbd/nzbstuff.py#L106
        var match = Regex.Match(Subject, @"\b([\w\-+()' .,]+(?:\[[\w\-\/+()' .,]*][\w\-+()' .,]*)*\.[A-Za-z0-9]{2,4})\b");
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string GetFirstValidNonEmptyFilename(params Func<string>[] funcs)
    {
        return funcs
            .Select(x => x.Invoke())
            .Where(x => x == Path.GetFileName(x))
            .FirstOrDefault(x => x != "") ?? "";
    }
}