using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Extensions;

/// <summary>
/// Extension methods for registering OptiPowerTools ScheduledJobsInsights services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Marks the registration as done, so a second call is a no-op rather than a duplicate.</summary>
    private sealed class RegistrationMarker
    {
        /// <summary>The one instance ever needed; registered directly so DI never constructs it.</summary>
        public static readonly RegistrationMarker Instance = new();

        private RegistrationMarker()
        {
        }
    }

    /// <summary>
    /// Adds ScheduledJobsInsights services configured for Optimizely CMS with default options.
    /// Values bind from "OptiPowerTools:ScheduledJobsInsights" in configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection AddOptiPowerToolScheduledJobsInsights(this IServiceCollection services) =>
        services.AddOptiPowerToolScheduledJobsInsights(_ => { });

    /// <summary>
    /// Adds ScheduledJobsInsights services with the specified options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="setupAction">
    /// Applied after configuration binding, so a value set here wins over
    /// <c>appsettings.json</c>.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    /// <remarks>
    /// Calling this more than once is safe: the second call returns without registering anything. It
    /// would otherwise add a second <see cref="JobLogBackgroundWriter"/> draining a channel created
    /// with <c>SingleReader = true</c>, whose behaviour is undefined.
    /// </remarks>
    public static IServiceCollection AddOptiPowerToolScheduledJobsInsights(
        this IServiceCollection services,
        Action<OptiPowerToolScheduledJobsInsightsOptions> setupAction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(setupAction);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
            return services;

        services.AddSingleton(RegistrationMarker.Instance);

        services.AddOptions<OptiPowerToolScheduledJobsInsightsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.GetSection("OptiPowerTools:ScheduledJobsInsights").Bind(options);
                setupAction(options);
            })
            // Startup is the last moment a misconfiguration is cheap to notice; every one of these
            // otherwise degrades silently once jobs start running.
            .ValidateOnStart();

        services.TryAddSingleton<IValidateOptions<OptiPowerToolScheduledJobsInsightsOptions>,
            OptiPowerToolScheduledJobsInsightsOptionsValidator>();

        services.AddHttpContextAccessor();

        // Blazor Server rather than the Blazor Web App model: the components are hosted inside the
        // CMS shell MVC view via the Component Tag Helper, so nothing here owns a whole HTML page.
        services.AddServerSideBlazor();

        // One named policy for the page, the retention screen and the menu, so all three agree.
        services.AddAuthorization();

        // Lets the retention screen re-check the policy before a destructive write, rather than
        // trusting the authorization that happened when the page was first served.
        services.AddCascadingAuthenticationState();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AuthorizationOptions>,
            ConfigureScheduledJobsInsightsAuthorization>());

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

        // TryAdd: the host may already have registered one (a test host with a fake clock, say).
        services.TryAddSingleton(TimeProvider.System);

        // Singleton: the type scan is process-lifetime data, since base types and attributes are compiled in.
        services.AddSingleton<LoggedJobTypeIndex>();
        services.AddSingleton<IJobRetentionService, JobRetentionService>();

        // The same instance under its public, cleanup-facing face.
        services.AddSingleton<IJobRetentionPolicySource>(provider => provider.GetRequiredService<IJobRetentionService>());

        services.AddSingleton<IJobExecutionWriter, JobExecutionWriter>();
        services.AddSingleton<IJobExecutionQueryService, JobExecutionQueryService>();
        services.AddSingleton<ICleanupRepository, CleanupRepository>();
        services.AddHostedService<JobLogBackgroundWriter>();

        return services;
    }
}
