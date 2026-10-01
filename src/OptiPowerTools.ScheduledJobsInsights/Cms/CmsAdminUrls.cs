namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// URLs of the CMS's own scheduled job admin screens, so this package's views can link across to them.
/// </summary>
/// <remarks>
/// Optimizely does not expose a resolver for these, so they are hard-coded per CMS version (each
/// verified against a running site): CMS 13 serves the Settings SPA under <c>/Optimizely/Settings</c>,
/// CMS 12 under <c>/EPiServer/EPiServer.Cms.UI.Admin</c>, where the CMS 13 path is a 404. Keeping them
/// in one place means a future CMS release that moves the Settings SPA only breaks here. The links degrade gracefully: a wrong URL lands on the Settings
/// home rather than erroring.
/// </remarks>
internal static class CmsAdminUrls
{
    /// <summary>The native Scheduled Jobs list, under Settings &gt; Data &amp; Sync Management.</summary>
#if CMS12
    public const string ScheduledJobsList = "/EPiServer/EPiServer.Cms.UI.Admin/default#/ScheduledJobs";
#else
    public const string ScheduledJobsList = "/Optimizely/Settings/default#/ScheduledJobs";
#endif

    /// <summary>The native settings/detail page for a single scheduled job.</summary>
    /// <param name="scheduledJobId">The CMS's own id for the job, as recorded on each execution.</param>
    public static string ScheduledJobDetail(Guid scheduledJobId) =>
        $"{ScheduledJobsList}/detailScheduledJob/{scheduledJobId}";
}
