using Microsoft.AspNetCore.Authorization;
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
/// <see cref="OptiPowerToolScheduledJobsInsightsOptions.AuthorizationPolicy"/>.
/// </remarks>
public static class ScheduledJobsInsightsAuthorization
{
    /// <summary>
    /// Name of the policy applied to this package's endpoints. Referenced by
    /// <c>[Authorize(Policy = …)]</c>; a host normally configures it through
    /// <see cref="OptiPowerToolScheduledJobsInsightsOptions.AuthorizationPolicy"/> rather than
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
internal sealed class ConfigureScheduledJobsInsightsAuthorization : IPostConfigureOptions<AuthorizationOptions>
{
    private readonly OptiPowerToolScheduledJobsInsightsOptions _options;

    public ConfigureScheduledJobsInsightsAuthorization(IOptions<OptiPowerToolScheduledJobsInsightsOptions> options)
    {
        _options = options.Value;
    }

    public void PostConfigure(string? name, AuthorizationOptions options)
    {
        options.AddPolicy(ScheduledJobsInsightsAuthorization.PolicyName, Resolve(options));
    }

    private AuthorizationPolicy Resolve(AuthorizationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(_options.AuthorizationPolicy))
        {
            // Loudly, not silently: a misspelled policy name that fell back to the built-in check
            // would look like it was working while enforcing something the host did not ask for.
            return options.GetPolicy(_options.AuthorizationPolicy)
                ?? throw new InvalidOperationException(
                    $"OptiPowerTools.ScheduledJobsInsights is configured to use the authorization policy '{_options.AuthorizationPolicy}', but no policy with that name is registered. Register it with AddAuthorization(options => options.AddPolicy(...)), or clear AuthorizationPolicy to authorize on AuthorizedRoles instead.");
        }

        var builder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();

        if (!_options.AllowAnyAuthenticatedUser)
            builder = builder.RequireRole(_options.AuthorizedRoles);

        return builder.Build();
    }
}
