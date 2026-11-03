using Microsoft.Data.SqlClient;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>What <see cref="SeedData.SeedAsync"/> wrote, for tests to navigate to.</summary>
internal sealed record SeededData(long LogExecutionId, DateTimeOffset LogStartedAt, long LinkedExecutionId);

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

    /// <summary>The paging job's run count: two full pages of 50 and a partial third.</summary>
    public const int PagingRuns = 120;

    /// <summary>The result message of one paging run, numbered oldest first.</summary>
    public static string PagingRun(int number) => $"Paging run {number:D3}";

    /// <summary>The package's cleanup job: registered on every host, so a real job to link to.</summary>
    public const string CleanupJobType = "OptiPowerTools.ScheduledJobsInsights.Jobs.ScheduledJobsInsightsCleanupJob";

    /// <summary>
    /// The CMS's id for a registered job, looked up by type: the CMS's own Name column holds the
    /// display name on CMS 12 but the class name on CMS 13, while the type name is the same on both.
    /// </summary>
    public static async Task<Guid> ScheduledJobIdAsync(string connectionString, string typeName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT pkID FROM tblScheduledItem WHERE TypeName = @type", connection);
        command.Parameters.AddWithValue("@type", typeName);
        return (Guid)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"No scheduled job of type {typeName} is registered."));
    }

    /// <summary>The status of an execution, or null when it no longer exists.</summary>
    public static async Task<int?> StatusAsync(string connectionString, long executionId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"SELECT Status FROM {Schema}.JobExecutions WHERE Id = @id", connection);
        command.Parameters.AddWithValue("@id", executionId);
        // Status is a tinyint, so this is a boxed byte: an `as int?` cast would read every row as
        // missing, and a "was it deleted" check would pass without proving anything.
        return await command.ExecuteScalarAsync() is { } status and not DBNull ? Convert.ToInt32(status, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Inserts one execution directly, for tests that need rows of their own.</summary>
    public static async Task<long> InsertAsync(
        string connectionString, string jobName, string typeSuffix, int status, DateTimeOffset startedAt, bool completed = true)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        return await InsertExecutionAsync(connection, jobName, typeSuffix, status, startedAt, completed: completed);
    }

    /// <summary>Sets, or with null days clears, a retention override for a job type.</summary>
    public static async Task SetRetentionAsync(string connectionString, string jobTypeName, int? days)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"DELETE FROM {Schema}.JobRetentionPolicies WHERE JobTypeName = @type", ("@type", jobTypeName));
        if (days is { } value)
        {
            await ExecuteAsync(connection, $"""
                INSERT INTO {Schema}.JobRetentionPolicies (JobTypeName, RetentionDays, ModifiedBy, ModifiedAt)
                VALUES (@type, @days, 'e2e', SYSDATETIMEOFFSET())
                """, ("@type", jobTypeName), ("@days", value));
        }
    }

    /// <summary>The newest execution of a job type, or 0 when it has none.</summary>
    public static async Task<long> LatestExecutionIdAsync(string connectionString, string typeName)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"SELECT ISNULL(MAX(Id), 0) FROM {Schema}.JobExecutions WHERE JobTypeName = @type", connection);
        command.Parameters.AddWithValue("@type", typeName);
        return (long)(await command.ExecuteScalarAsync())!;
    }

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
        // Whole seconds: SQL Server and the page agree on them, and the time-zone test compares minutes.
        var logStartedAt = new DateTimeOffset(now.AddMinutes(-10).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var logExecutionId = await InsertExecutionAsync(connection, "E2E Log Job", "Log", status: 1,
            logStartedAt, summary: summary);

        // One set-based insert rather than 5,000 round trips.
        await ExecuteAsync(connection, $"""
            INSERT INTO {Schema}.JobLogEntries (JobExecutionId, Sequence, Timestamp, Severity, Source, Message)
            SELECT @id, n, DATEADD(MILLISECOND, n, @started), n % 6, 1, CONCAT('E2E log line ', FORMAT(n, 'D4'))
            FROM (SELECT TOP ({LogLines}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                  FROM sys.all_objects a CROSS JOIN sys.all_objects b) numbers
            """,
            ("@id", logExecutionId), ("@started", logStartedAt));

        foreach (var (name, index) in MetricNames.Select((name, index) => (name, index)))
        {
            await ExecuteAsync(connection, $"""
                INSERT INTO {Schema}.JobMetrics (JobExecutionId, Name, Value, Unit, RecordedAt)
                VALUES (@id, @name, @value, NULL, @at)
                """,
                ("@id", logExecutionId), ("@name", name), ("@value", 100.0 * (index + 1)), ("@at", now));
        }

        // The paging job: 120 runs, a minute apart, all older than everything above, so they never mix
        // with the other seeded runs in the list - each numbered in its result message, so a test can
        // tell exactly which runs a page holds. Newest is run 120.
        await ExecuteAsync(connection, $"""
            INSERT INTO {Schema}.JobExecutions
                (ScheduledJobId, JobName, JobTypeName, StartedAt, CompletedAt, Status, ResultMessage, MachineName)
            SELECT NEWID(), 'E2E Paging Job', '{TypePrefix}Paging', DATEADD(MINUTE, n, @oldest),
                   DATEADD(SECOND, 5, DATEADD(MINUTE, n, @oldest)), 1, CONCAT('Paging run ', FORMAT(n, 'D3')), 'e2e'
            FROM (SELECT TOP ({PagingRuns}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                  FROM sys.all_objects) numbers
            """,
            ("@oldest", now.AddDays(-1)));

        // Recorded against the cleanup job's real id, so its "Scheduled job settings" link leads to a job
        // the CMS knows. The other seeded executions carry random ids that match nothing.
        var cleanupJobId = await ScheduledJobIdAsync(connectionString, CleanupJobType);
        var linkedExecutionId = await InsertExecutionAsync(connection, "E2E Linked Job", "Linked", status: 1,
            now.AddMinutes(-5), scheduledJobId: cleanupJobId);

        return new SeededData(logExecutionId, logStartedAt, linkedExecutionId);
    }

    private static async Task<long> InsertExecutionAsync(
        SqlConnection connection, string jobName, string typeSuffix, int status, DateTimeOffset startedAt,
        string? summary = null, string? exceptionMessage = null, bool completed = true, Guid? scheduledJobId = null)
    {
        await using var command = new SqlCommand($"""
            INSERT INTO {Schema}.JobExecutions
                (ScheduledJobId, JobName, JobTypeName, StartedAt, CompletedAt, Status, ResultMessage,
                 ResultSummary, ExceptionMessage, MachineName)
            OUTPUT INSERTED.Id
            VALUES (@jobId, @name, @type, @started, @completed, @status, @message, @summary, @exception, 'e2e')
            """, connection);
        command.Parameters.AddWithValue("@jobId", scheduledJobId ?? Guid.NewGuid());
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
