// PORT-ONLY: DESIGN 2.5 render budget; ED-NEW-dextra-smooth-2. Reuses the event-driven native-window observer.
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Marrow.UI.Port.Armilla;

/// <summary>Visibility of an attached Dextra view, including its ancestors and native window. No polling.</summary>
public sealed class DextraViewWatch : IDisposable
{
    private readonly Control view;
    private readonly List<Visual> ancestors;
    private readonly AmbientWindowState? window;
    private bool available, disposed;

    public DextraViewWatch(Control view)
    {
        this.view = view;
        ancestors = [view, .. view.GetVisualAncestors()];
        window = AmbientWindowState.Acquire(view);
        if (window is not null) window.Changed += Refresh;
        foreach (var ancestor in ancestors) ancestor.PropertyChanged += OnProperty;
        available = Read();
    }

    public bool Available => available;
    public event Action? Changed;

    private bool Read() => view.IsEffectivelyVisible && TopLevel.GetTopLevel(view) is not null && (window?.Seen ?? true);
    private void OnProperty(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty) Refresh();
    }
    private void Refresh()
    {
        if (disposed) return;
        var next = Read();
        if (next == available) return;
        available = next;
        Changed?.Invoke();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var ancestor in ancestors) ancestor.PropertyChanged -= OnProperty;
        if (window is not null) { window.Changed -= Refresh; window.Dispose(); }
        ancestors.Clear();
    }
}
