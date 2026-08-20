using OptiPowerTools.ScheduledJobsInsights.Components.Shared;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

public class ResultSummaryBadgeTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("one line", 1)]
    [InlineData("one line\n", 1)]          // AppendLine leaves a trailing newline on every summary
    [InlineData("one line\r\n", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\nb\n", 2)]
    [InlineData("a\r\nb\r\n", 2)]
    [InlineData("\n", 1)]                   // one empty, terminated line — only "" counts as zero
    [InlineData("a\n\nb\n", 3)]             // a deliberate blank line still counts
    public void CountLines_TreatsATrailingNewlineAsTerminatingTheLastLine(string summary, int expected) =>
        Assert.Equal(expected, ResultSummaryBadge.CountLines(summary));

    [Fact]
    public void CountLines_MatchesWhatJobResultSummaryProduces()
    {
        // The counter's contract is really "what does a summary built the normal way look like",
        // so pin it against the builder rather than against hand-written strings alone.
        var summary = new JobResultSummary();
        summary.AppendSection("Totals");   // title + underline
        summary.AppendLine("  Rows: 12");
        summary.AppendLine("  Skipped: 3");

        Assert.Equal(4, ResultSummaryBadge.CountLines(summary.ToString()));
    }

    [Fact]
    public void Format_UsesTheSingularForOneLine() =>
        Assert.StartsWith("1 line ·", ResultSummaryBadge.Format("just the one"));

    [Fact]
    public void Format_GroupsThousandsInTheLineCount() =>
        Assert.StartsWith("2,000 lines ·", ResultSummaryBadge.Format(string.Concat(Enumerable.Repeat("x\n", 2000))));

    [Fact]
    public void Format_ReportsBytesBelowAKilobyte() =>
        Assert.EndsWith("· 12 B", ResultSummaryBadge.Format(new string('x', 12)));

    [Fact]
    public void Format_ReportsKilobytesAtOrAboveAKilobyte() =>
        Assert.EndsWith("· 2 KB", ResultSummaryBadge.Format(new string('x', 2048)));

    [Fact]
    public void Format_MeasuresBytesNotCharacters()
    {
        // A summary full of non-ASCII is bigger than its character count suggests, and the badge is a
        // size hint — reporting characters as bytes would understate it by half.
        var summary = new string('ł', 512);   // 2 bytes each in UTF-8

        Assert.EndsWith("· 1 KB", ResultSummaryBadge.Format(summary));
    }
}
