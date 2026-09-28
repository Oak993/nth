using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Marrow.Services.Git;

namespace Marrow.App.Tests;

public sealed class AppModelHydraMergeWorkerTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-034")] // App/AppModel+HydraMerge.swift:313: parsing is detached from the actor.
    public void The_compiled_prepare_state_machine_dispatches_its_parser_delegate_through_Task_Run()
    {
        // CLR Task.Run's default-scheduler contract verifies the worker boundary without adding a
        // production observer to the pure parser. Also reject a direct parse on the actor continuation.
        var prepare = typeof(AppModel).GetMethod("MergeHydraProject", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var machine = prepare.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        var move = machine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var calls = Members(move).ToArray();
        Assert.DoesNotContain(calls, m => m.DeclaringType == typeof(DiffParser) && m.Name == "Parse");
        Assert.Contains(calls, m => m.DeclaringType == typeof(Task) && m.Name == "Run");
        var parser = Assert.Single(calls.OfType<MethodInfo>(), m => m.Name.Contains("<MergeHydraProject>", StringComparison.Ordinal) &&
            Members(m).Any(c => c.DeclaringType == typeof(DiffParser) && c.Name == "Parse"));
        Assert.NotNull(parser.GetMethodBody());
    }

    private static IEnumerable<MemberInfo> Members(MethodInfo method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(c => unchecked((ushort)c.Value));
        for (var i = 0; i < bytes.Length;)
        {
            ushort value = bytes[i++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[i++]);
            var code = codes[value];
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, i),
                _ => 4,
            };
            if (code.OperandType == OperandType.InlineMethod)
            {
                var member = method.Module.ResolveMethod(BitConverter.ToInt32(bytes, i), method.DeclaringType?.GetGenericArguments(), method.GetGenericArguments());
                if (member is not null) yield return member;
            }
            i += size;
        }
    }
}
