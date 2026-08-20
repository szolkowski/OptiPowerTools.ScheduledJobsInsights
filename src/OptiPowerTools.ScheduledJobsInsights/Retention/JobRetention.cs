namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// Everything the retention screen shows about one job, and everything the cleanup job needs to act
/// on it.
/// </summary>
/// <param name="JobTypeName">CLR full name — the identity retention is keyed on.</param>
/// <param name="DisplayName">
/// The job's name as the CMS knows it, or the short type name for jobs the CMS no longer has.
/// </param>
/// <param name="IsRegistered">Whether Optimizely currently has this job registered.</param>
/// <param name="ExistsInCode">
/// Whether a logged job of this type still exists in the running application. False means only
/// history remains — the job was removed from the codebase — which is worth showing, since that
/// history is still worth managing. Distinct from <paramref name="IsRegistered"/>: a job can exist in
/// code without the CMS having registered it yet.
/// </param>
/// <param name="Attribute">The job's declared period, if it has a usable <see cref="JobRetentionAttribute"/>.</param>
/// <param name="AttributeDescription">The attribute's rationale, shown as a hint beside the value.</param>
/// <param name="HasInvalidAttribute">
/// True when the job carries an attribute whose value cannot be acted on. Surfaced rather than
/// swallowed: silently ignoring it would leave the author believing retention was configured.
/// </param>
/// <param name="Override">An administrator's choice, if one has been made.</param>
/// <param name="ModifiedBy">Who made that choice.</param>
/// <param name="ModifiedAt">When they made it.</param>
/// <param name="ExecutionCount">How many executions are currently stored for this job.</param>
internal sealed record JobRetention(
    string JobTypeName,
    string DisplayName,
    bool IsRegistered,
    bool ExistsInCode,
    RetentionPeriod? Attribute,
    string? AttributeDescription,
    bool HasInvalidAttribute,
    RetentionPeriod? Override,
    string? ModifiedBy,
    DateTimeOffset? ModifiedAt,
    int ExecutionCount)
{
    /// <summary>
    /// The period actually in force, and where it came from. Precedence is override, then attribute,
    /// then the installation default — the single place that order is expressed.
    /// </summary>
    public (RetentionPeriod Period, RetentionSource Source) Resolve(RetentionPeriod fallback) =>
        Override is { } chosen ? (chosen, RetentionSource.Override)
            : Attribute is { } declared ? (declared, RetentionSource.Attribute)
            : (fallback, RetentionSource.Default);
}
