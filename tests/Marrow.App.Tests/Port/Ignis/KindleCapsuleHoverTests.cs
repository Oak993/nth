using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.Ignis.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Port.Ignis;

// ED-NEW-kindlehover-1 (owner, 2026-10-11: "Dull pill. Doesn't even do anything when I hover mouse over it?"): the Kindle capsule
// answers the pointer in the composer chip's language (a 7 % primary-text wash under the pointer, 12 % pressed, eased), a cold
// capsule's ring and drop rest part-lifted (the drop at least 3:1) and lift fully and kindle under the pointer, the words stay primary, and
// a real press anywhere on the capsule (the ring, the padding) opens the popover in every state. Real headless pointer input and
// hit testing; the frame clock and the clock are driven by hand.
[Collection("Headless")]
public sealed class KindleCapsuleHoverTests(HeadlessSession headless)
{
    private static readonly Guid Chat = KindleFakeController.Chat;

    private static IgnisChatStatus Warm(TestRuntime runtime) =>
        IgnisChatStatus.Unknown(Chat, IgnisMode.WarmAndTidy, true) with
        {
            Provider = ProviderKind.Claude, Cache = IgnisCacheState.Warm, ExpiresAt = runtime.Time.GetUtcNow() + TimeSpan.FromMinutes(42), Ttl = TimeSpan.FromHours(1),
            ContextUsed = 244_000, ContextWindow = 1_000_000, TidyAt = 400_000, CanKeepWarm = true, CanTidy = true,
        };

    private static IgnisChatStatus Cold(TestRuntime runtime) => Warm(runtime) with { Cache = IgnisCacheState.Cold, ExpiresAt = null };

    private static IgnisChatStatus Off(TestRuntime runtime) => Cold(runtime) with { Mode = IgnisMode.Off, Cache = IgnisCacheState.Off };

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "ED-NEW-kindlehover-1")]
    public void Hover_washes_a_cold_capsule_lifts_its_ring_kindles_its_core_and_leaving_reverts() => headless.OnUiThread(() =>
    {
        using var rig = new KindleCapsuleRig(Cold);
        var capsule = rig.Capsule;
        var primary = rig.Resolve(ChromeTokens.PrimaryText);
        rig.Runtime.OnMain(() =>
        {
            Assert.Equal("Cold", capsule.Text);
            Assert.Equal(primary, capsule.WordsColor); // the words stay primary at rest
            Assert.Equal(0, capsule.WashColor.A);
            Assert.Equal(KindleCapsule.ColdRestLift, capsule.Glyph.Lift); // a Cold capsule rests part-lifted
            Assert.Equal(0, capsule.Glyph.Kindle);
            Assert.False(capsule.Glyph.IsAmbientRunning, "a cold capsule at rest runs no clock");
            Assert.NotNull(capsule.Button.Cursor);
            Assert.Null(ToolTip.GetTip(capsule.Button)); // OD-0138: no tooltip on Ignis
        });

        rig.Move(rig.Ring);
        rig.Runtime.RunUntilIdle();
        Assert.True(capsule.Button.IsPointerOver, "the ring takes the pointer");
        Assert.True(capsule.IsLifted);
        var hoverWash = rig.Resolve(ChromeTokens.Overlay(KindleCapsule.HoverWash));
        // eased, not snapped: part-way after one short frame
        rig.Step(30);
        Assert.InRange(capsule.WashColor.A, 1, hoverWash.A - 1);
        Assert.InRange(capsule.Glyph.Lift, KindleCapsule.ColdRestLift + .01, .99);
        Assert.InRange(capsule.Glyph.Kindle, .001, .99);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.Equal(hoverWash, capsule.WashColor);
        Assert.Equal(primary, capsule.WordsColor);
        Assert.Equal(1, capsule.Glyph.Lift);
        Assert.Equal(1, capsule.Glyph.Kindle);
        Assert.False(capsule.Glyph.IsAmbientRunning, "the kindling is an eased value, not a running clock");
        // the hovered ring, separator and drop are brighter than at rest
        var palette = KindlePalette.For(capsule.Glyph);
        Assert.True(KindleGlyph.TrackColor(palette, 1).A > KindleGlyph.TrackColor(palette, KindleCapsule.ColdRestLift).A, "the track lifts");
        Assert.NotEqual(KindleFlame.ColdBase(palette, KindleCapsule.ColdRestLift), KindleFlame.ColdBase(palette, 1));

        // pressed: the deeper wash (released away from the capsule, so no click)
        rig.Down(rig.Ring);
        rig.Runtime.RunUntilIdle();
        Assert.True(capsule.Button.IsPressed);
        for (var i = 0; i < 5; i++) rig.Step(40);
        Assert.Equal(rig.Resolve(ChromeTokens.Overlay(KindleCapsule.PressedWash)), capsule.WashColor);
        rig.Up(rig.Away);
        rig.Move(rig.Away);
        rig.Runtime.RunUntilIdle();
        Assert.False(rig.Presented, "a press released elsewhere does not open the popover");
        Assert.False(capsule.Button.IsPointerOver);
        rig.Step(30);
        Assert.InRange(capsule.Glyph.Lift, KindleCapsule.ColdRestLift + .01, .99);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.Equal(0, capsule.WashColor.A);
        Assert.Equal(primary, capsule.WordsColor);
        Assert.Equal(KindleCapsule.ColdRestLift, capsule.Glyph.Lift);
        Assert.Equal(0, capsule.Glyph.Kindle);

        // Reduce Motion: the same states, taken at once
        rig.Runtime.OnMain(() => EnvironmentValues.SetAccessibilityReduceMotion(capsule, true));
        rig.Move(rig.Ring);
        rig.Runtime.RunUntilIdle();
        Assert.Equal(hoverWash, capsule.WashColor);
        Assert.Equal(1, capsule.Glyph.Lift);
        Assert.Equal(1, capsule.Glyph.Kindle);
        rig.Move(rig.Away);
        rig.Runtime.RunUntilIdle();
        Assert.Equal(0, capsule.WashColor.A);
        Assert.Equal(KindleCapsule.ColdRestLift, capsule.Glyph.Lift);
        Assert.Equal(0, capsule.Glyph.Kindle);
    });

    // WCAG relative luminance contrast of a colour drawn over an opaque surface
    private static double Contrast(Color color, Color surface)
    {
        static double Channel(double v) { v /= 255; return v <= .03928 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        static double Luminance(double r, double g, double b) => .2126 * Channel(r) + .7152 * Channel(g) + .0722 * Channel(b);
        var a = color.A / 255.0;
        var over = Luminance(color.R * a + surface.R * (1 - a), color.G * a + surface.G * (1 - a), color.B * a + surface.B * (1 - a));
        var under = Luminance(surface.R, surface.G, surface.B);
        return (Math.Max(over, under) + .05) / (Math.Min(over, under) + .05);
    }

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "ED-NEW-kindlehover-1")]
    public void A_resting_Cold_drop_has_at_least_3_to_1_in_both_appearances_and_hover_brightens_it()
    {
        // the system theme's surfaces the composer glass is tinted with (AppTheme.Spec: white light, #1E1E20 dark)
        var accent = Colors.DodgerBlue;
        foreach (var (palette, surface) in new[] { (KindlePalette.Light(accent), Colors.White), (KindlePalette.Dark(accent), Color.FromRgb(0x1E, 0x1E, 0x20)) })
        {
            var bare = Contrast(KindleFlame.ColdBase(palette, 0), surface);
            var rest = Contrast(KindleFlame.ColdBase(palette, KindleCapsule.ColdRestLift), surface);
            var hover = Contrast(KindleFlame.ColdBase(palette, 1), surface);
            Assert.True(rest >= 3, $"the resting Cold drop has {rest:0.00}:1 on {surface}");
            Assert.True(rest > bare, "the resting drop is above the bare cold grey");
            Assert.True(hover > rest, "hover still brightens the drop");
            var trackBare = Contrast(KindleGlyph.TrackColor(palette, 0), surface);
            var trackRest = Contrast(KindleGlyph.TrackColor(palette, KindleCapsule.ColdRestLift), surface);
            var trackHover = Contrast(KindleGlyph.TrackColor(palette, 1), surface);
            Assert.True(trackRest > trackBare && trackHover > trackRest, "the ring track rests a little higher and hover lifts it further");
        }
    }

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "ED-NEW-kindlehover-1")]
    public void The_words_stay_primary_in_every_state_warm_does_not_lift_and_Off_lifts_without_kindling() => headless.OnUiThread(() =>
    {
        using var rig = new KindleCapsuleRig(Warm);
        var capsule = rig.Capsule;
        var primary = rig.Resolve(ChromeTokens.PrimaryText);
        rig.Runtime.OnMain(() =>
        {
            Assert.Equal("42m", capsule.Text);
            Assert.Equal(primary, capsule.WordsColor);
            Assert.Equal(0, capsule.Glyph.Lift);
        });
        // a warm capsule under the pointer: the wash, no lift and no kindling (its ember is already lit)
        rig.Move(rig.Ring);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.Equal(rig.Resolve(ChromeTokens.Overlay(KindleCapsule.HoverWash)), capsule.WashColor);
        Assert.Equal(0, capsule.Glyph.Lift);
        Assert.Equal(0, capsule.Glyph.Kindle);
        Assert.Equal(primary, capsule.WordsColor);
        rig.Move(rig.Away);
        rig.Runtime.OnMain(() => rig.Controller.Set(Cold(rig.Runtime)));
        for (var i = 0; i < 15; i++) rig.Step(60);
        Assert.Equal("Cold", capsule.Text);
        Assert.Equal(primary, capsule.WordsColor);
        Assert.Equal(KindleCapsule.ColdRestLift, capsule.Glyph.Lift);
        rig.Runtime.OnMain(() => rig.Controller.Set(Off(rig.Runtime)));
        for (var i = 0; i < 15; i++) rig.Step(60);
        Assert.Equal("Off", capsule.Text);
        Assert.Equal(primary, capsule.WordsColor);
        Assert.Equal(0, capsule.Glyph.Lift); // Off rests at the bare grey
        // Off lifts under the pointer but does not kindle: Ignis is off in this chat
        rig.Move(rig.Ring);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.Equal(primary, capsule.WordsColor);
        Assert.Equal(1, capsule.Glyph.Lift);
        Assert.Equal(0, capsule.Glyph.Kindle);
    });

    public static TheoryData<string> States => new() { "Cold", "Off", "Warm" };

    [Theory]
    [MemberData(nameof(States))]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "ED-NEW-kindlehover-1")]
    public void A_real_press_on_the_ring_or_the_padding_opens_and_closes_the_popover(string which) => headless.OnUiThread(() =>
    {
        using var rig = new KindleCapsuleRig(which switch { "Cold" => Cold, "Off" => Off, _ => Warm });
        var capsule = rig.Capsule;
        rig.Runtime.OnMain(() => Assert.Equal(which == "Warm" ? "42m" : which, capsule.Text));
        // the ring: before, only the words' glyphs took the pointer and a press here fell through to the composer
        rig.Move(rig.Ring);
        rig.Down(rig.Ring);
        rig.Up(rig.Ring);
        rig.Runtime.RunUntilIdle();
        Assert.True(rig.Presented, which + ": a press on the ring opens the popover");
        Assert.Equal((capsule.Popover, true), rig.Presenter.Calls[^1]);
        // open: the capsule stays lifted while the popover is up, even with the pointer away
        rig.Move(rig.Away);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.True(capsule.IsLifted);
        Assert.Equal(rig.Resolve(ChromeTokens.Overlay(KindleCapsule.HoverWash)), capsule.WashColor);
        // the empty padding after the words closes it again
        rig.Move(rig.Padding);
        rig.Down(rig.Padding);
        rig.Up(rig.Padding);
        rig.Runtime.RunUntilIdle();
        Assert.False(rig.Presented, which + ": a press on the padding closes it");
        Assert.Equal((capsule.Popover, false), rig.Presenter.Calls[^1]);
        WindowTransients.Hide(capsule.Popover);
    });
}
