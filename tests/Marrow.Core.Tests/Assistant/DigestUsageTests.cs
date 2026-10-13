using System.Text.RegularExpressions;
using Marrow.Core.Port.Assistant;
using static Marrow.Core.Tests.Fixtures.DigestSamples;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-11 (DESIGN.md rev 5, 6 point 4, 11.5, 11.6; ED-NEW-dextra-as11-2): the usage section says it per account and per window, and every
// number names its window, so two numbers for one account never look inconsistent ("Codex A: 22% of the 5-hour window used; 31% of the weekly
// window is unused and resets at 14:00, so I'm using it first"). Anything resetting today says when. The reserve is said without a number of
// its own. Use the owner made outside nth is unusual and names its window too.
[Trait("Feature", "AS-11")]
[Trait("Decision", "ED-NEW-dextra-as11-2")]
public sealed class DigestUsageTests
{
    // Every "n%" in a line is followed at once by "of the" and the window's own words.
    private static readonly Regex Figure = new(@"(?<n>\d+(?:\.\d+)?)%(?<rest>[^;.,]*)", RegexOptions.CultureInvariant);

    private static readonly Regex Named = new(@"^ of the (5-hour window|weekly window|daily budget|monthly budget|[A-Za-z ]+ weekly)\b", RegexOptions.CultureInvariant);

    private static DigestSection Usage(params DigestAccount[] accounts)
    {
        var digest = DigestComposer.Compose(Facts() with { Accounts = accounts });
        return digest.Sections.Single(s => s.Part == DigestPart.Usage);
    }

    private static DigestWindow Weekly(double used, DateTimeOffset? resets, double? floor = null) => Window(ReserveWindowKind.Weekly, used, floor, resets);

    private static DigestWindow FiveHour(double used, DateTimeOffset? resets = null, double? floor = null) => Window(ReserveWindowKind.FiveHour, used, floor, resets);

    [Fact]
    public void The_canvas_example_reads_the_way_the_canvas_says_it()
    {
        // 08:00 local; the weekly window resets at 14:00 local with 31% unused; the 5-hour window is at 22% and resets in the evening.
        var section = Usage(Account("Codex A", FiveHour(22, resets: Morning.AddHours(9)), Weekly(69, Morning.AddHours(6))));
        Assert.Equal("Codex A: 22% of the 5-hour window used, resets at 17:00; 31% of the weekly window is unused and resets at 14:00, so I'm using it first.", section.Items[0].Text);
    }

    [Fact]
    public void Every_number_in_the_usage_section_is_followed_by_the_window_it_is_a_share_of()
    {
        var section = Usage(
            Account("Codex A", FiveHour(22, Morning.AddHours(2), 70), Weekly(69, Morning.AddHours(5))),
            Account("Claude B", FiveHour(41), Weekly(80.4, Morning.AddDays(3), 70)),
            Account("OpenRouter key", Window(ReserveWindowKind.Day, 12.6, null, Morning.AddHours(12)), Window(ReserveWindowKind.Month, 40)),
            Account("Gemini", Window(ReserveWindowKind.Other, 9, null, null, "Flash weekly")));
        var all = string.Join(" ", section.Items.Select(i => i.Text));
        var matches = Figure.Matches(all);
        Assert.NotEmpty(matches);
        foreach (Match match in matches)
            Assert.True(Named.IsMatch(match.Groups["rest"].Value), $"“{match.Value}” does not name its window in: {all}");
        // No "n%" anywhere is a number the section does not name a window for: no bare reserve percentage either.
        Assert.DoesNotMatch(@"\d+% reserve", all);
    }

    [Fact]
    public void Used_is_rounded_to_the_nearest_whole_point_and_unused_is_what_is_left_of_the_hundred()
    {
        var section = Usage(Account("A", Weekly(68.6, Morning.AddHours(4))));
        Assert.Equal("A: 31% of the weekly window is unused and resets at 12:00, so I'm using it first.", section.Items[0].Text);
        var plain = Usage(Account("B", FiveHour(21.5)));
        Assert.Equal("B: 22% of the 5-hour window used.", plain.Items[0].Text);
    }

    [Fact]
    public void A_weekly_window_resetting_within_twelve_hours_with_a_fifth_unused_is_used_first_and_any_other_is_not()
    {
        var soon = Usage(Account("A", Weekly(79, Morning.AddHours(11)))).Items[0].Text;
        Assert.Contains("so I'm using it first", soon, StringComparison.Ordinal);
        var tooFar = Usage(Account("A", Weekly(50, Morning.AddHours(13)))).Items[0].Text;
        Assert.DoesNotContain("using it first", tooFar, StringComparison.Ordinal);
        var tooLittle = Usage(Account("A", Weekly(85, Morning.AddHours(2)))).Items[0].Text;
        Assert.DoesNotContain("using it first", tooLittle, StringComparison.Ordinal);
        var fiveHour = Usage(Account("A", FiveHour(10, Morning.AddHours(2)))).Items[0].Text;
        Assert.DoesNotContain("using it first", fiveHour, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reset_that_is_not_today_is_not_said_and_one_just_after_midnight_says_tomorrow()
    {
        var later = Usage(Account("A", Weekly(50, Morning.AddDays(3)))).Items[0].Text;
        Assert.Equal("A: 50% of the weekly window used.", later);
        // 08:00 local now; a reset 17 hours on is 01:00 tomorrow and is more than 12 hours off, so it is not "used first": it is just a usage line.
        var night = Usage(Account("A", FiveHour(30, Morning.AddHours(17)))).Items[0].Text;
        Assert.Equal("A: 30% of the 5-hour window used.", night);
        // Within 12 hours but past midnight: 11 hours on from 14:00 local is 01:00 tomorrow.
        var facts = Facts() with { Now = Morning.AddHours(6), Accounts = [Account("A", Weekly(60, Morning.AddHours(17)))] };
        var text = DigestComposer.Compose(facts).Sections.Single(s => s.Part == DigestPart.Usage).Items[0].Text;
        Assert.Equal("A: 40% of the weekly window is unused and resets at 01:00 tomorrow, so I'm using it first.", text);
    }

    [Fact]
    public void Accounts_are_listed_by_name_and_their_windows_in_a_fixed_order()
    {
        var section = Usage(Account("Zed", Weekly(10, null), FiveHour(5)), Account("alpha", Window(ReserveWindowKind.Month, 3), Window(ReserveWindowKind.Day, 4)));
        Assert.Equal(["alpha: 4% of the daily budget used; 3% of the monthly budget used.", "Zed: 5% of the 5-hour window used; 10% of the weekly window used."], section.Items.Select(i => i.Text));
    }

    [Fact]
    public void The_reserve_holds_is_said_once_and_a_crossed_reserve_names_the_account_and_the_window()
    {
        var holds = Usage(Account("A", FiveHour(40, floor: 70), Weekly(30, null, 70)), Account("B", FiveHour(10)));
        Assert.Equal("Your reserves hold.", holds.Items[^1].Text);
        var crossed = Usage(Account("A", FiveHour(75, floor: 70)), Account("B", FiveHour(10, floor: 70)));
        Assert.Equal("A is past your reserve on the 5-hour window.", crossed.Items[^1].Text);
        Assert.DoesNotContain(crossed.Items, i => i.Text == "Your reserves hold.");
    }

    [Fact]
    public void With_no_rule_there_is_no_reserve_sentence_and_with_no_account_read_there_is_no_usage_section()
    {
        var none = Usage(Account("A", FiveHour(40)));
        Assert.DoesNotContain(none.Items, i => i.Text.Contains("reserve", StringComparison.Ordinal));
        var digest = DigestComposer.Compose(Facts() with { Accounts = [Account("Unread")] });
        Assert.DoesNotContain(digest.Sections, s => s.Part == DigestPart.Usage);
    }

    [Fact]
    public void Use_outside_nth_is_unusual_and_names_the_window()
    {
        var account = Account("Codex A", FiveHour(40)) with { Outside = [new DigestOutside(ReserveWindowKind.Weekly, "", 6.2), new DigestOutside(ReserveWindowKind.FiveHour, "", 1.0)] };
        var digest = DigestComposer.Compose(Facts() with { Accounts = [account] });
        var unusual = digest.Sections.Single(s => s.Part == DigestPart.Unusual);
        Assert.Equal(["6% of the weekly window on Codex A went on use outside nth in the last day."], unusual.Items.Select(i => i.Text));
    }

    [Fact]
    public void A_window_of_another_kind_goes_by_the_name_its_provider_gave_it()
    {
        var text = Usage(Account("Claude", Window(ReserveWindowKind.Other, 12, null, null, "Opus weekly"))).Items[0].Text;
        Assert.Equal("Claude: 12% of the Opus weekly used.", text);
    }

    [Fact]
    public void A_reading_that_is_not_a_number_or_is_out_of_range_reads_as_none_or_as_a_whole_window()
    {
        Assert.Equal("A: 0% of the 5-hour window used.", Usage(Account("A", FiveHour(double.NaN))).Items[0].Text);
        Assert.Equal("A: 100% of the 5-hour window used.", Usage(Account("A", FiveHour(140))).Items[0].Text);
        Assert.Equal("A: 0% of the 5-hour window used.", Usage(Account("A", FiveHour(-3))).Items[0].Text);
    }

    [Fact]
    public void The_usage_lines_link_to_their_account()
    {
        var section = Usage(Account("Codex A", FiveHour(22)));
        Assert.Equal(new DigestLink(DigestLinkKind.Account, "Codex A"), section.Items[0].Link);
    }
}
