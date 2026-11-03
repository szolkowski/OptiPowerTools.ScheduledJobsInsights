using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// One per run: a browser, a logged-in administrator, and seeded history on the target stack. Every
/// test class joins <see cref="StackCollection"/>, so this happens once and the tests run one at a time
/// against the same stack.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string? _storageState;

    internal E2ETarget Target => E2ETarget.Current
        ?? throw new InvalidOperationException("No SJI_E2E_TARGET; E2EFact should have skipped this test.");

    internal SeededData Data { get; private set; } = null!;

    /// <summary>Where failed tests leave a Playwright trace and a screenshot.</summary>
    internal static string ArtifactsDirectory { get; } =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts");

    public async Task InitializeAsync()
    {
        if (E2ETarget.Current is not { } target)
            return;

        // Idempotent and quick once the browser is there; the first run downloads it.
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) is not 0)
            throw new InvalidOperationException("Playwright could not install Chromium.");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = Environment.GetEnvironmentVariable("SJI_E2E_HEADED") != "1",
        });

        await using var context = await _browser.NewContextAsync(new() { BaseURL = target.BaseUrl.ToString() });
        var page = await context.NewPageAsync();
        if (!await AdministratorExistsAsync(target.ConnectionString))
            await RegisterAdministratorAsync(page);
        await LogInAsync(page);

        _storageState = Path.Combine(Path.GetTempPath(), $"sji-e2e-{target.Name}-auth.json");
        await context.StorageStateAsync(new() { Path = _storageState });

        Data = await SeedData.SeedAsync(target.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
    }

    /// <summary>
    /// Runs <paramref name="test"/> in a fresh, logged-in browser context. On failure it keeps the trace
    /// and a screenshot under <see cref="ArtifactsDirectory"/> before rethrowing; on success it keeps
    /// nothing.
    /// </summary>
    internal async Task RunAsync(string testName, Func<BrowserSession, Task> test, string? timezoneId = null)
    {
        await using var context = await _browser!.NewContextAsync(new()
        {
            BaseURL = Target.BaseUrl.ToString(),
            StorageStatePath = _storageState,
            ViewportSize = new() { Width = 1440, Height = 900 },
            // The browser's own zone, which the page reads and reports back in its time-zone cookie.
            TimezoneId = timezoneId,
        });
        await context.Tracing.StartAsync(new() { Screenshots = true, Snapshots = true });

        var session = new BrowserSession(Target, await context.NewPageAsync());
        try
        {
            await test(session);
            await context.Tracing.StopAsync();
        }
        catch
        {
            var stem = Path.Combine(ArtifactsDirectory, $"{Target.Name}-{testName}");
            Directory.CreateDirectory(ArtifactsDirectory);
            await session.Page.ScreenshotAsync(new() { Path = stem + ".png", FullPage = true });
            await context.Tracing.StopAsync(new() { Path = stem + ".trace.zip" });
            throw;
        }
    }

    /// <summary>
    /// Asked of the database rather than read off <c>/util/register</c>: whether that page keeps
    /// serving its form once a user exists depends on how the host configured registration - on CMS 12
    /// it stops, on the CMS 13 dev host it does not. A fresh stack has no user; one kept running with
    /// <c>--keep</c> already has this one.
    /// </summary>
    private static async Task<bool> AdministratorExistsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM AspNetUsers WHERE UserName = @name", connection);
        command.Parameters.AddWithValue("@name", E2ETarget.AdminUser);
        return (int)(await command.ExecuteScalarAsync())! > 0;
    }

    private static async Task RegisterAdministratorAsync(IPage page)
    {
        await page.GotoAsync("/Util/Register");
        await page.Locator("input[name=Username]").FillAsync(E2ETarget.AdminUser);
        await page.Locator("input[name=Email]").FillAsync("e2e@example.test");
        await page.Locator("input[name=Password]").FillAsync(E2ETarget.AdminPassword);
        await page.Locator("input[name=ConfirmPassword]").FillAsync(E2ETarget.AdminPassword);
        await page.Locator("form [type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/Util/Register", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task LogInAsync(IPage page)
    {
        await page.GotoAsync("/Util/Login");
        await page.Locator("input[name=UserName]").FillAsync(E2ETarget.AdminUser);
        await page.Locator("input[name=Password]").FillAsync(E2ETarget.AdminPassword);
        await page.Locator("input[name=Submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/Util/Login", StringComparison.OrdinalIgnoreCase));
    }
}

[CollectionDefinition(Name)]
public sealed class StackCollection : ICollectionFixture<StackFixture>
{
    public const string Name = "Stack";
}
