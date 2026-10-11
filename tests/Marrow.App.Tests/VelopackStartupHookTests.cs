using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Marrow.Testing;
using Velopack;
using Velopack.Locators;

namespace Marrow.App.Tests;

// PORT-ONLY (no Swift twin): OD-0062 / ED-235. The Velopack hook is the first desktop work Program.Main does, and a run that
// Velopack did not install (a dev run, this test host) never calls Velopack at all: its Run() writes a log file under
// %LOCALAPPDATA% on every call. Program.Main cannot run in a test host (STAThread, the single-instance lock, the full
// Avalonia lifetime, the real %LOCALAPPDATA%\Marrow Code folder), so the order is proved from Main's own IL. The
// installed case (Update.exe running the app with --veloapp-install, -updated, -obsolete or -uninstall) cannot be tested
// here without installing the app onto this PC: Velopack's Run() exits the process for those arguments.
public sealed class VelopackStartupHookTests
{
    [Fact]
    [Trait("Decision", "ED-235")]
    public void Main_runs_the_Velopack_hook_before_any_desktop_work_after_the_private_MCP_dispatch()
    {
        var main = typeof(Program).GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static)!;
        var il = main.GetMethodBody()!.GetILAsByteArray()!;
        var calls = new List<MethodBase>();
        for (var at = 0; at < il.Length && calls.Count < 9;)
        {
            var code = il[at] == 0xFE ? Codes[(short)(0xFE00 | il[at + 1])] : Codes[il[at]];
            var operandAt = at + (il[at] == 0xFE ? 2 : 1);
            if (code == OpCodes.Call || code == OpCodes.Callvirt)
                calls.Add(main.Module.ResolveMethod(BitConverter.ToInt32(il, operandAt))!);
            at = operandAt + OperandSize(code, il, operandAt);
        }

        // SK-17 owner-authorized headless exception: this exact private dispatch may precede the desktop hook.
        // No settings, lock, toast, app lifetime or other calls can be inserted before the hook.
        Assert.Equal(new[] { "FirstOrDefault", "op_Equality", "OpenStandardInput", "OpenStandardOutput",
            "RunClient", "GetAwaiter", "GetResult", "get_BaseDirectory", nameof(Program.RunVelopackHook) },
            calls.Select(call => call.Name));
        Assert.Equal(typeof(Enumerable), calls[0].DeclaringType);
        Assert.Equal(typeof(string), calls[1].DeclaringType);
        Assert.Equal(typeof(Console), calls[2].DeclaringType);
        Assert.Equal(typeof(Console), calls[3].DeclaringType);
        Assert.Equal(typeof(Marrow.Services.Port.Skills.Providers.SkillsMcpBridge), calls[4].DeclaringType);
        Assert.Equal(typeof(AppContext), calls[7].DeclaringType);
        Assert.Equal(typeof(Program), calls[8].DeclaringType);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void Only_a_Velopack_install_runs_the_hook()
    {
        var root = Path.Combine(Path.GetTempPath(), "velopack-layout-" + Guid.NewGuid().ToString("N"));
        var current = Directory.CreateDirectory(Path.Combine(root, "current")).FullName;
        try
        {
            // a dev build or a test host: no Update.exe above the app's folder
            Assert.False(Program.InstalledByVelopack(current + Path.DirectorySeparatorChar));
            Assert.False(Program.InstalledByVelopack(current));
            Assert.False(Program.InstalledByVelopack(AppContext.BaseDirectory));
            // Velopack's layout: <root>\Update.exe and <root>\current\Marrow.App.exe
            File.WriteAllBytes(Path.Combine(root, "Update.exe"), []);
            Assert.True(Program.InstalledByVelopack(current + Path.DirectorySeparatorChar));
            Assert.True(Program.InstalledByVelopack(current));
            // Update.exe beside the app, not above it, is not that layout
            Assert.False(Program.InstalledByVelopack(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void A_run_that_Velopack_did_not_install_never_calls_Velopack()
    {
        // Velopack's Run() logs to %LOCALAPPDATA%\velopack\velopack.log on every call; the hook must not reach it here.
        var log = new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "velopack", "velopack.log"));
        var before = log.Exists ? (log.Length, log.LastWriteTimeUtc) : (-1L, default(DateTime));

        Program.RunVelopackHook(AppContext.BaseDirectory);

        log.Refresh();
        Assert.Equal(before, log.Exists ? (log.Length, log.LastWriteTimeUtc) : (-1L, default(DateTime)));
        Assert.False(VelopackLocator.IsCurrentSet);
    }

    // nth installer PLAN.md 8, 8a and 10, unit U5 (ED-NEW-inst-u5-1): the three Velopack hooks and nth's start-time self-heal.

    [Fact]
    [Trait("Decision", "ED-247")]
    public void RunVelopackHook_registers_nths_install_hooks_after_the_skills_hook_and_before_Velopack_runs()
    {
        var calls = MethodCalls.Of(typeof(Program).GetMethod(nameof(Program.RunVelopackHook), BindingFlags.Static | BindingFlags.NonPublic)!);
        var names = calls.Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        var skills = names.IndexOf("SkillsUninstallHook.Register");
        var nth = names.IndexOf("NthInstallHook.Register");
        var run = names.IndexOf("VelopackApp.Run");
        Assert.True(skills >= 0 && nth > skills && run > nth, string.Join(", ", names));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Each_Velopack_hook_runs_its_own_steps_on_the_fakes()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            const string Root = FakeShellLinks.DefaultRoot;
            var shell = new FakeShellLinks();
            shell.AddFile(Root + @"\current\nth.exe");
            var registry = new FakeUserRegistry();
            registry.WriteVelopackUninstallKey(Root);
            registry.Seed(UninstallEntry.RunKeyPath, "nth", "\"" + Root + "\\nth.exe\" --at-sign-in");
            InstallOptions.ForInstall(desktopShortcut: true).Write(InstallOptions.DefaultOptionsPath(temp, "1.0.1"));
            VelopackHook? install = null, update = null, uninstall = null;
            var reports = new List<InstallHookReport>();
            NthInstallHook.Register(hook => install = hook, hook => update = hook, hook => uninstall = hook,
                version => new InstallHookContext(Root, temp, null, version.ToFullString()), () => shell, () => registry, reports.Add);
            static string[] Names(InstallHookReport report) => [.. report.Steps.Select(step => step[..step.IndexOf(':', StringComparison.Ordinal)])];

            install!(SemanticVersion.Parse("1.0.1"));
            Assert.Equal(new[] { "start-move", "start-restamp", "desktop", "uninstaller-copy" }, Names(reports[^1]));
            Assert.Equal(new InstallResult(DesktopShortcutOutcome.Created), reports[^1].Result);
            Assert.True(shell.Peek(FakeShellLinks.DesktopLink)!.IsMadeByNth);

            update!(SemanticVersion.Parse("1.0.2"));
            Assert.Equal(new[] { "start-move", "start-restamp", "desktop-repair", "uninstaller-copy", "uninstall-key" }, Names(reports[^1]));

            uninstall!(SemanticVersion.Parse("1.0.2"));
            Assert.Equal(new[] { "desktop-remove: 1 changed of 1", "sign-in: removed" }, reports[^1].Steps);
            Assert.Null(shell.Peek(FakeShellLinks.DesktopLink));
            Assert.Equal(3, reports.Count);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_hook_never_throws_into_Velopack_and_always_disposes_its_shell()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var version = SemanticVersion.Parse("1.0.1");
            VelopackHook? install = null, update = null, uninstall = null;
            NthInstallHook.Register(hook => install = hook, hook => update = hook, hook => uninstall = hook,
                _ => throw new InvalidOperationException("no root"), () => new FakeShellLinks(), () => new FakeUserRegistry());
            install!(version);
            update!(version);
            uninstall!(version);

            NthInstallHook.Register(hook => install = hook, hook => update = hook, hook => uninstall = hook,
                v => new InstallHookContext(FakeShellLinks.DefaultRoot, temp, null, v.ToFullString()),
                () => throw new System.Runtime.InteropServices.COMException("no shell"), () => new FakeUserRegistry());
            install(version);

            // the real shell over a temporary folder: the hook ends with it disposed
            var root = Directory.CreateDirectory(Path.Combine(temp, "root", "current")).Parent!.FullName;
            var links = new ShellLinks(new Dictionary<ShellKnownFolder, string>
            {
                [ShellKnownFolder.Desktop] = Directory.CreateDirectory(Path.Combine(temp, "Desktop")).FullName,
                [ShellKnownFolder.Programs] = Directory.CreateDirectory(Path.Combine(temp, "Programs")).FullName,
                [ShellKnownFolder.Profile] = Directory.CreateDirectory(Path.Combine(temp, "Profile")).FullName,
            });
            var reports = new List<InstallHookReport>();
            NthInstallHook.Register(hook => install = hook, hook => update = hook, hook => uninstall = hook,
                v => new InstallHookContext(root, temp, null, v.ToFullString()), () => links, () => throw new UnauthorizedAccessException("no hive"), reports.Add);
            install(version);
            Assert.Empty(reports); // the registry could not be opened: nothing ran, nothing threw
            Assert.Throws<ObjectDisposedException>(() => links.ReadLink(Path.Combine(temp, "Desktop", "nth.lnk")));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_hook_context_is_the_root_above_current_the_inherited_variable_and_the_full_version()
    {
        var context = NthInstallHook.ContextFor(@"C:\Users\Ana\AppData\Local\Programs\nth\current\", SemanticVersion.Parse("1.0.1-beta.2"));

        Assert.Equal(@"C:\Users\Ana\AppData\Local\Programs\nth", context.Root);
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), context.LocalAppData);
        Assert.Equal(Environment.GetEnvironmentVariable(InstallOptions.EnvironmentVariable), context.OptionsVariable);
        Assert.Equal("1.0.1-beta.2", context.Version);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_self_heal_runs_only_in_a_Velopack_install_on_the_pool_and_never_throws()
    {
        const string Current = FakeShellLinks.DefaultRoot + @"\current";
        var shell = new FakeShellLinks();
        shell.AddFile(Current + @"\nth.exe");
        shell.Seed(FakeShellLinks.StartLink, new ShellLink(Current + @"\nth.exe", AppUserModelId: "velopack.NthApp"));
        var asked = new List<string>();

        Assert.Null(NthInstallHook.StartSelfHeal(Current, dir => { asked.Add(dir); return false; }, () => shell, () => new FakeUserRegistry()));
        Assert.Equal(new[] { Current }, asked);
        Assert.Equal(0, shell.Changes);

        var report = await NthInstallHook.StartSelfHeal(Current, _ => true, () => shell, () => new FakeUserRegistry())!;
        Assert.Equal(new[] { "start-heal: restamped", "desktop-repair: 0 changed of 0", "uninstall-key: no key" }, report!.Steps);
        Assert.Equal(ShellLinkKeys.NthAppUserModelId, shell.Peek(FakeShellLinks.StartLink)!.AppUserModelId);

        Assert.Null(await NthInstallHook.StartSelfHeal(Current, _ => true, () => throw new System.Runtime.InteropServices.COMException("no shell"), () => new FakeUserRegistry())!);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_app_starts_the_self_heal_after_its_first_window_is_up()
    {
        var finish = typeof(Program).GetMethod("FinishStartup", BindingFlags.Static | BindingFlags.NonPublic)!;
        var machine = finish.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        var names = MethodCalls.Of(machine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!)
            .Select(c => c.DeclaringType!.Name + "." + c.Name).ToList();
        var launched = names.IndexOf("AppShell.FinishLaunching");
        var heal = names.IndexOf("NthInstallHook.StartSelfHeal");
        Assert.True(launched >= 0 && heal > launched, string.Join(", ", names));
    }

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    // The bytes of an instruction's operand (switch carries a count and that many 4-byte targets).
    private static int OperandSize(OpCode code, byte[] il, int operandAt) => code.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, operandAt),
        _ => 4,
    };
}
