using System.Globalization;
using System.Text.RegularExpressions;

namespace Marrow.UI.Theme.Wallpapers;

/// <summary>ED-397: preserve the page's signed powers, dot reduction and constant rotation angles.</summary>
internal static class WallpaperShaderArithmetic
{
    public const string Helpers = """
        float wl_dot(float2 a, float2 b) { return dot(a,b); }
        // Skia reduces x+(y+z). Rotating both operands gives z+(x+y), the page's (x+y)+z rounding, without scalar expansion.
        float wl_dot(float3 a, float3 b) { return dot(a.zxy,b.zxy); }
        float wl_dot(float4 a, float4 b) { float4 p = a*b; return ((p.x+p.y)+p.z)+p.w; }
        float wl_pow2(float x) { return x*x; }
        float2 wl_pow2(float2 x) { return x*x; }
        float3 wl_pow2(float3 x) { return x*x; }
        float4 wl_pow2(float4 x) { return x*x; }
        float wl_pow4(float x) { float y = x*x; return y*y; }
        float2 wl_pow4(float2 x) { float2 y = x*x; return y*y; }
        float3 wl_pow4(float3 x) { float3 y = x*x; return y*y; }
        float4 wl_pow4(float4 x) { float4 y = x*x; return y*y; }

        """;

    public static string Translate(string source)
    {
        var matches = Regex.Matches(source, @"\bpow\s*\(");
        // Work backwards so nested calls are already translated when an outer call is parsed.
        foreach (Match match in matches.Reverse())
        {
            var start = source.IndexOf('(', match.Index) + 1; var depth = 0; var comma = -1; var end = -1;
            for (var i = start; i < source.Length; i++)
            {
                if (source[i] == '(') depth++;
                else if (source[i] == ')') { if (depth == 0) { end = i; break; } depth--; }
                else if (source[i] == ',' && depth == 0) comma = i;
            }
            if (comma < 0 || end < 0) continue;
            var exponent = source[(comma + 1)..end].Trim();
            if (!double.TryParse(exponent, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n is not (2 or 4)) continue;
            var replacement = $"wl_pow{(int)n}({source[start..comma]})";
            source = source[..match.Index] + replacement + source[(end + 1)..];
        }
        source = Regex.Replace(source, @"\bdot\s*\(", "wl_dot(");
        // Fold constant rotation angles at their call sites. The raster sin/cos approximation otherwise moves a hard crease.
        source = Regex.Replace(source, @"\b(?:vec2|mat2)\s+rot\s*\(", "inline $0");
        return Regex.Replace(source, @"\bfloat\s+starN\s*\(", "inline $0");
    }
}
