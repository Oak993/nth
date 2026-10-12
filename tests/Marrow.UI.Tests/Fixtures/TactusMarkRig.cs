using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (ED-NEW-pcctl-11). A Tactus mark in a 64 DIP headless window on black, with a manual frame source and clock
/// that start 10 s in; <see cref="Run"/> steps both 8 ms at a time.
/// </summary>
internal sealed class TactusMarkRig : IDisposable
{
    private const int Frame = 8;
    private readonly IDisposable frameScope;
    private readonly IDisposable clockScope;

    public TactusMarkRig(TactusMark mark, bool reduceMotion = false)
    {
        Frames = new ManualFrameSource();
        Time = new ManualTimeProvider();
        frameScope = FrameClock.Source.Override(Frames);
        clockScope = SwiftClock.Provider.Override(Time);
        Time.Advance(TimeSpan.FromSeconds(10));
        Mark = mark;
        Window = new Window { Width = 64, Height = 64, Content = mark, Background = Brushes.Black };
        EnvironmentValues.SetAccessibilityReduceMotion(Window, reduceMotion);
        Window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    public ManualFrameSource Frames { get; }

    public ManualTimeProvider Time { get; }

    public TactusMark Mark { get; }

    public Window Window { get; }

    /// <summary>Advances the clock and delivers a frame every 8 ms for <paramref name="ms"/>.</summary>
    public void Run(int ms)
    {
        for (var at = 0; at < ms; at += Frame)
        {
            Time.Advance(TimeSpan.FromMilliseconds(Frame));
            Frames.Advance(TimeSpan.FromMilliseconds(Frame));
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The window's rendered pixels as RGB, row by row.</summary>
    public (int R, int G, int B)[] Capture(out int width)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = Window.CaptureRenderedFrame()!;
        var size = bitmap.PixelSize;
        width = size.Width;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        var pixels = new (int, int, int)[size.Width * size.Height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = i * 4;
            pixels[i] = (bytes[p + (rgba ? 0 : 2)], bytes[p + 1], bytes[p + (rgba ? 2 : 0)]);
        }
        return pixels;
    }

    public void Dispose()
    {
        Window.Close();
        clockScope.Dispose();
        frameScope.Dispose();
    }
}
