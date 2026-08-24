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

        // Not RequirePositive: the truncation marker has to fit inside the limit, so a limit of 1
        // would produce a message shorter than the ellipsis replacing it.
        if (options.InterruptedExecutionThreshold < TimeSpan.Zero)
            failures.Add($"InterruptedExecutionThreshold cannot be negative (was {options.InterruptedExecutionThreshold}); use TimeSpan.Zero to disable the sweep.");

        if (options.MaxLogMessageLength is > 0 and < 16)
            failures.Add($"MaxLogMessageLength must be at least 16 (was {options.MaxLogMessageLength}), or zero to use the default.");

        if (options.LogFlushInterval <= TimeSpan.Zero)
            failures.Add($"LogFlushInterval must be greater than zero (was {options.LogFlushInterval}).");

        if (!IsUsableShellPath(options.CmsShellPath))
        {
            failures.Add($"CmsShellPath must be an absolute path with at least one segment and no query string "
                 + $"or fragment — \"{options.CmsShellPath}\" is not. It is used as a route template, a "
                 + "menu URL and the base for the UI's own links at the same time.");
        }

        if (!options.AllowAnyAuthenticatedUser
            && string.IsNullOrWhiteSpace(options.AuthorizationPolicy)
            && options.AuthorizedRoles.Count == 0)
        {
            failures.Add("AuthorizedRoles is empty, so nobody could reach the UI. Name at least one role, set "
                 + "AuthorizationPolicy to a policy of your own, or set AllowAnyAuthenticatedUser if "
                 + "access is already restricted elsewhere.");
        }

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
}
