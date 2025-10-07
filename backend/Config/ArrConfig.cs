namespace NzbWebDAV.Config;

public class ArrConfig
{
    public List<ConnectionDetails> RadarrInstances { get; set; } = [];
    public List<ConnectionDetails> SonarrInstances { get; set; } = [];
    public List<QueueRule> QueueRules { get; set; } = [];

    public class ConnectionDetails
    {
        public required string Host { get; set; }
        public required string ApiKey { get; set; }
    }

    public class QueueRule
    {
        public string Message { get; set; } = null!;
        public QueueAction Action { get; set; }
    }

    public enum QueueAction
    {
        DoNothing = 0,
        Remove = 1,
        RemoveAndBlocklist = 2,
        RemoveAndBlocklistAndSearch = 3
    }
}