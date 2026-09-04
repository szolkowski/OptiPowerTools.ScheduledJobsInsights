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
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions { CmsShellPath = "/custom/shell" });
        var configure = new ConfigureScheduledJobsInsightsMvcOptions(options);
        var mvcOptions = new MvcOptions();

        configure.Configure(mvcOptions);

        var convention = Assert.Single(mvcOptions.Conventions.OfType<ScheduledJobsInsightsCmsRouteConvention>());
        Assert.Equal("/custom/shell", convention.Path);
    }

    [Fact]
    public void Configure_AddsRouteConvention_UsingConfiguredCmsRetentionPath()
    {
        // Both paths have to reach the convention: the retention screen is a second route rather than
        // a query string on the first, so that the CMS menu can highlight it — the shell compares each
        // menu item's URL with the request path and ignores the query string.
        var options = Options.Create(new OptiPowerToolsScheduledJobsInsightsOptions
        {
            CmsShellPath = "/custom/shell",
            CmsRetentionPath = "/custom/retention"
        });
        var mvcOptions = new MvcOptions();

        new ConfigureScheduledJobsInsightsMvcOptions(options).Configure(mvcOptions);

        var convention = Assert.Single(mvcOptions.Conventions.OfType<ScheduledJobsInsightsCmsRouteConvention>());
        Assert.Equal("/custom/retention", convention.RetentionPath);
    }
}
