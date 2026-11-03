using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The cleanup job, run from the CMS's own admin against real SQL Server: it deletes what is past its
/// retention, keeps what is not, and never deletes a run still marked Running - however old, because a
/// long job can outlive its own retention and deleting its row would destroy a live run's history.
/// Interrupted-marking runs last, after the deletes, so a stranded run is marked this pass and only
/// becomes deletable on a later one. That ordering was once wrong, and is what the last check pins.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class CleanupTests(StackFixture stack)
{
    private const string RetiredType = "E2E.Retired";

    [E2EFact]
    public Task TheCleanupJob_DeletesWhatIsPastItsRetention_AndSparesALiveRun() =>
        stack.RunAsync(nameof(TheCleanupJob_DeletesWhatIsPastItsRetention_AndSparesALiveRun), async session =>
        {
            var target = session.Target;
            var db = target.ConnectionString;
            var now = DateTimeOffset.UtcNow;

            await SeedData.SetRetentionAsync(db, RetiredType, days: 1);
            try
            {
                var expired = new[]
                {
                    await SeedData.InsertAsync(db, "E2E Retired Job", "Retired", status: 1, now.AddDays(-10)),
                    await SeedData.InsertAsync(db, "E2E Retired Job", "Retired", status: 2, now.AddDays(-9)),
                };
                var recent = await SeedData.InsertAsync(db, "E2E Retired Job", "Retired", status: 1, now.AddHours(-1));
                var stranded = await SeedData.InsertAsync(db, "E2E Retired Job", "Retired", status: 0, now.AddDays(-10), completed: false);

                var cleanupRun = await CmsAdmin.StartAsync(session.Page, target, SeedData.CleanupJobType);
                await WaitUntilFinishedAsync(db, cleanupRun);

                foreach (var id in expired)
                    Assert.Null(await SeedData.StatusAsync(db, id));
                Assert.Equal(1, await SeedData.StatusAsync(db, recent));
                // Not deleted - it was Running when the deletes ran - and marked Interrupted afterwards.
                Assert.Equal(4, await SeedData.StatusAsync(db, stranded));

                // The run reports what it did, per job type, in its own log on the detail page.
                await session.OpenDetailAsync(cleanupRun);
                await Assertions.Expect(session.Page.Locator(".console")).ToContainTextAsync($"Deleted 2 execution(s) under {RetiredType} (1 day(s))");
            }
            finally
            {
                await SeedData.SetRetentionAsync(db, RetiredType, days: null);
            }
        });

    private static async Task WaitUntilFinishedAsync(string connectionString, long executionId)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (await SeedData.StatusAsync(connectionString, executionId) is not (0 or null))
                return;
            await Task.Delay(500);
        }

        throw new TimeoutException("The cleanup job never finished.");
    }
}
