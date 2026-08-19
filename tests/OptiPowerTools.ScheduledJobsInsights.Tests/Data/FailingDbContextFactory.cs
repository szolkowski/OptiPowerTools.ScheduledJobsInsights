using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Data;

/// <summary>
/// Test-only <see cref="IDbContextFactory{TContext}"/> that fails the way an unavailable database
/// does — every attempt to obtain a context throws.
/// </summary>
/// <remarks>
/// Stands in for the case that matters most in the write path: a transient SQL failure while
/// persisting log lines must never escape into the hosted service (which would stop the whole
/// application) or into the running job (which would fail it).
/// </remarks>
internal sealed class FailingDbContextFactory : IDbContextFactory<ScheduledJobsInsightsDbContext>
{
    /// <summary>Number of times a context was requested — i.e. how many write attempts were made.</summary>
    public int Attempts { get; private set; }

    public ScheduledJobsInsightsDbContext CreateDbContext()
    {
        Attempts++;
        throw new InvalidOperationException("Simulated database failure.");
    }
}
