using EPiServer.DataAbstraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// Display names for the jobs Optimizely currently has registered.
/// </summary>
/// <remarks>
/// <para>
/// The adapter between this package and the CMS's own job registry, split out of
/// <see cref="JobRetentionService"/>. Best-effort by design: losing the registry costs display names
/// and the "registered" flag, never the screen — the same treatment
/// <c>LoggedScheduledJobBase</c> gives its own job-name lookup.
/// </para>
/// <para>
/// Resolves <see cref="IScheduledJobRepository"/> from a fresh scope per call rather than holding one.
/// This type is a singleton (it is held by one), and Optimizely registers that repository
/// imperatively — nothing guarantees it is not scoped, and nothing guarantees it stays whatever it is
/// today across CMS 13.x. Holding it would be a captive dependency: the exact bug already found once
/// in <c>ScheduledJobsInsightsMenuProvider</c>, which fails Development's scope validation and stops
/// the application starting. <c>JobLoggingContext</c> is registered transient for the same reason.
/// </para>
/// </remarks>
internal sealed class RegisteredJobNames
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RegisteredJobNames> _logger;

    public RegisteredJobNames(IServiceScopeFactory scopeFactory, ILogger<RegisteredJobNames> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Job type name to display name, for everything the CMS currently has registered.</summary>
    public Dictionary<string, string> Read()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scheduledJobRepository = scope.ServiceProvider.GetRequiredService<IScheduledJobRepository>();

            // Duplicate type names are possible in a misconfigured installation; the first wins rather
            // than throwing, since a duplicate must not take the whole screen down.
            var registered = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var job in scheduledJobRepository.List().Where(job => !string.IsNullOrEmpty(job.TypeName)))
                registered.TryAdd(job.TypeName, string.IsNullOrEmpty(job.Name) ? ShortNameOf(job.TypeName) : job.Name);

            return registered;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ScheduledJobsInsights could not list registered scheduled jobs; the retention screen will show job type names instead of display names.");
            return [];
        }
    }

    /// <summary>The class name alone, for a job the CMS no longer knows about.</summary>
    public static string ShortNameOf(string jobTypeName)
    {
        var lastDot = jobTypeName.LastIndexOf('.');
        return lastDot >= 0 && lastDot < jobTypeName.Length - 1 ? jobTypeName[(lastDot + 1)..] : jobTypeName;
    }
}
