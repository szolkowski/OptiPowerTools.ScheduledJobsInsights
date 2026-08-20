using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Persists job execution data on behalf of <see cref="LoggedScheduledJobBase"/>.
/// Begin/Complete/SetInputData/SetResultSummary are low-frequency and written synchronously and
/// immediately; Log/RecordMetric are potentially high-frequency and buffered — see
/// <see cref="JobLogBackgroundWriter"/>.
/// </summary>
/// <remarks>
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
    void Complete(long executionId, bool succeeded, string? resultMessage, Exception? exception);

    /// <summary>Buffers a log line for asynchronous batched persistence.</summary>
    void Log(long executionId, int sequence, LogSeverity severity, string message, LogEntrySource source);

    /// <summary>Synchronously persists the input-data snapshot for an execution.</summary>
    void SetInputData(long executionId, string inputDataJson);

    /// <summary>
    /// Synchronously persists the result-summary text for an execution, replacing any previous value.
    /// </summary>
    /// <param name="executionId">Execution to attach the summary to.</param>
    /// <param name="summary">
    /// The rendered summary. Truncated to <see cref="MaxResultSummaryLength"/> if longer, so callers
    /// writing an execution directly are bounded the same way <see cref="JobResultSummary"/> is.
    /// </param>
    void SetResultSummary(long executionId, string summary);

    /// <summary>
    /// The configured character limit for result summaries.
    /// </summary>
    /// <remarks>
    /// Exposed here because <see cref="LoggedScheduledJobBase"/> needs the configured value to build
    /// its <see cref="JobResultSummary"/>, and the writer is the only DI-resolved collaborator it
    /// holds — derived jobs forward a fixed pair of constructor arguments to <c>base</c>, so an
    /// <c>IOptions&lt;T&gt;</c> parameter cannot be added there without breaking every one of them.
    /// </remarks>
    int MaxResultSummaryLength { get; }

    /// <summary>Buffers a metric value for asynchronous batched persistence.</summary>
    void RecordMetric(long executionId, string name, double value, string? unit);
}
