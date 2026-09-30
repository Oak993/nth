// Replaces only the test-owned window platform with a composite-contract spy.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

public sealed class SettingsWindowPlatformScope : IDisposable
{
    private readonly HostWindow window;
    private readonly FieldInfo field;
    private readonly IWindowImpl original;
    private readonly Size? originalFrame;
    public SettingsWindowPlatformProxy Spy { get; }
    public IWindowImpl Platform { get; }

    public SettingsWindowPlatformScope(HostWindow window)
    {
        this.window = window;
        originalFrame = window.FrameSize;
        // Existing worktree fixture mechanism, preserved: MCPSettingsPageProofsMcpATests and
        // the currently integrated SettingsWindowPlatformScope replace this one backing field.
        field = typeof(TopLevel).GetField("<PlatformImpl>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(TopLevel).FullName, "<PlatformImpl>k__BackingField");
        original = Assert.IsAssignableFrom<IWindowImpl>(field.GetValue(window));
        var adapter = SettingsHeadlessPlatformAdapter.Create(original);
        Platform = adapter.Platform;
        Spy = adapter.Spy;
        field.SetValue(window, Platform);
    }

    public void SetOuterFrame(Size frame)
    {
        Spy.OuterFrame = frame;
        SetReportedFrame(frame);
        Assert.Equal(frame, window.FrameSize);
    }

    private void SetReportedFrame(Size? frame)
    {
        // WindowBase caches the platform-reported frame separately from IWindowImpl. Preserve
        // the existing fixture's report of the frame, never replace its computed centering.
        var setter = typeof(WindowBase).GetProperty(nameof(WindowBase.FrameSize))?.GetSetMethod(nonPublic: true)
            ?? throw new MissingMethodException(typeof(WindowBase).FullName, "set_FrameSize");
        setter.Invoke(window, [frame]);
    }

    public void Dispose()
    {
        field.SetValue(window, original);
        if (Spy.OuterFrame is not null) SetReportedFrame(originalFrame);
    }
}
