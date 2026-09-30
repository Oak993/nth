using System.Reflection;
using System.Text;
using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal.Port;
using Marrow.UI.Port;
using SkiaSharp;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

[Collection("Headless")]
public sealed class TerminalSurfaceTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-TERM-010")] // TerminalStore.swift:21-23; SwiftTerm UTF8Decoder and Terminal.swift feed
    public void Split_UTF8_output_and_split_CSI_sequences_reach_the_actual_surface_without_corruption()
    {
        headless.OnUiThread(() =>
        {
            var view = MakeView();
            var bytes = Encoding.UTF8.GetBytes("caf\u00e9 e\u0301 \u4e2d\U0001f642");
            foreach (var value in bytes) Output(view, [value]);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("caf\u00e9 e\u0301 \u4e2d\U0001f642", SurfaceText(view), StringComparison.Ordinal);
            Output(view, "\r\x1b["u8.ToArray());
            Output(view, "2Kbye"u8.ToArray());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("bye", SurfaceText(view).TrimEnd());
            AssertAccessibleSelectionAndCopy(view);
        });
    }

    [Fact]
    [Trait("Req", "UI-TERM-010")] // SwiftTerm Colors.swift:96-114; TerminalStore.swift:50-56
    [Trait("Req", "PR-TERM-045")]
    public void ANSI_foreground_and_background_are_painted_by_the_production_terminal_surface()
    {
        headless.OnUiThread(() =>
        {
            var view = MakeView();
            Output(view, "\x1b[31;44mMMMM\x1b[0m"u8.ToArray());
            Dispatcher.UIThread.RunJobs();
            var surface = Assert.IsAssignableFrom<Control>(view.Child);
            surface.Measure(new Size(160, 60));
            surface.Arrange(new Rect(0, 0, 160, 60));
            using var bitmap = new RenderTargetBitmap(new PixelSize(160, 60));
            using (var context = bitmap.CreateDrawingContext()) surface.Render(context);
            using var stream = new MemoryStream();
            bitmap.Save(stream, new PngBitmapEncoderOptions());
            using var pixels = SKBitmap.Decode(stream.ToArray());
            Assert.Contains(pixels.Pixels, pixel => pixel.Red == 73 && pixel.Green == 46 && pixel.Blue == 225);
            Assert.Contains(pixels.Pixels, pixel => pixel.Red == 194 && pixel.Green == 54 && pixel.Blue == 33);
        });
    }

    private static TerminalHostView MakeView() => new()
    {
        Font = SwiftFont.MonospacedSystemFont(12, SwiftFont.Weight.Regular),
        NativeForegroundColor = new SwiftColor(1, 1),
        NativeBackgroundColor = new SwiftColor(0, 1),
        CaretColor = new SwiftColor(0, 1),
    };

    // This invokes the production ProcessPipe.ReadabilityHandler boundary; no parallel parser or wiring lambda.
    private static void Output(TerminalHostView view, byte[] bytes) =>
        typeof(TerminalHostView).GetMethod("OnOutput", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, [bytes]);

    private static string SurfaceText(TerminalHostView view)
    {
        // The actual painter now composes the selectable text because TextBlock.Render is sealed.
        // Keep inspecting the production text control, not the parser or a parallel text snapshot.
        var surface = Assert.IsAssignableFrom<Decorator>(view.Child);
        return Assert.IsAssignableFrom<SelectableTextBlock>(surface.Child).Text ?? "";
    }

    private static void AssertAccessibleSelectionAndCopy(TerminalHostView view)
    {
        view.Width = 160;
        view.Height = 60;
        var window = new Avalonia.Controls.Window { Width = 160, Height = 60, Content = view };
        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var surface = Assert.IsAssignableFrom<Decorator>(view.Child);
            var text = Assert.IsAssignableFrom<SelectableTextBlock>(surface.Child);
            Assert.True(text.IsVisible);
            Assert.True(text.Bounds.Width > 0 && text.Bounds.Height > 0);
            var peer = ControlAutomationPeer.CreatePeerForElement(text)!;
            Assert.Equal(text.Text, peer.GetName());
            Assert.False(peer.IsOffscreen());

            // Source's W advance and font line height locate real cell hit tests; selection
            // and Ctrl+C must use production routing, not injected selection indices or Copy().
            var metrics = new FormattedText("W", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight), text.FontSize, Brushes.White);
            var scale = window.RenderScaling;
            var width = Math.Max(1, Math.Round(metrics.Width * scale) / scale);
            var height = Math.Ceiling(metrics.Height * scale) / scale;
            var from = surface.TranslatePoint(new Point(width * 0.2, height * 0.5), window)!.Value;
            var to = surface.TranslatePoint(new Point(width * 3.2, height * 0.5), window)!.Value;
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("bye", text.SelectedText);
            Assert.True(text.CanCopy);

            var clipboard = window.Clipboard!;
            var clear = clipboard.ClearAsync();
            HeadlessSession.RunDispatcherUntil(clear, TimeSpan.FromSeconds(5));
            clear.GetAwaiter().GetResult();
            window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
            window.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
            Dispatcher.UIThread.RunJobs();
            var copied = clipboard.TryGetTextAsync();
            HeadlessSession.RunDispatcherUntil(copied, TimeSpan.FromSeconds(5));
            Assert.Equal("bye", copied.GetAwaiter().GetResult());
        }
        finally { window.Close(); }
    }
}
