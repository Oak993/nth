// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-5, ED-NEW-pcws-6, ED-NEW-pcws-8; DESIGN.md 2.13).
// nth's end of the link to its agent inside the child session. It reads the agent's hello within 10 s and accepts only the
// token nth gave it and a version it knows; then it asks the agent to launch apps, list its windows and run tool calls, and
// answers the agent's own asks (an approval, a guarded-action card) with the chat's asks, so the user answers them in the
// pill and the chat as on the desk. A launch is answered within 20 s and a window query within 5 s (on the engine's clock),
// a tool call has no limit of its own (the engine's 25 s budget and the user's stop end it; a stopped call tells the agent
// to cancel). A broken or closed link fails every waiting request at once ("the separate workspace closed"). Log lines carry
// a stable event name and the message's type and ids (WorkspaceAgentProtocol.Describe), never the token or an argument.
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using static Marrow.Services.Port.PcControl.Workspace.WorkspaceAgentProtocol;

namespace Marrow.Services.Port.PcControl.Workspace;

/// <summary>The link closed while a request waited.</summary>
public sealed class WorkspaceClosedException() : IOException("the separate workspace closed");

public sealed class WorkspaceLink : IAsyncDisposable
{
    public const int HelloTimeoutMs = 10_000;
    public const int LaunchTimeoutMs = 20_000;
    public const int QueryTimeoutMs = 5_000;

    private readonly Stream stream;
    private readonly WorkspaceLineReader reader;
    private readonly WorkspaceLineWriter writer;
    private readonly IPcClock clock;
    private readonly Action<string> log;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<WorkspaceMessage>> pending = new();
    private readonly ConcurrentDictionary<long, IWorkspaceAsks> callAsks = new();
    private readonly ConcurrentDictionary<string, IWorkspaceAsks> threadAsks = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource closing = new();
    private Task loop = Task.CompletedTask;
    private long nextId;
    private int closed;

    private WorkspaceLink(Stream stream, WorkspaceLineReader reader, IPcClock clock, Action<string> log)
    {
        this.stream = stream;
        this.reader = reader;
        this.clock = clock;
        this.log = log;
        writer = new WorkspaceLineWriter(stream);
    }

    /// <summary>The link closed (the agent ended, the pipe broke, or nth closed it); raised once.</summary>
    public event Action? Closed;

    public bool IsOpen => Volatile.Read(ref closed) == 0;

    /// <summary>
    /// Waits for the agent's hello on a fresh connection; with the right token and a known version it answers welcome and
    /// returns the link. Otherwise (silent for 10 s, malformed, a newer version, a wrong token) it closes the stream and
    /// returns null.
    /// </summary>
    public static async Task<WorkspaceLink?> Accept(Stream stream, string token, IPcClock clock, Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(log);
        var reader = new WorkspaceLineReader(stream);
        WorkspaceMessage? hello;
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            var reading = reader.ReadLine(wait.Token);
            var timeout = clock.Delay(HelloTimeoutMs, wait.Token);
            var first = await Task.WhenAny(reading, timeout).ConfigureAwait(false);
            await wait.CancelAsync().ConfigureAwait(false);
            if (first != reading)
            {
                Forget(reading);
                log("workspace.hello.timeout");
                await stream.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
            try
            {
                hello = Parse(await reading.ConfigureAwait(false));
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
            {
                log("workspace.hello.broken " + error.GetType().Name);
                await stream.DisposeAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
        }
        if (hello is null || hello.Type != Hello)
        {
            log("workspace.hello.malformed");
            await stream.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        var link = new WorkspaceLink(stream, reader, clock, log);
        if (hello.Newer)
        {
            log("workspace.hello.newer " + Describe(hello));
            var refusal = Message(Failed);
            refusal["reason"] = "version";
            await link.TryWrite(refusal).ConfigureAwait(false);
            link.Close();
            return null;
        }
        if (!TokenMatches(token, hello.Str("token")))
        {
            log("workspace.hello.refused");
            link.Close();
            return null;
        }
        if (!await link.TryWrite(Message(Welcome)).ConfigureAwait(false))
        {
            link.Close();
            return null;
        }
        log("workspace.hello.ok");
        link.loop = link.Read();
        return link;
    }

    /// <summary>Starts an app inside the workspace and returns its new windows there.</summary>
    public async Task<WorkspaceLaunch> Launch(string appIdOrPath, CancellationToken cancellationToken)
    {
        var request = Message(WorkspaceAgentProtocol.Launch);
        request["target"] = appIdOrPath;
        try
        {
            var reply = await Ask(request, NextId(), LaunchTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (reply.Type != Launched) return WorkspaceLaunch.Failed(reply.Str("reason") ?? "the workspace could not start it");
            return new WorkspaceLaunch(reply.Bool("started") ?? false, ToWindows(reply.Body["windows"]), reply.Str("problem"));
        }
        catch (TimeoutException)
        {
            return WorkspaceLaunch.Failed("the workspace did not answer within 20 s");
        }
        catch (WorkspaceClosedException)
        {
            return WorkspaceLaunch.Failed("the separate workspace closed");
        }
    }

    /// <summary>The windows inside the workspace; none when it does not answer within 5 s or closed.</summary>
    public async Task<IReadOnlyList<WorkspaceWindow>> Windows(CancellationToken cancellationToken)
    {
        try
        {
            var reply = await Ask(Message(WindowsAsk), NextId(), QueryTimeoutMs, cancellationToken).ConfigureAwait(false);
            return reply.Type == WindowsReply ? ToWindows(reply.Body["windows"]) : [];
        }
        catch (Exception error) when (error is TimeoutException or WorkspaceClosedException)
        {
            return [];
        }
    }

    /// <summary>One window's live facts; null when it is gone, or the workspace does not answer within 5 s or closed.</summary>
    public async Task<WorkspaceWindow?> Window(long handle, CancellationToken cancellationToken)
    {
        var request = Message(WindowAsk);
        request["handle"] = handle;
        try
        {
            var reply = await Ask(request, NextId(), QueryTimeoutMs, cancellationToken).ConfigureAwait(false);
            return reply.Type == WindowReply ? ToWindow(reply.Body["window"]) : null;
        }
        catch (Exception error) when (error is TimeoutException or WorkspaceClosedException)
        {
            return null;
        }
    }

    /// <summary>Runs one tool call in the agent's engine; a cancelled call tells the agent to cancel it and throws.</summary>
    public async Task<PcResult> Call(WorkspaceCall call, IWorkspaceAsks asks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(asks);
        var id = NextId();
        var request = Message(WorkspaceAgentProtocol.Call);
        request["thread"] = call.Thread;
        request["tool"] = call.Tool;
        request["args"] = call.Args.DeepClone();
        request["settings"] = ToJson(call.Settings);
        request["vision"] = call.Vision;
        request["grid"] = ToJson(call.Grid);
        request["head"] = call.Head;
        request["model"] = call.Model;
        callAsks[id] = asks;
        threadAsks[call.Thread] = asks;
        try
        {
            var reply = await Ask(request, id, null, cancellationToken).ConfigureAwait(false);
            return reply.Type == Result ? ToResult(reply.Body["result"]) : PcResult.Fail(PcErrorCode.NotSupported, reply.Str("reason") ?? "the workspace could not run the step");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancel = Message(Cancel);
            cancel["call"] = id;
            Observe(TryWrite(cancel));
            throw;
        }
        catch (WorkspaceClosedException)
        {
            return PcResult.Fail(PcErrorCode.WindowGone, "the separate workspace closed while the step ran");
        }
        finally
        {
            callAsks.TryRemove(id, out _);
        }
    }

    /// <summary>The chat's turn ended: the agent's engine for that chat ends its turn too.</summary>
    public void EndTurn(string thread)
    {
        var message = Message(WorkspaceAgentProtocol.EndTurn);
        message["thread"] = thread;
        threadAsks.TryRemove(thread, out _);
        Observe(TryWrite(message));
    }

    public async ValueTask DisposeAsync()
    {
        if (IsOpen) await TryWrite(Message(Bye)).ConfigureAwait(false);
        Close();
        await loop.ConfigureAwait(false);
        writer.Dispose();
        closing.Dispose();
    }

    private long NextId() => Interlocked.Increment(ref nextId);

    // Sends a request and waits for its reply (at most timeoutMs on the clock when given).
    private async Task<WorkspaceMessage> Ask(JsonObject request, long id, int? timeoutMs, CancellationToken cancellationToken)
    {
        request["id"] = id;
        var waiter = new TaskCompletionSource<WorkspaceMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = waiter;
        try
        {
            if (!IsOpen) throw new WorkspaceClosedException();
            if (!await TryWrite(request, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new WorkspaceClosedException();
            }
            if (timeoutMs is not { } limit) return await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = clock.Delay(limit, wait.Token);
            var first = await Task.WhenAny(waiter.Task, timeout).ConfigureAwait(false);
            await wait.CancelAsync().ConfigureAwait(false);
            if (first == waiter.Task) return await waiter.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            log("workspace.request.timeout " + Describe(new WorkspaceMessage(request)));
            throw new TimeoutException();
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    private async Task Read()
    {
        try
        {
            while (await reader.ReadLine(closing.Token).ConfigureAwait(false) is { } line)
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
                if (message.Re is { } re && pending.TryRemove(re, out var waiter))
                {
                    waiter.TrySetResult(message);
                    continue;
                }
                if (message.Type is Approve or WorkspaceAgentProtocol.Confirm)
                {
                    Observe(Answer(message));
                    continue;
                }
                if (message.Type == Bye) break;
                log("workspace.protocol.unexpected " + Describe(message));
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (IsOpen) log("workspace.link.broken " + error.GetType().Name);
        }
        finally
        {
            Close();
        }
    }

    // The agent asks the user something for a step: the chat's asks answer it (the call's own, else the chat's latest).
    private async Task Answer(WorkspaceMessage ask)
    {
        if (ask.Id is not { } id) return;
        var asks = ask.Long("call") is { } call && callAsks.TryGetValue(call, out var forCall) ? forCall
            : ask.Str("thread") is { } thread && threadAsks.TryGetValue(thread, out var forThread) ? forThread : null;
        JsonObject reply;
        try
        {
            if (ask.Type == Approve)
            {
                var app = new PcAppIdentity(ask.Str("exe") ?? "", ask.Str("aumid"));
                var answer = asks is null ? new WorkspaceApproval(PcApproval.Denied, null)
                    : await asks.Approve(app, ask.Str("name") ?? "", ToCapability(ask.Str("capability")), closing.Token).ConfigureAwait(false);
                reply = Reply(Approved, id);
                reply["answer"] = Wire(answer.Answer);
                reply["said"] = answer.Said;
            }
            else
            {
                var answer = asks is null ? new WorkspaceConfirm(false, null)
                    : await asks.Confirm(ask.Str("word") ?? "", ask.Str("element") ?? "", ask.Str("app") ?? "", closing.Token).ConfigureAwait(false);
                reply = Reply(Confirmed, id);
                reply["allowed"] = answer.Allowed;
                reply["note"] = answer.Note;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await TryWrite(reply).ConfigureAwait(false);
    }

    // A write that never throws for a closed link: false when it could not be written.
    private async Task<bool> TryWrite(JsonObject message, CancellationToken cancellationToken = default)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, closing.Token);
            await writer.Write(message, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested && IsOpen) log("workspace.write.failed " + error.GetType().Name);
            return false;
        }
    }

    // Closed is raised before what waits fails, so a caller that sees the workspace close also finds the host past it.
    private void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) == 1) return;
        closing.Cancel();
        stream.Dispose();
        log("workspace.link.closed");
        try
        {
            Closed?.Invoke();
        }
        finally
        {
            foreach (var (id, waiter) in pending)
            {
                if (pending.TryRemove(id, out _)) waiter.TrySetException(new WorkspaceClosedException());
            }
        }
    }

    // A task nobody awaits: its failure is logged, never lost or thrown on the finalizer.
    private void Observe(Task task) => task.ContinueWith(t => log("workspace.task.failed " + t.Exception!.InnerException?.GetType().Name), CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    // The hello read that lost to the timeout: its end is not needed.
    private static void Forget(Task<string?> task) => task.ContinueWith(t => _ = t.Exception, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
