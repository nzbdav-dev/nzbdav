using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;

namespace NzbWebDAV.Extensions;

public static class CancellationTokenExtensions
{
    public static CancellationTokenContext SetContext<T>(this CancellationToken ct, T? value)
    {
        return CancellationTokenContext.SetContext(ct, value);
    }

    public static T? GetContext<T>(this CancellationToken ct)
    {
        return CancellationTokenContext.GetContext<T>(ct);
    }
}