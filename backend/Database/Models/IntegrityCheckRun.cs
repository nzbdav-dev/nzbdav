using System.ComponentModel.DataAnnotations;

namespace NzbWebDAV.Database.Models;

public class IntegrityCheckRun
{
    [Key]
    public string RunId { get; set; } = string.Empty;

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public RunTypeOption RunType { get; set; } = RunTypeOption.Manual;

    public string? ScanDirectory { get; set; }

    public int MaxFilesToCheck { get; set; }

    public CorruptFileActionOption CorruptFileAction { get; set; } = CorruptFileActionOption.Log;

    public bool Mp4DeepScan { get; set; }

    public bool AutoMonitor { get; set; }

    public bool UnmonitorValidatedFiles { get; set; }

    public bool DirectDeletionFallback { get; set; }

    public int NzbSegmentSamplePercentage { get; set; }

    public int NzbSegmentThresholdPercentage { get; set; }

    public int ValidFiles { get; set; }

    public int CorruptFiles { get; set; }

    public int TotalFiles { get; set; }

    public bool IsRunning { get; set; }

    public StatusOption Status { get; set; } = StatusOption.Initialized;

    public string? CurrentFile { get; set; }

    public double? ProgressPercentage { get; set; }

    public enum StatusOption
    {
        Initialized = 1,
        Started = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }

    public enum CorruptFileActionOption
    {
        Log = 1,
        Delete = 2,
        DeleteViaArr = 3
    }

    public enum RunTypeOption
    {
        Manual = 1,
        Scheduled = 2
    }
}
