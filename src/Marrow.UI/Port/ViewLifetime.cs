using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

public static class Popovers
{
    public static Ambient<IPopoverPresenter> Presenter { get; } = new(new AvaloniaPopoverPresenter());

    private sealed class AvaloniaPopoverPresenter : IPopoverPresenter
    {
        public void SetPresented(Popup popup, bool presented) => popup.IsOpen = presented;
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

    public ForEach(Panel panel, Func<TItem, TId> id, Func<TItem, Control> make, Action<Control, TItem> update)
    {
        this.panel = panel;
        this.id = id;
        this.make = make;
        this.update = update;
    }

    public void Apply(IReadOnlyList<TItem> items)
    {
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
            panel.Children.Remove(row);
            (row as IDisposable)?.Dispose();
        }
        for (var i = 0; i < next.Count; i++)
        {
            var at = panel.Children.IndexOf(next[i]);
            if (at == i) continue;
            if (at >= 0) panel.Children.Move(at, i);
            else panel.Children.Insert(i, next[i]);
        }
    }
}
