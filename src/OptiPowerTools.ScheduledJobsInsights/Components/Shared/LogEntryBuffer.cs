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
/// <para>
/// The bound is two bounds, because a line count alone does not describe the cost: multiplied by the
/// per-message limit it permits far more memory than it appears to. A character budget bounds the
/// text itself, and whichever is reached first stops the buffer.
/// </para>
/// </remarks>
internal sealed class LogEntryBuffer
{
    private readonly List<JobLogEntry> _entries = [];
    private readonly HashSet<int> _seen = [];
    private readonly int _maxEntries;
    private readonly int _maxCharacters;
    private int _characters;

    /// <summary>
    /// Creates a buffer bounded by both a line count and a total character budget.
    /// </summary>
    /// <param name="maxEntries">
    /// The line bound. A non-positive value is treated as unbounded, matching how the query service
    /// reads the same option — the validator rejects one at startup, so this only covers a buffer
    /// built directly.
    /// </param>
    /// <param name="maxCharacters">
    /// The text bound, in characters, and the one that actually describes memory: a line count
    /// multiplied by the per-message limit permits far more than it appears to. Non-positive is
    /// unbounded. Whichever bound is reached first stops the buffer.
    /// </param>
    public LogEntryBuffer(int maxEntries, int maxCharacters = 0)
    {
        _maxEntries = maxEntries > 0 ? maxEntries : int.MaxValue;
        _maxCharacters = maxCharacters > 0 ? maxCharacters : int.MaxValue;
    }

    /// <summary>
    /// Everything held, in sequence order. Typed as the concrete list because <c>Virtualize</c>
    /// binds to an <see cref="ICollection{T}"/>.
    /// </summary>
    public List<JobLogEntry> Entries => _entries;

    /// <summary>Whether the entries are already in sequence order.</summary>
    private bool IsSorted()
    {
        for (var i = 1; i < _entries.Count; i++)
        {
            if (_entries[i - 1].Sequence > _entries[i].Sequence)
                return false;
        }

        return true;
    }

    /// <summary>Highest sequence with no gap below it — where the next fetch resumes.</summary>
    public int ResumeFrom { get; private set; }

    /// <summary>Whether the bound has been reached, so no further line will be held.</summary>
    /// <remarks>
    /// <para>
    /// The caller checks this to stop fetching altogether. Without that it would keep issuing the
    /// same capped query every poll tick and discarding every row it returned.
    /// </para>
    /// <para>
    /// True once either bound has forced a line to be refused. The character budget is why this is
    /// expressed through <see cref="Truncated"/> rather than by comparing the running total: a
    /// budget with room for a short line but not the one that arrived has stopped the log just as
    /// surely, and a buffer that reported itself not-full there would have the page re-fetching and
    /// re-discarding on every tick — the exact waste this exists to prevent. A later, shorter line
    /// might technically have fit; the log is already truncated and says so, and predictable beats
    /// marginal here.
    /// </para>
    /// </remarks>
    public bool IsFull => _entries.Count >= _maxEntries || Truncated;

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

            // The first line is always kept, however long: refusing it would leave a reader looking at
            // an empty log with only a truncation notice to explain it.
            var length = entry.Message?.Length ?? 0;
            if (_entries.Count > 0 && _characters + (long)length > _maxCharacters)
            {
                Truncated = true;
                break;
            }

            _seen.Add(entry.Sequence);
            _entries.Add(entry);
            _characters += length;
            added = true;
        }

        if (!added)
            return false;

        // Only sort when something actually landed out of order. A fetch arrives ordered and appends
        // at the tail, so the common tick is already sorted and a full O(n log n) pass over as many as
        // MaxLogEntriesPerExecution rows — every tick, per circuit — bought nothing. Out-of-order does
        // happen: the writer's channel-full fallback writes one record synchronously while earlier
        // ones are still buffered, so 100 can land before 95.
        if (!IsSorted())
            _entries.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));

        while (_seen.Contains(ResumeFrom + 1))
            ResumeFrom++;

        return true;
    }
}
