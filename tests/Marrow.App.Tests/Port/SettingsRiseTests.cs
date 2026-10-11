using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// PORT-ONLY src/Marrow.App/Port/SettingsRise.cs and its use in WindowManager.ShowSettings (OD-0074, ED-249). Every
// expected value is transcribed from E:/Droppy/work/design/settings-rise/SPEC.md (the locked canvas's variant 2 "Rise"),
// never from the C# under test:
//   section 1: nothing of the window shows before glass-ready G (the window stays cloaked, ED-236's hand-over); the open
//   is done at G + 260 ms
//   section 2: the glass fades 0 to 1 over 80 ms on cubic-bezier(.2,.8,.2,1), no scale; at 60 Hz 0.698, 0.912, 0.976,
//   0.997, 1.0
//   section 3: 18 items each rise from 10 px below at opacity 0 over 140 ms on the same curve, opacity = 1 - y / 10,
//   starting at G + 12 ms x i; at 60 Hz from an item's start 0.464, 0.750, 0.876, 0.938, 0.970, 0.988, 0.997, 1.000;
//   sidebar: Search 0, General 1 ... About 10; page: the title 1, card 1's five rows 2 to 6 (each with the divider below
//   it), card 2 whole 7; card 1's surface fades with the glass; the caption glyphs are whole from G
//   section 7: reduce motion: the window appears complete at G
// The headless window has no Win32 handle, so a recording stand-in takes the cloak (SettingsRiseRig); the frame clock and
// SwiftClock are manual, so every time is exact.
[Collection("Headless")]
public sealed class SettingsRiseTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private const double Frame = 1.0 / 60;

    private static readonly string[] PageTitles = Marrow.App.Port.Settings.SettingsSidebar.Pages("").Select(page => page.Title).ToArray(); // the ready pages, in sidebar order
    // OD-0088 (ED-250): the Appearance page starts with the Look card; its rows are Theme and Accent.
    private static readonly string[] CardRowTitles = ["Theme", "Accent"];

    // SPEC.md section 2 and section 3's 60 Hz samples.
    private static readonly double[] GlassAt60Hz = [0.698, 0.912, 0.976, 0.997, 1.0];
    private static readonly double[] ItemAt60Hz = [0.464, 0.750, 0.876, 0.938, 0.970, 0.988, 0.997, 1.000];

    // The parts the spec names, found in the view by their own types (not through the code under test).
    private static (Control[] Glass, Control Card1, List<(int Index, string Name, Control[] Parts)> Items) Parts(SettingsView view)
    {
        var backdrop = Assert.Single(Nodes<WindowBackdrop>(view));
        var veil = Assert.Single(Nodes<PaneTopVeil>(view));
        var sheet = Assert.IsAssignableFrom<Panel>(((Control)veil.Parent!).Parent);
        var wash = Assert.IsType<Rectangle>(sheet.Children[0]);
        var tint = Assert.IsType<Rectangle>(sheet.Children[1]);
        var items = new List<(int, string, Control[])> { (0, "Search", [Assert.Single(Nodes<SidebarSearchField>(view))]) };
        var rows = Nodes<SidebarRow>(view);
        Assert.Equal(PageTitles.Length, rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            Assert.Contains(Nodes<TextBlock>(rows[i]), text => text.Text == PageTitles[i]);
            items.Add((Math.Min(i + 1, Marrow.App.Port.SettingsRiseTiming.LastIndex), PageTitles[i], [rows[i]])); // CZ-4: an 11th row (Customization) starts with index 10, as every block past it does (ED-249)
        }
        var section = Nodes<ChromeSection>(view).First();
        var heading = Assert.IsAssignableFrom<StackPanel>(section.Child).Children[0];
        Assert.Contains(Nodes<TextBlock>(heading), text => text.Text == "Appearance");
        items.Add((1, "title Appearance", [heading]));
        // OD-0088 (ED-250): the first card is the Look card (Port/LookCard.cs), whose blocks are, top to bottom, the mini
        // window, the Theme row with the divider below it, the Accent row and the Signature fold (shut, 0 tall); the
        // source's first card, now without its Theme row, follows whole, then the picture card.
        var look = Assert.Single(Nodes<Marrow.App.Port.LookCard>(view));
        var card1 = Nodes<ChromeCard>(view).First();
        Assert.Same(look.Card, card1);
        var cardRows = Nodes<ChromeRow>(card1);
        var dividers = Nodes<ChromeRowDivider>(card1);
        Assert.Equal(2, cardRows.Length);
        Assert.Single(dividers);
        for (var r = 0; r < 2; r++) Assert.Contains(Nodes<TextBlock>(cardRows[r]), text => text.Text == CardRowTitles[r]);
        items.Add((2, "look mini window", [look.Mini]));
        items.Add((3, "Theme", [cardRows[0], dividers[0]]));
        items.Add((4, "Accent", [cardRows[1]]));
        items.Add((5, "look fold", [look.Fold]));
        var rowsCard = Nodes<ChromeCard>(view)[1];
        Assert.Equal(["Transparency", "Sidebar", "Activity list", "Open the sidebar at launch"], Nodes<ChromeRow>(rowsCard).Select(row => Nodes<TextBlock>(row).First().Text));
        items.Add((6, "rows card", [rowsCard]));
        var card2 = Assert.Single(Nodes<ChromeCard>(Assert.Single(Nodes<WallpaperCard>(view))));
        items.Add((7, "picture card", [card2]));
        return ([backdrop, wash, tint], card1, items);
    }

    private static double Y(Control control) => control.RenderTransform switch
    {
        null => 0,
        TranslateTransform translate => translate.X == 0 ? translate.Y : throw new Xunit.Sdk.XunitException($"x moved: {translate.X}"),
        var other => throw new Xunit.Sdk.XunitException($"unexpected transform {other.GetType().Name}"),
    };

    // Against the exact curve (SpecRise) to 1e-4; against the spec's three-place tables to 0.001.
    private static void Expect(Control control, double opacity, double y, string what, double tolerance = 1e-4)
    {
        Assert.True(Math.Abs(control.Opacity - opacity) <= tolerance, $"{what}: opacity {control.Opacity:F5}, expected {opacity:F5}");
        Assert.True(Math.Abs(Y(control) - y) <= tolerance * SpecRise.Offset, $"{what}: y {Y(control):F5}, expected {y:F5}");
    }

    private static void ExpectHidden((Control[] Glass, Control Card1, List<(int Index, string Name, Control[] Parts)> Items) parts, string when)
    {
        foreach (var layer in parts.Glass) Expect(layer, 0, 0, $"{when}: glass {layer.GetType().Name}");
        Expect(parts.Card1, 0, 0, $"{when}: card 1 surface");
        foreach (var (_, name, controls) in parts.Items)
            foreach (var control in controls) Expect(control, 0, SpecRise.Offset, $"{when}: {name}");
    }

    private static void ExpectSettled((Control[] Glass, Control Card1, List<(int Index, string Name, Control[] Parts)> Items) parts, string when)
    {
        foreach (var control in parts.Glass.Append(parts.Card1).Concat(parts.Items.SelectMany(item => item.Parts)))
        {
            Assert.Equal(1, control.Opacity);
            Assert.Null(control.RenderTransform);
            Assert.True(control.IsHitTestVisible, $"{when}: {control.GetType().Name} takes no input");
        }
    }

    // ShowSettings on Appearance (the canvas's page) through the stand-in cloak; returns once the window is shown cloaked.
    private static (HostWindow Window, SettingsView View) Open(WindowManager manager, SettingsRiseRig rig)
    {
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        return (Settings(manager), View(manager));
    }

    // The two frames of the hand-over: the first draws the hidden state, the second (G) reveals it.
    private static void ToGlassReady(SettingsRiseRig rig)
    {
        rig.Frame(Frame);
        rig.Frame(Frame);
        Assert.NotNull(rig.Reveal);
    }

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Settings_is_shown_cloaked_and_uncloaked_only_after_a_frame_has_drawn_the_hidden_start() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, view) = Open(manager, rig);
        // Cloaked before it is shown, shown without activation, and still cloaked.
        Assert.Equal(["cloak", "shown"], rig.Events);
        Assert.True(rig.Cloaked);
        Assert.True(window.IsVisible);
        Assert.False(window.IsActive);
        Assert.True(window.ShowActivated); // restored after the one inactive show

        // Frame 1 sets the hidden start; it has not been drawn yet, so the window stays cloaked.
        rig.Frame(Frame);
        Assert.Equal(["cloak", "shown"], rig.Events);
        var parts = Parts(view);
        ExpectHidden(parts, "frame 1");

        // Frame 2: what was drawn is the hidden start, so the window is uncloaked (G) and then activated.
        var seenAtUncloak = false;
        rig.AtUncloak = () => { ExpectHidden(parts, "at the uncloak"); seenAtUncloak = true; };
        rig.Frame(Frame);
        Assert.True(seenAtUncloak);
        Assert.Equal(["cloak", "shown", "uncloak", "activated"], rig.Events);
        Assert.False(rig.Cloaked);
        Assert.True(window.IsActive);
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void The_spec_curve_used_by_these_tests_reproduces_the_spec_samples()
    {
        // The spec's samples are Chromium's computed values printed to three places (Chromium solves the curve to a
        // looser epsilon): within 0.001 of the exact curve.
        for (var k = 0; k < GlassAt60Hz.Length; k++) Assert.InRange(SpecRise.Glass((k + 1) * Frame), GlassAt60Hz[k] - 0.001, GlassAt60Hz[k] + 0.001);
        for (var k = 0; k < ItemAt60Hz.Length; k++) Assert.InRange(SpecRise.Item(0, (k + 1) * Frame), ItemAt60Hz[k] - 0.001, ItemAt60Hz[k] + 0.001);
        Assert.InRange(SpecRise.Item(2, 2 * Frame), 0.268, 0.270); // section 3: Theme row at G+33: 0.269 x 0.912
    }

    [Fact]
    [Trait("Decision", "ED-249")]
    public void The_glass_and_card_one_s_surface_fade_in_over_80_ms_without_moving() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (_, view) = Open(manager, rig);
        ToGlassReady(rig);
        var parts = Parts(view);
        foreach (var layer in parts.Glass.Append(parts.Card1)) Expect(layer, 0, 0, "glass at G");
        for (var k = 1; k <= GlassAt60Hz.Length; k++)
        {
            rig.At(k * Frame);
            foreach (var layer in parts.Glass.Append(parts.Card1))
            {
                Expect(layer, GlassAt60Hz[k - 1], 0, $"{layer.GetType().Name} at G+{k}/60 (table)", 0.001);
                Expect(layer, SpecRise.Glass(k * Frame), 0, $"{layer.GetType().Name} at G+{k}/60");
                Assert.Null(layer.RenderTransform); // no scale, no offset
            }
        }
        // Section 3: a card 1 row's opacity on screen is its rise times the card's fade (Theme at G+33: 0.269 x 0.912).
        Assert.Equal(0.245, 0.269 * 0.912, 3);
    });

    // OD-0075, the owner's intent of 2026-10-05 and ED-338: the window's corners are the backdrop's own glass (and the detail sheet's), extended to the window's
    // rectangle, so they fade in with it. The old corner wedge was a layer of its own that Rise had to be told about; this proves the corner pixel on rendered frames: the bare
    // backdrop at G (no hard square corner of scrim before the glass), the fraction of the glass's end colour that the spec's curve gives mid-fade (0.698 at G+1/60), the body's own
    // colour at every step, and the end.
    [Fact]
    [Trait("Decision", "ED-249")]
    [Trait("Decision", "ED-338")] // implements owner decision OD-0075
    public void The_windows_corners_fade_in_with_the_glass_not_as_a_hard_square_corner() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, _) = Open(manager, rig);
        var grey = Color.FromRgb(128, 128, 128);
        window.Background = new SolidColorBrush(grey); // the system's Acrylic stood in for by a known grey
        void Render() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        // ED-NEW-textsize-2: the 26 pt shape and its 1 pt hairline are drawn at the base scale, so the first clear edge row is the third.
        var pairs = CornerSeam.Pairs(780, 580, Chrome.WindowCornerRadius * Marrow.UI.Common.AppZoom.BaseScale, [2, 3, 4, 5], edgeInset: 2);
        Assert.NotEmpty(pairs);
        (int R, int G, int B)[] Wedge() { Render(); return CornerSeam.Rgb(window, pairs.Select(pair => pair.Wedge).ToArray()); }
        (int R, int G, int B)[] Body() => CornerSeam.Rgb(window, pairs.Select(pair => pair.Body).ToArray());

        ToGlassReady(rig); // G: the glass is at 0
        var atStart = Wedge();
        foreach (var colour in atStart) Assert.True(CornerSeam.Near(colour, (grey.R, grey.G, grey.B), 2), $"at G a corner pixel shows {colour}, not the bare backdrop {grey}: a hard square corner");
        foreach (var colour in Body()) Assert.True(CornerSeam.Near(colour, (grey.R, grey.G, grey.B), 2), $"at G the body shows {colour}, not the bare backdrop");

        var steps = new List<(int Step, (int R, int G, int B)[] Wedge, (int R, int G, int B)[] Body)>();
        foreach (var step in new[] { 1, 2 }) // the clock only runs forward
        {
            rig.At(step * Frame);
            steps.Add((step, Wedge(), Body()));
        }
        rig.At(5 * Frame); // the glass is whole (G + 83 ms)
        var end = Wedge();
        var endBody = Body();
        for (var index = 0; index < pairs.Length; index++)
        {
            Assert.True(CornerSeam.Near(end[index], endBody[index]), $"{pairs[index].Corner} at the end: wedge {end[index]}, body {endBody[index]}");
            Assert.False(CornerSeam.Near(end[index], (grey.R, grey.G, grey.B), 20), $"{pairs[index].Corner} at the end shows the bare backdrop {end[index]}");
        }
        foreach (var (step, mid, midBody) in steps)
        {
            var fade = SpecRise.Glass(step * Frame); // 0.698, 0.912
            for (var index = 0; index < pairs.Length; index++)
            {
                // the corner has come the glass's fraction of the way from the bare backdrop to its end colour, as the body beside it has
                var fraction = (mid[index].R - grey.R) / (double)(end[index].R - grey.R);
                Assert.InRange(fraction, fade - 0.06, fade + 0.06);
                Assert.True(CornerSeam.Near(mid[index], midBody[index]), $"{pairs[index].Corner} at G+{step}/60: wedge {mid[index]}, body {midBody[index]}");
            }
        }
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Every_item_rises_10_px_on_one_curve_from_its_own_start_12_ms_apart() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (_, view) = Open(manager, rig);
        ToGlassReady(rig);
        var parts = Parts(view);
        Assert.Equal(1 + PageTitles.Length + 7, parts.Items.Count); // Search, the listed pages, the Appearance page's seven blocks
        for (var k = 0; k <= 15; k++)
        {
            if (k > 0) rig.At(k * Frame);
            foreach (var (index, name, controls) in parts.Items)
            {
                var opacity = SpecRise.Item(index, k * Frame);
                foreach (var control in controls) Expect(control, opacity, SpecRise.Offset * (1 - opacity), $"{name} (i {index}) at G+{k}/60");
            }
            // Search (i 0) at its own +16.7 ms steps: the spec's table.
            if (k is >= 1 and <= 8) Expect(parts.Items[0].Parts[0], ItemAt60Hz[k - 1], SpecRise.Offset * (1 - ItemAt60Hz[k - 1]), $"Search at G+{k}/60 (table)", 0.001);
        }
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Two_columns_rise_top_to_bottom_in_the_spec_s_order() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (_, view) = Open(manager, rig);
        ToGlassReady(rig);
        var parts = Parts(view);
        // Halfway between starts i and i + 1 (the stagger is not frame-aligned, section 8 quirk 4): every item up to i has
        // started, none after it.
        for (var i = 0; i <= 10; i++)
        {
            rig.At(SpecRise.Stagger * i + SpecRise.Stagger / 2);
            foreach (var (index, name, controls) in parts.Items)
                foreach (var control in controls)
                {
                    if (index <= i) Assert.True(control.Opacity > 0, $"{name} (i {index}) has not started by G+{SpecRise.Stagger * i + 0.006:F3}");
                    else Assert.Equal(0, control.Opacity);
                }
            // The two columns move together: the same index has the same value in both.
            foreach (var group in parts.Items.GroupBy(item => item.Index))
            {
                var values = group.SelectMany(item => item.Parts).Select(control => (control.Opacity, Y(control))).Distinct().ToArray();
                Assert.Single(values);
            }
        }
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void The_open_is_done_at_260_ms_from_glass_ready_and_asks_for_no_more_frames() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (_, view) = Open(manager, rig);
        ToGlassReady(rig);
        var parts = Parts(view);
        var about = parts.Items.Single(item => item.Name == "About").Parts[0];
        rig.At(0.250);
        Assert.True(about.Opacity < 1, "About still rising at G+250 ms");
        Assert.True(Y(about) > 0);
        Assert.Equal(SpecRise.Item(10, 0.250), about.Opacity, 3);
        Assert.True(rig.PendingFor(manager.SettingsOpening) > 0);
        // Every other item has settled by G+248 ms (item 9's end); About is the last.
        foreach (var (index, _, controls) in parts.Items.Where(item => item.Index < 10))
            foreach (var control in controls) Assert.Equal(1, control.Opacity, 3);
        rig.At(SpecRise.OpenDone);
        ExpectSettled(parts, "G+260 ms");
        Assert.Equal(0, rig.PendingFor(manager.SettingsOpening));
        Assert.True(manager.SettingsOpening!.Done);
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void The_caption_buttons_are_whole_from_glass_ready_and_not_in_the_wave() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, _) = Open(manager, rig);
        var layer = WindowChrome.CaptionLayer(window);
        var buttons = Nodes<CaptionButton>(layer);
        Assert.NotEmpty(buttons);
        rig.AtUncloak = () =>
        {
            foreach (var control in buttons.Cast<Control>().Prepend(layer)) Expect(control, 1, 0, "caption at G");
        };
        ToGlassReady(rig);
        for (var k = 1; k <= 16; k++)
        {
            rig.At(k * Frame);
            foreach (var control in buttons.Cast<Control>().Prepend(layer)) Expect(control, 1, 0, $"caption at G+{k}/60");
        }
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Reduce_motion_reveals_the_complete_window_after_the_same_cloaked_show() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, view) = Open(manager, rig);
        // The app sets the system setting on a window when it opens (SystemReduceMotion.Follow at Opened), which a first
        // open does inside its show: before the first frame.
        EnvironmentValues.SetAccessibilityReduceMotion(window, true);
        rig.Frame(Frame);
        Assert.Equal(["cloak", "shown"], rig.Events); // still cloaked: the plain tint is never on screen
        var parts = Parts(view);
        var seenAtUncloak = false;
        rig.AtUncloak = () => { ExpectSettled(parts, "reduce motion at the uncloak"); seenAtUncloak = true; };
        rig.Frame(Frame);
        Assert.True(seenAtUncloak);
        Assert.Equal(["cloak", "shown", "uncloak", "activated"], rig.Events);
        Assert.Equal(0, rig.PendingFor(manager.SettingsOpening)); // no wave
        for (var k = 1; k <= 16; k++)
        {
            rig.Frame(Frame);
            ExpectSettled(parts, $"reduce motion at G+{k}/60");
        }
        Assert.True(manager.SettingsOpening!.Done);

        // A later open with reduce motion: the same.
        window.Close(); // the close control hides the Settings window (WindowManager.swift:98)
        Dispatcher.UIThread.RunJobs();
        rig.Events.Clear();
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        rig.AtUncloak = () => ExpectSettled(parts, "second reduce-motion open at the uncloak");
        ToGlassReady(rig);
        Assert.Equal(["cloak", "shown", "uncloak", "activated"], rig.Events);
        Assert.Equal(0, rig.PendingFor(manager.SettingsOpening));
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Every_later_open_plays_the_same_rise_from_a_hidden_start() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, view) = Open(manager, rig);
        ToGlassReady(rig);
        var parts = Parts(view);
        var first = new List<double[]>();
        for (var k = 1; k <= 16; k++)
        {
            rig.At(k * Frame);
            first.Add([.. parts.Glass.Append(parts.Card1).Concat(parts.Items.SelectMany(item => item.Parts)).Select(control => control.Opacity + Y(control))]);
        }
        ExpectSettled(parts, "first open done");

        for (var open = 2; open <= 3; open++)
        {
            window.Close(); // hidden, kept (WindowManager.swift:98)
            Dispatcher.UIThread.RunJobs();
            DesignGapsLockedClose.CompleteAndCheckCancellation(window, manager, rig);
            rig.Events.Clear();
            manager.ShowSettings(SettingsPage.Appearance);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(window, Settings(manager));
            Assert.Equal(["cloak", "shown"], rig.Events);
            // The settled frame of the last open is never displayed: the uncloak waits for the hidden start to be drawn.
            rig.Frame(Frame);
            Assert.Equal(["cloak", "shown"], rig.Events);
            ExpectHidden(parts, $"open {open}, frame 1");
            rig.AtUncloak = () => ExpectHidden(parts, $"open {open} at the uncloak");
            rig.Frame(Frame);
            Assert.Equal(["cloak", "shown", "uncloak", "activated"], rig.Events);
            for (var k = 1; k <= 16; k++)
            {
                rig.At(k * Frame);
                var now = parts.Glass.Append(parts.Card1).Concat(parts.Items.SelectMany(item => item.Parts)).Select(control => control.Opacity + Y(control)).ToArray();
                Assert.Equal(first[k - 1], now);
            }
            ExpectSettled(parts, $"open {open} done");
        }
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Bringing_an_open_window_forward_plays_nothing_and_a_cloaked_one_is_not_activated_early() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, view) = Open(manager, rig);
        // Asked again while still cloaked: no second show, no early activation.
        manager.ShowSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["cloak", "shown"], rig.Events);
        ToGlassReady(rig);
        rig.At(SpecRise.OpenDone);
        var parts = Parts(view);
        ExpectSettled(parts, "settled");
        Deactivate(window);
        rig.Events.Clear();
        manager.ShowSettings();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["activated"], rig.Events); // already up: brought forward, no cloak, no wave
        Assert.Equal(0, rig.PendingFor(manager.SettingsOpening));
        ExpectSettled(parts, "brought forward");
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Without_a_cloak_the_window_is_shown_and_activated_at_once_with_nothing_animated() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig { Accepts = false };
        var (window, view) = Open(manager, rig);
        // Shown and activated at once, as Present always did (headless raises Activated for the show and for Activate).
        Assert.Equal(["cloak", "shown"], rig.Events.Take(2));
        Assert.NotEmpty(rig.Events.Skip(2));
        Assert.All(rig.Events.Skip(2), name => Assert.Equal("activated", name));
        Assert.True(window.IsActive);
        Assert.Null(manager.SettingsOpening);
        Assert.Equal(0, rig.PendingFor(manager.SettingsOpening));
        Dispatcher.UIThread.RunJobs();
        ExpectSettled(Parts(view), "no cloak");
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Input_reaches_the_window_while_the_wave_runs() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, view) = Open(manager, rig);
        ToGlassReady(rig);
        rig.At(0.050);
        var parts = Parts(view);
        foreach (var control in parts.Items.SelectMany(item => item.Parts)) Assert.True(control.IsHitTestVisible);
        var general = parts.Items.Single(item => item.Name == "General").Parts[0];
        Assert.True(general.Opacity is > 0 and < 1); // mid-rise
        Click(window, general);
        Assert.Contains(Nodes<ChromeSection>(view), section => Nodes<TextBlock>(section).Any(text => text.Text == "New threads")); // the General page
        rig.At(SpecRise.OpenDone);
        Assert.Equal(0, rig.PendingFor(manager.SettingsOpening));
        foreach (var control in Nodes<SidebarRow>(view).Cast<Control>().Concat(Nodes<ChromeCard>(view)))
        {
            Assert.Equal(1, control.Opacity);
            Assert.Null(control.RenderTransform);
        }
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void A_row_s_own_opacity_is_multiplied_by_the_rise_and_kept_after_it() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        manager.Model.Settings.SidebarMode = SidebarMode.PanelOnly; // the launch row is drawn at 0.5 (SettingsView.swift:449-450)
        var (_, view) = Open(manager, rig);
        // OD-0088 (ED-250): the launch row is now inside the card that rises whole (block 6, after the Look card's blocks), so
        // that card is given an opacity of its own here (as the launch row had as a block) and the row keeps its 0.5 inside it.
        var block = Parts(view).Items.Single(item => item.Name == "rows card").Parts[0];
        var launch = Nodes<ChromeRow>(block).Single(row => Nodes<TextBlock>(row).First().Text == "Open the sidebar at launch");
        block.Opacity = 0.5;
        ToGlassReady(rig);
        for (var k = 1; k <= 15; k++)
        {
            rig.At(k * Frame);
            Expect(block, 0.5 * SpecRise.Item(6, k * Frame), SpecRise.Offset * (1 - SpecRise.Item(6, k * Frame)), $"rows card at G+{k}/60");
            Assert.Equal(0.5, launch.Opacity);
        }
        rig.At(SpecRise.OpenDone);
        Assert.Equal(0.5, block.Opacity);
        Assert.Null(block.RenderTransform);
        Assert.Equal(0.5, launch.Opacity);
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void Hiding_the_window_during_the_open_settles_it_and_never_leaves_it_cloaked() => WithManager((_, manager) =>
    {
        using var rig = new SettingsRiseRig();
        var (window, view) = Open(manager, rig);
        rig.Frame(Frame);
        Assert.True(rig.Cloaked);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(rig.Cloaked);
        Assert.Equal(0, rig.PendingFor(manager.SettingsOpening));
        ExpectSettled(Parts(view), "hidden while cloaked");

        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        ToGlassReady(rig);
        rig.At(0.050);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, rig.PendingFor(manager.SettingsOpening));
        ExpectSettled(Parts(view), "hidden mid-wave");
    });
    [Fact]
    [Trait("Decision", "ED-249")]
    public void A_page_with_more_than_ten_blocks_still_ends_its_wave_at_260_ms() => WithManager((_, manager) =>
    {
        // ED-249's ENGINEERING DECISION for pages the canvas does not show: blocks past index 10 start with index 10.
        using var rig = new SettingsRiseRig();
        manager.SettingsCloak = rig.Cloak;
        manager.ShowSettings(SettingsPage.General);
        Dispatcher.UIThread.RunJobs();
        var view = View(manager);
        ToGlassReady(rig);
        Assert.True(manager.SettingsOpening!.Layout!.Page.Count > 11, "the General page has more than ten blocks");
        rig.At(0.250);
        var moving = Nodes<Control>(view).Where(control => control.IsAnimating(Visual.OpacityProperty) && control.Opacity < 1).ToArray();
        Assert.NotEmpty(moving);
        var last = SpecRise.Item(10, 0.250);
        foreach (var control in moving)
            Assert.True(control.Opacity >= last - 1e-4 || control.GetBaseValue(Visual.OpacityProperty) is { HasValue: true, Value: < 1 },
                $"{control.GetType().Name} at {control.Opacity:F4}, below index 10's {last:F4}");
        rig.At(SpecRise.OpenDone);
        Assert.True(manager.SettingsOpening.Done);
        Assert.DoesNotContain(Nodes<Control>(view), control => control.IsAnimating(Visual.OpacityProperty) || control.IsAnimating(Visual.RenderTransformProperty));
    });
    // The canvas frames' times (SPEC.md section 9): every 1/60 s from the click (f00) to settled (f28), G at f12. Before G
    // the port's window is cloaked, so only f12 to f28 have a window to draw. MARROW_RISE_FRAMES_DIR names the folder the
    // side-by-side is made from (E:/Droppy/work/design/settings-rise/port-frames); unset, a throwaway folder is used.
    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    [Trait("Decision", "ED-249")]
    public void Port_frames_render_at_the_canvas_times(string appearance) => WithManager((_, manager) =>
    {
        var root = Environment.GetEnvironmentVariable("MARROW_RISE_FRAMES_DIR") is { Length: > 0 } given
            ? given
            : System.IO.Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "rise-frames");
        var folder = System.IO.Path.Combine(root, appearance, "open");
        System.IO.Directory.CreateDirectory(folder);
        manager.Model.Settings.Theme = appearance == "dark" ? Marrow.UI.Theme.AppTheme.Dark : Marrow.UI.Theme.AppTheme.Light; // the canvas's "Dark" / light board
        using var rig = new SettingsRiseRig();
        var (window, view) = Open(manager, rig);
        ToGlassReady(rig);
        var parts = Parts(view);
        // One line per element per frame, named as the canvas's samples.json names them.
        var log = new System.Text.StringBuilder("frame,board_ms,after_glass_ready_ms,element,opacity,translate_y\n");
        var alphas = new List<double>();
        for (var f = 12; f <= 28; f++)
        {
            var after = (f - 12) * Frame;
            if (f > 12) rig.At(after);
            Drain(window);
            using (var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame"))
            {
                // Raw pixels with their alpha (Avalonia's PNG encoder writes this surface without it); tools/compare.py
                // turns them into the PNGs.
                using var buffer = frame.Lock();
                var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, $"open_f{f:00}_t{Math.Round(600 + f * 1000.0 / 60):0}ms.raw"), bytes);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "raw.txt"), $"{buffer.Size.Width} {buffer.Size.Height} {buffer.RowBytes} {buffer.Format} {buffer.AlphaFormat}\n");
                double sum = 0;
                for (var i = 3; i < bytes.Length; i += 4) sum += bytes[i];
                alphas.Add(sum / (bytes.Length / 4) / 255);
            }
            var elements = new List<(string Name, Control Control)> { ("glass", parts.Glass[0]), ("card1", parts.Card1) };
            foreach (var (index, name, controls) in parts.Items)
            {
                var canvasName = name switch
                {
                    "Search" => "search",
                    "title Appearance" => "page title",
                    "picture card" => "card 2 (picture drop)",
                    _ when index >= 2 && CardRowTitles.Contains(name) && controls.Length > 0 && controls[0] is ChromeRow => "row:" + name,
                    _ => "nav:" + name,
                };
                elements.Add((canvasName, controls[0]));
            }
            foreach (var (name, control) in elements)
                log.Append(System.FormattableString.Invariant($"{f},{600 + f * 1000.0 / 60:F1},{after * 1000:F1},{name},{control.Opacity:F4},{Y(control):F4}\n"));
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "values.csv"), log.ToString());
        Assert.Equal(17, System.IO.Directory.GetFiles(folder, "open_f*.raw").Length);
        // At G only the caption glyphs are drawn; settled, the glass and the page cover the window.
        Assert.True(alphas[0] < 0.02, $"at G the window draws {alphas[0]:P1} coverage");
        Assert.True(alphas[^1] > alphas[0] + 0.2, $"settled {alphas[^1]:P1} vs {alphas[0]:P1} at G");
    });
}
