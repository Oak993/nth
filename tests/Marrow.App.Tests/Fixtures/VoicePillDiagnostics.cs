using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.Voice;
using Marrow.UI.Port;
using Marrow.UI.Port.Voice.Orb;
using SkiaSharp;

namespace Marrow.App.Tests.Fixtures;

// Optional, exact-frame layer captures for locating V-T133 defects. Never changes the acceptance image or detector.
internal static class VoicePillDiagnostics
{
    public static bool Selected(string tag) => Environment.GetEnvironmentVariable("VT12_DIAG_CASES") is { } path
        && File.ReadAllLines(path).Contains(tag, StringComparer.Ordinal);

    public static void CaptureComponents(VoicePillScene scene, string tag,
        (int Stride, byte[] Bytes, PixelSize Size) frame, Rect pillRect, Point orb, double ballRadius,
        IReadOnlyList<(int X, int Y)[]> components)
    {
        var directory = Environment.GetEnvironmentVariable("VT12_DUMP")!;
        Directory.CreateDirectory(directory);
        var scale = scene.Window.RenderScaling;
        var orbView = VoicePillProbe.Slot(scene.View).Orb;
        var runtime = orbView.Handler!.Scene!.Runtime(orbView.Kind)!;
        bool rgba;
#pragma warning disable CS0618
        using (var original = scene.Window.CaptureRenderedFrame())
#pragma warning restore CS0618
        {
            using (var buffer = original!.Lock()) rgba = buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888;
            original.Save(Path.Combine(directory, "original-" + tag + ".png"), new PngBitmapEncoderOptions());
        }
        int[] Pixel(int x, int y) => [frame.Bytes[y * frame.Stride + x * 4 + (rgba ? 0 : 2)], frame.Bytes[y * frame.Stride + x * 4 + 1], frame.Bytes[y * frame.Stride + x * 4 + (rgba ? 2 : 0)], frame.Bytes[y * frame.Stride + x * 4 + 3]];
        int[] Median(int x, int y)
        {
            var samples = new int[49];
            var result = new int[3];
            for (var c = 0; c < 3; c++)
            {
                var n = 0;
                for (var dy = -3; dy <= 3; dy++)
                for (var dx = -3; dx <= 3; dx++) samples[n++] = Pixel(x + dx, y + dy)[c];
                Array.Sort(samples);
                result[c] = samples[24];
            }
            return result;
        }
        File.WriteAllText(Path.Combine(directory, "components-" + tag + ".json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            tag, scale, format = rgba ? "Rgba8888" : "Bgra8888", orbUniforms = runtime.Values.ToArray(), orbMrw = new[] { runtime.Mrw.Lift, runtime.Mrw.Desat, runtime.Mrw.Dark },
            pill = new[] { pillRect.X, pillRect.Y, pillRect.Width, pillRect.Height }, orb = new[] { orb.X, orb.Y }, ballRadius,
            components = components.Select((members, id) => new
            {
                id, size = members.Length,
                bounds = new[] { members.Min(p => p.X), members.Min(p => p.Y), members.Max(p => p.X), members.Max(p => p.Y) },
                pixels = members.Select(p => new { x = p.X, y = p.Y, pillOffset = new[] { (p.X + .5 - pillRect.X) / scale, (p.Y + .5 - pillRect.Y) / scale }, orbOffset = new[] { (p.X + .5 - orb.X) / scale, (p.Y + .5 - orb.Y) / scale }, rgba = Pixel(p.X, p.Y), median = Median(p.X, p.Y) })
            })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        // Transparent RGBA witness of the carried shader, at the same window-pixel origin and uniforms.
        using (var bitmap = new SKBitmap(new SKImageInfo(frame.Size.Width, frame.Size.Height, SKColorType.Rgba8888, SKAlphaType.Premul)))
        using (var canvas = new SKCanvas(bitmap))
        using (var renderer = new OrbRenderer())
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)scale);
            var matrix = orbView.TransformToVisual(scene.Window)!.Value;
            canvas.Concat(new SKMatrix { ScaleX = (float)matrix.M11, SkewX = (float)matrix.M21, TransX = (float)matrix.M31, SkewY = (float)matrix.M12, ScaleY = (float)matrix.M22, TransY = (float)matrix.M32, Persp2 = 1 });
            for (var k = 0; k < 2; k++)
            {
                var kind = (Marrow.Core.Port.Voice.VoiceOrbKind)k;
                var source = orbView.Handler.Scene.Runtime(kind);
                var opacity = orbView.Handler.Scene.Opacity(kind);
                if (source is not null && opacity > 0) renderer.Draw(canvas, null, source.Values, source.Mrw, source.DevicePx, source.RenderPx, (float)OrbScene.ViewPx, (float)opacity);
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(directory, "rgba-orb-" + tag + ".png"));
            encoded.SaveTo(file);
        }
        CaptureLayers(scene, tag, frame.Size);
    }

    private static void CaptureLayers(VoicePillScene scene, string tag, PixelSize size)
    {
        var directory = Environment.GetEnvironmentVariable("VT12_DUMP")!;
        var pill = VoicePillProbe.Pill(scene.View);
        var orb = VoicePillProbe.Slot(scene.View).Orb;
        var filtered = pill.Children.OfType<Border>().Single();
        var material = (Panel)filtered.Child!;
        var contents = pill.Children.OfType<Panel>().Single();
        var rim = material.Children.OfType<VoicePillRim>().Single();
        var materialMask = material.OpacityMask;
        var contentMask = contents.OpacityMask;
        var orbMask = orb.OpacityMask;
        var visibility = new[] { pill.IsVisible, filtered.IsVisible, contents.IsVisible, orb.IsVisible, pill.Cross.IsVisible, pill.Body.IsVisible, rim.IsVisible };
        var labelVisibility = pill.Labels.Select(label => label.IsVisible).ToArray();
        var edgeFilter = filtered.Effect;
        var materialClip = material.Clip;
        var bodyEffect = SwiftGlass.GetEffect(pill.Body);
        var bodyBackground = pill.Body.Background;
        var uniforms = orb.Handler!.Scene!.Runtime(orb.Kind)!.Values.ToArray();
        void Save(string name)
        {
            foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
            for (var i = 0; i < 3; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
#pragma warning disable CS0618
            using var bitmap = scene.Window.CaptureRenderedFrame();
#pragma warning restore CS0618
            bitmap!.Save(Path.Combine(directory, "bisect-" + name + "-" + tag + ".png"), new PngBitmapEncoderOptions());
        }
        void Layers(string name, bool glass, bool text, bool ball, bool mask, bool cross = false)
        {
            pill.IsVisible = true;
            filtered.IsVisible = glass;
            contents.IsVisible = text || cross;
            foreach (var label in pill.Labels) label.IsVisible = text;
            pill.Cross.IsVisible = cross;
            orb.IsVisible = ball;
            material.OpacityMask = mask ? materialMask : null;
            contents.OpacityMask = mask ? contentMask : null;
            Save(name);
        }
        try
        {
            Save("whole");
            // The mask is a coverage operation, not coloured ink. Its isolated RGBA image records the coverage field.
            var scale = scene.Window.RenderScaling;
            using (var coverage = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
            {
                using (var dc = coverage.CreateDrawingContext())
                using (dc.PushTransform(pill.TransformToVisual(scene.Window)!.Value))
                using (dc.PushGeometryClip(pill.ContentClip)) dc.DrawRectangle(materialMask, null, new Rect(pill.Bounds.Size));
                coverage.Save(Path.Combine(directory, "bisect-mask-" + tag + ".png"), new PngBitmapEncoderOptions());
            }
            Layers("background", false, false, false, false);
            Layers("glass", true, false, false, false);
            Layers("orb", false, false, true, false);
            Layers("text", false, true, false, false);
            Layers("mask-glass", true, false, false, true);
            Layers("mask-text", false, true, false, true);
            // Counterfactual mask/orb pair uses the same coverage field translated into orb coordinates.
            var group = new DrawingGroup();
            using (var dc = group.Open())
            using (dc.PushTransform(pill.TransformToVisual(orb)!.Value))
            using (dc.PushGeometryClip(pill.ContentClip)) dc.DrawRectangle(materialMask, null, new Rect(pill.Bounds.Size));
            orb.OpacityMask = new DrawingBrush(group) { SourceRect = new RelativeRect(new Rect(orb.Bounds.Size), RelativeUnit.Absolute), Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            Layers("mask-orb", false, false, true, false);
            orb.OpacityMask = orbMask;
            Layers("glass-orb", true, false, true, false);
            Layers("glass-text", true, true, false, false);
            Layers("orb-text", false, true, true, false);
            Layers("masked-glass-orb", true, false, true, true);
            Layers("masked-glass-text", true, true, false, true);
            Layers("cross", false, false, false, true, true);
            Layers("without-mask", true, true, true, false, true);
            pill.Body.IsVisible = false;
            Layers("rim", true, false, false, false);
            Layers("mask-rim", true, false, false, true);
            pill.Body.IsVisible = visibility[5]; rim.IsVisible = false;
            Layers("body", true, false, false, false);
            Layers("mask-body", true, false, false, true);
            // A geometry/coverage witness: same capsule, mask, filter and measured body colour, with no glass shader.
            // It is a counterfactual capture, not a reference picture or a production candidate.
            using (var painted = SKBitmap.Decode(Path.Combine(directory, "bisect-mask-body-" + tag + ".png")))
            {
                var position = pill.TranslatePoint(new Point(pill.Bounds.Width / 2, pill.Bounds.Height / 2), scene.Window)!.Value;
                var colour = painted.GetPixel((int)(position.X * scale), (int)(position.Y * scale));
                SwiftGlass.SetEffect(pill.Body, null);
                pill.Body.Background = new SolidColorBrush(Color.FromArgb(colour.Alpha, colour.Red, colour.Green, colour.Blue));
                Layers("flat-capsule-no-glass", true, false, false, true);
                filtered.Effect = null;
                Layers("flat-capsule-no-glass-unfiltered", true, false, false, true);
                filtered.Effect = edgeFilter;
                pill.Body.Background = bodyBackground;
                SwiftGlass.SetEffect(pill.Body, bodyEffect);
            }
            rim.IsVisible = visibility[6]; filtered.Effect = null;
            Layers("mask-glass-unfiltered", true, false, false, true);
            // Counterfactuals distinguish the pill's extra capsule coverage from the shared material's own SDF edge.
            filtered.Effect = edgeFilter;
            material.Clip = null;
            Layers("mask-glass-no-clip", true, false, false, true);
            rim.IsVisible = false;
            Layers("mask-body-no-clip", true, false, false, true);
            filtered.Effect = null;
            Layers("mask-body-no-clip-unfiltered", true, false, false, true);
            rim.IsVisible = visibility[6];
            Layers("mask-glass-no-clip-unfiltered", true, false, false, true);
        }
        finally
        {
            pill.Body.Background = bodyBackground;
            SwiftGlass.SetEffect(pill.Body, bodyEffect);
            pill.IsVisible = visibility[0]; filtered.IsVisible = visibility[1]; contents.IsVisible = visibility[2]; orb.IsVisible = visibility[3]; pill.Cross.IsVisible = visibility[4];
            pill.Body.IsVisible = visibility[5]; rim.IsVisible = visibility[6]; filtered.Effect = edgeFilter; material.Clip = materialClip;
            for (var k = 0; k < pill.Labels.Count; k++) pill.Labels[k].IsVisible = labelVisibility[k];
            material.OpacityMask = materialMask; contents.OpacityMask = contentMask; orb.OpacityMask = orbMask;
            Save("restored");
        }
        Xunit.Assert.Equal(uniforms, orb.Handler!.Scene!.Runtime(orb.Kind)!.Values.ToArray());
    }

    public static void Capture(VoicePillScene scene, string tag)
    {
        if (Environment.GetEnvironmentVariable("VT12_DIAG_CASES") is not null) return;
        if (Environment.GetEnvironmentVariable("VT12_DUMP") is not { } directory) return;
        var pill = VoicePillProbe.Pill(scene.View);
        var orb = VoicePillProbe.Slot(scene.View).Orb;
        var rim = pill.GetVisualDescendants().OfType<VoicePillRim>().Single();
        Rect Bounds(Control control) => new(control.TranslatePoint(default, scene.Window)!.Value, control.Bounds.Size);
        File.WriteAllText(Path.Combine(directory, "geometry-" + tag + ".txt"),
            $"scale={scene.Window.RenderScaling}\npill={Bounds(pill)}\ncross={Bounds(pill.Cross)}\ncrossClip={pill.Cross.ClipToBounds}, {pill.Cross.Clip}\norb={Bounds(orb)}\n");
        void Save(string layer)
        {
            foreach (var visual in scene.Window.GetVisualDescendants()) visual.InvalidateVisual();
            for (var i = 0; i < 3; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
#pragma warning disable CS0618
            using var frame = scene.Window.CaptureRenderedFrame();
#pragma warning restore CS0618
            frame!.Save(Path.Combine(directory, "layer-" + layer + "-" + tag + ".png"), new PngBitmapEncoderOptions());
        }
        Save("whole");
        foreach (var (name, control) in new (string, Control)[] { ("rim", rim), ("cross", pill.Cross), ("body", pill.Body), ("orb", orb), ("pill", pill) })
        {
            var shown = control.IsVisible;
            try { control.IsVisible = false; Save("without-" + name); }
            finally { control.IsVisible = shown; Save("restored-" + name); }
        }
    }
}
