namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The stack a run is pointed at, and what differs between the CMS versions as far as a browser can
/// tell. Chosen by <c>SJI_E2E_TARGET</c> (<c>cms13</c> or <c>cms12</c>), which scripts/e2e.sh sets;
/// with it unset there is no target and every test skips.
/// </summary>
internal sealed record E2ETarget(
    string Name,
    Uri BaseUrl,
    string ConnectionString,
    string PackageId,
    string AdminPath,
    string ScheduledJobsAdmin,
    bool DetailPageHighlightsMenu)
{
    private const string Password = "Episerver123!";

    /// <summary>The administrator the fixture registers on the stack's empty database.</summary>
    public const string AdminUser = "e2eadmin";

    /// <summary>A throwaway stack's throwaway password - the same placeholder the dev stacks use.</summary>
    public const string AdminPassword = Password;

    /// <summary>
    /// A CMS editor: signed in and allowed into the CMS, but not in a role the insights pages accept.
    /// </summary>
    public const string EditorUser = "e2eeditor";

    /// <summary>The CMS's editing UI, which an editor may open.</summary>
    public string CmsEditPath => $"{AdminPath}/CMS/";

    public static E2ETarget? Current { get; } = Resolve(Environment.GetEnvironmentVariable("SJI_E2E_TARGET"));

    /// <summary>
    /// The configuration the stack's web container is running in, set by scripts/e2e.sh: "standard",
    /// or one of the scenarios --scenarios re-creates it under (a menu placement, the insights database
    /// unreachable). Each test runs in exactly one.
    /// </summary>
    public static string Scenario { get; } =
        Environment.GetEnvironmentVariable("SJI_E2E_SCENARIO") is { Length: > 0 } scenario ? scenario : "standard";

    private static E2ETarget? Resolve(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        null or "" => null,
        "cms13" => new E2ETarget(
            "cms13",
            BaseUrlOr("http://localhost:5105"),
            Database(6005),
            "OptiPowerTools.ScheduledJobsInsights",
            "/Optimizely",
            "/Optimizely/Settings/default#/ScheduledJobs",
            DetailPageHighlightsMenu: true),
        "cms12" => new E2ETarget(
            "cms12",
            BaseUrlOr("http://localhost:5106"),
            Database(6006),
            "OptiPowerTools.ScheduledJobsInsights.Cms12",
            "/EPiServer",
            "/EPiServer/EPiServer.Cms.UI.Admin/default#/ScheduledJobs",
            // Known gap, filed rather than fixed: CMS 12's navigation highlights no entry on ?id=.
            DetailPageHighlightsMenu: false),
        _ => throw new InvalidOperationException(
            $"SJI_E2E_TARGET is '{name}'; expected 'cms13' or 'cms12'."),
    };

    private static Uri BaseUrlOr(string fallback) =>
        new(Environment.GetEnvironmentVariable("SJI_E2E_BASE_URL") is { Length: > 0 } url ? url : fallback);

    private static string Database(int port) =>
        $"Server=localhost,{port};Database=ScheduledJobsInsightsE2E;User Id=sa;Password={Password};" +
        "TrustServerCertificate=True;Encrypt=False;";

    /// <summary>The stack's web container (docker-compose.e2e.yml), whose log a failed test keeps.</summary>
    public string WebContainer => $"sjinsights-e2e-web{Name[^2..]}";

    public string Url(string pathAndQuery) => new Uri(BaseUrl, pathAndQuery).ToString();

    /// <summary>The CMS's own settings page for one scheduled job, where it is started and stopped.</summary>
    public string ScheduledJobAdmin(Guid jobId) => $"{ScheduledJobsAdmin}/detailScheduledJob/{jobId}";
}

/// <summary>
/// A test that runs only against a stack, and only in its scenario. Skipped, with the reason, when
/// <c>SJI_E2E_TARGET</c> is unset - which is how CI compiles this project without starting a browser -
/// or when the stack is running in a different scenario.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EFactAttribute : FactAttribute
{
    /// <param name="scenario">The stack configuration this test needs; see <see cref="E2ETarget.Scenario"/>.</param>
    public E2EFactAttribute(string scenario = "standard")
    {
        if (E2ETarget.Current is null)
            Skip = "Local only: run scripts/e2e.sh, which sets SJI_E2E_TARGET.";
        else if (!string.Equals(scenario, E2ETarget.Scenario, StringComparison.Ordinal))
            Skip = $"Runs in the '{scenario}' scenario (scripts/e2e.sh --scenarios); this run is '{E2ETarget.Scenario}'.";
    }
}
