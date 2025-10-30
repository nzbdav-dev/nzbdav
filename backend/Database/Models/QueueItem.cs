namespace NzbWebDAV.Database.Models;

public class QueueItem
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string FileName { get; set; } = null!;
    public string JobName { get; set; } = null!;
    public long NzbFileSize { get; set; }
    public long TotalSegmentBytes { get; set; }
    public string Category { get; set; } = null!;
    public PriorityOption Priority { get; set; }
    public PostProcessingOption PostProcessing { get; set; }
    public DateTime? PauseUntil { get; set; }

    public enum PriorityOption
    {
        Default = -100,
        Duplicate = -3,
        Paused = -2,
        Low = -1,
        Normal = 0,
        High = 1,
        Force = 2
    }

    public enum PostProcessingOption
    {
        Default = -1,
        None = 0,
        Repair = 1,
        RepairUnpack = 2,
        RepairUnpackDelete = 3
    }
}