using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Views.Panes;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Web;

// PORT-ONLY: web surfaces W8a, Visus's services in the app (E:/Droppy/work/design/web-surfaces/DESIGN.md 0.2 "One service,
// WebSurface, owns every web view", 2.3 "Users who never open Visus never pay for a browser process", 3.7 the reserved chords
// from inside pages, 5.2 the search engine and local-only suggestions, 8.3 the remembered harmless schemes; ED-NEW-web-w8a-2).
// No Swift counterpart. The one WebSurface of the process, made the first time a Visus tab needs a page (so a session that
// never opens Visus starts no browser), its 250 ms tick, the chord binding W3b left as a seam, the history and the remembered
// app choices on disk, and the per-thread sessions. Tests replace the engine and the live view and keep everything in memory.
// W8b adds the site permissions on disk (5.5), the downloads of this run and where they go (5.4), and the system seams the
// prompts use (the save dialog, Show in folder, nth's toast for a page notification); ED-NEW-web-w8b-1, -2.

namespace Marrow.App.Views.Browser;

/// <summary>Visus's process-wide services.</summary>
public static class VisusHost
{
    /// <summary>The engine's periodic pass (WebSurface.Tick: "about every 250 ms on the UI thread").</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>How long after a change the history and the remembered choices are written.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private static readonly ConditionalWeakTable<ThreadRuntime, VisusSession> Sessions = [];
    private static readonly ConditionalWeakTable<TopLevel, string> WindowIds = [];
    private static WebSurface? surface;
    private static IDisposable? chords;
    private static DispatcherTimer? ticker;
    private static DispatcherTimer? saver;
    private static VisusHistory? history;
    private static ExternalSchemeChoices? schemeChoices;
    private static VisusSiteZoom? siteZoom;
    public static VisusSiteZoom SiteZoom
    {
        get
        {
            if (siteZoom is not null) return siteZoom;
            siteZoom = VisusSiteZoom.FromJson(ReadFile("site-zoom.json"));
            siteZoom.Changed += (_, _) => ScheduleSave();
            return siteZoom;
        }
    }
    private static PermissionStore? permissions;
    private static VisusDownloads? downloads;
    private static int windows;

    /// <summary>Makes the process's WebSurface (the real engine in the app; tests put a fake engine here).</summary>
    public static Func<WebSurface> SurfaceFactory { get; set; } = CreateSurface;

    /// <summary>Makes a tab's live content; null is WebViewHost's own (Avalonia's NativeWebView). Tests pass a plain control.</summary>
    public static Func<Marrow.Core.Web.IWebViewSurface, Control>? LiveViewFactory { get; set; }

    /// <summary>The folder the history and the remembered choices live in, inside the web data folder (so the uninstall and
    /// "Clear browsing data" reach them); null keeps them in memory (tests).</summary>
    public static string? DataFolder { get; set; } = Path.Combine(WebProfiles.DefaultUserDataFolder, "nth");

    /// <summary>The search engine (Settings › Visus › Search engine, W10); DuckDuckGo until the setting is wired.</summary>
    public static Func<SearchEngine> SearchEngine { get; set; } = () => SearchEngines.Default;

    /// <summary>Settings › Visus › "Show search suggestions" (W10, off): the address field also asks the search engine for suggestions.</summary>
    public static Func<bool> SearchSuggestions { get; set; } = () => false;

    /// <summary>Asks the search engine for suggested searches (W10; only with "Show search suggestions" on, never for a private tab). Tests replace it.</summary>
    public static Func<SearchEngine, string, CancellationToken, Task<IReadOnlyList<string>>> SuggestionSource { get; set; } = AskSearchEngineAsync;

    private static Task<IReadOnlyList<string>> AskSearchEngineAsync(SearchEngine engine, string typed, CancellationToken token) =>
        (suggestionAsker ??= new SearchSuggestionSource()).AskAsync(engine, typed, token, ExternalApps.IsRegistered);

    /// <summary>How long the field waits after the last key before asking the search engine (so a word being typed is asked once).</summary>
    public static TimeSpan SuggestionDelay { get; set; } = TimeSpan.FromMilliseconds(180);

    private static SearchSuggestionSource? suggestionAsker;

    /// <summary>
    /// DESIGN 8.1: a page's user-initiated new window opens "a new Visus tab (or the default browser, per the link setting)". True while the
    /// reader's "Open links from chats in" is Default browser (W10): a normal tab's new window then goes to the default browser.
    /// </summary>
    public static Func<bool> PageWindowsInDefaultBrowser { get; set; } = () => false;

    /// <summary>Whether an app on this PC handles a scheme (the registry's <c>URL Protocol</c> mark).</summary>
    public static Func<string, bool>? IsRegisteredScheme { get; set; } = ExternalApps.IsRegistered;

    /// <summary>The name of the app that handles a scheme, as Windows shows it.</summary>
    public static Func<string, string?>? SystemAppName { get; set; } = ExternalApps.FriendlyName;

    /// <summary>Opens an app link the user typed and allowed (the shell's default handler).</summary>
    public static Func<string, bool> OpenExternal { get; set; } = link =>
        SwiftURL.Create(link) is { } url && Marrow.Services.Port.Workspace.Open.Current(url);

    /// <summary>Opens a page in the default browser (the ⋮ menu's "Open in default browser").</summary>
    public static Func<string, bool> OpenInDefaultBrowser { get; set; } = link =>
        WebPolicy.IsWeb(link) && SwiftURL.Create(link) is { } url && Marrow.Services.Port.Workspace.Open.Current(url);

    public static TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Whether the process's WebSurface exists (a session that never opened a page has none).</summary>
    public static bool IsStarted => surface is not null;

    /// <summary>The engine reported a newer runtime: the ⋮ menu offers "Restart Visus to update" (DESIGN 2.1).</summary>
    public static bool IsUpdateReady { get; private set; }

    /// <summary>The process's WebSurface, made on first use.</summary>
    public static WebSurface Surface
    {
        get
        {
            if (surface is not null) return surface;
            surface = SurfaceFactory();
            chords = PaneShortcuts.Bind(surface);
            surface.Engine.NewRuntimeVersionAvailable += OnNewRuntime;
            if (Dispatcher.UIThread.CheckAccess())
            {
                ticker = new DispatcherTimer(TickInterval, DispatcherPriority.Background, (_, _) => surface?.Tick());
                ticker.Start();
            }
            return surface;
        }
    }

    /// <summary>The visits of browse and preview tabs (5.2 suggestions).</summary>
    public static VisusHistory History
    {
        get
        {
            if (history is not null) return history;
            history = VisusHistory.FromJson(ReadFile("history.json"));
            history.Changed += ScheduleSave;
            return history;
        }
    }

    /// <summary>The remembered "open this app from this site" answers (8.3).</summary>
    public static ExternalSchemeChoices SchemeChoices
    {
        get
        {
            if (schemeChoices is not null) return schemeChoices;
            schemeChoices = ExternalSchemeChoices.FromJson(ReadFile("external-apps.json"));
            schemeChoices.Changed += ScheduleSave;
            return schemeChoices;
        }
    }

    /// <summary>DESIGN 5.5: the answers kept per profile and site (browse and preview; private tabs keep none).</summary>
    public static PermissionStore Permissions
    {
        get
        {
            if (permissions is not null) return permissions;
            permissions = PermissionStore.FromJson(ReadFile("permissions.json"));
            permissions.Changed += ScheduleSave;
            return permissions;
        }
    }

    /// <summary>DESIGN 5.4: the files Visus tabs saved in this run.</summary>
    public static VisusDownloads Downloads => downloads ??= new VisusDownloads();

    /// <summary>Where Save puts a file: the user's Downloads folder, the one the composer's recent-downloads list reads.</summary>
    public static Func<string?> DownloadsFolder { get; set; } = () => Marrow.App.Views.Composer.RecentDownloads.DirectoryProvider.Current();

    /// <summary>Settings › Visus "Ask where to save" (W10 wires the setting): Save opens the system dialog too. Off.</summary>
    public static Func<bool> AskWhereToSave { get; set; } = () => false;

    /// <summary>Save as…: the system save dialog over a window, starting in a folder with a name; the chosen path or null.</summary>
    public static Func<TopLevel?, string, string?, Task<string?>> PickSavePath { get; set; } = PickWithDialogAsync;

    /// <summary>The guarded file upload card's Choose (8.4): the system open dialog; the chosen paths or null.</summary>
    public static Func<TopLevel?, bool, Task<IReadOnlyList<string>?>> PickOpenFiles { get; set; } = PickFilesWithDialogAsync;

    /// <summary>Show in folder: File Explorer with the file selected (the file itself is never opened).</summary>
    public static Action<string> ShowInFolder { get; set; } = path => Marrow.UI.Port.ShellWorkspace.RevealInFileExplorer(path);

    /// <summary>A browse tab's page notification as nth's own toast: source, title, body, sound.</summary>
    public static Action<string, string, string, bool> ShowNotification { get; set; } = SystemToast;

    /// <summary>Whether a file exists (Save picks a free name: "report (1).csv").</summary>
    public static Func<string, bool> FileExists { get; set; } = File.Exists;

    /// <summary>The Visus session of a thread (its tabs live as long as the thread's runtime).</summary>
    public static VisusSession SessionFor(ThreadRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return Sessions.GetValue(runtime, r => new VisusSession(r) { ThreadFolder = r.VisusThreadFolder, AgentThread = r.ThreadID.ToString("D") });
    }

    /// <summary>A stable id for a window (the engine's live-view cap and focus are per window, 2.3).</summary>
    public static string WindowIdOf(TopLevel? top)
    {
        if (top is null) return "main";
        return WindowIds.GetValue(top, _ => "w" + Interlocked.Increment(ref windows).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>"Restart Visus" (2.1, 2.7): a fresh browser process, every tab restored by address.</summary>
    public static void Restart()
    {
        IsUpdateReady = false;
        surface?.Restart();
    }

    /// <summary>Writes what changed now (the app's shutdown; tests).</summary>
    public static void Flush()
    {
        saver?.Stop();
        WriteFile("history.json", history?.ToJson());
        WriteFile("external-apps.json", schemeChoices?.ToJson());
        WriteFile("site-zoom.json", siteZoom?.ToJson());
        WriteFile("permissions.json", permissions?.ToJson());
    }

    /// <summary>Tests: a fresh host over the given surface and live view, nothing on disk.</summary>
    internal static void UseForTests(Func<WebSurface> factory, Func<Marrow.Core.Web.IWebViewSurface, Control>? liveView)
    {
        ResetForTests();
        SurfaceFactory = factory;
        LiveViewFactory = liveView;
        DataFolder = null;
        IsRegisteredScheme = null;
        SystemAppName = null;
    }

    internal static void ResetForTests()
    {
        ticker?.Stop();
        ticker = null;
        saver?.Stop();
        saver = null;
        chords?.Dispose();
        chords = null;
        if (surface is not null) surface.Engine.NewRuntimeVersionAvailable -= OnNewRuntime;
        surface?.Dispose();
        surface = null;
        history = null;
        schemeChoices = null;
        siteZoom = null;
        permissions = null;
        downloads = null;
        DownloadsFolder = () => Marrow.App.Views.Composer.RecentDownloads.DirectoryProvider.Current();
        AskWhereToSave = () => false;
        SearchSuggestions = () => false;
        PageWindowsInDefaultBrowser = () => false;
        SuggestionDelay = TimeSpan.FromMilliseconds(180);
        SuggestionSource = AskSearchEngineAsync;
        PickSavePath = PickWithDialogAsync;
        PickOpenFiles = PickFilesWithDialogAsync;
        ShowInFolder = path => Marrow.UI.Port.ShellWorkspace.RevealInFileExplorer(path);
        ShowNotification = SystemToast;
        FileExists = File.Exists;
        IsUpdateReady = false;
        Sessions.Clear();
        SurfaceFactory = CreateSurface;
        LiveViewFactory = null;
        SearchEngine = () => SearchEngines.Default;
        Time = TimeProvider.System;
    }

    /// <summary>The live content a tab's WebViewHost draws (the native view, or the test's).</summary>
    internal static WebViewHost CreateHost(WebSurfaceView view) => new(view, LiveViewFactory);

    private static WebSurface CreateSurface() =>
        new(new WebView2Engine(), new WebProfiles(WebProfiles.DefaultUserDataFolder, Language()));

    private static string Language()
    {
        var name = CultureInfo.CurrentUICulture.Name;
        return string.IsNullOrEmpty(name) ? "en-US" : name;
    }

    private static void OnNewRuntime(object? sender, EventArgs e) => IsUpdateReady = true;

    private static async Task<string?> PickWithDialogAsync(TopLevel? top, string fileName, string? folder)
    {
        if (top?.StorageProvider is not { CanSave: true } storage) return null;
        var options = new FilePickerSaveOptions
        {
            Title = "Save as",
            SuggestedFileName = fileName,
            ShowOverwritePrompt = true,
        };
        if (folder is not null) options.SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(folder);
        var file = await storage.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    private static async Task<IReadOnlyList<string>?> PickFilesWithDialogAsync(TopLevel? top, bool multiple)
    {
        if (top?.StorageProvider is not { CanOpen: true } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a file for the page", AllowMultiple = multiple });
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        return paths.Count == 0 ? null : paths;
    }

    // DESIGN 5.5: "in browse they map to nth's own toast". The title names the site, so a page cannot pose as nth or another app.
    private static void SystemToast(string source, string title, string body, bool sound)
    {
        var request = new Marrow.App.Port.ToastRequest("visus:" + Guid.NewGuid().ToString("N"), source + " · " + title, body, sound,
            new Dictionary<string, object>(StringComparer.Ordinal) { ["visus"] = source });
        _ = AddToastAsync(Marrow.App.Port.ToastNotifications.Center.Current, request);
    }

    private static async Task AddToastAsync(Marrow.App.Port.IToastCenter center, Marrow.App.Port.ToastRequest request)
    {
        try
        {
            await center.Add(request);
        }
        catch (Exception)
        {
            // a toast that could not show is dropped (the page's own notification is gone either way)
        }
    }

    private static void ScheduleSave()
    {
        if (DataFolder is null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Flush();
            return;
        }
        saver ??= new DispatcherTimer(SaveDelay, DispatcherPriority.Background, (_, _) => Flush());
        saver.Stop();
        saver.Start();
    }

    private static string? ReadFile(string name)
    {
        if (DataFolder is null) return null;
        try
        {
            var path = Path.Combine(DataFolder, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteFile(string name, string? json)
    {
        if (DataFolder is null || json is null) return;
        try
        {
            Directory.CreateDirectory(DataFolder);
            var path = Path.Combine(DataFolder, name);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>DESIGN 8.3: what Windows knows about the apps that handle a scheme.</summary>
public static class ExternalApps
{
    /// <summary>An app on this PC handles <paramref name="scheme"/> (HKEY_CLASSES_ROOT\scheme has the <c>URL Protocol</c> value).</summary>
    public static bool IsRegistered(string scheme)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(scheme) || scheme.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))) return false;
        return Registered(scheme);
    }

    /// <summary>The friendly name of the app that opens <paramref name="scheme"/> (AssocQueryString), or null.</summary>
    public static string? FriendlyName(string scheme)
    {
        if (!OperatingSystem.IsWindows() || !IsRegistered(scheme)) return null;
        var size = 0u;
        if (AssocQueryStringW(AssocIsProtocol, AssocFriendlyAppName, scheme, null, null, ref size) != 1 || size == 0) return null; // S_FALSE: the size
        var buffer = new char[size];
        if (AssocQueryStringW(AssocIsProtocol, AssocFriendlyAppName, scheme, null, buffer, ref size) != 0) return null;
        var name = new string(buffer, 0, (int)Math.Max(0, size - 1)).Trim();
        return name.Length == 0 ? null : name;
    }

    [SupportedOSPlatform("windows")]
    private static bool Registered(string scheme)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(scheme);
            return key?.GetValue("URL Protocol") is not null;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    private const uint AssocIsProtocol = 0x1000; // ASSOCF_IS_PROTOCOL
    private const int AssocFriendlyAppName = 4; // ASSOCSTR_FRIENDLYAPPNAME

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, EntryPoint = "AssocQueryStringW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int AssocQueryStringW(uint flags, int str, string assoc, string? extra, [Out] char[]? output, ref uint size);
}
