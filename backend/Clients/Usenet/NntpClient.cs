using System.Runtime.CompilerServices;
using NzbWebDAV.Clients.Usenet.Contexts;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Models;
using NzbWebDAV.Models.Nzb;
using NzbWebDAV.Streams;
using Serilog;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

/// <summary>
/// Abstract base class for NNTP clients with default implementations of utility methods.
/// </summary>
public abstract class NntpClient : INntpClient
{
    public virtual int PipeliningDepth => 0;

    public abstract Task ConnectAsync(
        string host, int port, bool useSsl, CancellationToken cancellationToken);

    public abstract Task<UsenetResponse> AuthenticateAsync(
        string user, string pass, CancellationToken cancellationToken);

    public abstract Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    public abstract Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds, Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken);

    public abstract Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken);

    public abstract Task<UsenetDateResponse> DateAsync(
        CancellationToken cancellationToken);

    public abstract void Dispose();

    public virtual Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        string segmentId,
        CancellationToken cancellationToken
    )
    {
        var message = $"{GetType().Name} does not support acquiring exclusive connections.";
        throw new NotSupportedException(message);
    }

    public virtual Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync
    (
        IReadOnlyList<SegmentId> segmentIds,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(segmentIds);
        if (segmentIds.Count == 0)
        {
            throw new ArgumentException("At least one segment ID is required.", nameof(segmentIds));
        }

        return AcquireExclusiveConnectionAsync(segmentIds[0], cancellationToken);
    }

    public virtual Task<UsenetDecodedBodyResponse> DecodedBodyAsync
    (
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        var message = $"{GetType().Name} does not support DecodedBodyAsync with exclusive connections.";
        throw new NotSupportedException(message);
    }

    public virtual Task<UsenetDecodedBodyBatch> DecodedBodiesAsync
    (
        IReadOnlyList<SegmentId> segmentIds,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        var message = $"{GetType().Name} does not support DecodedBodiesAsync with exclusive connections.";
        throw new NotSupportedException(message);
    }

    public virtual Task<UsenetDecodedArticleResponse> DecodedArticleAsync
    (
        SegmentId segmentId,
        UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken
    )
    {
        var message = $"{GetType().Name} does not support DecodedArticleAsync with exclusive connections.";
        throw new NotSupportedException(message);
    }

    public virtual async Task<UsenetYencHeader> GetYencHeadersAsync(string segmentId, CancellationToken ct)
    {
        var decodedBodyResponse = await DecodedBodyAsync(segmentId, ct).ConfigureAwait(false);
        await using var stream = decodedBodyResponse.Stream!;
        var headers = await stream.GetYencHeadersAsync(ct).ConfigureAwait(false);
        return headers ?? throw new NonRetryableDownloadException(
            $"Article <{segmentId}> is not yEnc-encoded; only yEnc binaries are supported.");
    }

    public virtual async Task<long> GetFileSizeAsync(NzbFile file, CancellationToken ct)
    {
        if (file.Segments.Count == 0) return 0;
        var headers = await GetYencHeadersAsync(file.Segments[^1].MessageId, ct).ConfigureAwait(false);
        file.Segments[^1].ByteRange = LongRange.FromStartAndSize(headers.PartOffset, headers.PartSize);
        return headers.PartOffset + headers.PartSize;
    }

    public virtual async Task<NzbFileStream> GetFileStream(
        NzbFile nzbFile,
        int articleBufferSize,
        CancellationToken ct,
        bool usePipelinedBodyRequests = true,
        string? fileName = null,
        InFlightArticleBudget? inFlightArticleBudget = null)
    {
        var segmentIds = nzbFile.GetSegmentIds();
        var fileSize = await GetFileSizeAsync(nzbFile, ct).ConfigureAwait(false);
        return new NzbFileStream(
            segmentIds,
            fileSize,
            this,
            articleBufferSize,
            nzbFile.GetSegmentByteRanges(),
            usePipelinedBodyRequests,
            ResolveFileName(fileName, nzbFile),
            nzbFile.GetSegmentFallbackIds(),
            inFlightArticleBudget);
    }

    public virtual NzbFileStream GetFileStream(
        NzbFile nzbFile,
        long fileSize,
        int articleBufferSize,
        bool usePipelinedBodyRequests = true,
        string? fileName = null,
        InFlightArticleBudget? inFlightArticleBudget = null)
    {
        return new NzbFileStream(
            nzbFile.GetSegmentIds(),
            fileSize,
            this,
            articleBufferSize,
            nzbFile.GetSegmentByteRanges(),
            usePipelinedBodyRequests,
            ResolveFileName(fileName, nzbFile),
            nzbFile.GetSegmentFallbackIds(),
            inFlightArticleBudget
        );
    }

    public virtual NzbFileStream GetFileStream(
        string[] segmentIds,
        long fileSize,
        int articleBufferSize,
        LongRange[]? segmentByteRanges = null,
        bool usePipelinedBodyRequests = true,
        string? fileName = null,
        string[][]? segmentFallbacks = null,
        InFlightArticleBudget? inFlightArticleBudget = null)
    {
        return new NzbFileStream(
            segmentIds,
            fileSize,
            this,
            articleBufferSize,
            segmentByteRanges,
            usePipelinedBodyRequests,
            fileName,
            segmentFallbacks,
            inFlightArticleBudget);
    }

    private static string? ResolveFileName(string? fileName, NzbFile nzbFile)
    {
        if (!string.IsNullOrEmpty(fileName)) return fileName;
        var subjectName = nzbFile.GetSubjectFileName();
        return string.IsNullOrEmpty(subjectName) ? null : subjectName;
    }

    public virtual async Task CheckAllSegmentsAsync
    (
        IEnumerable<string> segmentIds,
        int concurrency,
        IProgress<int>? progress,
        CancellationToken cancellationToken
    )
    {
        using var childCt = ContextualCancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = childCt.Token;

        var tasks = segmentIds
            .Select(async segmentId => (
                SegmentId: segmentId,
                Result: await StatAsync(segmentId, token).ConfigureAwait(false)
            ))
            .WithConcurrencyAsync(concurrency, token);

        var processed = 0;
        await foreach (var task in tasks.ConfigureAwait(false))
        {
            progress?.Report(++processed);
            if (task.Result.ResponseType == UsenetResponseType.ArticleExists) continue;
            await childCt.CancelAsync().ConfigureAwait(false);

            // Definitive missing (430 / provider 451) fails the health check; any other
            // response (e.g. a stale connection's goodbye line) must stay retryable.
            if (UsenetArticleAvailability.IsDefinitiveMissing(task.Result))
                throw new UsenetArticleNotFoundException(task.SegmentId, task.Result.ResponseMessage);
            throw new UsenetUnexpectedResponseException(task.SegmentId, task.Result.ResponseMessage);
        }
    }

    public virtual async IAsyncEnumerable<PipelinedStatResult> StatsPipelinedAsync
    (
        IReadOnlyList<string> segmentIds,
        int depth,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var segmentId in segmentIds)
        {
            var response = await StatAsync(segmentId, cancellationToken).ConfigureAwait(false);
            yield return new PipelinedStatResult
            {
                SegmentId = segmentId,
                Exists = response.ResponseType == UsenetResponseType.ArticleExists,
            };
        }
    }

    public virtual async IAsyncEnumerable<PipelinedBodyResult> DecodedBodiesPipelinedAsync
    (
        IReadOnlyList<string> segmentIds,
        int depth,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (segmentIds.Count == 0) yield break;

        depth = Math.Max(1, depth);
        for (var batchStart = 0; batchStart < segmentIds.Count; batchStart += depth)
        {
            var batchSize = Math.Min(depth, segmentIds.Count - batchStart);
            var batchIds = new SegmentId[batchSize];
            for (var index = 0; index < batchSize; index++)
                batchIds[index] = segmentIds[batchStart + index];

            var batch = await DecodedBodiesAsync(
                batchIds, onConnectionReadyAgain: null, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < batch.Responses.Count; index++)
            {
                var segmentId = segmentIds[batchStart + index];
                yield return await MapPipelinedBodyResultAsync(
                    batch.Responses[index], segmentId, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public virtual async IAsyncEnumerable<PipelinedArticleResult> DecodedArticlesPipelinedAsync
    (
        IReadOnlyList<string> segmentIds,
        int depth,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        await foreach (var body in DecodedBodiesPipelinedAsync(segmentIds, depth, cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return new PipelinedArticleResult
            {
                SegmentId = body.SegmentId,
                Found = body.Found,
                Stream = body.Stream,
                ArticleHeaders = null,
                DefinitivelyMissing = body.DefinitivelyMissing,
            };
        }
    }

    private static async Task<PipelinedBodyResult> MapPipelinedBodyResultAsync
    (
        Task<UsenetDecodedBodyResponse> responseTask,
        string segmentId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var response = await responseTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (UsenetArticleAvailability.IsDefinitiveMissing(response))
            {
                return new PipelinedBodyResult
                {
                    SegmentId = segmentId,
                    Found = false,
                    Stream = null,
                };
            }

            if (response.ResponseType != UsenetResponseType.ArticleRetrievedBodyFollows)
            {
                throw new UsenetUnexpectedResponseException(segmentId, response.ResponseMessage);
            }

            if (HasSegmentIdMismatch(segmentId, response.SegmentId, response.ResponseMessage, out var actualId))
            {
                Log.Warning(
                    "Pipelined BODY SegmentId mismatch: expected {Expected}, got {Actual} (message: {Message}). " +
                    "Treating as not found so queue rescue can refetch.",
                    NormalizeSegmentId(segmentId), actualId, response.ResponseMessage);
                if (response.Stream != null)
                {
                    try { await response.Stream.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception e) { Log.Debug(e, "Failed to dispose mismatched pipelined BODY stream"); }
                }

                return new PipelinedBodyResult
                {
                    SegmentId = segmentId,
                    Found = false,
                    Stream = null,
                };
            }

            return new PipelinedBodyResult
            {
                SegmentId = segmentId,
                Found = true,
                Stream = response.Stream,
            };
        }
        catch (UsenetArticleNotFoundException)
        {
            return new PipelinedBodyResult
            {
                SegmentId = segmentId,
                Found = false,
                Stream = null,
                DefinitivelyMissing = true,
            };
        }
    }

    /// <summary>
    /// Returns true when the response carries a different message-id than requested.
    /// Prefers <see cref="UsenetDecodedBodyResponse.SegmentId"/> when set; also checks
    /// a <c>&lt;mid&gt;</c> echoed in <c>ResponseMessage</c> (typical 222 form). Cache /
    /// synthetic responses without a wire id are treated as matching.
    /// </summary>
    internal static bool HasSegmentIdMismatch(
        string requestedSegmentId,
        string? responseSegmentId,
        string? responseMessage,
        out string actualId)
    {
        var expected = NormalizeSegmentId(requestedSegmentId);
        actualId = "";

        var responseId = NormalizeSegmentId(responseSegmentId);
        if (responseId.Length > 0 &&
            !string.Equals(responseId, expected, StringComparison.OrdinalIgnoreCase))
        {
            actualId = responseId;
            return true;
        }

        if (TryExtractMessageIdFromResponseMessage(responseMessage, out var wireId) &&
            !string.Equals(NormalizeSegmentId(wireId), expected, StringComparison.OrdinalIgnoreCase))
        {
            actualId = NormalizeSegmentId(wireId);
            return true;
        }

        return false;
    }

    internal static string NormalizeSegmentId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        // Strip the angle brackets independently: an upstream SegmentId conversion
        // may have already removed one bracket while whitespace shielded the other
        // (e.g. "<id@host>\n" arrives here as "id@host>\n").
        var trimmed = id.Trim();
        if (trimmed.Length > 0 && trimmed[0] == '<')
            trimmed = trimmed[1..];
        if (trimmed.Length > 0 && trimmed[^1] == '>')
            trimmed = trimmed[..^1];
        return trimmed.Trim();
    }

    /// <summary>
    /// Mirrors UsenetSharp's message-id shape rules so invalid ids fail as
    /// <see cref="UsenetArticleNotFoundException"/> instead of ArgumentException.
    /// </summary>
    internal static bool IsValidSegmentId(string? id)
    {
        var value = NormalizeSegmentId(id);
        if (value.Length is < 3 or > 248) return false;
        if (value[0] == '@' || value[^1] == '@' || !value.Contains('@')) return false;
        if (value.Contains('<') || value.Contains('>')) return false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Normalize and validate a segment id before handing it to UsenetSharp.
    /// Permanently-invalid ids throw <see cref="UsenetArticleNotFoundException"/>.
    /// </summary>
    internal static SegmentId PrepareSegmentId(SegmentId segmentId)
    {
        var normalized = NormalizeSegmentId(segmentId);
        if (!IsValidSegmentId(normalized))
            throw new UsenetArticleNotFoundException(normalized);

        return normalized;
    }

    internal static bool TryExtractMessageIdFromResponseMessage(string? message, out string messageId)
    {
        messageId = "";
        if (string.IsNullOrEmpty(message)) return false;
        var start = message.IndexOf('<');
        if (start < 0) return false;
        var end = message.IndexOf('>', start + 1);
        if (end < 0) return false;
        messageId = message[(start + 1)..end];
        return messageId.Length > 0;
    }

    public virtual async Task CheckAllSegmentsPipelinedAsync
    (
        IReadOnlyList<string> segmentIds,
        int depth,
        int fallbackConcurrency,
        IProgress<int>? progress,
        CancellationToken cancellationToken
    )
    {
        if (segmentIds.Count == 0) return;

        var processed = 0;
        var missing = new List<string>();
        try
        {
            await foreach (var result in StatsPipelinedAsync(segmentIds, depth, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                progress?.Report(++processed);
                if (result.Exists) continue;

                // Exists=false means a definitive miss on the primary (connection-level
                // codes throw from BaseNntpClient). Collect every miss so backups can
                // still satisfy individual segments without skipping the rest of the sample.
                missing.Add(result.SegmentId);
            }
        }
        catch (Exception e) when (!e.IsCancellationException())
        {
            // Session/protocol/transport failure during the primary-only sweep (e.g.
            // UsenetUnexpectedResponseException for a buffered 400 goodbye, or
            // UsenetSharp UsenetProtocolException when the connection dies mid-batch):
            // fall back to the concurrent path rather than failing the check.
            Log.Debug(
                e,
                "Pipelined STAT sweep failed after {Processed}/{Total} segments; falling back to concurrent STAT",
                processed,
                segmentIds.Count);
            // Use a synchronous adapter — Progress<T> posts asynchronously and can
            // reorder or race progress observers (and tests collecting reports).
            var fallbackProgress = progress == null
                ? null
                : new MappedProgress(progress, n => Math.Max(processed, n));
            await CheckAllSegmentsAsync(segmentIds, fallbackConcurrency, fallbackProgress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (missing.Count == 0) return;

        // Recheck only the misses so backups can still satisfy those articles.
        // Continue progress from the already-reported pipelined count.
        var recheckProgress = progress == null
            ? null
            : new MappedProgress(progress, n => processed + n);
        await CheckAllSegmentsAsync(missing, fallbackConcurrency, recheckProgress, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class MappedProgress(IProgress<int> target, Func<int, int> map) : IProgress<int>
    {
        public void Report(int value) => target.Report(map(value));
    }
}
