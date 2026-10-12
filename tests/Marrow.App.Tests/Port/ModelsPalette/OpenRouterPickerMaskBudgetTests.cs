using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.ModelsPalette;

// ED-NEW-lean-19 (lean pass L19): a scroll that leaves the picker's top and bottom fades where they stand makes no new opacity
// mask. Measured in a Debug build on the headless UI thread: the bytes the UI thread allocates for one ScrollChanged raised on the
// list once both fades have settled in the middle of a long list (the routed event's own args are made once and reused).
[Collection("Headless")]
public sealed class OpenRouterPickerMaskBudgetTests(HeadlessSession headless)
{
    // Measured (Debug): 17,048 B per ScrollChanged on the unchanged code (a new gradient mask and two curves every time; limit then 25,600 B,
    // 1.5x); 88 B after L19, all of it Avalonia's route (the same event on a plain ScrollViewer with two empty handlers: 88 B). Limit 1.3x.
    private const double BytesPerScrollLimit = 115;

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void A_scroll_that_leaves_the_fades_unchanged_allocates_within_budget() => headless.OnUiThread(() =>
    {
        using var platform = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        using var runtime = new TestRuntime(); var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var picker = new OpenRouterPicker(Enumerable.Range(0, 30).Select(i => new PickerModel("acme/" + i, "Model " + i, "Acme", "#10AA60")).ToArray(), "acme/0", _ => { }, () => { });
        var window = new Avalonia.Controls.Window { Content = picker }; window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var scroll = picker.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, 300); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 40; i++) { runtime.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); Dispatcher.UIThread.RunJobs(); }
            Assert.InRange(scroll.Offset.Y, 2, scroll.Extent.Height - scroll.Viewport.Height - 2); // both fades show
            var args = new ScrollChangedEventArgs(default, new Vector(0, 1), default);
            void Raise() { args.Handled = false; scroll.RaiseEvent(args); }
            for (var i = 0; i < 60; i++) Raise(); // warm
            const int count = 200;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < count; i++) Raise();
            var perScroll = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)count;
            // for the message: the same event raised on a plain ScrollViewer as deep in a window, heard by two handlers that do nothing (the
            // picker has two): Avalonia's route alone
            var plain = new ScrollViewer();
            plain.ScrollChanged += (_, _) => { }; plain.ScrollChanged += (_, _) => { };
            var plainWindow = new Avalonia.Controls.Window { Content = new Decorator { Child = new Border { Child = new Border { Child = new StackPanel { Children = { plain } } } } } }; plainWindow.Show();
            try
            {
                void RaisePlain() { args.Handled = false; plain.RaiseEvent(args); }
                for (var i = 0; i < 60; i++) RaisePlain();
                var plainBefore = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < count; i++) RaisePlain();
                var route = (GC.GetAllocatedBytesForCurrentThread() - plainBefore) / (double)count;
                Assert.True(perScroll <= BytesPerScrollLimit, $"one ScrollChanged with unchanged fades allocates {perScroll:F1} B (limit {BytesPerScrollLimit} B; the route alone {route:F1} B)");
                Assert.True(perScroll - route <= 0, $"the picker's own share of one ScrollChanged with unchanged fades is {perScroll - route:F1} B (target 0)");
            }
            finally { plainWindow.Close(); }
        }
        finally { window.Close(); }
    });
}
