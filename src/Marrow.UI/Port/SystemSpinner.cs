// PORT-ONLY. SwiftUI's `ProgressView()` (indeterminate, .controlSize(.small) at the port's sites) is AppKit's spinning
// NSProgressIndicator: the system's own busy indicator. The port's sites build an Avalonia ProgressBar (IsIndeterminate),
// which has no template in this theme-less app and drew nothing (probe 2026-10-01; 8 sites: the write-for-me button,
// the message editor's file check, running tool rows, the downloads popover, root view, wallpaper install, usage
// reset, Hydra reports).
//
// OD-0042 ("Mac look") / OD-0045, ED-231: the macOS spinner as the "Apple macOS 27 UI Kit" draws it (Progress Indicators /
// <Dark|Light> / Content Area / Circular / Indeterminate, 2 Sm and 3 Rg, SOURCE FACT (Apple design resource), read
// 2026-10-02; E:\Droppy\work\apple-kit\macos27-uikit-controls.md): eight capsule spokes every 45 degrees, each 4 x 10
// spanning 6 to 16 from the centre at 32 points (the 16-point spinner is exactly half), white in Dark and black in Light,
// the spoke at 12 o'clock at 0.55 and the others clockwise at 0.06, 0.13, 0.20, 0.27, 0.34, 0.41, 0.48. The kit is
// static: the spinner here steps the lead one spoke clockwise every eighth of Period, and Period (1 s) is UNVERIFIED.
// A spinner given no size is .controlSize(.small), 16 pt (the size the port's sites set). Driven by the port's frame
// clock (ED-117) so tests can step it; with no frame source bound it stands still. It keeps turning under reduce motion:
// nothing in the source stops an indeterminate ProgressView there. PortControlThemes gives every ProgressBar built without
// a template this spinner (indeterminate) or a plain track-and-fill bar (determinate, for completeness; no site uses one
// yet). Replaces the Windows progress ring of the ED-229 proposal.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. The system busy indicator (see the file note).</summary>
public sealed class SystemSpinner : Control
{
    /// <summary>One turn of the lead round the eight spokes (seconds). UNVERIFIED: the kit is static.</summary>
    public const double Period = 1.0;

    /// <summary>The spokes, every 45 degrees from 12 o'clock.</summary>
    public const int Spokes = 8;

    // The spoke colour: white in Dark, black in Light (the kit's 3 Rg / 2 Sm fills).
    private static readonly SwiftColor Ink = SwiftColor.Dynamic(new SwiftColor(white: 0), new SwiftColor(white: 1));

    private readonly Action<TimeSpan> frame;
    private readonly ISolidColorBrush ink; // made once: SwiftColor.Brush binds the control's theme, so never call it per frame
    private IFrameSource? source;
    private bool waiting;

    /// <summary>The brightest spoke (0 at 12 o'clock, counting clockwise), as last drawn.</summary>
    public int Lead { get; private set; }

    /// <summary>The spoke colour for the spinner's current theme.</summary>
    public Color SpokeColor => ink.Color;

    /// <summary>A spoke at <paramref name="size"/> points: inner and outer reach from the centre and its width, scaled from
    /// the kit's 32-point spinner (6 to 16, 4 wide).</summary>
    public static (double Inner, double Outer, double Width) Spoke(double size) => (size * 6 / 32, size * 16 / 32, size * 4 / 32);

    /// <summary>The opacity of <paramref name="spoke"/> when <paramref name="lead"/> leads: 0.55 for the lead, then 0.06 for
    /// the spoke clockwise of it, rising by 0.07 a spoke round to 0.48 just behind the lead.</summary>
    public static double SpokeOpacity(int spoke, int lead)
    {
        var behind = ((spoke - lead) % Spokes + Spokes) % Spokes;
        return behind == 0 ? 0.55 : 0.06 + 0.07 * (behind - 1);
    }

    public SystemSpinner()
    {
        frame = OnFrame;
        ink = (ISolidColorBrush)Ink.Brush(this);
        IsHitTestVisible = false;
        AttachedToVisualTree += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    /// <summary>.controlSize(.small): 16 pt when the site gives no size.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    private void Start()
    {
        source = FrameClock.Source.Current;
        Request();
    }

    private void Stop()
    {
        if (waiting) source?.CancelFrame(frame);
        waiting = false;
        source = null;
    }

    private void Request()
    {
        if (source is null || waiting) return;
        try
        {
            source.RequestFrame(frame);
            waiting = true;
        }
        catch (SwiftTrapException)
        {
            source = null; // no frame source bound (a headless test that did not ask for one): the arc stands still
        }
    }

    private void OnFrame(TimeSpan now)
    {
        waiting = false;
        Lead = Math.Min(Spokes - 1, (int)Math.Floor(now.TotalSeconds % Period / (Period / Spokes) + 1e-9));
        if (IsEffectivelyVisible) InvalidateVisual(); // a hidden spinner keeps its phase without repainting
        Request();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var (inner, outer, width) = Spoke(size);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        // One capsule standing up from the centre (12 o'clock), turned into place for each spoke.
        var capsule = new RoundedRect(new Rect(center.X - width / 2, center.Y - outer, width, outer - inner), width / 2);
        for (var spoke = 0; spoke < Spokes; spoke++)
        {
            using (context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateRotation(spoke * Math.PI * 2 / Spokes) * Matrix.CreateTranslation(center.X, center.Y)))
            using (context.PushOpacity(SpokeOpacity(spoke, Lead)))
                context.DrawRectangle(ink, null, capsule);
        }
    }
}
