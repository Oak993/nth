#version 300 es
precision highp float;
#define MAXN ${MAXN}
uniform sampler2D u_wall;
uniform vec2 u_px;
uniform float u_k;
uniform int u_n;
uniform vec4 u_box[MAXN];
uniform vec4 u_prm[MAXN];
uniform vec4 u_acc[MAXN];
uniform float u_dark;
uniform float u_glass;
out vec4 o;

// dashersw/liquid-glass-js: roundedRectDistance / pillDistance (a pill is the same distance with radius = height / 2)
float sdRR(vec2 p, vec2 hs, float r){ vec2 q = abs(p) - (hs - r); return length(max(q, 0.)) + min(max(q.x, q.y), 0.) - r; }
// the outward normal of that shape (dashersw: shapeNormal)
vec2 nRR(vec2 p, vec2 hs, float r){
  vec2 q = abs(p) - (hs - r); vec2 s = sign(p);
  if (q.x > 0. && q.y > 0.) return s*normalize(q);
  return q.x > q.y ? vec2(s.x, 0.) : vec2(0., s.y);
}
// a smooth outward normal: the gradient of the distance, by central differences
vec2 nSmooth(vec2 q, vec2 hs, float r){
  float e = .5;
  vec2 g = vec2(sdRR(q + vec2(e, 0.), hs, r) - sdRR(q - vec2(e, 0.), hs, r), sdRR(q + vec2(0., e), hs, r) - sdRR(q - vec2(0., e), hs, r));
  return g/max(length(g), 1e-4);
}
float erfA(float x){
  float s = sign(x); x = abs(x);
  float t = 1./(1. + .3275911*x);
  float y = 1. - (((((1.061405429*t - 1.453152027)*t) + 1.421413741)*t - .284496736)*t + .254829592)*t*exp(-x*x);
  return s*y;
}
float ign(vec2 v){ return fract(52.9829189*fract(dot(v, vec2(.06711056, .00583715)))); }
vec3 wallAt(vec2 P){ return texture(u_wall, vec2(P.x*u_k/u_px.x, 1. - P.y*u_k/u_px.y)).rgb; }

// rizzytoday/liquid-glass: buildDisplacementMap(), per pixel. Returns (R, B) of the map in 0..1; 0.5 is no displacement.
vec2 rizzyMap(vec2 q, vec2 hs, float R, float minD){
  vec2 uv = (q + hs)/(2.*hs);
  float bpx = minD*.035;                                   // border .07 * .5 * min(w, h)
  float sig = max(minD*.1375, 2.5);                        // blur 11 px on an 80 px pill
  float dIn = sdRR(q, hs - bpx, min(R, min(hs.x, hs.y) - bpx));
  float cov = .5 - .5*erfA(dIn/(sig*1.41421));             // the blurred neutral rectangle
  return mix(vec2(1. - uv.x, uv.y), vec2(.5), .93*cov);    // red right to left, blue top to bottom, centre neutral
}

void main(){
  vec2 frag = gl_FragCoord.xy;
  vec2 P = vec2(frag.x, u_px.y - frag.y)/u_k;
  vec3 base = texture(u_wall, frag/u_px).rgb;
  int hit = -1;
  float sh = 0.;
  for (int i = 0; i < MAXN; i++) {
    if (i >= u_n) break;
    vec4 B = u_box[i]; float R = u_prm[i].x;
    float d = sdRR(P - B.xy, B.zw, R);
    if (d < 1.5/u_k) hit = i;
    float sp = 8. + B.w*.3;
    float ds = sdRR(P - B.xy - vec2(0., 4. + B.w*.2), B.zw, R);
    sh = max(sh, 1. - smoothstep(-sp*.35, sp*1.5, ds));
  }
  vec3 col = base*(1. - sh*mix(.16, .34, u_dark)*u_glass);
  if (hit >= 0 && u_glass > .001) {
    vec4 B = u_box[hit]; vec4 PR = u_prm[hit]; vec4 AC = u_acc[hit];
    vec2 q = P - B.xy; vec2 hs = B.zw; float R = PR.x;
    float d = sdRR(q, hs, R);
    float depth = max(-d, 0.);
    vec2 n = nSmooth(q, hs, R);
    float minD = 2.*min(hs.x, hs.y);
    vec2 uvl = (q + hs)/(2.*hs);

    // --- rizzytoday: the displacement map (red right to left, blue top to bottom, the centre neutral)
    vec2 m = rizzyMap(q, hs, R, minD) - .5;
    float sc = -180.*clamp(minD/80., .34, 1.05)*PR.w;
    vec2 dm = m*sc;

    // --- dashersw: refraction along the shape's normal toward the rim, plus the corner boost. Here the normal is smooth
    // (no seam at the corner diagonals), the rim profile is a soft curve a few pixels wide instead of a one-pixel
    // exponential, and the total offset saturates smoothly at a few pixels, so a hard edge in the wallpaper bends at the
    // rim without being smeared across it.
    float rimW = clamp(minD*.22, 6., 16.);
    float prof = 1. - clamp(depth/rimW, 0., 1.);
    prof *= prof;
    float cornerD = max(min(uvl.x, 1. - uvl.x), min(uvl.y, 1. - uvl.y))*minD;
    float corner = exp(-cornerD*.3);
    float maxD = clamp(minD*.16, 5., 14.)*PR.w;
    vec2 dG = dm + n*maxD*(.85*prof + .15*exp(-depth*.15) + .25*corner*prof);
    float dl = length(dG);
    dG *= maxD*(1. - exp(-dl/maxD))/max(dl, 1e-4);
    // the colour fringe, proportional to the offset (rizzytoday's three channels), so it fades with it
    vec2 dR = dG*.93, dB = dG*1.07;

    // --- dashersw: the blur, nine taps, a little wider at the rim; the three channels each read the wallpaper where
    // their own displacement points
    float br = 5.*PR.z + 3.5*prof;
    vec3 g = vec3(0.);
    const vec2 tap[9] = vec2[9](vec2(0.), vec2(1., 0.), vec2(-1., 0.), vec2(0., 1.), vec2(0., -1.), vec2(.7071, .7071), vec2(-.7071, .7071), vec2(.7071, -.7071), vec2(-.7071, -.7071));
    for (int j = 0; j < 9; j++) {
      vec2 o2 = tap[j]*br*(j == 0 ? 0. : (j < 5 ? .55 : .8));
      float w = j == 0 ? .2 : .1;
      g += w*vec3(wallAt(P + dR + o2).r, wallAt(P + dG + o2).g, wallAt(P + dB + o2).b);
    }

    // saturation and lift of the backdrop, then dashersw's top-to-bottom tint (white to .7 grey), a dark twin in dark mode
    float lum = dot(g, vec3(.299, .587, .114));
    g = mix(vec3(lum), g, mix(1.16, 1.24, u_dark));
    vec3 tl = mix(vec3(1.), vec3(.74), uvl.y);
    vec3 td = mix(vec3(.30, .31, .36), vec3(.14, .15, .19), uvl.y);
    g = mix(g, mix(tl, td, u_dark), PR.y);
    vec3 acc = mix(vec3(.0, .37, .72), vec3(.38, .80, 1.), u_dark);
    g = mix(g, acc, AC.x);
    g += (1. - uvl.y)*mix(.045, .03, u_dark);

    // --- the specular rim: thin, two-sided, strongest at the top left and bottom right corners
    vec2 Ld = normalize(vec2(-.52, -.85));
    float f = dot(n, Ld);
    float lit = pow(max(f, 0.), 1.35), back = pow(max(-f, 0.), 1.7);
    float band = exp(-depth/1.1), band2 = exp(-depth/4.2);
    float spec = band*(.18 + 1.05*lit + .5*back) + band2*(.12*lit + .07*back);
    g += spec*mix(.5, .62, u_dark);
    g = mix(g, g*.9, band*.35*(1. - u_dark)*(1. - lit));
    g = mix(g, g*.7, band2*.22*u_dark*(1. - lit));

    float mask = (1. - smoothstep(-.75/u_k, .75/u_k, d))*u_glass;
    col = mix(col, g, mask);
  }
  col += (ign(frag) + ign(frag + vec2(37., 17.)) - 1.)/255.;
  o = vec4(col, 1.);
}
