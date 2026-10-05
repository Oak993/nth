using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using System.Diagnostics;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// Native evidence that the app's own glass material (F.GlassMaterial, ED-180, ED-320) is drawn on a real window: the real factory main window
// (WindowManager.MakeWindow, WindowChrome.Configure, the production Win32 compositor and its GPU path) holds the fine stripes of GlassWindowNativeTests
// (six physical pixels, StripeContrast about 42.5 straight) inside its content, and the real chat chrome row (PR-CHAT-171, ChatView.swift:1120-1124,
// its GlassEffectContainer the first child of its Panel) and the real composer area as the main chat mounts it (UI-COMPOSER-002, ComposerView.swift:23,
// :1304, its container the only child of a Border) lie over them. The composed screen (ScreenGrab, BitBlt with CAPTUREBLT) along a line inside each
// capsule and inside the pill, above their glyphs, shows the stripes frosted once the scene has settled; with the glass taken off, the same line shows
// them sharp, so the frost is the material's. What the material shows over the clear window's desktop Acrylic (where the app draws nothing opaque) is
// not asserted here: ED-241 on the unmerged unit/polish-composer-glass, still open.
// Needs a free interactive desktop; the runner takes native.lock around the run.
[Collection("WindowManagerNative")]
public sealed class GlassMaterialNativeTests(WindowManagerNativeSession native)
{
    private const double Sharp = 42.5;
    private const double Blurred = 6;

    // The scene settles: the first frames of a fresh process build the material's GPU programs, which took up to about 4 s on this PC (ED-333):
    // the line is read until it is frosted, for at most 8 s, and how long that took is written out.
    private static ScreenGrab Settled(Func<ScreenGrab> line, string what)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var grab = line();
        while (grab.StripeContrast() >= Blurred && clock.ElapsedMilliseconds < 8000)
        {
            Thread.Sleep(100);
            grab = line();
        }
        Console.WriteLine($"GlassMaterialNativeTests: {what} frosted {clock.ElapsedMilliseconds} ms after the scene settled (contrast {grab.StripeContrast():F2})");
        return grab;
    }

    // A line one physical pixel high and three long blocks of the screen, from a control's point (x, y) to (x + width, y), in points.
    private ScreenGrab Line(Control control, double x, double y, double width) => native.OnUi(() =>
    {
        var start = control.PointToScreen(new Point(x, y));
        var end = control.PointToScreen(new Point(x + width, y));
        return ScreenGrab.Of(start.X, start.Y, Math.Max(8, end.X - start.X), 1);
    });

    private void ShowOverStripes(WindowManagerNativeSubject subject, Control content)
    {
        native.OnUi(() =>
        {
            subject.Settings.Hide();
            subject.MainContent.Children.Add(new Panel { Children = { new Stripes(), content } });
            subject.Main.Topmost = true;
        });
        subject.Focus(subject.Main, subject.MainProbe);
        Thread.Sleep(1200); // the scene settles: layout, the material's first frames, the window's own updates
    }

    [Fact]
    [Trait("Req", "PR-CHAT-171")] // App/Views/Chat/ChatView.swift:1120-1124 GlassEffectContainer(spacing: 2): one glass pass for the row
    public void On_the_real_main_window_the_stripes_behind_the_chrome_rows_capsules_are_frosted_and_sharp_without_the_glass() => native.Run(subject =>
    {
        var row = native.OnUi(() =>
        {
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main)); // the frame clock the app binds at start-up
            var model = new Marrow.App.AppModel();
            model.Settings.SidebarFloats = false;
            model.Settings.HydraEnabled = true;
            model.Settings.HasSeenHydraIntro = true; // no first-run intro popover over the row
            var folder = Directory.CreateTempSubdirectory("rec-composer-chat-").FullName;
            var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", folder).Url))!.Id);
            return new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, folder, new GitStatusModel()) { VerticalAlignment = VerticalAlignment.Top };
        });
        ShowOverStripes(subject, row);
        var (container, pieces, below) = native.OnUi(() =>
        {
            var container = Assert.Single(row.GetVisualDescendants().OfType<GlassEffectContainer>());
            var pieces = container.GetVisualDescendants().OfType<Control>()
                .Where(control => SwiftGlass.GetEffect(control) is not null && control.IsEffectivelyVisible && control.Bounds.Height >= 24)
                .ToArray();
            return (container, pieces, row.Bounds.Height + 150);
        });
        Assert.True(pieces.Length >= 4, $"only {pieces.Length} glass pieces in the row"); // the threads, new chat, Hydra and lock circles, the toggles capsule, ... (ChatView.swift:1124-1174)
        var evidence = native.OnUi(() =>
        {
            var origin = row.PointToScreen(default);
            var corner = row.PointToScreen(new Point(row.Bounds.Width, row.Bounds.Height + 200));
            return ScreenGrab.Of(origin.X, origin.Y, corner.X - origin.X, corner.Y - origin.Y);
        });
        var png = Path.Combine(Path.GetTempPath(), "chat-chrome-row-over-stripes.png");
        evidence.SavePng(png);
        Console.WriteLine("GlassMaterialNativeTests: the row over the stripes, " + png);
        var bare = Line(row, 100, below, 400);
        Assert.InRange(bare.StripeContrast(), Sharp - 6, Sharp + 3); // the instrument sees the stripes straight below the row

        // a line 3 points below each capsule's top edge, clear of its rounded ends; in a circle, 16 points across, 5 points below its top (inside the
        // circle and above the 16 pt glyph it centres)
        ScreenGrab Inside(Control piece) => native.OnUi(() => (piece.Bounds.Height, piece.Bounds.Width)) is var (height, width)
            ? width > height + 8 ? Line(piece, height / 2, 3, width - height) : Line(piece, width / 2 - 8, 5, 16) : throw new InvalidOperationException();
        foreach (var piece in pieces)
        {
            var frosted = Settled(() => Inside(piece), piece.GetType().Name);
            Assert.True(frosted.StripeContrast() < Blurred, $"{piece.GetType().Name}: the stripes behind it keep a contrast of {frosted.StripeContrast():F2} of {Sharp}");
        }

        native.OnUi(() =>
        {
            SwiftGlass.SetContainerSpacing(container, null);
            foreach (var piece in pieces) SwiftGlass.SetEffect(piece, null);
        });
        Thread.Sleep(600);
        foreach (var piece in pieces)
        {
            var through = Inside(piece);
            Assert.True(through.StripeContrast() > 20, $"{piece.GetType().Name}: without the glass its line keeps only {through.StripeContrast():F2}, so the line does not measure the glass");
        }
    }, factType: typeof(GlassMaterialNativeTests));

    [Fact]
    [Trait("Req", "UI-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:1304 .glassEffect(.regular, in: RoundedRectangle(cornerRadius: 22, style: .continuous))
    public void On_the_real_main_window_the_stripes_behind_the_main_chat_composer_pill_are_frosted_and_sharp_without_the_glass() => native.Run(subject =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var area = native.OnUi(() =>
        {
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main));
            var model = new Marrow.App.AppModel();
            // a runtime for a thread the model does not hold resolves no thread, so the pill leaves its controls out (ComposerView.swift:160) and starts no provider
            var runtime = new ThreadRuntime(Guid.NewGuid(), model);
            var area = new ComposerArea(runtime, workingDirectory: null) { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 40) };
            AppEnvironment.SetAppModel(area, model);
            return area;
        });
        ShowOverStripes(subject, area);
        var (pill, container, width) = native.OnUi(() =>
        {
            var pill = Assert.Single(area.GetVisualDescendants().OfType<Control>(),
                control => SwiftGlass.GetEffect(control) is { } effect && effect.Shape.Equals(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)));
            var container = Assert.Single(area.GetVisualDescendants().OfType<Control>(), control => SwiftGlass.GetContainerSpacing(control) == 8);
            Assert.True(pill.Bounds.Height >= 44, $"the pill is laid out: {pill.Bounds}");
            return (pill, container, pill.Bounds.Width);
        });
        var bare = Line(pill, 24, -30, width - 48);
        Assert.InRange(bare.StripeContrast(), Sharp - 6, Sharp + 3); // the instrument sees the stripes straight above the composer
        var frosted = Settled(() => Line(pill, 24, 3, width - 48), "the pill"); // 3 points below the pill's top edge, above its 22 pt text row, clear of the 22 pt corners
        Assert.True(frosted.StripeContrast() < Blurred, $"the stripes behind the pill keep a contrast of {frosted.StripeContrast():F2} of {Sharp}");

        native.OnUi(() =>
        {
            SwiftGlass.SetContainerSpacing(container, null);
            SwiftGlass.SetEffect(pill, null);
        });
        Thread.Sleep(600);
        var through = Line(pill, 24, 3, width - 48);
        Assert.True(through.StripeContrast() > 20, $"without the glass the pill's line keeps only {through.StripeContrast():F2}, so the line does not measure the glass");
    }, factType: typeof(GlassMaterialNativeTests));

    [Fact]
    [Trait("Req", "PR-TERM-074")] // App/Views/Chat/ChatView.swift:1456-1463 .presentationBackground { WindowBackdrop(opacity:) }; OD-0005 (the material over app content)
    public void On_the_real_main_window_the_scripts_sheet_frosts_the_stripes_under_it_and_without_the_glass_its_scrim_alone_shows_them() => native.Run(subject =>
    {
        var (anchor, present) = native.OnUi(() =>
        {
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main));
            var model = new Marrow.App.AppModel();
            var folder = Directory.CreateTempSubdirectory("rec-composer-scripts-").FullName;
            var project = model.AddProject(new Project("P", folder).Url);
            var anchor = new Border(); // where the chat column stands; the sheet centres on it
            return (anchor, (Action)(() => typeof(ScriptsMenu).GetMethod("PresentScriptsEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .Invoke(null, [model, project.Id, anchor])));
        });
        ShowOverStripes(subject, anchor);
        native.OnUi(present);
        Thread.Sleep(1200); // the sheet grows in (PopoverAppear) and its glass settles
        var (sheet, backdrop, width) = native.OnUi(() =>
        {
            var backdrop = subject.Main.GetVisualDescendants().OfType<WindowBackdrop>().Single(layer => layer.GetVisualAncestors().OfType<Marrow.App.Port.WindowSheetLayer>().Any());
            var sheet = backdrop.GetVisualAncestors().OfType<Border>().First(border => border.ClipToBounds && border.CornerRadius == new CornerRadius(Chrome.WindowCornerRadius));
            Assert.Equal(new Size(580, 460), sheet.Bounds.Size);
            return (sheet, backdrop, sheet.Bounds.Width);
        });
        var bare = Line(sheet, 30, -20, width - 60);
        Assert.InRange(bare.StripeContrast(), Sharp - 6, Sharp + 3); // the instrument sees the stripes straight above the sheet
        var frosted = Settled(() => Line(sheet, 30, 6, width - 60), "the scripts sheet"); // 6 points below the sheet's top edge, inside its 20 point padding, clear of its 26 point corners
        Assert.True(frosted.StripeContrast() < Blurred, $"the stripes behind the scripts sheet keep a contrast of {frosted.StripeContrast():F2} of {Sharp}");

        native.OnUi(() =>
        {
            foreach (var glass in backdrop.GetVisualDescendants().OfType<Control>().Where(control => SwiftGlass.GetEffect(control) is not null).ToArray()) SwiftGlass.SetEffect(glass, null);
        });
        Thread.Sleep(600);
        var scrimmed = Line(sheet, 30, 6, width - 60);
        Assert.True(scrimmed.StripeContrast() > 20, $"without the glass the sheet's line keeps only {scrimmed.StripeContrast():F2}, so the line does not measure the glass");
    }, factType: typeof(GlassMaterialNativeTests));

    private static string Evidence => GalleryRun.OptionalOutputFolder() ?? Path.Combine(Path.GetTempPath(), "marrow-glass-native");

    private static void Themeless(AvaloniaWindow window) =>
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });

    private static Border Pill()
    {
        var pill = new Border { Width = 818, Height = 46, Background = Brushes.Transparent, CornerRadius = new CornerRadius(22), HorizontalAlignment = HorizontalAlignment.Left };
        SwiftGlass.SetEffect(pill, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)));
        return pill;
    }

    [Fact]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:179-182; ComposerView.swift:1304; OD-0107, OD-0005
    public void The_composer_pill_glass_on_a_real_window_is_translucent_over_the_Acrylic_lit_over_busy_content_and_costs_under_two_ms_a_frame() => native.Run(subject =>
    {
        Directory.CreateDirectory(Evidence);
        var (work, scale) = native.OnUi(() =>
        {
            var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
            if (screen.WorkingArea.Width < 1400 || screen.WorkingArea.Height < 900)
                throw new InvalidOperationException("NATIVE_PRECONDITION: the glass tests need a working area of at least 1400 x 900 pixels.");
            subject.Settings.Hide();
            subject.Main.Hide();
            return (screen.WorkingArea, screen.Scaling);
        });
        var origin = new PixelPoint(work.X + (work.Width - (int)(1000 * scale)) / 2, work.Y + (work.Height - (int)(400 * scale)) / 2);
        var (stripes, window, bars, acrylicPill, busyPill) = native.OnUi(() =>
        {
            var stripes = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(origin.X - 40, origin.Y - 40),
                Width = 1080, Height = 480, Content = new Stripes(),
            };
            Themeless(stripes);
            native.Register(stripes);
            stripes.Show();
            var bars = new GlassBars { Height = 160, VerticalAlignment = VerticalAlignment.Bottom };
            var acrylicPill = Pill();
            acrylicPill.Margin = new Thickness(91, 60, 0, 0);
            acrylicPill.VerticalAlignment = VerticalAlignment.Top;
            var busyPill = Pill();
            busyPill.Margin = new Thickness(91, 0, 0, 57);
            busyPill.VerticalAlignment = VerticalAlignment.Bottom;
            var window = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = origin, Width = 1000, Height = 400,
                Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent],
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                Content = new Panel { Children = { bars, acrylicPill, busyPill } },
            };
            Themeless(window);
            native.Register(window);
            window.Show();
            return (stripes, window, bars, acrylicPill, busyPill);
        });
        Thread.Sleep(1200);
        PixelPoint At(Control piece, double x, double y) => native.OnUi(() => piece.PointToScreen(new Point(x, y)));

        // Over the Acrylic: the pill's middle against the Acrylic just above it, and the pill's top rim against its middle.
        var a = At(acrylicPill, -20, -24);
        var acrylic = ScreenGrab.Of(a.X, a.Y, (int)(858 * scale), (int)(94 * scale));
        acrylic.SavePng(Path.Combine(Evidence, "pill-over-acrylic.png"));
        var aIn = ScreenGrab.Of(At(acrylicPill, 300, 23).X, At(acrylicPill, 300, 23).Y, (int)(200 * scale), 1);
        var aOut = ScreenGrab.Of(At(acrylicPill, 300, -12).X, At(acrylicPill, 300, -12).Y, (int)(200 * scale), 1);
        var aRim = ScreenGrab.Of(At(acrylicPill, 300, 0.5).X, At(acrylicPill, 300, 0.5).Y, (int)(200 * scale), 1);
        var aBelowRim = ScreenGrab.Of(At(acrylicPill, 300, 8).X, At(acrylicPill, 300, 8).Y, (int)(200 * scale), 1);
        Assert.True(aOut.StripeContrast() < 3, $"the instrument: the Acrylic blurs the stripes ({aOut.StripeContrast():F2})");
        Assert.True(aIn.MeanLuma() >= aOut.MeanLuma() + 8, $"over the Acrylic the glass is a light film over it, not a dark veil: inside {aIn.MeanLuma():F1}, Acrylic {aOut.MeanLuma():F1}");
        Assert.True(aIn.MeanLuma() <= aOut.MeanLuma() + 70, $"and the Acrylic shows through it: inside {aIn.MeanLuma():F1}, Acrylic {aOut.MeanLuma():F1}");
        Assert.True(aRim.MeanLuma() >= aBelowRim.MeanLuma() + 10, $"its top rim: {aRim.MeanLuma():F1} against {aBelowRim.MeanLuma():F1}");

        // Over the busy content: blurred (the bars' steps are gone), lit at the top, and the frame cost while the bars move under it.
        var b = At(busyPill, -20, -24);
        var costs = new List<double>();
        GlassMaterial.FrameCostForTests = (size, elapsed) => { if (size.Width == 818 && size.Height == 46) lock (costs) costs.Add(elapsed.TotalMilliseconds); };
        try
        {
            var bIn = ScreenGrab.Of(At(busyPill, 300, 23).X, At(busyPill, 300, 23).Y, (int)(200 * scale), 1);
            var bOut = ScreenGrab.Of(At(busyPill, 300, 80).X, At(busyPill, 300, 80).Y, (int)(200 * scale), 1);
            ScreenGrab.Of(b.X, b.Y, (int)(858 * scale), (int)(160 * scale)).SavePng(Path.Combine(Evidence, "pill-over-busy-content.png"));
            Assert.True(bOut.StripeContrast() > 8, $"the instrument: the bars are sharp outside the glass ({bOut.StripeContrast():F2})");
            Assert.True(bIn.StripeContrast() < bOut.StripeContrast() * 0.25, $"the glass blurs them: {bIn.StripeContrast():F2} against {bOut.StripeContrast():F2}");
            var watch = Stopwatch.StartNew();
            var frames = 0;
            while (watch.ElapsedMilliseconds < 2500)
            {
                native.OnUi(() => { bars.Phase += 2; bars.InvalidateVisual(); });
                frames++;
                Thread.Sleep(16);
            }
            Thread.Sleep(200);
        }
        finally { GlassMaterial.FrameCostForTests = null; }
        double[] sorted;
        lock (costs) sorted = [.. costs.OrderBy(c => c)];
        File.WriteAllText(Path.Combine(Evidence, "frame-cost.txt"),
            $"composer pill 818 x 46 at scale {scale}: {sorted.Length} draws; median {sorted[sorted.Length / 2]:F3} ms, p95 {sorted[(int)(sorted.Length * 0.95)]:F3} ms, max {sorted[^1]:F3} ms\n");
        Assert.True(sorted.Length >= 60, $"the pill was drawn {sorted.Length} times while the bars moved");
        Assert.True(sorted[sorted.Length / 2] < 2.0, $"median cost {sorted[sorted.Length / 2]:F3} ms");
        Assert.True(sorted[(int)(sorted.Length * 0.95)] < 4.0, $"p95 cost {sorted[(int)(sorted.Length * 0.95)]:F3} ms");
        GC.KeepAlive(stripes);
        GC.KeepAlive(window);
    }, factType: typeof(GlassMaterialNativeTests));

    // ED-356 (the first frame; the knob-cache unit's finding: on a real window the glass first drew 34 to 53 ms after the window was shown,
    // with or without the GPU warm-up). A fresh window, red around a glass piece over black and white stripes, is shown while a thread reads the screen
    // there as fast as it can (a few hundred grabs a second, every presented frame seen). The first grab that shows the window (its red) must already
    // show the glass over the stripes (blurred: no stripe steps), never the sharp stripes with the glass still to come.
    [Fact]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:179-182; the owner's bar: no pop-in
    public void A_freshly_shown_window_has_its_glass_in_the_first_frame_it_presents() => native.Run(subject =>
    {
        var (work, scale) = native.OnUi(() =>
        {
            var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
            subject.Settings.Hide();
            subject.Main.Hide();
            return (screen.WorkingArea, screen.Scaling);
        });
        var origin = new PixelPoint(work.X + (work.Width - (int)(400 * scale)) / 2, work.Y + (work.Height - (int)(200 * scale)) / 2);
        var results = new List<(double At, bool Shown, double Glass, double Bare, int Draws)>();
        var running = true;
        // The window's red at (10, 10), the glass's middle at (200, 100), the bare stripes at (60, 100), all in pt from the window's corner.
        var (gx, gy, gw, gh) = (origin.X, origin.Y, (int)(400 * scale), (int)(200 * scale));
        var clock = Stopwatch.StartNew();
        var reader = new Thread(() =>
        {
            while (Volatile.Read(ref running))
            {
                var grab = ScreenGrab.Of(gx, gy, gw, gh);
                var (r, g, b) = grab.At((int)(10 * scale), (int)(10 * scale));
                double Contrast(int cx, int cy)
                {
                    double steps = 0;
                    for (var x = cx - (int)(30 * scale); x < cx + (int)(30 * scale); x++) steps += Math.Abs(grab.Luma(x + 1, cy) - grab.Luma(x, cy));
                    return steps / (int)(60 * scale);
                }
                var sample = (clock.Elapsed.TotalMilliseconds, r > 170 && g < 90 && b < 90, Contrast((int)(200 * scale), (int)(100 * scale)), Contrast((int)(60 * scale), (int)(100 * scale)), Volatile.Read(ref GlassMaterial.DrawsForTests));
                lock (results) results.Add(sample);
            }
        }) { IsBackground = true };
        var steps = new List<string>();
        GlassMaterial.TraceForTests = step => { lock (steps) steps.Add($"{clock.Elapsed.TotalMilliseconds:F1} ms {step}"); };
        reader.Start();
        Thread.Sleep(300);
        var window = native.OnUi(() =>
        {
            lock (steps) steps.Add($"{clock.Elapsed.TotalMilliseconds:F1} ms Show");
            var glass = new Border { Width = 200, Height = 80, Background = Brushes.Transparent, CornerRadius = new CornerRadius(20),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            SwiftGlass.SetEffect(glass, GlassEffect.Regular(GlassShape.RoundedRectangle(20, RoundedCornerStyle.Continuous)));
            var stripes = new Stripes { Width = 340, Height = 120, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var window = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = origin, Width = 400, Height = 200,
                RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                Content = new Panel { Background = new SolidColorBrush(Color.FromRgb(220, 40, 40)), Children = { stripes, glass } }, // the themeless template draws no window background
            };
            Themeless(window);
            native.Register(window);
            window.Show();
            return window;
        });
        Thread.Sleep(1200);
        Volatile.Write(ref running, false);
        reader.Join();
        GlassMaterial.TraceForTests = null;
        (double At, bool Shown, double Glass, double Bare, int Draws)[] seen;
        lock (results) seen = [.. results];
        var first = Array.FindIndex(seen, s => s.Shown);
        Assert.True(first >= 0, "the window was never seen");
        var glassAt = Array.FindIndex(seen, s => s.Shown && s.Glass < 3);
        File.WriteAllText(Path.Combine(Evidence, "first-frame.txt"),
            $"{seen.Length} grabs; window first seen at grab {first} ({seen[first].At:F1} ms), glass first seen at grab {glassAt} ({(glassAt < 0 ? double.NaN : seen[glassAt].At):F1} ms)\n" +
            string.Join("\n", seen.Skip(Math.Max(0, first - 2)).Take(12).Select(s => $"{s.At:F1} ms shown {s.Shown} glass contrast {s.Glass:F2} bare {s.Bare:F2} material draws {s.Draws}")) + "\n" +
            string.Join("\n", steps.Take(20)) + "\n");
        Assert.True(seen[first].Bare > 10, $"the instrument: the stripes beside the glass are sharp in the first frame ({seen[first].Bare:F2})");
        Assert.True(seen[first].Glass < 3, $"the first frame the window presents already has its glass: stripe contrast {seen[first].Glass:F2} under it" +
            $" (the glass is first seen {(glassAt < 0 ? double.NaN : seen[glassAt].At - seen[first].At):F1} ms later)");
        GC.KeepAlive(window);
    }, factType: typeof(GlassMaterialNativeTests));
    [Fact]
    [Trait("Req", "PR-WIN-180")] // Chrome.swift:179-182; OD-0107, Mac frame 18, ED-358 (4)
    public void A_large_dark_card_has_a_flat_body_and_no_bright_rim_on_a_real_window() => native.Run(subject =>
    {
        var (window, card) = native.OnUi(() =>
        {
            subject.Main.Hide();
            subject.Settings.Hide();
            var card = new Border
            {
                Width = 400, Height = 267, CornerRadius = new CornerRadius(22), Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            SwiftGlass.SetEffect(card, GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)));
            var window = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false,
                Width = 520, Height = 387, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                Content = new Panel { Background = new SolidColorBrush(Color.FromRgb(27, 27, 27)), Children = { card } },
            };
            Themeless(window);
            native.Register(window);
            window.Show();
            return (window, card);
        });
        var point = native.OnUi(() => card.PointToScreen(new Point(200, 46)));
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (ScreenPixels.At(point.X, point.Y).G is < 43 or > 51 && DateTime.UtcNow < deadline) Thread.Sleep(20);
        var points = native.OnUi(() => Enumerable.Range(0, 11).Select(y => card.PointToScreen(new Point(200, y + 0.5)))
            .Append(card.PointToScreen(new Point(200, 46))).Append(card.PointToScreen(new Point(200, 206))).ToArray());
        var colours = points.Select(p => ScreenPixels.At(p.X, p.Y)).ToArray();
        var body = colours[^2].G;
        Assert.InRange(body, (byte)43, (byte)51); // the Mac fit's approximately 44 before the site's tint, over the 27 backdrop
        Assert.InRange(Math.Abs(body - colours[^1].G), 0, 2);
        foreach (var pixel in colours.Take(11)) Assert.InRange(Math.Abs(pixel.G - body), 0, 3);
        var origin = native.OnUi(() => window.PointToScreen(default));
        var scale = native.OnUi(() => window.RenderScaling);
        var folder = Evidence;
        Directory.CreateDirectory(folder);
        ScreenGrab.Of(origin.X, origin.Y, (int)Math.Round(520 * scale), (int)Math.Round(387 * scale))
            .SavePng(Path.Combine(folder, "dark-card-native.png"));
        File.WriteAllText(Path.Combine(folder, "dark-card-native.txt"),
            $"scale {scale}; edge rows 0..10, upper body, lower body: {string.Join(" ", colours)}{Environment.NewLine}");
        GC.KeepAlive(window);
    }, factType: typeof(GlassMaterialNativeTests));

    [Fact]
    [Trait("Req", "PR-WIN-180")] // HydraPanel.swift:99-121; OD-0107, Mac frame 18; ED-358 (4)
    public void The_card_material_on_a_real_window_matches_the_Mac_body_and_band_over_the_remade_backdrop() => native.Run(subject =>
    {
        Avalonia.Media.Imaging.Bitmap? bitmap = null;
        var previous = native.OnUi(() => Marrow.UI.Theme.ThemeManager.Current);
        AvaloniaWindow? window = null;
        try
        {
            window = native.OnUi(() =>
            {
                Marrow.UI.Theme.ThemeManager.Current = Marrow.UI.Theme.AppTheme.Monokai;
                subject.Main.Hide();
                subject.Settings.Hide();
                var model = subject.Manager.Model;
                // No selected head or project: this surface needs only an idle, isolated runtime, not NewThread's project picker.
                var scene = HydraCardCalibrationSite.Create(model, model.Runtime(Guid.NewGuid()));
                bitmap = scene.Bitmap;
                var window = new AvaloniaWindow
                {
                    WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false,
                    Width = 520, Height = 347, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
                    Content = scene.Host,
                };
                Themeless(window);
                native.Register(window);
                window.Show();
                return window;
            });
            var (origin, scale) = native.OnUi(() => (window.PointToScreen(default), window.RenderScaling));
            var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
            bool OwnsSamples() => new[] { new Point(26,26), new Point(490,26), new Point(270,61), new Point(439,61),
                new Point(90,120), new Point(260,170), new Point(270,266), new Point(80,80), new Point(490,320) }
                .All(p => WindowManagerNativeMethods.RootAt(new NativeWindowPoint(origin.X+(int)Math.Round(p.X*scale),origin.Y+(int)Math.Round(p.Y*scale))) == handle);
            ScreenGrab Grab() => ScreenGrab.Of(origin.X, origin.Y, (int)Math.Round(520 * scale), (int)Math.Round(347 * scale));
            using var reference = SkiaSharp.SKBitmap.Decode(HydraCardCalibrationSite.Backdrop);
            Assert.NotNull(reference);
            bool BackdropIsDrawn(ScreenGrab candidate) => new[] { new PixelPoint(26,26), new PixelPoint(490,26) }.All(p =>
            {
                var expected = reference.GetPixel(p.X,p.Y);
                var seen = candidate.At((int)Math.Round(p.X*scale),(int)Math.Round(p.Y*scale));
                return Math.Sqrt(Math.Pow(expected.Red-seen.R,2)+Math.Pow(expected.Green-seen.G,2)+Math.Pow(expected.Blue-seen.B,2)) < 8;
            });
            double Mean(ScreenGrab grab, int fromY, int toY)
            {
                var values = new List<double>();
                for (var y = (int)Math.Round(fromY * scale); y < (int)Math.Round(toY * scale); y++)
                    for (var x = (int)Math.Round(270 * scale); x < (int)Math.Round(440 * scale); x++) values.Add(grab.Luma(x, y));
                return values.Average();
            }
            // Wait for the known image outside the card, independent of all material targets: a foreign window's
            // text can accidentally average into the body's range while this window has not presented its scene yet.
            var deadline = DateTime.UtcNow.AddSeconds(8);
            var grab = Grab();
            while ((!OwnsSamples() || !BackdropIsDrawn(grab)) && DateTime.UtcNow < deadline) { Thread.Sleep(50); grab = Grab(); }
            Assert.True(OwnsSamples(), "NATIVE_PRECONDITION: another window covers the calibration samples");
            Assert.True(BackdropIsDrawn(grab), "NATIVE_PRECONDITION: the calibration window has not presented its known backdrop");
            var upper = Mean(grab, 106, 156);
            var lower = Mean(grab, 246, 286);
            var band = Mean(grab, 61, 62);
            // Screen pixels through the clear band/body join must remain continuous, as in Mac frame 18.
            double Step(bool vertical)
            {
                var values = new List<double>();
                var x0 = (int)Math.Round((vertical ? 105 : 90) * scale);
                var x1 = (int)Math.Round((vertical ? 380 : 115) * scale);
                var y0 = (int)Math.Round((vertical ? 90 : 105) * scale);
                var y1 = (int)Math.Round((vertical ? 115 : 290) * scale);
                for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++) values.Add(Math.Abs(grab.Luma(x + (vertical ? 0 : 1), y + (vertical ? 1 : 0)) - grab.Luma(x,y)));
                return values.Max();
            }
            var topStep = Step(true); var sideStep = Step(false);
            var cornerStep = Enumerable.Range(18,18).Select(d => Math.Abs(
                grab.Luma((int)Math.Round((60+d-1)*scale),(int)Math.Round((60+d+1)*scale)) -
                grab.Luma((int)Math.Round((60+d+1)*scale),(int)Math.Round((60+d-1)*scale)))).Max();
            var folder = HydraCardCalibrationSite.Evidence;
            Directory.CreateDirectory(folder);
            grab.SavePng(Path.Combine(folder, "hydra18-native-real-site.png"));
            File.WriteAllText(Path.Combine(folder, "hydra18-native-real-site.txt"), $"scale {scale}; upper {upper:F3}; lower {lower:F3}; band {band:F3}; top step {topStep:F3}; side step {sideStep:F3}; corner step {cornerStep:F3}{Environment.NewLine}");
            Assert.InRange(upper, 43.36 - 2.5, 43.36 + 2.5);
            Assert.InRange(lower, 42.70 - 2.5, 42.70 + 2.5);
            Assert.InRange(band, 43.99 - 3, 43.99 + 3);
            Assert.InRange(Math.Abs(upper - lower), 0, 2);
            Assert.InRange(topStep, 0, 3);
            Assert.InRange(sideStep, 0, 3);
            Assert.InRange(cornerStep, 0, 4);
            GC.KeepAlive(window);
        }
        finally { native.OnUi(() => { window?.Close(); Marrow.UI.Theme.ThemeManager.Current = previous; }); bitmap?.Dispose(); }
    }, factType: typeof(GlassMaterialNativeTests));
}
