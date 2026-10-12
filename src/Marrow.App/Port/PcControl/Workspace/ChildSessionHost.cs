// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-2, ED-NEW-pcws-5, ED-NEW-pcws-7; DESIGN.md 2.13,
// 3.1). The workspace as the app runs it, behind IWorkspaceHost. Its status reads Windows' child-session switch
// (WTSIsChildSessionsEnabled) and never writes it. The first app Tactus starts there starts the workspace once (one start at
// a time): a fresh current-user pipe and token, the cloaked WorkspaceView connected to a child session whose shell is
// `"<nth.exe>" --tactus-workspace-agent <pipe> <token>`, then the agent's hello within 30 s. Listing and steps never start it.
// While the preview is not released (WorkspacePreview) nothing starts at all. When the session ends or the link breaks, the
// workspace is Ready again and the next launch starts a new one. The token and the program line are never logged; log lines
// are event names (a capped file in the Tactus log folder).
using System.IO.Pipes;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.App.Port.PcControl.Workspace;

/// <summary>What ChildSessionHost runs on: the native pieces in the app, fakes in tests.</summary>
internal sealed class ChildSessionSeams
{
    public required IChildSessionReadiness Readiness { get; init; }

    /// <summary>Makes the workspace's window (cloaked, on the UI thread inside).</summary>
    public required Func<IWorkspaceSurface> Surface { get; init; }

    /// <summary>A current-user pipe server by name, connected once the agent connects.</summary>
    public required Func<string, CancellationToken, Task<Stream>> Accept { get; init; }

    /// <summary>nth's own exe, which the child session starts as the agent.</summary>
    public required string Executable { get; init; }

    public IPcClock Clock { get; init; } = new SystemPcClock();

    public Action<string> Log { get; init; } = _ => { };

    public bool Released { get; init; } = WorkspacePreview.Released;

    public Func<string> NewName { get; init; } = WorkspacePipe.NewName;

    public Func<string> NewToken { get; init; } = WorkspacePipe.NewToken;
}

internal sealed class ChildSessionHost : IWorkspaceHost, IDisposable
{
    public const int AcceptTimeoutMs = 30_000;
    public const string NotRunning = "the separate workspace is not running";

    private readonly ChildSessionSeams seams;
    private readonly SemaphoreSlim starting = new(1, 1);
    private readonly object gate = new();
    private Session? session;
    private TaskCompletionSource givenBack = Done();
    private int apps;
    private bool disposed;
    private string? lastProblem;

    public ChildSessionHost(ChildSessionSeams seams)
    {
        ArgumentNullException.ThrowIfNull(seams);
        this.seams = seams;
    }

    public bool Released => seams.Released;

    public WorkspaceStatus Status()
    {
        if (Live() is not null) return new WorkspaceStatus(WorkspaceState.Running, Volatile.Read(ref apps));
        return WorkspaceStatus.Of(seams.Readiness.Enabled());
    }

    /// <summary>"Watch workspace": the view-only picture in picture; false when no workspace runs.</summary>
    public bool Watch()
    {
        if (Live() is not { } live) return false;
        live.Surface.Watch();
        return true;
    }

    public async Task<WorkspaceLaunch> Launch(string appIdOrPath, CancellationToken cancellationToken)
    {
        if (await Start(cancellationToken).ConfigureAwait(false) is not { } link) return WorkspaceLaunch.Failed(Volatile.Read(ref lastProblem) ?? NotRunning);
        var launched = await link.Launch(appIdOrPath, cancellationToken).ConfigureAwait(false);
        await Count(link, cancellationToken).ConfigureAwait(false);
        return launched;
    }

    public async Task<IReadOnlyList<WorkspaceWindow>> Windows(CancellationToken cancellationToken)
    {
        if (Live() is not { } live) return [];
        var windows = await live.Link.Windows(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref apps, windows.Select(w => w.Info.App.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        return windows;
    }

    public Task<WorkspaceWindow?> Window(long handle, CancellationToken cancellationToken) =>
        Live() is { } live ? live.Link.Window(handle, cancellationToken) : Task.FromResult<WorkspaceWindow?>(null);

    public Task<PcResult> Call(WorkspaceCall call, IWorkspaceAsks asks, CancellationToken cancellationToken) =>
        Live() is { } live ? live.Link.Call(call, asks, cancellationToken) : Task.FromResult(PcResult.Fail(PcErrorCode.WindowGone, NotRunning));

    public Task WhileTakenOver(CancellationToken cancellationToken)
    {
        lock (gate) return givenBack.Task.WaitAsync(cancellationToken);
    }

    public void EndTurn(string thread) => Live()?.Link.EndTurn(thread);

    public void Dispose()
    {
        Session? ending;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            ending = session;
            session = null;
            givenBack.TrySetResult();
        }
        if (ending is not null) End(ending, "workspace.host.disposed");
    }

    private Session? Live()
    {
        var live = Volatile.Read(ref session);
        return live is { Link.IsOpen: true } ? live : null;
    }

    // Starts the workspace once: the view connected to a child session that runs the agent, and the agent's hello.
    private async Task<WorkspaceLink?> Start(CancellationToken cancellationToken)
    {
        if (Live() is { } live) return live.Link;
        if (!seams.Released) return Refuse(WorkspacePreview.NotReleasedLine);
        await starting.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Live() is { } again) return again.Link;
            if (Volatile.Read(ref disposed)) return Refuse(NotRunning);
            if (seams.Readiness.Enabled() != true) return Refuse("the separate workspace is not set up (Settings, Tactus, Separate workspace)");
            var name = seams.NewName();
            var token = seams.NewToken();
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var accepting = seams.Accept(name, waiting.Token);
            var surface = seams.Surface();
            seams.Log("workspace.start");
            if (await surface.Connect(WorkspacePipe.StartProgram(seams.Executable, name, token), cancellationToken).ConfigureAwait(false) is { } problem)
            {
                await waiting.CancelAsync().ConfigureAwait(false);
                Forget(accepting);
                surface.Dispose();
                seams.Log("workspace.start.failed");
                return Refuse(problem);
            }
            var timeout = seams.Clock.Delay(AcceptTimeoutMs, waiting.Token);
            if (await Task.WhenAny(accepting, timeout).ConfigureAwait(false) != accepting || !accepting.IsCompletedSuccessfully)
            {
                await waiting.CancelAsync().ConfigureAwait(false);
                Forget(accepting);
                surface.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                seams.Log("workspace.agent.silent");
                return Refuse("the workspace's agent did not connect within 30 s");
            }
            await waiting.CancelAsync().ConfigureAwait(false);
            var stream = await accepting.ConfigureAwait(false);
            if (await WorkspaceLink.Accept(stream, token, seams.Clock, seams.Log, cancellationToken).ConfigureAwait(false) is not { } link)
            {
                surface.Dispose();
                return Refuse("the workspace's agent was refused");
            }
            var made = new Session(surface, link);
            surface.TakenOver += TakenOver;
            surface.Ended += () => End(made, "workspace.session.ended");
            link.Closed += () => End(made, "workspace.link.closed");
            lock (gate)
            {
                if (disposed)
                {
                    End(made, "workspace.host.disposed");
                    return null;
                }
                session = made;
            }
            Volatile.Write(ref lastProblem, null);
            seams.Log("workspace.running");
            return link;
        }
        finally
        {
            starting.Release();
        }
    }

    private WorkspaceLink? Refuse(string problem)
    {
        Volatile.Write(ref lastProblem, problem);
        return null;
    }

    private async Task Count(WorkspaceLink link, CancellationToken cancellationToken)
    {
        var windows = await link.Windows(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref apps, windows.Select(w => w.Info.App.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private void TakenOver(bool taken)
    {
        lock (gate)
        {
            if (taken && givenBack.Task.IsCompleted) givenBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            else if (!taken) givenBack.TrySetResult();
        }
    }

    // The session ended, the link closed or nth is closing: the workspace is Ready again, the view goes, steps go on.
    private void End(Session ended, string why)
    {
        lock (gate)
        {
            if (ended.Ending) return;
            ended.Ending = true;
            if (ReferenceEquals(session, ended)) session = null;
            givenBack.TrySetResult();
        }
        Volatile.Write(ref apps, 0);
        seams.Log(why);
        ended.Surface.Dispose();
        Forget(ended.Link.DisposeAsync().AsTask());
    }

    private void Forget(Task task) => task.ContinueWith(t => seams.Log("workspace.task.failed " + t.Exception!.InnerException?.GetType().Name),
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static TaskCompletionSource Done()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    private sealed class Session(IWorkspaceSurface surface, WorkspaceLink link)
    {
        public IWorkspaceSurface Surface { get; } = surface;

        public WorkspaceLink Link { get; } = link;

        public bool Ending { get; set; }
    }
}

/// <summary>The app's workspace: the native readiness, the view on the UI thread, a current-user pipe, a capped log.</summary>
internal static class ChildSessionDesktop
{
    public const long LogCapBytes = 256 * 1024;

    public static ChildSessionSeams Seams() => new()
    {
        Readiness = new ChildSessionReadiness(),
        Surface = () => Avalonia.Threading.Dispatcher.UIThread.Invoke(() => (IWorkspaceSurface)new WorkspaceView(new RdpActiveX(), new WorkspaceWindowing())),
        Accept = Accept,
        Executable = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "nth.exe"),
        Log = Log("workspace.log"),
    };

    /// <summary>The agent's log: the same capped file kind, inside the child session.</summary>
    public static Action<string> AgentLog() => Log("workspace-agent.log");

    // A pipe only this user can open (PipeOptions.CurrentUserOnly), one connection, waiting for the agent.
    private static async Task<Stream> Accept(string name, CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Event names only, one per line with the time, written in order off the caller's thread; the file starts over past 256 KB.
    private static Action<string> Log(string file)
    {
        var gate = new object();
        var tail = Task.CompletedTask;
        return line =>
        {
            var stamped = DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + " " + line + Environment.NewLine;
            lock (gate) tail = tail.ContinueWith(_ => Append(file, stamped), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        };
    }

    private static void Append(string file, string text)
    {
        try
        {
            var folder = StoragePaths.Directory("pc-control");
            var path = Path.Combine(folder, file);
            Directory.CreateDirectory(folder);
            if (File.Exists(path) && new FileInfo(path).Length > LogCapBytes) File.Delete(path);
            File.AppendAllText(path, text);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // a log that cannot be written never stops the workspace
            _ = error;
        }
    }
}
