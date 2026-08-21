using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Data;

/// <summary>
/// Makes the <em>first</em> save fail with a unique-constraint violation, then behaves normally.
/// </summary>
/// <remarks>
/// Reproduces the read-then-write race on <c>JobRetentionPolicies.JobTypeName</c> deterministically.
/// Two real callers racing cannot be provoked reliably from a test — against Sqlite on one connection
/// they simply serialise, so a "two administrators at once" test passes whether or not the retry
/// exists. Injecting the conflict is the only way to assert the recovery rather than the scheduling.
/// </remarks>
internal sealed class ConflictOnFirstSaveDbContextFactory : IDbContextFactory<ScheduledJobsInsightsDbContext>
{
    private readonly SqliteDbContextFactory _inner;
    private int _saves;

    public ConflictOnFirstSaveDbContextFactory(SqliteDbContextFactory inner)
    {
        _inner = inner;
    }

    /// <summary>How many contexts have been handed out — one per save attempt.</summary>
    public int Attempts { get; private set; }

    public ScheduledJobsInsightsDbContext CreateDbContext()
    {
        Attempts++;
        var dbContext = _inner.CreateDbContext();

        if (++_saves == 1)
            dbContext.SavingChanges += ThrowConflict;

        return dbContext;
    }

    private static void ThrowConflict(object? sender, SavingChangesEventArgs e) =>
        throw new DbUpdateException(
            "Simulated unique index violation on JobRetentionPolicies.JobTypeName.",
            new InvalidOperationException("UNIQUE constraint failed"));
}
