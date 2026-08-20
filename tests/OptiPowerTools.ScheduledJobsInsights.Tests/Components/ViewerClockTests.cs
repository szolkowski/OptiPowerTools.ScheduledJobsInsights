using OptiPowerTools.ScheduledJobsInsights.Components.Shared;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

public class ViewerClockTests
{
    /// <summary>
    /// A zone with a whole-hour offset *and* daylight saving, so both the conversion and the
    /// summer/winter difference are observable. UTC+1 in winter, UTC+2 in summer.
    /// </summary>
    private const string Warsaw = "Europe/Warsaw";

    /// <summary>A half-hour offset, which catches an implementation that assumes whole hours.</summary>
    private const string Kolkata = "Asia/Kolkata";

    /// <summary>Negative offset, to prove the sign is rendered rather than assumed positive.</summary>
    private const string NewYork = "America/New_York";

    [Fact]
    public void Timestamp_ConvertsToTheViewerZone_AndLabelsTheOffset()
    {
        var summer = new DateTimeOffset(2026, 8, 19, 15, 37, 16, TimeSpan.Zero);

        Assert.Equal("2026-08-19 17:37:16 UTC+02:00", ViewerClock.ForZone(Warsaw).Timestamp(summer));
    }

    [Fact]
    public void Timestamp_UsesTheOffsetInForceAtThatInstant_NotToday()
    {
        // The reason the browser's *zone id* is passed rather than its current offset. These two
        // instants are in the same zone but on opposite sides of a daylight-saving change; an
        // implementation that captured one offset and reused it renders one of them an hour out.
        var clock = ViewerClock.ForZone(Warsaw);
        var winter = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var summer = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal("2026-01-15 13:00:00 UTC+01:00", clock.Timestamp(winter));
        Assert.Equal("2026-07-15 14:00:00 UTC+02:00", clock.Timestamp(summer));
    }

    [Fact]
    public void Timestamp_RendersANegativeOffsetWithItsSign()
    {
        var value = new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal("2026-08-19 11:00:00 UTC-04:00", ViewerClock.ForZone(NewYork).Timestamp(value));
    }

    [Fact]
    public void Timestamp_HandlesAHalfHourOffset()
    {
        var value = new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal("2026-08-19 20:30:00 UTC+05:30", ViewerClock.ForZone(Kolkata).Timestamp(value));
    }

    [Fact]
    public void Timestamp_InUtc_SaysUtcRatherThanPlusZero() =>
        Assert.Equal(
            "2026-08-19 15:00:00 UTC",
            ViewerClock.Utc.Timestamp(new DateTimeOffset(2026, 8, 19, 15, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void CompactTimestamp_ConvertsButOmitsSecondsAndOffset() =>
        Assert.Equal(
            "2026-08-19 17:37",
            ViewerClock.ForZone(Warsaw).CompactTimestamp(new DateTimeOffset(2026, 8, 19, 15, 37, 16, TimeSpan.Zero)));

    [Fact]
    public void TimeOfDay_ConvertsAndKeepsMilliseconds() =>
        Assert.Equal(
            "17:37:16.123",
            ViewerClock.ForZone(Warsaw).TimeOfDay(new DateTimeOffset(2026, 8, 19, 15, 37, 16, 123, TimeSpan.Zero)));

    [Fact]
    public void ConvertingAValueThatAlreadyCarriesAnOffset_DoesNotDoubleCount()
    {
        // Stored values are DateTimeOffset, so they already know their offset. Converting must
        // re-express the same instant, not add one offset on top of another.
        var value = new DateTimeOffset(2026, 8, 19, 17, 0, 0, TimeSpan.FromHours(2)); // 15:00 UTC

        Assert.Equal("2026-08-19 17:00:00 UTC+02:00", ViewerClock.ForZone(Warsaw).Timestamp(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not/A/Real/Zone")]
    [InlineData("Europe/Warsaw; DROP TABLE")]          // cookie contents are untrusted input
    [InlineData("../../etc/localtime")]
    [InlineData("<script>alert(1)</script>")]
    public void ForZone_FallsBackToUtc_ForAnythingUnusable(string? zoneId)
    {
        var clock = ViewerClock.ForZone(zoneId);

        Assert.True(clock.IsUtcFallback);
        Assert.Equal("UTC", clock.Label);
    }

    [Fact]
    public void ForZone_FallsBackToUtc_ForAnAbsurdlyLongId() =>
        Assert.True(ViewerClock.ForZone(new string('a', 500)).IsUtcFallback);

    [Fact]
    public void ForZone_TreatsAnExplicitUtcIdAsTheUtcFallback()
    {
        // Not a failure, but it should report itself the same way so the page says "UTC" once.
        var clock = ViewerClock.ForZone("UTC");

        Assert.True(clock.IsUtcFallback);
        Assert.Equal("UTC", clock.Label);
    }

    [Fact]
    public void ForZone_ReportsTheResolvedZoneAsItsLabel()
    {
        var clock = ViewerClock.ForZone(Warsaw);

        Assert.False(clock.IsUtcFallback);
        Assert.Equal(Warsaw, clock.Label);
    }
}
