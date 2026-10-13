using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 (step 7), 6 and 10, unit U0 (ED-NEW-inst-u0-1, amending ED-247's
// hand-over). Velopack's programs are started only through IVelopackRunner, with command lines decided in one place:
// Setup.exe always silent, with its log and the install root, and NTH_SETUP_OPTIONS pointing at the options file;
// uninstall through the root's Update.exe, silent. Nothing here starts a Velopack program.
public sealed class VelopackCommandTests
{
    private const string Profile = @"C:\Users\Ana-María Østergård-Fernández";
    private const string Engine = Profile + @"\AppData\Local\nth Setup\1.0.0\nth Setup Engine.exe";
    private const string Root = Profile + @"\AppData\Local\Programs\nth";
    private const string Log = Profile + @"\AppData\Local\nth Setup\1.0.0\setup.log";
    private const string Options = Profile + @"\AppData\Local\nth Setup\1.0.0\install-options.json";

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Setup_runs_silent_with_its_log_and_root_and_the_options_path_in_its_environment()
    {
        var command = VelopackCommand.Install(Engine, Root, Log, Options);

        Assert.Equal(VelopackCommandKind.Install, command.Kind);
        Assert.Equal(Engine, command.Executable);
        Assert.Equal(new[] { "--silent", "--log", Log, "--installto", Root }, command.Arguments);
        Assert.Equal(Profile + @"\AppData\Local\nth Setup\1.0.0", command.WorkingDirectory);
        Assert.Equal(Options, Assert.Single(command.Environment, e => e.Key == "NTH_SETUP_OPTIONS").Value);
        Assert.Single(command.Environment);
        Assert.Equal(InstallOptions.EnvironmentVariable, command.Environment.Keys.Single());
        Assert.Equal(Root, command.InstallRoot);
        Assert.Equal(Log, command.LogPath);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Uninstall_runs_the_roots_Update_exe_silently_in_the_root()
    {
        var command = VelopackCommand.Uninstall(Root);

        Assert.Equal(VelopackCommandKind.Uninstall, command.Kind);
        Assert.Equal(Root + @"\Update.exe", command.Executable);
        Assert.Equal(new[] { "--uninstall", "--silent" }, command.Arguments);
        Assert.Equal(Root, command.WorkingDirectory);
        Assert.Empty(command.Environment);
        Assert.Null(command.LogPath);
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData("nth Setup Engine.exe", Root, Log, Options)]
    [InlineData(Engine, @"Programs\nth", Log, Options)]
    [InlineData(Engine, Root, "setup.log", Options)]
    [InlineData(Engine, Root, Log, "install-options.json")]
    [InlineData(Engine, "", Log, Options)]
    public void Every_path_in_a_command_must_be_a_full_path(string engine, string root, string log, string options) =>
        Assert.Throws<ArgumentException>(() => VelopackCommand.Install(engine, root, log, options));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_uninstall_needs_a_full_root() =>
        Assert.Throws<ArgumentException>(() => VelopackCommand.Uninstall(@"Programs\nth"));
}
