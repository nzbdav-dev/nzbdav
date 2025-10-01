using System.Text.Json;
using System.Text.Json.Serialization;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Api.SabControllers.GetQueue;

public class GetQueueResponse : SabBaseResponse
{
    [JsonPropertyName("queue")]
    public QueueObject Queue { get; init; } = new();

    public class QueueObject
    {
        [JsonPropertyName("paused")]
        public bool Paused { get; init; } = false;

        [JsonPropertyName("slots")]
        public List<QueueSlot> Slots { get; init; } = new();
    }

    public class QueueSlot
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("nzo_id")]
        public string NzoId { get; init; }

        [JsonPropertyName("priority")]
        public string Priority { get; init; }

        [JsonPropertyName("filename")]
        public string Filename { get; init; }

        [JsonPropertyName("cat")]
        public string Category { get; init; }

        [JsonPropertyName("percentage")]
        public string Percentage { get; init; }

        [JsonPropertyName("true_percentage")]
        public string TruePercentage { get; init; }

        [JsonPropertyName("status")]
        public string Status { get; init; }

        [JsonPropertyName("timeleft")]
        [JsonConverter(typeof(SabnzbdQueueTimeConverter))]
        public TimeSpan TimeLeft { get; init; }

        [JsonPropertyName("mb")]
        public string SizeInMB { get; init; }

        [JsonPropertyName("mbleft")]
        public string SizeLeftInMB { get; init; }

        public static QueueSlot FromQueueItem
        (
            QueueItem queueItem,
            int index = 0,
            int progressPercentage = 0,
            string status = "Queued"
        )
        {
            return new QueueSlot
            {
                Index = index,
                NzoId = queueItem!.Id.ToString(),
                Priority = queueItem.Priority.ToString(),
                Filename = queueItem.FileName,
                Category = queueItem.Category,
                Percentage = (progressPercentage % 100).ToString(),
                TruePercentage = progressPercentage.ToString(),
                Status = status,
                TimeLeft = TimeSpan.Zero,
                SizeInMB = FormatSizeMB(queueItem.TotalSegmentBytes),
                SizeLeftInMB = FormatSizeMB((100 - progressPercentage) * queueItem.TotalSegmentBytes / 100),
            };
        }

        private static string FormatSizeMB(long bytes)
        {
            var megabytes = bytes / (1024.0 * 1024.0);
            return megabytes.ToString("0.00");
        }
    }

    public class SabnzbdQueueTimeConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) =>
            throw new NotImplementedException();

        public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(@"d\:h\:m\:s"));
    }
}