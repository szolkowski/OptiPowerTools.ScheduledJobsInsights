namespace OptiPowerTools.ScheduledJobsInsights.Retention;

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
