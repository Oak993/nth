using System.Reflection;
using System.Reflection.Emit;

namespace Marrow.App.Tests.Fixtures;

// Test-only decoder: inspect actual compiled operations where process-wide field assignments have no observer seam.
// Operand boundaries are decoded, so a token byte cannot be mistaken for an opcode.
public static class CompiledLaunchOrder
{
    public static IReadOnlyList<MemberInfo> Members(MethodInfo method)
    {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        var members = new List<MemberInfo>();
        for (var at = 0; at < bytes.Length;)
        {
            var first = bytes[at++];
            var code = codes[first == 0xfe ? (ushort)(0xfe00 | bytes[at++]) : first];
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, at),
                _ => 4,
            };
            if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField)
                members.Add(method.Module.ResolveMember(BitConverter.ToInt32(bytes, at),
                    method.DeclaringType!.GetGenericArguments(), method.GetGenericArguments())!);
            at += size;
        }
        return members;
    }
}
