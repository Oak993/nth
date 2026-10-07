using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Views.Composer;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using SkiaSharp;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;

namespace Marrow.App.Tests.Fixtures;

// VT-12, OD-0125: the approved design page's pill (voice-composer.html, drawn in headless Chrome by tests/fixtures/voice/make-pill-design-refs.py) over the port's own frame without the
// pill, at the same scale, state, theme and backdrop. V-T133 asks of each speck on the port's pill whether the design has a speck within 1 pixel of the same spot. This reads the locked
// references (every file checked against the manifest's SHA-256) and runs V-T133's own detector on them; the Dump half writes the generator's input (the pill-less frame and the state
// the pill is in) when VT12_DESIGN_DUMP names a folder. The references are never regenerated to make a failure pass.
internal static class VoicePillDesignReference
{
    /// <summary>The checked-in folder of references and the manifest.</summary>
    public static string Folder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "tests", "fixtures", "voice", "ref-pill-design");
            if (File.Exists(Path.Combine(candidate, "manifest.json"))) return candidate;
        }
        throw new FileNotFoundException("tests/fixtures/voice/ref-pill-design/manifest.json");
    }

    public static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>The manifest's files: path relative to the folder, and its SHA-256.</summary>
    public static IReadOnlyDictionary<string, string> Files()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "manifest.json")));
        return manifest.RootElement.GetProperty("files").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString()!);
    }

    /// <summary>Whether the design has a reference for this identity (the port had specks when the references were made).</summary>
    public static bool Has(string tag)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Folder(), "manifest.json")));
        return manifest.RootElement.GetProperty("identities").TryGetProperty(tag, out _);
    }

    /// <summary>
    /// The design's specks for an identity, as window pixels: V-T133's detector (7x7 median, more than 8 levels, 8-connected components of at most 9 pixels that touch no ink of the
    /// design's own label and cross and lie outside the ball and 1 pixel) run on the locked render.
    /// </summary>
    public static HashSet<(int X, int Y)> Specks(string tag, Func<int, int, bool> insideBall)
    {
        var folder = Folder();
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "manifest.json")));
        var entry = manifest.RootElement.GetProperty("identities").GetProperty(tag);
        var crop = entry.GetProperty("crop").EnumerateArray().Select(v => v.GetInt32()).ToArray(); // window pixels: x, y, width, height
        var files = manifest.RootElement.GetProperty("files");
        (byte[] Bytes, int Width, int Height) Read(string name)
        {
            var path = Path.Combine(folder, name);
            if (Sha256(path) != files.GetProperty(name).GetString()) throw new InvalidOperationException($"{name} is not the locked reference (its SHA-256 differs from the manifest)");
            using var bitmap = SKBitmap.Decode(path);
            using var converted = bitmap.Copy(SKColorType.Bgra8888);
            return (converted.Bytes, converted.Width, converted.Height);
        }
        var render = Read(entry.GetProperty("render").GetString()!);
        var ink = Read(entry.GetProperty("ink").GetString()!);
        if (render.Width != crop[2] || render.Height != crop[3] || ink.Width != crop[2] || ink.Height != crop[3]) throw new InvalidOperationException($"{tag}: the references are not the crop's size");
        var frame = (Stride: render.Width * 4, render.Bytes, Size: new PixelSize(render.Width, render.Height));
        var (outlier, w, h) = ComposerVoiceCleanPillTests.Outliers(frame, new Rect(0, 0, render.Width, render.Height));
        bool Inked(int x, int y) => ink.Bytes[(y * w + x) * 4] < 255; // black ink on white: any pixel the glyph touches
        var seen = new bool[w * h];
        var specks = new HashSet<(int X, int Y)>();
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            if (!outlier[y * w + x] || seen[y * w + x]) continue;
            var stack = new Stack<(int, int)>();
            var members = new List<(int X, int Y)>();
            stack.Push((x, y));
            seen[y * w + x] = true;
            while (stack.Count > 0)
            {
                var (cx, cy) = stack.Pop();
                members.Add((cx, cy));
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[ny * w + nx] || !outlier[ny * w + nx]) continue;
                    seen[ny * w + nx] = true;
                    stack.Push((nx, ny));
                }
            }
            if (members.Count <= 9 && !members.Any(m => Inked(m.X, m.Y) || insideBall(crop[0] + m.X, crop[1] + m.Y)))
                foreach (var m in members) specks.Add((crop[0] + m.X, crop[1] + m.Y));
        }
        return specks;
    }

    /// <summary>
    /// The generator's input for one identity: the frame with the pill hidden (the same orb, glow and composer) and the state the pill is in (its opacity, each label's opacity, position and
    /// sweep, the cross's opacity and scale, the orb slot's size), under <paramref name="folder"/>. Hides the pill for the capture and shows it again.
    /// </summary>
    public static void Dump(VoicePillScene scene, string tag, string folder, Rect area)
    {
        Directory.CreateDirectory(folder);
        var rig = scene.Rig;
        var scale = scene.Window.RenderScaling;
        var state = rig.Main.OnMain(() =>
        {
            var pill = Pill(scene.View);
            var origin = pill.TranslatePoint(default, scene.Window)!.Value;
            var slot = Slot(scene.View);
            var cross = pill.Cross.TransformToVisual(pill)!.Value;
            return new
            {
                tag,
                scale,
                style = pill.Style.ToString(),
                pill = new[] { origin.X, origin.Y, pill.Bounds.Width, pill.Bounds.Height },
                pillOpacity = pill.Opacity,
                orbSlotDiameter = slot.Diameter,
                cross = new { opacity = pill.Cross.Opacity, scale = cross.M11, x = cross.M31, y = cross.M32, size = pill.Cross.Bounds.Width },
                labels = pill.Labels.Select(label => new
                {
                    words = label.Words, runs = label.Runs.Select(r => new { r.Text, r.Weight }), opacity = label.Opacity, sweep = label.Sweep, fontSize = label.FontSize,
                    x = label.TransformToVisual(pill)!.Value.M31, y = label.TransformToVisual(pill)!.Value.M32, height = label.Bounds.Height,
                }).ToArray(),
                area = new[] { area.X, area.Y, area.Width, area.Height },
            };
        });
        File.WriteAllText(Path.Combine(folder, $"state-{tag}.json"), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        rig.Main.OnMain(() => Pill(scene.View).IsVisible = false);
        try
        {
            rig.PumpWindow(scene.Window);
            rig.Main.OnMain(() =>
            {
                for (var k = 0; k < 3; k++)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }
#pragma warning disable CS0618
                using var frame = scene.Window.CaptureRenderedFrame();
#pragma warning restore CS0618
                frame!.Save(Path.Combine(folder, $"pillless-{tag}.png"), new PngBitmapEncoderOptions());
            });
        }
        finally { rig.Main.OnMain(() => Pill(scene.View).IsVisible = true); }
    }
}
