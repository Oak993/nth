// Squall: a rain squall passing far out over the sea at sunset. Under a long, dark shelf of cloud the rain hangs in grey
// veils that trail and lean with the wind; the low sun behind them turns their edges gold and lays a bright band along
// the horizon beneath, and the calm sea in front mirrors it all. The veils drift slowly along the horizon; their field
// repeats along it, so they travel exactly one period a loop.
// Palette: 0 the high sky, 1 the cloud's underside, 2 the rain, 3 the glow at the horizon, 4 the sea, 5 the sun.
// Options: x = motion (how much the veils sway), y = rain (light to heavy).
const float T = 75.0;

float veils(vec2 p, float a, float m) {
  float D = 6.0;
  float R = D / TAU;
  float x = p.x + 0.35 * (p.y + 0.2);
  float ph = TAU * x / D + a;
  vec3 q = vec3(R * cos(ph), R * sin(ph), p.y * 0.35);
  float v = gnoise(q * vec3(1.3, 1.3, 0.8) + vec3(0.0, 0.0, 0.2 * m * sin(a))) * 0.7 + gnoise(q * vec3(3.2, 3.2, 1.0) + 3.0) * 0.3;
  float streak = gnoise(vec3(R * cos(ph) * 30.0, R * sin(ph) * 30.0, p.y * 1.5)) * 0.5 + 0.5;
  return smoothstep(0.02, 0.35, v) * (0.7 + 0.3 * streak);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float rn = mix(0.5, 1.3, u_opt.y);
  float yh = -0.3;
  vec2 sun = vec2(0.55, yh + 0.12) + u_sun + tiltD(-1.0);
  vec2 ps = p + tiltD(-0.5);
  // the sky: dark cloud shelf above, a clear band of glow beneath it
  float sb = 0.14 + 0.07 * gnoise(vec3(ps.x * 1.1, 0.0, 1.0)) + 0.03 * gnoise(vec3(ps.x * 4.0, 0.0, 2.0));
  float shelf = smoothstep(sb - 0.03, sb + 0.12, ps.y);
  vec3 sky = mix(u_pal[3], u_pal[0], smoothstep(yh, 0.4, ps.y));
  // the shelf: heavy cloud, darker as it rises, its body rolled and lumpy, drifting with the rain
  float D2 = 9.0;
  float ph2 = TAU * ps.x / D2 + a;
  vec3 cq = vec3(D2 / TAU * cos(ph2), D2 / TAU * sin(ph2), ps.y * 1.8) * 1.4;
  float body = fbm4(cq + vec3(0.0, 0.0, 2.0));
  vec3 cloud = setL(u_pal[1], u_pal[1].x * (1.15 - 0.35 * smoothstep(sb, 1.1, ps.y) + 0.35 * body));
  cloud = mix(cloud, u_pal[0], 0.25 * smoothstep(0.5, 1.2, ps.y));
  sky = mix(sky, cloud, shelf);
  float sd = length((p - sun) * vec2(0.7, 1.0));
  sky = mix(sky, u_pal[5], (smoothstep(0.055, 0.045, length(p - sun)) * 0.9 + exp(-sd * sd / 0.02) * 0.5 + exp(-sd / 0.5) * 0.25) * (1.0 - shelf * 0.8));
  // the cloud's underside: soft mammatus-like swellings lit from below by the sun
  float cu = gnoise(vec3(ps * vec2(2.2, 4.0), 3.0));
  sky = mix(sky, u_pal[3], shelf * smoothstep(0.55, 0.15, ps.y) * sat(cu * 0.8 + 0.3) * 0.35 * exp(-abs(ps.x - sun.x) * 0.8));
  // the rain veils, hanging from the shelf to the sea
  float vh = smoothstep(yh - 0.01, yh + 0.08, ps.y) * smoothstep(sb + 0.1, sb - 0.05, ps.y);
  float v = veils(ps, a, m) * vh * rn;
  vec3 rc = mix(u_pal[2], u_pal[3], exp(-abs(ps.x - sun.x) * 1.4) * 0.6);
  sky = mix(sky, rc, sat(v * 0.8));
  // the shelf's lower edge catches the sun
  sky = mix(sky, u_pal[3], exp(-pow((ps.y - sb) / 0.04, 2.0)) * 0.45 * exp(-abs(ps.x - sun.x) * 0.9));
  // the sea mirrors the sky
  float dz = max(yh - p.y, 0.0);
  vec2 rp = vec2(p.x + 0.01 * lnoise(vec2(p.x * 3.0, dz * 40.0), a, 0.4 * m), 2.0 * yh - p.y);
  vec2 rs = rp + tiltD(-0.5);
  float rsb = 0.14 + 0.07 * gnoise(vec3(rs.x * 1.1, 0.0, 1.0)) + 0.03 * gnoise(vec3(rs.x * 4.0, 0.0, 2.0));
  float rshelf = smoothstep(rsb - 0.03, rsb + 0.12, rs.y);
  vec3 refl = mix(u_pal[3], u_pal[0], smoothstep(yh, 0.4, rs.y));
  refl = mix(refl, u_pal[1], rshelf);
  float rsd = length((rp - sun) * vec2(0.7, 1.0));
  refl = mix(refl, u_pal[5], exp(-rsd * rsd / 0.03) * 0.7);
  float rv = veils(rs, a, m) * smoothstep(yh - 0.01, yh + 0.08, rs.y) * smoothstep(rsb + 0.1, rsb - 0.05, rs.y) * rn;
  refl = mix(refl, rc, sat(rv * 0.6));
  vec3 sea = mix(refl, u_pal[4], 0.15 + 0.55 * smoothstep(0.0, 0.7, dz));
  vec3 col = mix(sea, sky, smoothstep(-edgePx(), edgePx(), p.y - yh));
  return outLab(col);
}
