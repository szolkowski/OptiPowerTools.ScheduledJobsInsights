using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The detail page against a seeded execution with a 5,000-line log, a 250-line summary and three
/// metrics: the parts that depend on a real browser - virtualization, scrolling through JS interop,
/// native details elements - rather than on what bUnit can render.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class DetailPageTests(StackFixture stack)
{
    [E2EFact]
    public Task TheLog_CountsEveryLine_ButRendersOnlyAWindowOfThem() =>
        stack.RunAsync(nameof(TheLog_CountsEveryLine_ButRendersOnlyAWindowOfThem), async session =>
        {
            await session.OpenDetailAsync(stack.Data.LogExecutionId);
            var page = session.Page;

            await Assertions.Expect(page.Locator(".log-count")).ToHaveTextAsync($"{SeedData.LogLines:N0} lines");
            await Assertions.Expect(page.GetByText(SeedData.LogLine(1), new() { Exact = true })).ToBeVisibleAsync();

            // Virtualize keeps only the visible slice (plus overscan) in the DOM. All 5,000 would mean
            // it had stopped virtualizing - and a page that slow on every chatty job.
            var rendered = await page.Locator(".console-line").CountAsync();
            Assert.InRange(rendered, 1, 200);
        });

    [E2EFact]
    public Task JumpToEnd_ReachesTheLastLine_AndJumpToStart_TheFirst() =>
        stack.RunAsync(nameof(JumpToEnd_ReachesTheLastLine_AndJumpToStart_TheFirst), async session =>
        {
            await session.OpenDetailAsync(stack.Data.LogExecutionId);
            var page = session.Page;
            var first = page.GetByText(SeedData.LogLine(1), new() { Exact = true });
            var last = page.GetByText(SeedData.LogLine(SeedData.LogLines), new() { Exact = true });

            // Virtualize keeps no element at either end, so this only works if the interop module set
            // the console's scrollTop - a renamed JS function or a wrong module path fails here.
            await page.GetByRole(AriaRole.Button, new() { Name = "Jump to end" }).ClickAsync();
            await Assertions.Expect(last).ToBeVisibleAsync();
            await Assertions.Expect(first).ToHaveCountAsync(0);

            await page.GetByRole(AriaRole.Button, new() { Name = "Jump to start" }).ClickAsync();
            await Assertions.Expect(first).ToBeVisibleAsync();
            await Assertions.Expect(last).ToHaveCountAsync(0);
        });

    [E2EFact]
    public Task ALongSummary_StartsCollapsed_AndOpensOnClick() =>
        stack.RunAsync(nameof(ALongSummary_StartsCollapsed_AndOpensOnClick), async session =>
        {
            await session.OpenDetailAsync(stack.Data.LogExecutionId);
            var page = session.Page;
            var summary = page.Locator("details.accordion").Filter(new() { Has = page.Locator(".accordion-title", new() { HasText = "Result summary" }) });

            // Above the 200-line threshold the section starts closed, so the report does not bury the log.
            await Assertions.Expect(summary).Not.ToHaveAttributeAsync("open", "");
            await Assertions.Expect(summary.Locator(".accordion-badge")).ToContainTextAsync($"{SeedData.SummaryLines} lines");

            await summary.Locator("summary").ClickAsync();
            await Assertions.Expect(summary.Locator(".summary-text")).ToContainTextAsync("E2E summary line 250");
        });

    [E2EFact]
    public Task TheMetrics_ListEverySeededMetric() =>
        stack.RunAsync(nameof(TheMetrics_ListEverySeededMetric), async session =>
        {
            await session.OpenDetailAsync(stack.Data.LogExecutionId);
            var table = session.Page.Locator(".metrics-table");

            await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(SeedData.MetricNames.Length);
            foreach (var name in SeedData.MetricNames)
                await Assertions.Expect(table).ToContainTextAsync(name);
        });
}
