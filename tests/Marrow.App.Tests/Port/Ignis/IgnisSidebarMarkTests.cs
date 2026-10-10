using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port.Ignis.Runtime;
using Marrow.App.Port.Ignis.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Ignis;

// ED-NEW-ignis-guards: the sidebar's keep-warm mark, the owner's variant B (2026-10-10): an ember line at rest, an invisible
// hover zone at the trailing end that fades the flame in and slides out "Turn off" (lit) or "Turn on" (cooled), the douse and
// the relight, no tooltip. A real controller over the runtime fakes, headless, the test runtime's clock; nothing reaches a provider.
[Collection("Headless")]
[Trait("Decision", "OD-0134")]
public sealed class IgnisSidebarMarkTests(HeadlessSession headless)
{
    private sealed class Rig : IDisposable
    {
        public readonly TestRuntime Runtime = new();
        public readonly FakeIgnisPort Port = new();
        public readonly FakeSavingsLedger Ledger = new();
        public readonly IgnisController Controller;
        public readonly IgnisSidebarMark Mark;
        public readonly AvaloniaWindow Window;
        public int Selected;
        private readonly List<IDisposable> scopes = [];

        public Rig(ManualFrameSource? frames = null, bool dark = false, double rowHeight = 28)
        {
            if (frames is not null) scopes.Add(FrameClock.Source.Override(frames));
            scopes.Add(Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform()));
            var settings = new IgnisSettings(Runtime.Defaults);
            Controller = Runtime.OnMain(() => new IgnisController(settings, new FakeIgnisPoints(), Ledger, new FakeIgnisEngine(), new FakeIgnisEnvironment()));
            Mark = Runtime.OnMain(() => new IgnisSidebarMark(new IgnisRowWarmthSource(Controller, Port.ThreadID), () => Selected++));
            var row = new Panel { Width = 260, Height = rowHeight, Children = { Mark } };
            Window = Runtime.OnMain(() =>
            {
                var window = new AvaloniaWindow
                {
                    Width = 300, Height = rowHeight + 40, Background = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Color.FromRgb(0xF5, 0xF5, 0xF7)),
                    RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                    Content = new Border { Padding = new Thickness(20), Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top },
                };
                window.Show();
                Mark.Place(8 + 52); // a plain row's trailing clearance
                Settle(window);
                return window;
            });
        }

        public void Settle() => Runtime.OnMain(() => Settle(Window));

        private static void Settle(AvaloniaWindow window)
        {
            for (var i = 0; i < 4; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        }

        /// <summary>A user turn that writes and reads the cache (the chat is warm and kept warm from here).</summary>
        public void Warm() => Runtime.OnMain(() =>
        {
            Controller.Attach(Port);
            Controller.Observe(Port.ThreadID, IgnisSamples.Usage(read: 1_000, write1h: 200));
            Controller.TurnFinished(Port.ThreadID);
        });

        public void On(Action action) => Runtime.OnMain(action);

        public T On<T>(Func<T> read) => Runtime.OnMain(read);

        public void Dispose()
        {
            Runtime.OnMain(Window.Close);
            for (var i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose();
            Runtime.Dispose();
        }
    }

    [Fact]
    public void The_look_is_pinned_to_the_owners_numbers()
    {
        Assert.Equal((2.5, 2.0, 7.0, 2.0), (IgnisSidebarMark.LineWidth, IgnisSidebarMark.LineRadius, IgnisSidebarMark.LineInset, IgnisSidebarMark.LineLead));
        Assert.Equal((.8, 2.6, .5), (IgnisSidebarMark.BreathLow, IgnisSidebarMark.BreathSeconds, IgnisSidebarMark.CooledLineOpacity));
        Assert.Equal(32, IgnisSidebarMark.ZoneWidth); // lead: about 32, the row's full height
        Assert.InRange(IgnisSidebarMark.RevealSeconds, .18, .22);
        Assert.InRange(IgnisSidebarMark.DouseSeconds, .35, .45);
        Assert.Equal(1.14, IgnisSidebarMark.HoverBrightness); // +14 %
        Assert.Equal(TimeSpan.FromMilliseconds(120), IgnisSidebarMark.LeaveGrace);
        Assert.Equal((20.0, 6.0), (IgnisSidebarMark.PillHeight, IgnisSidebarMark.PillTravel));
        // the breath: .8 at the start of a cycle, 1 at its middle, .8 again at its end; cooled, a still .5
        Assert.Equal(.8, OpacityAt(1, 0), 3);
        Assert.Equal(1, OpacityAt(1, 1.3), 3);
        Assert.Equal(.8, OpacityAt(1, 2.6), 3);
        Assert.Equal(.5, OpacityAt(0, 1.3), 3);
        // the douse's squash: none at the ends, deepest at 45 %
        Assert.Equal((1.0, 1.0, 0.0), IgnisSidebarMark.EmberGlyph.Squash(0));
        Assert.Equal((1.0, 1.0, 0.0), IgnisSidebarMark.EmberGlyph.Squash(1));
        var deepest = IgnisSidebarMark.EmberGlyph.Squash(.45);
        Assert.Equal(.62, deepest.ScaleY, 3);
        Assert.Equal(.92, deepest.ScaleX, 3);
    }

    private static double OpacityAt(double warmth, double seconds) => IgnisSidebarMark.LineOpacityAt(warmth, seconds);

    [Fact]
    public void A_chat_never_kept_warm_shows_nothing_and_a_lit_one_shows_only_its_line_at_rest() => headless.OnUiThread(() =>
    {
        using var rig = new Rig();
        Assert.False(rig.On(() => rig.Mark.Shows));
        Assert.False(rig.On(() => rig.Mark.IsVisible));
        Assert.Null(rig.On(() => rig.Mark.ActionTitle));
        rig.Warm();
        rig.Settle();
        rig.On(() =>
        {
            Assert.True(rig.Mark.IsVisible);
            Assert.Equal(1, rig.Mark.Warmth);
            Assert.Equal(0, rig.Mark.Reveal); // no flame at rest
            Assert.Equal(0, rig.Mark.Glyph.Opacity);
            Assert.Equal(0, rig.Mark.Pill.Opacity);
            Assert.False(rig.Mark.Pill.IsHitTestVisible);
            Assert.Equal(IgnisWords.TurnOff, rig.Mark.ActionTitle);
            Assert.Equal(IgnisWords.EmberLitAccessibleName, Avalonia.Automation.AutomationProperties.GetName(rig.Mark.Line));
            Assert.Null(ToolTip.GetTip(rig.Mark.Zone)); // the owner: no tooltip at all
            Assert.Null(ToolTip.GetTip(rig.Mark.Pill));
            Assert.Equal(IgnisSidebarMark.ZoneWidth, rig.Mark.Zone.Bounds.Width);
            Assert.Equal(28, rig.Mark.Zone.Bounds.Height); // the row's full height
            var zoneRight = rig.Mark.Zone.TranslatePoint(new Point(rig.Mark.Zone.Bounds.Width, 0), rig.Mark)!.Value.X;
            Assert.Equal(rig.Mark.Bounds.Width - 60, zoneRight, 3); // just before the row's trailing content
            var glyph = rig.Mark.Glyph.TranslatePoint(new Point(IgnisSidebarMark.GlyphSize / 2, 0), rig.Mark.Zone)!.Value.X;
            Assert.Equal(IgnisSidebarMark.ZoneWidth / 2, glyph, 3); // centred on the flame
            var line = rig.Mark.Line.TranslatePoint(default, rig.Mark)!.Value;
            Assert.Equal((2.0, 7.0), (line.X, line.Y));
            Assert.Equal((2.5, 14.0), (rig.Mark.Line.Bounds.Width, rig.Mark.Line.Bounds.Height));
        });
    });

    [Fact]
    public void Hovering_the_zone_shows_the_flame_brighter_and_the_pill_and_leaving_waits_120_ms() => headless.OnUiThread(() =>
    {
        using var rig = new Rig();
        rig.Warm();
        rig.Settle();
        rig.On(() => rig.Mark.SetPointer(zoneOver: true, pillOver: false));
        rig.On(() =>
        {
            Assert.True(rig.Mark.IsHot);
            Assert.Equal(1, rig.Mark.Reveal);
            Assert.Equal(1, rig.Mark.Glyph.Opacity);
            Assert.Equal(IgnisSidebarMark.HoverBrightness, rig.Mark.FlameBrightness);
            Assert.True(rig.Mark.Pill.IsHitTestVisible);
            Assert.Equal(IgnisWords.TurnOff, rig.Mark.PillWords);
        });
        // zone and pill are one region: crossing from one to the other within the grace keeps it
        rig.On(() => rig.Mark.SetPointer(false, false));
        rig.Runtime.Advance(TimeSpan.FromMilliseconds(60));
        rig.On(() => rig.Mark.SetPointer(false, true));
        rig.Runtime.Advance(TimeSpan.FromMilliseconds(200));
        Assert.True(rig.On(() => rig.Mark.IsHot));
        rig.On(() => rig.Mark.SetPointer(false, false));
        rig.Runtime.Advance(TimeSpan.FromMilliseconds(119));
        Assert.True(rig.On(() => rig.Mark.IsHot));
        rig.Runtime.Advance(TimeSpan.FromMilliseconds(2));
        rig.On(() =>
        {
            Assert.False(rig.Mark.IsHot);
            Assert.Equal(0, rig.Mark.Reveal);
            Assert.Equal(1, rig.Mark.FlameBrightness);
            Assert.False(rig.Mark.Pill.IsHitTestVisible);
        });
    });

    [Fact]
    public void Turn_off_cools_the_chat_and_the_pill_then_reads_Turn_on_which_lights_it_again() => headless.OnUiThread(() =>
    {
        using var rig = new Rig();
        rig.Warm();
        rig.Settle();
        rig.On(() => rig.Mark.SetPointer(true, false));
        rig.On(() => rig.Mark.Pill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
        rig.Settle();
        rig.On(() =>
        {
            Assert.Equal(IgnisEmber.Cooled, rig.Controller.Ember(rig.Port.ThreadID).Ember);
            Assert.Single(rig.Ledger.Recorded, e => e.Kind == IgnisEventKind.LetCool);
            Assert.True(rig.Mark.IsVisible); // dimmed, not gone
            Assert.Equal(0, rig.Mark.Warmth);
            Assert.Equal(1, rig.Mark.DouseProgress);
            Assert.Equal(IgnisWords.TurnOn, rig.Mark.PillWords);
            Assert.Equal(IgnisWords.TurnOn, rig.Mark.ActionTitle);
            Assert.Equal(1, rig.Mark.Reveal); // still hovered: the dim flame and "Turn on"
            Assert.Equal(1, rig.Mark.FlameBrightness); // a cooled flame does not brighten
            Assert.Equal(IgnisWords.EmberCooledAccessibleName, Avalonia.Automation.AutomationProperties.GetName(rig.Mark.Line));
        });
        rig.On(() => rig.Mark.Pill.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
        rig.Settle();
        rig.On(() =>
        {
            Assert.Equal(IgnisEmber.Lit, rig.Controller.Ember(rig.Port.ThreadID).Ember);
            Assert.Single(rig.Ledger.Recorded, e => e.Kind == IgnisEventKind.KeepWarmOn);
            Assert.Equal(1, rig.Mark.Warmth);
            Assert.Equal(IgnisWords.TurnOff, rig.Mark.PillWords);
            Assert.Equal(IgnisSidebarMark.HoverBrightness, rig.Mark.FlameBrightness);
        });
        // the row menu's item does the same (keyboard access)
        rig.On(rig.Mark.Act);
        Assert.Equal(IgnisEmber.Cooled, rig.On(() => rig.Controller.Ember(rig.Port.ThreadID).Ember));
        // the next message lights it again, as before
        rig.On(() =>
        {
            rig.Controller.Observe(rig.Port.ThreadID, IgnisSamples.Usage(read: 1_000, write1h: 50));
            rig.Controller.TurnFinished(rig.Port.ThreadID);
        });
        rig.Settle();
        Assert.Equal(IgnisWords.TurnOff, rig.On(() => rig.Mark.ActionTitle));
    });

    [Fact]
    public void Clicking_the_flames_zone_selects_the_row_and_never_cools_it() => headless.OnUiThread(() =>
    {
        using var rig = new Rig();
        rig.Warm();
        rig.Settle();
        rig.On(() =>
        {
            var centre = rig.Mark.Zone.TranslatePoint(new Point(IgnisSidebarMark.ZoneWidth / 2, 14), rig.Window)!.Value;
            rig.Window.MouseMove(centre);
            rig.Window.MouseDown(centre, MouseButton.Left);
            rig.Window.MouseUp(centre, MouseButton.Left);
        });
        rig.Settle();
        Assert.Equal(1, rig.Selected);
        Assert.Equal(IgnisEmber.Lit, rig.On(() => rig.Controller.Ember(rig.Port.ThreadID).Ember));
        Assert.True(rig.On(() => rig.Mark.IsHot)); // the real pointer over the zone shows the pill
    });

    [Fact]
    public void The_timings_run_as_pinned_and_reduce_motion_makes_them_instant() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var rig = new Rig(frames);
        rig.Warm();
        rig.Settle();
        void Step(int ms) { rig.Runtime.Advance(TimeSpan.FromMilliseconds(ms)); rig.On(() => frames.Advance(TimeSpan.FromMilliseconds(ms))); }
        rig.On(() => rig.Mark.SetPointer(true, false));
        Step(16);
        Step(84); // 0.1 s: on its way
        var halfway = rig.On(() => (rig.Mark.Reveal, rig.Mark.FlameBrightness));
        Assert.InRange(halfway.Reveal, .3, .99);
        Assert.InRange(halfway.FlameBrightness, 1.01, 1.139);
        Step(101); // past 0.2 s
        Assert.Equal((1.0, IgnisSidebarMark.HoverBrightness), rig.On(() => (rig.Mark.Reveal, rig.Mark.FlameBrightness)));

        rig.On(rig.Mark.Act); // Turn off: the douse
        Step(16);
        Step(194); // 0.21 s: half way
        var mid = rig.On(() => (rig.Mark.DouseProgress, rig.Mark.Warmth));
        Assert.InRange(mid.DouseProgress, .2, .95);
        Assert.InRange(mid.Warmth, .05, .8);
        Step(220); // past 0.42 s
        Assert.Equal((1.0, 0.0), rig.On(() => (rig.Mark.DouseProgress, rig.Mark.Warmth)));

        // Reduce Motion: the relight and the hover land at once
        rig.On(() => EnvironmentValues.SetAccessibilityReduceMotion(rig.Mark, true));
        rig.On(rig.Mark.Act);
        Assert.Equal(1.0, rig.On(() => rig.Mark.Warmth));
        rig.On(() => rig.Mark.SetPointer(false, false));
        rig.Runtime.Advance(TimeSpan.FromMilliseconds(121));
        Assert.Equal(0.0, rig.On(() => rig.Mark.Reveal));
    });

    [Fact]
    public void The_hover_zone_is_invisible_only_the_flame_and_the_pill_draw() => headless.OnUiThread(() =>
    {
        foreach (var dark in new[] { false, true })
        {
            using var rig = new Rig(dark: dark, rowHeight: 40);
            rig.Warm();
            rig.Settle();
            var (zone, glyph, pill) = rig.On(() => (Rect(rig.Mark.Zone, rig.Window), Rect(rig.Mark.Glyph, rig.Window), Rect(rig.Mark.Pill, rig.Window)));
            var rest = rig.On(() => Pixels(rig.Window));
            rig.On(() => rig.Mark.SetPointer(true, false));
            rig.Settle();
            var hot = rig.On(() => Pixels(rig.Window));
            Assert.Equal(rest.Width, hot.Width);
            var flame = glyph.Inflate(8);
            var pillDrawn = pill.Inflate(12); // the pill and its soft shadow
            int compared = 0, changedInZone = 0, changedInPill = 0;
            for (var y = (int)zone.Top; y < (int)zone.Bottom; y++)
                for (var x = (int)zone.Left; x < (int)zone.Right; x++)
                {
                    var point = new Point(x + .5, y + .5);
                    if (flame.Contains(point) || pillDrawn.Contains(point)) continue;
                    compared++;
                    if (rest.At(x, y) != hot.At(x, y)) changedInZone++;
                }
            for (var y = (int)pill.Top; y < (int)pill.Bottom; y++)
                for (var x = (int)pill.Left; x < (int)pill.Right; x++)
                    if (rest.At(x, y) != hot.At(x, y)) changedInPill++;
            Assert.True(compared >= 100, compared + " pixels compared");
            Assert.Equal(0, changedInZone); // no circle, fill, highlight or border, ever
            Assert.True(changedInPill > 50, "the pill draws");
        }
    });

    private static Rect Rect(Visual visual, Visual root) => new(visual.TranslatePoint(default, root)!.Value, visual.Bounds.Size);

    private sealed record Frame(int Width, int Height, int Stride, byte[] Bytes)
    {
        public uint At(int x, int y) => BitConverter.ToUInt32(Bytes, y * Stride + x * 4);
    }

    private static Frame Pixels(TopLevel window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = HeadlessWindowExtensions.CaptureRenderedFrame(window) ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return new Frame(buffer.Size.Width, buffer.Size.Height, buffer.RowBytes, bytes);
    }
}
