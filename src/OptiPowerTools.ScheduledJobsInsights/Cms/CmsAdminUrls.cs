namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// URLs of the CMS's own scheduled job admin screens, so this package's views can link across to them.
/// </summary>
/// <remarks>
/// Optimizely does not expose a resolver for these, so they are hard-coded against the CMS 13 shell
/// (verified against a running site). Keeping them in one place means a future CMS release that moves
/// the Settings SPA only breaks here. The links degrade gracefully: a wrong URL lands on the Settings
/// home rather than erroring.
/// </remarks>
internal static class CmsAdminUrls
{
    /// <summary>The native Scheduled Jobs list, under Settings &gt; Data &amp; Sync Management.</summary>
    public const string ScheduledJobsList = "/Optimizely/Settings/default#/ScheduledJobs";

    /// <summary>The native settings/detail page for a single scheduled job.</summary>
    /// <param name="scheduledJobId">The CMS's own id for the job, as recorded on each execution.</param>
    public static string ScheduledJobDetail(Guid scheduledJobId) =>
        $"{ScheduledJobsList}/detailScheduledJob/{scheduledJobId}";
}
