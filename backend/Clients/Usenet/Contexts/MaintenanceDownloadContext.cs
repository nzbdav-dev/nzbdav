namespace NzbWebDAV.Clients.Usenet.Contexts;

/// <summary>
/// Marks NNTP work belonging to background library health / maintenance.
/// Attribution only — does not change admission priority at the provider gate.
/// </summary>
public sealed class MaintenanceDownloadContext
{
    public static MaintenanceDownloadContext Instance { get; } = new();

    private MaintenanceDownloadContext()
    {
    }
}
