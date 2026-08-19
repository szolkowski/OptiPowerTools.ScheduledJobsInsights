using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OptiPowerTools.ScheduledJobsInsights.Data.Entities;

namespace OptiPowerTools.ScheduledJobsInsights.Data;

/// <summary>
/// EF Core context for the ScheduledJobsInsights schema. Applied via standard EF Core Migrations
/// (see <c>Data/Migrations</c>), auto-applied at startup by <c>UseOptiPowerToolScheduledJobsInsights</c>
/// when <c>AutoMigrateDatabase</c> is enabled.
/// </summary>
internal class ScheduledJobsInsightsDbContext : DbContext
{
    /// <summary>Fixed schema name — not runtime-configurable, so migrations never need to bake a dynamic value.</summary>
    public const string SchemaName = "scheduled_jobs_insights";

    public ScheduledJobsInsightsDbContext(DbContextOptions<ScheduledJobsInsightsDbContext> options)
        : base(options)
    {
    }

    public DbSet<JobExecution> JobExecutions => Set<JobExecution>();

    public DbSet<JobLogEntry> JobLogEntries => Set<JobLogEntry>();

    public DbSet<JobMetric> JobMetrics => Set<JobMetric>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        // Sqlite (used only by the test suite's in-memory provider) can't translate ORDER BY over
        // DateTimeOffset columns natively — SQL Server has no such limitation. This conversion keeps
        // ordering/comparison working under Sqlite without affecting the production SQL Server schema.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var converter = new DateTimeOffsetToBinaryConverter();
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                        property.SetValueConverter(converter);
                }
            }
        }

        modelBuilder.Entity<JobExecution>(entity =>
        {
            entity.ToTable("JobExecutions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.JobName).HasMaxLength(400).IsRequired();
            entity.Property(e => e.JobTypeName).HasMaxLength(400).IsRequired();
            entity.Property(e => e.MachineName).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => new { e.StartedAt, e.Id }).IsDescending(true, true);
            entity.HasIndex(e => e.ScheduledJobId);
        });

        modelBuilder.Entity<JobLogEntry>(entity =>
        {
            entity.ToTable("JobLogEntries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Message).IsRequired();
            entity.HasIndex(e => new { e.JobExecutionId, e.Sequence }).IsUnique();
            entity.HasOne(e => e.JobExecution)
                .WithMany(e => e.LogEntries)
                .HasForeignKey(e => e.JobExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobMetric>(entity =>
        {
            entity.ToTable("JobMetrics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Unit).HasMaxLength(50);
            entity.HasIndex(e => new { e.JobExecutionId, e.Name });
            entity.HasOne(e => e.JobExecution)
                .WithMany(e => e.Metrics)
                .HasForeignKey(e => e.JobExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
