// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1 table; unit AS-2; ED-NEW-dextra-as2-8). No Swift twin.
// The one-line taps at existing points of the app (ThreadRuntime.Handle, AppModel.TurnFinished and ThreadNeedsAttention,
// ThreadRuntime.HydraHeadFinished, MultiAccountRouting). Each tap reads a few facts on the main thread, makes a draft and
// hands it to the bus; the privacy filter and the words of the line are made later, off the main thread. Existing
// behaviour does not change: a tap does nothing while nobody listens, and it never throws into the app.
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Port.Usage;
using Marrow.Services.Providers;

namespace Marrow.App.Assistant;

/// <summary>The app's taps on the feed bus. Main thread only.</summary>
internal sealed class AssistantFeedTaps(AppModel app, AssistantEventBus bus) : IDisposable
{
    /// <summary>The most characters of a reply the tap copies (the shield reads no more than this anyway).</summary>
    private const int ReplyChars = FeedShield.MaxScanChars;

    /// <summary>How many of a chat's last entries are searched for its last reply.</summary>
    private const int ReplySearch = 64;

    private readonly AccountWindowWatcher accounts = new();
    private Guid described;
    private bool disposed;

    /// <summary>The owner's "Private account" mark for a chat's account (5.4). The app has no such mark yet; set by the unit that adds it.</summary>
    public Func<ChatThread, bool>? IsPrivateAccount { get; set; }

    /// <summary>ThreadRuntime.Handle: the provider events that matter to the Assistant. Everything else is ignored.</summary>
    public void TapProvider(Guid threadId, ProviderEvent @event)
    {
        TapReserve(threadId, @event);
        if (disposed || !bus.HasSubscribers) return;
        try
        {
            if (app.Thread(threadId) is not { } thread) return;
            switch (@event)
            {
                case ProviderEvent.TurnStarted:
                    Publish(thread, FeedEventKind.TurnStarted, FeedSeverity.Quiet, "started a turn");
                    break;
                case ProviderEvent.ToolStarted tool:
                    Publish(thread, FeedEventKind.ToolStarted, FeedSeverity.Quiet, ToolLead(tool.Call));
                    break;
                case ProviderEvent.Approval approval:
                    described = threadId;
                    Publish(thread, FeedEventKind.Approval, FeedSeverity.NeedsYou, "waiting for approval", approval.Value.Title);
                    break;
                case ProviderEvent.Question question:
                    described = threadId;
                    Publish(thread, FeedEventKind.Question, FeedSeverity.NeedsYou, "asked a question",
                        question.Value.Questions.FirstOrDefault(item => !item.IsSecret)?.Prompt);
                    break;
                case ProviderEvent.UsageLimit limit:
                    Publish(thread, FeedEventKind.UsageLimit, FeedSeverity.Info, limit.ResetsAt is { } resets
                        ? $"hit a usage limit, resets in {AccountWindowWatcher.Relative(resets - bus.Now)}"
                        : "hit a usage limit");
                    break;
                case ProviderEvent.TokenSpend spend when spend.Value.TotalTokens > 0:
                    Publish(thread, FeedEventKind.TokenSpend, FeedSeverity.Quiet, "", null, spend.Value.TotalTokens);
                    break;
                case ProviderEvent.AgentStarted started:
                    Publish(thread, FeedEventKind.AgentStarted, FeedSeverity.Quiet, "started a helper", started.Value.Description);
                    break;
                case ProviderEvent.AgentProgress progress:
                    Publish(thread, FeedEventKind.AgentProgress, FeedSeverity.Quiet, "helper working", progress.Summary ?? progress.LastTool, 0, progress.AgentID);
                    break;
                case ProviderEvent.AgentFinished finished when finished.Status != TurnStatus.Running:
                    Publish(thread, FeedEventKind.AgentFinished,
                        finished.Status == TurnStatus.Failed ? FeedSeverity.Error : FeedSeverity.Info,
                        finished.Status switch { TurnStatus.Failed => "helper failed", TurnStatus.Interrupted => "helper stopped", _ => "helper finished" },
                        finished.Summary);
                    break;
                case ProviderEvent.TurnCompleted { Error: { } error }:
                    Publish(thread, FeedEventKind.Error, FeedSeverity.Error, "hit an error", error);
                    break;
                case ProviderEvent.Exited { Error: { } error }:
                    Publish(thread, FeedEventKind.Error, FeedSeverity.Error, "stopped unexpectedly", error);
                    break;
                case ProviderEvent.Notice { Value.NoticeLevel: Notice.Level.Error } notice:
                    Publish(thread, FeedEventKind.Error, FeedSeverity.Error, "hit an error", notice.Value.Message);
                    break;
            }
        }
        catch (Exception)
        {
            // a tap must never break the app
        }
    }

    // AS-13: the usage reserve reads the ledger's spend and the turns of mission chats whether or not anyone watches the feed.
    // AS-15: so do the cost guard's caps (what each assistant spent today, what a task spent, and the money on API keys).
    private void TapReserve(Guid threadId, ProviderEvent @event)
    {
        if (disposed) return;
        var reserve = app.Reserve;
        var costs = app.Costs;
        if (reserve is null && costs is null) return;
        try
        {
            switch (@event)
            {
                case ProviderEvent.TokenSpend spend when spend.Value.TotalTokens > 0:
                    reserve?.OnSpend(threadId, spend.Value.Model, spend.Value.TotalTokens);
                    costs?.OnSpend(threadId, spend.Value);
                    break;
                case ProviderEvent.TurnStarted when app.Thread(threadId) is { MissionID: { } mission } thread:
                    reserve?.TurnStarted(mission, threadId, thread.Model);   // the reserve tells the caps (ReserveGuard.Costs)
                    break;
                case ProviderEvent.TurnCompleted:
                    reserve?.TurnEnded(threadId);
                    break;
            }
        }
        catch (Exception)
        {
            // a tap must never break the app
        }
    }

    /// <summary>An account as the reserve names it: the feed's key, the owner's name for it, its provider.</summary>
    public ReserveAccountRef AccountRef(UsageAccountKey account) => new(AccountKey(account), AccountName(account), account.Provider.Id);

    /// <summary>AppModel.TurnFinished: a chat's turn ended. A head reports through <see cref="TapHeadFinished"/> instead.</summary>
    public void TapTurnFinished(Guid threadId, TurnStatus status, bool continues)
    {
        if (disposed || !bus.HasSubscribers) return;
        try
        {
            if (app.Thread(threadId) is not { IsHydraHead: false } thread) return;
            var (lead, severity) = status switch
            {
                TurnStatus.Completed => ("finished", FeedSeverity.Info),
                TurnStatus.Interrupted => ("stopped", FeedSeverity.Info),
                TurnStatus.Failed => ("stopped with an error", FeedSeverity.Error),
                _ => (null, FeedSeverity.Quiet),
            };
            if (lead is null) return;
            if (continues)
            {
                lead += ", more to come";
                if (severity == FeedSeverity.Info) severity = FeedSeverity.Quiet;
            }
            Publish(thread, FeedEventKind.TurnFinished, severity, lead, LastReply(threadId));
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// AppModel.ThreadNeedsAttention. An approval or a question already described the chat's need in this same call
    /// (ThreadRuntime.Handle taps first), so the bus gets one needs-you line, not two.
    /// </summary>
    public void TapNeedsAttention(Guid threadId)
    {
        if (described == threadId)
        {
            described = Guid.Empty;
            return;
        }
        if (disposed || !bus.HasSubscribers) return;
        try
        {
            if (app.Thread(threadId) is not { } thread) return;
            Publish(thread, FeedEventKind.NeedsAttention, FeedSeverity.NeedsYou, "waiting for your decision");
        }
        catch (Exception)
        {
        }
    }

    /// <summary>ThreadRuntime.HydraHeadFinished: a head's report landed. The line belongs to the head's own chat when it has one.</summary>
    public void TapHeadFinished(Guid leadId, Guid headId, HydraHeadInfo info, HydraHeadInfo.Status status, string? summary)
    {
        if (disposed || !bus.HasSubscribers) return;
        try
        {
            if ((app.Thread(headId) ?? app.Thread(leadId)) is not { } thread) return;
            var (verb, severity) = status switch
            {
                HydraHeadInfo.Status.Completed => ("finished", FeedSeverity.Info),
                HydraHeadInfo.Status.Failed => ("failed", FeedSeverity.Error),
                HydraHeadInfo.Status.Stopped => ("was stopped", FeedSeverity.Info),
                _ => (null, FeedSeverity.Quiet),
            };
            if (verb is null) return;
            Publish(thread, FeedEventKind.HeadFinished, severity, $"{info.DisplayName} {verb}", summary ?? info.Task);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>MultiAccountRouting: an account's usage windows were polled.</summary>
    public void TapAccountSnapshot(AccountPlanLimitsSnapshot snapshot)
    {
        if (!disposed) app.Reserve?.OnSnapshot(snapshot); // AS-13: the reserve reads every poll, watched or not
        if (disposed || !bus.HasSubscribers) return;
        try
        {
            if (snapshot.Limits?.Windows is not { Count: > 0 } windows) return;
            var key = AccountKey(snapshot.Account);
            var name = AccountName(snapshot.Account);
            var readings = windows.Select(window => new AccountWindowReading(key, name, window.Id, window.Title, window.Percent, window.ResetsAt));
            foreach (var draft in accounts.Observe(readings, bus.Now)) bus.Publish(draft);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>MultiAccountRouting.NoteLimit: a provider said an account hit its limit.</summary>
    public void TapAccountLimit(ThreadRuntime runtime, UsageAccountKey? observed, DateTimeOffset? resetsAt)
    {
        if (disposed || !bus.HasSubscribers) return;
        try
        {
            var account = observed;
            if (account is null && runtime.Thread is { } thread)
                account = runtime.TryHandshakeAccountId(out var id) ? KeyOf(thread.Provider, id) : UsageAccountKey.Default(thread.Provider);
            if (account is null) return;
            if (accounts.NoteLimit(AccountKey(account), AccountName(account), resetsAt, bus.Now) is { } draft) bus.Publish(draft);
        }
        catch (Exception)
        {
        }
    }

    private void Publish(ChatThread thread, FeedEventKind kind, FeedSeverity severity, string lead, string? detail = null,
        long cost = 0, string? stream = null)
    {
        var facts = FeedChatFacts.From(thread, app.Thread, IsPrivateAccount);
        var draft = new FeedDraft(kind, severity, bus.Now, lead)
        {
            Chat = facts,
            ThreadId = thread.Id,
            MissionId = thread.MissionID,
            AssistantId = thread.AssistantID,
            Detail = facts.Hidden ? null : Cut(detail),
            CostDelta = cost,
            Stream = stream,
        };
        if (facts.Hidden && !draft.SurvivesHiding) return;
        bus.Publish(draft);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        described = Guid.Empty;
        IsPrivateAccount = null;
        accounts.Dispose();
    }

    // What a tool is doing, by its kind. A tool's title and detail can hold a command line, so only an MCP tool's own
    // name (one word) is repeated.
    private static string ToolLead(ToolCall call) => call.ToolCallKind switch
    {
        ToolCall.Kind.Command => "running a command",
        ToolCall.Kind.Read => "reading a file",
        ToolCall.Kind.Edit => "editing files",
        ToolCall.Kind.Search => "searching",
        ToolCall.Kind.Web => "using the web",
        ToolCall.Kind.Mcp when call.Title.Length is > 0 and <= 60 && !call.Title.Any(char.IsWhiteSpace) => "using the tool " + call.Title,
        ToolCall.Kind.Mcp => "using a connector tool",
        ToolCall.Kind.Agent => "starting a helper",
        _ => "using a tool",
    };

    // The first words of the chat's last reply (5.2: the first 300 characters, cut after the shield has run).
    private string? LastReply(Guid threadId)
    {
        if (app.ExistingRuntime(threadId) is not { } runtime) return null;
        var entries = runtime.Entries;
        for (var index = entries.Count - 1; index >= 0 && index >= entries.Count - ReplySearch; index--)
            if (entries[index].Item.TimelineItemContent is TimelineItem.Content.Assistant reply && reply.Value.Text.Length > 0)
                return reply.Value.Text;
        return null;
    }

    private static string? Cut(string? text) => text is { Length: > ReplyChars } ? text[..ReplyChars] : text;

    private static string AccountKey(UsageAccountKey account) => account.Provider.Id + ":" + account.AccountId;

    // The key the usage scheduler files an account's snapshots under (HandshakeLimits.Key): the migrated first account
    // keeps the provider's default key, every other account its own id.
    private UsageAccountKey KeyOf(ProviderKind provider, Guid id) =>
        app.Settings.Accounts.Accounts.FirstOrDefault(row => row.Id == id) is { UsesLegacyCredentials: true }
            ? UsageAccountKey.Default(provider)
            : new UsageAccountKey(provider, id.ToString("D"));

    // The account's own name when the service has two accounts or more (AccountStore.DisplayName), else the provider's.
    private string AccountName(UsageAccountKey account)
    {
        var row = account.IsDefault
            ? app.Settings.Accounts.ForService(account.Provider).FirstOrDefault(item => item.UsesLegacyCredentials)
            : Guid.TryParse(account.AccountId, out var id) ? app.Settings.Accounts.Accounts.FirstOrDefault(item => item.Id == id) : null;
        return row is not null && app.Settings.Accounts.DisplayName(row.Id) is { Length: > 0 } name ? name : account.Provider.DisplayName;
    }
}
