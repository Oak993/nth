using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-13 (DESIGN.md rev 5, 11.3; ED-NEW-dextra-as13-1): the owner's words become a rule, or one short question, or a reason
// they could not be read. A standing rule is of the whole window; a task keeps what is left; "Keep 30% on every account, or only
// on Claude 20x?" is asked once.
public sealed class ReserveRuleTextTests
{
    private static readonly string[] Names = ["Codex A", "Claude B", "Claude 20x", "OpenRouter key"];

    private static ReserveRule Made(string text, ReserveLifetime lifetime = ReserveLifetime.Standing)
    {
        var reading = ReserveRuleText.Read(text, lifetime, Names);
        Assert.Null(reading.Problem);
        Assert.Null(reading.Question);
        return Assert.IsType<ReserveRule>(reading.Rule);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    public void A_standing_rule_that_names_its_accounts_is_made_of_the_whole_window()
    {
        var rule = Made("Always keep 30% on Claude 20x");
        Assert.Equal(ReserveLifetime.Standing, rule.Lifetime);
        Assert.Equal(30, rule.KeepPercent);
        Assert.Equal(ReserveBasis.WholeWindow, rule.Basis);
        Assert.Equal(ReserveScopeKind.Accounts, rule.Scope.Kind);
        Assert.Equal(["Claude 20x"], rule.Scope.AccountNames);
        Assert.Equal(ReserveWindows.Both, rule.Windows);
        Assert.Equal("Always keep 30% on Claude 20x", ReserveWords.Title(rule));

        Assert.Equal(["Codex A"], Made("always keep 25% on codex a").Scope.AccountNames);        // spelt as the owner named the account
        Assert.Equal(12.5, Made("keep 12.5 percent on Codex A").KeepPercent);
        Assert.Equal(["Claude 20x", "Codex A"], Made("keep 20% on Claude 20x and Codex A").Scope.AccountNames.Order());
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    public void The_longest_account_name_wins_so_Claude_20x_is_not_read_as_Claude_B_or_Claude()
    {
        var names = new[] { "Claude", "Claude B", "Claude 20x" };
        var reading = ReserveRuleText.Read("keep 40% on Claude 20x", ReserveLifetime.Standing, names);
        Assert.Equal(["Claude 20x"], reading.Rule!.Scope.AccountNames);
        reading = ReserveRuleText.Read("keep 40% on Claude B", ReserveLifetime.Standing, names);
        Assert.Equal(["Claude B"], reading.Rule!.Scope.AccountNames);
        // A name inside a longer word is not the name.
        reading = ReserveRuleText.Read("keep 40% on Codex Aardvark", ReserveLifetime.Standing, ["Codex A", "Codex B"]);
        Assert.Null(reading.Rule?.Scope.AccountNames.FirstOrDefault());
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    public void A_task_keeps_what_is_left_unless_it_says_the_whole_window_and_a_standing_rule_never_does()
    {
        Assert.Equal(ReserveBasis.WhatIsLeft, Made("Keep 30% of what's left on every account", ReserveLifetime.Task).Basis);
        Assert.Equal(ReserveBasis.WhatIsLeft, Made("keep 30% on every account", ReserveLifetime.Task).Basis);
        Assert.Equal(ReserveBasis.WholeWindow, Made("keep 30% of the whole window on every account", ReserveLifetime.Task).Basis);
        Assert.Equal(ReserveBasis.WholeWindow, Made("keep 30% of what's left on Claude 20x").Basis);   // standing: the whole window, whatever it says
        var task = Made("Keep 30% of what's left on every account", ReserveLifetime.Task);
        Assert.Equal(ReserveScopeKind.EveryAccount, task.Scope.Kind);
        Assert.Equal(ReserveLifetime.Task, task.Lifetime);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    public void A_rule_that_does_not_say_which_accounts_gets_exactly_one_short_question()
    {
        var reading = ReserveRuleText.Read("Keep 30% of what's left", ReserveLifetime.Task, Names, focus: "Claude 20x");
        var question = Assert.IsType<ReserveRuleQuestion>(reading.Question);
        Assert.Null(reading.Rule);
        Assert.Equal("Keep 30% on every account, or only on Claude 20x?", question.Text);
        Assert.Equal("Claude 20x", question.Only);
        Assert.Equal(ReserveScopeKind.EveryAccount, question.Everywhere.Scope.Kind);
        Assert.Equal(["Claude 20x"], question.OnlyOne.Scope.AccountNames);
        Assert.Equal(ReserveBasis.WhatIsLeft, question.OnlyOne.Basis);
        Assert.Equal(30, question.Everywhere.KeepPercent);

        // Without a focus it asks about the first account; with one account, or "every account" said, it asks nothing.
        Assert.Equal("Keep 25% on every account, or only on Codex A?", ReserveRuleText.Read("keep 25%", ReserveLifetime.Standing, Names).Question!.Text);
        Assert.Equal("Keep 25% on every account, or only on Claude 20x?", ReserveRuleText.Read("keep 25%", ReserveLifetime.Standing, Names, "claude 20X").Question!.Text);
        Assert.NotNull(ReserveRuleText.Read("keep 25%", ReserveLifetime.Standing, ["Codex A"]).Rule);
        Assert.NotNull(ReserveRuleText.Read("keep 25% on all accounts", ReserveLifetime.Standing, Names).Rule);
        Assert.NotNull(ReserveRuleText.Read("keep 25% everywhere", ReserveLifetime.Standing, Names).Rule);
        Assert.NotNull(ReserveRuleText.Read("keep 25%", ReserveLifetime.Standing, []).Rule);
    }

    [Fact]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    public void An_API_key_s_reserve_is_in_dollars_a_day_or_a_month()
    {
        var day = Made("Keep $5 a day on the OpenRouter key");
        Assert.Equal(5m, day.KeepDollars);
        Assert.Equal(ReserveWindows.Day, day.Windows);
        Assert.Equal(["OpenRouter key"], day.Scope.AccountNames);
        Assert.Equal(ReserveLifetime.Standing, day.Lifetime);
        Assert.Equal("Keep $5 a day on OpenRouter key", ReserveWords.Title(day));

        var month = Made("keep $12.50 a month on OpenRouter key");
        Assert.Equal(12.5m, month.KeepDollars);
        Assert.Equal(ReserveWindows.Month, month.Windows);
        Assert.Equal(ReserveWindows.Month, Made("keep $20 monthly on the OpenRouter key", ReserveLifetime.Task).Windows);
    }

    [Theory]
    [Trait("Feature", "AS-13")]
    [Trait("Decision", "ED-NEW-dextra-as13-1")]
    [InlineData("", "Say a rule like: always keep 25% on Codex A.")]
    [InlineData("   ", "Say a rule like: always keep 25% on Codex A.")]
    [InlineData("always keep plenty on Codex A", "Say how much to keep, for example: always keep 25% on Codex A.")]
    [InlineData("keep 150% on Codex A", "A reserve is between 0% and 100%.")]
    [InlineData("keep $5 a day", "Say which key it is for, for example: keep $5 a day on the OpenRouter key.")]
    [InlineData("keep $0 a day on the OpenRouter key", "Say how many dollars to keep, for example: keep $5 a day on the OpenRouter key.")]
    [InlineData("Channel A: at most 15% of any account a day", "That is a cap on spending, not a reserve to keep. Say a rule like: always keep 25% on Codex A.")]
    public void Words_that_are_not_a_rule_say_why(string text, string problem)
    {
        var reading = ReserveRuleText.Read(text, ReserveLifetime.Standing, Names);
        Assert.Null(reading.Rule);
        Assert.Null(reading.Question);
        Assert.Equal(problem, reading.Problem);
        Assert.Null(ReserveRuleText.Read(null, ReserveLifetime.Standing, Names).Rule);
    }
}
