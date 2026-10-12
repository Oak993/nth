using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A main thread on a scripted offline session (ThreadRuntimeTurnRig) and, beside it, the side chat the app makes for it. Every
/// provider session the side chat launches is a scripted one too: the launcher is replaced, a dummy executable satisfies the
/// installed check, and each launch is kept with its configuration, so a test reads what a turn was sent with and how many fresh
/// sessions it took. Nothing here starts a process or reaches a network.
/// </summary>
public sealed class SideChatRig : IDisposable
{
    private readonly IDisposable launcher;
    private readonly string executable;

    public SideChatRig(Func<ChatThread, ChatThread>? configure = null, Action<ThreadRuntimeScriptedSession>? script = null)
    {
        executable = Path.Combine(Path.GetTempPath(), "nth-w6-claude-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(executable, []);
        Script = script;
        Main = new ThreadRuntimeTurnRig(ProviderKind.Claude, configure, settings: app => app.Settings.SetBinaryPath(executable, ProviderKind.Claude));
        launcher = AccountSessionFactory.Launcher.Override((registry, configuration, id, fresh) =>
        {
            var session = new ThreadRuntimeScriptedSession();
            Script?.Invoke(session);
            Launches.Add((configuration, session));
            return new AccountSessionLaunch(configuration, session);
        });
    }

    /// <summary>Scripts every side-chat session as it is made.</summary>
    public Action<ThreadRuntimeScriptedSession>? Script { get; set; }

    /// <summary>The main thread's rig.</summary>
    public ThreadRuntimeTurnRig Main { get; }

    public AppModel App => Main.App;

    public AppModelHarness H => Main.H;

    public Guid ParentId => Main.InitialThread.Id;

    /// <summary>Each session the side chat launched, in order, with the configuration it was launched with.</summary>
    public List<(SessionConfiguration Configuration, ThreadRuntimeScriptedSession Session)> Launches { get; } = [];

    /// <summary>The side chat's thread record now, if it has one.</summary>
    /// <summary>Runs <paramref name="body"/> on the main actor: now when the caller is already on it (inside a wait's condition), else on the loop.</summary>
    public T OnMain<T>(Func<T> body) => MainActor.IsIsolated ? body() : H.Main.OnMain(body);

    public ChatThread? Side => OnMain(() => App.SideChat(ParentId));

    public ThreadRuntime SideRuntime => OnMain(() => App.Runtime(App.SideChat(ParentId)!.Id));

    public ChatThread MainThread => OnMain(() => App.Thread(ParentId)!);

    public ChatThread MakeSide() => OnMain(() => App.EnsureSideChat(ParentId)!);

    /// <summary>The person asks the side chat; true when the turn was taken.</summary>
    public bool Ask(string text) => OnMain(() => App.SendSideChat(ParentId, text));

    /// <summary>Pumps the main loop until <paramref name="done"/> holds (it is read on the loop).</summary>
    public void Until(Func<bool> done) => H.Main.Loop.RunUntil(done, AppModelHarness.Bound);

    /// <summary>The main thread finishes a turn: the person asked <paramref name="question"/> and the model answered <paramref name="answer"/>.</summary>
    public void MainTurn(string question, string answer)
    {
        Main.Send(question);
        H.Main.OnMain(() => Main.Session.Emit(new ProviderEvent.MessageCompleted("m-" + Guid.NewGuid().ToString("N"), answer)));
        Main.Complete();
    }

    /// <summary>Waits for the side chat's <paramref name="count"/>th launch and returns its session.</summary>
    public ThreadRuntimeScriptedSession Launch(int count)
    {
        Until(() => Launches.Count >= count && Launches[count - 1].Session.Inputs.Count >= 1);
        return Launches[count - 1].Session;
    }

    /// <summary>The side chat's model answers on <paramref name="session"/> and the turn ends.</summary>
    public void Answer(ThreadRuntimeScriptedSession session, string text, TurnStatus status = TurnStatus.Completed)
    {
        H.Main.OnMain(() =>
        {
            if (text.Length != 0) session.Emit(new ProviderEvent.MessageCompleted("s-" + Guid.NewGuid().ToString("N"), text));
            session.Emit(new ProviderEvent.TurnCompleted(status, null));
        });
        Until(() => SideRuntime.Phase == RuntimePhase.Idle);
    }

    public void Dispose()
    {
        Main.H.Main.OnMain(() =>
        {
            foreach (var launch in Launches) launch.Session.Stop();
        });
        Main.Dispose();
        launcher.Dispose();
        try { File.Delete(executable); }
        catch (IOException) { }
    }
}
