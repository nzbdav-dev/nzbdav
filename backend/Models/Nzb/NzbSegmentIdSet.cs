using System.Text.Json;

namespace NzbWebDAV.Models.Nzb;

public static class NzbSegmentIdSet
{
    public static string Encode(IReadOnlyList<string> segmentIds)
    {
        ArgumentNullException.ThrowIfNull(segmentIds);
        if (segmentIds.Count == 0)
            throw new ArgumentException("At least one segment id is required.", nameof(segmentIds));

        return segmentIds.Count == 1
            ? segmentIds[0]
            : JsonSerializer.Serialize(segmentIds, (JsonSerializerOptions?)null);
    }

    public static string[] Decode(string encodedSegmentId)
    {
        if (string.IsNullOrWhiteSpace(encodedSegmentId))
            return [];

        if (encodedSegmentId[0] != '[')
            return [encodedSegmentId];

        try
        {
            var decoded = JsonSerializer.Deserialize<string[]>(encodedSegmentId, (JsonSerializerOptions?)null);
            return decoded?
                .Where(segmentId => !string.IsNullOrWhiteSpace(segmentId))
                .ToArray()
                   ?? [encodedSegmentId];
        }
        catch (JsonException)
        {
            return [encodedSegmentId];
        }
    }
}
