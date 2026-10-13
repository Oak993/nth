using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;
using static Marrow.Core.Tests.Fixtures.DigestSamples;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 6, canvas "Morning digest"; ED-NEW-dextra-as11-1): the digest is composed with no model from the facts nth holds.
// Its sections come in the order of 6, an empty one is left out (the first always speaks), the Suggestions lane follows them, and the
// last line is "That's everything for today." It names what needs the owner with each item linked, and what finished with the reviewer's verdict.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-1")]
public sealed class DigestComposerTests
{
    private static DigestFacts Everything() => Facts() with
    {
        NeedsYou = [Ask("A send for Store"), Ask("A reserve check", "c2")],
        Finished = [Done("Invoice INV-1183 filed"), Done("Dependency audit", hoursAgo: 8)],
        Running = [Running("Shop login fix", "Reviewing")],
        Accounts = [Account("Codex A", Window(ReserveWindowKind.FiveHour, 22, 70))],
        DutyRuns = [Run("Channel A numbers", ReportStyle.SilentUnlessChanged, DutyRunOutcome.Done, "Views are up 8% this week")],
        Today = [new DigestScheduled("w1", "Watch Channel A numbers", Morning.AddHours(1)), new DigestScheduled("w2", "File new invoices", Morning.AddHours(5))],
        ExpiredLogins = ["Channel B"],
        Answered = [new DigestAnswered("Pitch deck v3", 2, new DigestLink(DigestLinkKind.Thread, "t1"))],
        Asturs = [new DigestAstur("Store", "Two listings need new prices.")],
        Suggestions = [Offer("s1"), Offer("s2")],
        OfferedToday = 2,
        Seal = new DigestSeal(new DateOnly(2026, 10, 12), "3fa9c1b27de04a5b" + new string('0', 48)),
    };

    [Fact]
    public void The_sections_come_in_the_order_of_the_design_then_the_lane_the_seal_and_the_end_line()
    {
        var digest = DigestComposer.Compose(Everything());
        Assert.Equal(
            [DigestPart.NeedsYou, DigestPart.Finished, DigestPart.Running, DigestPart.Usage, DigestPart.Duties, DigestPart.Today, DigestPart.Unusual, DigestPart.Answered, DigestPart.Asturs],
            digest.Sections.Select(s => s.Part));
        var lines = digest.Text.Split('\n');
        var labels = new[] { "Needs you:", "Finished:", "Running:", "Usage:", "Duties:", "Today:", "Unusual:", "Answered for you:", "From your Asturs:", "Suggestions · 2 of at most 3 today", "Activity log 12 Oct: seal" };
        var places = labels.Select(label => Array.FindIndex(lines, l => l.StartsWith(label, StringComparison.Ordinal))).ToList();
        Assert.DoesNotContain(-1, places);
        Assert.Equal(places.Order(), places);
        Assert.Equal(Digest.EndLine, lines[^1]);
    }

    [Fact]
    public void The_digest_ends_on_the_one_line_it_always_ends_on_even_when_there_is_nothing_to_say()
    {
        var digest = DigestComposer.Compose(Facts());
        Assert.Equal("That's everything for today.", Digest.EndLine);
        Assert.EndsWith("\n" + Digest.EndLine, digest.Text, StringComparison.Ordinal);
        Assert.EndsWith(Digest.EndLine, digest.Spoken, StringComparison.Ordinal);
        // Nothing waits, nothing finished, nothing is running: the one section that always speaks says so, and the rest are left out.
        var only = Assert.Single(digest.Sections);
        Assert.Equal(DigestPart.NeedsYou, only.Part);
        Assert.Equal("Needs you: nothing.", only.Text);
        Assert.Equal("", digest.LaneTitle);
        Assert.Null(digest.SealLine);
    }

    [Fact]
    public void The_header_says_morning_digest_with_the_local_time_and_a_digest_the_owner_asked_for_does_not_say_morning()
    {
        Assert.Equal("Morning digest · 08:00", DigestComposer.Compose(Facts()).Header);
        Assert.Equal("Digest · 08:00", DigestComposer.Compose(Facts(DigestKind.Asked)).Header);
    }

    [Fact]
    public void What_needs_you_is_counted_and_each_item_is_linked_to_its_card()
    {
        var section = DigestComposer.Compose(Everything()).Sections[0];
        Assert.Equal("Needs you: 2. A send for Store; A reserve check", section.Text);
        Assert.Equal(
            [new DigestLink(DigestLinkKind.Decision, "c1"), new DigestLink(DigestLinkKind.Decision, "c2")],
            section.Items.Select(i => i.Link));
        Assert.True(section.Inline);
    }

    [Fact]
    public void Many_items_are_cut_to_five_and_the_rest_are_counted()
    {
        var asks = Enumerable.Range(1, 8).Select(i => Ask("Card " + i, "c" + i)).ToList();
        var section = DigestComposer.Compose(Facts() with { NeedsYou = asks }).Sections[0];
        Assert.Equal("8.", section.Lead);
        Assert.Equal(6, section.Items.Count);
        Assert.Equal("and 3 more", section.Items[^1].Text);
        Assert.Equal(DigestLinkKind.None, section.Items[^1].Link.Kind);
    }

    [Fact]
    public void What_finished_names_the_reviewers_verdict_oldest_first()
    {
        var section = DigestComposer.Compose(Everything()).Sections[1];
        Assert.Equal("Finished: “Dependency audit” (reviewed: passed); “Invoice INV-1183 filed” (reviewed: passed)", section.Text);
        var odd = DigestComposer.Compose(Facts() with { Finished = [Done("A", DigestReview.Failed), Done("B", DigestReview.NotReviewed, 2)] }).Sections[1];
        Assert.Equal("Finished: “A” (reviewed: failed); “B” (not reviewed)", odd.Text);
    }

    [Fact]
    public void What_is_running_says_its_state_and_the_next_checkpoint_when_one_is_known()
    {
        var running = new DigestRunning("Channel A's next video", new DigestLink(DigestLinkKind.Mission, "m"), "Working") { NextCheckpoint = Morning.AddHours(3) };
        var tomorrow = new DigestRunning("Taxes", new DigestLink(DigestLinkKind.Mission, "t"), "Planning") { NextCheckpoint = Morning.AddHours(24) };
        var section = DigestComposer.Compose(Facts() with { Running = [running, tomorrow, Running("Shop login fix", "Reviewing")] }).Sections[1];
        Assert.Equal(
            "Running: “Channel A's next video” (working; next checkpoint about 11:00); “Taxes” (planning; next checkpoint tomorrow about 08:00); “Shop login fix” (reviewing)",
            section.Text);
    }

    [Fact]
    public void What_runs_today_lists_only_what_is_still_to_come_today_in_time_order()
    {
        var facts = Facts() with
        {
            Today =
            [
                new DigestScheduled("late", "Late duty", Morning.AddHours(13)),
                new DigestScheduled("past", "Already ran", Morning.AddMinutes(-5)),
                new DigestScheduled("soon", "Soon duty", Morning.AddHours(1)),
                new DigestScheduled("tomorrow", "Tomorrow duty", Morning.AddHours(20)),
            ],
        };
        var section = DigestComposer.Compose(facts).Sections[1];
        Assert.Equal("Today: Soon duty at 09:00; Late duty at 21:00", section.Text);
    }

    [Fact]
    public void What_the_delegate_answered_says_how_many_in_which_chat_and_that_each_can_be_overridden()
    {
        var facts = Facts() with { Answered = [new DigestAnswered("Pitch deck v3", 2, new DigestLink(DigestLinkKind.Thread, "t")), new DigestAnswered("Taxes", 1, new DigestLink(DigestLinkKind.Thread, "u"))] };
        var section = DigestComposer.Compose(facts).Sections[1];
        Assert.Equal("Answered for you: 2 questions in “Pitch deck v3” (each with Override); 1 question in “Taxes” (each with Override)", section.Text);
    }

    [Fact]
    public void Each_Astur_adds_one_paragraph_under_its_name()
    {
        var facts = Facts() with { Asturs = [new DigestAstur("Store", "Two listings\nneed new prices."), new DigestAstur("Finance", "")] };
        var section = DigestComposer.Compose(facts).Sections[1];
        Assert.Equal(DigestPart.Asturs, section.Part);
        Assert.Equal("From your Asturs: Store: Two listings need new prices.", section.Text);
    }

    [Fact]
    public void The_lane_says_how_many_places_of_the_three_are_used_and_carries_each_suggestion()
    {
        var digest = DigestComposer.Compose(Everything());
        Assert.Equal("Suggestions · 2 of at most 3 today", digest.LaneTitle);
        Assert.Equal(["s1", "s2"], digest.Suggestions.Select(s => s.Key));
        Assert.Contains("A line for s1. A cost.", digest.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_seal_of_the_last_days_activity_log_is_printed_in_groups_of_four()
    {
        var digest = DigestComposer.Compose(Everything());
        Assert.Equal("Activity log 12 Oct: seal 3fa9 c1b2 7de0 4a5b", digest.SealLine);
        Assert.Equal(new string('0', 48), digest.Seal!.Hmac[16..]);
    }

    [Fact]
    public void The_same_facts_always_give_the_same_digest()
    {
        Assert.Equal(DigestComposer.Compose(Everything()).Text, DigestComposer.Compose(Everything()).Text);
        Assert.Equal(DigestComposer.Compose(Everything()).Spoken, DigestComposer.Compose(Everything()).Spoken);
    }

    [Fact]
    public void A_title_is_data_a_secret_in_it_is_masked_and_a_long_one_is_cut()
    {
        var secret = "sk-" + new string('a', 40);
        var facts = Facts() with { NeedsYou = [Ask("Send the key " + secret), Ask(new string('x', 400))] };
        var section = DigestComposer.Compose(facts).Sections[0];
        Assert.DoesNotContain(secret, section.Text, StringComparison.Ordinal);
        Assert.All(section.Items, i => Assert.True(i.Text.Length <= DigestComposer.TitleChars + 1));
    }

    [Fact]
    public void The_spoken_digest_is_a_few_short_sentences_that_end_on_the_end_line_and_name_no_bare_number_of_usage()
    {
        var spoken = DigestComposer.Compose(Everything()).Spoken;
        Assert.Equal("2 things need you. Finished: “Dependency audit” and “Invoice INV-1183 filed”. Running: “Shop login fix”. Your reserves hold. One thing looks unusual. That's everything for today.", spoken);
        Assert.DoesNotContain("%", spoken, StringComparison.Ordinal);
    }
}
