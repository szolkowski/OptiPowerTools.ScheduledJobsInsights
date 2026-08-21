using EPiServer.DataAbstraction;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Everything <see cref="LoggedScheduledJobBase"/> needs in order to record an execution, bundled
/// into the single argument a derived job forwards to <c>base(context)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The bundle exists so the base class can gain collaborators without breaking every job that
/// derives from it. A constructor taking the individual services would be repeated verbatim in every
/// consumer's codebase, which would make adding so much as a clock a breaking change; adding a
/// property here is not.
/// </para>
/// <para>
/// Resolved from DI — Optimizely constructs job instances through
/// <c>ActivatorUtilities.GetServiceOrCreateInstance</c>, so a derived job simply declares it as a
/// constructor parameter alongside anything else it needs.
/// </para>
/// </remarks>
public sealed class JobLoggingContext
{
    /// <summary>Initializes a new instance of <see cref="JobLoggingContext"/>.</summary>
    /// <param name="writer">Sink that persists executions, log lines and metrics.</param>
    /// <param name="scheduledJobRepository">Used to resolve a job's display name from its id.</param>
    /// <param name="options">Package options; supplies the result summary bound.</param>
    /// <param name="timeProvider">Clock, so job timing is testable.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public JobLoggingContext(
        IJobExecutionWriter writer,
        IScheduledJobRepository scheduledJobRepository,
        IOptions<OptiPowerToolScheduledJobsInsightsOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(scheduledJobRepository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Writer = writer;
        ScheduledJobRepository = scheduledJobRepository;
        TimeProvider = timeProvider;
        MaxResultSummaryLength = options.Value.MaxResultSummaryLength > 0
            ? options.Value.MaxResultSummaryLength
            : JobResultSummary.DefaultMaxLength;
    }

    /// <summary>Sink that persists executions, log lines and metrics.</summary>
    public IJobExecutionWriter Writer { get; }

    /// <summary>Used to resolve a job's display name from its scheduled job id.</summary>
    public IScheduledJobRepository ScheduledJobRepository { get; }

    /// <summary>Clock used for job timing.</summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Character bound applied to <see cref="LoggedScheduledJobBase.Summary"/>, from
    /// <see cref="OptiPowerToolScheduledJobsInsightsOptions.MaxResultSummaryLength"/>. Always
    /// positive: a non-positive configured value falls back to
    /// <see cref="JobResultSummary.DefaultMaxLength"/>.
    /// </summary>
    public int MaxResultSummaryLength { get; }
}
