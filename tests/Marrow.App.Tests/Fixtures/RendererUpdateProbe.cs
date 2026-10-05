using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Watches a top level's UI-thread render updates (native runs). Avalonia 12.1.2's CompositingRenderer runs every
/// invalidation of a visual (InvalidateVisual, a render-affecting property, a layout move) as one UI-thread update: it
/// queues its <c>_update</c> action with the compositor and, when it runs, redraws the visuals in <c>_dirty</c>. The probe
/// wraps that action, so it sees every UI-thread update of the window and the visuals each one redrew. Read and dispose on
/// the UI thread.
/// </summary>
public sealed class RendererUpdateProbe : IDisposable
{
    private const BindingFlags Field = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly object renderer;
    private readonly FieldInfo update;
    private readonly Action original;
    private readonly List<Visual[]> updates = [];

    public RendererUpdateProbe(TopLevel top)
    {
        Dispatcher.UIThread.VerifyAccess();
        renderer = typeof(TopLevel).GetProperty("Renderer", Field)!.GetValue(top)
            ?? throw new InvalidOperationException("The top level has no renderer.");
        update = renderer.GetType().GetField("_update", Field) ?? throw new MissingFieldException(renderer.GetType().Name, "_update");
        var dirty = renderer.GetType().GetField("_dirty", Field) ?? throw new MissingFieldException(renderer.GetType().Name, "_dirty");
        original = (Action)update.GetValue(renderer)!;
        update.SetValue(renderer, (Action)(() =>
        {
            updates.Add(((HashSet<Visual>)dirty.GetValue(renderer)!).ToArray());
            original();
        }));
    }

    /// <summary>The visuals each UI-thread update since the probe was installed (or last cleared) redrew.</summary>
    public IReadOnlyList<Visual[]> Updates
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            return updates.ToArray();
        }
    }

    public void Clear()
    {
        Dispatcher.UIThread.VerifyAccess();
        updates.Clear();
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        update.SetValue(renderer, original);
    }
}
