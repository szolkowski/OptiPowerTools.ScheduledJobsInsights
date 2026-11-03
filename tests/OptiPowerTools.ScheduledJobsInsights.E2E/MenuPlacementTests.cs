using System.Text.Json;
using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The three <c>MenuPlacement</c> values other than the default, each in a scenario of its own: the web
/// container is re-created with the placement set (scripts/e2e.sh --scenarios). The default,
/// DataSyncManagement, is what every other test runs under.
/// </summary>
/// <remarks>
/// What each placement must keep true is the rule the menu provider is built around: <b>one entry per
/// page</b>, at the placement's path and nowhere else, because the shell identifies an entry by its URL
/// and resolves duplicates differently per CMS UI version. Beyond that, each of our pages must resolve
/// a product - an empty <c>data-epi-product-id</c> leaves the menu spinning for ever - and the CMS's
/// own admin must keep its Settings navigation, which a stray entry once made vanish.
/// Outside DataSyncManagement our pages highlight no <c>nav-list__link</c> on either CMS version: their
/// entries sit at the top of a product's navigation, not in a second-level list. That is the expected
/// shape, not asserted either way.
/// </remarks>
[Collection(StackCollection.Name)]
public sealed class MenuPlacementTests(StackFixture stack)
{
    [E2EFact("menu-CmsSection")]
    public Task CmsSection_PutsOneEntryPerPage_UnderTheCmsProduct() =>
        AssertPlacementAsync(nameof(CmsSection_PutsOneEntryPerPage_UnderTheCmsProduct), "/global/cms", "global_cms");

    // The section is named after CustomSectionName ("OptiPowerTools" by default) for both placements.
    [E2EFact("menu-TopLevel")]
    public Task TopLevel_PutsOneEntryPerPage_InASectionOfItsOwn() =>
        AssertPlacementAsync(nameof(TopLevel_PutsOneEntryPerPage_InASectionOfItsOwn), "/global/optipowertools", "global_optipowertools");

    [E2EFact("menu-CustomSection")]
    public Task CustomSection_PutsOneEntryPerPage_InTheNamedSection() =>
        AssertPlacementAsync(nameof(CustomSection_PutsOneEntryPerPage_InTheNamedSection), "/global/optipowertools", "global_optipowertools");

    private Task AssertPlacementAsync(string testName, string parentPath, string productId) =>
        stack.RunAsync(testName, async session =>
        {
            var page = session.Page;

            var entries = await InsightsMenuEntriesAsync(page, "/ScheduledJobsInsightsCms/Index");
            Assert.Equal(
                [
                    ($"{parentPath}/scheduledjobsinsights", "/ScheduledJobsInsightsCms/Index"),
                    ($"{parentPath}/scheduledjobsinsightsretention", "/ScheduledJobsInsightsCms/Retention"),
                ],
                entries.OrderBy(e => e.Path, StringComparer.Ordinal));

            // Each page is drawn inside the product its entry belongs to, with its entry on screen.
            await session.OpenListAsync();
            await AssertProductAsync(page, productId);
            await session.OpenRetentionAsync();
            await AssertProductAsync(page, productId);

            // The CMS's own Scheduled Jobs admin keeps its Settings navigation, with its own entry
            // selected and none of ours in it.
            await page.GotoAsync(session.Target.ScheduledJobsAdmin);
            Assert.Equal("Scheduled Jobs", await session.SelectedMenuEntryAsync());
            Assert.Empty(await page.Locator("li.nav-list__link").Filter(new() { HasText = "Insights" }).AllInnerTextsAsync());
        });

    private static async Task AssertProductAsync(IPage page, string productId)
    {
        await Assertions.Expect(page.Locator("[data-epi-product-id]").First)
            .ToHaveAttributeAsync("data-epi-product-id", productId);
        // A link to each page, outside the page's own content: the menu drew both entries.
        foreach (var url in new[] { "/ScheduledJobsInsightsCms/Index", "/ScheduledJobsInsightsCms/Retention" })
        {
            await Assertions.Expect(page.Locator($"a[href*='{url}']:not(.sji-content a)")).ToHaveCountAsync(1);
        }
    }

    /// <summary>
    /// Every entry leading to one of this package's pages, as (menu path, URL), read from the navigation
    /// tree the shell fetches when a page loads.
    /// </summary>
    private static async Task<List<(string Path, string Url)>> InsightsMenuEntriesAsync(IPage page, string path)
    {
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GotoAsync(path),
            r => r.Url.Contains("epiplatformnavigation", StringComparison.OrdinalIgnoreCase));
        using var json = JsonDocument.Parse(await response.TextAsync());

        var entries = new List<(string, string)>();
        Walk(json.RootElement.GetProperty("menuItems"));
        return entries;

        void Walk(JsonElement items)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("url", out var url) && url.GetString() is { } value
                    && value.Contains("ScheduledJobsInsightsCms", StringComparison.OrdinalIgnoreCase))
                    entries.Add((item.GetProperty("path").GetString()!, value));
                if (item.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                    Walk(children);
            }
        }
    }
}
