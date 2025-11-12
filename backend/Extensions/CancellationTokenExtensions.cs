using System.Collections.Concurrent;

namespace NzbWebDAV.Extensions;

public static class CancellationTokenExtensions
{
    private static readonly ConcurrentDictionary<LookupKey, object?> Context = new();

    public static CancellationTokenScopedContext SetScopedContext<T>(this CancellationToken ct, T? value)
    {
        var lookupKey = new LookupKey() { CancellationToken = ct, Type = typeof(T) };
        Context[lookupKey] = value;
        return new CancellationTokenScopedContext(lookupKey, value);
    }

    public static T? GetContext<T>(this CancellationToken ct)
    {
        var lookupKey = new LookupKey() { CancellationToken = ct, Type = typeof(T) };
        return Context.TryGetValue(lookupKey, out var result) && result is T context ? context : default;
    }

    public class CancellationTokenScopedContext(LookupKey lookupKey, object? value) : IDisposable
    {
        public void Dispose()
        {
            Context.Remove(lookupKey, out _);
        }
    }

    public record struct LookupKey
    {
        public CancellationToken CancellationToken;
        public Type Type;
    }
}