using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/ChatView.swift (B3 52bc792), :1106-1174, :1120-1124 (GlassEffectContainer(spacing: 2) around the row's capsules:
// one glass pass for the row). PR-CHAT-171: the review census (ChatViewGlassPlacementReviewTests) proves the row's material is in the composition
// tree beside the container; this proves what it draws. Fine black and white stripes (six pixels, the GlassWindowNativeTests pattern) lie under the
// real chrome row once the scene has settled: along a line inside each capsule, above its glyphs, the stripes are frosted; the same line with the
// glass taken off shows them sharp through the capsule's own tint, so the frost is the material's and not an opaque fill.
[Collection("Headless")]
public sealed class ChatChromeRowGlassFrostTests(HeadlessSession headless)
{
    private static void Settle()
    {
        for (var pass = 0; pass < 8; pass++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    // The strongest channel's range (0 to 1) along a line of the frame.
    private static double Contrast(AvaloniaWindow window, int y, int from, int to) =>
        FramePixels.Levels(window, Enumerable.Range(from, to - from).Select(x => (x, y)).ToArray()) is var levels ? levels.Max() - levels.Min() : 0;

    // A line inside a capsule: 3 points below its top edge (above the 16 pt glyphs a 32 pt capsule centres), clear of its rounded ends; in a circle,
    // 16 points across, 5 points below its top (inside the circle and above its glyph).
    private static (int Y, int From, int To) Inside(Control piece, Visual root)
    {
        var at = piece.TranslatePoint(default, root)!.Value;
        var size = piece.Bounds.Size;
        return size.Width > size.Height + 8
            ? ((int)Math.Ceiling(at.Y + 3), (int)Math.Ceiling(at.X + size.Height / 2), (int)Math.Floor(at.X + size.Width - size.Height / 2))
            : ((int)Math.Ceiling(at.Y + 5), (int)Math.Ceiling(at.X + size.Width / 2 - 8), (int)Math.Floor(at.X + size.Width / 2 + 8));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-171")] // App/Views/Chat/ChatView.swift:1120-1124
    public void The_stripes_behind_the_chrome_rows_capsules_are_frosted_once_the_scene_settles_and_sharp_without_the_glass()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                model.Settings.HydraEnabled = true;
                model.Settings.HasSeenHydraIntro = true;
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id);
                var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel()) { VerticalAlignment = VerticalAlignment.Top };
                var scene = new Panel { Children = { new Stripes(), row } };
                var window = new AvaloniaWindow { Content = scene, Width = 1400, Height = 200 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Settle();

                var container = Assert.Single(row.GetVisualDescendants().OfType<GlassEffectContainer>());
                var pieces = container.GetVisualDescendants().OfType<Control>()
                    .Where(control => SwiftGlass.GetEffect(control) is not null && control.IsEffectivelyVisible && control.Bounds.Height >= 24)
                    .ToArray();
                Assert.True(pieces.Length >= 4, $"only {pieces.Length} glass pieces in the row"); // the threads, new chat, Hydra and lock circles, the toggles capsule, ... (ChatView.swift:1124-1174)
                // OD-0147: this measure is the release material's (its frost reaches 3 pt inside the edge); the shipped canvas keeps its outer rim clear
                // and is measured past it (the canvas case below), so the pieces are drawn here with the release look
                foreach (var piece in pieces) SwiftGlass.SetEffect(piece, SwiftGlass.GetEffect(piece)!.WithLook(GlassLook.Release));
                Settle();
                var lines = pieces.Select(piece => (piece, line: Inside(piece, window))).ToArray();
                // the instrument: the stripes are sharp below the row, where no glass is
                var below = (int)Math.Ceiling(row.TranslatePoint(new Point(0, row.Bounds.Height), window)!.Value.Y) + 20;
                Assert.True(Contrast(window, below, 100, 700) > 0.9, $"the bare stripes keep only {Contrast(window, below, 100, 700):F2}");
                foreach (var (piece, (y, from, to)) in lines)
                {
                    var frosted = Contrast(window, y, from, to);
                    Assert.True(frosted < 0.25, $"{piece.GetType().Name} at {piece.TranslatePoint(default, window)} ({piece.Bounds.Size}): the stripes behind it keep {frosted:F2} of their contrast");
                }

                // the control: the same lines without the glass (the container's pass and every piece's effect off) show the stripes through the capsules
                SwiftGlass.SetContainerSpacing(container, null);
                foreach (var piece in pieces) SwiftGlass.SetEffect(piece, null);
                Settle();
                foreach (var (piece, (y, from, to)) in lines)
                {
                    var bare = Contrast(window, y, from, to);
                    Assert.True(bare > 0.5, $"{piece.GetType().Name}: without the glass its line keeps only {bare:F2}, so the line does not measure the glass");
                }
            });
        });
    }

    // A line through a piece's middle: a capsule's straight run, or 16 points across a circle's centre.
    private static (int Y, int From, int To) Middle(Control piece, Visual root)
    {
        var at = piece.TranslatePoint(default, root)!.Value;
        var size = piece.Bounds.Size;
        var y = (int)Math.Round(at.Y + size.Height / 2);
        return size.Width > size.Height + 8
            ? (y, (int)Math.Ceiling(at.X + size.Height / 2), (int)Math.Floor(at.X + size.Width - size.Height / 2))
            : (y, (int)Math.Ceiling(at.X + size.Width / 2 - 8), (int)Math.Floor(at.X + size.Width / 2 + 8));
    }

    [Fact]
    [Trait("Decision", "OD-0147")] // the shipped canvas glass: the stripes behind each of the row's pieces are frosted through its middle (the same bound) and sharp there without the glass
    public void The_canvas_glass_of_the_chrome_rows_capsules_frosts_the_stripes_behind_their_middles()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                model.Settings.HydraEnabled = true;
                model.Settings.HasSeenHydraIntro = true;
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id);
                var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel()) { VerticalAlignment = VerticalAlignment.Top };
                var scene = new Panel { Children = { new Stripes(), row } };
                var window = new AvaloniaWindow { Content = scene, Width = 1400, Height = 200 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                Settle();

                var container = Assert.Single(row.GetVisualDescendants().OfType<GlassEffectContainer>());
                var pieces = container.GetVisualDescendants().OfType<Control>()
                    .Where(control => SwiftGlass.GetEffect(control) is not null && control.IsEffectivelyVisible && control.Bounds.Height >= 24)
                    .ToArray();
                Assert.True(pieces.Length >= 4, $"only {pieces.Length} glass pieces in the row");
                Assert.All(pieces, piece => Assert.NotNull(GlassLooks.Of(SwiftGlass.GetEffect(piece)!).Canvas)); // the shipped material
                // the pieces' glass alone: their glyphs and labels set aside, so a line through a piece's middle reads its glass
                GlassAloneScene.Hide(row, pieces);
                Settle();
                var lines = pieces.Select(piece => (piece, line: Middle(piece, window))).ToArray();
                var below = (int)Math.Ceiling(row.TranslatePoint(new Point(0, row.Bounds.Height), window)!.Value.Y) + 20;
                Assert.True(Contrast(window, below, 100, 700) > 0.9, $"the bare stripes keep only {Contrast(window, below, 100, 700):F2}");
                foreach (var (piece, (y, from, to)) in lines)
                {
                    var frosted = Contrast(window, y, from, to);
                    Assert.True(frosted < 0.25, $"{piece.GetType().Name} at {piece.TranslatePoint(default, window)} ({piece.Bounds.Size}): the stripes behind its middle keep {frosted:F2} of their contrast");
                }

                SwiftGlass.SetContainerSpacing(container, null);
                foreach (var piece in pieces) SwiftGlass.SetEffect(piece, null);
                Settle();
                foreach (var (piece, (y, from, to)) in lines)
                {
                    var bare = Contrast(window, y, from, to);
                    Assert.True(bare > 0.5, $"{piece.GetType().Name}: without the glass its middle keeps only {bare:F2}, so the line does not measure the glass");
                }
            });
        });
    }
}
