namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// How long history is kept: a positive number of days, or indefinitely.
/// </summary>
/// <remarks>
/// A type rather than a bare <c>int?</c> because "indefinite" and "not configured" are both naturally
/// expressed as null and mean opposite things. Here a <see cref="RetentionPeriod"/> always *is* a
/// configured period; whether one exists at all is expressed by the nullability of the field holding
/// it.
/// </remarks>
/// <param name="Days">Days to keep, or <c>null</c> for indefinitely.</param>
public readonly record struct RetentionPeriod(int? Days)
{
    /// <summary>Keep forever — the cleanup job skips these executions entirely.</summary>
    public static RetentionPeriod Indefinite => new((int?)null);

    /// <summary>Keep for a fixed number of days.</summary>
    public static RetentionPeriod OfDays(int days) => new(days);

    /// <summary>Whether this period never expires.</summary>
    public bool IsIndefinite => Days is null;

    /// <summary>The instant before which executions are eligible for deletion.</summary>
    /// <returns><c>null</c> when retention is indefinite, since nothing is ever eligible.</returns>
    public DateTimeOffset? CutoffFrom(DateTimeOffset now) =>
        Days is { } days ? now.AddDays(-days) : null;

    /// <summary>Reads an attribute's declared period, or <c>null</c> if it declares nothing usable.</summary>
    public static RetentionPeriod? FromAttribute(JobRetentionAttribute attribute)
    {
        if (!attribute.IsValid)
            return null;

        return attribute.IsIndefinite ? Indefinite : OfDays(attribute.Days);
    }
}

/// <summary>Where a job's effective retention came from — the precedence chain, highest first.</summary>
internal enum RetentionSource
{
    /// <summary>An administrator set it in the Job Retention screen.</summary>
    Override,

    /// <summary>Declared by the job's own <see cref="JobRetentionAttribute"/>.</summary>
    Attribute,

    /// <summary>Nothing more specific applies, so the installation-wide default is used.</summary>
    Default
}
