// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-5, ED-NEW-pcws-7, ED-NEW-pcws-8; DESIGN.md 2.13).
// The agent: what `nth.exe --tactus-workspace-agent <pipe> <token>` runs inside the child session. It connects back to nth,
// says hello with its token, and then serves nth: it starts apps there (and waits up to 10 s for their first windows, woken
// by "a window was shown"), lists its windows, and runs tool calls in its own engine over the same backends as on the desk
// (UI Automation, capture, the action ladder), one engine per chat. Its R4 is the child session's own pointer and keyboard,
// so it never borrows the user's and never waits for a still moment of theirs. Every approval and guarded-action card goes
// back to nth outside, which shows it in the pill and the chat as on the desk; a minimized window inside is shown without
// asking (nothing of the user's is there). The engine here keeps no audit (nth writes the chat's log), keeps nothing on
// disk, and has no wait budget of its own: nth keeps the 25 s budget and its tokens, and its stop cancels the step here.
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using static Marrow.Services.Port.PcControl.Workspace.WorkspaceAgentProtocol;

namespace Marrow.Services.Port.PcControl.Workspace;

/// <summary>The settings the agent's engine reads: the latest snapshot nth sent with a call.</summary>
public sealed class WorkspaceAgentSettings
{
    private PcSettings current = ToSettings(null);

    public PcSettings Read() => Volatile.Read(ref current);

    public void Take(PcSettings settings) => Volatile.Write(ref current, settings);
}

/// <summary>A clock whose waits never end by themselves: the agent's engine has no 25 s budget (nth outside keeps it).</summary>
public sealed class WorkspaceNoBudgetClock(IPcClock clock) : IPcClock
{
    public long NowMs => clock.NowMs;

    public Task Delay(int milliseconds, CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
}

/// <summary>
/// The agent's stop key: always armed. nth outside arms the user's stop key for every step it hands in, and its stop cancels
/// the step here; the chord's name is the user's.
/// </summary>
public sealed class WorkspaceArmedStopKey(Func<string> chord) : IPcStopKey
{
    public string Chord => chord();

    public bool Arm() => true;
}

public sealed class WorkspaceAgent : IAsyncDisposable
{
    public const int LaunchWaitMs = 10_000;
    public const int WelcomeTimeoutMs = 10_000;

    public const int ExitDone = 0;
    public const int ExitBroken = 3;
    public const int ExitRefused = 4;

    private readonly PcAssembly assembly;
    private readonly WorkspaceAgentSettings settings;
    private readonly Action<string> log;
    private readonly IPcClock clock;
    private readonly ConcurrentDictionary<string, PcControlEngine> engines = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, CancellationTokenSource> calls = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<WorkspaceMessage>> asks = new();
    private readonly ConcurrentDictionary<string, string> notes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string Thread, string App, PcCapability Capability), string> said = new();
    private readonly AsyncLocal<long?> currentCall = new();
    private readonly ConcurrentDictionary<Task, byte> running = new();
    private WorkspaceLineWriter? writer;
    private CancellationToken stopping;
    private long nextId;

    /// <param name="clock">The link's own clock for the welcome's 10 s (the system clock unless a test gives one).</param>
    public WorkspaceAgent(PcAssembly assembly, WorkspaceAgentSettings settings, Action<string>? log = null, IPcClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(settings);
        this.assembly = assembly;
        this.settings = settings;
        this.log = log ?? (_ => { });
        this.clock = clock ?? new SystemPcClock();
    }

    /// <summary>
    /// The agent's backends: the desk's own kinds of backend, made inside the child session, with no audit (nth outside
    /// writes the chat's log), nothing learned kept on disk, and no wait budget of its own. A known browser starts with
    /// Tactus's own profile under <paramref name="browserProfiles"/>.
    /// </summary>
    public static PcBackends Backends(PcBackends native, string browserProfiles)
    {
        ArgumentNullException.ThrowIfNull(native);
        return new PcBackends
        {
            Catalog = native.Catalog,
            Security = native.Security,
            Reader = native.Reader,
            Feed = native.Feed,
            Messages = native.Messages,
            Foreground = native.Foreground,
            Own = native.Own,
            Launcher = new WorkspaceLauncher(native.Launcher, browserProfiles),
            Clock = native.Clock,
            Input = native.Input,
            Hold = native.Hold,
            RealHost = native.RealHost,
            Capture = native.Capture,
            Audit = _ => PcNoAudit.Instance,
            WaitClock = new WorkspaceNoBudgetClock(native.Clock),
            Now = native.Now,
            CapabilitiesFile = () => null,
            DevProofsFile = null,
            Owned = native.Owned,
        };
    }

    /// <summary>The agent's composition over its backends: the settings nth sends, and a stop key that is always armed.</summary>
    public static PcAssembly Assembly(PcBackends backends, WorkspaceAgentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(backends);
        ArgumentNullException.ThrowIfNull(settings);
        return new PcAssembly(backends, settings.Read, _ => new WorkspaceArmedStopKey(() => settings.Read().StopChord));
    }

    /// <summary>How an app the model named is known: an AUMID ("…!App") or an exe.</summary>
    public static PcAppIdentity Identity(string target) =>
        target.Contains('!', StringComparison.Ordinal) ? new PcAppIdentity("", target) : new PcAppIdentity(target);

    /// <summary>
    /// Says hello with the token, waits up to 10 s for welcome, then serves nth until it says bye or the link ends: 0 when
    /// it ended, 3 when the link broke, 4 when nth refused the agent.
    /// </summary>
    public async Task<int> Run(Stream stream, string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new WorkspaceLineReader(stream);
        using var write = new WorkspaceLineWriter(stream);
        writer = write;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopping = stop.Token;
        try
        {
            var hello = Message(Hello);
            hello["token"] = token;
            hello["pid"] = Environment.ProcessId;
            await write.Write(hello, stop.Token).ConfigureAwait(false);
            var answer = await AwaitWelcome(reader, stop.Token).ConfigureAwait(false);
            if (answer is not { Type: Welcome, Newer: false })
            {
                log("workspace.agent.refused" + (answer is null ? "" : " " + Describe(answer)));
                return ExitRefused;
            }
            log("workspace.agent.welcome");
            while (await reader.ReadLine(stop.Token).ConfigureAwait(false) is { } line)
            {
                if (Parse(line) is not { } message)
                {
                    log("workspace.protocol.malformed");
                    continue;
                }
                if (message.Newer)
                {
                    log("workspace.protocol.newer " + Describe(message));
                    continue;
                }
                if (message.Re is { } re && asks.TryRemove(re, out var waiter))
                {
                    waiter.TrySetResult(message);
                    continue;
                }
                if (message.Type == Bye) break;
                Serve(message, stop.Token);
            }
            log("workspace.agent.done");
            return ExitDone;
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
            log("workspace.agent.broken " + error.GetType().Name);
            return cancellationToken.IsCancellationRequested ? ExitDone : ExitBroken;
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            foreach (var (_, waiter) in asks) waiter.TrySetCanceled(stop.Token);
            asks.Clear();
            foreach (var task in running.Keys) await Settled(task).ConfigureAwait(false);
            writer = null;
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var (_, engine) in engines) engine.Dispose();
        engines.Clear();
        return ValueTask.CompletedTask;
    }

    private async Task<WorkspaceMessage?> AwaitWelcome(WorkspaceLineReader reader, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reading = reader.ReadLine(wait.Token);
        var timeout = clock.Delay(WelcomeTimeoutMs, wait.Token);
        var first = await Task.WhenAny(reading, timeout).ConfigureAwait(false);
        await wait.CancelAsync().ConfigureAwait(false);
        if (first != reading)
        {
            _ = reading.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return null;
        }
        return Parse(await reading.ConfigureAwait(false));
    }

    // One request from nth, served on its own task (calls run side by side, each in its app's stream).
    private void Serve(WorkspaceMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case WorkspaceAgentProtocol.Launch:
                Track(Launch(message, cancellationToken));
                break;
            case WindowsAsk:
                Track(Windows(message, cancellationToken));
                break;
            case WindowAsk:
                Track(Window(message, cancellationToken));
                break;
            case Call:
                Track(RunCall(message, cancellationToken));
                break;
            case Cancel:
                if (message.Long("call") is { } id && calls.TryGetValue(id, out var call)) call.Cancel();
                break;
            case WorkspaceAgentProtocol.EndTurn:
                if (message.Str("thread") is { } thread && engines.TryGetValue(thread, out var engine)) engine.EndTurn();
                break;
            default:
                log("workspace.protocol.unexpected " + Describe(message));
                break;
        }
    }

    // A served request's end once the link is going: a broken link or a cancelled step is expected then.
    private static async Task Settled(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // logged by Track's continuation when it faulted; a cancelled step needs no line
            _ = error;
        }
    }

    private void Track(Task task)
    {
        running[task] = 0;
        task.ContinueWith(t =>
        {
            running.TryRemove(t, out _);
            if (t.IsFaulted) log("workspace.agent.task.failed " + t.Exception!.InnerException?.GetType().Name);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task Launch(WorkspaceMessage message, CancellationToken cancellationToken)
    {
        if (message.Id is not { } id) return;
        var reply = Reply(Launched, id);
        var target = message.Str("target");
        if (string.IsNullOrWhiteSpace(target))
        {
            reply["started"] = false;
            reply["problem"] = "no app was named";
            await Send(reply, cancellationToken).ConfigureAwait(false);
            return;
        }
        var parts = assembly.Shared;
        var app = Identity(target.Trim());
        var before = parts.Catalog.Windows().Select(w => w.Handle).ToHashSet();
        parts.Feed.Watch(PcChangeFeed.Desktop, 0);
        var launch = parts.Launcher.Launch(target.Trim());
        if (!launch.Started)
        {
            reply["started"] = false;
            reply["problem"] = launch.Problem ?? "Windows could not start it";
            await Send(reply, cancellationToken).ConfigureAwait(false);
            return;
        }
        // The app's first window, woken by "a window was shown" events, at most 10 s.
        var deadline = parts.Clock.NowMs + LaunchWaitMs;
        List<PcWindowInfo> fresh;
        while (true)
        {
            var version = parts.Feed.Version(PcChangeFeed.Desktop);
            fresh = [.. parts.Catalog.Windows().Where(w => !before.Contains(w.Handle) && !parts.Own.Owns(w.Handle)
                && (launch.ProcessId is { } pid ? w.ProcessId == pid || w.App.Key == app.Key : w.App.Key == app.Key))];
            var left = deadline - parts.Clock.NowMs;
            if (fresh.Count > 0 || left <= 0) break;
            await parts.Feed.Changed(PcChangeFeed.Desktop, version, (int)left, cancellationToken).ConfigureAwait(false);
        }
        reply["started"] = true;
        reply["windows"] = ToJson(fresh.Select(Facts));
        await Send(reply, cancellationToken).ConfigureAwait(false);
    }

    private async Task Windows(WorkspaceMessage message, CancellationToken cancellationToken)
    {
        if (message.Id is not { } id) return;
        var parts = assembly.Shared;
        var reply = Reply(WindowsReply, id);
        reply["windows"] = ToJson(parts.Catalog.Windows().Where(w => !parts.Own.Owns(w.Handle)).Select(Facts));
        await Send(reply, cancellationToken).ConfigureAwait(false);
    }

    private async Task Window(WorkspaceMessage message, CancellationToken cancellationToken)
    {
        if (message.Id is not { } id) return;
        var reply = Reply(WindowReply, id);
        var handle = message.Long("handle") ?? 0;
        reply["window"] = handle > 0 && handle < WorkspaceRefs.Bit && assembly.Shared.Catalog.Window((nint)handle) is { } info && !assembly.Shared.Own.Owns(info.Handle)
            ? ToJson(Facts(info)) : null;
        await Send(reply, cancellationToken).ConfigureAwait(false);
    }

    private WorkspaceWindow Facts(PcWindowInfo info) => new(info, PcPolicy.IsElevated(assembly.Shared.Security.Process(info.ProcessId)));

    private async Task RunCall(WorkspaceMessage message, CancellationToken cancellationToken)
    {
        if (message.Id is not { } id) return;
        var thread = message.Str("thread");
        var tool = message.Str("tool");
        PcResult result;
        if (thread is not { Length: > 0 } || tool is not { Length: > 0 } || message.Body["args"] is not JsonObject args)
        {
            result = PcResult.Fail(PcErrorCode.BadArgs, "the step reached the workspace without its chat, tool or arguments");
        }
        else
        {
            settings.Take(ToSettings(message.Body["settings"]));
            var vision = message.Bool("vision") ?? true;
            var head = message.Str("head");
            var grid = ToGrid(message.Body["grid"]);
            var model = message.Str("model");
            var engine = engines.GetOrAdd(thread, key => new PcControlEngine(assembly.WorkspaceParts(new Approver(this), new Confirmer(this)),
                new PcCaller(key, () => true, vision) { Head = head, Grid = grid, Model = model }));
            using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            calls[id] = call;
            currentCall.Value = id;
            try
            {
                result = await engine.Call(tool, (JsonObject)args.DeepClone(), call.Token).ConfigureAwait(false);
            }
            finally
            {
                calls.TryRemove(id, out _);
            }
        }
        var reply = Reply(Result, id);
        reply["result"] = ToJson(result);
        await Send(reply, cancellationToken).ConfigureAwait(false);
    }

    private async Task Send(JsonObject message, CancellationToken cancellationToken)
    {
        if (writer is not { } write) return;
        await write.Write(message, cancellationToken).ConfigureAwait(false);
    }

    // An ask for the user, answered by nth outside; it waits until answered or the step is cancelled.
    private async Task<WorkspaceMessage> AskOutside(JsonObject request, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref nextId);
        request["id"] = id;
        request["call"] = currentCall.Value;
        var waiter = new TaskCompletionSource<WorkspaceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        asks[id] = waiter;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping);
            await Send(request, linked.Token).ConfigureAwait(false);
            return await waiter.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            asks.TryRemove(id, out _);
        }
    }

    /// <summary>Approvals for steps inside: asked of the user by nth outside, the same card as on the desk.</summary>
    private sealed class Approver(WorkspaceAgent agent) : IPcApprover
    {
        // No standing grant is kept here: nth outside answers every ask (from its own grants first).
        public PcApproval? Standing(PcAppIdentity app, PcCapability capability) => null;

        public async Task<PcApproval> Ask(PcAskSource source, PcAppIdentity app, string appName, PcCapability capability, bool risky, bool allowAlways, CancellationToken cancellationToken)
        {
            var request = Message(Approve);
            request["thread"] = source.Thread;
            request["exe"] = app.ExePath;
            request["aumid"] = app.Aumid;
            request["name"] = appName;
            request["capability"] = Wire(capability);
            var reply = await agent.AskOutside(request, cancellationToken).ConfigureAwait(false);
            var answer = ToApproval(reply.Str("answer"));
            if (answer is not (PcApproval.Chat or PcApproval.Always) && reply.Str("said") is { Length: > 0 } words) agent.said[(source.Thread, app.Key, capability)] = words;
            // An "Always" is a standing grant nth outside keeps and checks; here it counts for the chat.
            return answer == PcApproval.Always ? PcApproval.Chat : answer;
        }

        public string? Reason(string thread, PcAppIdentity app, PcCapability capability) =>
            agent.said.TryRemove((thread, app.Key, capability), out var words) ? words : null;
    }

    /// <summary>Guarded-action cards for steps inside: shown by nth outside; a minimized window inside is shown without asking.</summary>
    private sealed class Confirmer(WorkspaceAgent agent) : IPcConfirmer
    {
        public async Task<bool> Confirm(PcAskSource source, string word, string elementName, string appName, CancellationToken cancellationToken)
        {
            var request = Message(WorkspaceAgentProtocol.Confirm);
            request["thread"] = source.Thread;
            request["word"] = word;
            request["element"] = elementName;
            request["app"] = appName;
            var reply = await agent.AskOutside(request, cancellationToken).ConfigureAwait(false);
            var allowed = reply.Bool("allowed") ?? false;
            if (!allowed && reply.Str("note") is { Length: > 0 } note) agent.notes[source.Thread] = note;
            return allowed;
        }

        // Inside the workspace nothing is the user's, so a minimized window is shown for the step without asking.
        public Task<PcShowAnswer> Show(PcAskSource source, string appName, string windowTitle, CancellationToken cancellationToken) =>
            Task.FromResult(PcShowAnswer.Show);

        public string? Note(string thread) => agent.notes.TryRemove(thread, out var note) ? note : null;
    }
}
