// Apogee: the curve of a planet seen from high orbit, its night side below, and the thin shell of its atmosphere lit
// along the limb by a sun about to rise beyond it. The air glows brightest where the sun is near and fades round the
// curve; faint cloud lies on the dark side, turning slowly with the planet. The sun creeps along the limb and back.
// Palette: 0 space high, 1 space near the limb, 2 the night side, 3 the lit air (inner, warm), 4 the air's outer haze,
// 5 the sun at the limb.
// Options: x = motion (how far the sun travels along the limb), y = air (a thin line to a deep glow).
const float T = 90.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float air = mix(0.6, 1.5, u_opt.y);
  vec2 qs = p + tiltD(-1.0);
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(-0.7, 1.1, qs.y));
  // a few faint stars, dark theme only
  vec2 sc = qs * 38.0;
  vec2 cid = floor(sc);
  vec3 sh = hash33(vec3(cid, 2.0));
  float star = step(0.86, sh.z) * smoothstep(0.1, 0.0, length(sc - cid - 0.5 - 0.35 * sh.xy)) * (0.4 + 0.6 * sh.x);
  // the planet
  vec2 q = p + tiltD(1.0);
  vec2 C = vec2(0.25, -3.35);
  float Rp = 2.75;
  vec2 dc = q - C;
  float r = length(dc);
  float d = r - Rp;
  float ang = atan(dc.x, dc.y);
  // where the sun sits along the limb
  float sa = 0.36 + 0.14 * m * sin(a + 1.1) + 0.35 * u_sun.x;
  float near = gauss(ang - sa, 0.32);
  float wide = gauss(ang - sa, 0.9);
  col = mix(col, u_pal[5], star * u_dark * 0.6 * smoothstep(0.0, 0.3, d));
  // the air above the limb: a bright thin shell and a wide soft haze, both strongest toward the sun
  float shell = exp(-max(d, 0.0) / (0.025 * air)) * smoothstep(-0.012, 0.004, d);
  float haze = exp(-max(d, 0.0) / (0.2 * air)) * smoothstep(-0.03, 0.01, d);
  col = mix(col, u_pal[4], sat(haze * (0.25 + 0.55 * wide) * (0.85 + 0.15 * sin(a + 1.0))));
  // the night side, with faint cloud turning
  vec2 pc = vec2(ang * 3.0, d * 6.0);
  float cl = lnoise(pc * vec2(1.2, 2.5), a, 0.25 * m) * 0.6 + gnoise(vec3(pc * vec2(3.0, 6.0), 4.0)) * 0.4;
  vec3 ground = u_pal[2];
  ground.x += 0.03 * smoothstep(-0.1, 0.5, cl) * (0.4 + 0.6 * wide);
  ground = mix(ground, u_pal[4], 0.22 * exp(d / 0.12) * (0.3 + 0.7 * wide));
  float body = smoothstep(edgePx() * 0.7, -edgePx() * 0.7, d);
  col = mix(col, ground, body);
  col = mix(col, u_pal[3], sat(shell * (0.18 + 0.82 * near) * 0.9));
  // the sun itself, just behind the limb: a small glow and a faint flare along the shell
  vec2 sp = C + Rp * vec2(sin(sa), cos(sa));
  vec2 dsun = q - sp;
  float sun = exp(-dot(dsun, dsun) / 0.004) * 0.7 + exp(-dot(dsun, dsun) / 0.06) * 0.25;
  col = mix(col, u_pal[5], sat(sun * (0.6 + 0.4 * smoothstep(-0.02, 0.02, d))));
  return outLab(col);
}
