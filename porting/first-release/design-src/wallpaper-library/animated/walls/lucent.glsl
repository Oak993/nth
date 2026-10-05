// Lucent: the light that passes through cut glass onto a sunlit table. It falls as a broad band of coloured light; in
// it the glass gathers the sun into a few long, bright curves of caustic light, each edged with a little
// spectrum. The glass turns imperceptibly in the moving air, so the curves slide, fold and part.
// The caustic is computed, not drawn: a gently curved glass surface bends parallel light, and where the bent rays
// converge (where the map's Jacobian falls to zero) the light piles up; done once per colour channel at slightly
// different strengths, which is the dispersion.
// Palette: 0 table, 1 table's far tone, 2 the pool's warm colour, 3 the pool's cool colour, 4 shade, 5 caustic light.
// Options: x = motion (how far the curves travel), y = caustic (soft to sharp).
const float T = 60.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.4, 1.3, u_opt.x);
  float foc = mix(0.0, 1.0, u_opt.y);
  // the light through the glass lies in a broad band across the table, its edges soft and wavering
  vec2 pc = vec2(0.35, 0.05) + u_sun * 0.6;
  vec2 d = rot(p - pc, -0.5) / vec2(2.4, 0.95);
  float rr = length(d);
  float pool = smoothstep(1.25, 0.45, rr + 0.08 * lnoise(p * 0.9, a, 0.25));
  float core = pool * (0.6 + 0.4 * smoothstep(0.9, 0.0, rr));
  // the glass surface: a few long waves, its curvature exact
  vec2 x = rot(p - pc, -0.38) * vec2(0.8, 1.15) + 0.05 * m * vec2(lnoise(p * 0.6, a, 0.3), lnoise(p * 0.6 + 3.0, a, 0.3));
  float hxx = 0.0;
  float hyy = 0.0;
  float hxy = 0.0;
  for (int i = 0; i < 4; i++) {
    float f = float(i);
    float an = f * 2.39996 + 0.9;
    float kk = 2.6 + 0.9 * f;
    vec2 k = vec2(cos(an), sin(an)) * kk;
    float sg = mod(f, 2.0) < 0.5 ? 1.0 : -1.0;
    float A = (0.62 - 0.08 * f) / (kk * kk) * (0.8 + 0.2 * m);
    float s = -A * sin(dot(k, x) + sg * a + f * 1.3 + 0.35 * m * sin(a + f));
    hxx += s * k.x * k.x;
    hyy += s * k.y * k.y;
    hxy += s * k.x * k.y;
  }
  float tr = hxx + hyy;
  float det = hxx * hyy - hxy * hxy;
  vec3 z = vec3(0.94, 1.0, 1.065) * 2.15;
  vec3 J = 1.0 + z * tr + z * z * det;
  float eps = mix(0.22, 0.05, foc);
  vec3 I = (1.0 + eps) / (abs(J) + eps);
  I = max(I - 1.0, vec3(0.0));
  // the table and the light
  vec3 table = mix(u_pal[0], u_pal[1], smoothstep(-1.2, 1.4, p.y + 0.2 * p.x));
  table = mix(table, u_pal[4], 0.35 * smoothstep(0.6, 1.9, length((p - vec2(-0.2, 0.0)) * vec2(0.6, 1.0))));
  vec3 lin = lab2lin(table);
  vec3 tint = lab2lin(mix(u_pal[2], u_pal[3], smoothstep(-0.9, 0.9, d.x - 0.6 * d.y)));
  lin = mix(lin, lin * mix(0.62, 0.5, u_dark), pool);
  lin = mix(lin, tint, core * mix(0.75, 0.6, u_dark));
  vec3 cl = mix(lab2lin(u_pal[5]), tint / max(dot(tint, vec3(0.33)), 1e-3) * 0.6, 0.35);
  lin += cl * I * core * mix(0.16, 0.12, u_dark);
  // a faint weave in the cloth
  lin *= 1.0 + 0.02 * gnoise(vec3(p * vec2(240.0, 240.0), 1.0));
  return lin2srgb(shoulder(lin));
}
