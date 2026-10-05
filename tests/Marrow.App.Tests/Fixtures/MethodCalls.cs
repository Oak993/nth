using System.Reflection;
using System.Reflection.Emit;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The methods a method body calls, in IL order (call and callvirt operands), read from its IL bytes. For proving that a start-up path registers or calls something when the path itself
/// cannot be run in a test host (the Velopack hook writes to the real %LOCALAPPDATA% every time it runs; VelopackStartupHookTests reads Main's IL the same way). Lambdas are separate methods:
/// a call inside one is not listed for its enclosing method.
/// </summary>
internal static class MethodCalls
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    public static IReadOnlyList<MethodBase> Of(MethodInfo method)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var calls = new List<MethodBase>();
        for (var at = 0; at < il.Length;)
        {
            var code = il[at] == 0xFE ? Codes[unchecked((short)(0xFE00 | il[at + 1]))] : Codes[il[at]];
            var operandAt = at + (il[at] == 0xFE ? 2 : 1);
            if (code == OpCodes.Call || code == OpCodes.Callvirt)
                calls.Add(method.Module.ResolveMethod(BitConverter.ToInt32(il, operandAt))!);
            at = operandAt + OperandSize(code, il, operandAt);
        }
        return calls;
    }

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
