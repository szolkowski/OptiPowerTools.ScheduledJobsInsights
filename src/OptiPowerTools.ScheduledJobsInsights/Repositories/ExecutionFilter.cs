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

/// <summary>A page of execution list results.</summary>
internal sealed record ExecutionPage(IReadOnlyList<Data.Entities.JobExecution> Items, ExecutionCursor? NextCursor, bool HasMore);
