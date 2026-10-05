// PORT-ONLY, first-release UE-4 (OD-0100 place A). The little marks of a row's status, drawn as the picked design draws them (ue-page.css .okc .wrn .adot .qdot .rsi, a 16-unit box shown at 14 pt).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal enum UpdatesGlyphKind
{
    /// <summary>The green seal with a check: up to date, updated.</summary>
    Check,

    /// <summary>The accent dot: an update is available.</summary>
    AccentDot,

    /// <summary>The quiet dot: waiting, or checked only when updated.</summary>
    QuietDot,

    /// <summary>The arrow turning back on itself: restart to finish.</summary>
    Restart,

    /// <summary>The warning triangle: did not update.</summary>
    Warning,
}

internal sealed class UpdatesGlyph : Control
{
    public const double Size = 14;

    private static readonly Geometry CheckMark = Geometry.Parse("M4.9 8.2 7 10.2 11.1 5.8");
    private static readonly Geometry Triangle = Geometry.Parse("M8 1.4 15.2 14.2H.8Z");
    private static readonly Geometry Bang = Geometry.Parse("M8 6v3.8M8 11.9v.1");
    private static readonly Geometry RestartArc = Geometry.Parse("M13.2 8.4A5.2 5.2 0 1 1 11.5 4.3M12 1.6v3h-3");

    public UpdatesGlyph(UpdatesGlyphKind kind)
    {
        Kind = kind;
        Width = Height = Size;
        IsHitTestVisible = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public UpdatesGlyphKind Kind { get; }

    public override void Render(DrawingContext context)
    {
        var theme = ActualThemeVariant;
        if (Kind is UpdatesGlyphKind.AccentDot or UpdatesGlyphKind.QuietDot)
        {
            var accent = Kind == UpdatesGlyphKind.AccentDot;
            var color = accent ? Chrome.Accent.Resolve(theme) : SwiftColor.Tertiary.Resolve(theme);
            context.DrawEllipse(new SolidColorBrush(color), null, new Point(Size / 2, Size / 2), accent ? 3.5 : 3, accent ? 3.5 : 3);
            return;
        }
        using var scale = context.PushTransform(Matrix.CreateScale(Size / 16, Size / 16));
        switch (Kind)
        {
            case UpdatesGlyphKind.Check:
                context.DrawEllipse(new SolidColorBrush(Chrome.Success.Resolve(theme)), null, new Point(8, 8), 7, 7);
                context.DrawGeometry(null, new Pen(Brushes.White, 1.7, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), CheckMark);
                break;
            case UpdatesGlyphKind.Warning:
                context.DrawGeometry(new SolidColorBrush(Chrome.Warning.Resolve(theme)), null, Triangle);
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(0x1d, 0x13, 0x00)), 1.5, lineCap: PenLineCap.Round), Bang);
                break;
            case UpdatesGlyphKind.Restart:
                context.DrawGeometry(null, new Pen(new SolidColorBrush(Chrome.Accent.Resolve(theme)), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), RestartArc);
                break;
        }
    }
}
