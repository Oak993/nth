// Source: DroppyCode/UI/Chrome/Chrome.swift (B3 52bc792), :171-182. Known app-rendered backdrop fixtures.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Marrow.UI.Tests.Fixtures;
internal sealed class GlassBackdropPattern : Control
{
    public bool Flat { get; init; }
    public bool IsolatedStroke { get; init; }
    public bool Gradient { get; init; }
    public bool Blue { get; set; }
    public int RenderCount { get; private set; }
    public override void Render(DrawingContext context)
    {
        RenderCount++;
        if (Gradient)
        {
            for (var x = 0; x < 256; x++)
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)x, (byte)x, (byte)x)), null, new Rect(x, 0, 1, 160));
            return;
        }
        context.DrawRectangle(Blue ? Brushes.Blue : Flat ? Brushes.Gray : Brushes.Black, null, new Rect(Bounds.Size));
        if (Flat) return;
        if (IsolatedStroke) context.DrawRectangle(Brushes.White, null, new Rect(127, 0, 2, 160));
        else for (var x = 0; x < 256; x += 4) context.DrawRectangle(Brushes.White, null, new Rect(x, 0, 2, 160));
    }
}

internal sealed class GlassScene : IDisposable
{
    private readonly Window window;
    public Window Window => window;
    public GlassScene(Control backdrop, Control glass)
    {
        window = new Window { Width = 256, Height = 160, RequestedThemeVariant = ThemeVariant.Dark,
            Content = new Panel { Children = { backdrop, glass } } };
        window.Show();
    }
    public static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
    public Func<int, int, Color> Pixels(string? evidenceName = null)
    {
        for (var i = 0; i < 4; i++) Drain();
        using var bitmap = window.CaptureRenderedFrame()!;
        if (evidenceName is not null) bitmap.Save(Path.Combine(AppContext.BaseDirectory, evidenceName), new PngBitmapEncoderOptions());
        var size = bitmap.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        var rgba = bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888;
        return (x, y) =>
        {
            var p = (y * size.Width + x) * 4;
            return Color.FromArgb(bytes[p + 3], bytes[p + (rgba ? 0 : 2)], bytes[p + 1], bytes[p + (rgba ? 2 : 0)]);
        };
    }
    public void Dispose() => window.Close();
}

