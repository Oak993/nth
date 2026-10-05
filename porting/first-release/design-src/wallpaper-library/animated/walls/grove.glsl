// Grove: tall trunks standing in morning fog, rank behind rank. The far ranks are almost fog, the near ones carry the
// most colour, and soft shafts of light lean down through the gaps from a sun behind the trees. The fog drifts between
// the ranks and thickens near the ground, so the trunks seem to come and go. The middle of the window stays open fog:
// the nearest trunks keep to the sides. Each rank is its own Perspective layer.
// Palette: 0 fog high, 1 fog low (in dark packs a warm mist under a moonlit blue), 2 far trunks, 3 near trunks, 4 the light,
// 5 the light's core.
// Options: x = motion (how much the fog drifts and the shafts waver), y = density (thin to thick fog).
const float T = 84.0;

float h1(float x) { return hash33(vec3(x, 3.1, 7.7)).x * 0.5 + 0.5; }

// one rank of trunks: x = the cover 0..1 at q, y = where across the trunk q lies (-1 shade side .. 1 sun side), for trunks
// dens per unit of width, wa..wb wide, their edges soft
vec2 rank(vec2 q, float dens, float wa, float wb, float soft, float seed, float open, float there0) {
  float x = q.x * dens + seed;
  float c0 = floor(x);
  float cov = 0.0;
  float side = 0.0;
  for (int i = 0; i < 3; i++) {
    float c = c0 + float(i) - 1.0;
    float there = step(there0, h1(c * 1.31 + seed));
    float cx = c + 0.18 + 0.64 * h1(c * 2.17 + seed * 1.9);
    float wd = mix(wa, wb, h1(c * 3.71 + seed * 0.7)) * (1.0 - 0.06 * q.y);
    float lean = (h1(c * 5.3 + seed) - 0.5) * 0.05 * q.y + 0.01 * sin(q.y * 1.7 + c);
    float dx = (x - cx) / dens - lean;
    float keep = mix(1.0, smoothstep(0.42, 0.95, abs(cx / dens - seed / dens)), open);
    float cv = there * keep * smoothstep(wd + soft, wd - soft, abs(dx));
    if (cv > cov) { cov = cv; side = clamp(dx / wd, -1.0, 1.0); }
  }
  return vec2(cov, side);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float den = mix(0.5, 1.35, u_opt.y);
  // the fog itself, lit from a sun high behind the trees
  vec2 sun = vec2(0.55, 1.35) + u_sun;
  vec2 ds = p - sun;
  float glow = exp(-dot(ds, ds) / 1.6);
  vec3 fog = mix(u_pal[1], u_pal[0], smoothstep(-0.9, 0.9, p.y + 0.15 * lnoise(p * 0.5 + 4.0, a, 0.3 * m)));
  fog = mix(fog, u_pal[4], 0.45 * glow);
  // shafts of light leaning down from the sun, wavering slowly
  float ang = atan(ds.x, -ds.y);
  float sh = lnoise(vec2(ang * 4.0, 1.0), a, 0.3 * m) * 0.7 + lnoise(vec2(ang * 8.5, 4.0), a, 0.25 * m) * 0.3;
  float shaft = smoothstep(0.0, 0.7, sh) * smoothstep(3.6, 0.6, length(ds)) * (0.6 + 0.4 * glow);
  // the fog pools brighter near the ground
  fog = mix(fog, mix(u_pal[4], u_pal[1], 0.5), 0.16 * smoothstep(-0.2, -1.1, p.y));
  vec3 col = fog;
  for (int i = 0; i < 4; i++) {
    float f = float(i);
    float fk = f / 3.0;
    vec2 q = p + tiltD(mix(-1.0, 1.0, fk));
    float dens = mix(1.9, 0.7, fk);
    vec2 rk = rank(q, dens, mix(0.014, 0.06, fk), mix(0.024, 0.1, fk), mix(0.01, 0.018, fk) + 0.004 * u_focus, 17.0 + f * 9.3, fk * fk, mix(0.5, 0.32, fk));
    // the fog in front of this rank: thicker for the far ranks and near the ground, lying in banks that drift
    float bank = lnoise(vec2(q.x * 0.4 + f * 2.7, q.y * 1.6 - f), a, 0.55 * m);
    float fogA = sat((mix(0.78, 0.14, fk) + 0.3 * smoothstep(0.2, -1.0, q.y) + 0.5 * bank) * den);
    vec3 tc = mix(u_pal[2], u_pal[3], fk);
    tc.x += 0.025 * rk.y * (1.0 - fogA);
    tc = mix(tc, mix(fog, u_pal[4], 0.3 * shaft), fogA);
    col = mix(col, tc, rk.x);
    // the light in the fog between this rank and the next
    col = mix(col, u_pal[4], sat(shaft * mix(0.1, 0.16, u_dark) * den * (1.0 - fk * 0.6)));
  }
  col = mix(col, u_pal[5], 0.18 * glow * glow);
  return outLab(col);
}
