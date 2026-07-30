using System.Collections.Concurrent;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Services.StreamTrace;
using NzbWebDAV.Streams;
using UsenetSharp.Models;
using UsenetSharp.Streams;

namespace NzbWebDAV.Tests.Streams;

public class MultiSegmentStreamAdaptiveWidthTests
{
    private const int BodyPipelineBatchSize = 4;

    [Fact]
    public async Task DiscriminatingBaseline_Buffer32FixedBatch4_AllowsEightOutstandingBatches()
    {
        const int articleBufferSize = 32;
        const int segmentCount = 64;
        const int segmentSize = 16;

        var client = new ControlledBatchNntpClient(segmentCount, segmentSize);
        await using var stream = CreatePipelinedStream(
            client, segmentCount, articleBufferSize, segmentSize);

        // Leave all responses gated so permits stay held while the producer fills.
        await client.WaitUntilAsync(
            () => client.MaxActiveBatches >= articleBufferSize / BodyPipelineBatchSize,
            TimeSpan.FromSeconds(5));

        Assert.Equal(8, client.MaxActiveBatches);
        Assert.Contains(4, client.ObservedBatchSizes);
        Assert.All(client.ObservedBatchSizes.Take(8), size => Assert.Equal(4, size));
    }

    [Fact]
    public async Task Starvation_EventuallyNarrowsBatchSizesToFourTwoAndOne()
    {
        const int articleBufferSize = 32;
        const int segmentCount = 96;
        const int segmentSize = 8;

        var client = new ControlledBatchNntpClient(segmentCount, segmentSize);
        await using var stream = CreatePipelinedStream(
            client, segmentCount, articleBufferSize, segmentSize);

        // Hold each gate until the consumer has sampled readiness on an incomplete task.
        var buffer = new byte[segmentSize];
        var readinessSamples = new List<bool>();
        var widthSamples = new List<int>();
        await foreach (var _ in ConsumeWithStarvationLockstepAsync(
            stream, client, buffer, segmentCount, readinessSamples, widthSamples))
        {
        }

        // Production reported starvation at every boundary: the consumer always arrived
        // before the gated segment completed.
        Assert.All(readinessSamples, ready => Assert.False(ready));

        // Width is asserted from the sizer, not from issued batch sizes: while blocked on a
        // full channel the producer can narrow 4→2→1 and resume issuing at 1, never emitting
        // a width-2 batch.
        Assert.Contains(4, client.ObservedBatchSizes);
        Assert.Contains(2, widthSamples);
        Assert.Contains(1, widthSamples);
        Assert.Equal(1, stream.PrefetchBatchWidth);
        Assert.Contains(1, client.ObservedBatchSizes);
    }

    /// <summary>
    /// Covers the widening direction end to end: once the producer runs ahead again, boundaries
    /// sample as ready and the narrowed pipeline keeps delivering every byte in order. The
    /// 1→2→4 ladder itself is asserted deterministically in
    /// <see cref="AdaptiveBodyBatchSizerTests.SixteenConsecutiveReady_RecoversOneStepAtATime"/>;
    /// reproducing 32 consecutive ready boundaries against an in-memory producer is timing
    /// dependent and cannot be asserted reliably when the suite runs in parallel.
    /// </summary>
    [Fact]
    public async Task ReadyBoundariesResume_AfterStarvationNarrowsPipeline()
    {
        const int articleBufferSize = 32;
        const int segmentCount = 200;
        const int segmentSize = 8;

        var client = new ControlledBatchNntpClient(segmentCount, segmentSize, uniqueBytes: true);
        await using var stream = CreatePipelinedStream(
            client, segmentCount, articleBufferSize, segmentSize);

        var buffer = new byte[segmentSize];
        using var actual = new MemoryStream();

        // Starve first so the pipeline narrows to 1.
        var starved = 0;
        await foreach (var n in ConsumeWithStarvationLockstepAsync(stream, client, buffer, 48))
        {
            Assert.Equal(segmentSize, n);
            actual.Write(buffer, 0, n);
            starved++;
        }

        Assert.Equal(48, starved);
        Assert.Equal(1, stream.PrefetchBatchWidth);

        // Open every remaining gate so the producer can run ahead of the consumer again.
        client.ReleaseAllUpTo(segmentCount - 1);

        var widthSamples = new List<int>();
        var readySamples = new List<bool>();
        stream.TestOnSegmentReadiness = ready => readySamples.Add(ready);
        try
        {
            while (true)
            {
                var n = await stream.ReadAsync(buffer);
                if (n == 0) break;
                actual.Write(buffer, 0, n);
                widthSamples.Add(stream.PrefetchBatchWidth);
            }
        }
        finally
        {
            stream.TestOnSegmentReadiness = null;
        }

        // Readiness is wired through from the real boundary sample, not stuck at starved.
        Assert.Contains(true, readySamples);

        // Widths only ever move inside the configured band, and the stream stays byte-exact
        // across the narrow region and any widening that occurred.
        Assert.All(widthSamples, width => Assert.InRange(width, 1, BodyPipelineBatchSize));
        Assert.All(client.ObservedBatchSizes, size => Assert.InRange(size, 1, BodyPipelineBatchSize));
        Assert.Equal(client.ExpectedConcatenation, actual.ToArray());
    }

    [Fact]
    public async Task Bounds_RespectChannelCapacityBatchWidthAndByteBudget()
    {
        const int articleBufferSize = 16;
        const int segmentCount = 48;
        const int segmentSize = 100;
        var budget = new InFlightArticleBudget(segmentSize * articleBufferSize);

        var client = new ControlledBatchNntpClient(segmentCount, segmentSize);
        await using var stream = CreatePipelinedStream(
            client, segmentCount, articleBufferSize, segmentSize, budget);

        // Fill the pipeline without consuming so occupancy peaks.
        await client.WaitUntilAsync(
            () => client.MaxActiveBatches >= 1 && client.StartedSegmentCount >= articleBufferSize,
            TimeSpan.FromSeconds(5));

        Assert.True(
            client.MaxStartedMinusReleased <= articleBufferSize + BodyPipelineBatchSize,
            $"Started work {client.MaxStartedMinusReleased} exceeded " +
            $"{articleBufferSize}+{BodyPipelineBatchSize}");
        Assert.True(
            client.MaxUnpublishedInBatch <= BodyPipelineBatchSize,
            $"Unpublished batch tasks peaked at {client.MaxUnpublishedInBatch}");
        Assert.True(budget.LeasedBytes <= budget.CapBytes);

        // Drain while releasing so disposal/lease accounting stays clean.
        client.ReleaseAllUpTo(segmentCount - 1);
        var buffer = new byte[segmentSize];
        while (await stream.ReadAsync(buffer) > 0)
        {
        }

        Assert.True(budget.LeasedBytes <= budget.CapBytes);
        Assert.Equal(0, client.ActiveBatches);
    }

    [Fact]
    public async Task FifoFidelity_SurvivesWidthTransitionsInBothDirections()
    {
        const int articleBufferSize = 32;
        const int segmentCount = 120;
        const int segmentSize = 4;

        var client = new ControlledBatchNntpClient(segmentCount, segmentSize, uniqueBytes: true);
        var expected = client.ExpectedConcatenation;
        await using var stream = CreatePipelinedStream(
            client, segmentCount, articleBufferSize, segmentSize);

        var buffer = new byte[segmentSize];
        var actual = new MemoryStream();

        // Starve to force 4→2→1, then release ahead to recover toward 4.
        await foreach (var n in ConsumeWithStarvationLockstepAsync(stream, client, buffer, 40))
            actual.Write(buffer, 0, n);

        client.ReleaseAllUpTo(segmentCount - 1);
        while (true)
        {
            var n = await stream.ReadAsync(buffer);
            if (n == 0) break;
            actual.Write(buffer, 0, n);
        }

        Assert.Contains(1, client.ObservedBatchSizes);
        Assert.Equal(expected, actual.ToArray());
    }

    [Fact]
    public async Task Disposal_ReleasesEveryCallbackPermitAndLeaseExactlyOnce()
    {
        const int articleBufferSize = 32;
        const int segmentCount = 48;
        const int segmentSize = 16;
        var budget = new InFlightArticleBudget(segmentSize * 64);

        var client = new ControlledBatchNntpClient(segmentCount, segmentSize);
        var stream = CreatePipelinedStream(
            client, segmentCount, articleBufferSize, segmentSize, budget);

        await client.WaitUntilAsync(
            () => client.MaxActiveBatches >= 4, TimeSpan.FromSeconds(5));

        await stream.DisposeAsync();

        await client.WaitUntilAsync(() => client.ActiveBatches == 0, TimeSpan.FromSeconds(5));
        Assert.Equal(client.BatchIssueCount, client.CallbackCount);
        Assert.Equal(0, budget.LeasedBytes);
        Assert.Equal(0, client.ActiveBodyStreams);
    }

    [Fact]
    public async Task NonPipelinedMode_DoesNotEmitPrefetchWidthOrChangeBatchSizing()
    {
        const int articleBufferSize = 16;
        const int segmentCount = 24;
        const int segmentSize = 8;

        var previous = StreamTrace.Buffer;
        var traceBuffer = new StreamTraceBuffer(capacity: 1_000, maxSessions: 16);
        StreamTrace.Configure(traceBuffer);
        try
        {
            var sessionId = Guid.NewGuid();
            using var scope = MultiProviderNntpClient.BeginReadSessionScope(sessionId);
            traceBuffer.RangeOpen(
                sessionId, "/view/t.bin", "GET", 0, null, segmentCount * segmentSize, null, null);

            var client = new ControlledBatchNntpClient(segmentCount, segmentSize);
            // Individual mode completes bodies immediately (no batch gating).
            client.ReleaseAllUpTo(segmentCount - 1);

            await using var stream = MultiSegmentStream.Create(
                client.SegmentIds.AsMemory(),
                client,
                articleBufferSize,
                estimatedSegmentSize: segmentSize,
                failFastOnFirstSegment: false,
                usePipelinedBodyRequests: false,
                CancellationToken.None,
                fileName: "non-pipe.bin");

            var readBuf = new byte[segmentSize];
            for (var i = 0; i < segmentCount; i++)
            {
                var n = await stream.ReadAsync(readBuf);
                Assert.Equal(segmentSize, n);
            }

            Assert.Equal(0, client.BatchIssueCount);
            Assert.DoesNotContain(
                traceBuffer.GetSessionEvents(sessionId),
                e => e.Kind == StreamTraceKind.PrefetchWidth.ToString());
        }
        finally
        {
            if (previous is not null)
                StreamTrace.Configure(previous);
            else
                StreamTrace.Configure(new StreamTraceBuffer(capacity: 1, maxSessions: 10, enabled: false));
        }
    }

    private static MultiSegmentStream CreatePipelinedStream(
        ControlledBatchNntpClient client,
        int segmentCount,
        int articleBufferSize,
        int segmentSize,
        InFlightArticleBudget? budget = null)
    {
        var exactSizes = Enumerable.Repeat((long)segmentSize, segmentCount).ToArray();
        return (MultiSegmentStream)MultiSegmentStream.Create(
            client.SegmentIds.AsMemory(),
            client,
            articleBufferSize,
            estimatedSegmentSize: 0, // disable byte ceiling so channel depth is the limit
            failFastOnFirstSegment: false,
            usePipelinedBodyRequests: true,
            CancellationToken.None,
            fileName: "adaptive.bin",
            exactSegmentSizes: exactSizes,
            inFlightArticleBudget: budget);
    }

    /// <summary>
    /// Reads <paramref name="count"/> segments while holding each BODY gate closed until
    /// the consumer has sampled readiness on that incomplete task (avoids the race where
    /// releasing as soon as a batch starts makes every boundary look "ready").
    /// Records the readiness values production actually sampled, plus the batch width after
    /// each boundary, so assertions can follow the sizer without depending on when the
    /// producer happens to issue its next batch.
    /// </summary>
    private static async IAsyncEnumerable<int> ConsumeWithStarvationLockstepAsync(
        MultiSegmentStream stream,
        ControlledBatchNntpClient client,
        byte[] buffer,
        int count,
        ICollection<bool>? readinessSamples = null,
        ICollection<int>? widthSamples = null)
    {
        try
        {
            for (var i = 0; i < count; i++)
            {
                var readiness = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                // Instance-scoped: other MultiSegmentStream tests running in parallel cannot
                // complete this readiness TCS.
                stream.TestOnSegmentReadiness = ready =>
                {
                    readinessSamples?.Add(ready);
                    readiness.TrySetResult();
                };
                var readTask = stream.ReadAsync(buffer).AsTask();
                await readiness.Task.WaitAsync(TimeSpan.FromSeconds(5));
                client.ReleaseSegment(i);
                var n = await readTask;
                // The boundary observation has been applied by the time ReadAsync returns.
                widthSamples?.Add(stream.PrefetchBatchWidth);
                yield return n;
            }
        }
        finally
        {
            stream.TestOnSegmentReadiness = null;
        }
    }
}

/// <summary>
/// NNTP fake that gates per-segment BODY completion and holds each exclusive batch
/// callback until every body stream in the batch is disposed. Used only by adaptive
/// width tests — do not fold timing into <see cref="Fakes.FakeNntpClient"/>.
/// </summary>
internal sealed class ControlledBatchNntpClient : NntpClient
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _gates = new();
    private readonly ConcurrentDictionary<int, byte[]> _payloads = new();
    private readonly object _statsGate = new();
    private int _activeBatches;
    private int _maxActiveBatches;
    private int _callbackCount;
    private int _batchIssueCount;
    private int _startedSegments;
    private int _completedResponses;
    private int _releasedThrough = -1;
    private int _activeBodyStreams;
    private int _maxStartedMinusReleased;
    private int _maxUnpublishedInBatch;

    public ControlledBatchNntpClient(int segmentCount, int segmentSize, bool uniqueBytes = false)
    {
        SegmentIds = Enumerable.Range(0, segmentCount).Select(i => $"seg-{i}").ToArray();
        for (var i = 0; i < segmentCount; i++)
        {
            var bytes = new byte[segmentSize];
            if (uniqueBytes)
            {
                for (var b = 0; b < segmentSize; b++)
                    bytes[b] = (byte)((i * 17 + b * 3) % 256);
            }
            else
            {
                Array.Fill(bytes, (byte)(i % 256));
            }

            _payloads[i] = bytes;
            _gates[i] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        ExpectedConcatenation = _payloads.OrderBy(p => p.Key).SelectMany(p => p.Value).ToArray();
    }

    public string[] SegmentIds { get; }
    public byte[] ExpectedConcatenation { get; }
    public List<int> ObservedBatchSizes { get; } = [];
    public int ActiveBatches
    {
        get { lock (_statsGate) return _activeBatches; }
    }
    public int MaxActiveBatches
    {
        get { lock (_statsGate) return _maxActiveBatches; }
    }
    public int CallbackCount
    {
        get { lock (_statsGate) return _callbackCount; }
    }
    public int BatchIssueCount
    {
        get { lock (_statsGate) return _batchIssueCount; }
    }
    public int StartedSegmentCount
    {
        get { lock (_statsGate) return _startedSegments; }
    }
    public int CompletedResponseCount
    {
        get { lock (_statsGate) return _completedResponses; }
    }
    public int ActiveBodyStreams
    {
        get { lock (_statsGate) return _activeBodyStreams; }
    }
    public int MaxStartedMinusReleased
    {
        get { lock (_statsGate) return _maxStartedMinusReleased; }
    }
    public int MaxUnpublishedInBatch
    {
        get { lock (_statsGate) return _maxUnpublishedInBatch; }
    }

    public void ReleaseSegment(int index)
    {
        if (!_gates.TryGetValue(index, out var gate)) return;
        if (!gate.TrySetResult()) return;
        lock (_statsGate)
        {
            _completedResponses++;
            _releasedThrough = Math.Max(_releasedThrough, index);
            UpdateStartedMinusReleasedUnlocked();
        }
    }

    public void ReleaseAllUpTo(int inclusiveIndex)
    {
        for (var i = 0; i <= inclusiveIndex; i++)
            ReleaseSegment(i);
    }

    public async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.Fail($"Condition not met within {timeout.TotalSeconds:0.#}s");
    }

    public override Task ConnectAsync(
        string host, int port, bool useSsl, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public override Task<UsenetResponse> AuthenticateAsync(
        string user, string pass, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        DecodedBodyAsync(segmentId, null, cancellationToken);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = IndexOf(segmentId);
        var payload = _payloads[index];
        // Individual / rescue path completes immediately.
        var response = CreateResponse(segmentId.ToString(), payload, () =>
            onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved));
        return Task.FromResult(response);
    }

    public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var batchSize = segmentIds.Count;
        var remaining = batchSize;
        void OnBodyDisposed()
        {
            if (Interlocked.Decrement(ref remaining) == 0)
            {
                onConnectionReadyAgain?.Invoke(ArticleBodyResult.Retrieved);
                lock (_statsGate)
                {
                    _callbackCount++;
                    _activeBatches--;
                }
            }
        }

        lock (_statsGate)
        {
            _batchIssueCount++;
            ObservedBatchSizes.Add(batchSize);
            _activeBatches++;
            _maxActiveBatches = Math.Max(_maxActiveBatches, _activeBatches);
            _startedSegments += batchSize;
            // Producer materializes the whole batch before the first channel write.
            _maxUnpublishedInBatch = Math.Max(_maxUnpublishedInBatch, batchSize);
            UpdateStartedMinusReleasedUnlocked();
        }

        var responses = new Task<UsenetDecodedBodyResponse>[batchSize];
        for (var i = 0; i < batchSize; i++)
        {
            var index = IndexOf(segmentIds[i]);
            var key = segmentIds[i].ToString();
            var payload = _payloads[index];
            var gate = _gates[index];
            responses[i] = AwaitGateAsync(gate, key, payload, OnBodyDisposed, cancellationToken);
        }

        // Return immediately so the producer can issue further batches while bodies are gated.
        return Task.FromResult(new UsenetDecodedBodyBatch { Responses = responses });
    }

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        string segmentId, CancellationToken cancellationToken) =>
        Task.FromResult(new UsenetExclusiveConnection(null));

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        IReadOnlyList<SegmentId> segmentIds, CancellationToken cancellationToken) =>
        Task.FromResult(new UsenetExclusiveConnection(null));

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken) =>
        DecodedBodyAsync(segmentId, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);

    public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken) =>
        DecodedBodiesAsync(
            segmentIds, exclusiveConnection.OnConnectionReadyAgain, cancellationToken);

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public override void Dispose()
    {
    }

    private async Task<UsenetDecodedBodyResponse> AwaitGateAsync(
        TaskCompletionSource gate,
        string segmentId,
        byte[] payload,
        Action onDisposed,
        CancellationToken cancellationToken)
    {
        try
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Cancellation / fault before the body materializes must still release the batch permit.
            onDisposed();
            throw;
        }

        return CreateResponse(segmentId, payload, onDisposed);
    }

    private void UpdateStartedMinusReleasedUnlocked()
    {
        var released = _releasedThrough + 1;
        _maxStartedMinusReleased = Math.Max(_maxStartedMinusReleased, _startedSegments - released);
    }

    private int IndexOf(SegmentId segmentId)
    {
        var key = segmentId.ToString();
        for (var i = 0; i < SegmentIds.Length; i++)
        {
            if (SegmentIds[i] == key) return i;
        }

        throw new UsenetArticleNotFoundException(key, "430 No such article");
    }

    private UsenetDecodedBodyResponse CreateResponse(
        string segmentId, byte[] bytes, Action? onDispose)
    {
        Interlocked.Increment(ref _activeBodyStreams);
        var headers = new UsenetYencHeader
        {
            FileName = "adaptive.bin",
            FileSize = bytes.Length,
            LineLength = 128,
            PartNumber = 1,
            TotalParts = 1,
            PartOffset = 0,
            PartSize = bytes.Length,
        };
        Stream inner = new MemoryStream(bytes, writable: false);
        inner = new DisposableCallbackStream(inner, () =>
        {
            Interlocked.Decrement(ref _activeBodyStreams);
            onDispose?.Invoke();
        });

        return new UsenetDecodedBodyResponse
        {
            SegmentId = segmentId,
            ResponseCode = (int)UsenetResponseType.ArticleRetrievedBodyFollows,
            ResponseMessage = "222 controlled body",
            Stream = new CachedYencStream(headers, inner),
        };
    }
}
