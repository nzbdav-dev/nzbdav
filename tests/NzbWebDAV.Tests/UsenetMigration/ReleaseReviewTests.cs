using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Api.Controllers.UsenetMigration;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.UsenetMigration.Triage;

namespace NzbWebDAV.Tests.UsenetMigration;

public class ReleaseReviewTests
{
    [Fact]
    public void ReleaseDto_OmitsVerdictForAlreadyMigratedRelease()
    {
        var release = Release("already", included: true, verdict: "green",
            reasons: [VerdictReason.AlreadyMigrated]);

        var dto = ReleaseDto.From(release);

        Assert.Null(dto.Verdict);
        Assert.Contains(VerdictReason.AlreadyMigrated, dto.VerdictReasons);
    }

    [Fact]
    public async Task ReleaseSort_OnlyDefaultPutsMigratingReleasesFirst()
    {
        await using var harness = await MigrationTestHarness.CreateAsync();
        await using (var seed = harness.Mig())
        {
            seed.Releases.AddRange(
                Release("z-migrating", included: true, verdict: "green", bytes: 10),
                Release("a-migrating", included: true, verdict: "amber", bytes: 50),
                Release("a-already", included: true, verdict: "green",
                    bytes: 100, reasons: [VerdictReason.AlreadyMigrated]),
                Release("b-excluded", included: false, verdict: "green", bytes: 75),
                Release("c-red", included: true, verdict: "red", bytes: 25));
            await seed.SaveChangesAsync();
        }

        await using var context = harness.Mig();
        var defaultOrder = await UsenetMigrationController.ApplyReleaseSort(
                context.Releases.AsNoTracking(), null)
            .Select(r => r.StoreRef)
            .ToListAsync();
        var nameOrder = await UsenetMigrationController.ApplyReleaseSort(
                context.Releases.AsNoTracking(), "name")
            .Select(r => r.StoreRef)
            .ToListAsync();
        var largestOrder = await UsenetMigrationController.ApplyReleaseSort(
                context.Releases.AsNoTracking(), "bytes")
            .Select(r => r.StoreRef)
            .ToListAsync();

        Assert.Equal(
            ["a-migrating", "z-migrating", "a-already", "b-excluded", "c-red"],
            defaultOrder);
        Assert.Equal(
            ["a-already", "a-migrating", "b-excluded", "c-red", "z-migrating"],
            nameOrder);
        Assert.Equal(
            ["a-already", "b-excluded", "a-migrating", "c-red", "z-migrating"],
            largestOrder);
    }

    private static MigrationRelease Release(
        string name,
        bool included,
        string verdict,
        long bytes = 0,
        params string[] reasons) => new()
        {
            StoreRef = name,
            StoreBasename = name,
            SubmitFileName = name,
            QueueFileName = $"{name}.nzb",
            JobName = name,
            Verdict = verdict,
            VerdictReasons = JsonSerializer.Serialize(reasons),
            Included = included,
            EstFetchBytesLazy = bytes,
            ScannedAt = DateTime.UtcNow,
        };
}
