using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The pages keep working under a strict Content Security Policy - no inline styles, no inline
/// scripts. Two decisions exist for exactly this, and both fail silently when undone: severity and
/// status colours come from CSS classes, not style attributes (otherwise the log loses all its
/// colour), and the time-zone script is an external file, not inline (otherwise the cookie is never
/// set and every reader stays on UTC).
/// </summary>
/// <remarks>
/// Inline styles are checked in the DOM, not through violation reports: a style attribute this package
/// wrote and one the framework's Virtualize wrote are reported identically, against the page's URL.
/// Virtualize draws the log's two scroll spacers as inline-styled divs, and sets overflow-anchor on the
/// console - so under a policy that blocks inline styles the log cannot scroll its full length. No
/// Optimizely back office runs under such a policy, though: the CMS shell's own navigation injects a
/// style element on both versions. Those three are the only inline styles allowed here.
/// </remarks>
[Collection(StackCollection.Name)]
public sealed class CspTests(StackFixture stack)
{
    /// <summary>
    /// Every violation, from every page the test opens. Reported into the test as it happens: a list
    /// kept in the page's own window would be discarded by each navigation, and only the last page's
    /// violations would ever be checked.
    /// </summary>
    private readonly List<string> _violations = [];

    private const string Policy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "font-src 'self' data:; connect-src 'self' ws: wss:";

    [E2EFact]
    public Task UnderAStrictPolicy_ThePagesGoLive_ColourTheLog_AndFollowTheReadersZone() =>
        stack.RunAsync(nameof(UnderAStrictPolicy_ThePagesGoLive_ColourTheLog_AndFollowTheReadersZone), async session =>
        {
            var page = session.Page;
            await page.Context.ClearCookiesAsync(new() { Name = "sji-timezone" });

            // Opening a page waits for it to go live, which blazor.server.js has to do under the policy.
            await session.OpenListAsync();
            await AssertNoInlineStylesOfOurOwnAsync(page);
            await session.OpenDetailAsync(stack.Data.LogExecutionId);
            await AssertNoInlineStylesOfOurOwnAsync(page);

            // The severity colour reaches the line through a class and a custom property only.
            var errorSeverity = page.Locator(".console-line.sji-sev-error .console-severity").First;
            await Assertions.Expect(errorSeverity).ToHaveCSSAsync("color", "rgb(239, 83, 80)");

            // The external time-zone script ran and set its cookie, so the next view is not in UTC.
            await session.OpenListAsync();
            await Assertions.Expect(page.Locator(".timezone-note")).Not.ToHaveTextAsync("Times shown in UTC");

            await session.OpenRetentionAsync();
            await AssertNoInlineStylesOfOurOwnAsync(page);

            var all = _violations.ToList();
            var ours = all.Where(IsOurs).ToList();
            Assert.True(ours.Count == 0,
                "CSP violations caused by this package:\n  " + string.Join("\n  ", ours) +
                "\nAll violations:\n  " + string.Join("\n  ", all));
        }, timezoneId: "Asia/Kathmandu", prepare: EnforcePolicyAsync);

    /// <summary>Adds the policy to every document the site serves, and reports each violation.</summary>
    private async Task EnforcePolicyAsync(IBrowserContext context)
    {
        await context.RouteAsync("**/*", async route =>
        {
            if (route.Request.ResourceType != "document")
            {
                await route.ContinueAsync();
                return;
            }

            var response = await route.FetchAsync();
            var headers = new Dictionary<string, string>(response.Headers) { ["content-security-policy"] = Policy };
            await route.FulfillAsync(new() { Response = response, Headers = headers });
        });

        await context.ExposeFunctionAsync("sjiReportCsp", (string violation) =>
        {
            lock (_violations)
                _violations.Add(violation);
        });
        await context.AddInitScriptAsync("""
            document.addEventListener('securitypolicyviolation', e => window.sjiReportCsp(
                `${e.violatedDirective} | ${e.blockedURI} | ${e.sourceFile} | ${e.sample}`));
            """);
    }

    /// <summary>
    /// No element on the page carries an inline style except Virtualize's spacers and its console
    /// anchoring. Checked as attributes, which stay in the DOM whether or not the policy applies them.
    /// </summary>
    private static async Task AssertNoInlineStylesOfOurOwnAsync(IPage page)
    {
        var styled = await page.EvaluateAsync<string[]>("""
            [...document.querySelectorAll('.sji-content [style]')]
                .filter(e => !(e.matches('.console') && e.getAttribute('style').trim() === 'overflow-anchor: none;'))
                .filter(e => !(e.parentElement?.matches('.console') && !e.className
                               && /^height: [\d.]+px; flex-shrink: 0;$/.test(e.getAttribute('style').trim())))
                .map(e => `${e.tagName.toLowerCase()}.${e.className} style="${e.getAttribute('style')}"`)
            """);
        Assert.True(styled.Length == 0, "Inline styles written by this package:\n  " + string.Join("\n  ", styled));
    }

    /// <summary>
    /// Whether a violation is this package's doing. Ours: anything blocked from this package's static
    /// assets, any inline script or eval, and any inline style element, unless the CMS shell's own
    /// script raised it. Inline style <em>attributes</em> are not judged here - see the remarks above.
    /// Not ours, measured on both CMS versions: the shell's navigation bundle injecting a style
    /// element, and external fonts its stylesheets pull in from a CDN.
    /// </summary>
    private static bool IsOurs(string violation)
    {
        var parts = violation.Split(" | ");
        var (directive, blocked, source) = (parts[0], parts[1], parts[2]);
        if (blocked.Contains("/_content/OptiPowerTools.ScheduledJobsInsights", StringComparison.OrdinalIgnoreCase)
            || (blocked.Contains("/_framework/", StringComparison.OrdinalIgnoreCase) && !directive.StartsWith("style-src-attr", StringComparison.Ordinal)))
            return true;

        return blocked is "inline" or "eval"
            && !directive.StartsWith("style-src-attr", StringComparison.Ordinal)
            && !source.Contains("/Shell/", StringComparison.OrdinalIgnoreCase);
    }
}
