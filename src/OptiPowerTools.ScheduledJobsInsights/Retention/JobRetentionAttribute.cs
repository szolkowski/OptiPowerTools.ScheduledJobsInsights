namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// Declares how long this job's execution history should be kept, overriding the installation-wide
/// <see cref="Configuration.OptiPowerToolsScheduledJobsInsightsOptions.RetentionDays"/>.
/// </summary>
/// <remarks>
/// <para>
/// Put this on a job whose retention is a property of the job itself rather than of the installation
/// — a job that logs enormously and is only useful for a week, or an audit job whose history has to
/// be kept for compliance. It travels with the code, so a fresh deployment gets it right without
/// anyone remembering to configure it.
/// </para>
/// <para>
/// It is a default, not a mandate: an administrator can override it per job in the CMS's
/// <em>Job Retention</em> screen, and that choice wins. The screen shows this value and its
/// <see cref="Description"/> alongside, so whoever changes it can see what the job's author intended
/// and why.
/// </para>
/// <example>
/// <code>
/// [ScheduledJob(DisplayName = "Nightly Catalog Sync")]
/// [JobRetention(7, Description = "Logs one line per SKU; a week is enough to diagnose a bad run.")]
/// public class CatalogSyncJob : LoggedScheduledJobBase { }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class JobRetentionAttribute : Attribute
{
    /// <summary>Pass as <c>days</c> to keep this job's history forever.</summary>
    public const int Indefinite = -1;

    /// <summary>Declares a retention period for the job.</summary>
    /// <param name="days">
    /// Days of history to keep, or <see cref="Indefinite"/> to keep it forever. Any other
    /// non-positive value is ignored, and the job falls back to the installation default — an
    /// attribute cannot throw usefully at startup, so a bad value is reported in the retention screen
    /// rather than crashing the application.
    /// </param>
    public JobRetentionAttribute(int days)
    {
        Days = days;
    }

    /// <summary>Days of history to keep, or <see cref="Indefinite"/>.</summary>
    public int Days { get; }

    /// <summary>
    /// Why this job needs a different retention from everything else. Shown next to the value in the
    /// retention screen, so an administrator deciding whether to override it can see the reasoning.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Whether this attribute declares indefinite retention.</summary>
    public bool IsIndefinite => Days == Indefinite;

    /// <summary>Whether <see cref="Days"/> is a value this package can act on.</summary>
    internal bool IsValid => Days > 0 || Days == Indefinite;
}
