// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.7 settle, 2.11 several apps). The
// change source behind the settle and the waits: out-of-context WinEvent hooks, one per watched process, on one thread of
// its own with a message loop (the hook calls arrive there). Elements created, destroyed, shown, hidden or reordered
// count as structure; a top-level window created or shown counts as a window opened; names, values, states, selections
// and locations count as properties. The caret and the cursor are skipped (they move all the time). Watching window 0
// (the desktop) hooks every process's top-level shows, for pc_launch_app. Reading only: the hooks change nothing in the
// apps. Never used by automated tests.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class WinEvents : IPcChangeSource, IDisposable
{
    // WM_APP + 0x51: the loop runs the queued work.
    private const uint WorkMessage = 0x8000 + 0x51;

    private readonly ConcurrentQueue<Action> work = new();
    private readonly Dictionary<int, Hooked> processes = [];
    private readonly Dictionary<nint, int> watched = [];
    private readonly Win32.WinEventProc onEvent;
    private readonly Win32.WinEventProc onDesktop;
    private readonly ManualResetEventSlim ready = new();
    private readonly Thread loop;
    private uint threadId;
    private nint desktopHook;
    private Action<nint, int, PcChangeKind>? desktopChanged;
    private bool disposed;

    private sealed class Hooked(nint hook, Action<nint, int, PcChangeKind> changed)
    {
        public nint Hook { get; } = hook;
        public Action<nint, int, PcChangeKind> Changed { get; set; } = changed;
        public HashSet<nint> Windows { get; } = [];
    }

    public WinEvents()
    {
        // Held in fields: the hooks call these delegates for as long as they live.
        onEvent = OnEvent;
        onDesktop = OnDesktop;
        loop = new Thread(Run) { IsBackground = true, Name = "Tactus WinEvents" };
        loop.SetApartmentState(ApartmentState.STA);
        loop.Start();
        ready.Wait();
    }

    public void Watch(nint window, int processId, Action<nint, int, PcChangeKind> changed) => Post(() =>
    {
        if (window == PcChangeFeed.Desktop)
        {
            desktopChanged = changed;
            if (desktopHook == 0)
                desktopHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_SHOW, Win32.EVENT_OBJECT_SHOW, 0, onDesktop, 0, 0, Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
            return;
        }
        if (watched.ContainsKey(window)) return;
        if (!processes.TryGetValue(processId, out var hooked))
        {
            var hook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_CREATE, Win32.EVENT_OBJECT_VALUECHANGE, 0, onEvent, (uint)processId, 0, Win32.WINEVENT_OUTOFCONTEXT);
            if (hook == 0) return;
            processes[processId] = hooked = new Hooked(hook, changed);
        }
        hooked.Changed = changed;
        hooked.Windows.Add(window);
        watched[window] = processId;
    });

    public void Unwatch(nint window) => Post(() =>
    {
        if (window == PcChangeFeed.Desktop)
        {
            if (desktopHook != 0) Win32.UnhookWinEvent(desktopHook);
            desktopHook = 0;
            desktopChanged = null;
            return;
        }
        if (!watched.Remove(window, out var processId) || !processes.TryGetValue(processId, out var hooked)) return;
        hooked.Windows.Remove(window);
        if (hooked.Windows.Count > 0) return;
        Win32.UnhookWinEvent(hooked.Hook);
        processes.Remove(processId);
    });

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Post(() =>
        {
            foreach (var hooked in processes.Values) Win32.UnhookWinEvent(hooked.Hook);
            processes.Clear();
            watched.Clear();
            if (desktopHook != 0) Win32.UnhookWinEvent(desktopHook);
            desktopHook = 0;
        });
        Win32.PostThreadMessage(threadId, Win32.WM_QUIT, 0, 0);
        loop.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();
    }

    private void Post(Action action)
    {
        work.Enqueue(action);
        Win32.PostThreadMessage(threadId, WorkMessage, 0, 0);
    }

    private void Run()
    {
        // The first PeekMessage makes this thread's message queue, so posts from other threads land.
        PeekMessage(out _, 0, 0, 0, 0);
        threadId = Win32.GetCurrentThreadId();
        ready.Set();
        while (Win32.GetMessage(out var message, 0, 0, 0) > 0)
        {
            if (message.Message == WorkMessage && message.Window == 0)
            {
                while (work.TryDequeue(out var action)) action();
                continue;
            }
            Win32.DispatchMessage(ref message);
        }
        while (work.TryDequeue(out var action)) action();
    }

    private void OnEvent(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (window == 0 || objectId is Win32.OBJID_CARET or Win32.OBJID_CURSOR) return;
        var root = Win32.GetAncestor(window, Win32.GA_ROOT);
        if (root == 0) root = window;
        Win32.GetWindowThreadProcessId(root, out var processId);
        if (!processes.TryGetValue(processId, out var hooked)) return;
        var topLevel = root == window && objectId == Win32.OBJID_WINDOW && childId == 0;
        var kind = eventType switch
        {
            Win32.EVENT_OBJECT_CREATE or Win32.EVENT_OBJECT_SHOW when topLevel => PcChangeKind.Window,
            Win32.EVENT_OBJECT_CREATE or Win32.EVENT_OBJECT_DESTROY or Win32.EVENT_OBJECT_SHOW or Win32.EVENT_OBJECT_HIDE or Win32.EVENT_OBJECT_REORDER => PcChangeKind.Structure,
            _ => PcChangeKind.Property,
        };
        hooked.Changed(root, processId, kind);
    }

    private void OnDesktop(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (window == 0 || objectId != Win32.OBJID_WINDOW || childId != 0 || desktopChanged is not { } changed) return;
        if (Win32.GetAncestor(window, Win32.GA_ROOT) != window || !Win32.IsWindowVisible(window)) return;
        Win32.GetWindowThreadProcessId(window, out var processId);
        changed(window, processId, PcChangeKind.Window);
    }

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out Win32.MSG message, nint window, uint min, uint max, uint remove);
}
