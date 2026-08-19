using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;

namespace OptiPowerTools.ScheduledJobsInsights.Extensions;

/// <summary>
/// Extension methods for registering OptiPowerTools ScheduledJobsInsights services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ScheduledJobsInsights services configured for Optimizely CMS with default options.
    /// Values bind from "OptiPowerTools:ScheduledJobsInsights" in configuration.
    /// </summary>
    public static IServiceCollection AddOptiPowerToolScheduledJobsInsights(this IServiceCollection services) =>
        services.AddOptiPowerToolScheduledJobsInsights(_ => { });

    /// <summary>
    /// Adds ScheduledJobsInsights services with the specified options.
    /// </summary>
    public static IServiceCollection AddOptiPowerToolScheduledJobsInsights(
        this IServiceCollection services,
        Action<OptiPowerToolScheduledJobsInsightsOptions> setupAction)
    {
        services.AddOptions<OptiPowerToolScheduledJobsInsightsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.GetSection("OptiPowerTools:ScheduledJobsInsights").Bind(options);
                setupAction(options);
            });

        services.AddHttpContextAccessor();

        // Blazor Server rather than the Blazor Web App model: the components are hosted inside the
        // CMS shell MVC view via the Component Tag Helper, so nothing here owns a whole HTML page.
        services.AddServerSideBlazor();

        services.AddSingleton<ScheduledJobsInsightsMenuProvider>();
        services.AddSingleton<IConfigureOptions<MvcOptions>, ConfigureScheduledJobsInsightsMvcOptions>();

        services.AddPooledDbContextFactory<ScheduledJobsInsightsDbContext>((provider, optionsBuilder) =>
        {
            var insightsOptions = provider.GetRequiredService<IOptions<OptiPowerToolScheduledJobsInsightsOptions>>().Value;
            optionsBuilder.UseSqlServer(insightsOptions.ConnectionString);
        });

        services.AddSingleton(provider =>
        {
            var insightsOptions = provider.GetRequiredService<IOptions<OptiPowerToolScheduledJobsInsightsOptions>>().Value;
            return Channel.CreateBounded<JobRecord>(new BoundedChannelOptions(insightsOptions.LogChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false
            });
        });

        services.AddSingleton<IJobExecutionWriter, JobExecutionWriter>();
        services.AddSingleton<IJobExecutionQueryService, JobExecutionQueryService>();
        services.AddSingleton<ICleanupRepository, CleanupRepository>();
        services.AddHostedService<JobLogBackgroundWriter>();

        return services;
    }
}
