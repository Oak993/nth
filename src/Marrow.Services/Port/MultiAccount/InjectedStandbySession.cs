// PORT-ONLY: OD-0116, OD-0120, OD-0122, ED-439. A standby that holds the next account's context as injected history, with no model turn (Codex, Pi).
using System.Text.RegularExpressions;
using Marrow.Core.Accounts;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

/// <summary>
/// With no turn the model never runs, so nothing can act and no read-back exists to wait for: "loaded" is the readiness, and it shows the same in-sync tick (OD-0122).
/// Any activity on the waiting thread, a refused inject or an exit turns the standby into a deferred delivery that keeps the package, so no context is lost.
/// </summary>
public sealed partial class InjectedStandbySession : IAdoptableStandby
{
    /// <summary>A full archive beyond this many characters goes as Marrow's summary: it must fit the model's window with the conversation to come.</summary>
    public const int FullArchiveCharacterCap = 400_000;

    private readonly ProviderSession session;
    private readonly IStandbyInjectionSession injector;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Func<bool>? canTransfer;
    private string? injectedBody;
    private string? injectedBriefing;
    private bool ownsSession = true;
    private bool discarded;
    private bool ended;

    private InjectedStandbySession(ProviderSession session, IStandbyInjectionSession injector, SessionConfiguration configuration, Func<bool>? canTransfer)
    {
        this.session = session;
        this.injector = injector;
        this.canTransfer = canTransfer;
        Configuration = configuration;
        session.OnEvent = Observe;
        injector.OnStandbyViolation = Fault;
    }

    public SessionConfiguration Configuration { get; }
    public string SessionId { get; private set; } = "";
    public HandoffPackage? ReceivedPackage { get; private set; }
    public bool ReadBackRequested => false;
    public string? FaultReason { get; private set; }

    public StandbyKind Kind => FaultReason is null ? StandbyKind.Injected : StandbyKind.Deferred;

    public static async Task<InjectedStandbySession> StartAsync(ProviderSession session, SessionConfiguration configuration, CancellationToken ct, Func<bool>? canTransfer = null)
    {
        if (session is not IStandbyInjectionSession injector)
            throw new NotSupportedException("This service cannot load context as history without a model turn.");
        var standby = new InjectedStandbySession(session, injector, configuration, canTransfer);
        Task<string>? start = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            start = injector.StartInjectedStandby(ct);
            standby.SessionId = await start.WaitAsync(ct);
            if (string.IsNullOrWhiteSpace(standby.SessionId)) throw new InvalidOperationException("The standby did not report its session identity.");
            if (standby.FaultReason is not null || !session.IsRunning) throw new InvalidOperationException("The standby exited while starting.");
            return standby;
        }
        catch
        {
            standby.Release();
            if (start is not null) _ = DiscardLateStartAsync(start, session, injector);
            throw;
        }
    }

    private static async Task DiscardLateStartAsync(Task<string> start, ProviderSession session, IStandbyInjectionSession injector)
    {
        try { await start; }
        catch { /* A failed start still releases anything it opened after cancellation. */ }
        finally
        {
            try { await injector.DiscardStandby(); }
            catch { /* The process must stop even if the waiting history cannot be deleted. */ }
            finally { session.Stop(); }
        }
    }

    public async Task<string?> ReceiveAsync(HandoffPackage package, bool readBack, CancellationToken ct)
    {
        var (body, briefing) = Parts(package);
        await gate.WaitAsync(ct);
        try
        {
            if (!ownsSession || discarded) throw new InvalidOperationException("The standby is unavailable.");
            ReceivedPackage = package;
            if (FaultReason is not null) return null;
            if (canTransfer?.Invoke() == false) throw new InvalidOperationException("Context transfer is no longer authorized.");
            var texts = Delta(body, briefing);
            try
            {
                foreach (var text in texts)
                {
                    ct.ThrowIfCancellationRequested();
                    if (canTransfer?.Invoke() == false) throw new InvalidOperationException("Context transfer is no longer authorized.");
                    await injector.InjectStandbyHistory(text, ct);
                    if (FaultReason is not null) return null;
                }
            }
            catch (StandbyUnavailableException error)
            {
                // A refused inject: nothing is held, so the context goes the deferred way.
                Fault("inject: " + error.Message);
                await DiscardAsync();
                return null;
            }

            if (canTransfer?.Invoke() == false) throw new InvalidOperationException("Context transfer is no longer authorized.");
            injectedBody = body;
            injectedBriefing = briefing;
            return null;
        }
        finally { gate.Release(); }
    }

    // The standby is read-only for the model: there is nothing to fill, because there was never a read-back.
    public Task FillGapsAsync(IReadOnlyList<string> gaps, CancellationToken ct) => Task.CompletedTask;

    public async Task PrepareForAdoptionAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!ownsSession || discarded || FaultReason is not null || ReceivedPackage is null || !session.IsRunning)
                throw new InvalidOperationException("Only a loaded standby can be prepared for adoption.");
            if (canTransfer?.Invoke() == false) throw new InvalidOperationException("Context transfer is no longer authorized.");
            try
            {
                await injector.EndStandbyAsync(ct);
                ended = true;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // The handover itself failed (a CLI that opens its own session here): nothing was lost, the context goes the deferred way.
                Fault("handover: " + error.Message);
                await DiscardAsync();
            }
        }
        catch { if (ownsSession) await DiscardAsync(); throw; }
        finally { gate.Release(); }
    }

    public ProviderSession TakeSession()
    {
        if (!ownsSession || discarded || !ended || FaultReason is not null || gate.CurrentCount == 0 || !session.IsRunning)
            throw new InvalidOperationException("Only a loaded standby can be adopted.");
        canTransfer = null;
        ownsSession = false;
        session.OnEvent = null;
        injector.OnStandbyViolation = null;
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        if (!ownsSession) return;
        await gate.WaitAsync();
        try { await DiscardAsync(); }
        finally { gate.Release(); }
    }

    private async Task DiscardAsync()
    {
        if (!ownsSession || discarded) return;
        discarded = true;
        canTransfer = null;
        session.OnEvent = null;
        injector.OnStandbyViolation = null;
        try { await injector.DiscardStandby(); }
        catch { /* A thread that cannot be deleted still dies with its process. */ }
        finally { session.Stop(); }
    }

    // Stops without waiting on the gate: a failed start has nothing to protect.
    private void Release()
    {
        if (!ownsSession || discarded) return;
        discarded = true;
        session.OnEvent = null;
        injector.OnStandbyViolation = null;
        _ = injector.DiscardStandby();
        session.Stop();
    }

    private void Fault(string reason)
    {
        if (!ownsSession || discarded || ended) return;
        FaultReason ??= reason;
    }

    private void Observe(ProviderEvent value)
    {
        if (!ownsSession) return;
        switch (value)
        {
            case ProviderEvent.Exited exit:
                Fault("exited: " + (exit.Error ?? "the standby exited"));
                break;
            case ProviderEvent.TurnStarted or ProviderEvent.MessageDelta or ProviderEvent.MessageCompleted or ProviderEvent.ToolStarted
                or ProviderEvent.ToolOutput or ProviderEvent.ToolUpdated or ProviderEvent.Approval or ProviderEvent.Question
                or ProviderEvent.AgentStarted or ProviderEvent.AgentEvent:
                Fault(value.GetType().Name);
                break;
        }
    }

    private (string Body, string? Briefing) Parts(HandoffPackage package)
    {
        var body = package.Bring switch
        {
            Bring.Full when package.Summary.FilePath is { } path => FullOrSummary(File.ReadAllText(path), package),
            Bring.Full => throw new InvalidOperationException("The full conversation archive is unavailable."),
            Bring.MarrowSummary => package.Summary.Text,
            Bring.AiSummary when !string.IsNullOrWhiteSpace(package.Briefing) => package.Summary.Text,
            Bring.AiSummary => throw new InvalidOperationException("The outgoing account did not produce an AI briefing."),
            _ => throw new ArgumentOutOfRangeException(nameof(package)),
        };
        return (body, string.IsNullOrWhiteSpace(package.Briefing) ? null : package.Briefing);
    }

    private static string FullOrSummary(string archive, HandoffPackage package) => archive.Length > FullArchiveCharacterCap ? package.Summary.Text : archive;

    // The first load carries everything; a later package carries only what is new or changed.
    private List<string> Delta(string body, string? briefing)
    {
        var texts = new List<string>();
        if (injectedBody is null)
        {
            texts.Add("[Marrow Handshake] Recorded context of this conversation so far, loaded as history. It needs no reply.\n" + body
                + (briefing is null ? "" : "\nOutgoing account's briefing:\n" + briefing));
            return texts;
        }

        if (body != injectedBody && Changes(injectedBody, body) is { Length: > 0 } changes) texts.Add(changes);
        if (briefing is not null && briefing != injectedBriefing)
            texts.Add("[Marrow Handshake] Outgoing account's updated briefing. It needs no reply.\n" + briefing);
        return texts;
    }

    // Marrow's own summary has a header (with a new archive path on every build), one block per item, and a trailer, so a rebuilt summary is compared block by block.
    // Any other text is compared whole: an extension is loaded as the extension, anything else replaces the earlier text.
    private static string? Changes(string old, string current)
    {
        if (Split(old) is { } before && Split(current) is { } after)
        {
            var parts = new List<string>();
            if (before.Header != after.Header) parts.Add("Header of the recorded context, updated:\n" + after.Header);
            var known = before.Blocks.GroupBy(block => block.Id).ToDictionary(group => group.Key, group => group.First().Text);
            foreach (var block in after.Blocks)
            {
                if (!known.TryGetValue(block.Id, out var earlier)) parts.Add(block.Text);
                else if (earlier != block.Text) parts.Add("(entry updated)\n" + block.Text);
            }

            if (before.Trailer != after.Trailer) parts.Add(after.Trailer.TrimStart('\n'));
            if (parts.Count == 0) return "";
            return "[Marrow Handshake] Recorded context, newer entries and changes since the last load. It needs no reply."
                + (after.Archive is null ? "" : "\nLatest archive of the full conversation: " + after.Archive) + "\n" + string.Join("\n", parts);
        }

        return current.StartsWith(old, StringComparison.Ordinal)
            ? "[Marrow Handshake] Recorded context, continued. It needs no reply.\n" + current[old.Length..]
            : "[Marrow Handshake] Updated recorded context; it replaces the earlier recorded context. It needs no reply.\n" + current;
    }

    private const string ArchivePrefix = "Full conversation, including all shortened output: ";
    private const string HistoryTitle = "\nConversation and tool history (chronological):\n";

    [GeneratedRegex(@"^\[([^;\]\r\n]+); turn [^\]\r\n]*\]\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BlockStart();

    private sealed record Summary(string Header, string? Archive, List<(string Id, string Text)> Blocks, string Trailer);

    private static Summary? Split(string text)
    {
        var historyAt = text.IndexOf(HistoryTitle, StringComparison.Ordinal);
        var trailerAt = text.LastIndexOf("\nLast error:\n", StringComparison.Ordinal);
        if (historyAt < 0 || trailerAt < historyAt + HistoryTitle.Length) return null;
        string? archive = null;
        var lines = text[..historyAt].Split('\n').ToList();
        var archiveLine = lines.FindIndex(line => line.StartsWith(ArchivePrefix, StringComparison.Ordinal));
        if (archiveLine >= 0)
        {
            archive = lines[archiveLine][ArchivePrefix.Length..].TrimEnd('\r');
            lines.RemoveAt(archiveLine);
        }

        var body = text[(historyAt + HistoryTitle.Length)..trailerAt];
        var starts = BlockStart().Matches(body);
        if (starts.Count == 0 && body.Trim().Length > 0) return null;
        // The archive path also appears inside shortened outputs; it is not a change to the entry.
        string Plain(string value) => (archive is null ? value : value.Replace(archive, "<archive>", StringComparison.Ordinal)).TrimEnd();
        var blocks = new List<(string Id, string Text)>();
        for (var index = 0; index < starts.Count; index++)
        {
            var end = index + 1 < starts.Count ? starts[index + 1].Index : body.Length;
            blocks.Add((starts[index].Groups[1].Value, Plain(body[starts[index].Index..end])));
        }

        return new Summary(Plain(string.Join('\n', lines)), archive, blocks, Plain(text[trailerAt..]));
    }
}
