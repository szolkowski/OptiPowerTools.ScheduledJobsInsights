using EPiServer.DataAbstraction;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Extensions;
using OptiPowerTools.ScheduledJobsInsights.Tests.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Extensions;

/// <summary>
/// The hub-mapping guard, exercised against a real routing pipeline — endpoint inspection is the
/// whole mechanism, so a substituted route builder would prove nothing.
/// </summary>
public class HubMappingTests
{
    private static WebApplication BuildApp(bool? mapBlazorHub, SqliteDbContextFactory database)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<IScheduledJobRepository>());
        builder.Services.AddOptiPowerToolsScheduledJobsInsights(options =>
        {
            options.ConnectionString = "Server=.;Database=NotUsed;Trusted_Connection=True;";
            options.AutoMigrateDatabase = false;
            options.MapBlazorHub = mapBlazorHub;
        });
        builder.Services.AddSingleton<IDbContextFactory<ScheduledJobsInsightsDbContext>>(database);

        var app = builder.Build();

        // As a real host does, and as the README instructs — UseEndpoints requires it.
        app.UseRouting();

        return app;
    }

    private static int BlazorEndpointCount(WebApplication app) =>
        app.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Count(e => e.RoutePattern.RawText?.Contains("_blazor", StringComparison.OrdinalIgnoreCase) == true
                        && e.RoutePattern.RawText?.Contains("negotiate", StringComparison.OrdinalIgnoreCase) == true);

    [Fact]
    public void CallingBothEntryPoints_WithTheHubForcedOn_MapsItOnce()
    {
        // The gap the endpoint check could not close: MapBlazorHub = true is the documented escape
        // hatch for "the host maps its hub after us", and it deliberately bypasses detection — so
        // without a per-application guard, Use... followed by Map... mapped the hub twice and every
        // Blazor request failed with AmbiguousMatchException.
        using var database = new SqliteDbContextFactory();
        using var app = BuildApp(mapBlazorHub: true, database);

        app.UseOptiPowerToolsScheduledJobsInsights();
        app.MapOptiPowerToolsScheduledJobsInsights();

        Assert.Equal(1, BlazorEndpointCount(app));
    }

    [Fact]
    public void WithAutoDetection_TheHubIsStillMappedOnce()
    {
        using var database = new SqliteDbContextFactory();
        using var app = BuildApp(mapBlazorHub: null, database);

        app.UseOptiPowerToolsScheduledJobsInsights();
        app.MapOptiPowerToolsScheduledJobsInsights();

        Assert.Equal(1, BlazorEndpointCount(app));
    }

    [Fact]
    public void WithTheHubSuppressed_NothingIsMapped()
    {
        using var database = new SqliteDbContextFactory();
        using var app = BuildApp(mapBlazorHub: false, database);

        app.UseOptiPowerToolsScheduledJobsInsights();

        Assert.Equal(0, BlazorEndpointCount(app));
    }
}
