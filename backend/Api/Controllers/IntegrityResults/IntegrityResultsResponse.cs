using System.Text.Json.Serialization;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Tasks;

namespace NzbWebDAV.Api.Controllers.IntegrityResults;

public class IntegrityResultsResponse
{
    [JsonPropertyName("jobRuns")]
    public List<IntegrityJobRun> JobRuns { get; set; } = new();

    [JsonPropertyName("allFiles")]
    public List<IntegrityFileResult> AllFiles { get; set; } = new();
}

public class IntegrityJobRun
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty; // Send as UTC string for consistent timezone handling

    [JsonPropertyName("runId")]
    public string? RunId { get; set; } // Execution run identifier

    [JsonPropertyName("startTime")]
    public string? StartTime { get; set; } // When the run started - UTC string format

    [JsonPropertyName("endTime")]
    public string? EndTime { get; set; } // When the run completed - UTC string format

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("corruptFiles")]
    public int CorruptFiles { get; set; }

    [JsonPropertyName("validFiles")]
    public int ValidFiles { get; set; }

    [JsonPropertyName("files")]
    public List<IntegrityFileResult> Files { get; set; } = new();

    [JsonPropertyName("parameters")]
    public IntegrityCheckRunParameters? Parameters { get; set; } // Run parameters used
}

public class IntegrityFileResult
{
    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = null!;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = null!;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = null!;

    [JsonPropertyName("isLibraryFile")]
    public bool IsLibraryFile { get; set; }

    [JsonPropertyName("lastChecked")]
    public string LastChecked { get; set; } = null!; // Send as UTC string for consistent timezone handling

    [JsonPropertyName("status")]
    public IntegrityCheckFileResult.StatusOption Status { get; set; } = IntegrityCheckFileResult.StatusOption.Unknown;

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; } // Error details for failed checks

    [JsonPropertyName("actionTaken")]
    public IntegrityCheckFileResult.ActionOption? ActionTaken { get; set; } // Action taken for corrupt files

    [JsonPropertyName("runId")]
    public string? RunId { get; set; } // Execution run identifier
}
