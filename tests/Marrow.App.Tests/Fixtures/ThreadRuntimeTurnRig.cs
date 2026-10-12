using System.Reflection;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>One thread runtime over a scripted offline session: the session is installed with the exact launch
/// signature EnsureSession computes, so a turn reuses it and no provider executable is ever resolved. The harness
/// makes every CLI executable missing and keeps API keys empty (AppModelHarness.PrepareBootstrap).</summary>
public sealed class ThreadRuntimeTurnRig : IDisposable
{
    public ThreadRuntimeTurnRig(
        ProviderKind provider = ProviderKind.Claude,
        Func<ChatThread, ChatThread>? configure = null,
        string? projectFolder = null,
        bool install = true,
        Action<AppModel>? settings = null,
        bool hydraOn = false,
        Func<AppModelHarness, string>? projectFactory = null,
        Func<AppModelHarness, ChatThread, ChatThread>? configureWithHarness = null)
    {
        H = new AppModelHarness();
        Project = AppModelSamples.Project("Project", projectFolder ?? projectFactory?.Invoke(H) ?? H.NewFolder("project"));
        var thread = AppModelSamples.Thread(Project) with { Provider = provider, Model = "offline-model", HydraEnabled = hydraOn };
        if (configure is not null) thread = configure(thread);
        if (configureWithHarness is not null) thread = configureWithHarness(H, thread);
        InitialThread = thread;
        H.WriteLibrary([Project], [thread]);
        App = H.MakeModel();
        H.PrepareBootstrap(App);
        if (settings is not null) H.Main.OnMain(() => settings(App));
        Runtime = H.Main.OnMain(() => App.Runtime(thread.Id));
        H.Main.Loop.Run(() => Runtime.EnsureLoaded(), AppModelHarness.Bound);
        Session = new ThreadRuntimeScriptedSession();
        if (install) Install(Session);
    }

    public AppModelHarness H { get; }
    public Project Project { get; }
    public ChatThread InitialThread { get; }
    public AppModel App { get; }
    public ThreadRuntime Runtime { get; }
    public ThreadRuntimeScriptedSession Session { get; private set; }

    /// <summary>The thread record now.</summary>
    public ChatThread Thread => H.Main.OnMain(() => App.Thread(InitialThread.Id)!);

    /// <summary>The directory a session runs in: the worktree when the thread has one, else the project.</summary>
    public string Directory => InitialThread.WorktreePath ?? Project.Path;

    /// <summary>Makes <paramref name="session"/> the runtime's live session, the way EnsureSession would have left it.</summary>
    public void Install(ThreadRuntimeScriptedSession session, bool? antiSlopEnabled = null, bool observe = true)
    {
        Session = session;
        void Apply()
        {
            var current = App.Thread(InitialThread.Id)!;
            if (observe) Runtime.Observe(session);
            var keeps = (bool)typeof(ThreadRuntime).GetMethod("KeepsHydraPolicyInSystemPrompt", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [current.Provider])!;
            var signature = typeof(ThreadRuntime).GetMethod("MakeSessionSignature", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [current, Directory, antiSlopEnabled ?? App.Settings.AntiSlopEnabled, keeps ? App.HydraLaunch(current) : null]);
            ThreadRuntimeReflection.Set(Runtime, "session", session);
            ThreadRuntimeReflection.Set(Runtime, "sessionSignature", signature);
        }
        // From inside a main-loop action (the loop runs everything an action posts before OnMain returns), install in place.
        if (H.Main.Loop.CheckAccess()) Apply();
        else H.Main.OnMain(Apply);
    }

    /// <summary>Runs StartTurn (ThreadRuntime.swift:1175) up to its first real suspension on the main loop and hands
    /// back its task; <paramref name="whileStarting"/> runs at that point, before anything else is pumped.</summary>
    public Task StartTurn(
        string text,
        IReadOnlyList<Attachment>? attachments = null,
        IReadOnlyList<long>? hydraHeads = null,
        bool hydraBrief = false,
        Action? whileStarting = null)
    {
        Task task = null!;
        H.Main.OnMain(() =>
        {
            task = ThreadRuntimeReflection.Call<Task>(Runtime, "StartTurn", text, attachments ?? [], hydraHeads, hydraBrief)!;
            whileStarting?.Invoke();
        });
        return task;
    }

    /// <summary>Pumps the main loop until the task is done.</summary>
    public void Finish(Task task) => H.Main.Loop.Run(() => task, AppModelHarness.Bound);

    public void Send(string text, IReadOnlyList<Attachment>? attachments = null, IReadOnlyList<long>? hydraHeads = null) =>
        Finish(StartTurn(text, attachments, hydraHeads));

    /// <summary>The provider ends the running turn; pumps until the runtime is idle.</summary>
    public void Complete(TurnStatus status = TurnStatus.Completed)
    {
        H.Main.OnMain(() => Session.Emit(new ProviderEvent.TurnCompleted(status, null)));
        H.Main.Loop.RunUntil(() => Runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
    }

    public void Dispose() => H.Dispose();
}
