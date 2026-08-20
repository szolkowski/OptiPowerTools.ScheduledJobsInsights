using System.Globalization;
using OptiPowerTools.ScheduledJobsInsights.Components.Shared;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

public class DisplayFormatTests
{
    /// <summary>
    /// A culture whose separators are the mirror image of the invariant ones, so any accidental
    /// reliance on the ambient culture shows up as a swapped comma and period rather than silently
    /// passing on a developer machine that happens to be set to en-US.
    /// </summary>
    private static readonly CultureInfo Mismatched = new("pl-PL");

    private static T InCulture<T>(CultureInfo culture, Func<T> render)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return render();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(310.994, "310.99")]
    [InlineData(1864, "1864")]
    public void MetricValue_UsesAPeriodDecimalSeparator(double value, string expected) =>
        Assert.Equal(expected, DisplayFormat.MetricValue(value));

    [Theory]
    [InlineData(1, "1 line")]
    [InlineData(0, "0 lines")]
    [InlineData(2019, "2,019 lines")]
    public void LineCount_GroupsThousandsWithCommas(int count, string expected) =>
        Assert.Equal(expected, DisplayFormat.LineCount(count));

    [Fact]
    public void Duration_ShowsAnEmDash_WhileStillRunning() =>
        Assert.Equal("—", DisplayFormat.Duration(DateTimeOffset.UtcNow, completedAt: null));

    [Theory]
    [InlineData(310, "310 ms")]
    [InlineData(999, "999 ms")]
    [InlineData(1000, "1.0 s")]
    [InlineData(60_400, "60.4 s")]
    public void Duration_SwitchesToSecondsAtOneSecond(int elapsedMs, string expected)
    {
        var start = new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, DisplayFormat.Duration(start, start.AddMilliseconds(elapsedMs)));
    }

    [Fact]
    public void EveryFormatter_IgnoresTheAmbientCulture()
    {
        // The whole point of this class. Under pl-PL the ambient separators are inverted — "1 234,5"
        // where invariant gives "1,234.5" — so a formatter that forgot its IFormatProvider fails here
        // while passing everywhere else.
        var start = new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero);

        var rendered = InCulture(Mismatched, () => new[]
        {
            DisplayFormat.MetricValue(310.994),
            DisplayFormat.LineCount(2019),
            DisplayFormat.Duration(start, start.AddMilliseconds(60_400))
        });

        Assert.Equal(["310.99", "2,019 lines", "60.4 s"], rendered);
    }

    [Fact]
    public void ResultSummaryBadge_AlsoIgnoresTheAmbientCulture()
    {
        var summary = string.Concat(Enumerable.Repeat("x\n", 2000));

        Assert.Equal("2,000 lines · 3.9 KB", InCulture(Mismatched, () => ResultSummaryBadge.Format(summary)));
    }
}
