// Lagoon: sunlight focused by moving water onto a shallow floor. The caustic net is computed from the water surface
// itself: the surface is a sum of travelling waves whose speeds are whole multiples of the loop, its curvature is exact,
// and where the refracted light converges the floor brightens. It is done once per colour channel at slightly
// different focal depths, so each line carries a faint rainbow edge.
// Palette: 0 deep water, 1 shallow water, 2 sand, 3 caustic light, 4 shade, 5 sparkle.
// Options: x = motion (how strongly the water moves), y = clarity (how sharp the light lines are).
const float T = 40.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.5, 1.4, u_opt.x);
  float clar = mix(0.2, 1.0, u_opt.y);
  vec2 x = p + 0.06 * m * vec2(lnoise(p * 0.8, a, 0.3), lnoise(p * 0.8 + 4.0, a, 0.3));
  x.y *= 1.12;
  float hxx = 0.0;
  float hyy = 0.0;
  float hxy = 0.0;
  for (int i = 0; i < 7; i++) {
    float f = float(i);
    float an = f * 2.39996 + 0.3;
    float kk = 10.5 + 3.0 * sin(f * 1.7 + 0.4);
    vec2 k = vec2(cos(an), sin(an)) * kk;
    float sg = mod(f, 2.0) < 0.5 ? 1.0 : -1.0;
    float n = sg * (1.0 + floor(f / 3.0));
    float A = 0.3 / (kk * kk) * (0.7 + 0.3 * m) * (1.0 - 0.06 * f);
    float s = -A * sin(dot(k, x) + n * a + f * 1.3);
    hxx += s * k.x * k.x;
    hyy += s * k.y * k.y;
    hxy += s * k.x * k.y;
  }
  float tr = hxx + hyy;
  float det = hxx * hyy - hxy * hxy;
  vec3 z = vec3(0.95, 1.0, 1.06) * 2.3;
  vec3 J = 1.0 + z * tr + z * z * det;
  float eps = mix(0.35, 0.06, clar);
  vec3 I = (1.0 + eps) / (abs(J) + eps);
  I = max(pow(I, vec3(1.2)) - 0.8, vec3(0.0));

  float depth = smoothstep(-1.3, 1.3, p.y * 0.85 - p.x * 0.3);
  vec3 water = mix(u_pal[1], u_pal[0], depth);
  water = mix(water, u_pal[2], mix(0.25, 0.08, u_dark) * (1.0 - depth));
  vec3 base = lab2lin(water);
  vec3 cl = lab2lin(u_pal[3]);
  vec3 lin = base * 0.78 + cl * I * mix(0.16, 0.22, u_dark) * (1.0 - 0.45 * depth);
  float lum = dot(I, vec3(0.3, 0.5, 0.2));
  lin += lab2lin(u_pal[5]) * pow(max(lum - 3.0, 0.0), 2.0) * 0.02;
  lin = mix(lin, lab2lin(u_pal[4]), 0.3 * smoothstep(0.6, 2.0, length(p * vec2(0.7, 1.0))));
  return lin2srgb(shoulder(lin));
}
