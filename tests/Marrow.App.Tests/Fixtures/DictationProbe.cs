using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// VT-11 test fixture: the parts of a composer the dictation tests look at, and its pixels. A frame is read as the rendered window's bytes (the headless surface is 4 bytes a pixel); two
// frames are compared by the sum of their per-pixel differences inside a region, so no assumption is made about the colour behind the text.
internal static class DictationProbe
{
    public static DictationLayer Layer(ComposerView view) => LogicalQuery.SelfAndDescendants<DictationLayer>(view).Single();

    /// <summary>Takes the keyboard from the editor (to the orb's button), so it draws no caret in the pictures.</summary>
    public static void Blur(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceOrbButton>(view).First().Focus();

    public static ComposerNSTextView Box(ComposerView view) => LogicalQuery.SelfAndDescendants<ComposerNSTextView>(view).Single();

    /// <summary>The editor's text area in window pixels (its box).</summary>
    public static PixelRect Region(ComposerNSTextView box, AvaloniaWindow window)
    {
        var origin = box.TranslatePoint(default, window) ?? throw new Xunit.Sdk.XunitException("the editor is not in the window");
        return new PixelRect((int)Math.Floor(origin.X), (int)Math.Floor(origin.Y), (int)Math.Ceiling(box.Bounds.Width), (int)Math.Ceiling(Math.Max(box.Bounds.Height, 24)));
    }

    /// <summary>The rendered window as bytes, row by row (stride is the first number returned).</summary>
    public static (int Stride, byte[] Bytes, PixelSize Size) Frame(AvaloniaWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return (buffer.RowBytes, bytes, buffer.Size);
    }

    /// <summary>The sum, over <paramref name="region"/>, of the largest channel difference between two frames, in units of a full-scale pixel.</summary>
    public static double Difference((int Stride, byte[] Bytes, PixelSize Size) frame, (int Stride, byte[] Bytes, PixelSize Size) other, PixelRect region)
    {
        double sum = 0;
        var top = Math.Max(0, region.Y);
        var bottom = Math.Min(frame.Size.Height, region.Bottom);
        var left = Math.Max(0, region.X);
        var right = Math.Min(frame.Size.Width, region.Right);
        for (var y = top; y < bottom; y++)
        for (var x = left; x < right; x++)
        {
            var at = y * frame.Stride + x * 4;
            var most = 0;
            for (var c = 0; c < 3; c++) most = Math.Max(most, Math.Abs(frame.Bytes[at + c] - other.Bytes[at + c]));
            sum += most / 255.0;
        }
        return sum;
    }

    /// <summary>
    /// Runs frames until the layer has handed the text back to the editor, and returns the last frame the layer drew and the first the editor drew (null when it never did, or did on the
    /// first frame). Everything else in the region must be still for these to compare: the tests clear the editor's focus so it draws no caret.
    /// </summary>
    public static ((int Stride, byte[] Bytes, PixelSize Size)? Layer, (int Stride, byte[] Bytes, PixelSize Size)? Editor) HandOver(VoiceComposerRig rig, AvaloniaWindow window,
        DictationLayer layer, double frame = 1.0 / 60, int limit = 1500)
    {
        (int Stride, byte[] Bytes, PixelSize Size)? last = null;
        for (var i = 0; i < limit; i++)
        {
            rig.Elapse(window, frame);
            if (rig.Main.OnMain(() => layer.HandedOver)) return (last, last is null ? null : Frame(window));
            last = Frame(window);
        }
        return (last, null);
    }

    /// <summary>True when the two frames are the same bytes everywhere inside <paramref name="region"/>.</summary>
    public static bool Same((int Stride, byte[] Bytes, PixelSize Size) frame, (int Stride, byte[] Bytes, PixelSize Size) other, PixelRect region)
    {
        for (var y = Math.Max(0, region.Y); y < Math.Min(frame.Size.Height, region.Bottom); y++)
        for (var x = Math.Max(0, region.X); x < Math.Min(frame.Size.Width, region.Right); x++)
        {
            var at = y * frame.Stride + x * 4;
            for (var c = 0; c < 4; c++)
                if (frame.Bytes[at + c] != other.Bytes[at + c]) return false;
        }
        return true;
    }
}
