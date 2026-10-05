// Fluted: colour drifting behind a pane of reeded glass. Each reed is a small cylindrical lens that flips and squeezes
// the colour behind it, with a soft highlight down its face and a fine seam between reeds. The glass stays still; only
// the colour behind it moves.
// Palette: 0 base, 1 to 4 the drifting colours, 5 the glass highlight.
// Options: x = motion (how far the colours drift), y = reeds (fine to wide).
const float T = 48.0;

vec3 field(vec2 p, float a, float m) {
  vec3 acc = u_pal[0] * 0.25;
  float ws = 0.25;
  for (int i = 0; i < 4; i++) {
    float f = float(i);
    float n = mod(f, 2.0) < 0.5 ? 1.0 : -1.0;
    vec2 c = vec2(cos(f * 1.9 + 0.5) * 1.05, sin(f * 2.7 + 1.0) * 0.55) + orbit(n * a + f * 1.7, 0.45 * m);
    vec2 d = p - c;
    float w = exp(-dot(d, d) / 0.28);
    acc += u_pal[i + 1] * w;
    ws += w;
  }
  return acc / ws;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float W = mix(0.065, 0.16, u_opt.y);
  float xr = p.x / W;
  float cell = floor(xr);
  float u = fract(xr) * 2.0 - 1.0;
  vec2 ps = vec2((cell + 0.5) * W - u * W * 0.85, p.y + 0.015 * u * u);
  vec3 c = (field(ps, a, m) * 2.0 + field(ps + vec2(0.0, 0.05), a, m) + field(ps - vec2(0.0, 0.05), a, m)) * 0.25;
  float lum = 0.965 + 0.05 * (1.0 - u * u);
  float hl = gauss(u + 0.5, 0.18) * 0.1 + gauss(u - 0.72, 0.07) * 0.04;
  float sw = max(0.06, 1.6 * u_px / W);
  float seam = gauss(1.0 - abs(u), sw) * 0.05;
  vec3 col = vec3(c.x * lum - seam, c.yz * (1.0 + 0.08 * (1.0 - u * u)));
  col = mix(col, u_pal[5], hl);
  return outLab(col);
}
