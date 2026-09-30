using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Window.Native;

// Deliberately a content stub. These large test-owned hit regions exercise ED-063's declared
// Win32 interface, not WindowChrome's drawing, production traffic-light geometry, or Settings UI.
internal sealed class WindowManagerNativeContent : Grid
{
    internal Border CloseZone { get; } = Zone(Win32Properties.Win32HitTestValue.Close);
    internal Border MinimizeZone { get; } = Zone(Win32Properties.Win32HitTestValue.MinButton);
    internal Border MaximizeZone { get; } = Zone(Win32Properties.Win32HitTestValue.MaxButton);
    internal Border CaptionZone { get; } = new() { Background = Brushes.LightGray, Height = WindowChrome.TitlebarHeight, VerticalAlignment = VerticalAlignment.Top };

    internal WindowManagerNativeContent()
    {
        Background = Brushes.White;
        Win32Properties.SetNonClientHitTestResult(CaptionZone, Win32Properties.Win32HitTestValue.Caption);
        Children.Add(CaptionZone);
        var canvas = new Canvas { Height = WindowChrome.TitlebarHeight, VerticalAlignment = VerticalAlignment.Top };
        var controls = new[] { CloseZone, MinimizeZone, MaximizeZone };
        for (var index = 0; index < controls.Length; index++)
        {
            Canvas.SetLeft(controls[index], 24 + index * 44);
            Canvas.SetTop(controls[index], 10);
            canvas.Children.Add(controls[index]);
        }
        Children.Add(canvas);
    }

    private static Border Zone(Win32Properties.Win32HitTestValue value)
    {
        var zone = new Border { Width = 30, Height = 28, Background = Brushes.DarkGray };
        Win32Properties.SetNonClientHitTestResult(zone, value);
        return zone;
    }
}
