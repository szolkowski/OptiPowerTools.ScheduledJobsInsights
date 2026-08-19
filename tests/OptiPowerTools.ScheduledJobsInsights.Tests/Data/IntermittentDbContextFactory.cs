using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Data;

/// <summary>
/// Test-only <see cref="IDbContextFactory{TContext}"/> that fails a fixed number of times and then
/// starts working — a database outage that ends, rather than one that never does.
/// </summary>
internal sealed class IntermittentDbContextFactory : IDbContextFactory<ScheduledJobsInsightsDbContext>
{
    private readonly IDbContextFactory<ScheduledJobsInsightsDbContext> _inner;
    private readonly int _failuresBeforeRecovery;

    private int _attempts;

    public IntermittentDbContextFactory(
        IDbContextFactory<ScheduledJobsInsightsDbContext> inner,
        int failuresBeforeRecovery)
    {
        _inner = inner;
        _failuresBeforeRecovery = failuresBeforeRecovery;
    }

    public ScheduledJobsInsightsDbContext CreateDbContext()
    {
        if (++_attempts <= _failuresBeforeRecovery)
            throw new InvalidOperationException($"Simulated database failure {_attempts}.");

        return _inner.CreateDbContext();
    }
}
