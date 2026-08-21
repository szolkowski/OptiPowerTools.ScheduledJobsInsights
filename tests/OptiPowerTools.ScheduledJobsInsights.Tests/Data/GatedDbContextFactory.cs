using Microsoft.EntityFrameworkCore;
using OptiPowerTools.ScheduledJobsInsights.Data;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Data;

/// <summary>
/// Wraps a real factory and holds the <em>first</em> context request open until it is released, so a
/// test can pin a write in flight and drive something else into it deliberately.
/// </summary>
/// <remarks>
/// Exists for the shutdown-race test: overlapping two drains is otherwise a matter of timing, and a
/// test that reproduces a race only sometimes is worse than no test at all.
/// </remarks>
internal sealed class GatedDbContextFactory : IDbContextFactory<ScheduledJobsInsightsDbContext>, IDisposable
{
    private readonly SqliteDbContextFactory _inner;
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;

    public GatedDbContextFactory(SqliteDbContextFactory inner)
    {
        _inner = inner;
    }

    /// <summary>Completes once a write has actually started and is being held.</summary>
    public Task FirstCallEntered => _entered.Task;

    /// <summary>How many contexts have been requested — one per attempted batch write.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Lets the held call proceed.</summary>
    public void Release() => _release.TrySetResult();

    public ScheduledJobsInsightsDbContext CreateDbContext()
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            _entered.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
        }

        return _inner.CreateDbContext();
    }

    public void Dispose() => Release();
}
