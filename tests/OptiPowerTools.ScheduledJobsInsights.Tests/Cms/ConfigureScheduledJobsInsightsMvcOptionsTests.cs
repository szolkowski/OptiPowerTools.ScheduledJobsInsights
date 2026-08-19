using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ConfigureScheduledJobsInsightsMvcOptionsTests
{
    [Fact]
    public void Configure_AddsRouteConvention_UsingConfiguredCmsShellPath()
    {
        var options = Options.Create(new OptiPowerToolScheduledJobsInsightsOptions { CmsShellPath = "/custom/shell" });
        var configure = new ConfigureScheduledJobsInsightsMvcOptions(options);
        var mvcOptions = new MvcOptions();

        configure.Configure(mvcOptions);

        var convention = Assert.Single(mvcOptions.Conventions.OfType<ScheduledJobsInsightsCmsRouteConvention>());
        Assert.Equal("/custom/shell", convention.Path);
    }
}
