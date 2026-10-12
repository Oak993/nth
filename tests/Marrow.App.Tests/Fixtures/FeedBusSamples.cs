using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Drafts for the bus tests (AS-2). Nothing here reads the code under test.</summary>
internal static class FeedBusSamples
{
    public static readonly DateTimeOffset Start = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public static FeedChatFacts Facts(string title = "Fix the parser", ProviderKind provider = ProviderKind.Claude, bool hidden = false) =>
        new(title, provider, hidden, false, false);

    public static FeedDraft Draft(FeedEventKind kind, FeedSeverity severity, DateTimeOffset at, string lead, Guid thread,
        string? detail = null, FeedChatFacts? chat = null, long cost = 0, string? stream = null) =>
        new(kind, severity, at, lead)
        {
            ThreadId = thread,
            Detail = detail,
            Chat = chat,
            CostDelta = cost,
            Stream = stream,
        };
}
