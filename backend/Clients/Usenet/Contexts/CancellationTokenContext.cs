using System.Collections.Concurrent;

namespace NzbWebDAV.Clients.Usenet.Contexts;

public class CancellationTokenContext : IDisposable
{
    private static readonly ConcurrentDictionary<LookupKey, object?> Context = new();

    private LookupKey _lookupKey;

    private CancellationTokenContext(LookupKey lookupKey)
    {
        _lookupKey = lookupKey;
    }

    public static CancellationTokenContext SetContext<T>(CancellationToken ct, T? value)
    {
        var lookupKey = new LookupKey() { CancellationToken = ct, Type = typeof(T) };
        Context[lookupKey] = value;
        return new CancellationTokenContext(lookupKey);
    }

    public static T? GetContext<T>(CancellationToken ct)
    {
        var lookupKey = new LookupKey() { CancellationToken = ct, Type = typeof(T) };
        return Context.TryGetValue(lookupKey, out var result) && result is T context ? context : default;
    }

    public void Dispose()
    {
        Context.Remove(_lookupKey, out _);
    }

    private record struct LookupKey
    {
        public CancellationToken CancellationToken;
        public Type Type;
    }
}