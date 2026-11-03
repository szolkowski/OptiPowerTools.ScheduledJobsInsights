namespace OptiPowerTools.ScheduledJobsInsights.Web.Samples;

/// <summary>
/// Not part of the NuGet package — attribute values the samples share, where the two dev hosts need
/// different ones. These files are compiled by both the CMS 13 host and the CMS 12 one
/// (OptiPowerTools.ScheduledJobsInsights.Cms12.Web), which defines CMS12.
/// </summary>
internal static class SampleDefaults
{
    /// <summary>
    /// The samples' <c>DefaultEnabled</c>. The intent on both CMS versions is the same: listed in the
    /// Scheduled Jobs admin, startable by hand, never fired by the scheduler on its own.
    /// </summary>
    /// <remarks>
    /// CMS 13 registers a <c>DefaultEnabled = false</c> job as a disabled one. CMS 12 does not register
    /// it at all — its plug-in descriptors take <c>Enabled</c> from <c>DefaultEnabled</c>, and the job
    /// locator skips disabled descriptors — so the samples would not appear. They are enabled there
    /// instead, which is still safe: none of them sets an <c>IntervalLength</c>, so CMS 12 gives them no
    /// next execution and they only ever run when started.
    /// </remarks>
#if CMS12
    public const bool Enabled = true;
#else
    public const bool Enabled = false;
#endif
}
