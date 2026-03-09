namespace NzbWebDAV.Database.Models;

public class HistoryItem
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string FileName { get; set; } = null!;
    public string JobName { get; set; } = null!;
    public string Category { get; set; }
    public DownloadStatusOption DownloadStatus { get; set; }
    public long TotalSegmentBytes { get; set; }
    public int DownloadTimeSeconds { get; set; }
    public string? FailMessage { get; set; }
    public Guid? DownloadDirId { get; set; }
    public Guid? NzbBlobId { get; set; }

    public enum DownloadStatusOption
    {
        Completed = 1,
        Failed = 2,
    }
}