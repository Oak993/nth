using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.4 "Start with Windows (hidden)"; ED-NEW-dextra-as10-14, -15): the second switch of the first
// duty's card. It is off until the owner taps it: nothing is written to the Run key until then. On, it is one value that starts nth
// with --hidden; off, the value goes. The power plan's wake-timer setting is read from powercfg's words.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-14")]
public sealed class StartWithWindowsTests
{
    private const string Exe = @"C:\Users\me\AppData\Local\nth\current\nth.exe";

    [Fact]
    public void It_is_off_until_the_owner_taps_it_and_nothing_is_written_before()
    {
        var key = new FakeRunKey();
        var start = new StartWithWindows(key);
        Assert.False(start.IsOn(Exe));
        Assert.Empty(key.Values);
    }

    [Fact]
    public void Turned_on_it_is_one_value_that_starts_nth_hidden_and_turned_off_the_value_goes()
    {
        var key = new FakeRunKey();
        var start = new StartWithWindows(key);
        Assert.Null(start.Turn(true, Exe));
        Assert.Equal("\"" + Exe + "\" --hidden", Assert.Single(key.Values).Value);
        Assert.True(start.IsOn(Exe));
        Assert.Null(start.Turn(false, Exe));
        Assert.Empty(key.Values);
        Assert.False(start.IsOn(Exe));
    }

    [Fact]
    public void An_entry_for_another_install_reads_as_off_so_the_owner_can_turn_it_on_again()
    {
        var key = new FakeRunKey();
        key.Values["nth"] = "\"D:\\old\\nth.exe\" --hidden";
        Assert.False(new StartWithWindows(key).IsOn(Exe));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nth.exe")]
    [InlineData("C:\\a\"b\\nth.exe")]
    public void A_path_windows_cannot_start_is_refused_and_nothing_is_written(string exe)
    {
        var key = new FakeRunKey();
        Assert.NotNull(new StartWithWindows(key).Turn(true, exe));
        Assert.Empty(key.Values);
    }

    [Fact]
    public void A_start_up_list_windows_will_not_change_gives_a_reason_not_an_exception()
    {
        var key = new FakeRunKey { Throw = new UnauthorizedAccessException("Access to the registry key is denied.") };
        Assert.Contains("would not change the start-up list", new StartWithWindows(key).Turn(true, Exe), StringComparison.Ordinal);
        Assert.Contains("would not change", new StartWithWindows(key).Turn(false, Exe), StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_for_an_install_quotes_the_path()
    {
        Assert.Equal("\"C:\\Program Files\\nth\\nth.exe\" --hidden", StartWithWindows.CommandFor(@"C:\Program Files\nth\nth.exe"));
    }

    [Fact]
    public void Wake_timers_on_in_both_plans_say_nothing_to_the_card()
    {
        var state = WakeTimerPolicy.Parse(Powercfg(ac: 1, dc: 1));
        Assert.True(state.Allowed);
        Assert.Null(state.Words);
    }

    [Fact]
    public void Wake_timers_off_say_so_with_the_place_to_turn_them_on()
    {
        var state = WakeTimerPolicy.Parse(Powercfg(ac: 0, dc: 0));
        Assert.False(state.Allowed);
        Assert.Contains("will not wake for this duty", state.Words, StringComparison.Ordinal);
        Assert.Contains("Allow wake timers", state.Words, StringComparison.Ordinal);
    }

    [Fact]
    public void Wake_timers_off_on_battery_or_plugged_in_only_say_when_the_pc_wakes()
    {
        Assert.Contains("only when plugged in", WakeTimerPolicy.Parse(Powercfg(ac: 1, dc: 0)).Words, StringComparison.Ordinal);
        Assert.Contains("only on battery", WakeTimerPolicy.Parse(Powercfg(ac: 0, dc: 1)).Words, StringComparison.Ordinal);
        Assert.True(WakeTimerPolicy.Parse(Powercfg(ac: 2, dc: 2)).Allowed); // "important wake timers only" still counts Task Scheduler's
    }

    [Theory]
    [InlineData("")]
    [InlineData("Power Scheme GUID: x\n  Subgroup GUID: y")]
    public void Words_with_no_setting_in_them_are_unknown_not_a_guess(string output) => Assert.Null(WakeTimerPolicy.Parse(output).Allowed);

    [Fact]
    public async Task The_setting_is_read_from_powercfg_with_an_argument_list()
    {
        var runner = new FakeProcessRunner((_, _) => new ProcessOutcome(0, Powercfg(ac: 0, dc: 0), ""));
        var state = await WakeTimerPolicy.Read(runner, @"C:\Windows\System32", CancellationToken.None);
        Assert.False(state.Allowed);
        var (file, arguments) = Assert.Single(runner.Runs);
        Assert.Equal(@"C:\Windows\System32\powercfg.exe", file);
        Assert.Equal(["/qh", "SCHEME_CURRENT", "SUB_SLEEP", "BD3B718A-0680-4D9D-8AB2-E1D2B4AC806D"], arguments);
    }

    [Fact]
    public async Task A_powercfg_that_fails_or_cannot_run_is_unknown()
    {
        Assert.Null((await WakeTimerPolicy.Read(new FakeProcessRunner((_, _) => new ProcessOutcome(1, "", "x")), null, CancellationToken.None)).Allowed);
        var cannot = new FakeProcessRunner((_, _) => new ProcessOutcome(0, "", "")) { Throw = new System.ComponentModel.Win32Exception("not found") };
        Assert.Null((await WakeTimerPolicy.Read(cannot, null, CancellationToken.None)).Allowed);
    }

    private static string Powercfg(int ac, int dc) =>
        "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)\r\n  Subgroup GUID: 238c9fa8-0aad-41ed-83f4-97be242c8f20  (Sleep)\r\n"
        + "    Power Setting GUID: bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d  (Allow wake timers)\r\n      Possible Setting Index: 000\r\n"
        + $"    Current AC Power Setting Index: 0x{ac:x8}\r\n    Current DC Power Setting Index: 0x{dc:x8}\r\n";
}
