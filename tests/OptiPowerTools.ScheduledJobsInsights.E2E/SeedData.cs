using Microsoft.Data.SqlClient;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>What <see cref="SeedData.SeedAsync"/> wrote, for tests to navigate to.</summary>
internal sealed record SeededData(long LogExecutionId);

/// <summary>
/// Writes execution history straight into the stack's database, so the tests assert against exact,
/// known content instead of whatever a real job wrote. Every row it owns is tagged with a job type
/// under <see cref="TypePrefix"/>, and a run deletes those first, which keeps a stack left running
/// with <c>--keep</c> exact across repeated runs.
/// </summary>
internal static class SeedData
{
    private const string TypePrefix = "E2E.";
    private const string Schema = "scheduled_jobs_insights";

    /// <summary>The log job's line count — above any viewport, so the console has to virtualize.</summary>
    public const int LogLines = 5000;

    /// <summary>The log job's summary length — above the detail page's 200-line auto-collapse threshold.</summary>
    public const int SummaryLines = 250;

    public static readonly string[] MetricNames = ["DurationMs", "ItemsProcessed", "ThreadAllocatedBytes"];

    /// <summary>The text of one seeded log line, zero-padded so line 1 is never a prefix of line 10.</summary>
    public static string LogLine(int number) => $"E2E log line {number:D4}";

    public static async Task<SeededData> SeedAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Children cascade with their execution at the database level.
        await ExecuteAsync(connection, $"DELETE FROM {Schema}.JobExecutions WHERE JobTypeName LIKE '{TypePrefix}%'");

        var now = DateTimeOffset.UtcNow;
        await InsertExecutionAsync(connection, "E2E Failing Job", "Failing", status: 2, now.AddMinutes(-40),
            exceptionMessage: "Seeded failure.");
        await InsertExecutionAsync(connection, "E2E Stopped Job", "Stopped", status: 3, now.AddMinutes(-30));
        await InsertExecutionAsync(connection, "E2E Interrupted Job", "Interrupted", status: 4, now.AddMinutes(-20),
            completed: false);

        var summary = string.Join('\n', Enumerable.Range(1, SummaryLines).Select(i => $"E2E summary line {i:D3}"));
        var logExecutionId = await InsertExecutionAsync(connection, "E2E Log Job", "Log", status: 1,
            now.AddMinutes(-10), summary: summary);

        // One set-based insert rather than 5,000 round trips.
        await ExecuteAsync(connection, $"""
            INSERT INTO {Schema}.JobLogEntries (JobExecutionId, Sequence, Timestamp, Severity, Source, Message)
            SELECT @id, n, DATEADD(MILLISECOND, n, @started), n % 6, 1, CONCAT('E2E log line ', FORMAT(n, 'D4'))
            FROM (SELECT TOP ({LogLines}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                  FROM sys.all_objects a CROSS JOIN sys.all_objects b) numbers
            """,
            ("@id", logExecutionId), ("@started", now.AddMinutes(-10)));

        foreach (var (name, index) in MetricNames.Select((name, index) => (name, index)))
        {
            await ExecuteAsync(connection, $"""
                INSERT INTO {Schema}.JobMetrics (JobExecutionId, Name, Value, Unit, RecordedAt)
                VALUES (@id, @name, @value, NULL, @at)
                """,
                ("@id", logExecutionId), ("@name", name), ("@value", 100.0 * (index + 1)), ("@at", now));
        }

        return new SeededData(logExecutionId);
    }

    private static async Task<long> InsertExecutionAsync(
        SqlConnection connection, string jobName, string typeSuffix, int status, DateTimeOffset startedAt,
        string? summary = null, string? exceptionMessage = null, bool completed = true)
    {
        await using var command = new SqlCommand($"""
            INSERT INTO {Schema}.JobExecutions
                (ScheduledJobId, JobName, JobTypeName, StartedAt, CompletedAt, Status, ResultMessage,
                 ResultSummary, ExceptionMessage, MachineName)
            OUTPUT INSERTED.Id
            VALUES (NEWID(), @name, @type, @started, @completed, @status, @message, @summary, @exception, 'e2e')
            """, connection);
        command.Parameters.AddWithValue("@name", jobName);
        command.Parameters.AddWithValue("@type", TypePrefix + typeSuffix);
        command.Parameters.AddWithValue("@started", startedAt);
        command.Parameters.AddWithValue("@completed", completed ? startedAt.AddSeconds(5) : DBNull.Value);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@message", $"Seeded {typeSuffix.ToLowerInvariant()} run.");
        command.Parameters.AddWithValue("@summary", (object?)summary ?? DBNull.Value);
        command.Parameters.AddWithValue("@exception", (object?)exceptionMessage ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}
