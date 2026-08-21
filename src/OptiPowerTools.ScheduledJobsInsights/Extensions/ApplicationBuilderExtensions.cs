using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public static IEndpointRouteBuilder MapOptiPowerToolScheduledJobsInsights(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<OptiPowerToolScheduledJobsInsightsOptions>>().Value;

        if (options.MapBlazorHub ?? !HasBlazorHub(endpoints))
            endpoints.MapBlazorHub().RequireAuthorization(ScheduledJobsInsightsAuthorization.PolicyName);

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
    /// <see cref="OptiPowerToolScheduledJobsInsightsOptions.MapBlazorHub"/> is for.
    /// </remarks>
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
    public static IApplicationBuilder UseOptiPowerToolScheduledJobsInsights(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices
            .GetRequiredService<IOptions<OptiPowerToolScheduledJobsInsightsOptions>>().Value;

        if (options.AutoMigrateDatabase)
            TryMigrate(app.ApplicationServices);

        app.UseEndpoints(endpoints => endpoints.MapOptiPowerToolScheduledJobsInsights());

        return app;
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
