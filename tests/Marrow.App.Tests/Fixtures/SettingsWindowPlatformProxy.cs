// Forwards the real headless input interface while recording window platform calls.
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Platform;

namespace Marrow.App.Tests.Window;

public class SettingsWindowPlatformProxy : DispatchProxy
{
    public IWindowImpl Original { get; set; } = null!;
    public double? DesktopScale { get; set; }
    public Size? OuterFrame { get; set; }
    public Action? BeforeShow { get; set; }
    public Action? BeforeActivate { get; set; }
    public List<string> Calls { get; } = [];

    // Separate diagnostics so existing assertions about platform Calls do not change meaning.
    public List<string> HeadlessCalls { get; } = [];
    public Action<MethodInfo>? BeforeForward { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var method = targetMethod ?? throw new ArgumentNullException(nameof(targetMethod));
        if (method.Name == "get_DesktopScaling" && DesktopScale is { } scaling) return scaling;
        if (method.Name == "get_FrameSize" && OuterFrame is { } frame) return (Size?)frame;
        if (method.Name is "Show" or "Activate" or "Move" or "BeginMoveDrag" or "BeginResizeDrag") Calls.Add(method.Name);
        if (method.Name == "Show") BeforeShow?.Invoke();
        if (method.Name == "Activate") BeforeActivate?.Invoke();
        if (method.DeclaringType == SettingsHeadlessPlatformAdapter.HeadlessContract) HeadlessCalls.Add(method.Name);
        BeforeForward?.Invoke(method);
        // For the explicit internal headless implementations, MethodInfo is the actual interface
        // method inherited by the composite. Invoke routes it to Original's explicit implementation.
        // PlatformImpl on the TopLevel stays this proxy throughout the resulting input callbacks.
        try { return method.Invoke(Original, args); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}
