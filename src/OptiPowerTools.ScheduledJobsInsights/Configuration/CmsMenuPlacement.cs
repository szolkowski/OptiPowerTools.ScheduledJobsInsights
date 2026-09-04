namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Controls where the menu entries appear in Optimizely CMS navigation.
/// </summary>
/// <remarks>
/// Selects <em>one</em> location, and deliberately so. The CMS shell identifies a menu entry by its
/// URL — it compares the request path against each registered item and has no idea which entry was
/// clicked — so two entries pointing at the same page cannot both be resolved correctly, and which
/// one wins differs between CMS UI versions. See
/// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.MenuPlacement"/> for what that cost in
/// practice.
/// </remarks>
public enum CmsMenuPlacement
{
    /// <summary>
    /// Places the menu item as a sub-entry under the existing CMS section.
    /// </summary>
    CmsSection = 0,

    /// <summary>
    /// Places the menu item directly in the global navigation bar as a top-level entry.
    /// </summary>
    TopLevel = 1,

    /// <summary>
    /// Creates a new section group and nests the menu item underneath it.
    /// The section name is controlled by <see cref="OptiPowerToolsScheduledJobsInsightsOptions.CustomSectionName"/>.
    /// </summary>
    CustomSection = 2,

    /// <summary>
    /// Places the menu item inside the CMS's own <em>Settings &gt; Data &amp; Sync Management</em>
    /// group, directly below the native <em>Scheduled Jobs</em> page. This is the default.
    /// </summary>
    /// <remarks>
    /// The default because it is where an administrator looking at a job goes to find its history,
    /// and because it is the only placement that keeps the reader inside the admin navigation tree:
    /// the entry is a leaf of the CMS's Settings branch, so the shell resolves that branch and the
    /// Settings sub-navigation stays on screen. A leaf placed at the top of a product's own
    /// navigation has no children, and the shell then renders no second panel at all — correct
    /// behaviour for a top-level entry, but it reads as leaving the admin view.
    /// </remarks>
    DataSyncManagement = 3
}
