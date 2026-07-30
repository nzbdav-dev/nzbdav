using Microsoft.EntityFrameworkCore;
using NzbWebDAV.UsenetMigration;
using NzbWebDAV.UsenetMigration.Model;

namespace NzbWebDAV.Tests.UsenetMigration;

public sealed class MigrationSessionStateMachineTests
{
    private static readonly IReadOnlyDictionary<MigrationSessionTransition, ExpectedRule> ExpectedRules =
        new Dictionary<MigrationSessionTransition, ExpectedRule>
        {
            [MigrationSessionTransition.Connect] = new("connected",
                "idle", "connected", "mapped", "scanned", "complete", "cancelled", "linked"),
            [MigrationSessionTransition.MapCategories] = new("mapped",
                "connected", "mapped", "scanned"),
            [MigrationSessionTransition.StartScan] = new("scanning",
                "connected", "mapped", "scanned", "complete", "cancelled", "linked"),
            [MigrationSessionTransition.CancelScan] = new("scan_cancelling", "scanning"),
            [MigrationSessionTransition.CompleteScanCancellation] = new("mapped", "scan_cancelling"),
            [MigrationSessionTransition.CompleteScan] = new("scanned", "scanning"),
            [MigrationSessionTransition.StartRun] = new("running", "scanned"),
            [MigrationSessionTransition.CompleteEmptyRun] = new("complete", "scanned"),
            [MigrationSessionTransition.PauseRun] = new("paused", "running"),
            [MigrationSessionTransition.ResumeRun] = new("running", "paused"),
            [MigrationSessionTransition.BeginCancellation] = new("cancelling", "running", "paused"),
            [MigrationSessionTransition.CompleteCancellation] = new("cancelled", "cancelling"),
            [MigrationSessionTransition.CompleteRun] = new("complete", "running"),
            [MigrationSessionTransition.ReturnRunToReview] = new("scanned", "running"),
            [MigrationSessionTransition.StartLinkPlan] = new("linking", "complete", "linked"),
            [MigrationSessionTransition.CompleteLinkPlan] = new("linked", "linking"),
            [MigrationSessionTransition.StartApply] = new("applying", "linked"),
            [MigrationSessionTransition.CompleteApply] = new("linked", "applying"),
            [MigrationSessionTransition.StartRestore] = new("restoring", "linked"),
            [MigrationSessionTransition.CompleteRestore] = new("linked", "restoring"),
        };

    public static IEnumerable<object[]> TransitionMatrix()
    {
        foreach (var (transition, expected) in ExpectedRules)
            foreach (var status in MigrationSessionStatus.All)
                yield return [(int)transition, status, expected.SourceStatuses.Contains(status)];
    }

    [Theory]
    [MemberData(nameof(TransitionMatrix))]
    public void Contract_AllowsOnlyDeclaredSourceStates(
        int transitionValue,
        string status,
        bool expected)
    {
        var transition = (MigrationSessionTransition)transitionValue;
        Assert.Equal(expected, MigrationSessionStateMachine.CanTransition(transition, status));
    }

    [Fact]
    public void Contract_DeclaresEveryTransitionTarget()
    {
        Assert.Equal(Enum.GetValues<MigrationSessionTransition>().Length, ExpectedRules.Count);
        foreach (var (transition, expected) in ExpectedRules)
            Assert.Equal(expected.TargetStatus,
                MigrationSessionStateMachine.GetRule(transition).TargetStatus);
    }

    [Theory]
    [InlineData("scanning", true)]
    [InlineData("scan_cancelling", true)]
    [InlineData("running", true)]
    [InlineData("paused", true)]
    [InlineData("cancelling", true)]
    [InlineData("linking", true)]
    [InlineData("applying", true)]
    [InlineData("restoring", true)]
    [InlineData("idle", false)]
    [InlineData("connected", false)]
    [InlineData("mapped", false)]
    [InlineData("scanned", false)]
    [InlineData("complete", false)]
    [InlineData("cancelled", false)]
    [InlineData("linked", false)]
    public void ActiveStateContract_IsExplicit(string status, bool expected) =>
        Assert.Equal(expected, MigrationSessionStateMachine.IsWorkActive(status));

    [Fact]
    public async Task TryTransitionSessionAsync_CompetingClaimsHaveOneWinner()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s => s.Status = "scanned");

        var results = await Task.WhenAll(
            h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartScan),
            h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartRun));

        Assert.Single(results, r => r.Outcome == MigrationSessionTransitionOutcome.Applied);
        Assert.Single(results, r => r.Outcome == MigrationSessionTransitionOutcome.Rejected);
        var session = await h.Store.GetSessionAsync();
        Assert.Contains(session.Status, new[] { "scanning", "running" });
    }

    [Fact]
    public async Task TryTransitionSessionAsync_RepeatedRequestIsIdempotent()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s => s.Status = "scanned");

        var first = await h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartRun);
        var repeated = await h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartRun);

        Assert.Equal(MigrationSessionTransitionOutcome.Applied, first.Outcome);
        Assert.Equal(MigrationSessionTransitionOutcome.AlreadyApplied, repeated.Outcome);
        Assert.True(repeated.Succeeded);
        Assert.Equal("running", repeated.CurrentStatus);
    }

    [Fact]
    public async Task ApplyConnectionAsync_RejectsActiveOperationWithoutMutatingConnectionData()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "applying";
            s.AltmountMetadataRoot = "old-root";
        });

        var result = await h.Store.ApplyConnectionAsync(
            new MigrationConnectionValues("new-root", "new-config", "new-store", 99, 4),
            [new AltmountCategory { Name = "tv", Dir = "tv", Type = "sonarr" }]);

        Assert.Equal(MigrationSessionTransitionOutcome.Rejected, result.Outcome);
        await using var check = h.Mig();
        var session = await check.SessionState.SingleAsync();
        Assert.Equal("applying", session.Status);
        Assert.Equal("old-root", session.AltmountMetadataRoot);
        Assert.Empty(await check.Preferences.ToListAsync());
        Assert.Empty(await check.CategoryMap.ToListAsync());
    }

    [Fact]
    public async Task ConnectAndScanRace_NeverStartsScanWithPartiallyAppliedRoots()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "connected";
            s.AltmountMetadataRoot = "old-root";
        });

        var results = await Task.WhenAll(
            h.Store.ApplyConnectionAsync(
                new MigrationConnectionValues("new-root", null, null, null, null),
                []),
            h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartScan));

        var connect = results[0];
        var scan = results[1];
        Assert.True(scan.Succeeded);

        await using var check = h.Mig();
        var session = await check.SessionState.SingleAsync();
        Assert.Equal("scanning", session.Status);
        Assert.Equal(
            connect.Succeeded ? "new-root" : "old-root",
            session.AltmountMetadataRoot);
    }

    [Fact]
    public async Task ResetAndScanRace_HasOneDestructiveWinner()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "connected";
            s.AltmountMetadataRoot = "metadata-root";
        });

        var resetTask = h.Store.ResetAsync();
        var scanTask = h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartScan);
        await Task.WhenAll(resetTask, scanTask);

        var reset = await resetTask;
        var scan = await scanTask;
        Assert.NotEqual(reset, scan.Succeeded);

        var session = await h.Store.GetSessionAsync();
        Assert.Equal(reset ? "idle" : "scanning", session.Status);
    }

    [Fact]
    public async Task Step6CompetingClaims_HaveOneWinner()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s => s.Status = "linked");

        var results = await Task.WhenAll(
            h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartLinkPlan),
            h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartApply),
            h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartRestore));

        Assert.Single(results, r => r.Outcome == MigrationSessionTransitionOutcome.Applied);
        Assert.Equal(2, results.Count(r => r.Outcome == MigrationSessionTransitionOutcome.Rejected));
        Assert.Contains((await h.Store.GetSessionAsync()).Status, new[] { "linking", "applying", "restoring" });
    }

    private sealed record ExpectedRule(string TargetStatus, params string[] SourceStatuses);
}
