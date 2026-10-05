// Borealis: curtains of aurora over a night sky. Each curtain has a bright foot and a long upward fade, folds that
// travel along it, and vertical rays that shimmer. In light mode the curtains are washes of colour on a dawn sky.
// Palette: 0 zenith, 1 horizon, 2 curtain foot, 3 curtain body, 4 curtain crown, 5 horizon glow.
// Options: x = motion (how far the curtains fold and sway), y = glow.
const float T = 60.0;

float stars(vec2 p, float a) {
  float sc = 30.0;
  vec2 q = p * sc;
  vec2 id = floor(q);
  vec3 h = hash33(vec3(id, 3.7)) * 0.5 + 0.5;
  vec2 c = id + 0.5 + (h.xy - 0.5) * 0.7;
  float d = length(q - c) / sc;
  float r = 0.85 * u_px;
  float s = exp(-(d * d) / (r * r)) * step(0.84, h.z) * (h.z - 0.84) * 6.0;
  float tw = 0.7 + 0.3 * sin(a * 3.0 + h.x * TAU);
  float vis = sat((1.0 / sc) / u_px / 9.0 - 0.5);
  return s * tw * vis;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.6, u_opt.x);
  float glow = mix(0.5, 1.5, u_opt.y);
  float g = sat(p.y * 0.5 + 0.5);
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(0.0, 0.9, g));
  vec3 skyLin = lab2lin(sky);
  vec3 add = vec3(0.0);
  float wsum = 0.0;
  float wl = 0.0;
  for (int i = 0; i < 3; i++) {
    float f = float(i);
    float x = p.x * (0.85 + 0.12 * f) + f * 1.7;
    float base = -0.46 + 0.29 * f + 0.1 * p.x * (1.0 - f);
    float fold = 0.12 * sin(x * 1.2 + a + f * 2.0) + 0.05 * sin(x * 2.5 - 2.0 * a + f);
    fold += 0.15 * m * lnoise(vec2(x * 0.55, f * 4.0), a + f * 1.3, 0.55);
    float d = p.y - (base + fold);
    float h = 0.38 + 0.1 * f;
    float prof = d < 0.0 ? exp(-(d * d) / 0.0016) : exp(-d / h) * (0.4 + 0.6 * exp(-(d * d) / 0.02));
    float cov = smoothstep(-0.4, 0.45, lnoise(vec2(x * 0.4 + 3.0 * f, 7.0 + f), a, 0.45 + 0.3 * m));
    float rays = sat(0.55 + 0.6 * lnoise(vec2(x * 6.5 + fold * 8.0, 2.0 * f), 2.0 * a, 0.12 + 0.3 * m));
    rays = mix(1.0, rays * rays * 1.5, smoothstep(-0.02, 0.12, d) * 0.85);
    vec3 col = mix(u_pal[2], u_pal[3], smoothstep(0.0, 0.18, d));
    col = mix(col, u_pal[4], smoothstep(0.1, 0.5, d));
    float I = prof * cov * rays * (1.0 - 0.2 * f) * (1.0 + 0.6 * smoothstep(0.12, 0.5, d));
    add += lab2lin(col) * I;
    wsum += I;
    wl += I * (1.0 - 0.7 * smoothstep(0.05, 0.45, d)) * mix(1.0, 1.0 / max(rays, 0.4), 0.6);
  }
  vec3 hz = lab2lin(u_pal[5]) * exp(-(p.y + 1.0) * 2.6);
  vec3 dk = skyLin + add * glow * 0.85 + hz * 0.35 + vec3(stars(p, a)) * (1.0 - sat(wsum * 1.5)) * 0.9;
  vec3 avg = add / max(wsum, 1e-4);
  float al = 1.0 - exp(-wl * glow * 3.0);
  vec3 lt = mix(skyLin + hz * 0.15, avg, al * 0.9);
  return lin2srgb(shoulder(mix(lt, dk, u_dark)));
}
