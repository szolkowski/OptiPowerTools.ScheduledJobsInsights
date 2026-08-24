namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Controls where the menu item appears in Optimizely CMS navigation.
/// </summary>
public enum CmsMenuPlacement
{
    /// <summary>
    /// Places the menu item as a sub-entry under the existing CMS section.
    /// This is the default behavior.
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
    CustomSection = 2
}
