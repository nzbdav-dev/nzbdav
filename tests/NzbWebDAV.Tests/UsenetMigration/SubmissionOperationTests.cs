using NzbWebDAV.UsenetMigration.Runner;

namespace NzbWebDAV.Tests.UsenetMigration;

public class SubmissionOperationTests
{
    [Fact]
    public void Interrupt_CancelsCurrentEpoch_AndResumeGetsFreshToken()
    {
        var gate = new SubmissionOperationGate();

        long firstEpoch;
        using (var first = gate.Begin(CancellationToken.None))
        {
            firstEpoch = first.Epoch;
            Assert.False(first.Token.IsCancellationRequested);

            gate.Interrupt();

            Assert.True(first.Token.IsCancellationRequested);
        }

        using var resumed = gate.Begin(CancellationToken.None);
        Assert.True(resumed.Epoch > firstEpoch);
        Assert.False(resumed.Token.IsCancellationRequested);
    }

    [Theory]
    [InlineData("running", true)]
    [InlineData("paused", false)]
    [InlineData("cancelled", false)]
    [InlineData("complete", false)]
    public async Task SubmitBoundary_RequiresLiveRunningSession(string status, bool expected)
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s => s.Status = status);

        Assert.Equal(expected,
            await SubmissionWorkerPool.CanSubmitNextAsync(
                h.Store, CancellationToken.None));
    }

    [Fact]
    public async Task SubmitBoundary_RejectsInterruptedEpoch_EvenWhenSessionStillRunning()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s => s.Status = "running");
        using var interrupted = new CancellationTokenSource();
        interrupted.Cancel();

        Assert.False(await SubmissionWorkerPool.CanSubmitNextAsync(
            h.Store, interrupted.Token));
    }
}
