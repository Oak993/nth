// PORT-ONLY. Text is drawn greyscale everywhere, on every surface (OD-0099 Q-TEXT A; ED-NEW-text-greyscale).
//
// SOURCE FACT: the macOS app draws all its text with greyscale antialiasing, no subpixel (LCD) colour fringes. ENGINEERING
// DECISION: Avalonia 12.1.2 asks Skia for subpixel antialiased text by default (TextRenderingMode.Unspecified resolves to
// SubpixelAntialias; it is only actually drawn where the layer under the text is opaque, so the same text looked fringed
// on a window's opaque layer and grey inside an opacity or glass layer). TextOptions is not a styled property in 12.1.2: a
// Visual holds a plain TextOptions value (set by TextOptions.SetTextRenderingMode / SetTextHintingMode) and the
// compositor merges each visual's value with its parent's (TextOptions.MergeWith, unspecified inherits), so setting it on
// the TopLevel reaches every Avalonia-drawn text below it: TextBlock, FormattedText through DrawingContext.DrawText, the
// terminal surface, and glyph runs. Windows, popups (PopupRoot) and every other TopLevel get it the moment the application's
// styles apply to them, before their first frame, through a style that sets this class's marker property and a handler of
// the marker's change. Hinting is None: CoreText does no grid fitting, and a hinted face snaps stems and heights to the
// pixel grid, which alters the glyph shapes and advances the reference frames show; Slight would still snap vertically.
// The text the port draws itself through SkiaSharp states greyscale edging on its own SKFont (TaskbarBadge.cs), and a
// source test keeps every SKFont from asking for LCD edging.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. The one place that sets how Avalonia rasterizes text.</summary>
public static class GreyscaleText
{
    /// <summary>Greyscale antialiasing: smooths edges with coverage only, never with the R, G and B subpixels.</summary>
    public const TextRenderingMode RenderingMode = TextRenderingMode.Antialias;

    /// <summary>No hinting: outlines are scaled only, as CoreText draws them.</summary>
    public const TextHintingMode HintingMode = TextHintingMode.None;

    /// <summary>Set by the application's style on every TopLevel; its change applies the text options.</summary>
    public static readonly AttachedProperty<bool> AppliedProperty =
        AvaloniaProperty.RegisterAttached<TopLevel, TopLevel, bool>("Applied");

    static GreyscaleText() =>
        AppliedProperty.Changed.AddClassHandler<TopLevel>((topLevel, e) =>
        {
            if (e.NewValue is true) Apply(topLevel);
        });

    /// <summary>Sets greyscale antialiasing and no hinting on <paramref name="root"/>; everything drawn below inherits it.</summary>
    public static void Apply(Visual root)
    {
        TextOptions.SetTextRenderingMode(root, RenderingMode);
        TextOptions.SetTextHintingMode(root, HintingMode);
    }

    /// <summary>Gives every TopLevel the mode.</summary>
    public static void Register(Application application)
    {
        _ = AppliedProperty; // runs the static constructor, which hooks the change handler
        application.Styles.Add(new Style(selector => selector.Is<TopLevel>())
        {
            Setters = { new Setter(AppliedProperty, true) },
        });
    }
}
