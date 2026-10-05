// Mist: coloured mist lying in four layers at different depths. A slow camera orbit shifts each layer by an amount set
// by its depth, so the near veils slide across the far ones (parallax); the near veils are larger and softer, the far
// ones finer and paler, and the clouds themselves change shape only slowly. Calm, but never a flat gradient.
// Palette: 0 the far ground, 1 to 4 the layers from far to near, 5 a faint light from above.
// Options: x = motion (how far the camera drifts), y = depth (how far apart the layers sit).
const float T = 90.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float dp = mix(0.35, 1.4, u_opt.y);
  vec2 cam = orbit(a, 0.22 * m) + vec2(0.0, 0.05 * m * sin(2.0 * a));
  vec3 col = mix(u_pal[0], setL(u_pal[0], u_pal[0].x * 1.06 + 0.01), smoothstep(-1.0, 1.0, p.y));
  col = mix(col, u_pal[5], 0.16 * smoothstep(0.1, 1.2, p.y - 0.2 * p.x));
  for (int i = 0; i < 4; i++) {
    float f = float(i);
    float z = 0.25 + 0.55 * f * dp;
    float sc = 1.25 - 0.22 * f;
    vec2 q = (p + cam * z) * sc + vec2(f * 3.1, f * 1.7);
    float n = gnoise(vec3(q * 0.9, f * 2.0) + vec3(orbit(a + f, 0.08 * m), 0.06 * m * sin(a + f)))
            + 0.45 * gnoise(vec3(q * 1.9, f * 5.0 + 1.0) + vec3(orbit(-a, 0.06 * m), 0.0));
    float soft = 0.08 + 0.07 * f;
    float d = smoothstep(-0.12 - soft, 0.25 + soft, n + 0.1 * (p.y * (f - 1.5) * 0.3));
    float al = (0.42 + 0.12 * f) * d;
    vec3 lc = u_pal[1 + i];
    lc.x += 0.035 * smoothstep(0.25, 0.75, d) * (1.0 - smoothstep(0.75, 1.0, d));
    col = mix(col, lc, al);
    col.x -= 0.012 * smoothstep(0.2, 0.6, d) * (1.0 - smoothstep(0.6, 1.0, d)) * (f / 3.0);
  }
  return outLab(col);
}
