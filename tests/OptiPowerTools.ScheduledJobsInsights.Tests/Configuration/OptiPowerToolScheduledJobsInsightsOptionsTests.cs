using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Configuration;

public class OptiPowerToolScheduledJobsInsightsOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new OptiPowerToolScheduledJobsInsightsOptions();

        Assert.Equal("/ScheduledJobsInsightsCms/Index", options.CmsShellPath);
        Assert.True(options.EnableCmsMenu);
        Assert.True(options.EnableStandardAuthorization);
        Assert.Equal(CmsMenuPlacement.CmsSection, options.MenuPlacement);
        Assert.Contains("Administrators", options.AuthorizedRoles);
    }
}
