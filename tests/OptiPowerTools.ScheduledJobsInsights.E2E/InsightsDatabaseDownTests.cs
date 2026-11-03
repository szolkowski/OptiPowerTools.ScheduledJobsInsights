using Microsoft.Data.SqlClient;
using Microsoft.Playwright;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// The insights database unreachable while the CMS's own keeps working - the "insights-db-down"
/// scenario, in which scripts/e2e.sh re-creates the web container with the insights connection string
/// pointed at a port nothing listens on.
/// </summary>
/// <remarks>
/// The rule under test: <b>a tool that observes scheduled jobs must never be able to prevent them</b>.
/// The CMS starts (the failed migration at startup is caught), jobs built on LoggedScheduledJobBase
/// run and finish as they would without the package, and the pages - whose whole job is reading that
/// database - say so instead of failing silently or taking the circuit down.
/// </remarks>
[Collection(StackCollection.Name)]
public sealed class InsightsDatabaseDownTests(StackFixture stack)
{
    // Returns "Nothing to do." at once: the shortest run that still goes through LoggedScheduledJobBase.
    private const string JobType = "OptiPowerTools.ScheduledJobsInsights.Web.Samples.QuietJob";

    [E2EFact("insights-db-down")]
    public Task TheExecutionList_ReportsTheFailure_InsteadOfAnEmptyHistory() =>
        stack.RunAsync(nameof(TheExecutionList_ReportsTheFailure_InsteadOfAnEmptyHistory), async session =>
        {
            await session.OpenListAsync();

            var page = session.Page;
            await Assertions.Expect(page.Locator(".load-error[role=alert]"))
                .ToContainTextAsync("Could not read the execution history");
            // An unreachable database is not "no executions": saying so would be a lie.
            await Assertions.Expect(page.Locator(".sji-content")).Not.ToContainTextAsync("No job executions recorded yet");
        });

    [E2EFact("insights-db-down")]
    public Task TheDetailAndRetentionPages_ReportTheFailure() =>
        stack.RunAsync(nameof(TheDetailAndRetentionPages_ReportTheFailure), async session =>
        {
            var page = session.Page;

            await session.OpenDetailAsync(stack.Data.LogExecutionId);
            await Assertions.Expect(page.Locator(".load-error[role=alert]"))
                .ToContainTextAsync($"Could not read execution #{stack.Data.LogExecutionId}");

            await session.OpenRetentionAsync();
            await Assertions.Expect(page.Locator(".error-text[role=alert]")).Not.ToBeEmptyAsync();
        });

    [E2EFact("insights-db-down")]
    public Task AJob_StillRunsAndSucceeds_WithNothingToRecordItIn() =>
        stack.RunAsync(nameof(AJob_StillRunsAndSucceeds_WithNothingToRecordItIn), async session =>
        {
            var target = session.Target;
            var page = session.Page;
            var jobId = await SeedData.ScheduledJobIdAsync(target.ConnectionString, JobType);
            var previous = await LatestCmsLogAsync(target.ConnectionString, jobId);

            await CmsAdmin.OpenJobAsync(page, target, jobId);
            await CmsAdmin.Button(page, "Start").ClickAsync();

            // The CMS's own execution log, in its own database - the insights one records nothing here.
            // Up to a minute: CMS 12 queues a manual start for its scheduler's next pass.
            for (var attempt = 0; attempt < 120; attempt++)
            {
                var latest = await LatestCmsLogAsync(target.ConnectionString, jobId);
                if (latest.Id > previous.Id)
                {
                    // The job's own return value: ExecuteJob ran to the end and nothing replaced its
                    // result with the recording failure.
                    Assert.Equal("Nothing to do.", latest.Text);
                    return;
                }

                await Task.Delay(500);
            }

            throw new TimeoutException($"{JobType} was started but the CMS never logged an execution.");
        });

    /// <summary>The newest entry in the CMS's own log for one job, or (0, null) when it has none.</summary>
    private static async Task<(int Id, string? Text)> LatestCmsLogAsync(string connectionString, Guid jobId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT TOP 1 pkID, Text FROM tblScheduledItemLog WHERE fkScheduledItemId = @id ORDER BY pkID DESC", connection);
        command.Parameters.AddWithValue("@id", jobId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1))
            : (0, null);
    }
}
