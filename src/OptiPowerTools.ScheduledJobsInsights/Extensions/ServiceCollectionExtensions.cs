using System.Threading.Channels;
using EPiServer.DataAbstraction;
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
    /// Values bind from the
    /// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.ConfigurationSectionName"/> section.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
    public static IServiceCollection AddOptiPowerToolsScheduledJobsInsights(this IServiceCollection services) =>
        services.AddOptiPowerToolsScheduledJobsInsights(_ => { });

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
    public static IServiceCollection AddOptiPowerToolsScheduledJobsInsights(
        this IServiceCollection services,
        Action<OptiPowerToolsScheduledJobsInsightsOptions> setupAction)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(setupAction);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
        {
            // The guard covers the registrations that must not be duplicated — a second background
            // writer draining a single-reader channel, most of all — but the caller's options are not
            // one of them. Dropping them silently meant a consumer who called this after a shared
            // library had already made a bare call got no configuration and no error: the connection
            // string they set was simply ignored. Configure is additive and ordered, so appending it
            // here gives the later caller the last word, which is what they would expect.
            services.Configure(setupAction);
            return services;
        }

        services.AddSingleton(RegistrationMarker.Instance);

        services.AddOptions<OptiPowerToolsScheduledJobsInsightsOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.GetSection(OptiPowerToolsScheduledJobsInsightsOptions.ConfigurationSectionName).Bind(options);
                setupAction(options);
            })
            // Startup is the last moment a misconfiguration is cheap to notice; every one of these
            // otherwise degrades silently once jobs start running.
            .ValidateOnStart();

        services.TryAddSingleton<IValidateOptions<OptiPowerToolsScheduledJobsInsightsOptions>,
            OptiPowerToolsScheduledJobsInsightsOptionsValidator>();

        services.AddHttpContextAccessor();



        // One named policy for the page, the retention screen and the menu, so all three agree.
        services.AddAuthorization();

        // Gated, because these two reach past this package into the host's own choices:
        // AddServerSideBlazor grafts circuit services into what may be a Blazor Web App, and
        // AddCascadingAuthenticationState registers a provider for the whole application. The hub half
        // of this already had an opt-out; the service half did not.
        if (ResolveOptions(services, setupAction).AddBlazorServices)
        {
            // Blazor Server rather than the Blazor Web App model: the components are hosted inside the
            // CMS shell MVC view via the Component Tag Helper, so nothing here owns a whole HTML page.
            services.AddServerSideBlazor();

            // Lets the retention screen re-check the policy before a destructive write, rather than
            // trusting the authorization that happened when the page was first served.
            services.AddCascadingAuthenticationState();
        }
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AuthorizationOptions>,
            ConfigureScheduledJobsInsightsAuthorization>());

        services.AddSingleton<ScheduledJobsInsightsMenuProvider>();
        services.AddSingleton<IConfigureOptions<MvcOptions>, ConfigureScheduledJobsInsightsMvcOptions>();

        services.AddPooledDbContextFactory<ScheduledJobsInsightsDbContext>((provider, optionsBuilder) =>
        {
            var insightsOptions = provider.GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>().Value;
            optionsBuilder.UseSqlServer(insightsOptions.ConnectionString);

            // The escape hatch for everything this package cannot decide for a host: retry-on-failure,
            // a command timeout, a managed-identity token provider. Applied last so it can override
            // what is set above. For a package whose premise is that its own database may be
            // unreachable, refusing consumers the ability to configure resilience was the wrong
            // default.
            insightsOptions.ConfigureDbContext?.Invoke(optionsBuilder);
        });

        services.AddSingleton(provider =>
        {
            var insightsOptions = provider.GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>().Value;
            return Channel.CreateBounded<JobRecord>(new BoundedChannelOptions(insightsOptions.LogChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = false
            });
        });

        // TryAdd: the host may already have registered one (a test host with a fake clock, say).
        services.TryAddSingleton(TimeProvider.System);

        // Singleton: the type scan is process-lifetime data, since base types and attributes are
        // compiled in. GetService rather than a constructor dependency, because Optimizely's scanner
        // is absent in a host that has not initialised the platform, and the index has a fallback.
        services.AddSingleton(provider => new LoggedJobTypeIndex(
            provider.GetService<EPiServer.Framework.TypeScanner.ITypeScannerLookup>()));
        services.AddSingleton<JobRetentionPolicyStore>();
        services.AddSingleton<RegisteredJobNames>();
        services.AddSingleton<IJobRetentionService, JobRetentionService>();

        // The same instance under its public, cleanup-facing face.
        services.AddSingleton<IJobRetentionPolicySource>(provider => provider.GetRequiredService<IJobRetentionService>());

        // Per-application, so calling both Use... and Map... cannot map the Blazor hub twice.
        services.AddSingleton<HubMappedMarker>();

        services.AddSingleton<IJobExecutionWriter, JobExecutionWriter>();

        // What every logged job takes in its constructor. Transient rather than singleton so it
        // inherits whatever lifetime the host gives IScheduledJobRepository — the same constraint
        // jobs were already under when they took that repository directly. Built by a factory because
        // the constructor is internal: ActivatorUtilities, which AddTransient<T>() uses, needs a
        // public one.
        services.AddTransient(serviceProvider => new JobLoggingContext(
            serviceProvider.GetRequiredService<IJobExecutionWriter>(),
            serviceProvider.GetRequiredService<IScheduledJobRepository>(),
            serviceProvider.GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>()
                .Value.MaxResultSummaryLength,
            serviceProvider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IJobExecutionQueryService, JobExecutionQueryService>();
        services.AddSingleton<ICleanupRepository, CleanupRepository>();
        services.AddHostedService<JobLogBackgroundWriter>();

        return services;
    }

    /// <summary>
    /// The options as configured by this call, for the few registrations that have to branch on them.
    /// </summary>
    /// <remarks>
    /// Registration happens before any service provider exists, so <c>IOptions</c> cannot be resolved
    /// here. Binding a throwaway copy is the only way to read a value that decides *what to register*
    /// rather than how a registered service behaves. Deliberately rare: everything else defers to
    /// <c>IOptions</c> at resolve time, where <c>appsettings.json</c> and validation both apply.
    /// </remarks>
    private static OptiPowerToolsScheduledJobsInsightsOptions ResolveOptions(
        IServiceCollection services,
        Action<OptiPowerToolsScheduledJobsInsightsOptions> setupAction)
    {
        var options = new OptiPowerToolsScheduledJobsInsightsOptions();

        var configuration = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(IConfiguration))?
            .ImplementationInstance as IConfiguration;

        configuration?.GetSection(OptiPowerToolsScheduledJobsInsightsOptions.ConfigurationSectionName).Bind(options);

        // Same precedence as the real binding: configuration first, then code.
        setupAction(options);

        return options;
    }
}
