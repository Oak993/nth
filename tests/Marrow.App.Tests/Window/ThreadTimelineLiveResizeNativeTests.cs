using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for UI-CHAT-001's live-resize clause with the timeline shown (STOP App.Views.Chat.ThreadTimeline #9): OD-0013 (the edge strip must be fixed) is
// closed by OD-0017, `stretch` on every resizable window (ED-101), whose binding WindowManagerFactoryStretchTests proves; the residual it accepts (a one-frame DWM lag,
// about 1 resize step in 150 to 250 at probe P4's drag speed, and the moving grow-edge strip in 0.16 % of samples) is a named deviation. Here the real factory main
// window shows a real ThreadTimeline on a long thread, over an opaque fill that marks the app's layer, and its right edge is dragged by the system's pointer
// (SendInput), out and back, step by step: the screen just inside the window's moving edge shows the app's layer in every step but a residual within OD-0017's
// bound; the sizing loop is the window's live resize (WindowLiveResize, ThreadWindow.swift's), and once it ends the timeline is at its end with rows on screen.
// Needs a free interactive desktop; the runner takes native.lock around the run.
[Collection("WindowManagerNative")]
public sealed class ThreadTimelineLiveResizeNativeTests(WindowManagerNativeSession native)
{
    private static readonly Color Fill = Color.FromRgb(32, 96, 160);

    private static IEnumerable<TimelineEntry> Turns(int count)
    {
        var reply = string.Join(" ", Enumerable.Repeat("This answer is long enough to wrap across several lines in the column.", 8));
        for (var i = 0; i < count; i++)
        {
            var turn = Guid.NewGuid();
            yield return new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "Question number " + i + " about the layout." }), "user-" + i));
            yield return new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = reply }), "reply-" + i));
            yield return new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.TurnEnd(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = 2, FilesChanged = 0, Additions = 0, Deletions = 0 }), "end-" + i));
        }
    }

    // How far a block's mean colour is from the fill (sum of channel differences).
    private static double FromFill(ScreenGrab grab)
    {
        double r = 0, g = 0, b = 0;
        for (var y = 0; y < grab.Height; y++)
            for (var x = 0; x < grab.Width; x++) { var (pr, pg, pb) = grab.At(x, y); r += pr; g += pg; b += pb; }
        var n = grab.Width * grab.Height;
        return Math.Abs(r / n - Fill.R) + Math.Abs(g / n - Fill.G) + Math.Abs(b / n - Fill.B);
    }

    [Fact]
    [Trait("Req", "UI-CHAT-001")] // ThreadTimeline.swift:359-440 with OD-0017 (closes OD-0013), ED-101
    public void A_real_live_resize_with_the_timeline_shown_keeps_the_apps_layer_at_the_moving_edge_within_the_accepted_residual() => native.Run(subject =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var (view, runtime) = native.OnUi(() =>
        {
            subject.Settings.Hide();
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main));
            var model = new Marrow.App.AppModel();
            var id = Guid.NewGuid();
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(id), Storage.ThreadURL(id));
            DocumentPrefetch.Shared.Warm([id], urgent: true);
            var runtime = new ThreadRuntime(id, model);
            var view = new ThreadTimeline(runtime, new ChromeScrollModel(), new TimelineScrollState(), "Project", null, false, 480);
            var host = new Border { Background = new SolidColorBrush(Fill), Child = view };
            AppEnvironment.SetAppModel(host, model);
            subject.MainContent.Children.Add(host);
            subject.Main.Topmost = true;
            typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(Turns(30)));
            return (view, runtime);
        });
        subject.Focus(subject.Main, subject.MainProbe);
        Thread.Sleep(2500); // the history task, the timeline's hold, the layout settling
        ScrollViewer Scroller() => view.GetVisualDescendants().OfType<ScrollViewer>().First();
        bool AtEnd() => native.OnUi(() => Scroller().Extent.Height - (Scroller().Offset.Y + Scroller().Viewport.Height) < 2);
        bool RowsOnScreen() => native.OnUi(() =>
        {
            var scroller = Scroller();
            return view.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text)
                && text.TranslatePoint(default, scroller) is { } at && new Rect(at, text.Bounds.Size).Intersects(new Rect(scroller.Bounds.Size)));
        });
        N.Until(AtEnd, "the long thread did not open at its end", 5000);
        Assert.True(RowsOnScreen(), "no row on screen before the resize");

        var handle = subject.MainProbe.Handle;
        subject.Focus(subject.Main, subject.MainProbe); // the foreground again, right before the press
        using var pointer = new WindowManagerNativePointer(subject);
        var edge = WindowManagerNativePointer.RightEdge(subject.MainProbe, resizable: true);
        pointer.Down(subject.MainProbe, edge, N.HTRIGHT);
        N.Until(() => N.Gui(native.ThreadId).Capture == handle, "the resize did not capture");
        var samples = new List<double>();
        var widths = new List<int>();
        var startWidth = WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle).VisibleBounds!.Right - WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle).VisibleBounds!.Left;
        var resizing = false;
        try
        {
            // out by 240 px and back, 6 px a step at about 60 steps a second (P4's drag speed)
            var path = Enumerable.Range(1, 40).Select(step => edge.X + 6 * step).Concat(Enumerable.Range(1, 40).Select(step => edge.X + 240 - 6 * step)).ToArray();
            foreach (var x in path)
            {
                pointer.Move(subject.MainProbe, new NativeWindowPoint(x, edge.Y));
                Thread.Sleep(16);
                resizing |= subject.MainProbe.IsResizing;
                var visible = WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle).VisibleBounds!;
                var middle = visible.Top + (visible.Bottom - visible.Top) / 2;
                widths.Add(visible.Right - visible.Left);
                samples.Add(FromFill(ScreenGrab.Of(visible.Right - 7, middle - 40, 5, 80))); // just inside the moving edge
            }
        }
        finally { pointer.Up(subject.MainProbe, new NativeWindowPoint(edge.X, edge.Y)); }
        N.Until(() => !subject.MainProbe.IsResizing && N.Gui(native.ThreadId).Capture == 0, "the resize did not end");
        Assert.True(resizing, "the drag was not the window's live resize");
        Assert.True(widths.Max() - startWidth >= 200 && widths[^1] - startWidth <= 12, $"the window went from {startWidth} to {widths.Max()} and back to {widths[^1]} px wide");
        var strips = samples.Count(distance => distance > 40);
        Console.WriteLine($"ThreadTimelineLiveResizeNativeTests: {strips} of {samples.Count} steps showed something other than the app's layer at the moving edge (max distance {samples.Max():F1})");
        // OD-0017's accepted residual: the DWM type B frame about 1 step in 150 to 250, the grow-edge strip in 0.16 % of samples; 2 of 80 is the bound kept here
        Assert.True(strips <= 2, $"{strips} of {samples.Count} steps showed the window's backdrop at the moving edge instead of the app's layer");
        Thread.Sleep(800);
        N.Until(AtEnd, "after the resize the timeline is not at its end", 5000);
        Assert.True(RowsOnScreen(), "the pane is blank after the resize");
        GC.KeepAlive(runtime);
    }, factType: typeof(ThreadTimelineLiveResizeNativeTests));
}
