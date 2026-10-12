// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.2, 12.3 point 2, 12.4; unit AS-7; ED-NEW-dextra-as7-7 to -10). No Swift twin.
// The controller's side of taint and auto-answering. Every provider event of a Dextra chat (a mission's agent or the
// home chat) passes AgentTaintTracker: a tool call that reads untrusted content taints that chat before its output
// reaches the model, and instructions found in what it read go to the feed and the mission's card. The home chat's
// taint lasts until the owner's own next message; a mission agent's until its mission ends. It fails closed: an event
// nth cannot place taints the chat. Grants an Allow used are recorded (a Once grant is spent), and the third parties the
// owner confirmed a step to are remembered for this session.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;

namespace Marrow.App.Assistant;

public sealed partial class AssistantController
{
    /// <summary>The most recipients the session remembers as confirmed; past it the oldest are forgotten (and asked again).</summary>
    public const int ConfirmedCap = 500;

    /// <summary>The most audit lines one grant keeps in UsedBy; past it the oldest go (a Once grant needs only the first).</summary>
    public const int UsedByCap = 100;

    private readonly AgentTaintTracker tracker = new();
    private readonly Dictionary<Guid, TaintContext> homeTaint = [];
    private readonly List<string> confirmedRecipients = [];

    /// <summary>
    /// ThreadRuntime.Handle's tap (main thread): what one event of a Dextra chat brings in. A chat that is not Dextra's
    /// is not looked at. Never throws: when nth cannot place an event, the chat is tainted (fail closed).
    /// </summary>
    public void OnProviderEvent(Guid thread, ProviderEvent @event)
    {
        if (@event is not (ProviderEvent.ToolStarted or ProviderEvent.ToolUpdated or ProviderEvent.ToolOutput or ProviderEvent.AgentEvent
            or ProviderEvent.TurnCompleted or ProviderEvent.Exited)) return;
        var record = Runner.MissionOf(thread);
        if (record is null && !IsHome(thread)) return;
        try
        {
            var chat = o.Thread(thread);
            var provider = chat?.Provider ?? record?.Provider ?? o.Provider;
            var seen = tracker.Observe(thread, @event, () => record is not null ? MissionClassifierOf(record, chat) : HomeClassifier(chat), provider);
            foreach (var step in seen.Steps) Taken(thread, record is not null, step.Taint, step.WritesFiles);
            foreach (var found in seen.Found) Report(thread, record is not null, found);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Taken(thread, record is not null, Taint.From(ContentOrigin.ToolOutput, "a tool call nth could not place"), writesFiles: true);
            Runner.ReportFault("A tool call could not be checked for untrusted content, so the chat is treated as having read some: " + error.Message);
        }
    }

    /// <summary>
    /// The owner sent his own message in a chat (ThreadRuntime's send taps): Dextra's home chat starts a fresh, untainted
    /// turn context (12.4). A mission's agents keep their taint until the mission ends, whoever types in their chat.
    /// </summary>
    public void OwnerMessage(Guid thread)
    {
        if (Runner.MissionOf(thread) is not null || !IsHome(thread)) return;
        lock (gate) homeTaint.Remove(thread);
    }

    /// <summary>
    /// Tainted material reached the home chat (a wake's batch with tainted results, a tainted report; AS-8 calls this
    /// for each wake): the brain is tainted for the rest of the wake, until the owner's own message (12.4).
    /// </summary>
    public void AbsorbHome(Guid thread, Taint taint)
    {
        if (!taint.IsTainted) return;
        lock (gate)
        {
            var now = homeTaint.GetValueOrDefault(thread) ?? TaintContext.Clean;
            homeTaint[thread] = new TaintContext(TaintWiring.Capped(now.Taint.Union(taint)));
        }
    }

    /// <summary>What the home chat carries now.</summary>
    public TaintContext HomeTaint(Guid thread)
    {
        lock (gate) return homeTaint.GetValueOrDefault(thread) ?? TaintContext.Clean;
    }

    /// <summary>A tool's result carries untrusted material into the chat (IAssistantToolHost, 12.4).</summary>
    public void Absorb(AssistantChat chat, Taint taint)
    {
        ArgumentNullException.ThrowIfNull(chat);
        if (!taint.IsTainted) return;
        if (chat.MissionId is not null && Runner.MissionOf(chat.Thread) is not null) Runner.AbsorbAgent(chat.Thread, taint, writesFiles: false);
        else if (chat.MissionId is { } mission) Runner.Absorb(mission, taint);
        else AbsorbHome(chat.Thread, taint);
    }

    /// <summary>The third parties the owner confirmed a step to in this session (12.4 "the first message ... is always confirmed").</summary>
    public IReadOnlyList<string> ConfirmedRecipients()
    {
        lock (gate) return [.. confirmedRecipients];
    }

    private void Confirm(IEnumerable<string> recipients)
    {
        lock (gate)
        {
            foreach (var recipient in recipients)
                if (!confirmedRecipients.Contains(recipient, StringComparer.Ordinal)) confirmedRecipients.Add(recipient);
            if (confirmedRecipients.Count > ConfirmedCap) confirmedRecipients.RemoveRange(0, confirmedRecipients.Count - ConfirmedCap);
        }
    }

    /// <summary>An Allow used these grants (12.2): each records the line that used it, so a Once grant is spent.</summary>
    private void UseGrants(IReadOnlyList<Guid> used, string by)
    {
        lock (gate)
        {
            for (var index = 0; index < grants.Count; index++)
            {
                if (!used.Contains(grants[index].Id)) continue;
                var list = grants[index].UsedBy.Append(by).TakeLast(UsedByCap);
                grants[index] = grants[index] with { UsedBy = new ValueList<string>(list) };
            }
        }
    }

    private bool IsHome(Guid thread) => Identify(thread) is { Role: AssistantChatRole.Home };

    // The home chat reads clean inside its own folder (its worktree or project) and nowhere else.
    private ClassifierContext HomeClassifier(ChatThread? chat)
    {
        var folder = chat is null ? null : chat.WorktreePath ?? o.Projects().FirstOrDefault(p => p.Id == chat.ProjectID)?.Path;
        return new ClassifierContext
        {
            WorkingDirectory = folder,
            AccessFolders = folder is null ? ValueList<string>.Empty : new ValueList<string>([folder]),
            ProtectedRoots = new ValueList<string>(o.ProtectedRoots.Where(r => !string.IsNullOrWhiteSpace(r))),
            Environment = o.Environment ?? MissionClassifier.CurrentEnvironment(),
            Allowlist = new ValueList<string>(o.Allowlist()),
        };
    }

    private void Taken(Guid thread, bool mission, Taint taint, bool writesFiles)
    {
        if (mission) Runner.AbsorbAgent(thread, taint, writesFiles);
        else AbsorbHome(thread, taint);
    }

    // 12.4: instructions found are never followed; they show in the feed and on the mission's card.
    private void Report(Guid thread, bool mission, InstructionsFound found)
    {
        if (mission) Runner.Found(thread, found);
        try { o.InstructionFound?.Invoke(thread, found.Source, found.Finding); }
        catch (Exception error) when (error is not OperationCanceledException) { Runner.ReportFault("The feed could not show found instructions: " + error.Message); }
    }

    /// <summary>When the switch settles, the activity log says whether nth now answers for the owner, and why not.</summary>
    private void WatchAutoAnswer()
    {
        var auto = o.AutoAnswer;
        if (ReferenceEquals(auto, AutoAnswerSwitch.Off)) return;
        _ = auto.Ready.ContinueWith(_ => o.Agents.Post(() => TryAudit(new AuditEntry
        {
            Assistant = o.Profiles().FirstOrDefault()?.Id ?? AssistantNames.Assistant,
            Tool = "auto_answer",
            ActionClass = ActionClass.Agents.ToString(),
            Result = auto.Status + (auto.Report is { Passed: false } failed && failed.Failures.Count > 0 ? " First: " + failed.Failures[0] : ""),
        })), TaskScheduler.Default);
    }
}
