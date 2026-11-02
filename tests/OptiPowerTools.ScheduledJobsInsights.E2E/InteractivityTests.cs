namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// Each page goes live - its Blazor circuit connects - and loads everything it needs with nothing
/// failing on the way. A page that only ever prerenders looks right and does nothing, which is how a
/// missing base href and a 404 on blazor.server.js both presented; opening a page waits for the
/// interactive marker, so a page that never goes live fails here.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class InteractivityTests(StackFixture stack)
{
    [E2EFact]
    public Task TheListPage_GoesLive_WithEveryAssetLoadedAndAQuietConsole() =>
        stack.RunAsync(nameof(TheListPage_GoesLive_WithEveryAssetLoadedAndAQuietConsole), async session =>
        {
            await session.OpenListAsync();

            AssertAssetsAndConsole(session, "css/scheduled-jobs-insights.css", "js/timezone.js");
        });

    [E2EFact]
    public Task TheDetailPage_GoesLive_WithEveryAssetLoadedAndAQuietConsole() =>
        stack.RunAsync(nameof(TheDetailPage_GoesLive_WithEveryAssetLoadedAndAQuietConsole), async session =>
        {
            await session.OpenDetailAsync(stack.Data.LogExecutionId);
            // The interop module is imported once the circuit is up; its buttons enable when it has.
            await session.Page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Jump to end" })
                .And(session.Page.Locator(":enabled")).WaitForAsync();

            AssertAssetsAndConsole(session, "css/scheduled-jobs-insights.css", "js/timezone.js", "js/detail-interop.js");
        });

    [E2EFact]
    public Task TheRetentionPage_GoesLive_WithEveryAssetLoadedAndAQuietConsole() =>
        stack.RunAsync(nameof(TheRetentionPage_GoesLive_WithEveryAssetLoadedAndAQuietConsole), async session =>
        {
            await session.OpenRetentionAsync();

            AssertAssetsAndConsole(session, "css/scheduled-jobs-insights.css", "js/timezone.js");
        });

    /// <summary>
    /// Every package and framework asset the page requested came back 200, the ones named were among
    /// them - served from this package's own _content path - and the browser reported no error.
    /// </summary>
    private static void AssertAssetsAndConsole(BrowserSession session, params string[] packageAssets)
    {
        var failed = session.AssetResponses.Where(r => r.Status != 200).ToList();
        Assert.True(failed.Count == 0, "Assets that did not load: " + string.Join(", ", failed));

        var loaded = session.AssetResponses.Select(r => r.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("/_framework/blazor.server.js", loaded);
        foreach (var asset in packageAssets)
            Assert.Contains($"/_content/{session.Target.PackageId}/{asset}", loaded);

        Assert.True(session.PageErrors.Count == 0, "Uncaught errors: " + string.Join(" | ", session.PageErrors));
        Assert.True(session.ConsoleErrors.Count == 0, "Console errors: " + string.Join(" | ", session.ConsoleErrors));
    }
}
