namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>Backs <see cref="Jobs.ScheduledJobsInsightsCleanupJob"/>'s retention enforcement.</summary>
public interface ICleanupRepository
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> executions started before <paramref name="cutoff"/>
    /// (child log/metric rows disappear via <c>ON DELETE CASCADE</c>) and returns the number deleted.
    /// Synchronous — matches <c>ScheduledJobBase.Execute()</c>'s synchronous contract.
    /// </summary>
    int DeleteExecutionsOlderThan(DateTimeOffset cutoff, int batchSize);
}
