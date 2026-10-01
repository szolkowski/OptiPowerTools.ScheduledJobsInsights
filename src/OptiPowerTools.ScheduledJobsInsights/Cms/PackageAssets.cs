namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// Where this package's static web assets are served from. The SDK publishes them under
/// <c>_content/{PackageId}</c>, and the package id differs between the CMS 12 and CMS 13 builds.
/// </summary>
internal static class PackageAssets
{
    /// <summary>The NuGet package id this assembly ships in.</summary>
#if CMS12
    public const string PackageId = "OptiPowerTools.ScheduledJobsInsights.Cms12";
#else
    public const string PackageId = "OptiPowerTools.ScheduledJobsInsights";
#endif

    /// <summary>Base path of the static web assets, without a leading slash.</summary>
    public const string BasePath = "_content/" + PackageId;
}
