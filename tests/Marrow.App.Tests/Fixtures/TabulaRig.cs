using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Views.Artifacts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Save and open dialogs a test answers: each request is recorded and answered by the test's function.</summary>
public sealed class FakeTabulaFiles : ITabulaFiles
{
    public List<TabulaSaveRequest> Saves { get; } = [];

    public Func<TabulaSaveRequest, string?> SaveAnswer { get; set; } = _ => null;

    public List<string> Opens { get; } = [];

    public IReadOnlyList<string> OpenAnswer { get; set; } = [];

    public Task<string?> SaveAsync(Control owner, TabulaSaveRequest request)
    {
        Saves.Add(request);
        return Task.FromResult(SaveAnswer(request));
    }

    public Task<IReadOnlyList<string>> OpenAsync(Control owner, string title, bool multiple)
    {
        Opens.Add(title);
        return Task.FromResult(OpenAnswer);
    }
}

/// <summary>A clipboard that keeps what was copied.</summary>
public sealed class FakeTabulaClipboard : ITabulaClipboard
{
    public List<string> Texts { get; } = [];

    public List<byte[]> Images { get; } = [];

    public Task<bool> SetTextAsync(Control owner, string text)
    {
        Texts.Add(text);
        return Task.FromResult(true);
    }

    public Task<bool> SetImageAsync(Control owner, byte[] png)
    {
        Images.Add(png);
        return Task.FromResult(true);
    }
}

/// <summary>
/// PORT-ONLY (web W14, ED-NEW-web-w14-1): a Tabula pane body on a real headless window. A store in a throwaway threads
/// folder (R-TEST-9) with a clock that moves a second per version, the streams, the artifacts runtime over the fake web
/// engine (no WebView2 starts), dialogs, clipboard and the chat recorded, a manual frame source, and Reduce Motion as the
/// test sets it. The window has the overlay layers, so menus and cards open as popups do in the app.
/// </summary>
public sealed class TabulaRig : IDisposable
{
    public static readonly Guid Chat = Guid.Parse("7a3e9c10-2b4d-4f6e-8a1c-3d5e7f9a1b20");
    public static readonly Guid Neighbour = Guid.Parse("7a3e9c10-2b4d-4f6e-8a1c-3d5e7f9a1b21");
    public static readonly Guid Elsewhere = Guid.Parse("7a3e9c10-2b4d-4f6e-8a1c-3d5e7f9a1b22");
    public static readonly ArtifactAuthor Model = new("claude", "sonnet", null);

    private readonly IDisposable actorScope;
    private readonly IDisposable frameScope;
    private readonly IDisposable motionScope;
    private DateTimeOffset now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public TabulaRig(double width = 640, double height = 720, bool reduceMotion = false, bool web = true,
        Func<TabulaPendingAsk, string, bool>? trySendAsk = null)
    {
        Runtime = new TestRuntime();
        actorScope = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        Frames = new ManualFrameSource();
        frameScope = FrameClock.Source.Override(Frames);
        Motion = new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion };
        motionScope = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(Motion);
        var data = TestDataDirectory.Read();
        if (string.IsNullOrEmpty(data))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        Root = Path.Combine(data, "Tabula", Guid.NewGuid().ToString("N"));
        Threads = Path.Combine(Root, "threads");
        Directory.CreateDirectory(Threads);
        Store = new ArtifactStore(() => Threads, () => now = now.AddSeconds(1));
        Streams = new ArtifactStreams(Runtime.Time);
        Engine = new FakeWebEngine(Runtime.Time);
        Surface = new WebSurface(Engine, new WebProfiles(@"C:\nth-test\WebView2", "en-US"), Runtime.Time, work => work());
        Artifacts = new ArtifactRuntime(Store, new ArtifactLibraryFiles(Path.Combine(Root, "lib")), post: work => work());
        StateFile = Path.Combine(Path.GetDirectoryName(Store.ArtifactsFolder(Chat))!, TabulaState.FileName);
        State = TabulaState.Load(Chat, StateFile);
        ProjectThreads = [Chat, Neighbour];
        Names[Chat] = "Pricing work";
        Names[Neighbour] = "Landing page";
        Names[Elsewhere] = "Other project";
        Context = new TabulaContext
        {
            Thread = Chat,
            Store = Store,
            State = State,
            Streams = Streams,
            Runtime = web ? Artifacts : null,
            Surface = () => web ? Surface : null,
            LiveFactory = _ => new Border { Focusable = true },
            ProjectThreads = () => ProjectThreads,
            AllThreads = () => Store.Threads(),
            ThreadName = id => Names.GetValueOrDefault(id),
            ProjectFolder = () => ProjectFolder,
            SendTurn = Sent.Add,
            TrySendAsk = trySendAsk,
            OpenInVisus = null,
            Expand = () => Expanded++,
            Draft = Drafts.Add,
            OpenLink = Links.Add,
            Files = Files,
            Clipboard = Clipboard,
            Time = Runtime.Time,
            Post = work => work(),
        };
        View = new TabulaView(Context);
        Window = OverlayWindow(View);
        Window.Width = width;
        Window.Height = height;
        Window.Show();
        Layout();
    }

    public TestRuntime Runtime { get; }

    public ManualFrameSource Frames { get; }

    public FakeGeniePlatform Motion { get; }

    public string Root { get; }

    public string Threads { get; }

    public ArtifactStore Store { get; }

    public ArtifactStreams Streams { get; }

    public FakeWebEngine Engine { get; }

    public WebSurface Surface { get; }

    public ArtifactRuntime Artifacts { get; }

    public string StateFile { get; }

    public TabulaState State { get; }

    public List<Guid> ProjectThreads { get; }

    public Dictionary<Guid, string> Names { get; } = [];

    public string? ProjectFolder { get; set; }

    public List<string> Sent { get; } = [];

    public List<string> Drafts { get; } = [];

    public List<string> Links { get; } = [];

    public int Expanded { get; private set; }

    public FakeTabulaFiles Files { get; } = new();

    public FakeTabulaClipboard Clipboard { get; } = new();

    public TabulaContext Context { get; }

    public TabulaView View { get; }

    public AvaloniaWindow Window { get; }

    public ArtifactSnapshot Create(ArtifactKind kind, string content, string title = "Pricing calculator", string? id = null, Guid? chat = null, string? prompt = "Make a pricing calculator", string? language = null) =>
        Store.Create(chat ?? Chat, new ArtifactDraft { Kind = kind, Title = title, Content = content, Id = id, Language = language }, Model, prompt);

    public ArtifactSnapshot CreateProject(string title, params (string Path, string Content)[] files) =>
        Store.Create(Chat, new ArtifactDraft { Kind = ArtifactKind.Project, Title = title, Files = files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal) }, Model, "Make a site");

    public ArtifactSnapshot Update(string id, string content, string? prompt = "Tighter layout") =>
        Store.Update(Chat, id, new ArtifactRevision { Content = content }, Model, prompt);

    /// <summary>Creates an artifact and shows it in the pane.</summary>
    public ArtifactSnapshot Show(ArtifactKind kind, string content, string title = "Pricing calculator", string? id = null)
    {
        var snapshot = Create(kind, content, title, id);
        State.Select(new TabulaRef(Chat, snapshot.Artifact.Id));
        Layout();
        return snapshot;
    }

    public void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    /// <summary>One animation frame of <paramref name="milliseconds"/>; the clock moves with it, so frame sleeps resume.</summary>
    public void Frame(double milliseconds = 1000.0 / 60)
    {
        Runtime.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Layout();
    }

    /// <summary>Frames for <paramref name="milliseconds"/> of time.</summary>
    public void Run(double milliseconds)
    {
        for (var at = 0.0; at < milliseconds; at += 1000.0 / 60) Frame();
    }

    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// A model message with its untrusted page-content block's random marker (ED-NEW-web-sec-agent-6) replaced by "MARKER", so two
    /// messages built from the same page words compare equal. The marker must be 16 hex digits and the same in the opening and
    /// the closing tag, and the message must hold exactly one block; anything else fails here.
    /// </summary>
    public static string FenceMarkerMasked(string message)
    {
        var opening = System.Text.RegularExpressions.Regex.Matches(message, "<untrusted-page-content origin=\"[^\"]*\" marker=\"([0-9a-f]{16})\">");
        var closing = System.Text.RegularExpressions.Regex.Matches(message, "</untrusted-page-content marker=\"([0-9a-f]{16})\">");
        Xunit.Assert.Single(opening);
        Xunit.Assert.Single(closing);
        Xunit.Assert.Equal(opening[0].Groups[1].Value, closing[0].Groups[1].Value);
        return message.Replace(opening[0].Groups[1].Value, "MARKER", StringComparison.Ordinal);
    }

    /// <summary>A header action by name, invoked.</summary>
    public void Invoke(string action)
    {
        var found = View.Content.Actions.FirstOrDefault(a => a.Name == action) ?? throw new InvalidOperationException("No header action " + action + ": " + string.Join(", ", View.Content.Actions.Select(a => a.Name)));
        found.Invoke();
        Layout();
    }

    /// <summary>The open menu's row with this title, chosen.</summary>
    public void Choose(string title)
    {
        var item = View.MenuItems.FirstOrDefault(i => Title(i) == title)
            ?? throw new InvalidOperationException("No menu row " + title + ": " + string.Join(", ", View.MenuItems.Select(Title)));
        LogicalQuery.SelfAndDescendants<Button>(item).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout();
    }

    public IReadOnlyList<string> MenuTitles => View.MenuItems.Select(Title).ToList();

    private static string Title(PopoverItem item) => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item).Select(t => t.FullText).FirstOrDefault() ?? "";

    /// <summary>A window whose template has the overlay layers a popup needs headless (as the UI tests' popup host).</summary>
    private static AvaloniaWindow OverlayWindow(Control content)
    {
        var window = new AvaloniaWindow { Width = 400, Height = 300, Content = content };
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, scope) =>
        {
            var layers = new VisualLayerManager
            {
                EnableOverlayLayer = true,
                EnableAdornerLayer = true,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
                }.RegisterInNameScope(scope),
            };
            typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(layers, true);
            return layers;
        });
        return window;
    }

    public void Dispose()
    {
        Window.Close();
        Surface.Dispose();
        motionScope.Dispose();
        frameScope.Dispose();
        actorScope.Dispose();
        Runtime.Dispose();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
