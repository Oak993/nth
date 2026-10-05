// SK-11f / DL-11 (skills-ONDEMAND-PLAN.md 3.6 and 4.2, ED-309): the environment run_command's child gets carries addons\bin by the PATH rule, and nothing else
// about the call (shell, argv, folder, time and output limits, every other variable) is different from what the Mac's code passes. Observed through
// ShellObserver (ED-207), so no command needs to run.
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Shims;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-11f")]
public sealed class NativeProviderCommandEnvironmentTests
{
    private const string Json = """{"command":"exit 0"}""";

    private static async Task<ShellInvocation> Observe(OpenAICompatibleSession session)
    {
        ShellInvocation? seen = null;
        using (ShellObserver.Observe(call => seen = call))
            await session.ExecuteToolForTests(new OpenAICompatibleSession.PendingToolCall("call-1", "run_command", Json));
        return seen ?? throw new InvalidOperationException("run_command did not reach Shell.Run.");
    }

    [Fact, Trait("Decision", "ED-309"), Trait("Rule", "R-TEST-9")]
    public async Task The_child_gets_addons_bin_on_PATH_and_every_other_part_of_the_call_is_the_Macs()
    {
        await using var rig = NativeSessionRig.Create([]);
        var call = await Observe(rig.Session);

        Assert.Equal(WindowsShellForCommands.Resolve(), call.Executable);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command", "exit 0"], call.Arguments);
        Assert.Equal(rig.Session.Configuration.WorkingDirectory.Path, call.Directory);
        Assert.Equal(120, call.Timeout);
        Assert.Equal(24_000, call.OutputLimit);
        Assert.Null(call.Input);

        var login = LoginEnvironment.Current;
        var child = call.Environment!;
        var bin = Path.Combine(rig.StorageRoot, "addons", "bin");
        Assert.Equal(ShimPath.Insert(login.GetValueOrDefault("PATH"), bin, Path.PathSeparator), child["PATH"]);
        Assert.Equal(login.Where(p => p.Key != "PATH").OrderBy(p => p.Key, StringComparer.Ordinal), child.Where(p => p.Key != "PATH").OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(login.Count, child.Count);
        Assert.True(Directory.Exists(bin));
    }

    [Fact, Trait("Rule", "R-TEST-10")]
    public async Task With_no_data_root_bound_the_child_gets_exactly_the_environment_it_always_got()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSession(NativeSessionRig.MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), NativeSessionRig.MakeSessionConfiguration());
        var call = await Observe(session);
        Assert.Same(LoginEnvironment.Current, call.Environment);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command", "exit 0"], call.Arguments);
    }
}
