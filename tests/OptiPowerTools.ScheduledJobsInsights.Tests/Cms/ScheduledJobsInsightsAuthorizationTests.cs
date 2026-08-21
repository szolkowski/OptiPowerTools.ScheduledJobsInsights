using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Extensions;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobsInsightsAuthorizationTests
{
    /// <summary>Builds a provider with the package registered, plus any host policies.</summary>
    private static IServiceProvider Host(
        Action<OptiPowerToolScheduledJobsInsightsOptions> configure,
        Action<AuthorizationOptions>? hostPolicies = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        if (hostPolicies is not null)
            services.AddAuthorization(hostPolicies);

        services.AddOptiPowerToolScheduledJobsInsights(options =>
        {
            options.ConnectionString = "Server=.;Database=x;Trusted_Connection=True;";
            configure(options);
        });

        return services.BuildServiceProvider();
    }

    private static AuthorizationPolicy PolicyFrom(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value
            .GetPolicy(ScheduledJobsInsightsAuthorization.PolicyName)!;

    private static ClaimsPrincipal User(params string[] roles)
    {
        var identity = new ClaimsIdentity(authenticationType: "Test");
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task ByDefault_OnlyTheConfiguredRolesAreAuthorized()
    {
        var provider = Host(options => options.AuthorizedRoles = ["Administrators"]);
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        Assert.True((await authorization.AuthorizeAsync(User("Administrators"), null, ScheduledJobsInsightsAuthorization.PolicyName)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(User("Editors"), null, ScheduledJobsInsightsAuthorization.PolicyName)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, ScheduledJobsInsightsAuthorization.PolicyName)).Succeeded);
    }

    [Fact]
    public async Task AllowAnyAuthenticatedUser_DropsTheRoleRequirementButNotAuthentication()
    {
        // The permissive mode is now named for what it does. On a site with front-end membership
        // this admits ordinary visitors, which is exactly why it has to be opted into explicitly.
        var provider = Host(options => options.AllowAnyAuthenticatedUser = true);
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        Assert.True((await authorization.AuthorizeAsync(User("Editors"), null, ScheduledJobsInsightsAuthorization.PolicyName)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, ScheduledJobsInsightsAuthorization.PolicyName)).Succeeded);
    }

    [Fact]
    public async Task AHostPolicy_ReplacesTheBuiltInRoleCheckEntirely()
    {
        var provider = Host(
            options =>
            {
                options.AuthorizationPolicy = "HostPolicy";
                // Deliberately contradictory: if the host policy wins, these roles are irrelevant.
                options.AuthorizedRoles = ["Administrators"];
            },
            hostPolicies: authorization => authorization.AddPolicy(
                "HostPolicy",
                policy => policy.RequireAuthenticatedUser().RequireRole("Operations")));

        var authorizationService = provider.GetRequiredService<IAuthorizationService>();

        Assert.True((await authorizationService.AuthorizeAsync(User("Operations"), null, ScheduledJobsInsightsAuthorization.PolicyName)).Succeeded);
        Assert.False((await authorizationService.AuthorizeAsync(User("Administrators"), null, ScheduledJobsInsightsAuthorization.PolicyName)).Succeeded);
    }

    [Fact]
    public void AMisspelledHostPolicy_FailsLoudly()
    {
        // Falling back to the built-in check would look like it worked while enforcing something the
        // host never asked for — the worst possible failure mode for an authorization setting.
        var provider = Host(options => options.AuthorizationPolicy = "NoSuchPolicy");

        var exception = Assert.Throws<InvalidOperationException>(() => PolicyFrom(provider));

        Assert.Contains("NoSuchPolicy", exception.Message, StringComparison.Ordinal);
    }
}
