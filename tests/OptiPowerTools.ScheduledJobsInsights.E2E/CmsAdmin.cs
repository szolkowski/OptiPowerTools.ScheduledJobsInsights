using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>Driving the CMS's own Scheduled Jobs admin: starting and stopping a real job.</summary>
internal static class CmsAdmin
{
    /// <summary>
    /// A full navigation every time: the admin is a single-page app routed on the hash, and moving
    /// between two of its hash routes in place does not reliably re-render it.
    /// </summary>
    public static async Task OpenJobAsync(IPage page, E2ETarget target, Guid jobId)
    {
        await page.GotoAsync("about:blank");
        await page.GotoAsync(target.ScheduledJobAdmin(jobId));
    }

    /// <summary>The admin's button, once the admin has loaded the job and enabled it.</summary>
    public static ILocator Button(IPage page, string name) =>
        page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true }).And(page.Locator(":enabled"));

    /// <summary>Starts a job from its admin page and returns the execution it records.</summary>
    public static async Task<long> StartAsync(IPage page, E2ETarget target, string jobType)
    {
        var jobId = await SeedData.ScheduledJobIdAsync(target.ConnectionString, jobType);
        var previous = await SeedData.LatestExecutionIdAsync(target.ConnectionString, jobType);

        await OpenJobAsync(page, target, jobId);
        await Button(page, "Start").ClickAsync();

        // Up to a minute: CMS 12 queues a manual start and picks it up on its scheduler's next pass.
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var latest = await SeedData.LatestExecutionIdAsync(target.ConnectionString, jobType);
            if (latest > previous)
                return latest;
            await Task.Delay(500);
        }

        throw new TimeoutException($"{jobType} was started but never recorded an execution.");
    }

    public static async Task StopAsync(IPage page, E2ETarget target, string jobType)
    {
        await OpenJobAsync(page, target, await SeedData.ScheduledJobIdAsync(target.ConnectionString, jobType));
        await Button(page, "Stop").ClickAsync();
    }
}
