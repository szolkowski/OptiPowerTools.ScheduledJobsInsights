using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;
using OptiPowerTools.ScheduledJobsInsights.Repositories;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

/// <summary>
/// Shared setup for rendering the two pages under bUnit: a substituted query service, options, and
/// the JS interop the components reach for.
/// </summary>
/// <remarks>
/// <para>
/// Members are <c>internal</c> rather than <c>protected</c> because they traffic in internal types
/// (<see cref="IJobExecutionQueryService"/>, <see cref="JobExecution"/>, <see cref="ExecutionStatus"/>).
/// Derived test classes are in this assembly, so internal reaches them; the class itself stays public
/// so xUnit discovers the tests that inherit it.
/// </para>
/// <para>
/// Substituting <see cref="IJobExecutionQueryService"/> at all depends on the library granting
/// <c>InternalsVisibleTo("DynamicProxyGenAssembly2")</c> — the interface is internal, and NSubstitute
/// emits its proxy into Castle's dynamic assembly rather than into this one.
/// </para>
/// <para>
/// JS interop runs in loose mode. These tests assert on rendered output rather than on interop, and
/// <c>Virtualize</c> does its own interop for the scroll spacers that would otherwise have to be
/// stubbed in every log test. Tests that assert a specific JS call should switch to strict mode and
/// set up the module explicitly.
/// </para>
/// </remarks>
public abstract class ComponentTestBase : BunitContext
{
    protected ComponentTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        QueryService = Substitute.For<IJobExecutionQueryService>();

        // Sensible empties, so a test only configures what it is actually about.
        QueryService.GetMetricsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<JobMetric>>([]));
        QueryService.GetLogEntriesAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<JobLogEntry>>([]));
        QueryService.GetDistinctJobNamesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([]));
        QueryService.GetExecutionsAsync(
                Arg.Any<ExecutionFilter>(), Arg.Any<ExecutionCursor?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExecutionPage([], null, false)));

        Options = new OptiPowerToolScheduledJobsInsightsOptions();

        Services.AddSingleton(QueryService);
        Services.AddSingleton<IOptions<OptiPowerToolScheduledJobsInsightsOptions>>(
            new OptionsWrapper<OptiPowerToolScheduledJobsInsightsOptions>(Options));
    }

    internal IJobExecutionQueryService QueryService { get; }

    internal OptiPowerToolScheduledJobsInsightsOptions Options { get; }

    /// <summary>A fixed instant, so rendered timestamps are assertable rather than relative to now.</summary>
    protected static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a completed, successful execution. Tests override only the field under test.</summary>
    internal static JobExecution AnExecution(
        long id = 1,
        string jobName = "Nightly Import",
        ExecutionStatus status = ExecutionStatus.Succeeded,
        string? resultMessage = "Imported 12 items.",
        string? resultSummary = null,
        string? inputDataJson = null,
        string? exceptionMessage = null,
        string? exceptionStackTrace = null,
        DateTimeOffset? completedAt = null,
        Guid? scheduledJobId = null) =>
        new()
        {
            Id = id,
            ScheduledJobId = scheduledJobId ?? Guid.NewGuid(),
            JobName = jobName,
            JobTypeName = "Contoso.Jobs.NightlyImportJob",
            StartedAt = Noon,
            CompletedAt = status == ExecutionStatus.Running ? null : completedAt ?? Noon.AddSeconds(3),
            Status = status,
            ResultMessage = resultMessage,
            ResultSummary = resultSummary,
            InputDataJson = inputDataJson,
            ExceptionMessage = exceptionMessage,
            ExceptionStackTrace = exceptionStackTrace,
            MachineName = "web-01"
        };

    /// <summary>A summary of <paramref name="lines"/> numbered lines, for the size and collapse rules.</summary>
    protected static string ASummaryOf(int lines) =>
        string.Concat(Enumerable.Range(1, lines).Select(i => $"line {i}\n"));
}
