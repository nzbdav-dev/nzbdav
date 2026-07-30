using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models.Nzb;
using Serilog;
using UsenetSharp.Models;

namespace NzbWebDAV.Services;

public class PlaybackFastVerifier
{
    private static readonly TimeSpan DefaultSegmentTimeout = TimeSpan.FromSeconds(15);

    private readonly UsenetStreamingClient _usenetClient;

    public PlaybackFastVerifier(UsenetStreamingClient usenetClient)
    {
        _usenetClient = usenetClient;
    }

    /// <summary>
    /// Probes segment availability for a candidate NZB. <paramref name="priority"/> decides how
    /// these probes compete for provider connections: user-initiated playback selection passes
    /// High, while background verification (Watchtower, keep-fresh) stays Low so it yields to
    /// playback at a saturated provider.
    /// </summary>
    public async Task<VerifyOutcome> VerifyAsync(
        Stream nzbStream, string mode, int sampleCount, CancellationToken ct, TimeSpan? segmentTimeout = null,
        SemaphorePriority priority = SemaphorePriority.Low)
    {
        if (mode == "none") return new VerifyOutcome(Verdict.Available, null);

        NzbDocument nzb;
        try
        {
            nzb = await NzbDocument.LoadAsync(nzbStream).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Debug("Fast-verify: NZB parse failed: {Message}", e.Message);
            return new VerifyOutcome(Verdict.Dead, null);
        }

        var samples = PickSampleSegments(nzb, Math.Max(1, sampleCount));
        if (samples.Count == 0) return new VerifyOutcome(Verdict.Dead, null);

        var attribution = new MultiProviderNntpClient.ResponderAttribution();
        MultiProviderNntpClient.AttributionContext.Value = attribution;

        var timeout = segmentTimeout ?? DefaultSegmentTimeout;
        var tasks = samples.Select(s => CheckSegmentAsync(s, mode, timeout, priority, ct)).ToList();
        Verdict[] results;
        try
        {
            results = await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new VerifyOutcome(Verdict.Timeout, attribution.Host);
        }

        if (results.Any(r => r == Verdict.Dead))
            return new VerifyOutcome(Verdict.Dead, attribution.Host);
        if (results.All(r => r == Verdict.Timeout))
            return new VerifyOutcome(Verdict.Timeout, attribution.Host);
        return new VerifyOutcome(Verdict.Available, attribution.Host);
    }

    private async Task<Verdict> CheckSegmentAsync(
        string messageId, string mode, TimeSpan timeout, SemaphorePriority priority, CancellationToken ct)
    {
        // The priority context lives on this candidate's own child token: registering it on
        // the parent token shared by concurrent candidates would let one candidate's disposal
        // strip priority from its siblings.
        var timeoutCts = ContextualCancellationTokenSource.CreateLinkedTokenSource(ct);
        var priorityScope = timeoutCts.Token.SetContext(new DownloadPriorityContext
        {
            Priority = priority,
        });
        timeoutCts.CancelAfter(timeout);
        var work = CheckSegmentCoreAsync(messageId, mode, timeoutCts.Token);
        try
        {
            var winner = await Task.WhenAny(work, Task.Delay(timeout, ct)).ConfigureAwait(false);
            if (winner != work)
            {
                ct.ThrowIfCancellationRequested();
                Log.Debug("Fast-verify timed out after {Timeout:n0}s on {Segment}", timeout.TotalSeconds, messageId);
                return Verdict.Timeout;
            }
            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Fast-verify timed out after {Timeout:n0}s on {Segment}", timeout.TotalSeconds, messageId);
            return Verdict.Timeout;
        }
        catch (Exception e) when (!e.IsCancellationException())
        {
            Log.Debug("Fast-verify errored on {Segment}: {Message}", messageId, e.Message);
            return Verdict.Timeout;
        }
        finally
        {
            // A timeout returns while `work` may still hold the token, so priority and the
            // token source are released only once the probe itself finishes.
            _ = work.ContinueWith(static (t, s) =>
            {
                _ = t.Exception;
                var (scope, source) = ((CancellationTokenContext, ContextualCancellationTokenSource))s!;
                scope.Dispose();
                source.Dispose();
            }, (priorityScope, timeoutCts), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task<Verdict> CheckSegmentCoreAsync(string messageId, string mode, CancellationToken ct)
    {
        if (mode == "body")
        {
            var resp = await _usenetClient.DecodedBodyAsync(messageId, ct).ConfigureAwait(false);
            return resp.ResponseType == UsenetResponseType.ArticleRetrievedBodyFollows
                ? Verdict.Available
                : Verdict.Dead;
        }

        var stat = await _usenetClient.StatAsync(messageId, ct).ConfigureAwait(false);
        return stat.ResponseType == UsenetResponseType.ArticleExists
            ? Verdict.Available
            : Verdict.Dead;
    }

    public readonly record struct VerifyOutcome(Verdict Verdict, string? ResponderHost);

    private static List<string> PickSampleSegments(NzbDocument nzb, int sampleCount)
    {
        var dataFile = nzb.Files
            .Where(f => f.Segments.Count > 0 && !IsPar2(f))
            .OrderByDescending(f => f.GetTotalYencodedSize())
            .FirstOrDefault();
        var anyFile = dataFile ?? nzb.Files
            .Where(f => f.Segments.Count > 0)
            .OrderByDescending(f => f.GetTotalYencodedSize())
            .FirstOrDefault();
        if (anyFile is null) return new List<string>();

        var segs = anyFile.Segments;
        var n = Math.Min(sampleCount, segs.Count);
        if (n <= 1) return new List<string> { segs[0].MessageId };

        var indices = new SortedSet<int>();
        for (var i = 0; i < n; i++)
        {
            var idx = (int)Math.Round(i * (segs.Count - 1) / (double)(n - 1));
            indices.Add(idx);
        }
        return indices.Select(i => segs[i].MessageId).ToList();
    }

    private static bool IsPar2(NzbFile file)
    {
        var name = file.GetSubjectFileName();
        if (!string.IsNullOrEmpty(name) && name.EndsWith(".par2", StringComparison.OrdinalIgnoreCase))
            return true;
        return file.Subject.Contains(".par2", StringComparison.OrdinalIgnoreCase);
    }

    public enum Verdict
    {
        Available,
        Dead,
        Timeout,
    }
}
