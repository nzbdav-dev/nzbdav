namespace NzbWebDAV.Api.Controllers.GetServerHealth;

public class GetServerHealthResponse : BaseApiResponse
{
    public List<ServerHealthInfo> Servers { get; set; } = new();
}

public class ServerHealthInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public int Priority { get; set; }
    public int MaxConnections { get; set; }
    public bool IsAvailable { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int TotalSuccesses { get; set; }
    public int TotalFailures { get; set; }
    public DateTime? LastSuccessTime { get; set; }
    public DateTime? LastFailureTime { get; set; }
    public string? LastException { get; set; }
}
