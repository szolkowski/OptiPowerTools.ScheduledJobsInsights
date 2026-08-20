using System.Globalization;
using System.Text;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>
/// Renders the size annotation shown beside the <em>Result summary</em> heading, and the line count
/// the detail view uses to decide whether that section starts collapsed.
/// </summary>
/// <remarks>
/// Extracted from <c>Detail.razor</c> rather than left as private helpers because the line counting
/// has enough edge cases — trailing newlines, CRLF, a single unterminated line — to be worth testing
/// directly, and a Razor component cannot be. Sits alongside <see cref="LogSeverityStyles"/> and
/// <see cref="ExecutionStatusStyles"/>, which are presentation-only for the same reason.
/// </remarks>
internal static class ResultSummaryBadge
{
    /// <summary>Formats a summary's size as "1,240 lines · 38.4 KB".</summary>
    /// <remarks>
    /// Invariant, via <see cref="DisplayFormat"/> — see the reasoning there. In short: this runs on
    /// the server, so the ambient culture is the host's rather than the reader's, and on a machine
    /// set to pl-PL the badge rendered "2.000 lines · 3,9 KB" beside hard-coded English labels.
    /// </remarks>
    public static string Format(string summary) =>
        $"{DisplayFormat.LineCount(CountLines(summary))} · {FormatSize(Encoding.UTF8.GetByteCount(summary))}";

    /// <summary>
    /// Counts the lines in a summary, treating a trailing newline as ending the last line rather
    /// than starting an empty one — which matters because every <c>AppendLine</c> leaves one. Only
    /// an empty summary counts as zero lines; a lone newline is one (empty) line.
    /// </summary>
    /// <remarks>
    /// Iterates rather than splitting: this runs on every two-second poll tick over a string that can
    /// be 100 KB, and <c>Split</c> would allocate an array of thousands of substrings each time.
    /// Counting only <c>\n</c> handles CRLF as well, since every CRLF contains exactly one.
    /// </remarks>
    public static int CountLines(string summary)
    {
        if (string.IsNullOrEmpty(summary))
            return 0;

        var lines = 1;
        foreach (var character in summary)
        {
            if (character == '\n')
                lines++;
        }

        return summary[^1] == '\n' ? lines - 1 : lines;
    }

    private static string FormatSize(int bytes) =>
        bytes < 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB");
}
