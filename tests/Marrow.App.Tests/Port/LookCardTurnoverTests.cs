using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// OD-0088, OD-0076 (ED-254): the Settings window turns over when the Theme control changes the appearance, as the locked
// board's scenarios 08 and 09 do (SPEC 6.5): every window colour eases 0.32 s, CSS ease, and nothing changes in the click's own
// frame. Proved on the real Settings window (WindowManager, the manual frame clock), from its rendered frames at 60 Hz: the
// click frame is the frame before it, no part of the window takes more than half of its change in one frame (but the Theme
// control and the detail lines, which slide and crossfade by design), and sampled colours follow the curve.
[Collection("Headless")]
public sealed class LookCardTurnoverTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private const double FrameSeconds = 1.0 / 60;

    // A rendered frame of the window: its bytes (RGBA), size and row stride.
    private static (byte[] Pixels, int Width, int Height, int Stride) Capture(HostWindow window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return (bytes, buffer.Size.Width, buffer.Size.Height, buffer.RowBytes);
    }

    // ED-253 (round 4): the frame before a change, drawn whole. A frame drawn by dirty regions can keep a few pixels that
    // an earlier drawing left outside them (31 opaque pixels at the end of a Models page line, x 649-651, seen only when
    // other test classes had run first in the process); the change redraws the whole window, which then "jumps" there. The
    // frame before is the window's current state drawn fresh, as the end is compared with a fresh drawing below.
    private static (byte[] Pixels, int Width, int Height, int Stride) Fresh(HostWindow window)
    {
        foreach (var visual in window.GetVisualDescendants()) visual.InvalidateVisual();
        Drain(window);
        return Capture(window);
    }

    private static int At((byte[] Pixels, int Width, int Height, int Stride) frame, int x, int y, int channel) =>
        frame.Pixels[y * frame.Stride + x * 4 + channel];

    private static void Step(SettingsRiseRig rig, HostWindow window, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            rig.Frame(FrameSeconds);
            Drain(window);
        }
    }

    private void Open(AppTheme theme, Action<SettingsRiseRig, HostWindow, LookCard, AppModel> check, bool reduceMotion = false) =>
        OpenPage(SettingsPage.Appearance, theme, (rig, window, view, model) => check(rig, window, Nodes<LookCard>(view).Single(), model), reduceMotion);

    private void OpenPage(SettingsPage page, AppTheme theme, Action<SettingsRiseRig, HostWindow, SettingsView, AppModel> check, bool reduceMotion = false) =>
        WithManager((h, manager) =>
        {
            using var rig = new SettingsRiseRig { Accepts = false };
            using var accent = SwiftColor.AccentSource.Override(() => Color.Parse("#0067C0"));
            manager.Model.Settings.Theme = theme;
            manager.ShowSettings(page);
            Dispatcher.UIThread.RunJobs();
            var window = Settings(manager);
            // OD-0151: the panel is in the main window, which opens at 69 % x 92 % of the work area (ED-NEW-settingsinapp-3). These
            // frames are drawn and compared whole, so the window is the smallest that holds a panel of about the Settings window's
            // 780 x 580 (the panel is 816 x 580 here).
            window.Width = 860;
            window.Height = 680;
            if (reduceMotion) EnvironmentValues.SetAccessibilityReduceMotion(window, true);
            Drain(window);
            Step(rig, window, 60);
            // OD-0151: the Settings panel puts the focus in its search field as it opens; its blinking caret is not part of a turnover, so
            // these frames start, as the Settings window's did, with nothing focused.
            window.FocusManager?.Focus(null);
            Drain(window);
            check(rig, window, View(manager), manager.Model);
        });

    private static double Ease(int k) => CssCurve.At(CssCurve.Ease, k * 1000.0 / 60, 320);

    // ED-NEW-gatefix3-3: observe the actual settings glass inputs without a new product seam.
    private static (double Darkness, Color Tint) GlassInputs(HostWindow window)
    {
        var backdrop = Nodes<SettingsOverlay>((Control)window.Content!).Single().Backdrop;
        var piece = backdrop.GetVisualDescendants().OfType<Control>().Single(c => SwiftGlass.GetEffect(c) is not null);
        var material = typeof(SwiftGlass).Assembly.GetType("Marrow.UI.Port.GlassMaterial", throwOnError: true)!;
        var attachment = material.GetMethod("AttachmentFor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [piece])!;
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Assert.True((bool)attachment.GetType().GetField("appearanceKnown", fields)!.GetValue(attachment)!);
        return ((double)attachment.GetType().GetField("darkShown", fields)!.GetValue(attachment)!,
            (Color)attachment.GetType().GetField("tintShown", fields)!.GetValue(attachment)!);
    }

    private static void GlassEases((double Darkness, Color Tint) from, (double Darkness, Color Tint) to,
        IReadOnlyList<(double Darkness, Color Tint)> frames)
    {
        Assert.Equal(25, frames.Count);
        Assert.NotEqual(from.Darkness, to.Darkness);
        for (var k = 0; k < frames.Count; k++)
        {
            var progress = CssCurve.At(CssCurve.Ease, k * Math.Round(FrameSeconds * TimeSpan.TicksPerSecond) / TimeSpan.TicksPerMillisecond, 320);
            Assert.Equal(from.Darkness + (to.Darkness - from.Darkness) * progress, frames[k].Darkness, 6);
            if (from.Tint == to.Tint)
            {
                Assert.Equal(from.Tint, frames[k].Tint);
                continue;
            }
            var expected = CssCurve.Mix((from.Tint.R, from.Tint.G, from.Tint.B, from.Tint.A / 255.0),
                (to.Tint.R, to.Tint.G, to.Tint.B, to.Tint.A / 255.0), progress);
            var seen = frames[k].Tint;
            Assert.True(Math.Abs(seen.R - expected.R) <= 1 && Math.Abs(seen.G - expected.G) <= 1 && Math.Abs(seen.B - expected.B) <= 1
                && Math.Abs(seen.A / 255.0 - expected.A) <= 1.0 / 255 + 1e-9, $"glass tint frame {k}: {seen}, curve {expected}");
        }
    }

    // R-TEST-5 (ED-253, round 4): these frames are proved against the process-wide theme and accent, which an AppSettings init
    // in a parallel collection assigns (AppSettings.swift:728) and posts from its own thread; the collection never runs in
    // parallel with another, so no other test turns the window while it is proved (it did in the full App run).
    [Fact]
    [Trait("Rule", "R-TEST-5")]
    [Trait("Decision", "ED-253")]
    public void These_tests_run_in_a_collection_that_never_runs_in_parallel_with_another()
    {
        var collection = typeof(LookCardTurnoverTests).GetCustomAttributesData()
            .SingleOrDefault(attribute => attribute.AttributeType == typeof(CollectionAttribute))?
            .ConstructorArguments[0].Value as string;
        Assert.Equal("Headless", collection);
        var definition = typeof(LookCardTurnoverTests).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributesData())
            .Single(attribute => attribute.AttributeType == typeof(CollectionDefinitionAttribute)
                && Equals(attribute.ConstructorArguments[0].Value, collection));
        Assert.Contains(definition.NamedArguments, argument =>
            argument.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization)
            && Equals(argument.TypedValue.Value, true));
    }

    private static Rect Box(Control control, Visual to) => new(control.TranslatePoint(default, to)!.Value, control.Bounds.Size);

    // ED-253: and on a page scrolled under the pane's top veil (PaneTopVeil, Chrome.swift:579-628), whose gradients turn over
    // with the rest.
    [Theory]
    [InlineData("dark", 0)]
    [InlineData("light", 0)]
    [InlineData("dark", 60)]
    [InlineData("light", 60)]
    [Trait("Decision", "ED-254")]
    [Trait("Decision", "ED-253")]
    public void The_theme_control_turns_the_whole_settings_window_over_in_point_32_seconds_and_nothing_cuts(string appearance, double scrolled) =>
        Open(appearance == "dark" ? AppTheme.Dark : AppTheme.Light, (rig, window, card, model) =>
        {
            var dark = appearance == "dark";
            if (scrolled > 0)
            {
                var page = Nodes<ScrollViewer>((Control)window.Content!).Single(s => s.GetVisualDescendants().Contains(card));
                page.Offset = new Vector(0, scrolled);
                Drain(window);
                Step(rig, window, 60);
                Assert.True(Nodes<PaneTopVeil>((Control)window.Content!).Single().GetVisualDescendants().OfType<Border>().Any(b => b.Background is LinearGradientBrush), "the veil is drawn");
            }
            var title = Nodes<TextBlock>(card.ThemeRow).Single(t => t.Text == "Theme");
            var captions = window.GetVisualDescendants().OfType<CaptionButton>().ToArray();
            Assert.Equal(3, captions.Length);
            // Moving by design (SPEC 6.6, 6.7): the control's pill and labels, and the two crossfading detail lines.
            var moving = new[] { Box(card.Segments, window).Inflate(3), Box(card.ThemeDetail, window).Inflate(2), Box(card.AccentDetail, window).Inflate(2) };
            var pre = Fresh(window);
            var titleFrom = ((ISolidColorBrush)title.Foreground!).Color;

            var glassFrom = GlassInputs(window);
            Click(window, card.Segments.Buttons[dark ? 1 : 2]); // scenario 08: Light on a dark window (light board: Dark)
            Assert.Equal(dark ? AppTheme.Light : AppTheme.Dark, model.Settings.Theme);
            var frames = new List<(byte[] Pixels, int Width, int Height, int Stride)> { Capture(window) };
            var titles = new List<Color> { ((ISolidColorBrush)title.Foreground!).Color };
            var glassFrames = new List<(double Darkness, Color Tint)> { GlassInputs(window) };
            var darkness = new List<double[]> { captions.Select(c => c.Darkness).ToArray() };
            // The veil's two fades (their top colour), when the page is scrolled under it.
            var fades = Nodes<PaneTopVeil>((Control)window.Content!).Single().GetVisualDescendants().OfType<Border>().Where(b => b.Background is LinearGradientBrush).ToArray();
            Color[] Fades() => fades.Select(b => ((LinearGradientBrush)b.Background!).GradientStops[0].Color).ToArray();
            var veil = new List<Color[]> { Fades() };
            for (var k = 1; k <= 24; k++)
            {
                Step(rig, window);
                frames.Add(Capture(window));
                titles.Add(((ISolidColorBrush)title.Foreground!).Color);
                glassFrames.Add(GlassInputs(window));
                darkness.Add(captions.Select(c => c.Darkness).ToArray());
                veil.Add(Fades());
            }
            Step(rig, window, 40);
            var end = Capture(window);
            var veilEnd = Fades();
            var titleTo = ((ISolidColorBrush)title.Foreground!).Color;
            Assert.NotEqual(titleFrom, titleTo);
            GlassEases(glassFrom, GlassInputs(window), glassFrames);

            // The click frame draws what the frame before it drew: no pixel of the window has moved (a cut changes them all).
            var still = 0;
            for (var y = 0; y < pre.Height; y++)
                for (var x = 0; x < pre.Width; x++)
                    for (var c = 0; c < 4; c++)
                        still = Math.Max(still, Math.Abs(At(frames[0], x, y, c) - At(pre, x, y, c)));
            Assert.True(still <= 2, $"the click frame differs from the frame before by {still} levels");

            // No frame takes a part of the window more than half of its way at once, outside what moves by design.
            var changed = 0;
            var total = new int[pre.Width, pre.Height];
            for (var y = 0; y < pre.Height; y++)
                for (var x = 0; x < pre.Width; x++)
                {
                    var d = 0;
                    for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(At(end, x, y, c) - At(pre, x, y, c)));
                    total[x, y] = d;
                    if (d > 8) changed++;
                }
            Assert.True(changed > pre.Width * pre.Height / 2, $"only {changed} pixels turned over");
            for (var k = 0; k < frames.Count; k++)
            {
                var before = k == 0 ? pre : frames[k - 1];
                var jumps = 0;
                for (var y = 0; y < pre.Height; y++)
                    for (var x = 0; x < pre.Width; x++)
                    {
                        if (total[x, y] <= 8 || moving.Any(r => r.Contains(new Point(x + 0.5, y + 0.5)))) continue;
                        var d = 0;
                        for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(At(frames[k], x, y, c) - At(before, x, y, c)));
                        if (d > total[x, y] / 2) jumps++;
                    }
                Assert.True(jumps <= changed / 100, $"frame {k}: {jumps} of {changed} turning pixels took over half their change at once");
            }

            // Sampled colours follow the curve: the window's own backdrop and sheet (top of the detail pane, clear of words),
            // the Look card's fill (below its Accent row's words) and the mini window's surface.
            var cardFill = card.TranslatePoint(new Point(40, card.Bounds.Height - 4), window)!.Value;
            var miniSurface = card.Mini.TranslatePoint(new Point(118, 124), window)!.Value;
            // ED-NEW-gatefix3-3: OD-0147 / ED-NEW-glasslook-4(2) made composed RGBA nonlinear.
            // Exact RGBA frames from the approved glass landing 51fb3dde8; all other samples keep their curve bounds.
            var approvedCard = Convert.FromHexString(dark
                ? "19191BCC202022CC2C2C2CCD3D3D3DCE4F4F4FCF626263CF717171CF7F7F7FCE8A8A8ACD919191CC999999CB9F9F9FCBA3A3A3CAA7A7A7CBAAAAAACAAEAEAECAAFAFAFCAB1B1B1CAB1B1B1C9B1B1B1C9B1B1B1C9B1B1B1C9B1B1B1C9B1B1B1C9B1B1B1C9"
                : "B1B1B1C9ACACACCAA3A3A3CB959595CB878788CD777777CE686869CF595959CF4C4C4CCF424243CE383839CE313131CE2B2B2BCD272727CC222222CC202021CD1D1D1DCC1B1B1CCC19191BCC19191BCC19191BCC19191BCC19191BCC19191BCC19191BCC");
            if (scrolled == 60)
                Assert.Equal(approvedCard, frames.SelectMany(f => Enumerable.Range(0, 4)
                    .Select(c => (byte)At(f, (int)cardFill.X, (int)cardFill.Y, c))).ToArray());
            // Scrolled, the window's top is drawn under the veil: two layers each on the curve, which a pixel there does not follow
            // exactly; the veil's own colours are checked below instead.
            foreach (var (name, point) in new[] { ("window", new Point(450, 8)), ("card fill", cardFill), ("mini surface", miniSurface) }.Where(s => scrolled == 0 || s.Item1 != "window"))
            {
                var (px, py) = ((int)point.X, (int)point.Y);
                for (var k = 0; k < frames.Count; k++)
                    for (var c = 0; c < 4; c++)
                    {
                        var from = At(pre, px, py, c);
                        var to = At(end, px, py, c);
                        var expected = from + (to - from) * Ease(k);
                        var seen = At(frames[k], px, py, c);
                        if (scrolled == 60 && name == "card fill")
                            Assert.Equal(approvedCard[k * 4 + c], seen);
                        else
                            Assert.True(Math.Abs(seen - expected) <= 0.03 * Math.Abs(to - from) + 1.5, $"{name} ({px}, {py}) channel {c} frame {k}: {seen}, curve {expected:F1} ({from} to {to})");
                    }
            }

            // The row title's colour (a ChromeRow's SwiftColor brush) and the caption glyphs, frame by frame on the curve.
            for (var k = 0; k < titles.Count; k++)
            {
                var p = Ease(k);
                var expected = CssCurve.Mix((titleFrom.R, titleFrom.G, titleFrom.B, titleFrom.A / 255.0), (titleTo.R, titleTo.G, titleTo.B, titleTo.A / 255.0), p);
                var seen = titles[k];
                Assert.True(Math.Abs(seen.R - expected.R) <= 1 && Math.Abs(seen.A / 255.0 - expected.A) <= 1.0 / 255 + 1e-9, $"title frame {k}: {seen}, curve {expected}");
                foreach (var value in darkness[k])
                    Assert.Equal(dark ? 1 - p : p, value, 3);
            }

            // The veil's fades, frame by frame on the curve from what they showed to the new appearance's (ED-253).
            Assert.Equal(scrolled > 0 ? 2 : 0, fades.Length);
            for (var f = 0; f < fades.Length; f++)
            {
                var (from, to) = (veil[0][f], veilEnd[f]);
                Assert.NotEqual(from, to);
                for (var k = 0; k < veil.Count; k++)
                {
                    var expected = CssCurve.Mix((from.R, from.G, from.B, from.A / 255.0), (to.R, to.G, to.B, to.A / 255.0), Ease(k));
                    var seen = veil[k][f];
                    Assert.True(Math.Abs(seen.R - expected.R) <= 1 && Math.Abs(seen.B - expected.B) <= 1 && Math.Abs(seen.A / 255.0 - expected.A) <= 1.0 / 255 + 1e-9, $"veil fade {f} frame {k}: {seen}, curve {expected}");
                }
            }
        });

    [Fact]
    [Trait("Decision", "ED-254")]
    public void Under_reduce_motion_the_window_takes_the_new_appearance_at_once() => Open(AppTheme.Dark, (rig, window, card, model) =>
    {
        Click(window, card.Segments.Buttons[1]);
        var now = Capture(window);
        Step(rig, window, 40);
        var end = Capture(window);
        var most = 0;
        for (var i = 0; i < now.Pixels.Length; i++) most = Math.Max(most, Math.Abs(now.Pixels[i] - end.Pixels[i]));
        Assert.True(most <= 2, $"the click frame is {most} levels from the end");
        Assert.All(window.GetVisualDescendants().OfType<CaptionButton>(), c => Assert.Equal(0, c.Darkness));
    }, reduceMotion: true);

    // ED-254: the window's wash is Chrome.glassTint as it is now (the source's WindowBackdrop :263-265, 12 %), so a named theme
    // washes the window with its surface, easing 0.32 s from the last theme's.
    [Fact]
    [Trait("Decision", "ED-254")]
    public void A_named_theme_washes_the_window_with_its_surface_easing_in() => Open(AppTheme.Dark, (rig, window, card, model) =>
    {
        var backdrop = Nodes<SettingsView>((Control)window.Content!).Single().RiseBackdrop;
        var tint = (SwiftShapeBorder)((Panel)backdrop.Child!).Children[1]; // ED-181: the backdrop's layers are SwiftShapeBorders
        var from = ((ISolidColorBrush)tint.Background!).Color;
        Assert.Equal(AppTheme.Dark.Spec().Surface.Opacity(0.12).Resolve(ThemeVariant.Dark), from);
        var to = SwiftColor.Hex("282A36").Opacity(0.12).Resolve(ThemeVariant.Dark); // Dracula's surface
        model.Settings.Theme = AppTheme.Dracula;
        Drain(window);
        for (var k = 0; k <= 22; k++)
        {
            if (k > 0) Step(rig, window);
            var expected = CssCurve.Mix((from.R, from.G, from.B, from.A / 255.0), (to.R, to.G, to.B, to.A / 255.0), Ease(k));
            var seen = ((ISolidColorBrush)tint.Background!).Color;
            Assert.True(Math.Abs(seen.R - expected.R) <= 1 && Math.Abs(seen.B - expected.B) <= 1, $"frame {k}: {seen}, curve {expected}");
        }
        Assert.Equal(to, ((ISolidColorBrush)tint.Background!).Color);
    });

    // ED-253: the veil's tint is Chrome.glassTint (Chrome.swift:589, 30 % in dark) read as it is drawn, so on a scrolled page a
    // named theme of the same appearance turns it to its surface too, easing 0.32 s from the last theme's.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void On_a_scrolled_page_a_named_theme_turns_the_veil_tint_to_its_surface_easing_in() => Open(AppTheme.Dark, (rig, window, card, model) =>
    {
        var page = Nodes<ScrollViewer>((Control)window.Content!).Single(s => s.GetVisualDescendants().Contains(card));
        page.Offset = new Vector(0, 60);
        Drain(window);
        Step(rig, window, 60);
        var tint = Nodes<PaneTopVeil>((Control)window.Content!).Single().GetVisualDescendants().OfType<Border>().Where(b => b.Background is LinearGradientBrush).Last();
        Color Top() => ((LinearGradientBrush)tint.Background!).GradientStops[0].Color;
        var from = Top();
        Assert.Equal(AppTheme.Dark.Spec().Surface.Opacity(0.30).Resolve(ThemeVariant.Dark), from);
        var to = SwiftColor.Hex("282A36").Opacity(0.30).Resolve(ThemeVariant.Dark); // Dracula's surface
        model.Settings.Theme = AppTheme.Dracula;
        Drain(window);
        for (var k = 0; k <= 22; k++)
        {
            if (k > 0) Step(rig, window);
            var expected = CssCurve.Mix((from.R, from.G, from.B, from.A / 255.0), (to.R, to.G, to.B, to.A / 255.0), Ease(k));
            var seen = Top();
            Assert.True(Math.Abs(seen.R - expected.R) <= 1 && Math.Abs(seen.B - expected.B) <= 1, $"frame {k}: {seen}, curve {expected}");
        }
        Assert.Equal(to, Top());
    });

    // ED-253: every page of the window turns over without a cut, and ends as a fresh drawing of the new appearance would.
    [Theory]
    [InlineData(SettingsPage.General)]
    [InlineData(SettingsPage.Hydra)]
    [InlineData(SettingsPage.Providers)]
    [InlineData(SettingsPage.Mcp)]
    [InlineData(SettingsPage.Models)]
    [InlineData(SettingsPage.SourceControl)]
    [InlineData(SettingsPage.Shortcuts)]
    [InlineData(SettingsPage.Archive)]
    [InlineData(SettingsPage.About)]
    [Trait("Decision", "ED-253")]
    public void Every_page_turns_over_without_a_cut_and_ends_on_the_new_appearance(SettingsPage page) => OpenPage(page, AppTheme.Dark, (rig, window, view, model) =>
        TurnsOverWithoutACut(page, rig, window, () => model.Settings.Theme = AppTheme.Light));

    // ED-253: under System the system's own appearance change (no theme change, so no ThemeManager.DidChange) turns the General
    // page over all the same, the heatmap's cells included.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void Under_system_the_general_page_turns_over_when_the_system_appearance_changes() => OpenPage(SettingsPage.General, AppTheme.System, (rig, window, view, model) =>
    {
        var application = Application.Current!;
        Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant); // the headless system's
        // The system turns dark (WindowManager.SetAppearance leaves System to it), and light again.
        TurnsOverWithoutACut(SettingsPage.General, rig, window, () => application.RequestedThemeVariant = ThemeVariant.Dark);
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
        var canvas = window.GetVisualDescendants().OfType<TokenActivityHeatmapCanvas>().Single();
        // The middle of a past day's cell in the first row, two fifths across (the grid's first cells may hold no day).
        var gap = TokenActivityStyle.CellGap;
        var size = (canvas.Bounds.Height - gap * (Marrow.Services.Store.TokenActivityGrid.Rows - 1)) / Marrow.Services.Store.TokenActivityGrid.Rows;
        var columns = (int)Math.Round((canvas.Bounds.Width + gap) / (size + gap));
        var cell = canvas.TranslatePoint(new Point(columns * 2 / 5 * (size + gap) + size / 2, size / 2), window)!.Value;
        var frames = TurnsOverWithoutACut(SettingsPage.General, rig, window, () => application.RequestedThemeVariant = ThemeVariant.Light);
        // The cell (Chrome.overlay(0.07) over the easing window) on the curve every frame: [0] before, [^1] the end.
        var (px, py) = ((int)cell.X, (int)cell.Y);
        for (var k = 0; k < frames.Count - 2; k++)
            for (var c = 0; c < 4; c++)
            {
                var from = At(frames[0], px, py, c);
                var to = At(frames[^1], px, py, c);
                var expected = from + (to - from) * Ease(k);
                var seen = At(frames[k + 1], px, py, c);
                Assert.True(Math.Abs(seen - expected) <= 0.03 * Math.Abs(to - from) + 1.5, $"heatmap cell ({px}, {py}) channel {c} frame {k}: {seen}, curve {expected:F1} ({from} to {to})");
            }
    });

    // The frames of the turnover (the change's own first), checked: none cuts, and it ends as a fresh drawing would.
    private static List<(byte[] Pixels, int Width, int Height, int Stride)> TurnsOverWithoutACut(SettingsPage page, SettingsRiseRig rig, HostWindow window, Action change)
    {
        // A page still filling in (a list loading in the background) is let settle first: two frames alike in a row.
        var pre = Capture(window);
        for (var i = 0; i < 120; i++)
        {
            Step(rig, window);
            var next = Capture(window);
            var same = next.Pixels.AsSpan().SequenceEqual(pre.Pixels);
            pre = next;
            if (same) break;
        }
        pre = Fresh(window);
        change();
        Drain(window);
        var frames = new List<(byte[] Pixels, int Width, int Height, int Stride)> { Capture(window) };
        for (var k = 1; k <= 24; k++)
        {
            Step(rig, window);
            frames.Add(Capture(window));
        }
        Step(rig, window, 40);
        var end = Capture(window);
        foreach (var visual in window.GetVisualDescendants()) visual.InvalidateVisual();
        Drain(window);
        var fresh = Capture(window);
        var stale = 0;
        var staleAt = default(PixelPoint);
        for (var y = 0; y < pre.Height; y++)
            for (var x = 0; x < pre.Width; x++)
                for (var c = 0; c < 4; c++)
                    if (Math.Abs(At(end, x, y, c) - At(fresh, x, y, c)) > 2) { stale++; staleAt = new PixelPoint(x, y); }
        Assert.True(stale == 0, $"{page}: {stale} pixel channels end apart from a fresh drawing, e.g. at {staleAt} ({string.Join(",", Enumerable.Range(0, 4).Select(c => At(end, staleAt.X, staleAt.Y, c)))} against {string.Join(",", Enumerable.Range(0, 4).Select(c => At(fresh, staleAt.X, staleAt.Y, c)))}) DIAG {string.Join(" < ", window.GetVisualsAt(new Point(staleAt.X + 0.5, staleAt.Y + 0.5)).Take(1).SelectMany(v => v.GetSelfAndVisualAncestors()).Take(8).Select(v => v.GetType().Name + (v is TextBlock t ? "[" + t.Text + "]" : "")))}");
        var changed = 0;
        var total = new int[pre.Width, pre.Height];
        for (var y = 0; y < pre.Height; y++)
            for (var x = 0; x < pre.Width; x++)
            {
                var d = 0;
                for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(At(end, x, y, c) - At(pre, x, y, c)));
                total[x, y] = d;
                if (d > 8) changed++;
            }
        Assert.True(changed > pre.Width * pre.Height / 2, $"only {changed} pixels turned over");
        for (var k = 0; k < frames.Count; k++)
        {
            var before = k == 0 ? pre : frames[k - 1];
            var jumps = 0;
            var jumpAt = default(PixelPoint);
            for (var y = 0; y < pre.Height; y++)
                for (var x = 0; x < pre.Width; x++)
                {
                    if (total[x, y] <= 8) continue;
                    var d = 0;
                    for (var c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(At(frames[k], x, y, c) - At(before, x, y, c)));
                    if (d > total[x, y] / 2 || (k == 0 && d > 2)) { jumps++; jumpAt = new PixelPoint(x, y); }
                }
            // The click frame draws what the frame before it did; no later frame takes more than 1 % of the turning pixels over
            // half their change at once (LookCardTurnoverTests' rule above; a few blended pixels are not monotonic).
            Assert.True(k == 0 ? jumps == 0 : jumps <= changed / 100, $"{page} frame {k}: {jumps} of {changed} turning pixels jumped, e.g. at {jumpAt} ({string.Join(",", Enumerable.Range(0, 4).Select(c => At(before, jumpAt.X, jumpAt.Y, c)))} to {string.Join(",", Enumerable.Range(0, 4).Select(c => At(frames[k], jumpAt.X, jumpAt.Y, c)))}, end {string.Join(",", Enumerable.Range(0, 4).Select(c => At(end, jumpAt.X, jumpAt.Y, c)))}) DIAG {string.Join(" < ", window.GetVisualsAt(new Point(jumpAt.X + 0.5, jumpAt.Y + 0.5)).Take(1).SelectMany(v => v.GetSelfAndVisualAncestors()).Take(8).Select(v => v.GetType().Name + (v is TextBlock t ? "[" + t.Text + "]" : "")))}");
        }
        frames.Add(end);
        frames.Insert(0, pre);
        return frames;
    }
}
