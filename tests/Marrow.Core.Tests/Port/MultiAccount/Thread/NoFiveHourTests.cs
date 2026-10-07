using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Core.Port.MultiAccount.Shared;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount.Thread;

// MA-10 (step 3b, job 0; OD-0115, checklist 4 and 9): "No 5-hour limit" is shown only when the service reported it. A plan's name never decides it.
[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class NoFiveHourTests
{
    [Fact]
    public void The_cell_says_the_pages_words_when_the_usage_line_flags_it()
    {
        Assert.Equal("No 5-hour limit", NoFiveHourText.For(new AccountUsageLine(null, null, 0.2, null, true, false)));
        Assert.Equal(MultiAccountWords.NoFiveHour, NoFiveHourText.For(new AccountUsageLine(null, null, 0.2, null, true, false)));
    }

    [Fact]
    public void Without_the_flag_there_is_no_cell_whatever_else_the_line_says()
    {
        Assert.Null(NoFiveHourText.For(null));
        Assert.Null(NoFiveHourText.For(new AccountUsageLine(0.4, null, 0.2, null, false, false)));
        Assert.Null(NoFiveHourText.For(new AccountUsageLine(null, null, null, null, false, false)));
        Assert.Null(NoFiveHourText.For(new AccountUsageLine(null, null, null, null, false, true))); // "no limit" at all is the local words, not this
    }

    [Theory]
    [InlineData("Free")]
    [InlineData("Pro")]
    [InlineData("Max")]
    [InlineData("Team")]
    [InlineData("Plus")]
    [InlineData(null)]
    public void A_plans_name_never_makes_the_cell_appear_or_go(string? plan)
    {
        var rig = new AccountsRig();
        var account = rig.Connect(ProviderKind.Claude, plan);
        var port = new FakeThreadAccountPort(rig, ProviderKind.Claude);
        Assert.False(Assert.Single(port.Accounts).NoFiveHour);
        rig.Usage[account.Id] = new AccountUsageLine(null, null, 0.1, null, true, false);
        port.Refresh();
        Assert.True(Assert.Single(port.Accounts).NoFiveHour);
        rig.Usage[account.Id] = new AccountUsageLine(0.5, null, 0.1, null, false, false);
        port.Refresh();
        Assert.False(Assert.Single(port.Accounts).NoFiveHour);
    }
}
