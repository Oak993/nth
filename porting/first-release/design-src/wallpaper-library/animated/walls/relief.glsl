// Relief: a plaster relief of a few plain forms (discs raised and cut, an arch) on a wall, each
// panel washed in its own faint colour. A low light travels slowly round it, so the bevelled edges catch and lose the
// light and the shadows lengthen, swing and shorten, as across a gallery wall through an afternoon.
// Palette: 0 wall, 1 the large forms, 2 the raised disc, 3 the cuts, 4 shadow and the cool fill, 5 the warm light.
// Options: x = motion (how far the light travels), y = depth (shallow to deep relief).
const float T = 72.0;

float box(vec2 p, vec2 c, vec2 b, float r) {
  vec2 d = abs(p - c) - b + r;
  return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
}
float edge(float sd, float w) { return smoothstep(w, -w, sd); }

// the relief's height, and in w which surface the point is on (0 wall, 1 the large forms, 2 the raised disc, 3 a cut)
vec2 relief(vec2 p, float dep) {
  float w = 0.022;
  float sA = length(p - vec2(1.18, 0.3)) - 0.9;
  float sB = length(p - vec2(0.62, -0.35)) - 0.42;
  vec2 ac = vec2(-1.02, -1.0);
  float sD = min(box(p, ac, vec2(0.34, 0.55), 0.0), length(p - ac - vec2(0.0, 0.55)) - 0.34);
  float sE = box(p, vec2(0.0, -1.02), vec2(3.0, 0.13), 0.0);
  float sF = length(p - vec2(-0.78, 0.6)) - 0.15;
  float eA = edge(sA, w);
  float eB = edge(sB, w);
  float eD = edge(sD, w);
  float eE = edge(sE, w);
  float eF = edge(sF, w);
  float h = 0.05 * eA + 0.045 * eB + 0.06 * eD + 0.025 * eE - 0.035 * eF;
  float id = max(eA, eD) + eB - eF;
  return vec2(h * dep, id);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.3, u_opt.x);
  float dep = mix(0.55, 1.5, u_opt.y);
  float az = 2.25 + 0.75 * m * sin(a) + 0.12 * m * sin(2.0 * a + 0.5) - 0.9 * u_sun.x;
  float el = max(0.14, 0.3 + 0.07 * cos(a) + 0.5 * u_sun.y);
  vec2 ld = vec2(cos(az), sin(az));
  vec3 L = vec3(ld * cos(el), sin(el));
  vec2 r0 = relief(p, dep);
  float e = 0.004;
  float hx = relief(p + vec2(e, 0.0), dep).x - r0.x;
  float hy = relief(p + vec2(0.0, e), dep).x - r0.x;
  vec3 n = normalize(vec3(-hx / e, -hy / e, 1.0));
  float dif = dot(n, L);
  // the shadow: march toward the light and see whether the relief rises above the ray
  float sh = 1.0;
  float tl = tan(el);
  for (int i = 0; i < 12; i++) {
    float s = 0.01 + 0.026 * float(i);
    float hh = relief(p + ld * s, dep).x;
    sh = min(sh, sat(1.0 - (hh - r0.x - s * tl) / (0.006 + 0.16 * s)));
  }
  // the surfaces' colours: the wall and each panel faintly tinted
  float id = r0.y;
  vec3 alb = mix(u_pal[0], u_pal[1], sat(id));
  alb = mix(alb, u_pal[2], sat(id - 1.0));
  alb = mix(alb, u_pal[3], sat(-id));
  // a broad wash of the light from its side, so the wall is never flat
  float wash = sat(0.5 + 0.38 * dot(p, ld));
  float lit = sat(dif / sin(el)) * sh;
  vec3 col = mix(u_pal[4], alb, 0.45 + 0.55 * lit);
  col = mix(col, u_pal[5], sat(dif - sin(el)) * 0.75 * sh);
  col.x *= 0.84 + 0.24 * wash;
  col = mix(col, u_pal[5], 0.22 * wash * wash * lit);
  col = mix(col, u_pal[4], 0.18 * (1.0 - wash));
  // plaster: a faint fixed trowel texture
  float tex = gnoise(vec3(p * vec2(9.0, 5.0), 1.3)) * 0.6 + gnoise(vec3(p * 60.0, 2.0)) * 0.3 + gnoise(vec3(p * 220.0, 4.0)) * 0.25;
  col.x += 0.009 * tex;
  return outLab(col);
}
