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
    public void RegisteringTwice_LetsTheLaterCallConfigure()
    {
        // Reversed deliberately. This used to assert that the second call's options were discarded,
        // which encoded the defect rather than a decision: a consumer configuring the package after a
        // shared library had already made a bare call got no configuration and no error. The guard is
        // there to stop a second background writer draining a single-reader channel — which
        // RegisteringTwice_RegistersOneBackgroundWriter still pins — not to ignore the caller.
        // Configure is additive and ordered, so the later call has the last word.
        var services = Registered(options => options.PageTitle = "From the first call");
        services.AddOptiPowerToolsScheduledJobsInsights(options => options.PageTitle = "From the second");

        var provider = services.BuildServiceProvider();

        Assert.Equal(
            "From the second",
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

    [Fact]
    public void ByDefault_BlazorServerServicesAreRegistered()
    {
        var services = Registered();

        Assert.Contains(services, IsBlazorServerRegistration);
    }

    [Fact]
    public void WithAddBlazorServicesFalse_TheHostsOwnBlazorRegistrationsAreLeftAlone()
    {
        // The service-side counterpart to MapBlazorHub. AddServerSideBlazor grafts circuit services
        // into what may be a Blazor Web App, and AddCascadingAuthenticationState registers a provider
        // for the whole application — neither is this package's to decide for a host that has already
        // chosen.
        var services = Registered(options => options.AddBlazorServices = false);

        Assert.DoesNotContain(services, IsBlazorServerRegistration);
        Assert.DoesNotContain(services, IsCascadingAuthenticationStateRegistration);
    }

    [Fact]
    public void WithAddBlazorServicesFalse_EverythingElseIsStillRegistered()
    {
        // The opt-out is about Blazor's own services, not about the package.
        var services = Registered(options => options.AddBlazorServices = false);

        Assert.Contains(services, d => d.ServiceType == typeof(IJobExecutionWriter));
        Assert.Contains(services, d => d.ServiceType == typeof(ICleanupRepository));
    }

    /// <summary>
    /// Whether a descriptor came from <c>AddServerSideBlazor</c>. Matched by name because the circuit
    /// types are internal to ASP.NET Core, and <c>CircuitOptions</c> itself is never a service type —
    /// it arrives through <c>IConfigureOptions</c>.
    /// </summary>
    private static bool IsBlazorServerRegistration(ServiceDescriptor descriptor) =>
        descriptor.ServiceType.FullName?.Contains("Circuit", StringComparison.Ordinal) == true
        || descriptor.ImplementationType?.FullName?.Contains("Circuit", StringComparison.Ordinal) == true;

    /// <summary>Whether a descriptor came from <c>AddCascadingAuthenticationState</c>.</summary>
    private static bool IsCascadingAuthenticationStateRegistration(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName?.Contains("CascadingAuthenticationState", StringComparison.Ordinal) == true;

    [Fact]
    public void ASecondCall_DoesNotUndoTheFirstCallsOtherValues()
    {
        // Additive, not replacing: the later call sets what it names and leaves the rest alone.
        var services = Registered(options => options.ConnectionString = "first");
        services.AddOptiPowerToolsScheduledJobsInsights(options => options.PageTitle = "Second Call");

        var resolved = services.BuildServiceProvider()
            .GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>().Value;

        Assert.Equal("Second Call", resolved.PageTitle);
        Assert.Equal("first", resolved.ConnectionString);
    }

    [Fact]
    public void ASecondCall_StillRegistersOnlyOneBackgroundWriter()
    {
        // The half the guard is actually for: two writers draining a channel created with
        // SingleReader = true is undefined behaviour.
        var services = Registered();
        services.AddOptiPowerToolsScheduledJobsInsights(_ => { });

        Assert.Single(services, d => d.ImplementationType == typeof(JobLogBackgroundWriter));
    }

    [Fact]
    public void AddBlazorServicesFalse_IsHonouredWhenItComesFromFactoryRegisteredConfiguration()
    {
        // The shape every real host uses, and the one shape this was never tested against.
        // WebApplicationBuilder, HostApplicationBuilder and HostBuilder all register IConfiguration
        // through a factory rather than an instance — so an instance-only read came back null, the
        // section was never bound, and AddServerSideBlazor plus AddCascadingAuthenticationState were
        // grafted into a host that had asked in configuration for neither. Silently: the value binds
        // into IOptions correctly, so nothing downstream disagreed.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OptiPowerTools:ScheduledJobsInsights:AddBlazorServices"] = "false"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(_ => configuration);
        services.AddOptiPowerToolsScheduledJobsInsights(options =>
            options.ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;");

        Assert.DoesNotContain(services, IsBlazorServerRegistration);
        Assert.DoesNotContain(services, IsCascadingAuthenticationStateRegistration);

        // And the option really did bind, so the registration decision and IOptions agree.
        var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<IOptions<OptiPowerToolsScheduledJobsInsightsOptions>>().Value.AddBlazorServices);
    }
}
