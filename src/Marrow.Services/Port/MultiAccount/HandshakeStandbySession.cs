// PORT-ONLY: OD-0114; ED-403. A private collector owns priming until real TurnCompleted acknowledgement.
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public sealed class HandshakeStandbySession : IAdoptableStandby
{
    private readonly ProviderSession session;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, string> messages = new(StringComparer.Ordinal);
    private readonly List<string> messageOrder = [];
    private TaskCompletionSource<string?>? completion;
    private Func<Task<HandshakeCostObservation?>>? observeUsage;
    private Func<bool>? canTransfer;
    private bool needsReadBack;
    private Action<string>? textProgress;
    private Action<TokenSpend>? spendObserved;
    private bool ready;
    private bool ownsSession = true;
    private bool stopped;
    private ValueList<ModelOption> reportedModels = ValueList<ModelOption>.Empty;

    private HandshakeStandbySession(ProviderSession session, SessionConfiguration configuration)
    {
        this.session = session;
        Configuration = configuration;
        session.OnEvent = Collect;
    }

    public SessionConfiguration Configuration { get; }
    public string SessionId { get; private set; } = "";
    public HandoffPackage? ReceivedPackage { get; private set; }
    public bool ReadBackRequested { get; private set; }

    public static async Task<HandshakeStandbySession> StartAsync(ProviderSession session, SessionConfiguration configuration, CancellationToken ct, Func<bool>? canTransfer = null,
        Func<Task<HandshakeCostObservation?>>? observeUsage = null, Action<TokenSpend>? spendObserved = null)
    {
        // These providers enforce IsFinalReport before exposing tools or executing an action.
        // Claude additionally verifies its temporary agent, effective policy and actual init identity.
        if (configuration.Provider is not (ProviderKind.Deepseek or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local)
            && !(configuration.Provider == ProviderKind.Claude && session is ClaudeSession)
            && !(configuration.Provider == ProviderKind.Pi && session is PiSession))
            throw new NotSupportedException("This service cannot yet receive context in a verified text-only session.");
        var standby = new HandshakeStandbySession(session, configuration) { observeUsage = observeUsage, canTransfer = canTransfer, spendObserved = spendObserved };
        standby.BindTransferGuard(canTransfer);
        Task<string>? start = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            start = session is ClaudeSession claude ? claude.StartHandshakeAsync(ct)
                : session is PiSession pi ? pi.StartTextOnlyHeadAsync(ct) : session.Start();
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
            Bring.AiSummary when !string.IsNullOrWhiteSpace(package.Briefing) => package.Briefing + "\nRecorded context:\n" + package.Summary.Text,
            Bring.AiSummary => throw new InvalidOperationException("The outgoing account did not produce an AI briefing."),
            _ => throw new ArgumentOutOfRangeException(nameof(package)),
        };
        if (package.Bring != Bring.AiSummary && !string.IsNullOrWhiteSpace(package.Briefing))
            context += "\nOutgoing account's briefing:\n" + package.Briefing;
        var prompt = "[nth Handshake] Read this recorded context without taking any actions. "
            + (readBack ? "Reply with the files, open tasks, and latest user instructions you understood.\n" : "Acknowledge the context and wait for the user's next turn.\n")
            + context;
        return ReceivePackageAsync(package, prompt, readBack, ct);
    }

    private async Task<string?> ReceivePackageAsync(HandoffPackage package, string prompt, bool readBack, CancellationToken ct)
    {
        var answer = await RunTextTurnAsync(prompt, readBack, ct);
        ReceivedPackage = package;
        ReadBackRequested = readBack;
        return answer;
    }

    public async Task FillGapsAsync(IReadOnlyList<string> gaps, CancellationToken ct) =>
        _ = await RunTextTurnAsync("[nth Handshake] These recorded facts were missing from your read-back. Retain them without taking actions:\n"
            + string.Join("\n", gaps), true, ct);

    public Task<string?> TextTurnAsync(string prompt, CancellationToken ct, Action<string>? progress = null) => RunTextTurnAsync(prompt, true, ct, progress: progress);

    public async Task<string?> WriteIncomingLightBriefingAsync(HandoffSummary summary, CancellationToken ct)
    {
        // Haiku is the documented light Claude family. The running private account must actually report the alias.
        if (session is not ClaudeSession || !reportedModels.Any(option => option.Id == "haiku"))
            throw new NotSupportedException("This account did not report a supported light model for its AI briefing.");
        if (summary.FilePath is not { } path) throw new InvalidOperationException("The full conversation archive is unavailable.");
        var archive = await File.ReadAllTextAsync(path, ct);
        return await RunTextTurnAsync("[nth Handshake] Write a compact briefing for your successor: the goal, why decisions were made, what is next, and pitfalls. Take no actions. Use this complete recorded conversation:\n" + archive, true, ct, model: "haiku");
    }

    public async Task PrepareForAdoptionAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!ownsSession || stopped || !ready || completion is not null || !session.IsRunning)
                throw new InvalidOperationException("Only a completed standby can be prepared for adoption.");
            if (session is ClaudeSession claude) await claude.RestoreHandshakeToolsAsync(ct);
            if (session is PiSession) throw new NotSupportedException("A private Pi text head cannot become a working session.");
        }
        catch { if (ownsSession) Stop(); throw; }
        finally { gate.Release(); }
    }

    public ProviderSession TakeSession()
    {
        if (!ownsSession || stopped || !ready || completion is not null || gate.CurrentCount == 0 || !session.IsRunning)
            throw new InvalidOperationException("Only a completed standby can be adopted.");
        if (session is ClaudeSession { HandshakeToolsRestored: false })
            throw new InvalidOperationException("The CLI tools must be restored before adoption.");
        if (session is PiSession) throw new NotSupportedException("A private Pi text head cannot become a working session.");
        BindTransferGuard(null);
        canTransfer = null;
        ownsSession = false;
        session.OnEvent = null;
        return session;
    }

    public ValueTask DisposeAsync() { if (ownsSession) Stop(); return ValueTask.CompletedTask; }

    private async Task<string?> RunTextTurnAsync(string text, bool readBack, CancellationToken ct, string? model = null, Action<string>? progress = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!ownsSession || stopped || session is ClaudeSession { HandshakeToolsRestored: true })
                throw new InvalidOperationException("The standby is unavailable for text-only transfer.");
            ready = false;
            needsReadBack = readBack;
            textProgress = progress;
            messages.Clear();
            messageOrder.Clear();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var result = completion.Task;
            // API/Local enforce IsFinalReport before any tool request. CLI factories must supply a verified restricted launch.
            if (canTransfer?.Invoke() == false) throw new InvalidOperationException("Context transfer is no longer authorized.");
            using var usage = observeUsage is null ? null : await observeUsage();
            if (canTransfer?.Invoke() == false) throw new InvalidOperationException("Context transfer is no longer authorized.");
            await session.Send(new TurnInput
            {
                Text = text, Images = ValueList<Attachment>.Empty, Model = model ?? Configuration.Model, Effort = model is null ? Configuration.Effort : null,
                FastMode = Configuration.FastMode, RuntimeMode = RuntimeMode.Supervised, InteractionMode = InteractionMode.Build,
                IsFinalReport = true,
            }).WaitAsync(linked.Token);
            var answer = await result.WaitAsync(linked.Token);
            if (session is ClaudeSession claude) SessionId = claude.VerifiedHandshakeIdentity;
            ready = true;
            usage?.Complete();
            return answer;
        }
        catch { if (ownsSession) Stop(); throw; }
        finally { completion = null; textProgress = null; gate.Release(); }
    }

    private void Collect(ProviderEvent value)
    {
        if (stopped || !ownsSession) return;
        if (value is ProviderEvent.TokenSpend spend) { spendObserved?.Invoke(spend.Value); return; }
        if (value is ProviderEvent.Models catalog) { reportedModels = catalog.Value; return; }
        if (value is ProviderEvent.Exited exit) { Fail(exit.Error ?? "The standby exited."); return; }
        // A restricted head must also refuse an action during startup or between completed turns.
        if (value is ProviderEvent.ToolStarted or ProviderEvent.ToolOutput or ProviderEvent.ToolUpdated
            or ProviderEvent.Approval or ProviderEvent.Question or ProviderEvent.AgentStarted or ProviderEvent.AgentEvent
            or ProviderEvent.AgentProgress or ProviderEvent.AgentFinished)
        { Fail("The standby attempted an action during text-only context transfer."); return; }
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
        try { textProgress?.Invoke(string.Join("\n", messageOrder.Select(key => messages[key]).Where(part => part.Length > 0))); }
        catch (Exception error) { Fail(error.Message); }
    }

    private void Fail(string reason)
    {
        completion?.TrySetException(new InvalidOperationException(reason));
        Stop();
    }

    private void BindTransferGuard(Func<bool>? guard)
    {
        if (session is ClaudeSession claude) claude.CanTransferContext = guard;
        if (session is OpenAICompatibleSession api) api.CanTransferContext = guard;
    }

    private void Stop()
    {
        if (stopped || !ownsSession) return;
        stopped = true;
        ready = false;
        BindTransferGuard(null);
        canTransfer = null;
        completion?.TrySetException(new InvalidOperationException("The standby was closed before context transfer completed."));
        session.OnEvent = null;
        session.Stop();
    }
}
