using EPiServer.DataAbstraction;
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
    /// <summary>
    /// Builds a context. Internal: in production DI supplies it, and a consumer wanting one for a
    /// unit test uses <see cref="ForWriter"/>.
    /// </summary>
    /// <remarks>
    /// Kept off the public surface so collaborators can be added here later without breaking anyone.
    /// A public constructor would have frozen its own parameter list at 1.0, which is the very problem
    /// this type exists to solve — it would just have moved it one level up.
    /// </remarks>
    /// <param name="writer">Sink that persists executions, log lines and metrics.</param>
    /// <param name="scheduledJobRepository">
    /// Job-name lookup, or <c>null</c> when there is no CMS to ask — then the job's type name is used.
    /// </param>
    /// <param name="maxResultSummaryLength">
    /// Summary bound; a non-positive value falls back to <see cref="JobResultSummary.DefaultMaxLength"/>.
    /// </param>
    /// <param name="timeProvider">Clock, so job timing is testable.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="writer"/> or <paramref name="timeProvider"/> is <c>null</c>.
    /// </exception>
    internal JobLoggingContext(
        IJobExecutionWriter writer,
        IScheduledJobRepository? scheduledJobRepository,
        int maxResultSummaryLength,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(timeProvider);

        Writer = writer;
        ScheduledJobRepository = scheduledJobRepository;
        TimeProvider = timeProvider;
        MaxResultSummaryLength = maxResultSummaryLength > 0
            ? maxResultSummaryLength
            : JobResultSummary.DefaultMaxLength;
    }

    /// <summary>
    /// Builds a context around a writer of your own, for unit-testing a job derived from
    /// <see cref="LoggedScheduledJobBase"/>. Not needed in production, where DI supplies the context.
    /// </summary>
    /// <param name="writer">
    /// The sink the job under test will record to — typically a fake or a mock, which is the point.
    /// </param>
    /// <param name="scheduledJobRepository">
    /// Job-name lookup. Omit it unless the test asserts on the resolved name; the job's type name is
    /// used instead.
    /// </param>
    /// <param name="maxResultSummaryLength">
    /// Summary bound. Omit for <see cref="JobResultSummary.DefaultMaxLength"/>.
    /// </param>
    /// <param name="timeProvider">Clock. Omit for <see cref="TimeProvider.System"/>.</param>
    /// <returns>A context suitable for passing to a job's constructor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <c>null</c>.</exception>
    /// <remarks>
    /// Every parameter but the writer is optional so that a collaborator added in a later version does
    /// not break a test written against this one.
    /// </remarks>
    public static JobLoggingContext ForWriter(
        IJobExecutionWriter writer,
        IScheduledJobRepository? scheduledJobRepository = null,
        int maxResultSummaryLength = 0,
        TimeProvider? timeProvider = null) =>
        new(writer, scheduledJobRepository, maxResultSummaryLength, timeProvider ?? TimeProvider.System);

    /// <summary>Sink that persists executions, log lines and metrics.</summary>
    /// <remarks>
    /// Internal on purpose. A derived job reaching this could call <c>Log</c> and <c>RecordMetric</c>
    /// directly, bypassing the execution-id guard and the sequence counter that
    /// <see cref="LoggedScheduledJobBase"/>'s own <c>Log</c>/<c>RecordMetric</c> apply — writing
    /// unsequenced rows against an execution that may not exist. Jobs use the protected members;
    /// nothing outside this assembly needs the writer from here.
    /// </remarks>
    internal IJobExecutionWriter Writer { get; }

    /// <summary>Used to resolve a job's display name from its scheduled job id.</summary>
    /// <remarks>
    /// Internal because it is this package's own plumbing, not part of its contract: exposing it
    /// would put an EPiServer abstraction on the frozen surface for no consumer benefit.
    /// </remarks>
    internal IScheduledJobRepository? ScheduledJobRepository { get; }

    /// <summary>Clock used for job timing.</summary>
    internal TimeProvider TimeProvider { get; }

    /// <summary>
    /// Character bound applied to <see cref="LoggedScheduledJobBase.Summary"/>, from
    /// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.MaxResultSummaryLength"/>. Always
    /// positive: a non-positive configured value falls back to
    /// <see cref="JobResultSummary.DefaultMaxLength"/>.
    /// </summary>
    /// <remarks>
    /// Public, unlike the collaborators above: it is a plain number describing a rendering policy, and
    /// a job that builds its summary in chunks has a legitimate reason to read the bound it is working
    /// against.
    /// </remarks>
    public int MaxResultSummaryLength { get; }
}
