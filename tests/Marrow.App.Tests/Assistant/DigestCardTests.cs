using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ActivityViewStage;
using static Marrow.App.Tests.Fixtures.DigestRigs;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 2.2, 6; the approved canvas "Morning digest"; ED-NEW-dextra-as11-11): the digest card in a headless window. A header ("Morning digest · 08:00"),
// the sections as a numbered list in the design's order with each item a link to its source, the Suggestions lane with Accept and Dismiss, the seal of the last day's
// activity log, and the line it ends on. Accessible names carry what each control is for; a tap on a suggestion becomes the line that took its place, with no fade under
// Reduce Motion; the lane sits beside its buttons on a wide card and above them on a narrow one; long text and right-to-left text stay inside the card.
[Collection("Headless")]
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-11")]
public sealed class DigestCardTests(HeadlessSession headless)
{
    private static ChatHabit Habit(string key, string label)
    {
        DateTimeOffset Friday(int weeksAgo) => new DateTimeOffset(2026, 10, 9, 16, 5, 0, TimeSpan.Zero).AddDays(-7 * weeksAgo);
        return new ChatHabit("app:" + key, label, MissionRig.Project, "App", [Friday(2), Friday(1), Friday(0)]);
    }

    // A rig with a digest composed from real state: a card waiting, a finished task, a duty found something, and two suggestions in the lane.
    private static async Task<DutyHostRig> Composed(string? title = null)
    {
        var rig = Quiet(Setup(dayFinal: _ => new string('a', 64), habits: () => [Habit("a", title ?? "Re-price listings"), Habit("b", "File the invoices")], logins: () => ["Channel B"]));
        Card(rig, "Which chart style?");
        Finished(rig, "Dependency audit");
        // The reviewer passed it, so its changes wait for the owner's Merge: he discards them, and only the question is left waiting.
        var merge = rig.Controller.Queue.Waiting.Single(c => c.Kind == DecisionKind.Merge);
        Assert.Null(rig.Controller.Queue.Answer(merge.Id, DecisionAnswer.Discard));
        await rig.Host.Digests!.Compose(DigestKind.Scheduled);
        return rig;
    }

    private static DigestCard Make(DutyHostRig rig, out DigestModel model, bool links = true)
    {
        model = new DigestModel(rig.Host.Digests!, () => TimeZoneInfo.Utc);
        if (links) model.OpenLink = _ => { };
        return new DigestCard(model);
    }

    [Fact]
    public async Task The_card_shows_the_header_the_numbered_sections_in_order_the_seal_and_the_line_it_ends_on()
    {
        using var rig = await Composed();
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            using var stage = new ReserveViewStage(card, 700, 900);
            Assert.Equal("Morning digest · 06:00", card.Header);
            Assert.Equal(["Needs you: 1. Which chart style?", "Finished: “Dependency audit” (reviewed: passed)", "Unusual: Your sign-in for Channel B expired."], card.SectionTexts);
            Assert.Equal("Activity log 12 Oct: seal aaaa aaaa aaaa aaaa", card.Seal);
            Assert.Equal("That's everything for today.", card.EndLine);
            Assert.Equal("Suggestions · 2 of at most 3 today", card.LaneTitle);
            var texts = Texts(card);
            Assert.True(Array.IndexOf(texts, "1.") < Array.IndexOf(texts, "3.") && Array.IndexOf(texts, "3.") < Array.IndexOf(texts, card.LaneTitle) && Array.IndexOf(texts, card.LaneTitle) < Array.IndexOf(texts, card.Seal) && Array.IndexOf(texts, card.Seal) < Array.IndexOf(texts, card.EndLine));
            Assert.Equal("Morning digest · 06:00", AutomationProperties.GetName(card));
            model.Dispose();
        });
    }

    [Fact]
    public async Task Each_linked_item_is_a_button_named_for_what_it_opens_and_a_tap_opens_it()
    {
        using var rig = await Composed();
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            var opened = new List<DigestLink>();
            model.OpenLink = opened.Add;
            using var stage = new ReserveViewStage(card, 700, 900);
            Assert.Equal(["Open: Which chart style?", "Open: “Dependency audit”", "Open: Your sign-in for Channel B expired."], card.Links.Select(AutomationProperties.GetName));
            foreach (var link in card.Links) Click(link);
            Assert.Equal([DigestLinkKind.Decision, DigestLinkKind.Mission, DigestLinkKind.Account], opened.Select(l => l.Kind));
            model.Dispose();
        });
    }

    [Fact]
    public async Task With_nothing_to_open_the_items_are_plain_text_and_not_dead_buttons()
    {
        using var rig = await Composed();
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model, links: false);
            using var stage = new ReserveViewStage(card, 700, 900);
            Assert.Empty(card.Links);
            Assert.Contains(Texts(card), text => text.StartsWith("Which chart style?", StringComparison.Ordinal));
            model.Dispose();
        });
    }

    [Fact]
    public async Task Each_suggestion_has_Accept_and_Dismiss_named_for_the_suggestion_and_the_card_has_no_Read_aloud_until_a_voice_is_bound()
    {
        using var rig = await Composed();
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            using var stage = new ReserveViewStage(card, 700, 900);
            Assert.Equal(2, card.LaneRows.Count);
            var row = card.LaneRows[0];
            Assert.StartsWith("You started", row.Text, StringComparison.Ordinal);
            Assert.Equal("Accept: " + row.Text, AutomationProperties.GetName(row.AcceptButton!));
            Assert.Equal("Dismiss: " + row.Text, AutomationProperties.GetName(row.DismissButton!));
            Assert.Null(card.ReadAloudButton);

            string? spoken = null;
            model.ReadAloud = text => spoken = text;
            stage.Layout();
            Assert.NotNull(card.ReadAloudButton);
            Assert.Equal("Read the digest aloud", AutomationProperties.GetName(card.ReadAloudButton!));
            Click(card.ReadAloudButton!);
            Assert.Equal(model.Digest!.Spoken, spoken);
            Assert.EndsWith("That's everything for today.", spoken, StringComparison.Ordinal);
            model.Dispose();
        });
    }

    [Fact]
    public async Task Dismissing_a_suggestion_turns_its_row_into_the_line_that_took_its_place_and_the_focus_goes_to_the_next_open_one()
    {
        using var rig = await Composed();
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            using var stage = new ReserveViewStage(card, 700, 900);
            Click(card.LaneRows[0].DismissButton!);
            stage.Layout();
            Assert.Equal("Dismissed. I'll offer fewer like this.", card.LaneRows[0].Text);
            Assert.Null(card.LaneRows[0].AcceptButton);
            Assert.NotNull(card.LaneRows[1].AcceptButton);
            Assert.True(card.LaneRows[1].AcceptButton!.IsFocused);
            Assert.Equal("Dismissed. I'll offer fewer like this.", AutomationProperties.GetName(card.LaneRows[0]));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(card.LaneRows[0].Children.OfType<StackPanel>().Single().Children[0]));
            model.Dispose();
        });
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Accepting_a_suggestion_acts_and_says_what_it_did_and_a_suggestion_that_could_not_be_acted_on_keeps_its_buttons_with_the_reason()
    {
        using var rig = await Composed();
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            using var stage = new ReserveViewStage(card, 700, 900);
            Click(card.LaneRows[0].AcceptButton!);
            stage.Layout();
            Assert.Equal("The duty card is in Decisions.", card.LaneRows[0].Text);
            Assert.Contains(rig.Controller.Queue.Waiting, c => c.Title.StartsWith("Create the duty", StringComparison.Ordinal));
            model.Dispose();
        });
        // A mission suggestion whose project is gone: the buttons stay and the reason shows under the line.
        var broken = new SuggestionPlan(SuggestionDo.StartMission, "Look") { Brief = "Look.", ProjectId = Guid.NewGuid() };
        Offer(rig, Candidate("m9", broken, urgency: 0));
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            using var stage = new ReserveViewStage(card, 700, 900);
            var row = card.LaneRows.Single(r => r.Key == "m9");
            Click(row.AcceptButton!);
            stage.Layout();
            var again = card.LaneRows.Single(r => r.Key == "m9");
            Assert.NotNull(again.AcceptButton);
            Assert.Equal("The project it was for is gone.", again.Problem!.Text);
            model.Dispose();
        });
        await Task.CompletedTask;
    }

    [Fact]
    public async Task On_a_wide_card_the_buttons_sit_beside_the_line_and_on_a_narrow_one_under_it()
    {
        using var rig = await Composed();
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            using var wide = new ReserveViewStage(card, 700, 900);
            var row = card.LaneRows[0];
            Assert.Equal((0, 1), (Grid.GetRow(row.Buttons!), Grid.GetColumn(row.Buttons!)));
            model.Dispose();
            var narrowCard = Make(rig, out var narrowModel);
            using var narrow = new ReserveViewStage(narrowCard, 320, 900);
            var narrowRow = narrowCard.LaneRows[0];
            Assert.Equal((1, 0), (Grid.GetRow(narrowRow.Buttons!), Grid.GetColumn(narrowRow.Buttons!)));
            Assert.True(narrowCard.Bounds.Width <= 320.01);
            narrowModel.Dispose();
        });
    }

    [Fact]
    public async Task Long_text_and_right_to_left_text_stay_inside_the_card_at_a_narrow_width()
    {
        using var rig = await Composed(new string('w', 120) + " listings");
        Card(rig, new string('q', 300));
        await rig.Host.Digests!.Compose(DigestKind.Asked);
        headless.OnUiThread(() =>
        {
            foreach (var flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            {
                var card = Make(rig, out var model);
                using var stage = new ReserveViewStage(card, 320, 1400, flow);
                Assert.True(card.Bounds.Width <= 320.01, $"the card is {card.Bounds.Width} wide");
                foreach (var link in card.Links)
                {
                    var corner = link.TranslatePoint(new Point(link.Bounds.Width, 0), stage.Window)!.Value;
                    Assert.InRange(corner.X, -0.5, 320.5);
                }
                foreach (var row in card.LaneRows)
                {
                    var point = row.AcceptButton!.TranslatePoint(new Point(0, 0), stage.Window)!.Value;
                    Assert.InRange(point.X, 0, 320);
                }
                model.Dispose();
            }
        });
    }

    [Fact]
    public async Task With_Reduce_Motion_an_answered_row_appears_at_once_and_otherwise_it_fades_in()
    {
        using var rig = await Composed();
        headless.OnUiThread(() =>
        {
            using var still = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            var card = Make(rig, out var model);
            using var stage = new ReserveViewStage(card, 700, 900);
            Click(card.LaneRows[0].DismissButton!);
            stage.Layout();
            var status = card.LaneRows[0].Children.OfType<StackPanel>().Single().Children[0];
            Assert.Equal(1, status.Opacity);
            Assert.Null(status.Transitions);
            model.Dispose();
        });
        headless.OnUiThread(() =>
        {
            using var moving = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
            var card = Make(rig, out var model);
            using var stage = new ReserveViewStage(card, 700, 900);
            Click(card.LaneRows[1].DismissButton!);
            var status = card.LaneRows[1].Children.OfType<StackPanel>().Single().Children[0];
            Assert.Equal(0, status.Opacity);                                  // it starts clear and eases in; nothing snaps
            Assert.NotNull(status.Transitions);
            model.Dispose();
        });
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_digest_composed_off_the_main_thread_waits_for_the_main_thread_to_reach_the_card()
    {
        // A headless session reports every thread as the UI thread, so the main thread is the test's own here: what matters is that the model hands the change over to it.
        using var rig = Quiet();
        DigestModel? model = null;
        DigestCard? card = null;
        ReserveViewStage? stage = null;
        var uiThread = 0;
        var posted = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var seenOn = new List<int>();
        headless.OnUiThread(() =>
        {
            uiThread = Environment.CurrentManagedThreadId;
            model = new DigestModel(rig.Host.Digests!, () => TimeZoneInfo.Utc, () => Environment.CurrentManagedThreadId == uiThread, posted.Enqueue);
            model.Changed += () => seenOn.Add(Environment.CurrentManagedThreadId);
            card = new DigestCard(model);
            stage = new ReserveViewStage(card, 700, 900);
            Assert.False(card.IsVisible);
        });
        await Task.Run(() => rig.Host.Digests!.Compose(DigestKind.Asked));      // the digest is kept, and the service says so, on a pool thread
        Assert.Empty(seenOn);                                                      // nothing was drawn from that thread
        Assert.NotEmpty(posted);                                                   // the changes (the ledger rolled to the day, the digest kept) wait for the main thread
        headless.OnUiThread(() =>
        {
            while (posted.TryDequeue(out var work)) work();
            stage!.Layout();
            Assert.True(card!.IsVisible);
            Assert.Equal("Digest · 06:00", card.Header);
            Assert.NotEmpty(seenOn);
            Assert.All(seenOn, thread => Assert.Equal(uiThread, thread));
            stage.Dispose();
            model!.Dispose();
        });
    }

    [Fact]
    public void Before_the_first_digest_the_card_is_not_shown()
    {
        using var rig = Quiet();
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            Assert.False(card.IsVisible);
            model.Dispose();
        });
    }

    [Fact]
    public async Task A_digest_of_an_earlier_day_lists_what_it_offered_without_buttons()
    {
        using var rig = await Composed();
        rig.Clock.Advance(TimeSpan.FromDays(1));
        rig.Host.Digests!.Book.Update(l => l.OnDay(new DateOnly(2026, 10, 14)));
        headless.OnUiThread(() =>
        {
            var card = Make(rig, out var model);
            using var stage = new ReserveViewStage(card, 700, 900);
            Assert.False(model.IsToday);
            Assert.Equal(2, card.LaneRows.Count);
            Assert.All(card.LaneRows, r => Assert.Null(r.AcceptButton));
            model.Dispose();
        });
        await Task.CompletedTask;
    }

    [Fact]
    public void The_cards_text_meets_four_and_a_half_to_one_in_both_appearances()
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var background = variant == ThemeVariant.Dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xF6, 0xF6, 0xF6);
            var card = Chrome.Overlay(0.04).Resolve(variant);
            var ground = Blend(card, background);
            foreach (var (name, text) in new[] { ("primary", Chrome.PrimaryText.Resolve(variant)), ("secondary", Chrome.SecondaryText.Resolve(variant)) })
            {
                var ratio = Contrast(Blend(text, ground), ground);
                Assert.True(ratio >= 4.5, $"{name} text is {ratio:0.00}:1 in {variant}");
            }
        }
    }

    private static Color Blend(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }

    private static double Contrast(Color a, Color b) => (Math.Max(Luminance(a), Luminance(b)) + 0.05) / (Math.Min(Luminance(a), Luminance(b)) + 0.05);
}
