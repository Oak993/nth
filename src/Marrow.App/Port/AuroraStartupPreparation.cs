using System.Reflection;
using System.Runtime.CompilerServices;
using Marrow.UI.Port;

namespace Marrow.App.Port;

// Prepare the launch's own managed UI methods before its first animated frame. This invokes no bootstrap,
// I/O, provider or model mutation; the actual drawing warm call is in AuroraBootPreparation.
internal static class AuroraStartupPreparation
{
    private static readonly Lazy<bool> prepared = new(() =>
    {
        using var work = AuroraBootTrace.Begin("Boot: before-show UI/bootstrap JIT");
        foreach (var type in new[] { typeof(BootStageWindow), typeof(AuroraBootPresentation), typeof(AuroraBootStage) })
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            if (!method.ContainsGenericParameters && method.GetMethodBody() is not null) RuntimeHelpers.PrepareMethod(method.MethodHandle);
        var bootstrap = typeof(AppModel).GetMethod(nameof(AppModel.Bootstrap))!;
        RuntimeHelpers.PrepareMethod(bootstrap.MethodHandle);
        var state = bootstrap.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        if (state?.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) is { } advance)
            RuntimeHelpers.PrepareMethod(advance.MethodHandle);
        return true;
    });
    internal static void Prepare() => _ = prepared.Value;
}
