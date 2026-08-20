using System.Reflection;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Retention;

/// <summary>
/// Finds the jobs this package can actually govern — concrete <see cref="LoggedScheduledJobBase"/>
/// subclasses — and any <see cref="JobRetentionAttribute"/> they declare.
/// </summary>
/// <remarks>
/// <para>
/// Only logged jobs are indexed, and that is the point: a job deriving from Optimizely's own
/// <c>ScheduledJobBase</c> never writes a row here, so it has no history to retain and listing it
/// would bury the handful of jobs that do among the CMS's two dozen built-ins.
/// </para>
/// <para>
/// One scan serves both questions, done once and lazily: attributes and base types are compiled in,
/// so nothing can change them without a restart, and a consumer who never opens the retention screen
/// never pays for it. Assemblies that cannot be reflected over are skipped rather than allowed to
/// fail the scan — <see cref="ReflectionTypeLoadException"/> is routine in a CMS process, where
/// plugins and optional dependencies frequently fail to load types.
/// </para>
/// </remarks>
internal sealed class LoggedJobTypeIndex
{
    private readonly Lazy<Dictionary<string, JobRetentionAttribute?>> _loggedJobs;

    public LoggedJobTypeIndex()
    {
        _loggedJobs = new Lazy<Dictionary<string, JobRetentionAttribute?>>(
            Scan, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>CLR full names of every concrete logged job in the application.</summary>
    public IReadOnlyCollection<string> LoggedJobTypeNames => _loggedJobs.Value.Keys;

    /// <summary>Whether a job type still exists in the running application.</summary>
    public bool Exists(string jobTypeName) => _loggedJobs.Value.ContainsKey(jobTypeName);

    /// <summary>The attribute a logged job declares, or null if it declares none.</summary>
    public JobRetentionAttribute? FindAttribute(string jobTypeName) =>
        _loggedJobs.Value.GetValueOrDefault(jobTypeName);

    private static Dictionary<string, JobRetentionAttribute?> Scan()
    {
        // A plain dictionary is enough: Lazy(ExecutionAndPublication) guarantees exactly one thread
        // ever runs this, and the result is never mutated afterwards.
        var found = new Dictionary<string, JobRetentionAttribute?>(StringComparer.Ordinal);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
                continue;

            foreach (var type in SafeGetTypes(assembly))
            {
                if (type is { IsAbstract: false, FullName: { } fullName }
                    && typeof(LoggedScheduledJobBase).IsAssignableFrom(type))
                {
                    found.TryAdd(fullName, type.GetCustomAttribute<JobRetentionAttribute>(inherit: false));
                }
            }
        }

        return found;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Partial results are still useful — the types that did load may include the jobs.
            return ex.Types.OfType<Type>();
        }
        catch (Exception)
        {
            return [];
        }
    }
}
