// Cirrus: looking up into the sky just after dawn. Thin, high cirrus lies across it in long combed streaks, lit from a
// sun still below the lower left, and their light turns slowly from rose to gold and back, as the colour of a real
// dawn shifts. A lower veil, softer and nearer, drifts across the high one.
// Palette: 0 the sky overhead, 1 the sky toward the sun, 2 the clouds' shaded side, 3 the clouds' first light (rose),
// 4 their second light (gold), 5 the glow round the hidden sun.
// Options: x = motion (how much the clouds drift and comb), y = cover (a few streaks to a full veil).
const float T = 90.0;

vec3 mixh(vec3 a, vec3 b, float k) {
  float ca = length(a.yz);
  float cb = length(b.yz);
  float ha = atan(a.z, a.y + 1e-6);
  float dh = atan(b.z, b.y + 1e-6) - ha;
  dh -= TAU * floor(dh / TAU + 0.5);
  float h = ha + dh * k;
  float c = mix(ca, cb, k);
  // near-opposite hues have no shortest way round: there it falls back to the straight mix, so nothing ever jumps
  return mix(vec3(mix(a.x, b.x, k), c * cos(h), c * sin(h)), mix(a, b, k), smoothstep(2.3, 2.9, abs(dh)));
}

// one layer of streaks: long along x, combed fine across y, the fibres bent by a slow warp
float streaks(vec2 q, float a, float m, float seed, float cov) {
  float w = lnoise(q * vec2(0.5, 1.2) + seed, a, 0.22 * m);
  vec2 s = vec2(q.x * 0.55, q.y * 2.6 + 0.5 * w + 0.12 * sin(q.x * 1.3 + seed));
  float body = lnoise(s + vec2(seed, 0.0), a, 0.28 * m);
  float fib = gnoise(vec3(s.x * 2.2, s.y * 9.0, seed + 0.4 * w));
  float d = body + 0.32 * fib + mix(-0.28, 0.12, cov);
  return smoothstep(-0.02, 0.45, d) * (0.75 + 0.25 * fib);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float cov = u_opt.y;
  vec2 sun = vec2(-1.75, -1.25) + u_sun;
  vec2 ds = p - sun;
  float near = exp(-dot(ds, ds) / 3.2);
  // the dawn moves through its colours once and back over the loop
  float k = 0.5 - 0.5 * cos(a);
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(0.6, 3.2, length(ds)));
  sky = mix(sky, u_pal[5], 0.4 * near * (0.8 + 0.2 * k));
  vec3 lit = mixh(u_pal[3], u_pal[4], k);
  // the high layer, combed at a slant, and a nearer, softer veil
  vec2 q1 = rot(p + tiltD(-1.0), 0.32) * 1.1;
  float c1 = streaks(q1, a, m, 0.0, cov);
  vec2 q2 = rot(p + tiltD(1.0), 0.22) * 0.7 + vec2(3.0, 1.0);
  float c2 = streaks(q2, a + 1.7, m * 0.8, 11.0, cov * 0.7) * 0.6;
  float facing = sat(0.35 + 0.65 * near * 1.6 - 0.15 * max(p.y, 0.0));
  vec3 cc = mix(u_pal[2], lit, facing);
  vec3 col = mix(sky, cc, c1 * 0.62);
  col = mix(col, mix(u_pal[2], lit, 0.55 * facing + 0.2), c2 * 0.4);
  return outLab(col);
}
