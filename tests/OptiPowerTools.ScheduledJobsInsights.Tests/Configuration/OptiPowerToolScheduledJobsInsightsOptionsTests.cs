using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Configuration;

public class OptiPowerToolScheduledJobsInsightsOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new OptiPowerToolScheduledJobsInsightsOptions();

        Assert.Equal("/ScheduledJobsInsightsCms/Index", options.CmsShellPath);
        Assert.True(options.EnableCmsMenu);
        Assert.False(options.AllowAnyAuthenticatedUser);
        Assert.Null(options.AuthorizationPolicy);
        Assert.Null(options.MapBlazorHub);
        Assert.Equal(CmsMenuPlacement.CmsSection, options.MenuPlacement);
        Assert.Contains("Administrators", options.AuthorizedRoles);
        Assert.Equal(JobResultSummary.DefaultMaxLength, options.MaxResultSummaryLength);
    }
}
