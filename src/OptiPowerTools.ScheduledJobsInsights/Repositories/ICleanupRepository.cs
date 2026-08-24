namespace OptiPowerTools.ScheduledJobsInsights.Repositories;

/// <summary>Deletes aged-out job execution history. Child log/metric rows cascade at the database level.</summary>
/// <remarks>
/// <para>
/// Public only because <see cref="Jobs.ScheduledJobsInsightsCleanupJob"/> must be public for
/// Optimizely to discover it, which forces its constructor parameter types to be public too.
/// </para>
/// <para>
/// <b>Not an extension point.</b> Do not implement it in consuming code. Members may be added in a
/// minor version — which is precisely why implementing it is unsupported, and why no
/// <c>[Obsolete]</c> shim or default implementation will be provided for one.
/// </para>
/// </remarks>
public interface ICleanupRepository
{
    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> finished executions older than
    /// <paramref name="cutoff"/>, skipping any job type that has its own retention rule.
    /// </summary>
    /// <remarks>
    /// Executions still <see cref="Configuration.ExecutionStatus.Running"/> are never deleted, however
    /// old: a job may legitimately run for longer than its own retention, and removing the row under a
    /// live run destroys that run's history rather than trimming an old one.
    /// </remarks>
    /// <param name="cutoff">Executions started before this are eligible.</param>
    /// <param name="batchSize">Maximum executions to delete in this call.</param>
    /// <param name="excludedJobTypeNames">
    /// Job types governed by their own retention, handled by
    /// <see cref="DeleteExecutionsOlderThan(string, DateTimeOffset, int, CancellationToken)"/>
    /// instead. Excluding them here is what stops the installation default from deleting history a
    /// job asked to keep.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancelled when an administrator stops the cleanup job. A batch already in flight completes;
    /// the loop simply does not start another.
    /// </param>
    /// <returns>How many executions were deleted.</returns>
    int DeleteExecutionsOlderThan(
        DateTimeOffset cutoff,
        int batchSize,
        IReadOnlyCollection<string> excludedJobTypeNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> finished executions of one job type older than
    /// <paramref name="cutoff"/>.
    /// </summary>
    /// <remarks>
    /// As above, executions still <see cref="Configuration.ExecutionStatus.Running"/> are left alone
    /// whatever their age.
    /// </remarks>
    /// <param name="jobTypeName">CLR full name of the job whose history is being trimmed.</param>
    /// <param name="cutoff">Executions started before this are eligible.</param>
    /// <param name="batchSize">Maximum executions to delete in this call.</param>
    /// <param name="cancellationToken">Cancelled when an administrator stops the cleanup job.</param>
    /// <returns>How many executions were deleted.</returns>
    int DeleteExecutionsOlderThan(
        string jobTypeName,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks executions still running since before <paramref name="cutoff"/> as
    /// <see cref="Configuration.ExecutionStatus.Interrupted"/>.
    /// </summary>
    /// <param name="cutoff">Runs started before this and still unfinished are given up on.</param>
    /// <param name="batchSize">
    /// Maximum executions marked per statement. Batched for the same reason as the deletes: a single
    /// unbounded update takes locks across the table for as long as it runs, which on a first sweep
    /// through a backlog of stranded rows blocks every job that starts meanwhile.
    /// </param>
    /// <param name="cancellationToken">Cancelled when an administrator stops the cleanup job.</param>
    /// <returns>How many executions were marked.</returns>
    /// <remarks>
    /// A process recycled mid-run cannot record its own outcome, so nothing else will ever finish
    /// these rows. Left alone they accumulate, and every count, filter and "is it still running?"
    /// question is wrong for as long as they sit there.
    /// </remarks>
    int MarkInterruptedExecutions(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default);
}
