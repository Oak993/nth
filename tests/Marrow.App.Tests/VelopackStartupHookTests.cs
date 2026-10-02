using System.Reflection;
using System.Reflection.Emit;
using Velopack;
using Velopack.Locators;

namespace Marrow.App.Tests;

// PORT-ONLY (no Swift twin): OD-0062 / ED-235. The Velopack hook is the first thing Program.Main does, and in a run that
// Velopack did not install (a dev run, this test host) it must change nothing. Program.Main cannot run in a test host
// (STAThread, the single-instance lock, the full Avalonia lifetime, the real %LOCALAPPDATA%\Marrow Code folder), so the
// order is proved from Main's own IL (its first two calls), and the no-op from the library call Main makes. The installed
// case (Update.exe running the app with --veloapp-install, -updated, -obsolete or -uninstall) cannot be tested here
// without installing the app onto this PC: Velopack's Run() exits the process for those arguments.
public sealed class VelopackStartupHookTests
{
    [Fact]
    [Trait("Decision", "ED-235")]
    public void Main_calls_VelopackApp_Build_then_Run_before_anything_else()
    {
        var main = typeof(Program).GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static)!;
        var il = main.GetMethodBody()!.GetILAsByteArray()!;
        var calls = new List<MethodBase>();
        for (var at = 0; at < il.Length && calls.Count < 2;)
        {
            var code = il[at] == 0xFE ? Codes[(short)(0xFE00 | il[at + 1])] : Codes[il[at]];
            var operandAt = at + (il[at] == 0xFE ? 2 : 1);
            if (code == OpCodes.Call || code == OpCodes.Callvirt)
                calls.Add(main.Module.ResolveMethod(BitConverter.ToInt32(il, operandAt))!);
            at = operandAt + OperandSize(code, il, operandAt);
        }

        Assert.Equal(2, calls.Count);
        Assert.Equal(typeof(VelopackApp), calls[0].DeclaringType);
        Assert.Equal(nameof(VelopackApp.Build), calls[0].Name);
        Assert.Equal(typeof(VelopackApp), calls[1].DeclaringType);
        Assert.Equal(nameof(VelopackApp.Run), calls[1].Name);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void The_hook_returns_and_changes_nothing_when_the_app_was_not_installed_by_Velopack()
    {
        var before = Environment.GetEnvironmentVariables();

        // No hook argument: the plain launch a dev run is.
        VelopackApp.Build().SetArgs([]).Run();
        // A bare hook name without the version Update.exe always passes is not a hook call either.
        VelopackApp.Build().SetArgs(["--veloapp-install"]).Run();
        // A launch argument the app handles itself (a toast activation) passes straight through.
        VelopackApp.Build().SetArgs(["--some-toast-argument", "1.7.0"]).Run();

        Assert.False(VelopackLocator.Current.IsPortable);
        Assert.Null(VelopackLocator.Current.CurrentlyInstalledVersion);
        Assert.Null(VelopackLocator.Current.AppUserModelId);
        var after = Environment.GetEnvironmentVariables();
        Assert.Equal(before.Count, after.Count);
        foreach (var key in before.Keys) Assert.Equal(before[key], after[key]);
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
