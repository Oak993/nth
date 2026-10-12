using System.Reflection;
using System.Reflection.Emit;
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
        for (var at = 0; at < il.Length && calls.Count < 16;)
        {
            var code = il[at] == 0xFE ? Codes[(short)(0xFE00 | il[at + 1])] : Codes[il[at]];
            var operandAt = at + (il[at] == 0xFE ? 2 : 1);
            if (code == OpCodes.Call || code == OpCodes.Callvirt)
                calls.Add(main.Module.ResolveMethod(BitConverter.ToInt32(il, operandAt))!);
            at = operandAt + OperandSize(code, il, operandAt);
        }

        // SK-17 owner-authorized headless exception: this exact private dispatch may precede the desktop hook.
        // ED-NEW-mainorder-2: the ask_advisor relay (ED-NEW-ignisadvisor-2) shares it, as this exact second dispatch.
        // No settings, lock, toast, app lifetime or other calls can be inserted before the hook.
        Assert.Equal(new[] { "FirstOrDefault", "op_Equality", "OpenStandardInput", "OpenStandardOutput",
            "RunClient", "GetAwaiter", "GetResult",
            "FirstOrDefault", "op_Equality", "OpenStandardInput", "OpenStandardOutput",
            "RunClient", "GetAwaiter", "GetResult", "get_BaseDirectory", nameof(Program.RunVelopackHook) },
            calls.Select(call => call.Name));
        Assert.Equal(typeof(Enumerable), calls[0].DeclaringType);
        Assert.Equal(typeof(string), calls[1].DeclaringType);
        Assert.Equal(typeof(Console), calls[2].DeclaringType);
        Assert.Equal(typeof(Console), calls[3].DeclaringType);
        Assert.Equal(typeof(Marrow.Services.Port.Skills.Providers.SkillsMcpBridge), calls[4].DeclaringType);
        Assert.Equal(typeof(Enumerable), calls[7].DeclaringType);
        Assert.Equal(typeof(string), calls[8].DeclaringType);
        Assert.Equal(typeof(Console), calls[9].DeclaringType);
        Assert.Equal(typeof(Console), calls[10].DeclaringType);
        Assert.Equal(typeof(Marrow.Services.Port.Advisor.AdvisorMcpBridge), calls[11].DeclaringType);
        Assert.Equal(typeof(AppContext), calls[14].DeclaringType);
        Assert.Equal(typeof(Program), calls[15].DeclaringType);
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
