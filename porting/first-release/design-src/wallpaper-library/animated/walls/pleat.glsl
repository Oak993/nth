// Pleat: a sheet of heavy paper folded in a Miura pattern, the tessellation of zigzag pleats that folds flat in one
// movement. Every facet is a small flat parallelogram, so each turns to the lamp at its own angle: rows of light and
// shade that zig across the sheet. The sheet slowly gathers and relaxes, as if someone held its ends, so the facets
// tilt and the light runs over them; the creases catch a hairline of light and the paper keeps its fine tooth.
// Palette: 0 paper, 1 paper's second tint, 2 lamp light, 3 shade, 4 bounce light from the far side, 5 the fold light.
// Options: x = motion (how far the sheet gathers), y = pleats (wide to fine).
const float T = 64.0;

// a triangle wave with softly rounded corners, and its slope
vec2 tri(float x, float r) {
  float f = fract(x) - 0.5;
  float s = sqrt(f * f + r * r);
  return vec2(s * 2.0, 2.0 * f / s);
}

vec3 wall(vec2 p, float t) {
  float a = TAU * t / T;
  float m = mix(0.35, 1.4, u_opt.x);
  float sc = mix(1.7, 3.2, u_opt.y);
  vec2 pp = p + tiltD(0.2);
  // the sheet bows away from us toward the top, so its pleats grow finer there
  float persp = 1.0 + 0.32 * (pp.y + 1.0);
  vec2 q = rot(vec2(pp.x * persp, pp.y + 0.12 * pp.y * pp.y), -0.18) * sc;
  // the gathering: the zigzag deepens and the facets steepen, then relax
  float g = 0.5 + 0.5 * sin(a);
  float k = 0.45 + 0.25 * m * g;
  float depth = (0.28 + 0.18 * m * g) / sc;
  float r = max(0.035, edgePx() * sc * 2.0);
  vec2 tx = tri(q.x * 0.5, r);
  float arg = q.y + k * tx.x;
  vec2 ty = tri(arg, r);
  // the height is depth * ty.x; its slopes, chained through the zigzag
  float dhy = depth * ty.y * sc;
  float dhx = depth * ty.y * k * tx.y * 0.5 * sc;
  vec2 gr = rot(vec2(dhx, dhy), 0.18);
  vec3 n = normalize(vec3(-gr * 1.25, 1.0));
  float la = 2.2 + 0.25 * m * sin(a + 0.8) - 0.6 * u_sun.x;
  vec3 L = normalize(vec3(cos(la), sin(la), 0.85));
  float dif = dot(n, L);
  vec3 L2 = normalize(vec3(-cos(la), -sin(la), 0.5));
  float bounce = sat(dot(n, L2));
  // the creases: ridges where the zigzag turns over, valleys where it turns under
  float fy = abs(fract(arg) - 0.5);
  float ridge = exp(-fy * fy / (0.0015 + r * r * 0.5));
  float valley = exp(-pow(0.5 - fy, 2.0) / (0.002 + r * r * 0.5));
  // the lamp: a soft pool from the upper left that drifts slowly
  vec2 lc = vec2(-0.5, 0.45) + u_sun + orbit(a, 0.18 * m);
  float pool = exp(-dot(p - lc, p - lc) / 2.8);
  vec3 paper = mix(u_pal[0], u_pal[1], smoothstep(-1.6, 1.6, p.x - 0.4 * p.y));
  vec3 col = mix(u_pal[3], paper, smoothstep(-0.35, 1.0, dif));
  col = mix(col, u_pal[2], smoothstep(0.75, 1.0, dif) * 0.55 * pool);
  col = mix(col, u_pal[4], bounce * 0.25 * (1.0 - smoothstep(0.2, 0.7, dif)));
  col.x *= 0.82 + 0.26 * pool;
  col.x -= 0.05 * valley * (0.4 + g);
  col = mix(col, u_pal[5], ridge * sat(dif) * 0.35 * (0.35 + 0.65 * pool));
  float tooth = gnoise(vec3(p * vec2(150.0, 80.0), 0.5)) + 0.5 * gnoise(vec3(p * 400.0, 2.0));
  col.x += 0.006 * tooth;
  return outLab(col);
}
