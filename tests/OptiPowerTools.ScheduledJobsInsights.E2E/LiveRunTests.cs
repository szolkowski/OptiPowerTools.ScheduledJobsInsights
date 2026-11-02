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

            var executionId = await CmsAdmin.StartAsync(page, target, JobType);

            await session.OpenDetailAsync(executionId);
            // Under the name the CMS's own admin shows - on CMS 13 too, whose job registry holds only
            // the class name. Recording that name headed every CMS 13 run "SlowMigrationJob".
            await Assertions.Expect(page.Locator(".page-header h1")).ToHaveTextAsync("Sample: Slow Migration");
            await Assertions.Expect(page.Locator(".status-badge")).ToHaveTextAsync("Running");
            await Assertions.Expect(page.Locator(".live-indicator")).ToBeVisibleAsync();
            // The page polls while the run is live: the log keeps growing without a reload.
            var count = page.Locator(".log-count");
            var before = await count.InnerTextAsync();
            await Assertions.Expect(count).Not.ToHaveTextAsync(before, new() { Timeout = 15_000 });

            await CmsAdmin.StopAsync(page, target, JobType);

            await session.OpenDetailAsync(executionId);
            await Assertions.Expect(page.Locator(".status-badge")).ToHaveTextAsync("Stopped", new() { Timeout = 30_000 });
            await Assertions.Expect(page.Locator(".sji-content")).ToContainTextAsync("Stopped early");
            await Assertions.Expect(page.Locator(".live-indicator")).ToHaveCountAsync(0);

            // The automatic metrics reach the database through the buffered writer, after the run is
            // already marked finished - so a page that stopped reading the moment the status flipped
            // showed a finished run with an empty metrics table. It reads once more for exactly this.
            await Assertions.Expect(page.Locator(".metrics-table")).ToContainTextAsync("DurationMs", new() { Timeout = 30_000 });
        });
}
