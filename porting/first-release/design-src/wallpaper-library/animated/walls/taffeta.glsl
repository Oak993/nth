// Taffeta: a length of shot silk lifting in a slow wind. Shot silk is woven with a warp of one colour and a weft of
// another, so the cloth shows one where it faces you and the other where it turns away: as the billows travel across
// it, the colour rolls from one to the other along every fold. A long soft sheen rides the crests, and the troughs
// fall into a deep, rich shade.
// Palette: 0 the deepest shade, 1 the warp (facing colour), 2 the weft (the colour at a slant), 3 the sheen, 4 the
// warm light on the crests, 5 the shade's colour.
// Options: x = motion (how high the cloth billows), y = folds (broad billows to fine ripples).
// Dynamic: u_sun turns the light, so the sheen slides across the folds through the day.
const float T = 45.0;

float cloth(vec2 q, float a, float m) {
  // flow lines across the cloth, bent by billows travelling along it; the folds lie along them
  float v = q.y;
  float u = q.x * 3.6 + 0.9 * sin(v * 0.9 + a + 0.3 * q.x) + 0.28 * m * sin(v * 1.7 - 2.0 * a + 1.0) + 0.12 * m * sin(v * 3.1 + 3.0 * a + 2.0);
  float h = (sin(u) * 0.62 + 0.24 * sin(2.0 * u + 0.6 + 0.5 * sin(a + v * 0.7))) * (0.75 + 0.35 * sin(v * 0.6 - q.x * 0.5 + a));
  // a soft crease in each trough, where the cloth folds under
  h -= 0.4 * pow(0.5 - 0.5 * sin(u + 0.25), 5.0);
  // the whole sheet lifts and settles in long swells
  h += 0.5 * m * sin(v * 1.1 + q.x * 0.6 - a);
  return h;
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.45, 1.4, u_opt.x);
  float fk = mix(0.75, 1.5, u_opt.y);
  vec2 q = rot(p, -0.42) * fk;
  float e = 0.004;
  float h0 = cloth(q, a, m);
  float hx = cloth(q + vec2(e, 0.0), a, m);
  float hy = cloth(q + vec2(0.0, e), a, m);
  vec2 g = vec2(hx - h0, hy - h0) / e * 0.36;
  vec3 n = normalize(vec3(-g, 1.0));
  vec3 L = normalize(vec3(vec2(-0.45, 0.55) + u_sun, 0.7));
  vec3 V = vec3(0.0, 0.0, 1.0);
  float facing = n.z;
  // the two threads: the warp seen face on, the weft as the cloth turns away
  float slant = smoothstep(0.93, 0.55, facing);
  vec3 base = mix(u_pal[1], u_pal[2], slant * 0.8);
  float dif = sat(dot(n, L) * 1.1 + 0.05);
  float occ = smoothstep(-0.9, 0.5, h0);
  vec3 col = mix(u_pal[0], base, smoothstep(0.0, 0.8, dif * (0.55 + 0.45 * occ)));
  col = mix(col, setL(base, base.x * 1.18), smoothstep(0.75, 1.0, dif) * 0.6);
  col = mix(col, u_pal[5], 0.35 * (1.0 - occ) * (1.0 - dif));
  // silk's sheen: long and soft across the threads, a brighter line along the crest
  vec3 H = normalize(L + V);
  float nh = sat(dot(n, H));
  float sheen = pow(nh, 7.0) * 0.45 + pow(nh, 70.0) * 0.7;
  vec3 sc = mix(u_pal[3], u_pal[4], smoothstep(0.2, 0.9, h0 * 0.5 + 0.5));
  col = mix(col, sc, sat(sheen * mix(0.75, 0.9, u_dark)));
  col.x *= 0.92 + 0.1 * smoothstep(2.2, 0.4, length(p - vec2(-0.6, 0.6)));
  return outLab(col);
}
