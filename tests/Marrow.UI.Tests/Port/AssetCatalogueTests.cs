using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
public sealed class AssetCatalogueTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-215")]
    public void All_77_non_tour_images_resolve_with_their_declared_intent_and_original_bytes()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "tests", "Marrow.UI.Tests", "Port", "AssetCatalogueManifest.json")));
            Assert.Equal(77, manifest.RootElement.GetArrayLength());
            foreach (var entry in manifest.RootElement.EnumerateArray())
            {
                var name = entry.GetProperty("name").GetString()!;
                Assert.False(name.StartsWith("tour-", StringComparison.Ordinal));
                var image = SwiftImage.Named(name);
                Assert.False(image.IsEmpty, name);
                Assert.Equal(entry.GetProperty("intent").GetString() == "template" ? ImageRenderingMode.Template : ImageRenderingMode.Original, image.EffectiveRenderingMode);
                if (name.StartsWith("provider-", StringComparison.Ordinal) || name.StartsWith("mcp-", StringComparison.Ordinal) || name.StartsWith("Update", StringComparison.Ordinal))
                {
                    using var contents = AssetLoader.Open(new Uri("avares://Marrow.UI/Assets/Images/" + name + ".imageset/Contents.json"));
                    Assert.Equal(entry.GetProperty("contentsSha256").GetString(), Convert.ToHexString(SHA256.HashData(contents)));
                }
                foreach (var file in entry.GetProperty("files").EnumerateArray())
                {
                    var filename = file.GetProperty("filename").GetString()!;
                    var asset = name is "droppy-logo" or "hydra-mark" or "badge-edit" || name.StartsWith("hydra-head-", StringComparison.Ordinal)
                        ? filename : name + ".imageset/" + filename;
                    using var stream = AssetLoader.Open(new Uri("avares://Marrow.UI/Assets/Images/" + asset));
                    Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(stream)));
                    if (filename.EndsWith(".svg", StringComparison.Ordinal)) Assert.NotEmpty(image.Icon!.Shapes);
                    else if (file.GetProperty("scale").GetString() == "2x") Assert.NotNull(image.Bitmap2x);
                    else Assert.NotNull(image.Bitmap);
                }
            }
        });
    }

    [Fact]
    [Trait("Decision", "ED-215")]
    public void Provider_vectors_draw_real_pixels_preserving_Metas_gradient_and_OpenCodes_opacity()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            // Reference provider-meta.imageset/meta.svg: #0064E0 -> #0082FB -> #0064E0 stroke.
            var meta = SwiftImage.Named("provider-meta").Resizable().AspectRatio(ContentMode.Fit);
            meta.Width = 100; meta.Height = 100;
            var window = new Window { Width = 100, Height = 100, Background = Brushes.White, Content = meta };
            window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                using var bitmap = Capture(window);
                var blues = new List<Color>();
                for (var y = 0; y < bitmap.PixelSize.Height; y++)
                    for (var x = 0; x < bitmap.PixelSize.Width; x++)
                    {
                        var pixel = Pixel(bitmap, x, y);
                        if (pixel.R < 20 && pixel.B > 180) blues.Add(pixel);
                    }
                Assert.NotEmpty(blues);
                Assert.True(blues.Select(c => c.G).Distinct().Count() > 3); // Gradient, not a flat replacement colour.
            }
            finally { window.Close(); }

            // Reference provider-opencode.imageset/opencode.svg: x8..24,y16..32 fill-opacity=0.3.
            var opencode = SwiftImage.Named("provider-opencode").Resizable().AspectRatio(ContentMode.Fit).ForegroundStyle(SwiftColor.Rgb(0xFF0000));
            opencode.Width = 85.1; opencode.Height = 85.1;
            window = new Window { Width = 86, Height = 86, Background = Brushes.White, Content = opencode };
            opencode.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            opencode.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            window.Show(); Dispatcher.UIThread.RunJobs();
            try
            {
                using var bitmap = Capture(window);
                var inner = Pixel(bitmap, 43, 51);
                Assert.Equal(255, inner.R); Assert.InRange(inner.G, (byte)173, (byte)184); Assert.Equal(inner.G, inner.B);
                var solid = Pixel(bitmap, 19, 43);
                Assert.Equal(Colors.Red, solid);
            }
            finally { window.Close(); }
        });
    }

    private static WriteableBitmap Capture(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var bitmap = new WriteableBitmap(frame.PixelSize, frame.Dpi, frame.Format ?? PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        frame.CopyPixels(new PixelRect(frame.PixelSize), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);
        return bitmap;
    }
    private static Color Pixel(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[4]; Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
        return buffer.Format == PixelFormat.Rgba8888 ? Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2]) : Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
}
