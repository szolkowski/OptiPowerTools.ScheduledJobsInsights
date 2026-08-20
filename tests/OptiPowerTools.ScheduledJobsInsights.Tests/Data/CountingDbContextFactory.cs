using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Data;

/// <summary>
/// Wraps another factory and counts how many contexts were handed out — i.e. how many times the
/// database was actually reached. Lets a caching test assert the absence of a query.
/// </summary>
internal sealed class CountingDbContextFactory : IDbContextFactory<ScheduledJobsInsightsDbContext>
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _inner;

    public CountingDbContextFactory(IDbContextFactory<ScheduledJobsInsightsDbContext> inner)
    {
        _inner = inner;
    }

    /// <summary>Number of contexts created so far.</summary>
    public int Count { get; private set; }

    public ScheduledJobsInsightsDbContext CreateDbContext()
    {
        Count++;
        return _inner.CreateDbContext();
    }
}
