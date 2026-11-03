using System.Reflection;

namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// The name a job declares for itself, as the CMS's own Scheduled Jobs admin shows it.
/// </summary>
/// <remarks>
/// Read from the job's attribute rather than from the CMS's job registry, because the registry's
/// <c>ScheduledJob.Name</c> means different things on the two CMS versions. CMS 12 stores the
/// attribute's display name in it; CMS 13 stores the class name (<c>DefaultScheduledJobScanner</c> sets
/// <c>Name = Type.Name</c>) and its admin reads the display name from the attribute instead. Taking the
/// registry's name therefore recorded and listed every CMS 13 job by its class name - "SlowMigrationJob"
/// where the CMS itself says "Sample: Slow Migration". The attribute is the same on both.
/// </remarks>
internal static class ScheduledJobDisplayName
{
    /// <summary>The declared display name, or null when the type declares none (or cannot be read).</summary>
    public static string? Of(Type jobType)
    {
        try
        {
#if CMS12
            var declared = jobType.GetCustomAttribute<EPiServer.PlugIn.ScheduledPlugInAttribute>(inherit: false)?.DisplayName;
#else
            var declared = jobType.GetCustomAttribute<EPiServer.Scheduler.ScheduledJobAttribute>(inherit: false)?.DisplayName;
#endif
            return string.IsNullOrWhiteSpace(declared) ? null : declared;
        }
        catch (Exception)
        {
            // Reading an attribute loads its assembly, which can fail for a plugin whose dependencies
            // are not all present. A name is never worth more than the caller's fallback.
            return null;
        }
    }
}
