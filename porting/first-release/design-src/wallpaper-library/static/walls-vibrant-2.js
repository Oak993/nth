// Static wallpaper library, tier 1: Vibrant, second set (OD-0110 second amendment). Rich, bold, saturated, still premium.
// roles: [L dark, C dark, L light, C light] per palette slot; packs give the hues (degrees) and the richness.
// Premium-option data per wallpaper (provisional, in the brief's shape until OPTIONS-FORMAT.md exists):
//   dyn:   the time-of-day look. dawn / day / dusk / night: [dL, chroma scale, hue shift in degrees], applied to every
//          palette role; light (where the wallpaper has a light direction): [x, y] per key time, unit direction.
//   persp: { def: on by default (1) or off (0), depth: 0..1 how much depth the parallax should suggest }.
//   focus: { mode: 'post' } the cheap built-in softening (the static render is drawn once).

SW.add({
  id: 'dichroic', name: 'Dichroic', tier: 'vibrant', grain: .22,
  desc: 'Tall panes of dichroic glass standing off a sunlit wall: each one shifts colour across its face and throws its complementary light behind it.',
  opt: { label: 'Cast light', def: .55 },
  roles: [[.62, .19, .7, .17], [.58, .2, .66, .18], [.66, .18, .74, .16], [.6, .19, .68, .17], [.7, .16, .78, .14], [.17, .025, .955, .012]],
  packs: [
    { n: 'Spectra', k: 'rich', h: [330, 265, 200, 5, 75, 275] },
    { n: 'Lagoon', h: [215, 180, 250, 155, 195, 230] },
    { n: 'Ember', h: [15, 345, 45, 320, 65, 300] },
    { n: 'Violet', h: [300, 275, 325, 250, 345, 285] },
    { n: 'Frost', k: 'quiet', h: [230, 205, 260, 185, 290, 240], c: .45 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 8, light: [-.85, .35] },
    day: { l: 0, c: 1, h: 0, light: [-.45, .85] },
    dusk: { l: -.025, c: 1.12, h: 14, light: [.85, .35] },
    night: { l: -.09, c: .82, h: -10, light: [-.3, .9] },
  },
  light: { def: [-.45, .85] },
  persp: { def: 1, depth: .7 },
  focus: { def: 0 },
  src: `
float dkmF(){ return 1. - s01(.3, .7, u_pal[5].x); }
vec4 paneG(float i){
  if (i < .5) return vec4(.62, .3, .26, .78);
  if (i < 1.5) return vec4(1.02, .12, .28, .82);
  if (i < 2.5) return vec4(1.42, -.08, .27, .8);
  if (i < 3.5) return vec4(-1.32, -.42, .3, .62);
  return vec4(-.92, -.72, .2, .44);
}
float paneA(float i){ return i < .5 ? -.2 : i < 1.5 ? -.1 : i < 2.5 ? .02 : i < 3.5 ? .16 : .26; }
float paneD(vec2 p, float i){ vec4 g = paneG(i); return sdBox(rot(paneA(i))*(p - g.xy), g.zw, .022); }
vec2 paneQ(vec2 p, float i){ vec4 g = paneG(i); return rot(paneA(i))*(p - g.xy)/g.zw; }
vec3 faceC(float i, vec2 q){ return mix(ramp(fract(i*.27)), ramp(fract(i*.27 + .5)), sm1(sat(.5 + .45*q.y + .2*q.x))); }
vec3 castC(float i, vec2 q){ return mix(ramp(fract(i*.27 + .5)), ramp(fract(i*.27 + .7)), sm1(sat(.5 - .4*q.y))); }
vec3 wallD(vec2 p){
  vec2 sp = vec2(-1.1, 1.05);
  float r = length(p - sp);
  vec3 c = mix(lift(u_pal[5], mix(.025, .11, dkmF())), dim(u_pal[5], mix(.1, .32, dkmF())), sat(r*.36));
  float k = mix(.35, 1., u_opt);
  vec2 off = vec2(.16, -.13);
  for (int j = 0; j < 5; j++) {
    float i = float(j);
    vec2 q = p - off*(1. + .25*mod(i*1.3, 1.));
    float d = paneD(q, i);
    float soft = .012 + .02*sat(-d*2.);
    float inside = 1. - s01(-soft, soft, d);
    vec3 t = castC(i, paneQ(q, i));
    vec3 lit = vec3(mix(c.x, t.x + .08, mix(.25, .55, dkmF())), mix(c.yz, t.yz*1.3, .85));
    c = mix(c, lit, inside*k);
    c.x += k*mix(.06, .12, dkmF())*exp(-abs(d + .01)/.008)*(1. - s01(-.1, .3, dot(normalize(q - paneG(i).xy), normalize(off))));
  }
  return c;
}
vec3 wall(vec2 p){
  vec3 col = wallD(p);
  vec2 Ld = normalize(vec2(-.62, .78));
  for (int j = 0; j < 5; j++) {
    float i = float(j);
    float d = paneD(p, i);
    float a = cover(d);
    if (a > 0.) {
      float e = .002;
      vec2 n = normalize(vec2(paneD(p + vec2(e, 0.), i) - paneD(p - vec2(e, 0.), i), paneD(p + vec2(0., e), i) - paneD(p - vec2(0., e), i)) + 1e-6);
      float ins = max(-d, 0.);
      vec3 under = wallD(p + n*.03*exp(-ins/.03));
      vec2 q = paneQ(p, i);
      vec3 f = faceC(i, q);
      vec3 tr = vec3(mix(under.x*(.7 + .3*f.x), f.x, mix(.35, .6, dkmF())), mix(under.yz, f.yz*1.1, .72));
      float sheen = exp(-pow((q.x*.5 + q.y*.85 - .45)/.35, 2.));
      tr.x += mix(.06, .1, dkmF())*sheen;
      tr.yz *= 1. - .2*sheen;
      float facing = sat(dot(n, Ld));
      float rim = exp(-ins/.0045);
      tr.x += rim*(.18*facing + .04) - .06*exp(-abs(ins - .011)/.004)*(1. - facing);
      tr.x += .04*exp(-ins/.022)*facing;
      tr.yz *= 1. - .4*rim*facing;
      col = mix(col, tr, a);
    }
  }
  return col;
}`,
});

SW.add({
  id: 'chiffon', name: 'Chiffon', tier: 'vibrant', grain: .25,
  desc: 'Two lengths of sheer chiffon drifting across the frame, light passing through every soft fold and the colour deepening where they cross.',
  opt: { label: 'Folds', def: .5 },
  roles: [[.5, .2, .7, .17], [.58, .2, .74, .16], [.66, .18, .79, .14], [.72, .15, .83, .12], [.78, .12, .87, .1], [.12, .05, .965, .015]],
  packs: [
    { n: 'Fuchsia', k: 'rich', h: [352, 326, 300, 20, 45, 300] },
    { n: 'Ocean', h: [255, 230, 205, 280, 300, 262] },
    { n: 'Coral', h: [15, 355, 335, 30, 45, 330] },
    { n: 'Mint', h: [165, 190, 215, 140, 120, 230] },
    { n: 'Pearl', k: 'quiet', h: [330, 300, 275, 20, 45, 290], c: .42 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: -8, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.2, .95] },
    dusk: { l: -.025, c: 1.12, h: 10, light: [.9, .3] },
    night: { l: -.09, c: .82, h: -12, light: [0, 1] },
  },
  light: { def: [-.2, .95] },
  persp: { def: 1, depth: .5 },
  focus: { def: 0 },
  src: `
float dkmF(){ return 1. - s01(.3, .7, u_pal[5].x); }
float cY(float x, float k){ return k < .5 ? -.25 + .3*sin(.78*x + .2) + .1*sin(1.7*x - 1.) + .12*x : .42 + .22*sin(.9*x + 2.4) + .08*sin(2.*x + .5) - .08*x; }
float cW(float x, float k){ return (k < .5 ? .48 : .36) + .08*sin(1.2*x + 2.3*k) + .03*sin(2.7*x - k); }
vec3 sheet(vec2 p, float k, vec3 col, out float a, out float dd){
  float x = p.x;
  float y = cY(x, k);
  float w = cW(x, k);
  float e = .01;
  float sl = (cY(x + e, k) - cY(x - e, k))/(2.*e);
  float hem = .012*sin(x*9. + k*3.)*sin(x*3.1 + 1.) + .006*gn(vec2(x*6. + k*3., k));
  float d = (abs(p.y - y) - w)/sqrt(1. + sl*sl) + hem;
  a = 1. - s01(-pxu()*1.2, pxu()*1.2, d);
  dd = d;
  float v = (p.y - y)/w;
  float nf = mix(.5, 1.5, u_opt)*(k > .5 ? .6 : 1.);
  float ph = v*nf*(1. + .6*sin(x*.9 + k*2.)) + .9*sin(x*.8 + k*2.2) + .3*sin(x*1.9 - k) + .35*v*v;
  float s = sin(ph*PI), c = cos(ph*PI);
  float graze = abs(s);
  float dens = .3 + .45*pow(graze, 1.5) + .35*exp(-max(-d, 0.)/.018);
  float sheen = pow(sat(.5 + .5*c), 6.)*(.6 + .4*sin(x*.8 + k));
  vec3 fc = ramp(sat(.08 + .45*k + .2*v + .12*sin(x*.6 + k*1.7)));
  vec3 tr = vec3(mix(col.x, fc.x, mix(.5, .72, dkmF())), mix(col.yz, fc.yz*1.2, .55 + .4*graze));
  tr.x += mix(.04, .12, dkmF())*sheen - mix(.05, -.03, dkmF())*graze*graze;
  tr.x += mix(.02, .06, dkmF())*exp(-max(-d, 0.)/.004);
  a *= sat(dens);
  return tr;
}
vec3 wall(vec2 p){
  vec3 col = mix(u_pal[5], mix(u_pal[5], u_pal[0], .16), s01(-1.4, 1.4, p.x - p.y));
  col.x += mix(.012, .05, dkmF())*exp(-dot(p - vec2(-.5, .65), p - vec2(-.5, .65))*.45);
  float a, d;
  vec3 t = sheet(p, 1., col, a, d);
  col = mix(col, t, a);
  float a0, d0;
  t = sheet(p - vec2(.04, -.08), 0., col, a0, d0);
  col = dim(col, mix(.07, .2, dkmF())*(1. - s01(-.1, .08, d0)));
  t = sheet(p, 0., col, a, d);
  col = mix(col, t, a);
  return col;
}`,
});

SW.add({
  id: 'plush', name: 'Plush', tier: 'vibrant', grain: .32,
  desc: 'Heavy crushed velvet hanging in deep folds: dark where the pile faces you and glowing along every fold as it turns away.',
  opt: { label: 'Crush', def: .5 },
  roles: [[.3, .15, .64, .16], [.4, .19, .72, .16], [.58, .2, .83, .12], [.76, .13, .92, .06], [.22, .1, .57, .15], [.08, .04, .48, .12]],
  packs: [
    { n: 'Ruby', k: 'rich', h: [10, 0, 352, 15, 5, 355] },
    { n: 'Sapphire', h: [264, 258, 250, 235, 266, 266] },
    { n: 'Emerald', h: [160, 166, 172, 150, 162, 168] },
    { n: 'Amethyst', h: [308, 302, 295, 320, 306, 300] },
    { n: 'Dove', k: 'quiet', h: [285, 275, 265, 250, 280, 275], c: .35 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 6, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.45, .85] },
    dusk: { l: -.025, c: 1.12, h: 8, light: [.9, .35] },
    night: { l: -.09, c: .82, h: -6, light: [-.2, .95] },
  },
  light: { def: [-.5, .86], own: true },
  persp: { def: 1, depth: .45 },
  focus: { def: 0 },
  src: `
float hV(vec2 p){
  vec2 q = p - vec2(-2.3, -1.55);
  float r = length(q);
  float a = atan(q.y, q.x);
  float u = a*11. + .5*sin(r*1.2 + a*2.) + .2*sin(r*2.7 - a*5.);
  float h = sin(u) + .38*sin(2.03*u + 1.3 + .8*sin(r*2.)) + .12*sin(4.1*u + r);
  return .105*r*h;
}
vec3 wall(vec2 p){
  float e = .0025;
  float h0 = hV(p), hx = hV(p + vec2(e, 0.)), hy = hV(p + vec2(0., e));
  vec2 gr = vec2(hx - h0, hy - h0)/e;
  float cr = u_opt;
  vec2 cn = vec2(gn(p*2.2 + 4.), gn(p*2.2 + 11.)) + .5*vec2(gn(p*5.1), gn(p*5.1 + 3.));
  vec3 n = normalize(vec3(-gr + cr*.14*cn, 1.));
  vec3 L = normalize(vec3(normalize(u_light)*.7, .72));
  float dif = sat(dot(n, L));
  float gz = sat((1. - n.z)*1.7);
  float pch = s01(-.4, .4, fbm3(p*1.1 + 7.));
  float side = .5 + .5*dot(normalize(n.xy + 1e-4), normalize(L.xy));
  float sheen = pow(gz, 2.)*(.25 + .75*side)*(1. + cr*(.8*pch - .3));
  float t = sat(.5 + .32*sin(p.x*.5 - p.y*.4 + .6));
  vec3 base = mix(u_pal[4], u_pal[0], sm1(t));
  vec3 c = vec3(base.x*(.45 + .35*dif), base.yz*1.05);
  vec3 sh = mix(u_pal[1], u_pal[2], sm1(sheen*1.2));
  sh = mix(sh, u_pal[3], .35*sm1(sheen*1.5 - .9));
  c = mix(c, sh, sat(sheen*1.6));
  c.x += .1*pow(sat(sheen*1.2), 3.);
  float hn = hV(p)/(.105*length(p - vec2(-2.3, -1.55)));
  float val = s01(.2, -1.2, hn);
  c = mix(c, vec3(u_pal[5].x, u_pal[5].yz*1.2), .6*val*(1. - sat(sheen*1.5)));
  c.x += .012*gn(p/cssu()*vec2(.8, .3))*(.4 + sheen);
  return c;
}`,
});

SW.add({
  id: 'twist', name: 'Twist', tier: 'vibrant', grain: .42,
  desc: 'Two wide ribbons twisting through space, turning their bright face and their deep reverse to you in turn, with a highlight at every turn.',
  opt: { label: 'Twists', def: .5 },
  roles: [[.58, .2, .64, .19], [.66, .19, .72, .17], [.74, .16, .8, .14], [.34, .15, .44, .17], [.46, .18, .56, .18], [.13, .04, .95, .018]],
  packs: [
    { n: 'Sunset', k: 'rich', h: [350, 22, 58, 285, 318, 285] },
    { n: 'Glacier', h: [222, 200, 180, 262, 242, 245] },
    { n: 'Orchid', h: [318, 338, 2, 268, 292, 290] },
    { n: 'Jade', h: [162, 150, 138, 215, 192, 210] },
    { n: 'Mauve', k: 'quiet', h: [330, 350, 15, 280, 300, 300], c: .42 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 10, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .9] },
    dusk: { l: -.025, c: 1.12, h: 15, light: [.9, .3] },
    night: { l: -.09, c: .82, h: -10, light: [-.2, .95] },
  },
  light: { def: [-.59, .81], own: true },
  persp: { def: 1, depth: .8 },
  focus: { def: 0 },
  src: `
float tY(float x, float k){ return k < .5 ? -.12 + .3*sin(.7*x + .5) + .08*sin(1.6*x - .6) + .14*x : .5 + .2*sin(.8*x + 2.6) + .06*sin(1.9*x) - .1*x; }
float tTh(float x, float k){ return (k < .5 ? 1. : .8)*mix(.8, 1.45, u_opt)*x + .55*sin(.5*x + 1.1 + k*2.) + .5 + k*1.3; }
vec3 bgT(vec2 p){
  vec3 c = mix(lift(u_pal[5], mix(.025, .07, u_dark)), dim(u_pal[5], mix(.1, .3, u_dark)), sat(.5 - .32*p.y + .12*p.x));
  return c;
}
float ribD(vec2 p, float W, float k){
  float y = tY(p.x, k);
  float hw = W*abs(cos(tTh(p.x, k))) + .0035;
  float e = .01;
  float sl = (tY(p.x + e, k) - tY(p.x - e, k))/(2.*e);
  return (abs(p.y - y) - hw)/sqrt(1. + sl*sl);
}
vec3 ribC(vec2 p, float W, float k, vec3 col){
  float th = tTh(p.x, k);
  float cs = cos(th), sn = sin(th);
  float y = tY(p.x, k);
  float hw = W*abs(cs) + .0035;
  float v = (p.y - y)/hw;
  vec3 n = normalize(vec3(0., -sn, cs));
  if (cs < 0.) n = -n;
  vec3 L = normalize(vec3(normalize(u_light)*.77, .64));
  float dif = sat(dot(n, L));
  vec3 H = normalize(L + vec3(0., 0., 1.));
  float nh = sat(dot(n, H));
  float t = sat(.5 + .26*p.x + .1*v*abs(cs));
  vec3 fr = ramp(t*.5);
  vec3 bk = mix(u_pal[3], u_pal[4], sm1(t));
  vec3 c = cs > 0. ? fr : bk;
  c = vec3(c.x*(.64 + .46*dif), c.yz*(1.05 + .1*(1. - dif)));
  c.x += .14*pow(nh, 26.) + .06*pow(nh, 5.);
  c.x += .006*gn(p/cssu()*.5);
  c.yz *= 1. - .45*pow(nh, 40.);
  c.x += .06*exp(-max(1. - abs(v), 0.)*hw/.004)*(.4 + .6*dif);
  c.x -= .04*exp(-max(1. - abs(v), 0.)*hw/.03)*(1. - dif);
  return c;
}
vec3 wall(vec2 p){
  float W = .3;
  vec3 col = bgT(p);
  float d1 = ribD(p, W*.62, 1.);
  float s1 = ribD(p - vec2(.04, -.09), W*.62, 1.);
  col = dim(col, mix(.16, .3, u_dark)*(1. - s01(-.08, .12, s1)));
  vec3 c1 = ribC(p, W*.62, 1., col);
  c1 = mix(c1, dim(u_pal[5], .1), mix(.32, .38, u_dark));
  float fw1 = pxu()*1.5 + .006*s01(.2, 1.4, abs(p.x - .2));
  col = mix(col, c1, 1. - s01(-fw1, fw1, d1));
  float s0 = ribD(p - vec2(.07, -.17), W, 0.);
  col = dim(col, mix(.2, .38, u_dark)*(1. - s01(-.12, .22, s0)) + mix(.06, .1, u_dark)*(1. - s01(-.02, .05, ribD(p - vec2(.02, -.05), W, 0.))));
  float fw0 = pxu()*1.5 + .005*s01(.2, 1.4, abs(p.x + .1));
  col = mix(col, ribC(p, W, 0., col), 1. - s01(-fw0, fw0, ribD(p, W, 0.)));
  return col;
}`,
});

SW.add({
  id: 'soapfilm', name: 'Soap Film', tier: 'vibrant', grain: .2,
  desc: 'A soap bubble, thin as air: bands of interference colour swirling at its rim as it drains, and the reflection of an arched window on its face.',
  opt: { label: 'Swirl', def: .5 },
  roles: [[.66, .17, .66, .16], [.7, .18, .7, .16], [.74, .17, .74, .15], [.7, .18, .7, .16], [.66, .17, .66, .16], [.11, .025, .93, .015]],
  packs: [
    { n: 'Iris', k: 'rich', h: [305, 225, 160, 85, 20, 270] },
    { n: 'Lagoon', h: [255, 215, 178, 148, 285, 240] },
    { n: 'Rose', h: [340, 12, 45, 300, 270, 320] },
    { n: 'Citrine', h: [85, 55, 25, 125, 165, 250] },
    { n: 'Opal', k: 'quiet', h: [305, 225, 160, 85, 20, 260], c: .45 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 12, light: [-.9, .35] },
    day: { l: 0, c: 1, h: 0, light: [-.55, .8] },
    dusk: { l: -.025, c: 1.12, h: 15, light: [.9, .35] },
    night: { l: -.09, c: .82, h: -12, light: [-.4, .9] },
  },
  light: { def: [-.55, .8] },
  persp: { def: 1, depth: .6 },
  focus: { def: 0 },
  src: `
vec2 swirlS(vec2 p, vec2 c, float s, float r){
  vec2 d = p - c;
  float a = s*exp(-dot(d, d)/(r*r));
  return c + rot(a)*d;
}
vec3 bgS(vec2 p){
  vec3 c = mix(lift(u_pal[5], mix(.02, .05, u_dark)), dim(u_pal[5], mix(.1, .35, u_dark)), sat(.5 + .25*p.x - .3*p.y));
  vec2 b = p - vec2(-1.15, -.55);
  c = mix(c, mix(c, u_pal[1], .4), .5*exp(-dot(b, b)*2.2));
  vec2 b2 = p - vec2(-.35, .85);
  c = mix(c, mix(c, u_pal[0], .35), .45*exp(-dot(b2, b2)*3.));
  return c;
}
vec3 wall(vec2 p){
  vec3 col = bgS(p);
  vec2 C = vec2(.78, .02);
  float R = .8;
  vec2 q = (p - C)/R;
  float r = length(q);
  vec3 sh = col;
  if (r < 1.06) {
    float z = sqrt(max(1. - r*r, 0.));
    float sw = mix(.5, 2.4, u_opt);
    vec2 s = q*(1. + .35*(1. - z));
    s = swirlS(s, vec2(.45, .2), sw, .3);
    s = swirlS(s, vec2(-.45, -.3), -sw*.8, .32);
    s = swirlS(s, vec2(.15, -.62), sw*.7, .25);
    s = swirlS(s, vec2(-.2, .35), -sw*.5, .22);
    s += .22*vec2(fbm3(s*1.7 + 3.), fbm3(s*1.7 + 8.));
    float T = .55 - .45*s.y + .1*fbm3(s*vec2(1.8, 3.) + 1.);
    T = max(T, 0.);
    float ph = pow(T, 1.45)*7.5;
    float tri = abs(fract(ph*.5) - .5)*2.;
    vec3 fc = ramp(tri);
    float thin = s01(.03, .2, T);
    fc.yz *= .6 + .55*exp(-T*.8);
    fc.x += .04*cos(ph*PI*2.);
    float F = .08 + .92*pow(1. - z, 2.);
    float rf = F*thin;
    float addL = .85*fc.x*rf;
    vec3 thD = vec3(col.x + addL, col.yz + fc.yz*1.35*addL/max(fc.x, .3));
    float rfl = pow(F, 1.6)*thin;
    vec3 thL = vec3(col.x - .015*rfl + .01*(1. - z), mix(col.yz, fc.yz*1.05, sat(rfl*1.5)));
    thL.x = mix(thL.x, fc.x, .25*rfl);
    vec3 th = mix(thL, thD, u_dark);
    vec2 wq = q*(1. + .35*z);
    vec2 wc = wq - vec2(-.42, .44);
    wc.x *= 1. + .5*wc.y;
    wc *= vec2(.85, 1.);
    float arch = length(vec2(wc.x, max(wc.y - .05, 0.))) - .13;
    float wsh = max(max(abs(wc.x) - .13, -.17 - wc.y), wc.y > .05 ? arch : -1.);
    float win = (1. - s01(-.01, .01, wsh))*(.45 + .55*s01(-.17, .18, wc.y))*(1. - .25*exp(-abs(wc.x)/.006));
    float bloom = exp(-max(wsh, 0.)/.03)*.25;
    float wb2 = sdBox(wq - vec2(.5, -.55), vec2(.06, .035), .02);
    win += .35*(1. - s01(-.01, .01, wb2));
    th.x += mix(.2, .34, u_dark)*win + mix(.03, .06, u_dark)*bloom;
    th.yz *= 1. - .8*sat(win);
    float rim = exp(-(1. - r)/.012)*(r < 1. ? 1. : 0.);
    th.x += mix(.06, .14, u_dark)*rim;
    th.x -= .07*(1. - u_dark)*exp(-abs(r - .985)/.012);
    col = mix(col, th, cover((r - 1.)*R));
  }
  vec2 g = (p - C - vec2(.08, -R - .02))/vec2(R*.9, .06);
  col.x += mix(.015, .04, u_dark)*exp(-dot(g, g))*s01(1.06, .8, r);
  return col;
}`,
});

SW.add({
  id: 'stardust', name: 'Stardust', tier: 'vibrant', grain: .3,
  desc: 'A glowing cloud of gas drifting across deep space, threaded with dark dust lanes, a young star at its bright heart.',
  opt: { label: 'Dust', def: .5 },
  roles: [[.45, .17, .6, .16], [.55, .2, .68, .17], [.66, .19, .76, .15], [.78, .14, .84, .12], [.9, .07, .95, .05], [.07, .03, .5, .1]],
  packs: [
    { n: 'Orion', k: 'rich', h: [285, 320, 350, 15, 330, 275] },
    { n: 'Carina', h: [262, 228, 200, 178, 200, 255] },
    { n: 'Rosette', h: [300, 330, 355, 15, 340, 300] },
    { n: 'Helix', h: [275, 240, 200, 170, 150, 250] },
    { n: 'Pillar', k: 'quiet', h: [270, 295, 320, 345, 300, 270], c: .45 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 10 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.025, c: 1.12, h: 14 },
    night: { l: -.1, c: .82, h: -8 },
  },
  persp: { def: 1, depth: .35 },
  focus: { def: 0 },
  src: `
float starN(vec2 p, float s, float dens, float sz){
  vec2 cell = floor(p/s);
  vec2 sp = (cell + .15 + .7*h22(cell))*s;
  float on = step(dens, h21(cell + 9.7));
  float r = max(sz*(.5 + 1.2*h21(cell + 3.1)), cssu()*.5);
  float d = length(p - sp);
  return on*(exp(-d*d/(r*r)) + .08*exp(-d/(r*5.)));
}
vec3 wall(vec2 p){
  vec3 col = mix(lift(u_pal[5], .015), dim(u_pal[5], .25), sat(.5 + .3*p.y - .2*p.x));
  col = mix(col, mix(col, u_pal[1], .25), (1. - u_dark)*sat(.6 - .3*p.y));
  vec2 q = rot(.42)*p;
  vec2 w = q + .55*vec2(fbm(q*.9 + 1.), fbm(q*.9 + 7.));
  vec2 w2 = w + .18*vec2(fbm3(w*1.8 + 4.), fbm3(w*1.8 + 9.));
  float band = exp(-pow((w.y + .12 + .18*sin(w.x*.8 + 1.))/.55, 2.));
  float f1 = fbm(w2*1.2 + 3.);
  float dens = band*s01(-.8, .9, f1 + .25*band)*.8 + .2*band*band;
  float wisp = pow(1. - abs(gn(w2*3.2 + 2.)), 4.)*band*.6;
  dens = sat(dens + wisp*.5);
  vec2 hc = vec2(.95, .32);
  float hr = length(p - hc);
  float core = exp(-hr*2.6);
  float tc = sat(.42 + 1.1*fbm(w*.7 + 5.) - .25*(p.x - hc.x) + .3*core);
  vec3 gas = ramp(tc);
  float ridge = 1. - abs(gn(w2*2.4 + 11.) + .3*gn(w2*5.3));
  float dust = s01(.55, .92, ridge)*s01(.1, .7, band)*mix(.2, 1., u_opt);
  dust = sat(dust + .45*u_opt*s01(.2, .6, fbm3(w2*1.2 + 20.))*band);
  float em = dens*(1. - .85*dust);
  vec3 dk = vec3(col.x + mix(.6, 1., u_dark)*(.75*gas.x*em*em + .2*gas.x*em), col.yz + gas.yz*1.15*sat(em*1.6));
  dk.x += .35*core*core*(1. - .5*dust) + .1*core;
  vec3 lt = vec3(mix(col.x, gas.x - .08, sat(em*1.3)*.75), mix(col.yz, gas.yz*1.3, sat(em*1.8 + .1)));
  lt.x += .06*core;
  lt = mix(lt, dim(lt, .35), dust*dens*.6);
  col = mix(mix(dk, lt, .25), dk, u_dark);
  float st = starN(p, .05, .82, .0012) + .6*starN(p + 3.1, .021, .7, .0008) + 1.5*starN(p + 7.7, .19, .86, .0024);
  col.x += .7*st*(1. - .8*dust*dens)*mix(.35, 1., u_dark);
  col.x += .5*exp(-hr*hr/.0004)*u_dark + .2*exp(-hr/.04);
  return col;
}`,
});

SW.add({
  id: 'horizon', name: 'Horizon', tier: 'vibrant', grain: .28,
  desc: 'The curve of a planet seen from orbit at sunrise: a thin bright skin of atmosphere along the limb and the sun just breaking over it.',
  opt: { label: 'Atmosphere', def: .55 },
  roles: [[.62, .17, .62, .16], [.72, .16, .72, .15], [.82, .13, .84, .11], [.3, .12, .48, .14], [.95, .06, .97, .04], [.115, .03, .92, .03]],
  packs: [
    { n: 'Earthrise', k: 'rich', h: [245, 210, 75, 255, 85, 265] },
    { n: 'Mars', h: [25, 50, 80, 15, 90, 300] },
    { n: 'Neptune', h: [215, 195, 175, 240, 200, 250] },
    { n: 'Venus', h: [330, 20, 70, 300, 80, 290] },
    { n: 'Moonlit', k: 'quiet', h: [240, 220, 260, 250, 230, 260], c: .4 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 10, light: [.5, .1] },
    day: { l: 0, c: 1, h: 0, light: [.35, .3] },
    dusk: { l: -.025, c: 1.12, h: 15, light: [.6, .05] },
    night: { l: -.12, c: .82, h: -10, light: [.5, 0] },
  },
  light: { def: [.35, .3] },
  persp: { def: 1, depth: .8 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec2 C = vec2(-.35, -4.35);
  float R = 3.72;
  vec2 v = p - C;
  float r = length(v);
  float d = r - R;
  vec2 dir = v/r;
  float ang = atan(dir.x, dir.y);
  float sa = .3;
  float da = ang - sa;
  vec2 S = C + R*vec2(sin(sa), cos(sa));
  float ds = length(p - S);
  vec3 space = mix(u_pal[5], mix(dim(u_pal[5], .22), mix(u_pal[5], u_pal[3], .35), 1. - u_dark), sat(p.y*.5 + .3));
  space = mix(space, mix(u_pal[5], u_pal[0], mix(.25, .2, u_dark)), sat(.35*exp(-max(d, 0.)*2.2)));
  vec2 cell = floor(p/.04);
  vec2 sp = (cell + .2 + .6*h22(cell))*.04;
  float sz = max(.0011, cssu()*.45)*(.6 + 1.1*h21(cell + 3.1));
  float on = step(.8, h21(cell + 9.7));
  space.x += .45*on*exp(-dot(p - sp, p - sp)/(sz*sz))*u_dark*s01(.05, .5, d);
  float k = mix(.6, 1.3, u_opt);
  float warm = exp(-da*da*9.);
  vec3 airC = mix(u_pal[1], u_pal[2], warm);
  float thin = exp(-abs(d)/.012)*(d > 0. ? 1. : .4);
  float glow = exp(-max(d, 0.)/(.07 + .05*k))*(d > 0. ? 1. : 0.);
  float haze = exp(-max(d, 0.)/.32)*(d > 0. ? 1. : 0.);
  float side = exp(-da*da*1.2);
  vec3 col = space;
  col = mix(col, airC, sat(haze*.35*k*side));
  col = mix(col, lift(airC, .05), sat(glow*.85*k*(.4 + .6*side)));
  col.x += thin*.25*k*(.3 + .7*side);
  if (d < 0.) {
    float dep = -d;
    vec2 g = vec2(ang*R, dep*(6. + 30.*dep));
    float cl = s01(-.1, .5, fbm(g*vec2(3.2, 1.6) + 2.));
    float lit = s01(-.9, .25, -da*1.2 + .2) ;
    vec3 surf = mix(u_pal[3], dim(u_pal[3], .5), s01(.0, .6, dep));
    surf = mix(surf, mix(u_pal[2], lift(u_pal[3], .25), .5), cl*mix(.35, .55, 1. - u_dark)*lit*exp(-dep*1.5));
    surf.x += .12*lit*exp(-dep/.05)*side;
    surf = mix(surf, airC, .5*exp(-dep/.035)*(.4 + .6*side));
    col = mix(col, surf, cover(d));
  }
  float sun = exp(-ds*ds/.0009);
  col.x += .55*sun + .22*exp(-ds/.06) + .1*exp(-ds/.3);
  col.yz = mix(col.yz, u_pal[4].yz, sat(sun + .5*exp(-ds/.06)));
  float streak = exp(-abs(p.y - S.y)/.004)*exp(-abs(p.x - S.x)/.6);
  col.x += .12*streak*(.5 + .5*u_dark);
  return col;
}`,
});

SW.add({
  id: 'cloudsea', name: 'Cloud Sea', tier: 'vibrant', grain: .3,
  desc: 'Above a sea of cloud at sunset: every billow lit gold on the side that faces the low sun and the far banks dissolving into the glow.',
  opt: { label: 'Billows', def: .5 },
  roles: [[.64, .16, .86, .1], [.46, .15, .74, .12], [.33, .13, .62, .12], [.21, .09, .5, .1], [.9, .11, .97, .06], [.26, .1, .62, .1]],
  packs: [
    { n: 'Golden', k: 'rich', h: [55, 20, 340, 290, 85, 300] },
    { n: 'Twilight', h: [330, 300, 275, 255, 60, 270] },
    { n: 'Coral', h: [30, 5, 335, 300, 70, 310] },
    { n: 'Arctic', h: [200, 220, 245, 260, 90, 240] },
    { n: 'Pastel', k: 'quiet', h: [350, 325, 300, 275, 20, 285], c: .5 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: -10, light: [-.95, .15] },
    day: { l: 0, c: 1, h: 0, light: [-.3, .9] },
    dusk: { l: -.025, c: 1.12, h: 10, light: [.95, .1] },
    night: { l: -.14, c: .82, h: -15, light: [0, .8] },
  },
  light: { def: [-.3, .9] },
  persp: { def: 1, depth: 1 },
  focus: { def: 0 },
  src: `
float hCS(vec2 q, float t, float bl){
  float lod = exp(-t*.05);
  vec2 w = q*.42 + .35*vec2(gn(q*.11 + 3.), gn(q*.11 + 9.));
  float h = .5*abs(gn(w)) + .3*abs(gn(rot(.7)*w*2.1 + 4.3));
  h += lod*(.2*abs(gn(rot(1.4)*w*4.4 + 1.7)) + .11*abs(gn(rot(2.1)*w*9.1 + 7.1)) );
  h += .2*gn(q*.07 + 5.);
  return .34*bl*h;
}
vec3 skyCS(vec3 d, vec3 S){
  float t = sat(d.y*1.6);
  vec3 sky = mix(u_pal[0], u_pal[1], sm1(t*2.2));
  sky = mix(sky, u_pal[2], sm1(t*1.6 - .45));
  sky = mix(sky, u_pal[5], sm1(t*1.3 - .7));
  float cs = max(dot(d, S), 0.);
  sky = mix(sky, u_pal[4], .5*pow(cs, 12.) + .35*pow(cs, 90.));
  return sky;
}
vec3 wall(vec2 p){
  float bl = mix(.7, 1.3, u_opt);
  vec3 S = normalize(vec3(-.78, .09, 1.));
  vec3 d = normalize(vec3(p.x, p.y - .12, 1.7));
  vec3 col = skyCS(d, S);
  vec2 Sp = S.xy/S.z*1.7 + vec2(0., .12);
  float sd = length(p - Sp);
  col = mix(col, lift(u_pal[4], .04), 1. - s01(.05, .058, sd));
  col.x += .2*exp(-sd/.04);
  if (d.y < .01) {
    vec3 ro = vec3(0., 1., 0.);
    float top = .5*bl;
    float dy = max(-d.y, .004);
    float t = (ro.y - top)/dy;
    float tp = t;
    float hit = 0.;
    for (int i = 0; i < 44; i++) {
      if (hit < .5 && t < 90.) {
        vec3 q = ro + d*t;
        float gap = q.y - hCS(q.xz, t, bl);
        if (gap < 0.) { hit = 1.; } else { tp = t; t += clamp(gap*.75/dy, .01*t + .012, 4.); }
      }
    }
    float ta = tp, tb = t;
    for (int j = 0; j < 5; j++) {
      float tm = .5*(ta + tb);
      vec3 q = ro + d*tm;
      if (q.y < hCS(q.xz, tm, bl)) tb = tm; else ta = tm;
    }
    t = tb;
    vec3 P = ro + d*t;
    float e = .06 + .004*t;
    float h0 = hCS(P.xz, t, bl);
    float hx = hCS(P.xz + vec2(e, 0.), t, bl), hz = hCS(P.xz + vec2(0., e), t, bl);
    vec3 n = normalize(vec3(-(hx - h0)/e, 1., -(hz - h0)/e));
    float dif = sat(dot(n, normalize(vec3(S.x, .35, S.z*.6)))*.55 + .5);
    float ht = sat(h0/(.48*bl));
    float back = pow(max(dot(d, S), 0.), 4.);
    float graze = pow(1. - sat(dot(n, -d)), 2.);
    float lum = sat(.12 + .6*ht + .55*(dif - .5) + .15*back);
    vec3 c = mix(u_pal[2], u_pal[1], sm1(lum*1.6));
    c = mix(c, u_pal[0], sm1(lum*1.5 - .55));
    c = mix(c, u_pal[4], sm1(lum*1.4 - .9)*.7);
    c = mix(c, u_pal[4], sat(graze*back*1.6)*.6);
    c = mix(c, u_pal[3], sat(.55 - ht*1.3)*.5);
    c.x += .02*fbm3(P.xz*4.)*exp(-t*.1);
    vec3 hzc = skyCS(vec3(d.x, .0, d.z), S);
    float fog = 1. - exp(-t*.055);
    c = mix(c, hzc, fog*.92);
    c = mix(c, hzc, s01(.0, -.02, -d.y - .002)*0.);
    col = mix(col, c, hit*s01(.004, -.002, d.y));
    col = mix(col, hzc, (1. - hit)*s01(.004, -.002, d.y));
  }
  return col;
}`,
});

SW.add({
  id: 'aster', name: 'Aster', tier: 'vibrant', grain: .25,
  desc: 'A star-shaped flower made only of light: two rings of luminous petals opening from the corner, brightest along their rims and where they overlap.',
  opt: { label: 'Glow', def: .5 },
  roles: [[.6, .2, .66, .18], [.66, .19, .7, .17], [.72, .17, .74, .15], [.78, .15, .78, .13], [.9, .08, .9, .06], [.09, .04, .95, .02]],
  packs: [
    { n: 'Magenta', k: 'rich', h: [330, 300, 270, 355, 20, 290] },
    { n: 'Cyan', h: [210, 190, 250, 170, 220, 250] },
    { n: 'Amber', h: [45, 20, 0, 70, 85, 340] },
    { n: 'Ultraviolet', h: [285, 265, 305, 245, 320, 275] },
    { n: 'Moonflower', k: 'quiet', h: [300, 260, 230, 330, 280, 270], c: .4 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 10 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.025, c: 1.12, h: 14 },
    night: { l: -.09, c: .82, h: -10 },
  },
  persp: { def: 1, depth: .6 },
  focus: { def: 0 },
  src: `
float dkmF(){ return 1. - s01(.3, .7, u_pal[5].x); }
vec3 wall(vec2 p){
  vec2 O = vec2(1.02, -.5);
  vec3 bg = mix(u_pal[5], dim(u_pal[5], mix(.08, .3, dkmF())), sat(length(p - vec2(-1.2, .8))*.4));
  bg = mix(bg, mix(bg, u_pal[0], .35), exp(-length(p - O)*1.3)*mix(.5, 1., dkmF()));
  float Ls = 0., ws = 0.;
  vec2 ab = vec2(0.);
  float g = mix(.6, 1.3, u_opt);
  for (int k = 0; k < 16; k++) {
    float i = float(k);
    float ring = i < 9. ? 0. : 1.;
    float j = ring < .5 ? i : i - 9.;
    float n = ring < .5 ? 9. : 7.;
    float th = j/n*2.*PI + ring*.35 + .2;
    vec2 q = rot(-th)*(p - O);
    float Lp = ring < .5 ? 1.25 : .78;
    q.y -= .07*q.x*q.x*(ring < .5 ? 1. : -1.);
    float s = q.x/Lp;
    float w = (ring < .5 ? .3 : .24)*pow(sat(sin(PI*sat(s))), .75)*(1. - .25*s);
    float d = abs(q.y) - w;
    float inside = 1. - s01(-.004, .004, max(d, max(-q.x, q.x - Lp)));
    float rimd = max(w - abs(q.y), 0.);
    float rim = exp(-rimd/.012) + .35*exp(-rimd/.06);
    float vein = exp(-abs(q.y)/.004)*s01(.0, .3, s)*s01(1., .5, s)*.4;
    float I = inside*(.12 + .55*rim + vein)*(.6 + .4*s01(1., .2, s));
    vec3 c = ramp(sat(ring*.45 + .5*s + .08*sin(i*2.3)));
    Ls += I*c.x;
    ab += I*c.yz;
    ws += I;
  }
  vec2 hue = ab/max(ws, 1e-3);
  float E = 1. - exp(-ws*g*1.1);
  vec3 dk = vec3(bg.x + .78*E*(Ls/max(ws, 1e-3)), mix(bg.yz, hue*1.15, sat(E*1.6)));
  dk.yz *= 1. - .35*s01(.6, 1., E);
  vec3 lt = vec3(bg.x - .12*E + .1*s01(.5, 1., E), mix(bg.yz, hue*1.25, sat(E*1.5)));
  vec3 col = mix(lt, dk, dkmF());
  float cr = length(p - O);
  col.x += mix(.1, .4, dkmF())*exp(-cr/.05)*g;
  col.yz *= 1. - .4*exp(-cr/.04);
  return col;
}`,
});

SW.add({
  id: 'poppy', name: 'Poppy', tier: 'vibrant', grain: .3,
  desc: 'A poppy petal seen close up against the sun: thin as silk, glowing from within, crinkled and veined, its ruffled edge lit like a filament.',
  opt: { label: 'Backlight', def: .55 },
  roles: [[.34, .16, .52, .19], [.52, .21, .64, .2], [.66, .2, .74, .17], [.82, .15, .86, .11], [.26, .1, .42, .14], [.12, .04, .93, .03]],
  packs: [
    { n: 'Poppy', k: 'rich', h: [355, 20, 45, 75, 340, 300] },
    { n: 'Iris', h: [285, 300, 320, 340, 270, 250] },
    { n: 'Peony', h: [330, 345, 0, 20, 315, 300] },
    { n: 'Hydrangea', h: [240, 220, 200, 185, 260, 250] },
    { n: 'Blush', k: 'quiet', h: [335, 350, 5, 20, 320, 300], c: .5 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 8, light: [.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [.4, .9] },
    dusk: { l: -.025, c: 1.12, h: 12, light: [-.9, .3] },
    night: { l: -.09, c: .82, h: -10, light: [0, 1] },
  },
  light: { def: [.4, .9] },
  persp: { def: 1, depth: .5 },
  focus: { def: 0 },
  src: `
float edgeP(float a, float k){ return (k < .5 ? 3.25 : 2.45) + .1*sin(a*7. + k*2.) + .05*sin(a*15. + 1. + k) + .025*sin(a*31. + k*4.); }
vec3 petal(vec2 p, vec2 B, float k, vec3 col){
  vec2 v = p - B;
  float r = length(v), a = atan(v.y, v.x);
  float er = edgeP(a, k);
  float d = r - er;
  float s = r/er;
  float cr = fbm3(vec2(a*9., r*2.2) + k*5.);
  float crk = .5 + .5*sin(a*46. + 2.5*cr + r*1.5);
  float vein = pow(.5 + .5*sin(a*150. + 3.*fbm3(vec2(a*6., r*1.4) + 3.) + k*3.), 6.);
  float bl = mix(.6, 1.3, u_opt);
  vec3 c = ramp(sat(.1 + .75*s*s + .1*cr - .25*(1. - k)));
  c = mix(u_pal[4], c, s01(.0, .55, s));
  c.x += (.04*crk - .02 + .06*cr)*(.5 + .5*s);
  c.x -= .025*vein*s01(.2, .9, s);
  c.yz *= 1. + .12*vein;
  float rim = exp(-max(-d, 0.)/.012);
  c.x += bl*(.14*rim + .08*exp(-max(-d, 0.)/.08))*s01(.5, 1., s);
  c.yz *= 1. - .25*rim;
  c = mix(c, dim(c, .55), (1. - bl*.6)*s01(.6, .0, s)*.6);
  float sh = 1. - s01(-.25, .05, d - .1);
  c = mix(c, col, 0.);
  return mix(dim(col, .4*sh*(1. - u_dark*.2)), c, cover(d));
}
vec3 wall(vec2 p){
  vec3 bg = mix(u_pal[5], dim(u_pal[5], .25), sat(.5 - .3*p.x + .2*p.y));
  vec2 b1 = p - vec2(-.8, .7);
  bg = mix(bg, mix(bg, u_pal[2], .5), .6*exp(-dot(b1, b1)*3.));
  vec2 b2 = p - vec2(.3, .95);
  bg = mix(bg, mix(bg, u_pal[3], .45), .5*exp(-dot(b2, b2)*6.));
  vec3 col = bg;
  col = petal(p, vec2(-.2, -2.5), 0., col);
  col = petal(p, vec2(-1.75, -1.45), 1., col);
  return col;
}`,
});

SW.add({
  id: 'nautilus', name: 'Nautilus', tier: 'vibrant', grain: .28,
  desc: 'A nautilus shell cut through its heart: chamber after chamber spiralling inward, every wall lined with glowing mother-of-pearl.',
  opt: { label: 'Nacre', def: .55 },
  roles: [[.36, .14, .58, .15], [.5, .18, .68, .16], [.64, .17, .77, .14], [.78, .12, .86, .1], [.9, .06, .95, .04], [.12, .04, .93, .02]],
  packs: [
    { n: 'Abalone', k: 'rich', h: [255, 215, 175, 300, 330, 262] },
    { n: 'Coral', h: [350, 15, 40, 320, 60, 330] },
    { n: 'Lagoon', h: [215, 190, 165, 240, 140, 230] },
    { n: 'Amethyst', h: [290, 310, 330, 265, 350, 280] },
    { n: 'Pearl', k: 'quiet', h: [250, 280, 310, 220, 30, 260], c: .4 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 10, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .9] },
    dusk: { l: -.025, c: 1.12, h: 14, light: [.9, .3] },
    night: { l: -.09, c: .82, h: -10, light: [-.2, .95] },
  },
  light: { def: [-.4, .9] },
  persp: { def: 1, depth: .5 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec3 bg = mix(lift(u_pal[5], mix(.02, .05, u_dark)), dim(u_pal[5], mix(.1, .32, u_dark)), sat(.5 - .3*p.y + .2*p.x));
  vec2 C = vec2(1.0, -.12);
  vec2 v = rot(2.75)*(p - C);
  float r = length(v);
  float ph = atan(v.y, v.x);
  float b = .19;
  float s = log(max(r, 1e-4)/.012)/b;
  float u = (s - ph)/(2.*PI);
  float n = floor(u), f = u - n;
  float N = 4.;
  float Th = ph + 2.*PI*n;
  float shellD = (u - N)*2.*PI*b*r;
  vec3 col = bg;
  float sh = 1. - s01(-.02, .2, (u - N - .05)*2.*PI*b*length(rot(2.75)*(p - C - vec2(.05, -.08))));
  col = dim(col, mix(.08, .45, u_dark)*sh*s01(.0, .08, -shellD + .1));
  if (u < N + .02) {
    float D = mix(.42, .55, sat(n/N));
    float sp = Th + .55*sin(PI*f) + .25*f;
    float ci = floor(sp/D);
    float cf = sp/D - ci;
    float wallT = .07;
    float wlw = min(f, 1. - f);
    float septd = min(cf, 1. - cf)*D*r*.9;
    float nac = mix(.5, 1.2, u_opt);
    float t = sat(.15 + .65*sm1(sat(s/(2.*PI*N))) + .08*sin(ci*1.7));
    vec3 c = ramp(t);
    c = vec3(c.x*(.62 + .38*cf + .1*sin(f*PI)), c.yz);
    float ir = sin(ph*2. + f*3. + ci*.9)*.5 + .5;
    c.yz = mix(c.yz, rot(ir*1. - .5)*c.yz*1.15, .5*nac);
    c.x += .05*nac*pow(ir, 3.);
    float sept = exp(-septd/(.004 + .006*r));
    vec3 pearl = mix(u_pal[4], u_pal[3], .4 + .3*sin(ph + ci));
    c = mix(c, pearl, sat(sept*1.2));
    c.x -= .08*exp(-septd/(.03*r + .01))*(1. - sept);
    float wd = wlw*2.*PI*b*r;
    float wl2 = exp(-wd/(.006 + .012*r));
    c = mix(c, mix(pearl, u_pal[4], .5), sat(wl2*1.1));
    c.x -= .06*exp(-wd/(.04*r + .01))*(1. - wl2);
    float outS = s01(-.006, .002, shellD);
    float lip = exp(-max(-shellD, 0.)/.02);
    c = mix(c, lift(pearl, .03), lip*.8);
    col = mix(c, col, outS);
  }
  vec2 sb = (p - vec2(-.3, .7))/vec2(1.4, .6);
  col.x += .03*exp(-dot(sb, sb))*(1. - u_dark*.5);
  return col;
}`,
});

SW.add({
  id: 'pour', name: 'Pour', tier: 'vibrant', grain: .2,
  desc: 'An acrylic pour still wet on the panel: ribbons of paint dragged into long swirls, lacy cells blooming in one band and a gloss over all of it.',
  opt: { label: 'Flow', def: .5 },
  roles: [[.6, .2, .62, .2], [.5, .19, .52, .19], [.7, .16, .72, .16], [.42, .17, .44, .17], [.8, .12, .82, .12], [.11, .03, .97, .01]],
  packs: [
    { n: 'Tropical', k: 'rich', h: [200, 330, 75, 280, 40, 260] },
    { n: 'Ocean', h: [230, 200, 180, 260, 165, 245] },
    { n: 'Flamingo', h: [350, 320, 20, 300, 50, 330] },
    { n: 'Galaxy', h: [290, 260, 320, 240, 200, 275] },
    { n: 'Marble', k: 'quiet', h: [220, 300, 60, 260, 30, 250], c: .3 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 8, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.45, .85] },
    dusk: { l: -.025, c: 1.12, h: 12, light: [.9, .3] },
    night: { l: -.09, c: .82, h: -8, light: [-.2, .95] },
  },
  light: { def: [-.45, .85] },
  persp: { def: 0, depth: .2 },
  focus: { def: 0 },
  src: `
float flowP(vec2 p, out vec2 b){
  vec2 q = rot(.45)*p;
  vec2 a = vec2(fbm3(q*.55 + vec2(0., 0.)), fbm3(q*.55 + vec2(5.2, 1.3)));
  b = vec2(fbm3(q*.8 + 1.6*a + vec2(1.7, 9.2)), fbm3(q*.8 + 1.6*a + vec2(8.3, 2.8)));
  float sw = mix(.45, 1., u_opt);
  return q.y*.8 + sw*(.9*a.x + .55*b.y);
}
vec3 wall(vec2 p){
  vec2 b, b1, b2;
  float f = flowP(p, b);
  float e = .003;
  float fx = flowP(p + vec2(e, 0.), b1), fy = flowP(p + vec2(0., e), b2);
  float gl = length(vec2(fx - f, fy - f))/e;
  float t = f*24. + 2.5*sin(f*4.1) + 1.2*sin(f*9.7);
  float id = floor(t);
  float fr = t - id;
  float ed = min(fr, 1. - fr)/(24.*max(gl, 1e-3));
  float h = h21(vec2(id, 3.1));
  float ground = step(.45, h21(vec2(id, 7.7)));
  vec3 pc = ramp(floor(h*4.99)/4.);
  vec3 col = mix(pc, u_pal[5], ground);
  vec3 nb = mix(ramp(floor(h21(vec2(id + 1., 3.1))*4.99)/4.), u_pal[5], step(.45, h21(vec2(id + 1., 7.7))));
  float aa = s01(-pxu(), pxu(), ed - .0015);
  float lace = exp(-ed/.004)*.5;
  vec3 col0 = col;
  col = mix(mix(col, nb, .5), col, aa);
  col.x -= .06*lace*(1. - ground);
  float cellBand = step(.8, h21(vec2(id, 11.)));
  vec2 cq = p*16. + b*8.;
  vec2 ci = floor(cq), cf = cq - ci;
  float md = 9.;
  for (int j = -1; j < 2; j++) for (int i = -1; i < 2; i++) {
    vec2 o = vec2(float(i), float(j));
    vec2 r = o + h22(ci + o)*.8 + .1 - cf;
    md = min(md, dot(r, r));
  }
  float cell = s01(.16, .08, md)*s01(.0, .04, ed)*cellBand;
  vec3 cc = mix(ramp(fract(h + .4)), u_pal[5], .35*(1. - ground));
  col = mix(col, cc, cell);
  col.x -= .08*cellBand*s01(.2, .14, md)*s01(.08, .16, md)*s01(.0, .04, ed);
  float dif = s01(.0, .05, length(col - nb));
  float rel = exp(-ed/.012)*dif;
  vec2 sb = (p - vec2(-.6, .55))/vec2(1.1, .5);
  float soft = exp(-pow(dot(sb, sb), 1.5));
  col.x += mix(.05, .09, u_dark)*soft*(.6 + .4*rel) + .03*rel*s01(-.5, .8, p.y - p.x*.3);
  col.yz *= 1. - .15*soft;
  return col;
}`,
});

SW.add({
  id: 'arcade', name: 'Arcade', tier: 'vibrant', grain: .3,
  desc: 'A run of painted arches receding into the light, each wall a deeper colour, the sun dropping arch-shaped patches of light onto the next one.',
  opt: { label: 'Sun', def: .55 },
  roles: [[.44, .17, .66, .17], [.54, .2, .72, .17], [.64, .19, .78, .15], [.4, .16, .58, .17], [.9, .08, .96, .05], [.3, .12, .5, .14]],
  packs: [
    { n: 'Muralla', k: 'rich', h: [10, 350, 25, 255, 60, 330] },
    { n: 'Majorelle', h: [262, 245, 285, 210, 80, 270] },
    { n: 'Flamingo', h: [345, 325, 5, 290, 50, 320] },
    { n: 'Lagoon', h: [195, 175, 215, 250, 90, 230] },
    { n: 'Plaster', k: 'quiet', h: [345, 325, 5, 265, 60, 310], c: .5 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 10, light: [.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [.5, .85] },
    dusk: { l: -.025, c: 1.12, h: 14, light: [-.9, .3] },
    night: { l: -.13, c: .82, h: -15, light: [.2, .9] },
  },
  light: { def: [.5, .85] },
  persp: { def: 1, depth: 1 },
  focus: { def: 0 },
  src: `
float archD(vec2 l){
  float w = .5, yb = -1.05, yc = .05;
  vec2 a = vec2(abs(l.x), l.y);
  float body = max(a.x - w, yb - l.y);
  float top = length(vec2(a.x, l.y - yc)) - w;
  return l.y > yc ? top : body;
}
vec3 wall(vec2 p){
  vec2 V = vec2(-1.36, -.08);
  float k0 = .72;
  vec2 sun = vec2(.22, -.12)*mix(.6, 1.4, u_opt);
  vec3 col = lift(u_pal[4], .02);
  float done = 0.;
  for (int k = 0; k < 11; k++) {
    float i = float(k);
    float s = pow(k0, i - 1.);
    vec2 l = (p - V)/s;
    float dOuter = i < .5 ? -1. : archD(l);
    float sN = s*k0;
    vec2 lN = (p - V)/sN;
    float dIn = archD(lN);
    float thick = .9;
    vec2 lT = (p - V)/(sN*thick);
    float dT = archD(lT);
    float onWall = (1. - done)*cover(dOuter*s)*(1. - cover(dIn*sN));
    float onRev = (1. - done)*cover(dIn*sN)*(1. - cover(dT*sN*thick));
    if (onWall + onRev > 0.) {
      float u = sat(i/9.);
      vec3 base = ramp(u*.75);
      base = mix(base, u_pal[3], .25*mod(i, 2.));
      vec2 lp = (p - V - sun*s)/s;
      float lit = cover(archD(lp)*s - .004);
      if (i < .5) lit = .3 + .7*s01(.1, -.1, p.x*.55 - p.y + .55 + .08*sin(p.y*3.))*s01(1.7, .9, p.x);
      vec3 shade = vec3(base.x*.68, base.yz*1.08);
      vec3 wc = mix(shade, vec3(base.x*1.08 + .04, base.yz*.95), lit);
      wc = mix(wc, u_pal[4], .18*u);
      wc.x += .012*fbm3(p*vec2(9., 7.)/s);
      vec2 rl = lN;
      float side = rl.x;
      float isFloor = s01(-.97, -1.02, rl.y);
      vec3 rc = mix(vec3(base.x*.82, base.yz), vec3(base.x*1.12, base.yz*.9), s01(.0, .3, -side));
      rc = mix(rc, vec3(base.x*.6, base.yz*1.05), s01(.1, .3, rl.y - .05)*.6);
      rc = mix(rc, mix(u_pal[4], base, .55), isFloor);
      col = mix(col, wc, onWall);
      col = mix(col, rc, onRev);
      done = sat(done + onWall + onRev);
    }
  }
  float gr = length((p - V - vec2(0., -.3))*vec2(1., .8));
  col.x += .05*exp(-gr*2.5)*(1. - done*.8);
  return col;
}`,
});

SW.add({
  id: 'canyon', name: 'Canyon', tier: 'vibrant', grain: .3,
  desc: 'Looking up through a slot canyon: wind-carved walls folding over one another, glowing where daylight bounces down between them.',
  opt: { label: 'Glow', def: .55 },
  roles: [[.3, .13, .48, .15], [.42, .17, .6, .17], [.56, .19, .72, .16], [.72, .16, .84, .12], [.92, .07, .96, .04], [.16, .07, .36, .1]],
  packs: [
    { n: 'Antelope', k: 'rich', h: [335, 355, 15, 40, 70, 310] },
    { n: 'Amethyst', h: [290, 305, 325, 345, 30, 280] },
    { n: 'Lagoon', h: [230, 210, 190, 175, 120, 245] },
    { n: 'Sapphire', h: [262, 248, 232, 215, 80, 265] },
    { n: 'Dusk', k: 'quiet', h: [275, 295, 320, 345, 40, 265], c: .45 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: -8, light: [-.6, .8] },
    day: { l: 0, c: 1, h: 0, light: [-.2, 1] },
    dusk: { l: -.025, c: 1.12, h: 10, light: [.6, .8] },
    night: { l: -.12, c: .8, h: -8, light: [0, 1] },
  },
  light: { def: [-.2, 1] },
  persp: { def: 1, depth: 1 },
  focus: { def: 0 },
  src: `
float cwE(float y, float i, float sd){
  return .22*sin(y*(1.3 + .25*i) + i*2.1 + sd) + .11*sin(y*(2.9 + .3*i) - i*1.3 + sd*2.) + .04*gn(vec2(y*3.5 + i*5., sd));
}
vec3 wall(vec2 p){
  vec2 G = vec2(.42, 0.);
  float gl = mix(.6, 1.35, u_opt);
  vec3 sky = mix(u_pal[4], lift(u_pal[3], .05), s01(.9, -.2, p.y));
  vec3 col = sky;
  float done = 0.;
  for (int k = 0; k < 9; k++) {
    float i = float(k);
    float u = i/8.;
    float side = mod(i, 2.) < .5 ? -1. : 1.;
    float gap = .025 + 1.05*pow(u, 1.6);
    float off = G.x + .1*sin(p.y*.8 + i)*(1. - u) + .05*u*side;
    float e = side*(p.x - off) - gap - cwE(p.y, i, side*3.)*(.35 + .9*u);
    float e2 = side*(p.x + .004 - off) - gap - cwE(p.y, i, side*3.)*(.35 + .9*u);
    float a = cover(-e*(1. + .3*u));
    if (a > 0.) {
      float dep = max(e, 0.);
      vec3 c = ramp(sat(.92 - .95*u + .1*sin(p.y*2. + i)));
      float rim = exp(-dep/(.008 + .02*u));
      float glow = exp(-dep/(.08 + .1*u))*gl;
      c = mix(mix(c, u_pal[0], .55), c, .35 + .65*sat(glow));
      c = mix(c, mix(u_pal[3], u_pal[4], .4), rim*.42*gl);
      float str = .5 + .5*sin((p.y*9. - cwE(p.y, i, side*3.)*6. + side*dep*4.)*2. + i);
      c.x += .025*str*(1. - rim) - .012;
      c = mix(c, mix(u_pal[0], u_pal[5], .5), s01(.05, .9, dep)*u*.7);
      col = mix(col, c, a);
    }
  }
  float beam = exp(-pow((p.x - G.x - .05*p.y)/.06, 2.))*s01(-1., 1., p.y);
  col.x += .08*beam*gl;
  col.yz *= 1. - .2*beam;
  return col;
}`,
});

SW.add({
  id: 'ink', name: 'Ink', tier: 'vibrant', grain: .25,
  desc: 'Ink poured into still water, unfurling into veils and fine curling threads that glow where you see them edge on.',
  opt: { label: 'Billow', def: .5 },
  roles: [[.6, .2, .48, .2], [.68, .18, .56, .19], [.56, .2, .42, .19], [.72, .16, .62, .17], [.8, .12, .7, .14], [.08, .03, .975, .01]],
  packs: [
    { n: 'Indigo', k: 'rich', h: [262, 290, 320, 240, 350, 260] },
    { n: 'Carmine', h: [10, 350, 330, 30, 300, 330] },
    { n: 'Teal', h: [200, 175, 225, 160, 250, 220] },
    { n: 'Violet', h: [300, 325, 280, 345, 260, 285] },
    { n: 'Sumi', k: 'quiet', h: [262, 280, 240, 300, 220, 260], c: .35 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 8 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.025, c: 1.12, h: 12 },
    night: { l: -.09, c: .82, h: -8 },
  },
  persp: { def: 1, depth: .6 },
  focus: { def: 0 },
  src: `
float dkmF(){ return 1. - s01(.3, .7, u_pal[5].x); }
vec3 wall(vec2 p){
  vec3 bg = mix(u_pal[5], dim(u_pal[5], mix(.04, .3, dkmF())), sat(.5 - .3*p.y + .15*p.x));
  float bl = mix(.6, 1.4, u_opt);
  vec2 q = rot(-.35)*p;
  vec2 w = q + .55*bl*vec2(fbm3(q*.7 + 1.), fbm3(q*.7 + 6.));
  w += .18*bl*vec2(fbm3(w*1.9 + 3.), fbm3(w*1.9 + 9.));
  vec2 wp = rot(.35)*w;
  float env = exp(-pow((wp.y + .5 - .28*wp.x - .14*wp.x*wp.x + .12*sin(wp.x*1.7))/(.32 + .12*sat(wp.x + .5)), 2.))*s01(-2.1, -.9, wp.x);
  float body = s01(-.35, .55, fbm(w*.85 + 2.));
  float sh = 0., a = .7, f = 1.5;
  vec2 ww = w;
  for (int o = 0; o < 3; o++) {
    float fo = float(o);
    ww = rot(.9)*ww;
    sh += a*pow(1. - abs(gn(ww*f + fo*3.17 + vec2(.37, fo*.71))), 7.);
    a *= .5; f *= 2.1;
  }
  float D = sat(env*(.45*body + .8*sh*(.4 + .8*body)));
  float t = sat(.5 + .35*w.x + .3*fbm3(w*.6 + 8.));
  vec3 ic = ramp(t);
  vec3 lt = vec3(mix(bg.x, ic.x - .1, sat(D*1.25)), mix(bg.yz, ic.yz*1.2, sat(D*1.9)));
  vec3 dk = vec3(bg.x + 1.1*ic.x*(1. - exp(-D*2.1)), bg.yz + ic.yz*1.15*sat(D*2.1));
  return mix(lt, dk, dkmF());
}`,
});

SW.add({
  id: 'agate', name: 'Agate', tier: 'vibrant', grain: .25,
  desc: 'A polished slice of banded agate held to the light: fine translucent bands of colour following one another around a milky quartz heart.',
  opt: { label: 'Bands', def: .5 },
  roles: [[.3, .14, .5, .17], [.42, .18, .6, .18], [.54, .18, .7, .16], [.68, .15, .8, .12], [.86, .05, .94, .04], [.2, .04, .86, .03]],
  packs: [
    { n: 'Fire', k: 'rich', h: [345, 5, 25, 45, 60, 330] },
    { n: 'Blue Lace', h: [255, 235, 215, 200, 230, 262] },
    { n: 'Amethyst', h: [300, 285, 320, 270, 300, 295] },
    { n: 'Moss', h: [175, 160, 190, 145, 160, 200] },
    { n: 'Chalcedony', k: 'quiet', h: [250, 270, 230, 300, 240, 260], c: .4 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 8, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .9] },
    dusk: { l: -.025, c: 1.12, h: 12, light: [.9, .3] },
    night: { l: -.09, c: .82, h: -8, light: [-.2, .95] },
  },
  light: { def: [-.4, .9] },
  persp: { def: 0, depth: .3 },
  focus: { def: 0 },
  src: `
float agD(vec2 p){
  vec2 C = vec2(1.1, -.55);
  vec2 q = p - C;
  q += .2*vec2(fbm3(p*.9 + 2.), fbm3(p*.9 + 7.));
  float a = atan(q.y, q.x);
  float r = length(q*vec2(1., 1.2));
  return r*(1. + .1*sin(a*3. + 1.) + .05*sin(a*5. + 2.)) + .025*fbm3(p*4.);
}
vec3 wall(vec2 p){
  float d = agD(p);
  float K = mix(18., 30., u_opt);
  float t = d*K;
  float grp = .5 + .5*sin(d*5.3 + 1.3*fbm3(p*.7 + 9.));
  float fine = pow(.5 + .5*sin(t*2.*PI), 3.);
  float fine2 = pow(.5 + .5*sin(t*2.*PI*2.3 + 1.), 6.);
  float lines = s01(.3, 1., grp)*(.7*fine + .5*fine2);
  float wide = s01(.55, .1, grp);
  vec3 base = ramp(sat(.95 - d*.62 + .1*sin(d*3.1)));
  vec3 col = base;
  col = mix(col, mix(u_pal[4], base, .3), sat(lines)*.7);
  col = mix(col, dim(base, .25), wide*.45);
  col.x += .06*wide*(.5 + .5*sin(d*9.));
  float rind = s01(1.62, 1.8, d + .05*fbm3(p*2.));
  col = mix(col, mix(u_pal[5], u_pal[0], .25), rind*.9);
  float core = s01(.42, .22, d);
  vec3 qz = mix(u_pal[4], u_pal[3], .35);
  qz.x += .05*sin(d*60.)*core;
  col = mix(col, qz, core*.85);
  col.x += .08*exp(-pow((d - .4)/.03, 2.));
  vec2 sb = (p - vec2(-.6, .55))/vec2(1.3, .55);
  float soft = exp(-dot(sb, sb));
  col.x += .06*soft;
  col.yz *= 1. - .12*soft;
  return col;
}`,
});

SW.add({
  id: 'trails', name: 'Trails', tier: 'vibrant', grain: .3,
  desc: 'A long exposure of light swept through the dark: two bundles of fine luminous trails curving past each other, white-hot at the core.',
  opt: { label: 'Exposure', def: .5 },
  roles: [[.68, .2, .62, .2], [.74, .18, .68, .18], [.62, .2, .56, .2], [.8, .14, .72, .15], [.7, .17, .64, .17], [.08, .04, .93, .03]],
  packs: [
    { n: 'Neon', k: 'rich', h: [340, 20, 210, 250, 300, 275] },
    { n: 'Motorway', h: [25, 50, 0, 70, 350, 265] },
    { n: 'Lagoon', h: [190, 165, 230, 145, 260, 245] },
    { n: 'Synth', h: [310, 280, 330, 200, 260, 285] },
    { n: 'Ember', k: 'quiet', h: [20, 40, 350, 60, 330, 300], c: .45 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 10 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.025, c: 1.12, h: 14 },
    night: { l: -.09, c: .82, h: -10 },
  },
  persp: { def: 1, depth: .7 },
  focus: { def: 0 },
  src: `
float trY(float x, float b){ return b < .5 ? -.38 + .52*sin(.62*x - .15) + .06*sin(1.7*x + .3) : .42 + .16*sin(.95*x + 2.2) - .22*x + .04*sin(2.1*x); }
vec3 wall(vec2 p){
  vec3 bg = mix(lift(u_pal[5], mix(.02, .04, u_dark)), dim(u_pal[5], mix(.08, .3, u_dark)), sat(.5 + .3*p.y - .1*p.x));
  float ex = mix(.6, 1.4, u_opt);
  float Ls = 0., ws = 0.;
  vec2 ab = vec2(0.);
  for (int bi = 0; bi < 2; bi++) {
    float b = float(bi);
    float e = .01;
    float y0 = trY(p.x, b);
    float sl = (trY(p.x + e, b) - trY(p.x - e, b))/(2.*e);
    float nrm = 1./sqrt(1. + sl*sl);
    float spread = (b < .5 ? 1. : .7)*(.01 + .03*sat((p.x + 1.7)/3.4) + .006*sin(p.x*1.3 + b*2.));
    for (int k = 0; k < 8; k++) {
      float i = float(k);
      float off = (i - 3.5)*spread + .006*sin(p.x*3.1 + i*1.7 + b*4.);
      float d = abs(p.y - y0 - off)*nrm;
      float wdt = .0016 + .0011*h21(vec2(i, b + 3.)) + pxu()*.35;
      float tap = b < .5 ? s01(-2.1 + .1*i, -1.5 + .05*i, p.x) : s01(1.9 - .1*i, 1.2 - .05*i, p.x)*s01(-1.9, -1.1 + .08*i, p.x);
      float core = exp(-d*d/(wdt*wdt));
      float glow = exp(-d/.016)*.32 + exp(-d/.08)*.07;
      float I = (core + glow)*tap*(.6 + .4*h21(vec2(i*1.3, b)))*(1. + .08*sin(p.x*40. + i*7.)*0.);
      vec3 c = ramp(fract(b*.55 + i*.09 + .04*p.x));
      Ls += I*c.x*(1. + .5*core);
      ab += I*c.yz*(1. - .55*core);
      ws += I;
    }
  }
  vec2 hue = ab/max(ws, 1e-3);
  float E = 1. - exp(-ws*ex*.9);
  float hot = s01(.75, 1.4, ws*ex*.6);
  vec3 dk = vec3(bg.x + E*mix(.6, .98, hot), mix(bg.yz, hue*1.2, sat(E*1.5))*(1. - .7*hot));
  vec3 lt = vec3(mix(bg.x, .68 + .3*hot, sat(E*1.1)), mix(bg.yz, hue*1.35, sat(E*1.6))*(1. - .55*hot));
  return mix(lt, dk, u_dark);
}`,
});

SW.add({
  id: 'coil', name: 'Coil', tier: 'vibrant', grain: .42,
  desc: 'A thick glossy coil spiralling across the frame, each turn passing behind the last, with the studio light gliding along it.',
  opt: { label: 'Turns', def: .5 },
  roles: [[.56, .2, .62, .19], [.64, .19, .7, .17], [.72, .16, .78, .15], [.48, .18, .54, .18], [.8, .12, .85, .1], [.13, .04, .94, .02]],
  packs: [
    { n: 'Candy', k: 'rich', h: [340, 15, 50, 300, 80, 285] },
    { n: 'Ocean', h: [250, 220, 190, 275, 170, 250] },
    { n: 'Lagoon', h: [185, 170, 200, 220, 160, 230] },
    { n: 'Grape', h: [295, 315, 340, 270, 355, 280] },
    { n: 'Porcelain', k: 'quiet', h: [250, 230, 210, 280, 200, 250], c: .35 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 10, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .9] },
    dusk: { l: -.025, c: 1.12, h: 14, light: [.9, .3] },
    night: { l: -.09, c: .82, h: -10, light: [-.2, .95] },
  },
  light: { def: [-.59, .81], own: true },
  persp: { def: 1, depth: .9 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec3 bg = mix(lift(u_pal[5], mix(.025, .06, u_dark)), dim(u_pal[5], mix(.1, .32, u_dark)), sat(.5 - .3*p.y + .15*p.x));
  vec2 A = normalize(vec2(1., -.3));
  vec2 N = vec2(-A.y, A.x);
  vec2 O = vec2(.35, -.6);
  float om = mix(9., 13., u_opt);
  float R = .3, r = .085;
  vec2 v = p - O;
  float u = dot(v, A), w = dot(v, N);
  float P = 2.*PI/om;
  float kt = .95;
  float b1 = 9., b2 = 9., t1 = 0., t2 = 0.;
  float sep = .3*P;
  for (int k = 0; k < 96; k++) {
    float t = u + (float(k)/95. - .5)*P*1.8;
    vec2 dv = vec2(u, w) - vec2(t + kt*R*sin(om*t), R*cos(om*t));
    float dd = dot(dv, dv);
    if (dd < b1) {
      if (abs(t - t1) > sep) { b2 = b1; t2 = t1; }
      b1 = dd; t1 = t;
    } else if (dd < b2 && abs(t - t1) > sep) { b2 = dd; t2 = t; }
  }
  float st = P*1.8/95.;
  for (int it = 0; it < 3; it++) {
    float c1 = b1, c2 = b2, n1 = t1, n2 = t2;
    for (int k = 0; k < 7; k++) {
      float o = (float(k)/6. - .5)*st*1.2;
      float ta = t1 + o, tb = t2 + o;
      vec2 da = vec2(u, w) - vec2(ta + kt*R*sin(om*ta), R*cos(om*ta));
      vec2 db = vec2(u, w) - vec2(tb + kt*R*sin(om*tb), R*cos(om*tb));
      float ea = dot(da, da), eb = dot(db, db);
      if (ea < c1) { c1 = ea; n1 = ta; }
      if (eb < c2) { c2 = eb; n2 = tb; }
    }
    b1 = c1; b2 = c2; t1 = n1; t2 = n2; st *= .25;
  }
  float tF = sin(om*t1) >= sin(om*t2) ? t1 : t2;
  float tB = sin(om*t1) >= sin(om*t2) ? t2 : t1;
  float span = 3.5;
  vec3 col = bg;
  vec2 so = vec2(.05, -.12);
  vec2 vs = p - so - O;
  float us = dot(vs, A), wsd = dot(vs, N);
  float shd = 9., tS = us;
  for (int k = 0; k < 32; k++) {
    float t = us + (float(k)/31. - .5)*P*1.8;
    vec2 dv = vec2(us, wsd) - vec2(t + kt*R*sin(om*t), R*cos(om*t));
    float dd = dot(dv, dv);
    if (dd < shd) { shd = dd; tS = t; }
  }
  float stS = P*1.8/31.;
  for (int it = 0; it < 2; it++) {
    float bS = shd, nS = tS;
    for (int k = 0; k < 7; k++) {
      float t = tS + (float(k)/6. - .5)*stS*1.2;
      vec2 dv = vec2(us, wsd) - vec2(t + kt*R*sin(om*t), R*cos(om*t));
      float dd = dot(dv, dv);
      if (dd < bS) { bS = dd; nS = t; }
    }
    shd = bS; tS = nS; stS *= .25;
  }
  float sdd = sqrt(shd);
  col = dim(col, mix(.2, .36, u_dark)*(1. - s01(r*.4, r*3.2, sdd)) + mix(.1, .16, u_dark)*(1. - s01(r*.9, r*1.6, sdd)));
  for (int pass = 0; pass < 2; pass++) {
    float t = pass == 0 ? tB : tF;
    float ct = clamp(t, -span, span);
    vec2 c2 = vec2(ct + kt*R*sin(om*ct), R*cos(om*ct));
    vec2 dl = vec2(u, w) - c2;
    float dist = length(dl);
    float fw = pxu()*1.4 + .007*s01(.25, 1.9, abs(ct - .1));
    float a = 1. - s01(-fw, fw, dist - r);
    if (a > 0.) {
      vec2 tg = normalize(vec2(1. + kt*R*om*cos(om*ct), -R*om*sin(om*ct)));
      float s = dot(dl, vec2(-tg.y, tg.x))/r;
      float zc = sqrt(max(1. - s*s, 0.));
      vec2 nsc = vec2(-tg.y, tg.x)*s;
      vec2 nscr = A*nsc.x + N*nsc.y;
      vec3 n = normalize(vec3(nscr, zc));
      vec3 L = normalize(vec3(normalize(u_light)*.77, .64));
      float dif = sat(dot(n, L));
      vec3 H = normalize(L + vec3(0., 0., 1.));
      float sp = pow(sat(dot(n, H)), 28.);
      float depth = sin(om*ct);
      vec3 c = ramp(sat(.5 + ct/(2.*span)));
      c = vec3(c.x*(.55 + .5*dif), c.yz*(1.05 + .1*(1. - dif)));
      c.x += .17*sp + .04*pow(sat(dot(n, H)), 6.) + .05*pow(1. - zc, 3.);
      c.yz *= 1. - .35*sp;
      float bounce = sat(-dot(nscr, normalize(u_light)))*pow(1. - zc, 1.5);
      c = mix(c, mix(c, u_pal[5], .5), .25*bounce*(1. - u_dark*.5));
      c.x += .03*bounce*(1. - u_dark);
      c.x += .006*gn(p/cssu()*vec2(.5, .5));
      c = dim(c, .25*sat(-depth));
      float endc = s01(span - .02, span + .01, abs(t));
      col = mix(col, c, a*(1. - endc));
    }
  }
  return col;
}`,
});

SW.add({
  id: 'rings', name: 'Rings', tier: 'vibrant', grain: .28,
  desc: 'A ringed giant hanging in the dark, its banded clouds lit from one side and its rings sweeping in front, finely divided and shadowed by the planet.',
  opt: { label: 'Ring tilt', def: .5 },
  roles: [[.56, .13, .7, .13], [.66, .15, .78, .13], [.76, .13, .85, .1], [.84, .08, .9, .06], [.7, .1, .8, .09], [.07, .03, .94, .03]],
  packs: [
    { n: 'Saturn', k: 'rich', h: [55, 30, 75, 85, 40, 270] },
    { n: 'Ice Giant', h: [205, 185, 225, 200, 240, 250] },
    { n: 'Rose', h: [350, 15, 330, 30, 300, 290] },
    { n: 'Violet', h: [290, 265, 310, 250, 280, 275] },
    { n: 'Moon', k: 'quiet', h: [250, 230, 270, 220, 260, 255], c: .35 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 8, light: [-.95, .2] },
    day: { l: 0, c: 1, h: 0, light: [-.8, .5] },
    dusk: { l: -.025, c: 1.12, h: 12, light: [.95, .2] },
    night: { l: -.1, c: .82, h: -8, light: [-.8, .4] },
  },
  light: { def: [-.97, .24], own: true },
  persp: { def: 1, depth: 1 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec3 col = mix(lift(u_pal[5], mix(.02, .015, u_dark)), dim(u_pal[5], mix(.1, .3, u_dark)), sat(.5 + .3*p.y + .2*p.x));
  col = mix(col, mix(col, u_pal[1], .35), (1. - u_dark)*sat(.5 - .4*p.y)*.6);
  vec2 cell = floor(p/.04);
  vec2 sp = (cell + .2 + .6*h22(cell))*.04;
  float sz = max(.001, cssu()*.45)*(.6 + 1.1*h21(cell + 3.1));
  col.x += .45*step(.82, h21(cell + 9.7))*exp(-dot(p - sp, p - sp)/(sz*sz))*u_dark;
  vec2 C = vec2(1.0, .28);
  float R = .6;
  float tilt = -.3 - .12*u_opt;
  float fl = mix(.16, .3, u_opt);
  vec2 q = rot(tilt)*(p - C);
  float rho = length(q*vec2(1., 1./fl))/R;
  vec2 Ls = normalize(u_light);
  float rr = rho;
  float band = 0.;
  float rIn = 1.3, rOut = 2.35;
  float ringM = s01(rIn - .01, rIn + .03, rr)*s01(rOut + .02, rOut - .04, rr);
  float cas = 1. - .9*exp(-pow((rr - 1.86)/.025, 2.));
  float fine = .62 + .12*sin(rr*90.) + .14*sin(rr*23. + 1.) + .1*gn(vec2(rr*40., 1.));
  float dens = ringM*cas*sat(fine)*(.55 + .45*s01(rIn, 1.6, rr))*(1. - .4*s01(2.0, rOut, rr));
  vec3 ringC = mix(u_pal[3], u_pal[4], .5 + .5*sin(rr*7.));
  vec2 dp = p - C;
  float pr = length(dp);
  float along = dot(dp, Ls);
  float perp = abs(dot(dp, vec2(-Ls.y, Ls.x)));
  float pShadow = s01(R + .03, R - .03, perp)*s01(.0, -.1, along)*step(.0, q.y);
  vec3 rc = vec3(ringC.x*(1. - .8*pShadow)*(.75 + .25*u_dark) + .05, ringC.yz*(1. - .5*pShadow));
  float front = step(q.y, 0.);
  col = mix(col, rc, sat(dens*(1. - front)*1.05));
  float pa = cover(pr - R);
  if (pa > 0.) {
    vec2 pq = rot(tilt)*dp/R;
    float z = sqrt(max(1. - dot(pq, pq), 0.));
    vec3 n = vec3(dp/R, z);
    float dif = sat(dot(n, normalize(vec3(Ls, .8))));
    float lat = pq.y/max(z*.0 + 1., .01);
    float bands = .5 + .5*sin(lat*14. + 1.5*gn(vec2(pq.x*2., lat*6.)) + 2.*sin(lat*5.));
    vec3 pc = mix(u_pal[0], u_pal[2], bands);
    pc = mix(pc, u_pal[1], .4*exp(-pow(lat/.18, 2.)));
    float term = s01(-.1, .6, dif);
    vec3 night = mix(dim(u_pal[5], .1), vec3(.5, u_pal[5].yz*2.5), 1. - u_dark);
    pc = mix(night, vec3(pc.x*(.55 + .5*term), pc.yz), sm1(term));
    pc.x += .05*pow(1. - z, 3.)*term;
    float rsh = exp(-pow((q.y - fl*R*rIn*.92 - .02)/.012, 2.))*s01(-.3, .2, -q.x);
    pc = dim(pc, .35*rsh*term);
    col = mix(col, pc, pa);
  }
  col = mix(col, rc, sat(dens*front*1.1));
  col.x += .04*dens*front*s01(.0, -.4, q.x)*u_dark;
  return col;
}`,
});

SW.add({
  id: 'icecave', name: 'Ice Cave', tier: 'vibrant', grain: .28,
  desc: 'Inside a glacier cave: the ice scalloped by meltwater into shallow cups, glowing blue where daylight soaks through from the mouth of the cave.',
  opt: { label: 'Daylight', def: .55 },
  roles: [[.24, .1, .56, .12], [.38, .14, .68, .13], [.54, .15, .79, .12], [.72, .12, .88, .09], [.92, .05, .97, .03], [.12, .06, .48, .1]],
  packs: [
    { n: 'Glacier', k: 'rich', h: [262, 245, 228, 210, 195, 265] },
    { n: 'Aqua', h: [220, 205, 190, 180, 170, 230] },
    { n: 'Violet Ice', h: [290, 275, 260, 245, 230, 285] },
    { n: 'Mint', h: [200, 185, 170, 160, 150, 215] },
    { n: 'Silver', k: 'quiet', h: [250, 240, 230, 220, 210, 255], c: .4 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: 15, light: [.8, .6] },
    day: { l: 0, c: 1, h: 0, light: [.5, .85] },
    dusk: { l: -.025, c: 1.12, h: 15, light: [-.8, .6] },
    night: { l: -.12, c: .82, h: 10, light: [.3, .95] },
  },
  light: { def: [.5, .85] },
  persp: { def: 1, depth: .9 },
  focus: { def: 0 },
  src: `
vec3 cellIC(vec2 q){
  vec2 ci = floor(q), cf = q - ci;
  float d1 = 9., d2 = 9.;
  vec2 v1 = vec2(0.);
  for (int j = -1; j < 2; j++) for (int i = -1; i < 2; i++) {
    vec2 o = vec2(float(i), float(j));
    vec2 r = o + .15 + .7*h22(ci + o) - cf;
    float d = dot(r, r);
    if (d < d1) { d2 = d1; d1 = d; v1 = r; } else if (d < d2) d2 = d;
  }
  return vec3(v1, sqrt(d2) - sqrt(d1));
}
vec3 wall(vec2 p){
  vec2 M = vec2(.95, .78);
  float dm = length((p - M)*vec2(.8, 1.));
  float day = mix(.6, 1.35, u_opt);
  float z = 1.25 - .32*p.y + .1*p.x;
  vec2 q = vec2(p.x*1.1, p.y*1.5)/z*2.;
  q += .25*vec2(gn(q*.35 + 2.), gn(q*.35 + 7.));
  vec3 c0 = cellIC(q);
  vec2 rv = c0.xy;
  float cup = length(rv);
  vec3 n = normalize(vec3(-rv*.8*s01(.0, .7, c0.z*1.6), 1.));
  vec3 Ld = normalize(vec3(M - p, .6));
  float dif = sat(dot(n, Ld));
  float ridge = exp(-c0.z/.035);
  float thin = exp(-dm*1.1)*day;
  float t = sat(.15 + .45*thin + .35*dif - .15*cup + .1*fbm3(p*1.2 + 3.));
  vec3 col = ramp(t);
  col.x += .05*ridge*(.3 + thin) + .05*pow(dif, 4.);
  float strata = .5 + .5*sin((p.y*7. + .5*sin(p.x*1.3) + .6*fbm3(p*.9))*2.);
  col = mix(col, dim(col, .15), .25*strata*(1. - thin));
  col = mix(col, u_pal[5], .45*s01(.2, 1.7, dm)*(1. - .5*dif));
  col = mix(col, u_pal[4], sat(exp(-dm*dm*7.)*day*.9));
  col.x += .2*exp(-dm/.12)*day;
  return col;
}`,
});
