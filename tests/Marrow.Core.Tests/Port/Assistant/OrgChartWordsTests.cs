using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-17 (DESIGN.md rev 5, 2.1, 9.4; the canvas's work lines and detail panel; ED-NEW-dextra-as17-4): the words of the org chart and of the sidebar's Astur rows, one
// set for both so they never read differently.
[Trait("Feature", "AS-17")]
[Trait("Decision", "ED-NEW-dextra-as17-4")]
public sealed class OrgChartWordsTests
{
    [Fact]
    public void The_work_line_says_what_the_Astur_is_doing_in_the_words_of_its_state()
    {
        Assert.Equal("Voiceover for video 42", OrgChartWords.WorkLine(AsturPresence.Working, ["Voiceover for video 42"]));
        Assert.Equal("Voiceover for video 42 (+2 more)", OrgChartWords.WorkLine(AsturPresence.Working, ["Voiceover for video 42", "Thumbnails", "Subtitles"]));
        Assert.Equal("Weekly stats for both channels", OrgChartWords.WorkLine(AsturPresence.Thinking, ["Weekly stats for both channels"]));
        Assert.Equal("Waiting on you: weekly update send", OrgChartWords.WorkLine(AsturPresence.Waiting, ["Weekly update"], "weekly update send"));
        Assert.Equal("Waiting on you", OrgChartWords.WorkLine(AsturPresence.Waiting, []));
        Assert.Equal("Paused: Espresso machines", OrgChartWords.WorkLine(AsturPresence.Paused, ["Espresso machines"]));
        Assert.Equal("Stopped: Espresso machines", OrgChartWords.WorkLine(AsturPresence.Stopped, ["Espresso machines"]));
        Assert.Equal("Blocked: Sign in to the shop", OrgChartWords.WorkLine(AsturPresence.Blocked, ["Sign in to the shop"]));
        Assert.Equal("Nothing at work", OrgChartWords.WorkLine(AsturPresence.Idle, []));
    }

    [Fact]
    public void A_state_with_no_task_to_name_still_says_the_state()
    {
        Assert.Equal("Working", OrgChartWords.WorkLine(AsturPresence.Working, []));
        Assert.Equal("Thinking", OrgChartWords.WorkLine(AsturPresence.Thinking, []));
        Assert.Equal("Paused", OrgChartWords.WorkLine(AsturPresence.Paused, []));
        Assert.Equal("Blocked", OrgChartWords.WorkLine(AsturPresence.Blocked, []));
        Assert.Equal("Stopped", OrgChartWords.WorkLine(AsturPresence.Stopped, []));
    }

    [Fact]
    public void A_long_or_untidy_line_is_one_clean_line_cut_with_an_ellipsis()
    {
        var long120 = OrgChartWords.WorkLine(AsturPresence.Working, ["word   word\n" + new string('x', 400)]);
        Assert.True(long120.Length <= OrgChartWords.WorkMax, long120);
        Assert.EndsWith("\u2026", long120, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', long120);
        Assert.StartsWith("word word ", long120, StringComparison.Ordinal);
        Assert.Equal("Waiting on you: " + new string('y', 119) + "\u2026", OrgChartWords.WorkLine(AsturPresence.Waiting, [], new string('y', 300)));
    }

    [Theory]
    [InlineData("Fix the login", "Fix the login")]
    [InlineData("Next video: Why lenses fog up", "Next video\u2026")]
    [InlineData("Espresso machines under $800", "Espresso\u2026")]
    [InlineData("Supercalifragilistic", "Supercalifrag\u2026")]
    [InlineData("   ", "Task")]
    [InlineData("a b  c", "a b c")]
    public void A_chip_label_is_the_start_of_the_title_cut_at_a_word_and_never_longer_than_14(string title, string label)
    {
        Assert.Equal(label, OrgChartWords.ChipLabel(title));
        Assert.True(OrgChartWords.ChipLabel(title).Length <= OrgChartWords.ChipMax);
    }

    [Fact]
    public void The_slice_in_words_follows_the_larger_of_its_percent_and_its_dollars_and_says_when_it_is_reached()
    {
        Assert.Equal("38% of its daily slice", OrgChartWords.SliceUse(.38));
        Assert.Equal("0% of its daily slice", OrgChartWords.SliceUse(0));
        Assert.Equal("13% of its daily slice", OrgChartWords.SliceUse(.125));
        Assert.Equal("At its daily slice", OrgChartWords.SliceUse(1));
        Assert.Equal("At its daily slice", OrgChartWords.SliceUse(1.4));
        Assert.Equal("No daily slice of its own", OrgChartWords.SliceUse(null));
        Assert.Equal("Nothing used today", OrgChartWords.TotalUse(0));
        Assert.Equal("46% of the busiest window today, all together", OrgChartWords.TotalUse(46));
    }

    [Fact]
    public void An_Asturs_slice_reads_as_the_canvas_card_does()
    {
        Assert.Equal("Up to 15% of any account a day", OrgChartWords.SliceLine(new AsturBudget(15)));
        Assert.Equal("Up to 7.5% of any account a day and $5 a day on an API key", OrgChartWords.SliceLine(new AsturBudget(7.5, 5m)));
        Assert.Equal("Up to 10% of any account a day and $2.5 a day on an API key", OrgChartWords.SliceLine(new AsturBudget(10, 2.5m)));
        Assert.Equal("No slice of its own: your reserves apply", OrgChartWords.SliceLine(null));
    }

    [Fact]
    public void A_node_is_read_by_its_name_its_state_in_a_word_what_waits_and_the_work_line()
    {
        Assert.Equal("Channel A, Working. Voiceover for video 42", OrgChartWords.NodeName("Channel A", AsturPresence.Working, "Voiceover for video 42", 0));
        Assert.Equal("Store, Needs you, 2 decisions open. Waiting on you: weekly update send", OrgChartWords.NodeName("Store", AsturPresence.Waiting, "Waiting on you: weekly update send", 2));
        Assert.Equal("Store, Needs you, 1 decision open", OrgChartWords.NodeName("Store", AsturPresence.Waiting, "", 1));
        Assert.Equal("2 missions \u00B7 3 duties", OrgChartWords.Pair(OrgChartWords.Count(2, "mission", "missions"), OrgChartWords.Count(3, "duty", "duties")));
        Assert.Equal("1 mission \u00B7 1 duty", OrgChartWords.Pair(OrgChartWords.Count(1, "mission", "missions"), OrgChartWords.Count(1, "duty", "duties")));
        Assert.Equal("+3", OrgChartWords.MoreChip(3));
    }

    [Fact]
    public void Pause_and_Resume_say_what_they_did_to_one_assistants_tasks_or_why_nothing_happened()
    {
        Assert.Equal("Pausing 2 tasks of Channel A: each stops at its next turn boundary and nothing is cut.", OrgChartWords.PauseLine("Channel A", 2));
        Assert.Equal("Pausing 1 task of Store: each stops at its next turn boundary and nothing is cut.", OrgChartWords.PauseLine("Store", 1, "ignored"));
        Assert.Equal("Channel A has nothing to pause.", OrgChartWords.PauseLine("Channel A", 0));
        Assert.Equal("That task is not here any more.", OrgChartWords.PauseLine("Channel A", 0, "That task is not here any more."));
        Assert.Equal("Resumed 3 tasks of Store.", OrgChartWords.ResumeLine("Store", 3));
        Assert.Equal("Store has nothing to resume.", OrgChartWords.ResumeLine("Store", 0, " "));
        Assert.Equal("Cannot resume.", OrgChartWords.ResumeLine("Store", 0, "Cannot resume."));
    }
}
