using EPiServer.DataAbstraction;
using Microsoft.Extensions.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// Display names for the jobs Optimizely currently has registered.
/// </summary>
/// <remarks>
/// The adapter between this package and the CMS's own job registry, split out of
/// <see cref="JobRetentionService"/>. Best-effort by design: losing the registry costs display names
/// and the "registered" flag, never the screen — the same treatment
/// <c>LoggedScheduledJobBase</c> gives its own job-name lookup.
/// </remarks>
internal sealed class RegisteredJobNames
{
    private readonly IScheduledJobRepository _scheduledJobRepository;
    private readonly ILogger<RegisteredJobNames> _logger;

    public RegisteredJobNames(IScheduledJobRepository scheduledJobRepository, ILogger<RegisteredJobNames> logger)
    {
        _scheduledJobRepository = scheduledJobRepository;
        _logger = logger;
    }

    /// <summary>Job type name to display name, for everything the CMS currently has registered.</summary>
    public Dictionary<string, string> Read()
    {
        try
        {
            // Duplicate type names are possible in a misconfigured installation; the first wins rather
            // than throwing, since a duplicate must not take the whole screen down.
            var registered = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var job in _scheduledJobRepository.List().Where(job => !string.IsNullOrEmpty(job.TypeName)))
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
