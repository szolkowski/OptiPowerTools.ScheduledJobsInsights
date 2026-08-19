using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Data.Entities;

/// <summary>
/// A single log line captured during a <see cref="JobExecution"/> — either an intercepted
/// <c>OnStatusChanged</c> message or an explicit <see cref="Logging.LoggedScheduledJobBase.Log"/> call.
/// </summary>
internal class JobLogEntry
{
    public long Id { get; set; }

    public long JobExecutionId { get; set; }

    /// <summary>Per-execution monotonic counter. The authoritative ordering key — see remarks on why timestamp alone is not.</summary>
    /// <remarks>
    /// Under a tight logging loop, <see cref="DateTimeOffset"/> resolution collisions make timestamp-only
    /// ordering unreliable. Sort/query by <c>(JobExecutionId, Sequence)</c>, never by <see cref="Timestamp"/> alone.
    /// </remarks>
    public int Sequence { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public LogSeverity Severity { get; set; } = LogSeverity.Default;

    public LogEntrySource Source { get; set; } = LogEntrySource.DevLog;

    public string Message { get; set; } = string.Empty;

    public JobExecution? JobExecution { get; set; }
}
