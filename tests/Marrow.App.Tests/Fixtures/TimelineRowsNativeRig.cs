using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// The rig of the TimelineRows native tests (PR-CHAT-110, PR-CHAT-145): a real WindowManager main window over the real AppModel, on a known
// backdrop so that nothing else on the desktop is under the pointer, with a library of one thread written to the child's own data
// folder first (as GalleryContent does), and the input a person gives it: a real mouse click and a real key press (SendInput, keybd_event).
// No provider CLI is ever started (GalleryContent.Equip gives every one a binary path that does not exist).
internal static class TimelineRowsNativeRig
{
    /// <summary>A real git checkout with <c>changed.txt</c> committed as "before" and then as "after" (the checkpoints of two turns).</summary>
    internal static (string Root, string Start, string Before, string After) MakeCheckout(string label)
    {
        var root = Path.Combine(Path.GetTempPath(), "timelinerows-native", label + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        foreach (var arguments in new[] { "init -q -b main", "config user.name native", "config user.email native@example.com", "config commit.gpgsign false", "config core.autocrlf false" })
            Git(root, arguments);
        File.WriteAllText(Path.Combine(root, "start.txt"), "start\n");
        Git(root, "add -A"); Git(root, "commit -q -m start");
        var start = Git(root, "rev-parse HEAD").Trim();
        File.WriteAllText(Path.Combine(root, "changed.txt"), "before\n");
        Git(root, "add -A"); Git(root, "commit -q -m before");
        var before = Git(root, "rev-parse HEAD").Trim();
        File.WriteAllText(Path.Combine(root, "changed.txt"), "after\n");
        Git(root, "add -A"); Git(root, "commit -q -m after");
        var after = Git(root, "rev-parse HEAD").Trim();
        return (root, start, before, after);
    }

    internal static string Git(string root, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })
            ?? throw new InvalidOperationException("git did not start");
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(30000) || process.ExitCode != 0)
            throw new InvalidOperationException("git " + arguments + " failed: " + process.StandardError.ReadToEnd());
        return output;
    }

    /// <summary>Removes a checkout (git marks its object files read-only).</summary>
    internal static void Delete(string root)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Point? pointer;

    /// <summary>Starts noting where the window's pointer is, as the window reports it.</summary>
    internal static void Watch(WindowManagerNativeSession native, AvaloniaWindow window) => native.OnUi(() =>
    {
        window.AddHandler(InputElement.PointerMovedEvent, (object? _, PointerEventArgs e) => pointer = e.GetPosition(window), RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerPressedEvent, (object? _, PointerPressedEventArgs e) => pointer = e.GetPosition(window), RoutingStrategies.Tunnel, handledEventsToo: true);
    });

    /// <summary>
    /// A real left click on the middle of a control, through SendInput, so hover, press and release reach the app as a person's would. The cursor is aimed by what the window reports (the window
    /// extends its client area into its decorations, and Avalonia's own screen/client conversions of it do not agree with the pointer Windows delivers): it is moved, the position the window
    /// saw is compared with the control's middle, and it is moved again until they agree. Sent only when the window under the point belongs to this process; the pointer is put back afterwards.
    /// Answers the Stopwatch timestamp taken just before the button went down.
    /// </summary>
    internal static long Tap(WindowManagerNativeSession native, AvaloniaWindow window, Control control)
    {
        var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
        native.OnUi(() => // a long thread has the control off the window's edge: its scroller puts it in the middle
        {
            if (control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller) return;
            var top = control.TranslatePoint(default, scroller)?.Y ?? 0;
            scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, scroller.Offset.Y + top - scroller.Viewport.Height / 2));
        });
        Thread.Sleep(500);
        Point Middle() => native.OnUi(() => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? throw new InvalidOperationException("the control is not under its window"));
        // The thread settles into place (it scrolls to its end as it loads): wait until the control has stood still for half a second.
        var local = Middle();
        for (var steady = 0; steady < 40 && steady < 6; )
        {
            Thread.Sleep(100);
            var now = Middle();
            if (Math.Abs(now.X - local.X) < 0.5 && Math.Abs(now.Y - local.Y) < 0.5) steady++; else { steady = 0; local = now; }
        }
        var scale = native.OnUi(() => window.RenderScaling);
        if (N.GetForegroundWindow() != handle) Front(native, window);
        var screen = CenterOf(native, control);
        if (N.GetForegroundWindow() != handle || N.RootAt(new NativeWindowPoint(screen.X, screen.Y)) != handle)
            throw new InvalidOperationException($"NATIVE_PRECONDITION: the control is covered or the main window is not in front (foreground 0x{N.GetForegroundWindow():X}, window 0x{handle:X}, root at the control 0x{N.RootAt(new NativeWindowPoint(screen.X, screen.Y)):X} at {screen}, control {native.OnUi(() => control.Bounds)}, in the window at {local}).");
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        try
        {
            for (var round = 0; round < 5; round++)
            {
                var root = N.RootAt(new NativeWindowPoint(screen.X, screen.Y));
                _ = N.GetWindowThreadProcessId(root, out var owner);
                if (owner != Environment.ProcessId) throw new InvalidOperationException("NATIVE_PRECONDITION: the click would land on a window of another process.");
                N.Check(N.SetCursorPos(screen.X, screen.Y), "SetCursorPos");
                N.Mouse(N.MOUSE_MOVE);
                Thread.Sleep(200);
                var seen = native.OnUi(() => pointer);
                local = Middle();
                if (seen is { } at && Math.Abs(at.X - local.X) < 1 && Math.Abs(at.Y - local.Y) < 1) break;
                if (seen is null) continue;
                screen = new PixelPoint(screen.X + (int)Math.Round((local.X - seen.Value.X) * scale), screen.Y + (int)Math.Round((local.Y - seen.Value.Y) * scale));
            }
            local = Middle();
            var aimed = native.OnUi(() => pointer);
            if (aimed is not { } finalAt || Math.Abs(finalAt.X - local.X) >= 1.5 || Math.Abs(finalAt.Y - local.Y) >= 1.5)
                throw new InvalidOperationException($"NATIVE_PRECONDITION: the pointer could not be put on the control (the window saw {aimed}, the control's middle is {local}).");
            Thread.Sleep(150);
            var pressed = Stopwatch.GetTimestamp();
            N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP);
            Thread.Sleep(200);
            return pressed;
        }
        finally { _ = N.SetCursorPos(original.X, original.Y); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    /// <summary>A real key press (down and up) of a virtual key; whatever has the keyboard focus gets it.</summary>
    internal static void Press(byte virtualKey)
    {
        keybd_event(virtualKey, 0, 0, 0);
        Thread.Sleep(60);
        keybd_event(virtualKey, 0, 2, 0);
        Thread.Sleep(400);
    }

    internal const byte Return = 0x0D;
    internal const byte Escape = 0x1B;

    /// <summary>The real main window, shown over a backdrop, with the thread selected and in front: the window, its model, and the backdrop (closed with the session).</summary>
    internal static (AvaloniaWindow Window, AppModel Model) Open(WindowManagerNativeSession native, WindowManagerNativeSubject subject, Guid thread)
    {
        PixelRect work = default;
        double scale = 1;
        native.OnUi(() =>
        {
            var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("NATIVE_PRECONDITION: no native screen is available.");
            work = screen.WorkingArea; scale = screen.Scaling;
            subject.Settings.Hide();
            subject.Main.Hide();
        });
        var (window, model) = native.OnUi(() =>
        {
            var backdrop = GalleryBackdrop.Cover(work, scale);
            native.Register(backdrop);
            backdrop.Show();
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(backdrop)); // as Program.cs binds the frame clock (ED-083), before any view that animates
            UserDefaults.Standard.Current.RemoveObject("DroppyCodeMainWindow"); // the stand-in window saved its frame under the main window's name
            var manager = new WindowManager();
            var model = manager.Model;
            GalleryContent.Equip(model, null);
            model.Settings.HasSeenHydraIntro = true;
            model.SelectedThreadID = thread;
            manager.ShowMain();
            var main = manager.MainWindow!;
            native.Register(main);
            return (main, model);
        }, TimeSpan.FromSeconds(40));
        // WindowRectAnchor finds the window of the current event (NSApp.currentEvent), and AppCurrentEvent starts listening when it is first asked; the app has a desktop lifetime to fall back
        // on, this session has none, so it is asked now, before the first click.
        native.OnUi(() => _ = AppCurrentEvent.CurrentWindow);
        Thread.Sleep(600);
        native.OnUi(() => { window.Topmost = true; if (!window.IsVisible) window.Show(); });
        Front(native, window);
        return (window, model);
    }

    // WindowManagerNativeForeground's header promises no simulated key: it acquires the foreground by AttachThreadInput, which Windows denies when the foreground belongs to a process
    // of another integrity or an app that does not share its queue (attachError=5, as on this PC when the owner's apps are in front). This rig departs from that promise on purpose and
    // only here: a bare Alt tap from this process is the documented way to let it take the foreground, nothing else is pressed with it, and it is sent only when no key or button
    // is down (a held key would be changed by it), as the Acquire precondition requires of an attach.
    private static bool NoKeyIsHeld()
    {
        for (var key = 1; key < 256; key++)
            if ((N.GetAsyncKeyState(key) & 0x8000) != 0) return false;
        return true;
    }

    /// <summary>Brings the window to the foreground. Windows hands the foreground to a process that has just given input: a bare Alt tap from this one (it opens nothing in a window of ours) lets it take it.</summary>
    internal static void Front(WindowManagerNativeSession native, AvaloniaWindow window)
    {
        var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (NoKeyIsHeld()) { keybd_event(0x12, 0, 0, 0); keybd_event(0x12, 0, 2, 0); }
                native.OnUi(() => WindowManagerNativeForeground.Acquire(handle, native.ThreadId));
                return;
            }
            catch (Exception error) { last = error; Thread.Sleep(400); }
        }
        throw new InvalidOperationException("NATIVE_PRECONDITION: the main window could not take the foreground: " + last!.Message);
    }

    /// <summary>The thread library for a real revert: a project on the checkout, one Claude thread of two turns, each with the checkpoints of its start and end and a turn-end card of changed.txt.</summary>
    internal static (Guid Thread, Guid FirstTurn, Guid SecondTurn) Seed(WindowManagerNativeSession native, (string Root, string Start, string Before, string After) checkout)
    {
        var thread = Guid.NewGuid();
        foreach (var (index, start, end) in new[] { (0L, checkout.Start, checkout.Before), (1L, checkout.Before, checkout.After) })
        {
            Git(checkout.Root, "update-ref " + Marrow.Services.Git.Git.CheckpointRef(thread, index, "start") + " " + start);
            Git(checkout.Root, "update-ref " + Marrow.Services.Git.Git.CheckpointRef(thread, index, "end") + " " + end);
        }
        var turns = new[] { Guid.NewGuid(), Guid.NewGuid() };
        native.OnUi(() =>
        {
            var now = SwiftDate.Now;
            var project = new Project("revert-native", checkout.Root);
            var chat = new ChatThread(project.Id, ProviderKind.Claude, "opus", "high", RuntimeMode.Supervised) with
            {
                Id = thread, Title = "Revert in a real window", HasCustomTitle = true, UpdatedAt = now, CreatedAt = now.AddHours(-1), LastStatus = TurnStatus.Completed, Branch = "main", ProviderSessionID = "native-session", // Claude rewinds to the anchor of the turn before the reverted one (ThreadRuntime.swift:1886-1890)
            };
            var items = new List<TimelineItem>();
            var records = new List<TurnRecord>();
            for (var index = 0; index < 2; index++)
            {
                var turn = turns[index];
                var started = now.AddMinutes(-10 + index * 4);
                var user = new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = index == 0 ? "Write the first line." : "Rewrite the line." }), date: started);
                items.Add(user);
                items.Add(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "Done." }), date: started.AddSeconds(5)));
                items.Add(new TimelineItem(turn, new TimelineItem.Content.TurnEnd(new TurnSummary
                {
                    TurnID = turn, Status = TurnStatus.Completed, Duration = 5, FilesChanged = 1, Additions = 1, Deletions = index,
                    Changes = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("changed.txt", 1, index, false)]), 1, index),
                }), date: started.AddSeconds(6)));
                records.Add(new TurnRecord
                {
                    Id = turn, Index = index, Status = TurnStatus.Completed, StartedAt = started, CompletedAt = started.AddSeconds(5), UserItemID = user.Id,
                    TouchedPaths = new ValueList<string>(["changed.txt"]), ProviderAnchor = "anchor-" + index,
                    BaseCheckpoint = Marrow.Services.Git.Git.CheckpointRef(thread, index, "start"), EndCheckpoint = Marrow.Services.Git.Git.CheckpointRef(thread, index, "end"),
                });
            }
            var library = new Library() with { Projects = new ValueList<Project>([project]), Threads = new ValueList<ChatThread>([chat]) };
            DiskWriter.Shared.WriteSynchronously(library, Storage.LibraryURL);
            DiskWriter.Shared.WriteSynchronously(new ThreadDocument(thread) { Items = new ValueList<TimelineItem>(items), Turns = new ValueList<TurnRecord>(records) }, Storage.ThreadURL(thread));
        });
        return (thread, turns[0], turns[1]);
    }

    /// <summary>How many windows of the native UI thread are visible now (the backdrop, the main window and each popover's popup window).</summary>
    internal static int VisibleWindows(WindowManagerNativeSession native)
    {
        var count = 0;
        NativeWindowEnumerator callback = (window, _) => { if (N.IsWindowVisible(window)) count++; return true; };
        _ = N.EnumThreadWindows(native.ThreadId, callback, 0);
        GC.KeepAlive(callback);
        return count;
    }

    /// <summary>The visible controls of a type under a window that satisfy the test, in visual order.</summary>
    internal static List<T> Find<T>(WindowManagerNativeSession native, AvaloniaWindow window, Func<T, bool> test) where T : Control =>
        native.OnUi(() => window.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible && control.Bounds.Width > 0 && test(control)).ToList());

    /// <summary>The physical screen point at the middle of a control, as a real pointer there reaches it. Avalonia's PointToScreen and PointToClient of this window disagree by the height of its
    /// caption (the window extends its client area into the decorations): the pointer Windows delivers is reported by PointToClient, so the point is moved until that reads the control's middle.</summary>
    internal static PixelPoint CenterOf(WindowManagerNativeSession native, Control control) => native.OnUi(() =>
    {
        var window = TopLevel.GetTopLevel(control) ?? throw new InvalidOperationException("the control is not in a window");
        var local = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? throw new InvalidOperationException("the control is not under its window");
        var screen = window.PointToScreen(local);
        for (var round = 0; round < 3; round++)
        {
            var back = window.PointToClient(screen);
            if (Math.Abs(back.X - local.X) < 0.5 && Math.Abs(back.Y - local.Y) < 0.5) break;
            screen = new PixelPoint(screen.X + (int)Math.Round((local.X - back.X) * window.RenderScaling), screen.Y + (int)Math.Round((local.Y - back.Y) * window.RenderScaling));
        }
        return screen;
    });
}
