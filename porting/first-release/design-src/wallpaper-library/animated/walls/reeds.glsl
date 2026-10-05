// Reeds: the edge of a lake at sunrise, seen through a stand of tall reeds. Mist lies on the water and the low sun glows
// through it; the reeds stand dark against the light along the bottom of the frame, nearer ones taller and softer as a
// lens sees them, each with its feathery plume, and they sway in the morning air at their own pace.
// Palette: 0 the high sky, 1 the mist, 2 the sun's glow, 3 the water, 4 the reeds, 5 the sun.
// Options: x = motion (how far the reeds sway), y = reeds (sparse to dense).
const float T = 36.0;

// one rank of reeds: x positions on a grid, each reed a thin stem bending from its root, topped with a plume
vec2 rank(vec2 p, float a, float m, float f, float den, float soft) {
  float sp = 0.07 + 0.05 * f;
  float cell = floor(p.x / sp);
  float cov = 0.0;
  float plume = 0.0;
  for (int i = 0; i < 5; i++) {
    float c = cell + float(i) - 2.0;
    vec3 h = hash33(vec3(c, f, 2.0)) * 0.5 + 0.5;
    float x0 = (c + 0.2 + 0.6 * h.x) * sp;
    // the stand thins toward the middle, where the light comes through
    float side = 0.25 + 0.75 * smoothstep(0.15, 1.1, abs(x0 + 0.35));
    float on = step(1.0 - den * side, h.z);
    float base = -1.05;
    float ht = (0.35 + 0.4 * h.y) * (0.7 + 0.35 * f) * (0.55 + 0.45 * side);
    float u = sat((p.y - base) / ht);
    float n = mod(c, 2.0) < 0.5 ? 1.0 : -1.0;
    float bend = (0.04 + 0.05 * h.x) * m * sin(a * (1.0 + floor(h.y * 2.0)) * n + h.z * TAU) + 0.03 * (h.y - 0.5);
    float x = x0 + bend * u * u + 0.02 * u;
    float w = (0.0011 + 0.0008 * f) * (1.0 - 0.55 * u) + soft * 0.3;
    float stem = smoothstep(w + edgePx(), w, abs(p.x - x)) * step(p.y, base + ht);
    // the plume: a feathery tassel that droops to one side from the top of the stem, its fibres catching the light
    float dir = h.x < 0.5 ? -1.0 : 1.0;
    vec2 tq = p - vec2(x0 + bend + 0.02, base + ht);
    float s = sat(-tq.y / (0.2 + 0.07 * f) + 0.12);
    float cx = dir * (0.05 + 0.03 * f) * s * s + bend * 0.3 * s;
    float wd = (0.01 + 0.03 * sin(3.14159 * sat(s))) * (1.0 + 0.5 * f) + soft * 3.0;
    float across = (tq.x - cx) / wd;
    float fib = 0.55 + 0.45 * gnoise(vec3(across * 2.5, tq.y * 60.0, c + f * 7.0));
    float plc = exp(-across * across) * smoothstep(1.0, 0.75, s) * smoothstep(0.0, 0.08, s) * fib;
    cov = max(cov, stem * on);
    plume = max(plume, plc * on);
  }
  return vec2(cov, plume);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float den = mix(0.35, 0.8, u_opt.y);
  float yh = -0.32;
  vec2 sun = vec2(-0.55, yh + 0.2) + u_sun + tiltD(-1.0);
  // the sky, the mist and the sun
  float sd = length((p - sun) * vec2(0.8, 1.0));
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(yh, 1.1, p.y));
  sky = mix(sky, u_pal[2], exp(-sd / 0.55) * 0.75);
  sky = mix(sky, u_pal[5], smoothstep(0.07, 0.05, length(p - sun)) * 0.9 + exp(-sd * sd / 0.02) * 0.5);
  // the water, still, holding the glow and mist
  float dz = max(yh - p.y, 0.0);
  vec3 water = mix(u_pal[1], u_pal[3], smoothstep(0.0, 0.5, dz));
  water = mix(water, u_pal[2], exp(-pow((p.x - sun.x) / (0.15 + 0.6 * dz), 2.0)) * 0.55 * exp(-dz * 2.0));
  water.x += 0.012 * lnoise(vec2(p.x * 3.0, dz * 30.0), a, 0.3 * m);
  vec3 col = mix(water, sky, smoothstep(-edgePx(), edgePx(), p.y - yh));
  // the mist on the water
  float mist = exp(-pow((p.y - yh - 0.02) / 0.12, 2.0)) * (0.7 + 0.3 * lnoise(vec2(p.x * 1.4, 0.0), a, 0.3 * m));
  col = mix(col, mix(u_pal[1], u_pal[2], 0.4 * exp(-abs(p.x - sun.x))), mist * 0.6);
  // the reeds: three ranks, the far ones fainter in the mist, the near ones taller and out of focus
  vec3 rc = u_pal[4];
  for (int k = 0; k < 3; k++) {
    float f = float(k);
    vec2 q = p + tiltD(-0.3 + 0.6 * f);
    vec2 r = rank(q, a, m, f, den * (0.6 + 0.2 * f), 0.0015 * f * f);
    vec3 c = mix(rc, u_pal[1], (0.55 - 0.25 * f) * (1.0 - f / 3.0));
    // the plumes are thin enough to glow where the sun is behind them
    vec3 pc = mix(c, u_pal[2], 0.2 + 0.5 * exp(-length(q - sun) * 1.1));
    col = mix(col, pc, sat(r.y * 1.3) * (0.8 - 0.1 * f));
    col = mix(col, c, r.x);
  }
  return outLab(col);
}
