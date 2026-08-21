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
internal sealed class OptiPowerToolScheduledJobsInsightsOptionsValidator
    : IValidateOptions<OptiPowerToolScheduledJobsInsightsOptions>
{
    public ValidateOptionsResult Validate(string? name, OptiPowerToolScheduledJobsInsightsOptions options)
    {
        List<string>? failures = null;

        void Fail(string message) => (failures ??= []).Add(message);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Fail("ConnectionString is required. Set OptiPowerTools:ScheduledJobsInsights:ConnectionString "
                 + "to the SQL Server database that should hold the execution history.");
        }

        RequirePositive(options.LogChannelCapacity, nameof(options.LogChannelCapacity), Fail);
        RequirePositive(options.LogBatchSize, nameof(options.LogBatchSize), Fail);
        RequirePositive(options.PageSize, nameof(options.PageSize), Fail);
        RequirePositive(options.CleanupBatchSize, nameof(options.CleanupBatchSize), Fail);

        if (options.LogFlushInterval <= TimeSpan.Zero)
            Fail($"LogFlushInterval must be greater than zero (was {options.LogFlushInterval}).");

        if (!IsUsableShellPath(options.CmsShellPath))
        {
            Fail($"CmsShellPath must be an absolute path with at least one segment and no query string "
                 + $"or fragment — \"{options.CmsShellPath}\" is not. It is used as a route template, a "
                 + "menu URL and the base for the UI's own links at the same time.");
        }

        if (!options.AllowAnyAuthenticatedUser
            && string.IsNullOrWhiteSpace(options.AuthorizationPolicy)
            && options.AuthorizedRoles.Count == 0)
        {
            Fail("AuthorizedRoles is empty, so nobody could reach the UI. Name at least one role, set "
                 + "AuthorizationPolicy to a policy of your own, or set AllowAnyAuthenticatedUser if "
                 + "access is already restricted elsewhere.");
        }

        return failures is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(int value, string optionName, Action<string> fail)
    {
        if (value <= 0)
            fail($"{optionName} must be greater than zero (was {value}).");
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
