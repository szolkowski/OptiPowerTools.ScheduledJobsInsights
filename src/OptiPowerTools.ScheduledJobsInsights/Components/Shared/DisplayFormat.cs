using System.Globalization;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>
/// Every number the two views render goes through here, so none of them depend on the ambient
/// culture. Timestamps are <see cref="ViewerClock"/>'s job.
/// </summary>
/// <remarks>
/// Numbers are formatted invariantly on purpose. Durations, metric values and counts are diagnostics
/// sitting inside hard-coded English labels, and they get copied into tickets and compared between
/// environments — a duration that reads "310.99" on one host and "310,99" on another is worse than
/// useless for that. There is no reader who benefits from a localized GC collection count. Time
/// zones are a different question and get a different answer; see <see cref="ViewerClock"/>.
/// </remarks>
internal static class DisplayFormat
{
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
