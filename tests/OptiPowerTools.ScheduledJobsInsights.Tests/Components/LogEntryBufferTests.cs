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

    [Fact]
    public void ANewBuffer_ResumesFromZero() => Assert.Equal(0, new LogEntryBuffer().ResumeFrom);

    [Fact]
    public void AContiguousFetch_AdvancesTheResumePointToTheEnd()
    {
        var buffer = new LogEntryBuffer();

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
        var buffer = new LogEntryBuffer();

        buffer.Merge(Lines(1, 2, 5));

        Assert.Equal(2, buffer.ResumeFrom);
    }

    [Fact]
    public void ALineArrivingOutOfOrder_IsStillShownImmediately()
    {
        // Withholding it until the gap filled would hide the tail of a log for ever if a batch were
        // genuinely dropped.
        var buffer = new LogEntryBuffer();

        buffer.Merge(Lines(1, 2, 5));

        Assert.Equal([1, 2, 5], SequencesOf(buffer));
    }

    [Fact]
    public void AFilledGap_AdvancesTheResumePointPastEverythingAlreadyHeld()
    {
        var buffer = new LogEntryBuffer();
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
        var buffer = new LogEntryBuffer();
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
        var buffer = new LogEntryBuffer();

        buffer.Merge(Lines(4, 1, 3));
        buffer.Merge(Lines(2));

        Assert.Equal([1, 2, 3, 4], SequencesOf(buffer));
        Assert.Equal(4, buffer.ResumeFrom);
    }

    [Fact]
    public void AnEmptyFetch_ChangesNothing()
    {
        var buffer = new LogEntryBuffer();
        buffer.Merge(Lines(1, 2));

        Assert.False(buffer.Merge([]));

        Assert.Equal(2, buffer.ResumeFrom);
    }
}
