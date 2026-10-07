using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/RowGlide.swift (B3 52bc792), :1-252.
//
// A thread row's glide to its new place in the sidebar: down to the settled rows as it settles, back up among
// the open ones as it reopens (:4-11).

/// <summary>RowGlideAnimator (:12-138). @MainActor @Observable final class.</summary>
public sealed class RowGlideAnimator : MainActorModel
{
    // :15, R-LIFE-2: an ambient, so a test can replace it for its own async flow (the same shape as
    // UI/Common/Genie.swift's GenieAnimator.Shared, Marrow.UI.Common.Genie.cs).
    public static Ambient<RowGlideAnimator> Shared { get; } = new(() => new RowGlideAnimator());

    // :18 nonisolated static let spring = Chrome.glideSpring
    public static Spring Spring => Chrome.GlideSpring;

    /// <summary>Glide (:20-46). A Swift struct: an immutable record (R-VAL-1).</summary>
    public sealed record Glide
    {
        // :21-24
        public enum Direction { Settle, Reopen }

        public Guid Id { get; } = Guid.NewGuid(); // :26 let id = UUID()
        public required Guid ThreadID { get; init; } // :27
        // :28 `let direction: Direction`; R-NAME-3: the capitalized member would collide with the nested type name.
        public required Direction GlideDirection { get; init; }
        public required IImage Departure { get; init; } // :30
        public required IImage Arrival { get; init; } // :31
        public required double DepartureFill { get; init; } // :34
        public required double ArrivalFill { get; init; } // :35
        public required Rect From { get; init; } // :36
        public required Rect To { get; init; } // :37
        public required Rect Bounds { get; init; } // :39
        public DateTimeOffset LaunchedAt { get; } = SwiftDate.Now; // :40 let launchedAt = Date.now
        public bool HasArrived { get; init; } // :42
        public bool HasLanded { get; init; } // :45
    }

    // The three AppKit reads this file makes (NSWorkspace reduce motion at :76, NSScreen backing scale factor and
    // ImageRenderer at :135-136) are the identical three reads UI/Common/Genie.swift makes; this reuses the seam
    // built for that file (Marrow.UI.Port.IGeniePlatform / WindowsGeniePlatform) rather than inventing a second one.
    private readonly IGeniePlatform platform;

    private ValueList<Glide> glidesField = ValueList<Glide>.Empty; // :48 private(set) var glides: [Glide] = []

    // :49-53, R-OBS-5 (@ObservationIgnored): a plain field, one cell per thread, kept once made.
    private readonly Dictionary<Guid, ObservedValue<bool>> hidingCells = [];

    // :61 private static let landingWindow: TimeInterval = 0.5
    private static readonly double LandingWindowSeconds = 0.5;

    public RowGlideAnimator(IGeniePlatform? platform = null)
    {
        this.platform = platform ?? new WindowsGeniePlatform();
    }

    public ValueList<Glide> Glides
    {
        get => Read(glidesField);
        private set => Set(ref glidesField, value);
    }

    // :65-83 launch(threadID:direction:from:fallback:bounds:departure:arrival:departureFill:arrivalFill:)
    public void Launch(
        Guid threadID,
        Glide.Direction direction,
        Rect from,
        Rect fallback,
        Rect bounds,
        IImage departure,
        IImage arrival,
        double departureFill,
        double arrivalFill)
    {
        // :76
        if (!(from.Width > 1 && from.Height > 1) || platform.AccessibilityDisplayShouldReduceMotion) return;
        Glides = new ValueList<Glide>(Glides.Where(g => g.ThreadID != threadID)); // :77
        Glides = Glides.Appending(new Glide // :78-81
        {
            ThreadID = threadID,
            GlideDirection = direction,
            Departure = departure,
            Arrival = arrival,
            DepartureFill = departureFill,
            ArrivalFill = arrivalFill,
            From = from,
            To = fallback,
            Bounds = bounds,
        });
        SetHiding(threadID, true); // :82
    }

    // :86-92 land(threadID:settled:at:)
    public void Land(Guid threadID, bool settled, Rect at)
    {
        var index = IndexOf(g => g.ThreadID == threadID); // :87
        if (index < 0) return;
        var glide = Glides[index];
        if (!(SwiftDate.TimeIntervalSince(SwiftDate.Now, glide.LaunchedAt) < LandingWindowSeconds)) return; // :88
        if (glide.GlideDirection != (settled ? Glide.Direction.Settle : Glide.Direction.Reopen)) return; // :89
        Glides = Glides.SetItem(index, glide with { To = at, HasLanded = true }); // :90-91
    }

    // :96-98 isHiding(_:)
    public bool IsHiding(Guid threadID) => HidingCell(threadID).Value;

    // :100-105 hidingCell(_:)
    private ObservedValue<bool> HidingCell(Guid threadID)
    {
        if (hidingCells.TryGetValue(threadID, out var cell)) return cell;
        cell = new ObservedValue<bool>(false);
        hidingCells[threadID] = cell;
        return cell;
    }

    // :109-112 setHiding(_:_:)
    private void SetHiding(Guid threadID, bool hiding)
    {
        var cell = HidingCell(threadID);
        if (cell.Value != hiding) cell.Value = hiding;
    }

    // :116-120 arrive(_:)
    public void Arrive(Guid id)
    {
        var index = IndexOf(g => g.Id == id); // :117
        if (index < 0) return;
        var glide = Glides[index] with { HasArrived = true }; // :118
        Glides = Glides.SetItem(index, glide);
        SetHiding(glide.ThreadID, false); // :119
    }

    // :122-128 finish(_:)
    public void Finish(Guid id)
    {
        var index = IndexOf(g => g.Id == id); // :123
        if (index < 0) return;
        var threadID = Glides[index].ThreadID; // :124
        Glides = Glides.Mutating(list => list.RemoveAt(index)); // :125
        // A thread with no ghost left in the air shows; one relaunched meanwhile stays hidden. :126-127
        SetHiding(threadID, Glides.Any(g => g.ThreadID == threadID && !g.HasArrived));
    }

    private int IndexOf(Func<Glide, bool> match)
    {
        var glides = Glides;
        for (var i = 0; i < glides.Count; i++)
        {
            if (match(glides[i])) return i;
        }
        return -1;
    }

    // :131-137 render(_:size:colorScheme:). Not exercised by this part's records (PR-SIDEBAR-136 to -142,
    // UI-SIDEBAR-050/051): it is the helper SidebarView.swift calls, off-Swift-file, to build the face images
    // handed to Launch; SidebarView.swift is a different unit. ENGINEERING DECISION: colorScheme (SwiftUI
    // ColorScheme) maps to Avalonia's ThemeVariant, applied over the content the same way `.environment(\.colorScheme,
    // colorScheme)` scopes it in Swift.
    public IImage? Render(Control face, Size size, ThemeVariant colorScheme) =>
        Render(face, size, colorScheme, platform.MainScreenBackingScaleFactor ?? 2); // :135 existing caller contract

    // :127-134 PR-SIDEBAR-135 windows_path: SidebarThreadRow supplies its own window's DPI (minimum 2).
    internal IImage? Render(Control face, Size size, ThemeVariant colorScheme, double scale)
    {
        // :133 .frame(width: size.width, height: size.height, alignment: .topLeading): a new frame view around the face; the
        // caller's own control keeps the width and height it has.
        var framed = new Border { Width = size.Width, Height = size.Height, Child = face };
        var scope = new ThemeVariantScope { RequestedThemeVariant = colorScheme, Child = framed }; // :134
        return platform.RenderImage(scope, scale); // :136
    }
}

/// <summary>
/// RowGlideLayer (:140-154). The window-wide layer the glides draw in. It never takes clicks.
/// </summary>
public sealed class RowGlideLayer : Panel
{
    private readonly RowGlideAnimator model;
    private readonly Dictionary<Guid, RowGlideView> rows = [];

    public RowGlideLayer(RowGlideAnimator? animator = null)
    {
        model = animator ?? RowGlideAnimator.Shared.Current;
        IsHitTestVisible = false; // :151 .allowsHitTesting(false)
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw); // :152 .accessibilityHidden(true)
        this.Track(() => model.Glides, Apply); // :143-148 ZStack(alignment: .topLeading) { ForEach(shared.glides) }
    }

    private void Apply(ValueList<RowGlideAnimator.Glide> glides)
    {
        var seen = new HashSet<Guid>();
        foreach (var glide in glides)
        {
            seen.Add(glide.Id);
            if (rows.TryGetValue(glide.Id, out var row)) row.Update(glide);
            else Children.Add(rows[glide.Id] = new RowGlideView(glide, model));
        }
        foreach (var gone in rows.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            Children.Remove(rows[gone]);
            rows.Remove(gone);
        }
    }
}

/// <summary>
/// RowGlideView (:156-243), private in Swift. One ghost's flight: a single spring-driven progress value (:224-226,
/// Chrome.glideSpring, response 0.6, damping ratio 0.86) that drives the interpolated frame, the crossfading faces,
/// the lift and its shadow, and the travelling check.
/// </summary>
public sealed class RowGlideView : Control
{
    // :166-167. Read from the interpolation, which never touches live UI state (R-ISO nonisolated), so these stay
    // plain static fields, cited separately from SidebarThreadRow's own check insets (26 and 6): UI-SIDEBAR-051.
    public const double TrailingCheckInset = 36;
    public const double LeadingCheckInset = 16;

    private RowGlideAnimator.Glide glide;
    private readonly RowGlideAnimator animator; // :15, :233-235 the layer's own animator (RowGlideAnimator.shared unless a caller injects one)
    private readonly SwiftImage check;
    private Action<TimeSpan>? frame;
    private IFrameSource? source;
    private ContinuousInstant started;
    private bool waitingFrame;
    private double progress;

    public RowGlideView(RowGlideAnimator.Glide glide, RowGlideAnimator? animator = null)
    {
        this.glide = glide;
        this.animator = animator ?? RowGlideAnimator.Shared.Current;
        IsHitTestVisible = false;
        var settles = glide.GlideDirection == RowGlideAnimator.Glide.Direction.Settle; // :185
        // :215-217 Image(systemName: settles ? "checkmark.circle.fill" : "checkmark.circle"); OD-0010: Lucide
        // circle-check, filled for the `.fill` case, from porting/icons/sf-symbols-to-lucide.json.
        check = SwiftImage.SystemName(settles ? "checkmark.circle.fill" : "checkmark.circle")
            .Font(Chrome.InlineIconFont)
            .ForegroundStyle(Chrome.Success);
        frame = OnFrame;
        AttachedToVisualTree += (_, _) => Start(); // :227 .onAppear { hasStarted = true }, which fires the keyframe
        DetachedFromVisualTree += (_, _) => Stop();
        this.Task(() => true, RunHandover); // :228-236 .task
    }

    /// <summary>The row synced back onto an existing ghost (RowGlideLayer.Apply / ForEach).</summary>
    public void Update(RowGlideAnimator.Glide next)
    {
        glide = next;
        InvalidateVisual();
    }

    private void Start()
    {
        started = SwiftClock.Now;
        progress = 0;
        source = FrameClock.Source.Current;
        Request();
    }

    private void Stop()
    {
        if (waitingFrame) source?.CancelFrame(frame!);
        waitingFrame = false;
        source = null;
    }

    private void Request()
    {
        if (waitingFrame || source is null) return;
        waitingFrame = true;
        source.RequestFrame(frame!);
    }

    private void OnFrame(TimeSpan _)
    {
        waitingFrame = false;
        if (source is null) return;
        var elapsed = (SwiftClock.Now - started).TotalSeconds;
        var animation = Animation.Spring(RowGlideAnimator.Spring); // :225-226 SpringKeyframe(1.0, spring: ...)
        progress = elapsed >= animation.Duration ? 1.0 : animation.Progress(elapsed);
        InvalidateVisual();
        if (elapsed < animation.Duration) Request();
    }

    // :230-236. R-TASK-3: `try? await Task.sleep` never throws; the code after it runs whether or not the sleep
    // completed, exactly as Swift's `try?` swallows CancellationError and falls through to the next statement.
    private async Task RunHandover(bool _)
    {
        var duration = RowGlideAnimator.Spring.SettlingDuration; // :231
        await SwiftTask.TrySleep(TimeSpan.FromSeconds(Math.Max(duration - 0.12, 0))); // :232
        animator.Arrive(glide.Id); // :233
        await SwiftTask.TrySleep(TimeSpan.FromSeconds(0.12)); // :234
        animator.Finish(glide.Id); // :235
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var p = progress;
        var from = glide.From;
        var to = glide.To;
        var frameRect = InterpolatedFrame(from, to, p); // :176-181
        var lift = Lift(p); // :183
        var fill = Fill(glide.DepartureFill, glide.ArrivalFill, p, lift); // :184
        var settles = glide.GlideDirection == RowGlideAnimator.Glide.Direction.Settle; // :185
        var checkTravel = Smoothstep(0.05, 0.95, p); // :186
        var checkStart = settles ? frameRect.Right - TrailingCheckInset : frameRect.Left + LeadingCheckInset; // :187
        var checkEnd = settles ? frameRect.Left + LeadingCheckInset : frameRect.Right - TrailingCheckInset; // :188

        using (context.PushClip(glide.Bounds)) // :223, FrameShape(frame: glide.bounds)
        {
            var shape = new RoundedRect(frameRect, Chrome.RowCornerRadius); // :172, :207
            // :208 .shadow(color: .black.opacity(0.28 * lift), radius: 10, y: 4) (sigma 10, ED-327) on the filled shape: its own coverage, cast at
            // the fill's alpha and drawn under it, through it (SwiftShadow.Cast, DrawCast; ED-358).
            var brush = Chrome.Overlay(fill).Brush(this);
            SwiftShadow.DrawCast(context, shape, SwiftShadow.Cast(Color.FromArgb((byte)Math.Round(0.28 * lift * 255), 0, 0, 0), SwiftShadow.FillAlpha(brush)),
                radius: 10, y: 4);
            context.DrawRectangle(brush, null, shape); // :206-207

            using (context.PushClip(shape)) // :203 .clipShape(shape)
            {
                using (context.PushOpacity(1 - Smoothstep(0.2, 0.65, p))) // :196
                {
                    context.DrawImage(glide.Departure, new Rect(glide.Departure.Size), new Rect(frameRect.Position, from.Size)); // :193-196
                }
                using (context.PushOpacity(Smoothstep(0.35, 0.8, p))) // :200
                {
                    context.DrawImage(glide.Arrival, new Rect(glide.Arrival.Size), new Rect(frameRect.Position, to.Size)); // :197-200
                }
            }

            // :211-223 the check is part of the ghost, before the final FrameShape clip: it is cut at the list's bounds too.
            // :211-220. The check crosses on its own, over the crossfading faces, at the ghost's vertical centre.
            var checkX = checkStart + (checkEnd - checkStart) * checkTravel; // :220
            var checkY = frameRect.Center.Y; // :220
            var checkScale = settles ? 1.35 - 0.35 * Smoothstep(0, 0.5, p) : 1; // :218
            var checkOpacity = settles ? 1 - 0.2 * p : 1 - Smoothstep(0.55, 0.9, p); // :219
            check.Measure(Size.Infinity);
            check.Arrange(new Rect(check.DesiredSize));
            using (context.PushOpacity(Math.Clamp(checkOpacity, 0, 1)))
            using (context.PushTransform(
                Matrix.CreateTranslation(-check.DesiredSize.Width / 2, -check.DesiredSize.Height / 2) *
                Matrix.CreateScale(checkScale, checkScale) *
                Matrix.CreateTranslation(checkX, checkY)))
            {
                check.Render(context);
            }
        }
    }

    // :176-181
    private static Rect InterpolatedFrame(Rect from, Rect to, double p) => new(
        from.X + (to.X - from.X) * p,
        from.Y + (to.Y - from.Y) * p,
        from.Width + (to.Width - from.Width) * p,
        from.Height + (to.Height - from.Height) * p);

    // :183. The lift comes up as the row takes off and is gone by the time it lands.
    public static double Lift(double p) => Math.Min(Smoothstep(0, 0.12, p), 1 - Smoothstep(0.7, 1, p));

    // :184
    public static double Fill(double departureFill, double arrivalFill, double p, double lift) =>
        Math.Max(departureFill + (arrivalFill - departureFill) * p, 0.12 * lift);

    // :239-242 smoothstep(_:_:_:)
    public static double Smoothstep(double edge0, double edge1, double x)
    {
        var t = Math.Min(Math.Max((x - edge0) / (edge1 - edge0), 0), 1);
        return t * t * (3 - 2 * t);
    }
}
