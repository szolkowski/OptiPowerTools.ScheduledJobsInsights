namespace OptiPowerTools.ScheduledJobsInsights.Tests.Components;

/// <summary>
/// Groups the detail-page test classes so xUnit does not run them concurrently.
/// </summary>
/// <remarks>
/// <c>Detail.PollInterval</c> is a static test seam. Shortening it in one class while another is
/// rendering the same component would make both depend on the order they happened to interleave in.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DetailTestCollection
{
    public const string Name = "Detail page";
}
