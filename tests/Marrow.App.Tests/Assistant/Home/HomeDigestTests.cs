using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.DigestRigs;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-11 (DESIGN.md rev 5, 2.2 "The morning digest is pinned at the top as the day's first card", 5.2 "the home's composer chips", 6; the approved canvas;
// ED-NEW-dextra-as11-13): the home carries the digest. Its card sits above the chips and the chat as the thread's first card (the empty thread's mark gives way to it),
// in a frame that never takes more than half the column; each suggestion open today is a chip after the common asks whose tap brings that suggestion's Accept into view
// and never accepts it; the chips follow the lane as it changes; and a tap on an item of the digest opens what it names.
[Collection("Headless")]
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-13")]
public sealed class HomeDigestTests(HeadlessSession headless)
{
    private static IDisposable Still() => Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });

    private static ChatHabit Habit(string key, string label)
    {
        DateTimeOffset Friday(int weeksAgo) => new DateTimeOffset(2026, 10, 9, 16, 5, 0, TimeSpan.Zero).AddDays(-7 * weeksAgo);
        return new ChatHabit("app:" + key, label, MissionRig.Project, "App", [Friday(2), Friday(1), Friday(0)]);
    }

    private static AssistantHomeView Home(DutyHostRig rig, out DigestModel model, bool empty = true, Func<AssistantHomeParts, AssistantHomeParts>? change = null)
    {
        var digests = rig.Host.Digests!;
        var shown = new DigestModel(digests, () => TimeZoneInfo.Utc) { OpenLink = _ => { } };
        model = shown;
        var parts = new AssistantHomeParts(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls())
        {
            Name = DextraHost.Name,
            ConversationEmpty = () => empty,
            Digest = () => new DigestCard(shown),
            Suggestions = () => [.. digests.Open.Select(HomeSuggestion.Of)],
            SuggestionsChanged = digests.Watch,
        };
        return new AssistantHomeView(change?.Invoke(parts) ?? parts);
    }

    private static async Task<DutyHostRig> WithDigest(int cards = 1, bool composed = true)
    {
        var rig = Quiet(Setup(habits: () => [Habit("a", "Re-price listings")]));
        for (var i = 0; i < cards; i++) Card(rig, "Which chart style " + i + "?");
        if (composed) await rig.Host.Digests!.Compose(DigestKind.Scheduled);
        return rig;
    }

    [Fact]
    public async Task The_digest_is_the_first_card_above_the_chips_and_the_empty_threads_mark_gives_way_to_it()
    {
        using var rig = await WithDigest();
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Home(rig, out var model);
            using var stage = new HomeStage(home, 1200, 900);
            Assert.True(home.DigestCard!.IsVisible);
            Assert.False(home.HeroShown);                                      // the digest took the mark's place at the top
            var frame = home.DigestFrame!;
            Assert.StartsWith("Morning digest", AutomationProperties.GetName(home.DigestCard), StringComparison.Ordinal);
            Assert.True(Grid.GetRow(frame) < Grid.GetRow(home.Chips), "the digest sits above the chips");
            Assert.True(frame.Bounds.Bottom <= home.Chips.Bounds.Top + 0.5, "the digest ends before the chips begin");
            model.Dispose();
        });
    }

    [Fact]
    public async Task With_no_digest_yet_the_card_is_hidden_and_the_mark_shows_and_a_digest_composed_later_takes_its_place()
    {
        using var rig = await WithDigest(composed: false);
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Home(rig, out var model);
            using var stage = new HomeStage(home, 1200, 900);
            Assert.False(home.DigestCard!.IsVisible);
            Assert.True(home.HeroShown);
            model.Dispose();
        });
        await rig.Host.Digests!.Compose(DigestKind.Asked);
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Home(rig, out var model);
            using var stage = new HomeStage(home, 1200, 900);
            Assert.True(home.DigestCard!.IsVisible);
            Assert.False(home.HeroShown);
            model.Dispose();
        });
    }

    [Fact]
    public async Task A_digest_that_arrives_while_the_home_is_open_takes_the_marks_place_at_once()
    {
        using var rig = await WithDigest(composed: false);
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Home(rig, out var model);
            using var stage = new HomeStage(home, 1200, 900);
            Assert.True(home.HeroShown);
            Assert.False(home.DigestCard!.IsVisible);
            var facts = new DigestFacts("d-late", new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc, DigestKind.Scheduled);
            rig.Host.Digests!.Book.Add(DigestComposer.Compose(facts));
            stage.Layout();
            Assert.True(home.DigestCard.IsVisible);
            Assert.False(home.HeroShown);
            model.Dispose();
        });
    }

    [Fact]
    public async Task The_frame_never_takes_more_than_half_of_the_column_so_the_chat_stays_in_reach()
    {
        using var rig = await WithDigest(cards: 12);
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Home(rig, out var model);
            using var stage = new HomeStage(home, 1200, 640);
            var frame = home.DigestFrame!;
            var column = home.ConversationColumn;
            Assert.True(column.Bounds.Height > 0);
            Assert.True(frame.Bounds.Height <= Math.Max(AssistantHomeView.DigestMinHeight, column.Bounds.Height * 0.5) + 0.5, $"frame {frame.Bounds.Height} in a column of {column.Bounds.Height}");
            Assert.True(frame.Bounds.Height > 0);
            model.Dispose();
        });
    }

    [Fact]
    public async Task Each_open_suggestion_is_a_chip_after_the_common_asks_named_for_its_whole_line_and_a_tap_brings_its_Accept_into_view_without_accepting()
    {
        using var rig = await WithDigest();
        Assert.Single(rig.Host.Digests!.Open);
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Home(rig, out var model);
            using var stage = new HomeStage(home, 1200, 900);
            var chip = Assert.Single(home.SuggestionChips);
            var suggestion = rig.Host.Digests.Open[0];
            Assert.Equal("Suggestion: " + suggestion.Line, AutomationProperties.GetName(chip));
            Assert.Equal("Make “Re-price listings” a duty", Texts(chip).Single());
            var asks = HomeWords.Chips.Count;
            Assert.Equal(asks, home.Chips.Children.IndexOf(chip));            // after the four common asks
            Click(chip);
            stage.Layout();
            var row = ((DigestCard)home.DigestCard!).LaneRows.Single();
            Assert.True(row.AcceptButton!.IsFocused);
            Assert.Single(rig.Host.Digests.Open);                              // looking is not accepting
            Assert.DoesNotContain(rig.Controller.Queue.Waiting, c => c.Title.StartsWith("Create the duty", StringComparison.Ordinal));
            model.Dispose();
        });
    }

    [Fact]
    public async Task The_chips_follow_the_lane_as_a_suggestion_is_answered_and_a_new_one_arrives()
    {
        using var rig = await WithDigest();
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = Home(rig, out var model);
            using var stage = new HomeStage(home, 1200, 900);
            Assert.Single(home.SuggestionChips);
            var key = rig.Host.Digests!.Open.Single().Key;
            Assert.NotNull(rig.Host.Digests.Dismiss(key));
            stage.Layout();
            Assert.Empty(home.SuggestionChips);
            Assert.DoesNotContain(home.Chips.Children, c => c is Button b && (AutomationProperties.GetName(b) ?? "").StartsWith("Suggestion:", StringComparison.Ordinal));
            rig.Host.Digests.Suggest("dextra", "Back up the invoices?", null);
            stage.Layout();
            var chip = Assert.Single(home.SuggestionChips);
            Assert.Equal("Suggestion: Back up the invoices?", AutomationProperties.GetName(chip));
            model.Dispose();
        });
    }

    [Fact]
    public void A_long_suggestion_is_cut_to_a_short_chip_and_keeps_its_whole_line_for_the_screen_reader()
    {
        var plan = new SuggestionPlan(SuggestionDo.StartMission, new string('w', 120)) { Brief = "Do it." };
        var chip = HomeSuggestion.Of(new Suggestion("k", SuggestionKind.FromBrain, "The whole line.", "A cost.", plan, DateTimeOffset.UnixEpoch));
        Assert.True(chip.Chip.Length <= HomeSuggestion.ChipChars);
        Assert.EndsWith("…", chip.Chip, StringComparison.Ordinal);
        Assert.Equal("The whole line.", chip.Line);
        var duty = HomeSuggestion.Of(new Suggestion("d", SuggestionKind.IdleDuty, "Slower?", "Saves.", new SuggestionPlan(SuggestionDo.ChangeDuty, "Espresso price"), DateTimeOffset.UnixEpoch));
        Assert.Equal("Slow down “Espresso price”", duty.Chip);
    }

    [Fact]
    public void A_home_made_without_a_digest_service_has_no_card_and_no_suggestion_chips_and_nothing_else_changes()
    {
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var home = new AssistantHomeView(new AssistantHomeParts(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls()) { Name = DextraHost.Name });
            using var stage = new HomeStage(home, 1200, 900);
            Assert.Null(home.DigestCard);
            Assert.Empty(home.SuggestionChips);
            Assert.True(home.HeroShown);
            Assert.Equal(HomeWords.Chips.Count, home.Chips.Children.Count);
        });
    }

    [Fact]
    public void A_home_that_leaves_the_screen_lets_go_of_the_lane()
    {
        var watchers = 0;
        headless.OnUiThread(() =>
        {
            using var still = Still();
            var parts = new AssistantHomeParts(new FakeMissionJobs(), new FakeDecisions(), new FakeGlobalControls())
            {
                Name = DextraHost.Name,
                Suggestions = () => [],
                SuggestionsChanged = _ =>
                {
                    watchers++;
                    return new CallbackDisposable(() => watchers--);
                },
            };
            var home = new AssistantHomeView(parts);
            using (new HomeStage(home, 1200, 900)) Assert.Equal(1, watchers);
        });
        Assert.Equal(0, watchers);
    }
}
