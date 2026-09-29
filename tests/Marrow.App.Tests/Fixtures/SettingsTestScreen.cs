// Creates Avalonia's concrete headless Screen, never a native monitor.
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using Xunit;

namespace Marrow.App.Tests.Window;

public static class SettingsTestScreen
{
    private static int nextKey = -100000;

    public static Screen Create(PixelRect bounds, PixelRect workingArea, double scale, bool primary)
    {
        // Exact local 12.1.2 metadata: Screen is abstract; its .ctor() is private protected (0x1882).
        // Avalonia.Headless contains one nonabstract nested PlatformScreenStub : PlatformScreen,
        // with public .ctor(int) (0x1886, signature 20010108). Reuse that existing implementation.
        var type = typeof(AvaloniaHeadlessPlatform).Assembly.GetTypes().Single(candidate =>
            candidate.Name == "PlatformScreenStub" && !candidate.IsAbstract && typeof(Screen).IsAssignableFrom(candidate));
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types: [typeof(int)], modifiers: null)
            ?? throw new MissingMethodException(type.FullName, ".ctor(int)");
        var screen = Assert.IsAssignableFrom<Screen>(constructor.Invoke([Interlocked.Increment(ref nextKey)]));
        Set(screen, nameof(Screen.Bounds), bounds);
        Set(screen, nameof(Screen.WorkingArea), workingArea);
        Set(screen, nameof(Screen.Scaling), scale);
        Set(screen, nameof(Screen.IsPrimary), primary);
        Assert.Equal(bounds, screen.Bounds);
        Assert.Equal(workingArea, screen.WorkingArea);
        Assert.Equal(scale, screen.Scaling);
        Assert.Equal(primary, screen.IsPrimary);
        return screen;
    }

    private static void Set(Screen screen, string property, object value)
    {
        // Screen metadata: these four setters are protected (0x884), nonabstract and nonvirtual.
        var info = typeof(Screen).GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(Screen).FullName, property);
        var setter = info.GetSetMethod(nonPublic: true)
            ?? throw new MissingMethodException(typeof(Screen).FullName, "set_" + property);
        setter.Invoke(screen, [value]);
    }
}
