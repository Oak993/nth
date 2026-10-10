using SkiaSharp;

namespace Marrow.UI.Port;

// ED-NEW-launch-fix (1): Boot B's first frame is the first thing the render thread links GPU programs for, so the app's
// program cache (Marrow.App's GpuProgramCache, which this assembly cannot name) must be installed on the render thread's
// display before it: the app sets ContextReady, and Boot B's handler calls it with each new GPU context, the context
// current, before it draws with it. Programs linked before the cache is installed are never handed to it to keep.
/// <summary>The app's hook for Boot B's GPU context (see the file note).</summary>
public static class AuroraBootGpu
{
    /// <summary>Called on the render thread with a GPU context current, before Boot B first draws with that context (once per
    /// context; never on the CPU renderer). Null: nothing to do (tests).</summary>
    public static Action<GRContext>? ContextReady { get; set; }

    /// <summary>Calls <paramref name="hook"/> when <paramref name="context"/> is a context it has not seen (the last one is
    /// kept in <paramref name="seen"/>); a hook that throws never stops the draw. True when it was a new context.</summary>
    internal static bool Arrive<T>(ref T? seen, T? context, Action<T>? hook) where T : class
    {
        if (context is null || ReferenceEquals(context, seen)) return false;
        seen = context;
        if (hook is null) return false;
        try { hook(context); }
        catch (Exception) { } // the cache is an optimisation: the frame is drawn regardless
        return true;
    }
}
