using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Extensions;

/// <summary>
/// Extension methods for configuring the OptiPowerTools ScheduledJobsInsights middleware pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Maps the Blazor Server hub the ScheduledJobsInsights components connect over.
    /// Call inside <c>UseEndpoints(...)</c>, or on an <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The components themselves are not routable endpoints. They are hosted inside the CMS shell
    /// view (<c>Views/ScheduledJobsInsightsCms/Index.cshtml</c>) through the Component Tag Helper,
    /// so they render within the Optimizely chrome and inherit its styling; this hub is only what
    /// makes them interactive afterwards.
    /// </para>
    /// <para>
    /// A hub mapped here carries the package's authorization policy, so the circuit is guarded in its
    /// own right rather than relying solely on the authorization of the page that served the
    /// markers. When another hub is already mapped, this maps nothing and changes nothing — a shared
    /// hub belongs to the host, and imposing this policy on it would lock out its own components.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <c>null</c>.</exception>
    public static IEndpointRouteBuilder MapOptiPowerToolsScheduledJobsInsights(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>().Value;

        // Guarded per application, not per call. Both public entry points reach here — Use... calls
        // Map... — so a host that calls both would otherwise map the hub twice whenever MapBlazorHub
        // is set to true explicitly, because that bypasses the detection below. Mapping twice is the
        // AmbiguousMatchException on every Blazor request that the detection exists to prevent, and
        // the configuration most likely to hit it is the one someone reaches for while already
        // debugging hub problems.
        if (AlreadyMappedHub(endpoints))
            return endpoints;

        if (options.MapBlazorHub ?? !HasBlazorHub(endpoints))
        {
            endpoints.MapBlazorHub().RequireAuthorization(ScheduledJobsInsightsAuthorization.PolicyName);
            MarkHubMapped(endpoints);
        }

        return endpoints;
    }

    /// <summary>
    /// Whether something has already mapped the Blazor hub.
    /// </summary>
    /// <remarks>
    /// Mapping it twice puts two endpoints on the same route pattern, and every Blazor request in
    /// the application then fails with <c>AmbiguousMatchException</c> — with nothing in the message
    /// naming this package. The same reasoning as the deliberate absence of <c>MapControllers()</c>
    /// below, applied to the one endpoint this package does have to own. A host that maps its hub
    /// *after* this call cannot be detected, which is what
    /// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.MapBlazorHub"/> is for.
    /// </remarks>
    private static bool AlreadyMappedHub(IEndpointRouteBuilder endpoints) =>
        endpoints.ServiceProvider.GetService<HubMappedMarker>()?.Mapped == true;

    private static void MarkHubMapped(IEndpointRouteBuilder endpoints)
    {
        var marker = endpoints.ServiceProvider.GetService<HubMappedMarker>();
        if (marker is not null)
            marker.Mapped = true;
    }

    private static bool HasBlazorHub(IEndpointRouteBuilder endpoints) =>
        endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Any(endpoint => endpoint.RoutePattern.RawText?.Contains("_blazor", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// Applies pending migrations and maps the Blazor Server hub used by the ScheduledJobsInsights UI.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this after <c>UseAuthorization()</c>. Placing it before the application's own
    /// <c>UseEndpoints(...)</c> is recommended so the hub is registered alongside everything else.
    /// </para>
    /// <para>
    /// This deliberately does not call <c>MapControllers()</c>. The host does that, and mapping
    /// controllers from two separate <c>UseEndpoints(...)</c> blocks registers every controller
    /// action twice, which fails at request time with <c>AmbiguousMatchException</c>.
    /// <see cref="Cms.ScheduledJobsInsightsCmsController"/> is found through the usual MVC
    /// application-part discovery, so the host's own <c>MapControllers()</c> picks it up.
    /// </para>
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <c>null</c>.</exception>
    public static IApplicationBuilder UseOptiPowerToolsScheduledJobsInsights(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices
            .GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>().Value;

        if (options.AutoMigrateDatabase)
            TryMigrate(app.ApplicationServices);

        ResolveAuthorizationPolicyEarly(app.ApplicationServices);
        ReportResolvedRetention(app.ApplicationServices, options);
        ReportMissingWebAssets(app.ApplicationServices);
        ReportDuplicateEndpointsWhenStarted(app.ApplicationServices, options);

        app.UseEndpoints(endpoints => endpoints.MapOptiPowerToolsScheduledJobsInsights());

        return app;
    }

    /// <summary>
    /// Forces this package's authorization policy to be built now, so a misconfiguration is reported
    /// at startup rather than at whatever request happens to need authorization first.
    /// </summary>
    /// <remarks>
    /// <see cref="AuthorizationOptions"/> is a single shared instance resolved lazily on the first
    /// authorization decision in the application, which could be hours after a deployment and in a
    /// request that has nothing to do with this package. Touching it here moves the Critical log line
    /// about an unregistered policy next to the rest of the startup output, where somebody will see
    /// it. Nothing is thrown either way — the policy resolves to deny-all, so the pages are closed
    /// rather than open.
    /// </remarks>
    private static void ResolveAuthorizationPolicyEarly(IServiceProvider services)
    {
        try
        {
            _ = services.GetRequiredService<IOptions<AuthorizationOptions>>()
                .Value
                .GetPolicy(ScheduledJobsInsightsAuthorization.PolicyName);
        }
        catch (Exception ex)
        {
            // A host that configures AuthorizationOptions in a way that throws is the host's problem,
            // but it must not become a failure to start caused by this line.
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("OptiPowerTools.ScheduledJobsInsights")
                .LogCritical(ex, "ScheduledJobsInsights could not resolve its authorization policy at startup. Access to the insights pages may be denied.");
        }
    }

    /// <summary>
    /// Runs the startup checks that need the application's endpoints, once those exist.
    /// </summary>
    /// <remarks>
    /// Deferred to <c>ApplicationStarted</c> because endpoints are still being built while
    /// <c>Configure</c> runs, so nothing useful can be counted yet. Best-effort throughout: no
    /// lifetime, no data source or no logger simply means no diagnostic, and anything thrown while
    /// inspecting the host's own endpoints is swallowed to Debug rather than allowed to escape into
    /// startup.
    /// </remarks>
    private static void ReportDuplicateEndpointsWhenStarted(
        IServiceProvider services,
        OptiPowerToolsScheduledJobsInsightsOptions options)
    {
        var lifetime = services.GetService<IHostApplicationLifetime>();

        if (lifetime is null)
            return;

        lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                var endpoints = services.GetService<EndpointDataSource>()?.Endpoints;

                if (endpoints is null || Logger(services) is not { } logger)
                    return;

                StartupDiagnostics.ReportDuplicateEndpoints(endpoints, options, logger);
            }
            catch (Exception ex)
            {
                Logger(services)?.LogDebug(
                    ex, "ScheduledJobsInsights could not inspect the application's endpoints for duplicates.");
            }
        });
    }

    /// <summary>States the resolved installation-wide retention in the startup log.</summary>
    private static void ReportResolvedRetention(
        IServiceProvider services,
        OptiPowerToolsScheduledJobsInsightsOptions options)
    {
        if (Logger(services) is { } logger)
            StartupDiagnostics.ReportResolvedRetention(options, logger);
    }

    /// <summary>Warns when the host application is missing the static web assets the UI needs.</summary>
    private static void ReportMissingWebAssets(IServiceProvider services)
    {
        if (Logger(services) is { } logger)
            StartupDiagnostics.ReportMissingWebAssets(services.GetService<IWebHostEnvironment>()?.WebRootFileProvider, logger);
    }

    /// <summary>The package's own logger, or <c>null</c> if the host has no logging.</summary>
    private static ILogger? Logger(IServiceProvider services) =>
        services.GetService<ILoggerFactory>()?.CreateLogger(StartupDiagnostics.LoggerCategory);

    /// <summary>
    /// Applies pending migrations, logging and continuing if the insights database cannot be reached.
    /// </summary>
    /// <remarks>
    /// Deliberately not fail-fast. An exception here happens inside <c>Configure</c>, so it aborts
    /// application startup — meaning an unreachable *reporting* database stops the entire CMS from
    /// booting. That is the same inversion this package refuses everywhere else in the write path: a
    /// tool that observes scheduled jobs must never be able to prevent them, and a site must not go
    /// down because its execution history is unavailable. Logged at Critical, because the UI and all
    /// recording will be broken until it is resolved and somebody needs to see that.
    /// </remarks>
    private static void TryMigrate(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        try
        {
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ScheduledJobsInsightsDbContext>>();
            using var dbContext = dbContextFactory.CreateDbContext();
            dbContext.Database.Migrate();
        }
        catch (Exception ex)
        {
            scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("OptiPowerTools.ScheduledJobsInsights")
                .LogCritical(
                    ex,
                    "ScheduledJobsInsights could not apply database migrations at startup. The application will continue and scheduled jobs will run normally, but execution history will not be recorded and the insights UI will not work until the database is reachable.");
        }
    }
}
