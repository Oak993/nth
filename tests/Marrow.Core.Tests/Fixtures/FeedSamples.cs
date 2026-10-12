using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>Drafts, facts and chats for the Assistant feed tests (AS-2). Nothing here reads the code under test.</summary>
internal static class FeedSamples
{
    public static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public static FeedChatFacts Facts(string title = "Fix the parser", ProviderKind provider = ProviderKind.Claude,
        bool hidden = false, bool local = false, bool privateAccount = false) =>
        new(title, provider, hidden, local, privateAccount);

    public static FeedDraft Draft(FeedEventKind kind, FeedSeverity severity, double seconds, string lead, Guid? thread = null,
        string? detail = null, FeedChatFacts? chat = null, long cost = 0, string? stream = null) =>
        new(kind, severity, Start.AddSeconds(seconds), lead)
        {
            ThreadId = thread,
            Detail = detail,
            Chat = chat,
            CostDelta = cost,
            Stream = stream,
        };

    public static FeedDraft Tokens(Guid thread, double seconds, long tokens) =>
        Draft(FeedEventKind.TokenSpend, FeedSeverity.Quiet, seconds, "", thread, null, null, tokens);

    public static FeedDraft Progress(Guid thread, double seconds, string note, string stream = "a1") =>
        Draft(FeedEventKind.AgentProgress, FeedSeverity.Quiet, seconds, "helper working", thread, note, null, 0, stream);

    public static FeedDraft Quiet(Guid thread, double seconds, string tag) =>
        Draft(FeedEventKind.ToolStarted, FeedSeverity.Quiet, seconds, tag, thread);

    public static FeedDraft Info(Guid thread, double seconds, string tag) =>
        Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, seconds, tag, thread);

    public static FeedDraft NeedsYou(Guid thread, double seconds, string tag) =>
        Draft(FeedEventKind.Approval, FeedSeverity.NeedsYou, seconds, tag, thread);

    public static ChatThread Chat(Guid? id = null, string title = "Fix the parser", ProviderKind provider = ProviderKind.Claude,
        bool hidden = false, Guid? parent = null, Guid? mission = null, Guid? assistant = null) =>
        new(Guid.NewGuid(), provider, null, null, RuntimeMode.Supervised)
        {
            Id = id ?? Guid.NewGuid(),
            Title = title,
            HiddenFromAssistant = hidden,
            ParentThreadID = parent,
            MissionID = mission,
            AssistantID = assistant,
        };

    public static Func<Guid, ChatThread?> Lookup(params ChatThread[] chats)
    {
        var map = chats.ToDictionary(chat => chat.Id);
        return id => map.GetValueOrDefault(id);
    }
}
