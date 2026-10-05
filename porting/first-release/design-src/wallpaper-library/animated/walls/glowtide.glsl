// Glowtide: a beach at dusk when the water is full of bioluminescent plankton, seen from low on the sand. Each wave that
// breaks lights up from within along its crest, a soft blue-green that glows through the water rather than on it; the
// whitewater behind it glows in a lace of foam, and the wave runs up the wet sand as a thin luminous sheet and slides
// back. The wet sand is a rough mirror: it holds the evening sky and a smeared reflection of every glowing crest.
// Three waves of a set come in one after another and the set repeats exactly.
// Palette: 0 the high sky, 1 the dusk at the horizon, 2 the sea, 3 the glow, 4 the sand, 5 the glow's bright core.
// Options: x = motion (how far the waves run up), y = glow (faint to bright).
const float T = 30.0;

vec3 skyAt(float h) {
  return mix(u_pal[1], u_pal[0], pow(sat(h / 0.85), 0.55));
}

// the glow at a point (X across, Z away) on the water; det = 1 adds the foam's lace
float glowAt(float X, float Z, float a, float m, float wl, float det) {
  float g = 0.0;
  for (int i = 0; i < 3; i++) {
    float f = float(i);
    float ph = fract(a / TAU + f / 3.0);
    float env = smoothstep(0.04, 0.2, ph) * (1.0 - smoothstep(0.78, 1.0, ph));
    // the front: comes in from far out, slows as it reaches the beach, runs up the sand and slides back
    float wob = (0.08 + 0.04 * Z) * gnoise(vec3(X * 0.6, f * 4.0, 1.0)) + 0.04 * sin(X * 1.6 + f * 2.0);
    float zin = mix(6.0, wl, smoothstep(0.0, 0.62, ph));
    float run = (0.16 + 0.16 * m) * sin(3.14159265 * sat((ph - 0.5) / 0.5));
    float zf = zin - run + wob;
    float dZ = Z - zf;
    float w = 0.03 + 0.03 * Z;
    // the crest breaks along its length a stretch at a time
    float bn = gnoise(vec3(X * 0.7, f * 3.0, ph * 2.2)) * 0.7 + 0.3 * gnoise(vec3(X * 2.3, f * 5.0, ph * 3.0));
    float brk = smoothstep(-0.35, 0.25, bn + 1.2 * ph - 0.55);
    float crest = exp(-dZ * dZ / (w * w)) * brk;
    // the whitewater trailing seaward of the front, and the glow it leaves under the surface
    float trail = smoothstep(-w, w, dZ) * exp(-max(dZ, 0.0) / (0.35 + 0.5 * ph));
    float lace = 0.6;
    if (det > 0.5) {
      float fb = fbm3(vec3(X * 2.4, Z * 3.2 - ph * 2.0, f * 3.0 + ph));
      lace = smoothstep(-0.12, 0.28, fb) * (0.55 + 0.45 * smoothstep(0.1, 0.5, gnoise(vec3(X * 7.0, Z * 9.0, f + ph))));
      lace = mix(0.6, lace, exp(-Z * 0.25));
    }
        // the whitewater's glow comes and goes in patches, which follow the water, not the line of sight
    float brT = smoothstep(-0.3, 0.5, 0.5 * bn + gnoise(vec3(X * 0.8, Z * 2.2 - ph * 1.5, f * 2.0 + 7.0)) + 0.3 * ph);
    g += env * (crest * 1.4 + trail * brT * (0.1 + 0.9 * lace) * 0.45);
  }
  return g;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.45, 1.25, u_opt.x);
  float gk = mix(0.5, 1.35, u_opt.y);
  float yh = 0.2;
  vec3 gl = lab2lin(u_pal[3]);
  vec3 gh = lab2lin(u_pal[5]);
  // the sky, a few faint stars high up in the dark
  vec3 sky = lab2lin(skyAt(p.y - yh));
  vec3 sh = hash33(vec3(floor(p * 160.0), 2.0));
  sky += vec3(0.6, 0.7, 0.8) * step(0.994, sh.x * 0.5 + 0.5) * smoothstep(0.3, 0.9, p.y) * u_dark * 0.25;
  // the ground in perspective: Z is the distance from us, X across
  float dz = max(yh - p.y, 1e-3);
  float Z = 1.0 / dz;
  float X = p.x * Z;
  float wl = 1.5 + 0.12 * gnoise(vec3(X * 0.3, 0.0, 3.0));
  // the line the waves reach on the sand, and the wet band below it
  float reach = wl - (0.16 + 0.16 * m) - 0.04;
  float onWater = smoothstep(wl - 0.05, wl + 0.12, Z);
  float wet = smoothstep(reach - 0.12, reach + 0.03, Z) * (1.0 - onWater);
  // the sea: dark water that mirrors the dusk more strongly toward the horizon
  float fres = 0.08 + 0.85 * pow(sat(1.0 - dz * 1.6), 4.0);
  float sw = lnoise(vec2(X * 0.5, Z * 1.2), a, 0.6) * 0.5 + 0.5;
  vec3 sea = mix(lab2lin(u_pal[2]), lab2lin(skyAt(dz * (0.8 + 0.4 * sw))), fres);
  // the sand: dark grain at our feet; where wet, a rough mirror of the sky streaked toward us
  float grain = (gnoise(vec3(X * 34.0, Z * 34.0, 1.0)) * 0.6 + gnoise(vec3(X * 9.0, Z * 9.0, 2.0)) * 0.4) * mix(0.5, 1.0, u_dark);
  vec3 dry = lab2lin(setL(u_pal[4], u_pal[4].x * (1.0 + 0.08 * grain)));
  float streak = gnoise(vec3(X * 9.0, Z * 1.5, 4.0)) * 0.5 + 0.5;
  vec3 mirror = lab2lin(skyAt(dz * (1.0 + 0.6 * streak)));
  vec3 sand = mix(dry, mix(dry * 0.7, mirror, 0.55 + 0.25 * streak), wet);
  vec3 lin = mix(sand, sea, onWater);
  // the glow: in the water and the thin sheets running over the sand
  float e = glowAt(X, Z, a, m, wl, 1.0) * smoothstep(reach - 0.15, reach + 0.15, Z);
  // its reflection in the wet sand: the glow a little farther out seen in the mirror, smeared and broken by the grain
  float refl = glowAt(X + 0.03 * (streak - 0.5), Z * 1.12 + 0.05 + 0.1 * streak, a, m, wl, 0.0) * wet * (0.3 + 0.7 * streak * streak);
  float E = (e + refl * 0.8) * gk;
  vec3 em = gl * E + gh * pow(sat(e * gk * 0.8), 3.0) * 0.6;
  // dark: emitted light added to the night; light: the glow laid over the pale dusk so it keeps its colour
  vec3 dk = lin + em * 0.7;
  vec3 lt = mix(lin, mix(gl, gh, sat(e * 0.5)), sat(E * 0.55));
  lin = mix(lt, dk, u_dark);
  // the faint glow of the whole bay lifts the near sand a little
  lin += gl * 0.02 * gk * (1.0 - onWater) * smoothstep(0.0, 1.0, 1.0 / (wl - Z + 1.0)) * u_dark;
  // haze on the horizon
  lin = mix(lin, lab2lin(u_pal[1]), exp(-dz * 30.0) * 0.55);
  lin = mix(lin, sky, smoothstep(-edgePx(), edgePx(), p.y - yh));
  return lin2srgb(shoulder(lin));
}
