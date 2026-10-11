// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-settings; PLAN unit U5). The design's lit flame (phoenix-designs.html `.flame` with
// --heat 1: the outer flame in --ember-outer #ff6a1f over the cold silhouette, the core in --ember-core #ffd57e, a soft glow in
// --ember-glow), drawn from the design's own 24 x 24 paths. Settings › Ignis's sidebar row and the Updates row's mark.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.App.Port.Ignis.Settings;

public sealed class IgnisFlameMark : Control
{
    internal const string OuterPath = "M12.6 2.2c.5 2.9-1.1 4.7-2.8 6.5C8.2 10.4 6.5 12.3 6.5 15.2c0 3.5 2.5 6.3 5.6 6.3s5.9-2.6 5.9-6.2c0-2.4-1.1-4.2-2.3-5.5.1 1.4-.4 2.6-1.4 3.1.4-3.5-.4-7.7-1.7-10.7z";
    internal const string CorePath = "M12.3 12.2c.1 1.5-.8 2.3-1.5 3.1-.5.6-.9 1.3-.9 2.2 0 1.6 1.1 2.7 2.3 2.7s2.4-1.1 2.4-2.6c0-1.1-.6-2-1.2-2.6 0 .7-.3 1.2-.8 1.4.1-1.6-.1-3-.3-4.2z";

    /// <summary>--ember-outer.</summary>
    public static readonly Color Outer = Color.FromRgb(0xff, 0x6a, 0x1f);
    /// <summary>--ember-core.</summary>
    public static readonly Color Core = Color.FromRgb(0xff, 0xd5, 0x7e);

    private static readonly Geometry OuterGeometry = StreamGeometry.Parse(OuterPath);
    private static readonly Geometry CoreGeometry = StreamGeometry.Parse(CorePath);
    private static readonly IBrush OuterBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Outer);
    private static readonly IBrush CoreBrush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Core);

    // ED-NEW-ignismono-1: the sidebar draws the flame in the other symbols' silver (the core cut out of the outer flame, no glow).
    private static readonly Geometry MonoGeometry = new CombinedGeometry(GeometryCombineMode.Exclude, OuterGeometry, CoreGeometry);
    private readonly IBrush? mono;

    public IgnisFlameMark() : this(null) { }

    /// <param name="monochrome">The brush of a one-colour flame (the Settings sidebar), or null for the design's lit flame.</param>
    public IgnisFlameMark(IBrush? monochrome)
    {
        mono = monochrome;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // aria-hidden: the row's text names it
        // The design's drop-shadow(0 0 4px var(--ember-glow)) at heat 1.
        if (mono is null) Effect = new DropShadowEffect { OffsetX = 0, OffsetY = 0, BlurRadius = 4, Color = Color.FromArgb(0x8c, 0xff, 0x6a, 0x1f) };
    }

    internal bool IsMonochrome => mono is not null;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsNaN(Width) ? 16 : Width, double.IsNaN(Height) ? 16 : Height);

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;
        var scale = side / 24;
        var offset = new Point((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            if (mono is not null) { context.DrawGeometry(mono, null, MonoGeometry); return; }
            context.DrawGeometry(OuterBrush, null, OuterGeometry);
            context.DrawGeometry(CoreBrush, null, CoreGeometry);
        }
    }
}
