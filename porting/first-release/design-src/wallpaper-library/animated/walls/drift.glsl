// Drift: hills standing in morning fog, one ridge behind another. Each ridge is a soft silhouette that pales toward its
// foot where the fog pools; the far ridges are nearly sky, the near one carries the most colour, and the fog rises and
// settles so the ridges seem to come and go.
// Palette: 0 sky, 1 sky low, 2 far ridge, 3 middle ridge, 4 near ridge, 5 the light behind.
// Options: x = motion (how much the fog rises and settles), y = density (how thick the fog lies).
const float T = 84.0;

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.3, 1.4, u_opt.x);
  float den = mix(0.45, 1.35, u_opt.y);
  vec3 sky = mix(u_pal[1], u_pal[0], smoothstep(-0.6, 1.0, p.y));
  vec2 ls = p - vec2(0.75, 0.42);
  sky = mix(sky, u_pal[5], 0.4 * exp(-dot(ls, ls) / 0.5));
  vec3 col = sky;
  for (int i = 0; i < 4; i++) {
    float f = float(i);
    float fk = f / 3.0;
    vec2 pp = p + tiltD(fk * 2.0 - 1.0);
    float sc = 0.7 + 0.35 * f;
    float yk = 0.32 - 0.36 * f;
    float edge = yk + (0.12 + 0.03 * f) * (0.6 * sin(pp.x * sc + f * 2.1) + 0.4 * sin(pp.x * sc * 2.3 + f * 0.7))
               + 0.035 * m * lnoise(vec2(pp.x * 0.9 + f * 3.0, f), a, 0.4);
    float soft = 0.012 + 0.03 * (1.0 - fk);
    float body = smoothstep(soft, -soft, pp.y - edge);
    vec3 rc = mix(u_pal[2], u_pal[3], sat(fk * 1.5));
    rc = mix(rc, u_pal[4], sat(fk * 1.5 - 0.5));
    rc = mix(sky, rc, 0.45 + 0.55 * fk);
    float fog = smoothstep(0.0, 0.42, edge - pp.y) * (0.55 + 0.25 * sin(a + f * 1.9)) * den;
    fog *= 0.75 + 0.25 * lnoise(vec2(pp.x * 1.3 + f * 5.0, pp.y * 2.0), a, 0.3 * m);
    rc = mix(rc, mix(sky, u_pal[5], 0.25), sat(fog * (0.75 - 0.25 * fk)));
    col = mix(col, rc, body);
  }
  return outLab(col);
}
