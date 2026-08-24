using OptiPowerTools.ScheduledJobsInsights.Components.Shared;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

/// <summary>
/// The detail page's incremental log fetch. Tested here rather than through the component because the
/// behaviour that matters only appears across two polls, and the poll interval is two seconds.
/// </summary>
public class LogEntryBufferTests
{
    private static IReadOnlyList<JobLogEntry> Lines(params int[] sequences) =>
        [.. sequences.Select(sequence => new JobLogEntry
        {
            Id = sequence,
            JobExecutionId = 1,
            Sequence = sequence,
            Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(sequence),
            Message = $"line {sequence}"
        })];

    private static int[] SequencesOf(LogEntryBuffer buffer) => [.. buffer.Entries.Select(e => e.Sequence)];

    /// <summary>
    /// A buffer whose bound cannot be reached, for the tests about resume points rather than capacity.
    /// There is deliberately no parameterless constructor: an unbounded buffer is the defect this
    /// bound exists to fix, so asking for one has to be explicit.
    /// </summary>
    private static LogEntryBuffer Unbounded() => new(int.MaxValue);

    [Fact]
    public void ANewBuffer_ResumesFromZero() => Assert.Equal(0, Unbounded().ResumeFrom);

    [Fact]
    public void AContiguousFetch_AdvancesTheResumePointToTheEnd()
    {
        var buffer = Unbounded();

        Assert.True(buffer.Merge(Lines(1, 2, 3)));

        Assert.Equal(3, buffer.ResumeFrom);
        Assert.Equal([1, 2, 3], SequencesOf(buffer));
    }

    [Fact]
    public void AGap_HoldsTheResumePointBelowIt()
    {
        // The whole point. The writer's channel-full fallback inserts one record synchronously while
        // earlier ones are still buffered, so 100 can reach the database before 95. Resuming from the
        // highest line seen would ask for everything after 100 and never fetch 95-99 again — the rows
        // exist, and only a manual reload would show them.
        var buffer = Unbounded();

        buffer.Merge(Lines(1, 2, 5));

        Assert.Equal(2, buffer.ResumeFrom);
    }

    [Fact]
    public void ALineArrivingOutOfOrder_IsStillShownImmediately()
    {
        // Withholding it until the gap filled would hide the tail of a log for ever if a batch were
        // genuinely dropped.
        var buffer = Unbounded();

        buffer.Merge(Lines(1, 2, 5));

        Assert.Equal([1, 2, 5], SequencesOf(buffer));
    }

    [Fact]
    public void AFilledGap_AdvancesTheResumePointPastEverythingAlreadyHeld()
    {
        var buffer = Unbounded();
        buffer.Merge(Lines(1, 2, 5));

        // The next poll resumes from 2 and so re-reads 5 alongside the late arrivals.
        buffer.Merge(Lines(3, 4, 5));

        Assert.Equal(5, buffer.ResumeFrom);
        Assert.Equal([1, 2, 3, 4, 5], SequencesOf(buffer));
    }

    [Fact]
    public void ARe_ReadLine_IsNotDuplicated()
    {
        // Overlapping re-reads are the cost of resuming from the contiguous point, so they have to be
        // free of side effects.
        var buffer = Unbounded();
        buffer.Merge(Lines(1, 2, 3));

        Assert.False(buffer.Merge(Lines(1, 2, 3)));

        Assert.Equal([1, 2, 3], SequencesOf(buffer));
        Assert.Equal(3, buffer.ResumeFrom);
    }

    [Fact]
    public void EntriesStayInSequenceOrder_HoweverTheyArrive()
    {
        // Virtualize renders the list as given; an out-of-order line would appear in the wrong place
        // in the console even after its neighbours arrived.
        var buffer = Unbounded();

        buffer.Merge(Lines(4, 1, 3));
        buffer.Merge(Lines(2));

        Assert.Equal([1, 2, 3, 4], SequencesOf(buffer));
        Assert.Equal(4, buffer.ResumeFrom);
    }

    [Fact]
    public void AnEmptyFetch_ChangesNothing()
    {
        var buffer = Unbounded();
        buffer.Merge(Lines(1, 2));

        Assert.False(buffer.Merge([]));

        Assert.Equal(2, buffer.ResumeFrom);
    }

    [Fact]
    public void TheBound_StopsTheBufferGrowing_AndSaysSo()
    {
        // The reason the bound lives here and not only on the query: a running execution is polled
        // every couple of seconds and each fetch asks only for what is new, so a query-side cap
        // bounds one fetch while the buffer keeps every line the run ever writes.
        var buffer = new LogEntryBuffer(maxEntries: 3);

        buffer.Merge(Lines(1, 2));
        Assert.False(buffer.Truncated);
        Assert.False(buffer.IsFull);

        var added = buffer.Merge(Lines(3, 4, 5));

        Assert.True(added);
        Assert.True(buffer.IsFull);
        Assert.True(buffer.Truncated);
        Assert.Equal([1, 2, 3], SequencesOf(buffer));
    }

    [Fact]
    public void AFullBuffer_RefusesEverythingFurther()
    {
        var buffer = new LogEntryBuffer(maxEntries: 2);
        buffer.Merge(Lines(1, 2));

        var added = buffer.Merge(Lines(3, 4));

        Assert.False(added);
        Assert.Equal([1, 2], SequencesOf(buffer));
        Assert.True(buffer.Truncated);
    }

    [Fact]
    public void AReReadLineDoesNotFlagTruncation_EvenWhenTheBufferIsExactlyFull()
    {
        // Duplicates are tested before capacity. Otherwise a full buffer re-reading a line it already
        // holds — which happens on every poll while a gap is open — would claim lines were dropped.
        var buffer = new LogEntryBuffer(maxEntries: 2);
        buffer.Merge(Lines(1, 2));

        var added = buffer.Merge(Lines(1, 2));

        Assert.False(added);
        Assert.True(buffer.IsFull);
        Assert.False(buffer.Truncated);
    }

    [Fact]
    public void ABufferFilledExactlyToItsBound_DoesNotClaimTruncation()
    {
        var buffer = new LogEntryBuffer(maxEntries: 3);

        buffer.Merge(Lines(1, 2, 3));

        Assert.True(buffer.IsFull);
        Assert.False(buffer.Truncated);
        Assert.Equal(3, buffer.ResumeFrom);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveBound_IsTreatedAsUnbounded(int bound)
    {
        // Matches how JobExecutionQueryService reads the same option. The validator rejects a
        // non-positive value at startup, so this only covers a buffer built directly.
        var buffer = new LogEntryBuffer(bound);

        buffer.Merge(Lines(1, 2, 3));

        Assert.False(buffer.IsFull);
        Assert.False(buffer.Truncated);
        Assert.Equal(3, buffer.Entries.Count);
    }

    [Fact]
    public void TheCharacterBudget_StopsTheBufferBeforeTheLineCountDoes()
    {
        // A line count is only a proxy for memory: multiplied by MaxLogMessageLength it permits far
        // more than it appears to, and the product is what a Blazor circuit holds per viewer.
        var buffer = new LogEntryBuffer(maxEntries: 1_000, maxCharacters: 20);

        buffer.Merge(Lines(1, 2, 3, 4, 5, 6));

        Assert.True(buffer.Truncated);
        Assert.True(buffer.IsFull);
        Assert.True(buffer.Entries.Sum(e => e.Message.Length) <= 20);
        Assert.True(buffer.Entries.Count < 6);
    }

    [Fact]
    public void AnOversizedFirstLine_IsKeptRatherThanLeavingTheLogEmpty()
    {
        // Refusing it would render a truncation notice above nothing at all, which reads as a bug.
        var buffer = new LogEntryBuffer(maxEntries: 1_000, maxCharacters: 1);

        Assert.True(buffer.Merge(Lines(1)));
        Assert.Single(buffer.Entries);
    }

    [Fact]
    public void OutOfOrderLines_AreStillSorted()
    {
        // Guards the skip-the-sort optimisation: the common tick arrives ordered and appends at the
        // tail, but the writer's channel-full fallback writes one record synchronously while earlier
        // ones are still buffered, so 100 can land before 95.
        var buffer = Unbounded();

        buffer.Merge(Lines(3, 1, 2));

        Assert.Equal([1, 2, 3], SequencesOf(buffer));
    }

    [Fact]
    public void OutOfOrderLines_AcrossTwoFetches_AreStillSorted()
    {
        var buffer = Unbounded();

        buffer.Merge(Lines(5, 4));
        buffer.Merge(Lines(1));

        Assert.Equal([1, 4, 5], SequencesOf(buffer));
    }
}
