using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Data.Entities;

/// <summary>
/// An administrator's retention choice for one job type, set in the Job Retention screen. Highest
/// precedence: it beats the job's <see cref="JobRetentionAttribute"/> and the installation default.
/// </summary>
/// <remarks>
/// A row exists only where an override has been set — its absence is what makes the attribute (or
/// failing that, the default) apply. Keyed on <c>JobTypeName</c> rather than the display name because
/// the CLR type name survives a job being renamed in the CMS, and a retention rule that silently
/// stopped applying after a rename would fail in the worst direction: quietly keeping data forever.
/// </remarks>
internal class JobRetentionPolicy
{
    public long Id { get; set; }

    /// <summary>CLR full name of the job, matching <see cref="JobExecution.JobTypeName"/>.</summary>
    public string JobTypeName { get; set; } = string.Empty;

    /// <summary>Days of history to keep, or <c>null</c> for indefinitely.</summary>
    public int? RetentionDays { get; set; }

    /// <summary>Who last changed this. Retention is destructive, so the trail matters.</summary>
    public string ModifiedBy { get; set; } = string.Empty;

    public DateTimeOffset ModifiedAt { get; set; }
}
