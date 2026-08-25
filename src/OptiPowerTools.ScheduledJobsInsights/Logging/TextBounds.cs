namespace OptiPowerTools.ScheduledJobsInsights.Logging;

/// <summary>
/// The one place this package decides where a string may be cut.
/// </summary>
/// <remarks>
/// Extracted because it had to be got right three times and was got right twice. A cut that lands
/// between a high and a low surrogate stores half a code point, which renders as a replacement glyph
/// — so an emoji at the boundary of a truncated summary came out broken. Two of the three call sites
/// were fixed in separate rounds of review; the third, and the one on the normal path, was not. A
/// shared helper is what stops there being a fourth.
/// </remarks>
internal static class TextBounds
{
    /// <summary>
    /// The largest length not greater than <paramref name="count"/> that does not split a surrogate
    /// pair.
    /// </summary>
    /// <param name="text">The text about to be cut.</param>
    /// <param name="count">The desired length.</param>
    /// <returns>
    /// <paramref name="count"/>, or one less when that would leave a high surrogate stranded at the
    /// end. Clamped to the bounds of <paramref name="text"/>.
    /// </returns>
    public static int CutAt(string text, int count)
    {
        if (count <= 0)
            return 0;

        if (count >= text.Length)
            return text.Length;

        // Only the character immediately before the cut matters: a high surrogate there means its low
        // partner is the character being dropped.
        return char.IsHighSurrogate(text[count - 1]) ? count - 1 : count;
    }
}
