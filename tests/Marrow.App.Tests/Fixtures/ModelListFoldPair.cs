using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Views.Composer;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-stutterfix-8: two model lists side by side in one window, searched in the same frame; Plain folds every row (the fold before the
// change), Held holds the rows its viewport can never show. Each frame steps both and asserts the viewports show the same thing.
internal sealed class ModelListFoldPair(EffortListEnv env, Avalonia.Controls.Window window, ModelList plain, ModelList held)
{
    public readonly ModelList Plain = plain, Held = held;

    public static ScrollViewer Scroll(Control list) => Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
    private static TextBox Field(Control list) => Assert.Single(list.GetVisualDescendants().OfType<TextBox>());

    // The rows the viewport shows: where, how tall, how opaque; with the list's extent, viewport and offset.
    public static string Seen(Control list)
    {
        var scroll = Scroll(list); var content = (Visual)scroll.Content!;
        var shown = Models(list).Where(r => r.IsEffectivelyVisible).Select(r => (Row: r, Y: r.TranslatePoint(default, content)!.Value.Y))
            .Where(x => x.Y < scroll.Offset.Y + scroll.Viewport.Height && x.Y + x.Row.Bounds.Height > scroll.Offset.Y)
            .Select(x => FormattableString.Invariant($"{Name(x.Row)}@{x.Y:R}+{x.Row.Bounds.Height:R}/{x.Row.Opacity:R}"));
        return FormattableString.Invariant($"extent {scroll.Extent.Height:R} viewport {scroll.Viewport.Height:R} offset {scroll.Offset.Y:R} | ") + string.Join(" ", shown);
    }

    private static byte[] Pixels(Control list)
    {
        var scroll = Scroll(list);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(scroll.Bounds.Width), (int)Math.Ceiling(scroll.Bounds.Height)));
        bitmap.Render(scroll);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); return stream.ToArray();
    }

    public void Search(string text)
    {
        try
        {
            ModelListSearch.HoldUnseenRows = false; Field(Plain).Text = text; Dispatcher.UIThread.RunJobs();
            ModelListSearch.HoldUnseenRows = true; Field(Held).Text = text; Dispatcher.UIThread.RunJobs();
        }
        finally { ModelListSearch.HoldUnseenRows = true; }
        window.UpdateLayout();
    }

    public void Key(Key key)
    {
        foreach (var list in new[] { Plain, Held }) Field(list).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        window.UpdateLayout();
    }

    // One frame: both viewports show the same rows and pixels. Returns how many rows each list resized or hid in it.
    public (int Plain, int Held) Frame()
    {
        var before = (Heights(Plain), Heights(Held));
        env.Step(16); window.UpdateLayout();
        Assert.Equal(Seen(Plain), Seen(Held));
        Assert.Equal(Pixels(Plain), Pixels(Held));
        return (Changed(before.Item1, Heights(Plain)), Changed(before.Item2, Heights(Held)));
    }

    private static double[] Heights(Control list) => Models(list).Select(r => r.IsVisible ? r.Height : -1).ToArray();
    private static int Changed(double[] a, double[] b) => a.Zip(b).Count(x => x.First != x.Second);
}
