namespace OptiPowerTools.ScheduledJobsInsights.Extensions;

/// <summary>
/// Records whether this package has already mapped the Blazor hub for the current application.
/// </summary>
/// <remarks>
/// <para>
/// A container singleton rather than a static field, deliberately: a static would be shared by every
/// application in the process, so a test host or a second in-process application would find the hub
/// "already mapped" and silently go without one.
/// </para>
/// <para>
/// Mapping the hub twice registers two endpoints on the same route pattern, and every Blazor request
/// then fails with <c>AmbiguousMatchException</c>. Endpoint inspection alone cannot prevent it,
/// because a host that sets
/// <see cref="Configuration.OptiPowerToolsScheduledJobsInsightsOptions.MapBlazorHub"/> to <c>true</c>
/// is explicitly asking to skip that detection — and both public entry points lead here.
/// </para>
/// </remarks>
internal sealed class HubMappedMarker
{
    /// <summary>Whether this application has already had its hub mapped by this package.</summary>
    public bool Mapped { get; set; }
}
