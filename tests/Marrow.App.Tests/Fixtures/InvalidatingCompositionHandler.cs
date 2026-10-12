using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// ED-NEW-lean-9: a render-server visual that is invalidated at every refresh over <paramref name="bounds"/> and draws nothing
/// but one empty opacity layer.
/// Shown where a dot field would be, it makes the compositor do a tick's work for a dirty visual of the same area, so the
/// bytes of its ticks are Avalonia's own share of a tick with the dots, and the rest is the dot field's (allocation budgets).
/// </summary>
public sealed class InvalidatingCompositionHandler(Rect bounds) : CompositionCustomVisualHandler
{
    private int draws;

    /// <summary>Draws so far (render thread; headless: the test thread).</summary>
    public int Draws => Volatile.Read(ref draws);

    public override void OnMessage(object message) => Next();

    public override void OnAnimationFrameUpdate() => Next();

    private void Next()
    {
        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override Rect GetRenderBounds() => bounds;

    // One opacity layer, pushed and popped, as a dot field pushes them: Avalonia's per-draw cost of using one is in the reference.
    public override void OnRender(ImmediateDrawingContext context)
    {
        using (context.PushOpacity(0.5, bounds)) { }
        Interlocked.Increment(ref draws);
    }
}
