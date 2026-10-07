using System.Runtime.InteropServices;
using System.Text.Json;
using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;
using Marrow.App.Tests.Wallpapers;

if (args[0] == "--scan")
{
    var folder = Path.GetFullPath(args[1]);
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
    using var output = File.CreateText(args[2]);
    foreach (var input in manifest.RootElement.GetProperty("renders").EnumerateArray())
    {
        if (input.GetProperty("wallpaper").GetString() is not ("tunnel" or "stairwell")) continue;
        var reference = new WallpaperFidelityCase(input);
        using var expected = SKBitmap.Decode(Path.Combine(folder, reference.File));
        using var actual = WallpaperRendererBridge.Render(reference);
        var result = WallpaperPerceptualComparison.Measure(expected, actual);
        var pixels = new List<object>();
        for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
            {
                var delta = WallpaperPerceptualComparison.Delta(expected.GetPixel(x,y), actual.GetPixel(x,y));
                if (delta > WallpaperPerceptualComparison.PixelLimit) pixels.Add(new { X=x, Y=y, Delta=delta });
            }
        output.WriteLine(JsonSerializer.Serialize(new { reference.Label, reference.File,
            result.Mean, result.Max, Pixels=pixels }));
    }
    return;
}

// Developer-only instrumentation: the production catalogue and uniform inputs are unchanged.
// A request supplies a diagnostic main, keeping the original shader prefix and compiler settings.
var request = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var wall = WallpaperCatalogue.Get(WallpaperKind.Static, request.GetProperty("wallpaper").GetString()!);
var source = wall.Source[..wall.Source.IndexOf("void main()", StringComparison.Ordinal)]
    + request.GetProperty("main").GetString();
source = WallpaperCatalogue.SkSL(wall with { Source = source });
using var effect = SKRuntimeEffect.CreateShader(source, out var error)
    ?? throw new InvalidOperationException(error);
var uniforms = new SKRuntimeEffectUniforms(effect);
foreach (var property in request.GetProperty("uniforms").EnumerateObject())
{
    if (property.Value.ValueKind == JsonValueKind.Array)
        uniforms[property.Name] = property.Value.EnumerateArray().Select(v => v.GetSingle()).ToArray();
    else uniforms[property.Name] = property.Value.GetSingle();
}
using var shader = effect.ToShader(uniforms);
using var surface = SKSurface.Create(new SKImageInfo(160, 100, SKColorType.RgbaF32, SKAlphaType.Premul));
using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
surface.Canvas.DrawRect(0, 0, 160, 100, paint);
using var snapshot = surface.Snapshot();
using var bitmap = new SKBitmap(new SKImageInfo(160, 100, SKColorType.RgbaF32, SKAlphaType.Premul));
if (!snapshot.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
    throw new InvalidOperationException("Could not read the float probe");
var bytes = new byte[160 * 100 * 16];
for (var y = 0; y < 100; y++)
    Marshal.Copy(bitmap.GetPixels() + y * bitmap.RowBytes, bytes, y * 160 * 16, 160 * 16);
File.WriteAllBytes(args[1], bytes);
