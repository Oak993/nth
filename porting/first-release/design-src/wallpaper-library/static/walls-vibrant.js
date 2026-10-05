// Static wallpaper library, tier 1: Vibrant. Rich, bold, saturated, still premium.
// roles: [L dark, C dark, L light, C light] per palette slot; packs give the hues (degrees) and the richness.
SW.add({
  id: 'riptide', name: 'Riptide', tier: 'vibrant', grain: .3,
  desc: 'Seven waves of colour stacked from the horizon down, each crest catching the light and casting a soft shadow on the one behind.',
  opt: { label: 'Shadow', def: .5 },
  roles: [[.30, .13, .86, .07], [.38, .16, .80, .10], [.47, .18, .74, .13], [.57, .18, .69, .15], [.67, .16, .64, .16], [.17, .05, .96, .02]],
  packs: [
    { n: 'Coral', k: 'rich', h: [300, 335, 10, 40, 70, 290] },
    { n: 'Lagoon', h: [275, 250, 225, 200, 175, 270] },
    { n: 'Orchid', h: [285, 305, 325, 345, 5, 280] },
    { n: 'Tropic', h: [215, 188, 160, 125, 92, 232] },
    { n: 'Sage', k: 'quiet', h: [240, 210, 175, 145, 110, 230], c: .42 },
  ],
  src: `
float rEdge(float x, float i){
  return .8 - i*.3 + .15*sin(x*.95 + i*1.9 + .4) + .085*sin(x*2.1 - i*2.7 + 1.3) + .03*gn(vec2(x*1.3 + i*7.1, i*3.3));
}
vec3 wall(vec2 p){
  vec3 col = mix(u_pal[5], u_pal[0], sat(.55 - p.y*.45));
  float soft = mix(.035, .15, u_opt);
  for (int k = 0; k < 7; k++) {
    float i = float(k);
    float e = .01;
    float y = rEdge(p.x, i);
    float sl = (rEdge(p.x + e, i) - rEdge(p.x - e, i))/(2.*e);
    float d = (p.y - y)/sqrt(1. + sl*sl);
    float a = cover(d);
    col = dim(col, mix(.38, .55, u_dark)*exp(-max(d, 0.)/soft)*(1. - a));
    vec3 c = ramp(i/6. + .05*sin(p.x*.8 + i*2.));
    float dep = max(-d, 0.);
    c = dim(c, .34*s01(0., .5, dep));
    c.x += .075*exp(-dep/.03) + .03*exp(-dep/.12);
    col = mix(col, c, a);
  }
  col.x += .035*s01(1.8, -1.2, p.x - p.y*.6);
  return col;
}`,
});

SW.add({
  id: 'corolla', name: 'Corolla', tier: 'vibrant', grain: .3,
  desc: 'A fan of long curved petals opening from the corner, each one cupped, lit along its edge and shading the petal beneath.',
  opt: { label: 'Openness', def: .5 },
  roles: [[.42, .17, .62, .16], [.50, .19, .68, .16], [.58, .19, .74, .15], [.65, .17, .80, .13], [.72, .15, .86, .11], [.16, .05, .95, .025]],
  packs: [
    { n: 'Peony', k: 'rich', h: [345, 5, 28, 52, 78, 320] },
    { n: 'Iris', h: [250, 270, 290, 310, 330, 265] },
    { n: 'Tropic', h: [205, 180, 155, 125, 95, 220] },
    { n: 'Marigold', h: [15, 32, 50, 68, 88, 35] },
    { n: 'Blush', k: 'quiet', h: [10, 350, 330, 305, 285, 20], c: .45 },
  ],
  src: `
vec3 wall(vec2 p){
  vec2 O = vec2(-2.05, -1.5);
  vec3 col = mix(u_pal[5], dim(u_pal[5], .35), sat(length(p - vec2(1.3, .9))*.42));
  float step_ = mix(.118, .158, u_opt);
  for (int k = 0; k < 10; k++) {
    float i = float(k);
    float th = .1 + i*step_;
    vec2 q = rot(-th)*(p - O);
    q.y -= .045*q.x*q.x - .12*q.x;
    float Lp = 4.5 + .35*sin(i*1.7);
    float s = q.x/Lp;
    float w = (.4 + .05*sin(i*2.3))*pow(sat(sin(PI*sat(s))), .62);
    float d = max(abs(q.y) - w, max(-q.x, q.x - Lp));
    vec2 qs = rot(-th)*(p - O - vec2(.045, -.06));
    qs.y -= .045*qs.x*qs.x - .12*qs.x;
    float ws = (.4 + .05*sin(i*2.3))*pow(sat(sin(PI*sat(qs.x/Lp))), .62);
    float sh = 1. - s01(-.05, .11, abs(qs.y) - ws);
    float a = cover(d*.85);
    col = dim(col, mix(.3, .42, u_dark)*sh*(1. - a));
    float ny = clamp(q.y/max(w, 1e-3), -1.2, 1.2);
    vec3 c = ramp(i/9. + .1*(s - .5));
    c = dim(c, .3*(1. - s01(.25, .75, s)));
    c.x += .05*ny - .04*ny*ny;
    float edge = max(w - abs(q.y), 0.);
    c.x += (ny > 0. ? .06 : .02)*exp(-edge/.012);
    c.x += .03*exp(-edge/.07);
    col = mix(col, c, a);
  }
  return col;
}`,
});

SW.add({
  id: 'prism', name: 'Prism', tier: 'vibrant', grain: .25,
  desc: 'A glowing orb seen through a pane of reeded glass: every flute flips and slices it into ribbons of colour.',
  opt: { label: 'Flute width', def: .35 },
  roles: [[.25, .12, .82, .07], [.36, .16, .74, .11], [.56, .20, .67, .17], [.80, .15, .90, .09], [.50, .18, .80, .11], [.14, .05, .93, .025]],
  packs: [
    { n: 'Sunset', k: 'rich', h: [285, 330, 18, 75, 40, 270] },
    { n: 'Electric', h: [265, 235, 305, 200, 285, 255] },
    { n: 'Lime', h: [210, 165, 125, 98, 145, 215] },
    { n: 'Cherry', h: [330, 0, 20, 60, 350, 320] },
    { n: 'Rose', k: 'quiet', h: [330, 350, 15, 45, 0, 320], c: .5 },
  ],
  src: `
vec3 bgP(vec2 p){
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .32*p.x - .3*p.y));
  c = mix(c, u_pal[5], .55*s01(-.25, -1.15, p.y));
  vec2 oc = vec2(.38, .12);
  float r = length(p - oc);
  c = mix(c, u_pal[4], .65*exp(-r*r*1.5));
  vec3 orb = mix(u_pal[3], u_pal[2], sm1(r/.58));
  orb.x += .06*s01(.55, .0, length(p - oc - vec2(-.16, .2)));
  c = mix(c, orb, 1. - s01(.5, .64, r));
  float r2 = length(p - vec2(-.82, -.42));
  c = mix(c, mix(u_pal[4], u_pal[2], sm1(r2/.3)), (1. - s01(.22, .34, r2))*.9);
  return c;
}
vec3 wall(vec2 p){
  float fw = mix(.035, .12, u_opt);
  float fx = p.x/fw;
  float cx = (floor(fx) + .5)*fw;
  float f = fract(fx) - .5;
  float xs = cx - 1.6*fw*sin(f*PI);
  vec3 acc = vec3(0.);
  for (int j = 0; j < 6; j++) {
    float a = float(j)*1.047 + .3;
    acc += bgP(vec2(xs, p.y) + vec2(.04, .025)*vec2(cos(a), sin(a)));
  }
  vec3 c = acc/6.;
  float hl = exp(-pow((f + .27)/.07, 2.));
  float seam = exp(-(.5 - abs(f))/.018);
  c.x += .03*hl - .03*seam + .03*cos(f*PI) - .015;
  c.yz *= 1. - .2*hl;
  return c;
}`,
});

SW.add({
  id: 'solstice', name: 'Solstice', tier: 'vibrant', grain: .3,
  desc: 'The sun sitting low over five ridges of mountains, each farther one paler in the haze and lit along its crest.',
  opt: { label: 'Haze', def: .55 },
  roles: [[.64, .17, .90, .09], [.50, .17, .86, .08], [.34, .14, .82, .06], [.19, .08, .76, .07], [.9, .13, .985, .05], [.12, .04, .50, .07]],
  packs: [
    { n: 'Golden Hour', k: 'rich', h: [55, 25, 345, 285, 85, 290] },
    { n: 'Blue Hour', h: [300, 275, 255, 245, 70, 255] },
    { n: 'Desert', h: [50, 32, 15, 345, 88, 15] },
    { n: 'Rose', h: [15, 350, 320, 290, 55, 305] },
    { n: 'Polar', k: 'quiet', h: [210, 222, 236, 250, 95, 240], c: .5 },
  ],
  src: `
float ridgeS(float x, float i){
  float s = 0., a = .5, f = 1.;
  for (int o = 0; o < 5; o++) {
    s += a*(1. - abs(gn(vec2(x*f + i*13.7, i*5.3 + float(o)*3.1))));
    f *= 2.07; a *= .47;
  }
  return s;
}
vec3 wall(vec2 p){
  float hz = -.12;
  float t = sat((p.y - hz)/1.15);
  vec3 sky = mix(u_pal[0], u_pal[1], sm1(t*2.4));
  sky = mix(sky, u_pal[2], sm1(t*2. - .6));
  sky = mix(sky, u_pal[3], sm1(t*1.7 - .75));
  vec2 sp = vec2(.42, hz + .17);
  float sr = length(p - sp);
  sky = mix(sky, u_pal[4], .5*exp(-sr*2.2) + .3*exp(-sr*8.));
  vec3 col = mix(sky, lift(u_pal[4], .02), cover(sr - .085));
  float hzA = mix(.45, 1., u_opt);
  for (int k = 0; k < 5; k++) {
    float i = float(k);
    float u = i/4.;
    vec2 q = p + u_tilt*(.3 + .7*u);
    float base = hz + .2 - i*.2;
    float amp = .2 + i*.06;
    float sc = .5 + i*.08;
    float e = .006;
    float y = base + amp*(ridgeS(q.x*sc, i) - .62);
    float y2 = base + amp*(ridgeS((q.x + e)*sc, i) - .62);
    float sl = (y2 - y)/e;
    float a = cover((q.y - y)/sqrt(1. + sl*sl));
    float far = 1. - i/4.;
    vec3 m = mix(mix(u_pal[0], u_pal[1], .55), u_pal[1], sm1(u*2.));
    m = mix(m, u_pal[2], sm1(u*2. - .6));
    m = mix(m, u_pal[5], sm1(u*1.6 - .55));
    m = dim(m, .18 + .12*u);
    m = mix(m, u_pal[0], far*far*hzA*.55);
    m = mix(m, u_pal[4], .28*far*exp(-length(p - sp)*1.2));
    float dep = max(y - q.y, 0.);
    m = mix(m, mix(u_pal[0], u_pal[1], .6), hzA*.38*s01(.0, .3, dep)*(.3 + .7*far));
    m.x += .08*exp(-dep/.007)*exp(-abs(p.x - sp.x)*.8)*(.35 + .65*far);
    col = mix(col, m, a);
  }
  return col;
}`,
});

SW.add({
  id: 'northlight', name: 'Northlight', tier: 'vibrant', grain: .3,
  desc: 'Curtains of aurora over a still northern lake, the ridge and the light mirrored in the water, with stars after dark.',
  opt: { label: 'Intensity', def: .6 },
  roles: [[.17, .05, .93, .05], [.80, .19, .70, .17], [.56, .20, .60, .17], [.70, .16, .68, .15], [.13, .03, .60, .06], [.11, .04, .78, .08]],
  packs: [
    { n: 'Arctic', k: 'rich', h: [250, 155, 300, 178, 250, 265], hl: [40, 158, 305, 182, 255, 255] },
    { n: 'Rose', h: [262, 352, 292, 18, 262, 272], hl: [60, 352, 292, 18, 270, 265] },
    { n: 'Glacier', h: [232, 192, 255, 178, 232, 248] },
    { n: 'Solar', h: [282, 62, 12, 35, 282, 288] },
    { n: 'Hush', k: 'quiet', h: [250, 165, 280, 195, 250, 262], c: .45 },
  ],
  src: `
float curtainB(vec2 p, float seed, float yb, float h){
  float c = yb + .2*sin(p.x*.9 + seed) + .12*sin(p.x*2.1 + seed*2.3) + .05*gn(vec2(p.x*2.6, seed));
  float up = p.y - c;
  float wx = p.x + .2*up*sin(p.x*1.3 + seed);
  float r = .5 + .5*gn(vec2(wx*9. + 2.2*gn(vec2(wx*1.3, seed + 4.)), up*.3 + seed*2.));
  float env = s01(-.3, .5, gn(vec2(p.x*.6 + seed*3., seed)));
  float core = (up > 0. ? exp(-up/h) : exp(up/.03))*(.15 + .85*r*r*r)*(1. + .9*exp(-max(up, 0.)/.04));
  float glow = exp(-abs(up - .12)/.32)*.3;
  return (core*1.9 + glow)*env;
}
vec3 skyB(vec2 p){
  vec3 c = mix(u_pal[0], u_pal[5], s01(-.45, 1., p.y));
  vec2 cell = floor(p/.045);
  vec2 sp = (cell + .2 + .6*h22(cell))*.045;
  float sz = max(.0011, cssu()*.45)*(.6 + 1.1*h21(cell + 3.1));
  float on = step(.78, h21(cell + 9.7));
  c.x += .5*on*exp(-dot(p - sp, p - sp)/(sz*sz))*u_dark*s01(-.25, .5, p.y);
  float k = mix(.7, 1.5, u_opt);
  float i1 = curtainB(p, 1.3, .02, .34)*k;
  float i2 = curtainB(p*vec2(1.2, 1.) + vec2(2.3, 0.), 7.9, .36, .3)*k*.75;
  vec3 a1 = mix(u_pal[1], u_pal[2], s01(.0, .65, p.y + .02));
  vec3 a2 = mix(u_pal[3], u_pal[2], s01(.25, .85, p.y));
  c = mix(c, a1, sat(i1*1.1));
  c = mix(c, a2, sat(i2));
  c.x += .12*sat(i1 + i2)*u_dark;
  c = mix(c, u_pal[1], .12*k*exp(-abs(p.y + .3)*3.5));
  return c;
}
float ridgeB(float x){
  float s = 0., a = .5, f = 1.;
  for (int o = 0; o < 4; o++) { s += a*(1. - abs(gn(vec2(x*f*.8 + 4.1, float(o)*2.3)))); f *= 2.1; a *= .5; }
  return -.43 + .2*s - .05*x*x*.1;
}
vec3 sceneB(vec2 p){
  vec3 c = skyB(p);
  float d = p.y - ridgeB(p.x);
  vec3 m = mix(u_pal[4], u_pal[0], .3*s01(-.15, 0., d));
  m.x += .05*exp(-abs(d)/.006)*u_dark;
  return mix(c, m, cover(d));
}
vec3 wall(vec2 p){
  float lake = -.45;
  vec3 c = sceneB(p);
  if (p.y < lake + .01) {
    float w = .006*gn(vec2(p.x*3., p.y*55.)) + .004*gn(vec2(p.x*9., p.y*120.));
    vec2 rp = vec2(p.x + w, 2.*lake - p.y + w*2.);
    vec3 r = dim(sceneB(rp), mix(.1, .28, u_dark));
    r = mix(r, u_pal[4], mix(.15, .3, u_dark)*s01(lake, lake - .55, p.y));
    c = mix(c, r, cover(p.y - lake));
  }
  return c;
}`,
});

SW.add({
  id: 'satin', name: 'Satin', tier: 'vibrant', grain: .22,
  desc: 'Folds of iridescent satin: the colour shifts with the angle of every fold, with a soft sheen on the ridges.',
  opt: { label: 'Fold depth', def: .5 },
  roles: [[.40, .16, .68, .12], [.48, .19, .73, .13], [.56, .19, .78, .13], [.64, .17, .83, .12], [.72, .15, .88, .10], [.10, .05, .50, .06]],
  packs: [
    { n: 'Iridescent', k: 'rich', h: [330, 290, 250, 190, 80, 285] },
    { n: 'Peacock', h: [225, 195, 165, 265, 295, 230] },
    { n: 'Rose Gold', h: [20, 40, 62, 10, 350, 15] },
    { n: 'Ruby', h: [10, 350, 330, 25, 45, 355] },
    { n: 'Pearl', k: 'quiet', h: [300, 260, 220, 40, 10, 270], c: .35 },
  ],
  src: `
float hS(vec2 p){
  vec2 q = rot(.35)*p;
  q += .28*vec2(sin(q.y*1.3 + .5), sin(q.x*1.1 + 1.7));
  float w = 1.6*sin(q.y*1.1 + .4) + .8*sin(q.y*2.3 - q.x*.5);
  return .5*sin(q.x*3.4 + w) + .25*sin(q.x*6.1 + 1.3*w - q.y*1.1) + .2*sin(q.y*2.9 + q.x*1.2);
}
vec3 wall(vec2 p){
  float e = .003, amp = mix(.3, .75, u_opt);
  float h0 = hS(p), hx = hS(p + vec2(e, 0.)), hy = hS(p + vec2(0., e));
  vec3 n = normalize(vec3(-(hx - h0)/e*amp, -(hy - h0)/e*amp, 1.));
  vec3 Ld = normalize(vec3(-.5, .62, .6));
  float dif = dot(n, Ld);
  vec3 H = normalize(Ld + vec3(0., 0., 1.));
  float nh = clamp(dot(n, H), 0., 1.);
  float t = .5 + .45*sin(p.x*.55 - p.y*.4 + h0*.5);
  vec3 c = ramp(t);
  c.yz = rot(n.x*.9 - n.y*.5)*c.yz;
  float occ = sat(dif*.9 + .1);
  c = mix(c, u_pal[5], .7*(1. - occ)*(1. - occ));
  c.x *= .78 + .3*occ;
  c.x += .16*pow(nh, 26.) + .12*pow(nh, 160.);
  c.yz *= 1. - .4*pow(nh, 26.);
  return c;
}`,
});

SW.add({
  id: 'orbit', name: 'Orbit', tier: 'vibrant', grain: .45,
  desc: 'Six spheres resting on a seamless studio sweep under a large soft key light, each with its contact shadow and the room in its rim.',
  opt: { label: 'Gloss', def: .6 },
  roles: [[.55, .18, .62, .17], [.62, .19, .68, .16], [.68, .18, .74, .15], [.74, .16, .80, .13], [.80, .14, .86, .11], [.19, .05, .93, .025]],
  packs: [
    { n: 'Dusk', k: 'rich', h: [332, 356, 22, 302, 276, 285] },
    { n: 'Ocean', h: [255, 225, 195, 170, 285, 245] },
    { n: 'Candy', h: [350, 30, 80, 205, 285, 290] },
    { n: 'Glacier', h: [205, 220, 188, 236, 172, 222] },
    { n: 'Stone', k: 'quiet', h: [40, 60, 250, 20, 200, 60], c: .32 },
  ],
  src: `
vec3 studio(vec2 p){
  float t = s01(-.42, .2, p.y + .04*p.x);
  vec3 fl = mix(lift(u_pal[5], .035), u_pal[5], s01(-1., -.25, p.y));
  vec3 wl = mix(u_pal[5], dim(u_pal[5], .28), s01(-.1, 1.1, p.y));
  vec3 c = mix(fl, wl, t);
  c.x += .055*exp(-dot(p - vec2(-.55, .15), p - vec2(-.55, .15))*.3) - .035*s01(.4, 1.8, p.x);
  return c;
}
void ball(inout vec3 col, vec2 p, vec2 g, float r, vec3 bc, float bl){
  vec2 c = g + vec2(0., r);
  vec2 q = (p - c)/r;
  float d = length(p - c) - r;
  float aa = pxu()*1.3 + bl;
  float a = 1. - smoothstep(-aa, aa, d);
  vec2 sq = (p - g - vec2(.62*r, .2*r))/vec2(1.35*r, .36*r);
  float soft = exp(-dot(sq, sq)*1.2);
  vec2 cq = (p - g - vec2(.08*r, 0.))/vec2(.62*r, .1*r);
  float cont = exp(-dot(cq, cq)*1.5);
  col = dim(col, (mix(.26, .4, u_dark)*soft + mix(.42, .55, u_dark)*cont)*(1. - a));
  vec2 bq = (p - g)/vec2(1.3*r, .35*r);
  col.yz = mix(col.yz, bc.yz, .22*exp(-dot(bq, bq))*(1. - a));
  float z = sqrt(max(1. - dot(q, q), 0.));
  vec3 n = normalize(vec3(q, z + .002));
  vec3 K = normalize(vec3(-.62, .55, .56)), F = normalize(vec3(.85, .05, .5));
  float kw = sat(dot(n, K)*.62 + .38);
  kw = kw*kw*(3. - 2.*kw);
  float fw = sat(dot(n, F))*.2;
  float bo = sat(-n.y)*.22;
  vec3 b = vec3(bc.x*(.34 + .64*kw + fw + .25*bo), bc.yz*(1. + .55*kw*(1. - kw) + .12*(1. - kw) - .18*kw*kw));
  b.x += .045*(1. - kw)*pow(1. - z, 1.5);
  b = mix(b, studio(vec2(p.x, g.y - .1)), .22*bo);
  vec3 R = vec3(2.*n.z*n.x, 2.*n.z*n.y, 2.*n.z*n.z - 1.);
  float gl = mix(.3, 1., u_opt);
  vec3 env = mix(dim(u_pal[5], .15), lift(u_pal[5], .1), s01(-.25, .25, R.y));
  float fr = pow(1. - z, 3.);
  b = mix(b, env, (.08 + .42*gl)*fr);
  float box = sdBox(R.xy - vec2(-.44, .42), vec2(.24, .16), .13);
  float sb = (1. - s01(-.14, .12, box))*s01(-.1, .35, R.z);
  b = mix(b, vec3(min(b.x + .3, .975), b.yz*.5), sb*gl*.7);
  float fb = (1. - s01(-.1, .1, sdBox(R.xy - vec2(.62, .02), vec2(.05, .2), .05)))*s01(-.1, .3, R.z);
  b.x += .06*fb*gl;
  col = mix(col, b, a);
}
vec3 wall(vec2 p){
  vec3 col = studio(p);
  ball(col, p, vec2(1.08, -.1), .11, u_pal[4], .006);
  ball(col, p, vec2(-.1, -.16), .17, u_pal[3], .004);
  ball(col, p, vec2(-1.08, -.36), .31, u_pal[1], .002);
  ball(col, p, vec2(.6, -.62), .46, u_pal[0], 0.);
  ball(col, p, vec2(-.42, -.9), .29, u_pal[2], .003);
  ball(col, p, vec2(1.45, -.96), .25, u_pal[4], .005);
  return col;
}`,
});

SW.add({
  id: 'atoll', name: 'Atoll', tier: 'vibrant', grain: .25,
  desc: 'A tropical lagoon from above: deep channels, turquoise shallows netted with caustic light, and a sandbar edged with foam.',
  opt: { label: 'Caustics', def: .55 },
  roles: [[.24, .09, .52, .13], [.34, .12, .62, .14], [.49, .13, .74, .12], [.64, .12, .84, .09], [.74, .06, .93, .05], [.93, .02, .99, .01]],
  packs: [
    { n: 'Atoll', k: 'rich', h: [258, 236, 205, 186, 85, 200] },
    { n: 'Emerald', h: [232, 192, 168, 152, 92, 170] },
    { n: 'Coral Sea', h: [262, 242, 215, 196, 45, 205] },
    { n: 'Lilac', h: [292, 272, 252, 232, 65, 260] },
    { n: 'Glacier Lake', k: 'quiet', h: [242, 222, 210, 200, 80, 210], c: .5 },
  ],
  src: `
float shoreL(float x){ return -.5 + .22*sin(x*.8 + .6) + .1*sin(x*1.9 - 1.) + .04*gn(vec2(x*2.5, 3.)); }
vec3 wall(vec2 p){
  float sy = shoreL(p.x);
  float up = p.y - sy;
  float D = .84 - .62*sat(up*.75) + .16*fbm(p*.9 + vec2(3., 1.)) - .1*s01(.5, 1.6, up);
  vec3 c = ramp(sat(D*1.12 - .05));
  float reef = s01(.12, .35, fbm(p*1.6 + 9.))*s01(.2, .5, up)*s01(1.5, .7, up);
  c = dim(c, .22*reef);
  c.yz = mix(c.yz, u_pal[1].yz, .25*reef);
  vec2 q = p*vec2(.9, 1.15);
  vec2 w = q*3.4 + .9*vec2(gn(q*1.3), gn(q*1.3 + 5.2));
  float ca = exp(-abs(gn(w))*9.) + .6*exp(-abs(gn(w*1.65 + 2.3))*11.);
  float shal = s01(.35, .85, D)*s01(-.01, .04, up);
  c.x += mix(.03, .12, u_opt)*ca*shal;
  c.yz *= 1. - .2*ca*shal*u_opt;
  float edge = up + .012*gn(vec2(p.x*9., 1.));
  float sand = cover(edge);
  vec3 sd = u_pal[4];
  sd.x += .02*gn(p*7.) - .06*s01(-.08, 0., edge);
  sd = mix(sd, dim(u_pal[3], .15), .4*s01(-.06, 0., edge));
  c = mix(c, sd, sand);
  float foam = exp(-abs(edge - .006)/.006) + .55*exp(-abs(edge - .055 - .01*gn(vec2(p.x*4., 7.)))/.007) + .3*exp(-abs(edge - .12 - .02*gn(vec2(p.x*3., 2.)))/.008);
  foam *= .55 + .45*gn(p*vec2(18., 40.));
  c = mix(c, u_pal[5], sat(foam)*.75*(1. - sand));
  return c;
}`,
});

SW.add({
  id: 'radiant', name: 'Radiant', tier: 'vibrant', grain: .3,
  desc: 'Pleated rays fanning up from a glow below the frame, each fold lit on one face and shadowed on the other.',
  opt: { label: 'Pleats', def: .5 },
  roles: [[.45, .18, .70, .14], [.52, .20, .74, .15], [.58, .20, .78, .15], [.62, .19, .82, .14], [.66, .17, .85, .12], [.90, .11, .985, .04]],
  packs: [
    { n: 'Sunrise', k: 'rich', h: [300, 340, 15, 45, 75, 80] },
    { n: 'Aqua', h: [272, 242, 212, 182, 152, 190] },
    { n: 'Berry', h: [322, 342, 2, 292, 270, 350] },
    { n: 'Citrus', h: [112, 92, 72, 52, 32, 95] },
    { n: 'Sand', k: 'quiet', h: [42, 30, 20, 52, 64, 72], c: .4 },
  ],
  src: `
vec3 wall(vec2 p){
  vec2 O = vec2(.0, -1.32);
  vec2 q = p - O;
  float r = length(q);
  float a = atan(q.x, q.y);
  float N = mix(5., 9., u_opt);
  float s = (a + .1*r)*N/PI*2.;
  float x = cos(s*PI);
  float ws = pxu()*N*2./max(r, .05);
  float fold = x/(abs(x) + .035 + ws*2.5);
  vec3 c = ramp(.5 + a*.37 + .03*r);
  c = dim(c, .26*s01(.3, 2.6, r));
  c.x += .055*fold*(1. - .4*exp(-r)) - .015*(1. - abs(fold));
  c.yz *= 1. - .1*fold;
  float g = exp(-r*1.15);
  c = mix(c, u_pal[5], .8*g*g + .25*g);
  return c;
}`,
});

SW.add({
  id: 'spectrum', name: 'Spectrum', tier: 'vibrant', grain: .3,
  desc: 'A single beam of light breaking into a wide spectrum as it crosses a hazy, darkened room.',
  opt: { label: 'Spread', def: .55 },
  roles: [[.62, .20, .60, .18], [.71, .18, .68, .16], [.80, .17, .78, .15], [.72, .15, .70, .13], [.60, .18, .60, .16], [.15, .03, .95, .012]],
  packs: [
    { n: 'Rainbow', k: 'rich', h: [25, 62, 108, 205, 292, 262] },
    { n: 'Fire', h: [10, 32, 55, 78, 96, 25] },
    { n: 'Ice', h: [178, 198, 222, 250, 280, 245] },
    { n: 'Rose', h: [330, 350, 12, 32, 300, 320] },
    { n: 'Mist', k: 'quiet', h: [25, 62, 108, 205, 292, 252], c: .45 },
  ],
  src: `
vec3 wall(vec2 p){
  vec3 col = mix(lift(u_pal[5], .05), dim(u_pal[5], .35), sat(.42 - p.y*.3 + p.x*.1));
  col.x += .03*fbm3(p*1.6)*u_dark;
  vec2 P0 = vec2(-.62, .3);
  vec2 dIn = normalize(P0 - vec2(-2.2, .95));
  vec2 v = p - P0;
  float along = dot(v, dIn), across = dot(v, vec2(-dIn.y, dIn.x));
  float Iin = exp(-across*across/(.0007 + .0004*u_opt))*s01(.04, -.08, along);
  float ang = atan(v.y, v.x);
  float spread = mix(.35, .75, u_opt);
  float a0 = -.2 - spread;
  float t = (ang - a0)/spread;
  float r = length(v);
  float soft = .025 + .05/(1. + r*3.);
  float fan = s01(-soft*4., soft*4., t)*s01(1. + soft*4., 1. - soft*4., t)*s01(.0, .25, r);
  float haze = .6 + .4*fbm3(vec2(ang*14., r*1.1 + 3.));
  float I = fan*haze*exp(-r*.32);
  float bloom = s01(-.6, .5, t)*s01(1.6, .5, t)*exp(-r*.5)*.22;
  col = mix(col, ramp(sat(t)), sat(bloom));
  col = mix(col, ramp(sat(t)), sat(I*1.5));
  col.x += .14*I*u_dark;
  col = mix(col, lift(u_pal[2], .2), sat(Iin*.9));
  col.x += mix(.12, .25, u_dark)*exp(-r*r*120.);
  return col;
}`,
});
