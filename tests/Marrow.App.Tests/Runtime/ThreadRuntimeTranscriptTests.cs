using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.App.Tests.Runtime;

// ThreadRuntime.apiTranscript() (ThreadRuntime.swift:1640-1686), the history an API-key provider is sent with each turn,
// and serviceTier(for:) (:1392-1397), the per-turn fast-mode tier.
public sealed class ThreadRuntimeTranscriptTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static TimelineEntry User(Guid turn, string text, string id) => new(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = text }), id));

    private static TimelineEntry Reply(Guid turn, string text, string id) => new(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text }), id));

    private static TimelineEntry Tool(Guid turn, string id) => new(new TimelineItem(turn, new TimelineItem.Content.Tool(new ToolCall { Title = "ls", ToolCallKind = ToolCall.Kind.Command }), id));

    private static IReadOnlyList<TranscriptMessage> Transcript(ThreadRuntime runtime, IEnumerable<TimelineEntry> entries, Guid? currentTurn = null)
    {
        typeof(ThreadRuntime).GetProperty("Entries")!.SetValue(runtime, new ValueList<TimelineEntry>(entries));
        typeof(ThreadRuntime).GetField("currentTurnID", Hidden)!.SetValue(runtime, currentTurn);
        return (IReadOnlyList<TranscriptMessage>)typeof(ThreadRuntime).GetMethod("ApiTranscript", Hidden)!.Invoke(runtime, [])!;
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-048")] // ThreadRuntime.swift:1644-1670
    public void The_transcript_replays_user_and_assistant_text_joins_an_assistant_run_and_leaves_out_the_current_turn()
    {
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            var (one, two, now) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var transcript = Transcript(h.Runtime,
            [
                User(one, "first question", "u1"),
                Reply(one, "looking", "r1a"),
                Tool(one, "t1"), // :1652-1656 tools are not replayed
                Reply(one, "found it", "r1b"),
                Reply(one, "", "r1c"), // :1651 empty text is skipped
                User(two, "second question", "u2"),
                Reply(two, "answer two", "r2"),
                User(now, "the turn being sent", "u3"), // :1647 the current turn is excluded
            ], currentTurn: now);
            Assert.Equal(
                new[] { (TranscriptMessage.Role.User, "first question"), (TranscriptMessage.Role.Assistant, "looking\n\nfound it"), (TranscriptMessage.Role.User, "second question"), (TranscriptMessage.Role.Assistant, "answer two") },
                transcript.Select(m => (m.TranscriptMessageRole, m.Text)));
        });
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-074")] // ThreadRuntime.swift:1392-1397 serviceTier(for:)
    public void The_service_tier_is_codex_only_its_models_fast_tier_with_fast_mode_and_default_without()
    {
        foreach (var provider in new[] { ProviderKind.Codex, ProviderKind.Claude })
        {
            using var h = new ThreadRuntimePart12Harness(provider);
            h.Main.OnMain(() =>
            {
                Marrow.App.Tests.App.Fixtures.HydraCookbookResolveFixture.SetModels(h.App.Providers, provider,
                    new ModelOption("fast-model", "Fast", fastTier: "priority"), new ModelOption("plain-model", "Plain"));
                string? Tier(string model, bool fast)
                {
                    var thread = h.Thread with { Provider = provider, Model = model, FastMode = fast };
                    return (string?)typeof(ThreadRuntime).GetMethod("ServiceTier", Hidden)!.Invoke(h.Runtime, [thread]);
                }
                if (provider == ProviderKind.Codex)
                {
                    Assert.Equal("priority", Tier("fast-model", fast: true)); // :1396 fastMode ? tier
                    Assert.Equal("default", Tier("fast-model", fast: false)); // :1396 : "default"
                    Assert.Null(Tier("plain-model", fast: true)); // :1395 a model with no fast tier sends none
                }
                else
                    Assert.Null(Tier("fast-model", fast: true)); // :1394 Codex only
            });
        }
    }

    [Fact]
    [Trait("Req", "PR-RUNTIME-049")] // ThreadRuntime.swift:1671-1684
    public void The_transcript_keeps_the_last_40_messages_within_60000_characters_trimming_from_the_oldest_kept()
    {
        using var h = new ThreadRuntimePart12Harness();
        h.Main.OnMain(() =>
        {
            // 45 alternating messages: only the last 40 are kept (:1671).
            var turn = Guid.NewGuid();
            var many = Enumerable.Range(0, 45).Select(i => i % 2 == 0 ? User(turn, "q" + i, "u" + i) : Reply(turn, "a" + i, "r" + i)).ToList();
            var kept = Transcript(h.Runtime, many);
            Assert.Equal(40, kept.Count);
            Assert.Equal("a5", kept[0].Text); // messages 5..44

            // From the newest back: a message that no longer fits is cut to the budget left, after an ellipsis, when
            // more than 200 characters remain (:1676-1678); everything older is dropped once the budget is spent.
            var t = Guid.NewGuid();
            var budget = Transcript(h.Runtime,
            [
                User(t, new string('o', 500), "old"),
                Reply(t, new string('x', 30_000), "mid"),
                User(t, new string('y', 35_000), "new"),
            ]);
            Assert.Equal(2, budget.Count); // the 500-character oldest one is dropped (budget 0 by then)
            Assert.Equal("…" + new string('x', 25_000), budget[0].Text); // 60000 - 35000 left for the older one
            Assert.Equal(35_000, budget[1].Text.Length);

            // With 200 or fewer characters left, the message and everything older are dropped (:1679-1682).
            var tight = Transcript(h.Runtime,
            [
                User(t, "older", "o2"),
                Reply(t, new string('x', 1_000), "m2"),
                User(t, new string('y', 59_850), "n2"),
            ]);
            Assert.Equal(new[] { 59_850 }, tight.Select(m => m.Text.Length));
        });
    }
}
