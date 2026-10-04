// PORT-ONLY, owner-ruled addition (OD-0091 fourth amendment: "the EXACT site exports, never a re-creation"; first-release voice typing, VT-8,
// voice-SPEC V-11.1, V-11.3). The fragment shader of voice-orbs.js (`FRAG`, itself a function-by-function WGSL translation of the Liquid Orb Editor
// export; "Liquid Orb Editor" by LerSent001, MIT, Copyright (c) 2026 LerSent001: THIRD_PARTY_NOTICES) translated to SkSL function by function with the
// same names. The only changes from the GLSL ES 3.00 text are mechanical: vecN -> floatN, the std140 block `u.name` -> the member's place in one `uniform float4 u[35]`
// (OrbUniformLayout: `u.size` is `u[0].xy`, `u.time` is `u[0].z`, `u.colorA` is `u[10]`; `mrw` is `u[34]`), `main()` -> `main(float2 coords)` returning the colour, and gl_FragCoord (bottom-left origin) ->
// `float2(coords.x, u[0].xy.y - coords.y)` (Skia's coords are top-left). Only the two presets' branches are carried, as in the source.
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Orb;

internal static class OrbShaders
{
    /// <summary>The SkSL of both orbs: one program, the preset chosen by the style member of the uniform block (9 Siri Wave, 19 Voice Membrane).</summary>
    public const string Source = """
// the export's 136-float uniform block (the WGSL `Uniforms` struct, 34 float4) and, in u[34], the Marrow term mrw: x lift, y desat, z dark (1) / light (0), w 0.
// One array so a frame sets ONE uniform (OrbUniformLayout names which floats are which member).
uniform float4 u[35];
const float GL_CLEAR_EA = 0.995;   // effect.wgsl
const float GL_CLEAR_EB = 1.04;    // effect.wgsl

// effect.wgsl mfEdgeD
float mfEdgeD(float soft) {
  return soft - 0.005;
}
// effect.wgsl mfEdgeGlow
float3 mfEdgeGlow(float3 col, float2 uv, float2 ctr, float rad, float soft, float glow, float3 glowRGB) {
  if (glow <= 0.0) { return col; }
  float r = length(uv - ctr);
  float outside = smoothstep(rad - max(soft, 0.0005), rad + max(soft, 0.0005), r);
  return col + glowRGB * (glow * exp(-max(r - rad, 0.0) * 11.0) * outside);
}
// effect.wgsl glsFinishPresetFluid
float3 glsFinishPresetFluid(float3 colorIn, float2 p) {
  float3 color = colorIn;
  color = mix(color, u[14].rgb,
              u[2].y * 0.22 * smoothstep(0.15, 1.15, dot(p, float2(-0.32, 0.78))));
  color = color * (1.0 - u[2].y * 0.34
                  * smoothstep(-0.1, 1.2, dot(p, float2(0.45, -0.62))));
  color = color * (1.0 - u[2].y * 0.22 * smoothstep(0.72, 1.08, length(p)));
  return clamp(color, float3(0.0), float3(1.0));
}
// effect.wgsl glsFinishEmissionFluid
float3 glsFinishEmissionFluid(float3 colorIn, float2 p) {
  float3 color = colorIn;
  if (u[4].w > 0.5) {
    color = mix(color, u[14].rgb,
                u[2].y * 0.22 * smoothstep(0.15, 1.15, dot(p, float2(-0.32, 0.78))));
  }
  color = color * (1.0 - u[2].y * 0.34
                  * smoothstep(-0.1, 1.2, dot(p, float2(0.45, -0.62))));
  color = color * (1.0 - u[2].y * 0.22 * smoothstep(0.72, 1.08, length(p)));
  return clamp(color, float3(0.0), float3(1.0));
}
// effect.wgsl glsSiriBand
float2 glsSiriBand(float2 q, float drift, float phaseOffset, float amplitude,
                 float mainY, float envelope, float softness) {
  float y = amplitude * envelope * sin(q.x * 1.0 + drift + phaseOffset);
  float distanceToLine = abs(q.y - y);
  float line = 0.018 / (sqrt(distanceToLine * distanceToLine + softness * softness) + 0.026);
  float bandDistance = max(0.0, max(q.y - max(mainY, y), min(mainY, y) - q.y));
  float band = 0.018 / (bandDistance + 0.075);
  return float2(line, band);
}
// effect.wgsl glsSiriFluid
float3 glsSiriFluid(float2 p, float t) {
  float scale = 0.74 + u[1].y * 0.34;
  float2 q = p / scale;
  float xNorm = q.x;
  float envelopeBase = cos(1.57079633 * min(abs(0.9 * xNorm), 1.0));
  float envelope = envelopeBase * envelopeBase;
  float low = 0.5 + 0.5 * cos(t * 0.37);
  float mid = 0.5 + 0.5 * sin(t * 0.51 + 1.2);
  float high = 0.5 + 0.5 * cos(t * 0.73 + 2.1);
  float drift = t * 2.4;
  float mainAmplitude = 0.25 + u[1].w * 0.075 + low * 0.018;
  float bandAmplitude = mainAmplitude + mid * 0.025 + high * 0.018;
  float mainY = mainAmplitude * envelope * sin(q.x * 1.1 + drift);
  float separation = 1.85 + u[1].z * 0.2 + mid * 0.28;
  float softness = 0.035 + (1.0 - u[1].w) * 0.018 + mid * 0.006;

  float2 band0 = glsSiriBand(q, drift, -separation, bandAmplitude, mainY, envelope, softness);
  float2 band1 = glsSiriBand(q, drift, -separation * 0.34, bandAmplitude, mainY, envelope, softness);
  float2 band2 = glsSiriBand(q, drift, separation * 0.34, bandAmplitude, mainY, envelope, softness);
  float2 band3 = glsSiriBand(q, drift, separation, bandAmplitude, mainY, envelope, softness);
  float w0 = band0.x + band0.y;
  float w1 = band1.x + band1.y;
  float w2 = band2.x + band2.y;
  float w3 = band3.x + band3.y;
  float total = w0 + w1 + w2 + w3;
  float dominant0 = w0 * w0;
  float dominant1 = w1 * w1;
  float dominant2 = w2 * w2;
  float dominant3 = w3 * w3;
  float dominantTotal = dominant0 + dominant1 + dominant2 + dominant3;
  float3 spectral = (u[10].rgb * dominant0 + u[12].rgb * dominant1
                + u[11].rgb * dominant2 + u[13].rgb * dominant3)
                / max(dominantTotal, 0.0001);
  float energy = (1.0 - exp(-total * 0.58)) * envelope;
  float mainDistance = abs(q.y - mainY);
  float whiteCore = exp(-mainDistance * mainDistance / 0.0028) * envelope;
  float glassFill = (u[4].w > 0.5) ? 1.0 : 0.0;
  float3 atmosphere = mix(u[13].rgb, u[11].rgb,
                       smoothstep(-0.7, 0.7, q.y)) * 0.018 * glassFill;
  float3 color = atmosphere + spectral * energy * 1.14;
  color = color + u[14].rgb * whiteCore * (0.18 + 0.1 * low);
  float emissionMask = mix(smoothstep(0.08, 0.25, energy + whiteCore * 0.12),
                         1.0, glassFill);
  color = color * emissionMask;
  color = color / (float3(1.0) + color * 0.18);
  return glsFinishEmissionFluid(color, p);
}
// effect.wgsl glsVoiceWaveFluid
float3 glsVoiceWaveFluid(float2 p, float t) {
  float scale = 0.76 + u[1].y * 0.34;
  float2 q = p / scale;
  float rimEnvelope = pow(max(1.0 - q.x * q.x, 0.0), 0.72);
  float drift = t * 0.82;
  float amplitude = 0.2 + u[1].z * 0.018;
  float mainY = rimEnvelope * (amplitude * sin(q.x * 1.48 + drift)
              + 0.055 * sin(q.x * 3.2 - drift * 0.43 + 1.1));
  float distance_ = q.y - mainY;
  float width = 0.11 + (1.0 - u[1].w) * 0.075;
  float membrane = exp(-distance_ * distance_ / max(width * width, 0.001)) * rimEnvelope;
  float upperVeil = exp(-(distance_ - 0.105) * (distance_ - 0.105)
                      / max(width * width * 2.4, 0.001)) * rimEnvelope;
  float lowerVeil = exp(-(distance_ + 0.115) * (distance_ + 0.115)
                      / max(width * width * 2.8, 0.001)) * rimEnvelope;
  float crest = exp(-distance_ * distance_ / 0.0026) * rimEnvelope;
  float depth = sqrt(max(1.0 - clamp(dot(p, p), 0.0, 1.0), 0.0));
  float3 color = mix(u[10].rgb * 0.7, u[13].rgb * 0.34,
                  smoothstep(-0.82, 0.82, q.y));
  color = mix(color, u[11].rgb, upperVeil * 0.7);
  color = mix(color, u[12].rgb, lowerVeil * 0.62);
  color = color + mix(u[11].rgb, u[12].rgb, 0.46) * membrane * 0.34;
  color = color + u[14].rgb * crest * 0.14;
  color = color * (0.58 + 0.42 * depth);
  return glsFinishPresetFluid(color, p);
}
// effect.wgsl glsPresetFluid (only the two voice presets' branches are carried: 9 and 19)
float3 glsPresetFluid(float2 p, int style, float t) {
  if (style == 9) { return glsSiriFluid(p, t); }
  if (style == 19) { return glsVoiceWaveFluid(p, t); }
  return float3(0.0);
}
// effect.wgsl glsOver
float3 glsOver(float3 dst, float3 src, float a) {
  float k = clamp(a, 0.0, 1.0);
  return src * k + dst * (1.0 - k);
}
// effect.wgsl glsRefractionProfile
float glsRefractionProfile(float t) {
  float depth = clamp(t, 0.0, 1.0);
  float circular = sqrt(max(1.0 - (1.0 - depth) * (1.0 - depth), 0.0));
  return 1.0 - circular;
}
// effect.wgsl glsHighlightLobe
float glsHighlightLobe(float2 normal, float2 direction, float cut, float power) {
  float angular = clamp((dot(normal, direction) - cut) / max(1.0 - cut, 0.001), 0.0, 1.0);
  return pow(angular, power);
}
// effect.wgsl glsContourWave
float2 glsContourWave(float angle, float t) {
  int style = int(u[3].w + 0.5);
  if (style == 19) {
    float wave = sin(angle * 2.0 + t * 0.27) * 0.72
               + sin(angle * 4.0 - t * 0.16 + 2.1) * 0.28;
    float slope = cos(angle * 2.0 + t * 0.27) * 1.44
                + cos(angle * 4.0 - t * 0.16 + 2.1) * 1.12;
    return float2(wave, slope);
  }
  float wave = sin(angle * 3.0 + t * 0.62) * 0.52
             + sin(angle * 5.0 - t * 0.41 + 1.7) * 0.31
             + sin(angle * 2.0 + t * 0.23 + 3.1) * 0.17;
  float slope = cos(angle * 3.0 + t * 0.62) * 1.56
              + cos(angle * 5.0 - t * 0.41 + 1.7) * 1.55
              + cos(angle * 2.0 + t * 0.23 + 3.1) * 0.34;
  return float2(wave, slope);
}
// effect.wgsl glsContourStrength
float glsContourStrength() {
  if (u[3].w >= 18.5) { return 0.11; }
  return (u[3].w >= 15.5) ? 0.16 : 0.09;
}
// effect.wgsl glsContourScale
float glsContourScale(float2 uv, float t, float amount) {
  if (amount <= 0.0) { return 1.0; }
  float2 contour = glsContourWave(atan(uv.y, uv.x), t);
  return 1.0 + clamp(amount, 0.0, 1.0) * glsContourStrength() * contour.x;
}
// effect.wgsl glsContourNormal
float2 glsContourNormal(float2 uv, float rad, float t, float amount) {
  float distance_ = length(uv);
  if (distance_ <= 0.0001) { return float2(0.0); }
  float2 radial = uv / distance_;
  float2 contour = glsContourWave(atan(uv.y, uv.x), t);
  float slope = clamp(amount, 0.0, 1.0) * glsContourStrength() * contour.y;
  float2 tangent = float2(-radial.y, radial.x);
  return normalize(radial - tangent * (rad * slope / distance_));
}
// effect.wgsl glsRefractionNormal (its style-23 body, Refractive Gel, is not reached by these presets)
float2 glsRefractionNormal(float2 base, float2 p, float t, int style) {
  return base;
}
// effect.wgsl orbGlassLiquidAnim
float4 orbGlassLiquidAnim(float2 uv01) {
  float2 fc = float2(uv01.x, 1.0 - uv01.y) * u[0].xy;
  float2 uv = (2.0 * fc - u[0].xy) / max(min(u[0].xy.x, u[0].xy.y), 1.0);

  float rad = max(u[1].x, 0.05);
  float t = u[0].z * u[0].w;
  int s = int(u[3].w + 0.5);
  bool emissionOnly = u[4].w <= 0.5 && (s == 9 || s == 14 || s == 24);
  float contourRad = rad * glsContourScale(uv, t, u[5].y);

  if (length(uv) > contourRad * (1.01 + mfEdgeD(u[4].x))) {
    float3 halo = clamp(mfEdgeGlow(float3(0.0), uv, float2(0.0), contourRad,
                                u[4].x, u[4].y, u[21].rgb),
                     float3(0.0), float3(1.0));
    float haloAlpha = max(halo.r, max(halo.g, halo.b));
    return float4(halo, haloAlpha);
  }

  float2 p   = uv / contourRad;
  float pd = length(p);

  float clearFa = 1.0 - smoothstep(GL_CLEAR_EA, GL_CLEAR_EB, pd);
  float2 contourNormal = glsContourNormal(uv, rad, t, u[5].y);
  float2 normal = glsRefractionNormal(contourNormal, p, t, s);
  float edgeDepth = max(1.0 - pd, 0.0);
  float refractionWidth = 0.015 + 0.95 * clamp(u[3].x, 0.0, 1.0);
  float refractionT = edgeDepth / max(refractionWidth, 0.001);
  float refractionProfile = pow(glsRefractionProfile(refractionT), 0.68);
  float refractionAmount = 1.6 * clamp(u[5].x, 0.0, 1.0)
                         * refractionProfile;
  float2 refractedP = p - normal * refractionAmount;
  float3 fcol = float3(0.0);
  if (clearFa > 0.0) {
    if (s >= 9) {
      if (u[4].w > 0.5) {
        float channelSplit = 0.14 * clamp(u[2].w, 0.0, 2.0)
                           * clamp(u[5].x, 0.0, 1.0)
                           * refractionProfile;
        float3 redSample = glsPresetFluid(refractedP - normal * channelSplit, s, t);
        float3 greenSample = glsPresetFluid(refractedP, s, t);
        float3 blueSample = glsPresetFluid(refractedP + normal * channelSplit, s, t);
        fcol = float3(redSample.r, greenSample.g, blueSample.b);
      }
      else { fcol = glsPresetFluid(p, s, t); }
    }
    // (s < 9: glsFluid, the legacy sheet programs, not reached by these presets)
  }

  float lum = dot(fcol, float3(0.213, 0.715, 0.072));
  float3 clearSat = clamp(float3(lum) + (fcol - float3(lum)) * 1.22,
                       float3(0.0), float3(1.0));
  bool particleGlassOverlay = s == 24;
  float3 col = particleGlassOverlay ? float3(0.0) : glsOver(u[20].rgb, clearSat, 0.99 * clearFa);
  if (emissionOnly) {
    float signal = max(clearSat.r, max(clearSat.g, clearSat.b));
    float emissionCoverage = smoothstep(0.025, 0.16, signal);
    col = clearSat * emissionCoverage;
  }
  if (u[4].w > 0.5) {
    float surfaceWidth = particleGlassOverlay
      ? 0.09 + 0.12 * clamp(u[3].y, 0.0, 1.0)
      : 0.026 + 0.055 * clamp(u[3].y, 0.0, 1.0);
    float surfaceBand = (1.0 - smoothstep(0.0, surfaceWidth, edgeDepth)) * clearFa;
    float opticalRim = pow(surfaceBand, particleGlassOverlay ? 1.3 : 1.8);
    float innerRimAlpha = particleGlassOverlay
      ? opticalRim * u[5].x * 0.14
      : opticalRim * u[5].x * 0.45;
    col = glsOver(col, u[15].rgb, innerRimAlpha);

    float2 coolDirection = normalize(float2(0.84, 0.54));
    float2 warmDirection = normalize(float2(-0.62, -0.78));
    float coolSplit = glsHighlightLobe(normal, coolDirection, -0.32, 1.8);
    float warmSplit = glsHighlightLobe(normal, warmDirection, -0.28, 2.0);
    float dispersion = opticalRim * clamp(u[2].w, 0.0, 2.0)
                     * (0.8 + 0.8 * u[3].y);
    col = glsOver(col, u[16].rgb, dispersion * coolSplit);
    col = glsOver(col, u[17].rgb, dispersion * warmSplit);

    float edgeShadow = opticalRim * (0.015 + 0.15 * u[3].y)
                     * (0.15 + 0.85 * max(dot(normal, float2(0.45, -0.89)), 0.0));
    col = col * (1.0 - edgeShadow);

    float2 keyDirection = normalize(float2(-0.68, 0.73));
    float2 fillDirection = normalize(float2(0.74, -0.67));
    float key = opticalRim * glsHighlightLobe(normal, keyDirection, 0.2, 2.8)
              * clamp(u[2].z, 0.0, 2.0) * 1.4;
    float fill = opticalRim * glsHighlightLobe(normal, fillDirection, 0.4, 3.6)
               * clamp(u[2].z, 0.0, 2.0) * 1.0;
    col = glsOver(col, u[18].rgb, key);
    col = glsOver(col, u[19].rgb, fill);
  }

  float ballA = 1.0 - smoothstep(0.99 - mfEdgeD(u[4].x), 1.01 + mfEdgeD(u[4].x), pd);
  col = clamp(col * max(u[3].z, 0.0), float3(0.0), float3(1.0)) * ballA;
  float3 edged = mfEdgeGlow(col, uv, float2(0.0), contourRad,
                         u[4].x, u[4].y, u[21].rgb);
  float3 finalColor = clamp(edged, float3(0.0), float3(1.0));
  float emissionAlpha = max(finalColor.r, max(finalColor.g, finalColor.b));
  float sphereAlpha = clamp(max(ballA, emissionAlpha), 0.0, 1.0);
  float finalAlpha = (emissionOnly || particleGlassOverlay) ? emissionAlpha : sphereAlpha;
  return float4(finalColor, finalAlpha);
}
float4 main(float2 coords) {
  // shader-source.ts vs_main hands uv = (x, 1 - y) of the clip square in 0..1. GLSL's gl_FragCoord has a bottom-left origin and Skia's coords a
  // top-left one: the flip is taken here, so the image comes out the way the WebGL canvas shows it (voice-SPEC V-11.3, settled by the fidelity test)
  float2 fragCoord = float2(coords.x, u[0].xy.y - coords.y);
  float2 inUv = float2(fragCoord.x / u[0].xy.x, 1.0 - fragCoord.y / u[0].xy.y);
  // shader-source.ts fs_main
  float4 c = orbGlassLiquidAnim(inUv);
  float2 fc = float2(inUv.x, 1.0 - inUv.y) * u[0].xy;
  float2 uv = (2.0 * fc - u[0].xy) / max(min(u[0].xy.x, u[0].xy.y), 1.0);
  float rad = max(u[1].x, 0.05);
  float t = u[0].z * u[0].w;
  float contourRad = rad * glsContourScale(uv, t, u[5].y);
  float2 q = (2.0 * fc - u[0].xy) / u[0].xy;
  float fitEnd = 1.0;
  float fitFeather = 2.0 / max(min(u[0].xy.x, u[0].xy.y), 1.0);
  float fitStart = min(mix(contourRad, fitEnd, 0.5), fitEnd - fitFeather);
  float fit = 1.0 - smoothstep(fitStart, fitEnd, max(abs(q.x), abs(q.y)));
  float4 o = float4(c.rgb * fit, c.a * fit);

  // ---- Marrow (exactly nothing when u[34].x = u[34].y = 0): the error look drains the colour inside the ball;
  // the lift puts a soft shadow under the popped ball, behind what the source drew
  if (u[34].y > 0.0) {
    float l = dot(o.rgb, float3(0.2126, 0.7152, 0.0722));
    o.rgb = mix(o.rgb, float3(l) * 0.86, u[34].y * 0.78);
  }
  if (u[34].x > 0.0) {
    float r = length(uv - float2(0.0, -0.10 * rad));
    float sr = max(r - rad * 0.92, 0.0) / max(rad, 0.001);
    float sh = u[34].x * (0.30 + 0.12 * (1.0 - u[34].z)) * exp(-sr * sr * 7.0) * smoothstep(rad * 0.98, rad * 1.02, length(uv));
    o.a = o.a + sh * (1.0 - o.a);
  }
  return o;
}
""";

    private static readonly Lazy<SKRuntimeEffect> effect = new(() =>
        SKRuntimeEffect.CreateShader(Source, out var error) ?? throw new InvalidOperationException("The voice orb shader did not compile: " + error));

    /// <summary>The compiled program (compiled on first use; GpuWarmup compiles it ahead).</summary>
    public static SKRuntimeEffect Effect => effect.Value;
}
