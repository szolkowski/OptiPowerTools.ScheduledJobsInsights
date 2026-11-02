using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The pages keep working under a strict Content Security Policy - no inline styles, no inline
/// scripts - which is the ordinary case for a CMS back office. Two decisions exist for exactly this,
/// and both fail silently when undone: severity and status colours come from CSS classes, not style
/// attributes (otherwise the log loses all its colour), and the time-zone script is an external file,
/// not inline (otherwise the cookie is never set and every reader stays on UTC).
/// </summary>
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
            await session.OpenDetailAsync(stack.Data.LogExecutionId);

            // The severity colour reaches the line through a class and a custom property only.
            var errorSeverity = page.Locator(".console-line.sji-sev-error .console-severity").First;
            await Assertions.Expect(errorSeverity).ToHaveCSSAsync("color", "rgb(239, 83, 80)");

            // The external time-zone script ran and set its cookie, so the next view is not in UTC.
            await session.OpenListAsync();
            await Assertions.Expect(page.Locator(".timezone-note")).Not.ToHaveTextAsync("Times shown in UTC");

            await session.OpenRetentionAsync();

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
    /// Whether a violation is this package's doing rather than the CMS shell's. Ours: anything blocked
    /// from this package's static assets or the Blazor runtime, and any inline style, inline script or
    /// eval - the regressions the policy exists to catch - unless the CMS shell's own script raised it.
    /// Not ours, measured on both CMS versions: the shell's navigation bundle injecting a style
    /// element, and external fonts its stylesheets pull in from a CDN.
    /// </summary>
    private static bool IsOurs(string violation)
    {
        var parts = violation.Split(" | ");
        var (blocked, source) = (parts[1], parts[2]);
        if (blocked.Contains("/_content/OptiPowerTools.ScheduledJobsInsights", StringComparison.OrdinalIgnoreCase)
            || blocked.Contains("/_framework/", StringComparison.OrdinalIgnoreCase))
            return true;

        return blocked is "inline" or "eval"
            && !source.Contains("/Shell/", StringComparison.OrdinalIgnoreCase);
    }
}
