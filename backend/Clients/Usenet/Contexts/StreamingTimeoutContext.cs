namespace NzbWebDAV.Clients.Usenet.Contexts;

public record StreamingTimeoutContext
{
    public required TimeSpan PerAttemptTimeout { get; init; }
    public required int MaxRetries { get; init; }
}
