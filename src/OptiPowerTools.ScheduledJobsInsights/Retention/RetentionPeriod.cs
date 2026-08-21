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
/// <remarks>
/// <para>
/// There is no public constructor, and that is the point. The only two ways to make one are
/// <see cref="Indefinite"/> and <see cref="OfDays"/>, which is what makes the type's promise —
/// a positive number of days, or forever — true rather than merely documented. A zero would make
/// <see cref="CutoffFrom"/> return <em>now</em> and delete a job's entire history, including the run
/// currently in progress; a negative one would put the cutoff in the future and do the same.
/// </para>
/// <para>
/// <c>default(RetentionPeriod)</c> is <see cref="Indefinite"/>: of the values a zeroed struct could
/// mean, keeping everything is the only harmless one.
/// </para>
/// </remarks>
public readonly record struct RetentionPeriod
{
    /// <summary>Days to keep, or <c>null</c> for indefinitely.</summary>
    public int? Days { get; private init; }

    /// <summary>Keep forever — the cleanup job skips these executions entirely.</summary>
    public static RetentionPeriod Indefinite => new() { Days = null };

    /// <summary>Keep for a fixed number of days.</summary>
    /// <param name="days">A positive number of days.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="days"/> is zero or negative.</exception>
    public static RetentionPeriod OfDays(int days)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);

        return new RetentionPeriod { Days = days };
    }

    /// <summary>
    /// Reads a period from storage, returning <c>null</c> when the stored value cannot be acted on.
    /// </summary>
    /// <param name="days">The stored value: <c>null</c> for indefinite, otherwise a day count.</param>
    /// <remarks>
    /// Storage is outside this type's control — a hand-edited row, a restored backup, a script — so
    /// reading is the one path that must cope with a value <see cref="OfDays"/> would reject. A
    /// non-positive value is reported as unusable rather than obeyed, since obeying it would delete
    /// the history it was supposed to govern.
    /// </remarks>
    internal static RetentionPeriod? FromStoredValue(int? days)
    {
        if (days is null)
            return Indefinite;

        return days > 0 ? OfDays(days.Value) : null;
    }

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
