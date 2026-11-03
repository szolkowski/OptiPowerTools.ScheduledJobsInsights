namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The pages render inside the CMS's own shell, on both CMS versions. Every assertion here guards a
/// failure that produced no error at all when it happened: tag helpers rendering as literal tags, a
/// navigation that resolved no product and spun forever, or the wrong menu entry highlighted.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class ShellTests(StackFixture stack)
{
    private const string InsightsEntry = "Scheduled Jobs Insights";
    private const string RetentionEntry = "Scheduled Jobs Insights - Retention";

    [E2EFact]
    public Task TheListPage_SitsInTheCmsShell_WithItsOwnEntrySelected() =>
        stack.RunAsync(nameof(TheListPage_SitsInTheCmsShell_WithItsOwnEntrySelected), async session =>
        {
            await session.OpenListAsync();

            await AssertInsideTheShellAsync(session);
            Assert.Equal(InsightsEntry, await session.SelectedMenuEntryAsync());
        });

    [E2EFact]
    public Task TheRetentionPage_SitsInTheCmsShell_WithItsOwnEntrySelected() =>
        stack.RunAsync(nameof(TheRetentionPage_SitsInTheCmsShell_WithItsOwnEntrySelected), async session =>
        {
            await session.OpenRetentionAsync();

            await AssertInsideTheShellAsync(session);
            Assert.Equal(RetentionEntry, await session.SelectedMenuEntryAsync());
        });

    [E2EFact]
    public Task TheDetailPage_SitsInTheCmsShell() =>
        stack.RunAsync(nameof(TheDetailPage_SitsInTheCmsShell), async session =>
        {
            await session.OpenDetailAsync(stack.Data.LogExecutionId);

            await AssertInsideTheShellAsync(session);
            // An execution has no menu entry of its own. CMS 13 keeps the insights entry selected;
            // CMS 12 selects none - a known, cosmetic gap, asserted so a change either way is noticed.
            Assert.Equal(
                session.Target.DetailPageHighlightsMenu ? InsightsEntry : null,
                await session.SelectedMenuEntryAsync());
        });

    private static async Task AssertInsideTheShellAsync(BrowserSession session)
    {
        var page = session.Page;

        // A tag helper that is not registered, or does not exist on this CMS version, renders as a
        // literal element of its own name - and the page still loads.
        Assert.Equal(0, await page.Locator("episerver-resources, platform-navigation, platform-navigation-wrapper").CountAsync());

        // The shell decides which product's navigation to draw by matching the request path against
        // registered menu entries. When nothing matches, this is empty and the menu never loads.
        var product = await page.Locator("[data-epi-product-id]").First.GetAttributeAsync("data-epi-product-id");
        Assert.False(string.IsNullOrEmpty(product), "The CMS shell resolved no product for this page.");

        // The admin sub-navigation stays on screen: the CMS's own Scheduled Jobs entry sits beside ours.
        await page.Locator("li.nav-list__link").Filter(new() { HasTextRegex = new("^Scheduled Jobs$") })
            .WaitForAsync(new() { Timeout = 15_000 });
    }
}
