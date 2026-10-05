// Static wallpaper library: the shader contract every wallpaper is written against.
//
// A wallpaper is one function  vec3 wall(vec2 p)  that returns the colour of a point in OKLab. p is the point in
// height units: the window's height runs from -1 (bottom) to +1 (top) and x runs from -aspect to +aspect, so the picture
// is the same composition at any size and only gains detail with more pixels. It is maths only: no textures, no
// derivatives, every loop has a constant bound, arrays are indexed by constants or loop indices only, no bit operations
// and no preprocessor in the wallpaper source, so it ports to Skia SkSL as written (the uniforms become SkSL uniforms,
// main() becomes  half4 main(float2 coord)  with coord flipped to y-up).
//
// The uniforms: u_res (the target size in device pixels), u_dpr (device pixels per CSS pixel; micro texture such as
// paper fibre and weave is sized in CSS pixels so it keeps its physical size on any DPI), u_pal[6] (the colour pack, in
// OKLab, already mixed on the CPU for the theme and for a pack crossfade), u_dark (0 light .. 1 dark, for the few
// things that are not colour, such as stars), u_grain (0..1) and u_opt (0..1, the wallpaper's own option).
// main() adds the grain (luminance only, in OKLab L), converts to sRGB and dithers by one 8-bit step (triangular), so
// no ramp bands.
//
// The premium options add six uniforms (OPTIONS-FORMAT.md): u_tilt (vec2, the Perspective displacement in height
// units, |u_tilt| <= 0.015, already zero when Perspective is off), u_tg (1: main() shifts the picture by u_tilt; 0: the
// wallpaper layers by itself), u_light (vec2, the current direction toward the light), u_light0 (the authored one), u_lw
// (the strength of the engine's brightness wash from the change of light; 0 when the wallpaper reads u_light itself) and
// u_focus (float 0..1, read only by wallpapers that soften themselves; the others are blurred by the engine).
(function (root) {
  const HEAD = `#version 300 es
precision highp float;
uniform vec2 u_res;
uniform float u_dpr;
uniform vec3 u_pal[6];
uniform float u_dark;
uniform float u_grain;
uniform float u_opt;
uniform vec2 u_tilt;
uniform float u_tg;
uniform vec2 u_light;
uniform vec2 u_light0;
uniform float u_lw;
uniform float u_focus;
out vec4 o;
`;
  // the shared library: every wallpaper may use these
  const LIB = `const float PI = 3.14159265;
float sat(float x){ return clamp(x, 0., 1.); }
float s01(float a, float b, float x){ return smoothstep(a, b, x); }
float pxu(){ return 2./u_res.y; }
float cssu(){ return 2.*u_dpr/u_res.y; }
mat2 rot(float a){ float c = cos(a), s = sin(a); return mat2(c, s, -s, c); }
float h21(vec2 p){ vec3 q = fract(vec3(p.xyx)*.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y)*q.z); }
vec2 h22(vec2 p){ vec3 q = fract(vec3(p.xyx)*vec3(.1031, .1030, .0973)); q += dot(q, q.yzx + 33.33); return fract((q.xx + q.yz)*q.zy); }
float gn(vec2 p){
  vec2 i = floor(p), f = p - i;   // not fract(p): on an exact integer the two can disagree and a whole row jumps a cell
  vec2 u = f*f*f*(f*(f*6. - 15.) + 10.);
  float a = dot(h22(i)*2. - 1., f);
  float b = dot(h22(i + vec2(1., 0.))*2. - 1., f - vec2(1., 0.));
  float c = dot(h22(i + vec2(0., 1.))*2. - 1., f - vec2(0., 1.));
  float d = dot(h22(i + vec2(1., 1.))*2. - 1., f - vec2(1., 1.));
  return 1.6*mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}
float fbm(vec2 p){ float s = 0., a = .5; for (int i = 0; i < 5; i++) { s += a*gn(p); p = mat2(1.6, 1.2, -1.2, 1.6)*p + vec2(1.7, 9.2); a *= .5; } return s; }
float fbm3(vec2 p){ float s = 0., a = .5; for (int i = 0; i < 3; i++) { s += a*gn(p); p = mat2(1.6, 1.2, -1.2, 1.6)*p + vec2(1.7, 9.2); a *= .5; } return s; }
float sm1(float x){ x = sat(x); return mix(x, x*x*(3. - 2.*x), .6); }
vec3 ramp(float t){ t = sat(t)*4.; vec3 c = mix(u_pal[0], u_pal[1], sm1(t)); c = mix(c, u_pal[2], sm1(t - 1.)); c = mix(c, u_pal[3], sm1(t - 2.)); return mix(c, u_pal[4], sm1(t - 3.)); }
vec3 dim(vec3 c, float k){ return vec3(c.x*(1. - k), c.yz*(1. - .3*k)); }
vec3 lift(vec3 c, float k){ return vec3(c.x + k, c.yz*(1. - .6*sat(k*2.))); }
float cover(float d){ float w = pxu(); return 1. - smoothstep(-w, w, d); }
float sdBox(vec2 p, vec2 b, float r){ vec2 q = abs(p) - b + r; return length(max(q, 0.)) + min(max(q.x, q.y), 0.) - r; }
`;
  const MAIN = `vec3 s2lin(vec3 c){ return mix(c/12.92, pow((c + .055)/1.055, vec3(2.4)), step(.04045, c)); }
vec3 lin2s(vec3 c){ c = clamp(c, 0., 1.); return mix(c*12.92, 1.055*pow(c, vec3(1./2.4)) - .055, step(.0031308, c)); }
vec3 fromLab(vec3 L){
  float l = L.x + .3963377774*L.y + .2158037573*L.z;
  float m = L.x - .1055613458*L.y - .0638541728*L.z;
  float q = L.x - .0894841775*L.y - 1.291485548*L.z;
  l = l*l*l; m = m*m*m; q = q*q*q;
  return lin2s(vec3(4.0767416621*l - 3.3077115913*m + .2309699292*q, -1.2684380046*l + 2.6097574011*m - .3413193965*q, -.0041960863*l - .7034186147*m + 1.707614701*q));
}
void main(){
  vec2 fc = gl_FragCoord.xy;
  vec2 p = (fc/u_res - .5)*2.*vec2(u_res.x/u_res.y, 1.);
  p += u_tilt*u_tg;
  vec3 L = wall(p);
  L.x += u_lw*dot(p, u_light - u_light0);
  float g = h21(fc*1.13 + 3.7) + h21(fc*.97 + 19.1) - 1.;
  L.x += g*u_grain*.034;
  vec3 c = fromLab(L);
  c += (h21(fc + 41.) + h21(fc + 87.3) - 1.)/255.;
  o = vec4(c, 1.);
}
`;
  const list = [];
  const opts = {};
  const SW = {
    HEAD, LIB, MAIN, list, opts,
    add(w) { list.push(w); },
    // the premium-option data of wallpapers whose entry carries none (the first thirty): SW.options({ id: { dyn, light, persp, focus } })
    options(map) { Object.assign(opts, map); },
    // the full fragment shader of one wallpaper
    fs(w) { return HEAD + LIB + w.src + '\n' + MAIN; },
  };
  root.SW = SW;
})(typeof window !== 'undefined' ? window : globalThis);
