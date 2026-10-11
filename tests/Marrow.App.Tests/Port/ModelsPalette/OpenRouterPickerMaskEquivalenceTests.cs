using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Tests.Fixtures;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.ModelsPalette;

// ED-NEW-lean-19 (lean pass L19): the picker keeps its opacity mask while the edge fades stand still, and every frame draws the same mask
// as a mask made on every call (the code before L19). A scripted run scrolls into the middle (both fades in), to the end (the bottom fade
// leaves), back up and to the top, and searches (the list's height folds), with extra ScrollChanged events on every frame; the mask's
// stops are recorded at 48 frames with the reuse on and off and must be identical.
[Collection("Headless")]
public sealed class OpenRouterPickerMaskEquivalenceTests(HeadlessSession headless)
{
    private static List<string> Run(bool reuse, out int masksMade)
    {
        var previous = OpenRouterPicker.ReuseMask;
        OpenRouterPicker.ReuseMask = reuse;
        try
        {
            using var platform = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
            using var runtime = new TestRuntime(); var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
            var picker = new OpenRouterPicker(Enumerable.Range(0, 30).Select(i => new PickerModel("acme/" + i, "Model " + i, i < 15 ? "Acme" : "Beta", "#10AA60")).ToArray(), "acme/0", _ => { }, () => { });
            var window = new Avalonia.Controls.Window { Content = picker }; window.Show();
            try
            {
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var scroll = picker.GetVisualDescendants().OfType<ScrollViewer>().First();
                var masks = new HashSet<IBrush>(ReferenceEqualityComparer.Instance);
                var record = new List<string>();
                var args = new ScrollChangedEventArgs(default, new Vector(0, 1), default);
                for (var frame = 0; frame < 48; frame++)
                {
                    switch (frame)
                    {
                        case 2: scroll.Offset = new Vector(0, 300); break;
                        case 14: scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height); break;
                        case 24: scroll.Offset = new Vector(0, 150); break;
                        case 30: scroll.Offset = new Vector(0, 0); break;
                        case 38: picker.Search.Text = "model 1"; break;
                    }
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    args.Handled = false; scroll.RaiseEvent(args);
                    runtime.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); Dispatcher.UIThread.RunJobs();
                    var mask = Assert.IsType<LinearGradientBrush>(scroll.OpacityMask);
                    masks.Add(mask);
                    record.Add($"{mask.StartPoint} {mask.EndPoint} " + string.Join(" ", mask.GradientStops.Select(stop => $"{stop.Color}@{stop.Offset:R}")));
                }
                masksMade = masks.Count;
                return record;
            }
            finally { window.Close(); }
        }
        finally { OpenRouterPicker.ReuseMask = previous; }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-19")]
    public void Every_frame_draws_the_mask_a_mask_made_on_every_call_draws() => headless.OnUiThread(() =>
    {
        var plain = Run(reuse: false, out var plainMasks);
        var kept = Run(reuse: true, out var keptMasks);
        Assert.Equal(plain, kept);
        Assert.True(plain.Distinct().Count() > 4, "the run moves the fades"); // the script really changes the mask
        Assert.True(keptMasks < plainMasks, $"the mask is kept while the fades stand ({keptMasks} masks against {plainMasks})");
    });
}
