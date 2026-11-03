using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// Timestamps render in the reader's zone, which reaches the server in a cookie the page sets - so the
/// very first view is in UTC, labelled as such, and every view after it is in the reader's own zone.
/// Kathmandu is UTC+05:45: a 45-minute offset cannot line up with UTC, or with the server's zone, by
/// accident.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class TimeZoneTests(StackFixture stack)
{
    private const string Zone = "Asia/Kathmandu";

    [E2EFact]
    public Task TheFirstViewIsInUtc_AndEveryViewAfterIt_InTheReadersZone() =>
        stack.RunAsync(nameof(TheFirstViewIsInUtc_AndEveryViewAfterIt_InTheReadersZone), async session =>
        {
            var page = session.Page;
            await page.Context.ClearCookiesAsync(new() { Name = "sji-timezone" });
            var started = stack.Data.LogStartedAt;
            var row = page.Locator("tr").Filter(new() { HasText = "E2E Log Job" });

            await session.OpenListAsync();
            await Assertions.Expect(page.Locator(".timezone-note")).ToHaveTextAsync("Times shown in UTC");
            await Assertions.Expect(row).ToContainTextAsync(Format(started.ToUniversalTime()));

            await session.OpenListAsync();
            // The label is whatever id the browser reports, and browsers keep some legacy spellings:
            // Chromium names this zone Asia/Katmandu. What matters is that the page shows the reader's
            // zone as their browser names it - and converts times with it, which the next line checks.
            var reported = await page.EvaluateAsync<string>("Intl.DateTimeFormat().resolvedOptions().timeZone");
            await Assertions.Expect(page.Locator(".timezone-note")).ToHaveTextAsync($"Times shown in {reported}");
            var local = TimeZoneInfo.ConvertTime(started, TimeZoneInfo.FindSystemTimeZoneById(Zone));
            await Assertions.Expect(row).ToContainTextAsync(Format(local));
        }, timezoneId: Zone);

    private static string Format(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
