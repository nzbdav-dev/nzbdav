using NzbWebDAV.Models;

namespace NzbWebDAV.Config;

public class UsenetProviderConfig
{
    public List<ConnectionDetails> Providers { get; set; } = [];

    public int TotalPooledConnections => Math.Max(1, Providers
        .Where(x => x.Type == ProviderType.Pooled)
        .Select(x => x.MaxConnections)
        .Sum());

    public class ConnectionDetails
    {
        public required ProviderType Type { get; set; }
        public required string Host { get; set; }
        public required int Port { get; set; }
        public required bool UseSsl { get; set; }
        public required string User { get; set; }
        public required string Pass { get; set; }
        public required int MaxConnections { get; set; }

        // Whether STAT existence checks may be pipelined for this provider. Not "required" so
        // that provider configs saved before this feature existed deserialize to false (i.e.
        // linear STAT) and only opt in once the user has tested + enabled pipelining.
        public bool StatPipeliningEnabled { get; set; } = false;
    }
}