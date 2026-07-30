using NzbWebDAV.Api.Controllers.UsenetMigration;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Models.UsenetMigration;

namespace NzbWebDAV.Tests.UsenetMigration;

public class SubmissionIssueTests
{
    [Fact]
    public async Task LoadSubmissionIssues_ReturnsFailedAndEvictedReasons()
    {
        await using var harness = await MigrationTestHarness.CreateAsync();
        await using (var seed = harness.Mig())
        {
            AddRelease(seed, "failed", "Failed.Release");
            AddRelease(seed, "evicted", "Evicted.Release");
            AddRelease(seed, "completed", "Completed.Release");
            seed.Submissions.AddRange(
                Submission("failed", "failed", "The store could not be read."),
                Submission("evicted", "evicted", null),
                Submission("completed", "completed", null));
            await seed.SaveChangesAsync();
        }

        await using var context = harness.Mig();
        var issues = await UsenetMigrationController.LoadSubmissionIssuesAsync(context);

        Assert.Collection(
            issues,
            failed =>
            {
                Assert.Equal("Failed.Release", failed.SubmitFileName);
                Assert.Equal("failed", failed.State);
                Assert.Equal("The store could not be read.", failed.Reason);
            },
            evicted =>
            {
                Assert.Equal("Evicted.Release", evicted.SubmitFileName);
                Assert.Equal("evicted", evicted.State);
                Assert.Contains("disappeared from both", evicted.Reason);
            });
    }

    private static void AddRelease(
        UsenetMigrationDbContext context,
        string storeRef,
        string submitFileName)
    {
        context.Releases.Add(new MigrationRelease
        {
            StoreRef = storeRef,
            StoreBasename = submitFileName,
            SubmitFileName = submitFileName,
            QueueFileName = $"{submitFileName}.nzb",
            JobName = submitFileName,
            Verdict = "green",
            VerdictReasons = "[]",
            ScannedAt = DateTime.UtcNow,
        });
    }

    private static MigrationSubmission Submission(
        string storeRef,
        string state,
        string? error) => new()
        {
            StoreRef = storeRef,
            State = state,
            Error = error,
            UpdatedAt = DateTime.UtcNow,
        };
}
