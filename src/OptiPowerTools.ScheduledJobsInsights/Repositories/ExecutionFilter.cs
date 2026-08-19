using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>Filter criteria for the paginated execution list.</summary>
internal sealed record ExecutionFilter(
    string? JobName = null,
    ExecutionStatus? Status = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);

/// <summary>Keyset pagination cursor — the last item's sort key from the previous page.</summary>
internal sealed record ExecutionCursor(DateTimeOffset StartedAt, long Id);

/// <summary>
/// One row of the execution list — deliberately not the <c>JobExecution</c> entity.
/// </summary>
/// <remarks>
/// The entity carries three unbounded columns the list never displays (<c>ResultSummary</c>,
/// <c>InputDataJson</c>, <c>ExceptionStackTrace</c>). Selecting whole entities meant dragging all of
/// them across for every row of every page; projecting to this record keeps the list query
/// proportional to what the grid actually renders however large those columns grow.
/// </remarks>
/// <param name="Id">Execution id, used for the detail link and as the keyset tie-break.</param>
/// <param name="JobName">Resolved job name.</param>
/// <param name="Status">Running/Succeeded/Failed.</param>
/// <param name="StartedAt">Start time — the primary keyset sort key.</param>
/// <param name="CompletedAt">Completion time, or null while still running.</param>
/// <param name="ResultMessage">One-line result, shown for successful runs.</param>
/// <param name="ExceptionMessage">Failure message, shown in place of the result for failed runs.</param>
/// <param name="HasResultSummary">
/// Whether a result summary exists, evaluated server-side so the text itself stays out of the query.
/// </param>
internal sealed record ExecutionListItem(
    long Id,
    string JobName,
    ExecutionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ResultMessage,
    string? ExceptionMessage,
    bool HasResultSummary);

/// <summary>A page of execution list results.</summary>
internal sealed record ExecutionPage(IReadOnlyList<ExecutionListItem> Items, ExecutionCursor? NextCursor, bool HasMore);
