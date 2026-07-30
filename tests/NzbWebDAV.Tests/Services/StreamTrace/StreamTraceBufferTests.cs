using System.Text.Json;
using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Services.StreamTrace;

namespace NzbWebDAV.Tests.Services.StreamTrace;

public class StreamTraceBufferTests
{
    [Fact]
    public void Record_PreservesPerSessionOrderingAndCapsBuffer()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();

        var rangeA = buffer.RangeOpen(sessionA, "/view/a.mkv", "GET", 0, 99, 1000, "ua", "127.0.0.1");
        buffer.Seek(sessionA, 50);
        buffer.Segment(sessionA, "provider-a", SegmentFetch.FetchStatus.Ok, 12, 0, "msgid@a");
        buffer.RangeOpen(sessionB, "/view/b.mkv", "GET", 0, null, 2000, null, null);
        buffer.ZeroFill(sessionA, "missing@a", 64);
        buffer.RangeEnd(sessionA, rangeA, ReadSession.EndReasonCode.Completed, 100);
        buffer.Failover(sessionB, "p1", "p2", "Missing");

        var eventsA = buffer.GetSessionEvents(sessionA);
        Assert.Equal(5, eventsA.Count);
        Assert.True(eventsA.Zip(eventsA.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence));
        Assert.Equal(StreamTraceKind.RangeOpen.ToString(), eventsA[0].Kind);
        Assert.Equal(StreamTraceKind.RangeEnd.ToString(), eventsA[^1].Kind);

        var sessions = buffer.ListSessions();
        Assert.Contains(sessions, s => s.SessionId == sessionA);
        Assert.Contains(sessions, s => s.SessionId == sessionB);
        Assert.Equal(100, buffer.Capacity);
    }

    [Fact]
    public void RangeEnd_IsolatesStallAttributionPerGeneration()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();

        var firstRange = buffer.RangeOpen(session, "/view/a.mkv", "GET", 0, null, 1000, null, null);
        buffer.AddStall(firstRange, StreamStallKind.ProviderWait, TimeSpan.FromMilliseconds(120));
        buffer.AddStall(firstRange, StreamStallKind.BodyDrain, TimeSpan.FromMilliseconds(30));
        buffer.AddStall(firstRange, StreamStallKind.ConsumerWait, TimeSpan.FromMilliseconds(400));
        // Sub-millisecond writes must still accumulate rather than truncate to zero.
        for (var i = 0; i < 10; i++)
            buffer.AddStall(firstRange, StreamStallKind.ClientWrite, TimeSpan.FromMicroseconds(300));
        buffer.ConnectionAcquired(firstRange, TimeSpan.FromMilliseconds(70), wasReused: true);
        buffer.ConnectionAcquired(firstRange, TimeSpan.FromMilliseconds(500), wasReused: false);
        buffer.RangeEnd(session, firstRange, ReadSession.EndReasonCode.Aborted, 4096);

        var first = buffer.GetSessionEvents(session).Last();
        Assert.Equal(120, first.ProviderWaitMs);
        Assert.Equal(30, first.BodyDrainMs);
        Assert.Equal(400, first.ConsumerWaitMs);
        Assert.Equal(3, first.ClientWriteMs);
        Assert.Equal(570, first.ConnectionWaitMs);
        Assert.Equal(1, first.ConnectionsReused);
        Assert.Equal(1, first.ConnectionsOpened);

        var secondRange = buffer.RangeOpen(session, "/view/a.mkv", "GET", 4096, null, 1000, null, null);
        buffer.AddStall(secondRange, StreamStallKind.ProviderWait, TimeSpan.FromMilliseconds(15));
        buffer.RangeEnd(session, secondRange, ReadSession.EndReasonCode.Completed, 8192);

        var second = buffer.GetSessionEvents(session).Last();
        Assert.Equal(15, second.ProviderWaitMs);
        Assert.Null(second.ConsumerWaitMs);
        Assert.Null(second.ConnectionWaitMs);
        Assert.Null(second.ConnectionsOpened);
    }

    [Fact]
    public void AddStall_WithoutRangeToken_IsIgnored()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);

        // No range has opened, so there is no generation to attribute to. This must not
        // create a session — otherwise background work would grow the session index forever.
        buffer.AddStall(null, StreamStallKind.ClientWrite, TimeSpan.FromSeconds(1));
        buffer.ConnectionAcquired(null, TimeSpan.FromSeconds(1), wasReused: false);

        Assert.Empty(buffer.ListSessions());
    }

    [Fact]
    public void LateFetchCompletion_IsNotBilledToTheNextRange()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();

        var aborted = buffer.RangeOpen(
            session, "/view/a.mkv", "GET", 0, null, 1_000_000, null, null);
        buffer.RangeEnd(session, aborted, ReadSession.EndReasonCode.Aborted, 4096);

        var next = buffer.RangeOpen(
            session, "/view/a.mkv", "GET", 4096, null, 1_000_000, null, null);
        // Prefetch from the aborted range finally resolves.
        buffer.AddFetchWait(aborted, TimeSpan.FromMilliseconds(12_580));
        buffer.AddStall(aborted, StreamStallKind.BodyDrain, TimeSpan.FromMilliseconds(900));
        buffer.AddFetchWait(next, TimeSpan.FromMilliseconds(15));
        buffer.RangeEnd(session, next, ReadSession.EndReasonCode.Completed, 8192);

        var ends = buffer.GetSessionEvents(session)
            .Where(e => e.Kind == StreamTraceKind.RangeEnd.ToString())
            .ToList();

        // The already-emitted aborted RangeEnd gains the late totals.
        Assert.Equal(12_580, ends[0].ProviderWaitMs);
        Assert.Equal(900, ends[0].BodyDrainMs);
        Assert.Equal(1, ends[0].Fetches);

        // The next range reports only work that started in that range.
        Assert.Equal(15, ends[1].ProviderWaitMs);
        Assert.Null(ends[1].BodyDrainMs);
        Assert.Equal(1, ends[1].Fetches);

        var jsonl = buffer.FormatEventsJsonl(100);
        Assert.Contains("\"providerWaitMs\":12580", jsonl);
        Assert.Contains("\"fetches\":1", jsonl);
    }

    [Fact]
    public void RangeEnd_ReportsTheFetchCountAttributedToTheRange()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();

        var range = buffer.RangeOpen(
            session, "/view/a.mkv", "GET", 0, null, 1_000_000, null, null);
        buffer.AddFetchWait(range, TimeSpan.FromMilliseconds(100));
        buffer.AddFetchWait(range, TimeSpan.FromMilliseconds(150));
        buffer.RangeEnd(session, range, ReadSession.EndReasonCode.Completed, 8192);

        var end = buffer.GetSessionEvents(session).Last();
        Assert.Equal(250, end.ProviderWaitMs);
        Assert.Equal(2, end.Fetches);
    }

    [Fact]
    public void OverlappingRangesOnTheSameSession_KeepIndependentTokens()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();

        var first = buffer.RangeOpen(session, "/view/a.mkv", "GET", 0, 99, 1000, null, null);
        var second = buffer.RangeOpen(session, "/view/a.mkv", "GET", 100, 199, 1000, null, null);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Value.Generation, second!.Value.Generation);

        buffer.AddFetchWait(first, TimeSpan.FromMilliseconds(40));
        buffer.AddFetchWait(second, TimeSpan.FromMilliseconds(90));

        // End in reverse open order.
        buffer.RangeEnd(session, second, ReadSession.EndReasonCode.Completed, 100);
        buffer.RangeEnd(session, first, ReadSession.EndReasonCode.Aborted, 50);

        var events = buffer.GetSessionEvents(session);
        var opens = events.Where(e => e.Kind == StreamTraceKind.RangeOpen.ToString()).ToList();
        var ends = events.Where(e => e.Kind == StreamTraceKind.RangeEnd.ToString()).ToList();

        Assert.Equal(first.Value.Generation, opens[0].RangeGeneration);
        Assert.Equal(second.Value.Generation, opens[1].RangeGeneration);
        Assert.Equal(second.Value.Generation, ends[0].RangeGeneration);
        Assert.Equal(90, ends[0].ProviderWaitMs);
        Assert.Equal(first.Value.Generation, ends[1].RangeGeneration);
        Assert.Equal(40, ends[1].ProviderWaitMs);

        var json = JsonSerializer.Serialize(ends[0]);
        Assert.Contains($"\"rangeGeneration\":{second.Value.Generation}", json);
    }

    [Fact]
    public void OldGenerationBeyondRetention_IsDroppedRatherThanChargedToCurrentRange()
    {
        var buffer = new StreamTraceBuffer(capacity: 200, maxSessions: 50);
        var session = Guid.NewGuid();

        var oldest = buffer.RangeOpen(session, "/view/a.mkv", "GET", 0, null, 1000, null, null);
        buffer.RangeEnd(session, oldest, ReadSession.EndReasonCode.Aborted, 1);

        for (var i = 0; i < 16; i++)
        {
            var mid = buffer.RangeOpen(session, "/view/a.mkv", "GET", i + 1, null, 1000, null, null);
            buffer.RangeEnd(session, mid, ReadSession.EndReasonCode.Completed, 1);
        }

        var current = buffer.RangeOpen(session, "/view/a.mkv", "GET", 100, null, 1000, null, null);
        buffer.AddFetchWait(oldest, TimeSpan.FromMilliseconds(9999));
        buffer.AddFetchWait(current, TimeSpan.FromMilliseconds(11));
        buffer.RangeEnd(session, current, ReadSession.EndReasonCode.Completed, 2);

        var ends = buffer.GetSessionEvents(session)
            .Where(e => e.Kind == StreamTraceKind.RangeEnd.ToString())
            .ToList();
        var currentEnd = ends.Last(e => e.RangeGeneration == current!.Value.Generation);
        Assert.Equal(11, currentEnd.ProviderWaitMs);
        Assert.Null(ends[0].ProviderWaitMs);
    }

    [Fact]
    public void RangeEnd_WithoutAnOpenRange_StillRecordsTheTerminalEvent()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();

        // A read that timed out while the stream was still opening never reached RangeOpen.
        // Dropping its RangeEnd would hide the stall this trace exists to explain.
        buffer.RangeEnd(
            session, null, ReadSession.EndReasonCode.Error, 0, "streaming-read-timeout");

        var end = Assert.Single(buffer.GetSessionEvents(session));
        Assert.Equal(StreamTraceKind.RangeEnd.ToString(), end.Kind);
        Assert.Equal("streaming-read-timeout", end.Message);
        Assert.Null(end.RangeGeneration);
        Assert.Null(end.ProviderWaitMs);
    }

    [Fact]
    public void DisabledBuffer_RecordsNothing()
    {
        var buffer = new StreamTraceBuffer(100, enabled: false);
        var session = Guid.NewGuid();

        var range = buffer.RangeOpen(session, "/view/a.mkv", "GET", 0, null, 10, null, null);
        buffer.Seek(session, 5);
        buffer.RangeEnd(session, range, ReadSession.EndReasonCode.Completed, 10);

        Assert.False(buffer.Enabled);
        Assert.Null(range);
        Assert.Empty(buffer.GetSessionEvents(session));
        Assert.Empty(buffer.ListSessions());
    }

    [Fact]
    public void ListSessions_ReturnsNewestFirst()
    {
        var buffer = new StreamTraceBuffer(100);
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();
        buffer.RangeOpen(older, "/old", "GET", 0, 1, 10, null, null);
        Thread.Sleep(5);
        buffer.RangeOpen(newer, "/new", "GET", 0, 1, 10, null, null);

        var sessions = buffer.ListSessions();
        Assert.Equal(newer, sessions[0].SessionId);
        Assert.Equal(older, sessions[1].SessionId);
    }

    [Fact]
    public void StopRecording_RetainsTheCaptureAndStopsAcceptingEvents()
    {
        var buffer = new StreamTraceBuffer(100, enabled: false);
        var session = Guid.NewGuid();
        buffer.EnableFor(TimeSpan.FromMinutes(15), 5_000, StreamTraceBuffer.SourceUi);
        buffer.RangeOpen(session, "/view/a.mkv", "GET", 0, 1, 10, null, null);

        var status = buffer.StopRecording();
        buffer.Seek(session, 5);

        Assert.False(status.Enabled);
        Assert.True(status.Retained);
        Assert.True(buffer.HasRetainedEvents);
        Assert.True(status.RetainedUntilUnixMs > 0);
        Assert.Equal(0, status.ExpiresAtUnixMs);
        Assert.Single(buffer.GetSessionEvents(session));
        Assert.Single(buffer.ListSessions());
        Assert.NotEmpty(buffer.FormatEventsJsonl(100));
    }

    [Fact]
    public void Discard_ReleasesTheRetainedCapture()
    {
        var buffer = new StreamTraceBuffer(100);
        var session = Guid.NewGuid();
        buffer.Seek(session, 5);
        buffer.StopRecording();

        var status = buffer.Discard();

        Assert.False(status.Enabled);
        Assert.False(status.Retained);
        Assert.False(buffer.HasRetainedEvents);
        Assert.Equal(0, status.RetainedUntilUnixMs);
        Assert.Equal(0, status.EventCount);
        Assert.Empty(buffer.ListSessions());
        Assert.Empty(buffer.GetSessionEvents(session));
    }

    [Fact]
    public void StopRecording_WithNoEvents_ReleasesTheEmptyRing()
    {
        var buffer = new StreamTraceBuffer(100, enabled: false);
        buffer.EnableFor(
            TimeSpan.FromMinutes(15),
            StreamTraceBuffer.DefaultUiCapacity,
            StreamTraceBuffer.SourceUi);

        var first = buffer.StopRecording();
        var second = buffer.StopRecording();

        Assert.False(first.Enabled);
        Assert.False(first.Retained);
        Assert.False(buffer.HasRetainedEvents);
        Assert.Equal(0, first.RetainedUntilUnixMs);
        Assert.Equal(0, first.EventCount);
        Assert.Equal(0, first.SessionCount);
        Assert.Equal(first, second);
        Assert.Empty(buffer.GetRecentEvents(100));
    }

    [Fact]
    public void StopRecording_IsIdempotentAndDoesNotExtendRetention()
    {
        var buffer = new StreamTraceBuffer(100);
        buffer.Seek(Guid.NewGuid(), 5);

        var first = buffer.StopRecording();
        Thread.Sleep(5);
        var second = buffer.StopRecording();

        Assert.True(first.Retained);
        Assert.True(second.Retained);
        Assert.Equal(first.RetainedUntilUnixMs, second.RetainedUntilUnixMs);
        Assert.Equal(first.EventCount, second.EventCount);
    }

    [Fact]
    public void EnableFor_ResumesRetainedCapture()
    {
        var buffer = new StreamTraceBuffer(100, enabled: false);
        var session = Guid.NewGuid();
        buffer.EnableFor(TimeSpan.FromMinutes(15), 5_000, StreamTraceBuffer.SourceUi);
        buffer.Seek(session, 5);
        var stopped = buffer.StopRecording();

        var resumed = buffer.EnableFor(
            TimeSpan.FromMinutes(30),
            StreamTraceBuffer.DefaultUiCapacity,
            StreamTraceBuffer.SourceUi);
        buffer.Seek(session, 6);

        Assert.True(stopped.Retained);
        Assert.True(resumed.Enabled);
        Assert.False(resumed.Retained);
        Assert.False(buffer.HasRetainedEvents);
        Assert.Equal(0, resumed.RetainedUntilUnixMs);
        Assert.Equal(5_000, resumed.Capacity);
        var events = buffer.GetSessionEvents(session);
        Assert.Equal(2, events.Count);
        Assert.Equal([1L, 2L], events.Select(entry => entry.Sequence));
        Assert.Equal(2, buffer.ListSessions().Single().EventCount);
        Assert.Equal(2, buffer.FormatEventsJsonl(100).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);

        buffer.Discard();
        var fresh = buffer.EnableFor(TimeSpan.FromMinutes(15), 100, StreamTraceBuffer.SourceUi);
        Assert.Equal(0, fresh.EventCount);
        Assert.Equal(0, fresh.SessionCount);
        Assert.Empty(buffer.GetSessionEvents(session));
    }

    [Fact]
    public void EnableFor_WhileAlreadyRecording_PreservesEventsAndRefreshesTtl()
    {
        var buffer = new StreamTraceBuffer(100, enabled: false);
        var session = Guid.NewGuid();
        buffer.EnableFor(TimeSpan.FromMinutes(15), 5_000, StreamTraceBuffer.SourceEnv);
        buffer.Seek(session, 5);
        var before = buffer.GetStatus().EventCount;

        var refreshed = buffer.EnableFor(
            TimeSpan.FromMinutes(60),
            StreamTraceBuffer.DefaultUiCapacity,
            StreamTraceBuffer.SourceUi);

        Assert.True(refreshed.Enabled);
        Assert.Equal(StreamTraceBuffer.SourceUi, refreshed.Source);
        Assert.Equal(before, refreshed.EventCount);
        Assert.Equal(5_000, refreshed.Capacity);
        Assert.Single(buffer.GetSessionEvents(session));
        Assert.True(refreshed.ExpiresAtUnixMs > 0);
    }

    [Fact]
    public void ExpireRetentionForTests_SetsExpiryUntilDiscard()
    {
        var buffer = new StreamTraceBuffer(100);
        buffer.Seek(Guid.NewGuid(), 5);
        buffer.StopRecording();

        buffer.ExpireRetentionForTests();

        Assert.True(buffer.IsRetentionExpired);
        Assert.True(buffer.GetStatus().Retained);

        var discarded = buffer.Discard();
        Assert.False(buffer.IsRetentionExpired);
        Assert.False(discarded.Retained);
    }

    [Fact]
    public void EnableFor_ClampsUiCapacityAndLeavesEnvCeilingHigher()
    {
        var ui = new StreamTraceBuffer(100, enabled: false);
        var uiStatus = ui.EnableFor(TimeSpan.FromMinutes(30), 999_999, StreamTraceBuffer.SourceUi);
        Assert.Equal(StreamTraceBuffer.UiMaxCapacity, uiStatus.Capacity);

        var env = new StreamTraceBuffer(100, enabled: false);
        var envStatus = env.EnableFor(TimeSpan.Zero, 150_000, StreamTraceBuffer.SourceEnv);
        Assert.Equal(150_000, envStatus.Capacity);
        Assert.Equal(0, envStatus.ExpiresAtUnixMs);
        Assert.True(env.Enabled);
        Assert.False(env.IsExpired);
    }

    [Fact]
    public void Status_ReportsOverflowAndRetainedWindowAfterTheRingWraps()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();
        for (var i = 0; i < 150; i++)
            buffer.Seek(session, i);

        var status = buffer.GetStatus();
        Assert.Equal(150, status.EventCount);
        Assert.Equal(100, status.RetainedEventCount);
        Assert.Equal(50, status.OverwrittenEventCount);
        Assert.True(status.Overflowed);
        Assert.Equal(51, status.OldestRetainedSequence);
        Assert.Equal(150, status.NewestRetainedSequence);
        Assert.True(status.OldestRetainedAtUnixMs > 0);
        Assert.True(status.NewestRetainedAtUnixMs >= status.OldestRetainedAtUnixMs);
    }

    [Fact]
    public void Status_DoesNotReportOverflowBelowCapacity()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();
        for (var i = 0; i < 50; i++)
            buffer.Seek(session, i);

        var status = buffer.GetStatus();
        Assert.Equal(50, status.EventCount);
        Assert.Equal(50, status.RetainedEventCount);
        Assert.Equal(0, status.OverwrittenEventCount);
        Assert.False(status.Overflowed);
    }

    [Fact]
    public void Status_ExactCapacityIsNotOverflow()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();
        for (var i = 0; i < 100; i++)
            buffer.Seek(session, i);

        var status = buffer.GetStatus();
        Assert.Equal(100, status.EventCount);
        Assert.Equal(100, status.RetainedEventCount);
        Assert.Equal(0, status.OverwrittenEventCount);
        Assert.False(status.Overflowed);
    }

    [Fact]
    public void CaptureSnapshot_IsConsistentWithStatusAndSessionCompleteness()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var kept = Guid.NewGuid();
        var evicted = Guid.NewGuid();
        for (var i = 0; i < 80; i++)
            buffer.Seek(evicted, i);
        for (var i = 0; i < 40; i++)
            buffer.Seek(kept, i);

        var snapshot = buffer.CaptureSnapshot();
        Assert.Equal(snapshot.Status.RetainedEventCount, snapshot.Events.Count);
        Assert.Equal(snapshot.Status.OldestRetainedSequence, snapshot.Events[0].Sequence);
        Assert.Equal(snapshot.Status.NewestRetainedSequence, snapshot.Events[^1].Sequence);
        Assert.True(snapshot.Status.Overflowed);

        var keptSummary = Assert.Single(snapshot.Sessions, s => s.SessionId == kept);
        Assert.True(keptSummary.EventsComplete);
        Assert.Equal(40, keptSummary.RetainedEventCount);

        var evictedSummary = snapshot.Sessions.FirstOrDefault(s => s.SessionId == evicted);
        if (evictedSummary is not null)
            Assert.False(evictedSummary.EventsComplete);
    }

    [Fact]
    public void FreezeForExport_IgnoresLaterStallMutations()
    {
        var buffer = new StreamTraceBuffer(capacity: 100, maxSessions: 50);
        var session = Guid.NewGuid();
        var range = buffer.RangeOpen(session, "/view/a.mkv", "GET", 0, null, 1000, null, null);
        buffer.AddStall(range, StreamStallKind.ProviderWait, TimeSpan.FromMilliseconds(50));
        buffer.RangeEnd(session, range, ReadSession.EndReasonCode.Completed, 100);

        var live = buffer.GetSessionEvents(session).Last();
        var frozen = live.FreezeForExport();
        Assert.Equal(50, frozen.ProviderWaitMs);

        buffer.AddFetchWait(range, TimeSpan.FromMilliseconds(500));
        Assert.Equal(50, frozen.ProviderWaitMs);
        Assert.True(live.ProviderWaitMs > 50);
    }

    [Fact]
    public void GetRecentEvents_CanExceedFormerUiCeilingWhenRingHoldsMore()
    {
        var buffer = new StreamTraceBuffer(capacity: 250, maxSessions: 50);
        var session = Guid.NewGuid();
        for (var i = 0; i < 250; i++)
            buffer.Seek(session, i);

        var recent = buffer.GetRecentEvents(250);
        Assert.Equal(250, recent.Count);
        Assert.Equal(1, recent[0].Sequence);
        Assert.Equal(250, recent[^1].Sequence);
    }

    [Fact]
    public void IsExpired_BecomesTrueAfterZeroTtlWindow()
    {
        var buffer = new StreamTraceBuffer(100, enabled: false);
        // A 1ms TTL is enough to expire without sleeping long in the test.
        buffer.EnableFor(TimeSpan.FromMilliseconds(1), 100, StreamTraceBuffer.SourceUi);
        Thread.Sleep(5);
        Assert.True(buffer.IsExpired);
        Assert.False(buffer.Enabled);

        buffer.StopRecording();
        Assert.False(buffer.IsExpired);
        Assert.False(buffer.Enabled);
    }

    [Fact]
    public void GetRecentEvents_ReturnsNewestWindowOldestFirst()
    {
        var buffer = new StreamTraceBuffer(100);
        var session = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
            buffer.Seek(session, i);

        var recent = buffer.GetRecentEvents(3);
        Assert.Equal(3, recent.Count);
        Assert.True(recent[0].Sequence < recent[1].Sequence);
        Assert.True(recent[1].Sequence < recent[2].Sequence);
        Assert.Equal(10, recent[^1].Sequence);
    }
}
