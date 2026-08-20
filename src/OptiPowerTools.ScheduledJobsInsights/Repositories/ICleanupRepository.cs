namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>Deletes aged-out job execution history. Child log/metric rows cascade at the database level.</summary>
public interface ICleanupRepository
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> executions older than <paramref name="cutoff"/>,
    /// skipping any job type that has its own retention rule.
    /// </summary>
    /// <param name="cutoff">Executions started before this are eligible.</param>
    /// <param name="batchSize">Maximum executions to delete in this call.</param>
    /// <param name="excludedJobTypeNames">
    /// Job types governed by their own retention, handled by
    /// <see cref="DeleteExecutionsOlderThan(string, DateTimeOffset, int)"/> instead. Excluding them
    /// here is what stops the installation default from deleting history a job asked to keep.
    /// </param>
    /// <returns>How many executions were deleted.</returns>
    int DeleteExecutionsOlderThan(DateTimeOffset cutoff, int batchSize, IReadOnlyCollection<string> excludedJobTypeNames);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> executions of one job type older than
    /// <paramref name="cutoff"/>.
    /// </summary>
    /// <param name="jobTypeName">CLR full name of the job whose history is being trimmed.</param>
    /// <param name="cutoff">Executions started before this are eligible.</param>
    /// <param name="batchSize">Maximum executions to delete in this call.</param>
    /// <returns>How many executions were deleted.</returns>
    int DeleteExecutionsOlderThan(string jobTypeName, DateTimeOffset cutoff, int batchSize);
}
