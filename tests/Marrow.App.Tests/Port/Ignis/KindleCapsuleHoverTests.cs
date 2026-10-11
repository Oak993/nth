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
// capsule's ring and drop lift and its core kindles faintly, Cold and Off words rest at the composer's secondary text colour, and
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

    private sealed class Rig : IDisposable
    {
        private readonly IDisposable frameScope, motionScope, presenterScope;
        public TestRuntime Runtime { get; } = new();
        public ManualFrameSource Frames { get; } = new();
        public RecordingPresenter Presenter { get; } = new();
        public KindleFakeController Controller { get; }
        public KindleCapsule Capsule { get; }
        public AvaloniaWindow Window { get; }

        public Rig(Func<TestRuntime, IgnisChatStatus> status)
        {
            frameScope = FrameClock.Source.Override(Frames);
            motionScope = ChromeTokens.ReduceMotionPlatform.Override(new RenderingGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
            presenterScope = Presentation.Presenter.Override(Presenter);
            Controller = new KindleFakeController(new IgnisSettings(Runtime.Defaults)) { Current = status(Runtime) };
            Capsule = Runtime.OnMain(() => new KindleCapsule(Controller, Chat));
            Window = Runtime.OnMain(() =>
            {
                var window = new AvaloniaWindow
                {
                    Content = new Border { Child = Capsule, Margin = new Thickness(40), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom },
                    Width = 600, Height = 400,
                };
                window.Show();
                return window;
            });
            Render();
        }

        public void Render()
        {
            for (var i = 0; i < 4; i++)
            {
                Window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
        }

        /// <summary>Moves the clock and the frame clock together and delivers one frame.</summary>
        public void Step(double milliseconds)
        {
            Runtime.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
            Runtime.RunUntilIdle();
        }

        /// <summary>The ring's centre (the glyph itself takes no hits: what is under it must).</summary>
        public Point Ring => Capsule.Glyph.TranslatePoint(new Point(KindleGlyph.RingSize / 2, Capsule.Glyph.Bounds.Height / 2), Window)!.Value;

        /// <summary>The empty padding after the words, inside the capsule's right end.</summary>
        public Point Padding => Capsule.Button.TranslatePoint(new Point(Capsule.Button.Bounds.Width - 5, Capsule.Button.Bounds.Height / 2), Window)!.Value;

        /// <summary>Well away from the capsule.</summary>
        public Point Away => new(560, 20);

        // pointer input reaches the view's model, so it is delivered on the main loop as the app's input is
        public void Move(Point point) => Runtime.OnMain(() => Window.MouseMove(point));
        public void Down(Point point) => Runtime.OnMain(() => Window.MouseDown(point, MouseButton.Left));
        public void Up(Point point) => Runtime.OnMain(() => Window.MouseUp(point, MouseButton.Left));
        public bool Presented => Runtime.OnMain(() => Capsule.IsPresented);

        public Color Resolve(SwiftColor color) => color.Resolve(Capsule.ActualThemeVariant);

        public void Dispose()
        {
            Runtime.OnMain(Window.Close);
            presenterScope.Dispose();
            motionScope.Dispose();
            frameScope.Dispose();
            Runtime.Dispose();
        }
    }

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "ED-NEW-kindlehover-1")]
    public void Hover_washes_a_cold_capsule_lifts_its_ring_kindles_its_core_and_leaving_reverts() => headless.OnUiThread(() =>
    {
        using var rig = new Rig(Cold);
        var capsule = rig.Capsule;
        rig.Runtime.OnMain(() =>
        {
            Assert.Equal("Cold", capsule.Text);
            Assert.Equal(rig.Resolve(ChromeTokens.SecondaryText), capsule.WordsColor);
            Assert.Equal(0, capsule.WashColor.A);
            Assert.Equal(0, capsule.Glyph.Lift);
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
        Assert.InRange(capsule.Glyph.Lift, .01, .99);
        Assert.InRange(capsule.Glyph.Kindle, .001, .99);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.Equal(hoverWash, capsule.WashColor);
        Assert.Equal(rig.Resolve(ChromeTokens.PrimaryText), capsule.WordsColor);
        Assert.Equal(1, capsule.Glyph.Lift);
        Assert.Equal(1, capsule.Glyph.Kindle);
        Assert.False(capsule.Glyph.IsAmbientRunning, "the kindling is an eased value, not a running clock");
        // the lifted ring, separator and drop are brighter than the cold grey they rest at
        var palette = KindlePalette.For(capsule.Glyph);
        Assert.True(KindleGlyph.TrackColor(palette, 1).A > KindleGlyph.TrackColor(palette, 0).A, "the track lifts");
        Assert.NotEqual(KindleFlame.ColdBase(palette, 0), KindleFlame.ColdBase(palette, 1));

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
        Assert.InRange(capsule.Glyph.Lift, .01, .99);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.Equal(0, capsule.WashColor.A);
        Assert.Equal(rig.Resolve(ChromeTokens.SecondaryText), capsule.WordsColor);
        Assert.Equal(0, capsule.Glyph.Lift);
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
        Assert.Equal(0, capsule.Glyph.Lift);
        Assert.Equal(rig.Resolve(ChromeTokens.SecondaryText), capsule.WordsColor);
    });

    [Fact]
    [Trait("Feature", "IG-4")]
    [Trait("Decision", "ED-NEW-kindlehover-1")]
    public void Cold_and_Off_words_rest_at_the_secondary_text_colour_and_warm_words_are_primary_and_unkindled() => headless.OnUiThread(() =>
    {
        using var rig = new Rig(Warm);
        var capsule = rig.Capsule;
        rig.Runtime.OnMain(() =>
        {
            Assert.Equal("42m", capsule.Text);
            Assert.Equal(rig.Resolve(ChromeTokens.PrimaryText), capsule.WordsColor);
        });
        // a warm capsule under the pointer: the wash, no lift and no kindling (its ember is already lit)
        rig.Move(rig.Ring);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.Equal(rig.Resolve(ChromeTokens.Overlay(KindleCapsule.HoverWash)), capsule.WashColor);
        Assert.Equal(0, capsule.Glyph.Lift);
        Assert.Equal(0, capsule.Glyph.Kindle);
        rig.Move(rig.Away);
        rig.Runtime.OnMain(() => rig.Controller.Set(Cold(rig.Runtime)));
        for (var i = 0; i < 15; i++) rig.Step(60);
        Assert.Equal("Cold", capsule.Text);
        Assert.Equal(rig.Resolve(ChromeTokens.SecondaryText), capsule.WordsColor);
        rig.Runtime.OnMain(() => rig.Controller.Set(Off(rig.Runtime)));
        for (var i = 0; i < 15; i++) rig.Step(60);
        Assert.Equal("Off", capsule.Text);
        Assert.Equal(rig.Resolve(ChromeTokens.SecondaryText), capsule.WordsColor);
        // Off lifts under the pointer but does not kindle: Ignis is off in this chat
        rig.Move(rig.Ring);
        for (var i = 0; i < 10; i++) rig.Step(60);
        Assert.Equal(rig.Resolve(ChromeTokens.PrimaryText), capsule.WordsColor);
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
        using var rig = new Rig(which switch { "Cold" => Cold, "Off" => Off, _ => Warm });
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
