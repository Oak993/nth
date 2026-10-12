using System.Reflection;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Velopack;

namespace Marrow.App.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers": nth's own start deletes %LOCALAPPDATA%\nth Setup when no setup is
// running; the uninstaller removes it too), unit U9 (ED-NEW-inst-u9-1). nth sweeps the setup's folders at its start, on the pool,
// only in a Velopack install and only while no setup or uninstaller is open; its before-uninstall hook does the same. Velopack's
// own dispatch is not run here (it needs an install). Temporary folders stand in for %LOCALAPPDATA% and %TEMP%.
public sealed class SetupLeftoversStartupTests : IDisposable
{
    private readonly string area = Path.Combine(Path.GetTempPath(), "nth-tests", "leftovers-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(area)) Directory.Delete(area, recursive: true);
    }

    private string Local => Path.Combine(area, "local");

    private string Temp => Path.Combine(area, "temp");

    private string Put(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    /// <summary>Velopack's layout: <c>&lt;root&gt;\Update.exe</c> and the app in <c>&lt;root&gt;\current</c>.</summary>
    private string VelopackCurrent()
    {
        var root = Path.Combine(area, "install");
        Put(Path.Combine(root, "Update.exe"));
        return Directory.CreateDirectory(Path.Combine(root, "current")).FullName + Path.DirectorySeparatorChar;
    }

    private (string Staged, string Library, string UninstallCopy, string Data) Leftovers() => (
        Put(Path.Combine(Local, "nth Setup", "1.0.0", "nth Setup Engine.exe")),
        Put(Path.Combine(Local, "nth Setup", "runtime", "4f9c2a", "libSkiaSharp.dll")),
        Put(Path.Combine(Temp, "nth Uninstall", "nth Uninstall.exe")),
        Put(Path.Combine(Local, "nth", "chats.db")));

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Nth_started_from_a_Velopack_install_removes_the_leftovers_when_no_setup_runs()
    {
        var (staged, library, copy, data) = Leftovers();
        var probe = new FakeSetupProbe(running: false);

        await SetupLeftoversStartup.SweepAtStart(VelopackCurrent(), Local, Temp, probe);

        Assert.False(File.Exists(staged));
        Assert.False(File.Exists(library));
        Assert.False(File.Exists(copy));
        Assert.False(Directory.Exists(Path.Combine(Local, "nth Setup")));
        Assert.True(File.Exists(data), "the data folder is never swept");
        Assert.Equal(1, probe.Asked);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Nth_leaves_everything_while_a_setup_or_the_uninstaller_is_open()
    {
        var (staged, library, copy, _) = Leftovers();
        var probe = new FakeSetupProbe(running: true);

        await SetupLeftoversStartup.SweepAtStart(VelopackCurrent(), Local, Temp, probe);

        Assert.All(new[] { staged, library, copy }, path => Assert.True(File.Exists(path)));
        Assert.Equal(1, probe.Asked);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_development_run_or_a_test_host_sweeps_nothing_and_does_not_look_for_a_setup()
    {
        var (staged, library, copy, _) = Leftovers();
        var probe = new FakeSetupProbe(running: false);
        var notVelopack = Directory.CreateDirectory(Path.Combine(area, "dev", "bin")).FullName;

        await SetupLeftoversStartup.SweepAtStart(notVelopack, Local, Temp, probe);
        await SetupLeftoversStartup.SweepAtStart(AppContext.BaseDirectory, Local, Temp, probe);

        Assert.All(new[] { staged, library, copy }, path => Assert.True(File.Exists(path)));
        Assert.Equal(0, probe.Asked);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_file_that_is_in_use_keeps_its_folder_and_never_faults_the_task()
    {
        var (staged, _, _, _) = Leftovers();
        using var running = new FileStream(staged, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await SetupLeftoversStartup.SweepAtStart(VelopackCurrent(), Local, Temp, new FakeSetupProbe(running: false));

        Assert.True(File.Exists(staged));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_folder_that_cannot_be_used_never_faults_the_task()
    {
        await SetupLeftoversStartup.SweepAtStart(VelopackCurrent(), "not a full path", Temp, new FakeSetupProbe(running: false));
        await SetupLeftoversStartup.SweepAtStart(VelopackCurrent(), "", Temp, new FakeSetupProbe(running: false));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_before_uninstall_hook_removes_the_leftovers_when_no_setup_runs_and_leaves_them_while_one_does()
    {
        var (staged, _, copy, data) = Leftovers();
        VelopackHook? registered = null;

        SetupLeftoversStartup.RegisterUninstall(hook => registered = hook, Local, Temp, new FakeSetupProbe(running: true));
        Assert.NotNull(registered);
        registered(SemanticVersion.Parse("1.0.0"));
        Assert.True(File.Exists(staged));

        SetupLeftoversStartup.RegisterUninstall(hook => registered = hook, Local, Temp, new FakeSetupProbe(running: false));
        registered(SemanticVersion.Parse("1.0.0"));

        Assert.False(File.Exists(staged));
        Assert.False(File.Exists(copy));
        Assert.True(File.Exists(data));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_before_uninstall_hook_never_throws_into_Velopack()
    {
        VelopackHook? registered = null;

        SetupLeftoversStartup.RegisterUninstall(hook => registered = hook, "not a full path", Temp, new FakeSetupProbe(running: false));

        registered!(SemanticVersion.Parse("1.0.0"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_hook_is_registered_before_Velopack_runs_it()
    {
        var calls = MethodCalls.Of(typeof(Program).GetMethod(nameof(Program.RunVelopackHook), BindingFlags.Static | BindingFlags.NonPublic)!);
        var names = calls.Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        var register = names.IndexOf("SetupLeftoversStartup.RegisterUninstall");
        var run = names.IndexOf("VelopackApp.Run");

        Assert.True(register >= 0, "RunVelopackHook never registers the setup's leftover sweep: " + string.Join(", ", names));
        Assert.True(run > register, "Run() must come after the registration: " + string.Join(", ", names));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_start_sweep_begins_once_the_main_window_is_set_up_and_never_from_Main()
    {
        var program = typeof(Program);
        var main = MethodCalls.Of(program.GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static)!).Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        Assert.DoesNotContain("SetupLeftoversStartup.SweepAtStart", main);
        var bodies = program.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Append(program)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetMethodBody() is not null)
            .Select(m => MethodCalls.Of(m).Select(c => c.DeclaringType!.Name + "." + c.Name).ToList()).ToList();

        var startup = Assert.Single(bodies, calls => calls.Contains("SetupLeftoversStartup.SweepAtStart"));

        Assert.True(startup.IndexOf("AppShell.FinishLaunching") < startup.IndexOf("SetupLeftoversStartup.SweepAtStart"), "after the main window is set up: " + string.Join(", ", startup));
    }
}
