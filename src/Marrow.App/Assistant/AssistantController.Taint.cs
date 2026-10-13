// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.2, 12.3 point 2, 12.4; unit AS-7; ED-NEW-dextra-as7-7 to -10, -12 to -18). No
// Swift twin.
// The controller's side of taint and auto-answering. Every provider event of a Dextra chat (a mission's agent or the
// home chat) passes AgentTaintTracker: a tool call that reads untrusted content taints that chat before its output
// reaches the model, and instructions found in what it read go to the feed and the mission's card (read off the main
// thread). The home chat's taint lasts until the owner's own next message, and is kept on disk so a restart does not
// clear it; a head of the home chat carries its lead's taint, and hands its own back to its lead. A mission agent's lasts
// until its mission ends. It fails closed: an event nth cannot place taints the chat. Grants an Allow used are recorded
// (a Once grant is spent), and the third parties the owner confirmed a step to are remembered for this session.
using System.Text.Json.Nodes;
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

    /// <summary>The most outputs waiting to be read for instructions; past it a new one is not read (its taint stands).</summary>
    public const int ScansWaitingCap = 32;

    /// <summary>
    /// What a home chat counts as having read when nth cannot tell what it read before it restarted (its record is
    /// damaged, or a newer nth wrote it): untrusted content, until the owner's own message (fail closed).
    /// </summary>
    public static readonly Taint UnknownAfterRestart = Taint.From(ContentOrigin.ToolOutput, "what this chat read before nth restarted (its record could not be read)");

    private readonly AgentTaintTracker tracker = new();
    private readonly Dictionary<Guid, TaintContext> homeTaint = [];
    private readonly List<string> confirmedRecipients = [];
    private readonly object saveGate = new();
    private readonly object scanGate = new();
    private JsonObject? homeTaintKept;
    private bool homeTaintWritable = true;
    private int scansWaiting;
    private bool scanDropReported;

    /// <summary>
    /// ThreadRuntime.Handle's tap (main thread): what one event of a Dextra chat brings in. A chat that is not Dextra's
    /// is not looked at. Never throws: when nth cannot place an event, the chat is tainted (fail closed). What a call
    /// returned is read for instructions off the main thread (<see cref="AssistantControllerOptions.ScanOn"/>).
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
            if (seen.Outputs.Count > 0) ScanLater(thread, record is not null, seen.Outputs);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Taken(thread, record is not null, Taint.From(ContentOrigin.ToolOutput, "a tool call nth could not place"), writesFiles: true);
            Runner.ReportFault("A tool call could not be checked for untrusted content, so the chat is treated as having read some: " + error.Message);
        }
    }

    /// <summary>
    /// The owner sent his own message in a chat (ThreadRuntime's owner sends: the composer, never nth's own sends): that
    /// home chat (or head of it) starts a fresh, untainted turn context (12.4). A mission's agents keep their taint until
    /// the mission ends, whoever types in their chat.
    /// </summary>
    public void OwnerMessage(Guid thread)
    {
        if (Runner.MissionOf(thread) is not null || !IsHome(thread)) return;
        bool removed;
        lock (gate) removed = homeTaint.Remove(thread);
        if (removed) SaveHomeTaint();
    }

    /// <summary>
    /// Tainted material reached a home chat (a tool call, a wake's batch with tainted results, a tainted report; AS-8
    /// calls this for each wake): it is tainted until the owner's own message (12.4). A head of the home chat hands it to
    /// its lead too, whose report its own merges into ("a merge is tainted if any input is").
    /// </summary>
    public void AbsorbHome(Guid thread, Taint taint)
    {
        if (!taint.IsTainted) return;
        var lead = HomeLeadOf(thread);
        bool changed;
        lock (gate)
        {
            changed = MergeHome(thread, taint);
            if (lead is { } to) changed |= MergeHome(to, taint);
        }
        if (changed) SaveHomeTaint();
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

    /// <summary>
    /// IAssistantToolHost: before an nth-dextra call of the chat is decided, the work already waiting on the main thread
    /// runs, so the chat's provider events that came first (a page it read) have tainted it (ED-NEW-dextra-as7-18). It
    /// fails (and the call is refused) when the main thread does not get to it in <see cref="AssistantControllerOptions.SettleWait"/>.
    /// </summary>
    public Task Settle(AssistantChat chat, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chat);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        o.Agents.Post(() => done.TrySetResult());
        return done.Task.IsCompleted ? Task.CompletedTask : done.Task.WaitAsync(o.SettleWait, o.Time, cancellationToken);
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

    // ---- heads of the home chat (12.4 "Taint follows text"; ED-NEW-dextra-as7-13) ----

    /// <summary>A head of the home chat: its lead wrote its brief (with the lead's last reply), so it starts with the lead's taint.</summary>
    private void HomeHeadAdded(ChatThread lead, ChatThread head)
    {
        if (lead.MissionID is not null || head.MissionID is not null || !IsHome(lead.Id) || !IsHome(head.Id)) return;
        var taint = HomeTaint(lead.Id).Taint;
        if (!taint.IsTainted) return;
        bool changed;
        lock (gate) changed = MergeHome(head.Id, taint);
        if (changed) SaveHomeTaint();
    }

    /// <summary>A head of the home chat is done: its report merges into its lead, which takes the head's taint with it.</summary>
    private void HomeHeadFinished(Guid head)
    {
        if (HomeLeadOf(head) is not { } lead) return;
        var taint = HomeTaint(head).Taint;
        if (!taint.IsTainted) return;
        bool changed;
        lock (gate) changed = MergeHome(lead, taint);
        if (changed) SaveHomeTaint();
    }

    /// <summary>The home chat a head belongs to, or null (not a head, a mission's, or nth cannot read the chat).</summary>
    private Guid? HomeLeadOf(Guid thread)
    {
        try
        {
            if (o.Thread(thread) is not { Hydra: not null, MissionID: null, ParentThreadID: { } lead }) return null;
            return IsHome(lead) ? lead : null;
        }
        catch (Exception error) when (error is not OperationCanceledException) { return null; }
    }

    // Under gate. True when the chat's taint grew.
    private bool MergeHome(Guid thread, Taint taint)
    {
        var now = homeTaint.GetValueOrDefault(thread) ?? TaintContext.Clean;
        var next = TaintWiring.Capped(now.Taint.Union(taint));
        if (next == now.Taint) return false;
        homeTaint[thread] = new TaintContext(next);
        return true;
    }

    // ---- the home chats' taint on disk (ED-NEW-dextra-as7-14) ----

    /// <summary>
    /// The home chats' taint as nth last saved it. A record nth cannot read, or one a newer nth wrote (left untouched,
    /// and not written this session), makes every home chat count as having read untrusted content until the owner's own
    /// message.
    /// </summary>
    private void LoadHomeTaint()
    {
        if (o.LoadHomeTaint is null) return;
        JsonObject? body = null;
        IReadOnlyDictionary<Guid, Taint>? chats = null;
        var newer = false;
        try
        {
            body = o.LoadHomeTaint();
            if (body is null) return;
            newer = HomeTaintJournal.IsNewer(body);
            if (!newer) chats = HomeTaintJournal.FromJson(body);
        }
        catch (Exception error) when (error is not OperationCanceledException) { chats = null; }
        if (chats is not null)
        {
            lock (gate)
            {
                foreach (var (thread, taint) in chats) homeTaint[thread] = new TaintContext(TaintWiring.Capped(taint));
                homeTaintKept = body;
            }
            return;
        }
        IReadOnlyList<Guid> homes;
        try { homes = o.HomeChats(); }
        catch (Exception error) when (error is not OperationCanceledException) { homes = []; }
        lock (gate)
        {
            homeTaintWritable = !newer;
            foreach (var thread in homes) homeTaint[thread] = new TaintContext(UnknownAfterRestart);
        }
        Runner.ReportFault(newer
            ? "The home chats' taint was saved by a newer nth; it is left as it is, and each home chat counts as tainted until your next message."
            : "The home chats' taint could not be read; each home chat counts as tainted until your next message.");
        SaveHomeTaint();
    }

    /// <summary>Writes the home chats' taint after each change; when it cannot, the taint still stands in this session.</summary>
    private void SaveHomeTaint()
    {
        if (o.SaveHomeTaint is null) return;
        lock (saveGate)
        {
            JsonObject body;
            lock (gate)
            {
                if (!homeTaintWritable) return;
                body = HomeTaintJournal.ToJson(homeTaint.ToDictionary(p => p.Key, p => p.Value.Taint), homeTaintKept);
            }
            try { o.SaveHomeTaint(body); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Runner.ReportFault("The home chats' taint could not be saved; it stands until nth closes: " + error.Message);
            }
        }
    }

    // ---- reading untrusted output for instructions, off the main thread (ED-NEW-dextra-as7-17) ----

    private void ScanLater(Guid thread, bool mission, IReadOnlyList<UntrustedOutput> outputs)
    {
        lock (scanGate)
        {
            if (scansWaiting >= ScansWaitingCap)
            {
                if (scanDropReported) return;
                scanDropReported = true;
                Runner.ReportFault("Too much untrusted text is waiting to be read for instructions; some is not read. Its taint stands.");
                return;
            }
            scansWaiting++;
        }
        try { o.ScanOn(() => Scan(thread, mission, outputs)); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            lock (scanGate) scansWaiting--;
            Runner.ReportFault("Untrusted text could not be read for instructions; its taint stands: " + error.Message);
        }
    }

    private void Scan(Guid thread, bool mission, IReadOnlyList<UntrustedOutput> outputs)
    {
        try
        {
            var found = new List<InstructionsFound>();
            foreach (var output in outputs)
                if (output.Scan() is { } finding) found.Add(finding);
            if (found.Count > 0) o.Agents.Post(() => { foreach (var item in found) Report(thread, mission, item); });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            o.Agents.Post(() => Runner.ReportFault("Untrusted text could not be read for instructions; its taint stands: " + error.Message));
        }
        finally
        {
            lock (scanGate)
            {
                scansWaiting--;
                if (scansWaiting == 0) scanDropReported = false;
            }
        }
    }

    /// <summary>The default reader of untrusted output: one at a time on the thread pool, never on the main thread.</summary>
    internal static Action<Action> SerialScanner()
    {
        var exclusive = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default).ExclusiveScheduler;
        return work => Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, exclusive);
    }

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
