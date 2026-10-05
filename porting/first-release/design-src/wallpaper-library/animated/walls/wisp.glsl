// Wisp: a single thread of smoke rising from something burning just below the frame, in still air. Near its source it
// is a fine, steady line; higher up slow waves travel along it, it widens, curls and frays, and finally thins away to
// nothing before the top. A side light catches one edge of it. It keeps to the left third; the rest is calm ground.
// Palette: 0 the ground low, 1 the ground high, 2 the smoke in shadow, 3 the smoke lit, 4 the side light, 5 the warm
// glow near the source.
// Options: x = motion (how much the smoke sways and curls), y = smoke (a thread to a plume).
const float T = 60.0;

// one strand: its density at p (x) and which side of it p lies on (y), rising from x0 with waves that travel up it
vec2 strand(vec2 p, float a, float m, float x0, float seed, float thick) {
  float h = p.y + 1.15;
  float hh = max(h, 0.0);
  float amp = (0.01 + 0.075 * pow(hh, 1.6)) * m;
  float xc = x0 + 0.05 * hh * sin(seed)
           + amp * sin(2.6 * hh - 3.0 * a + seed)
           + amp * 0.6 * sin(4.9 * hh - 5.0 * a + seed * 2.3)
           + 0.05 * pow(hh, 1.4) * lnoise(vec2(hh * 1.3, seed), a, 0.3 * m);
  // higher up the smoke frays: the coordinate is warped more and more
  vec2 wq = vec2(p.x, h);
  float fray = smoothstep(0.5, 1.9, hh);
  float wx = lnoise(wq * 3.2 + seed, a, 0.35) * 0.06 * fray * m + lnoise(wq * 7.0 + seed, a, 0.3) * 0.02 * fray;
  float wd = (0.006 + 0.05 * pow(hh, 1.35)) * thick;
  float dx = (p.x + wx - xc) / wd;
  float fade = smoothstep(0.0, 0.25, hh) * smoothstep(2.35, 1.0, hh);
  return vec2(exp(-dx * dx) * fade / (0.55 + 1.2 * hh * thick), clamp(dx, -1.0, 1.0));
}

// gradient noise whose lattice repeats every P cells along y (P a whole number): a texture can then slide up through
// it without end and come back to itself exactly, so the smoke truly rises, with no cross-fade and no wrap to hide
float pnoise(vec3 p, float P) {
  vec3 i = floor(p);
  vec3 f = p - i;
  vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
  float y0 = mod(i.y, P);
  float y1 = mod(i.y + 1.0, P);
  float n000 = dot(hash33(vec3(i.x, y0, i.z)), f);
  float n100 = dot(hash33(vec3(i.x + 1.0, y0, i.z)), f - vec3(1.0, 0.0, 0.0));
  float n010 = dot(hash33(vec3(i.x, y1, i.z)), f - vec3(0.0, 1.0, 0.0));
  float n110 = dot(hash33(vec3(i.x + 1.0, y1, i.z)), f - vec3(1.0, 1.0, 0.0));
  float n001 = dot(hash33(vec3(i.x, y0, i.z + 1.0)), f - vec3(0.0, 0.0, 1.0));
  float n101 = dot(hash33(vec3(i.x + 1.0, y0, i.z + 1.0)), f - vec3(1.0, 0.0, 1.0));
  float n011 = dot(hash33(vec3(i.x, y1, i.z + 1.0)), f - vec3(0.0, 1.0, 1.0));
  float n111 = dot(hash33(vec3(i.x + 1.0, y1, i.z + 1.0)), f - vec3(1.0, 1.0, 1.0));
  return 1.6 * mix(mix(mix(n000, n100, u.x), mix(n010, n110, u.x), u.y),
                   mix(mix(n001, n101, u.x), mix(n011, n111, u.x), u.y), u.z);
}

// the smoke higher up, where the thread has broken into a soft, curling plume. Its texture rises steadily: the noise
// coordinate slides down by one whole lattice period (4 cells, and 8 for the finer octave) twice a loop
float plume(vec2 p, float a, float m, float xc, float seed) {
  float h = p.y + 1.15;
  float W = 0.07 + 0.32 * max(h - 0.7, 0.0);
  float dx = (p.x - xc) / W;
  float env = exp(-dx * dx) * smoothstep(0.55, 1.1, h) * smoothstep(2.4, 1.3, h);
  float rise = 4.0 * fract(2.0 * a / TAU);
  vec2 wq = vec2(p.x * 3.0 + 0.6 * sin(h * 2.0 + p.x * 2.0), h * 1.6 - rise);
  // a slow drift of the third coordinate lets the sheets change shape as they climb; it is a closed loop too
  float z = seed + 0.35 * m * sin(a) ;
  float n = pnoise(vec3(wq, z + 0.25 * m * cos(a)), 4.0) + 0.5 * pnoise(vec3(wq * 2.0, z * 1.7 + 2.0), 8.0);
  // the smoke lies in thin curling sheets: the ridges of the noise, softened higher up
  float rid = 1.0 - abs(n);
  float sheets = pow(sat(rid), mix(5.0, 2.0, smoothstep(1.0, 2.2, h)));
  return env * (0.25 + 0.95 * sheets) / (0.5 + 0.6 * max(h - 0.7, 0.0)) * (0.7 + 0.3 * m / 1.35);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.35, u_opt.x);
  float th = mix(0.7, 1.6, u_opt.y);
  vec3 col = mix(u_pal[0], u_pal[1], smoothstep(-1.1, 1.0, p.y + 0.2 * p.x));
  vec2 sl = p - vec2(-1.6, 0.9) - u_sun;
  col = mix(col, u_pal[4], 0.3 * exp(-dot(sl, sl) / 2.2));
  col.x -= 0.02 * smoothstep(0.8, 2.2, length(p * vec2(0.6, 1.0) - vec2(0.3, -0.2)));
  vec2 sr = p - vec2(1.5, -0.6);
  col = mix(col, u_pal[5], 0.18 * exp(-dot(sr, sr) / 1.4));
  vec2 src = p - vec2(-0.72, -1.25);
  col = mix(col, u_pal[5], 0.16 * exp(-dot(src, src) / 0.12));
  vec2 r1 = strand(p, a, m, -0.72, 0.0, th);
  vec2 r2 = strand(p, a + 1.3, m * 1.1, -0.71, 2.4, th * 0.75);
  float d2 = r2.x * smoothstep(-0.3, 0.6, p.y) * 0.7;
  float hh = max(p.y + 1.15, 0.0);
  float xc = -0.72 + 0.08 * hh + 0.08 * m * hh * sin(1.6 * hh - a + 0.5);
  float d3 = plume(p, a, m, xc, 3.0) * th * 0.9;
  float d = r1.x + d2 + d3;
  float lit = 0.5 - 0.5 * mix(r1.y, r2.y, d2 / (r1.x + d2 + 1e-4));
  lit = mix(lit, sat(0.5 - 1.2 * (p.x - xc)), d3 / (d + 1e-4));
  vec3 sc = mix(u_pal[2], u_pal[3], lit);
  col = mix(col, sc, sat(d * 0.75));
  col = mix(col, u_pal[4], sat(d * lit * 0.2));
  return outLab(col);
}
