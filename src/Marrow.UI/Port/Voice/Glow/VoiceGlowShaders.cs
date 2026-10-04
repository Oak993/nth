// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.2): the SkSL of the glow's layers. Each CSS gradient of
// voice-glow.js's stylesheet recipe is evaluated the way the browser evaluates it: an ellipse of given radii at a given point, colour stops along the normalised
// distance (premultiplied linear interpolation), the first gradient of a background on top. voice-glow 0.2.1 (Jakub Antalik, MIT; THIRD_PARTY_NOTICES).
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Glow;

internal static class VoiceGlowShaders
{
    /// <summary>
    /// <c>dt()</c>: the seven lobes of one layer as a background of seven <c>radial-gradient(ellipse rx ry at cx cy, colour 0%, transparent F%)</c>, the first lobe on top.
    /// <c>lobeA</c> is each lobe's centre and radii in layer px, <c>lobeC</c> its colour (alpha last, not premultiplied) and <c>fade</c> F as a fraction.
    /// </summary>
    public const string LobesSource = """
uniform float4 lobeA[7];
uniform float4 lobeC[7];
uniform float fade;

half4 main(float2 p) {
  float4 acc = float4(0.0);
  for (int i = 6; i >= 0; i--) {
    float2 r = lobeA[i].zw;
    if (r.x <= 0.0001 || r.y <= 0.0001) { continue; }
    float t = length((p - lobeA[i].xy) / r);
    float g = 1.0 - clamp(t / fade, 0.0, 1.0);
    float a = lobeC[i].a * g;
    float4 c = float4(lobeC[i].rgb * a, a);
    acc = c + acc * (1.0 - c.a);
  }
  return half4(acc);
}
""";

    /// <summary>
    /// One <c>radial-gradient(ellipse rx ry at cx cy, ...)</c> of up to four colour stops (positions as fractions of the radii, premultiplied colours); beyond the
    /// last stop the last colour. The stroke's wash, the epicentre and (as alpha) every mask use it.
    /// </summary>
    public const string EllipseSource = """
uniform float4 ell;
uniform float4 pos;
uniform float4 s0;
uniform float4 s1;
uniform float4 s2;
uniform float4 s3;

half4 main(float2 p) {
  if (ell.z <= 0.0001 || ell.w <= 0.0001) { return half4(s3); }
  float t = length((p - ell.xy) / ell.zw);
  float4 c = s3;
  if (t <= pos.x) { c = s0; }
  else if (t <= pos.y) { c = mix(s0, s1, (t - pos.x) / max(pos.y - pos.x, 0.000001)); }
  else if (t <= pos.z) { c = mix(s1, s2, (t - pos.y) / max(pos.z - pos.y, 0.000001)); }
  else if (t <= pos.w) { c = mix(s2, s3, (t - pos.z) / max(pos.w - pos.z, 0.000001)); }
  return half4(c);
}
""";

    /// <summary>
    /// A layer's picture (<c>content</c>, an image of the layer drawn at the layer's own scale) through its mask: the ellipse of <c>ie()</c> and, for the inner light, the
    /// union of two edge gradients (<c>mask-composite: intersect, add</c>). <c>zf</c> maps this draw's coordinates to the layer's. The mask is the alpha of its stops (<c>ma</c>).
    /// </summary>
    public const string LayerSource = """
uniform shader content;
uniform shader noise;
uniform float4 ell;
uniform float4 pos;
uniform float4 ma;
uniform float4 edge;
uniform float4 dispShift;
uniform float zf;
uniform float opacity;
uniform float dispScale;

// the gradient linear-gradient(white, transparent r, transparent len - r, white) along one axis, as its alpha
float edgeAlpha(float v, float len, float r) {
  float p1 = r;
  float p2 = max(len - r, p1);
  float p3 = max(len, p2);
  if (v <= p1) { return p1 > 0.0 ? 1.0 - v / p1 : 0.0; }
  if (v <= p2) { return 0.0; }
  return p3 > p2 ? clamp((v - p2) / (p3 - p2), 0.0, 1.0) : 1.0;
}

half4 main(float2 p) {
  float2 q = p * zf;
  float2 pc = p;
  if (dispScale != 0.0) {
    // feDisplacementMap(in2 = feColorMatrix(feOffset(feTurbulence)), scale, xChannelSelector R, yChannelSelector G = 0.5): a horizontal shift by scale * (R - 0.5)
    half4 n = noise.eval(p - dispShift.xy / zf);
    float rr = n.a > 0.0 ? n.r / n.a : 0.0;
    pc = float2(p.x + dispScale * (rr - 0.5) / zf, p.y);
  }
  half4 c = content.eval(pc);
  float m = 0.0;
  if (ell.z > 0.0001 && ell.w > 0.0001) {
    float t = length((q - ell.xy) / ell.zw);
    m = ma.w;
    if (t <= pos.x) { m = ma.x; }
    else if (t <= pos.y) { m = mix(ma.x, ma.y, (t - pos.x) / max(pos.y - pos.x, 0.000001)); }
    else if (t <= pos.z) { m = mix(ma.y, ma.z, (t - pos.y) / max(pos.z - pos.y, 0.000001)); }
    else if (t <= pos.w) { m = mix(ma.z, ma.w, (t - pos.z) / max(pos.w - pos.z, 0.000001)); }
  }
  if (edge.y > 0.5) {
    float av = edgeAlpha(q.y, edge.w, edge.x);
    float ah = edgeAlpha(q.x, edge.z, edge.x);
    m = m * (av + ah - av * ah);
  }
  return c * half(m * opacity);
}
""";

    private static Lazy<SKRuntimeEffect> Compile(string source, string what) => new(() =>
        SKRuntimeEffect.CreateShader(source, out var error) ?? throw new InvalidOperationException("The voice glow's " + what + " shader did not compile: " + error));

    private static readonly Lazy<SKRuntimeEffect> lobes = Compile(LobesSource, "lobes");
    private static readonly Lazy<SKRuntimeEffect> ellipse = Compile(EllipseSource, "ellipse");
    private static readonly Lazy<SKRuntimeEffect> layer = Compile(LayerSource, "layer");

    /// <summary>The lobes program (compiled on first use; GpuWarmup compiles it ahead).</summary>
    public static SKRuntimeEffect Lobes => lobes.Value;

    public static SKRuntimeEffect Ellipse => ellipse.Value;

    public static SKRuntimeEffect Layer => layer.Value;
}
