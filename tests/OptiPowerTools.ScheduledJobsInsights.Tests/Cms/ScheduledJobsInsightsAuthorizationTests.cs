using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OptiPowerTools.ScheduledJobsInsights.Cms;
using OptiPowerTools.ScheduledJobsInsights.Configuration;
using OptiPowerTools.ScheduledJobsInsights.Extensions;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Cms;

public class ScheduledJobsInsightsAuthorizationTests
{
    /// <summary>Builds a provider with the package registered, plus any host policies.</summary>
    private static ServiceProvider Host(
        Action<OptiPowerToolsScheduledJobsInsightsOptions> configure,
        Action<AuthorizationOptions>? hostPolicies = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configureServices?.Invoke(services);
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        if (hostPolicies is not null)
            services.AddAuthorization(hostPolicies);

        services.AddOptiPowerToolsScheduledJobsInsights(options =>
        {
            options.ConnectionString = "Server=.;Database=x;Trusted_Connection=True;";
            configure(options);
        });

        return services.BuildServiceProvider();
    }

    private static AuthorizationPolicy PolicyFrom(ServiceProvider provider) =>
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
    public async Task AMisspelledHostPolicy_DeniesAccess_RatherThanFallingBackToTheBuiltInCheck()
    {
        // Falling back to the built-in role check would look like it worked while enforcing something
        // the host never asked for — the worst possible failure mode for an authorization setting. So
        // the policy resolves to deny-all: closed, not open, and not quietly something else.
        var provider = Host(options => options.AuthorizationPolicy = "NoSuchPolicy");

        var authorizationService = provider.GetRequiredService<IAuthorizationService>();
        var result = await authorizationService.AuthorizeAsync(
            User("Administrators"), null, ScheduledJobsInsightsAuthorization.PolicyName);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void AMisspelledHostPolicy_DoesNotThrow()
    {
        // It used to throw from PostConfigure. AuthorizationOptions is a single shared instance built
        // lazily at the first authorization decision *anywhere in the application*, so one mistyped
        // option string took down every [Authorize] endpoint in the CMS — not just this package's.
        var provider = Host(options => options.AuthorizationPolicy = "NoSuchPolicy");

        Assert.Null(Record.Exception(() => PolicyFrom(provider)));
    }

    [Fact]
    public void AMisspelledHostPolicy_IsReportedAtCritical()
    {
        // The failure is silent to the reader — the pages simply deny — so the log line is the only
        // thing that explains why.
        var logger = new RecordingLogger<ConfigureScheduledJobsInsightsAuthorization>();
        var provider = Host(
            options => options.AuthorizationPolicy = "NoSuchPolicy",
            configureServices: services =>
                services.AddSingleton<ILogger<ConfigureScheduledJobsInsightsAuthorization>>(logger));

        PolicyFrom(provider);

        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Critical && entry.Message.Contains("NoSuchPolicy", StringComparison.Ordinal));
    }
}
