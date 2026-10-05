// Tides: five broad waves of colour stacked across the window, each laid over the one behind with a soft shadow under
// its edge and a bright lip along its crest, rolling slowly, in the manner of the layered macOS wallpapers.
// Palette: 0 the farthest wave, 1 to 4 the waves toward the front, 5 crest light.
// Options: x = motion (how far the waves roll), y = depth (how strong the shadows are).
const float T = 45.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float depth = mix(0.35, 1.4, u_opt.y);
  vec2 dir = normalize(vec2(0.36, 1.0));
  float u = dot(p, dir);
  float v = dot(p, vec2(dir.y, -dir.x));
  vec3 col = setL(u_pal[0], u_pal[0].x + 0.03 + 0.04 * (1.0 - u_dark));
  col = mix(col, u_pal[5], 0.12 * smoothstep(0.4, 1.4, u));
  for (int k = 0; k < 5; k++) {
    float f = float(k);
    float e = 0.92 - 0.42 * f + (0.13 * sin(v * 1.15 + f * 1.9 + a) + 0.05 * sin(v * 2.5 - f + 2.0 * a)) * (0.55 + 0.45 * m)
            + 0.05 * m * lnoise(vec2(v * 0.6, f * 3.0), a, 0.4);
    float dd = e - u;
    float cov = smoothstep(-u_px, u_px, dd);
    float sh = exp(-max(-dd, 0.0) / 0.1) * (1.0 - cov) * 0.16 * depth;
    col.x -= sh * mix(0.6, 1.0, u_dark);
    vec3 lc = mix(u_pal[k], u_pal[k + 1], smoothstep(-1.6, 1.8, v) * 0.55);
    float crest = exp(-max(dd, 0.0) / 0.018) * cov;
    lc.x += 0.07 * crest - 0.06 * smoothstep(0.0, 0.42, dd);
    lc = mix(lc, u_pal[5], crest * 0.18);
    col = mix(col, lc, cov);
  }
  return outLab(col);
}
