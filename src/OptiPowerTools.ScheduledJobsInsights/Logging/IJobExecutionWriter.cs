using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Persists job execution data on behalf of <see cref="LoggedScheduledJobBase"/>.
/// Begin/Complete/SetInputData/SetResultSummary are low-frequency and written synchronously and
/// immediately; Log/RecordMetric are potentially high-frequency and buffered — see
/// <see cref="JobLogBackgroundWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resolve it from DI to record executions that are not scheduled jobs. Implementing it is also
/// supported, and is the intended way to unit-test a job — see
/// <see cref="JobLoggingContext.ForWriter"/>, which takes one. <b>No member will be added outside a
/// major version</b>, so an implementation or a mock written against 1.0 keeps compiling for the
/// whole of 1.x.
/// </para>
/// <para>
/// That is a commitment rather than a mechanism, deliberately. Default implementations would make
/// additions technically safe, but they also make the interface unmockable by Castle-based libraries
/// such as NSubstitute — which would break the very unit-testing scenario
/// <see cref="JobLoggingContext.ForWriter"/> exists to support. A promise that costs nothing is
/// worth more here than a mechanism that costs that.
/// </para>
/// <b>No member of this interface throws.</b> Implementations report failures out of band and carry
/// on. A job is running when these are called, and this package's contract is that it only
/// <em>observes</em> an execution: losing the record of a run must never turn into a failure of the
/// run itself. <see cref="BeginExecution"/> signals failure by returning <c>null</c>, after which
/// nothing else about that execution can be recorded.
/// </remarks>
public interface IJobExecutionWriter
{
    /// <summary>
    /// Inserts a new <c>JobExecution</c> row and returns its generated Id, or <c>null</c> if the
    /// execution could not be recorded — typically because the insights database is unreachable.
    /// </summary>
    /// <returns>
    /// The new execution's Id, or <c>null</c>. A null result means this run goes unrecorded; callers
    /// should skip the remaining calls rather than treating it as an error.
    /// </returns>
    long? BeginExecution(Guid scheduledJobId, string jobName, string jobTypeName);

    /// <summary>Marks an execution as finished, recording its outcome.</summary>
    /// <param name="executionId">Execution to complete.</param>
    /// <param name="outcome">
    /// How the run ended. <see cref="ExecutionStatus.Running"/> is not a valid completion and is
    /// recorded as <see cref="ExecutionStatus.Failed"/>.
    /// </param>
    /// <param name="resultMessage">The one-line result, or <c>null</c>.</param>
    /// <param name="exception">The exception that ended the run, or <c>null</c>.</param>
    void Complete(long executionId, ExecutionStatus outcome, string? resultMessage, Exception? exception);

    /// <summary>Buffers a log line for asynchronous batched persistence.</summary>
    void Log(long executionId, int sequence, LogSeverity severity, string message, LogEntrySource source);

    /// <summary>Synchronously persists the input-data snapshot for an execution.</summary>
    void SetInputData(long executionId, string inputDataJson);

    /// <summary>
    /// Synchronously persists the result-summary text for an execution, replacing any previous value.
    /// </summary>
    /// <param name="executionId">Execution to attach the summary to.</param>
    /// <param name="summary">
    /// The rendered summary. Truncated to the configured
    /// <see cref="Configuration.OptiPowerToolsScheduledJobsInsightsOptions.MaxResultSummaryLength"/>
    /// if longer, so callers writing an execution directly are bounded the same way
    /// <see cref="JobResultSummary"/> is.
    /// </param>
    void SetResultSummary(long executionId, string summary);

    /// <summary>Buffers a metric value for asynchronous batched persistence.</summary>
    void RecordMetric(long executionId, string name, double value, string? unit);
}
