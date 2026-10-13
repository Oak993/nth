using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Caps", 11.7; the approved canvas "Standing rules"; ED-NEW-dextra-as15-2): the owner says a cap in words
// ("Channel A: at most 15% of any account a day") and gets a cap, or one short reason why not. Reserves are the other way round and stay
// the reserve line's: "keep 30%" is never read as a cap.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-2")]
public sealed class CostCapTextTests
{
    private static readonly CapAssistant[] Assistants = [new("a", "Channel A"), new("b", "Channel B"), new("c", "Channel")];

    private static readonly string[] Keys = ["OpenRouter", "DeepSeek"];

    private static CostCapReading Read(string text, bool forTask = false) => CostCapText.Read(text, Assistants, Keys, forTask);

    [Fact]
    public void An_assistants_percent_a_day_is_a_cap_for_that_assistant()
    {
        var cap = Assert.IsType<CostCap>(Read("Channel A: at most 15% of any account a day").Cap);
        Assert.Equal(CostCapScope.AssistantDay, cap.Scope);
        Assert.Equal(15, cap.Percent);
        Assert.Equal("a", cap.AssistantId);
        Assert.Null(cap.Dollars);
    }

    [Fact]
    public void Without_a_name_the_cap_limits_each_assistant_on_its_own()
    {
        var cap = Assert.IsType<CostCap>(Read("at most 12.5 percent of any account per day").Cap);
        Assert.Equal(CostCapScope.AssistantDay, cap.Scope);
        Assert.Equal(12.5, cap.Percent);
        Assert.Null(cap.AssistantId);
    }

    [Fact]
    public void Dollars_a_day_with_no_assistant_are_the_global_cap_on_API_keys_and_may_name_the_keys()
    {
        var all = Assert.IsType<CostCap>(Read("at most $20 a day on API keys").Cap);
        Assert.Equal(CostCapScope.ApiDay, all.Scope);
        Assert.Equal(20m, all.Dollars);
        Assert.Empty(all.AccountNames);

        var named = Assert.IsType<CostCap>(Read("no more than $5.50 daily on the OpenRouter key").Cap);
        Assert.Equal(CostCapScope.ApiDay, named.Scope);
        Assert.Equal(5.5m, named.Dollars);
        Assert.Equal(["OpenRouter"], named.AccountNames);
    }

    [Fact]
    public void Dollars_for_a_named_assistant_are_that_assistants_cap_and_both_amounts_make_one_cap()
    {
        var money = Assert.IsType<CostCap>(Read("Channel B: at most $3 a day").Cap);
        Assert.Equal(CostCapScope.AssistantDay, money.Scope);
        Assert.Equal("b", money.AssistantId);
        Assert.Equal(3m, money.Dollars);
        Assert.Null(money.Percent);

        var both = Assert.IsType<CostCap>(Read("Channel A: at most 10% and $4 a day").Cap);
        Assert.Equal(10, both.Percent);
        Assert.Equal(4m, both.Dollars);
    }

    [Fact]
    public void The_longest_name_wins_and_a_name_inside_a_longer_word_is_not_the_name()
    {
        Assert.Equal("a", Read("Channel A: at most 15% a day").Cap!.AssistantId);          // not "Channel"
        Assert.Equal("c", Read("Channel: at most 15% a day").Cap!.AssistantId);
        Assert.Null(Read("Channels at most 15% a day").Cap!.AssistantId);                   // "Channels" is not "Channel"
    }

    [Fact]
    public void A_cap_for_one_task_is_set_where_the_task_starts_and_not_in_the_standing_list()
    {
        var problem = Read("at most 5% of any account for this task").Problem;
        Assert.Equal("A cap for one task is set where the task starts: Adjust on its plan.", problem);
        var task = Assert.IsType<CostCap>(Read("at most 5% of any account for this task", forTask: true).Cap);
        Assert.Equal(CostCapScope.Task, task.Scope);
        Assert.Equal(5, task.Percent);
        var money = Assert.IsType<CostCap>(Read("cap it at $2", forTask: true).Cap);
        Assert.Equal(2m, money.Dollars);
    }

    [Fact]
    public void Words_that_cannot_be_read_say_why_in_one_line()
    {
        Assert.Equal("Say a cap like: at most 15% of any account a day.", Read("   ").Problem);
        Assert.Equal("Say how much, for example: at most 15% of any account a day.", Read("at most a lot a day").Problem);
        Assert.Equal("Say when the cap applies, for example: at most 15% of any account a day.", Read("at most 15% of any account").Problem);
        Assert.Equal("A cap is more than 0% and at most 100%.", Read("at most 0% a day").Problem);
        Assert.Equal("A cap is more than 0% and at most 100%.", Read("at most 140% a day").Problem);
        Assert.Equal("Say how many dollars the cap is, for example: at most $20 a day on API keys.", Read("at most $0 a day").Problem);
        Assert.Null(Read("at most $0 a day").Cap);
    }

    [Fact]
    public void Only_words_about_a_cap_and_not_a_reserve_are_a_cap()
    {
        Assert.True(CostCapText.LooksLikeCap("Channel A: at most 15% of any account a day"));
        Assert.True(CostCapText.LooksLikeCap("no more than $20 a day"));
        Assert.True(CostCapText.LooksLikeCap("cap Channel A at 10% a day"));
        Assert.False(CostCapText.LooksLikeCap("always keep 25% on Codex A"));
        Assert.False(CostCapText.LooksLikeCap("keep at most 25% on Codex A"));                  // it says keep: a reserve
        Assert.False(CostCapText.LooksLikeCap("   "));
        Assert.False(CostCapText.LooksLikeCap(null));
    }
}
