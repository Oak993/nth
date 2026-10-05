// Ribbon: two satin ribbons floating across the window, twisting slowly as they go. Where a ribbon turns edge-on it
// narrows to a line; its two faces have their own colours, a sheen runs along it, and each casts a soft shadow behind.
// Palette: 0 background top, 1 background low, 2 ribbon face, 3 face second colour, 4 the reverse face, 5 sheen.
// Options: x = motion (how far the ribbons sway and twist), y = width.
const float T = 40.0;

float yc(float x, float a, float m, float off, float ph) {
  return off + (0.2 * sin(x * 1.05 + ph + a) + 0.09 * sin(x * 0.55 - a + ph * 2.0)) * (0.7 + 0.3 * m);
}

vec4 rib(vec2 p, float a, float m, float off, float W, float ph) {
  float y0 = yc(p.x, a, m, off, ph);
  float tw = p.x * 1.15 + ph + 0.8 * m * sin(a + p.x * 0.45);
  float ct = cos(tw);
  float h = W * abs(ct) + 1e-3;
  float v = (p.y - y0) / h;
  float cov = 1.0 - smoothstep(1.0 - 1.5 * u_px / h, 1.0 + 1.5 * u_px / h, abs(v));
  float bend = tw + 0.45 * v * sign(ct);
  vec2 n = vec2(-sin(bend), cos(bend));
  n *= sign(n.y);
  vec2 L = normalize(vec2(0.55, 0.83));
  float dif = dot(n, L);
  float spec = pow(sat(dot(n, normalize(L + vec2(0.0, 1.0)))), 40.0);
  float front = step(0.0, ct);
  vec3 fc = mix(u_pal[2], u_pal[3], 0.5 + 0.5 * sin(p.x * 0.7 + ph));
  vec3 c = mix(u_pal[4], fc, front);
  c.x *= 0.55 + 0.5 * sat(dif);
  c = mix(c, u_pal[5], spec * 0.7);
  c.x -= 0.06 * smoothstep(0.6, 1.0, abs(v));
  return vec4(c, cov);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float wk = mix(0.7, 1.35, u_opt.y);
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(-1.2, 1.1, p.y + 0.2 * p.x));
  vec2 p1 = p + tiltD(-0.5);
  vec2 p2 = p + tiltD(0.8);
  float sh1 = gauss(p1.y - yc(p1.x - 0.05, a, m, 0.38, 2.2) + 0.13, 0.2 * wk) * 0.1;
  col.x -= sh1 * mix(0.7, 1.0, u_dark);
  vec4 r1 = rib(p1, a, m, 0.38, 0.13 * wk, 2.2);
  col = mix(col, r1.xyz, r1.w);
  float sh2 = gauss(p2.y - yc(p2.x - 0.06, a, m, -0.18, 0.0) + 0.16, 0.26 * wk) * 0.14;
  col.x -= sh2 * mix(0.7, 1.0, u_dark);
  vec4 r2 = rib(p2, a, m, -0.18, 0.22 * wk, 0.0);
  col = mix(col, r2.xyz, r2.w);
  return outLab(col);
}
