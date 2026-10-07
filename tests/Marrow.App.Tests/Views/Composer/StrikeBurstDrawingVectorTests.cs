using System.Globalization;
using System.Text.Json;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

public sealed class StrikeBurstDrawingVectorTests
{
    private static void Near(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);
    private static string Rgba(SKColor c, double a) => $"rgba({c.Red},{c.Green},{c.Blue},{a.ToString("F3", CultureInfo.InvariantCulture)})";

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Every_locked_burst_ring_wash_and_glint_matches_geometry_colour_alpha_and_order()
    {
        var type = typeof(Strike).Assembly.GetType("Marrow.App.Views.Composer.StrikeBurstDrawing"); Assert.NotNull(type);
        dynamic drawing = Activator.CreateInstance(type)!;
        using var doc = StrikeVectors.Read("burst-draw.json");
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var maker = row.GetProperty("maker").GetString()!; var ink = StrikeInk.Of(StrikeOracleLiquid.From(maker, "dark"));
            Near(row.GetProperty("ink").GetDouble(), ink.Amount);
            var b = row.GetProperty("burst");
            var burst = new StrikeBurst(b.GetProperty("t").GetDouble(), 0, 0, b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(), b.GetProperty("r").GetDouble(), b.GetProperty("a").GetDouble(), b.GetProperty("rot").GetDouble(), b.GetProperty("n").GetInt32(), 5, 0);
            // gen:393: reconstruct the same time expression uniformly; no expected call is altered.
            var time = burst.T + row.GetProperty("u").GetDouble() * 0.19;
            drawing.Resolve(new StrikeGeometry(298, 34, 272, 24), burst, time, ink);
            try
            {
                foreach (var layer in new[] { "bubs", "zapb", "zapk" })
                {
                    var ops = row.GetProperty(layer).EnumerateArray().ToArray();
                    var arcs = ops.Where(o => o.GetProperty("op").GetString() == "arc").ToArray();
                    var moves = ops.Where(o => o.GetProperty("op").GetString() == "moveTo").ToArray();
                    var expectedCount = layer == "bubs" ? (int)drawing.RingCount * (int)drawing.ArcCount : (int)drawing.GlintCount * (layer == "zapk" && ink.Amount < 0.01 ? 0 : 1);
                    Assert.Equal(expectedCount, arcs.Length); Assert.Equal(arcs.Length, moves.Length);
                    for (var i = 0; i < arcs.Length; i++)
                    {
                        dynamic a = layer == "bubs" ? drawing.Arcs[i % (int)drawing.ArcCount] : drawing.Glints[i];
                        var radius = (double)a.Radius + (layer == "zapk" ? 0.7 : 0); var start = layer == "bubs" ? (double)a.Start : 0; var end = layer == "bubs" ? (double)a.End : 6.283185;
                        var args = arcs[i].GetProperty("args"); Near(args[0].GetDouble(), (double)a.X); Near(args[1].GetDouble(), (double)a.Y); Near(args[2].GetDouble(), radius); Near(args[3].GetDouble(), start); Near(args[4].GetDouble(), end);
                        var move = moves[i].GetProperty("args"); Near(move[0].GetDouble(), (double)a.X + radius * Math.Cos(start)); Near(move[1].GetDouble(), (double)a.Y + radius * Math.Sin(start));
                    }
                    var strokes = ops.Where(o => o.GetProperty("op").GetString() == "stroke").ToArray();
                    Assert.Equal(layer == "bubs" ? (int)drawing.RingCount : 0, strokes.Length);
                    for (var i = 0; i < strokes.Length; i++)
                    {
                        Near(strokes[i].GetProperty("lw").GetDouble(), (double)drawing.RingWidth + (i == 1 ? 1.4 : 0));
                        Assert.Equal(Rgba(i == 0 ? SKColors.White : new SKColor(20, 22, 30), i == 0 ? (double)drawing.RingAlpha : (double)drawing.DarkRingAlpha), strokes[i].GetProperty("ss").GetString());
                        Assert.Equal(i == 0 ? "source-over" : "destination-over", strokes[i].GetProperty("gco").GetString()); Assert.Equal("round", strokes[i].GetProperty("cap").GetString());
                    }
                    var fills = ops.Where(o => o.GetProperty("op").GetString() == "fill").ToArray();
                    Assert.Equal(layer == "zapb" || layer == "zapk" && ink.Amount >= 0.01 ? 1 : 0, fills.Length);
                    if (fills.Length > 0) Assert.Equal(Rgba(layer == "zapb" ? ink.Tint.Inner : ink.InkColor, layer == "zapb" ? (double)drawing.GlintAlpha : (double)drawing.InkGlintAlpha), fills[0].GetProperty("fs").GetString());
                    var washes = ops.Where(o => o.GetProperty("op").GetString() == "fillRect").ToArray();
                    Assert.Equal(layer == "zapb" ? (int)drawing.WashCount : 0, washes.Length);
                    var scales = ops.Where(o => o.GetProperty("op").GetString() == "scale").ToArray();
                    var translations = ops.Where(o => o.GetProperty("op").GetString() == "translate").ToArray();
                    for (var i = 0; i < washes.Length; i++)
                    {
                        dynamic w = drawing.Washes[i]; Near(washes[i].GetProperty("ga").GetDouble(), (double)w.Alpha);
                        Near(scales[i].GetProperty("args")[0].GetDouble() * 100, (double)w.Radius); Near(scales[i].GetProperty("args")[1].GetDouble() * 100, (double)w.Radius);
                        Near(translations[i].GetProperty("args")[0].GetDouble(), (double)w.X); Near(translations[i].GetProperty("args")[1].GetDouble(), (double)w.Y);
                        var stops = washes[i].GetProperty("fs").GetProperty("stops"); Assert.Equal(3, stops.GetArrayLength());
                        Assert.Equal($"rgba({ink.Tint.Wash.Red},{ink.Tint.Wash.Green},{ink.Tint.Wash.Blue},1)", stops[0][1].GetString());
                        Near(stops[1][0].GetDouble(), 0.45); Assert.EndsWith(",0.35)", stops[1][1].GetString(), StringComparison.Ordinal); Near(stops[2][0].GetDouble(), 1); Assert.EndsWith(",0)", stops[2][1].GetString(), StringComparison.Ordinal);
                    }
                }
            }
            catch (Xunit.Sdk.XunitException error) { throw new Xunit.Sdk.XunitException($"maker={maker} n={burst.N} u={row.GetProperty("u")}: {error.Message}"); }
        }
    }
}
