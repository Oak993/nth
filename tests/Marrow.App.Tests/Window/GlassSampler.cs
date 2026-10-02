using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// ED-236: samples the screen behind a glass window over time for GlassWindowNativeTests.
internal sealed class Sampler : IDisposable
{

    private readonly List<GlassSample> samples = [];
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Thread thread;
    private volatile bool running = true;

    public Sampler(PixelPoint at)
    {
        thread = new Thread(() =>
        {
            while (running)
            {
                var grab = ScreenGrab.Of(at.X, at.Y, 60, 12);
                lock (samples) samples.Add(new GlassSample(clock.Elapsed.TotalMilliseconds, grab.MeanLuma(), grab.StripeContrast()));
            }
        }) { IsBackground = true };
        thread.Start();
    }

    public double Now => clock.Elapsed.TotalMilliseconds;

    public GlassSample[] Stop()
    {
        running = false;
        thread.Join();
        lock (samples) return samples.ToArray();
    }

    public void Dispose() => Stop();
}
