namespace NzbWebDAV.Clients.Usenet.Concurrency;

public record SemaphorePriorityOdds
{
    public required int HighPriorityOdds { get; set; }
    public int LowPriorityOdds => 100 - HighPriorityOdds;
}