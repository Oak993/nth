using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Caps"; the approved canvas "Standing rules"; ED-NEW-dextra-as15-2): the words of a cap are the
// canvas's own where it has them ("Channel A: at most 15% of any account a day", "Standing · Channel A only"), in one place so the
// Reserves view, the card and the toast read the same.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-2")]
public sealed class CostCapWordsTests
{
    private static string? Name(string id) => id == "a" ? "Channel A" : null;

    [Fact]
    public void The_titles_are_the_canvas_s_where_it_has_them()
    {
        Assert.Equal("Channel A: at most 15% of any account a day", CostCapWords.Title(new CostCap("1", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "a" }, Name));
        Assert.Equal("At most 15% of any account a day", CostCapWords.Title(new CostCap("1", CostCapScope.AssistantDay) { Percent = 15 }, Name));
        Assert.Equal("At most 5% of any account for this task", CostCapWords.Title(new CostCap("1", CostCapScope.Task) { Percent = 5 }));
        Assert.Equal("At most $2 for this task", CostCapWords.Title(new CostCap("1", CostCapScope.Task) { Dollars = 2m }));
        Assert.Equal("At most 5% of any account and $2 for this task", CostCapWords.Title(new CostCap("1", CostCapScope.Task) { Percent = 5, Dollars = 2m }));
        Assert.Equal("At most $20 a day on API keys", CostCapWords.Title(new CostCap("1", CostCapScope.ApiDay) { Dollars = 20m }));
        Assert.Equal("At most $5 a day on the OpenRouter key", CostCapWords.Title(new CostCap("1", CostCapScope.ApiDay) { Dollars = 5m, AccountNames = new ValueList<string>(["OpenRouter"]) }));
        Assert.Equal("At most $5 a day on the OpenRouter and DeepSeek keys", CostCapWords.Title(new CostCap("1", CostCapScope.ApiDay) { Dollars = 5m, AccountNames = new ValueList<string>(["OpenRouter", "DeepSeek"]) }));
        Assert.Equal("Channel Z: at most 10.5% of any account a day", CostCapWords.Title(new CostCap("1", CostCapScope.AssistantDay) { Percent = 10.5, AssistantId = "Channel Z" }, Name));   // an id with no name shows as it is
    }

    [Fact]
    public void The_detail_line_is_the_canvas_s()
    {
        Assert.Equal("Standing · Channel A only", CostCapWords.Detail(new CostCap("1", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "a" }, Name));
        Assert.Equal("Standing · each assistant on its own", CostCapWords.Detail(new CostCap("1", CostCapScope.AssistantDay) { Percent = 15 }, Name));
        Assert.Equal("Standing · API keys, dollars", CostCapWords.Detail(new CostCap("1", CostCapScope.ApiDay) { Dollars = 20m }));
        Assert.Equal("This task", CostCapWords.Detail(new CostCap("1", CostCapScope.Task) { Percent = 5 }));
    }

    [Fact]
    public void The_cards_why_names_the_cap_that_sent_it()
    {
        Assert.Equal("Your cap for this task: at most 5% of any account", CostCapWords.Why(new CostCap("1", CostCapScope.Task) { Percent = 5 }));
        Assert.Equal("Standing cap for Channel A: at most 15% of any account a day", CostCapWords.Why(new CostCap("1", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "a" }, Name));
        Assert.Equal("Standing cap: at most 15% of any account a day for each assistant", CostCapWords.Why(new CostCap("1", CostCapScope.AssistantDay) { Percent = 15 }));
        Assert.Equal("Standing cap: at most $20 a day on API keys", CostCapWords.Why(new CostCap("1", CostCapScope.ApiDay) { Dollars = 20m }));
    }

    [Fact]
    public void Where_it_stands_says_the_window_what_is_used_and_the_cap_in_both_percent_and_dollars()
    {
        var task = new CostCap("1", CostCapScope.Task) { Percent = 5 };
        var reach = new CapReach("claude:20x", "Claude 20x", ReserveWindowKind.FiveHour, 4.2, 5, 1.3);
        Assert.Equal("Claude 20x's 5-hour window: 4.2% used by this task; your cap is 5%.", CostCapWords.Standing(task, reach));
        Assert.Equal("Claude 20x's 5-hour window: 4.2% used by this task; your cap is 5%. The next step needs about 1.3% more.", CostCapWords.Facts(task, reach));
        var day = new CostCap("2", CostCapScope.AssistantDay) { Percent = 15, AssistantId = "a" };
        Assert.Equal("Claude 20x's weekly window: 14.5% used today; your cap is 15%.",
            CostCapWords.Standing(day, new CapReach("claude:20x", "Claude 20x", ReserveWindowKind.Weekly, 14.5, 15, 0)));
        Assert.Equal("Claude 20x's weekly window: 14.5% used today; your cap is 15%.",
            CostCapWords.Facts(day, new CapReach("claude:20x", "Claude 20x", ReserveWindowKind.Weekly, 14.5, 15, 0)));   // no next step: nothing more to say

        var money = new CostCap("3", CostCapScope.ApiDay) { Dollars = 2m };
        var dollars = new CapReach("openrouter:key", "OpenRouter", null, 1.8, 2, 0.4);
        Assert.Equal("Today's API spend is $1.80; your cap is $2.00.", CostCapWords.Standing(money, dollars));
        Assert.Equal("Today's API spend is $1.80; your cap is $2.00. The next step needs about $0.40 more.", CostCapWords.Facts(money, dollars));
        Assert.Equal("This task has spent $1.80; your cap is $2.00.", CostCapWords.Standing(new CostCap("4", CostCapScope.Task) { Dollars = 2m }, dollars));
        Assert.Equal("today's API spend", CostCapWords.Where(dollars, daily: true));
        Assert.Equal("this task's spend", CostCapWords.Where(dollars, daily: false));
        Assert.Equal("$12.5", CostCapWords.Money(12.5));
        Assert.Equal("$100", CostCapWords.Money(100));
    }

    [Fact]
    public void The_amount_is_what_the_cap_names_and_nothing_else()
    {
        Assert.Equal("5%", CostCapWords.Amount(new CostCap("1", CostCapScope.Task) { Percent = 5 }));
        Assert.Equal("$20", CostCapWords.Amount(new CostCap("1", CostCapScope.ApiDay) { Dollars = 20m }));
        Assert.Equal("5% and $2", CostCapWords.Amount(new CostCap("1", CostCapScope.Task) { Percent = 5, Dollars = 2m }));
        Assert.Equal("at most 5% of any account and $2", CostCapWords.Body(new CostCap("1", CostCapScope.Task) { Percent = 5, Dollars = 2m }));
    }
}
