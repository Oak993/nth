// Replaces only a test-owned TopLevel's cached Screens wrapper.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

public sealed class SettingsScreenScope : IDisposable
{
    private readonly HostWindow window;
    private readonly FieldInfo field;
    private readonly object? original;
    public SettingsScreensImpl Impl { get; }

    public SettingsScreenScope(HostWindow window, IReadOnlyList<Screen> all, Screen? selected)
    {
        this.window = window;
        field = typeof(TopLevel).GetField("_screens", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(TopLevel).FullName, "_screens");
        original = field.GetValue(window);
        var platform = DispatchProxy.Create<IScreenImpl, SettingsScreensImpl>();
        Impl = (SettingsScreensImpl)platform;
        Impl.AllScreens = all;
        Impl.WindowScreen = selected;
        Impl.ExpectedWindow = window.PlatformImpl;
        var constructor = typeof(Screens).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types: [typeof(IScreenImpl)], modifiers: null)
            ?? throw new MissingMethodException(typeof(Screens).FullName, ".ctor(IScreenImpl)");
        var wrapper = Assert.IsType<Screens>(constructor.Invoke([platform]));
        field.SetValue(window, wrapper);
        Assert.Same(wrapper, window.Screens);
        Assert.Equal(all.Count, window.Screens.ScreenCount);
    }

    public static Screen Monitor(PixelRect bounds, PixelRect workingArea, double scale = 1, bool primary = false) =>
        SettingsTestScreen.Create(bounds, workingArea, scale, primary);

    public void Dispose()
    {
        Impl.Changed = null;
        field.SetValue(window, original);
    }
}
