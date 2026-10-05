// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the `x` that cancels a model download (DESIGN gen.py:456-459 .mx, voice.js progress cell).
using Avalonia;
using Avalonia.Media;

namespace Marrow.UI.Port.Settings;

/// <summary>The 20 pt round `x` of a downloading row: two strokes of 1.4 pt with round caps in an 8 pt box, named `Cancel the download`.</summary>
public sealed class VoiceCancelButton : VoiceGlyphButton
{
    public VoiceCancelButton() : base("Cancel the download")
    {
    }

    protected override void DrawGlyph(DrawingContext context, Color colour)
    {
        var pen = new Pen(new SolidColorBrush(colour), 1.4, lineCap: PenLineCap.Round);
        context.DrawLine(pen, new Point(7.5, 7.5), new Point(12.5, 12.5));
        context.DrawLine(pen, new Point(12.5, 7.5), new Point(7.5, 12.5));
    }
}
