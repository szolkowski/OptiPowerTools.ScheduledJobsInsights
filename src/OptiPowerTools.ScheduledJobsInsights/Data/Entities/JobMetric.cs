namespace OptiPowerTools.ScheduledJobsInsights.Data.Entities;

/// <summary>
/// A single named metric value recorded for a <see cref="JobExecution"/> — either one of the
/// automatic metrics (duration, allocated bytes, CPU time, GC counts) or a custom value recorded
/// via <see cref="Logging.LoggedScheduledJobBase.RecordMetric"/>. Both share this one table so the
/// UI has a uniform query surface across built-in and custom metrics.
/// </summary>
internal class JobMetric
{
    public long Id { get; set; }

    public long JobExecutionId { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Value { get; set; }

    public string? Unit { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public JobExecution? JobExecution { get; set; }
}
