using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// A real job, started and stopped from the CMS's own Scheduled Jobs admin, watched in this package's
/// detail page while it runs. The one test here that goes through the CMS end to end - and the one
/// place the CMS versions behave differently: CMS 12 queues a manual start for its scheduler service,
/// so the run begins a few seconds after Start is clicked rather than at once.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class LiveRunTests(StackFixture stack)
{
    // ~60 seconds in 2-second batches, stoppable, logging every batch: long enough to watch and stop.
    private const string JobType = "OptiPowerTools.ScheduledJobsInsights.Web.Samples.SlowMigrationJob";

    [E2EFact]
    public Task AStartedJob_RunsLiveInTheDetailPage_AndStoppingIt_RecordsStopped() =>
        stack.RunAsync(nameof(AStartedJob_RunsLiveInTheDetailPage_AndStoppingIt_RecordsStopped), async session =>
        {
            var target = session.Target;
            var page = session.Page;
            var jobId = await SeedData.ScheduledJobIdAsync(target.ConnectionString, JobType);
            var previous = await SeedData.LatestExecutionIdAsync(target.ConnectionString, JobType);

            await OpenJobAdminAsync(page, target, jobId);
            await Button(page, "Start").ClickAsync();

            var executionId = await WaitForNewExecutionAsync(target.ConnectionString, previous);

            await session.OpenDetailAsync(executionId);
            await Assertions.Expect(page.Locator(".status-badge")).ToHaveTextAsync("Running");
            await Assertions.Expect(page.Locator(".live-indicator")).ToBeVisibleAsync();
            // The page polls while the run is live: the log keeps growing without a reload.
            var count = page.Locator(".log-count");
            var before = await count.InnerTextAsync();
            await Assertions.Expect(count).Not.ToHaveTextAsync(before, new() { Timeout = 15_000 });

            await OpenJobAdminAsync(page, target, jobId);
            await Button(page, "Stop").ClickAsync();

            await session.OpenDetailAsync(executionId);
            await Assertions.Expect(page.Locator(".status-badge")).ToHaveTextAsync("Stopped", new() { Timeout = 30_000 });
            await Assertions.Expect(page.Locator(".sji-content")).ToContainTextAsync("Stopped early");
            await Assertions.Expect(page.Locator(".live-indicator")).ToHaveCountAsync(0);
        });

    /// <summary>
    /// A full navigation every time: the admin is a single-page app routed on the hash, and moving
    /// between two of its hash routes in place does not reliably re-render it.
    /// </summary>
    private static async Task OpenJobAdminAsync(IPage page, E2ETarget target, Guid jobId)
    {
        await page.GotoAsync("about:blank");
        await page.GotoAsync(target.ScheduledJobAdmin(jobId));
    }

    /// <summary>The admin's button, once the admin has loaded the job and enabled it.</summary>
    private static ILocator Button(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true }).And(page.Locator(":enabled"));

    private static async Task<long> WaitForNewExecutionAsync(string connectionString, long previous)
    {
        // Up to a minute: CMS 12 picks queued starts up on its scheduler's next pass.
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var latest = await SeedData.LatestExecutionIdAsync(connectionString, JobType);
            if (latest > previous)
                return latest;
            await Task.Delay(500);
        }

        throw new TimeoutException("The started job never recorded an execution.");
    }
}
