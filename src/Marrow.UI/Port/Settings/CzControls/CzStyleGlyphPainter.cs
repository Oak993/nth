// PORT-ONLY CZ-1, CZ-4 (CZ-T44): the Skia drawing of one style glyph (DESIGN gen.py:391-397, CZ-5.1), which makes no managed object once warm. A glyph turns every frame while it is seen, so its frame must not feed the
// collector: the paint, the path and the clip are kept, the two sweep shaders and the two blurs are made once for the process, and the turn is the CANVAS's: the sweep is made once with its centre at the origin, the canvas is
// turned by the angle about the capsule's centre, and the geometry is put in turned back (a kept path, rotated in place), so it lands where it always did while the sweep runs round it.
using SkiaSharp;
namespace Marrow.UI.Port.Settings;

internal sealed class CzStyleGlyphPainter
{
    private static readonly object Gate = new();
    private static SKShader? beamShader, auraShader;
    private static SKMaskFilter? beamBlur, auraBlur;

    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKPath path = new();
    private SKRoundRect? clip;
    private float clipWidth;

    private static readonly Dictionary<string, SKShader> shaderLights = [];
    private static SKMaskFilter? shaderBlur;

    // OD-0139: a shader light's glyph: its default pack around the capsule, a wide soft halo (the light spreads outward)
    private static SKShader ShaderLight(string style)
    {
        lock (Gate)
        {
            if (shaderLights.TryGetValue(style, out var shader)) return shader;
            var def = Marrow.Core.Models.ComposerLights.ShaderLightCatalog.Find(style)!;
            var pack = Marrow.Core.Models.ComposerLights.ShaderLightCatalog.Colours(def.Defaults, "#489CF9");
            SKColor[] colours = def.Id == "quies" ? [SKColor.Parse(pack[0]), SKColor.Parse("#FFF1E2"), SKColor.Parse(pack[0]), SKColor.Parse(pack[0])] : pack.Select(SKColor.Parse).ToArray();
            shader = SKShader.CreateSweepGradient(default, [.. colours, colours[0]], [0, .25f, .5f, .75f, 1], SKShaderTileMode.Clamp, 0, 360);
            shaderLights[style] = shader;
            return shader;
        }
    }

    private static SKShader Shader(string style)
    {
        if (style is not ("beam" or "aura")) return ShaderLight(style);
        lock (Gate)
        {
            if (style == "beam")
                return beamShader ??= SKShader.CreateSweepGradient(default,
                    [SKColors.Transparent, SKColors.Transparent, SKColor.Parse("#4F8BFF"), SKColor.Parse("#B46BFF"), SKColor.Parse("#FF6F7D"), SKColor.Parse("#FFB45C"), SKColors.Transparent, SKColors.Transparent],
                    [0, .48f, .60f, .72f, .84f, .92f, .98f, 1], SKShaderTileMode.Clamp, 0, 360);
            return auraShader ??= SKShader.CreateSweepGradient(default,
                [SKColor.Parse("#FF5500"), SKColor.Parse("#FF5500"), SKColors.Transparent, SKColors.Transparent, SKColor.Parse("#0055FF"), SKColor.Parse("#0055FF")],
                [0, .16f, .32f, .68f, .84f, 1], SKShaderTileMode.Clamp, 0, 360);
        }
    }

    private static SKMaskFilter Blur(string style)
    {
        lock (Gate) return style == "beam" ? beamBlur ??= SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6) : style == "aura" ? auraBlur ??= SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8) : shaderBlur ??= SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9);
    }

    /// <summary>Draws the glyph of <paramref name="style"/> ("beam", "aura", "off") in a tile <paramref name="width"/> wide and 56 high at the canvas's origin, its sweep turned to <paramref name="angle"/> degrees.</summary>
    internal void Draw(SKCanvas canvas, float width, string style, double angle, bool dark, float opacity)
    {
        if (clip is null || clipWidth != width)
        {
            clip?.Dispose();
            clip = new SKRoundRect(new SKRect(0, 0, width, 56), 8);
            clipWidth = width;
        }
        canvas.Save();
        canvas.ClipRoundRect(clip);
        paint.Style = SKPaintStyle.Fill;
        paint.Shader = null;
        paint.MaskFilter = null;
        paint.Color = dark ? new SKColor(255, 255, 255, (byte)(.07 * 255 * opacity)) : new SKColor(0, 0, 0, (byte)(.05 * 255 * opacity));
        canvas.DrawRect(0, 0, width, 56, paint);
        var capsule = new SKRect(width / 2 - 52, 16, width / 2 + 52, 40);
        if (style != "off")
        {
            var beam = style == "beam";
            var turn = (float)angle - 90;
            canvas.Save();
            canvas.Translate(width / 2, 28);
            canvas.RotateDegrees(turn);
            var back = SKMatrix.CreateRotationDegrees(-turn);
            var shaderLight = style is not ("beam" or "aura");
            var inflate = beam ? 3f : shaderLight ? 9f : 6f;
            var radius = beam ? 15f : shaderLight ? 21f : 18f;
            path.Reset();
            path.AddRoundRect(new SKRect(-52 - inflate, -12 - inflate, 52 + inflate, 12 + inflate), radius, radius);
            path.Transform(back);
            paint.Shader = Shader(style);
            paint.Color = SKColors.White.WithAlpha((byte)(.55 * 255 * opacity));
            paint.MaskFilter = Blur(style);
            canvas.DrawPath(path, paint);
            paint.MaskFilter = null;
            if (shaderLight)
            {
                // the rim: a thin line of the same colours on the glass's edge
                paint.Color = SKColors.White.WithAlpha((byte)(.8 * 255 * opacity));
                path.Reset();
                path.AddRoundRect(new SKRect(-52.5f, -12.5f, 52.5f, 12.5f), 12.5f, 12.5f);
                path.AddRoundRect(new SKRect(-51.5f, -11.5f, 51.5f, 11.5f), 11.5f, 11.5f);
                path.FillType = SKPathFillType.EvenOdd;
                path.Transform(back);
                canvas.DrawPath(path, paint);
                path.FillType = SKPathFillType.Winding;
            }
            if (beam)
            {
                paint.Color = SKColors.White.WithAlpha((byte)(255 * opacity));
                path.Reset();
                path.AddRoundRect(new SKRect(-53, -13, 53, 13), 13, 13);
                path.AddRoundRect(new SKRect(-51.5f, -11.5f, 51.5f, 11.5f), 11.5f, 11.5f);
                path.FillType = SKPathFillType.EvenOdd;
                path.Transform(back);
                canvas.DrawPath(path, paint);
            }
            paint.Shader = null;
            canvas.Restore();
        }
        var primary = dark ? new SKColor(236, 236, 236) : new SKColor(28, 28, 30);
        paint.Color = primary.WithAlpha((byte)(.05 * 255 * opacity));
        canvas.DrawRoundRect(capsule, 12, 12, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = primary.WithAlpha((byte)(.16 * 255 * opacity));
        canvas.DrawRoundRect(capsule, 12, 12, paint);
        canvas.Restore();
    }
}
