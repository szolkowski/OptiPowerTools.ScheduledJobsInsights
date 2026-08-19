using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>Maps <see cref="LogSeverity"/> to display colors. The only place in the package where severity becomes a color.</summary>
internal static class LogSeverityStyles
{
    public static string HexColor(LogSeverity severity) => severity switch
    {
        LogSeverity.Info => "#4FC3F7",
        LogSeverity.Success => "#66BB6A",
        LogSeverity.Warning => "#FFCA28",
        LogSeverity.Error => "#EF5350",
        LogSeverity.Debug => "#9E9E9E",
        _ => "#B0BEC5"
    };

    public static string Label(LogSeverity severity) => severity switch
    {
        LogSeverity.Info => "Info",
        LogSeverity.Success => "Success",
        LogSeverity.Warning => "Warning",
        LogSeverity.Error => "Error",
        LogSeverity.Debug => "Debug",
        _ => "Log"
    };
}
