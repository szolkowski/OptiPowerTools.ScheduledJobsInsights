namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// The retention rules in force, as the cleanup job needs them.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrower than the service backing the Job Retention screen. That one deals in
/// attributes, audit trails, execution counts and jobs that no longer exist — none of which the
/// cleanup job has any use for.
/// </para>
/// <para>
/// Internal, and free to change. The split used to carry a second job: keeping this face public so
/// that <see cref="Jobs.ScheduledJobsInsightsCleanupJob"/>, which must be public for Optimizely to
/// discover it, could take it as a constructor parameter. The job now resolves it from an
/// <see cref="IServiceProvider"/> instead, so the narrowing is once again about what the cleanup job
/// needs rather than about accessibility.
/// </para>
/// </remarks>
internal interface IJobRetentionPolicySource
{
    /// <summary>
    /// The installation-wide fallback, from
    /// <see cref="Configuration.OptiPowerToolsScheduledJobsInsightsOptions.RetentionDays"/>. Applies
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
