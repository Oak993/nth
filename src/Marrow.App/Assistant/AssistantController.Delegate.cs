// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "The delegate responder", 12.4 "What a tainted context can never do", 16 row AS-22; unit AS-22;
// ED-NEW-dextra-as22-1 to -12). No Swift twin.
// The controller's side of the delegate: where it answers (the owner's settings, the chat's menu, hidden chats, Dextra's own chats), what the engine says
// about an approval of one of the owner's chats (the classifier and rules a mission's approvals meet, in the chat's own folder), and the taint of the chat
// the delegate would answer in: a mission chat's is the runner's; any other chat's is read from its timeline since the owner last wrote in it
// (ChatTaintHistory), and a chat nth cannot read counts as having read something untrusted.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>What the delegate is made of: the owner's settings, its book of answers, the model and context it uses, and what it tells the app.</summary>
public sealed class DelegateSetup
{
    /// <summary>The owner's Dextra settings, read at every check (a switch applies at once).</summary>
    public required Func<DextraPreferences> Preferences { get; init; }

    /// <summary>What it answered for the owner and what he corrected.</summary>
    public required DelegateAnswerBook Book { get; init; }

    /// <summary>The model that reads a question. Null: no model is bound, so every question goes to the owner.</summary>
    public IDelegateModel? Model { get; init; }

    /// <summary>The project's decisions, past answers and the chat's turns. Null: the model is given the question alone.</summary>
    public IDelegateContext? Context { get; init; }

    /// <summary>A chat's timeline, oldest first, for its taint; null when the chat cannot be read (it then counts as having read something untrusted).</summary>
    public Func<Guid, IReadOnlyList<TimelineItem>?> Timeline { get; init; } = _ => null;

    /// <summary>The project "This project only" means until the owner names one: the project Dextra's home thread works in.</summary>
    public Func<Guid?> HomeProject { get; init; } = () => null;

    /// <summary>The privacy rules of 5.4 for a delegate on a provider (the owner's two reading switches).</summary>
    public Func<ProviderKind?, FeedBrain> Brain { get; init; } = provider => new FeedBrain(provider, provider == ProviderKind.Local);

    /// <summary>A line for the feed: the chat, the words, and whether it was an approval.</summary>
    public Action<Guid, string, bool>? Told { get; init; }

    public TimeSpan ModelTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>Whatever else lives and dies with the delegate (the book's writer): the controller disposes it.</summary>
    public IDisposable? Owned { get; init; }
}

public sealed partial class AssistantController
{
    /// <summary>The delegate responder (AS-22, 4.3), when the build has one: off until the owner turns it on.</summary>
    public DelegateResponder? Responder { get; }

    /// <summary>The longest a chain of heads is followed up to the chat the owner talks in.</summary>
    private const int DelegateAncestors = 4;

    private DelegateResponder? BuildDelegate()
    {
        if (o.Delegate is not { } setup) return null;
        var responder = new DelegateResponder(new DelegateResponderOptions
        {
            Agents = o.Agents,
            Queue = Queue,
            Book = setup.Book,
            Engage = DelegateEngage,
            AutoAnswering = () => AutoAnswering,
            MayAnswer = thread => PolicyEngine.DelegateAnswer(DelegatePolicyContext(thread)),
            DecideApproval = DelegateApproval,
            Audit = o.Audit,
            AssistantId = () => Root?.Id ?? o.Profiles().FirstOrDefault()?.Id ?? "",
            Brain = setup.Brain,
            LeaveToRunner = (thread, request) => Runner.OnQuestion(thread, request),
            MissionState = thread => Runner.MissionOf(thread)?.Mission.State,
            GrantsUsed = UseGrants,
            Told = setup.Told,
            Deliver = Deliver,
            ChatExists = thread => ChatKnown(thread),
            Model = setup.Model,
            Context = setup.Context,
            Time = o.Time,
            ModelTimeout = setup.ModelTimeout,
            Faulted = text => Runner.ReportFault(text),
        });
        return responder;
    }

    private bool ChatKnown(Guid thread)
    {
        try { return o.Thread(thread) is not null; }
        catch (Exception error) when (error is not OperationCanceledException) { return false; }
    }

    /// <summary>
    /// Where the delegate answers for <paramref name="thread"/>: null for Dextra's own chats (its home thread, an Astur's), for a chat the owner hid from it, and
    /// where the owner's settings have it off (the default). A head is the chat it belongs to for the chat's menu and the project. Main thread.
    /// </summary>
    private DelegateEngagement? DelegateEngage(Guid thread)
    {
        if (o.Delegate is not { } setup) return null;
        // Off everywhere and no chat has chosen: the common case costs one read of the settings and nothing else.
        var preferences = setup.Preferences();
        if (preferences.Delegate == DelegateScope.Off && preferences.DelegateChats.Count == 0) return null;
        if (o.Thread(thread) is not { } chat) return null;
        // Dextra's own chats are not the owner's, and a mark nth cannot read counts as the Assistant's (fail closed).
        if (chat.AssistantID is not null && chat.MissionID is null || chat.MissionID == Guid.Empty || chat.AssistantID == Guid.Empty) return null;
        var facts = FeedChatFacts.From(chat, o.Thread);
        if (facts.Hidden) return null;
        var root = DelegateRoot(chat);
        if (!DelegateReach.IsOn(preferences, root.Id, root.ProjectID, setup.HomeProject())) return null;
        var who = DelegateReach.Model(preferences, root.Id, root.ProjectID);
        var name = who is null ? Root?.Name ?? AssistantNames.Assistant : who.Provider.DisplayName;
        return new DelegateEngagement(chat, chat.Title.Length > 0 ? chat.Title : "A chat", name, who, chat.MissionID is not null, facts);
    }

    // The chat the owner talks in: a head's lead, up to a few levels (a loop in the data cannot hang it).
    private ChatThread DelegateRoot(ChatThread chat)
    {
        var current = chat;
        for (var depth = 0; depth < DelegateAncestors; depth++)
        {
            if (current.ParentThreadID is not { } parent || o.Thread(parent) is not { } lead) break;
            current = lead;
        }
        return current;
    }

    /// <summary>The engine's context for a delegate answering in a chat: Dextra's rules and grants, and the taint of what the chat has read.</summary>
    private PolicyContext DelegatePolicyContext(Guid thread)
    {
        var assistant = Root?.Id ?? o.Profiles().First().Id;
        if (Runner.MissionOf(thread) is { } mission)
            return MissionContext(assistant, mission.Id, Runner.TaintOf(mission.Id, thread)) with { Role = MissionRunner.RoleOf(mission, thread) };
        return MissionContext(assistant, null, DelegateTaint(thread));
    }

    /// <summary>
    /// What a chat of the owner's has read since he last wrote in it, and what its leads have (a head carries its lead's, 12.4). A chat nth cannot read
    /// counts as having read something untrusted: a delegate answers nothing in it.
    /// </summary>
    internal TaintContext DelegateTaint(Guid thread)
    {
        try
        {
            if (o.Delegate is not { } setup) return TaintContext.Clean;
            var taint = Taint.Clean;
            ChatThread? current = o.Thread(thread);
            for (var depth = 0; current is not null && depth <= DelegateAncestors; depth++)
            {
                var items = setup.Timeline(current.Id);
                taint = items is null
                    ? taint.Union(Taint.From(ContentOrigin.ToolOutput, "a chat nth could not read"))
                    : taint.Union(ChatTaintHistory.Since(items, DelegateClassifier(current), current.Provider));
                current = current.ParentThreadID is { } parent ? o.Thread(parent) : null;
            }
            return new TaintContext(TaintWiring.Capped(taint));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new TaintContext(Taint.From(ContentOrigin.ToolOutput, "a chat nth could not read: " + error.GetType().Name));
        }
    }

    // The classifier's view of an owner's chat: its own folder, nth's folders protected, and the probe that tells whether a file there would run in place
    // of a bare command (cmd runs a git.cmd from the working folder before git on PATH, and the chat's agent can write that folder).
    // A chat's history can hold hundreds of commands, so what the probe finds (it reads the folder) is asked once for each name: the main thread does not read a folder a
    // hundred times over.
    private ClassifierContext DelegateClassifier(ChatThread? chat)
    {
        var asked = new Dictionary<(string Folder, string Name), bool>();
        bool Shadowed(string folder, string name)
        {
            if (!asked.TryGetValue((folder, name), out var shadowed)) asked[(folder, name)] = shadowed = MissionClassifier.Shadowed(folder, name);
            return shadowed;
        }
        return HomeClassifier(chat) with { CommandShadowed = Shadowed };
    }

    // The engine's decision on an approval of one of the owner's chats: the same classifier, rules, grants and privacy check a mission's meet, in the
    // chat's own folder, with the chat's own taint.
    private ApprovalVerdict DelegateApproval(Guid thread, ApprovalRequest request)
    {
        var chat = o.Thread(thread);
        var context = DelegateClassifier(chat);
        var provider = chat?.Provider ?? o.Provider;
        var policy = DelegatePolicyContext(thread);
        var decided = MissionApprovals.Decide(request, context, policy, provider, o.PersonalDataRecipients(), confirmedRecipients: ConfirmedRecipients());
        var outgoing = MissionApprovals.Outgoing(request, provider);
        var recipients = decided.Outward.Recipients;
        return new ApprovalVerdict(decided.Classification, decided.Decision)
        {
            Outward = decided.Outward,
            StillBlocked = decided.Outward.HasPersonalData ? () => OutwardPrivacy.Check(outgoing, o.PersonalDataRecipients()).Refusal : null,
            OnAllowed = recipients.Count > 0 ? () => Confirm(recipients) : null,
        };
    }
}
