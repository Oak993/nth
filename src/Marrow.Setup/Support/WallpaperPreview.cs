namespace Marrow.Setup.Support;

/// <summary>Where the wallpaper in the preview came from (the chain's step that answered).</summary>
public enum WallpaperOrigin
{
    /// <summary><c>IDesktopWallpaper::GetWallpaper</c> for the window's monitor.</summary>
    Wallpaper,

    /// <summary>The transcoded copy Windows keeps (Spotlight, some slideshows).</summary>
    Transcoded,

    /// <summary><c>GetBackgroundColor</c>: a solid colour.</summary>
    Solid,

    /// <summary>Nothing readable: the theme gradient.</summary>
    Theme,
}

/// <summary>
/// What the preview draws as its desktop: a decoded image with Windows' position mode and the monitors' geometry, a solid
/// colour, or nothing (the theme gradient).
/// </summary>
public sealed record WallpaperScene(
    WallpaperOrigin Origin,
    WallpaperImage? Image,
    WallpaperPosition Position,
    WallpaperColor Background,
    WallpaperRect Monitor,
    WallpaperRect VirtualDesktop)
{
    public static WallpaperScene Theme { get; } = new(WallpaperOrigin.Theme, null, WallpaperPosition.Fill, default, new WallpaperRect(0, 0, 1920, 1080), new WallpaperRect(0, 0, 1920, 1080));

    public bool UsesTheme => Origin == WallpaperOrigin.Theme;
}

/// <summary>A preview's desktop drawn at a size: premultiplied BGRA pixels, and whether its labels are drawn in ink.</summary>
public sealed record WallpaperPicture(int Width, int Height, byte[] Bgra, bool InkLabels, double LabelLuminance);

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, "The wallpaper in the preview"; unit U2, ED-NEW-inst-u2-1). The desktop previews
/// draw the user's own wallpaper. Read on a background thread after the first frame, nothing copied or sent. The chain, in
/// order: <c>IDesktopWallpaper::GetWallpaper</c> for the monitor the window is on, the transcoded copy Windows keeps for
/// the current wallpaper (when the first is empty, as with Windows Spotlight, or over a limit), the background colour, the theme gradient.
/// Only local files of at most 64 MB are decoded (network and UNC paths are skipped), at twice the preview's pixel width,
/// within 400 ms (cancelled after); over any limit the next source is used. <see cref="Compose"/> draws Windows' position
/// mode (Fill crops to cover, Fit letterboxes on the background colour, Stretch scales, Tile repeats at the preview's scale,
/// Center draws at that scale on the background colour, Span crops the monitor's part of the virtual desktop) into the
/// preview's 16:10 frame (the monitor covers it, centred), and samples the luminance under the two labels: above 0.6 the
/// labels are ink with a pale halo, otherwise white with a dark shadow, as Windows does.
/// </summary>
public static class WallpaperPreview
{
    /// <summary>The labels' boxes as shares of the preview (the Recycle Bin's and nth's, from the component's em grid).</summary>
    public static readonly (double X, double Y, double Width, double Height)[] LabelBoxes =
    [
        (.015, .195 / .625, .3375, .0621 / .625),
        (.015, .435 / .625, .3375, .0621 / .625),
    ];

    /// <summary>Reads the chain for the monitor that holds <paramref name="windowCentre"/> (physical pixels).</summary>
    public static async Task<WallpaperScene> ReadAsync(IWallpaperSource source, (int X, int Y)? windowCentre, int previewPixelWidth, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        IReadOnlyList<WallpaperMonitor> monitors;
        try { monitors = source.Monitors(); }
        catch (Exception error) when (IsExpected(error)) { monitors = []; }
        var monitor = Pick(monitors, windowCentre);
        var bounds = monitor?.Bounds ?? new WallpaperRect(0, 0, 1920, 1080);
        var desktop = Union(monitors, bounds);
        var position = Safe(source.Position, WallpaperPosition.Fill);
        var background = Safe(source.BackgroundColor, null) ?? new WallpaperColor(0, 0, 0);
        var width = Math.Max(16, previewPixelWidth * WallpaperLimits.DecodeWidthFactor);
        if (position == WallpaperPosition.Span) width = Math.Max(width, (int)Math.Ceiling(width * (double)desktop.Width / Math.Max(1, bounds.Width)));

        if (monitor is not null && Safe(() => source.WallpaperPath(monitor.DevicePath), null) is { Length: > 0 } path)
        {
            var image = await TryDecodeAsync(source, path, width, cancellationToken).ConfigureAwait(false);
            if (image is not null) return new WallpaperScene(WallpaperOrigin.Wallpaper, image, position, background, bounds, desktop);
        }
        if (Safe(source.TranscodedWallpaperPath, null) is { Length: > 0 } transcoded)
        {
            var image = await TryDecodeAsync(source, transcoded, width, cancellationToken).ConfigureAwait(false);
            if (image is not null) return new WallpaperScene(WallpaperOrigin.Transcoded, image, position, background, bounds, desktop);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var solid = Safe(source.BackgroundColor, null);
        return solid is { } color
            ? new WallpaperScene(WallpaperOrigin.Solid, null, position, color, bounds, desktop)
            : WallpaperScene.Theme with { Monitor = bounds, VirtualDesktop = desktop };
    }

    /// <summary>True for a path the chain never opens: a UNC path or one Windows calls a network path.</summary>
    public static bool IsRemote(string path, WallpaperFile? file) =>
        file?.IsNetwork == true || (path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\.\", StringComparison.Ordinal))
        || path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase);

    private static async Task<WallpaperImage?> TryDecodeAsync(IWallpaperSource source, string path, int width, CancellationToken cancellationToken)
    {
        WallpaperFile? file;
        try { file = source.Inspect(path); }
        catch (Exception error) when (IsExpected(error)) { return null; }
        if (file is null || IsRemote(path, file) || file.Length <= 0 || file.Length > WallpaperLimits.MaxFileBytes) return null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(WallpaperLimits.DecodeBudget);
        try
        {
            var decode = source.DecodeAsync(path, width, budget.Token);
            var finished = await Task.WhenAny(decode, Task.Delay(WallpaperLimits.DecodeBudget + TimeSpan.FromMilliseconds(50), cancellationToken)).ConfigureAwait(false);
            if (finished != decode)
            {
                await budget.CancelAsync().ConfigureAwait(false);
                _ = decode.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                return null;
            }
            var image = await decode.ConfigureAwait(false);
            return image is { Width: > 0, Height: > 0 } && image.Bgra.Length >= image.Width * image.Height * 4 ? image : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error) when (IsExpected(error))
        {
            return null;
        }
    }

    /// <summary>Draws <paramref name="scene"/> at <paramref name="width"/> × <paramref name="height"/> pixels (null for the theme gradient).</summary>
    public static WallpaperPicture? Compose(WallpaperScene scene, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.UsesTheme || width <= 0 || height <= 0) return null;
        var pixels = new byte[width * height * 4];
        var bg = scene.Background;
        var mon = scene.Monitor;
        var mw = Math.Max(1, mon.Width);
        var mh = Math.Max(1, mon.Height);
        // the monitor covers the preview's 16:10 frame, centred
        var k = Math.Max(width / (double)mw, height / (double)mh);
        var ox = (width - mw * k) / 2;
        var oy = (height - mh * k) / 2;
        var image = scene.Image;
        for (var py = 0; py < height; py++)
        {
            for (var px = 0; px < width; px++)
            {
                // the pixel's centre in monitor coordinates (relative to the monitor's top left)
                var mx = (px + .5 - ox) / k;
                var my = (py + .5 - oy) / k;
                var (b, g, r) = (bg.B, bg.G, bg.R);
                if (image is not null && Map(scene, image, mx, my, mw, mh) is { } at) (b, g, r) = Sample(image, at.X, at.Y);
                var i = (py * width + px) * 4;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
        }
        var luminance = LabelLuminance(pixels, width, height);
        return new WallpaperPicture(width, height, pixels, luminance > WallpaperLimits.InkLabelLuminance, luminance);
    }

    /// <summary>The image pixel (fractional) a monitor point shows in the scene's position mode, or null for the background.</summary>
    public static (double X, double Y)? Map(WallpaperScene scene, WallpaperImage image, double mx, double my, double mw, double mh)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(image);
        double iw = image.Width, ih = image.Height;
        switch (scene.Position)
        {
            case WallpaperPosition.Stretch:
                return (mx / mw * iw, my / mh * ih);
            case WallpaperPosition.Fit:
            {
                var s = Math.Min(mw / iw, mh / ih);
                var x = (mx - (mw - iw * s) / 2) / s;
                var y = (my - (mh - ih * s) / 2) / s;
                return x < 0 || y < 0 || x >= iw || y >= ih ? null : (x, y);
            }
            case WallpaperPosition.Center:
            {
                // drawn at its own size (its pixels at the monitor's scale; the decode is at the preview's width)
                var s = ImageScale(scene, image);
                var x = (mx - (mw - iw * s) / 2) / s;
                var y = (my - (mh - ih * s) / 2) / s;
                return x < 0 || y < 0 || x >= iw || y >= ih ? null : (x, y);
            }
            case WallpaperPosition.Tile:
            {
                var s = ImageScale(scene, image);
                var x = mx / s % iw;
                var y = my / s % ih;
                return (x < 0 ? x + iw : x, y < 0 ? y + ih : y);
            }
            case WallpaperPosition.Span:
            {
                var d = scene.VirtualDesktop;
                double dw = Math.Max(1, d.Width), dh = Math.Max(1, d.Height);
                var s = Math.Max(dw / iw, dh / ih);
                var vx = mx + scene.Monitor.Left - d.Left;
                var vy = my + scene.Monitor.Top - d.Top;
                return ((vx - (dw - iw * s) / 2) / s, (vy - (dh - ih * s) / 2) / s);
            }
            default:
            {
                var s = Math.Max(mw / iw, mh / ih);
                return ((mx - (mw - iw * s) / 2) / s, (my - (mh - ih * s) / 2) / s);
            }
        }
    }

    /// <summary>The average relative luminance (WCAG, linear light) under the two labels.</summary>
    public static double LabelLuminance(byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        double sum = 0;
        var count = 0;
        foreach (var (bx, by, bw, bh) in LabelBoxes)
        {
            var x0 = (int)Math.Floor(bx * width);
            var y0 = (int)Math.Floor(by * height);
            var x1 = Math.Min(width, (int)Math.Ceiling((bx + bw) * width));
            var y1 = Math.Min(height, (int)Math.Ceiling((by + bh) * height));
            for (var y = Math.Max(0, y0); y < y1; y++)
            for (var x = Math.Max(0, x0); x < x1; x++)
            {
                var i = (y * width + x) * 4;
                sum += Luminance(bgra[i + 2], bgra[i + 1], bgra[i]);
                count++;
            }
        }
        return count == 0 ? 0 : sum / count;
    }

    public static double Luminance(byte r, byte g, byte b) => .2126 * Linear(r) + .7152 * Linear(g) + .0722 * Linear(b);

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4);
    }

    /// <summary>Center and Tile draw the image at its own size on the monitor; the decode shrank it to the preview's width.</summary>
    private static double ImageScale(WallpaperScene scene, WallpaperImage image) => image.SourceScale;

    private static (byte B, byte G, byte R) Sample(WallpaperImage image, double x, double y)
    {
        var w = image.Width;
        var h = image.Height;
        x = Math.Clamp(x - .5, 0, w - 1);
        y = Math.Clamp(y - .5, 0, h - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(w - 1, x0 + 1);
        var y1 = Math.Min(h - 1, y0 + 1);
        var fx = x - x0;
        var fy = y - y0;
        var p = image.Bgra;
        double Channel(int c)
        {
            var a = p[(y0 * w + x0) * 4 + c] * (1 - fx) + p[(y0 * w + x1) * 4 + c] * fx;
            var b = p[(y1 * w + x0) * 4 + c] * (1 - fx) + p[(y1 * w + x1) * 4 + c] * fx;
            return a * (1 - fy) + b * fy;
        }
        // premultiplied pixels over black: a transparent part of an image is the background's black, as on the desktop
        return ((byte)Math.Round(Channel(0)), (byte)Math.Round(Channel(1)), (byte)Math.Round(Channel(2)));
    }

    private static WallpaperMonitor? Pick(IReadOnlyList<WallpaperMonitor> monitors, (int X, int Y)? centre)
    {
        if (monitors.Count == 0) return null;
        if (centre is { } c)
            foreach (var m in monitors)
                if (c.X >= m.Bounds.Left && c.X < m.Bounds.Right && c.Y >= m.Bounds.Top && c.Y < m.Bounds.Bottom) return m;
        return monitors[0];
    }

    private static WallpaperRect Union(IReadOnlyList<WallpaperMonitor> monitors, WallpaperRect fallback)
    {
        if (monitors.Count == 0) return fallback;
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        foreach (var m in monitors)
        {
            l = Math.Min(l, m.Bounds.Left);
            t = Math.Min(t, m.Bounds.Top);
            r = Math.Max(r, m.Bounds.Right);
            b = Math.Max(b, m.Bounds.Bottom);
        }
        return new WallpaperRect(l, t, r, b);
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception error) when (IsExpected(error)) { return fallback; }
    }

    private static bool IsExpected(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException;
}
