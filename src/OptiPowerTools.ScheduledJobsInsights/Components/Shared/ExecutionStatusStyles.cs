using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>
/// Maps <see cref="ExecutionStatus"/> to the CSS class and label used by the status badge.
/// </summary>
/// <remarks>
/// A class name rather than a hex colour, for the same reason as
/// <see cref="LogSeverityStyles"/>: an inline <c>style</c> attribute is dropped outright under a
/// <c>style-src</c> policy without <c>'unsafe-inline'</c>, which would leave every badge unpainted
/// with nothing to indicate why. The palette lives in the stylesheet instead.
/// </remarks>
internal static class ExecutionStatusStyles
{
    /// <summary>The CSS class carrying this status's colour.</summary>
    public static string CssClass(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Succeeded => "sji-status-succeeded",
        ExecutionStatus.Failed => "sji-status-failed",
        // Amber rather than green or red: stopped is neither a success nor a fault.
        ExecutionStatus.Stopped => "sji-status-stopped",
        // Grey: nothing is known to have gone wrong, only that nothing was ever reported.
        ExecutionStatus.Interrupted => "sji-status-interrupted",
        _ => "sji-status-running"
    };

    public static string Label(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Succeeded => "Succeeded",
        ExecutionStatus.Failed => "Failed",
        ExecutionStatus.Stopped => "Stopped",
        ExecutionStatus.Interrupted => "Interrupted",
        _ => "Running"
    };
}
