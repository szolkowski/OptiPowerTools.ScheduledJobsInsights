using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>
/// Maps <see cref="LogSeverity"/> to the CSS class that colours it. The only place in the package
/// where severity becomes an appearance.
/// </summary>
/// <remarks>
/// A class name rather than a hex colour, deliberately. Emitting the colour inline meant three
/// <c>style</c> attributes per console line, and a CMS back office served under a <c>style-src</c>
/// policy without <c>'unsafe-inline'</c> — the normal case — drops every one of them: the log loses
/// all severity colouring, silently. Returning a class also puts the palette in the stylesheet with
/// the rest of the styling, where a host can override it and where a dark theme is possible at all.
/// </remarks>
internal static class LogSeverityStyles
{
    /// <summary>The CSS class carrying this severity's colour.</summary>
    public static string CssClass(LogSeverity severity) => severity switch
    {
        LogSeverity.Info => "sji-sev-info",
        LogSeverity.Success => "sji-sev-success",
        LogSeverity.Warning => "sji-sev-warning",
        LogSeverity.Error => "sji-sev-error",
        LogSeverity.Debug => "sji-sev-debug",
        _ => "sji-sev-default"
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
