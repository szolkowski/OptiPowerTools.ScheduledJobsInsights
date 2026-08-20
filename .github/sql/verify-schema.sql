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

IF SCHEMA_ID('scheduled_jobs_insights') IS NULL
BEGIN PRINT 'FAIL: schema scheduled_jobs_insights missing'; SET @failures += 1; END

-- The result summary column, added by AddResultSummary. max_length -1 is nvarchar(max).
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(@tbl) AND name = 'ResultSummary'
                 AND system_type_id = TYPE_ID('nvarchar') AND max_length = -1 AND is_nullable = 1)
BEGIN PRINT 'FAIL: JobExecutions.ResultSummary is not a nullable nvarchar(max)'; SET @failures += 1; END

-- The JobName index, added by AddJobNameIndex. Key order and the descending flags are the whole
-- point of it: leading with JobName makes the filtered list a seek, and StartedAt/Id descending
-- make the keyset page need no sort. Sqlite never sees this DDL.
;WITH keys AS (
    SELECT c.name, ic.key_ordinal, ic.is_descending_key
    FROM sys.indexes i
    JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
    JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
    WHERE i.object_id = OBJECT_ID(@tbl) AND i.name = 'IX_JobExecutions_JobName_StartedAt_Id'
)
SELECT @failures = @failures
    + CASE WHEN (SELECT COUNT(*) FROM keys) = 3 THEN 0 ELSE 1 END
    + CASE WHEN EXISTS (SELECT 1 FROM keys WHERE name='JobName'   AND key_ordinal=1 AND is_descending_key=0) THEN 0 ELSE 1 END
    + CASE WHEN EXISTS (SELECT 1 FROM keys WHERE name='StartedAt' AND key_ordinal=2 AND is_descending_key=1) THEN 0 ELSE 1 END
    + CASE WHEN EXISTS (SELECT 1 FROM keys WHERE name='Id'        AND key_ordinal=3 AND is_descending_key=1) THEN 0 ELSE 1 END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(@tbl) AND name = 'IX_JobExecutions_StartedAt_Id')
BEGIN PRINT 'FAIL: IX_JobExecutions_StartedAt_Id missing'; SET @failures += 1; END

-- Child rows must cascade with their parent execution; the cleanup job relies on it.
IF (SELECT COUNT(*) FROM sys.foreign_keys
    WHERE parent_object_id IN (OBJECT_ID('scheduled_jobs_insights.JobLogEntries'),
                               OBJECT_ID('scheduled_jobs_insights.JobMetrics'))
      AND delete_referential_action_desc = 'CASCADE') <> 2
BEGIN PRINT 'FAIL: JobLogEntries/JobMetrics are not both ON DELETE CASCADE'; SET @failures += 1; END

IF @failures > 0
    RAISERROR('Schema verification failed with %d problem(s).', 16, 1, @failures);
ELSE
    PRINT 'Schema verification passed.';
