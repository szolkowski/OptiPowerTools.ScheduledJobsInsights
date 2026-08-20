namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// Resolves and stores per-job retention. Backs the Job Retention screen and tells the cleanup job
/// how long each job's history should live.
/// </summary>
internal interface IJobRetentionService : IJobRetentionPolicySource
{
    /// <summary>
    /// Every job worth showing: those Optimizely currently has registered, those that only exist in
    /// execution history, and those that merely declare an attribute — unioned and ordered by name.
    /// </summary>
    Task<IReadOnlyList<JobRetention>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or clears an administrator override for one job type.
    /// </summary>
    /// <param name="jobTypeName">CLR full name of the job.</param>
    /// <param name="period">The chosen period, or <c>null</c> to clear the override and fall back.</param>
    /// <param name="modifiedBy">Who is making the change, for the audit trail.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SetOverrideAsync(string jobTypeName, RetentionPeriod? period, string modifiedBy, CancellationToken cancellationToken = default);

}
