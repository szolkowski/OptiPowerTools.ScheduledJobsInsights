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
        Assert.True(options.EnableCmsMenu);
        Assert.False(options.AllowAnyAuthenticatedUser);
        Assert.Null(options.AuthorizationPolicy);
        Assert.Null(options.MapBlazorHub);
        Assert.Equal(CmsMenuPlacement.CmsSection, options.MenuPlacement);
        // Empty on purpose, and not the same thing as "nobody": it resolves to the built-in role set
        // when the policy is built. A non-empty default could not be replaced from appsettings.json,
        // because ConfigurationBinder adds into an existing collection rather than clearing it.
        Assert.Empty(options.AuthorizedRoles);
        Assert.Equal(["Administrators", "CmsAdmins", "WebAdmins"], OptiPowerToolsScheduledJobsInsightsOptions.DefaultAuthorizedRoles);
        Assert.Equal(JobResultSummary.DefaultMaxLength, options.MaxResultSummaryLength);
    }
}
