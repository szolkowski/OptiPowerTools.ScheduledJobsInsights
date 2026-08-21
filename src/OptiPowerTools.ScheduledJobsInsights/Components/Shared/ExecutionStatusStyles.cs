using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>Maps <see cref="ExecutionStatus"/> to display colors/labels for the execution list badge.</summary>
internal static class ExecutionStatusStyles
{
    public static string HexColor(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Succeeded => "#66BB6A",
        ExecutionStatus.Failed => "#EF5350",
        // Amber rather than green or red: stopped is neither a success nor a fault.
        ExecutionStatus.Stopped => "#FFA726",
        _ => "#4FC3F7"
    };

    public static string Label(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Succeeded => "Succeeded",
        ExecutionStatus.Failed => "Failed",
        ExecutionStatus.Stopped => "Stopped",
        _ => "Running"
    };
}
