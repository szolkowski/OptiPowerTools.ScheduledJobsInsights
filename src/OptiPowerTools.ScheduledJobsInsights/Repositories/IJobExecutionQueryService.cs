using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>Read-only queries backing the Blazor execution list and detail views.</summary>
internal interface IJobExecutionQueryService
{
    Task<ExecutionPage> GetExecutionsAsync(ExecutionFilter filter, ExecutionCursor? after, int pageSize, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDistinctJobNamesAsync(CancellationToken cancellationToken = default);

    Task<JobExecution?> GetExecutionAsync(long executionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log lines for an execution, ordered by <c>Sequence</c>.
    /// </summary>
    /// <param name="executionId">Execution whose log to read.</param>
    /// <param name="afterSequence">
    /// Return only lines with a higher sequence than this. The detail view polls a running execution
    /// every couple of seconds and a chatty job can emit thousands of lines, so re-reading the whole
    /// log on each tick would be quadratic in the log length; passing the highest sequence already
    /// displayed makes each poll fetch only what is new. Zero reads the log from the start.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<JobLogEntry>> GetLogEntriesAsync(long executionId, int afterSequence = 0, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobMetric>> GetMetricsAsync(long executionId, CancellationToken cancellationToken = default);
}
