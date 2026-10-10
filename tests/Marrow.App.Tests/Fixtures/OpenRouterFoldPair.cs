using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.ModelsPalette;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-stutterfix-8: two OpenRouter pickers of 486 models over six makers side by side in one window, searched in the same frame; Plain
// folds every item (the fold before the change), Held holds the items its viewport can never show. Each frame steps both and asserts the
// viewports show the same thing.
internal sealed class OpenRouterFoldPair : IDisposable
{
    public const int Count = 486;
    private static readonly string[] Makers = ["Acme", "Bolt", "Cirrus", "Delta", "Echo", "Fable"];
    private readonly TestRuntime runtime = new();
    private readonly ManualFrameSource frames = new();
    private readonly IDisposable source;
    private readonly Avalonia.Controls.Window window;
    public readonly OpenRouterPicker Plain, Held;

    public OpenRouterFoldPair()
    {
        source = FrameClock.Source.Override(frames);
        Plain = Make(); Held = Make();
        window = new Avalonia.Controls.Window { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Plain, Held } }, Width = 680, Height = 800 };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        for (var i = 0; i < 60; i++) Step(); // the deferred rows come in
        Assert.Equal(Count, Items(Plain).OfType<PickerRow>().Count());
        Assert.Equal(Count, Items(Held).OfType<PickerRow>().Count());
    }

    private static OpenRouterPicker Make() => new(Enumerable.Range(0, Count).Select(i => new PickerModel($"{Makers[i * 6 / Count].ToLowerInvariant()}/m{i}", "Model " + i, Makers[i * 6 / Count], "#10AA60")).ToArray(),
        "acme/m0", _ => { }, () => { });

    private void Step() { runtime.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    public static ScrollViewer Scroll(OpenRouterPicker picker) => Assert.Single(picker.GetVisualDescendants().OfType<ScrollViewer>());

    // The list's groups and rows (the empty note last), in list order; the spacer is none of them.
    public static PickerFold[] Items(OpenRouterPicker picker) => ((Panel)Scroll(picker).Content!).Children.OfType<PickerFold>().ToArray();

    private static string Id(PickerFold item, int index) => item is PickerRow row ? row.Model.Name : "item " + index;
    private static double Scale(PickerFold item) => item.Child?.RenderTransform is ScaleTransform t ? t.ScaleX : 1;

    // The items the viewport shows: where, how tall, how opaque, how scaled; with the list's extent, viewport and offset.
    public static string Seen(OpenRouterPicker picker)
    {
        var scroll = Scroll(picker); var content = (Visual)scroll.Content!;
        var shown = Items(picker).Select((item, index) => (Item: item, Index: index)).Where(x => x.Item.IsEffectivelyVisible)
            .Select(x => (x.Item, x.Index, Y: x.Item.TranslatePoint(default, content)!.Value.Y))
            .Where(x => x.Y < scroll.Offset.Y + scroll.Viewport.Height && x.Y + x.Item.Bounds.Height > scroll.Offset.Y)
            .Select(x => FormattableString.Invariant($"{Id(x.Item, x.Index)}@{x.Y:R}+{x.Item.Bounds.Height:R}/{x.Item.Opacity:R}x{Scale(x.Item):R}"));
        return FormattableString.Invariant($"extent {scroll.Extent.Height:R} viewport {scroll.Viewport.Height:R} offset {scroll.Offset.Y:R} | ") + string.Join(" ", shown);
    }

    private static byte[] Pixels(OpenRouterPicker picker)
    {
        var scroll = Scroll(picker);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(scroll.Bounds.Width), (int)Math.Ceiling(scroll.Bounds.Height)));
        bitmap.Render(scroll);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); return stream.ToArray();
    }

    public void Search(string text)
    {
        try
        {
            OpenRouterPicker.HoldUnseenRows = false; Plain.Search.Text = text; Dispatcher.UIThread.RunJobs();
            OpenRouterPicker.HoldUnseenRows = true; Held.Search.Text = text; Dispatcher.UIThread.RunJobs();
        }
        finally { OpenRouterPicker.HoldUnseenRows = true; }
        window.UpdateLayout();
    }

    public void Key(Key key)
    {
        foreach (var picker in new[] { Plain, Held }) picker.Search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        window.UpdateLayout();
    }

    public void ScrollTo(double y)
    {
        foreach (var picker in new[] { Plain, Held }) Scroll(picker).Offset = new Vector(0, y);
        window.UpdateLayout();
    }

    // One frame: both viewports show the same items and pixels. Returns how many items each list resized or hid in it.
    public (int Plain, int Held) Frame()
    {
        var before = (Heights(Plain), Heights(Held));
        Step();
        Assert.Equal(Seen(Plain), Seen(Held));
        Assert.Equal(Pixels(Plain), Pixels(Held));
        return (Changed(before.Item1, Heights(Plain)), Changed(before.Item2, Heights(Held)));
    }

    // Every item, seen or not, where the plain fold has it.
    public void Same()
    {
        var plain = Items(Plain); var held = Items(Held);
        Assert.Equal(plain.Length, held.Length);
        for (var i = 0; i < plain.Length; i++)
        {
            Assert.Equal(Id(plain[i], i), Id(held[i], i));
            Assert.False(held[i].Held);
            Assert.Equal((plain[i].FoldAmount, plain[i].FoldOpacity, plain[i].Opacity, plain[i].IsVisible, plain[i].Bounds, Scale(plain[i])),
                (held[i].FoldAmount, held[i].FoldOpacity, held[i].Opacity, held[i].IsVisible, held[i].Bounds, Scale(held[i])));
        }
        Assert.Equal(((Panel)Scroll(Plain).Content!).Children.Count, ((Panel)Scroll(Held).Content!).Children.Count); // the spacer has gone
    }

    private static double[] Heights(OpenRouterPicker picker) => Items(picker).Select(r => r.IsVisible ? r.Bounds.Height : -1).ToArray();
    private static int Changed(double[] a, double[] b) => a.Zip(b).Count(x => x.First != x.Second);

    public void Dispose() { window.Close(); source.Dispose(); runtime.Dispose(); }
}
