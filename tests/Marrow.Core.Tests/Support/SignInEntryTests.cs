using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8 "Open at sign-in", unit U8 (ED-NEW-inst-u8-1). nth's per-user Run value
// on FakeUserRegistry: the command it holds (the launch target, quoted, with --at-sign-in), whose it is (a program inside the
// root), Windows' own on/off record in StartupApproved, turning it on and off, a foreign "nth" value left alone, and a refusing
// registry. Nothing here touches the real HKCU.
public sealed class SignInEntryTests
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private const string Current = Root + @"\current";
    private const string CurrentExe = Current + @"\nth.exe";
    private const string Stub = Root + @"\nth.exe";
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string OursCommand = "\"" + CurrentExe + "\" --at-sign-in";
    private static readonly LaunchTarget Launch = new(CurrentExe, "", Current, CurrentExe, 0, FromStart: true);

    private static FakeUserRegistry Profile()
    {
        var registry = new FakeUserRegistry();
        registry.SeedKey(Run);
        return registry;
    }

    // ---- the command and the argument ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_command_is_the_quoted_launch_target_then_at_sign_in()
    {
        Assert.Equal("\"" + CurrentExe + "\" --at-sign-in", SignInEntry.Command(Launch));
        Assert.Equal("--at-sign-in", SignInEntry.Argument);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_command_keeps_the_launch_targets_own_arguments_before_at_sign_in()
    {
        var launch = Launch with { Arguments = "--from-start " };

        Assert.Equal("\"" + CurrentExe + "\" --from-start --at-sign-in", SignInEntry.Command(launch));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_target_is_Starts_or_current_nth_exe_and_never_the_root_stub()
    {
        var shell = new FakeShellLinks();
        shell.AddFile(CurrentExe);
        shell.AddFile(Stub); // the stub exists, and must still not be chosen (VELOPACK_CHECK.md check 2)
        var shortcut = new DesktopShortcut(shell, Root);

        Assert.Equal(OursCommand, SignInEntry.Command(shortcut.CurrentLaunchTarget())); // Start missing

        shell.Seed(FakeShellLinks.StartLink, new ShellLink(CurrentExe, "", Current, CurrentExe, 0));
        Assert.Equal(OursCommand, SignInEntry.Command(shortcut.CurrentLaunchTarget())); // Start present
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("--at-sign-in", true)]
    [InlineData("--AT-SIGN-IN", true)]
    [InlineData("--silent", false)]
    [InlineData("--at-sign-in-now", false)]
    [InlineData("at-sign-in", false)]
    public void An_at_sign_in_launch_is_recognised_by_its_argument(string argument, bool expected)
    {
        Assert.Equal(expected, SignInEntry.IsAtSignInLaunch([argument]));
        Assert.Equal(expected, SignInEntry.IsAtSignInLaunch(["--other", argument, "last"]));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void No_arguments_is_not_an_at_sign_in_launch()
    {
        Assert.False(SignInEntry.IsAtSignInLaunch([]));
    }

    // ---- reading ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void No_key_and_no_value_read_as_off()
    {
        Assert.Equal(new SignInState(SignInStatus.Off), SignInEntry.Read(new FakeUserRegistry(), Root));
        Assert.Equal(new SignInState(SignInStatus.Off), SignInEntry.Read(Profile(), Root));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_value_that_starts_a_program_in_the_root_reads_as_on()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", OursCommand);

        var state = SignInEntry.Read(registry, Root);

        Assert.Equal(new SignInState(SignInStatus.On, OursCommand), state);
        Assert.True(state.IsOn);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"C:\Old\nth\nth.exe --at-sign-in")]
    [InlineData("\"C:\\Users\\Ana\\Dev\\nth\\bin\\nth.exe\"")]
    [InlineData("")]
    public void A_value_that_starts_something_outside_the_root_reads_as_foreign(string command)
    {
        var registry = Profile();
        registry.Seed(Run, "nth", command);

        var state = SignInEntry.Read(registry, Root);

        Assert.Equal(SignInStatus.Foreign, state.Status);
        Assert.False(state.IsOn);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_value_that_is_not_a_command_reads_as_foreign()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", 1);

        Assert.Equal(SignInStatus.Foreign, SignInEntry.Read(registry, Root).Status);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, SignInStatus.DisabledInWindows)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, SignInStatus.DisabledInWindows)]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, SignInStatus.On)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, SignInStatus.On)]
    [InlineData(new byte[0], SignInStatus.On)]
    public void Windows_turning_the_entry_off_in_Startup_apps_reads_as_not_on(byte[] approved, SignInStatus expected)
    {
        var registry = Profile();
        registry.Seed(Run, "nth", OursCommand);
        registry.Seed(Approved, "nth", approved);

        var state = SignInEntry.Read(registry, Root);

        Assert.Equal(expected, state.Status);
        Assert.Equal(expected == SignInStatus.On, state.IsOn);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Windows_turning_off_another_entry_does_not_matter()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", OursCommand);
        registry.Seed(Approved, "OneDrive", new byte[] { 0x03, 0, 0, 0 });

        Assert.Equal(SignInStatus.On, SignInEntry.Read(registry, Root).Status);
    }

    // ---- turning it on ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_on_writes_the_run_value_once_and_a_repeat_writes_nothing()
    {
        var registry = Profile();

        var first = SignInEntry.Enable(registry, Root, Launch);
        var writes = registry.Writes;
        var second = SignInEntry.Enable(registry, Root, Launch);

        Assert.Equal(new SignInChangeResult(SignInChangeOutcome.Changed), first);
        Assert.Equal(OursCommand, registry.GetValue(Run, "nth"));
        Assert.Equal(new SignInChangeResult(SignInChangeOutcome.Unchanged), second);
        Assert.Equal(writes, registry.Writes);
        Assert.Equal(1, writes);
        Assert.True(SignInEntry.Read(registry, Root).IsOn);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_on_creates_the_run_key_when_the_profile_has_none()
    {
        var registry = new FakeUserRegistry();

        var result = SignInEntry.Enable(registry, Root, Launch);

        Assert.Equal(SignInChangeOutcome.Changed, result.Outcome);
        Assert.True(registry.KeyExists(Run));
        Assert.Equal(OursCommand, registry.GetValue(Run, "nth"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_on_rewrites_a_stale_command_of_nths_own()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", "\"" + Root + "\\current\\Marrow.App.exe\"");

        var result = SignInEntry.Enable(registry, Root, Launch);

        Assert.Equal(SignInChangeOutcome.Changed, result.Outcome);
        Assert.Equal(OursCommand, registry.GetValue(Run, "nth"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_on_clears_the_flag_Windows_keeps_when_the_user_turned_it_off()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", OursCommand);
        registry.Seed(Approved, "nth", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        registry.Seed(Approved, "OneDrive", new byte[] { 0x03, 0, 0, 0 });

        var result = SignInEntry.Enable(registry, Root, Launch);

        Assert.Equal(SignInChangeOutcome.Changed, result.Outcome);
        Assert.Null(registry.GetValue(Approved, "nth"));
        Assert.NotNull(registry.GetValue(Approved, "OneDrive")); // another entry's record is not ours to touch
        Assert.True(SignInEntry.Read(registry, Root).IsOn);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_on_never_overwrites_a_value_named_nth_that_is_not_nths()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", @"C:\Old\nth\nth.exe");
        registry.Seed(Run, "Other", @"C:\Other\other.exe");
        var writes = registry.Writes;

        var result = SignInEntry.Enable(registry, Root, Launch);

        Assert.Equal(new SignInChangeResult(SignInChangeOutcome.Foreign), result);
        Assert.Equal(@"C:\Old\nth\nth.exe", registry.GetValue(Run, "nth"));
        Assert.Equal(writes, registry.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Other_run_values_are_never_touched_by_turning_on_or_off()
    {
        var registry = Profile();
        registry.Seed(Run, "OneDrive", @"""C:\OneDrive.exe"" /background");

        SignInEntry.Enable(registry, Root, Launch);
        SignInEntry.Disable(registry, Root);

        Assert.Equal(@"""C:\OneDrive.exe"" /background", registry.GetValue(Run, "OneDrive"));
        Assert.Equal(1, registry.ValueCount(Run));
    }

    // ---- turning it off ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_off_deletes_the_run_value_and_Windows_flag_for_it()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", OursCommand);
        registry.Seed(Approved, "nth", new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        var result = SignInEntry.Disable(registry, Root);

        Assert.Equal(new SignInChangeResult(SignInChangeOutcome.Changed), result);
        Assert.Null(registry.GetValue(Run, "nth"));
        Assert.Null(registry.GetValue(Approved, "nth"));
        Assert.Equal(SignInStatus.Off, SignInEntry.Read(registry, Root).Status);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_off_when_Windows_had_turned_it_off_still_cleans_up()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", OursCommand);
        registry.Seed(Approved, "nth", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        var result = SignInEntry.Disable(registry, Root);

        Assert.Equal(SignInChangeOutcome.Changed, result.Outcome);
        Assert.Null(registry.GetValue(Run, "nth"));
        Assert.Null(registry.GetValue(Approved, "nth"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_off_with_nothing_there_writes_nothing()
    {
        var registry = Profile();

        var result = SignInEntry.Disable(registry, Root);

        Assert.Equal(new SignInChangeResult(SignInChangeOutcome.Unchanged), result);
        Assert.Equal(0, registry.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Turning_off_never_removes_a_value_named_nth_that_is_not_nths()
    {
        var registry = Profile();
        registry.Seed(Run, "nth", @"C:\Old\nth\nth.exe");

        var result = SignInEntry.Disable(registry, Root);

        Assert.Equal(new SignInChangeResult(SignInChangeOutcome.Foreign), result);
        Assert.Equal(@"C:\Old\nth\nth.exe", registry.GetValue(Run, "nth"));
        Assert.Equal(0, registry.Writes);
    }

    // ---- a registry that refuses ----

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_registry_that_refuses_gives_failed_with_its_HRESULT_and_never_throws()
    {
        const int accessDenied = unchecked((int)0x80070005);
        var registry = new RefusingRegistry(Profile(), new UnauthorizedAccessException("denied") { HResult = accessDenied });

        var on = SignInEntry.Enable(registry, Root, Launch);
        var off = SignInEntry.Disable(registry.Seeded(Run, "nth", OursCommand), Root);

        Assert.Equal(new SignInChangeResult(SignInChangeOutcome.Failed, accessDenied), on);
        Assert.Equal(new SignInChangeResult(SignInChangeOutcome.Failed, accessDenied), off);
    }

    // a registry that throws on every write, as a locked-down profile does
    private sealed class RefusingRegistry(FakeUserRegistry inner, Exception error) : IUserRegistry
    {
        public RefusingRegistry Seeded(string subKey, string name, object value)
        {
            inner.Seed(subKey, name, value);
            return this;
        }

        public bool KeyExists(string subKey) => inner.KeyExists(subKey);

        public void CreateKey(string subKey) => throw error;

        public object? GetValue(string subKey, string name) => inner.GetValue(subKey, name);

        public void SetString(string subKey, string name, string value) => throw error;

        public void SetDword(string subKey, string name, int value) => throw error;

        public void DeleteValue(string subKey, string name) => throw error;
    }
}
