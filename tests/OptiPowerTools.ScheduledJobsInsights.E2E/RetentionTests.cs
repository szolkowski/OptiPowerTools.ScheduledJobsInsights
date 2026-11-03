using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The retention screen saves as soon as a value is chosen - there is no Save button - so the only
/// proof a change took is that it survives a reload, attributed to whoever made it.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class RetentionTests(StackFixture stack)
{
    // A sample job present on both dev hosts, with no [JobRetention] of its own. Found by type name,
    // which every row shows and which is the same on both CMS versions - unlike its display name.
    private const string JobType = "OptiPowerTools.ScheduledJobsInsights.Web.Samples.QuietJob";

    [E2EFact]
    public Task ChoosingARetention_SavesIt_AndRecordsWhoChangedIt() =>
        stack.RunAsync(nameof(ChoosingARetention_SavesIt_AndRecordsWhoChangedIt), async session =>
        {
            await session.OpenRetentionAsync();
            var page = session.Page;

            try
            {
                await Select(page).SelectOptionAsync("14");
                await Assertions.Expect(Row(page).Locator(".saved")).ToHaveTextAsync("Saved");

                await session.OpenRetentionAsync();
                await Assertions.Expect(Select(page)).ToHaveValueAsync("14");
                await Assertions.Expect(Row(page).Locator(".effective")).ToHaveTextAsync("14 days");
                await Assertions.Expect(Row(page).Locator(".audit")).ToContainTextAsync(E2ETarget.AdminUser);
            }
            finally
            {
                // Back to the default, so a stack kept with --keep starts the next run the same way.
                await Select(page).SelectOptionAsync("inherit");
                await Assertions.Expect(Row(page).Locator(".saved")).ToHaveTextAsync("Saved");
            }
        });

    [E2EFact]
    public Task EachJob_IsListedUnderTheNameTheCmsAdminShows() =>
        stack.RunAsync(nameof(EachJob_IsListedUnderTheNameTheCmsAdminShows), async session =>
        {
            await session.OpenRetentionAsync();

            // The display name the job declares, not its class name - which is all CMS 13's job
            // registry holds, and what this screen used to show there.
            await Assertions.Expect(Row(session.Page).Locator(".job-name")).ToHaveTextAsync("Sample: Quiet");
        });

    private static ILocator Row(IPage page) =>
        page.Locator("tr").Filter(new() { Has = page.Locator(".job-type", new() { HasText = JobType }) });

    private static ILocator Select(IPage page) => Row(page).Locator("select");
}
