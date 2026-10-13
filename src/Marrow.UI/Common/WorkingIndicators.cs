using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Theme;

// Source: DroppyCode/UI/Common/WorkingIndicators.swift (B2 1fbf81e), :1-345.
//
// :1-11 file header: ported from Zeron (https://github.com/zeronsh/zeron), MIT License, Copyright (c) 2026 Wing,
// apps/ios/Zeron/Views/Loaders.swift and apps/ios/Zeron/Theme/Motion.swift. Upstream drives the pulse from a
// repeating CAKeyframeAnimation the render server plays on its own once installed, with the shared clock origin
// `layer.convertTime(1, from: nil)` (:313): every spinner in the app reads the same absolute clock, so two of them
// never drift apart. Avalonia has no CALayer; this port reads that same shared, absolute clock (SwiftClock.Now,
// F.SwiftClock: one BCL TimeProvider for the whole process) every frame from the frame source seam (F.FrameClock,
// R-VIEW-10) instead of a CAKeyframeAnimation, and evaluates GradientSpin.Opacity continuously rather than at the
// 41 discrete samples :306-308 uses: PR-THEME-167's windows_path allows this ("using the curve directly as an
// easing function is acceptable only if it is sampled at least this finely"). ENGINEERING DECISION: repainting on
// the frame source, not a genuine compositor-thread animation, is the closest available seam; there is no
// Avalonia.Rendering.Composition dependency in this tree to reach CALayer's off-main-thread behaviour.

namespace Marrow.UI.Common;

/// <summary>:17-45 <c>enum GradientSpin</c>.</summary>
public static class GradientSpin
{
    /// <summary>
    /// :22-29 <c>rowTints(accent:)</c>: a lighter shade on top, the accent itself in the middle, a deeper shade
    /// below. Windows' accent (PlatformColorValues.AccentColor1, read through Marrow.UI.Chrome.Chrome.AccentNSColor / SwiftColor) is
    /// already sRGB, so there is no separate colour-space conversion to fall back from (ED-051).
    /// </summary>
    public static Color[] RowTints(Color accent) =>
    [
        Blend(accent, Colors.White, 0.38), // :25 base.blended(withFraction: 0.38, of: .white)
        accent, // :26 base
        Blend(accent, Colors.Black, 0.22), // :27 base.blended(withFraction: 0.22, of: .black)
    ];

    // NSColor.blended(withFraction:of:): self * (1 - fraction) + other * fraction, per channel including alpha.
    private static Color Blend(Color baseColor, Color other, double fraction) => Color.FromArgb(
        Channel(baseColor.A, other.A, fraction),
        Channel(baseColor.R, other.R, fraction),
        Channel(baseColor.G, other.G, fraction),
        Channel(baseColor.B, other.B, fraction));

    private static byte Channel(byte baseComponent, byte otherComponent, double fraction)
    {
        var value = baseComponent / 255.0 * (1 - fraction) + otherComponent / 255.0 * fraction;
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
    }

    public const double Dim = 0.1; // :30
    public const double Period = 0.75; // :31

    /// <summary>:33-44 <c>opacity(phase:)</c>: full at 0, eases down to dim by 45%, holds to 92%, rises to full by 100%.</summary>
    public static double Opacity(double phase)
    {
        var p = phase % 1; // :35 phase.truncatingRemainder(dividingBy: 1)
        if (p < 0) p += 1; // :36
        if (p < 0.45) // :37
        {
            var t = p / 0.45; // :38
            return 1 - (1 - Dim) * (t * t * (3 - 2 * t)); // :39
        }
        if (p < 0.92) return Dim; // :41
        var rise = (p - 0.92) / 0.08; // :42
        return Dim + (1 - Dim) * rise; // :43
    }
}

/// <summary>:47-53 <c>struct SpinnerCell</c>: one cell's row (for the tint) and its lag behind the wave, as a
/// fraction of the period (R-VAL-1).</summary>
public readonly record struct SpinnerCell(int Row, int Column, double Lag);

/// <summary>
/// PORT-ONLY layout math shared by the animated grid (:196-205, :274-293) and the still grid (:122-136), which
/// PR-THEME-166 records as producing the same geometry through nested stacks with the same 0.8 spacings.
/// </summary>
internal static class SpinnerGeometry
{
    internal static int RowCount(IReadOnlyList<SpinnerCell> cells)
    {
        var max = -1;
        for (var index = 0; index < cells.Count; index++) if (cells[index].Row > max) max = cells[index].Row; // indexed: no enumerator per frame (ED-NEW-lean-8)
        return max + 1; // :198, :278 (cells.map(\.row).max() ?? -1) + 1
    }

    /// <summary>:196-205 <c>contentSize</c>.</summary>
    internal static Size ContentSize(IReadOnlyList<SpinnerCell> cells, int columns, double cellSize)
    {
        var rows = RowCount(cells);
        var gap = cellSize * 0.8; // :199
        var width = columns * cellSize + Math.Max(0, columns - 1) * gap; // :201
        var height = rows * cellSize + Math.Max(0, rows - 1) * gap; // :202
        return new Size(width, height);
    }

    /// <summary>
    /// :274-293 <c>placeCells()</c>. AppKit's coordinate system starts at the bottom, so upstream reverses the row
    /// index (:287-289 comment); Avalonia's is top-origin like SwiftUI's own layout coordinates, so row 0 sits at
    /// the smallest y directly, with no reversal (PR-THEME-166 windows_path).
    /// </summary>
    internal static Rect CellFrame(SpinnerCell cell, Size bounds, Size content, double cellSize, double gap)
    {
        var originX = Math.Round((bounds.Width - content.Width) / 2, MidpointRounding.AwayFromZero); // :282
        var originY = Math.Round((bounds.Height - content.Height) / 2, MidpointRounding.AwayFromZero); // :283
        var x = originX + cell.Column * (cellSize + gap); // :289
        var y = originY + cell.Row * (cellSize + gap); // :290, row 0 at top: no AppKit-style reversal
        return new Rect(x, y, cellSize, cellSize);
    }

    /// <summary>
    /// Each cell in its row's brush at <c>GradientSpin.Opacity(phase - lag)</c>. ED-NEW-lean-8: the brushes are made once per
    /// tint (<see cref="RowBrushes"/>), not once per cell per frame; a solid brush of the same colour draws the same pixels.
    /// </summary>
    internal static void Paint(DrawingContext context, IReadOnlyList<SpinnerCell> cells, int columns, double cellSize,
        Size bounds, IBrush[] rowBrushes, double phase)
    {
        var content = ContentSize(cells, columns, cellSize);
        var gap = cellSize * 0.8;
        for (var index = 0; index < cells.Count; index++)
        {
            var cell = cells[index];
            var alpha = Math.Clamp(GradientSpin.Opacity(phase - cell.Lag), 0, 1);
            if (alpha <= 0) continue;
            using (context.PushOpacity(alpha))
                context.FillRectangle(rowBrushes[cell.Row], CellFrame(cell, bounds, content, cellSize, gap));
        }
    }

    /// <summary>One immutable solid brush per row tint (ED-NEW-lean-8).</summary>
    internal static IBrush[] RowBrushes(Color[] tints)
    {
        var brushes = new IBrush[tints.Length];
        for (var index = 0; index < tints.Length; index++) brushes[index] = new ImmutableSolidColorBrush(tints[index]);
        return brushes;
    }
}

/// <summary>PORT-ONLY (ED-NEW-perf-idle-4). The draw loop the Hydra decorations share: the 60 Hz cap as a timeline's minimum interval.</summary>
internal static class WorkingLoop
{
    public const double Interval = 1.0 / 60;
}

/// <summary>
/// PORT-ONLY (ED-NEW-lean-8, the lead's 60 Hz decorative cap): decides which frames of the main-thread frame clock repaint a
/// decoration whose picture moves continuously. It is the ambient pacer's rule (AmbientRefreshPacer, ED-NEW-frame-pacing),
/// one pacer per run of frames: the frames are counted as the display's refreshes on a grid locked to the frames themselves,
/// so a frame's timing jitter does not move a slot, and every 60 Hz slot repaints once: every refresh at 60 Hz (and on a
/// 60.1 Hz panel), every second at 120 Hz, every fourth at 240 Hz, two or three apart, evenly, at 144 and 165 Hz. A display
/// whose refresh period is unknown is paced on a 1/60 s grid of due times (the pacer's own fallback, ED-117 (2), ED-232). A
/// pause longer than a quarter second starts the count again, and the first frame of a run always repaints.
/// </summary>
public struct DecorationPace
{
    private AmbientRefreshPacer? pacer;
    private long frame;

    /// <summary>Where a new run reads the display's refresh period: the render frame source's (DWM's); tests set a fixed one.</summary>
    internal static Ambient<Func<TimeSpan?>> Period { get; } = new((Func<TimeSpan?>)(() => RenderFrameSource.PeriodSource()));

    /// <summary>Whether the frame at <paramref name="now"/> repaints; the first frame of a run always does.</summary>
    public bool Due(ContinuousInstant now)
    {
        pacer ??= new AmbientRefreshPacer(Period.Current);
        return pacer.Beat(++frame, now.Ticks / (double)TimeSpan.TicksPerSecond);
    }

    /// <summary>Starts a new run: the next frame repaints, and the count starts there.</summary>
    public void Reset() => pacer = null;
}

/// <summary>
/// :141-163 <c>SpinnerCells: NSViewRepresentable</c> and :165-317 <c>final class SpinnerLayerView: NSView</c>,
/// merged: an Avalonia control has no host/hosted split. The cells as layers, each pulsing on a repeating animation
/// with one shared clock origin (R-VIEW-1, R-VIEW-10).
/// </summary>
internal sealed class SpinnerLayerView : Control
{
    private readonly SpinnerCell[] cells;
    private readonly int columns;
    private readonly double cellSize;
    private Color[] tints = [Colors.Transparent, Colors.Transparent, Colors.Transparent];
    private IBrush[] brushes = SpinnerGeometry.RowBrushes([Colors.Transparent, Colors.Transparent, Colors.Transparent]);
    private readonly AnimationTimeline loop; // ED-NEW-perf-idle-4: a draw loop at 60 a second that parks while unseen
    private bool attached;
    private bool animated;

    internal SpinnerLayerView(IReadOnlyList<SpinnerCell> cells, int columns, double cellSize) // :150-154, :210-234
    {
        this.cells = [.. cells];
        this.columns = columns;
        this.cellSize = cellSize;
        Focusable = false;
        IsHitTestVisible = false; // :208 override func hitTest(_ point: NSPoint) -> NSView? { nil }
        ApplyTints();
        animated = !EnvironmentValues.GetAccessibilityReduceMotion(this);
        loop = new AnimationTimeline(() => new AnimationTimelineSchedule(WorkingLoop.Interval), _ => Draw());

        AttachedToVisualTree += (_, _) =>
        {
            // :236-244 viewDidMoveToWindow(): a spinner built off screen resolves its tints for the window's
            // appearance and starts its animation from the shared clock, rather than from whenever it appeared.
            attached = true;
            ApplyTints();
            UpdateAnimated();
            ThemeManager.DidChange += OnThemeChanged; // :185 NotificationCenter observer (R-NOTE-1)
        };
        DetachedFromVisualTree += (_, _) =>
        {
            // :193-195 deinit removes the observer; a Control has no deinit, so detaching from the tree is this
            // port's lifetime boundary (R-LIFE-1).
            ThemeManager.DidChange -= OnThemeChanged;
            attached = false;
            loop.Disappear();
        };
        ActualThemeVariantChanged += (_, _) => ApplyTints(); // :246-251 viewDidChangeEffectiveAppearance()
        PropertyChanged += (_, e) =>
        {
            if (e.Property == EnvironmentValues.AccessibilityReduceMotionProperty) UpdateAnimated();
        };
    }

    /// <summary>:256-270 <c>applyTints()</c>: every cell's row tint, from the current theme's accent, resolved for
    /// this view's own appearance.</summary>
    private void ApplyTints()
    {
        var accent = Marrow.UI.Chrome.Chrome.AccentNSColor.Resolve(ActualThemeVariant); // :261-263 effectiveAppearance.performAsCurrentDrawingAppearance
        tints = GradientSpin.RowTints(accent);
        brushes = SpinnerGeometry.RowBrushes(tints);
        InvalidateVisual(); // :264-269 CATransaction with actions disabled: an instant write, no crossfade (PR-THEME-170)
    }

    private void OnThemeChanged() // :252-254 @objc private func themeDidChange()
    {
        // The spinner's own dispatcher, and only while it is the UI dispatcher of the thread that posts: ThemeManager is set from any
        // actor, and a thread with no running Avalonia application makes a Dispatcher.UIThread of its own that answers CheckAccess() with
        // true, so asking UIThread retinted a spinner from a foreign thread (where it threw, or read a system accent no app was there to
        // give). Any other thread posts to the spinner's dispatcher, which retints it if it is still in a window.
        var own = Dispatcher;
        if (own.CheckAccess() && ReferenceEquals(own, Avalonia.Threading.Dispatcher.UIThread)) { ApplyTints(); return; }
        own.Post(() => { if (TopLevel.GetTopLevel(this) is not null) ApplyTints(); });
    }

    /// <summary>:295-317 <c>installAnimations()</c>: starts or stops the shared-clock repaint depending on reduce motion.</summary>
    private void UpdateAnimated()
    {
        animated = !EnvironmentValues.GetAccessibilityReduceMotion(this);
        if (attached)
        {
            if (animated) loop.Appear(); // draws at once the first time; nothing when it is already on screen
            else loop.Disappear(); // Reduce Motion: no frame is left waiting
        }
        InvalidateVisual();
    }

    /// <summary>ED-NEW-lean-8 (tests): how many frames have repainted the spinner.</summary>
    internal int Invalidations { get; private set; }

    // ED-NEW-lean-8: the pulse is drawn from the clock at render time, so it repaints only at the 60 Hz cap;
    // ED-NEW-perf-idle-4: the timeline asks for no frame in between, and none while no window of the app can be seen.
    private void Draw()
    {
        Invalidations++;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => SpinnerGeometry.ContentSize(cells, columns, cellSize);

    protected override Size ArrangeOverride(Size finalSize) => SpinnerGeometry.ContentSize(cells, columns, cellSize);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        // :306-308 GradientSpin.opacity(phase: step/40 - cell.lag), sampled from one shared clock; :298-300 frozen at
        // -lag when not animated.
        var phase = animated
            ? SwiftClock.Now.Ticks / (double)TimeSpan.TicksPerSecond / GradientSpin.Period
            : 0;
        SpinnerGeometry.Paint(context, cells, columns, cellSize, Bounds.Size, brushes, phase);
    }
}

/// <summary>
/// :114-138 <c>private struct StillSpinnerCells: View</c>: a spinner's cells as plain shapes, frozen at the start of
/// the wave, for an image renderer that cannot capture a hosted platform view (:84-87). Private in Swift, and
/// reachable only through <see cref="MiniSpinner"/> here too.
/// </summary>
file sealed class StillSpinnerGrid : Control
{
    private readonly SpinnerCell[] cells;
    private readonly int columns;
    private readonly double cellSize;
    private readonly Color[] tints;
    private readonly IBrush[] brushes;

    internal StillSpinnerGrid(IReadOnlyList<SpinnerCell> cells, int columns, double cellSize, Color[] tints) // :115-121
    {
        this.cells = [.. cells];
        this.columns = columns;
        this.cellSize = cellSize;
        this.tints = tints; // :120 tints: one per row, resolved by the caller
        brushes = SpinnerGeometry.RowBrushes(tints);
        Focusable = false;
        IsHitTestVisible = false;
    }

    protected override Size MeasureOverride(Size availableSize) => SpinnerGeometry.ContentSize(cells, columns, cellSize);

    protected override Size ArrangeOverride(Size finalSize) => SpinnerGeometry.ContentSize(cells, columns, cellSize);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        // :128-132 opacity(phase: -lag): every cell frozen at the value it would hold at the very start of the wave.
        SpinnerGeometry.Paint(context, cells, columns, cellSize, Bounds.Size, brushes, 0);
    }
}

/// <summary>
/// :55-81 <c>struct WorkingSpinner: View</c>: the 3x3 working indicator shown while a reply is being written
/// (R-VIEW-1). Hidden from assistive technology; the working line's own text carries the meaning.
/// </summary>
public sealed class WorkingSpinner : Decorator
{
    /// <summary>:67-74 the 3x3 grid; phase = distance from the bottom-centre cell, so the wave travels upward.</summary>
    public static readonly IReadOnlyList<SpinnerCell> Cells = BuildCells();

    private static SpinnerCell[] BuildCells()
    {
        var cells = new List<SpinnerCell>(9);
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var dx = (double)(column - 1); // :70
                var dy = (double)(2 - row); // :71
                cells.Add(new SpinnerCell(row, column, Math.Sqrt(dx * dx + dy * dy) / 2.5)); // :72
            }
        }
        return [.. cells];
    }

    /// <summary>:64 <c>var cellSize: CGFloat = 3.5</c>.</summary>
    public WorkingSpinner(double cellSize = 3.5)
    {
        // :77-79 SpinnerCells(cells: Self.cells, columns: 3, cellSize: cellSize, animated: !reduceMotion,
        // theme: ThemeManager.current).accessibilityHidden(true)
        Child = new SpinnerLayerView(Cells, 3, cellSize);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :79 .accessibilityHidden(true)
    }
}

/// <summary>:83-112 <c>struct MiniSpinner: View</c>: the 2x3 mini indicator whose cells snake clockwise, for
/// working threads in lists (R-VIEW-1).</summary>
public sealed class MiniSpinner : Decorator
{
    // :90-93 the fixed clockwise perimeter ring.
    private static readonly (int Row, int Column)[] Ring =
    [
        (0, 0), (0, 1), (1, 1), (2, 1), (2, 0), (1, 0),
    ];

    /// <summary>:94-100 the 2x3 grid; each cell's lag is its position in the ring divided by the ring's length.</summary>
    public static readonly IReadOnlyList<SpinnerCell> Cells = BuildCells();

    private static SpinnerCell[] BuildCells()
    {
        var cells = new List<SpinnerCell>(6);
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                var index = Array.FindIndex(Ring, r => r.Row == row && r.Column == column); // :96
                if (index < 0) index = 0; // :96 ?? 0: unreachable, every one of the six cells is in the ring
                cells.Add(new SpinnerCell(row, column, index / (double)Ring.Length)); // :97
            }
        }
        return [.. cells];
    }

    /// <summary>:85-88 <c>var cellSize: CGFloat = 2.4</c> and <c>var isStill = false</c>.</summary>
    public MiniSpinner(double cellSize = 2.4, bool isStill = false)
    {
        // :102-109 Group { if isStill { StillSpinnerCells(...) } else { SpinnerCells(...) } }
        Child = isStill
            ? new StillSpinnerGrid(Cells, 2, cellSize,
                GradientSpin.RowTints(Marrow.UI.Chrome.Chrome.AccentNSColor.Resolve(ActualThemeVariant))) // :105 tints resolved at build time
            : new SpinnerLayerView(Cells, 2, cellSize);
        // :110 .accessibilityLabel(Text("Working")), applied outside the isStill/animated branch, so both forms
        // carry it (PR-THEME-171).
        AutomationProperties.SetName(this, "Working");
    }
}

/// <summary>
/// :320-345 <c>enum WorkingWords</c>: the word beside the working indicator, rotated every few seconds and seeded
/// per thread, so two threads working at once do not say the same thing.
/// </summary>
public static class WorkingWords
{
    /// <summary>:323-328 the fixed list of twenty-two words, in order.</summary>
    public static readonly IReadOnlyList<string> Words =
    [
        "Thinking", "Pondering", "Scheming", "Brewing", "Weaving", "Tinkering",
        "Musing", "Composing", "Sifting", "Untangling", "Distilling", "Sketching",
        "Plotting", "Riffing", "Combobulating", "Percolating", "Marinating",
        "Noodling", "Puzzling", "Conjuring", "Creating", "Dancing",
    ];

    /// <summary>:329 <c>static let rotateSeconds: Int64 = 7</c>.</summary>
    public const long RotateSeconds = 7;

    /// <summary>
    /// :331-339 <c>seed(_:)</c>: FNV-1a over the identifier's UTF-8 bytes. The caller supplies the identifier
    /// exactly as ThreadTimeline.swift:2265 does (the thread's UUID string); this file does not format it.
    /// </summary>
    public static ulong Seed(string identifier)
    {
        var hash = 0xcbf29ce484222325UL; // :333
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(identifier)) // :335 identifier.utf8
        {
            hash ^= b; // :335
            hash = unchecked(hash * 0x100000001b3UL); // :336 hash = hash &* 0x100000001b3, R-NUM-4
        }
        return hash;
    }

    /// <summary>:341-344 <c>word(seed:elapsedSeconds:)</c>.</summary>
    public static string Word(ulong seed, long elapsedSeconds)
    {
        var step = (ulong)(Math.Max(0, elapsedSeconds) / RotateSeconds); // :342
        return Words[(int)(unchecked(seed + step) % (ulong)Words.Count)]; // :343 seed &+ step, R-NUM-4
    }
}
