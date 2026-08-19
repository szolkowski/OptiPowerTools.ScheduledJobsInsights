using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Persists job execution data on behalf of <see cref="LoggedScheduledJobBase"/>.
/// Begin/Complete/SetInputData are low-frequency and written synchronously and immediately;
/// Log/RecordMetric are potentially high-frequency and buffered — see <see cref="JobLogBackgroundWriter"/>.
/// </summary>
public interface IJobExecutionWriter
{
    /// <summary>Inserts a new <c>JobExecution</c> row and returns its generated Id.</summary>
    long BeginExecution(Guid scheduledJobId, string jobName, string jobTypeName);

    /// <summary>Marks an execution as finished, recording its outcome.</summary>
    void Complete(long executionId, bool succeeded, string? resultMessage, Exception? exception);

    /// <summary>Buffers a log line for asynchronous batched persistence.</summary>
    void Log(long executionId, int sequence, LogSeverity severity, string message, LogEntrySource source);

    /// <summary>Synchronously persists the input-data snapshot for an execution.</summary>
    void SetInputData(long executionId, string inputDataJson);

    /// <summary>Buffers a metric value for asynchronous batched persistence.</summary>
    void RecordMetric(long executionId, string name, double value, string? unit);
}
