using Microsoft.Extensions.Options;

namespace OptiPowerTools.ScheduledJobsInsights.Configuration;

/// <summary>
/// Validates the package options at startup.
/// </summary>
/// <remarks>
/// <para>
/// Fail-fast is the exception to this package's usual rule. Everywhere else a failure costs history
/// rather than the run, because the alternative is letting a reporting problem stop real work. A
/// misconfigured option is different: it is discovered before any job has run, it will not fix
/// itself, and every one of the cases below degrades *silently* — a zero batch size spins a core
/// while writing nothing, an empty connection string leaves a menu entry that 500s, and a job that
/// records nothing looks exactly like a job that was never run.
/// </para>
/// <para>
/// Every message names the option and says what a valid value looks like, since the person reading
/// it has a stack trace at startup and no other context.
/// </para>
/// </remarks>
internal sealed class OptiPowerToolsScheduledJobsInsightsOptionsValidator
    : IValidateOptions<OptiPowerToolsScheduledJobsInsightsOptions>
{
    public ValidateOptionsResult Validate(string? name, OptiPowerToolsScheduledJobsInsightsOptions options)
    {
        // A plain list rather than a lazily-created one behind a local function: the closure made the
        // final null check unanalysable, and one always-allocated empty list at startup is nothing.
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("ConnectionString is required. Set OptiPowerTools:ScheduledJobsInsights:ConnectionString "
                 + "to the SQL Server database that should hold the execution history.");
        }

        RequirePositive(options.LogChannelCapacity, nameof(options.LogChannelCapacity), failures);
        RequirePositive(options.LogBatchSize, nameof(options.LogBatchSize), failures);
        RequirePositive(options.PageSize, nameof(options.PageSize), failures);
        RequirePositive(options.CleanupBatchSize, nameof(options.CleanupBatchSize), failures);
        RequirePositive(options.MaxLogEntriesPerExecution, nameof(options.MaxLogEntriesPerExecution), failures);
        RequirePositive(options.MaxLogCharactersPerExecution, nameof(options.MaxLogCharactersPerExecution), failures);

        if (options.InterruptedExecutionThreshold < TimeSpan.Zero)
            failures.Add($"InterruptedExecutionThreshold cannot be negative (was {options.InterruptedExecutionThreshold}); use TimeSpan.Zero to disable the sweep.");

        // Both of these are floors rather than "must be positive": the truncation marker has to fit
        // inside the limit. Below that, the notice appended to a cut value is longer than the value it
        // replaced, so the stored text comes out *longer* than the configured maximum — the one thing
        // the option is there to guarantee. (This comment used to sit above the check above, which is
        // not what it describes.)
        if (options.MaxLogMessageLength is > 0 and < 16)
            failures.Add($"MaxLogMessageLength must be at least 16 (was {options.MaxLogMessageLength}), or zero to use the default.");

        if (options.MaxResultSummaryLength > 0 && options.MaxResultSummaryLength < MinimumSummaryLength)
            failures.Add($"MaxResultSummaryLength must be at least {MinimumSummaryLength} (was {options.MaxResultSummaryLength}), or zero to use the default — below that, the truncation notice is longer than the summary it replaces.");

        if (options.LogFlushInterval <= TimeSpan.Zero)
            failures.Add($"LogFlushInterval must be greater than zero (was {options.LogFlushInterval}).");

        // Zero or negative would spin the detail page's PeriodicTimer as fast as the database answers,
        // one query per tick per open page.
        if (options.DetailPollInterval <= TimeSpan.Zero)
            failures.Add($"DetailPollInterval must be greater than zero (was {options.DetailPollInterval}).");

        if (!IsUsableShellPath(options.CmsShellPath))
        {
            failures.Add($"CmsShellPath must be an absolute path with at least one segment and no query string "
                 + $"or fragment — \"{options.CmsShellPath}\" is not. It is used as a route template, a "
                 + "menu URL and the base for the UI's own links at the same time.");
        }

        if (!IsUsableShellPath(options.CmsRetentionPath))
        {
            failures.Add($"CmsRetentionPath must be an absolute path with at least one segment and no query string "
                 + $"or fragment — \"{options.CmsRetentionPath}\" is not. It is a route template and a menu URL, "
                 + "like CmsShellPath.");
        }

        // Not implied by the two checks above: both values can be individually valid and still be the
        // same path. Two actions on one route template is an AmbiguousMatchException on every request
        // to the UI, and the CMS shell — which highlights the entry whose URL equals the request path —
        // would have two entries claiming the same one.
        if (IsUsableShellPath(options.CmsShellPath)
            && IsUsableShellPath(options.CmsRetentionPath)
            && string.Equals(options.CmsShellPath, options.CmsRetentionPath, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"CmsRetentionPath must differ from CmsShellPath (both are \"{options.CmsShellPath}\"). "
                 + "The retention screen has a path of its own so the CMS menu can highlight it: the shell matches "
                 + "the request path against each menu item's URL and ignores the query string.");
        }

        // AuthorizedRoles being empty is no longer a misconfiguration: it is the default, and it
        // resolves to the built-in role set. Nobody can lock themselves out by leaving it unset, and a
        // host that wants a narrower rule than "one of these roles" names a policy instead.

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(int value, string optionName, List<string> failures)
    {
        if (value <= 0)
            failures.Add($"{optionName} must be greater than zero (was {value}).");
    }

    /// <summary>
    /// Whether the configured shell path can serve as a route template and a menu URL at once.
    /// </summary>
    private static bool IsUsableShellPath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && path[0] == '/'
        && path.Length > 1
        && path[^1] != '/'
        && path.IndexOfAny(['?', '#', ' ']) < 0;

    /// <summary>
    /// Smallest usable <c>MaxResultSummaryLength</c>: the truncation notice plus its newline, plus one
    /// character of actual summary.
    /// </summary>
    /// <remarks>
    /// Derived rather than hard-coded, so it cannot drift away from the notice it is measuring.
    /// </remarks>
    private static readonly int MinimumSummaryLength =
        Environment.NewLine.Length + Logging.JobResultSummary.TruncationNotice.Length + 1;
}
