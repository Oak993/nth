using System.Reflection;
using System.Reflection.Emit;

namespace Marrow.UI.Port;

// ED-NEW-T45. Keep the pinned WinUI effect's COM implementation and replace only
// its numeric property. Avalonia's own shared brush hard-codes sigma 30; changing
// that brush would also change every main window and popover in the application.
internal static class ToolTipBackdropEffect
{
    private static readonly Lazy<(Type Blur, Type Saturation)> effects = new(BuildEffects);

    internal static object Create(object compositor, double sigma, double saturation)
    {
        var assembly = Assembly.Load("Avalonia.Win32");
        // MicroCom proxy types live in Avalonia.Win32, as do these pinned interfaces.
        Type T(string name) => assembly.GetType("Avalonia.Win32.WinRT." + name, true)!;
        var utils = T("Composition.WinUiCompositionUtils");
        var runtime = T("ICompositor").GetInterfaces().First(t => t.Name == "IUnknown").Assembly
            .GetType("MicroCom.Runtime.MicroComRuntime", true)!;
        object Qi(object value, Type type) => runtime.GetMethods().Single(m => m.Name == "QueryInterface" && m.IsGenericMethodDefinition)
            .MakeGenericMethod(type).Invoke(null, [value])!;
        var held = new List<IDisposable>();
        object Hold(object value) { if (value is IDisposable disposable) held.Add(disposable); return value; }
        try
        {
            var factoryType = T("ICompositionEffectSourceParameterFactory");
            var activation = T("NativeWinRTMethods").GetMethod("CreateActivationFactory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(factoryType);
            var factory = Hold(activation.Invoke(null, ["Windows.UI.Composition.CompositionEffectSourceParameter"])!);
            var text = Hold(Activator.CreateInstance(T("HStringInterop"), "backdrop")!);
            var handle = T("HStringInterop").GetProperty("Handle")!.GetValue(text);
            var parameter = Hold(factoryType.GetMethod("Create")!.Invoke(factory, [handle])!);
            var source = Hold(Qi(parameter, T("IGraphicsEffectSource")));
            var pair = effects.Value;
            var blur = Hold(Activator.CreateInstance(pair.Blur, source, (float)sigma)!);
            var saturated = Hold(Activator.CreateInstance(pair.Saturation, blur, (float)saturation)!);
            // Retain the managed effect's native shadow for the whole factory call,
            // exactly as Avalonia's own CreateAcrylicBlurBackdropBrush does.
            var lease = runtime.GetMethods().Single(m => m.Name == "LeaseNativePointerForCall" && m.IsGenericMethodDefinition);
            // Windows can release a nested source while still inspecting the graph.
            // Preserve its CCW too, as the pinned Avalonia acrylic factory does.
            Hold(lease.MakeGenericMethod(T("IGraphicsEffectSource")).Invoke(null, [blur])!);
            Hold(lease.MakeGenericMethod(T("IGraphicsEffectSource")).Invoke(null, [saturated])!);
            var effectFactory = Hold(T("ICompositor").GetMethod("CreateEffectFactory")!.Invoke(compositor, [saturated])!);
            var effectBrush = Hold(T("ICompositionEffectFactory").GetMethod("CreateBrush")!.Invoke(effectFactory, null)!);
            var backdrop = Hold(utils.GetMethod("CreateBackdropBrush")!.Invoke(null, [compositor])!);
            T("ICompositionEffectBrush").GetMethod("SetSourceParameter")!.Invoke(effectBrush, [handle, backdrop]);
            return Qi(effectBrush, T("ICompositionBrush")); // independent retained COM reference
        }
        finally { for (var index = held.Count - 1; index >= 0; index--) held[index].Dispose(); }
    }

    private static (Type, Type) BuildEffects()
    {
        var assembly = Assembly.Load("Avalonia.Win32");
        Type T(string name) => assembly.GetType("Avalonia.Win32.WinRT." + name, true)!;
        var dynamicAssembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Marrow.ToolTipEffects"), AssemblyBuilderAccess.Run);
        var access = typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute).GetConstructor([typeof(string)])!;
        dynamicAssembly.SetCustomAttribute(new CustomAttributeBuilder(access, [assembly.GetName().Name!]));
        var module = dynamicAssembly.DefineDynamicModule("Effects");
        Type Build(string name, string parent)
        {
            var baseType = T("Composition." + parent);
            var source = T("IGraphicsEffectSource"); var property = T("IPropertyValue");
            var valueType = T("WinRTPropertyValue");
            var type = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed, baseType);
            var number = type.DefineField("number", typeof(float), FieldAttributes.Private | FieldAttributes.InitOnly);
            var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [source, typeof(float)]);
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, baseType.GetConstructor([source])!);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stfld, number); il.Emit(OpCodes.Ret);
            var get = type.DefineMethod("GetProperty", MethodAttributes.Public | MethodAttributes.Virtual, property, [typeof(uint)]);
            il = get.GetILGenerator(); var inherited = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Brtrue_S, inherited);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, number);
            il.Emit(OpCodes.Newobj, valueType.GetConstructor([typeof(float)])!); il.Emit(OpCodes.Ret);
            il.MarkLabel(inherited); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, baseType.GetMethod("GetProperty")!); il.Emit(OpCodes.Ret);
            type.DefineMethodOverride(get, baseType.GetMethod("GetProperty")!);
            return type.CreateType()!;
        }
        return (Build("HelpTagBlur", "WinUIGaussianBlurEffect"), Build("HelpTagSaturation", "SaturationEffect"));
    }
}
