using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
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

    /// <summary>Captures everything written through an <see cref="ILoggerFactory"/>.</summary>
    private sealed class CapturingLoggerProvider(List<(LogLevel Level, string Message)> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capturing(sink);

        public void Dispose() { }

        private sealed class Capturing(List<(LogLevel, string)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> fmt) =>
                sink.Add((level, fmt(state, ex)));
        }
    }

    /// <summary>An application lifetime whose started-token this test controls.</summary>
    private sealed class TestLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }

        public void SignalStarted() => _started.Cancel();
    }

    private sealed class StubEndpointDataSource(IReadOnlyList<Endpoint> endpoints) : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints => endpoints;

        public override IChangeToken GetChangeToken() => new NeverChanges();

        private sealed class NeverChanges : IChangeToken
        {
            public bool HasChanged => false;
            public bool ActiveChangeCallbacks => false;
            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new Noop();
            private sealed class Noop : IDisposable { public void Dispose() { } }
        }
    }

    private static RouteEndpoint Route(string pattern) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, EndpointMetadataCollection.Empty, pattern);

    /// <summary>
    /// Runs the parts of <c>Use…</c> that precede its <c>UseEndpoints</c> call, which needs a real
    /// routing pipeline and is covered by running the dev host instead.
    /// </summary>
    private static (List<(LogLevel Level, string Message)> Log, TestLifetime Lifetime) RunStartup(
        IReadOnlyList<Endpoint> endpoints)
    {
        var log = new List<(LogLevel, string)>();
        var lifetime = new TestLifetime();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(new CapturingLoggerProvider(log)).SetMinimumLevel(LogLevel.Trace));
        // The options are bound through Configure<IConfiguration>, so one has to be resolvable.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        services.AddSingleton<EndpointDataSource>(new StubEndpointDataSource(endpoints));
        services.AddOptiPowerToolsScheduledJobsInsights(options =>
        {
            options.ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;";
            // Nothing here should touch a database.
            options.AutoMigrateDatabase = false;
        });

        var app = Substitute.For<IApplicationBuilder>();
        app.ApplicationServices.Returns(services.BuildServiceProvider());

        // UseEndpoints needs UseRouting to have run; everything under test happens before it.
        try
        {
            app.UseOptiPowerToolsScheduledJobsInsights();
        }
        catch (InvalidOperationException)
        {
        }

        return (log, lifetime);
    }

    [Fact]
    public void UseOptiPowerToolsScheduledJobsInsights_ReportsTheResolvedRetentionAtStartup()
    {
        var (log, _) = RunStartup([]);

        Assert.Contains(log, entry =>
            entry.Level == LogLevel.Information &&
            entry.Message.Contains("default retention", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDuplicateEndpointCheck_DoesNotRunUntilTheApplicationHasStarted()
    {
        // Endpoints are still being built while Configure runs, so counting them there finds nothing
        // and would report a clean bill of health on a broken host.
        var duplicated = new Endpoint[] { Route("/_blazor"), Route("/_blazor") };

        var (log, lifetime) = RunStartup(duplicated);

        Assert.DoesNotContain(log, entry => entry.Message.Contains("is registered", StringComparison.Ordinal));

        lifetime.SignalStarted();

        Assert.Contains(log, entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("_blazor", StringComparison.Ordinal));
    }

    [Fact]
    public void OnACleanlyWiredHost_TheStartedCheckReportsNothing()
    {
        var (log, lifetime) = RunStartup([Route("/_blazor"), Route("/ScheduledJobsInsightsCms/Index")]);

        lifetime.SignalStarted();

        Assert.DoesNotContain(log, entry => entry.Level == LogLevel.Error);
    }

    private sealed class ThrowingEndpointDataSource : EndpointDataSource
    {
        public override IReadOnlyList<Endpoint> Endpoints => throw new InvalidOperationException("endpoints unavailable");

        public override IChangeToken GetChangeToken() => throw new InvalidOperationException("no token");
    }

    /// <summary>Builds the same harness as <see cref="RunStartup"/> with individual pieces removed.</summary>
    private static List<(LogLevel Level, string Message)> RunStartupWith(
        bool withLifetime,
        EndpointDataSource? dataSource,
        out TestLifetime? lifetime)
    {
        var log = new List<(LogLevel, string)>();
        var created = withLifetime ? new TestLifetime() : null;
        lifetime = created;

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(new CapturingLoggerProvider(log)).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        if (created is not null)
            services.AddSingleton<IHostApplicationLifetime>(created);

        if (dataSource is not null)
            services.AddSingleton<EndpointDataSource>(dataSource);

        services.AddOptiPowerToolsScheduledJobsInsights(options =>
        {
            options.ConnectionString = "Server=localhost;Database=Test;Trusted_Connection=True;";
            options.AutoMigrateDatabase = false;
        });

        var app = Substitute.For<IApplicationBuilder>();
        app.ApplicationServices.Returns(services.BuildServiceProvider());

        try
        {
            app.UseOptiPowerToolsScheduledJobsInsights();
        }
        catch (InvalidOperationException)
        {
        }

        return log;
    }

    [Fact]
    public void WithNoApplicationLifetime_TheEndpointCheckIsSkippedRatherThanFailing()
    {
        // A host that has no lifetime to hook is a host this check cannot run on. That is not an
        // error, and the rest of startup must be unaffected.
        var log = RunStartupWith(withLifetime: false, dataSource: null, out _);

        Assert.Contains(log, entry => entry.Message.Contains("default retention", StringComparison.Ordinal));
        Assert.DoesNotContain(log, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void WithNoEndpointDataSource_TheStartedCheckReportsNothing()
    {
        var log = RunStartupWith(withLifetime: true, dataSource: null, out var lifetime);

        lifetime!.SignalStarted();

        Assert.DoesNotContain(log, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void WhenInspectingEndpointsThrows_ItIsSwallowedToDebug_NotIntoStartup()
    {
        // The endpoints belong to the host. A diagnostic that took the application down while looking
        // at them would be worse than the fault it exists to describe — this package observes, and
        // that rule does not stop applying because the code is only a warning.
        var log = RunStartupWith(withLifetime: true, dataSource: new ThrowingEndpointDataSource(), out var lifetime);

        var thrown = Record.Exception(() => lifetime!.SignalStarted());

        Assert.Null(thrown);
        Assert.Contains(log, entry =>
            entry.Level == LogLevel.Debug &&
            entry.Message.Contains("could not inspect", StringComparison.Ordinal));
        Assert.DoesNotContain(log, entry => entry.Level == LogLevel.Error);
    }
}
