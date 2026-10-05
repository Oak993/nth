// Veil: an aurora seen through high haze. The curtains are there, their foot along a slow wave, their colour turning
// from the foot to the top, but the haze in front softens them to washes of light; where the haze thickens it takes
// their colour and glows, where it thins the curtains show clearer. A few faint stars wait behind it all.
// Palette: 0 the sky high, 1 the sky low, 2 the aurora's foot, 3 the aurora's crown, 4 the haze, 5 the stars.
// Options: x = motion (how much the curtains sway and the haze drifts), y = haze (thin to thick).
const float T = 90.0;

// one curtain: its light at q, a foot that waves, a soft fall above it, and a slow grain of rays
vec2 curtain(vec2 q, float a, float m, float base, float seed) {
  float foot = base + 0.16 * sin(q.x * 0.85 + seed + 0.5 * m * sin(a + seed)) + 0.11 * lnoise(vec2(q.x * 0.9, seed), a, 0.35 * m) + 0.04 * lnoise(vec2(q.x * 2.4, seed + 7.0), a, 0.3 * m);
  float h = q.y - foot;
  float rays = 0.65 + 0.35 * lnoise(vec2(q.x * 3.2 + 0.4 * q.y, seed + 3.0), a, 0.3 * m);
  float lum = smoothstep(-0.13, 0.05, h) * exp(-max(h, 0.0) / 0.55) * rays;
  return vec2(lum, sat(h / 0.6));
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float hz = mix(0.35, 1.2, u_opt.y);
  vec2 qf = p + tiltD(-1.0);
  vec3 col = mix(u_pal[1], u_pal[0], smoothstep(-1.0, 1.0, p.y));
  // the stars, faint and behind everything
  vec2 sc = qf * 46.0;
  vec2 cid = floor(sc);
  vec3 sh = hash33(vec3(cid, 5.0));
  float star = step(0.82, sh.z) * smoothstep(0.12, 0.0, length(sc - cid - 0.5 - 0.35 * sh.xy)) * (0.5 + 0.5 * sh.x);
  // two curtains: a far one high and faint, a nearer one lower and brighter
  vec2 c1 = curtain(qf, a, m, 0.18, 1.0);
  vec2 c2 = curtain(p, a + 2.1, m, -0.22, 4.0);
  float lum = c1.x * 0.55 + c2.x;
  float up = mix(c1.y, c2.y, c2.x / (c1.x * 0.55 + c2.x + 1e-4));
  vec3 ac = mix(u_pal[2], u_pal[3], smoothstep(0.05, 0.75, up));
  // the haze in front, drifting: it hides the stars, softens the curtains and glows with their light
  vec2 qn = p + tiltD(1.0);
  float hn = lnoise(qn * vec2(0.5, 2.0) + 2.0, a, 0.3 * m) * 0.65 + gnoise(vec3(qn * vec2(1.4, 4.0), 1.0)) * 0.35;
  float haze = smoothstep(-0.12, 0.5, hn) * hz;
  col = mix(col, u_pal[5], star * u_dark * (1.0 - sat(haze * 1.4)) * 0.7);
  // seen through the haze the curtains soften; the haze itself scatters their light and glows with it
  float seen = lum * (1.0 - 0.5 * sat(haze));
  col = mix(col, ac, sat(seen * 0.75));
  float glowLum = c1.x * 0.55 + c2.x * 0.8 + 0.25 * curtain(qn * 0.8, a, m, -0.1, 4.0).x;
  vec3 hc = mix(u_pal[4], ac, sat(glowLum * 1.1));
  col = mix(col, hc, sat(haze * 0.7) * (0.5 + 0.5 * sat(glowLum + 0.15)));
  return outLab(col);
}
