using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// One test's page, plus what it saw: console errors, uncaught exceptions, and every response for
/// the package's and the framework's static assets. Recording starts before the first navigation, so
/// nothing a page does while loading is missed.
/// </summary>
internal sealed class BrowserSession
{
    private readonly List<string> _consoleErrors = [];
    private readonly List<string> _pageErrors = [];
    private readonly List<(string Url, int Status)> _assetResponses = [];

    public BrowserSession(E2ETarget target, IPage page)
    {
        Target = target;
        Page = page;
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                _consoleErrors.Add(message.Text);
        };
        page.PageError += (_, error) => _pageErrors.Add(error);
        page.Response += (_, response) =>
        {
            var path = new Uri(response.Url).AbsolutePath;
            if (path.StartsWith("/_content/", StringComparison.Ordinal)
                || path.StartsWith("/_framework/", StringComparison.Ordinal))
                _assetResponses.Add((path, response.Status));
        };
    }

    public E2ETarget Target { get; }

    public IPage Page { get; }

    public IReadOnlyList<string> ConsoleErrors => _consoleErrors;

    public IReadOnlyList<string> PageErrors => _pageErrors;

    public IReadOnlyList<(string Url, int Status)> AssetResponses => _assetResponses;

    /// <summary>The execution list.</summary>
    public Task OpenListAsync() => OpenAsync("/ScheduledJobsInsightsCms/Index");

    /// <summary>One execution's detail page — addressed by query string, never a path segment.</summary>
    public Task OpenDetailAsync(long id) => OpenAsync($"/ScheduledJobsInsightsCms/Index?id={id}");

    /// <summary>The retention screen, a route of its own.</summary>
    public Task OpenRetentionAsync() => OpenAsync("/ScheduledJobsInsightsCms/Retention");

    /// <summary>
    /// Navigates and waits until the page is <em>interactive</em>: the Blazor circuit has connected and
    /// rendered, which the page announces with a <c>data-sji-interactive</c> marker that prerendering
    /// never emits. A page that only ever prerenders - the symptom of a missing base href or a 404 on
    /// blazor.server.js - looks right and fails here.
    /// </summary>
    private async Task OpenAsync(string path)
    {
        var response = await Page.GotoAsync(path);
        Assert.Equal(200, response?.Status);
        await Page.Locator("[data-sji-interactive]").WaitForAsync(new()
        {
            State = WaitForSelectorState.Attached,
            Timeout = 30_000,
        });
    }

    /// <summary>The CMS navigation entry currently marked selected, or null when none is.</summary>
    public async Task<string?> SelectedMenuEntryAsync()
    {
        // The navigation is drawn client-side by the shell's own script, after the page loads.
        var navigation = Page.Locator("li.nav-list__link");
        await navigation.First.WaitForAsync(new() { Timeout = 15_000 });

        var selected = Page.Locator("li.nav-list__link.is-active");
        return await selected.CountAsync() == 0 ? null : (await selected.First.InnerTextAsync()).Trim();
    }
}
