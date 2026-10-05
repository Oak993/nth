// Glasswave: two rods of clear glass, flattened and flowing in slow waves, lit from behind by coloured light. Like any
// solid glass rod, each shows the light behind it turned over and squeezed, a darker band where the light reflects
// inside, and thin bright lines near the edges, every one split by the glass into a faint spectrum. The light the rods
// focus falls as a soft coloured glow on the wall behind.
// Palette: 0 backdrop, 1 backdrop glow, 2 first light behind the glass, 3 second light, 4 third light, 5 the white of
// the edges and highlights.
// Options: x = motion (how far the glass flows), y = thickness (slim rods to broad, deep ones).
// Perspective: the rods lie at three depths (tiltD -1, 0, +1), their glow on the wall behind them.
const float T = 40.0;

// one rod: returns (across -1..1, along, half width, coverage distance); its centre line is y = c(x)
vec4 rod(vec2 p, float i, float a, float m, float th) {
  float ph = i * 0.75;
  float x = p.x;
  float base = -0.5 + 0.34 * i;
  float tilt = 0.2 - 0.03 * i;
  float A = 0.17 + 0.03 * i;
  float c = base + tilt * x + 0.2 * sin(x * 0.8 + 0.6) + m * (A * sin(x * 1.25 + a + ph) + 0.05 * sin(x * 2.3 - 2.0 * a + ph * 2.0));
  float dc = tilt + 0.16 * cos(x * 0.8 + 0.6) + m * (A * 1.25 * cos(x * 1.25 + a + ph) + 0.115 * cos(x * 2.3 - 2.0 * a + ph * 2.0));
  float w = th * (0.24 - 0.065 * i) * (1.0 + 0.3 * sin(x * 0.9 - a + ph * 1.9 + 1.0));
  float d = (p.y - c) / sqrt(1.0 + dc * dc);
  return vec4(d / w, x, w, abs(d) - w);
}

vec3 env(float s, float u, float a, float i) {
  float g = s + 0.35 * sin(u * 0.9 + a + i * 1.7);
  vec3 c = mix(u_pal[2], u_pal[3], smoothstep(-0.9, 0.5, g));
  c = mix(c, u_pal[4], smoothstep(0.2, 1.2, g + 0.3 * sin(u * 0.6 - a)));
  return lab2lin(c);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.5, u_opt.x);
  float th = mix(0.7, 1.35, u_opt.y);
  vec3 wht = lab2lin(u_pal[5]);
  vec3 bg = lab2lin(mix(u_pal[0], u_pal[1], smoothstep(-1.4, 1.2, -p.y * 0.6 + p.x * 0.4)));
  vec3 col = bg;
  // the light the rods focus onto the wall, a little below them
  for (int j = 0; j < 3; j++) {
    vec4 r = rod(p + tiltD(float(j) - 1.5) + vec2(0.0, 0.12), float(j), a, m, th);
    float g = exp(-r.x * r.x * 0.35) * (0.6 + 0.4 * sin(r.y * 1.1 + a + float(j)));
    col += env(0.0, r.y, a, float(j)) * g * mix(0.1, 0.18, u_dark);
  }
  for (int j = 0; j < 3; j++) {
    float fj = float(j);
    vec4 r = rod(p + tiltD(fj - 1.0), fj, a, m, th);
    float v = r.x;
    float av = abs(v);
    float cov = smoothstep(1.3 * edgePx(), -1.3 * edgePx(), r.w);
    float nz = sqrt(max(1.0 - v * v, 0.0));
    // the light behind, seen through the rod: turned over and squeezed toward the centre
    vec3 c;
    vec3 vk = av * vec3(0.985, 1.0, 1.016);
    vec3 e1 = (vk - 0.9) / 0.03;
    vec3 e2 = (vk - 0.58) / 0.05;
    vec3 e3 = (vk - 0.76) / 0.07;
    vec3 line = exp(-e1 * e1) * 0.9 + exp(-e2 * e2) * 0.22;
    vec3 band = exp(-e3 * e3);
    c = env(-0.75 * v, r.y, a, fj);
    // what lies behind (the wall, or a rod further back) shows through, turned over with the rest
    c += (col - bg) * 0.55;
    c *= 0.75 + 0.45 * nz;
    c *= 1.0 - 0.45 * band;
    c += wht * line * mix(0.5, 0.8, u_dark);
    // the crest catches the studio light; the far edge reflects the dark
    float crest = exp(-pow((v - 0.72) / 0.12, 2.0));
    c += wht * crest * 0.12;
    c = mix(c, bg * 0.6, smoothstep(0.93, 1.0, av) * 0.5);
    // in light mode the glass reads lighter and clearer: the wall shows through it
    vec3 cl = mix(bg, c, 0.75) + wht * line * 0.15;
    c = mix(cl, c, u_dark);
    col = mix(col, c, cov);
  }
  return lin2srgb(shoulder(col));
}
