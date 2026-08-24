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
    /// Warns, once the application has started, if this package's endpoints resolve more than once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Duplicate endpoints surface as <c>AmbiguousMatchException</c> at request time, from
    /// <c>DefaultEndpointSelector</c>, naming two identical-looking candidates and nothing else. The
    /// exception arrives during endpoint <em>matching</em> — before authentication — so it is not
    /// even necessarily a logged-in administrator who finds it, and nothing in it points at this
    /// package or at the wiring that caused it.
    /// </para>
    /// <para>
    /// Two wirings are known to cause it on an Optimizely host, and both were reported from a real
    /// site. Calling <c>UseOptiPowerToolsScheduledJobsInsights()</c> <em>before</em> the host's own
    /// <c>UseEndpoints(...)</c> publishes the hub, and <c>MapContent()</c> then consolidates the
    /// already-published source into its own snapshot — so the hub is registered twice. Separately,
    /// on some stacks (Commerce was the reported one) <c>MapContent()</c> already maps attribute-routed
    /// controllers, and an additional <c>MapControllers()</c> duplicates every one of them, this
    /// package's page included. Neither is detectable while <c>Configure</c> is still running, which
    /// is why this waits for <c>ApplicationStarted</c>.
    /// </para>
    /// <para>
    /// Reported rather than thrown. The endpoints belong to the host, this package only observes,
    /// and a site that is limping is better than a site that will not boot — the same rule the write
    /// path follows. Everything here is best-effort: no data source, no message.
    /// </para>
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

                if (endpoints is null)
                    return;

                if (services.GetService<ILoggerFactory>()?.CreateLogger("OptiPowerTools.ScheduledJobsInsights") is not { } logger)
                    return;

                var page = endpoints
                    .OfType<RouteEndpoint>()
                    .Count(endpoint => string.Equals(
                        "/" + endpoint.RoutePattern.RawText?.TrimStart('/'),
                        "/" + options.CmsShellPath.TrimStart('/'),
                        StringComparison.OrdinalIgnoreCase));

                if (page > 1)
                {
                    logger.LogError(
                        "ScheduledJobsInsights' page is registered {Count} times at {Path}, so every request to it fails with AmbiguousMatchException. The usual cause is calling both MapContent() and MapControllers(): on some Optimizely stacks MapContent() already maps attribute-routed controllers, and the second call duplicates all of them. Remove MapControllers() if your host is one of those — but check first, because on a plain CMS host it is required and removing it makes this page return 404 instead.",
                        page,
                        options.CmsShellPath);
                }

                var hub = endpoints
                    .OfType<RouteEndpoint>()
                    .Count(endpoint => endpoint.RoutePattern.RawText?.Equals("_blazor", StringComparison.OrdinalIgnoreCase) == true
                        || endpoint.RoutePattern.RawText?.Equals("/_blazor", StringComparison.OrdinalIgnoreCase) == true);

                if (hub > 1)
                {
                    logger.LogError(
                        "The Blazor hub is registered {Count} times at /_blazor, so every Blazor request in this application fails with AmbiguousMatchException — not only this package's pages. Map the hub inside the host's own UseEndpoints(...) block with endpoints.MapOptiPowerToolsScheduledJobsInsights(), placed before MapContent(), and keep UseOptiPowerToolsScheduledJobsInsights() after that block for migrations. If the host maps its own hub, set MapBlazorHub to false.",
                        hub);
                }
            }
            catch (Exception ex)
            {
                services.GetService<ILoggerFactory>()?
                    .CreateLogger("OptiPowerTools.ScheduledJobsInsights")
                    .LogDebug(ex, "ScheduledJobsInsights could not inspect the application's endpoints for duplicates.");
            }
        });
    }

    /// <summary>
    /// States the resolved installation-wide retention in the startup log.
    /// </summary>
    /// <remarks>
    /// <c>RetentionDays</c> is the one sizing option the validator cannot reject, because zero or less
    /// is a documented, supported value meaning "keep indefinitely". That makes a typo
    /// indistinguishable from an intention: somebody writing <c>-30</c> for "thirty days" gets
    /// unbounded growth, silently, and the symptom shows up months later as a table nothing trims.
    /// Saying which of the two was resolved costs one line at startup. A negative value is called out
    /// separately from zero — zero is the documented way to ask for indefinite, a negative number is
    /// almost always a mistake.
    /// </remarks>
    private static void ReportResolvedRetention(
        IServiceProvider services,
        OptiPowerToolsScheduledJobsInsightsOptions options)
    {
        if (services.GetService<ILoggerFactory>()?.CreateLogger("OptiPowerTools.ScheduledJobsInsights") is not { } logger)
            return;

        if (options.RetentionDays > 0)
        {
            logger.LogInformation(
                "ScheduledJobsInsights default retention: {RetentionDays} day(s). Jobs with a rule of their own are unaffected.",
                options.RetentionDays);
        }
        else if (options.RetentionDays == 0)
        {
            logger.LogInformation(
                "ScheduledJobsInsights default retention is indefinite (RetentionDays = 0), so the default sweep trims nothing. Only jobs with a rule of their own will be trimmed.");
        }
        else
        {
            logger.LogWarning(
                "ScheduledJobsInsights default retention is indefinite because RetentionDays is {RetentionDays}, which is negative. Zero is the documented way to ask for indefinite retention; if you meant a number of days, set a positive value — as written, execution history will grow without bound.",
                options.RetentionDays);
        }
    }

    /// <summary>
    /// Warns when the host application is missing the static web assets this package's UI needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one hosting requirement a consumer owns and can silently get wrong. <c>blazor.server.js</c>
    /// comes from <c>Microsoft.AspNetCore.App.Internal.Assets</c>, which the Web SDK pulls in only
    /// when <c>RequiresAspNetWebAssets</c> is set — and it sets it automatically only when the
    /// *application* contains <c>.razor</c> files. This package's live in the package, so a consumer
    /// whose own project has none never gets it. This package cannot set it for them either: NuGet
    /// resolves that implicit reference during restore, before package MSBuild assets are imported.
    /// </para>
    /// <para>
    /// The failure mode is silent and hard to place: the script 404s, the circuit never starts, the
    /// page stays as prerendered HTML, and the log viewer renders as an empty black box. Nothing
    /// errors. One line at startup turns that into a five-second diagnosis.
    /// </para>
    /// <para>
    /// Warning rather than Critical, and skipped entirely when the file provider cannot be reached:
    /// this is a probe of the host's asset pipeline, and a diagnostic that cried wolf on a
    /// correctly-configured host would be worse than no diagnostic at all.
    /// </para>
    /// </remarks>
    private static void ReportMissingWebAssets(IServiceProvider services)
    {
        if (services.GetService<IWebHostEnvironment>()?.WebRootFileProvider is not { } fileProvider)
            return;

        if (fileProvider.GetFileInfo("_framework/blazor.server.js").Exists)
            return;

        services.GetService<ILoggerFactory>()?
            .CreateLogger("OptiPowerTools.ScheduledJobsInsights")
            .LogWarning(
                "ScheduledJobsInsights could not find _framework/blazor.server.js among the application's static web assets. Its pages will render but will not become interactive: the Blazor circuit cannot start, and the log viewer will appear empty. Add <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets> to the hosting application's project file and rebuild.");
    }

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
