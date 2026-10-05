// Isles: wooded islands on a still sea at first light, lying in ranks one behind another and drifting slowly past, the
// far ranks slower than the near, as from the deck of a boat. Mist pools at their feet, the low sun rims their crowns,
// and each is mirrored, softly broken by the swell, in the water below.
// Palette: 0 zenith, 1 the glow at the horizon, 2 the farthest islands, 3 the nearest islands, 4 deep water, 5 the sun.
// Options: x = motion (how much the mist and the water stir), y = mist (thin to thick).
const float T = 90.0;

// a rank of islands: periodic along the shore, so the rank drifts exactly one period per loop
float isle(float x, float a, float f) {
  float D = 3.6 + 2.4 * f;
  float u = TAU * x / D + a + f * 1.9;
  float g = 0.5 * sin(u) + 0.3 * sin(2.0 * u + 1.3 + f) + 0.18 * sin(3.0 * u + 0.4 + 2.0 * f) + 0.1 * sin(5.0 * u + 2.1 * f);
  float k = sat((g - 0.22) / 0.5);
  float dome = (0.06 + 0.05 * f) * sqrt(k) * (1.0 + 0.3 * sin(7.0 * u + f));
  // the crowns of the trees: fine bumps along the top, only where there is land
  float crown = (0.5 * sin(17.0 * u + f) + 0.3 * sin(31.0 * u + 2.0 * f) + 0.2 * sin(57.0 * u + 0.7)) * (0.007 + 0.004 * f);
  return dome + crown * sat(k * 5.0);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.4, u_opt.x);
  float mist = mix(0.4, 1.4, u_opt.y);
  float yh = -0.3;
  vec2 sun = vec2(0.95, yh + 0.17) + u_sun + tiltD(-1.0);
  // the sky, and the sun just above the far islands
  float sg = length((p - sun) * vec2(1.0, 1.25));
  vec3 sky = mix(u_pal[1], u_pal[0], pow(smoothstep(yh - 0.05, 1.05, p.y), 0.7));
  sky = mix(sky, u_pal[5], sat(smoothstep(0.036, 0.03, length(p - sun)) + exp(-sg * sg / 0.012) * 0.5 + exp(-sg / 0.4) * 0.3));
  // the water: the sky mirrored, darkened, broken by a gentle swell that moves toward the shore
  float dz = max(yh - p.y, 0.0);
  float z = 1.0 / (dz + 0.04);
  vec2 wq = vec2(p.x * z * 0.35, z * 0.9);
  float sw = lnoise(wq * vec2(1.0, 1.0), a, 0.35 * m) * 0.6 + 0.4 * lnoise(wq * 2.3 + 5.0, 2.0 * a, 0.25 * m);
  float wob = sw * (0.012 + 0.045 * dz);
  vec2 rp = vec2(p.x + wob, 2.0 * yh - p.y);
  float rg = length((rp - sun) * vec2(1.0, 1.25));
  vec3 refl = mix(u_pal[1], u_pal[0], pow(smoothstep(yh - 0.05, 1.05, rp.y), 0.7));
  refl = mix(refl, u_pal[5], exp(-rg * rg / 0.03) * 0.6 + exp(-rg / 0.5) * 0.22);
  vec3 water = mix(refl, u_pal[4], 0.3 + 0.4 * smoothstep(0.0, 0.7, dz));
  float glint = exp(-pow((p.x + wob * 3.0 - sun.x) / (0.08 + 0.25 * dz), 2.0)) * smoothstep(0.85, 0.0, dz) * sat(0.6 + 0.8 * sw);
  // fine ripples catching the sky: horizontal streaks, finer toward the horizon
  float st = lnoise(vec2(p.x * z * 0.12, z * 7.0), a, 0.3 * m);
  water.x += 0.025 * st * smoothstep(0.0, 0.05, dz);
  water = mix(water, u_pal[5], glint * sat(0.25 + 0.9 * st) * 0.55);
  vec3 col = mix(water, sky, smoothstep(-u_px, u_px, p.y - yh));
  for (int i = 0; i < 4; i++) {
    float f = float(i);
    float fk = f / 3.0;
    float base = yh - 0.008 - 0.018 * f - 0.022 * f * f;
    // each rank is its own Perspective layer, the far ranks moving least
    vec2 pl = p + tiltD(-0.6 + 0.53 * f);
    float h = isle(pl.x, a, f);
    vec3 ic = mix(u_pal[2], u_pal[3], fk);
    ic = mix(ic, u_pal[1], 0.18 * (1.0 - fk));
    // the island itself, rimmed on its sun side
    float top = base + h;
    float cov = smoothstep(edgePx(), -edgePx(), pl.y - top) * step(base - u_px, pl.y) * step(0.004, h);
    float side = 0.4 + 0.6 * exp(-abs(pl.x - sun.x) * 1.4);
    float rim = exp(-max(top - pl.y, 0.0) / (0.006 + 0.002 * f)) * side * (1.0 - 0.5 * fk);
    vec3 lc = mix(ic, u_pal[5], rim * 0.35);
    lc.x += 0.03 * (1.0 - fk) * smoothstep(0.0, 0.12, pl.y - base);
    col = mix(col, lc, cov);
    // its reflection, broken by the swell
    float hr = isle(pl.x + wob, a, f);
    float rcov = smoothstep(-edgePx() * 2.0, edgePx() * 2.0, pl.y - (base - hr * 0.92)) * step(pl.y, base) * step(0.004, hr);
    vec3 rc = mix(ic, u_pal[4], 0.3);
    rc.x *= 0.92;
    col = mix(col, rc, rcov * 0.9);
    // the mist lying at its feet
    float my = pl.y - base;
    float mf = exp(-abs(my) / (0.025 + 0.03 * f)) * (0.45 + 0.3 * lnoise(vec2(p.x * 1.4 + f * 3.0, f), a, 0.4 * m)) * mist;
    col = mix(col, mix(u_pal[1], u_pal[5], 0.25), sat(mf * (0.55 - 0.08 * f)));
  }
  return outLab(col);
}
