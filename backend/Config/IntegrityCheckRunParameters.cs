using System.Text.Json.Serialization;
using NzbWebDAV.Database.Models;

namespace NzbWebDAV.Config;

public class IntegrityCheckRunParameters
{
    [JsonPropertyName("scanDirectory")]
    public string? ScanDirectory { get; set; }

    [JsonPropertyName("maxFilesToCheck")]
    public int MaxFilesToCheck { get; set; }

    [JsonPropertyName("corruptFileAction")]
    public IntegrityCheckRun.CorruptFileActionOption CorruptFileAction { get; set; } = IntegrityCheckRun.CorruptFileActionOption.Log;

    [JsonPropertyName("mp4DeepScan")]
    public bool Mp4DeepScan { get; set; }

    [JsonPropertyName("autoMonitor")]
    public bool AutoMonitor { get; set; }

    [JsonPropertyName("unmonitorValidatedFiles")]
    public bool UnmonitorValidatedFiles { get; set; }

    [JsonPropertyName("directDeletionFallback")]
    public bool DirectDeletionFallback { get; set; }

    [JsonPropertyName("nzbSegmentSamplePercentage")]
    public int NzbSegmentSamplePercentage { get; set; }

    [JsonPropertyName("nzbSegmentThresholdPercentage")]
    public int NzbSegmentThresholdPercentage { get; set; }

    [JsonPropertyName("runType")]
    public IntegrityCheckRun.RunTypeOption RunType { get; set; } = IntegrityCheckRun.RunTypeOption.Manual;
}