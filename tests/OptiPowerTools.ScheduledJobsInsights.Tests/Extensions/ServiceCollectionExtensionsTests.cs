using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Extensions;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOptiPowerToolScheduledJobsInsights_RegistersOptionsAndMenuProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddOptiPowerToolScheduledJobsInsights(options =>
        {
            options.PageTitle = "Custom Title";
            options.ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OptiPowerToolScheduledJobsInsightsOptions>>().Value;

        Assert.Equal("Custom Title", options.PageTitle);
        Assert.NotNull(provider.GetRequiredService<ScheduledJobsInsightsMenuProvider>());
    }

    [Fact]
    public void AddOptiPowerToolScheduledJobsInsights_RegistersDataAndLoggingServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddOptiPowerToolScheduledJobsInsights(options =>
            options.ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;");

        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IJobExecutionWriter>());
        Assert.NotNull(provider.GetRequiredService<IJobExecutionQueryService>());
        Assert.NotNull(provider.GetRequiredService<ICleanupRepository>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is JobLogBackgroundWriter);
    }
}
