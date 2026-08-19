using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// A buffered write destined for the channel drained by <see cref="JobLogBackgroundWriter"/>.
/// Only high-frequency, insert-only writes (log lines, metrics) go through the buffer —
/// execution begin/complete/input-data are low-frequency and written synchronously instead.
/// </summary>
internal abstract record JobRecord(long ExecutionId);

internal sealed record LogRecordItem(
    long ExecutionId,
    int Sequence,
    LogSeverity Severity,
    string Message,
    LogEntrySource Source,
    DateTimeOffset Timestamp) : JobRecord(ExecutionId);

internal sealed record MetricRecordItem(
    long ExecutionId,
    string Name,
    double Value,
    string? Unit,
    DateTimeOffset RecordedAt) : JobRecord(ExecutionId);
