// Dunes: ridge after ridge of sand under a low sun. The far ridges fade into the haze, the near ones carry a lit face,
// a shadowed lee and fine wind ripples; as the sun swings a little, the light slides across the faces and the ripples
// creep.
// Palette: 0 zenith, 1 horizon, 2 far haze, 3 lit sand, 4 shadowed sand, 5 sun and crest light.
// Options: x = motion (how far the sun swings and the sand drifts), y = ripples.
const float T = 60.0;

float ridge(float x, float k) {
  return 0.55 * sin(x + 0.35 * sin(2.0 * x + k)) + 0.28 * sin(2.3 * x + 1.0 + k) + 0.1 * sin(4.7 * x + 2.0 * k);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float rip = u_opt.y;
  float sunX = 0.65 + 0.22 * m * sin(a);
  vec2 sun = vec2(sunX, 0.28 + 0.04 * m * cos(a));
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(-0.05, 1.0, p.y));
  float sg = length(p - sun);
  col = mix(col, u_pal[5], exp(-(sg * sg) / 0.03) * 0.75 + exp(-(sg * sg) / 0.35) * 0.25);
  for (int i = 0; i < 5; i++) {
    float f = float(i);
    float fk = f / 4.0;
    float sc = 1.0 + 0.45 * f;
    float x = p.x * sc + f * 2.3 + 0.06 * m * sin(a + f);
    float A = 0.07 + 0.05 * f;
    float y = 0.12 - 0.27 * f + A * ridge(x, f);
    float e = 0.003;
    float slope = A * (ridge(x + e, f) - ridge(x, f)) / e * sc;
    float dd = y - p.y;
    float cov = smoothstep(-u_px * 1.2, u_px * 1.2, dd);
    float lit = smoothstep(-0.55, 0.45, -slope + 0.14 * m * sin(a + f * 0.7) + 0.05);
    float litE = mix(lit, 0.42 + 0.2 * lit, smoothstep(0.02, 0.3, dd));
    vec3 lc = mix(u_pal[4], u_pal[3], litE);
    lc = mix(u_pal[2], lc, 0.35 + 0.65 * fk);
    float crest = exp(-max(dd, 0.0) / (0.006 + 0.004 * f)) * lit;
    lc = mix(lc, u_pal[5], crest * (0.25 + 0.3 * fk));
    lc.x *= 1.0 - (0.1 + 0.08 * fk) * smoothstep(0.0, 0.45, dd);
    float rf = 34.0 + 10.0 * f;
    float rv = sin(dd * rf + p.x * 3.0 + 0.6 * sin(p.x * 2.1 + f) + a);
    float fade = sat(1.0 - rf * u_px * 3.0) * fk * fk * rip;
    lc.x += 0.03 * rv * fade * (0.5 + 0.5 * lit);
    col = mix(col, lc, cov);
  }
  return outLab(col);
}
