// Fields: three soft fields of colour floating on a ground, after the colour-field painters: feathered, slightly uneven
// edges, a glow from within, and colours and edges that breathe so slowly the change is only noticed afterwards.
// Palette: 0 ground, 1 upper field, 2 middle band, 3 lower field, 4 inner glow, 5 unused highlight.
// Options: x = motion (how much the fields breathe), y = edges (crisp to feathered).
const float T = 90.0;

float field(vec2 p, vec2 c, vec2 hs, float soft, float a, float k, float m) {
  vec2 q = abs(p - c) - hs;
  float d = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - 0.05;
  d += 0.022 * lnoise(p * 2.3 + k * 7.0, a, 0.25 * m) + 0.01 * lnoise(p * 6.0 + k * 3.0, a, 0.2 * m);
  return d;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float soft = mix(0.025, 0.12, u_opt.y);
  vec3 col = mix(u_pal[0], setL(u_pal[0], u_pal[0].x * 0.94), smoothstep(-1.0, 1.0, -p.y));
  float b = 0.02 * m;
  float d1 = field(p, vec2(0.0, 0.47 + b * sin(a)), vec2(1.22, 0.3 + b * sin(a + 1.0)), soft, a, 1.0, m);
  float d2 = field(p, vec2(0.0, -0.06), vec2(1.22, 0.09 + 0.6 * b * sin(a + 2.5)), soft, a, 2.0, m);
  float d3 = field(p, vec2(0.0, -0.56 - b * sin(a + 1.7)), vec2(1.22, 0.27 + b * sin(a + 3.0)), soft, a, 3.0, m);
  vec3 c1 = hueShift(u_pal[1], 0.07 * m * sin(a));
  vec3 c2 = hueShift(u_pal[2], 0.07 * m * sin(a + 2.0));
  vec3 c3 = hueShift(u_pal[3], 0.07 * m * sin(a + 4.0));
  c1 = mix(c1, u_pal[4], 0.22 * smoothstep(0.0, -0.3, d1));
  c3 = mix(c3, u_pal[4], 0.18 * smoothstep(0.0, -0.28, d3));
  col = mix(col, c1, smoothstep(soft, -soft, d1));
  col = mix(col, c2, smoothstep(soft, -soft, d2));
  col = mix(col, c3, smoothstep(soft, -soft, d3));
  return outLab(col);
}
