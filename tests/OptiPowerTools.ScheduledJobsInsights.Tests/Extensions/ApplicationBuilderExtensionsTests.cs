using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Extensions;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Extensions;

/// <summary>
/// Only the fail-fast "options not registered" path is unit-testable here. The
/// <c>AutoMigrateDatabase</c> branch calls <c>Database.Migrate()</c> on a concrete EF Core
/// <c>DbContext</c> (not mockable without introducing a seam solely for this test), and the trailing
/// <c>UseEndpoints</c>/<c>MapControllers</c> call needs a real ASP.NET Core routing/MVC pipeline
/// (<c>AddRouting</c>/<c>AddControllers</c>/<c>AddRazorComponents</c> all wired up) to avoid throwing —
/// effectively an integration test, not a unit test. Same honest-gap tradeoff as
/// <see cref="OptiPowerTools.ScheduledJobsInsights.Tests.Data.SqliteDbContextFactory"/>: covered by
/// running the <c>.Web</c> dev host, not by this suite.
/// </summary>
public class ApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseOptiPowerToolsScheduledJobsInsights_MissingOptions_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        var app = Substitute.For<IApplicationBuilder>();
        app.ApplicationServices.Returns(serviceProvider);

        Assert.Throws<InvalidOperationException>(() => app.UseOptiPowerToolsScheduledJobsInsights());
    }
}
