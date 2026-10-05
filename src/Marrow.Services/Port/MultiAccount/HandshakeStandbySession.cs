// PORT-ONLY: OD-0114; ED-NEW-ma-ui. A private collector owns priming until real TurnCompleted acknowledgement.
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public sealed class HandshakeStandbySession : IStandbySession
{
    private readonly ProviderSession session;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, string> messages = new(StringComparer.Ordinal);
    private readonly List<string> messageOrder = [];
    private TaskCompletionSource<string?>? completion;
    private bool needsReadBack;
    private bool ready;
    private bool ownsSession = true;
    private bool stopped;

    private HandshakeStandbySession(ProviderSession session, SessionConfiguration configuration)
    {
        this.session = session;
        Configuration = configuration;
        session.OnEvent = Collect;
    }

    public SessionConfiguration Configuration { get; }
    public string SessionId { get; private set; } = "";

    public static async Task<HandshakeStandbySession> StartAsync(ProviderSession session, SessionConfiguration configuration, CancellationToken ct)
    {
        // These providers enforce IsFinalReport before exposing tools or executing an action.
        // A CLI needs its own verified restricted launch before it can be used for priming.
        if (configuration.Provider is not (ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local))
            throw new NotSupportedException("This service cannot yet receive context in a verified text-only session.");
        var standby = new HandshakeStandbySession(session, configuration);
        Task<string>? start = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            start = session.Start();
            standby.SessionId = await start.WaitAsync(ct);
            if (string.IsNullOrWhiteSpace(standby.SessionId)) throw new InvalidOperationException("The standby did not report its session identity.");
            if (standby.stopped || !session.IsRunning) throw new InvalidOperationException("The standby exited while starting.");
            return standby;
        }
        catch
        {
            standby.Stop();
            if (start is { IsCompleted: false }) _ = StopLateStartAsync(start, session);
            throw;
        }
    }

    private static async Task StopLateStartAsync(Task<string> start, ProviderSession session)
    {
        try { await start; }
        catch { /* Failed starts still release any partially connected provider. */ }
        finally { session.OnEvent = null; session.Stop(); }
    }

    public Task<string?> ReceiveAsync(HandoffPackage package, bool readBack, CancellationToken ct)
    {
        var context = package.Bring switch
        {
            Bring.Full when package.Summary.FilePath is { } path => File.ReadAllText(path),
            Bring.Full => throw new InvalidOperationException("The full conversation archive is unavailable."),
            Bring.MarrowSummary => package.Summary.Text,
            Bring.AiSummary when !string.IsNullOrWhiteSpace(package.Briefing) => package.Briefing,
            Bring.AiSummary => throw new InvalidOperationException("The outgoing account did not produce an AI briefing."),
            _ => throw new ArgumentOutOfRangeException(nameof(package)),
        };
        var prompt = "[Marrow Handshake] Read this recorded context without taking any actions. "
            + (readBack ? "Reply with the files, open tasks, and latest user instructions you understood.\n" : "Acknowledge the context and wait for the user's next turn.\n")
            + context;
        return RunTextTurnAsync(prompt, readBack, ct);
    }

    public async Task FillGapsAsync(IReadOnlyList<string> gaps, CancellationToken ct) =>
        _ = await RunTextTurnAsync("[Marrow Handshake] These recorded facts were missing from your read-back. Retain them without taking actions:\n"
            + string.Join("\n", gaps), true, ct);

    public Task<string?> TextTurnAsync(string prompt, CancellationToken ct) => RunTextTurnAsync(prompt, true, ct);

    public ProviderSession TakeSession()
    {
        if (!ownsSession || stopped || !ready || completion is not null || gate.CurrentCount == 0 || !session.IsRunning)
            throw new InvalidOperationException("Only a completed standby can be adopted.");
        ownsSession = false;
        session.OnEvent = null;
        return session;
    }

    public ValueTask DisposeAsync() { if (ownsSession) Stop(); return ValueTask.CompletedTask; }

    private async Task<string?> RunTextTurnAsync(string text, bool readBack, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!ownsSession || stopped) throw new InvalidOperationException("The standby is unavailable.");
            ready = false;
            needsReadBack = readBack;
            messages.Clear();
            messageOrder.Clear();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var result = completion.Task;
            // API/Local enforce IsFinalReport before any tool request. CLI factories must supply a verified restricted launch.
            await session.Send(new TurnInput
            {
                Text = text, Images = ValueList<Attachment>.Empty, Model = Configuration.Model, Effort = Configuration.Effort,
                FastMode = Configuration.FastMode, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
                IsFinalReport = true,
            }).WaitAsync(linked.Token);
            var answer = await result.WaitAsync(linked.Token);
            ready = true;
            return answer;
        }
        catch { if (ownsSession) Stop(); throw; }
        finally { completion = null; gate.Release(); }
    }

    private void Collect(ProviderEvent value)
    {
        if (stopped || !ownsSession) return;
        if (value is ProviderEvent.Exited exit) { Fail(exit.Error ?? "The standby exited."); return; }
        if (completion is null) return;
        switch (value)
        {
            case ProviderEvent.MessageDelta delta:
                Message(delta.Id, delta.Text, append: true);
                break;
            case ProviderEvent.MessageCompleted final:
                if (final.Text.Length > 0) Message(final.Id, final.Text, append: false);
                break;
            case ProviderEvent.PlanCompleted plan:
                Message(plan.Id, plan.Markdown, append: false);
                break;
            case ProviderEvent.TurnCompleted { Status: TurnStatus.Completed }:
                var text = string.Join("\n", messageOrder.Select(id => messages[id]).Where(part => part.Length > 0));
                if (needsReadBack && string.IsNullOrWhiteSpace(text)) Fail("The standby returned no read-back.");
                else completion.TrySetResult(needsReadBack ? text : null);
                break;
            case ProviderEvent.TurnCompleted failed:
                Fail(failed.Error ?? "The standby turn did not complete.");
                break;
            case ProviderEvent.Exited exited:
                Fail(exited.Error ?? "The standby exited.");
                break;
            case ProviderEvent.UsageLimit:
                Fail("The standby account ran out.");
                break;
            case ProviderEvent.ToolStarted or ProviderEvent.ToolOutput or ProviderEvent.ToolUpdated
                or ProviderEvent.Approval or ProviderEvent.Question or ProviderEvent.AgentStarted or ProviderEvent.AgentEvent:
                Fail("The standby attempted an action during text-only context transfer.");
                break;
        }
    }

    private void Message(string id, string text, bool append)
    {
        if (!messages.ContainsKey(id)) messageOrder.Add(id);
        messages[id] = append ? messages.GetValueOrDefault(id, "") + text : text;
    }

    private void Fail(string reason)
    {
        completion?.TrySetException(new InvalidOperationException(reason));
        Stop();
    }

    private void Stop()
    {
        if (stopped || !ownsSession) return;
        stopped = true;
        ready = false;
        completion?.TrySetException(new InvalidOperationException("The standby was closed before context transfer completed."));
        session.OnEvent = null;
        session.Stop();
    }
}
