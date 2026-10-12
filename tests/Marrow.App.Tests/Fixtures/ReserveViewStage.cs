using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-13: a control in a headless window with a manual frame clock (nothing animates by itself), for the plan card and the
/// Reserves view. Made inside <c>HeadlessSession.OnUiThread</c>.
/// </summary>
public sealed class ReserveViewStage : IDisposable
{
    private readonly IDisposable frames = FrameClock.Source.Override(new ManualFrameSource());

    public ReserveViewStage(Control content, double width = 900, double height = 700, FlowDirection flow = FlowDirection.LeftToRight)
    {
        Window = new AvaloniaWindow { Content = content, Width = width, Height = height, FlowDirection = flow };
        Window.Show();
        Layout();
    }

    public AvaloniaWindow Window { get; }

    public void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    public void Press(Key key)
    {
        Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Layout();
    }

    public void Dispose()
    {
        Window.Close();
        frames.Dispose();
    }
}
