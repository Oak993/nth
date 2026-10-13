using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Composer;

// Source: DroppyCode/App/Views/Composer/AttachmentFlight.swift (B3 52bc792), :1-207.

/// <summary>The one armed picker drop, consumed by the next new attachment cell.</summary>
public sealed class AttachmentDrops
{
    // :9 Swift static let shared, replaceable in tests without changing production lifetime.
    public static Ambient<AttachmentDrops> Shared { get; } = new(() => new AttachmentDrops());

    // :11-15 Swift struct Drop is an immutable value.
    public sealed record Drop(IImage Image, Rect FromScreen, double Radius);

    private Drop? drop;

    // :19-29 The old expiry deliberately compares image identity, including when the same image is re-armed.
    public void Arm(IImage image, Rect fromScreen, double radius = 6)
    {
        MainActor.AssumeIsolated();
        var armed = new Drop(image, fromScreen, radius);
        drop = armed;
        var weakSelf = new WeakReference<AttachmentDrops>(this);
        MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(2));
            if (weakSelf.TryGetTarget(out var owner) && ReferenceEquals(owner.drop?.Image, armed.Image)) owner.drop = null;
        });
    }

    // :31-35 Taking is destructive even when the later flight will return early.
    public Drop? Take()
    {
        MainActor.AssumeIsolated();
        var taken = drop;
        drop = null;
        return taken;
    }
}

/// <summary>One ghost between the picker row and the newly laid-out attachment chip.</summary>
public sealed class AttachmentFlight(IAttachmentFlightPlatform? platform = null)
{
    // :43 Swift static let shared.
    public static Ambient<AttachmentFlight> Shared { get; } = new(() => new AttachmentFlight());

    private readonly IAttachmentFlightPlatform platform = platform ?? new WindowsAttachmentFlightPlatform();
    private IAttachmentFlightSurface? window;

    // :98-102, ED-011: the critically damped envelope reaches 1/1000 at this time.
    public static TimeSpan SettlingDuration { get; } = TimeSpan.FromSeconds(Math.Log(1000) / (26.0 / (2 * 1.0)));

    // :47-123 The guard precedes teardown; a missing image or screen follows teardown.
    public void Fly(IImage image, Rect fromScreen, Rect toScreen, double fromRadius, double toRadius, Action? onLanded = null)
    {
        MainActor.AssumeIsolated();
        if (platform.ReduceMotion) return; // :55, UPSTREAM DEFECT 51, reproduced
        if (window is not null)
        {
            // :56-59 Hiding an old flight does not cancel its independently captured :111-116 completion.
            window.OrderOut();
            window = null;
        }

        var resolved = platform.ResolveImage(image);
        var screen = platform.MainScreen;
        if (resolved is null || screen is null) return; // :60-61

        var frame = fromScreen.Union(toScreen).Inflate(2); // :63
        var surface = platform.Create(frame, screen.Value, resolved); // :64-65
        window = surface;

        // :71-91 Values are relative to the main screen origin, and the model finishes at the destination.
        var start = new Rect(fromScreen.X - screen.Value.X, fromScreen.Y - screen.Value.Y,
            fromScreen.Width, fromScreen.Height);
        var end = new Rect(toScreen.X - screen.Value.X, toScreen.Y - screen.Value.Y,
            toScreen.Width, toScreen.Height);
        surface.Apply(start, fromRadius);

        // :93-121 All three properties use the same mass, stiffness, damping and clock.
        var source = FrameClock.Source.Current;
        var began = SwiftClock.Now;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var elapsed = SwiftClock.Now - began;
            var complete = elapsed >= SettlingDuration;
            var t = complete ? SettlingDuration.TotalSeconds : Math.Max(0, elapsed.TotalSeconds);
            // :98-102 m=1, k=170, c=26. These near-critical coefficients have damped frequency sqrt(170-169)=1.
            // The zero-initial-velocity solution uses all three source coefficients, not an approximated ease curve.
            const double decay = 26.0 / (2 * 1.0);
            var frequency = Math.Sqrt(170.0 / 1.0 - decay * decay);
            double Free(double at) => 1 - Math.Exp(-decay * at) * (Math.Cos(frequency * at) + decay / frequency * Math.Sin(frequency * at));
            // PORT-ONLY (ED-NEW-perf-motion-10): the free spring is 0.74 % short at the settling time, and the ghost left that much for the
            // chip to jump at the landing; it is folded into the second half of the flight (MotionLanding), so the ghost reaches the chip's
            // frame with no speed. The first half is the free spring exactly. Its speed is e^(-decay t) sin(frequency t) (decay^2 + frequency^2) / frequency.
            var settles = SettlingDuration.TotalSeconds;
            var settleSpeed = Math.Exp(-decay * settles) * Math.Sin(frequency * settles) * (decay * decay + frequency * frequency) / frequency;
            var progress = complete ? 1 : Marrow.UI.Port.MotionLanding.Land(Free(t), t, settles, 1 - Free(settles), settleSpeed);
            var current = new Rect(
                start.X + (end.X - start.X) * progress,
                start.Y + (end.Y - start.Y) * progress,
                start.Width + (end.Width - start.Width) * progress,
                start.Height + (end.Height - start.Height) * progress);
            surface.Apply(current, fromRadius + (toRadius - fromRadius) * progress);
            if (complete)
            {
                surface.OrderOut();
                window = null;
                onLanded?.Invoke(); // :111-115
            }
            else source.RequestFrame(tick!);
        };
        source.RequestFrame(tick);
        surface.Show(); // :122
    }
}

/// <summary>A layout probe that reports its exact screen rectangle on layout and window attachment.</summary>
public sealed class ScreenFrameReader : Control
{
    // :145-153 and :181-183 The callback is replaced when a surviving cell's isNew changes.
    public Action<Rect> OnChange { get; set; }

    public ScreenFrameReader(Action<Rect> onChange)
    {
        OnChange = onChange;
        IsHitTestVisible = false;
        LayoutUpdated += (_, _) => Report(); // :158-161
        AttachedToVisualTree += (_, _) => Report(); // :163-166
    }

    // The view this reader stands behind. In SwiftUI the reader is the content's background and so is exactly the content's
    // frame; here the reader is a sibling, so it measures the content it backs. Null: the reader's own bounds.
    public Func<Control?>? Subject { get; set; }

    // :168-171 Convert the subject's bounds into a screen rectangle whenever it has a window, through the view hierarchy so
    // that a visual transform is part of the frame (convert(bounds, to: nil)).
    private void Report()
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel) return;
        var subject = Subject?.Invoke() ?? this;
        if (subject.TransformToVisual(topLevel) is not { } transform) return;
        var inWindow = new Rect(subject.Bounds.Size).TransformToAABB(transform);
        var origin = topLevel.PointToScreen(inWindow.TopLeft);
        var scale = topLevel.RenderScaling;
        OnChange(new Rect(origin.X / scale, origin.Y / scale, inWindow.Width, inWindow.Height));
    }
}

/// <summary>The background reader around one chip; only a new cell can claim a drop.</summary>
public sealed class AttachmentArrival : Grid
{
    private bool isNew;
    private AttachmentDrops drops;
    private AttachmentFlight flight;
    private readonly ScreenFrameReader reader;

    // :189-205 The reader is behind the content and reports the latter's laid-out frame.
    public AttachmentArrival(bool isNew, AttachmentDrops? drops = null, AttachmentFlight? flight = null, Control? content = null)
    {
        this.isNew = isNew;
        this.drops = drops ?? AttachmentDrops.Shared.Current;
        this.flight = flight ?? AttachmentFlight.Shared.Current;
        reader = new ScreenFrameReader(Report) { Subject = () => Children.Count > 1 ? Children[1] : null }; // :192-193 background of the content
        Children.Add(reader);
        if (content is not null) Children.Add(content);
    }

    // :195-204 Taking happens before Fly tests reduced motion, image or screen availability.
    public void Report(Rect rect)
    {
        MainActor.AssumeIsolated();
        if (!isNew) return;
        // PORT-ONLY (ED-282): the reader also reports when the cell joins the window, before its first arrange, with an empty
        // rectangle; the ghost has no frame to land in then (it flew to the cell's origin, 0 by 0), so the cell's first laid-out frame is
        // its appearance.
        if (rect.Width <= 0 || rect.Height <= 0) return;
        // :1002, :1021 onAppear records the id in `drawn`, and the strip's next evaluation hands the cell isNew false: a cell that stays
        // (another one removed or added beside it) moves and reports again, and never takes the next picker's drop.
        isNew = false;
        if (drops.Take() is not { } drop) return;
        flight.Fly(drop.Image, drop.FromScreen, rect, drop.Radius, 10);
    }

    // :181-183 The background closure must be refreshed when a surviving cell gets new inputs.
    public void Update(bool isNew, AttachmentDrops? drops = null, AttachmentFlight? flight = null)
    {
        this.isNew = isNew;
        if (drops is not null) this.drops = drops;
        if (flight is not null) this.flight = flight;
        reader.OnChange = Report;
    }
}
