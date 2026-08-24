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
/// <b>Not an extension point.</b> Resolve it from DI; do not implement it in consuming code. Members
/// may be added in a minor version — which is precisely why implementing it is unsupported, and why
/// no <c>[Obsolete]</c> shim or default implementation will be provided for one. If you need a
/// different sink or a different rule source, the supported route is to replace this package's
/// registration for the concrete service, not to implement the interface and hope its shape holds.
/// </para>
/// </remarks>
public interface IJobRetentionPolicySource
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
