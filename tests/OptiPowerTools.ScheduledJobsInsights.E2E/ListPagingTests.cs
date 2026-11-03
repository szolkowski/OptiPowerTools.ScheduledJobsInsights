using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The execution list's keyset paging and its filters, against real SQL Server - the only place they
/// run on it. The unit tests run the same queries on Sqlite, through a converter that exists only for
/// Sqlite, so the descending composite indexes and the cursor predicate are exercised here alone.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class ListPagingTests(StackFixture stack)
{
    private const string PagingJob = "E2E Paging Job";

    [E2EFact]
    public Task PagingThroughAJobsRuns_VisitsEachRunOnce_AndComesBack() =>
        stack.RunAsync(nameof(PagingThroughAJobsRuns_VisitsEachRunOnce_AndComesBack), async session =>
        {
            await session.OpenListAsync();
            var page = session.Page;
            await FilterByJobAsync(session);

            // Newest first: 120 down to 71, then 70 down to 21, then the last 20.
            await ExpectPageAsync(page, first: 120, last: 71);
            await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
            await ExpectPageAsync(page, first: 70, last: 21);
            await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
            await ExpectPageAsync(page, first: 20, last: 1);
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Next" })).ToBeDisabledAsync();

            await page.GetByRole(AriaRole.Button, new() { Name = "Previous" }).ClickAsync();
            await ExpectPageAsync(page, first: 70, last: 21);
        });

    [E2EFact]
    public Task TheStatusFilter_ShowsOnlyThatStatus_AndSaysSoWhenNothingMatches() =>
        stack.RunAsync(nameof(TheStatusFilter_ShowsOnlyThatStatus_AndSaysSoWhenNothingMatches), async session =>
        {
            await session.OpenListAsync();
            var page = session.Page;
            var statusFilter = page.GetByLabel("Filter by status");
            var rows = page.Locator("tr.execution-row");

            await statusFilter.SelectOptionAsync("Interrupted");
            await Assertions.Expect(rows.Filter(new() { HasText = "E2E Interrupted Job" })).ToHaveCountAsync(1);
            foreach (var badge in await rows.Locator(".status-badge").AllInnerTextsAsync())
                Assert.Equal("Interrupted", badge.Trim());

            // A job with no failed run, filtered to failures: the empty state for a filter, not "no history".
            await FilterByJobAsync(session);
            await statusFilter.SelectOptionAsync("Failed");
            await Assertions.Expect(page.GetByText("No executions match this filter.")).ToBeVisibleAsync();
            await Assertions.Expect(rows).ToHaveCountAsync(0);
        });

    /// <summary>
    /// Selects the paging job in the job filter. Its options come from a list of job names cached for 60
    /// seconds, process-wide; on a stack kept with --keep, a page opened before this run's seeding may
    /// still be served the old list, so this reloads until the seeded job is offered.
    /// </summary>
    private static async Task FilterByJobAsync(BrowserSession session)
    {
        var page = session.Page;
        var jobFilter = page.GetByLabel("Filter by job");
        for (var attempt = 0; await jobFilter.Locator("option", new() { HasText = PagingJob }).CountAsync() == 0; attempt++)
        {
            Assert.True(attempt < 15, $"The job filter never offered {PagingJob}.");
            await Task.Delay(5_000);
            await session.OpenListAsync();
        }

        await jobFilter.SelectOptionAsync(PagingJob);
    }

    private static async Task ExpectPageAsync(IPage page, int first, int last)
    {
        var rows = page.Locator("tr.execution-row");
        await Assertions.Expect(rows).ToHaveCountAsync(first - last + 1);
        await Assertions.Expect(rows.First.Locator(".result-cell")).ToHaveTextAsync(SeedData.PagingRun(first));
        await Assertions.Expect(rows.Last.Locator(".result-cell")).ToHaveTextAsync(SeedData.PagingRun(last));
    }
}
