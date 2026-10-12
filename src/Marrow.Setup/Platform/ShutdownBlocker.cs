namespace Marrow.Setup.Platform;

/// <summary>Holds sign-out and shutdown with a reason Windows shows (ShutdownBlockReasonCreate), and lets go.</summary>
public interface IShutdownBlocker
{
    void Block(string reason);

    void Release();
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Close"; unit U1, ED-NEW-inst-u1-1). The reason is set on the setup window, so
/// Windows lists "nth Setup" with it on the sign-out screen while nth is being put in place; the window also answers
/// WM_QUERYENDSESSION with FALSE for that time. Calls before the window has a handle are kept and applied when it does.
/// </summary>
public sealed class WindowShutdownBlocker : IShutdownBlocker
{
    private nint window;
    private string? reason;

    /// <summary>The window handle, once it exists. A reason asked for before then is applied now.</summary>
    public nint Window
    {
        get => window;
        set
        {
            window = value;
            if (reason is not null) Apply(reason);
        }
    }

    public void Block(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        this.reason = reason;
        Apply(reason);
    }

    public void Release()
    {
        reason = null;
        if (window != 0 && OperatingSystem.IsWindows()) _ = Native.ShutdownBlockReasonDestroy(window);
    }

    /// <summary>The reason Windows holds for this window now, or null (used by --ui-smoke).</summary>
    public static unsafe string? Query(nint window)
    {
        if (window == 0 || !OperatingSystem.IsWindows()) return null;
        uint length = 0;
        if (!Native.ShutdownBlockReasonQuery(window, null, ref length) || length == 0) return null;
        var buffer = new char[length];
        fixed (char* text = buffer)
        {
            if (!Native.ShutdownBlockReasonQuery(window, text, ref length)) return null;
            return new string(text).TrimEnd('\0');
        }
    }

    private void Apply(string text)
    {
        if (window != 0 && OperatingSystem.IsWindows()) _ = Native.ShutdownBlockReasonCreate(window, text);
    }
}
