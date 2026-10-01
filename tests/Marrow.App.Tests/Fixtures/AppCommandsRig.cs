using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Helpers for AppCommandsFinishTests (App/AppCommands.swift, the 19 records finished there). Helper code lives here,
// never inside a test class (R-TEST-3). Nothing here opens a menu: the port draws none (OD-0008 C3), so every command is
// reached by its chord through MenuKeyEquivalents or by calling AppCommands.PerformKeyEquivalent.
public static class AppCommandsRig
{
    // ShortcutStore's constructor is private; it reads the calling flow's defaults once (Shortcuts.swift:267-274), which
    // are empty here, so every chord is its default (precedent: AppCommandsTests.FreshStore).
    public static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;

    public static KeyboardShortcut Chord(string key, ChordModifiers modifiers) => new(new KeyEquivalent(key), modifiers);

    /// <summary>A model over a library written first, each thread with its document, as AppCommandsTests does.</summary>
    public static AppModel Model(AppModelHarness h, IEnumerable<Project> projects, IEnumerable<ChatThread> threads)
    {
        h.WriteLibrary(projects, threads);
        foreach (var thread in threads) AppModelHarness.WriteDocument(thread.Id);
        return h.MakeModel();
    }

    public static T Show<T>(T window) where T : AvaloniaWindow
    {
        window.Width = 400;
        window.Height = 300;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>A real key-down and key-up delivered to <paramref name="window"/> through Avalonia's keyboard device.</summary>
    public static void Press(AvaloniaWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physical, null);
        if (window.IsVisible) window.KeyRelease(key, modifiers, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// A tunnel probe on the window, registered before the command set is attached: Avalonia runs a window's tunnelling
    /// instance handlers latest-registered first, so the probe sees a key only when nothing before it consumed the event.
    /// </summary>
    public static List<Key> Passed(AvaloniaWindow window)
    {
        var passed = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => passed.Add(e.Key), RoutingStrategies.Tunnel);
        return passed;
    }

    /// <summary>
    /// Delivers the platform's deactivation callback to a window. Headless Activate never deactivates the window that
    /// held the focus before, so a test that needs a visible but inactive window supplies the OS callback itself
    /// (precedent: AppCommandsProofTests).
    /// </summary>
    public static void Deactivate(AvaloniaWindow window)
    {
        var deactivated = (Action)typeof(Avalonia.Platform.IWindowBaseImpl)
            .GetProperty("Deactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(window.PlatformImpl)!;
        deactivated();
    }

    /// <summary>The app as AppInfo reports it when the bundle's display name is <paramref name="name"/> (Text.swift:3-9).</summary>
    public static IDisposable RunningAs(string name) => AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["CFBundleDisplayName"] = name,
        ["CFBundleShortVersionString"] = "1.7.0",
    }, isDevelopment: false));

    /// <summary>The rows the palette draws now, by the name each announces: the title, or "title, subtitle".</summary>
    public static string[] RowNames(CommandPalette palette) =>
        palette.RowsPanel.Children.Select(row => AutomationProperties.GetName(row) ?? "").ToArray();

    /// <summary>
    /// A sidebar whose change handlers are wired (Appear) without a window: a real render of a non-empty library is not
    /// available yet (the project row is a stub), and the take-up of the shortcut's question needs only the handlers.
    /// Appear is private, so it is called by reflection (ED-119; precedent: SidebarViewTests).
    /// </summary>
    public static SidebarView MountedSidebar(AppModel model)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var view = new SidebarView();
        host.Child = view;
        view.GetType().GetMethod("Appear", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
        return view;
    }

    public static void UnmountSidebar(SidebarView view) =>
        view.GetType().GetMethod("Disappear", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);

    /// <summary>
    /// The thread whose own row the sidebar is asking "archive?" on (SidebarView.swift:201 pendingArchiveID). Nothing
    /// public observes it without opening the row's popover, so it is read by reflection (ED-119).
    /// </summary>
    public static Guid? PendingArchive(SidebarView view)
    {
        var state = view.GetType().GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
        return (Guid?)state.GetType().GetProperty("PendingArchiveID", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.GetValue(state);
    }
}
