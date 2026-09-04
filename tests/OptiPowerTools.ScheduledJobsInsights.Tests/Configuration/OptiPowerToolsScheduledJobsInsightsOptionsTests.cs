using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Configuration;

public class OptiPowerToolsScheduledJobsInsightsOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new OptiPowerToolsScheduledJobsInsightsOptions();

        Assert.Equal("/ScheduledJobsInsightsCms/Index", options.CmsShellPath);
        // A path of its own, not a query string on the one above: the CMS shell highlights the menu
        // entry whose URL equals the request path, and never looks at the query string.
        Assert.Equal("/ScheduledJobsInsightsCms/Retention", options.CmsRetentionPath);
        Assert.NotEqual(options.CmsShellPath, options.CmsRetentionPath);
        Assert.True(options.EnableCmsMenu);
        Assert.False(options.AllowAnyAuthenticatedUser);
        Assert.Null(options.AuthorizationPolicy);
        Assert.Null(options.MapBlazorHub);
        // Beside the CMS's own Scheduled Jobs page, and one place only: a second entry for the same
        // page cannot be resolved by the shell, which identifies an entry by its URL.
        Assert.Equal(CmsMenuPlacement.DataSyncManagement, options.MenuPlacement);
        // Empty on purpose, and not the same thing as "nobody": it resolves to the built-in role set
        // when the policy is built. A non-empty default could not be replaced from appsettings.json,
        // because ConfigurationBinder adds into an existing collection rather than clearing it.
        Assert.Empty(options.AuthorizedRoles);
        Assert.Equal(["Administrators", "CmsAdmins", "WebAdmins"], OptiPowerToolsScheduledJobsInsightsOptions.DefaultAuthorizedRoles);
        Assert.Equal(JobResultSummary.DefaultMaxLength, options.MaxResultSummaryLength);
    }
}
