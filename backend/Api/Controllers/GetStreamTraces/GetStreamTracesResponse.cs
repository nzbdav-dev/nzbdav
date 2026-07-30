using System.Text.Json.Serialization;

namespace NzbWebDAV.Api.Controllers.GetStreamTraces;

public class GetStreamTracesResponse : BaseApiResponse
{
    [JsonPropertyName("enabled")] public required bool Enabled { get; init; }
    [JsonPropertyName("retained")] public required bool Retained { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
    [JsonPropertyName("expiresAtUnixMs")] public required long ExpiresAtUnixMs { get; init; }
    [JsonPropertyName("retainedUntilUnixMs")] public required long RetainedUntilUnixMs { get; init; }
    [JsonPropertyName("capacity")] public required int Capacity { get; init; }
    [JsonPropertyName("eventCount")] public required long EventCount { get; init; }
    [JsonPropertyName("sessionCount")] public required int SessionCount { get; init; }
    [JsonPropertyName("retainedEventCount")] public required long RetainedEventCount { get; init; }
    [JsonPropertyName("overwrittenEventCount")] public required long OverwrittenEventCount { get; init; }
    [JsonPropertyName("oldestRetainedSequence")] public required long OldestRetainedSequence { get; init; }
    [JsonPropertyName("newestRetainedSequence")] public required long NewestRetainedSequence { get; init; }
    [JsonPropertyName("oldestRetainedAtUnixMs")] public required long OldestRetainedAtUnixMs { get; init; }
    [JsonPropertyName("newestRetainedAtUnixMs")] public required long NewestRetainedAtUnixMs { get; init; }
    [JsonPropertyName("overflowed")] public required bool Overflowed { get; init; }
    [JsonPropertyName("sessions")] public required IReadOnlyList<StreamTraceSessionDto> Sessions { get; init; }
}

public class StreamTraceSessionDto
{
    [JsonPropertyName("sessionId")] public required Guid SessionId { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("firstAt")] public required long FirstAt { get; init; }
    [JsonPropertyName("lastAt")] public required long LastAt { get; init; }
    [JsonPropertyName("eventCount")] public required int EventCount { get; init; }
    [JsonPropertyName("lastKind")] public string? LastKind { get; init; }
}
