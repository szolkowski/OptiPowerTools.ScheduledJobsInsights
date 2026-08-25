using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

public class JobResultSummaryTests
{
    [Fact]
    public void NewSummary_IsEmpty()
    {
        var summary = new JobResultSummary();

        Assert.True(summary.IsEmpty);
        Assert.Equal(0, summary.Length);
        Assert.False(summary.IsTruncated);
        Assert.Equal(string.Empty, summary.ToString());
    }

    [Fact]
    public void AppendLine_PreservesNewlinesBetweenLines()
    {
        var summary = new JobResultSummary();

        summary.AppendLine("first").AppendLine("second");

        Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}", summary.ToString());
    }

    [Fact]
    public void Append_DoesNotAddALineBreak()
    {
        var summary = new JobResultSummary();

        summary.Append("a").Append("b");

        Assert.Equal("ab", summary.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Append_IgnoresNullAndEmpty(string? text)
    {
        var summary = new JobResultSummary();

        summary.Append(text);

        Assert.True(summary.IsEmpty);
    }

    [Fact]
    public void AppendLine_WithNull_WritesAnEmptyLine()
    {
        var summary = new JobResultSummary();

        summary.AppendLine(null);

        Assert.Equal(Environment.NewLine, summary.ToString());
    }

    [Fact]
    public void AppendSection_UnderlinesTheTitle_WithNoLeadingBlankLineWhenFirst()
    {
        var summary = new JobResultSummary();

        summary.AppendSection("Totals");

        Assert.Equal($"Totals{Environment.NewLine}------{Environment.NewLine}", summary.ToString());
    }

    [Fact]
    public void AppendSection_SeparatesFromPrecedingContent_WithABlankLine()
    {
        var summary = new JobResultSummary();
        summary.AppendLine("intro");

        summary.AppendSection("Totals");

        Assert.Equal(
            $"intro{Environment.NewLine}{Environment.NewLine}Totals{Environment.NewLine}------{Environment.NewLine}",
            summary.ToString());
    }

    [Fact]
    public void Clear_ResetsContentAndTruncatedState()
    {
        var summary = new JobResultSummary(maxLength: 32);
        summary.Append(new string('x', 200));
        Assert.True(summary.IsTruncated);

        summary.Clear();

        Assert.True(summary.IsEmpty);
        Assert.False(summary.IsTruncated);
        Assert.Equal(string.Empty, summary.ToString());
    }

    [Fact]
    public void Appends_PastTheLimit_TruncateAndStayWithinMaxLength()
    {
        var summary = new JobResultSummary(maxLength: 64);

        for (var i = 0; i < 100; i++)
            summary.AppendLine($"line {i} of a summary that will not fit");

        Assert.True(summary.IsTruncated);
        // The notice is budgeted for up front, so nothing downstream has to truncate a second time.
        Assert.True(summary.ToString().Length <= summary.MaxLength);
        Assert.EndsWith("truncated", summary.ToString());
    }

    [Fact]
    public void Truncation_KeepsWhatFits_RatherThanDroppingTheWholeAppend()
    {
        var summary = new JobResultSummary(maxLength: 64);

        summary.Append("KEEPME").Append(new string('x', 500));

        Assert.StartsWith("KEEPME", summary.ToString());
    }

    [Fact]
    public void AppendsAfterTruncation_AreDiscarded()
    {
        var summary = new JobResultSummary(maxLength: 32);
        summary.Append(new string('x', 500));
        var afterFirstOverflow = summary.ToString();

        summary.AppendLine("this should not appear");

        Assert.Equal(afterFirstOverflow, summary.ToString());
        Assert.DoesNotContain("should not appear", summary.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveMaxLength(int maxLength) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new JobResultSummary(maxLength));

    [Fact]
    public void DefaultConstructor_UsesDefaultMaxLength() =>
        Assert.Equal(JobResultSummary.DefaultMaxLength, new JobResultSummary().MaxLength);

    [Fact]
    public void Appends_FromParallelTasks_AreNotInterleavedOrLost()
    {
        // Jobs are free to fan work out across tasks — Log() is already safe there, and Summary
        // would be a trap if it were not.
        var summary = new JobResultSummary();

        Parallel.For(0, 200, i => summary.AppendLine($"line-{i:000}"));

        var lines = summary.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(200, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^line-\d{3}$", line));
        Assert.Equal(200, lines.Distinct().Count());
    }

    /// <summary>Whether any surrogate in the text is missing its partner.</summary>
    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;

                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Truncation_NeverSplitsASurrogatePair(int extraBudget)
    {
        // This is the truncation that runs on the normal path — the writer only ever sees text this
        // type has already bounded. Cutting between a high and a low surrogate stores half a code
        // point, which renders as a replacement glyph. The budget is varied so that at least one case
        // lands the cut inside a two-char emoji.
        var overhead = Environment.NewLine.Length + JobResultSummary.TruncationNotice.Length;
        var summary = new JobResultSummary(overhead + extraBudget);

        summary.Append(string.Concat(Enumerable.Repeat("\U0001F600", 20)));

        Assert.False(HasLoneSurrogate(summary.ToString()));
    }
}
