using OptiPowerTools.ScheduledJobsInsights.Logging;

namespace OptiPowerTools.ScheduledJobsInsights.Tests.Logging;

/// <summary>
/// The one place this package decides where a string may be cut. Extracted because it had to be got
/// right in three separate call sites and was got right in two.
/// </summary>
public class TextBoundsTests
{
    private const string Emoji = "\U0001F600";   // two UTF-16 chars

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ANonPositiveCount_CutsEverything(int count) =>
        Assert.Equal(0, TextBounds.CutAt("anything", count));

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(int.MaxValue)]
    public void ACountAtOrPastTheEnd_IsClampedToTheLength(int count) =>
        Assert.Equal(8, TextBounds.CutAt("anything", count));

    [Fact]
    public void AnEmptyString_CutsToZero() => Assert.Equal(0, TextBounds.CutAt(string.Empty, 5));

    [Fact]
    public void ACutThatSplitsNoPair_IsReturnedUnchanged() =>
        Assert.Equal(3, TextBounds.CutAt("abcdef", 3));

    [Fact]
    public void ACutBetweenAHighAndLowSurrogate_MovesBackOne()
    {
        // "a" + emoji: cutting at 2 would keep the high surrogate and drop its partner, storing half
        // a code point — which renders as a replacement glyph.
        var text = "a" + Emoji;

        Assert.Equal(1, TextBounds.CutAt(text, 2));
    }

    [Fact]
    public void ACutOnAPairBoundary_KeepsTheWholePair()
    {
        var text = Emoji + "tail";

        Assert.Equal(2, TextBounds.CutAt(text, 2));
    }

    [Fact]
    public void ACutInsideTheFirstPair_YieldsNothingRatherThanHalfACharacter()
    {
        Assert.Equal(0, TextBounds.CutAt(Emoji + "tail", 1));
    }

    [Fact]
    public void EveryCutOfAnAllEmojiString_LeavesNoLoneSurrogate()
    {
        // The property that matters, asserted across every boundary rather than at one chosen index.
        var text = string.Concat(Enumerable.Repeat(Emoji, 10));

        for (var count = 0; count <= text.Length + 2; count++)
        {
            var cut = text[..TextBounds.CutAt(text, count)];

            Assert.False(
                cut.Length > 0 && char.IsHighSurrogate(cut[^1]),
                $"cut at {count} ended on a lone high surrogate");
            Assert.Equal(0, cut.Length % 2);
        }
    }
}
