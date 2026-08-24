using System.Threading.Channels;
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
    public void AddOptiPowerToolsScheduledJobsInsights_RegistersOptionsAndMenuProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddOptiPowerToolsScheduledJobsInsights(options =>
        {
            options.PageTitle = "Custom Title";
            options.ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;";
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>().Value;

        Assert.Equal("Custom Title", options.PageTitle);
        Assert.NotNull(provider.GetRequiredService<ScheduledJobsInsightsMenuProvider>());
    }

    /// <summary>A collection with the package registered and nothing else of interest.</summary>
    private static ServiceCollection Registered(Action<OptiPowerToolsScheduledJobsInsightsOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddOptiPowerToolsScheduledJobsInsights(options =>
        {
            options.ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;";
            configure?.Invoke(options);
        });
        return services;
    }

    private static ServiceLifetime LifetimeOf<TService>(IServiceCollection services) =>
        services.Single(descriptor => descriptor.ServiceType == typeof(TService)).Lifetime;

    [Fact]
    public void AddOptiPowerToolsScheduledJobsInsights_RegistersDataAndLoggingServices()
    {
        var provider = Registered().BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IJobExecutionWriter>());
        Assert.NotNull(provider.GetRequiredService<IJobExecutionQueryService>());
        Assert.NotNull(provider.GetRequiredService<ICleanupRepository>());
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is JobLogBackgroundWriter);
    }

    [Fact]
    public void TheWriterAndItsChannel_AreSingletons()
    {
        // The lifetime is the contract here, not the resolution. The writer holds the channel that
        // the single background reader drains; a transient writer would hand each job its own.
        var services = Registered();

        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf<IJobExecutionWriter>(services));
        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf<Channel<JobRecord>>(services));
        Assert.Equal(ServiceLifetime.Singleton, LifetimeOf<IJobExecutionQueryService>(services));
    }

    [Fact]
    public void TheLoggingContext_IsTransient()
    {
        // Jobs are constructed per execution and the context is what they take, so it must not
        // outlive whatever lifetime the host gives IScheduledJobRepository.
        Assert.Equal(ServiceLifetime.Transient, LifetimeOf<JobLoggingContext>(Registered()));
    }

    [Fact]
    public void RegisteringTwice_DoesNotProduceTwoBackgroundWriters()
    {
        // Plausible with a shared bootstrap library plus Program.cs. Two writers would drain one
        // channel created with SingleReader = true, whose behaviour is undefined.
        var services = Registered();
        services.AddOptiPowerToolsScheduledJobsInsights(options =>
            options.ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;");

        var provider = services.BuildServiceProvider();

        Assert.Single(provider.GetServices<IHostedService>().OfType<JobLogBackgroundWriter>());
    }

    [Fact]
    public void RegisteringTwice_KeepsTheFirstCallsOptions()
    {
        // A second call is a no-op in full: it must not silently re-apply defaults over the values
        // the first one set.
        var services = Registered(options => options.PageTitle = "From the first call");
        services.AddOptiPowerToolsScheduledJobsInsights(options => options.PageTitle = "From the second");

        var provider = services.BuildServiceProvider();

        Assert.Equal(
            "From the first call",
            provider.GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>().Value.PageTitle);
    }

    [Fact]
    public void AnInvalidConfiguration_FailsAtStartupRatherThanAtFirstUse()
    {
        // ValidateOnStart is the point: every one of these degrades silently once jobs are running,
        // and the person who can fix it is looking at the console right now.
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddOptiPowerToolsScheduledJobsInsights(options =>
        {
            options.ConnectionString = string.Empty;
            options.LogBatchSize = 0;
        });

        var provider = services.BuildServiceProvider();
        var startupValidator = provider.GetServices<IStartupValidator>().Single();

        var exception = Assert.Throws<OptionsValidationException>(startupValidator.Validate);

        Assert.Contains("ConnectionString", exception.Message, StringComparison.Ordinal);
        Assert.Contains("LogBatchSize", exception.Message, StringComparison.Ordinal);
    }
}
