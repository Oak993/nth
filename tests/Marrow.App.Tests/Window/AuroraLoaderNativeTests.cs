using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.UI.Port;
using Marrow.Testing;
using Xunit;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// OD-0113. Real debug Win32 windows only, native.lock required. No bootstrap/provider/model invocation.
[Collection("WindowManagerNative")]
public sealed class AuroraLoaderNativeTests(WindowManagerNativeSession native)
{
    [Fact]
    [Trait("Decision", "ED-097")]
    public void Each_loader_runs_on_the_real_compositor_and_records_its_frame_times()
    {
        native.Run(subject =>
    {
        foreach (var dark in new[] { true, false })
        {
            foreach (var kind in new[] { AuroraLoaderKind.CurtainFold, AuroraLoaderKind.FlowArc, AuroraLoaderKind.AuroraSheen })
            {
                var capture = new AuroraLoaderTelemetry();
                var window = native.OnUi(() =>
                {
                    subject.Main.Hide(); subject.Settings.Hide();
                    var loader = new AuroraLoader(kind) { Telemetry = capture, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    if (kind == AuroraLoaderKind.AuroraSheen) loader.Width = 180;
                    var owned = new Avalonia.Controls.Window
                    {
                        Width = 260, Height = 180, ShowInTaskbar = false, Topmost = true,
                        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                        Content = loader, Background = dark ? Brushes.Black : Brushes.White,
                        WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(250, 250),
                    };
                    owned.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
                    native.Register(owned); owned.Show();
                    return owned;
                });
                try
                {
                    N.Until(() => capture.Count >= 180 && Enumerable.Range(0, capture.Count).Sum(capture.IntervalMilliseconds) >= 3000,
                        "the Aurora loader did not draw at least 180 frames over three seconds", 20000);
                    var directory = Path.Combine(Environment.GetEnvironmentVariable(WindowManagerNativeChildProtocol.DirectoryVariable)!, "aurora-capture");
                    Directory.CreateDirectory(directory);
                    var lines = new List<string> { "frame,interval_ms,handler_ms,painter_bytes,handler_bytes" };
                    for (var i = 0; i < capture.Count; i++) lines.Add(string.Create(CultureInfo.InvariantCulture, $"{i},{capture.IntervalMilliseconds(i):F6},{capture.DrawingMilliseconds(i):F6},{capture.AllocatedBytes(i)},{capture.HandlerAllocatedBytes(i)}"));
                    File.WriteAllLines(Path.Combine(directory, $"{kind}-{(dark ? "dark" : "light")}.csv"), lines);
                    // First 30 samples are warm-up/JIT, kept in raw evidence. The drawing itself owns zero allocations.
                    Assert.All(Enumerable.Range(30, capture.Count - 30), i => Assert.Equal(0, capture.AllocatedBytes(i)));
                    Assert.True(capture.Count >= 180);
                }
                finally { native.OnUi(window.Close); }
            }
        }
    }, factType: typeof(AuroraLoaderNativeTests));
        // The child writes only under its authenticated scratch root. The parent exports its own capture afterward.
        if (!WindowManagerNativeChildProtocol.IsChild)
        {
            var output = Environment.GetEnvironmentVariable("MARROW_AURORA_CAPTURE") ?? throw new InvalidOperationException("MARROW_AURORA_CAPTURE must name the scratch evidence directory.");
            Directory.CreateDirectory(output);
            var runs = Path.Combine(TestDataDirectory.Read()!, "WindowManagerNative");
            foreach (var run in Directory.EnumerateDirectories(runs))
            {
                var captured = Path.Combine(run, "aurora-capture");
                if (!Directory.Exists(captured)) continue;
                foreach (var file in Directory.EnumerateFiles(captured, "*.csv")) File.Copy(file, Path.Combine(output, Path.GetFileName(file)), true);
            }
        }
    }
}
