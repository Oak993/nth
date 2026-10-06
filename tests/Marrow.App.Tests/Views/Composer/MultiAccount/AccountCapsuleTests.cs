using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing.MultiAccountSettings;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer.MultiAccount;

// MA-9 (step 3a, OD-0115): the ONE shared capsule: all twelve styles and the plain account capsule, the usage light easing over 0.6 s, Reduce Motion, the click it raises.
[Collection("Headless")]
public sealed class AccountCapsuleTests(HeadlessSession headless)
{
    private static CapsuleContent Pill(int count = 3, int cur = 0, double? use = 0.3, bool handshake = false, int? stand = null, IReadOnlyList<int>? used = null, string name = "Deep work")
    {
        var steps = Enumerable.Range(0, count).Select(i => new CapsuleStep("S" + (i + 1), 0x808080u + (uint)i, false)).ToArray();
        var view = new CapsuleView(steps, cur, used, stand, handshake, use, 0.1, name);
        return new CapsuleContent(steps[cur].Mono, "Account", use ?? 0, false, "Pill " + name + ", step " + (cur + 1) + " of " + count + ": Account. Open accounts and pills", view);
    }

    private static CapsuleContent Plain(double use = 0.3) =>
        new("M2", "Claude Max 2", use, CapsuleLayout.IsHot(use), "Account: Claude Max 2, " + (int)Math.Round(use * 100) + "% of its 5-hour limit used. Change account", null);

    private void Run(string style, Action<MultiAccountHarness, AccountCapsule> check, bool motion = false, bool reduceMotion = false, Action<AccountCapsule>? setup = null)
    {
        var rig = new AccountsRig();
        AccountCapsule? capsule = null;
        MultiAccountHarness.Run(headless, rig, () =>
        {
            capsule = new AccountCapsule(style) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20) };
            setup?.Invoke(capsule);
            return capsule;
        }, h => check(h, capsule!), motion, reduceMotion);
    }

    private static string Text((byte R, byte G, byte B) c) => c.R + "," + c.G + "," + c.B;

    [Theory]
    [InlineData("1", 48)]
    [InlineData("2", 34)]
    [InlineData("3", 34)]
    [InlineData("4", 36)]
    [InlineData("5", 44)]
    [InlineData("6", 44)]
    [InlineData("7", 46)]
    [InlineData("8", 40)]
    [InlineData("9", 22)]
    [InlineData("10", 34)]
    [InlineData("11", 44)]
    [InlineData("11o", 44)]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Every_pill_style_is_the_pages_width_and_twenty_two_high(string style, int width) => Run(style, (h, capsule) =>
    {
        capsule.Apply(Pill(), animate: false);
        h.Settle();
        Assert.Equal(width, capsule.Bounds.Width, 3);
        Assert.Equal(22, capsule.Bounds.Height, 3);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_plain_account_capsule_is_at_least_thirty_four_wide_and_twenty_two_high() => Run("plain", (h, capsule) =>
    {
        capsule.Apply(Plain(), animate: false);
        h.Settle();
        Assert.True(capsule.Bounds.Width >= 34);
        Assert.Equal(22, capsule.Bounds.Height, 3);
        Assert.Equal("M2", capsule.Mono);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_capsule_is_a_button_that_says_what_it_is_for_a_screen_reader() => Run("1", (h, capsule) =>
    {
        var content = Pill();
        capsule.Apply(content, animate: false);
        h.Settle();
        Assert.Equal(content.Aria, AutomationProperties.GetName(capsule.Face));
        Assert.True(capsule.Face.Focusable);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_click_and_the_keyboard_raise_Clicked_and_nothing_else_happens() => Run("plain", (h, capsule) =>
    {
        capsule.Apply(Plain(), animate: false);
        h.Settle();
        var clicks = 0;
        capsule.Clicked += () => clicks++;
        var centre = capsule.TranslatePoint(new Point(capsule.Bounds.Width / 2, 11), h.Window)!.Value;
        h.Window.MouseDown(centre, MouseButton.Left);
        h.Window.MouseUp(centre, MouseButton.Left);
        h.Settle();
        Assert.Equal(1, clicks);
        capsule.Face.Focus();
        h.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        h.Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        h.Settle();
        Assert.Equal(2, clicks);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_usage_light_is_the_ramp_colour_of_what_is_left_and_eases_there_over_six_tenths_of_a_second() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.3), animate: false);
        h.Settle();
        Assert.Equal(Text(UsageLight.Color(0.7, dark: false)), Text(capsule.LightNow));
        capsule.Apply(Pill(use: 0.8), animate: true);
        h.Step(300);
        var middle = capsule.LightNow;
        Assert.NotEqual(Text(UsageLight.Color(0.7, false)), Text(middle));
        Assert.NotEqual(Text(UsageLight.Color(0.2, false)), Text(middle));
        h.Step(400);
        Assert.Equal(Text(UsageLight.Color(0.2, dark: false)), Text(capsule.LightNow));
    }, motion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Reduce_Motion_turned_on_while_the_light_eases_lands_it_at_once_and_leaves_no_frame_waiting() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.3), animate: false);
        h.Settle();
        capsule.Apply(Pill(use: 0.8), animate: true);
        h.Step(100);
        Assert.True(h.Frames.Pending > 0, "the ease waits for a frame");
        Marrow.UI.Port.EnvironmentValues.SetAccessibilityReduceMotion(h.Window, true);
        Assert.Equal(0, h.Frames.Pending);
        Assert.Equal(Text(UsageLight.Color(0.2, dark: false)), Text(capsule.LightNow));
    }, motion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Under_Reduce_Motion_the_light_lands_at_once() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.3), animate: false);
        h.Settle();
        capsule.Apply(Pill(use: 0.8), animate: true);
        Assert.Equal(Text(UsageLight.Color(0.2, dark: false)), Text(capsule.LightNow));
    }, motion: true, reduceMotion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void In_the_dark_theme_the_light_is_the_dark_ramp_and_a_theme_change_recolours_it_in_place() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.3), animate: false);
        h.Settle();
        Assert.Equal(Text(UsageLight.Color(0.7, false)), Text(capsule.LightNow));
        h.Window.RequestedThemeVariant = ThemeVariant.Dark;
        h.Settle();
        h.Step(700);
        Assert.Equal(Text(UsageLight.Color(0.7, dark: true)), Text(capsule.LightNow));
    }, motion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_next_light_shows_what_is_left_of_the_stand_by_account_only_while_a_handshake_runs() => Run("11", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.5, handshake: false), animate: false);
        h.Settle();
        Assert.Equal(Text(UsageLight.Color(0.5, false)), Text(capsule.NextNow));
        capsule.Apply(Pill(use: 0.5, handshake: true), animate: false);
        h.Settle();
        Assert.Equal(Text(UsageLight.Color(0.9, false)), Text(capsule.NextNow));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_steps_are_marked_used_current_and_next_and_the_stand_by_one_is_marked() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(count: 4, cur: 1, stand: 3), animate: false);
        h.Settle();
        Assert.Equal([StepRole.Used, StepRole.Cur, StepRole.Next, StepRole.Next], capsule.Marks.Select(m => m.Role));
        Assert.Equal([false, false, false, true], capsule.Marks.Select(m => m.Stand));
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_pill_name_is_the_tip_and_the_step_light_count_follows_the_pill() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(count: 4, cur: 2), animate: false);
        h.Settle();
        Assert.Equal("Deep work · step 3 of 4", capsule.TipText);
        capsule.Apply(Pill(count: 2, cur: 0), animate: false);
        h.Settle();
        Assert.Equal(2, capsule.Marks.Count);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Name_below_shows_the_name_under_the_capsule_and_gives_no_tip_while_the_others_keep_it_for_the_tip() => Run("2", (h, capsule) =>
    {
        capsule.Apply(Pill(), animate: false);
        h.Settle();
        Assert.Equal("Deep work", capsule.NameBelow);
        Assert.Null(capsule.TipText);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_plain_capsules_arc_is_the_use_and_it_is_hot_from_ninety_percent() => Run("plain", (h, capsule) =>
    {
        capsule.Apply(Plain(0.4), animate: false);
        h.Settle();
        Assert.Equal(0.4, capsule.ArcNow, 6);
        Assert.False(capsule.Hot);
        capsule.Apply(Plain(0.95), animate: false);
        h.Settle();
        Assert.Equal(0.95, capsule.ArcNow, 6);
        Assert.True(capsule.Hot);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_orbit_drifts_round_a_twenty_eight_second_lap_and_stands_still_under_Reduce_Motion() => Run("5", (h, capsule) =>
    {
        capsule.Apply(Pill(), animate: false);
        h.Settle();
        var before = capsule.Phase;
        h.Step(1000);
        Assert.True(capsule.Phase > before, "the orbit moved");
    }, motion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Under_Reduce_Motion_nothing_loops() => Run("5", (h, capsule) =>
    {
        capsule.Apply(Pill(), animate: false);
        h.Settle();
        var before = capsule.Phase;
        h.Step(1000);
        Assert.Equal(before, capsule.Phase);
        Assert.Equal(0, capsule.Phase);
    }, motion: true, reduceMotion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void A_style_that_does_not_move_runs_no_loop_and_leaves_no_frame_request() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(), animate: false);
        h.Settle();
        h.Step(100);
        Assert.Equal(0, h.Frames.Pending);
    }, motion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Liquid_fill_rises_to_the_use_never_below_four_percent_and_eases_over_nine_tenths_of_a_second() => Run("6", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.5), animate: false);
        h.Settle();
        Assert.Equal(0.5, capsule.FillNow, 6);
        capsule.Apply(Pill(use: 0), animate: true);
        h.Step(450);
        Assert.InRange(capsule.FillNow, 0.05, 0.49);
        h.Step(600);
        Assert.Equal(0.04, capsule.FillNow, 6);
    }, motion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_glow_blooms_on_hover_tighter_on_press_and_softly_during_a_handshake() => Run("11", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.8), animate: false);
        h.Settle();
        Assert.Equal(0, capsule.GlowOpacityNow, 6);
        var centre = capsule.TranslatePoint(new Point(22, 11), h.Window)!.Value;
        h.Window.MouseMove(centre);
        h.Settle();
        Assert.Equal(9, capsule.GlowBlurNow, 3);
        Assert.Equal(0.85 * CapsuleLayout.GlowBoost(0.2), capsule.GlowOpacityNow, 3);
        h.Window.MouseDown(centre, MouseButton.Left);
        h.Settle();
        Assert.Equal(2, capsule.GlowBlurNow, 3);
        h.Window.MouseUp(centre, MouseButton.Left);
        h.Window.MouseMove(new Point(400, 400));
        h.Settle();
        Assert.Equal(0, capsule.GlowOpacityNow, 6);
        capsule.Apply(Pill(use: 0.8, handshake: true), animate: false);
        h.Settle();
        Assert.Equal(6, capsule.GlowBlurNow, 3);
        Assert.Equal(0.55 * CapsuleLayout.GlowBoost(0.2), capsule.GlowOpacityNow, 3);
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Under_Reduce_Motion_the_glow_is_seven_pixels_of_blur_wherever_it_shows() => Run("11", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.8, handshake: true), animate: false);
        h.Settle();
        Assert.Equal(7, capsule.GlowBlurNow, 3);
    }, reduceMotion: true);

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_the_glow_styles_have_a_glow() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(), animate: false);
        h.Settle();
        Assert.False(capsule.HasGlow);
    });

    // ---- pixels: the capsule drawn, in both themes

    private static SKBitmap Draw(Control root, int width, int height)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(root);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static bool Near(SKColor pixel, (byte R, byte G, byte B) wanted, int slack = 40) =>
        Math.Abs(pixel.Red - wanted.R) <= slack && Math.Abs(pixel.Green - wanted.G) <= slack && Math.Abs(pixel.Blue - wanted.B) <= slack && pixel.Alpha > 150;

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Merged_capsule_draws_the_current_light_in_the_usage_colour_on_the_top_edge() => Run("1", (h, capsule) =>
    {
        capsule.Apply(Pill(count: 3, cur: 1, use: 0.3), animate: false);
        h.Settle();
        using var bitmap = Draw(capsule, 48, 22);
        // three dots of 3.5 with 3 between, centred: the middle one is the current step, 2.5 from the top
        var pixel = bitmap.GetPixel(24, 4);
        Assert.True(Near(pixel, UsageLight.Color(0.7, false)), $"the current light is {pixel}");
        var next = bitmap.GetPixel(24 + 6, 4);
        Assert.False(Near(next, UsageLight.Color(0.7, false), 10), "a step to come is not in the usage light");
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void Liquid_fill_tints_the_lower_part_of_the_glass_with_the_usage_light_and_leaves_the_top_clear() => Run("6", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.5), animate: false);
        h.Settle();
        using var bitmap = Draw(capsule, 44, 22);
        var low = bitmap.GetPixel(22, 20);
        var top = bitmap.GetPixel(22, 3);
        var wanted = UsageLight.Color(0.5, false);
        Assert.True(Math.Abs(low.Blue - wanted.B) < Math.Abs(top.Blue - wanted.B) || Math.Abs(low.Red - wanted.R) < Math.Abs(top.Red - wanted.R), $"low {low} top {top}");
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_original_colours_glow_rim_is_blue_on_the_left_and_pink_on_the_right_whatever_the_usage() => Run("11o", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.95), animate: false);
        h.Settle();
        using var bitmap = Draw(capsule, 44, 22);
        var left = bitmap.GetPixel(12, 0);
        var right = bitmap.GetPixel(32, 0);
        Assert.True(left.Blue > left.Red, $"left rim {left}");
        Assert.True(right.Red > right.Green, $"right rim {right}");
    });

    [Fact]
    [Trait("Feature", "MA-9")]
    [Trait("Rule", "R-TEST-4")]
    public void The_usage_light_glow_rim_is_the_usage_colour_not_the_original_blue_to_pink() => Run("11", (h, capsule) =>
    {
        capsule.Apply(Pill(use: 0.95), animate: false);
        h.Settle();
        using var bitmap = Draw(capsule, 44, 22);
        var left = bitmap.GetPixel(12, 0);
        Assert.True(Near(left, UsageLight.Color(0.05, false), 60), $"left rim {left}");
    });
}
