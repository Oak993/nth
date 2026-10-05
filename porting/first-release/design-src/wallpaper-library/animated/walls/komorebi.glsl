// Komorebi: sunlight through the leaves of a tree outside, falling on a quiet wall through a window. The leaves throw
// their shadows into the patch of sun, the far ones soft and the near ones clearer, and they sway and turn a little
// as a breeze moves the branches. Outside the patch the wall rests in cool shade.
// Palette: 0 the wall in shade, 1 the shade's second colour, 2 the sun on the wall, 3 the sun's warm core, 4 the
// patch's soft edge, 5 the green light the leaves give back.
// Options: x = motion (how much the breeze moves the leaves), y = leaves (a few to many).
const float T = 60.0;

// one layer of leaves: cells of size 1/sc, each holding one leaf (or none) that sways on its own whole-number rhythm;
// returns how much of the sun they block at p
float leaves(vec2 p, float sc, float a, float m, float many, float seed, float soft) {
  vec2 q = p * sc;
  vec2 c0 = floor(q);
  float sh = 0.0;
  for (int i = 0; i < 9; i++) {
    vec2 c = c0 + vec2(mod(float(i), 3.0) - 1.0, floor(float(i) / 3.0) - 1.0);
    vec3 h = hash33(vec3(c, seed));
    vec3 g = hash33(vec3(c + 17.0, seed + 3.0));
    float there = smoothstep(0.0, 0.25, g.x * 0.5 + 0.5 - 0.25 - 0.35 * many);
    float n = 1.0 + floor((g.y * 0.5 + 0.5) * 2.99);
    float ang = 0.95 + 0.55 * h.z + 0.12 * m * sin(n * a + g.z * 6.28);
    vec2 sw = 0.07 * m * vec2(sin(n * a + h.x * 6.28), cos(n * a + h.y * 6.28));
    vec2 d = rot(q - c - 0.5 - 0.3 * h.xy - sw, ang);
    float L = 0.55 + 0.25 * (g.z * 0.5 + 0.5);
    float u = d.x / L;
    float e = max(abs(u) - 1.0, abs(d.y) - 0.24 * L * (1.0 - u * u));
    sh = max(sh, there * smoothstep(soft, -soft, e));
  }
  return sh;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float many = u_opt.y;
  // the wall in shade, with a faint green light given back by the leaves
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(-1.2, 1.0, p.y - 0.3 * p.x));
  col = mix(col, u_pal[5], 0.14 * smoothstep(-0.4, 0.6, lnoise(p * 0.7 + 3.0, a, 0.15 * m)));
  // the patch of sun from a window, slanting across the wall
  vec2 sd = rot(p - vec2(0.35, 0.1) - u_sun, -0.35);
  float spot = smoothstep(0.35, -0.2, max(abs(sd.x) - 1.15, abs(sd.y) - 0.72));
  // the leaves of a tree outside throw their shadows into it, the far ones soft, the near ones clearer; where the
  // leaves crowd, toward the upper right, the shadow is deepest
  float crowd = smoothstep(-0.3, 1.5, p.x + p.y * 0.8 + 0.7 * lnoise(p * 0.7 + 9.0, a, 0.1 * m));
  float s1 = leaves(p + tiltD(-1.0), 2.2, a, m * 0.8, many, 1.0, 0.12 + 0.05 * u_focus) * (0.4 + 0.6 * crowd);
  float s2 = leaves(p + tiltD(1.0), 3.4, a + 0.7, m, many * 0.8, 5.0, 0.08 + 0.04 * u_focus) * crowd;
  float lit = spot * (1.0 - 0.45 * s1) * (1.0 - 0.6 * s2);
  vec3 sun = mix(u_pal[2], u_pal[3], smoothstep(0.6, 1.0, lit));
  col = mix(col, u_pal[4], 0.3 * spot);
  col = mix(col, sun, sat(lit * mix(0.85, 0.72, u_dark)));
  return outLab(col);
}
