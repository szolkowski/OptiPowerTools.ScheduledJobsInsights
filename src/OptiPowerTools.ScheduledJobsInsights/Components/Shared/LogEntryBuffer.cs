using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>
/// The log lines a detail page currently holds, and the sequence its next fetch should resume from.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the component so the merge can be tested without a second poll tick. The interesting
/// behaviour only shows across two fetches, and the poll interval is two seconds.
/// </para>
/// <para>
/// The point of the type is <see cref="ResumeFrom"/>: it is the highest <em>contiguous</em> sequence,
/// not the highest seen. Sequences come from an <c>Interlocked.Increment</c> and so have no gaps of
/// their own, but <c>JobExecutionWriter</c>'s channel-full fallback writes one record synchronously
/// while earlier ones are still buffered — so 100 can land before 95. Resuming from the highest seen
/// would request everything after 100 and never ask for 95 to 99 again; the rows exist, and only a
/// manual reload would ever show them.
/// </para>
/// <para>
/// Lines are exposed as soon as they arrive, gap or no gap. Withholding them until a gap filled would
/// hide the tail of a log for ever if a batch were genuinely dropped.
/// </para>
/// </remarks>
internal sealed class LogEntryBuffer
{
    private readonly List<JobLogEntry> _entries = [];
    private readonly HashSet<int> _seen = [];

    /// <summary>
    /// Everything held, in sequence order. Typed as the concrete list because <c>Virtualize</c>
    /// binds to an <see cref="ICollection{T}"/>.
    /// </summary>
    public List<JobLogEntry> Entries => _entries;

    /// <summary>Highest sequence with no gap below it — where the next fetch resumes.</summary>
    public int ResumeFrom { get; private set; }

    /// <summary>
    /// Merges a fetch, ignoring lines already held. Re-reading an overlap is expected whenever a gap
    /// is open, and is bounded by the size of that gap.
    /// </summary>
    /// <returns><c>true</c> if anything new was added.</returns>
    public bool Merge(IReadOnlyList<JobLogEntry> fetched)
    {
        var added = false;

        foreach (var entry in fetched)
        {
            if (!_seen.Add(entry.Sequence))
                continue;

            _entries.Add(entry);
            added = true;
        }

        if (!added)
            return false;

        _entries.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

        while (_seen.Contains(ResumeFrom + 1))
            ResumeFrom++;

        return true;
    }
}
