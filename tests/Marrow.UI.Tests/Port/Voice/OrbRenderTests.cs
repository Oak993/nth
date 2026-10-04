using SkiaSharp;

namespace Marrow.UI.Tests.Port.Voice;

// VT-8, V-11.2, V-11.6: what the drawn orb is, apart from the reference frames: a premultiplied ball centred in the 72 px view with the glow around it, the presets' own colours
// in both themes (the theme only changes the lift shadow's strength), the error look draining the colour.
public sealed class OrbRenderTests
{
    private static OrbJob Job(string orb, string state, string theme = "dark", int ball = 32) => new(orb, state, ball, theme);

    private static SKBitmap Alone(OrbJob job) => OrbRig.Render(job, background: SKColors.Transparent);

    private static double Chroma(SKBitmap bitmap, int radius)
    {
        double sum = 0; var n = 0;
        for (var y = 36 - radius; y <= 36 + radius; y++)
            for (var x = 36 - radius; x <= 36 + radius; x++)
            {
                if ((x - 36) * (x - 36) + (y - 36) * (y - 36) > radius * radius) continue;
                var p = bitmap.GetPixel(x, y);
                sum += Math.Max(p.Red, Math.Max(p.Green, p.Blue)) - Math.Min(p.Red, Math.Min(p.Green, p.Blue));
                n++;
            }
        return sum / n;
    }

    [Theory]
    [InlineData("siri-wave")]
    [InlineData("voice-membrane")]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    public void The_ball_is_opaque_at_the_centre_of_the_view_and_the_corners_are_clear(string orb)
    {
        using var bitmap = Alone(Job(orb, "rest-1.3"));
        Assert.Equal((72, 72), (bitmap.Width, bitmap.Height));
        Assert.True(bitmap.GetPixel(36, 36).Alpha >= 250, "the centre");
        Assert.True(bitmap.GetPixel(36, 8).Alpha == 0 && bitmap.GetPixel(0, 0).Alpha == 0 && bitmap.GetPixel(71, 71).Alpha == 0, "outside the ball and its glow");
    }

    [Theory]
    [InlineData("siri-wave", "hover")]
    [InlineData("voice-membrane", "processing")]
    [InlineData("siri-wave", "listen-loud")]
    [InlineData("voice-membrane", "blocked")]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    public void The_output_is_premultiplied_as_Skia_expects(string orb, string state)
    {
        using var bitmap = Alone(Job(orb, state));
        var bytes = bitmap.Bytes;   // RGBA, premultiplied (GetPixel would un-premultiply)
        for (var i = 0; i < bytes.Length; i += 4)
            Assert.True(bytes[i] <= bytes[i + 3] + 1 && bytes[i + 1] <= bytes[i + 3] + 1 && bytes[i + 2] <= bytes[i + 3] + 1, $"pixel {i / 4} is {bytes[i]}, {bytes[i + 1]}, {bytes[i + 2]}, {bytes[i + 3]}");
    }

    [Theory]
    [InlineData("siri-wave")]
    [InlineData("voice-membrane")]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    public void The_glow_lights_the_pixels_outside_the_ball_that_rest_leaves_clear(string orb)
    {
        using var rest = Alone(Job(orb, "rest-1.3"));
        using var hover = Alone(Job(orb, "hover"));
        // the ball is 32 px (16 px radius; 16.6 popped): 5 px past its edge is only the glow's
        Assert.Equal(0, rest.GetPixel(36 + 21, 36).Alpha);
        Assert.True(hover.GetPixel(36 + 21, 36).Alpha > 0, "the hover glow reaches past the ball");
    }

    [Theory]
    [InlineData("siri-wave")]
    [InlineData("voice-membrane")]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    public void The_theme_changes_nothing_while_the_lift_is_zero(string orb)
    {
        using var dark = Alone(Job(orb, "rest-1.3", "dark"));
        using var light = Alone(Job(orb, "rest-1.3", "light"));
        Assert.Equal(dark.Bytes, light.Bytes);
    }

    [Theory]
    [InlineData("siri-wave")]
    [InlineData("voice-membrane")]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    public void The_theme_only_changes_the_lift_shadows_strength_not_the_orbs_own_colours(string orb)
    {
        using var dark = Alone(Job(orb, "listen-speech", "dark"));
        using var light = Alone(Job(orb, "listen-speech", "light"));
        Assert.NotEqual(dark.Bytes, light.Bytes);
        // inside 12 px of the centre the shadow's alpha adds nothing (the ball is opaque there): the colours are the presets' own
        for (var y = 24; y <= 48; y++)
            for (var x = 24; x <= 48; x++)
            {
                if ((x - 36) * (x - 36) + (y - 36) * (y - 36) > 12 * 12) continue;
                Assert.Equal(dark.GetPixel(x, y), light.GetPixel(x, y));
            }
        // the shadow is stronger in the dark theme (0.30 against 0.42 x ... : mrw.z = 1 gives 0.30, light 0.42): the light theme's shadow is the heavier alpha
        var darkShadow = dark.GetPixel(36, 36 + 19).Alpha;
        var lightShadow = light.GetPixel(36, 36 + 19).Alpha;
        Assert.NotEqual(darkShadow, lightShadow);
    }

    [Theory]
    [InlineData("siri-wave")]
    [InlineData("voice-membrane")]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    public void The_error_look_drains_the_colour_inside_the_ball(string orb)
    {
        using var rest = Alone(Job(orb, "rest-4.0"));
        using var blocked = Alone(Job(orb, "blocked"));
        var calm = Chroma(blocked, 10);
        var normal = Chroma(rest, 10);
        Assert.True(calm < normal * 0.5, $"blocked chroma {calm:F1} against rest {normal:F1}");
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    public void The_two_orbs_are_different_pictures()
    {
        using var siri = Alone(Job("siri-wave", "rest-1.3"));
        using var membrane = Alone(Job("voice-membrane", "rest-1.3"));
        Assert.NotEqual(siri.Bytes, membrane.Bytes);
    }

    [Fact]
    [Trait("Feature", "VT-8")]
    [Trait("Decision", "ED-267")]
    public void The_smaller_slot_draws_a_smaller_ball()
    {
        using var big = Alone(Job("siri-wave", "rest-1.3", ball: 32));
        using var small = Alone(Job("siri-wave", "rest-1.3", ball: 26));
        int Opaque(SKBitmap b) { var n = 0; for (var y = 0; y < 72; y++) for (var x = 0; x < 72; x++) if (b.GetPixel(x, y).Alpha > 128) n++; return n; }
        var a = Opaque(big);
        var c = Opaque(small);
        Assert.InRange(a / (double)c, 1.4, 1.65);   // (32 / 26)^2 = 1.515
    }
}
