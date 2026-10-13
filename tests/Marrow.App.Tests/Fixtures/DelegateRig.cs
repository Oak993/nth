using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-22: the controller over fake chats with the delegate responder wired in, a fake model and a fake context. The owner's settings are a plain value
/// the test changes (the default has the delegate off). The main thread's queue is held still, so the model's answer reaches the main thread only when the
/// test says (<see cref="Think"/>). Nothing here calls a provider.
/// </summary>
internal sealed class DelegateRig : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    public DelegateRig(bool autoAnswer = true, bool manualClock = false, DextraPreferences? preferences = null, bool withModel = true, bool withContext = true)
    {
        Preferences = preferences ?? DextraPreferences.Default;
        Clock = manualClock ? new ManualTimeProvider(T0) : null;
        Book = new DelegateAnswerBook(Clock ?? TimeProvider.System);
        Context = new FakeDelegateContext { Chat = id => Core!.Chats.GetValueOrDefault(id) };
        var setup = new DelegateSetup
        {
            Preferences = () => Preferences,
            Book = Book,
            Model = withModel ? Model : null,
            Context = withContext ? Context : null,
            Timeline = id => Timelines.TryGetValue(id, out var items) ? items : [],
            HomeProject = () => HomeProject,
            Brain = provider => new FeedBrain(provider, provider == ProviderKind.Local, Preferences.ReadSameProvider, Preferences.ReadOtherProviders),
            Told = (thread, line, approval) =>
            {
                lock (Told) Told.Add((thread, line, approval));
            },
        };
        Core = new DextraTaintRig(autoAnswer, time: Clock, delegateSetup: setup);
        // The controller writes one line when the injection check settles, from the thread pool and through the main thread's queue: it is let through before the queue is held
        // still, so it cannot be mistaken for the model's answer when a test waits for the queue.
        if (autoAnswer) MissionRig.Until(() => Core.Logged().Any(e => e.Tool == "auto_answer"), "the controller's line about the injection check");
        Core.Agents.Defer = true;
        Core.Controller.Runner.Faulted += fault =>
        {
            lock (Faults) Faults.Add(fault);
        };
    }

    public DextraTaintRig Core { get; }

    public AssistantController Controller => Core.Controller;

    public DelegateResponder Responder => Core.Controller.Responder!;

    public FakeMissionAgents Agents => Core.Agents;

    public DecisionQueue Queue => Core.Controller.Queue;

    public ManualTimeProvider? Clock { get; }

    public DelegateAnswerBook Book { get; }

    public FakeDelegateModel Model { get; } = new();

    public FakeDelegateContext Context { get; }

    /// <summary>The owner's Dextra settings as they are now; a test replaces it.</summary>
    public DextraPreferences Preferences { get; set; }

    public Dictionary<Guid, IReadOnlyList<TimelineItem>?> Timelines { get; } = [];

    public Guid? HomeProject { get; set; }

    public List<(Guid Thread, string Line, bool Approval)> Told { get; } = [];

    /// <summary>The faults the runner was told (a log that could not be written, a model that threw).</summary>
    public List<string> Faults { get; } = [];

    /// <summary>What happened, in one text: for an assertion's message when an answer did not come.</summary>
    public string Why()
    {
        lock (Faults)
            return "notes: " + string.Join(" / ", Core.Agents.Notes.Select(n => n.Text)) + " | log: " + string.Join(" / ", Delegated().Select(d => d.Result))
                + " | cards: " + string.Join(" / ", Queue.Items.Select(c => c.Why)) + " | faults: " + string.Join(" / ", Faults) + " | prompts: " + Model.Asked;
    }

    public MissionProject Project => Core.Project;

    public void Everywhere() => Preferences = Preferences with { Delegate = DelegateScope.Everywhere };

    public void Off() => Preferences = Preferences with { Delegate = DelegateScope.Off, DelegateChats = ValueList<DelegateChatChoice>.Empty };

    /// <summary>One of the owner's own chats, with a live fake chat behind it (it can be sent a message).</summary>
    public ChatThread Owner(string title = "Pitch deck v3", ProviderKind provider = ProviderKind.Claude, Guid? project = null, Guid? parent = null, bool hidden = false)
    {
        var chat = MissionRig.NewChat(() => new ChatThread(project ?? Core.Project.Id, provider, "model-a", null, RuntimeMode.AutoAcceptEdits)
        {
            Title = title,
            ParentThreadID = parent,
            HiddenFromAssistant = hidden,
        });
        Core.Chats[chat.Id] = chat;
        Live(chat);
        return chat;
    }

    /// <summary>A mission started through the runner (its lead's chat is the mission's), with the main thread's queue running while it starts.</summary>
    public MissionRecord Mission(string title = "Fix the login timeout")
    {
        Core.Agents.Defer = false;
        try { return Core.Mission(title); }
        finally { Core.Agents.Defer = true; }
    }

    /// <summary>A live fake chat for an existing chat: idle, so a message to it is taken.</summary>
    public FakeMissionAgents.Chat Live(ChatThread chat) =>
        Core.Agents.Restore(chat.Id, new AgentLaunch(Guid.Empty, "dextra", chat.Title, "", chat.ProjectID, Core.Project.Path, chat.Provider));

    /// <summary>A question of a chat, as Claude asks it: one question, its choices.</summary>
    public bool Ask(Guid thread, string prompt, string[] choices, string id = "r1", bool multiple = false, bool other = false, string header = "") =>
        Controller.OnQuestion(thread, new QuestionRequest(id, new ValueList<QuestionRequest.Question>([
            new QuestionRequest.Question("q1", header, prompt, new ValueList<QuestionRequest.Choice>(choices.Select(c => new QuestionRequest.Choice(c))), multiple, other, false)])));

    public bool Ask(Guid thread, QuestionRequest request) => Controller.OnQuestion(thread, request);

    /// <summary>A command approval of a chat.</summary>
    public bool Approve(Guid thread, string command, string id = "a1") => Controller.OnApproval(thread, MissionRig.Approval(id, command));

    /// <summary>
    /// Lets the main thread's queue run until every question with the model has been judged. The queue also holds the runner's own work (it posts when a card is raised), so
    /// this runs it again and again until the responder has nothing with the model: the model's answer is the last thing to arrive.
    /// </summary>
    public void Think()
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (Responder.Pending > 0)
        {
            Core.Agents.RunPosted();
            if (Responder.Pending == 0) break;
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10), "the model's answer never reached the main thread: " + Why());
            Thread.Yield();
        }
        Core.Agents.RunPosted();
    }

    public List<AuditEntry> Delegated() => [.. Core.Logged().Where(e => e.Tool is "delegate" or "delegate_override")];

    public void Dispose() => Core.Dispose();
}
