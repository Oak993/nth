using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>OD-0110: the embedded design data is the catalogue and shader source of truth.</summary>
public static class WallpaperCatalogue
{
    private static readonly Lazy<IReadOnlyList<WallpaperDefinition>> catalogue = new(Load);
    public static IReadOnlyList<WallpaperDefinition> All => catalogue.Value;
    public static WallpaperDefinition Get(WallpaperKind kind, string id) => All.Single(w => w.Kind == kind && w.Id == id);

    private static JsonDocument Resource(string kind)
    {
        using var stream = typeof(WallpaperCatalogue).Assembly.GetManifestResourceStream($"Marrow.Wallpapers.{kind}.json")
            ?? throw new InvalidOperationException($"Missing wallpaper resource: {kind}");
        return JsonDocument.Parse(stream);
    }

    private static IReadOnlyList<WallpaperDefinition> Load()
    {
        var list = new List<WallpaperDefinition>();
        foreach (var kind in new[] { WallpaperKind.Static, WallpaperKind.Animated })
        {
            using var data = Resource(kind == WallpaperKind.Static ? "static" : "animated");
            var root = data.RootElement;
            foreach (var w in root.GetProperty("wallpapers").EnumerateArray())
            {
                var packs = w.GetProperty("packs").EnumerateArray().Select(p => new WallpaperPack(
                    p.TryGetProperty("id", out var id) ? id.GetString()! : p.GetProperty("name").GetString()!,
                    p.GetProperty("name").GetString()!,
                    p.GetProperty("dark").EnumerateArray().Select(c => c.GetString()!).ToArray(),
                    p.GetProperty("light").EnumerateArray().Select(c => c.GetString()!).ToArray())).ToArray();
                WallpaperOption Option(JsonElement o, string id, string key) => new(id, o.GetProperty("label").GetString()!,
                    o.GetProperty(key).GetDouble(), o.TryGetProperty("min", out var min) ? min.GetDouble() : 0,
                    o.TryGetProperty("max", out var max) ? max.GetDouble() : 1,
                    o.TryGetProperty("lo", out var lo) ? lo.GetString() : null, o.TryGetProperty("hi", out var hi) ? hi.GetString() : null);
                var options = kind == WallpaperKind.Static
                    ? new[] { Option(w.GetProperty("grain"), "grain", "def"), Option(w.GetProperty("option"), "option", "def") }
                    : w.GetProperty("options").EnumerateArray().Select(o => Option(o, o.GetProperty("id").GetString()!, "default")).ToArray();
                var source = kind == WallpaperKind.Static
                    ? root.GetProperty("contract").GetProperty("lib").GetString() + "\n" + w.GetProperty("src").GetString()
                        + "\n" + root.GetProperty("contract").GetProperty("main").GetString()
                    : root.GetProperty("common").GetString() + "\n" + w.GetProperty("glsl").GetString();
                list.Add(new(kind, w.GetProperty("id").GetString()!, w.GetProperty("name").GetString()!,
                    w.GetProperty("tier").GetString()!, w.GetProperty("desc").GetString()!, packs, options,
                    kind == WallpaperKind.Animated ? w.GetProperty("period").GetDouble() : 0, source,
                    kind == WallpaperKind.Animated ? w.GetProperty("defaults").GetProperty("pack").GetString()!
                        : packs[w.GetProperty("defaultPack").GetInt32()].Id,
                    kind == WallpaperKind.Animated ? Premium(w) : null,
                    kind == WallpaperKind.Static ? new StaticWallpaperData(w) : null));
            }
        }
        return list.AsReadOnly();
    }

    private static double[] Numbers(JsonElement list) => list.EnumerateArray().Select(n => n.GetDouble()).ToArray();

    private static WallpaperLookKey[] Looks(JsonElement set) => new[] { "dawn", "day", "dusk", "night" }.Select(key =>
    {
        var look = set.GetProperty(key); var tint = Numbers(look.GetProperty("tint"));
        return new WallpaperLookKey(Numbers(look.GetProperty("dL")), Numbers(look.GetProperty("kC")), Numbers(look.GetProperty("dh")), tint[0], tint[1]);
    }).ToArray();

    /// <summary>OPTIONS-FORMAT: the roles, Dynamic looks, Perspective, Focus and Match data of an animated wallpaper, as written in wallpapers.json.</summary>
    private static WallpaperPremium Premium(JsonElement w)
    {
        var dynamic = w.GetProperty("dynamic"); var sun = dynamic.GetProperty("sun");
        var m = w.GetProperty("match"); var warm = m.GetProperty("dark_warm"); var perspective = w.GetProperty("perspective");
        return new(w.GetProperty("roles").EnumerateArray().Select(Numbers).ToArray(),
            new(Looks(dynamic.GetProperty("light")), Looks(dynamic.GetProperty("dark")),
                new[] { "dawn", "day", "dusk", "night" }.Select(key => Numbers(sun.GetProperty(key))).ToArray()),
            new(perspective.GetProperty("mode").GetString()!, perspective.GetProperty("default").GetBoolean()),
            w.GetProperty("focus").GetProperty("default").GetDouble(), w.GetProperty("defaults").GetProperty("dynamic").GetBoolean(),
            new(Numbers(m.GetProperty("off")), m.GetProperty("hero").GetInt32(), m.GetProperty("ck").GetDouble(), m.GetProperty("lk").GetDouble(),
                Numbers(m.GetProperty("dl")), Numbers(m.GetProperty("dc")), Numbers(m.GetProperty("cm")),
                new(Numbers(warm.GetProperty("band")), warm.GetProperty("ground_hue").GetDouble(), warm.GetProperty("mid_hue").GetDouble(),
                    warm.GetProperty("ground_below").GetDouble(), warm.GetProperty("mid_below").GetDouble(), warm.GetProperty("mid_amount").GetDouble(),
                    warm.GetProperty("kc").GetDouble(), warm.GetProperty("dl").GetDouble(), warm.GetProperty("bright_dl").GetDouble(),
                    warm.GetProperty("bright_kc").GetDouble())));
    }

    /// <summary>Mechanical GLSL-subset translation, with the source host's coordinate/dither contract.</summary>
    public static string SkSL(WallpaperDefinition wall)
    {
        var source = WallpaperBranchArithmetic.Translate(wall.Id, WallpaperShaderArithmetic.Translate(wall.Source));
        if (wall.Kind == WallpaperKind.Static)
        {
            source = source.Replace("void main()", "half4 main(float2 coord)", StringComparison.Ordinal)
                .Replace("gl_FragCoord.xy", "float2(coord.x, u_res.y - coord.y)", StringComparison.Ordinal)
                .Replace("o = vec4(c, 1.);", "return half4(c, 1.);", StringComparison.Ordinal);
        }
        else source += """

            float wl_ign(vec2 v) { return fract(52.9829189 * fract(dot(v, vec2(.06711056, .00583715)))); }
            half4 main(float2 coord) {
                vec2 fc = vec2(coord.x, u_res.y - coord.y);
                vec2 p = (fc - .5 * u_res) * (2. / u_res.y) + u_tilt * u_pan;
                vec3 c = wall(p, u_t);
                c += u_dither * (wl_ign(fc) + wl_ign(fc + vec2(37., 17.)) - 1.) / 255.;
                return half4(c, 1.);
            }
            """;
        source = Regex.Replace(source, @"\b(vec[234]|mat[234])\b", m => m.Value.StartsWith("vec", StringComparison.Ordinal)
            ? "float" + m.Value[3] : "float" + m.Value[3] + "x" + m.Value[3]);
        var header = wall.Kind == WallpaperKind.Static
            ? "uniform float2 u_res; uniform float u_dpr; uniform float3 u_pal[6]; uniform float u_dark; uniform float u_grain; uniform float u_opt;\n"
                + "uniform float2 u_tilt; uniform float u_tg; uniform float2 u_light; uniform float2 u_light0; uniform float u_lw; uniform float u_focus;\n"
            : "uniform float2 u_res; uniform float u_t; uniform float3 u_pal[6]; uniform float u_dark; uniform float2 u_opt; uniform float u_px;\n"
                + "uniform float2 u_tilt; uniform float2 u_sun; uniform float u_focus; uniform float u_pan; uniform float u_dither;\n";
        return header + WallpaperShaderArithmetic.Helpers + WallpaperBranchArithmetic.Helpers(wall.Id) + source;
    }

    public static SKRuntimeEffect Compile(WallpaperDefinition wall) => SKRuntimeEffect.CreateShader(SkSL(wall), out var error)
        ?? throw new InvalidOperationException($"{wall.Kind}/{wall.Id}: {error}");
}
