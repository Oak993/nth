using Avalonia.Controls;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.Testing;

// PORT-ONLY: web surfaces W8a test rig (E:/Droppy/work/design/web-surfaces/DESIGN.md 13.3 "Visus chrome" headless tests;
// ED-NEW-web-w8a-2). No Swift counterpart. Visus over the fake engine: a manual clock, posted work run at once, nothing on
// disk, app links recorded instead of opened, and a live view that attaches the fake engine.

namespace Marrow.App.Tests.Fixtures;

public sealed class VisusRig : IDisposable
{
    public const string UserData = @"C:\nth-test\WebView2";

    private readonly Func<string, bool> openExternal;
    private readonly Func<string, bool> openInBrowser;
    private readonly IDisposable defaultsScope;
    private readonly IDisposable shortcutsScope;

    public VisusRig()
    {
        // The browser keys check nth's reserved chords (PaneShortcuts.Bind): the default bindings, on an in-memory store.
        defaultsScope = UserDefaults.Standard.Override(new InMemorySettingsStore());
        shortcutsScope = ShortcutStore.Shared.Override((ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
        Engine = new FakeWebEngine(Time);
        VisusHost.UseForTests(
            () => new WebSurface(Engine, new WebProfiles(UserData, "en-US"), Time, work => work()),
            surface =>
            {
                if (surface is WebSurfaceView { Engine.IsAttached: false } view) view.AttachNative(1, 2);
                return new Border { Focusable = true };
            });
        VisusHost.Time = Time;
        openExternal = VisusHost.OpenExternal;
        openInBrowser = VisusHost.OpenInDefaultBrowser;
        VisusHost.OpenExternal = link =>
        {
            Opened.Add(link);
            return true;
        };
        VisusHost.OpenInDefaultBrowser = link =>
        {
            OpenedInBrowser.Add(link);
            return true;
        };
        // W8b: downloads land in a folder of the rig's, the system dialogs answer what the test queues, and nothing is opened.
        VisusHost.DownloadsFolder = () => DownloadsFolder;
        VisusHost.FileExists = path => ExistingFiles.Contains(path);
        VisusHost.PickSavePath = (_, name, folder) =>
        {
            SaveDialogs.Add((name, folder));
            return Task.FromResult(NextSavePath);
        };
        VisusHost.PickOpenFiles = (_, multiple) =>
        {
            OpenDialogs.Add(multiple);
            return Task.FromResult(NextOpenFiles);
        };
        VisusHost.ShowInFolder = ShownInFolder.Add;
        VisusHost.ShowNotification = (source, title, body, sound) => Notifications.Add((source, title, body, sound));
        Session = new VisusSession { ThreadFolder = ThreadFolder };
    }

    /// <summary>W8b: the Downloads folder Save writes to.</summary>
    public const string DownloadsFolder = @"C:\nth-test\Downloads";

    /// <summary>Files the rig says exist (Save picks a free name next to them).</summary>
    public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What the next Save as… dialog returns (null: the user closed it).</summary>
    public string? NextSavePath { get; set; }

    /// <summary>The Save as… dialogs opened: the suggested name and folder.</summary>
    public List<(string Name, string? Folder)> SaveDialogs { get; } = [];

    /// <summary>What the next open dialog of the guarded file card returns (null: the user closed it).</summary>
    public IReadOnlyList<string>? NextOpenFiles { get; set; }

    /// <summary>The open dialogs shown (whether they took several files).</summary>
    public List<bool> OpenDialogs { get; } = [];

    /// <summary>Paths shown in File Explorer.</summary>
    public List<string> ShownInFolder { get; } = [];

    /// <summary>nth's toasts for page notifications: source, title, body, sound.</summary>
    public List<(string Source, string Title, string Body, bool Sound)> Notifications { get; } = [];

    /// <summary>The thread folder the rig's session stands for (it names the agent profile).</summary>
    public const string ThreadFolder = @"C:\nth-test\Library\threads\T1";

    public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));

    public FakeWebEngine Engine { get; }

    public VisusSession Session { get; }

    /// <summary>App links the user allowed (VisusHost.OpenExternal).</summary>
    public List<string> Opened { get; } = [];

    /// <summary>Pages handed to the default browser.</summary>
    public List<string> OpenedInBrowser { get; } = [];

    /// <summary>The tab's page engine, attached now if the host has not done it yet.</summary>
    public static FakeWebView Live(VisusTab tab)
    {
        var view = tab.View ?? throw new InvalidOperationException("the tab has no page");
        if (view.Engine is null) view.Activate();
        if (view.Engine is { IsAttached: false }) view.AttachNative(1, 2);
        return Assert.IsType<FakeWebView>(view.Engine);
    }

    /// <summary>A tab showing <paramref name="url"/> with its page live, its navigation finished.</summary>
    public VisusTab Page(string url, VisusTabKind kind = VisusTabKind.Browse, string? title = null)
    {
        var tab = Session.NewTab(kind, url);
        var page = Live(tab);
        if (title is not null) page.RaiseTitle(title);
        page.RaiseNavigationCompleted(url);
        return tab;
    }

    public void Dispose()
    {
        Session.Dispose();
        VisusHost.OpenExternal = openExternal;
        VisusHost.OpenInDefaultBrowser = openInBrowser;
        VisusHost.ResetForTests();
        shortcutsScope.Dispose();
        defaultsScope.Dispose();
    }
}
