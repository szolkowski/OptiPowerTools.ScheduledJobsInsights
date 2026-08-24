using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Configuration;

namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// The single authorization policy guarding everything this package exposes — the CMS shell page,
/// the retention screen and the menu entries.
/// </summary>
/// <remarks>
/// One named policy rather than a check written into the controller action, so that authorization is
/// ordinary endpoint metadata: the framework enforces it, the menu can ask the same question through
/// <see cref="IAuthorizationService"/>, and a host can substitute its own rules wholesale through
/// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.AuthorizationPolicy"/>.
/// </remarks>
public static class ScheduledJobsInsightsAuthorization
{
    /// <summary>
    /// Name of the policy applied to this package's endpoints. Referenced by
    /// <c>[Authorize(Policy = …)]</c>; a host normally configures it through
    /// <see cref="OptiPowerToolsScheduledJobsInsightsOptions.AuthorizationPolicy"/> rather than
    /// redefining it.
    /// </summary>
    public const string PolicyName = "OptiPowerTools.ScheduledJobsInsights";
}

/// <summary>
/// Builds <see cref="ScheduledJobsInsightsAuthorization.PolicyName"/> from the package options.
/// </summary>
/// <remarks>
/// Post-configure rather than configure: a policy the host registered with its own
/// <c>AddAuthorization(...)</c> call has to already exist before it can be adopted here.
/// </remarks>
internal sealed partial class ConfigureScheduledJobsInsightsAuthorization : IPostConfigureOptions<AuthorizationOptions>
{
    private readonly OptiPowerToolsScheduledJobsInsightsOptions _options;
    private readonly ILogger<ConfigureScheduledJobsInsightsAuthorization> _logger;

    public ConfigureScheduledJobsInsightsAuthorization(
        IOptions<OptiPowerToolsScheduledJobsInsightsOptions> options,
        ILogger<ConfigureScheduledJobsInsightsAuthorization> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void PostConfigure(string? name, AuthorizationOptions options)
    {
        options.AddPolicy(ScheduledJobsInsightsAuthorization.PolicyName, Resolve(options));
    }

    private AuthorizationPolicy Resolve(AuthorizationOptions options)
    {
        // Read once into a local: it is used three times below.
        var policyName = _options.AuthorizationPolicy;

        if (!string.IsNullOrWhiteSpace(policyName))
        {
            var configured = options.GetPolicy(policyName);
            if (configured is not null)
                return configured;

            // Loud, but scoped to this package. Throwing here would be far worse than it looks: this
            // runs from PostConfigure on the application's single shared AuthorizationOptions, which
            // is resolved lazily at the *first authorization decision anywhere in the CMS* — so one
            // mistyped option string took down every [Authorize] endpoint in the host, not just ours.
            // Denying access to our own endpoints is the honest failure: the misconfiguration is
            // reported at Critical, and nothing is silently left open.
            LogUnregisteredPolicy(_logger, policyName);

            return new AuthorizationPolicyBuilder()
                .RequireAssertion(_ => false)
                .Build();
        }

        var builder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();

        if (!_options.AllowAnyAuthenticatedUser)
            builder = builder.RequireRole(_options.AuthorizedRoles);

        return builder.Build();
    }

    /// <summary>
    /// Source-generated so the call allocates nothing — no <c>params object?[]</c> for the arguments,
    /// which is what <c>CA1873</c> objects to in the plain <c>LogCritical</c> form.
    /// </summary>
    /// <remarks>
    /// The alternative the rule suggests — wrapping the call in <c>IsEnabled(LogLevel.Critical)</c> —
    /// would be the wrong shape here: this is the one message an operator must not miss, and guarding
    /// it to save an allocation on a once-per-startup path optimises the wrong thing.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "ScheduledJobsInsights is configured to use the authorization policy '{PolicyName}', but no policy with that name is registered. Access to the insights pages is denied until this is fixed. Register the policy with AddAuthorization(options => options.AddPolicy(...)), or clear AuthorizationPolicy to authorize on AuthorizedRoles instead.")]
    private static partial void LogUnregisteredPolicy(ILogger logger, string? policyName);
}
