using System.Text;

namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// Builds the optional multi-line report a job may attach to its execution, shown as the
/// <em>Result summary</em> section of the execution detail view.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately separate from the string <c>ExecuteJob()</c> returns. That value is
/// Optimizely's "last execution message" and is rendered in a single grid cell of the CMS admin, so
/// it has to stay short; a summary has no such constraint and keeps its newlines.
/// </para>
/// <para>
/// A <see cref="StringBuilder"/> is wrapped rather than exposed so appends can be bounded: the
/// summary is persisted as a single unbounded column, and a job appending one line per processed
/// row could otherwise write megabytes into every execution. Once <see cref="MaxLength"/> is
/// reached further appends are discarded and <see cref="ToString"/> ends with a truncation notice,
/// so the result never exceeds <see cref="MaxLength"/> characters.
/// </para>
/// <para>
/// Appends are synchronized, matching the thread-safety of
/// <see cref="LoggedScheduledJobBase.Log"/> — a job that fans work out across tasks can append from
/// any of them.
/// </para>
/// </remarks>
public sealed class JobResultSummary
{
    /// <summary>Character limit applied when no explicit one is given — 100,000.</summary>
    public const int DefaultMaxLength = 100_000;

    /// <summary>
    /// Appended when content had to be dropped. Internal rather than private so
    /// <see cref="JobExecutionWriter"/> can bound a directly-written summary the same way.
    /// </summary>
    internal const string TruncationNotice = "… summary truncated";

    private readonly StringBuilder _builder = new();
    private readonly Lock _gate = new();

    /// <summary>Characters available for content, leaving room for <see cref="TruncationNotice"/>.</summary>
    private readonly int _contentBudget;

    private bool _truncated;

    /// <summary>Creates a summary bounded by <see cref="DefaultMaxLength"/>.</summary>
    public JobResultSummary()
        : this(DefaultMaxLength)
    {
    }

    /// <summary>Creates a summary bounded by <paramref name="maxLength"/> characters.</summary>
    /// <param name="maxLength">Maximum length of the rendered summary. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="maxLength"/> is not positive.</exception>
    public JobResultSummary(int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        MaxLength = maxLength;

        // Reserving the notice's length up front keeps ToString() within MaxLength, so nothing
        // downstream has to truncate a second time (and risk stacking two notices).
        _contentBudget = Math.Max(1, maxLength - (Environment.NewLine.Length + TruncationNotice.Length));
    }

    /// <summary>Maximum length of the rendered summary, including any truncation notice.</summary>
    public int MaxLength { get; }

    /// <summary>Whether nothing has been appended yet. An empty summary is never persisted.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
                return _builder.Length == 0;
        }
    }

    /// <summary>Characters appended so far, excluding any truncation notice.</summary>
    public int Length
    {
        get
        {
            lock (_gate)
                return _builder.Length;
        }
    }

    /// <summary>Whether the content limit was reached and later appends were discarded.</summary>
    public bool IsTruncated
    {
        get
        {
            lock (_gate)
                return _truncated;
        }
    }

    /// <summary>Appends text as-is, without a trailing line break.</summary>
    /// <param name="text">Text to append. Null and empty are no-ops.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public JobResultSummary Append(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return this;

        lock (_gate)
            AppendCore(text);

        return this;
    }

    /// <summary>Appends a line break.</summary>
    /// <returns>This instance, so calls can be chained.</returns>
    public JobResultSummary AppendLine()
    {
        lock (_gate)
            AppendCore(Environment.NewLine);

        return this;
    }

    /// <summary>Appends text followed by a line break.</summary>
    /// <param name="text">Text to append. Null is treated as an empty line.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public JobResultSummary AppendLine(string? text)
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(text))
                AppendCore(text);

            AppendCore(Environment.NewLine);
        }

        return this;
    }

    /// <summary>
    /// Appends an underlined heading, preceded by a blank line unless the summary is still empty.
    /// Long summaries are far easier to scan when broken into headed blocks.
    /// </summary>
    /// <param name="title">Heading text.</param>
    /// <returns>This instance, so calls can be chained.</returns>
    public JobResultSummary AppendSection(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        lock (_gate)
        {
            if (_builder.Length > 0)
                AppendCore(Environment.NewLine);

            AppendCore(title);
            AppendCore(Environment.NewLine);
            AppendCore(new string('-', title.Length));
            AppendCore(Environment.NewLine);
        }

        return this;
    }

    /// <summary>Discards everything appended so far, including the truncated state.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _builder.Clear();
            _truncated = false;
        }
    }

    /// <summary>The rendered summary, never longer than <see cref="MaxLength"/> characters.</summary>
    public override string ToString()
    {
        lock (_gate)
        {
            return _truncated
                ? _builder + Environment.NewLine + TruncationNotice
                : _builder.ToString();
        }
    }

    /// <summary>Appends within the content budget. Callers must hold <see cref="_gate"/>.</summary>
    private void AppendCore(string text)
    {
        if (_truncated)
            return;

        var remaining = _contentBudget - _builder.Length;
        if (text.Length <= remaining)
        {
            _builder.Append(text);
            return;
        }

        // Keep whatever still fits rather than dropping the whole append — a partial last line reads
        // better than a summary that stops one character short of the limit.
        _builder.Append(text, 0, Math.Max(0, remaining));
        _truncated = true;
    }
}
