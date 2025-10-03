using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NzbWebDAV.Database.Models;

public class IntegrityCheckFileResult
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string RunId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string FileId { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    public bool IsLibraryFile { get; set; }

    public DateTime LastChecked { get; set; }

    public StatusOption Status { get; set; } = StatusOption.Unknown;

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    public ActionOption? ActionTaken { get; set; }

    // Foreign key relationship
    [ForeignKey("RunId")]
    public IntegrityCheckRun? Run { get; set; }

    public enum StatusOption
    {
        Unknown = 1,
        Valid = 2,
        Corrupt = 3
    }

    public enum ActionOption
    {
        None = 1,                          // No action taken (null or log only)
        FileDeletedSuccessfully = 2,       // Direct deletion successful
        FileDeletedViaArr = 3,             // Deleted via Radarr/Sonarr
        DeleteFailedDirectFallback = 4,    // Arr failed, used direct deletion fallback
        DeleteFailedNoFallback = 5,        // Arr failed, fallback disabled
        DeleteError = 6                    // Error during deletion attempt
    }
}
