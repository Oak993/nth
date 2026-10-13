using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4; ED-NEW-dextra-as10-14): nth's command line for the two background starts. --hidden loads
// without the window; --duty <id> loads hidden and names one duty; every other argument belongs to somebody else; a duty id that is
// not a plain token is refused, so a command line can name nothing but a duty.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-14")]
public sealed class StartupArgumentsTests
{
    [Fact]
    public void No_arguments_or_only_other_peoples_arguments_are_a_normal_start()
    {
        Assert.Equal(StartupMode.Normal, StartupArguments.Parse(null));
        Assert.Equal(StartupMode.Normal, StartupArguments.Parse([]));
        Assert.Equal(StartupMode.Normal, StartupArguments.Parse(["--performance-profile", "nth-toast:abc"]));
    }

    [Fact]
    public void Hidden_loads_without_the_window_and_runs_no_duty()
    {
        var mode = StartupArguments.Parse(["--hidden"]);
        Assert.True(mode.Hidden);
        Assert.Null(mode.DutyId);
        Assert.False(mode.ForOneDuty);
    }

    [Theory]
    [InlineData("--duty", "d0123456789ab")]
    [InlineData("--duty=d0123456789ab", null)]
    public void A_duty_start_is_hidden_and_names_the_duty(string first, string? second)
    {
        var mode = StartupArguments.Parse(second is null ? [first] : [first, second]);
        Assert.True(mode.Hidden);
        Assert.True(mode.ForOneDuty);
        Assert.Equal("d0123456789ab", mode.DutyId);
        Assert.Null(mode.Problem);
    }

    [Theory]
    [InlineData("--duty", "../x")]
    [InlineData("--duty", "Has Space")]
    [InlineData("--duty", "")]
    [InlineData("--duty=", null)]
    [InlineData("--duty", null)]
    [InlineData("--duty=a;b", null)]
    [InlineData("--duty", "d1 --other")]
    public void A_duty_id_that_is_not_a_plain_token_is_refused_and_names_no_duty(string first, string? second)
    {
        var mode = StartupArguments.Parse(second is null ? [first] : [first, second]);
        Assert.Null(mode.DutyId);
        Assert.NotNull(mode.Problem);
    }

    [Fact]
    public void The_first_good_duty_wins_and_other_arguments_are_left_alone()
    {
        var mode = StartupArguments.Parse(["--hidden", "--duty", "d1", "--duty", "d2", "nth-toast:x"]);
        Assert.Equal("d1", mode.DutyId);
        Assert.True(mode.Hidden);
    }
}
