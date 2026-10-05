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
                    o.TryGetProperty("max", out var max) ? max.GetDouble() : 1);
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
                        : packs[w.GetProperty("defaultPack").GetInt32()].Id));
            }
        }
        return list.AsReadOnly();
    }

    /// <summary>Mechanical GLSL-subset translation, with the source host's coordinate/dither contract.</summary>
    public static string SkSL(WallpaperDefinition wall)
    {
        var source = wall.Source;
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
                vec2 p = (fc - .5 * u_res) * (2. / u_res.y);
                vec3 c = wall(p, u_t);
                c += (wl_ign(fc) + wl_ign(fc + vec2(37., 17.)) - 1.) / 255.;
                return half4(c, 1.);
            }
            """;
        source = Regex.Replace(source, @"\b(vec[234]|mat[234])\b", m => m.Value.StartsWith("vec", StringComparison.Ordinal)
            ? "float" + m.Value[3] : "float" + m.Value[3] + "x" + m.Value[3]);
        var header = wall.Kind == WallpaperKind.Static
            ? "uniform float2 u_res; uniform float u_dpr; uniform float3 u_pal[6]; uniform float u_dark; uniform float u_grain; uniform float u_opt;\n"
            : "uniform float2 u_res; uniform float u_t; uniform float3 u_pal[6]; uniform float u_dark; uniform float2 u_opt; uniform float u_px;\n";
        return header + source;
    }

    public static SKRuntimeEffect Compile(WallpaperDefinition wall) => SKRuntimeEffect.CreateShader(SkSL(wall), out var error)
        ?? throw new InvalidOperationException($"{wall.Kind}/{wall.Id}: {error}");
}
