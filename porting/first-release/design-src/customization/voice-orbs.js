// Marrow voice orbs: Siri Wave (the default) and Voice Membrane, for the composer's voice button (OD-0091).
//
// SOURCE: "Liquid Orb Editor" by LerSent001 (https://github.com/LerSent001/orb, https://lersent001.github.io/orb/),
// MIT License, Copyright (c) 2026 LerSent001 (notice in NOTICES.md beside this file; it travels with this code).
// This file is that site's "Copy code" Web export, run in WebGL2 instead of WebGPU:
//   - SEEDS: the export's own `stateSeeds` for the two presets, verbatim (the owner's pasted exports,
//     E:/Droppy/work/voice/reference/orb-exports/seeds.json; byte-identical to createWebExport() run on the source's
//     src/presets.ts and src/orb-states.ts: tools/build-orb-src.cjs). 136 floats, the WGSL `Uniforms` layout.
//   - The runtime: srgbToLinear, linearToSrgb, mixSrgb, the transition sample (numeric lerp, colours in linear light),
//     applyAudioUniforms with audioRules and audioFlowStrengths, motionPhase += dt * speed and time = phase / speed:
//     verbatim from the export.
//   - The shader: effect.wgsl and src/shader-source.ts (fs_main), translated WGSL -> GLSL ES 3.00 line by line, each
//     function named after its source. The uniform block is the WGSL struct, member for member: WGSL's uniform
//     layout and std140 agree for f32 / vec2 / vec4, so the export's 544-byte buffer is uploaded unchanged. Only the
//     branches these two presets reach are carried (flow indexes 9 and 19); the others are marked where they are cut.
// MARROW (ours, the state layer, off by default so the export renders unchanged):
//   - framing: `radius` (a source parameter) is set so the ball is `ball` CSS px inside a `size` px canvas, times
//     `scale` (the pop); `edgeSoftness` (a source parameter) can be set per size so the limb stays about 1.5 device px.
//   - speed: the caller can drive the speed (rest, hover, voice, processing) in place of the speed rule.
//   - glow: the caller's glow amount is written to the source's own `edgeGlow` (its Outer Glow).
//   - `lift` (a soft shadow under the popped ball) and `desat` (the calm error look): two terms after the source's
//     colour, exactly zero when 0. Rendering at 2x below 128 device px (as knob-orbs-2.js), filtered down.
//
// API (as the knob-orb modules):
//   orbs = [{ id: 'siri-wave' | 'voice-membrane', key, name, line, idleSpeed, thinkingSpeed, make(canvas, opts) }]
//   make(canvas, opts) -> { frame(t, s), resize(cssPx, ballPx), dispose(), setDark(dark), recolor() (no-op), phase }
//   opts: { size: canvas CSS px, ball: ball CSS px (omit for the source's own radius), dark, context: 'own'|'shared',
//           supersample: true (default below 128 device px), edge: 'pixel' (default when ball is given) | 'source' }
//   frame(t seconds, s): s = {
//     act 0..1        the Idle -> Thinking progress, eased by the caller as the export eases it
//     bands           { low, mid, high, all } 0..1: applyAudioUniforms, verbatim
//     speed           Marrow: a multiple of the Idle speed; omit to run the export's own speed (values[3])
//     scale, glow, lift, desat, reduceMotion }

const SEEDS = {"siri":{"idle":[1,1,0,0.2460000067949295,0.7200000286102295,0.3384000062942505,1.6640000343322754,0.23999999463558197,1.9800000190734863,0.11999999731779099,0.2800000011920929,0.23999999463558197,0.18000000715255737,0.18000000715255737,1.3600000143051147,9,0.004999999888241291,0,0,1,0.4399999976158142,0,2,0.41999998688697815,0.7699999809265137,0.23000000417232513,65,0,0,1,0.2199999988079071,0.25,0.7200000286102295,5,0.41999998688697815,1.25,0.550000011920929,0.30000001192092896,1.2000000476837158,0.699999988079071,0.7098039388656616,0.6509804129600525,0.45490196347236633,1,0.3686274588108063,0.529411792755127,0.5803921818733215,1,0.6039215922355652,0.3921568691730499,0.5411764979362488,1,0.38823530077934265,0.35686275362968445,0.5411764979362488,1,0.7137255072593689,0.7686274647712708,0.8235294222831726,1,1,1,1,1,0.6078431606292725,0.95686274766922,1,1,0.772549033164978,0.6627451181411743,1,1,0.9176470637321472,0.95686274766922,1,1,0.8627451062202454,0.9176470637321472,1,1,0.0117647061124444,0.01568627543747425,0.03529411926865578,1,0.42352941632270813,0.40784314274787903,0.5607843399047852,1,0.9686274528503418,0.9843137264251709,1,1,0.9372549057006836,0.9647058844566345,0.9921568632125854,1,0.8784313797950745,0.9333333373069763,0.9764705896377563,1,0.8313725590705872,0.9019607901573181,0.9686274528503418,1,0.7333333492279053,0.8352941274642944,0.9529411792755127,1,0.6509804129600525,0.7803921699523926,0.9411764740943909,1,0.529411792755127,0.6901960968971252,0.9215686321258545,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1],"thinking":[1,1,0,0.8199999928474426,0.7200000286102295,0.36000001430511475,3.200000047683716,0.5,2.200000047683716,0.11999999731779099,0.2800000011920929,0.23999999463558197,0.18000000715255737,0.18000000715255737,2,9,0.004999999888241291,0,0,1,0.4399999976158142,0,2,0.41999998688697815,0.7699999809265137,0.23000000417232513,65,0,0,1,0.2199999988079071,0.25,0.7200000286102295,5,0.41999998688697815,1.25,0.550000011920929,0.30000001192092896,1.2000000476837158,0.699999988079071,1,0.8470588326454163,0.41960784792900085,1,0.5098039507865906,0.95686274766922,1,1,1,0.48235294222831726,0.8352941274642944,1,0.5568627715110779,0.42352941632270813,1,1,1,1,1,1,1,1,1,1,0.6078431606292725,0.95686274766922,1,1,0.772549033164978,0.6627451181411743,1,1,0.9176470637321472,0.95686274766922,1,1,0.8627451062202454,0.9176470637321472,1,1,0.0117647061124444,0.01568627543747425,0.03529411926865578,1,0.5843137502670288,0.42352941632270813,1,1,0.9686274528503418,0.9843137264251709,1,1,0.9372549057006836,0.9647058844566345,0.9921568632125854,1,0.8784313797950745,0.9333333373069763,0.9764705896377563,1,0.8313725590705872,0.9019607901573181,0.9686274528503418,1,0.7333333492279053,0.8352941274642944,0.9529411792755127,1,0.6509804129600525,0.7803921699523926,0.9411764740943909,1,0.529411792755127,0.6901960968971252,0.9215686321258545,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1]},"voiceWave":{"idle":[1,1,0,0.26600000262260437,0.699999988079071,0.3312000036239624,1.1959999799728394,0.1932000070810318,2.200000047683716,0.07999999821186066,0.2199999988079071,0.36000001430511475,0.18000000715255737,0.20000000298023224,0.8370000123977661,19,0.004999999888241291,0,0,1,0.47999998927116394,0.029999999329447746,2,0.41999998688697815,0.7699999809265137,0.23000000417232513,65,0,0,1,0.2199999988079071,0.25,0.7200000286102295,5,0.41999998688697815,1.25,0.550000011920929,0.30000001192092896,1.2000000476837158,0.699999988079071,0.0313725508749485,0.019607843831181526,0.04313725605607033,1,0.4156862795352936,0.18431372940540314,0.4117647111415863,1,0.5490196347236633,0.27450981736183167,0.32156863808631897,1,0.3333333432674408,0.27450981736183167,0.49803921580314636,1,0.7098039388656616,0.5411764979362488,0.6470588445663452,1,1,1,1,1,0.8941176533699036,0.545098066329956,1,1,1,0.47058823704719543,0.5647059082984924,1,1,0.9450980424880981,0.9803921580314636,1,0.9058823585510254,0.8509804010391235,1,1,0.007843137718737125,0.003921568859368563,0.019607843831181526,1,0.42352941632270813,0.24313725531101227,0.4470588266849518,1,0.9686274528503418,0.9843137264251709,1,1,0.9372549057006836,0.9647058844566345,0.9921568632125854,1,0.8784313797950745,0.9333333373069763,0.9764705896377563,1,0.8313725590705872,0.9019607901573181,0.9686274528503418,1,0.7333333492279053,0.8352941274642944,0.9529411792755127,1,0.6509804129600525,0.7803921699523926,0.9411764740943909,1,0.529411792755127,0.6901960968971252,0.9215686321258545,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1],"thinking":[1,1,0,0.949999988079071,0.699999988079071,0.36000001430511475,2.5999999046325684,0.46000000834465027,2.200000047683716,0.07999999821186066,0.2199999988079071,0.36000001430511475,0.18000000715255737,0.20000000298023224,1.350000023841858,19,0.004999999888241291,0,0,1,0.47999998927116394,0.10000000149011612,2,0.41999998688697815,0.7699999809265137,0.23000000417232513,65,0,0,1,0.2199999988079071,0.25,0.7200000286102295,5,0.41999998688697815,1.25,0.550000011920929,0.30000001192092896,1.2000000476837158,0.699999988079071,0.03529411926865578,0.0117647061124444,0.054901961237192154,1,0.8078431487083435,0.1725490242242813,0.7960784435272217,1,1,0.3607843220233917,0.4431372582912445,1,0.48235294222831726,0.32549020648002625,1,1,1,0.8509804010391235,0.9411764740943909,1,1,1,1,1,0.8941176533699036,0.545098066329956,1,1,1,0.47058823704719543,0.5647059082984924,1,1,0.9450980424880981,0.9803921580314636,1,0.9058823585510254,0.8509804010391235,1,1,0.007843137718737125,0.003921568859368563,0.019607843831181526,1,0.8078431487083435,0.1725490242242813,0.7960784435272217,1,0.9686274528503418,0.9843137264251709,1,1,0.9372549057006836,0.9647058844566345,0.9921568632125854,1,0.8784313797950745,0.9333333373069763,0.9764705896377563,1,0.8313725590705872,0.9019607901573181,0.9686274528503418,1,0.7333333492279053,0.8352941274642944,0.9529411792755127,1,0.6509804129600525,0.7803921699523926,0.9411764740943909,1,0.529411792755127,0.6901960968971252,0.9215686321258545,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1,0.43529412150382996,0.6196078658103943,0.9098039269447327,1]},"background":{"siri":"#030409","voiceWave":"#020105"}};
const STYLE = { siri: 9, voiceWave: 19 };

// ---------------------------------------------------------------- the export's runtime, verbatim
const audioRules = [[3, "all", 0, 0.7, 5], [6, "mid", 0.85, 0, 7], [21, "low", 0.075, 0, 1], [10, "high", 0.16, 0, 2], [14, "all", 0, 0.12, 4]];
const audioFlowStrengths = { "9": 0.8, "10": 0.65, "11": 0.65, "14": 0.75, "19": 1, "21": 0.7 };
function applyAudioUniforms(values, bands) {
  const strength = audioFlowStrengths[Math.round(values[15])] ?? 0;
  if (!strength) return;
  for (const [index, band, additive, proportional, ceiling] of audioRules) {
    const input = bands[band];
    const level = (Number.isFinite(input) ? Math.max(0, Math.min(1, input)) : 0) * strength;
    if (!level) continue;
    values[index] = Math.min(Math.max(ceiling, values[index]), values[index] * (1 + proportional * level) + additive * level);
  }
}
function srgbToLinear(value) {
  return value <= 0.04045
    ? value / 12.92
    : ((value + 0.055) / 1.055) ** 2.4;
}
function linearToSrgb(value) {
  return value <= 0.0031308
    ? value * 12.92
    : 1.055 * value ** (1 / 2.4) - 0.055;
}
function mixSrgb(from, to, progress) {
  return linearToSrgb(
    srgbToLinear(from) + (srgbToLinear(to) - srgbToLinear(from)) * progress,
  );
}
// the export's sampleTransition(), with the progress handed in (the caller eases it as transitionProgress() does)
function sampleTransition(fromUniforms, targetUniforms, progress, displayedUniforms) {
  for (let index = 3; index < displayedUniforms.length; index += 1) {
    const colorComponent = index >= 40
      && (index - 40) % 4 < 3;
    displayedUniforms[index] = colorComponent
      ? mixSrgb(fromUniforms[index], targetUniforms[index], progress)
      : fromUniforms[index] + (targetUniforms[index] - fromUniforms[index]) * progress;
  }
  return displayedUniforms;
}

// ---------------------------------------------------------------- the shader (effect.wgsl -> GLSL ES 3.00)
const VERT = `#version 300 es
in vec2 aPos;
void main() { gl_Position = vec4(aPos, 0.0, 1.0); }`;

const FRAG = `#version 300 es
precision highp float;
precision highp int;
out vec4 fragColor;
// effect.wgsl struct Uniforms, member for member (std140 = the WGSL uniform layout for f32 / vec2 / vec4)
layout(std140) uniform Uniforms {
  vec2 size; float time; float speed; float radius; float zoom; float warp; float ridgeAmt; float sharp; float shade;
  float sheen; float gloss; float shellMidAlpha; float shellEdgeAlpha; float exposure; float style; float edgeSoftness;
  float edgeGlow; float paletteCount; float glassEnabled; float glassOpacity; float contourDeform; float bandDensity;
  float chromaticShift; float metalScale; float metalStretch; float metalAngle; float metalOffset; float metalPhase;
  float metalEvolution; float metalRoughness; float metalDepth; float particleDensity; float ribbonCount;
  float ribbonWidth; float ribbonTwist; float ribbonFold; float ribbonBreath; float particleSize; float particleBloom;
  vec4 colorA; vec4 colorB; vec4 colorC; vec4 colorD; vec4 highlightColor; vec4 shellInner; vec4 shellMid;
  vec4 shellEdge; vec4 sheenColor; vec4 specColor; vec4 canvasColor; vec4 glowColor;
  vec4 paletteStop0; vec4 paletteStop1; vec4 paletteStop2; vec4 paletteStop3; vec4 paletteStop4; vec4 paletteStop5;
  vec4 paletteStop6; vec4 paletteStop7; vec4 paletteStop8; vec4 paletteStop9; vec4 paletteStop10; vec4 paletteStop11;
} u;
uniform vec4 mrw;   // Marrow: x lift, y desat, z dark (1) / light (0)

const float GL_CLEAR_EA = 0.995;   // effect.wgsl
const float GL_CLEAR_EB = 1.04;    // effect.wgsl

// effect.wgsl mfEdgeD
float mfEdgeD(float soft) {
  return soft - 0.005;
}
// effect.wgsl mfEdgeGlow
vec3 mfEdgeGlow(vec3 col, vec2 uv, vec2 ctr, float rad, float soft, float glow, vec3 glowRGB) {
  if (glow <= 0.0) { return col; }
  float r = length(uv - ctr);
  float outside = smoothstep(rad - max(soft, 0.0005), rad + max(soft, 0.0005), r);
  return col + glowRGB * (glow * exp(-max(r - rad, 0.0) * 11.0) * outside);
}
// effect.wgsl glsFinishPresetFluid
vec3 glsFinishPresetFluid(vec3 colorIn, vec2 p) {
  vec3 color = colorIn;
  color = mix(color, u.highlightColor.rgb,
              u.shade * 0.22 * smoothstep(0.15, 1.15, dot(p, vec2(-0.32, 0.78))));
  color = color * (1.0 - u.shade * 0.34
                  * smoothstep(-0.1, 1.2, dot(p, vec2(0.45, -0.62))));
  color = color * (1.0 - u.shade * 0.22 * smoothstep(0.72, 1.08, length(p)));
  return clamp(color, vec3(0.0), vec3(1.0));
}
// effect.wgsl glsFinishEmissionFluid
vec3 glsFinishEmissionFluid(vec3 colorIn, vec2 p) {
  vec3 color = colorIn;
  if (u.glassEnabled > 0.5) {
    color = mix(color, u.highlightColor.rgb,
                u.shade * 0.22 * smoothstep(0.15, 1.15, dot(p, vec2(-0.32, 0.78))));
  }
  color = color * (1.0 - u.shade * 0.34
                  * smoothstep(-0.1, 1.2, dot(p, vec2(0.45, -0.62))));
  color = color * (1.0 - u.shade * 0.22 * smoothstep(0.72, 1.08, length(p)));
  return clamp(color, vec3(0.0), vec3(1.0));
}
// effect.wgsl glsSiriBand
vec2 glsSiriBand(vec2 q, float drift, float phaseOffset, float amplitude,
                 float mainY, float envelope, float softness) {
  float y = amplitude * envelope * sin(q.x * 1.0 + drift + phaseOffset);
  float distanceToLine = abs(q.y - y);
  float line = 0.018 / (sqrt(distanceToLine * distanceToLine + softness * softness) + 0.026);
  float bandDistance = max(0.0, max(q.y - max(mainY, y), min(mainY, y) - q.y));
  float band = 0.018 / (bandDistance + 0.075);
  return vec2(line, band);
}
// effect.wgsl glsSiriFluid
vec3 glsSiriFluid(vec2 p, float t) {
  float scale = 0.74 + u.zoom * 0.34;
  vec2 q = p / scale;
  float xNorm = q.x;
  float envelopeBase = cos(1.57079633 * min(abs(0.9 * xNorm), 1.0));
  float envelope = envelopeBase * envelopeBase;
  float low = 0.5 + 0.5 * cos(t * 0.37);
  float mid = 0.5 + 0.5 * sin(t * 0.51 + 1.2);
  float high = 0.5 + 0.5 * cos(t * 0.73 + 2.1);
  float drift = t * 2.4;
  float mainAmplitude = 0.25 + u.ridgeAmt * 0.075 + low * 0.018;
  float bandAmplitude = mainAmplitude + mid * 0.025 + high * 0.018;
  float mainY = mainAmplitude * envelope * sin(q.x * 1.1 + drift);
  float separation = 1.85 + u.warp * 0.2 + mid * 0.28;
  float softness = 0.035 + (1.0 - u.ridgeAmt) * 0.018 + mid * 0.006;

  vec2 band0 = glsSiriBand(q, drift, -separation, bandAmplitude, mainY, envelope, softness);
  vec2 band1 = glsSiriBand(q, drift, -separation * 0.34, bandAmplitude, mainY, envelope, softness);
  vec2 band2 = glsSiriBand(q, drift, separation * 0.34, bandAmplitude, mainY, envelope, softness);
  vec2 band3 = glsSiriBand(q, drift, separation, bandAmplitude, mainY, envelope, softness);
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
  vec3 spectral = (u.colorA.rgb * dominant0 + u.colorC.rgb * dominant1
                + u.colorB.rgb * dominant2 + u.colorD.rgb * dominant3)
                / max(dominantTotal, 0.0001);
  float energy = (1.0 - exp(-total * 0.58)) * envelope;
  float mainDistance = abs(q.y - mainY);
  float whiteCore = exp(-mainDistance * mainDistance / 0.0028) * envelope;
  float glassFill = (u.glassEnabled > 0.5) ? 1.0 : 0.0;
  vec3 atmosphere = mix(u.colorD.rgb, u.colorB.rgb,
                       smoothstep(-0.7, 0.7, q.y)) * 0.018 * glassFill;
  vec3 color = atmosphere + spectral * energy * 1.14;
  color = color + u.highlightColor.rgb * whiteCore * (0.18 + 0.1 * low);
  float emissionMask = mix(smoothstep(0.08, 0.25, energy + whiteCore * 0.12),
                         1.0, glassFill);
  color = color * emissionMask;
  color = color / (vec3(1.0) + color * 0.18);
  return glsFinishEmissionFluid(color, p);
}
// effect.wgsl glsVoiceWaveFluid
vec3 glsVoiceWaveFluid(vec2 p, float t) {
  float scale = 0.76 + u.zoom * 0.34;
  vec2 q = p / scale;
  float rimEnvelope = pow(max(1.0 - q.x * q.x, 0.0), 0.72);
  float drift = t * 0.82;
  float amplitude = 0.2 + u.warp * 0.018;
  float mainY = rimEnvelope * (amplitude * sin(q.x * 1.48 + drift)
              + 0.055 * sin(q.x * 3.2 - drift * 0.43 + 1.1));
  float distance_ = q.y - mainY;
  float width = 0.11 + (1.0 - u.ridgeAmt) * 0.075;
  float membrane = exp(-distance_ * distance_ / max(width * width, 0.001)) * rimEnvelope;
  float upperVeil = exp(-(distance_ - 0.105) * (distance_ - 0.105)
                      / max(width * width * 2.4, 0.001)) * rimEnvelope;
  float lowerVeil = exp(-(distance_ + 0.115) * (distance_ + 0.115)
                      / max(width * width * 2.8, 0.001)) * rimEnvelope;
  float crest = exp(-distance_ * distance_ / 0.0026) * rimEnvelope;
  float depth = sqrt(max(1.0 - clamp(dot(p, p), 0.0, 1.0), 0.0));
  vec3 color = mix(u.colorA.rgb * 0.7, u.colorD.rgb * 0.34,
                  smoothstep(-0.82, 0.82, q.y));
  color = mix(color, u.colorB.rgb, upperVeil * 0.7);
  color = mix(color, u.colorC.rgb, lowerVeil * 0.62);
  color = color + mix(u.colorB.rgb, u.colorC.rgb, 0.46) * membrane * 0.34;
  color = color + u.highlightColor.rgb * crest * 0.14;
  color = color * (0.58 + 0.42 * depth);
  return glsFinishPresetFluid(color, p);
}
// effect.wgsl glsPresetFluid (only the two voice presets' branches are carried: 9 and 19)
vec3 glsPresetFluid(vec2 p, int style, float t) {
  if (style == 9) { return glsSiriFluid(p, t); }
  if (style == 19) { return glsVoiceWaveFluid(p, t); }
  return vec3(0.0);
}
// effect.wgsl glsOver
vec3 glsOver(vec3 dst, vec3 src, float a) {
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
float glsHighlightLobe(vec2 normal, vec2 direction, float cut, float power) {
  float angular = clamp((dot(normal, direction) - cut) / max(1.0 - cut, 0.001), 0.0, 1.0);
  return pow(angular, power);
}
// effect.wgsl glsContourWave
vec2 glsContourWave(float angle, float t) {
  int style = int(u.style + 0.5);
  if (style == 19) {
    float wave = sin(angle * 2.0 + t * 0.27) * 0.72
               + sin(angle * 4.0 - t * 0.16 + 2.1) * 0.28;
    float slope = cos(angle * 2.0 + t * 0.27) * 1.44
                + cos(angle * 4.0 - t * 0.16 + 2.1) * 1.12;
    return vec2(wave, slope);
  }
  float wave = sin(angle * 3.0 + t * 0.62) * 0.52
             + sin(angle * 5.0 - t * 0.41 + 1.7) * 0.31
             + sin(angle * 2.0 + t * 0.23 + 3.1) * 0.17;
  float slope = cos(angle * 3.0 + t * 0.62) * 1.56
              + cos(angle * 5.0 - t * 0.41 + 1.7) * 1.55
              + cos(angle * 2.0 + t * 0.23 + 3.1) * 0.34;
  return vec2(wave, slope);
}
// effect.wgsl glsContourStrength
float glsContourStrength() {
  if (u.style >= 18.5) { return 0.11; }
  return (u.style >= 15.5) ? 0.16 : 0.09;
}
// effect.wgsl glsContourScale
float glsContourScale(vec2 uv, float t, float amount) {
  if (amount <= 0.0) { return 1.0; }
  vec2 contour = glsContourWave(atan(uv.y, uv.x), t);
  return 1.0 + clamp(amount, 0.0, 1.0) * glsContourStrength() * contour.x;
}
// effect.wgsl glsContourNormal
vec2 glsContourNormal(vec2 uv, float rad, float t, float amount) {
  float distance_ = length(uv);
  if (distance_ <= 0.0001) { return vec2(0.0); }
  vec2 radial = uv / distance_;
  vec2 contour = glsContourWave(atan(uv.y, uv.x), t);
  float slope = clamp(amount, 0.0, 1.0) * glsContourStrength() * contour.y;
  vec2 tangent = vec2(-radial.y, radial.x);
  return normalize(radial - tangent * (rad * slope / distance_));
}
// effect.wgsl glsRefractionNormal (its style-23 body, Refractive Gel, is not reached by these presets)
vec2 glsRefractionNormal(vec2 base, vec2 p, float t, int style) {
  return base;
}
// effect.wgsl orbGlassLiquidAnim
vec4 orbGlassLiquidAnim(vec2 uv01) {
  vec2 fc = vec2(uv01.x, 1.0 - uv01.y) * u.size;
  vec2 uv = (2.0 * fc - u.size) / max(min(u.size.x, u.size.y), 1.0);

  float rad = max(u.radius, 0.05);
  float t = u.time * u.speed;
  int s = int(u.style + 0.5);
  bool emissionOnly = u.glassEnabled <= 0.5 && (s == 9 || s == 14 || s == 24);
  float contourRad = rad * glsContourScale(uv, t, u.contourDeform);

  if (length(uv) > contourRad * (1.01 + mfEdgeD(u.edgeSoftness))) {
    vec3 halo = clamp(mfEdgeGlow(vec3(0.0), uv, vec2(0.0), contourRad,
                                u.edgeSoftness, u.edgeGlow, u.glowColor.rgb),
                     vec3(0.0), vec3(1.0));
    float haloAlpha = max(halo.r, max(halo.g, halo.b));
    return vec4(halo, haloAlpha);
  }

  vec2 p   = uv / contourRad;
  float pd = length(p);

  float clearFa = 1.0 - smoothstep(GL_CLEAR_EA, GL_CLEAR_EB, pd);
  vec2 contourNormal = glsContourNormal(uv, rad, t, u.contourDeform);
  vec2 normal = glsRefractionNormal(contourNormal, p, t, s);
  float edgeDepth = max(1.0 - pd, 0.0);
  float refractionWidth = 0.015 + 0.95 * clamp(u.shellMidAlpha, 0.0, 1.0);
  float refractionT = edgeDepth / max(refractionWidth, 0.001);
  float refractionProfile = pow(glsRefractionProfile(refractionT), 0.68);
  float refractionAmount = 1.6 * clamp(u.glassOpacity, 0.0, 1.0)
                         * refractionProfile;
  vec2 refractedP = p - normal * refractionAmount;
  vec3 fcol = vec3(0.0);
  if (clearFa > 0.0) {
    if (s >= 9) {
      if (u.glassEnabled > 0.5) {
        float channelSplit = 0.14 * clamp(u.gloss, 0.0, 2.0)
                           * clamp(u.glassOpacity, 0.0, 1.0)
                           * refractionProfile;
        vec3 redSample = glsPresetFluid(refractedP - normal * channelSplit, s, t);
        vec3 greenSample = glsPresetFluid(refractedP, s, t);
        vec3 blueSample = glsPresetFluid(refractedP + normal * channelSplit, s, t);
        fcol = vec3(redSample.r, greenSample.g, blueSample.b);
      }
      else { fcol = glsPresetFluid(p, s, t); }
    }
    // (s < 9: glsFluid, the legacy sheet programs, not reached by these presets)
  }

  float lum = dot(fcol, vec3(0.213, 0.715, 0.072));
  vec3 clearSat = clamp(vec3(lum) + (fcol - vec3(lum)) * 1.22,
                       vec3(0.0), vec3(1.0));
  bool particleGlassOverlay = s == 24;
  vec3 col = particleGlassOverlay ? vec3(0.0) : glsOver(u.canvasColor.rgb, clearSat, 0.99 * clearFa);
  if (emissionOnly) {
    float signal = max(clearSat.r, max(clearSat.g, clearSat.b));
    float emissionCoverage = smoothstep(0.025, 0.16, signal);
    col = clearSat * emissionCoverage;
  }
  if (u.glassEnabled > 0.5) {
    float surfaceWidth = particleGlassOverlay
      ? 0.09 + 0.12 * clamp(u.shellEdgeAlpha, 0.0, 1.0)
      : 0.026 + 0.055 * clamp(u.shellEdgeAlpha, 0.0, 1.0);
    float surfaceBand = (1.0 - smoothstep(0.0, surfaceWidth, edgeDepth)) * clearFa;
    float opticalRim = pow(surfaceBand, particleGlassOverlay ? 1.3 : 1.8);
    float innerRimAlpha = particleGlassOverlay
      ? opticalRim * u.glassOpacity * 0.14
      : opticalRim * u.glassOpacity * 0.45;
    col = glsOver(col, u.shellInner.rgb, innerRimAlpha);

    vec2 coolDirection = normalize(vec2(0.84, 0.54));
    vec2 warmDirection = normalize(vec2(-0.62, -0.78));
    float coolSplit = glsHighlightLobe(normal, coolDirection, -0.32, 1.8);
    float warmSplit = glsHighlightLobe(normal, warmDirection, -0.28, 2.0);
    float dispersion = opticalRim * clamp(u.gloss, 0.0, 2.0)
                     * (0.8 + 0.8 * u.shellEdgeAlpha);
    col = glsOver(col, u.shellMid.rgb, dispersion * coolSplit);
    col = glsOver(col, u.shellEdge.rgb, dispersion * warmSplit);

    float edgeShadow = opticalRim * (0.015 + 0.15 * u.shellEdgeAlpha)
                     * (0.15 + 0.85 * max(dot(normal, vec2(0.45, -0.89)), 0.0));
    col = col * (1.0 - edgeShadow);

    vec2 keyDirection = normalize(vec2(-0.68, 0.73));
    vec2 fillDirection = normalize(vec2(0.74, -0.67));
    float key = opticalRim * glsHighlightLobe(normal, keyDirection, 0.2, 2.8)
              * clamp(u.sheen, 0.0, 2.0) * 1.4;
    float fill = opticalRim * glsHighlightLobe(normal, fillDirection, 0.4, 3.6)
               * clamp(u.sheen, 0.0, 2.0) * 1.0;
    col = glsOver(col, u.sheenColor.rgb, key);
    col = glsOver(col, u.specColor.rgb, fill);
  }

  float ballA = 1.0 - smoothstep(0.99 - mfEdgeD(u.edgeSoftness), 1.01 + mfEdgeD(u.edgeSoftness), pd);
  col = clamp(col * max(u.exposure, 0.0), vec3(0.0), vec3(1.0)) * ballA;
  vec3 edged = mfEdgeGlow(col, uv, vec2(0.0), contourRad,
                         u.edgeSoftness, u.edgeGlow, u.glowColor.rgb);
  vec3 finalColor = clamp(edged, vec3(0.0), vec3(1.0));
  float emissionAlpha = max(finalColor.r, max(finalColor.g, finalColor.b));
  float sphereAlpha = clamp(max(ballA, emissionAlpha), 0.0, 1.0);
  float finalAlpha = (emissionOnly || particleGlassOverlay) ? emissionAlpha : sphereAlpha;
  return vec4(finalColor, finalAlpha);
}

void main() {
  // shader-source.ts vs_main hands uv = (x, 1 - y) of the clip square in 0..1; gl_FragCoord is bottom-left origin
  vec2 inUv = vec2(gl_FragCoord.x / u.size.x, 1.0 - gl_FragCoord.y / u.size.y);
  // shader-source.ts fs_main
  vec4 c = orbGlassLiquidAnim(inUv);
  vec2 fc = vec2(inUv.x, 1.0 - inUv.y) * u.size;
  vec2 uv = (2.0 * fc - u.size) / max(min(u.size.x, u.size.y), 1.0);
  float rad = max(u.radius, 0.05);
  float t = u.time * u.speed;
  float contourRad = rad * glsContourScale(uv, t, u.contourDeform);
  vec2 q = (2.0 * fc - u.size) / u.size;
  float fitEnd = 1.0;
  float fitFeather = 2.0 / max(min(u.size.x, u.size.y), 1.0);
  float fitStart = min(mix(contourRad, fitEnd, 0.5), fitEnd - fitFeather);
  float fit = 1.0 - smoothstep(fitStart, fitEnd, max(abs(q.x), abs(q.y)));
  vec4 o = vec4(c.rgb * fit, c.a * fit);

  // ---- Marrow (exactly nothing when mrw.x = mrw.y = 0): the error look drains the colour inside the ball;
  // the lift puts a soft shadow under the popped ball, behind what the source drew
  if (mrw.y > 0.0) {
    float l = dot(o.rgb, vec3(0.2126, 0.7152, 0.0722));
    o.rgb = mix(o.rgb, vec3(l) * 0.86, mrw.y * 0.78);
  }
  if (mrw.x > 0.0) {
    float r = length(uv - vec2(0.0, -0.10 * rad));
    float sr = max(r - rad * 0.92, 0.0) / max(rad, 0.001);
    float sh = mrw.x * (0.30 + 0.12 * (1.0 - mrw.z)) * exp(-sr * sr * 7.0) * smoothstep(rad * 0.98, rad * 1.02, length(uv));
    o.a = o.a + sh * (1.0 - o.a);
  }
  fragColor = o;
}`;

// ---------------------------------------------------------------- WebGL2 contexts (as knob-orbs-2.js)
const OWN_MAX = 6;
let ownLive = 0;
const GL_OPTS = { alpha: true, premultipliedAlpha: true, antialias: false, depth: false, stencil: false,
  preserveDrawingBuffer: false, powerPreference: 'default' };
function makeGL(canvas) {
  const gl = canvas.getContext('webgl2', GL_OPTS);
  if (!gl) return null;
  const st = { canvas, gl, prog: null, lost: false, buf: null };
  canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); st.lost = true; });
  canvas.addEventListener('webglcontextrestored', () => { st.prog = null; st.lost = false; initBuf(st); });
  initBuf(st);
  return st;
}
const POOL = new Map();
let G2 = null;
function shared(size) {
  if (typeof OffscreenCanvas !== 'undefined') {
    let st = POOL.get(size);
    if (!st) { st = makeGL(new OffscreenCanvas(size, size)); if (!st) return null; st.off = true; POOL.set(size, st); }
    return st.lost ? null : st;
  }
  if (!G2) { const c = document.createElement('canvas'); c.width = 256; c.height = 256; G2 = makeGL(c); }
  return G2 && !G2.lost ? G2 : null;
}
function initBuf(st) {
  const gl = st.gl;
  st.buf = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, st.buf);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
}
function program(st) {
  if (st.prog) return st.prog;
  const gl = st.gl;
  const sh = (type, src) => {
    const s = gl.createShader(type);
    gl.shaderSource(s, src); gl.compileShader(s);
    if (!gl.getShaderParameter(s, gl.COMPILE_STATUS) && !gl.isContextLost()) throw new Error('voice-orbs shader: ' + gl.getShaderInfoLog(s));
    return s;
  };
  const pr = gl.createProgram();
  gl.attachShader(pr, sh(gl.VERTEX_SHADER, VERT));
  gl.attachShader(pr, sh(gl.FRAGMENT_SHADER, FRAG));
  gl.bindAttribLocation(pr, 0, 'aPos');
  gl.linkProgram(pr);
  if (!gl.getProgramParameter(pr, gl.LINK_STATUS) && !gl.isContextLost()) throw new Error('voice-orbs link: ' + gl.getProgramInfoLog(pr));
  const blk = gl.getUniformBlockIndex(pr, 'Uniforms');
  gl.uniformBlockBinding(pr, blk, 0);
  const size = gl.getActiveUniformBlockParameter(pr, blk, gl.UNIFORM_BLOCK_DATA_SIZE);
  if (size !== 544) throw new Error('voice-orbs: the uniform block is ' + size + ' bytes, the export writes 544');
  st.prog = { pr, mrw: gl.getUniformLocation(pr, 'mrw') };
  return st.prog;
}

// ---------------------------------------------------------------- the orb
function makeOrb(key, canvas, opts = {}) {
  const idleSeed = new Float32Array(SEEDS[key].idle), thinkSeed = new Float32Array(SEEDS[key].thinking);
  const values = new Float32Array(136);
  let dark = opts.dark !== false;
  const mode = opts.context === 'own' || opts.context === 'shared' ? opts.context : (ownLive < OWN_MAX ? 'own' : 'shared');
  const own = mode === 'own' ? makeGL(canvas) : null;
  if (own) ownLive++;
  const bmp = !own && typeof OffscreenCanvas !== 'undefined';
  const ctx = own ? null : canvas.getContext(bmp ? 'bitmaprenderer' : '2d', { alpha: true });
  let cssPx = 0, dev = 0, scale = 1, rdev = 0, dpr = 1, ballFrac = 0;
  let lastT = null, motionPhase = opts.phase || 0, disposed = false, ubo = null, uboGl = null;

  function resize(px, ball) {
    dpr = Math.min(3, window.devicePixelRatio || 1);
    cssPx = px;
    ballFrac = ball ? ball / px : 0;
    dev = Math.max(2, Math.round(px * dpr));
    scale = opts.supersample === false ? 1 : dev < 128 ? 2 : 1;
    rdev = dev * scale;
    const back = own || bmp ? rdev : dev;
    if (canvas.width !== back || canvas.height !== back) { canvas.width = back; canvas.height = back; }
    canvas.style.width = px + 'px'; canvas.style.height = px + 'px';
  }

  function frame(t, s = {}) {
    if (disposed) return;
    const g = own || shared(rdev);
    if (!g || g.lost) return;
    if (!dev) resize(canvas.clientWidth || 72, opts.ball);
    const gl = g.gl;
    // the export's frame(): the state sample, the audio rules, the integrated motion phase
    const progress = Math.min(1, Math.max(0, +s.act || 0));
    sampleTransition(idleSeed, thinkSeed, progress, values);
    const frameDelta = lastT === null ? 0 : Math.min(0.1, Math.max(0, t - lastT));
    lastT = t;
    if (s.bands) applyAudioUniforms(values, s.bands);
    // Marrow: the state layer drives the speed (a multiple of the Idle speed) in place of the speed rule
    if (s.speed != null) values[3] = Math.max(0, +s.speed) * idleSeed[3];
    if (!s.reduceMotion) motionPhase += frameDelta * Math.max(values[3], 0);
    values[0] = rdev;
    values[1] = rdev;
    values[2] = motionPhase / Math.max(values[3], 0.001);
    // Marrow framing: the source's own radius and edge softness, set for the button
    if (ballFrac > 0) {
      values[4] = ballFrac * (s.scale == null ? 1 : +s.scale);
      if (opts.edge !== 'source') {
        const radPx = values[4] * rdev / 2;
        values[16] = Math.min(0.15, Math.max(0.005, (1.5 / Math.max(radPx, 1) - 0.01) / 2));
      }
    }
    if (s.glow != null) values[17] = Math.max(0, +s.glow);   // Marrow: our glow, through the source's Outer Glow

    if (!own && !bmp && (rdev > g.canvas.width || rdev > g.canvas.height)) {
      const sz = Math.max(256, 1 << Math.ceil(Math.log2(rdev)));
      g.canvas.width = sz; g.canvas.height = sz;
    }
    const P = program(g);
    if (uboGl !== gl) { ubo = gl.createBuffer(); uboGl = gl; gl.bindBuffer(gl.UNIFORM_BUFFER, ubo); gl.bufferData(gl.UNIFORM_BUFFER, 544, gl.DYNAMIC_DRAW); }
    gl.viewport(0, 0, rdev, rdev);
    gl.disable(gl.BLEND);
    gl.useProgram(P.pr);
    gl.bindBuffer(gl.UNIFORM_BUFFER, ubo);
    gl.bufferSubData(gl.UNIFORM_BUFFER, 0, values);
    gl.bindBufferBase(gl.UNIFORM_BUFFER, 0, ubo);
    gl.uniform4f(P.mrw, Math.max(0, Math.min(1, +s.lift || 0)), Math.max(0, Math.min(1, +s.desat || 0)), dark ? 1 : 0, 0);
    gl.bindBuffer(gl.ARRAY_BUFFER, g.buf);
    gl.enableVertexAttribArray(0);
    gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
    gl.drawArrays(gl.TRIANGLES, 0, 3);

    if (own) return;
    if (bmp) { ctx.transferFromImageBitmap(g.canvas.transferToImageBitmap()); return; }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.globalCompositeOperation = 'copy';
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(g.canvas, 0, g.canvas.height - rdev, rdev, rdev, 0, 0, dev, dev);
    ctx.globalCompositeOperation = 'source-over';
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    if (own) {
      ownLive--;
      if (own.prog) own.gl.deleteProgram(own.prog.pr);
      own.gl.deleteBuffer(own.buf);
      if (ubo) own.gl.deleteBuffer(ubo);
      const lc = own.gl.getExtension('WEBGL_lose_context');
      if (lc) lc.loseContext();
    } else {
      if (ubo && uboGl) uboGl.deleteBuffer(ubo);
      if (bmp) ctx.transferFromImageBitmap(null); else ctx.clearRect(0, 0, canvas.width, canvas.height);
    }
  }

  if (opts.size) resize(opts.size, opts.ball);
  return {
    frame, resize, dispose, mode,
    recolor() { /* the orbs keep the source's colours exactly (owner, 2026-10-03) */ },
    setDark(d) { dark = d !== false; },
    get phase() { return motionPhase; }, set phase(v) { motionPhase = v; },
    get values() { return values; },
  };
}

const META = {
  siri: { id: 'siri-wave', name: 'Siri Wave', line: 'A white-hot crest with chromatic bands fanning from it, in smoky glass.' },
  voiceWave: { id: 'voice-membrane', name: 'Voice Membrane', line: 'A magenta membrane across a violet sphere, a coral underside.' },
};
export const orbs = ['siri', 'voiceWave'].map((k) => ({
  id: META[k].id,
  key: k,
  name: META[k].name,
  line: META[k].line,
  style: STYLE[k],
  audioStrength: audioFlowStrengths[String(STYLE[k])],
  idleSpeed: SEEDS[k].idle[3],
  thinkingSpeed: SEEDS[k].thinking[3],
  background: SEEDS.background ? SEEDS.background[k] : null,
  source: 'Liquid Orb Editor preset "' + k + '" (MIT, LerSent001): the site\u2019s Copy code export, run in WebGL2',
  make: (canvas, opts) => makeOrb(k, canvas, opts),
}));
export default orbs;
