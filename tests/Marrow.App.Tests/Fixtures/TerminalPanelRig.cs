using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Shared rig for the TerminalPanel record tests (DroppyCode/App/Views/Terminal/TerminalPanel.swift). Helper types
// live here, never inside a test class (R-TEST-3). A rig run through Run starts no process at all: the shell that
// TerminalStore opens for a session is a FakeShellProcess, because these tests read the drawer's layout, tabs and
// bookkeeping, not what a shell does. A test that reads a real shell's output, process or exit runs through
// RunWithRealShell, where the only process the rig starts is the isolated PowerShell that TerminalStore itself opens
// for a session (ConPTY, no window of its own). Either way, Dispose terminates every session of the store (which ends a
// real shell and its whole tree) and closes every window the rig showed.
public sealed class TerminalPanelRig : IDisposable
{
    private readonly List<AvaloniaWindow> windows = [];

    public AppModel App { get; }
    public ThreadRuntime Runtime { get; }
    public AppModelHarness Store { get; }
    public ChatThread Thread { get; }

    private TerminalPanelRig(bool realShell)
    {
        Store = new AppModelHarness();
        if (!realShell) Store.UseFakeTerminalShell();
        var project = AppModelSamples.Project("Terminal panel", Store.NewFolder("terminal-panel"));
        Thread = AppModelSamples.Thread(project);
        Store.WriteLibrary([project], [Thread]);
        AppModelHarness.WriteDocument(Thread.Id);
        App = Store.MakeModel();
        Runtime = Store.Main.OnMain(() => new ThreadRuntime(Thread.Id, App));
    }

    /// <summary>Runs <paramref name="body"/> on the headless UI thread. The body makes each step of its own through
    /// <see cref="Step"/>: a Track-driven rebuild of the panel is posted to the main actor and only runs once that step
    /// ends, exactly as the existing terminal tests do it.</summary>
    public static void Run(HeadlessSession headless, Action<TerminalPanelRig> body) => RunIn(headless, realShell: false, body);

    /// <summary>
    /// <see cref="Run"/> with a real PowerShell behind every session, for the few tests that read what a real shell prints,
    /// its process or its exit. The rig ends every one of them when it is disposed.
    /// </summary>
    public static void RunWithRealShell(HeadlessSession headless, Action<TerminalPanelRig> body) => RunIn(headless, realShell: true, body);

    private static void RunIn(HeadlessSession headless, bool realShell, Action<TerminalPanelRig> body) => headless.OnUiThread(() =>
    {
        using var rig = new TerminalPanelRig(realShell);
        body(rig);
    });

    /// <summary>One step on the main actor; everything it posted has run when it returns.</summary>
    public void Step(Action step)
    {
        Store.Main.OnMain(step);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A real throwaway folder for a session's working directory.</summary>
    public static string Folder(string label) => TerminalTestFolders.Create(label);

    /// <summary>Shows a panel for the rig's thread in an explicitly sized window, with the AppModel inherited.</summary>
    public (Border Host, TerminalPanel Panel, AvaloniaWindow Window) Show(string directory, double width = 500, double height = 400)
    {
        Border host = null!;
        TerminalPanel panel = null!;
        AvaloniaWindow window = null!;
        Step(() =>
        {
            host = new Border();
            AppEnvironment.SetAppModel(host, App);
            panel = new TerminalPanel(Runtime, directory);
            host.Child = panel;
            window = new AvaloniaWindow { Content = host, Width = width, Height = height };
            windows.Add(window);
            window.Show();
            Settle(window);
        });
        Step(() => Settle(window));
        return (host, panel, window);
    }

    /// <summary>Lets layout, posted work and the headless render timer drain.</summary>
    public static void Settle(TopLevel window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The centre of <paramref name="control"/> in <paramref name="ancestor"/>'s coordinates.</summary>
    public static Point Center(Visual control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    /// <summary>The window every shown panel is in; kept so a test can close it before the rig is disposed.</summary>
    public void Close(AvaloniaWindow window)
    {
        window.Close();
        windows.Remove(window);
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        Store.Main.OnMain(() =>
        {
            foreach (var window in windows) window.Close();
            windows.Clear();
            Dispatcher.UIThread.RunJobs();
            App.Terminals.TerminateAll();
            Dispatcher.UIThread.RunJobs();
        });
        Store.Dispose();
    }

    /// <summary>A shortcut store of its own, so a test can clear or remap a chord without touching the shared one.</summary>
    public static IDisposable FreshShortcuts() =>
        ShortcutStore.Shared.Override((ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
}
