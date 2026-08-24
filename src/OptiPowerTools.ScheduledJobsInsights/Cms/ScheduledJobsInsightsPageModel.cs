namespace OptiPowerTools.ScheduledJobsInsights.Cms;

/// <summary>
/// What the CMS shell view needs in order to render the insights pages.
/// </summary>
/// <remarks>
/// <para>
/// A typed model rather than <c>ViewBag</c>, and the reason is specific rather than stylistic: these
/// are the values whose loss breaks the UI *silently*. Rename a <c>ViewBag</c> key and the component
/// simply receives <c>null</c> — the page renders in UTC, or the retention audit trail records
/// "unknown" — with no compile error and no runtime error to notice. In a repo built with
/// <c>TreatWarningsAsErrors</c>, nullable reference types enabled and <c>CA1305</c> promoted to an
/// error, this was the one untyped seam left, and it carried exactly the parameters that matter.
/// </para>
/// <para>
/// Internal: it crosses from this package's controller to this package's view and is no part of the
/// contract with a consumer.
/// </para>
/// </remarks>
/// <param name="ExecutionId">
/// Execution to show in detail, or <c>null</c> for the list. From the <c>id</c> query string.
/// </param>
/// <param name="ViewerTimeZone">
/// The reader's IANA time zone from the <c>sji-timezone</c> cookie, or <c>null</c> before the browser
/// has recorded one — in which case the pages render in UTC and say so.
/// </param>
/// <param name="ShowRetention">Whether to render the retention screen rather than the execution list.</param>
/// <param name="CurrentUser">
/// The signed-in user's name, for the retention screen's audit trail. Passed in because a component
/// has no <c>HttpContext</c> once the circuit takes over.
/// </param>
/// <param name="PageTitle">Title for the shell chrome and the browser tab.</param>
internal sealed record ScheduledJobsInsightsPageModel(
    long? ExecutionId,
    string? ViewerTimeZone,
    bool ShowRetention,
    string? CurrentUser,
    string PageTitle);
