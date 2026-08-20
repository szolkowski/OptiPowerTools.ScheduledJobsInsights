using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace OptiPowerTools.ScheduledJobsInsights.Components.Shared;

/// <summary>
/// Renders stored <see cref="DateTimeOffset"/> values in the time zone of the person reading the
/// page, falling back to UTC when that zone is unknown or unusable.
/// </summary>
/// <remarks>
/// <para>
/// This is a server-rendered UI, so the only zone it can reach for unaided is the *server's* — which
/// is rarely the reader's, and on a UTC container meant an administrator elsewhere was shown UTC
/// dressed up as local time. The browser's IANA zone id arrives instead as a component parameter
/// from the hosting view (see <c>Views/ScheduledJobsInsightsCms/Index.cshtml</c>), the same route the
/// execution id already takes; a small inline script stores it in a cookie for the view to read.
/// </para>
/// <para>
/// The zone id — not a UTC offset — is what gets passed, and that matters. An offset captured "now"
/// applied to an execution from before a daylight-saving change renders it an hour out;
/// <see cref="TimeZoneInfo.ConvertTime(DateTimeOffset, TimeZoneInfo)"/> resolves the rules per
/// timestamp instead, so history stays correct across the transition.
/// </para>
/// <para>
/// Everything here degrades to UTC rather than throwing. The id comes from a cookie, so it is
/// untrusted input: it may be absent, malformed, or name a zone this host has no data for.
/// </para>
/// <para>
/// Date ordering stays ISO (yyyy-MM-dd) and numbers stay invariant even here — see
/// <see cref="DisplayFormat"/>. Only the *zone* follows the reader; the format does not, because a
/// locale-ordered date reintroduces the day/month ambiguity this ordering exists to avoid.
/// </para>
/// </remarks>
internal sealed class ViewerClock
{
    /// <summary>Cookie the hosting view reads the browser's IANA zone id from.</summary>
    public const string CookieName = "sji-timezone";

    /// <summary>Longest zone id accepted from the cookie. Real ones are far shorter than this.</summary>
    private const int MaxZoneIdLength = 64;

    private readonly TimeZoneInfo _zone;

    private ViewerClock(TimeZoneInfo zone, string label)
    {
        _zone = zone;
        Label = label;
    }

    /// <summary>The UTC fallback, used whenever the reader's zone could not be established.</summary>
    public static ViewerClock Utc { get; } = new(TimeZoneInfo.Utc, "UTC");

    /// <summary>Human-readable name of the zone in use — "UTC" or an IANA id like "Europe/Warsaw".</summary>
    public string Label { get; }

    /// <summary>Whether times are being rendered in UTC because no usable zone was supplied.</summary>
    public bool IsUtcFallback => ReferenceEquals(this, Utc);

    /// <summary>
    /// Builds a clock for an IANA zone id, returning <see cref="Utc"/> for anything unusable.
    /// </summary>
    /// <param name="ianaZoneId">
    /// Zone id as reported by the browser, e.g. "Europe/Warsaw". Null, empty, over-long, malformed
    /// and unknown ids all fall back to UTC.
    /// </param>
    public static ViewerClock ForZone(string? ianaZoneId)
    {
        if (!IsPlausibleZoneId(ianaZoneId))
            return Utc;

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ianaZoneId);
            return zone.Equals(TimeZoneInfo.Utc) ? Utc : new ViewerClock(zone, ianaZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // The host has no data for this zone — plausible on a trimmed container image.
            return Utc;
        }
        catch (InvalidTimeZoneException)
        {
            // Zone data present but corrupt.
            return Utc;
        }
    }

    /// <summary>Full timestamp with an explicit offset — "2026-08-19 17:37:16 UTC+02:00".</summary>
    public string Timestamp(DateTimeOffset value)
    {
        var local = Convert(value);
        return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + OffsetSuffix(local);
    }

    /// <summary>
    /// Timestamp without seconds or offset, for the execution list — the zone is stated once above
    /// the table rather than repeated on every one of fifty rows.
    /// </summary>
    public string CompactTimestamp(DateTimeOffset value) =>
        Convert(value).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Time of day with milliseconds, for console log lines.</summary>
    public string TimeOfDay(DateTimeOffset value) =>
        Convert(value).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private DateTimeOffset Convert(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone);

    /// <summary>
    /// Renders the offset that actually applied at that instant, so a timestamp from the other side
    /// of a daylight-saving change is labelled with the offset it was recorded under.
    /// </summary>
    private static string OffsetSuffix(DateTimeOffset local)
    {
        if (local.Offset == TimeSpan.Zero)
            return "UTC";

        // TimeSpan has no sign specifier in its format strings — a negative offset formats as though
        // it were positive — so the sign has to be baked into the chosen format instead.
        var format = local.Offset < TimeSpan.Zero ? @"\-hh\:mm" : @"\+hh\:mm";

        return "UTC" + local.Offset.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Cheap shape check before hitting the zone database, since this value arrives from a cookie.
    /// IANA ids are ASCII words separated by slashes, with a few punctuation characters in the tail.
    /// </summary>
    private static bool IsPlausibleZoneId([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxZoneIdLength)
            return false;

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('/' or '_' or '-' or '+'))
                return false;
        }

        return true;
    }
}
