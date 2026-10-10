using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Port.Ignis.Runtime;
using Marrow.App.Port.Ignis.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Ignis;

// The rig and the pixel frame IgnisSidebarMarkTests drives the sidebar mark with (moved out of the test class unchanged but for their names: a test class holds no nested type and its file names no other type, R-TEST-2/R-TEST-3).
internal sealed class SidebarMarkRig : IDisposable
{
    public readonly TestRuntime Runtime = new();
    public readonly FakeIgnisPort Port = new();
    public readonly FakeSavingsLedger Ledger = new();
    public readonly IgnisController Controller;
    public readonly IgnisSidebarMark Mark;
    public readonly AvaloniaWindow Window;
    public int Selected;
    private readonly List<IDisposable> scopes = [];

    public SidebarMarkRig(ManualFrameSource? frames = null, bool dark = false, double rowHeight = 28)
    {
        if (frames is not null) scopes.Add(FrameClock.Source.Override(frames));
        scopes.Add(Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform()));
        var settings = new IgnisSettings(Runtime.Defaults);
        Controller = Runtime.OnMain(() => new IgnisController(settings, new FakeIgnisPoints(), Ledger, new FakeIgnisEngine(), new FakeIgnisEnvironment()));
        Mark = Runtime.OnMain(() => new IgnisSidebarMark(new IgnisRowWarmthSource(Controller, Port.ThreadID), () => Selected++));
        var row = new Panel { Width = 260, Height = rowHeight, Children = { Mark } };
        Window = Runtime.OnMain(() =>
        {
            var window = new AvaloniaWindow
            {
                Width = 300, Height = rowHeight + 40, Background = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Color.FromRgb(0xF5, 0xF5, 0xF7)),
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                Content = new Border { Padding = new Thickness(20), Child = row, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top },
            };
            window.Show();
            Mark.Place(8 + 52); // a plain row's trailing clearance
            Settle(window);
            return window;
        });
    }

    public void Settle() => Runtime.OnMain(() => Settle(Window));

    private static void Settle(AvaloniaWindow window)
    {
        for (var i = 0; i < 4; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    }

    /// <summary>A user turn that writes and reads the cache (the chat is warm and kept warm from here).</summary>
    public void Warm() => Runtime.OnMain(() =>
    {
        Controller.Attach(Port);
        Controller.Observe(Port.ThreadID, IgnisSamples.Usage(read: 1_000, write1h: 200));
        Controller.TurnFinished(Port.ThreadID);
    });

    public void On(Action action) => Runtime.OnMain(action);

    public T On<T>(Func<T> read) => Runtime.OnMain(read);

    public void Dispose()
    {
        Runtime.OnMain(Window.Close);
        for (var i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose();
        Runtime.Dispose();
    }
}

internal sealed record SidebarMarkFrame(int Width, int Height, int Stride, byte[] Bytes)
{
    public uint At(int x, int y) => BitConverter.ToUInt32(Bytes, y * Stride + x * 4);
}
