using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Shared;

namespace Marrow.Core.Tests.Port.MultiAccount.Thread;

// MA-10 (step 3b, job 0; checklist 4 and 9): the account's config line is "<model> · <effort>", and Fast is only the yellow bolt, never the word.
[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class AccountConfigTextTests
{
    private static readonly IReadOnlyList<ModelOption> Models =
    [
        new ModelOption("opus", "Opus 5.5", efforts: new ValueList<string>(["low", "medium", "high", "xhigh"]), defaultEffort: "medium", isDefault: true, fastTier: "fast"),
        new ModelOption("sonnet", "Sonnet 5.5", efforts: new ValueList<string>(["low", "medium"]), defaultEffort: "low"),
        new ModelOption("haiku", "Haiku 5", efforts: ValueList<string>.Empty),
    ];

    [Fact]
    public void The_line_is_the_model_a_middle_dot_and_the_efforts_title()
    {
        var line = AccountConfigText.Of("Opus 5.5", "high", false);
        Assert.Equal("Opus 5.5 · High", line.Text);
        Assert.False(line.Bolt);
    }

    [Fact]
    public void Fast_adds_the_bolt_and_nothing_to_the_words()
    {
        var plain = AccountConfigText.Of("Opus 5.5", "high", false);
        var fast = AccountConfigText.Of("Opus 5.5", "high", true);
        Assert.True(fast.Bolt);
        Assert.Equal(plain.Text, fast.Text);
        Assert.DoesNotContain("Fast", fast.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("xhigh", "Extra High")]
    [InlineData("extra-high", "Extra High")]
    [InlineData("medium", "Medium")]
    public void The_effort_is_titled_as_the_composers_chip_titles_it(string effort, string title) =>
        Assert.Equal("Opus 5.5 · " + title, AccountConfigText.Of("Opus 5.5", effort, false).Text);

    [Fact]
    public void A_model_without_efforts_is_just_its_name()
    {
        Assert.Equal("Haiku 5", AccountConfigText.Of("Haiku 5", null, false).Text);
        Assert.Equal("Haiku 5", AccountConfigText.Of("Haiku 5", "", false).Text);
    }

    [Fact]
    public void The_accounts_defaults_are_named_from_the_services_models()
    {
        var line = AccountConfigText.Of(new AccountDefaults { Model = "opus", Effort = "high", FastMode = true }, Models);
        Assert.Equal(("Opus 5.5 · High", true), (line.Text, line.Bolt));
    }

    [Fact]
    public void Defaults_that_say_nothing_fall_back_to_the_services_default_model_and_its_default_effort()
    {
        var line = AccountConfigText.Of(new AccountDefaults(), Models);
        Assert.Equal(("Opus 5.5 · Medium", false), (line.Text, line.Bolt));
    }

    [Fact]
    public void A_model_that_has_no_fast_tier_never_shows_the_bolt_even_if_the_flag_is_set()
    {
        var line = AccountConfigText.Of(new AccountDefaults { Model = "sonnet", FastMode = true }, Models);
        Assert.Equal(("Sonnet 5.5 · Low", false), (line.Text, line.Bolt));
    }

    [Fact]
    public void A_model_the_service_no_longer_lists_keeps_its_id_and_the_flag_as_set()
    {
        var line = AccountConfigText.Of(new AccountDefaults { Model = "retired-1", Effort = "high", FastMode = true }, Models);
        Assert.Equal(("retired-1 · High", true), (line.Text, line.Bolt));
    }

    [Fact]
    public void With_no_models_and_no_defaults_the_line_is_empty()
    {
        var line = AccountConfigText.Of(new AccountDefaults(), []);
        Assert.Equal(("", false), (line.Text, line.Bolt));
    }
}
