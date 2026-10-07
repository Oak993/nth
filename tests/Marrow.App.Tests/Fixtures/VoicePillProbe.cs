using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Fixtures;

// VT-12 test fixture: the parts of a composer the pill, cross and question tests look at, a pill on its own in a window (so a test sets its inputs on a frame of its own choosing), the
// label measured independently of the production code, and a frame sampler.
internal static class VoicePillProbe
{
    public const double FrameTime = 1.0 / 60;

    public static VoicePill Pill(ComposerView view) => LogicalQuery.SelfAndDescendants<VoicePill>(view).Single();

    public static VoiceOrbSlot Slot(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();

    public static VoiceLanguageQuestion Question(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceLanguageQuestion>(view).Single();

    public static VoiceGlowLayer Glow(ComposerView view) => LogicalQuery.SelfAndDescendants<VoiceGlowLayer>(view).Single();

    /// <summary>The width a label takes: the design's canvas measure (V-12.2), rounded up. Written here, not taken from the pill, and from the face the pill's font names.</summary>
    public static double Measure(string text, double size, bool semibold)
    {
        var font = SwiftFont.SystemFont(size, semibold ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Regular).Resolve();
        using var layout = new TextLayout(text, new Typeface(font.FontFamily, font.FontStyle, font.FontWeight), size, null);
        return Math.Ceiling(layout.WidthIncludingTrailingWhitespace);
    }

    /// <summary>The pill's open width for a label by the formula of V-12.2.</summary>
    public static double OpenWidth(double height, string words, double size, string? name = null) =>
        height + 8 + Measure(words, size, semibold: true) + (name is null ? 0 : Measure(" " + name, size, semibold: false)) + 8 + 24 + 8;

    /// <summary>The pill's rectangle in the composer's coordinates.</summary>
    public static Rect PillRect(ComposerView view) => Frame(Pill(view), view);

    /// <summary>The orb slot's centre in the composer's coordinates (before any lift).</summary>
    public static Point OrbCentre(ComposerView view)
    {
        var slot = Slot(view);
        return slot.TranslatePoint(new Point(slot.Diameter / 2, slot.Diameter / 2), view)!.Value;
    }

    /// <summary>A pill of its own in a window, on the rig's clocks: a test sets its inputs when it likes.</summary>
    public static (VoicePill Pill, AvaloniaWindow Window) Standalone(VoiceComposerRig rig, VoiceListeningStyle style = VoiceListeningStyle.Pill, double width = 480)
    {
        var pill = rig.Main.OnMain(() => new VoicePill { Style = style });
        var host = rig.Main.OnMain(() => new Canvas { Width = width, Height = 200, Children = { pill } });
        Canvas.SetLeft(pill, 100);
        Canvas.SetTop(pill, 80);
        var window = rig.Main.OnMain(() => Show(host, rig.App, width));
        rig.PumpWindow(window);
        return (pill, window);
    }

    /// <summary>The value read after each of the next <paramref name="count"/> frames (frame 0 is the value before any).</summary>
    public static List<T> Frames<T>(VoiceComposerRig rig, AvaloniaWindow window, int count, Func<T> read)
    {
        var seen = new List<T> { rig.Main.OnMain(read) };
        for (var k = 1; k <= count; k++)
        {
            rig.Elapse(window, FrameTime);
            seen.Add(rig.Main.OnMain(read));
        }
        return seen;
    }

    /// <summary>Whole seconds of frames, nothing read.</summary>
    public static void Run(VoiceComposerRig rig, AvaloniaWindow window, double seconds)
    {
        var frames = (int)Math.Round(seconds / FrameTime);
        for (var k = 0; k < frames; k++) rig.Elapse(window, FrameTime);
    }
}
