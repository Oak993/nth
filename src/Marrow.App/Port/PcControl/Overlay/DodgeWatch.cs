// PORT-ONLY: Tactus pill v2, when the dodge looks again (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Dodge rules", 10.6
// topmost re-assert). Inputs are window events, never frames: EVENT_SYSTEM_FOREGROUND (read at once), EVENT_OBJECT_
// LOCATIONCHANGE of a top-level window (debounced 100 ms: a window being dragged reads once it pauses), EVENT_OBJECT_
// REORDER (another topmost window may cover the band: the band goes on top again) and display changes (read at once).
// The hooks are out of context on a thread of their own with a message loop; their callbacks only stamp the kind, so they
// never slow the apps. The read itself (CaptionButtons, FullscreenWatch) runs off the UI thread.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Marrow.App.Port.PcControl.Overlay;

public enum DodgeEventKind
{
    Foreground,
    Location,
    Reorder,
    Display,
}

/// <summary>Raises window events for the dodge. The real one never runs in tests.</summary>
public interface IDodgeEventSource : IDisposable
{
    event Action<DodgeEventKind>? Raised;
}

/// <summary>The debounce: when the next dodge read is due, and whether a reorder asks for the bands on top again.</summary>
public sealed class DodgeWatch
{
    public const double DebounceSeconds = 0.1;

    private double? dueAt;
    private bool reorder;

    /// <summary>A read is waiting.</summary>
    public bool Pending => dueAt is not null;

    public void Event(DodgeEventKind kind, double now)
    {
        switch (kind)
        {
            case DodgeEventKind.Location:
                // Debounced: each move pushes the read 100 ms past itself, so a dragged window reads when it pauses.
                // An immediate read already due stays immediate.
                if (dueAt is not { } at || at > now) dueAt = now + DebounceSeconds;
                break;
            case DodgeEventKind.Reorder:
                reorder = true;
                dueAt = now;
                break;
            default:
                dueAt = now;
                break;
        }
    }

    /// <summary>True once when a read is due at <paramref name="now"/>; <paramref name="covered"/> says a reorder came in.</summary>
    public bool Due(double now, out bool covered)
    {
        covered = false;
        if (dueAt is not { } at || now < at) return false;
        dueAt = null;
        covered = reorder;
        reorder = false;
        return true;
    }
}

/// <summary>Out-of-context WinEvent hooks for the dodge, on their own thread. Never run by automated tests.</summary>
public sealed class Win32DodgeEvents : IDodgeEventSource
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectReorder = 0x8004;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int ObjIdWindow = 0;
    private const uint GaRoot = 2;
    private const uint WmQuit = 0x0012;

    private readonly Thread thread;
    private readonly ConcurrentBag<nint> hooks = [];
    private readonly WinEventProc callback;
    private uint threadId;

    public Win32DodgeEvents()
    {
        callback = OnEvent;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            foreach (var (from, to) in new[] { (EventSystemForeground, EventSystemForeground), (EventObjectReorder, EventObjectReorder), (EventObjectLocationChange, EventObjectLocationChange) })
            {
                var hook = SetWinEventHook(from, to, 0, callback, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
                if (hook != 0) hooks.Add(hook);
            }
            ready.Set();
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
            foreach (var hook in hooks) UnhookWinEvent(hook);
        }) { IsBackground = true, Name = "Tactus dodge events" };
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(2));
    }

    public event Action<DodgeEventKind>? Raised;

    public void Dispose()
    {
        if (threadId != 0) PostThreadMessage(threadId, WmQuit, 0, 0);
        thread.Join(TimeSpan.FromSeconds(1));
    }

    private void OnEvent(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (window == 0) return;
        switch (kind)
        {
            case EventSystemForeground:
                Raised?.Invoke(DodgeEventKind.Foreground);
                break;
            case EventObjectReorder:
                Raised?.Invoke(DodgeEventKind.Reorder);
                break;
            case EventObjectLocationChange when objectId == ObjIdWindow && childId == 0 && GetAncestor(window, GaRoot) == window:
                Raised?.Invoke(DodgeEventKind.Location);
                break;
        }
    }

    private delegate void WinEventProc(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Msg message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
