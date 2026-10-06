using System.Reflection;
using Avalonia;

namespace Marrow.App.Tests.Window.Native;

// The authenticated Aurora capture child alone forwards its native vsync ticks at 60 fps.
// Avalonia 12 strips PrivateApi members from reference assemblies; runtime reflection fails closed.
internal static class AuroraNativeRenderClock
{
    internal static void CaptureAt60()
    {
        var assembly = typeof(AvaloniaObject).Assembly;
        var locatorType = assembly.GetType("Avalonia.AvaloniaLocator", true)!;
        var locator = locatorType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var loopType = assembly.GetType("Avalonia.Rendering.IRenderLoop", true)!;
        var loop = locator.GetType().GetMethod("GetService")!.Invoke(locator, [loopType])
            ?? throw new InvalidOperationException("The native render loop is missing.");
        var timer = loop.GetType().GetProperty("Timer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(loop)
            ?? throw new InvalidOperationException("The native render loop has no timer: " + loop.GetType().FullName);
        var tick = timer.GetType().GetProperty("Tick", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("The native timer has no tick: " + timer.GetType().FullName);
        var callback = tick.GetValue(timer) as Action<TimeSpan>
            ?? throw new InvalidOperationException("The native compositor did not subscribe to its timer.");
        var interval = TimeSpan.FromSeconds(1.0 / 60);
        TimeSpan? next = null;
        Action<TimeSpan> capture = now =>
        {
            if (next is { } due && now < due) return;
            next ??= now;
            do { next += interval; } while (next <= now);
            callback(now); // This gates actual compositor work, not the telemetry samples.
        };
        tick.SetValue(timer, capture);
    }
}
