namespace NzbWebDAV.Clients.Usenet.Concurrency;

public record SemaphorePriorityOdds
{
    public required double HighPriorityOdds { get; set; }
    public double LowPriorityOdds => 1.0 - HighPriorityOdds;
}