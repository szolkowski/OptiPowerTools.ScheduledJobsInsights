using System.Globalization;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>
/// Every number and timestamp the two views render goes through here, so none of them depend on the
/// ambient culture or the server's time zone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Numbers are invariant.</b> Durations, metric values and counts are diagnostics sitting inside
/// hard-coded English labels, and they get copied into tickets and compared between environments —
/// a duration that reads "310.99" on one host and "310,99" on another is worse than useless for
/// that. There is no reader who benefits from a localized GC collection count.
/// </para>
/// <para>
/// <b>Timestamps are UTC, and say so.</b> These previously used <c>DateTimeOffset.LocalDateTime</c>,
/// which converts to the *server's* zone, not the viewer's — so on a UTC container an administrator
/// in another zone was shown UTC presented as if it were local time, with nothing on the page
/// indicating otherwise. Rendering UTC explicitly is unambiguous, stable across environments, and
/// matches how the values are stored. Converting to the viewer's own zone would be friendlier still,
/// but needs JS interop to discover it and is a separate change.
/// </para>
/// <para>
/// The ISO-style ordering (yyyy-MM-dd) is deliberate too: it sorts, it is unambiguous to every
/// reader, and it sidesteps the day/month ordering that makes an invariant "08/19/2026" actively
/// misleading outside the US.
/// </para>
/// </remarks>
internal static class DisplayFormat
{
    /// <summary>Full timestamp with an explicit zone — "2026-08-19 15:37:16 UTC".</summary>
    public static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>
    /// Timestamp without seconds, for the execution list. The column header carries the "(UTC)"
    /// rather than repeating it on every one of fifty rows.
    /// </summary>
    public static string CompactTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Time of day with milliseconds, for console log lines. No date and no zone suffix: a log line
    /// is read relative to the lines around it, and the page states the zone once above.
    /// </summary>
    public static string TimeOfDay(DateTimeOffset value) =>
        value.UtcDateTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>A line count with its unit — "1 line", "2,019 lines".</summary>
    public static string LineCount(int count) =>
        count == 1 ? "1 line" : count.ToString("N0", CultureInfo.InvariantCulture) + " lines";

    /// <summary>A metric's value, trimmed of trailing zeros.</summary>
    public static string MetricValue(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// How long an execution took, or an em dash while it is still running. Sub-second runs are shown
    /// in milliseconds because most jobs finish in that range and "0.0 s" tells the reader nothing.
    /// </summary>
    public static string Duration(DateTimeOffset startedAt, DateTimeOffset? completedAt)
    {
        if (completedAt is null)
            return "—";

        var duration = completedAt.Value - startedAt;
        return duration.TotalSeconds < 1
            ? duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms"
            : duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
    }
}
