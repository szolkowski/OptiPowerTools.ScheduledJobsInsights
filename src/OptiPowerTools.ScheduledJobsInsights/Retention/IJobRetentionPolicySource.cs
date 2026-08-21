namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// The retention rules in force, as the cleanup job needs them.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrower than the service backing the Job Retention screen. That one deals in
/// attributes, audit trails, execution counts and jobs that no longer exist — none of which the
/// cleanup job has any use for. Keeping the two apart is also what lets the richer type stay
/// internal: <see cref="Jobs.ScheduledJobsInsightsCleanupJob"/> has to be public for Optimizely to
/// discover it, so every type in its constructor must be public too.
/// </para>
/// <para>
/// Intended to be consumed rather than implemented. Members may be added in a future minor version,
/// which would break an outside implementation.
/// </para>
/// </remarks>
public interface IJobRetentionPolicySource
{
    /// <summary>
    /// The installation-wide fallback, from
    /// <see cref="Configuration.OptiPowerToolScheduledJobsInsightsOptions.RetentionDays"/>. Applies
    /// to every job with no rule of its own.
    /// </summary>
    RetentionPeriod DefaultPeriod { get; }

    /// <summary>
    /// The retention in force for each job that has a rule of its own, keyed by CLR job type name.
    /// Jobs absent from the result fall under <see cref="DefaultPeriod"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// Already resolved: an administrator's override wins over the job's
    /// <see cref="JobRetentionAttribute"/>, so callers do not repeat that precedence.
    /// </returns>
    Task<IReadOnlyDictionary<string, RetentionPeriod>> GetEffectiveOverridesAsync(CancellationToken cancellationToken = default);
}
