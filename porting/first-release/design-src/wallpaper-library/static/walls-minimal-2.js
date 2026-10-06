// Static wallpaper library, tier 3: Minimal, second set (OD-0110 second amendment). Calm, low in contrast and
// undistracting, still colourful: one quiet idea each, a real material or a true light, nothing busy in the middle.
// roles: [L dark, C dark, L light, C light] per palette slot (0..4 the ramp, 5 the ground); packs give the hues.
// Premium-option data per OPTIONS-FORMAT.md: dyn (four looks by the clock), light (own: the src reads u_light),
// persp (own: the src layers u_tilt itself), focus.
SW.add({
  id: 'plaster', name: 'Plaster', tier: 'minimal', grain: .3,
  desc: 'A wall of polished lime plaster under a raking light: broad, overlapping trowel sweeps, each with a soft lip, and a faint cloud in the tone.',
  opt: { label: 'Relief', def: .5 },
  roles: [[.27, .035, .86, .03], [.31, .04, .90, .025], [.36, .045, .95, .02], [.24, .04, .82, .035], [.33, .05, .92, .03], [.29, .035, .88, .028]],
  packs: [
    { n: 'Terracotta', k: 'rich', h: [30, 40, 55, 20, 45, 35], hl: [45, 55, 70, 35, 60, 50], c: 1.5 },
    { n: 'Sage', h: [165, 158, 150, 172, 155, 162], hl: [135, 125, 110, 145, 120, 130], c: .8 },
    { n: 'Mineral', h: [235, 228, 220, 245, 225, 232] },
    { n: 'Rose', h: [335, 342, 350, 325, 345, 338], hl: [355, 5, 20, 345, 10, 0] },
    { n: 'Chalk', k: 'quiet', h: [250, 240, 85, 255, 80, 245], hl: [80, 75, 85, 70, 80, 78], c: .45 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.07, h: -6, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.55, .8] },
    dusk: { l: -.016, c: 1.1, h: 8, light: [.9, .3] },
    night: { l: -.1, c: .82, h: -5, light: [.2, .85] },
  },
  light: { def: [-.55, .8], own: true },
  persp: { def: 0, depth: .3 },
  focus: { def: 0 },
  src: `
vec2 plH(vec2 p){
  float hs = 0., ts = 0., ws = .02;
  p += .07*vec2(gn(p*2.3 + 4.), gn(p*2.3 + 11.));
  vec2 i0 = floor(p/.4);
  for (int j = -2; j <= 2; j++) for (int k = -2; k <= 2; k++) {
    vec2 id = i0 + vec2(float(j), float(k));
    vec2 c = (id + .1 + .8*h22(id + 3.1))*.4;
    float a = (h21(id + 7.7) - .5)*1.8 + .45;
    float sz = .65 + .6*h21(id + 2.9);
    vec2 q = rot(a)*(p - c)/sz;
    q.y += .9*q.x*q.x;
    float e = length(q/vec2(.6, .2 + .1*h21(id + 4.4)));
    float edge = mix(.3, .9, s01(-.2, .2, q.y));
    float m = 1. - s01(edge, 1.04, e);
    float w = m*m*exp(5.*h21(id + 1.3));
    float sh = .5*(h21(id + 5.2) - .5) + .22*q.x - .3*q.y + .025*gn(q*vec2(3., 16.) + id);
    hs += w*sh*sz; ts += w*h21(id + 9.4); ws += w;
  }
  return vec2(hs, ts)/ws;
}
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  float lw = dot(p, L2);
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .3*lw));
  c = mix(c, u_pal[2], .5*exp(-dot(p - L2*1.6, p - L2*1.6)*.3));
  float cl = fbm(p*1.1 + .5*vec2(fbm3(p*.8 + 1.), fbm3(p*.8 + 7.)) + 3.);
  c = mix(c, u_pal[3], .3*s01(-.2, .6, cl));
  float rel = mix(.25, 1.3, u_opt);
  float e = .003;
  vec2 h = plH(p);
  vec2 gd = vec2(plH(p + vec2(e, 0.)).x - h.x, plH(p + vec2(0., e)).x - h.x)/e;
  float sl = clamp(dot(-gd, L2)*.007*rel, -.035, .035);
  c.x += sl*mix(1., .8, u_dark);
  float tn = h.y - .5;
  c = mix(c, tn > 0. ? u_pal[4] : u_pal[3], abs(tn)*.55);
  c.x += .035*tn*mix(1., .75, u_dark);
  c.x += .01*rel*s01(.3, .9, dot(-gd, L2)*.007 + .5)*s01(-.4, 1., lw);
  vec2 g = p/cssu();
  c.x += .005*gn(g*.4) + .004*(h21(floor(g*.8)) - .5);
  return c;
}`,
});

SW.add({
  id: 'felt', name: 'Felt', tier: 'minimal', grain: .2,
  desc: 'A sheet of thick wool felt lying in one soft, deep fold, its dense fibres catching a soft light along the crest.',
  opt: { label: 'Fold', def: .5 },
  roles: [[.29, .055, .80, .06], [.33, .06, .85, .055], [.40, .055, .91, .04], [.24, .06, .74, .07], [.36, .065, .88, .05], [.27, .05, .82, .055]],
  packs: [
    { n: 'Lake', k: 'rich', h: [245, 238, 230, 255, 215, 248], c: 1.35, cl: .95 },
    { n: 'Moss', h: [150, 140, 125, 160, 110, 148], hl: [140, 132, 120, 150, 110, 138], cl: .75 },
    { n: 'Coral', h: [355, 5, 15, 345, 10, 0], hl: [25, 32, 48, 15, 40, 28], cl: .8 },
    { n: 'Heather', h: [305, 295, 285, 315, 275, 300] },
    { n: 'Flannel', k: 'quiet', h: [250, 245, 240, 255, 235, 248], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.06, h: -6, light: [-.85, .4] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .9] },
    dusk: { l: -.018, c: 1.1, h: 8, light: [.85, .4] },
    night: { l: -.1, c: .84, h: -5, light: [.15, .9] },
  },
  light: { def: [-.4, .9], own: true },
  persp: { def: 1, depth: .45 },
  focus: { def: 0, own: true },
  src: `
float fH(vec2 p){
  vec2 r = rot(.42)*p;
  float y = r.y + .28 + .09*sin(r.x*1.1 + .5) + .04*sin(r.x*2.7);
  float k = mix(.3, 1., u_opt);
  float fold = .42*s01(-.5, .25, y) + .5*exp(-pow((y - .02)/.15, 2.)) - .12*exp(-pow((y + .2)/.1, 2.));
  return k*fold + .1*sin(r.x*.8 - r.y*1.1 + 1.) + .05*fbm3(p*.9 + 2.);
}
float fib(vec2 g, float a, float s){
  vec2 r = rot(a)*g;
  return exp(-abs(gn(vec2(r.x*.05, r.y*.35) + s) + .5*gn(r*.12 + s*3.))*6.);
}
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  float e = .006;
  float h = fH(p);
  vec2 gd = vec2(fH(p + vec2(e, 0.)) - h, fH(p + vec2(0., e)) - h)/e;
  vec3 n = normalize(vec3(-gd*.8, 1.));
  float dif = dot(n, normalize(vec3(L2, 1.))) - .7071;
  float lw = dot(p, L2);
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .25*lw));
  c = mix(c, u_pal[5], .3*s01(.4, 2., length(p + L2*.6)));
  c = mix(c, u_pal[3], .45*sat(-dif*2.5));
  c = mix(c, u_pal[2], .6*sat(dif*3.5));
  c.x += (dif > 0. ? .22 : .09)*dif*mix(1., .85, u_dark);
  float rim = pow(1. - n.z, 1.2)*sat(dot(normalize(n.xy + 1e-4), L2));
  c.x += .04*rim;
  vec2 g = p/cssu();
  float fz = mix(1., .3, u_focus);
  float fb = fib(g, .4, 1.) + fib(g, 2.1, 5.) + fib(g, -1.1, 9.) + fib(g*1.3, 3.3, 13.);
  float fu = .4*gn(g*.45) + .3*gn(g*1.1 + 3.) + .3*gn(g*.16 + 7.);
  float ks = mix(1., .75, u_dark)*(1. + .5*sat(dif*3.));
  c.x += fz*ks*(.05*fu + .04*(sat(fb) - .3));
  c = mix(c, u_pal[4], fz*.3*s01(.5, .95, fib(g*.8, 1.3, 21.)));
  c.x += .008*fbm3(p*5.);
  return c;
}`,
});

SW.add({
  id: 'marble', name: 'Marble', tier: 'minimal', grain: .2,
  desc: 'A honed slab of marble: clouds of mineral colour deep in the stone, one long vein crossing a corner and a few fine threads fading out.',
  opt: { label: 'Veins', def: .5 },
  roles: [[.28, .03, .94, .012], [.32, .04, .90, .02], [.36, .035, .88, .025], [.52, .03, .66, .03], [.40, .045, .80, .035], [.25, .025, .96, .008]],
  packs: [
    { n: 'Verde', k: 'rich', h: [170, 160, 150, 165, 155, 175], hl: [165, 155, 145, 160, 150, 170], c: 1.5, cl: 2.2 },
    { n: 'Calacatta', h: [255, 250, 75, 70, 80, 258], hl: [80, 75, 80, 245, 75, 85], cl: 1.6 },
    { n: 'Rosa', h: [345, 352, 0, 340, 355, 345], hl: [355, 5, 15, 350, 10, 0], cl: 2 },
    { n: 'Azul', h: [235, 228, 220, 230, 215, 238], cl: 2.2 },
    { n: 'Bardiglio', k: 'quiet', h: [250, 245, 240, 250, 235, 252], c: .45, cl: .8 },
  ],
  dyn: {
    dawn: { l: .01, c: 1.06, h: -6, light: [-.85, .35] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.015, c: 1.1, h: 8, light: [.85, .35] },
    night: { l: -.09, c: .85, h: -5, light: [.2, .85] },
  },
  persp: { def: 0, depth: .3 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec3 c = mix(u_pal[5], u_pal[0], sm1(.5 + .2*p.x - .25*p.y));
  vec2 q = p*.8;
  vec2 qw = q + .5*vec2(fbm(q*.9 + 1.), fbm(q*.9 + 5.));
  float cl = fbm(qw*1.3 + 3.);
  c = mix(c, u_pal[1], .5*s01(-.35, .55, cl));
  c = mix(c, u_pal[4], .35*s01(.05, .7, fbm(qw*2.1 + 21.)));
  float vk = mix(.35, 1.1, u_opt);
  vec2 dir = normalize(vec2(.78, .62));
  float t = dot(qw, dir);
  float f1 = t - .7 + .12*fbm3(qw*1.1 + 7.) + .01*gn(qw*7.);
  float wv = .003 + .01*s01(-.3, .5, gn(qw*2.2 + 4.));
  float v1 = exp(-abs(f1)/wv);
  c = mix(c, u_pal[2], .5*exp(-abs(f1)/.09));
  float ph = t*7. + 3.2*fbm(qw*1.1 + 13.) + .4*gn(qw*6. + 2.);
  float sv = abs(sin(ph));
  float v2 = exp(-sv/.035)*s01(-.05, .5, gn(qw*1.4 + 2.))*s01(-.9, .2, -f1);
  float ph3 = dot(qw, vec2(-.5, .87))*13. + 4.*fbm(qw*1.7 + 31.);
  float v3 = exp(-abs(sin(ph3))/.025)*s01(.15, .6, gn(qw*1.9 + 9.));
  c = mix(c, u_pal[3], sat(vk*(.7*v1 + .3*v2 + .16*v3)));
  vec2 g = p/cssu();
  c.x += .006*gn(g*.3) + .01*s01(.93, .99, h21(floor(g*.7)))*mix(1., .6, u_dark);
  c.x += .025*exp(-dot(p - vec2(-1.3, 1.), p - vec2(-1.3, 1.))*.35);
  return c;
}`,
});

SW.add({
  id: 'knit', name: 'Knit', tier: 'minimal', grain: .25,
  desc: 'Close on a soft cashmere knit lying in one loose fold: rows of plump stitches, a halo of fine fibre, the window light running along the crest.',
  opt: { label: 'Stitch', def: .35 },
  roles: [[.27, .05, .80, .06], [.33, .055, .86, .05], [.40, .05, .91, .04], [.22, .05, .72, .06], [.46, .045, .94, .03], [.24, .045, .76, .05]],
  packs: [
    { n: 'Heather', k: 'rich', h: [290, 282, 275, 300, 270, 292], c: 1.3 },
    { n: 'Oatmilk', h: [250, 255, 75, 245, 80, 250], hl: [70, 65, 75, 60, 80, 70], cl: .6 },
    { n: 'Sky', h: [235, 228, 222, 245, 220, 238] },
    { n: 'Rosehip', h: [350, 355, 5, 345, 10, 350], hl: [10, 15, 25, 0, 30, 8] },
    { n: 'Charcoal', k: 'quiet', h: [250, 248, 245, 255, 240, 250], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.06, h: -6, light: [-.9, .4] },
    day: { l: 0, c: 1, h: 0, light: [-.5, .85] },
    dusk: { l: -.018, c: 1.1, h: 8, light: [.9, .4] },
    night: { l: -.1, c: .84, h: -5, light: [.15, .9] },
  },
  light: { def: [-.5, .85], own: true },
  persp: { def: 1, depth: .4 },
  focus: { def: 0, own: true },
  src: `
float knH(vec2 p){
  vec2 r = rot(-.5)*p;
  float y = r.y + .35 + .08*sin(r.x*1.3 + .4);
  return .55*exp(-y*y/.16) + .1*sin(r.x*.9 - r.y*.7) + .04*fbm3(p*.8);
}
vec3 leg(vec2 q, vec2 c, float a){
  vec2 d = rot(a)*(q - c);
  vec2 u = d/vec2(.27, .6);
  return vec3(length(u), u);
}
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  float e = .006;
  float h = knH(p);
  vec2 gd = vec2(knH(p + vec2(e, 0.)) - h, knH(p + vec2(0., e)) - h)/e;
  float S = mix(.16, .1, u_opt);
  vec2 kp = (p + gd*.04)/vec2(S, S*.86);
  kp.x += .12*sin(kp.y*.23 + 1.);
  float fz = mix(1., .35, u_focus);
  vec3 best = vec3(9.);
  float sgn = 0.;
  vec2 cell = floor(kp);
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    vec2 id = cell + vec2(float(i), float(j));
    vec2 q = kp - id;
    float jit = .04*(h21(id) - .5);
    vec3 l1 = leg(q, vec2(.27 + jit, .6), -.42);
    vec3 l2 = leg(q, vec2(.73 + jit, .6), .42);
    if (l1.x < best.x) { best = l1; sgn = -1.; }
    if (l2.x < best.x) { best = l2; sgn = 1.; }
  }
  float yarn = 1. - s01(.8, 1.05, best.x);
  float tx = best.y;
  float tube = sqrt(sat(1. - tx*tx));
  float ply = .5 + .5*sin((best.z*3.2 + tx*1.4)*PI*2.);
  vec2 ln = rot(sgn*.42)*vec2(tx, 0.);
  float lit = dot(normalize(vec3(ln*.9 - gd*.5, tube + .2)), normalize(vec3(L2, 1.)));
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .25*dot(p, L2)));
  c = mix(c, u_pal[5], .3*s01(.4, 2., length(p + L2*.6)));
  float fold = dot(normalize(vec3(-gd*.8, 1.)), normalize(vec3(L2, 1.))) - .707;
  c = mix(c, u_pal[3], .3*sat(-fold*2.));
  c = mix(c, u_pal[2], .4*sat(fold*3.));
  c.x += .07*fold*mix(1., .85, u_dark);
  float k = mix(1., .8, u_dark)*fz;
  c.x += k*(.11*(lit - .7)*yarn - .06*(1. - yarn) - .05*pow(1. - tube, 2.)*yarn + .022*(ply - .5)*yarn*tube - .03*s01(.2, -.6, best.z)*yarn);
  vec2 g = p/cssu();
  float fib = exp(-abs(gn(vec2(g.x*.05, g.y*.4)) + .5*gn(g*.12))*6.) + exp(-abs(gn(vec2(g.y*.05, g.x*.38) + 4.))*7.);
  c = mix(c, u_pal[4], .1*sat(fib)*(.6 + .4*yarn)*(.5 + .5*fz));
  c.x += .008*gn(g*.5);
  return c;
}`,
});

SW.add({
  id: 'zellige', name: 'Zellige', tier: 'minimal', grain: .2,
  desc: 'Tall hand-cut zellige tiles in a quiet stack: each glazed face a little uneven and tilted its own way, so a window\'s soft reflection breaks across them.',
  opt: { label: 'Sheen', def: .5 },
  roles: [[.30, .05, .86, .045], [.34, .055, .88, .04], [.38, .05, .92, .03], [.27, .055, .80, .05], [.52, .035, .97, .012], [.24, .03, .82, .02]],
  packs: [
    { n: 'Teal', k: 'rich', h: [200, 195, 188, 210, 190, 205], c: 1.3 },
    { n: 'Sky', h: [235, 230, 225, 245, 230, 238] },
    { n: 'Rosewater', h: [345, 350, 358, 340, 355, 345], hl: [355, 5, 15, 345, 10, 0] },
    { n: 'Pistachio', h: [172, 166, 160, 178, 158, 172], hl: [135, 128, 120, 145, 115, 135], c: .85 },
    { n: 'Milk', k: 'quiet', h: [250, 245, 240, 255, 240, 250], hl: [85, 80, 90, 75, 85, 82], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.06, h: -6, light: [-.9, .35] },
    day: { l: 0, c: 1, h: 0, light: [-.5, .85] },
    dusk: { l: -.018, c: 1.1, h: 8, light: [.9, .35] },
    night: { l: -.1, c: .84, h: -5, light: [.15, .9] },
  },
  light: { def: [-.5, .85], own: true },
  persp: { def: 1, depth: .35 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  vec2 S = vec2(.15, .6);
  vec2 q = p/S + vec2(.06*gn(p*vec2(1.3, 2.2) + 2.), .015*gn(p*vec2(5., 1.2) + 8.));
  vec2 id = floor(q), f = q - id - .5;
  vec2 ef = (.5 - abs(f))*S;
  float ed = min(ef.x, ef.y);
  float gw = .006;
  float tile = s01(gw*.5, gw + .004, ed);
  float hv = h21(id + 3.), hv2 = h21(id + 11.);
  vec3 tc = mix(u_pal[1], u_pal[2], .3 + .45*hv);
  tc = mix(tc, u_pal[3], .4*hv2*hv2);
  float pool = 1. - s01(gw, .035, ed);
  tc = mix(tc, u_pal[3], .3*pool);
  vec2 tilt = (h22(id + 5.) - .5)*vec2(.22, .12);
  vec2 bump = .04*vec2(gn(p*vec2(9., 3.) + id), gn(p*vec2(9., 3.) + id + 4.)) + .05*sign(f)*pool;
  vec2 nxy = tilt + bump;
  vec2 rp = p - L2*.8 + nxy*1.8;
  float win = (1. - s01(-.05, .05, sdBox(rp, vec2(.45, .7), .2)))*exp(-dot(rp, rp)*.5);
  float sh = mix(.35, 1.2, u_opt);
  tc.x += .035*dot(nxy, L2);
  tc = mix(tc, u_pal[4], sat(win*.5*sh));
  tc.x += .02*sh*win;
  float lw = dot(p, L2);
  vec3 c = mix(mix(u_pal[1], u_pal[0], .5), u_pal[5], .3);
  c = mix(c, tc, tile);
  c.x += .035*(sm1(.5 + .3*lw) - .5);
  vec2 g = p/cssu();
  c.x += .004*gn(g*.5);
  return c;
}`,
});

SW.add({
  id: 'nappa', name: 'Nappa', tier: 'minimal', grain: .2,
  desc: 'Soft nappa leather with a fine pebbled grain: two gently padded panels meet in one long curved seam, topstitched in a tone of their own.',
  opt: { label: 'Grain', def: .5 },
  roles: [[.27, .05, .78, .06], [.32, .055, .84, .055], [.40, .05, .90, .04], [.22, .05, .70, .06], [.48, .03, .94, .02], [.25, .045, .76, .055]],
  packs: [
    { n: 'Navy', k: 'rich', h: [258, 252, 245, 265, 240, 258], c: 1.3 },
    { n: 'Sage', h: [165, 158, 150, 172, 150, 165], hl: [150, 142, 132, 160, 130, 150], c: .85 },
    { n: 'Rose', h: [350, 355, 5, 342, 10, 350], hl: [10, 15, 25, 0, 25, 10] },
    { n: 'Plum', h: [320, 315, 308, 328, 300, 320] },
    { n: 'Graphite', k: 'quiet', h: [250, 248, 245, 255, 240, 250], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.06, h: -6, light: [-.9, .4] },
    day: { l: 0, c: 1, h: 0, light: [-.5, .85] },
    dusk: { l: -.018, c: 1.1, h: 8, light: [.9, .4] },
    night: { l: -.1, c: .84, h: -5, light: [.15, .9] },
  },
  light: { def: [-.5, .85], own: true },
  persp: { def: 1, depth: .4 },
  focus: { def: 0, own: true },
  src: `
float npD(vec2 p){ return length((p - vec2(2.6, -1.9))*vec2(1., 1.15)) - 2.5; }
float npH(vec2 p){
  float d = npD(p);
  float puff = .12*s01(.0, .35, abs(d))*(1. - .3*s01(.4, 1.4, abs(d)));
  return puff - .05*exp(-pow(d/.025, 2.)) + .05*sin(p.x*.7 + p.y*.9 + 1.) + .02*fbm3(p*.9);
}
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  vec3 L3 = normalize(vec3(L2, 1.));
  float e = .006;
  float h = npH(p);
  vec2 gd = vec2(npH(p + vec2(e, 0.)) - h, npH(p + vec2(0., e)) - h)/e;
  vec2 g = p/cssu();
  float S = mix(9., 5.5, u_opt);
  vec2 q = g/S + .35*vec2(gn(g*.02), gn(g*.02 + 4.));
  vec2 n0 = floor(q), f = q - n0;
  float d1 = 8., d2 = 8.;
  vec2 r1 = vec2(0.);
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    vec2 o = vec2(float(i), float(j));
    vec2 rr = o + .5 + .7*(h22(n0 + o) - .5) - f;
    float d = dot(rr, rr);
    if (d < d1) { d2 = d1; d1 = d; r1 = rr; } else if (d < d2) { d2 = d; }
  }
  float ed = sqrt(d2) - sqrt(d1);
  float fz = mix(1., .3, u_focus);
  float dome = s01(0., .5, ed);
  vec2 bn = -r1*dome*fz/max(length(r1) + .25, .3);
  float stretch = 1. - .6*sat(length(gd)*1.2);
  vec3 n = normalize(vec3(-gd*.9 + bn*.75*stretch, 1.));
  float dif = dot(n, L3) - .7071;
  float lw = dot(p, L2);
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .25*lw));
  c = mix(c, u_pal[5], .3*s01(.4, 2., length(p + L2*.6)));
  float fold = dot(normalize(vec3(-gd*.9, 1.)), L3) - .7071;
  c = mix(c, u_pal[3], .5*sat(-fold*2.5));
  c = mix(c, u_pal[2], .45*sat(fold*3.));
  c.x += .1*fold*mix(1., .85, u_dark);
  c.x += .09*(dif - fold)*mix(1., .8, u_dark);
  c.x -= .018*(1. - s01(0., .1, ed))*fz*stretch;
  vec3 H = normalize(L3 + vec3(0., 0., 1.));
  float sp = pow(sat(dot(n, H)), 16.);
  c = mix(c, u_pal[4], .3*sp*s01(-.6, 1.2, lw));
  float d = npD(p);
  vec2 C = vec2(2.6, -1.9);
  vec2 dq = (p - C)*vec2(1., 1.15);
  float arc = atan(dq.y, dq.x)*2.5;
  float so = d + .045;
  float st = fract(arc/.026);
  float dash = s01(.12, .22, st)*s01(.88, .78, st);
  float stitch = dash*(1. - s01(.0025, .0045, abs(so)));

  vec3 tc = mix(u_pal[2], u_pal[4], .5);
  tc.x += .03*(.5 - abs(st - .5));
  c = dim(c, .25*(1. - s01(.0, .006, abs(so + .003)))*dash*(1. - stitch));
  c = mix(c, tc, stitch*.85*fz);
  c.x += .004*gn(g*.6);
  return c;
}`,
});

SW.add({
  id: 'hammered', name: 'Hammered', tier: 'minimal', grain: .2,
  desc: 'A pane of hammered glass in front of a soft evening window: every shallow dimple bends the colour behind it into its own small lens.',
  opt: { label: 'Dimples', def: .5 },
  roles: [[.24, .05, .86, .04], [.30, .07, .90, .05], [.40, .09, .93, .06], [.52, .09, .96, .05], [.66, .07, .985, .03], [.20, .04, .83, .035]],
  packs: [
    { n: 'Amber', k: 'rich', h: [265, 300, 340, 25, 60, 260], hl: [240, 290, 345, 30, 70, 230], c: 1.3 },
    { n: 'Lagoon', h: [240, 220, 200, 180, 150, 245] },
    { n: 'Iris', h: [265, 280, 295, 315, 340, 260] },
    { n: 'Mint', h: [235, 210, 185, 168, 150, 240], hl: [220, 195, 170, 150, 120, 225] },
    { n: 'Smoke', k: 'quiet', h: [250, 245, 240, 235, 80, 250], c: .35 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: -8, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .85] },
    dusk: { l: -.02, c: 1.12, h: 10, light: [.9, .3] },
    night: { l: -.11, c: .8, h: -6, light: [.2, .85] },
  },
  light: { def: [-.4, .85], own: true },
  persp: { def: 1, depth: .6 },
  focus: { def: 0, own: true },
  src: `
vec3 bgH(vec2 p){
  vec2 L2 = normalize(u_light);
  float sf = 1. + 2.*u_focus;
  vec3 c = mix(u_pal[5], u_pal[1], sm1(.5 + .35*p.y));
  vec2 sp = L2*vec2(.9, .55) + vec2(.25, 0.);
  float d = length((p - sp)*vec2(.8, 1.));
  c = mix(c, u_pal[2], .8*exp(-d*d*1.4/sf));
  c = mix(c, u_pal[3], .8*exp(-d*d*4./sf));
  c = mix(c, u_pal[4], .7*exp(-d*d*14./sf));
  c = mix(c, u_pal[0], .5*s01(.2, -1., p.y + .25*sin(p.x*1.3)));
  float bar = (1. - s01(.03*sf, .1*sf, abs(p.x - 1.08)))*.6 + (1. - s01(.03*sf, .1*sf, abs(p.y + .58)))*.4;
  c = dim(c, mix(.09, .22, u_dark)*sat(bar));
  c = mix(c, u_pal[3], .25*exp(-pow((p.x + 1.2)/.35, 2.)/sf));
  return c;
}
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  float S = mix(9., 5.5, u_opt);
  vec2 q = p*S;
  vec2 n0 = floor(q), f = q - n0;
  float d1 = 8., d2 = 8., ws = 0.;
  vec2 rs = vec2(0.);
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    vec2 g = vec2(float(i), float(j));
    vec2 o = .2 + .6*h22(n0 + g + 3.);
    vec2 r = g + o - f;
    float d = dot(r, r);
    float w = exp(-9.*d);
    rs += w*r; ws += w;
    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
  }
  float ed = sqrt(d2) - sqrt(d1);
  float k = (1. - u_focus*.6);
  vec2 nxy = -rs/ws*.75*k;
  vec3 c = bgH(p + nxy*.16);
  float spec = pow(sat(dot(normalize(vec3(nxy, 1.)), normalize(vec3(L2*.6, 1.)))), 40.);
  c.x += .05*spec*k;
  c.x += .02*dot(nxy, L2)*k;
  c.x += .006*(1. - s01(0., .06, ed))*k;
  vec2 g = p/cssu();
  c.x += .004*gn(g*.5);
  return c;
}`,
});

SW.add({
  id: 'fogbow', name: 'Fogbow', tier: 'minimal', grain: .3,
  desc: 'A fogbow over a still, misty sea: a broad pale arc in the drifting fog, its rim touched warm on the outside and cool within.',
  opt: { label: 'Bow', def: .5 },
  roles: [[.26, .04, .82, .04], [.32, .05, .88, .035], [.42, .06, .84, .07], [.44, .06, .90, .06], [.52, .03, .975, .012], [.22, .04, .78, .045]],
  packs: [
    { n: 'Pearl', k: 'rich', h: [250, 240, 215, 25, 250, 235], c: 1.35 },
    { n: 'Harbour', h: [220, 205, 195, 40, 210, 215] },
    { n: 'Heather', h: [285, 275, 250, 350, 280, 270] },
    { n: 'Sea Glass', h: [190, 175, 230, 30, 180, 195] },
    { n: 'Haar', k: 'quiet', h: [240, 235, 225, 30, 240, 235], c: .4 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.1, h: -8, light: [-.9, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.025, c: 1.14, h: 10, light: [.9, .25] },
    night: { l: -.12, c: .78, h: -6, light: [.2, .85] },
  },
  persp: { def: 1, depth: .6, own: true },
  focus: { def: .1 },
  src: `
vec3 wall(vec2 p){
  float hz = -.42;
  vec2 pf = p + u_tilt*.3;
  vec2 pn = p + u_tilt;
  float t = sat((pf.y - hz)/1.5);
  vec3 c = mix(u_pal[1], u_pal[0], sm1(t));
  c = mix(c, u_pal[4], .3*exp(-pow((pf.y - hz)/.3, 2.)));
  vec2 C = vec2(.15, -1.25);
  float r = length(pf - C);
  float R = 1.62;
  float k = mix(.45, 1., u_opt);
  float x = (r - R)/.17;
  float band = exp(-x*x*1.6);
  float fogm = .65 + .35*s01(-.4, .5, fbm3(pf*vec2(1.2, 2.2) + 3.));
  float fade = s01(hz - .05, hz + .5, pf.y)*(.55 + .45*s01(-1.8, .2, pf.x));
  c = mix(c, u_pal[4], .68*k*band*fogm*fade);
  c = mix(c, u_pal[3], .4*k*exp(-pow((r - R - .16)/.06, 2.))*fogm*fade);
  c = mix(c, u_pal[2], .32*k*exp(-pow((r - R + .17)/.07, 2.))*fogm*fade);
  c.x += .02*k*(1. - s01(R - .5, R - .1, r))*fade;
  float w1 = fbm(vec2(pf.x*.7, pf.y*2.6) + vec2(2., 7.));
  c = mix(c, u_pal[4], .18*s01(-.1, .7, w1)*s01(hz - .1, hz + .9, pf.y));
  if (p.y < hz) {
    float dy = hz - pn.y;
    vec3 s = mix(mix(u_pal[1], u_pal[4], .25), u_pal[5], sm1(dy/.6));
    s.x += .012*gn(vec2(pn.x*2., pn.y*60./(dy + .2)));
    s = mix(s, u_pal[4], .2*k*exp(-pow((length(vec2(pn.x, 2.*hz - pn.y) - C) - R)/.25, 2.))*exp(-dy*4.));
    c = mix(c, s, s01(0., .015, dy));
  }
  return c;
}`,
});

SW.add({
  id: 'duvet', name: 'Duvet', tier: 'minimal', grain: .25,
  desc: 'An unmade bed in the first sun: soft cotton thrown into a few broad, rounded folds, the light lying warm along each crest.',
  opt: { label: 'Folds', def: .5 },
  roles: [[.26, .035, .82, .035], [.32, .04, .89, .03], [.40, .045, .95, .02], [.22, .05, .76, .045], [.30, .035, .86, .03], [.24, .035, .80, .035]],
  packs: [
    { n: 'Morning', k: 'rich', h: [255, 265, 285, 250, 255, 255], hl: [250, 255, 70, 245, 250, 250], c: 1.4 },
    { n: 'Blush', h: [340, 348, 355, 330, 345, 338], hl: [350, 0, 20, 335, 355, 345] },
    { n: 'Sea', h: [215, 205, 195, 225, 210, 215] },
    { n: 'Sage', h: [165, 155, 140, 175, 160, 165], hl: [140, 130, 110, 150, 135, 140], c: .85 },
    { n: 'Cotton', k: 'quiet', h: [250, 248, 80, 250, 245, 250], hl: [80, 78, 85, 250, 80, 80], c: .4 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.08, h: -7, light: [-.95, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.55, .8] },
    dusk: { l: -.02, c: 1.12, h: 9, light: [.95, .3] },
    night: { l: -.11, c: .82, h: -5, light: [.15, .9] },
  },
  light: { def: [-.55, .8], own: true },
  persp: { def: 1, depth: .5 },
  focus: { def: 0 },
  src: `
float dvH(vec2 p){
  float k = mix(.4, 1., u_opt);
  vec2 d = p - vec2(2.35, 1.55);
  float r = length(d), th = atan(d.y, d.x);
  float h = .15*sin(p.x*.8 + .4) + .1*sin(p.y*1.2 - p.x*.5 + 1.);
  float amp = s01(.4, 1.3, r)*exp(-r*.32)*k;
  float th0 = -2.55, f = 0.;
  for (int i = 0; i < 5; i++) {
    float fi = float(i);
    float ti = th0 + .19*fi + .05*sin(fi*2.7) + .02*sin(r*1.7 + fi);
    float w = (.07 + .04*h21(vec2(fi, 2.)))*(.6 + .4*r);
    float s = (th - ti)*r;
    f += (.7 + .5*h21(vec2(fi, 5.)))*exp(-s*s/(w*w))*s01(.5 + .6*h21(vec2(fi, 8.)), 1.6 + .8*h21(vec2(fi, 9.)), r);
  }
  return h + amp*.75*f + .03*fbm3(p*1.6);
}
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  float e = .005;
  float h = dvH(p);
  vec2 gd = vec2(dvH(p + vec2(e, 0.)) - h, dvH(p + vec2(0., e)) - h)/e;
  vec3 n = normalize(vec3(-gd*.9, 1.));
  vec3 L3 = normalize(vec3(L2, .9));
  float dif = dot(n, L3) - dot(vec3(0., 0., 1.), L3);
  float ao = s01(-.1, .35, h);
  float lw = dot(p, L2);
  vec3 c = mix(u_pal[5], u_pal[1], sm1(.5 + .3*lw));
  c = mix(c, u_pal[3], .55*sat(-dif*2.2) + .25*(1. - ao));
  c = mix(c, u_pal[2], .55*sat(dif*2.6));
  c.x += (dif > 0. ? .14 : .1)*dif*mix(1., .85, u_dark) - .03*(1. - ao);
  c = mix(c, u_pal[4], .15*s01(.4, 2., length(p + L2*.7)));
  vec2 g = p/cssu();
  float wv = sin(g.x*1.9)*sin(g.y*1.9);
  c.x += .006*wv + .005*gn(g*.4);
  return c;
}`,
});

SW.add({
  id: 'gallery', name: 'Gallery', tier: 'minimal', grain: .3,
  desc: 'The empty corner of a gallery after hours: two plaster walls washed by soft scallops of light from the ceiling, deepening gently into the corner.',
  opt: { label: 'Lights', def: .5 },
  roles: [[.20, .03, .78, .03], [.26, .04, .85, .035], [.33, .05, .91, .035], [.43, .065, .95, .04], [.55, .07, .985, .03], [.17, .03, .72, .035]],
  packs: [
    { n: 'Warm', k: 'rich', h: [265, 280, 320, 40, 70, 260], hl: [35, 30, 40, 55, 75, 30], c: 1.25, cl: .85 },
    { n: 'Gallery Blue', h: [245, 240, 235, 225, 215, 250] },
    { n: 'Rose', h: [330, 340, 350, 5, 20, 325] },
    { n: 'Sage', h: [190, 180, 168, 150, 130, 195], hl: [140, 130, 120, 110, 100, 145], c: .9 },
    { n: 'White Cube', k: 'quiet', h: [250, 250, 250, 70, 75, 250], hl: [80, 80, 80, 80, 80, 80], c: .35 },
  ],
  dyn: {
    dawn: { l: .015, c: 1.06, h: -6 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.02, c: 1.1, h: 8 },
    night: { l: -.11, c: .82, h: -5 },
  },
  persp: { def: 1, depth: .5, own: true },
  focus: { def: 0 },
  src: `
float scal(vec2 q, float x0, float top, float sx){
  float dx = (q.x - x0)*sx;
  float edge = top - .25 - 1.5*dx*dx;
  float m = 1. - s01(-.04, .05, q.y - edge);
  float fall = exp(-max(edge - q.y, 0.)*2.1)*exp(-dx*dx*1.2);
  float hot = exp(-pow((q.y - edge + .06)/.1, 2.))*exp(-dx*dx*6.);
  return m*fall*(.75 + .25*exp(-dx*dx*3.)) + .25*hot*m;
}
vec3 wall(vec2 p){
  vec2 pn = p + u_tilt;
  float cx = .42 + u_tilt.x*.6;
  float fy = -.78 + .06*(p.x - cx)*step(p.x, cx)*-.6;
  float k = mix(.45, 1., u_opt);
  vec3 c;
  if (p.x > cx) {
    vec2 q = pn;
    c = mix(u_pal[1], u_pal[0], s01(.2, -1., q.y)*.4);
    float s = scal(q, .95, .95, 1.6) + scal(q, 1.75, .95, 1.6) + .7*scal(q, .38, .95, 1.6);
    c = mix(c, u_pal[3], sat(s*.75*k));
    c = mix(c, u_pal[4], sat(s*s*.3*k));
  } else {
    float xs = (cx - pn.x);
    vec2 q = vec2(cx - xs*1.9, pn.y);
    c = mix(u_pal[0], u_pal[5], .45 + .3*s01(0., 2., xs));
    float s = scal(vec2(xs*2.3, pn.y), .62, .98, 1.5) + scal(vec2(xs*2.3, pn.y), 2.05, 1.02, 1.3);
    c = mix(c, u_pal[2], sat(s*.6*k));
  }
  float ao = exp(-abs(p.x - cx)/.12);
  c = dim(c, .16*ao);
  c.x += .02*(1. - s01(0., .004, abs(p.x - cx)))*step(cx, p.x);
  if (pn.y < fy) {
    float dy = fy - pn.y;
    vec3 fl = mix(u_pal[1], u_pal[5], .5 + .3*s01(0., .3, dy));
    fl = dim(fl, .1*exp(-dy/.03));
    fl = mix(fl, u_pal[3], .12*k*exp(-dy*4.)*s01(cx - .2, cx + .5, p.x));
    c = mix(c, fl, s01(0., .006, dy));
  }
  vec2 g = p/cssu();
  c.x += .01*fbm3(pn*2.5) + .004*gn(g*.5);
  return c;
}`,
});

SW.add({
  id: 'brume', name: 'Brume', tier: 'minimal', grain: .35,
  desc: 'A pale winter sun seen through thick morning fog: a clean soft disc with a wide glow, and slow banks of mist drifting across it.',
  opt: { label: 'Fog', def: .5 },
  roles: [[.24, .035, .80, .035], [.30, .045, .86, .035], [.36, .05, .90, .035], [.46, .06, .94, .04], [.70, .05, .985, .02], [.21, .035, .76, .04]],
  packs: [
    { n: 'Apricot', k: 'rich', h: [270, 300, 340, 30, 50, 260], hl: [250, 300, 350, 40, 80, 240], c: 1.3 },
    { n: 'Silver', h: [240, 235, 230, 220, 225, 245], hl: [240, 235, 230, 220, 90, 245] },
    { n: 'Rose', h: [300, 320, 340, 355, 15, 290] },
    { n: 'Glacier', h: [215, 205, 195, 185, 180, 220], hl: [215, 205, 195, 185, 120, 220] },
    { n: 'Pewter', k: 'quiet', h: [245, 240, 235, 230, 240, 250], hl: [245, 240, 235, 230, 80, 250], c: .35 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -8, light: [-.6, .35] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .5] },
    dusk: { l: -.025, c: 1.15, h: 10, light: [.6, .3] },
    night: { l: -.12, c: .78, h: -6, light: [.25, .55] },
  },
  light: { def: [-.35, .5], own: true },
  persp: { def: 1, depth: .5, own: true },
  focus: { def: .1 },
  src: `
vec3 wall(vec2 p){
  vec2 S = u_light*vec2(1.1, 1.);
  vec2 pf = p + u_tilt*.2;
  float fg = mix(.6, 1.25, u_opt);
  float t = sat(.5 + .4*pf.y);
  vec3 c = mix(u_pal[5], u_pal[1], sm1(t));
  float d = length(pf - S);
  c = mix(c, u_pal[2], .7*exp(-d*d*.9));
  c = mix(c, u_pal[3], .65*exp(-d*d*4.));
  float R = .16;
  float disc = 1. - s01(R - .012, R + .012, d);
  float veil = s01(-.2, .6, fbm3(vec2(pf.x*.9, pf.y*3.) + vec2(3., 1.)));
  vec3 sunc = mix(u_pal[4], u_pal[3], .25*veil*fg);
  c = mix(c, sunc, disc*(1. - .35*veil*fg));
  c.x += .05*u_dark*disc*(1. - .5*veil*fg) + .03*u_dark*exp(-d*d*30.);
  float b1 = fbm(vec2(pf.x*.55 + .3*pf.y, pf.y*2.4) + vec2(7., 2.));
  float b2 = fbm(vec2(p.x*.4 - .2*p.y, p.y*1.7) + vec2(1., 9.) + u_tilt*2.);
  c = mix(c, mix(u_pal[2], u_pal[4], .3*exp(-d*1.5)), .3*fg*s01(-.05, .65, b1));
  c = mix(c, u_pal[1], .22*fg*s01(.0, .7, b2)*s01(.4, -.6, p.y));
  c = mix(c, u_pal[0], .3*s01(-.2, -1., p.y));
  return c;
}`,
});

SW.add({
  id: 'contrail', name: 'Contrail', tier: 'minimal', grain: .3,
  desc: 'A clear evening sky with a single contrail drawn high across it, crisp at its leading end and softening as it drifts, lit by a sun already set.',
  opt: { label: 'Trail', def: .5 },
  roles: [[.26, .06, .72, .07], [.32, .07, .80, .07], [.40, .08, .88, .06], [.50, .08, .93, .05], [.66, .07, .985, .025], [.22, .05, .66, .08]],
  packs: [
    { n: 'Evening', k: 'rich', h: [255, 270, 310, 345, 30, 250], hl: [240, 250, 330, 30, 60, 235], c: 1.3 },
    { n: 'Clear', h: [240, 232, 220, 205, 90, 248] },
    { n: 'Lilac', h: [275, 285, 300, 330, 20, 265] },
    { n: 'Mint', h: [215, 200, 180, 160, 100, 225] },
    { n: 'Pale', k: 'quiet', h: [240, 235, 230, 60, 80, 245], c: .4 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -8 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.025, c: 1.15, h: 10 },
    night: { l: -.13, c: .76, h: -6 },
  },
  persp: { def: 1, depth: .5, own: true },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec2 pn = p + u_tilt;
  float t = sat(.5 + .5*p.y);
  vec3 c = mix(u_pal[3], u_pal[1], sm1(t));
  c = mix(c, u_pal[0], sm1(t*1.3 - .3));
  c = mix(c, u_pal[2], .45*exp(-pow((p.y + 1.)/.45, 2.))*(.7 + .3*s01(1.5, -1., p.x)));
  c = mix(c, u_pal[4], .25*exp(-pow((p.y + 1.05)/.2, 2.))*s01(1.2, -1.5, p.x));
  vec2 A = vec2(-1.9, -.15), B = vec2(1.05, .62);
  vec2 ab = B - A;
  float L = length(ab);
  vec2 dir = ab/L, nr = vec2(-dir.y, dir.x);
  vec2 q = pn - A;
  float s = dot(q, dir);
  float u = sat(s/L);
  float off = dot(q, nr) - .08*sin(u*2.4) + .02*(1. - u)*gn(vec2(s*1.3, 2.));
  float age = 1. - u;
  float k = mix(.5, 1., u_opt);
  float w = .004 + .05*age*age + .012*age;
  float puff = .75 + .25*gn(vec2(s*14., 1.)) + .2*fbm3(vec2(s*5., off*20.));
  float tr = exp(-off*off/(w*w))*puff;
  float tw = exp(-pow((abs(off) - .006*(1. - age))/(w*.6), 2.))*(1. - age)*(1. - age);
  float end = s01(0., .05, s)*(1. - s01(L - .002, L, s));
  float m = sat(max(tr, tw*.8))*end*(1. - .6*age)*k;
  vec3 lc = mix(u_pal[4], u_pal[3], .5*s01(-.5, .7, p.y));
  c = mix(c, lc, sat(m*1.05));
  vec2 P = A + dir*L + nr*.08*sin(2.4);
  float pl = exp(-dot(pn - P, pn - P)/(.003*.003))*k;
  c = mix(c, u_pal[4], pl);
  return c;
}`,
});

SW.add({
  id: 'twilight', name: 'Twilight', tier: 'minimal', grain: .3,
  desc: 'Looking away from the sunset over a flat calm sea: the blue shadow of the Earth rising under a soft rose band of light, two threads of high cloud lit in it.',
  opt: { label: 'Belt', def: .5 },
  roles: [[.24, .06, .76, .06], [.30, .06, .82, .05], [.42, .08, .87, .06], [.50, .07, .92, .045], [.36, .06, .80, .05], [.20, .05, .70, .06]],
  packs: [
    { n: 'Venus', k: 'rich', h: [258, 250, 350, 320, 255, 255], c: 1.3 },
    { n: 'Peach', h: [245, 240, 25, 40, 240, 245] },
    { n: 'Violet', h: [275, 268, 320, 300, 270, 270] },
    { n: 'Sea', h: [225, 215, 340, 200, 215, 225] },
    { n: 'Dove', k: 'quiet', h: [245, 240, 350, 60, 240, 245], c: .4 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -8 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.025, c: 1.15, h: 10 },
    night: { l: -.13, c: .76, h: -6 },
  },
  persp: { def: 1, depth: .5, own: true },
  focus: { def: 0 },
  src: `
vec3 twSky(float y, float x){
  float sh = .22 + .015*sin(x*.8);
  float k = mix(.6, 1.2, u_opt);
  vec3 c = mix(u_pal[4], u_pal[0], sm1(sat((y - sh)/1.5)));
  float belt = exp(-pow((y - sh - .3)/.32, 2.));
  c = mix(c, u_pal[2], sat(.6*k*belt));
  c = mix(c, u_pal[3], .25*k*exp(-pow((y - sh - .12)/.14, 2.)));
  float es = 1. - s01(sh - .2, sh + .12, y);
  c = mix(c, u_pal[1], .8*es);
  c = mix(c, u_pal[3], .1*exp(-pow(y/.05, 2.)));
  return c;
}
vec3 wall(vec2 p){
  float hz = -.3;
  vec2 pf = p + u_tilt*.3;
  vec2 pn = p + u_tilt;
  vec3 c = twSky(pf.y - hz, pf.x);
  float k2 = mix(.6, 1.2, u_opt);
  for (int i = 0; i < 2; i++) {
    float fi = float(i);
    float yc = hz + .5 + .22*fi + .03*sin(pf.x*1.3 + fi) + .02*fbm3(vec2(pf.x*3., fi));
    float th = (.035 + .02*fi)*(.5 + .9*s01(-.3, .6, fbm3(vec2(pf.x*1.4 + fi*7., 2.))));
    float st = exp(-pow((pf.y - yc)/th, 2.))*s01(-1.9 + fi*.9, -.6 + fi*.8, pf.x)*(1. - s01(.6 + fi*.5, 1.7, pf.x));
    vec3 sc = mix(u_pal[3], u_pal[2], .5 + .3*fi);
    c = mix(c, sc, .4*k2*st*(.7 + .3*fbm3(vec2(pf.x*8., pf.y*40.))));
  }
  if (p.y < hz) {
    float dy = hz - pn.y;
    float yr = dy*1.2 + .01*gn(vec2(pn.x*4., pn.y*90./(dy + .2)));
    vec3 w = twSky(yr, pn.x);
    w = mix(w, u_pal[5], .45 + .45*s01(0., .7, dy));
    w.x += .012*gn(vec2(pn.x*3., pn.y*70./(dy + .25)));
    c = mix(c, w, s01(0., .003, dy));
  }
  return c;
}`,
});

SW.add({
  id: 'moonjar', name: 'Moon Jar', tier: 'minimal', grain: .3,
  desc: 'A round porcelain moon jar on a low plinth in soft studio light: a matte milky glaze, a faint seam where its two halves were joined, a long gentle shadow.',
  opt: { label: 'Shadow', def: .5 },
  roles: [[.25, .04, .84, .04], [.30, .045, .89, .035], [.40, .035, .92, .025], [.62, .025, .96, .014], [.33, .05, .80, .05], [.21, .04, .78, .045]],
  packs: [
    { n: 'Celadon Room', k: 'rich', h: [190, 180, 175, 90, 200, 195], hl: [180, 172, 90, 88, 185, 182], c: 1.3 },
    { n: 'Blush', h: [10, 15, 30, 80, 0, 8] },
    { n: 'Dusk Blue', h: [245, 238, 230, 90, 255, 248] },
    { n: 'Lavender', h: [290, 282, 275, 85, 300, 292] },
    { n: 'Stone', k: 'quiet', h: [250, 245, 80, 85, 245, 250], hl: [80, 78, 82, 85, 75, 80], c: .4 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.06, h: -6, light: [-.9, .45] },
    day: { l: 0, c: 1, h: 0, light: [-.6, .75] },
    dusk: { l: -.018, c: 1.1, h: 8, light: [.9, .45] },
    night: { l: -.1, c: .84, h: -5, light: [.15, .9] },
  },
  light: { def: [-.6, .75], own: true },
  persp: { def: 1, depth: .5 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  vec3 L3 = normalize(vec3(L2.x, .55, .8));
  float fl = -.52;
  vec3 c = mix(u_pal[0], u_pal[5], s01(-.9, 1., p.y));
  c = mix(c, u_pal[1], .5*exp(-dot(p - vec2(-.5, .35), p - vec2(-.5, .35))*.35));
  float R = .34;
  vec2 C = vec2(.62, fl + R*1.02);
  vec2 q = (p - C)/vec2(R*1.06, R);
  float prof = 1. + .025*sin(q.y*2.2 + .4) - .015*q.y;
  float inside = length(q)/prof;
  float body = cover((inside - 1.)*R);
  float nw = .3, ny0 = .9, ny1 = 1.03;
  float neck = (1. - s01(nw - .012, nw, abs(q.x)))*s01(ny0 - .01, ny0, q.y)*(1. - s01(ny1 - .004, ny1 + .004, q.y));
  float fw = .4, fy1 = -.97;
  float foot = (1. - s01(fw - .012, fw, abs(q.x)))*(1. - s01(fy1 - .004, fy1 + .004, -q.y + -.0 - 0.))*s01(-1.03 - .004, -1.03 + .004, q.y);
  foot = (1. - s01(fw - .012, fw, abs(q.x)))*s01(-1.035, -1.03, q.y)*(1. - s01(-.9, -.89, q.y));
  float k = mix(.3, 1.1, u_opt);
  float pd = p.y - fl;
  float sh = exp(-pow((p.x - C.x + L2.x*.5*k)/(.42 + .25*k), 2.))*exp(-max(-pd, 0.)*9.)*s01(.0, -.02, pd - .01);
  c = dim(c, mix(.1, .2, u_dark)*sh*k);
  c = dim(c, mix(.12, .2, u_dark)*exp(-pow((p.x - C.x)/(R*.55), 2.))*exp(-pow(pd/.012, 2.)));
  float z = sqrt(sat(1. - inside*inside));
  vec3 n = normalize(vec3(q.x, q.y*.95, z + .05));
  float dif = sat(dot(n, L3));
  float wrap = sat((dot(n, L3) + .35)/1.35);
  vec3 jc = mix(u_pal[4], u_pal[2], sm1(wrap));
  jc = mix(jc, u_pal[3], .55*pow(dif, 2.5));
  jc.x += .015*pow(sat(dot(reflect(-L3, n), vec3(0., 0., 1.))), 14.);
  jc.x -= .012*exp(-pow((q.y - .02*q.x)/.018, 2.))*(.4 + .6*z);
  jc = mix(jc, u_pal[5], .15*s01(-.4, -1., q.y)*(1. - dif));
  float cx = q.x/nw;
  vec3 cn = normalize(vec3(cx, .0, sqrt(sat(1. - cx*cx))));
  float cd = sat((dot(cn, L3) + .35)/1.35);
  vec3 nc = mix(u_pal[4], u_pal[2], sm1(cd));
  nc.x += .025*exp(-pow((q.y - ny1 + .012)/.008, 2.)) - .02*exp(-pow((q.y - ny1 + .035)/.012, 2.));
  float fx = q.x/fw;
  vec3 fn = normalize(vec3(fx, -.2, sqrt(sat(1. - fx*fx))));
  vec3 fc = mix(u_pal[4], u_pal[2], .7*sm1(sat((dot(fn, L3) + .35)/1.35)));
  fc = dim(fc, .12);
  vec2 g = p/cssu();
  float tex = .01*gn(g*.25) + .012*s01(.85, .97, h21(floor(g*.6)))*(h21(floor(g*.6) + 3.) - .5) + .01*fbm3(q*4.);
  jc.x += tex; nc.x += tex; fc.x += tex;
  jc.x += .03*u_dark; nc.x += .03*u_dark; fc.x += .02*u_dark;
  c = mix(c, fc, foot);
  c = mix(c, nc, neck*(1. - body*s01(ny0 + .02, ny0 - .02, q.y)));
  c = mix(c, jc, body);
  return c;
}`,
});

SW.add({
  id: 'pigment', name: 'Pigment', tier: 'minimal', grain: .3,
  desc: 'A single heap of pure pigment powder on a seamless studio sweep: perfectly matte, its slopes soft in the key light and a fine dusting spilled around its foot.',
  opt: { label: 'Dust', def: .5 },
  roles: [[.25, .04, .86, .035], [.30, .05, .90, .03], [.42, .075, .78, .085], [.31, .075, .68, .095], [.54, .065, .87, .06], [.21, .035, .82, .035]],
  packs: [
    { n: 'Ultramarine', k: 'rich', h: [255, 250, 268, 270, 262, 255], c: 1.25 },
    { n: 'Vermilion', h: [355, 5, 25, 15, 35, 350], hl: [40, 50, 30, 25, 40, 45] },
    { n: 'Malachite', h: [175, 170, 162, 168, 155, 180] },
    { n: 'Orchid', h: [320, 315, 330, 325, 340, 318] },
    { n: 'Graphite', k: 'quiet', h: [250, 245, 240, 245, 235, 250], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.06, h: -6, light: [-.9, .4] },
    day: { l: 0, c: 1, h: 0, light: [-.55, .8] },
    dusk: { l: -.018, c: 1.1, h: 8, light: [.9, .4] },
    night: { l: -.1, c: .84, h: -5, light: [.15, .9] },
  },
  light: { def: [-.55, .8], own: true },
  persp: { def: 1, depth: .6 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  vec3 c = mix(u_pal[0], u_pal[5], s01(-.9, 1., p.y));
  c = mix(c, u_pal[1], .45*exp(-dot(p - vec2(-.6, .3), p - vec2(-.6, .3))*.35));
  float x0 = .55, W = .6, H = .46, fl = -.5, E = .085;
  float dx = p.x - x0;
  float u = dx/W;
  float bz = sqrt(sat(1. - u*u));
  float base = fl - E*bz;
  float yrel = (p.y - fl)/H;
  float prof = pow(sat(1. - abs(u)), 1.2)*(1. + .035*sin(u*9. + 1.)*(1. - abs(u)));
  float pk = .035*exp(-u*u*60.) - .018*exp(-u*u*400.);
  float top = fl + H*(prof - pk) + .004*gn(vec2(p.x*45., 1.));
  float a = (1. - s01(-.002, .004, p.y - top))*s01(base - .006, base + .012, p.y)*(1. - s01(.985, 1., abs(u)));
  float dust = mix(.2, 1., u_opt);
  vec2 g = p/cssu();
  float gr = h21(floor(g*.8));
  vec2 eq = vec2(dx/(W*1.6), (p.y - fl + E*.9)/(E*2.));
  float pool = exp(-dot(eq, eq)*1.6)*(.6 + .4*gn(g*.08 + 3.));
  c = dim(c, mix(.1, .18, u_dark)*exp(-dot(vec2(dx + L2.x*.25, p.y - fl + .01)/vec2(W*1.05, E*1.2), vec2(dx + L2.x*.25, p.y - fl + .01)/vec2(W*1.05, E*1.2))*1.4));
  c = mix(c, mix(u_pal[3], u_pal[2], .4), .6*pool*dust*s01(.1, .9, gr*.5 + pool*.9));
  float r = max(1. - sat(yrel), .001);
  float sphi = clamp(dx/(W*pow(r, 1./1.2)), -1., 1.);
  float cphi = sqrt(1. - sphi*sphi);
  float al = .62;
  vec3 n = vec3(sphi*cos(al), sin(al), cphi*cos(al));
  vec3 Ld = normalize(vec3(L2.x, .55, .75));
  float dif = sat(dot(n, Ld));
  vec3 mc = mix(u_pal[3], u_pal[2], sm1(dif*1.15));
  mc = mix(mc, u_pal[4], .4*pow(dif, 4.));
  mc.x += .035*(gr - .5) + .015*gn(g*.3) + .01*fbm3(p*12.);
  mc = dim(mc, .12*s01(.08, 0., p.y - base));
  c = mix(c, mc, a);
  return c;
}`,
});

SW.add({
  id: 'lenticular', name: 'Lenticular', tier: 'minimal', grain: .3,
  desc: 'A lenticular cloud standing still over a far peak at dusk: smooth stacked lenses of vapour, their undersides catching the last warm light.',
  opt: { label: 'Layers', def: .5 },
  roles: [[.25, .06, .72, .07], [.32, .07, .80, .06], [.38, .06, .86, .05], [.58, .08, .95, .04], [.48, .09, .90, .06], [.18, .05, .58, .07]],
  packs: [
    { n: 'Dusk', k: 'rich', h: [262, 275, 290, 340, 20, 265], hl: [245, 260, 280, 345, 30, 255], c: 1.3 },
    { n: 'Glacier', h: [235, 225, 215, 205, 190, 240] },
    { n: 'Rose', h: [285, 300, 315, 350, 10, 280] },
    { n: 'Aurora', h: [225, 205, 190, 165, 150, 235] },
    { n: 'Ash', k: 'quiet', h: [245, 240, 240, 350, 30, 245], c: .4 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -8, light: [-.95, .1] },
    day: { l: 0, c: 1, h: 0, light: [-.6, .6] },
    dusk: { l: -.025, c: 1.15, h: 10, light: [.95, .05] },
    night: { l: -.13, c: .76, h: -6, light: [.2, .7] },
  },
  light: { def: [-.95, .1], own: true },
  persp: { def: 1, depth: .6, own: true },
  focus: { def: .05 },
  src: `
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  vec2 pc = p + u_tilt*.7;
  vec2 pm = p + u_tilt;
  float t = sat(.5 + .5*p.y);
  vec3 c = mix(u_pal[2], u_pal[0], sm1(t));
  c = mix(c, u_pal[4], .4*exp(-pow((p.y + 1.)/.45, 2.))*s01(1.6, -1.6, p.x*sign(-L2.x)));
  c = mix(c, u_pal[1], .3*exp(-pow((p.y + .5)/.4, 2.)));
  vec2 C = vec2(.72, .5);
  float nl = mix(2., 3.6, u_opt);
  float acc = 0.;
  vec3 cc = c;
  for (int k = 0; k < 4; k++) {
    float fk = float(k);
    float on = s01(fk - .5, fk + .3, nl);
    vec2 cen = C + vec2(.05*sin(fk*1.7), .072*fk - .1);
    vec2 rr = vec2(.7 - .12*fk + .04*sin(fk*2.3), .05 + .005*fk);
    vec2 q = (pc - cen)/rr;
    q.y += .06*q.x*q.x;
    float e = length(q);
    float body = (1. - s01(.7, 1.02, e + .04*fbm3(vec2(pc.x*3., pc.y*30.) + fk)))*on*(1. - .5*s01(.5, 1., abs(q.x)));
    float side = sat(.5 + .45*dot(vec2(q.x, q.y*.4), -L2));
    float under = s01(.3, -.9, q.y);
    vec3 lc = mix(u_pal[1], u_pal[3], .55*under + .35*(1. - side));
    lc = mix(lc, u_pal[4], .5*under*(1. - side));
    lc.x += .02*gn(vec2(pc.x*6., pc.y*80.) + fk) - .03*s01(.5, 1., q.y);
    cc = mix(cc, lc, body*.92);
    acc = max(acc, body);
  }
  c = cc;
  float ridge = -.86 + .16*exp(-pow((pm.x - .72)/.5, 2.)) + .05*fbm3(vec2(pm.x*2., 1.)) + .03*sin(pm.x*1.3);
  float mt = cover(pm.y - ridge);
  vec3 mc = mix(u_pal[5], u_pal[1], .25);
  mc = mix(mc, u_pal[3], .12*s01(ridge - .05, ridge, pm.y));
  mc = mix(mc, c, .22*s01(ridge - .3, ridge, pm.y));
  c = mix(c, mc, mt);
  return c;
}`,
});

SW.add({
  id: 'pillar', name: 'Pillar', tier: 'minimal', grain: .3,
  desc: 'A sun pillar over a frozen plain: the sun just gone, a slender column of its light standing up from the horizon through air full of ice.',
  opt: { label: 'Pillar', def: .5 },
  roles: [[.20, .05, .72, .07], [.27, .06, .80, .06], [.40, .08, .88, .06], [.58, .09, .95, .05], [.72, .07, .99, .025], [.30, .04, .88, .03]],
  packs: [
    { n: 'Solstice', k: 'rich', h: [258, 285, 330, 25, 55, 245], hl: [240, 270, 340, 35, 65, 235], c: 1.3 },
    { n: 'Rose Gold', h: [265, 300, 340, 10, 40, 255] },
    { n: 'Arctic', h: [240, 230, 215, 200, 190, 235] },
    { n: 'Violet', h: [270, 280, 300, 320, 350, 262] },
    { n: 'Pearl', k: 'quiet', h: [245, 250, 330, 40, 70, 240], c: .4 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -8, light: [-.3, .1] },
    day: { l: 0, c: 1, h: 0, light: [-.3, .5] },
    dusk: { l: -.025, c: 1.15, h: 10, light: [.3, .1] },
    night: { l: -.13, c: .76, h: -6, light: [.2, .6] },
  },
  light: { def: [-.3, .1], own: true },
  persp: { def: 1, depth: .7, own: true },
  focus: { def: .05 },
  src: `
vec3 wall(vec2 p){
  float hz = -.36;
  float sx = clamp(u_light.x, -1.2, 1.2)*1.1;
  vec2 pf = p + u_tilt*.3;
  float h = pf.y - hz;
  float t = sat(h/1.4);
  vec3 c = mix(u_pal[3], u_pal[1], sm1(t*1.5));
  c = mix(c, u_pal[0], sm1(t*1.3 - .3));
  float dx = pf.x - sx;
  c = mix(c, u_pal[2], .5*exp(-pow(h/.35, 2.))*exp(-dx*dx*.4));
  c = mix(c, u_pal[3], .5*exp(-pow(h/.12, 2.))*exp(-dx*dx*1.2));
  float k = mix(.4, 1.1, u_opt);
  float wpl = .035 + .025*sat(h);
  float col = exp(-dx*dx/(wpl*wpl))*exp(-max(h, 0.)*1.5)*s01(-.02, .06, h);
  col *= .8 + .2*gn(vec2(pf.y*6., 3.));
  c = mix(c, u_pal[4], sat(col*.75*k));
  c = mix(c, u_pal[3], .3*k*exp(-dx*dx/(.04*.04*16.))*exp(-max(h, 0.)*2.5)*s01(0., .1, h));
  if (p.y < hz) {
    vec2 pn = p + u_tilt;
    float dy = max(hz - pn.y, 1e-3);
    float z = .3/dy;
    vec3 sn = mix(mix(u_pal[5], u_pal[3], mix(.35, .12, u_dark)), mix(u_pal[1], u_pal[0], .3*u_dark), s01(0., .7, dy));
    sn = mix(sn, u_pal[3], .35*exp(-dy*6.)*exp(-pow(pn.x - sx, 2.)*.8));
    sn = mix(sn, u_pal[4], .25*k*exp(-pow((pn.x - sx)/(.05 + dy*.25), 2.))*exp(-dy*5.));
    float sg = gn(vec2(pn.x*z*1.2, z*.6)*vec2(1., 6.)) + .5*gn(vec2(pn.x*z*3., z*2.)*vec2(1., 6.));
    float fade = 1. - s01(.0, .6, pxu()*z/dy*4.);
    sn.x += .018*sg*fade;
    c = mix(c, sn, s01(0., .003, dy));
  }
  return c;
}`,
});

SW.add({
  id: 'noctilucent', name: 'Noctilucent', tier: 'minimal', grain: .3,
  desc: 'A summer night that never quite darkens: high, fine ripples of noctilucent cloud glowing pale blue above the last warm band on the horizon.',
  opt: { label: 'Glow', def: .5 },
  roles: [[.16, .05, .70, .07], [.22, .06, .78, .07], [.38, .08, .88, .06], [.55, .09, .94, .05], [.60, .08, .97, .03], [.13, .03, .62, .05]],
  packs: [
    { n: 'Midsummer', k: 'rich', h: [262, 268, 340, 25, 210, 260], hl: [245, 255, 340, 35, 205, 250], c: 1.3 },
    { n: 'Silver', h: [245, 250, 290, 330, 225, 245] },
    { n: 'Teal', h: [225, 220, 330, 15, 180, 225] },
    { n: 'Violet', h: [275, 282, 315, 350, 250, 272] },
    { n: 'Slate', k: 'quiet', h: [245, 245, 300, 30, 225, 245], c: .4 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.1, h: -8 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.025, c: 1.14, h: 10 },
    night: { l: -.12, c: .78, h: -6 },
  },
  persp: { def: 1, depth: .6, own: true },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  float hz = -.62;
  vec2 pf = p + u_tilt*.4;
  float h = pf.y - hz;
  float t = sat(h/1.6);
  vec3 c = mix(u_pal[2], u_pal[1], sm1(t*2.2));
  c = mix(c, u_pal[0], sm1(t*1.4 - .2));
  c = mix(c, u_pal[3], .55*exp(-pow(h/.16, 2.))*(.7 + .3*exp(-pow((pf.x + .4)/1.2, 2.))));
  float k = mix(.45, 1.1, u_opt);
  vec2 q = vec2(pf.x, pf.y*1.8);
  vec2 w = q + .25*vec2(fbm3(q*.8 + 2.), fbm3(q*.8 + 8.));
  float band = s01(-.05, .5, fbm(w*vec2(.6, 2.6) + 4.))*exp(-pow((pf.y - .12 + .15*sin(pf.x*.7))/.45, 2.));
  float ph = w.x*130. + w.y*50. + 16.*fbm3(w*1.6 + 2.) + 4.*gn(w*6.);
  float rip = pow(.5 + .5*sin(ph), 2.);
  float ph2 = w.x*-34. + w.y*80. + 10.*fbm3(w*1.3 + 9.);
  float rip2 = .5 + .5*sin(ph2);
  float streak = s01(-.2, .6, fbm3(vec2(w.x*1.2, w.y*14.) + 6.));
  float fine = mix(.3, 1., rip*(.6 + .4*rip2))*(.45 + .55*streak);
  float nlc = band*fine;
  c = mix(c, u_pal[4], sat(nlc*.75*k));
  c.x += .03*nlc*k*rip;
  vec2 pn = p + u_tilt;
  float tl = .016*fbm3(vec2(pn.x*9., 1.)) + .012 + .01*s01(.3, 1., fbm3(vec2(pn.x*2.5, 4.)));
  float dy = hz - pn.y;
  vec3 g = mix(u_pal[5], mix(u_pal[5], u_pal[1], .35), exp(-max(dy, 0.)*8.));
  g.x += .008*gn(vec2(pn.x*6., pn.y*30.));
  c = mix(c, g, 1. - s01(-.002, .002, pn.y - hz - tl));
  return c;
}`,
});

SW.add({
  id: 'emboss', name: 'Emboss', tier: 'minimal', grain: .25,
  desc: 'Thick cotton rag paper with one large circle blind-embossed into it: no ink, only the raised disc, pressed smooth, its soft shoulder catching a raking light.',
  opt: { label: 'Depth', def: .5 },
  roles: [[.28, .04, .90, .025], [.32, .045, .93, .02], [.36, .045, .95, .018], [.24, .045, .84, .03], [.34, .05, .88, .03], [.26, .04, .87, .028]],
  packs: [
    { n: 'Indigo', k: 'rich', h: [262, 255, 250, 268, 250, 262], c: 1.35, cl: 1.6 },
    { n: 'Blush', h: [345, 352, 0, 340, 355, 345], hl: [20, 25, 35, 10, 25, 20], cl: 1.5 },
    { n: 'Sage', h: [170, 165, 160, 178, 160, 170], hl: [140, 132, 125, 150, 130, 140], cl: 1.4 },
    { n: 'Lilac', h: [295, 288, 282, 302, 285, 295], cl: 1.5 },
    { n: 'Cotton', k: 'quiet', h: [250, 248, 245, 255, 245, 250], hl: [80, 78, 82, 75, 80, 80], c: .4, cl: .8 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.06, h: -6, light: [-.95, .3] },
    day: { l: 0, c: 1, h: 0, light: [-.6, .75] },
    dusk: { l: -.018, c: 1.1, h: 8, light: [.95, .3] },
    night: { l: -.1, c: .84, h: -5, light: [.15, .9] },
  },
  light: { def: [-.6, .75], own: true },
  persp: { def: 0, depth: .3 },
  focus: { def: 0, own: true },
  src: `
float emH(vec2 p){
  vec2 C = vec2(.78, -.18);
  float r = length(p - C);
  float R = .62;
  float sh = .03 + .04*u_focus;
  float disc = s01(R + sh, R - sh, r);
  return -mix(.3, 1.2, u_opt)*disc*.02;
}
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  float lw = dot(p, L2);
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .3*lw));
  c = mix(c, u_pal[2], .45*exp(-dot(p - L2*1.5, p - L2*1.5)*.4));
  c = mix(c, u_pal[5], .3*s01(.4, 2.2, length(p + L2*.6)));
  float e = .0025;
  float h = emH(p);
  vec2 gd = vec2(emH(p + vec2(e, 0.)) - h, emH(p + vec2(0., e)) - h)/e;
  float sl = dot(gd, L2);
  c.x += 1.1*sl*mix(1., .8, u_dark);
  c = mix(c, sl > 0. ? u_pal[2] : u_pal[3], sat(abs(sl)*9.));
  float inside = s01(-.002, -.012, h*mix(1.2, .3, u_opt)/.02*.02);
  c = mix(c, u_pal[4], .22*inside);
  vec2 g = p/cssu();
  float fz = mix(1., .4, u_focus);
  float fib = exp(-abs(gn(vec2(g.x*.02, g.y*.09)) + .4*gn(g*.05))*7.) + exp(-abs(gn(vec2(g.y*.02, g.x*.08) + 5.))*8.);
  float pulp = .5*gn(g*.12) + .3*gn(g*.35 + 3.) + .2*fbm3(p*6.);
  c.x += fz*(.01*sat(fib) + .012*pulp)*mix(1., .75, u_dark)*(1. - .55*inside);
  c.x += .004*gn(g*.8);
  return c;
}`,
});

SW.add({
  id: 'concrete', name: 'Concrete', tier: 'minimal', grain: .3,
  desc: 'A wall of fair-faced concrete in a raking light: large smooth panels, each a little different in tone and cloud, their fine joints and a few quiet form-tie holes.',
  opt: { label: 'Timber grain', def: .3 },
  roles: [[.29, .025, .84, .02], [.33, .03, .88, .018], [.37, .03, .92, .015], [.24, .03, .78, .022], [.35, .04, .86, .03], [.27, .025, .82, .02]],
  packs: [
    { n: 'Slate', k: 'rich', h: [240, 235, 230, 245, 225, 240], c: 1.6, cl: 1.8 },
    { n: 'Rose', h: [350, 355, 0, 345, 5, 350], hl: [15, 20, 25, 10, 25, 18], cl: 1.8 },
    { n: 'Sage', h: [175, 170, 165, 180, 160, 175], hl: [140, 135, 130, 150, 125, 140], c: 1.2, cl: 1.6 },
    { n: 'Dusk', h: [280, 275, 270, 290, 265, 280], cl: 1.7 },
    { n: 'Raw', k: 'quiet', h: [250, 248, 245, 255, 240, 250], hl: [85, 82, 80, 90, 75, 85], c: .6, cl: .9 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.06, h: -6, light: [-.95, .35] },
    day: { l: 0, c: 1, h: 0, light: [-.55, .8] },
    dusk: { l: -.018, c: 1.1, h: 8, light: [.95, .35] },
    night: { l: -.1, c: .84, h: -5, light: [.15, .9] },
  },
  light: { def: [-.55, .8], own: true },
  persp: { def: 0, depth: .3 },
  focus: { def: 0 },
  src: `
vec3 wall(vec2 p){
  vec2 L2 = normalize(u_light);
  float lw = dot(p, L2);
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .3*lw));
  c = mix(c, u_pal[2], .45*exp(-dot(p - L2*1.5, p - L2*1.5)*.4));
  c = mix(c, u_pal[5], .3*s01(.5, 2.2, length(p + L2*.6)));
  vec2 PS = vec2(1.2, .6);
  vec2 pq = p - vec2(-.25, -.12);
  vec2 pi = floor(pq/PS), pf = pq/PS - pi;
  float pt = h21(pi + 4.) - .5;
  c = mix(c, pt > 0. ? u_pal[4] : u_pal[3], abs(pt)*.4);
  c.x += .018*pt;
  float mott = fbm(p*1.6 + pi*3.1);
  c = mix(c, u_pal[3], .2*s01(.0, .6, mott));
  c.x -= .012*s01(.1, .7, mott);
  float edgeD = min(min(pf.x, 1. - pf.x)*PS.x, min(pf.y, 1. - pf.y)*PS.y);
  c.x -= .02*s01(.08, .0, edgeD)*(.5 + .5*gn(p*6.));
  c.x -= .018*(1. - s01(.0, .004, edgeD));
  c.x += .012*(1. - s01(.004, .01, edgeD))*s01(.0, .004, edgeD)*sat(dot(sign(pf - .5)*-1., L2));
  float k = mix(.0, 1., u_opt);
  float off = h21(vec2(floor(p.y/.1), 9.))*7.;
  float wg = gn(vec2(p.x*1.2 + off, p.y*22.)) + .5*gn(vec2(p.x*4. + off, p.y*70.));
  c.x += .01*k*wg;
  vec2 tf = (pf - vec2(.5, .5))*PS;
  tf.x = (fract(pf.x*3.) - .5)*PS.x/3.;
  tf.y = (fract(pf.y*2.) - .5)*PS.y/2.;
  float tr = length(tf);
  float hole = 1. - s01(.009, .013, tr);
  float cone = s01(.026, .01, tr)*(1. - hole);
  vec3 hc = dim(c, .14);
  c.x += .02*cone*dot(normalize(tf + 1e-4), L2);
  c = mix(c, hc, .8*hole);
  c.x -= .01*cone;
  vec2 g = p/cssu();
  c.x += .006*gn(g*.4) + .006*s01(.9, .98, h21(floor(g*.6)))*(h21(floor(g*.6) + 2.) - .5)*2.;
  float pit = s01(.96, .995, h21(floor(g/3.)));
  c.x -= .025*pit*(1. - s01(.0, .5, length(fract(g/3.) - .5)));
  return c;
}`,
});
