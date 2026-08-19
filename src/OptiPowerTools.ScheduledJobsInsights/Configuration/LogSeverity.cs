namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Severity of a single persisted job log line. Purely a data classification —
/// mapping to actual colors/CSS happens only in the Blazor UI layer.
/// </summary>
public enum LogSeverity : byte
{
    /// <summary>No particular severity — rendered in the UI's default (neutral) color.</summary>
    Default = 0,

    /// <summary>Informational line — rendered in blue.</summary>
    Info = 1,

    /// <summary>Successful step or outcome — rendered in green.</summary>
    Success = 2,

    /// <summary>Warning — rendered in yellow.</summary>
    Warning = 3,

    /// <summary>Error — rendered in red.</summary>
    Error = 4,

    /// <summary>Verbose/diagnostic detail — rendered in muted gray.</summary>
    Debug = 5
}
