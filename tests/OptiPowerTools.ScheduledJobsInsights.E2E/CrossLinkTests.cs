namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The links across to the CMS's own Scheduled Jobs screens land on a real page. Optimizely exposes no
/// resolver for those URLs, so they are hard-coded per CMS version - and the CMS 13 one is a 404 on
/// CMS 12. Nothing but a browser on each version can tell a right one from a wrong one.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class CrossLinkTests(StackFixture stack)
{
    private const string CleanupJob = "Scheduled Jobs Insights - Log Cleanup";

    [E2EFact]
    public Task TheListLinksToTheCmsScheduledJobsList() =>
        stack.RunAsync(nameof(TheListLinksToTheCmsScheduledJobsList), async session =>
        {
            await session.OpenListAsync();
            var page = session.Page;

            await page.Locator(".page-header a.action-link", new() { HasText = "Scheduled Jobs" }).ClickAsync();

            await page.WaitForURLAsync(url => new Uri(url).AbsolutePath.StartsWith(session.Target.AdminPath, StringComparison.OrdinalIgnoreCase));
            await Microsoft.Playwright.Assertions.Expect(page.GetByText(CleanupJob).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        });

    [E2EFact]
    public Task AnExecutionLinksToItsJobsCmsSettings() =>
        stack.RunAsync(nameof(AnExecutionLinksToItsJobsCmsSettings), async session =>
        {
            await session.OpenDetailAsync(stack.Data.LinkedExecutionId);
            var page = session.Page;

            await page.Locator("a.action-link", new() { HasText = "Scheduled job settings" }).ClickAsync();

            await page.WaitForURLAsync(url => url.Contains("detailScheduledJob/", StringComparison.OrdinalIgnoreCase));
            await Microsoft.Playwright.Assertions.Expect(page.GetByText(CleanupJob).First).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Microsoft.Playwright.Assertions.Expect(
                page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Start", Exact = true })).ToBeVisibleAsync();
        });
}
