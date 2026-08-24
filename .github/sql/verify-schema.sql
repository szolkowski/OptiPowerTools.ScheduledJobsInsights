-- Asserts that the applied SQL Server schema is the one the migrations were meant to produce.
--
-- Run by the Build & Test workflow after `dotnet ef database update`, and safe to run by hand
-- against a local database:
--
--   docker exec -i sjinsights-db /opt/mssql-tools18/bin/sqlcmd \
--     -S localhost -U sa -P "$SA_PASSWORD" -C -b -d ScheduledJobsInsights < .github/sql/verify-schema.sql
--
-- Exists because the test suite cannot cover any of this. The Sqlite provider used by the EF tests
-- never sees SQL Server DDL, and ScheduledJobsInsightsDbContext.OnModelCreating deliberately swaps in
-- a DateTimeOffset converter under Sqlite -- so descending index keys, nvarchar(max), the custom
-- schema and the cascade rules are all unverified by anything else.
--
-- sqlcmd must be invoked with -b so that the RAISERROR at the end sets a non-zero exit code.

SET NOCOUNT ON;
DECLARE @failures int = 0;
DECLARE @tbl nvarchar(200) = 'scheduled_jobs_insights.JobExecutions';
DECLARE @policies nvarchar(200) = 'scheduled_jobs_insights.JobRetentionPolicies';

IF SCHEMA_ID('scheduled_jobs_insights') IS NULL
BEGIN PRINT 'FAIL: schema scheduled_jobs_insights missing'; SET @failures += 1; END

-- Named once and reused by every column assertion below: max_length -1 is nvarchar(max), and a
-- width in bytes is twice the character count.
DECLARE @nvarchar int = TYPE_ID('nvarchar');

-- The result summary column, added by AddResultSummary. max_length -1 is nvarchar(max).
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(@tbl) AND name = 'ResultSummary'
                 AND system_type_id = @nvarchar AND max_length = -1 AND is_nullable = 1)
BEGIN PRINT 'FAIL: JobExecutions.ResultSummary is not a nullable nvarchar(max)'; SET @failures += 1; END

-- ---------------------------------------------------------------------------
-- Index key assertions.
--
-- Each key is checked individually and named on failure. The two composite checks here used to
-- accumulate into @failures with no PRINT, so a CI failure reported a count and left you unable to
-- tell which index, which column or which direction -- from the only script that checks any of this
-- DDL at all.
-- ---------------------------------------------------------------------------
DECLARE @idx sysname, @col sysname, @ord int, @desc bit;

DECLARE @expected TABLE (idx sysname, col sysname, ord int, is_desc bit, keys int);

-- Each index is named once. Repeating the literal per key row invited a typo in one of three rows,
-- where the check would then look for an index nobody meant to assert.
DECLARE
    @ixJobName   sysname = 'IX_JobExecutions_JobName_StartedAt_Id',
    @ixStatus    sysname = 'IX_JobExecutions_Status_StartedAt_Id',
    @ixStartedAt sysname = 'IX_JobExecutions_StartedAt_Id',
    @ixJobType   sysname = 'IX_JobExecutions_JobTypeName_StartedAt',
    @colStartedAt sysname = 'StartedAt',
    @colId        sysname = 'Id';

INSERT @expected VALUES
-- Leading with JobName makes the filtered list a seek; StartedAt/Id descending make the keyset page
-- need no sort.
    (@ixJobName,   'JobName',      1, 0, 3),
    (@ixJobName,   @colStartedAt,  2, 1, 3),
    (@ixJobName,   @colId,         3, 1, 3),
-- Status first for the same reason. Measured at 100,000 executions, "Running" with nothing running
-- cost 35,208 logical reads without this index against 3 with it.
    (@ixStatus,    'Status',       1, 0, 3),
    (@ixStatus,    @colStartedAt,  2, 1, 3),
    (@ixStatus,    @colId,         3, 1, 3),
-- The unfiltered list page -- the single hottest query in the UI. This one was previously checked for
-- existence only, and it is the one index whose migration uses EF's "all descending" shorthand
-- (descending: new bool[0]), so its flags are the least obvious from reading the migration and the
-- most valuable to assert.
    (@ixStartedAt, @colStartedAt,  1, 1, 2),
    (@ixStartedAt, @colId,         2, 1, 2),
-- Serves the cleanup job's per-job-type delete and its NOT IN exclusion form.
    (@ixJobType,   'JobTypeName',  1, 0, 2),
    (@ixJobType,   @colStartedAt,  2, 0, 2);

DECLARE keyCheck CURSOR LOCAL FAST_FORWARD FOR SELECT idx, col, ord, is_desc FROM @expected;
OPEN keyCheck;
FETCH NEXT FROM keyCheck INTO @idx, @col, @ord, @desc;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(@tbl) AND i.name = @idx
          AND c.name = @col AND ic.key_ordinal = @ord AND ic.is_descending_key = @desc)
    BEGIN
        PRINT 'FAIL: ' + @idx + ' key ' + CAST(@ord AS varchar(2)) + ' should be '
            + @col + CASE WHEN @desc = 1 THEN ' DESC' ELSE ' ASC' END;
        SET @failures += 1;
    END

    FETCH NEXT FROM keyCheck INTO @idx, @col, @ord, @desc;
END
CLOSE keyCheck;
DEALLOCATE keyCheck;

-- And no extra keys: an index that gained a column would still satisfy every check above.
IF EXISTS (
    SELECT i.name
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    WHERE i.object_id = OBJECT_ID(@tbl) AND i.name IN (SELECT DISTINCT idx FROM @expected)
      AND ic.key_ordinal > 0
    GROUP BY i.name
    HAVING COUNT(*) <> (SELECT MAX(keys) FROM @expected e WHERE e.idx = i.name))
BEGIN PRINT 'FAIL: one of the JobExecutions indexes has an unexpected number of key columns'; SET @failures += 1; END

-- The other two unbounded columns. Neither was asserted, and both are read back into a Blazor
-- circuit, so their type is not incidental.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(@tbl) AND name = 'InputDataJson'
                 AND system_type_id = @nvarchar AND max_length = -1)
BEGIN PRINT 'FAIL: JobExecutions.InputDataJson is not an nvarchar(max)'; SET @failures += 1; END

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(@tbl) AND name = 'ExceptionStackTrace'
                 AND system_type_id = @nvarchar AND max_length = -1)
BEGIN PRINT 'FAIL: JobExecutions.ExceptionStackTrace is not an nvarchar(max)'; SET @failures += 1; END

-- JobName's width is the documented tuning lever: it is the leading key of a composite index, so
-- narrowing it is what you reach for if insert cost ever outweighs list latency. max_length is in
-- bytes for nvarchar, so 400 characters is 800.
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(@tbl) AND name = 'JobName'
                 AND system_type_id = @nvarchar AND max_length = 800)
BEGIN PRINT 'FAIL: JobExecutions.JobName is not nvarchar(400)'; SET @failures += 1; END

-- Child rows must cascade with their parent execution; the cleanup job relies on it.
IF (SELECT COUNT(*) FROM sys.foreign_keys
    WHERE parent_object_id IN (OBJECT_ID('scheduled_jobs_insights.JobLogEntries'),
                               OBJECT_ID('scheduled_jobs_insights.JobMetrics'))
      AND delete_referential_action_desc = 'CASCADE') <> 2
BEGIN PRINT 'FAIL: JobLogEntries/JobMetrics are not both ON DELETE CASCADE'; SET @failures += 1; END

-- Per-job retention, added by AddJobRetentionPolicies. The unique index is what the resolver relies
-- on to guarantee one policy per job type.
IF OBJECT_ID(@policies) IS NULL
BEGIN PRINT 'FAIL: JobRetentionPolicies table missing'; SET @failures += 1; END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns
                   WHERE object_id = OBJECT_ID(@policies)
                     AND name = 'RetentionDays' AND is_nullable = 1)
    BEGIN PRINT 'FAIL: JobRetentionPolicies.RetentionDays must be nullable (null means indefinite)'; SET @failures += 1; END

    IF NOT EXISTS (SELECT 1 FROM sys.indexes i
                   JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                   JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
                   WHERE i.object_id = OBJECT_ID(@policies)
                     AND i.is_unique = 1 AND c.name = 'JobTypeName')
    BEGIN PRINT 'FAIL: JobRetentionPolicies.JobTypeName is not uniquely indexed'; SET @failures += 1; END
END

IF @failures > 0
    RAISERROR('Schema verification failed with %d problem(s).', 16, 1, @failures);
ELSE
    PRINT 'Schema verification passed.';
