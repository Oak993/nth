using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Marrow.App.Views.Composer;
using Marrow.Services.Store;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Hosts one ComposerTextView the way ComposerView.swift:275-288 does: a text binding, a height binding, a
/// placeholder, a controller and the three callbacks, inside a window with a white background and one other focusable
/// control. Records every onKey call (answering with <see cref="Answer"/>), every onAttach list and every height write.
/// The composer sits at the window's top-left corner, so window pixels and composer points coincide at scale 1.
/// </summary>
public sealed class ComposerTextViewHarness
{
    public const double ComposerHeight = 120;

    public ComposerTextViewHarness(string text = "", string placeholder = "", bool takesFocusOnAppear = true,
        double width = 400, double height = 0, bool show = true)
    {
        Text = text;
        Height = height;
        View = new ComposerTextView(
            text: new Binding<string>(() => Text, value => Text = value),
            height: new Binding<double>(() => Height, value => { Height = value; HeightWrites.Add(value); }),
            placeholder: placeholder,
            controller: Controller,
            onKey: key => { Keys.Add(key); return Answer(key); },
            onAttach: sources => Attached.Add(sources.ToList()),
            onCursorChange: _ => { },
            takesFocusOnAppear: takesFocusOnAppear)
        {
            Width = width,
            Height = ComposerHeight,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        Other = new Button { Content = "Other", Focusable = true };
        var stack = new StackPanel();
        stack.Children.Add(View);
        stack.Children.Add(Other);
        Window = new Avalonia.Controls.Window { Width = 1200, Height = 400, Background = Brushes.White, Content = stack };
        TextView = View.GetLogicalDescendants().OfType<ComposerNSTextView>().Single();
        if (!show) return;
        Window.Show();
        Pump();
    }

    public string Text { get; set; }

    public double Height { get; set; }

    public List<double> HeightWrites { get; } = [];

    public List<ComposerKey> Keys { get; } = [];

    public Func<ComposerKey, bool> Answer { get; set; } = _ => true;

    public List<List<AttachmentSource>> Attached { get; } = [];

    public ComposerController Controller { get; } = new();

    public ComposerTextView View { get; }

    public ComposerNSTextView TextView { get; }

    public Button Other { get; }

    public Avalonia.Controls.Window Window { get; }

    /// <summary>Runs queued UI work (MainActor posts and spawns, layout, input) until <paramref name="until"/> holds, at most 200 rounds.</summary>
    public static void Pump(Func<bool>? until = null)
    {
        for (var round = 0; round < 200; round++)
        {
            Dispatcher.UIThread.RunJobs();
            if (until?.Invoke() == true) return;
        }
    }

    public void Layout()
    {
        Pump();
        Window.UpdateLayout();
        Pump();
    }

    public void Focus()
    {
        TextView.Focus();
        Pump();
    }

    public void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, physical, null);
        Window.KeyRelease(key, modifiers, physical, null);
        Pump();
    }

    public void Type(string text)
    {
        Window.KeyTextInput(text);
        Pump();
    }

    /// <summary>The file path each source carries, for a list of <c>.file(URL)</c> sources (Storage.swift:224).</summary>
    public static List<string> FilePaths(IEnumerable<AttachmentSource> sources) =>
        sources.Select(source => source is AttachmentSource.File(var url) ? url : throw new Xunit.Sdk.XunitException($"not a file source: {source}")).ToList();

    public IStorageFile StorageFile(string path)
    {
        var lookup = Window.StorageProvider.TryGetFileFromPathAsync(path);
        Pump(() => lookup.IsCompleted);
        return lookup.GetAwaiter().GetResult() ?? throw new Xunit.Sdk.XunitException($"no storage file for {path}");
    }

    public void SetClipboard(DataTransfer data)
    {
        var clipboard = Window.Clipboard ?? throw new Xunit.Sdk.XunitException("the headless window has no clipboard");
        var set = clipboard.SetDataAsync(data);
        Pump(() => set.IsCompleted);
        set.GetAwaiter().GetResult();
    }

    public void Drop(DataTransfer data, Point at)
    {
        Window.DragDrop(at, Avalonia.Input.Raw.RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        Window.DragDrop(at, Avalonia.Input.Raw.RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
        Window.DragDrop(at, Avalonia.Input.Raw.RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        Pump();
    }

    /// <summary>
    /// The height ComposerTextView.swift:362-374 computes for <paramref name="text"/> laid out in the text view's own
    /// font at <paramref name="width"/>: ceil(max(18, content height) + 2 * 2). The content height comes from an
    /// independent Avalonia TextLayout of the same text, font and width (the port's stand-in for TextKit's used rect).
    /// </summary>
    public double ExpectedHeight(string text, double width)
    {
        var typeface = new Typeface(TextView.FontFamily, TextView.FontStyle, TextView.FontWeight);
        using var layout = new TextLayout(text, typeface, TextView.FontSize, null, textWrapping: TextWrapping.Wrap, maxWidth: width);
        return Math.Ceiling(Math.Max(18, layout.Height) + 2 * 2);
    }

    /// <summary>Temporary files for paste and drop tests, in a fresh folder under MARROW_PORT_DATA_DIR (R-TEST-9).</summary>
    public static string[] TemporaryFiles(params string[] names)
    {
        var root = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(root)) throw new Xunit.Sdk.XunitException("MARROW_PORT_DATA_DIR must point at a throwaway folder (R-TEST-9)");
        var folder = Path.Join(root, "ComposerTextViewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return names.Select(name =>
        {
            var path = Path.Join(folder, name);
            File.WriteAllBytes(path, [0x78]);
            return path;
        }).ToArray();
    }
}

/// <summary>The popover seam of R-VIEW-9, recording each presentation instead of opening a Popup (headless cannot).</summary>
public sealed class PopoverRecorder : IPopoverPresenter
{
    public List<(Popup Popup, bool Presented)> Calls { get; } = [];

    public void SetPresented(Popup popup, bool presented) => Calls.Add((popup, presented));

    /// <summary>The size the hosted content lays out at, unconstrained: the size ComposerTextView.swift:71 frames it to.</summary>
    public static Size ContentSize(Popup popup)
    {
        var child = popup.Child ?? throw new Xunit.Sdk.XunitException("the popup hosts no content");
        child.Measure(Size.Infinity);
        return child.DesiredSize;
    }
}

/// <summary>
/// Ink measurement on a rendered headless frame: the bounding box of the pixels that differ from the background,
/// counting only pixels whose difference reaches 30 percent of the strongest one in the area, so a light placeholder
/// colour and a black reference give comparable boxes.
/// </summary>
public static class Ink
{
    public static PixelRect? Bounds(TopLevel window, Rect area)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        return Bounds(frame, area);
    }

    private static PixelRect? Bounds(WriteableBitmap frame, Rect area)
    {
        using var buffer = frame.Lock();
        var width = buffer.Size.Width;
        var height = buffer.Size.Height;
        var left = Math.Max(0, (int)Math.Floor(area.X));
        var top = Math.Max(0, (int)Math.Floor(area.Y));
        var right = Math.Min(width, (int)Math.Ceiling(area.Right));
        var bottom = Math.Min(height, (int)Math.Ceiling(area.Bottom));
        var row = new byte[buffer.RowBytes];
        var differences = new int[Math.Max(0, right - left) * Math.Max(0, bottom - top)];
        var strongest = 0;
        for (var y = top; y < bottom; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            for (var x = left; x < right; x++)
            {
                // White background: every colour channel is 255 wherever nothing is drawn (both BGRA and RGBA).
                var difference = 3 * 255 - row[x * 4] - row[x * 4 + 1] - row[x * 4 + 2];
                differences[(y - top) * (right - left) + (x - left)] = difference;
                strongest = Math.Max(strongest, difference);
            }
        }
        if (strongest == 0) return null;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                if (differences[(y - top) * (right - left) + (x - left)] * 10 < strongest * 3) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        return new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// The ink of <paramref name="text"/> drawn by a plain TextBlock in <paramref name="font"/>'s face and size with its
    /// top-left corner at <paramref name="origin"/>, in a window of the same size and background.
    /// </summary>
    public static PixelRect? Reference(string text, TemplatedControl font, Point origin, Size windowSize, Rect area)
    {
        var block = new TextBlock
        {
            Text = text,
            FontFamily = font.FontFamily,
            FontSize = font.FontSize,
            FontWeight = font.FontWeight,
            FontStyle = font.FontStyle,
            Foreground = Brushes.Black,
        };
        Canvas.SetLeft(block, origin.X);
        Canvas.SetTop(block, origin.Y);
        var canvas = new Canvas();
        canvas.Children.Add(block);
        var window = new Avalonia.Controls.Window { Width = windowSize.Width, Height = windowSize.Height, Background = Brushes.White, Content = canvas };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { return Bounds(window, area); }
        finally { window.Close(); }
    }

    public static void AssertClose(PixelRect? expected, PixelRect? actual, int tolerance = 1)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        var e = expected.Value;
        var a = actual.Value;
        Assert.True(Math.Abs(e.X - a.X) <= tolerance && Math.Abs(e.Y - a.Y) <= tolerance
                    && Math.Abs(e.Right - a.Right) <= tolerance && Math.Abs(e.Bottom - a.Bottom) <= tolerance,
            $"ink {a} differs from the reference {e} by more than {tolerance} px");
    }
}
