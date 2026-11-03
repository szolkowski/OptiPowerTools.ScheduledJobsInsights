using System.Text.Json;
using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The pages and their menu entries answer to one authorization policy, so they can never disagree:
/// a user the policy refuses is turned away from the pages and offered no entry leading to them. The
/// user here is a CMS editor - signed in and allowed into the CMS, so a refusal is about the role, not
/// about being signed in.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class AuthorizationTests(StackFixture stack)
{
    [E2EFact]
    public Task AnEditor_IsTurnedAwayFromThePages_ThoughTheCmsLetsThemIn() =>
        stack.RunAsync(nameof(AnEditor_IsTurnedAwayFromThePages_ThoughTheCmsLetsThemIn), async session =>
        {
            var page = session.Page;

            // Signed in and allowed into the CMS's editing UI...
            await page.GotoAsync(session.Target.CmsEditPath);
            Assert.DoesNotContain("/Util/Login", page.Url, StringComparison.OrdinalIgnoreCase);

            // ...but not into either page. Optimizely answers an authenticated user who lacks the role
            // by sending them to the login page; what matters is that the page itself never renders.
            foreach (var path in new[] { "/ScheduledJobsInsightsCms/Index", "/ScheduledJobsInsightsCms/Retention" })
            {
                await page.GotoAsync(path);
                Assert.Contains("/Util/Login", page.Url, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0, await page.Locator(".sji-content").CountAsync());
            }
        }, asEditor: true);

    [E2EFact]
    public Task AnEditor_IsOfferedNoMenuEntry_WhereAnAdministratorHasTwo() =>
        stack.RunAsync(nameof(AnEditor_IsOfferedNoMenuEntry_WhereAnAdministratorHasTwo), async editor =>
        {
            var editorEntries = await InsightsMenuEntriesAsync(editor.Page, editor.Target.CmsEditPath);
            Assert.Empty(editorEntries);

            // The same question asked as the administrator, so an empty answer above cannot be an
            // artefact of how the navigation was read.
            await stack.RunAsync(nameof(AnEditor_IsOfferedNoMenuEntry_WhereAnAdministratorHasTwo) + "-admin", async admin =>
            {
                var adminEntries = await InsightsMenuEntriesAsync(admin.Page, "/ScheduledJobsInsightsCms/Index");
                Assert.Equal(2, adminEntries.Count);
            });
        }, asEditor: true);

    /// <summary>
    /// The menu entries leading to this package's pages, read from the navigation tree the CMS shell
    /// fetches for the current user when a page loads - the data the visible menu is drawn from.
    /// </summary>
    private static async Task<List<string>> InsightsMenuEntriesAsync(IPage page, string path)
    {
        var response = await page.RunAndWaitForResponseAsync(
            () => page.GotoAsync(path),
            r => r.Url.Contains("epiplatformnavigation", StringComparison.OrdinalIgnoreCase));
        using var json = JsonDocument.Parse(await response.TextAsync());

        var urls = new List<string>();
        Walk(json.RootElement.GetProperty("menuItems"));
        return urls.Where(u => u.Contains("ScheduledJobsInsightsCms", StringComparison.OrdinalIgnoreCase)).ToList();

        void Walk(JsonElement items)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                    urls.Add(url.GetString()!);
                if (item.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                    Walk(children);
            }
        }
    }
}
