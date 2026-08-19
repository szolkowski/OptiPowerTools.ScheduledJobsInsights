using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// Configures MVC options to register the <see cref="ScheduledJobsInsightsCmsRouteConvention"/>
/// using the resolved <see cref="OptiPowerToolScheduledJobsInsightsOptions.CmsShellPath"/>.
/// </summary>
internal sealed class ConfigureScheduledJobsInsightsMvcOptions : IConfigureOptions<MvcOptions>
{
    private readonly OptiPowerToolScheduledJobsInsightsOptions _options;

    public ConfigureScheduledJobsInsightsMvcOptions(IOptions<OptiPowerToolScheduledJobsInsightsOptions> options) =>
        _options = options.Value;

    public void Configure(MvcOptions mvcOptions) =>
        mvcOptions.Conventions.Add(new ScheduledJobsInsightsCmsRouteConvention(_options.CmsShellPath));
}
