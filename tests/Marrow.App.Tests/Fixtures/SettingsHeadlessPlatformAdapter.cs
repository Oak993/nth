using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Avalonia.Headless;
using Avalonia.Platform;

namespace Marrow.App.Tests.Window;

// Test-only compatibility for the actual Avalonia.Headless 12.1.2 contract.
// Upstream HeadlessWindowExtensions.GetImpl requires IHeadlessWindow, an INTERNAL interface:
// src/Headless/Avalonia.Headless/IHeadlessWindow.cs (tag 12.1.2, blob 44ac0a5acec3616c5151674a2edb78c087f6466e).
// No method signatures are copied: the composite inherits the real runtime interface, and the
// existing DispatchProxy dispatcher forwards each interface MethodInfo to the original object.
public static class SettingsHeadlessPlatformAdapter
{
    private static readonly Lazy<Type> Contract = new(() =>
        typeof(HeadlessWindowExtensions).Assembly.GetType("Avalonia.Headless.IHeadlessWindow", throwOnError: true)!);
    private static readonly Lazy<Type> Composite = new(BuildCompositeContract);

    public static Type HeadlessContract => Contract.Value;

    public static (IWindowImpl Platform, SettingsWindowPlatformProxy Spy) Create(IWindowImpl original)
    {
        ArgumentNullException.ThrowIfNull(original);
        var headless = HeadlessContract;
        if (!headless.IsInterface || !headless.IsInstanceOfType(original))
            throw new InvalidOperationException("The original platform does not implement Avalonia.Headless.IHeadlessWindow.");
        if (!RuntimeFeature.IsDynamicCodeSupported || !RuntimeFeature.IsDynamicCodeCompiled)
            throw new NotSupportedException("This test fixture requires the existing JIT testhost's dynamic proxy support.");

        var instance = DispatchProxy.Create(Composite.Value, typeof(SettingsWindowPlatformProxy));
        if (instance is not IWindowImpl platform || instance is not SettingsWindowPlatformProxy spy ||
            !headless.IsInstanceOfType(instance))
            throw new InvalidOperationException("The generated platform must implement BOTH original interface contracts.");
        spy.Original = original;
        return (platform, spy);
    }

    private static Type BuildCompositeContract()
    {
        var headless = HeadlessContract;
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Marrow.SettingsHeadlessContract"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("SettingsHeadlessContract");

        // Only the generated CONTRACT assembly needs this attribute here. The .NET 10
        // DispatchProxyGenerator independently handles internal-interface visibility for its
        // own generated implementation assembly (EnsureTypeIsVisible / AddInterfaceImpl).
        // This uses the same runtime mechanism as DispatchProxy itself; no product assembly,
        // global process permission or friend-assembly declaration is changed.
        if (!headless.IsVisible)
        {
            var visibility = BuildVisibilityAttribute(module);
            var targetName = headless.Assembly.GetName().Name
                ?? throw new InvalidOperationException("Headless contract assembly has no simple name.");
            assembly.SetCustomAttribute(new CustomAttributeBuilder(visibility, [targetName]));
        }

        var composite = module.DefineType("Marrow.App.Tests.Window.ISettingsHeadlessWindowPlatform",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        composite.AddInterfaceImplementation(typeof(IWindowImpl));
        composite.AddInterfaceImplementation(headless);
        // An empty interface that inherits the two real contracts. Defining methods here would
        // change their declaring MethodInfo and prevent direct invocation on the original.
        return composite.CreateTypeInfo()!.AsType();
    }

    private static ConstructorInfo BuildVisibilityAttribute(ModuleBuilder module)
    {
        // Source mechanism: dotnet/runtime v10.0.0 DispatchProxyGenerator.ProxyAssembly.
        // Define the attribute only inside this in-memory module, not in the test/product DLL.
        var attribute = module.DefineType("System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed, typeof(Attribute));
        var field = attribute.DefineField("assemblyName", typeof(string), FieldAttributes.Private | FieldAttributes.InitOnly);
        var constructor = attribute.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, [typeof(string)]);
        var attributeConstructor = typeof(Attribute).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: Type.EmptyTypes, modifiers: null)
            ?? throw new MissingMethodException(typeof(Attribute).FullName, ".ctor");
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, attributeConstructor);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, field);
        il.Emit(OpCodes.Ret);

        var getter = attribute.DefineMethod("get_AssemblyName",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            typeof(string), Type.EmptyTypes);
        var getterIl = getter.GetILGenerator();
        getterIl.Emit(OpCodes.Ldarg_0);
        getterIl.Emit(OpCodes.Ldfld, field);
        getterIl.Emit(OpCodes.Ret);
        var property = attribute.DefineProperty("AssemblyName", PropertyAttributes.None, typeof(string), Type.EmptyTypes);
        property.SetGetMethod(getter);
        attribute.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(AttributeUsageAttribute).GetConstructor([typeof(AttributeTargets)])!,
            [AttributeTargets.Assembly],
            [typeof(AttributeUsageAttribute).GetProperty(nameof(AttributeUsageAttribute.AllowMultiple))!],
            [true]));
        return attribute.CreateTypeInfo()!.AsType().GetConstructor([typeof(string)])!;
    }
}
