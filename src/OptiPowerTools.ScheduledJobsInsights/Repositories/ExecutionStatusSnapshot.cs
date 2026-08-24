using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>
/// The parts of an execution the detail page needs on a poll tick, without the parts it does not.
/// </summary>
/// <remarks>
/// <para>
/// The detail page polls a running execution every couple of seconds, and re-reading the whole
/// <c>JobExecution</c> row each time meant re-transferring three unbounded columns —
/// <c>ResultSummary</c> (100,000 characters by default), the unbounded <c>InputDataJson</c>, and
/// <c>ExceptionStackTrace</c> — for the entire life of the run, per viewer. That is the same reason
/// the list projects to <see cref="ExecutionListItem"/> rather than to the entity, and the same reason
/// the log fetch was made incremental: a poll must cost what has changed, not what exists.
/// </para>
/// <para>
/// <see cref="ResultSummaryLength"/> rather than the summary itself: it is enough to notice that a job
/// checkpointing with <c>FlushSummary</c> has grown its summary, which is when the page re-reads the
/// heavy columns. Comparing lengths can in principle miss an edit that preserves the length exactly,
/// which for an append-only summary does not happen — and the cost of being wrong is a stale summary
/// on a still-running job, not a wrong outcome.
/// </para>
/// </remarks>
/// <param name="Status">Current status.</param>
/// <param name="CompletedAt">When the run finished, or null while it is still going.</param>
/// <param name="ResultMessage">The one-line outcome.</param>
/// <param name="ExceptionMessage">The failure message, if it failed.</param>
/// <param name="ResultSummaryLength">
/// Characters currently in the result summary; zero when there is none.
/// </param>
internal sealed record ExecutionStatusSnapshot(
    ExecutionStatus Status,
    DateTimeOffset? CompletedAt,
    string? ResultMessage,
    string? ExceptionMessage,
    int ResultSummaryLength);
