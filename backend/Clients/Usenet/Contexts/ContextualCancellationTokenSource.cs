using NzbWebDAV.Extensions;

namespace NzbWebDAV.Clients.Usenet.Contexts;

public class ContextualCancellationTokenSource : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly List<CancellationTokenContext> _contexts;
    private bool _disposed;

    public CancellationToken Token => _cts.Token;

    private ContextualCancellationTokenSource(CancellationTokenSource cts)
    {
        _cts = cts;
        _contexts = [];
    }

    public static ContextualCancellationTokenSource CreateLinkedTokenSource(CancellationToken linkedToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
        var contextualCts = new ContextualCancellationTokenSource(cts);
        contextualCts.SetContext(linkedToken.GetContext<DownloadPriorityContext>());
        return contextualCts;
    }

    public static ContextualCancellationTokenSource CreateLinkedTokenSource
    (
        CancellationToken linkedToken1,
        CancellationToken linkedToken2
    )
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken1, linkedToken2);
        var contextualCts = new ContextualCancellationTokenSource(cts);
        contextualCts.SetContext(linkedToken1.GetContext<DownloadPriorityContext>());
        contextualCts.SetContext(linkedToken2.GetContext<DownloadPriorityContext>());
        return contextualCts;
    }

    private void SetContext<T>(T? value)
    {
        if (value == null) return;
        ObjectDisposedException.ThrowIf(_disposed, nameof(ContextualCancellationTokenSource));
        _contexts.Add(CancellationTokenContext.SetContext(_cts.Token, value));
    }

    public void Cancel()
    {
        _cts.Cancel();
    }

    public Task CancelAsync()
    {
        return _cts.CancelAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true)) return;
        foreach (var context in _contexts) context.Dispose();
        _contexts.Clear();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}