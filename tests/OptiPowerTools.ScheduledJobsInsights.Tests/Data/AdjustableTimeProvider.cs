namespace OptiPowerTools.ScheduledJobsInsights.Tests.Data;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test moves it, so time-based behaviour
/// can be asserted without any test actually waiting.
/// </summary>
internal sealed class AdjustableTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
