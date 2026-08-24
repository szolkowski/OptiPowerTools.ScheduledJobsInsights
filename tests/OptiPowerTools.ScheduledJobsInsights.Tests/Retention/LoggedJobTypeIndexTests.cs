using System.Reflection;
using EPiServer.Framework.TypeScanner;
using NSubstitute;
using OptiPowerTools.ScheduledJobsInsights.Retention;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Retention;

/// <summary>
/// The scan behind the retention screen and the cleanup job's governed-types list. Its filtering and
/// its tolerance of unloadable assemblies were previously exercised only incidentally, through
/// <see cref="JobRetentionServiceTests"/>.
/// </summary>
public class LoggedJobTypeIndexTests
{
    private static ITypeScannerLookup ScannerOver(params Type[] types)
    {
        var scanner = Substitute.For<ITypeScannerLookup>();
        scanner.AllTypes.Returns(types);
        return scanner;
    }

    [Fact]
    public void ALoggedJob_IsFoundWithItsAttribute()
    {
        var index = new LoggedJobTypeIndex(ScannerOver(typeof(ChattyTestJob)));

        Assert.True(index.Exists(typeof(ChattyTestJob).FullName!));
        Assert.Equal(7, index.FindAttribute(typeof(ChattyTestJob).FullName!)!.Days);
    }

    [Fact]
    public void ALoggedJobWithNoAttribute_IsStillFound()
    {
        // Being listed is what lets an administrator configure a job before it has ever run.
        var index = new LoggedJobTypeIndex(ScannerOver(typeof(PlainTestJob)));

        Assert.True(index.Exists(typeof(PlainTestJob).FullName!));
        Assert.Null(index.FindAttribute(typeof(PlainTestJob).FullName!));
    }

    [Fact]
    public void AnAbstractBaseClass_IsNotAJob()
    {
        // RetentionTestJobBase derives from LoggedScheduledJobBase but can never run.
        var index = new LoggedJobTypeIndex(ScannerOver(typeof(RetentionTestJobBase)));

        Assert.Empty(index.LoggedJobTypeNames);
    }

    [Fact]
    public void ATypeThatIsNotALoggedJob_IsIgnored()
    {
        // Optimizely's scanner returns every type in the application, so the filter is what keeps the
        // retention screen from listing the entire CMS.
        var index = new LoggedJobTypeIndex(ScannerOver(typeof(string), typeof(LoggedJobTypeIndexTests)));

        Assert.Empty(index.LoggedJobTypeNames);
    }

    [Fact]
    public void AnUnknownJobType_HasNoAttributeAndDoesNotExist()
    {
        var index = new LoggedJobTypeIndex(ScannerOver(typeof(PlainTestJob)));

        Assert.False(index.Exists("Contoso.Jobs.NeverHeardOf"));
        Assert.Null(index.FindAttribute("Contoso.Jobs.NeverHeardOf"));
    }

    [Fact]
    public void TheScanHappensOnce_AndIsThenCached()
    {
        // Attributes and base types are compiled in, so nothing can change without a restart — and a
        // consumer who never opens the retention screen should not pay for the scan at all.
        var scanner = ScannerOver(typeof(PlainTestJob));
        var index = new LoggedJobTypeIndex(scanner);

        _ = scanner.DidNotReceive().AllTypes;

        _ = index.LoggedJobTypeNames;
        _ = index.LoggedJobTypeNames;
        _ = index.Exists("anything");

        _ = scanner.Received(1).AllTypes;
    }

    [Fact]
    public void WithNoScanner_ItFallsBackToTheLoadedAssemblies()
    {
        // The fallback path, used by every unit test here and by any host that has not registered
        // Optimizely's scanner. The test jobs live in this assembly, which is loaded by definition.
        var index = new LoggedJobTypeIndex();

        Assert.True(index.Exists(typeof(ChattyTestJob).FullName!));
    }

    [Fact]
    public void AnAssemblyThatCannotBeFullyLoaded_DoesNotCostTheWholeScan()
    {
        // Plugins failing to load types is routine in a CMS. Losing the scan would empty the
        // retention screen and, worse, empty the cleanup job's list of jobs to leave alone.
        var scanner = Substitute.For<ITypeScannerLookup>();
        scanner.AllTypes.Returns(_ => throw new ReflectionTypeLoadException(
            [typeof(ChattyTestJob)],
            [new InvalidOperationException("one type would not load")]));

        var index = new LoggedJobTypeIndex(scanner);

        // The partial results survive: the exception carries the types that did load. Losing them
        // would not just empty the screen — the cleanup job's list of job types to leave alone comes
        // from here, so an empty index lets the default sweep delete history a job asked to keep.
        Assert.True(index.Exists(typeof(ChattyTestJob).FullName!));
    }

    [Fact]
    public void AScannerThatFailsOutright_FallsBackToTheLoadedAssemblies_RatherThanEmptying()
    {
        // The branch a real failure lands in: EPiServer.Framework raises its own
        // TypeScannerReflectionException, not ReflectionTypeLoadException. This used to return an empty
        // index, which is the *harmful* answer — the cleanup job's list of job types to leave alone
        // comes from here, so an empty one lets the default sweep delete history that a
        // [JobRetention(Indefinite)] explicitly asked to keep for ever.
        var scanner = Substitute.For<ITypeScannerLookup>();
        scanner.AllTypes.Returns(_ => throw new InvalidOperationException("scanner unavailable"));

        var index = new LoggedJobTypeIndex(scanner);

        // The fallback scans loaded assemblies, and this test assembly's own jobs are loaded.
        Assert.Contains(typeof(PlainTestJob).FullName, index.LoggedJobTypeNames);
    }

    [Fact]
    public void ATypeThatCannotBeInspected_DoesNotCostTheWholeIndex()
    {
        // IsAssignableFrom loads the base chain and GetCustomAttribute loads the attribute's assembly;
        // either can throw for a plugin whose dependencies are incomplete. That work used to sit
        // outside every guard, so one bad type escaped the Lazy and took down both the retention
        // screen and the cleanup job.
        var index = new LoggedJobTypeIndex(ScannerOver(
            new ThrowingType(),
            typeof(PlainTestJob)));

        Assert.Contains(typeof(PlainTestJob).FullName, index.LoggedJobTypeNames);
    }

    /// <summary>
    /// A <see cref="Type"/> whose inspection throws, standing in for a type whose base class or
    /// attribute assembly cannot be loaded.
    /// </summary>
    private sealed class ThrowingType : TypeDelegator
    {
        public override string? FullName => throw new TypeLoadException("base chain unavailable");
    }
}
