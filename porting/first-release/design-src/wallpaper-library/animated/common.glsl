// Marrow animated wallpapers: the shared math every wallpaper may call.
// A GLSL ES 3.00 subset that ports to Skia SkSL as written: no textures, no derivatives, no preprocessor, no arrays
// built in the shader, constant loop bounds, no bit operations and no integer modulo. Every function is pure math.
//
// The host declares these uniforms and keeps them eased (so nothing ever cuts):
//   vec3  u_pal[6]   the colour pack: six OKLab colours, already mixed for the theme and crossfaded between packs
//   float u_dark     0 = light theme, 1 = dark theme
//   vec2  u_opt      the wallpaper's two options, each 0..1 (x = motion amount, y = the wallpaper's second option)
//   float u_px       the size of one device pixel in p units, for anti-aliased edges
//   vec2  u_tilt     perspective: the pointer's (or the window's) offset, each -1..1, eased; (0, 0) when Perspective is off
//                    or under Reduce Motion. A wallpaper with depth shifts each layer by tiltD(depth); a flat wallpaper
//                    ignores it (the host pans its whole picture a few pixels instead).
//   vec2  u_sun      dynamic (time of day): the offset to add to the wallpaper's own light position or light direction,
//                    in p units, eased; (0, 0) when Dynamic is off or the wallpaper declares no light
//   float u_focus    Focus, 0 sharp .. 1 dreamy, eased. The host blurs the picture; a wallpaper may also widen its own
//                    hard edges with edgePx().
// wall(p, t): p is the point with the window's height spanning -1..1 (x grows with the aspect), t is the loop clock in
// seconds, 0 <= t < T. All motion is built from a = TAU * t / T, so frame(t) == frame(t + T) exactly.

const float TAU = 6.28318530718;

float sat(float x) { return clamp(x, 0.0, 1.0); }
vec2 rot(vec2 p, float a) { float c = cos(a); float s = sin(a); return vec2(c * p.x - s * p.y, s * p.x + c * p.y); }
vec2 orbit(float a, float r) { return r * vec2(cos(a), sin(a)); }
float gauss(float x, float w) { return exp(-(x * x) / (w * w)); }
// parallax: add tiltD(d) to p before evaluating a layer; d runs from -1 (the farthest layer) to +1 (the nearest). At full
// tilt the nearest layer moves 0.010 p units (4 px at 800 px high), the farthest the other way, so a few pixels at most.
vec2 tiltD(float d) { return u_tilt * (0.010 * d); }
// the width of an anti-aliased edge: one pixel when sharp, wider as Focus rises (use in place of u_px on hard edges)
float edgePx() { return u_px * (1.0 + 14.0 * u_focus); }

// ---- colour: OKLab (Bjorn Ottosson's matrices) and sRGB
vec3 lab2lin(vec3 c) {
  float l = c.x + 0.3963377774 * c.y + 0.2158037573 * c.z;
  float m = c.x - 0.1055613458 * c.y - 0.0638541728 * c.z;
  float s = c.x - 0.0894841775 * c.y - 1.2914855480 * c.z;
  l = l * l * l; m = m * m * m; s = s * s * s;
  return vec3(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
             -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
             -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
}
vec3 lin2lab(vec3 c) {
  c = max(c, vec3(0.0));
  float l = pow(0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b + 1e-9, 0.3333333);
  float m = pow(0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b + 1e-9, 0.3333333);
  float s = pow(0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b + 1e-9, 0.3333333);
  return vec3(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
              1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
              0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
}
vec3 lin2srgb(vec3 c) {
  c = clamp(c, 0.0, 1.0);
  return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
}
vec3 outLab(vec3 lab) { return lin2srgb(lab2lin(lab)); }
vec3 lch(float L, float C, float h) { return vec3(L, C * cos(h), C * sin(h)); }
vec3 hueShift(vec3 lab, float a) { return vec3(lab.x, rot(lab.yz, a)); }
vec3 setL(vec3 lab, float L) { return vec3(L, lab.yz); }
// a soft shoulder for light added in linear space: below the knee nothing changes, above it the value rolls toward 1
// and the very brightest light whitens a little, the way film and the eye do
vec3 shoulder(vec3 c) {
  float m = max(c.r, max(c.g, c.b));
  float k = 0.72;
  if (m <= k) return c;
  float n = k + (1.0 - k) * (1.0 - exp(-(m - k) / (1.0 - k)));
  vec3 r = c * (n / m);
  return mix(r, vec3(n), sat((m - 1.0) * 0.18));
}

// ---- noise: gradient noise on a fract-based hash (no sine, so it matches on every GPU)
vec3 hash33(vec3 p) {
  p = fract(p * vec3(0.1031, 0.1030, 0.0973));
  p += dot(p, p.yxz + 33.33);
  return fract((p.xxy + p.yxx) * p.zyx) * 2.0 - 1.0;
}
float gnoise(vec3 p) {
  vec3 i = floor(p);
  vec3 f = p - i;
  vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
  float n000 = dot(hash33(i), f);
  float n100 = dot(hash33(i + vec3(1.0, 0.0, 0.0)), f - vec3(1.0, 0.0, 0.0));
  float n010 = dot(hash33(i + vec3(0.0, 1.0, 0.0)), f - vec3(0.0, 1.0, 0.0));
  float n110 = dot(hash33(i + vec3(1.0, 1.0, 0.0)), f - vec3(1.0, 1.0, 0.0));
  float n001 = dot(hash33(i + vec3(0.0, 0.0, 1.0)), f - vec3(0.0, 0.0, 1.0));
  float n101 = dot(hash33(i + vec3(1.0, 0.0, 1.0)), f - vec3(1.0, 0.0, 1.0));
  float n011 = dot(hash33(i + vec3(0.0, 1.0, 1.0)), f - vec3(0.0, 1.0, 1.0));
  float n111 = dot(hash33(i + vec3(1.0, 1.0, 1.0)), f - vec3(1.0, 1.0, 1.0));
  return 1.6 * mix(mix(mix(n000, n100, u.x), mix(n010, n110, u.x), u.y),
                   mix(mix(n001, n101, u.x), mix(n011, n111, u.x), u.y), u.z);
}
// a point that travels once round a closed loop in noise space as a goes 0..TAU: the loop is how noise animates and
// still repeats exactly
vec3 loop3(vec2 p, float a, float r) { return vec3(p.x + r * cos(a), p.y + r * sin(a), r * sin(a + 2.1)); }
float lnoise(vec2 p, float a, float r) { return gnoise(loop3(p, a, r)); }
float fbm3(vec3 p) {
  float s = 0.0; float w = 0.5;
  for (int i = 0; i < 3; i++) { s += w * gnoise(p); p = p * 2.03 + vec3(1.7, 9.2, 3.1); w *= 0.5; }
  return s;
}
float fbm4(vec3 p) {
  float s = 0.0; float w = 0.5;
  for (int i = 0; i < 4; i++) { s += w * gnoise(p); p = p * 2.03 + vec3(1.7, 9.2, 3.1); w *= 0.5; }
  return s;
}
