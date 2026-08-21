using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Retention;

public class RetentionPeriodTests
{
    [Fact]
    public void OfDays_ProducesACutoffThatFarBack()
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(now.AddDays(-30), RetentionPeriod.OfDays(30).CutoffFrom(now));
    }

    [Fact]
    public void Indefinite_HasNoCutoff_SoNothingIsEverEligible()
    {
        // The cleanup job keys off exactly this: no cutoff means skip the job entirely.
        Assert.Null(RetentionPeriod.Indefinite.CutoffFrom(DateTimeOffset.UtcNow));
        Assert.True(RetentionPeriod.Indefinite.IsIndefinite);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void OfDays_RefusesAValueThatWouldDeleteEverything(int days)
    {
        // Zero puts the cutoff at "now" and negative puts it in the future. Either deletes the whole
        // history for that job, including the run currently in progress — whose disappearing row
        // then breaks the foreign key for its own buffered log lines.
        Assert.Throws<ArgumentOutOfRangeException>(() => RetentionPeriod.OfDays(days));
    }

    [Fact]
    public void ADefaultInstance_IsIndefinite()
    {
        // Of the things a zeroed struct could mean, keeping everything is the only harmless one.
        Assert.True(default(RetentionPeriod).IsIndefinite);
        Assert.Null(default(RetentionPeriod).CutoffFrom(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void FromStoredValue_ReadsNullAsIndefinite() =>
        Assert.True(RetentionPeriod.FromStoredValue(null)!.Value.IsIndefinite);

    [Fact]
    public void FromStoredValue_ReadsAPositiveDayCount() =>
        Assert.Equal(RetentionPeriod.OfDays(90), RetentionPeriod.FromStoredValue(90));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void FromStoredValue_ReportsAnUnusableStoredValue_RatherThanObeyingIt(int stored)
    {
        // Storage is outside this type's control: a hand-edited row, a restored backup, a script.
        // Reading has to cope with what writing would have refused.
        Assert.Null(RetentionPeriod.FromStoredValue(stored));
    }

    [Fact]
    public void FromAttribute_ReadsAPositiveDayCount() =>
        Assert.Equal(RetentionPeriod.OfDays(7), RetentionPeriod.FromAttribute(new JobRetentionAttribute(7)));

    [Fact]
    public void FromAttribute_ReadsIndefinite() =>
        Assert.Equal(
            RetentionPeriod.Indefinite,
            RetentionPeriod.FromAttribute(new JobRetentionAttribute(JobRetentionAttribute.Indefinite)));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void FromAttribute_RejectsValuesItCannotActOn(int days)
    {
        // Returning null rather than throwing: an attribute cannot fail usefully at startup, so a bad
        // value falls back to the default and is flagged in the retention screen instead.
        Assert.Null(RetentionPeriod.FromAttribute(new JobRetentionAttribute(days)));
    }
}
