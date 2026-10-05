// Terraces: flooded rice terraces seen from above at sunset. Each terrace is a still pool following the contour of the
// hill, and each pool holds its own piece of the evening sky, a little brighter or deeper than its neighbour; the thin
// green banks between them trace the hillside's curves. Cloud reflections drift slowly across the pools, and a breath of
// wind now and then dulls a pool's mirror. The clouds' field repeats along their path, so the loop is exact.
// Palette: 0 the sky's deepest, 1 the sky, 2 the sky's glow, 3 the banks, 4 the banks in the light, 5 the bright cloud.
// Options: x = motion (how fast the reflections drift), y = terraces (few to many).
const float T = 80.0;

float hill(vec2 p) {
  return 0.55 * p.y - 0.3 * p.x + 0.09 * dot(p, p) + 0.35 * fbm3(vec3(p * 0.7, 2.0)) + 0.12 * sin(p.x * 1.3 + 0.5);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float N = mix(6.0, 11.0, u_opt.y);
  float h = hill(p);
  float e = 0.004;
  vec2 g = vec2(hill(p + vec2(e, 0.0)) - h, hill(p + vec2(0.0, e)) - h) / e;
  float gl = length(g) + 1e-4;
  float x = h * N;
  float id = floor(x);
  float fr = fract(x);
  // the bank: a thin line at the lower edge of each pool, wider where the hill is gentle
  float dist = fr / (N * gl);
  // the bank: an earthen bund along the lower edge of each pool, a little uneven in width as banks built by hand are
  float bw = 0.024 + 0.018 * gnoise(vec3(p * 4.0, id * 1.3)) + 0.006 * sin(id * 1.7);
  float u = dist / bw;
  float bank = smoothstep(1.0 + edgePx() / bw, 0.7, u);
  float bank2 = smoothstep(2.2, 1.0, u);
  // the sky reflected in the pool: the same sky everywhere, but each pool turned a little toward its own slope
  float D = 7.0;
  float R = D / TAU;
  vec2 rq = p + 0.22 * vec2(sin(id * 2.1), cos(id * 1.3));
  float ph = TAU * (rq.x + 0.4 * rq.y) / D + a;
  float cl = fbm3(vec3(R * cos(ph), R * sin(ph), rq.y * 0.9 - 0.4 * rq.x) * 1.3);
  float glow = smoothstep(-1.4, 1.2, -p.x * 0.6 + p.y * 0.5);
  vec3 sky = mix(u_pal[0], u_pal[1], glow);
  sky = mix(sky, u_pal[2], smoothstep(0.55, 1.0, glow) * 0.8);
  sky = mix(sky, u_pal[5], smoothstep(0.0, 0.35, cl) * 0.55);
  // each pool a touch brighter or deeper; wind dulls one now and then
  float pv = sin(id * 12.9898) * 0.5;
  sky.x += 0.07 * pv;
  sky.yz *= 1.0 + 0.15 * cos(id * 4.1);
  float wind = smoothstep(0.55, 0.95, sin(a * (1.0 + mod(id, 2.0)) + id * 2.7)) * 0.5;
  float ripple = gnoise(vec3(p * 60.0, id));
  sky = mix(sky, u_pal[1], wind * 0.25);
  sky.x += wind * 0.015 * ripple;
  // the banks: grass, lit along their top by the low sun
  vec2 nd = g / gl;
  float facing = sat(dot(nd, normalize(vec2(-0.7, 0.7))) * 0.8 + 0.4);
  // the bund's profile: a low rounded ridge of earth with grass on its crown; lit on the side that faces the low sun
  float prof = cos(3.14159 * sat(u));
  float side = (facing - 0.6) * 2.0;
  float grass = gnoise(vec3(p * vec2(70.0, 90.0), id)) * 0.6 + gnoise(vec3(p * 22.0, id + 3.0)) * 0.4;
  float crown = smoothstep(0.1, 0.5, u) * smoothstep(1.0, 0.55, u) * smoothstep(-0.3, 0.4, grass + gnoise(vec3(p * 4.0, id + 5.0)));
  vec3 bk = mix(u_pal[3], u_pal[4], crown * 0.8);
  bk.x *= 0.9 + 0.22 * prof * side + 0.08 * grass;
  vec3 col = mix(sky, bk, bank);
  // the water close under each bund holds its dark reflection, and the edge of the pool below takes the bund's colour
  col.x -= 0.035 * (bank2 - bank);
  float lip = smoothstep(0.018 + edgePx(), 0.0, (1.0 - fr) / (N * gl));
  col = mix(col, u_pal[3], 0.3 * lip);
  return outLab(col);
}
