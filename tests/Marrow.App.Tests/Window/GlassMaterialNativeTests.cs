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
}
