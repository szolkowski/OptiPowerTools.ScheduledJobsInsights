using System.Reflection;
using EPiServer.Framework.TypeScanner;
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
    private readonly ITypeScannerLookup? _typeScanner;

    /// <summary>Initializes the index.</summary>
    /// <param name="typeScanner">
    /// Optimizely's own scanner, which is the supported way to enumerate types in a CMS: it sees the
    /// assemblies the platform scans, rather than whichever happen to be loaded at the moment this
    /// runs. Optional so the index still works where the platform is not present — unit tests, and
    /// any host that has not registered it — falling back to the loaded assemblies.
    /// </param>
    public LoggedJobTypeIndex(ITypeScannerLookup? typeScanner = null)
    {
        _typeScanner = typeScanner;
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

    private Dictionary<string, JobRetentionAttribute?> Scan()
    {
        // A plain dictionary is enough: Lazy(ExecutionAndPublication) guarantees exactly one thread
        // ever runs this, and the result is never mutated afterwards.
        var found = new Dictionary<string, JobRetentionAttribute?>(StringComparer.Ordinal);

        foreach (var type in CandidateTypes())
        {
            // Guarded per type, because the inspection itself can throw for reasons that have nothing
            // to do with this package: IsAssignableFrom loads the type's base chain, and
            // GetCustomAttribute loads the attribute's assembly — either can raise TypeLoadException
            // or FileNotFoundException for a plugin whose dependencies are not all present. Unguarded,
            // that escaped the Lazy and took down both the retention screen and the cleanup job. One
            // unreadable type should cost that type, nothing more.
            try
            {
                if (type is { IsAbstract: false, FullName: { } fullName }
                    && typeof(LoggedScheduledJobBase).IsAssignableFrom(type))
                {
                    found.TryAdd(fullName, type.GetCustomAttribute<JobRetentionAttribute>(inherit: false));
                }
            }
            catch (Exception)
            {
                // Skipped. Routine in a CMS hosting third-party plugins, and one unreadable type
                // should cost that type and nothing else.
            }
        }

        return found;
    }

    /// <summary>
    /// The types to consider: Optimizely's scan where the platform provides one, otherwise whatever
    /// is loaded.
    /// </summary>
    /// <remarks>
    /// The fallback is genuinely weaker, which is why it is only a fallback:
    /// <c>AppDomain.CurrentDomain.GetAssemblies()</c> returns what the CLR has loaded at that instant,
    /// and the result is cached for the process. A logged job in an assembly not yet loaded when the
    /// cleanup job first runs would be missing from the governed set — so a
    /// <c>[JobRetention(Indefinite)]</c> on it would not protect it, and the default sweep would
    /// delete history the author explicitly asked to keep for ever.
    /// </remarks>
    private IEnumerable<Type> CandidateTypes()
    {
        if (_typeScanner is null)
            return LoadedAssemblyTypes();

        try
        {
            return _typeScanner.AllTypes;
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Partial results are the point: the exception carries the types that did load, and one
            // unloadable plugin type must not empty the whole index. An empty index is not a
            // cosmetic loss — the cleanup job's list of jobs to *leave alone* comes from here, so
            // losing it would let the default sweep delete history a job asked to keep for ever.
            return ex.Types.Where(type => type is not null)!;
        }
        catch (Exception)
        {
            // Same reasoning, but it cannot end in an empty index: this class's whole purpose is
            // surviving a failed scan, and empty is the *harmful* answer here rather than the safe one
            // — the cleanup job's list of job types to leave alone comes from this index, so losing it
            // lets the default sweep delete history a [JobRetention(Indefinite)] asked to keep for
            // ever. Note EPiServer.Framework raises its own TypeScannerReflectionException rather than
            // ReflectionTypeLoadException, so this is the branch a real scanner failure lands in.
            // Falling back to the loaded assemblies is weaker (see the remarks above) and still far
            // better than nothing.
            return LoadedAssemblyTypes();
        }
    }

    private static IEnumerable<Type> LoadedAssemblyTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .SelectMany(SafeGetTypes);

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
