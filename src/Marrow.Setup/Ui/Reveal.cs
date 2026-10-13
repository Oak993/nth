using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Setup.Motion;
using Marrow.Setup.Theme;

namespace Marrow.Setup.Ui;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3b; the canvas's <c>RV</c>; unit U2, ED-NEW-inst-u2-1). Lines that come and go inside a
/// screen, one behaviour for the whole window. The layout changes only at a commit. Before it, whatever leaves, and any
/// element whose words change, fades out (0.16 s, critically damped, with a 4 DIP lift); a screen with nothing leaving
/// commits at once. At the commit every element of the screen's stack (and every button in its rows) is measured before
/// and after the change, in its parent's coordinates (a TranslateTransform is not part of the layout, so the offset
/// Reveal applies is never measured), the difference is added to its offset so it is drawn exactly where it was, and the
/// offset glides home on one critically damped spring (response 0.38 s, the Nocta switch's), keeping its speed. What
/// arrives rides with the element above it and fades in 60 ms after the commit (0.24 s) with a 5 DIP settle from above
/// (response 0.3 s); text whose words changed fades back the same way, without the settle. Interruptible: an element keeps
/// its offset, opacity and their speeds, a new change adds the next difference to the current offset, and a line asked to
/// leave and come back before it has faded simply fades back. Only <c>RenderTransform</c> (a TranslateTransform, composed
/// with any transform the element has) and <c>Opacity</c> are written. Calm: nothing moves; a line with a reserved place
/// keeps it while hidden (opacity 0, not hit-testable, hidden from Narrator) and only fades (0.15 s); anything else that
/// the change would move (found by a dry measure) fades out first, the layout changes, and it fades back in its new place.
/// A line that appears inside a polite live region makes the region announce it.
/// </summary>
public sealed class Reveal : IMotion
{
    public const double Response = 0.38, SettleResponse = 0.3, FadeIn = 0.24, FadeOut = 0.16, CalmFade = 0.15;
    public const double ArrivalDelay = 0.06, SettleFrom = -5, LeaveLift = -4, Gone = 0.04;

    internal const double SubStep = 1.0 / 240;

    /// <summary>
    /// The fastest a glide moves: 2.25 DIP in a 60 Hz frame, so a glide with the 5 DIP settle on top stays within the plan's
    /// 3 DIP a frame (PLAN.md 3b). A long glide (a centred block after a tall line comes) starts and ends on the spring and
    /// cruises at this speed in between.
    /// </summary>
    public const double MaxGlideSpeed = 135;

    /// <summary>The longest an arriving line waits for a glide to carry it clear of the buttons before it fades in anyway.</summary>
    public const double MaxRoomWait = 1.0;

    private readonly Control screen;
    private readonly Panel stack;
    private readonly Func<bool> calm;
    private readonly List<Panel> rows = [];
    private readonly Dictionary<Control, Item> items = [];
    private readonly List<Item> live = [];
    private readonly List<Item> pending = [];
    private readonly List<Action> atCommit = [];
    private Animator? animator;
    private List<Control>? calmMovers;
    private int pendingVersion, moversVersion = -1;
    private int instant;

    /// <param name="screen">The screen's root (laid out synchronously at a commit).</param>
    /// <param name="stack">The screen's stack: each child (but the flexible spaces) is tracked.</param>
    /// <param name="calm">Animation effects off or reduced motion, read at every change.</param>
    public Reveal(Control screen, Panel stack, Func<bool> calm)
    {
        this.screen = screen ?? throw new ArgumentNullException(nameof(screen));
        this.stack = stack ?? throw new ArgumentNullException(nameof(stack));
        this.calm = calm ?? throw new ArgumentNullException(nameof(calm));
    }

    /// <summary>Raised when a line arrives inside a polite live region (the region, which then carries the line's words as its name).</summary>
    public event Action<Control>? LiveRegionChanged;

    /// <summary>True while a change waits for its commit or anything still moves.</summary>
    public bool IsBusy => pending.Count > 0 || live.Count > 0;

    /// <summary>The frame loop (the window's). Without one every change is made at once.</summary>
    public void Attach(Animator frames) => animator = frames;

    /// <summary>Also tracks the children of <paramref name="row"/> (a button row, horizontally, or a line's container).</summary>
    public void Track(Panel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!rows.Contains(row)) rows.Add(row);
    }

    /// <summary>Makes <paramref name="container"/> a polite live region: a line that arrives in it is announced.</summary>
    public static T LiveRegion<T>(T container) where T : Control
    {
        ArgumentNullException.ThrowIfNull(container);
        AutomationProperties.SetLiveSetting(container, AutomationLiveSetting.Polite);
        return container;
    }

    /// <summary>Sets a screen up at once (before it fades in): every change inside <paramref name="setup"/> happens now, with no motion.</summary>
    public void Now(Action setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        instant++;
        try { setup(); }
        finally { instant--; }
    }

    /// <summary>Drops every offset, fade and pending change (a screen entered again).</summary>
    public void Reset()
    {
        foreach (var item in items.Values.ToArray()) Clear(item);
        pending.Clear();
        live.Clear();
        RunAtCommit();
    }

    /// <summary>
    /// Runs <paramref name="action"/> at the next commit, with the changes waiting for it (the canvas's <c>RV.at</c>): a line
    /// that comes by itself, such as the paused line, takes its place when the lines leaving have gone. At once when nothing waits.
    /// </summary>
    public void AtCommit(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (instant > 0 || pending.Count == 0 || animator is null || TopLevel.GetTopLevel(screen) is null)
        {
            action();
            return;
        }
        atCommit.Add(action);
    }

    private void RunAtCommit()
    {
        if (atCommit.Count == 0) return;
        var actions = atCommit.ToArray();
        atCommit.Clear();
        foreach (var action in actions) action();
    }

    /// <summary>Shows <paramref name="element"/> at the next commit; it fades in (unless <paramref name="fade"/> is false) riding with the element above it.</summary>
    public void Show(Control element, bool fade = true)
    {
        ArgumentNullException.ThrowIfNull(element);
        var item = ItemOf(element);
        if (instant > 0)
        {
            Clear(item);
            element.IsVisible = true;
            return;
        }
        if (item.ReservedOff)
        {
            // Calm kept its place: it only fades back
            item.ReservedOff = false;
            Restore(element);
            Own(item);
            item.Wait = -1;
            item.OpacityTarget = 1;
            Run(item);
            Announce(element);
            return;
        }
        if (item.Op is { Visible: false } leaving)
        {
            // asked back before it has gone: it simply fades back, no layout change
            leaving.Visible = null;
            if (leaving.IsEmpty) Drop(item);
            item.OpacityTarget = 1;
            item.Wait = -1;
            item.SettleTarget = 0;
            Bump();
            Run(item);
            return;
        }
        if (element.IsVisible) return;
        var op = OpOf(item);
        op.Visible = true;
        op.Fade = fade;
        Bump();
        Run(item);
    }

    /// <summary>
    /// Hides <paramref name="element"/>: it fades out with a small lift, then the layout changes. With <paramref name="reserve"/>
    /// in Calm it keeps its place and only fades.
    /// </summary>
    public void Hide(Control element, bool reserve = false)
    {
        ArgumentNullException.ThrowIfNull(element);
        var item = ItemOf(element);
        if (instant > 0)
        {
            Clear(item);
            if (reserve && calm())
            {
                element.IsVisible = true;
                Own(item);
                item.Opacity = item.OpacityTarget = 0;
                item.ReservedOff = true;
                Withdraw(element);
                Paint(item);
            }
            else element.IsVisible = false;
            return;
        }
        if (reserve && calm() && element.IsVisible && !item.ReservedOff)
        {
            Drop(item);
            Own(item);
            item.Settle = item.SettleTarget = item.SettleVelocity = 0;
            item.Wait = -1;
            item.ReservedOff = true;
            item.OpacityTarget = 0;
            Run(item);
            return;
        }
        if (!element.IsVisible)
        {
            if (item.Op is { Visible: true } showing)
            {
                // asked to leave before it arrived: nothing happens
                showing.Visible = null;
                ApplyChanges(item);
                Drop(item);
                Bump();
            }
            return;
        }
        if (item.Op is { Visible: false }) return;
        var op = OpOf(item);
        op.Visible = false;
        Own(item);
        item.SettleTarget = calm() ? 0 : LeaveLift;
        Bump();
        Run(item);
    }

    /// <summary>Shows or hides <paramref name="element"/>.</summary>
    public void Set(Control element, bool shown, bool reserve = false)
    {
        if (shown) Show(element);
        else Hide(element, reserve);
    }

    /// <summary>Changes the words of <paramref name="block"/>: the old words fade out, the layout changes, the new ones fade in.</summary>
    public void SetText(TextBlock block, string text)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(text);
        var item = ItemOf(block);
        if (instant > 0)
        {
            Clear(item);
            block.Text = text;
            return;
        }
        var future = item.Op?.Text ?? block.Text ?? "";
        if (string.Equals(future, text, StringComparison.Ordinal)) return;
        if (!block.IsEffectivelyVisible && item.Op is not { Visible: true })
        {
            block.Text = text;
            return;
        }
        var op = OpOf(item);
        op.Text = string.Equals(text, block.Text, StringComparison.Ordinal) ? null : text;
        if (op.IsEmpty)
        {
            Drop(item);
            item.OpacityTarget = 1;
            item.Wait = -1;
        }
        else Own(item);
        Bump();
        Run(item);
    }

    /// <summary>
    /// Makes <paramref name="change"/> at the next commit (Cancel becoming the primary Close). With <paramref name="fade"/>
    /// the element fades out before and back in after, like a change of words. <paramref name="undo"/> lets Calm's dry
    /// measure try the change and take it back; without it Calm treats everything in the screen as moving.
    /// </summary>
    public void Apply(Control element, Action change, Action? undo = null, bool fade = false)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(change);
        var item = ItemOf(element);
        if (instant > 0 || (!element.IsEffectivelyVisible && item.Op is null))
        {
            change();
            return;
        }
        var op = OpOf(item);
        op.Changes.Add((change, undo));
        op.FadeChange |= fade;
        if (fade) Own(item);
        Bump();
        Run(item);
    }

    /// <summary>
    /// A line whose words change in place: it keeps the height (and width) of its longest wording at the current width and
    /// text size, so nothing moves at all, and its words cross-fade (<see cref="SetText"/>). Returns the host to place in
    /// the layout instead of <paramref name="block"/>.
    /// </summary>
    public static ReserveBox Reserve(TextBlock block, TextRole role, params string[] wordings)
    {
        ArgumentNullException.ThrowIfNull(block);
        var box = new ReserveBox(block, role);
        box.SetWordings(wordings);
        return box;
    }

    /// <summary>The element's drawn offset (x, y) in DIP and opacity, for tests.</summary>
    public (double X, double Y, double Opacity) Pose(Control element)
    {
        if (!items.TryGetValue(element, out var item)) return (0, 0, element.Opacity);
        return (item.OffsetX, item.OffsetY + item.Settle, item.Own ? Math.Clamp(item.Opacity, 0, 1) : element.Opacity);
    }

    bool IMotion.Advance(double seconds)
    {
        if (pending.Count > 0) Check();
        var quiet = calm();
        foreach (var item in live.ToArray())
        {
            var dt = seconds;
            if (item.Wait <= 0) item.RoomWait = 0;
            if (item.Wait > 0)
            {
                item.Wait -= dt;
                if (item.Wait <= 0)
                {
                    // a line a long glide is still carrying over the buttons waits (its settle with it) until it is clear
                    if (item.RoomWait < MaxRoomWait && !ClearOfButtons(item))
                    {
                        item.Wait = 1e-9;
                        item.RoomWait += dt;
                    }
                    else
                    {
                        item.Wait = -1;
                        item.RoomWait = 0;
                        item.OpacityTarget = 1;
                    }
                }
            }
            var steps = Math.Max(1, (int)Math.Ceiling(dt / SubStep));
            var h = dt / steps;
            var fade = quiet ? CalmFade : item.OpacityTarget >= item.Opacity ? FadeIn : FadeOut;
            for (var i = 0; i < steps; i++)
            {
                Glide(ref item.OffsetX, ref item.VelocityX, h);
                Glide(ref item.OffsetY, ref item.VelocityY, h);
                if (item.ExtraY != 0 || item.ExtraVelocity != 0) Glide(ref item.ExtraY, ref item.ExtraVelocity, h);
                if (item.RoomWait <= 0 || item.Wait <= 0) SpringStep(ref item.Settle, ref item.SettleVelocity, item.SettleTarget, SettleResponse, h);
                if (item.Own) SpringStep(ref item.Opacity, ref item.OpacityVelocity, item.OpacityTarget, fade, h);
            }
            var still = Rest(ref item.OffsetX, ref item.VelocityX, 0, .01, .05)
                & Rest(ref item.OffsetY, ref item.VelocityY, 0, .01, .05)
                & Rest(ref item.ExtraY, ref item.ExtraVelocity, 0, .01, .05)
                & Rest(ref item.Settle, ref item.SettleVelocity, item.SettleTarget, .01, .05)
                & (!item.Own || Rest(ref item.Opacity, ref item.OpacityVelocity, item.OpacityTarget, .002, .01));
            if (item.ReservedOff && item.Opacity < Gone) Withdraw(item.Element);
            Paint(item);
            if (still && item.Wait < 0 && item.Op is null && !item.Dip)
            {
                if (item.Own && item.Opacity >= 1)
                {
                    item.Own = false;
                    item.Element.Opacity = 1;
                }
                item.Live = false;
                live.Remove(item);
            }
        }
        return live.Count > 0 || pending.Count > 0;
    }

    // ---- the commit ----

    private void Check()
    {
        if (TopLevel.GetTopLevel(screen) is null)
        {
            // not in a window: nothing can be measured or seen, so the change is made at once
            foreach (var item in pending.ToArray()) ApplyOp(item, out _);
            pending.Clear();
            foreach (var item in items.Values) Clear(item);
            RunAtCommit();
            return;
        }
        var wait = false;
        foreach (var item in pending)
        {
            if (!NeedsOut(item)) continue;
            Own(item);
            item.OpacityTarget = 0;
            item.Wait = -1;
            Run(item);
            if (item.Opacity > Gone) wait = true;
        }
        if (calm())
        {
            if (calmMovers is null || moversVersion != pendingVersion)
            {
                calmMovers = Movers();
                moversVersion = pendingVersion;
            }
            foreach (var element in calmMovers)
            {
                var item = ItemOf(element);
                Own(item);
                item.Dip = true;
                item.OpacityTarget = 0;
                item.Wait = -1;
                Run(item);
                if (item.Opacity > Gone) wait = true;
            }
        }
        if (!wait) Commit();
    }

    private bool NeedsOut(Item item)
    {
        var op = item.Op!;
        if (!item.Element.IsEffectivelyVisible || item.ReservedOff) return false;
        return op.Visible == false || op.Text is not null || op.FadeChange;
    }

    private void Commit()
    {
        var quiet = calm();
        var tracked = Tracked();
        var before = new Dictionary<Control, Point>();
        var heights = new Dictionary<Control, double>();
        foreach (var element in tracked)
        {
            if (!Rendered(element)) continue;
            before[element] = element.Bounds.Position;
            if (element is IGlidesHeight) heights[element] = element.Bounds.Height;
        }
        var arrived = new Dictionary<Item, string>();
        foreach (var item in pending.ToArray())
        {
            var op = item.Op!;
            ApplyOp(item, out var kind);
            if (kind is not null) arrived[item] = kind;
        }
        pending.Clear();
        RunAtCommit();
        calmMovers = null;
        screen.UpdateLayout();
        foreach (var element in tracked)
        {
            var item = ItemOf(element);
            arrived.TryGetValue(item, out var kind);
            if (!Rendered(element))
            {
                if (before.ContainsKey(element) || kind is not null) Clear(item);
                continue;
            }
            var after = element.Bounds.Position;
            if (!before.TryGetValue(element, out var was))
            {
                var ride = Ride(element);
                item.OffsetX = item.VelocityX = 0;
                item.OffsetY = quiet ? 0 : ride.OffsetY;
                item.VelocityY = quiet ? 0 : ride.VelocityY;
                if (kind == "in")
                {
                    Own(item);
                    item.Opacity = item.OpacityVelocity = item.OpacityTarget = 0;
                    item.Wait = quiet ? 0.0001 : ArrivalDelay;
                    item.Settle = quiet ? 0 : SettleFrom;
                    item.SettleVelocity = item.SettleTarget = 0;
                }
                Announce(element);
            }
            else
            {
                if (!quiet)
                {
                    if (kind == "swap") item.OffsetX = item.VelocityX = 0;
                    else item.OffsetX += was.X - after.X;
                    item.OffsetY += was.Y - after.Y;
                    // a card whose rows moved draws its edge where it was and carries it home with them (same spring, same start)
                    if (heights.TryGetValue(element, out var wasHeight)) item.ExtraY += wasHeight - element.Bounds.Height;
                }
                if (kind == "swap")
                {
                    item.Wait = quiet ? 0.0001 : ArrivalDelay;
                    item.SettleTarget = 0;
                }
                if (item.Dip)
                {
                    item.Dip = false;
                    item.OpacityTarget = 1;
                    item.Wait = -1;
                }
            }
            Run(item);
            Paint(item);
        }
    }

    /// <summary>Applies a pending change; <paramref name="kind"/> is "in", "pop", "out" or "swap" (null when nothing visible changed).</summary>
    private void ApplyOp(Item item, out string? kind)
    {
        var op = item.Op!;
        kind = null;
        foreach (var (change, _) in op.Changes) change();
        if (op.Text is not null && item.Element is TextBlock block) block.Text = op.Text;
        if (op.Visible == true)
        {
            item.Element.IsVisible = true;
            kind = op.Fade ? "in" : "pop";
        }
        else if (op.Visible == false)
        {
            item.Element.IsVisible = false;
            kind = "out";
        }
        else if (op.Text is not null || op.FadeChange) kind = "swap";
        item.Op = null;
    }

    private void ApplyChanges(Item item)
    {
        if (item.Op is null) return;
        foreach (var (change, _) in item.Op.Changes) change();
        item.Op.Changes.Clear();
        if (item.Op.Text is not null && item.Element is TextBlock block) block.Text = item.Op.Text;
        item.Op.Text = null;
    }

    /// <summary>Calm's dry measure: the elements the pending change would move, found by trying it and taking it back.</summary>
    private List<Control> Movers()
    {
        var tracked = Tracked();
        var before = new Dictionary<Control, Point>();
        foreach (var element in tracked)
            if (Rendered(element)) before[element] = element.Bounds.Position;
        var reversible = pending.All(p => p.Op!.Changes.All(c => c.Undo is not null));
        if (!reversible)
            return [.. tracked.Where(e => Rendered(e) && !pending.Exists(p => p.Element == e) && items.GetValueOrDefault(e)?.ReservedOff != true)];
        var undo = new List<Action>();
        foreach (var item in pending)
        {
            var op = item.Op!;
            var element = item.Element;
            foreach (var (change, back) in op.Changes)
            {
                change();
                undo.Add(back!);
            }
            if (op.Text is not null && element is TextBlock block)
            {
                var old = block.Text;
                block.Text = op.Text;
                undo.Add(() => block.Text = old);
            }
            if (op.Visible is { } visible)
            {
                var old = element.IsVisible;
                element.IsVisible = visible;
                undo.Add(() => element.IsVisible = old);
            }
        }
        screen.UpdateLayout();
        var moved = new List<Control>();
        foreach (var element in tracked)
        {
            if (!before.TryGetValue(element, out var was) || !Rendered(element)) continue;
            if (pending.Exists(p => p.Element == element)) continue;
            var now = element.Bounds.Position;
            if (Math.Abs(now.X - was.X) > .5 || Math.Abs(now.Y - was.Y) > .5) moved.Add(element);
        }
        for (var i = undo.Count - 1; i >= 0; i--) undo[i]();
        screen.UpdateLayout();
        return moved;
    }

    /// <summary>The elements whose place is tracked: the stack's children, the rows' children and anything with a pending change.</summary>
    private List<Control> Tracked()
    {
        var all = new List<Control>();
        foreach (var child in stack.Children)
            if (child is not Grow && !ScreenColumn.GetIsGhost(child)) all.Add(child);
        foreach (var row in rows)
            foreach (var child in row.Children)
                if (!all.Contains(child)) all.Add(child);
        foreach (var item in pending)
            if (!all.Contains(item.Element)) all.Add(item.Element);
        return all;
    }

    /// <summary>The offset an arriving element rides with: the element drawn above it in the same parent.</summary>
    private (double OffsetY, double VelocityY) Ride(Control element)
    {
        if (element.GetVisualParent() is not Panel parent) return (0, 0);
        var index = parent.Children.IndexOf(element);
        for (var i = index - 1; i >= 0; i--)
        {
            var above = parent.Children[i];
            // a flexible space stands still (the canvas's .grow has no motion of its own), so a line under one rides with nothing
            if (above is Grow && above.IsVisible) return (0, 0);
            if (!Rendered(above) || ScreenColumn.GetIsGhost(above)) continue;
            return items.TryGetValue(above, out var item) ? (item.OffsetY, item.VelocityY) : (0, 0);
        }
        return (0, 0);
    }

    private void Announce(Control element)
    {
        for (Visual? at = element; at is not null && !ReferenceEquals(at, screen); at = at.GetVisualParent())
        {
            if (at is not Control region || AutomationProperties.GetLiveSetting(region) == AutomationLiveSetting.Off) continue;
            var words = element is TextBlock block ? block.Text : AutomationProperties.GetName(element);
            if (!string.IsNullOrEmpty(words)) AutomationProperties.SetName(region, words);
            LiveRegionChanged?.Invoke(region);
            return;
        }
    }

    // ---- per element ----

    private Item ItemOf(Control element)
    {
        if (items.TryGetValue(element, out var item)) return item;
        item = new Item(element);
        items.Add(element, item);
        return item;
    }

    private Op OpOf(Item item)
    {
        if (item.Op is not null) return item.Op;
        item.Op = new Op();
        pending.Add(item);
        return item.Op;
    }

    private void Drop(Item item)
    {
        if (item.Op is null) return;
        pending.Remove(item);
        item.Op = null;
    }

    private void Bump() => pendingVersion++;

    private static void Own(Item item)
    {
        if (item.Own) return;
        item.Opacity = item.OpacityTarget = Math.Clamp(item.Element.Opacity, 0, 1);
        item.OpacityVelocity = 0;
        item.Own = true;
    }

    private void Clear(Item item)
    {
        Drop(item);
        item.OffsetX = item.VelocityX = item.OffsetY = item.VelocityY = 0;
        item.ExtraY = item.ExtraVelocity = 0;
        item.Settle = item.SettleVelocity = item.SettleTarget = 0;
        item.OpacityVelocity = 0;
        item.Wait = -1;
        item.RoomWait = 0;
        item.Dip = false;
        if (item.Own) item.Element.Opacity = 1;
        item.Opacity = item.OpacityTarget = 1;
        item.Own = false;
        if (item.ReservedOff) Restore(item.Element);
        item.ReservedOff = false;
        Paint(item);
        if (item.Live)
        {
            item.Live = false;
            live.Remove(item);
        }
    }

    private void Run(Item item)
    {
        if (!item.Live)
        {
            item.Live = true;
            live.Add(item);
        }
        if (animator is null)
        {
            // no frame loop: straight to the end state
            if (pending.Count > 0)
            {
                foreach (var p in pending.ToArray()) ApplyOp(p, out _);
                pending.Clear();
                RunAtCommit();
            }
            foreach (var each in live.ToArray())
            {
                var reserved = each.ReservedOff;
                var opacity = each.Own ? (each.Op is null && each.Wait < 0 ? each.OpacityTarget : 1) : 1;
                Clear(each);
                if (reserved)
                {
                    Own(each);
                    each.Opacity = each.OpacityTarget = 0;
                    each.ReservedOff = true;
                    Withdraw(each.Element);
                }
                else if (opacity < 1 && each.Element.IsVisible)
                {
                    Own(each);
                    each.Opacity = each.OpacityTarget = opacity;
                }
                Paint(each);
            }
            live.Clear();
            return;
        }
        animator.Start(this);
    }

    private static void Paint(Item item)
    {
        var x = item.OffsetX;
        var y = item.OffsetY + item.Settle;
        if (Math.Abs(x) < .005 && Math.Abs(y) < .005) x = y = 0;
        else
        {
            x = Math.Round(x * 100) / 100;
            y = Math.Round(y * 100) / 100;
        }
        if (item.Shift is not null || x != 0 || y != 0)
        {
            var shift = item.Shift ??= Attach(item.Element);
            shift.X = x;
            shift.Y = y;
        }
        if (item.Own) item.Element.Opacity = Math.Clamp(item.Opacity, 0, 1);
        if (item.Element is IGlidesHeight card) card.HeightExtra = Math.Abs(item.ExtraY) < .005 ? 0 : Math.Round(item.ExtraY * 100) / 100;
    }

    /// <summary>The element's TranslateTransform, composed with a transform it already has (the canvas's <c>translate</c>).</summary>
    private static TranslateTransform Attach(Control element)
    {
        var shift = new TranslateTransform();
        switch (element.RenderTransform)
        {
            case null:
                element.RenderTransform = shift;
                break;
            case TransformGroup group:
                group.Children.Add(shift);
                break;
            case Transform own:
                element.RenderTransform = new TransformGroup { Children = { own, shift } };
                break;
            default:
                element.RenderTransform = shift;
                break;
        }
        return shift;
    }

    private static void Withdraw(Control element)
    {
        element.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
    }

    private static void Restore(Control element)
    {
        element.IsHitTestVisible = true;
        element.ClearValue(AutomationProperties.AccessibilityViewProperty);
    }

    private static bool Rendered(Control element) => element.IsEffectivelyVisible && TopLevel.GetTopLevel(element) is not null;

    /// <summary>
    /// Whether <paramref name="item"/>'s element, on the way of its settle from where it is drawn now, is clear of the screen's
    /// buttons (for a button: of every line still drawn in the column, such as a paused line on its way out). Its glide
    /// carries it straight from here to its laid-out place, which is clear, so it stays clear on the way.
    /// </summary>
    private bool ClearOfButtons(Item item)
    {
        var element = item.Element;
        var row = stack.Children.OfType<ButtonRow>().FirstOrDefault(r => r.IsVisible);
        if (row is null || Drawn(element) is not { } drawn) return true;
        // where it is drawn now and where its settle takes it: the whole way of the settle
        var mine = drawn.Union(drawn.Translate(new Vector(0, -item.Settle)));
        var others = row.IsVisualAncestorOf(element)
            ? stack.Children.Where(c => c is not (Grow or ButtonRow) && !ScreenColumn.GetIsGhost(c))
            : row.Children.Cast<Control>();
        foreach (var other in others)
        {
            if (!other.IsVisible || other.Bounds.Height <= 0 || other.Opacity <= .01 || Drawn(other) is not { } box) continue;
            var overlap = mine.Intersect(box);
            if (overlap.Width > .5 && overlap.Height > .5) return false;
        }
        return true;
    }

    private Rect? Drawn(Control element) =>
        element.TransformToVisual(stack) is { } m ? new Rect(element.Bounds.Size).TransformToAABB(m) : null;

    private static void SpringStep(ref double value, ref double velocity, double target, double response, double h)
    {
        var omega = 2 * Math.PI / response;
        var acceleration = -omega * omega * (value - target) - 2 * omega * velocity;
        velocity += acceleration * h;
        value += velocity * h;
    }

    /// <summary>One step of a glide toward 0 on the critically damped spring (response <see cref="Response"/>), at no more than <see cref="MaxGlideSpeed"/> (the window's height glides on it too).</summary>
    internal static void Glide(ref double offset, ref double velocity, double h)
    {
        var omega = 2 * Math.PI / Response;
        velocity += (-omega * omega * offset - 2 * omega * velocity) * h;
        velocity = Math.Clamp(velocity, -MaxGlideSpeed, MaxGlideSpeed);
        offset += velocity * h;
    }

    internal static bool Rest(ref double value, ref double velocity, double target, double within, double slow)
    {
        if (Math.Abs(value - target) >= within || Math.Abs(velocity) >= slow) return false;
        value = target;
        velocity = 0;
        return true;
    }

    private sealed class Item(Control element)
    {
        public readonly Control Element = element;
        public TranslateTransform? Shift;
        public double OffsetX, VelocityX, OffsetY, VelocityY;
        public double ExtraY, ExtraVelocity; // a card's drawn height less its laid-out one (IGlidesHeight)
        public double Settle, SettleVelocity, SettleTarget;
        public double Opacity = 1, OpacityVelocity, OpacityTarget = 1;
        public double Wait = -1;
        public double RoomWait;
        public bool Own, Dip, ReservedOff, Live;
        public Op? Op;
    }

    private sealed class Op
    {
        public bool? Visible;
        public bool Fade = true;
        public string? Text;
        public bool FadeChange;
        public readonly List<(Action Change, Action? Undo)> Changes = [];

        public bool IsEmpty => Visible is null && Text is null && Changes.Count == 0 && !FadeChange;
    }
}

/// <summary>
/// A line whose words change in place (PLAN.md 3b, "Reserved, not animated"): the visible block over invisible copies of
/// every wording, so its height (and width) is that of its longest wording at the current width and text size, measured
/// at layout and again whenever the width, DPI or text size changes. The copies are not hit-testable and Narrator does not
/// see them.
/// </summary>
public sealed class ReserveBox : Panel
{
    private readonly TextRole role;

    public ReserveBox(TextBlock block, TextRole role)
    {
        Block = block ?? throw new ArgumentNullException(nameof(block));
        this.role = role;
        Children.Add(block);
        HorizontalAlignment = block.HorizontalAlignment;
        Margin = block.Margin;
        block.Margin = default;
        MaxWidth = block.MaxWidth;
    }

    public TextBlock Block { get; }

    /// <summary>The wordings whose longest the line keeps the place of.</summary>
    public IReadOnlyList<string> Wordings { get; private set; } = [];

    /// <summary>Replaces the wordings (the chosen install path).</summary>
    public void SetWordings(params string[] wordings)
    {
        Wordings = wordings ?? [];
        for (var i = Children.Count - 1; i >= 0; i--)
            if (!ReferenceEquals(Children[i], Block)) Children.RemoveAt(i);
        foreach (var words in Wordings)
        {
            var ghost = SetupTheme.Text(new TextBlock
            {
                Text = words,
                TextWrapping = Block.TextWrapping,
                TextAlignment = Block.TextAlignment,
                HorizontalAlignment = Block.HorizontalAlignment,
                Opacity = 0,
                IsHitTestVisible = false,
            }, role, ink: null);
            AutomationProperties.SetAccessibilityView(ghost, AccessibilityView.Raw);
            Children.Insert(0, ghost);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size();
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            size = new Size(Math.Max(size.Width, child.DesiredSize.Width), Math.Max(size.Height, child.DesiredSize.Height));
        }
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var width = child.HorizontalAlignment == HorizontalAlignment.Stretch ? finalSize.Width : Math.Min(finalSize.Width, child.DesiredSize.Width);
            var x = child.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => (finalSize.Width - width) / 2,
                HorizontalAlignment.Right => finalSize.Width - width,
                _ => 0,
            };
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
        }
        return finalSize;
    }
}

/// <summary>
/// A card whose drawn height can differ from its laid-out one (the glass group, ED-NEW-inst-picks-1): when a change at a commit
/// makes the card taller or shorter, <see cref="Reveal"/> sets <see cref="HeightExtra"/> to the drawn height less the new one and
/// carries it to 0 on the glide that carries the card's rows, so the card's edge moves with them instead of jumping.
/// </summary>
public interface IGlidesHeight
{
    /// <summary>The drawn height less the laid-out one (DIP): negative while the card is still catching up with a taller layout.</summary>
    double HeightExtra { get; set; }
}
