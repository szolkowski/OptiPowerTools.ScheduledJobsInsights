using System.Reflection;
using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests;

/// <summary>
/// Guards the handful of documentation claims that are mechanically checkable.
/// </summary>
/// <remarks>
/// The README is packed into the <c>.nupkg</c> and is what nuget.org renders, so a defect in it
/// cannot be corrected without publishing a new version. That makes drift between it and the code
/// more expensive than ordinary doc rot, and worth a test where the claim is a literal string.
/// </remarks>
public class DocumentationTests
{
    /// <summary>
    /// The repository root, found by walking up from the test assembly.
    /// </summary>
    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root above {AppContext.BaseDirectory}. This test reads README.md from the working tree.");
    }

    [Fact]
    public void EveryAutomaticMetricName_AppearsInTheReadme()
    {
        // These names are written into JobMetrics.Name and rendered on the detail page, so they are a
        // data contract: somebody reads the README, writes a dashboard query against it, and gets
        // nothing back. Exactly that happened once already — two metrics were deliberately renamed to
        // say whose thread and whose CPU they measure, and the README was never updated to match.
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot().FullName, "README.md"));

        var names = typeof(JobMetricNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(names);

        var missing = names.Where(name => !readme.Contains(name, StringComparison.Ordinal)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"These metric names are recorded by the package but documented nowhere in README.md: {string.Join(", ", missing)}");
    }
}
