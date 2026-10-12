using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Views;
using Marrow.App.Views.Panes;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The Side chat pane inside the real detail view (web W6): one thread of a real app model on a headless window, the pane the app
/// registers for Side chat, and scripted offline sessions for everything the side chat launches (a dummy executable satisfies the
/// installed check, the launcher is replaced). It is for a test that already runs on the headless UI thread.
/// </summary>
public sealed class SideChatPaneRig : IDisposable
{
    private readonly List<IDisposable> scopes = [];
    private readonly string executable;

    public SideChatPaneRig(bool reduceMotion = false, double width = 1400, double height = 900)
    {
        executable = Path.Combine(Path.GetTempPath(), "nth-w6-pane-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(executable, []);
        // LoginEnvironment.Load is completed once on a pool thread first, so a later Load().Wait() on the headless UI thread returns at once.
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        H = new AppModelHarness();
        Frames = new ManualFrameSource();
        scopes.Add(DragHandleView.CursorPlatform.Override(new ChatNoCursor()));
        scopes.Add(FrameClock.Source.Override(Frames));
        scopes.Add(Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion }));
        scopes.Add(AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            var session = new ThreadRuntimeScriptedSession();
            Script?.Invoke(session);
            AllLaunches.Add((configuration, session));
            return new AccountSessionLaunch(configuration, session);
        }));
        Model = H.MakeModel();
        H.PrepareBootstrap(Model);
        Window = new Avalonia.Controls.Window { Width = width, Height = height };
        H.Main.OnMain(() =>
        {
            Model.Settings.SetBinaryPath(executable, ProviderKind.Claude);
            Model.Settings.ShowsUsagePanel = false;
            Model.Settings.HydraEnabled = false;
            var project = Model.AddProject(new Project("Project", H.NewFolder("project")).Url);
            var thread = Model.NewThread(project)!;
            ThreadId = thread.Id;
            Model.UpdateThread(ThreadId, current => current with { Provider = ProviderKind.Claude, Model = "offline-model" });
            Resize = new WindowLiveResize(Window);
            Window.Show();
        });
        H.Main.Loop.Run(() => Model.Runtime(ThreadId).EnsureLoaded(), AppModelHarness.Bound);
        scopes.Add(MainActor.Executor.Override(AvaloniaMainExecutor.Instance));
        Detail = new DetailView
        {
            PaneAvailable = kind => kind == PaneKind.SideChat,
            PaneFactory = (kind, runtime) => kind == PaneKind.SideChat ? SideChatPane.Create(kind, runtime) : null,
        };
        H.Main.OnMain(() =>
        {
            Model.SelectedThreadID = ThreadId;
            AppEnvironment.SetAppModel(Detail, Model);
            AppEnvironment.SetWindowLiveResize(Detail, Resize);
            Window.Content = Detail;
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
        Pump();
    }

    public AppModelHarness H { get; }

    public AppModel Model { get; }

    public ManualFrameSource Frames { get; }

    public Guid ThreadId { get; private set; }

    public Avalonia.Controls.Window Window { get; }

    public WindowLiveResize Resize { get; private set; } = null!;

    public DetailView Detail { get; }

    /// <summary>Scripts each side-chat session as it is made.</summary>
    public Action<ThreadRuntimeScriptedSession>? Script { get; set; }

    /// <summary>Every session anything launched (the main thread's composer warms its own when a draft is typed, so this holds more than the side chat's).</summary>
    public List<(SessionConfiguration Configuration, ThreadRuntimeScriptedSession Session)> AllLaunches { get; } = [];

    /// <summary>Each session the side chat launched, in order.</summary>
    public List<(SessionConfiguration Configuration, ThreadRuntimeScriptedSession Session)> Launches =>
        AllLaunches.Where(launch => launch.Configuration.IgnisThreadID is { } thread && thread != ThreadId).ToList();

    public PaneWorkspace Workspace => Detail.Workspace ?? throw new InvalidOperationException("no workspace");

    /// <summary>Runs <paramref name="body"/> on the main actor: now when the caller is already on it (inside a wait's condition), else on the loop.</summary>
    public T OnMain<T>(Func<T> body) => MainActor.IsIsolated ? body() : H.Main.OnMain(body);

    public ThreadRuntime Runtime => OnMain(() => Model.Runtime(ThreadId));

    public ChatThread? Side => OnMain(() => Model.SideChat(ThreadId));

    public PaneCard? Card => Workspace.CardOf(PaneKind.SideChat);

    public SideChatPane? Pane => Card?.Content.Body as SideChatPane;

    /// <summary>One round of the dispatcher, the layout and a millisecond of the manual clock.</summary>
    public void Pump()
    {
        H.Main.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        });
        H.Main.AdvanceMilliseconds(1);
        H.Main.OnMain(() =>
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
    }

    /// <summary>Pumps (with real time passing for work on other threads) until <paramref name="done"/> holds.</summary>
    public void Until(Func<bool> done, string what = "the condition")
    {
        var end = DateTime.UtcNow + TimeSpan.FromSeconds(25);
        while (!OnMain(done))
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("Timed out waiting for " + what + ".");
            Pump();
            Thread.Sleep(5);
        }
    }

    /// <summary>Frames until the workspace stops moving.</summary>
    public void Settle()
    {
        for (var frame = 0; frame < 400; frame++)
        {
            Pump();
            Frames.Advance(TimeSpan.FromMilliseconds(16));
            H.Main.AdvanceMilliseconds(16);
            if (!Workspace.IsMoving && Frames.Pending == 0) break;
        }
        Pump();
    }

    /// <summary>Opens Side chat the way another surface does, by the thread's state.</summary>
    public void Open()
    {
        H.Main.OnMain(() => Runtime.Workspace = Runtime.Workspace.Open(PaneKind.SideChat));
        Settle();
        Until(() => Pane is not null && Model.SideChat(ThreadId) is not null && Pane.SideChatId is not null, "the side chat to be made and bound");
        Settle();
    }

    public void Dispose()
    {
        H.Main.OnMain(() =>
        {
            foreach (var launch in AllLaunches) launch.Session.Stop();
            Window.Hide();
            Window.Content = null;
            Dispatcher.UIThread.RunJobs();
        });
        foreach (var scope in scopes.AsEnumerable().Reverse()) scope.Dispose();
        H.Dispose();
        try { File.Delete(executable); }
        catch (IOException) { }
    }
}
