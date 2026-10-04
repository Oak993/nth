using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (scoped check of unit/blitz-frames at 2cc3021c0). PR-CHAT-171: the chrome row's GlassEffectContainer now sets SwiftGlass.SetContainerSpacing(2), so every glass
// piece of the row is drawn by ONE material visual behind the container (GlassMaterial.Synchronize), whose regions carry each piece's opacity and transform as they were
// when the container last synchronised. Before the unit each piece's material sat beside the piece, inside the soft-appear subtree, so the fade and the rise reached it.
// The container re-synchronises on a layout pass, on its own property changes and on a piece's own property changes, but the Hydra button's soft appear (ChatView.swift
// :1138-1140, Chrome.swift:933-960) moves only Opacity and a RenderTransform of an ancestor of the glass circle: no layout pass. These tests read the drawn regions
// (the container's last State) against the pieces as the frame shows them: mid-arrival and once settled the Hydra circle's glass must be drawn at the circle's own shown
// opacity and position (SwiftUI's glass fades and rises with its view).
[Collection("Headless")]
public sealed class ChatViewFramesReviewTests(HeadlessSession headless)
{
    private static readonly Type Material = typeof(SwiftGlass).Assembly.GetType("Marrow.UI.Port.GlassMaterial", throwOnError: true)!;

    // The container's drawn regions: (opacity, translation x, translation y) of each piece, as the material visual last received them.
    private static List<(double Opacity, double X, double Y)> Drawn(Control container)
    {
        var table = Material.GetField("attachments", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var args = new object?[] { container, null };
        Assert.True((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, args)!, "the container has a material attachment");
        var state = args[1]!.GetType().GetField("previous", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(args[1]);
        if (state is null) return [];
        var regions = (Array)state.GetType().GetProperty("Regions")!.GetValue(state)!;
        var drawn = new List<(double, double, double)>();
        foreach (var region in regions)
        {
            var transform = (Matrix)region.GetType().GetProperty("Transform")!.GetValue(region)!;
            drawn.Add(((double)region.GetType().GetProperty("Opacity")!.GetValue(region)!, transform.M31, transform.M32));
        }
        return drawn.OrderBy(r => r.Item2).ThenBy(r => r.Item3).ToList();
    }

    // What the frame shows: every glass piece of the row with its shown opacity (the product up to the container) and its position in the container.
    private static List<(double Opacity, double X, double Y)> Shown(Control container) =>
        container.GetVisualDescendants().OfType<Control>()
            .Where(c => SwiftGlass.GetEffect(c) is not null && !SwiftGlass.GetUsesDesktopBackdrop(c) && c.Bounds.Width > 0 && c.Bounds.Height > 0)
            .Select(c =>
            {
                var opacity = 1.0;
                for (Visual? node = c; node is not null && !ReferenceEquals(node, container); node = node.GetVisualParent()) opacity *= node.IsVisible ? node.Opacity : 0;
                var m = c.TransformToVisual(container)!.Value;
                return (opacity, m.M31, m.M32);
            })
            .Where(r => r.opacity > 0)
            .OrderBy(r => r.M31).ThenBy(r => r.M32).ToList();

    private static void Step(AppModelHarness h, AvaloniaWindow window, ManualTimeProvider time, ManualFrameSource frames, int milliseconds)
    {
        time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        // no forced layout pass: only what the app itself queues
        h.Main.RunUntilIdle();
        for (var pass = 0; pass < 3; pass++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    [Theory]
    [InlineData(100)] // mid-arrival on softAppear (.smooth(duration: 0.32))
    [InlineData(600)] // settled
    public void REVIEW_The_Hydra_buttons_glass_fades_and_rises_with_it_in_the_rows_one_glass_pass(int milliseconds)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (row, window) = h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                model.Settings.HydraEnabled = false;
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id);
                var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                var window = new AvaloniaWindow { Content = row, Width = 1400, Height = 300 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                return (row, window);
            });
            Step(h, window, time, frames, 0);
            Step(h, window, time, frames, 400);
            var container = h.Main.OnMain(() => Assert.Single(row.GetVisualDescendants().OfType<GlassEffectContainer>()));
            h.Main.OnMain(() => Assert.Equal(Shown(container), Drawn(container))); // at rest, before the arrival: the regions are the pieces
            var before = h.Main.OnMain(() => Shown(container).Count);
            h.Main.OnMain(() => model.Settings.HydraEnabled = true);
            Step(h, window, time, frames, 0);
            Step(h, window, time, frames, milliseconds);
            h.Main.OnMain(() =>
            {
                var shown = Shown(container); var drawn = Drawn(container);
                Assert.Equal(before + 1, shown.Count); // the Hydra circle is on screen
                Assert.True(shown.Count == drawn.Count && shown.Zip(drawn).All(p => Math.Abs(p.First.Opacity - p.Second.Opacity) < 0.02 && Math.Abs(p.First.X - p.Second.X) < 0.5 && Math.Abs(p.First.Y - p.Second.Y) < 0.5),
                    $"{milliseconds} ms: shown " + string.Join(" ", shown) + " | drawn " + string.Join(" ", drawn));
            });
        });
    }
}
