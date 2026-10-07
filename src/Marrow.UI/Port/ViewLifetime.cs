using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>
/// PORT-ONLY. View modifiers whose lifetime is the control's time in the visual tree (ED-071): what SwiftUI starts on
/// appear and stops on disappear. Source: draft C (proto-C ViewSupport.cs).
/// </summary>
public static class ViewLifetime
{
    /// <summary>A SwiftUI body sub-expression that reads observable state: re-applied after anything it read changes.</summary>
    public static T Track<T, TValue>(this T control, Func<TValue> value, Action<TValue> apply) where T : Control
    {
        Tracked<TValue>? tracked = null;
        control.AttachedToVisualTree += (_, _) => tracked ??= new Tracked<TValue>(value, apply);
        control.DetachedFromVisualTree += (_, _) => { tracked?.Dispose(); tracked = null; };
        return control;
    }

    /// <summary><c>.task(id:)</c>: started on attach, cancelled on detach, restarted when the id changes.</summary>
    public static T Task<T, TId>(this T control, Func<TId> id, Func<TId, System.Threading.Tasks.Task> body) where T : Control
    {
        ViewTask<TId>? task = null;
        Tracked<TId>? idWatch = null;
        control.AttachedToVisualTree += (_, _) =>
        {
            task ??= new ViewTask<TId>(id(), body);
            task.Appear();
            idWatch ??= new Tracked<TId>(id, task.UpdateId);
        };
        control.DetachedFromVisualTree += (_, _) =>
        {
            idWatch?.Dispose();
            idWatch = null;
            task?.Disappear();
        };
        return control;
    }
}

/// <summary>
/// PORT-ONLY. SwiftUI <c>@Environment(\.key)</c>: inherited attached properties; the nearest ancestor that sets a
/// value wins, exactly as <c>.environment(\.key, value)</c>. One property per environment key the source reads;
/// the foundation carries the pattern and the one key it needs (<c>\.accessibilityReduceMotion</c>, fed from
/// Windows' "Show animations" setting, ED-084). The UI units add the others the same way.
/// </summary>
public sealed class EnvironmentValues
{
    private EnvironmentValues() { }

    public static readonly AttachedProperty<bool> AccessibilityReduceMotionProperty =
        AvaloniaProperty.RegisterAttached<EnvironmentValues, Control, bool>("AccessibilityReduceMotion", false, inherits: true);

    public static bool GetAccessibilityReduceMotion(Control control) => control.GetValue(AccessibilityReduceMotionProperty);

    public static void SetAccessibilityReduceMotion(Control control, bool value) => control.SetValue(AccessibilityReduceMotionProperty, value);

    // PORT-ONLY ED-218: App supplies its window's live resize read without an App dependency in UI.
    public static readonly AttachedProperty<Func<bool>?> WindowIsLiveResizingProperty =
        AvaloniaProperty.RegisterAttached<EnvironmentValues, Control, Func<bool>?>("WindowIsLiveResizing", null, inherits: true);
    public static bool GetWindowIsLiveResizing(Control control) => control.GetValue(WindowIsLiveResizingProperty)?.Invoke() ?? false;
    public static void SetWindowIsLiveResizing(Control control, Func<bool>? read) => control.SetValue(WindowIsLiveResizingProperty, read);
}

/// <summary>
/// PORT-ONLY. SwiftUI <c>.popover(isPresented:)</c> (and sheets and extra windows) go through one seam. The default
/// opens the Avalonia Popup; headless tests record instead, because Avalonia 12.1.2 headless cannot open a Popup
/// in a theme-less window ("Unable to create IPopupImpl and no overlay layer is found", draft C).
/// </summary>
public interface IPopoverPresenter
{
    void SetPresented(Popup popup, bool presented);
}

/// <summary>A surface with approved motion of its own can keep the native popover host and replace only its animation.</summary>
public interface IPopoverMotion
{
    void Open(Control content);
    Action Close(Control content, Action closed);
}

public static class Presentation
{
    public static Ambient<IPopoverPresenter> Presenter { get; } = new(new AvaloniaPopoverPresenter());

    public static readonly AttachedProperty<IPopoverMotion?> MotionProperty =
        AvaloniaProperty.RegisterAttached<Popup, IPopoverMotion?>("Motion", typeof(Presentation));
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Popup, Action> motionDismiss = new();

    internal static void DismissMotion(Popup popup)
    {
        if (motionDismiss.TryGetValue(popup, out var dismiss)) dismiss();
    }

    internal static void OpenMotion(Popup popup, Control content)
    {
        if (popup.GetValue(MotionProperty) is { } motion) motion.Open(content);
        else PopoverAppear.Play(content, popup.Placement);
    }

    internal static Action CloseMotion(Popup popup, Control content, Action closed) =>
        popup.GetValue(MotionProperty) is { } motion ? motion.Close(content, closed) : PopoverAppear.Dismiss(content, popup.Placement, closed);

    /// <summary>
    /// Presents or dismisses <paramref name="popup"/> through <see cref="Presenter"/> and, while it is presented, keeps it
    /// among the transients over <paramref name="anchor"/>'s window (ED-253): the window's Escape runs
    /// <paramref name="dismiss"/> (which sets the presenting state false) as NSPopover's own Escape closes it.
    /// </summary>
    public static void SetPresented(Popup popup, bool presented, Visual anchor, Action dismiss)
    {
        if (presented && popup.GetValue(MotionProperty) is not null) motionDismiss.AddOrUpdate(popup, dismiss);
        else motionDismiss.Remove(popup);
        if (presented) WindowTransients.Show(popup, anchor, dismiss);
        else WindowTransients.Hide(popup);
        Presenter.Current.SetPresented(popup, presented);
    }

    private sealed class AvaloniaPopoverPresenter : IPopoverPresenter
    {
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Popup, Action> closing = new();

        public void SetPresented(Popup popup, bool presented)
        {
            var wasClosing = closing.TryGetValue(popup, out var stop);
            if (wasClosing) { closing.Remove(popup); stop!(); }
            if (!presented && popup.IsOpen && popup.Child is { } shown)
            {
                // NSPopover shrinks back into its arrow before it goes (PopoverAppear.cs).
                closing.AddOrUpdate(popup, CloseMotion(popup, shown, () =>
                {
                    closing.Remove(popup);
                    popup.IsOpen = false;
                }));
                return;
            }
            var wasOpen = popup.IsOpen;
            if (presented) PopoverHost.Prepare(popup); // the anchor's appearance and the panel fill (ED-238)
            popup.IsOpen = presented;
            // NSPopover grows in from its arrow (PopoverAppear.cs); the child is final once the popup has opened.
            if (presented && (!wasOpen || wasClosing && popup.GetValue(MotionProperty) is not null) && popup.IsOpen && popup.Child is { } content) OpenMotion(popup, content);
        }
    }
}

/// <summary>
/// PORT-ONLY. SwiftUI <c>ForEach(items, id: \.id) { row }</c>, and the identity rules behind <c>if</c>/<c>else</c>
/// branches and <c>.id(x)</c>: a row whose id survives keeps its CONTROL (so its @State survives) and has its inputs
/// updated in place; a new id gets a new control; a vanished id's control is removed and disposed; children
/// follow the items' order. Rows are keyed by the Swift id, never by index. Source: draft C.
/// </summary>
public sealed class ForEach<TItem, TId> where TId : notnull
{
    private readonly Panel panel;
    private readonly Func<TItem, TId> id;
    private readonly Func<TItem, Control> make;
    private readonly Action<Control, TItem> update;
    private readonly Dictionary<TId, Control> rows = [];
    private readonly HashSet<Control> removing = [];
    private readonly Dictionary<Control, RowGlide> glides = [];
    private IReadOnlyList<Control> ordered = [];
    private EventHandler? nextLayout;

    /// <summary>ED-218: active panel children in item order, excluding lingering removal transitions.</summary>
    public IReadOnlyList<Control> Controls => ordered.Select(SwiftTransition.Container).ToArray();

    public ForEach(Panel panel, Func<TItem, TId> id, Func<TItem, Control> make, Action<Control, TItem> update)
    {
        this.panel = panel;
        this.id = id;
        this.make = make;
        this.update = update;
        panel.DetachedFromVisualTree += (_, _) =>
        {
            CancelLayout(); StopGlides();
            foreach (var row in removing.ToArray()) SwiftTransition.FinishRemoval(panel, row);
            foreach (var row in rows.Values) SwiftTransition.CancelInsertion(row);
        };
    }

    public void Apply(IReadOnlyList<TItem> items) => ApplyLayout(() => rows, () => ApplyRows(items));

    /// <summary>ED-218: reuse the same keyed surviving-row glide with a caller's update-or-rebuild reconciliation.</summary>
    public void ApplyLayout<TKey>(Func<IReadOnlyDictionary<TKey, Control>> controls, Action reconcile) where TKey : notnull
    {
        // ED-218 (MCPSettingsPage.swift:55-57): capture layout positions plus any unfinished glide before mutation.
        CancelLayout();
        var animation = Transaction.Current;
        var before = new Dictionary<TKey, Point>();
        if (animation is not null && CanGlide())
            foreach (var pair in controls())
            {
                var control = SwiftTransition.Container(pair.Value);
                if (control.Bounds.Width > 0 || control.Bounds.Height > 0)
                    before[pair.Key] = control.Bounds.Position + (glides.TryGetValue(control, out var glide) ? glide.Offset : default(Vector));
            }
        StopGlides();
        reconcile();
        if (before.Count == 0 || animation is null) return;
        nextLayout = (_, _) =>
        {
            CancelLayout();
            if (!CanGlide()) return;
            foreach (var pair in controls())
            {
                var control = SwiftTransition.Container(pair.Value);
                if (!before.TryGetValue(pair.Key, out var from)) continue;
                var offset = from - control.Bounds.Position;
                if (offset == default(Vector)) continue;
                glides[control] = new RowGlide(control, offset, animation, CanGlide, () => glides.Remove(control));
            }
        };
        panel.LayoutUpdated += nextLayout;
        panel.InvalidateArrange();
    }


    private void ApplyRows(IReadOnlyList<TItem> items)
    {
        var animation = Transaction.Current;
        var next = new List<Control>(items.Count);
        var seen = new HashSet<TId>();
        foreach (var item in items)
        {
            var key = id(item);
            // Duplicate ids: SwiftUI behaviour is undefined (UNVERIFIED O-18) and no source site relies on it; keep the first.
            if (!seen.Add(key)) continue;
            if (rows.TryGetValue(key, out var row)) update(row, item);
            else rows[key] = row = make(item);
            next.Add(row);
        }
        foreach (var gone in rows.Keys.Where(key => !seen.Contains(key)).ToArray())
        {
            var row = rows[gone];
            rows.Remove(gone);
            if (SwiftTransition.HasTransition(row) || animation is not null)
            {
                removing.Add(row);
                SwiftTransition.Remove(panel, row, () => { if (removing.Remove(row)) (row as IDisposable)?.Dispose(); });
            }
            else { panel.Children.Remove(row); (row as IDisposable)?.Dispose(); }
        }
        ordered = next;
        // A row on its way out stays where it stands, behind the row that came before it (SwiftUI draws a leaving view in place
        // while the others settle), so the rows that stay are placed round it, each straight after the one before it and after any
        // leaving rows that stood there (UI-COMPOSER-007: the leaving chip fades where it was, it does not slide to the end).
        var leaving = removing.Select(SwiftTransition.Container).ToHashSet();
        for (var i = 0; i < next.Count; i++)
        {
            var row = next[i]; var container = SwiftTransition.Container(row);
            var target = 0;
            if (i > 0)
            {
                target = panel.Children.IndexOf(SwiftTransition.Container(next[i - 1])) + 1;
                if (target == 0) target = i; // the one before it is not in the panel yet (a panel the caller fills): the plain position
            }
            while (target < panel.Children.Count && leaving.Contains(panel.Children[target]) && !ReferenceEquals(panel.Children[target], container)) target++;
            var at = panel.Children.IndexOf(container);
            if (at == target) continue;
            if (at >= 0) panel.Children.Move(at, at < target ? target - 1 : target);
            else if (SwiftTransition.HasTransition(row) || animation is not null) SwiftTransition.Insert(panel, target, row);
            else panel.Children.Insert(target, row);
        }
    }

    private bool CanGlide() => !EnvironmentValues.GetAccessibilityReduceMotion(panel) && !EnvironmentValues.GetWindowIsLiveResizing(panel);
    private void CancelLayout() { if (nextLayout is not null) panel.LayoutUpdated -= nextLayout; nextLayout = null; }
    private void StopGlides() { foreach (var glide in glides.Values.ToArray()) glide.Stop(); glides.Clear(); }

    // Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :52-58.
    // PORT-ONLY ED-218 FLIP: layout sizes are final immediately; translate the old position to the new one.
    // :54-58 The row's transition and its surviving-layout glide run together, including interrupted insertion.
    private sealed class RowGlide
    {
        private readonly Control control;
        private ITransform? original;
        private readonly TransformGroup transform = new();
        private readonly TranslateTransform translation;
        private readonly IFrameSource source = FrameClock.Source.Current;
        private readonly Action<TimeSpan> tick;
        private readonly Action finished;
        private bool stopped;
        public Vector Offset => new(translation.X, translation.Y);
        public RowGlide(Control control, Vector offset, Animation animation, Func<bool> allowed, Action finished)
        {
            this.control = control; this.finished = finished; original = control.RenderTransform;
            translation = new TranslateTransform(offset.X, offset.Y);
            ComposeTransform();
            control.PropertyChanged += TransformChanged;
            var start = SwiftClock.Now;
            tick = _ =>
            {
                if (stopped) return;
                var now = SwiftClock.Now;
                if (!allowed() || FrameClock.IsComplete(start, now, TimeSpan.FromSeconds(animation.Duration))) { Stop(); return; }
                var remaining = 1 - animation.Progress((now - start).TotalSeconds);
                translation.X = offset.X * remaining; translation.Y = offset.Y * remaining;
                source.RequestFrame(tick!); // IFrameSource never invokes during RequestFrame; constructor has assigned tick.
            };
            source.RequestFrame(tick);
        }

        // :54 + ED-218: SwiftTransition owns the base transform and may replace it on every insertion frame.
        // Keep that current transform beneath our layout offset; never restore a stale insertion snapshot.
        private void TransformChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property != Visual.RenderTransformProperty || ReferenceEquals(control.RenderTransform, transform)) return;
            original = control.RenderTransform;
            ComposeTransform();
        }

        private void ComposeTransform()
        {
            transform.Children.Clear();
            // Retain mutable transforms themselves so in-place updates also continue beneath the glide.
            if (original is Transform mutable) transform.Children.Add(mutable);
            else if (original is not null) transform.Children.Add(new MatrixTransform(original.Value));
            transform.Children.Add(translation);
            control.RenderTransform = transform;
        }

        public void Stop()
        {
            if (stopped) return;
            stopped = true; source.CancelFrame(tick);
            control.PropertyChanged -= TransformChanged;
            control.RenderTransform = original;
            finished();
        }
    }
}
