// PORT-ONLY (ED-NEW-session-hotkeys; owner 2026-10-11): the numbers 1 to 9 that hold-Ctrl shows on the sidebar's session rows,
// and the order Ctrl+1..9 opens them in. The Mac app has Command-1..9 (AppCommands.swift:95-98, ThreadWindow.swift digitMonitor)
// but no hold-to-show numbers; this is a port-owned addition, designed in the sidebar's own tokens.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.App.Port;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Views.Sidebar;

/// <summary>
/// The sidebar's session numbering and the badges that show it. A sidebar publishes the session rows it shows, top to bottom
/// (<see cref="Publish"/>); the first nine are numbered 1 to 9 and Ctrl+N opens number N (<see cref="AppModel.SelectThreadNumber"/>).
/// <see cref="SetShown"/> fades every badge in or out together, on one presence value the frame clock drives (render-only:
/// opacity and a scale transform, so the list never lays out again while it runs).
/// </summary>
public sealed class SessionNumbers
{
    /// <summary>Numbers run 1 to 9: the digit keys.</summary>
    public const int Count = 9;

    /// <summary>
    /// Fade in: a critically damped spring of response 0.16 s, settled in about 0.2 s (owner: "they come in with a premium
    /// transition, fading in").
    /// </summary>
    public const double FadeInResponse = 0.16;

    /// <summary>
    /// Fade out: a critically damped spring of response 0.12 s, settled in about 0.15 s and a fifth of the way out on the first
    /// frame after the key-up (owner: "when I lift my finger off Ctrl ... they should fade out").
    /// </summary>
    public const double FadeOutResponse = 0.12;

    /// <summary>The presence is settled when it is this close to its end and this slow (per second); it then lands exactly.</summary>
    private const double SettledDistance = 0.004, SettledSpeed = 0.06;

    /// <summary>The scale a badge settles from as it fades in (0.96 to 1).</summary>
    public const double RestScale = 0.96;

    private sealed record Publisher(bool InPopover, Func<bool> OnScreen, IReadOnlyList<Guid> Order);

    private readonly Dictionary<object, Publisher> publishers = new(ReferenceEqualityComparer.Instance);
    private readonly List<SessionKeycap> keycaps = [];
    private IFrameSource? frames;
    private Action<TimeSpan>? tick;
    private double velocity; // the presence's speed (per second), kept across a turn so it bends rather than stops
    private ContinuousInstant springAt; // when the spring was last stepped

    /// <summary>Whether the badges are asked to show (the presence follows it on the frame clock).</summary>
    public bool IsShown { get; private set; }

    /// <summary>The badges' presence drawn now: 0 hidden, 1 shown. Every badge draws this one value.</summary>
    public double Presence { get; private set; }

    /// <summary>Raised after each presence step, for tests and the badges' covered marks.</summary>
    public event Action<double>? PresenceChanged;

    /// <summary>
    /// A sidebar's visible session rows in on-screen order (threads and their helper rows, across project groups, as the list
    /// draws them; rows inside a collapsed group are not in the list and so not numbered).
    /// </summary>
    public void Publish(object owner, bool inPopover, Func<bool> onScreen, IReadOnlyList<Guid> order)
    {
        var distinct = new List<Guid>(order.Count);
        var seen = new HashSet<Guid>();
        foreach (var id in order)
            if (seen.Add(id)) distinct.Add(id);
        var before = Order;
        publishers[owner] = new Publisher(inPopover, onScreen, distinct);
        if (!SameNumbers(before, Order)) Reapply();
    }

    /// <summary>The sidebar left the window: its order no longer counts.</summary>
    public void Withdraw(object owner)
    {
        var before = Order;
        if (publishers.Remove(owner) && !SameNumbers(before, Order)) Reapply();
    }

    /// <summary>
    /// The order the numbers follow: the floating sidebar while it is on screen, else the window's sidebar column (on screen or
    /// collapsed: Ctrl+N still opens the session the column would number), else none (the model's own sidebar order applies).
    /// </summary>
    public IReadOnlyList<Guid>? Order
    {
        get
        {
            Publisher? column = null;
            foreach (var publisher in publishers.Values)
            {
                if (publisher.InPopover)
                {
                    if (publisher.OnScreen()) return publisher.Order;
                }
                else if (column is null || publisher.OnScreen()) column = publisher;
            }
            return column?.Order;
        }
    }

    /// <summary>The number Ctrl+N gives the session, 1 to 9, or null past the ninth row or off the list.</summary>
    public long? NumberOf(Guid threadID)
    {
        if (Order is not { } order) return null;
        var limit = Math.Min(order.Count, Count);
        for (var index = 0; index < limit; index++)
            if (order[index] == threadID) return index + 1;
        return null;
    }

    /// <summary>
    /// Shows or hides every badge together, on a critically damped spring (<see cref="FadeInResponse"/> in, with the scale
    /// going 0.96 to 1, <see cref="FadeOutResponse"/> out). It gets moving on the first frame and never overshoots; a change
    /// mid-fade turns it from the presence and the speed it has, so it bends round rather than stopping dead. Reduce motion, or
    /// no frame clock, shows and hides at once.
    /// </summary>
    public void SetShown(bool shown, Control? context = null)
    {
        if (IsShown == shown) return;
        IsShown = shown;
        var target = shown ? 1.0 : 0.0;
        if (!FrameClock.IsBound || ReducesMotion(context))
        {
            StopTick();
            velocity = 0;
            if (Presence != target) Step(target);
            return;
        }
        if (Presence == target && velocity == 0)
        {
            StopTick();
            return;
        }
        if (tick is not null)
        {
            // The running spring turns here: the time since its last frame was spent going the old way, from now the new.
            SpringTo(!shown);
            return;
        }
        var source = FrameClock.Source.Current;
        springAt = SwiftClock.Now;
        Action<TimeSpan>? frame = null;
        frame = _ =>
        {
            if (!ReferenceEquals(tick, frame)) return;
            if (SpringTo(IsShown))
            {
                tick = null;
                frames = null;
                return;
            }
            source.RequestFrame(frame!);
        };
        tick = frame;
        frames = source;
        source.RequestFrame(frame);
    }

    // Steps the spring from its last step to now toward shown (true) or hidden; true once it has landed there exactly.
    private bool SpringTo(bool shown)
    {
        var now = SwiftClock.Now;
        var seconds = Math.Max(0, (now - springAt).TotalSeconds);
        springAt = now;
        var end = shown ? 1.0 : 0.0;
        var omega = 2 * Math.PI / (shown ? FadeInResponse : FadeOutResponse);
        var spring = new CriticalSpring(Presence) { Velocity = velocity };
        spring.Step(end, omega, seconds);
        velocity = spring.Velocity;
        var next = Math.Clamp(spring.Position, 0, 1);
        if (Math.Abs(next - end) < SettledDistance && Math.Abs(velocity) < SettledSpeed * omega)
        {
            velocity = 0;
            if (Presence != end) Step(end);
            return true;
        }
        if (next != Presence) Step(next);
        return false;
    }

    internal void Register(SessionKeycap keycap)
    {
        if (!keycaps.Contains(keycap)) keycaps.Add(keycap);
        keycap.Apply(Presence, NumberOf(keycap.ThreadID));
    }

    internal void Unregister(SessionKeycap keycap) => keycaps.Remove(keycap);

    /// <summary>The badge draws again from the current presence (its row rebuilt what it covers).</summary>
    internal void Reapply(SessionKeycap keycap) => keycap.Apply(Presence, NumberOf(keycap.ThreadID));

    private void Reapply()
    {
        foreach (var keycap in keycaps.ToArray()) keycap.Apply(Presence, NumberOf(keycap.ThreadID));
    }

    private void Step(double presence)
    {
        Presence = presence;
        Reapply();
        PresenceChanged?.Invoke(presence);
    }

    private void StopTick()
    {
        if (tick is { } running) frames?.CancelFrame(running);
        tick = null;
        frames = null;
    }

    private static bool SameNumbers(IReadOnlyList<Guid>? left, IReadOnlyList<Guid>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        var count = Math.Min(Count, left.Count);
        if (count != Math.Min(Count, right.Count)) return false;
        for (var index = 0; index < count; index++)
            if (left[index] != right[index]) return false;
        return true;
    }

    internal static bool ReducesMotion(Control? context) =>
        (context is not null && EnvironmentValues.GetAccessibilityReduceMotion(context)) ||
        Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
}

/// <summary>
/// One row's number badge: a small rounded keycap with the digit, over the row's leading mark (or its trailing status, on a
/// row whose text starts at its edge). It sits in an overlay, so it never moves the row's text; while it shows, the marks it
/// covers fade out under it by the same presence. It draws through opacity and a scale transform only.
/// </summary>
internal sealed class SessionKeycap : Decorator
{
    private readonly Func<IEnumerable<Control?>> covered;
    private readonly Control? accessible;
    private readonly TextBlock digit = new() { TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border cap = new();
    private readonly ScaleTransform scale = new(SessionNumbers.RestScale, SessionNumbers.RestScale);
    private readonly Dictionary<Control, IBrush> coveredMasks = new(ReferenceEqualityComparer.Instance);
    private SessionNumbers? numbers;
    private long? number;
    private bool numbered;

    /// <param name="threadID">The session the row opens.</param>
    /// <param name="covered">The row's marks under the badge (read each time it draws: a row rebuilds them).</param>
    /// <param name="accessible">The row's button, which carries the shortcut for assistive tech ("Ctrl+N").</param>
    public SessionKeycap(Guid threadID, Func<IEnumerable<Control?>> covered, Control? accessible)
    {
        ThreadID = threadID;
        this.covered = covered;
        this.accessible = accessible;
        IsHitTestVisible = false;
        Focusable = false;
        Opacity = 0;
        // :51 Chrome.iconSize, the leading slot's width: the badge centres on the mark it covers.
        Width = Chrome.IconSize;
        VerticalAlignment = VerticalAlignment.Center;
        SwiftFont.Caption.FontWeight(SwiftFont.Weight.Semibold).MonospacedDigit().Apply(digit);
        digit.Foreground = Chrome.PrimaryText.Opacity(0.92).Brush(digit);
        cap.Child = digit;
        cap.HorizontalAlignment = HorizontalAlignment.Center;
        cap.VerticalAlignment = VerticalAlignment.Center;
        cap.Padding = new Thickness(3, 0, 3, 0);
        // A keycap: the row's own overlay wash, a hairline edge, a slightly heavier lower lip.
        cap.Background = Chrome.Overlay(0.14).Brush(cap);
        cap.BorderBrush = Chrome.PrimaryText.Opacity(0.26).Brush(cap); // a firmer edge, so the key reads on the light sheet too
        cap.BorderThickness = new Thickness(1, 1, 1, 1.5);
        cap.RoundedRectangle(5, RoundedCornerStyle.Continuous);
        cap.RenderTransform = scale;
        cap.RenderTransformOrigin = RelativePoint.Center;
        Child = cap;
        // The keycap is as tall as the digit's line plus its lip, and never narrower than tall, at any text size.
        digit.SizeChanged += (_, _) => SizeCap();
        AttachedToVisualTree += (_, _) =>
        {
            try { numbers = AppEnvironment.GetAppModel(this).SessionNumbers; }
            catch (InvalidOperationException) { numbers = null; }
            numbers?.Register(this);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            numbers?.Unregister(this);
            numbers = null;
            Uncover();
        };
    }

    public Guid ThreadID { get; }

    /// <summary>The number shown, or null when the row has none.</summary>
    internal long? Number => number;

    /// <summary>Draws again from the shared presence: the row rebuilt a mark the badge covers.</summary>
    public void Refresh() => numbers?.Reapply(this);

    internal void Apply(double presence, long? next)
    {
        if (!numbered || next != number)
        {
            numbered = true;
            number = next;
            digit.Text = next?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (accessible is not null)
            {
                if (next is { } n) AutomationProperties.SetAcceleratorKey(accessible, "Ctrl+" + n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                else accessible.ClearValue(AutomationProperties.AcceleratorKeyProperty);
            }
        }
        var shown = next is null ? 0 : presence;
        Opacity = shown;
        var factor = SessionNumbers.RestScale + (1 - SessionNumbers.RestScale) * shown;
        scale.ScaleX = factor;
        scale.ScaleY = factor;
        if (shown <= 0)
        {
            Uncover();
            return;
        }
        // The marks under the badge fade out under it through an opacity mask, which multiplies whatever opacity the row
        // gives them (a hover, an activity fade, a crossfade) rather than replacing it: the row keeps owning their Opacity.
        var keep = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var mask = new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(255 * (1 - shown)), 0, 0, 0));
        foreach (var mark in covered())
        {
            if (mark is null) continue;
            // A mark with a mask of its own is left alone (the badge, opaque enough, sits over it).
            if (mark.OpacityMask is { } own && !(coveredMasks.TryGetValue(mark, out var ours) && ReferenceEquals(own, ours))) continue;
            keep.Add(mark);
            mark.OpacityMask = mask;
            coveredMasks[mark] = mask;
        }
        // A mark the row rebuilt away (or no longer covered) gets its own look back.
        foreach (var gone in coveredMasks.Keys.Where(mark => !keep.Contains(mark)).ToArray()) Release(gone);
    }

    private void Uncover()
    {
        foreach (var mark in coveredMasks.Keys.ToArray()) Release(mark);
    }

    private void Release(Control mark)
    {
        if (coveredMasks.Remove(mark, out var ours) && ReferenceEquals(mark.OpacityMask, ours)) mark.OpacityMask = null;
    }

    /// <summary>The share of the mark's drawing the badge lets through now: 1 uncovered, 0 hidden under a shown badge (tests).</summary>
    internal static double CoverOf(Control mark) =>
        mark.OpacityMask is ISolidColorBrush brush ? brush.Color.A / 255.0 * brush.Opacity : 1;

    private void SizeCap()
    {
        var height = Math.Ceiling(digit.DesiredSize.Height + 2.5);
        if (Math.Abs(cap.MinHeight - height) < 0.01) return;
        cap.MinHeight = height;
        cap.MinWidth = height;
    }
}
