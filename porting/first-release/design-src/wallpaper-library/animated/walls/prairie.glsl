// Prairie: a field of tall grass running to a low horizon under a soft sky, seen a little out of focus. Gusts of wind
// cross it one after another, and where they pass the grass bends and shows its paler side, so broad waves of light
// roll slowly over the field toward the far edge. The horizon hazes into the sky; a low sun warms one side.
// Palette: 0 the sky high, 1 the sky at the horizon, 2 the far field in haze, 3 the field near, 4 the bent grass's
// pale side, 5 the low sun.
// Options: x = motion (how strong the gusts are), y = grass (smooth to fine).
const float T = 60.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float gr = u_opt.y;
  float hor = 0.38 + 0.025 * sin(p.x * 1.1 + 0.4) + 0.012 * sin(p.x * 2.9 + 2.0);
  vec2 sun = vec2(1.15, 0.52) + u_sun;
  vec2 ds = (p - sun) * vec2(0.7, 1.4);
  float glow = exp(-dot(ds, ds) / 0.5);
  vec2 qs = p + tiltD(-1.0);
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(hor - 0.05, 1.1, qs.y));
  sky = mix(sky, u_pal[5], 0.45 * glow);
  // the field in its own ground plane: depth grows toward the horizon
  vec2 q = p + tiltD(1.0);
  float below = hor - q.y;
  float z = 1.0 / max(below + 0.06, 0.05);
  vec2 g = vec2(q.x * z, z);
  // the grass: the heads mottle the field in its own plane, finer toward the horizon and faded where they would be
  // finer than a pixel; near the bottom, out-of-focus blades stand upright
  float fine = sat(0.06 / (u_px * z * z) - 0.4);
  float st = gnoise(vec3(g.x * mix(1.4, 3.0, gr), g.y * mix(2.0, 4.0, gr), 1.0)) * 0.65 + gnoise(vec3(g.x * mix(3.0, 6.5, gr), g.y * mix(4.5, 9.0, gr), 2.0)) * 0.35 * fine;
  // the gusts: waves of bending that travel across the field, their fronts curved by slow noise
  float w1 = sin(g.x * 0.55 + g.y * 1.3 - 3.0 * a + 0.8 * lnoise(g * vec2(0.25, 0.4), a, 0.3));
  float w2 = sin(g.x * 0.9 - g.y * 0.7 - 2.0 * a + 1.7 + 0.6 * lnoise(g * vec2(0.3, 0.3) + 5.0, a, 0.3));
  float bend = smoothstep(0.2, 1.0, w1) * 0.65 + smoothstep(0.4, 1.0, w2) * 0.35;
  bend *= mix(0.5, 1.0, sat(m / 1.4)) * sat(1.4 - 1.5 * z * z * u_px);
  float haze = smoothstep(0.55, 0.0, below);
  vec3 field = mix(u_pal[3], u_pal[2], haze);
  field = mix(field, u_pal[4], bend * 0.75 * (1.0 - 0.5 * haze));
  field.x += 0.035 * st * sat(0.2 / (u_px * z * z * 4.0)) * (1.0 - haze) * (0.6 + 0.4 * bend);
  float nearB = smoothstep(-0.35, -1.0, q.y);
  float blades = gnoise(vec3(q.x * mix(7.0, 14.0, gr) + 0.25 * sin(q.y * 2.0 + q.x) + 0.04 * bend, q.y * 0.9, 3.0));
  field = mix(field, mix(u_pal[3], u_pal[4], 0.5 + 0.5 * blades), nearB * 0.35 * smoothstep(-0.1, 0.5, abs(blades)));
  field = mix(field, u_pal[5], 0.2 * glow * (1.0 - smoothstep(0.0, 0.8, below)));
  float edge = smoothstep(-0.01 - 0.004 * u_focus, 0.02, below);
  vec3 col = mix(sky, field, edge);
  return outLab(col);
}
