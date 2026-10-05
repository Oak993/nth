using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Xunit;

namespace Marrow.App.Tests.Port.Voice;

// VT-11, V-T76 (voice-SPEC V-8.3): the caret after the last word is 1.5 px wide and 17 px high, in the text colour, blinking on the design's keyframes
// (voice-composer.html:132-133 `blink 1.06s ease-in-out infinite`: 1 until 45 %, to 0 by 55 %, 0 until 95 %, back to 1 by 100 %); the design stills it under reduced motion.
public sealed class DictationCaretTests
{
    private static double EaseInOut(double x)
    {
        // cubic-bezier(.42, 0, .58, 1), symmetric about its middle: solved the way CssCurves solves.
        double Bez(double t) => 3 * (1 - t) * t * t + t * t * t; // y: control points 0 and 1
        double X(double t) => 3 * (1 - t) * (1 - t) * t * 0.42 + 3 * (1 - t) * t * t * 0.58 + t * t * t;
        double low = 0, high = 1;
        for (var i = 0; i < 80; i++)
        {
            var mid = (low + high) / 2;
            if (X(mid) < x) low = mid;
            else high = mid;
        }
        return Bez((low + high) / 2);
    }

    [Fact]
    [Trait("Decision", "ED-292")]
    public void The_caret_is_1_5_by_17_and_blinks_every_1_06_s()
    {
        Assert.Equal(1.5, DictationCaret.Width);
        Assert.Equal(17, DictationCaret.Height);
        Assert.Equal(1.06, DictationCaret.Period);
    }

    [Fact]
    [Trait("Decision", "ED-292")]
    public void The_blink_follows_the_keyframes_sample_by_sample()
    {
        for (var ms = 0; ms < 3200; ms += 7)
        {
            var seconds = ms / 1000.0;
            var phase = seconds % 1.06 / 1.06;
            var expected = phase <= 0.45 ? 1
                : phase < 0.55 ? 1 - EaseInOut((phase - 0.45) / 0.1)
                : phase <= 0.95 ? 0
                : EaseInOut((phase - 0.95) / 0.05);
            var actual = DictationCaret.Opacity(seconds, still: false);
            Assert.True(Math.Abs(expected - actual) < 1e-6, $"at {seconds:F3} s: {actual} against the keyframes' {expected}");
        }
    }

    [Fact]
    [Trait("Decision", "ED-292")]
    public void Under_reduce_motion_the_caret_does_not_blink()
    {
        for (var ms = 0; ms < 2200; ms += 50) Assert.Equal(1, DictationCaret.Opacity(ms / 1000.0, still: true));
    }
}
