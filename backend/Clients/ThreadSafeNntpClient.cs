﻿using NzbWebDAV.Exceptions;
using NzbWebDAV.Extensions;
using NzbWebDAV.Streams;
using Usenet.Nntp;
using Usenet.Nntp.Models;
using Usenet.Nntp.Responses;
using Usenet.Nzb;
using Usenet.Yenc;

namespace NzbWebDAV.Clients;

public class ThreadSafeNntpClient : INntpClient
{
    private readonly NntpConnection _connection;
    private readonly NntpClient _client;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public ThreadSafeNntpClient()
    {
        _connection = new NntpConnection();
        _client = new NntpClient(_connection);
    }

    public Task<bool> ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken)
    {
        return Synchronized(() => _client.ConnectAsync(host, port, useSsl), cancellationToken);
    }

    public Task<bool> AuthenticateAsync(string user, string pass, CancellationToken cancellationToken)
    {
        return Synchronized(() => _client.Authenticate(user, pass), cancellationToken);
    }

    public Task<NntpStatResponse> StatAsync(string segmentId, CancellationToken cancellationToken)
    {
        return Synchronized(() => _client.Stat(new NntpMessageId(segmentId)), cancellationToken);
    }

    public Task<NntpDateResponse> DateAsync(CancellationToken cancellationToken)
    {
        return Synchronized(() => _client.Date(), cancellationToken);
    }

    public async Task<YencHeaderStream> GetSegmentStreamAsync(string segmentId, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var articleBody = GetArticleBody(segmentId);
                var stream = YencStreamDecoder.Decode(articleBody);
                return new YencHeaderStream(
                    stream.Header,
                    new BufferToEndStream(stream.OnDispose(OnDispose))
                );

                // we only want to release the semaphore once the stream is disposed.
                void OnDispose() => _semaphore.Release();
            }
            catch (Exception)
            {
                // or if there is an error getting the stream itself.
                _semaphore.Release();
                throw;
            }
        });
    }

    public async Task<YencHeader> GetSegmentYencHeaderAsync(string segmentId, CancellationToken cancellationToken)
    {
        await using var stream = await GetSegmentStreamAsync(new NntpMessageId(segmentId), cancellationToken);
        return stream.Header;
    }

    public async Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken)
    {
        if (file.Segments.Count == 0) return 0;
        var header = await GetSegmentYencHeaderAsync(file.Segments[^1].MessageId.Value, cancellationToken);
        return header.PartOffset + header.PartSize;
    }

    public async Task WaitForReady(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        _semaphore.Release();
    }

    private Task<T> Synchronized<T>(Func<T> run, CancellationToken cancellationToken)
    {
        return Synchronized(() => Task.Run(run, cancellationToken), cancellationToken);
    }

    private async Task<T> Synchronized<T>(Func<Task<T>> run, CancellationToken cancellationToken)
    {
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            return await run();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private IEnumerable<string> GetArticleBody(string segmentId)
    {
        return _client.Body(new NntpMessageId(segmentId))?.Article?.Body
               ?? throw new UsenetArticleNotFoundException($"Article with message-id {segmentId} not found.");
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}