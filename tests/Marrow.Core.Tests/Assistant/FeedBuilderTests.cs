using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Assistant;

// AS-2 (DESIGN.md 5.1, 5.4): the one-line summary is built without a model, under the privacy rules. A hidden chat sends
// no text, a chat on another provider sends metadata only, and everything is masked. Expected lines are written out here.
public sealed class FeedBuilderTests
{
    private static readonly FeedBrain ClaudeBrain = new(ProviderKind.Claude);
    private static readonly Guid Thread = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Mission = Guid.Parse("22222222-0000-0000-0000-000000000002");
    private static readonly Guid Assistant = Guid.Parse("33333333-0000-0000-0000-000000000003");

    private static FeedEvent? Build(FeedDraft draft, FeedBrain? brain = null) => FeedBuilder.Build(draft, brain ?? ClaudeBrain, 7);

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_hidden_chat_keeps_only_that_a_turn_ended_and_carries_no_id()
    {
        var draft = FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 3, "finished", Thread,
            "The launch codes are in the plan", FeedSamples.Facts("SECRET PROJECT PLAN", hidden: true));
        var line = Assert.IsType<FeedEvent>(Build(draft));
        Assert.Equal("A hidden chat finished a turn", line.Summary);
        Assert.Equal(FeedTextAccess.Hidden, line.Text);
        Assert.Equal(FeedSeverity.Info, line.Severity);
        Assert.Equal((null, null, null, 0L), (line.ThreadId, line.MissionId, line.AssistantId, line.CostDelta));
        Assert.Equal(7, line.Sequence);
    }

    [Theory]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    [InlineData(FeedEventKind.Approval)]
    [InlineData(FeedEventKind.Question)]
    [InlineData(FeedEventKind.NeedsAttention)]
    public void A_hidden_chat_that_needs_the_owner_says_only_that(FeedEventKind kind)
    {
        var draft = FeedSamples.Draft(kind, FeedSeverity.NeedsYou, 3, "waiting for approval", Thread,
            "Run rm -rf on the secret folder?", FeedSamples.Facts("SECRET PROJECT PLAN", hidden: true));
        var line = Assert.IsType<FeedEvent>(Build(draft));
        Assert.Equal("A hidden chat needs you", line.Summary);
        Assert.Equal(FeedSeverity.NeedsYou, line.Severity);
        Assert.Null(line.ThreadId);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void Every_other_kind_from_a_hidden_chat_is_dropped_and_no_kind_lets_its_words_through()
    {
        foreach (var kind in Enum.GetValues<FeedEventKind>())
        {
            var draft = FeedSamples.Draft(kind, FeedSeverity.Error, 3, "LEAD-WORDS", Thread, "DETAIL-WORDS",
                FeedSamples.Facts("TITLE-WORDS", hidden: true), 99);
            var line = Build(draft, new FeedBrain(ProviderKind.Claude, false, true, true));
            if (line is null)
            {
                Assert.False(draft.SurvivesHiding, kind.ToString());
                continue;
            }
            Assert.True(draft.SurvivesHiding, kind.ToString());
            Assert.DoesNotContain("WORDS", line.Summary, StringComparison.Ordinal);
            Assert.Null(line.ThreadId);
            Assert.Equal(0, line.CostDelta);
        }
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_chat_on_another_provider_gives_its_title_and_status_but_not_its_words()
    {
        var draft = FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 3, "finished", Thread,
            "Replaced the parser with a hand-written one", FeedSamples.Facts("Fix the parser", ProviderKind.Codex));
        var line = Assert.IsType<FeedEvent>(Build(draft));
        Assert.Equal("Fix the parser: finished", line.Summary);
        Assert.Equal(FeedTextAccess.MetadataOnly, line.Text);
        Assert.Equal(Thread, line.ThreadId);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void A_chat_on_the_brains_provider_gives_the_first_words_of_its_reply()
    {
        var draft = FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 3, "finished", Thread,
            "Replaced the parser with a hand-written one.\nAll 14 tests pass.", FeedSamples.Facts("Fix the parser"));
        var line = Assert.IsType<FeedEvent>(Build(draft));
        Assert.Equal("Fix the parser: finished. Replaced the parser with a hand-written one. All 14 tests pass.", line.Summary);
        Assert.Equal(FeedTextAccess.Full, line.Text);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void The_reply_excerpt_is_cut_to_300_characters()
    {
        var draft = FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 3, "finished", Thread, new string('x', 900), FeedSamples.Facts("T"));
        var line = Assert.IsType<FeedEvent>(Build(draft));
        Assert.Equal("T: finished. " + new string('x', 300) + "…", line.Summary);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    public void The_title_and_the_words_are_masked_before_they_reach_the_line()
    {
        var draft = FeedSamples.Draft(FeedEventKind.Error, FeedSeverity.Error, 3, "hit an error", Thread,
            "login failed for password=hunter2 on card 4111 1111 1111 1111", FeedSamples.Facts("Deploy with sk-proj-AbCdEfGhIjKlMnOpQrStUv1234"));
        var line = Assert.IsType<FeedEvent>(Build(draft));
        Assert.Equal("Deploy with [secret]: hit an error. login failed for password=[secret] on card [card]", line.Summary);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-4")]
    public void The_lead_is_masked_too_and_an_empty_title_reads_as_a_chat()
    {
        var draft = FeedSamples.Draft(FeedEventKind.ToolStarted, FeedSeverity.Quiet, 3, "using token=abc123xyz", Thread, null, FeedSamples.Facts(""));
        Assert.Equal("A chat: using token=[secret]", Assert.IsType<FeedEvent>(Build(draft)).Summary);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-1")]
    public void A_token_line_names_the_sum_of_a_folded_run()
    {
        var folded = FeedSamples.Tokens(Thread, 0, 1_500).Merge(FeedSamples.Tokens(Thread, 1, 2_710)) with { Chat = FeedSamples.Facts("Fix the parser") };
        var line = Assert.IsType<FeedEvent>(Build(folded));
        Assert.Equal("Fix the parser: used 4,210 tokens", line.Summary);
        Assert.Equal((4_210L, 2), (line.CostDelta, line.Count));
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-1")]
    public void An_event_that_is_not_about_a_chat_is_its_own_line_and_keeps_its_ids()
    {
        var draft = new FeedDraft(FeedEventKind.MissionState, FeedSeverity.Info, FeedSamples.Start, "Mission \"Weekly report\" is Working")
        {
            MissionId = Mission,
            AssistantId = Assistant,
        };
        var line = Assert.IsType<FeedEvent>(Build(draft, FeedBrain.Unknown));
        Assert.Equal("Mission \"Weekly report\" is Working", line.Summary);
        Assert.Equal((Mission, Assistant, FeedTextAccess.None), (line.MissionId, line.AssistantId, line.Text));
        Assert.Null(line.ThreadId);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-1")]
    public void A_visible_chat_keeps_its_ids_and_severity()
    {
        var draft = FeedSamples.Draft(FeedEventKind.Approval, FeedSeverity.NeedsYou, 3, "waiting for approval", Thread, "Run the tests?", FeedSamples.Facts()) with
        {
            MissionId = Mission,
            AssistantId = Assistant,
        };
        var line = Assert.IsType<FeedEvent>(Build(draft));
        Assert.Equal((Thread, Mission, Assistant, FeedSeverity.NeedsYou, FeedEventKind.Approval), (line.ThreadId, line.MissionId, line.AssistantId, line.Severity, line.Kind));
        Assert.Equal("Fix the parser: waiting for approval. Run the tests?", line.Summary);
        Assert.Equal(FeedSamples.Start.AddSeconds(3), line.Time);
    }

    [Fact]
    [Trait("Feature", "AS-2")]
    [Trait("Decision", "ED-NEW-dextra-as2-2")]
    public void The_same_draft_gives_a_different_line_as_the_owners_switches_change()
    {
        var draft = FeedSamples.Draft(FeedEventKind.TurnFinished, FeedSeverity.Info, 3, "finished", Thread, "Done with the work.", FeedSamples.Facts("Fix", ProviderKind.Codex));
        Assert.Equal("Fix: finished", Build(draft, new FeedBrain(ProviderKind.Claude))!.Summary);
        Assert.Equal("Fix: finished. Done with the work.", Build(draft, new FeedBrain(ProviderKind.Claude, false, true, true))!.Summary);
        Assert.Equal("Fix: finished. Done with the work.", Build(draft, new FeedBrain(ProviderKind.Codex))!.Summary);
        Assert.Equal("Fix: finished", Build(draft, FeedBrain.Unknown)!.Summary);
    }
}
