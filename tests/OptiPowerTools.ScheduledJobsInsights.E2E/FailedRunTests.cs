using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// A real job that throws, started from the CMS's own admin: the package records the failure and
/// rethrows it, so the CMS reports the run failed exactly as it would without the package, while the
/// detail page shows what the CMS cannot - the log up to the failure, the stack trace, and the summary
/// the job had built before it threw.
/// </summary>
[Collection(StackCollection.Name)]
public sealed class FailedRunTests(StackFixture stack)
{
    // Throws on every second run of the process. Which run that is depends on what earlier test runs
    // did against a kept stack, so the test starts it until a run fails - at most twice.
    private const string JobType = "OptiPowerTools.ScheduledJobsInsights.Web.Samples.FlakyImportJob";
    private const string Failure = "Malformed record at row 42";
    private const int Failed = 2;

    [E2EFact]
    public Task AJobThatThrows_IsReportedFailedByTheCms_AndDetailedHere() =>
        stack.RunAsync(nameof(AJobThatThrows_IsReportedFailedByTheCms_AndDetailedHere), async session =>
        {
            var target = session.Target;
            var page = session.Page;

            var executionId = 0L;
            for (var attempt = 0; attempt < 2 && executionId == 0; attempt++)
            {
                var started = await CmsAdmin.StartAsync(page, target, JobType);
                if (await FinishedStatusAsync(target.ConnectionString, started) == Failed)
                    executionId = started;
            }

            Assert.True(executionId != 0, $"{JobType} ran twice without failing.");

            // The exception reached the CMS unchanged: its own log carries the job's message, not the
            // package's, and not a success.
            var jobId = await SeedData.ScheduledJobIdAsync(target.ConnectionString, JobType);
            Assert.Contains(Failure, await LatestCmsLogTextAsync(target.ConnectionString, jobId), StringComparison.Ordinal);

            await session.OpenDetailAsync(executionId);
            await Assertions.Expect(page.Locator(".status-badge")).ToHaveTextAsync("Failed");
            await Assertions.Expect(page.Locator(".error-text")).ToContainTextAsync(Failure);

            // Persisted on the way out of the failure path, though the job never flushed it.
            await Assertions.Expect(Section(page, "Result summary").Locator(".summary-text"))
                .ToContainTextAsync("Aborted at row 42");

            var stackTrace = Section(page, "Stack trace");
            await stackTrace.Locator("summary").ClickAsync();
            await Assertions.Expect(stackTrace).ToContainTextAsync("FlakyImportJob.ExecuteJob");

            // Log lines travel through the buffered writer and can land after the run is marked
            // failed; a finished run's page reads them once, so reload until they are there.
            var errorLine = page.GetByText("Encountered a malformed record", new() { Exact = false });
            for (var attempt = 0; attempt < 10 && await errorLine.CountAsync() == 0; attempt++)
            {
                await Task.Delay(1_000);
                await session.OpenDetailAsync(executionId);
            }

            await Assertions.Expect(errorLine).ToBeVisibleAsync();
        });

    private static ILocator Section(IPage page, string title) =>
        page.Locator("details.accordion").Filter(new() { Has = page.Locator(".accordion-title", new() { HasText = title }) });

    /// <summary>The execution's status once it has left Running - a quick job is usually there already.</summary>
    private static async Task<int?> FinishedStatusAsync(string connectionString, long executionId)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var status = await SeedData.StatusAsync(connectionString, executionId);
            if (status is not 0)
                return status;
            await Task.Delay(500);
        }

        throw new TimeoutException($"Execution {executionId} is still running.");
    }

    private static async Task<string> LatestCmsLogTextAsync(string connectionString, Guid jobId)
    {
        // The CMS writes its log entry as its executor unwinds, just after the package marks the run.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT TOP 1 Text FROM tblScheduledItemLog WHERE fkScheduledItemId = @id ORDER BY pkID DESC", connection);
            command.Parameters.AddWithValue("@id", jobId);
            if (await command.ExecuteScalarAsync() is string text && text.Contains(Failure, StringComparison.Ordinal))
                return text;
            await Task.Delay(500);
        }

        return "(the CMS logged no failure for this job)";
    }
}
