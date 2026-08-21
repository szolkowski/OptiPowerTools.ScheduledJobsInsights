using EPiServer.DataAbstraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data;
using OptiPowerTools.ScheduledJobsInsights.Extensions;
using OptiPowerTools.ScheduledJobsInsights.Logging;
using OptiPowerTools.ScheduledJobsInsights.Repositories;
using OptiPowerTools.ScheduledJobsInsights.Retention;
using OptiPowerTools.ScheduledJobsInsights.Tests.Data;
using OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests;

/// <summary>
/// The package as a consumer meets it: register the services, let Optimizely build a job the way it
/// really does, run it, and read the history back.
/// </summary>
/// <remarks>
/// Everything else here tests a class with its collaborators substituted. That leaves the one claim
/// the README actually makes — "derive from <see cref="LoggedScheduledJobBase"/> and every run is
/// recorded" — resting on the assumption that the DI graph, the constructor Optimizely invokes, the
/// buffered writer and the query service all fit together. This is the test that asserts they do.
/// </remarks>
public class ConsumerIntegrationTests
{
    /// <summary>A job as a consumer would write one: derive, take the context, do work.</summary>
    private sealed class ConsumerJob : LoggedScheduledJobBase
    {
        private readonly IGreetingService _greetings;

        public ConsumerJob(JobLoggingContext context, IGreetingService greetings)
            : base(context)
        {
            _greetings = greetings;
        }

        protected override string ExecuteJob()
        {
            LogInputData(new { Mode = "Incremental" });
            OnStatusChanged("Working");
            Log(_greetings.Greeting(), LogSeverity.Success);
            RecordMetric("ItemsProcessed", 12, "items");
            Summary.AppendLine("Processed 12 items.");
            return "Processed 12 items.";
        }
    }

    /// <summary>A consumer's own dependency, to prove ordinary DI still works alongside the context.</summary>
    private interface IGreetingService
    {
        string Greeting();
    }

    private sealed class GreetingService : IGreetingService
    {
        public string Greeting() => "hello from a real job";
    }

    private static ServiceProvider BuildConsumerHost(SqliteDbContextFactory database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(Substitute.For<IScheduledJobRepository>());
        services.AddSingleton<IGreetingService, GreetingService>();

        services.AddOptiPowerToolScheduledJobsInsights(options =>
        {
            options.ConnectionString = "Server=.;Database=NotUsed;Trusted_Connection=True;";
            options.LogFlushInterval = TimeSpan.FromMilliseconds(10);
        });

        // The one substitution: the package registers a SQL Server context factory, and there is no
        // SQL Server here. Everything else is exactly what a consumer's container holds.
        services.AddSingleton<IDbContextFactory<ScheduledJobsInsightsDbContext>>(database);

        // Scope validation on, as ASP.NET does in Development: resolving a singleton of ours that
        // holds a scoped service fails here rather than in somebody's application.
        //
        // ValidateOnBuild stays off. It eagerly validates every descriptor in the container,
        // including Blazor Server's own, which need an IWebHostEnvironment a bare ServiceCollection
        // does not have — it would fail on Microsoft's registrations, not ours.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task AJobBuiltTheWayOptimizelyBuildsOne_RecordsItsWholeRun()
    {
        using var database = new SqliteDbContextFactory();
        await using var provider = BuildConsumerHost(database);

        // Exactly how EPiServer.Scheduler.Internal.DefaultScheduledJobFactory constructs a job: no
        // registration of the job type itself, just the container plus its constructor.
        var job = ActivatorUtilities.GetServiceOrCreateInstance<ConsumerJob>(provider);

        var result = job.Execute();

        // Log lines and metrics are buffered; the hosted service is what writes them.
        var backgroundWriter = provider.GetServices<IHostedService>().OfType<JobLogBackgroundWriter>().Single();
        await backgroundWriter.StartAsync(CancellationToken.None);
        await backgroundWriter.StopAsync(CancellationToken.None);

        Assert.Equal("Processed 12 items.", result);

        var query = provider.GetRequiredService<IJobExecutionQueryService>();
        var page = await query.GetExecutionsAsync(new ExecutionFilter(), after: null, pageSize: 10);
        var listed = Assert.Single(page.Items);

        Assert.Equal(ExecutionStatus.Succeeded, listed.Status);
        Assert.Equal("Processed 12 items.", listed.ResultMessage);
        Assert.True(listed.HasResultSummary);

        var execution = await query.GetExecutionAsync(listed.Id);
        Assert.Contains("Processed 12 items.", execution!.ResultSummary!, StringComparison.Ordinal);
        Assert.Contains("Incremental", execution.InputDataJson!, StringComparison.Ordinal);

        var log = await query.GetLogEntriesAsync(listed.Id);
        // The status change and the job's own line, in the order they happened.
        Assert.Equal(["Working", "hello from a real job"], log.Select(entry => entry.Message));
        Assert.Equal(LogEntrySource.StatusChanged, log[0].Source);

        var metrics = await query.GetMetricsAsync(listed.Id);
        Assert.Contains(metrics, metric => metric.Name == "ItemsProcessed" && metric.Value == 12);
        Assert.Contains(metrics, metric => metric.Name == JobMetricNames.DurationMs);
    }

    [Fact]
    public async Task TheCleanupJob_IsAlsoConstructibleFromTheContainer()
    {
        // It is discovered by Optimizely like any other [ScheduledJob], so it goes through the same
        // path — and every type in its constructor has to be resolvable for that to work.
        using var database = new SqliteDbContextFactory();
        await using var provider = BuildConsumerHost(database);

        var job = ActivatorUtilities.GetServiceOrCreateInstance<OptiPowerTools.ScheduledJobsInsights.Jobs.ScheduledJobsInsightsCleanupJob>(provider);

        Assert.Equal("Deleted 0 job execution(s).", job.Execute());
    }

    [Fact]
    public void TheServicesAConsumerTouches_AreAllResolvable()
    {
        using var database = new SqliteDbContextFactory();
        using var provider = BuildConsumerHost(database);

        Assert.NotNull(provider.GetRequiredService<JobLoggingContext>());
        Assert.NotNull(provider.GetRequiredService<IJobExecutionWriter>());
        Assert.NotNull(provider.GetRequiredService<ICleanupRepository>());
        Assert.NotNull(provider.GetRequiredService<IJobRetentionPolicySource>());
    }

    [Fact]
    public void TheMenuProvider_DoesNotCaptureAScopedService()
    {
        // Optimizely registers menu providers as singletons and IAuthorizationService is scoped, so
        // taking one in the constructor is a captive dependency: the scoped service, and everything
        // it captured, alive for the process. Under Development's scope validation it does not merely
        // leak — the application fails to start.
        using var database = new SqliteDbContextFactory();
        using var provider = BuildConsumerHost(database);

        Assert.NotNull(provider.GetRequiredService<ScheduledJobsInsightsMenuProvider>());
    }

    [Fact]
    public void TheRetentionSourceAndTheScreenService_AreOneInstance()
    {
        // Deliberate: the public, cleanup-facing face and the internal, screen-facing one are the
        // same object, so a cache or scan inside it is not paid for twice.
        using var database = new SqliteDbContextFactory();
        using var provider = BuildConsumerHost(database);

        Assert.Same(
            provider.GetRequiredService<IJobRetentionPolicySource>(),
            provider.GetRequiredService<IJobRetentionService>());
    }
}
