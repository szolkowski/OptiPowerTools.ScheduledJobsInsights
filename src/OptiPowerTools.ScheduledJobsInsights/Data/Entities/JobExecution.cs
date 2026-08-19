using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Data.Entities;

/// <summary>
/// One row per <see cref="EPiServer.Scheduler.ScheduledJobBase.Execute"/> call.
/// </summary>
internal class JobExecution
{
    public long Id { get; set; }

    /// <summary>Correlates to <see cref="EPiServer.Scheduler.ScheduledJobBase.ScheduledJobId"/>.</summary>
    public Guid ScheduledJobId { get; set; }

    /// <summary>Resolved from <c>IScheduledJobRepository</c> at execution start, falling back to the job's type name.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>The job's CLR type full name — stays durable even if the CMS job definition is later renamed/deleted.</summary>
    public string JobTypeName { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public ExecutionStatus Status { get; set; } = ExecutionStatus.Running;

    /// <summary>The string returned by <c>Execute()</c> on success.</summary>
    public string? ResultMessage { get; set; }

    /// <summary>
    /// Optional multi-line report built during the run via
    /// <see cref="Logging.LoggedScheduledJobBase.Summary"/>. Unlike <see cref="ResultMessage"/>,
    /// which Optimizely renders in a single admin grid cell, this keeps its newlines and length.
    /// </summary>
    public string? ResultSummary { get; set; }

    public string? ExceptionMessage { get; set; }

    public string? ExceptionStackTrace { get; set; }

    /// <summary>JSON payload captured via <see cref="Logging.LoggedScheduledJobBase.LogInputData"/>.</summary>
    public string? InputDataJson { get; set; }

    public string MachineName { get; set; } = string.Empty;

    public ICollection<JobLogEntry> LogEntries { get; set; } = [];

    public ICollection<JobMetric> Metrics { get; set; } = [];
}
