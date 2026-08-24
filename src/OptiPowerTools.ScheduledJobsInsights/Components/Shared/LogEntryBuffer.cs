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
/// <para>
/// The buffer is <em>bounded</em>, and this is where the bound has to live. Capping the query alone
/// bounds one fetch, not the page: a still-running execution is polled every couple of seconds and
/// each fetch asks only for lines after the last one held, so an unbounded buffer accumulates every
/// line the run ever writes — held in server memory, per circuit, per viewer, for as long as the tab
/// is open. A job logging a line per row is then an out-of-memory on the server rather than on the
/// reader's machine.
/// </para>
/// </remarks>
internal sealed class LogEntryBuffer
{
    private readonly List<JobLogEntry> _entries = [];
    private readonly HashSet<int> _seen = [];
    private readonly int _maxEntries;

    /// <summary>Creates a buffer holding at most <paramref name="maxEntries"/> lines.</summary>
    /// <param name="maxEntries">
    /// The bound. A non-positive value is treated as unbounded, matching how the query service reads
    /// the same option — the validator rejects one at startup, so this only covers a buffer built
    /// directly.
    /// </param>
    public LogEntryBuffer(int maxEntries) =>
        _maxEntries = maxEntries > 0 ? maxEntries : int.MaxValue;

    /// <summary>
    /// Everything held, in sequence order. Typed as the concrete list because <c>Virtualize</c>
    /// binds to an <see cref="ICollection{T}"/>.
    /// </summary>
    public List<JobLogEntry> Entries => _entries;

    /// <summary>Highest sequence with no gap below it — where the next fetch resumes.</summary>
    public int ResumeFrom { get; private set; }

    /// <summary>Whether the bound has been reached, so no further line can be held.</summary>
    /// <remarks>
    /// The caller checks this to stop fetching altogether. Without that it would keep issuing the
    /// same capped query every poll tick and discarding every row it returned.
    /// </remarks>
    public bool IsFull => _entries.Count >= _maxEntries;

    /// <summary>Whether at least one line was dropped because the bound was reached.</summary>
    /// <remarks>
    /// Distinct from <see cref="IsFull"/>, which is true the moment the buffer is exactly full and
    /// nothing has yet been refused. This one drives the notice in the UI: a reader looking at a
    /// truncated log must be told, or they read a partial log as a complete one.
    /// </remarks>
    public bool Truncated { get; private set; }

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
            // Duplicates are tested before capacity, so re-reading a line already held never flags
            // truncation. Only a genuinely new line that cannot be kept does.
            if (_seen.Contains(entry.Sequence))
                continue;

            if (_entries.Count >= _maxEntries)
            {
                Truncated = true;
                break;
            }

            _seen.Add(entry.Sequence);
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
