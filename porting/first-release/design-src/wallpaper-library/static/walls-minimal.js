// Static wallpaper library, tier 3: Minimal. Calm, low contrast, undistracting, still colourful: one or two quiet
// ideas, soft light and a real material.
SW.add({
  id: 'linen', name: 'Linen', tier: 'minimal', grain: .15,
  thumbTex: 1.35,
  desc: 'Plain-woven linen in a soft wash of colour, gently draped, every thread slightly uneven and slubbed, lit by a window you cannot see.',
  opt: { label: 'Weave', def: .45 },
  roles: [[.27, .05, .90, .03], [.24, .06, .86, .04], [.33, .05, .95, .02], [.20, .04, .80, .04], [.30, .05, .90, .03], [.22, .05, .84, .04]],
  packs: [
    { n: 'Chambray', k: 'rich', h: [250, 240, 255, 235, 250, 245], c: 1.4 },
    { n: 'Flax', h: [70, 55, 80, 50, 70, 60], c: 1.3 },
    { n: 'Sage', h: [168, 158, 172, 152, 166, 162], hl: [150, 135, 160, 130, 150, 140] },
    { n: 'Blush', h: [20, 5, 30, 0, 20, 10] },
    { n: 'Oat', k: 'quiet', h: [75, 70, 80, 65, 75, 70], c: .45 },
  ],
  src: `
float drapeL(vec2 p){ return .55*sin(p.x*1.1 + .8*sin(p.y*.9 + 1.)) + .3*sin(p.x*2.3 - p.y*.6 + 2.) + .25*fbm3(p*.7 + 3.); }
vec3 wall(vec2 p){
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .3*p.x - .3*p.y));
  c = mix(c, u_pal[2], .6*exp(-dot(p - vec2(-1.1, .9), p - vec2(-1.1, .9))*.35));
  c = mix(c, u_pal[3], .35*s01(.8, 2.4, length(p - vec2(-.4, .4))));
  float e = .01;
  float h = drapeL(p);
  vec2 gd = vec2(drapeL(p + vec2(e, 0.)) - h, drapeL(p + vec2(0., e)) - h)/e;
  c.x += .045*dot(gd, normalize(vec2(-.6, .8)));
  vec2 q = p/cssu();
  float per = mix(3.6, 6.5, u_opt);
  float tx = q.x/per + .35*gn(vec2(q.y*.006 + .29, 1.41)), ty = q.y/per + .35*gn(vec2(2.37, q.x*.006 + .61));
  float ix = floor(tx), iy = floor(ty);
  float fx = abs(fract(tx) - .5)*2., fy = abs(fract(ty) - .5)*2.;
  float twx = .5 + .42*s01(-.3, .7, gn(vec2(ix*.71 + .37, q.y*.018 + .53))), twy = .5 + .42*s01(-.3, .7, gn(vec2(q.x*.018 + .53, iy*.71 + .37)));
  float wx = 1. - s01(twx*.7, twx, fx), wy = 1. - s01(twy*.7, twy, fy);
  float over = mod(ix + iy, 2.);
  float top = mix(wy, wx, over);
  float gap = (1. - wx)*(1. - wy);
  float idv = .5*(h21(vec2(ix, 3.)) + h21(vec2(7., iy))) - .5;
  float k = mix(1., .85, u_dark);
  c.x += k*(.022*(top - .5) - .035*gap + .022*idv + .012*(twx + twy - 1.));
  c.x += .006*fbm3(p*3.);
  return c;
}`,
});

SW.add({
  id: 'washi', name: 'Washi', tier: 'minimal', grain: .2,
  thumbTex: 1.7,
  desc: 'Handmade paper with cloudy pulp, long pale fibres, a few flecks of bark and one soft fold across it.',
  opt: { label: 'Fibres', def: .5 },
  roles: [[.26, .04, .93, .025], [.23, .05, .89, .035], [.31, .04, .97, .015], [.19, .04, .84, .03], [.28, .05, .91, .03], [.21, .04, .87, .03]],
  packs: [
    { n: 'Sakura', k: 'rich', h: [10, 350, 25, 340, 10, 0], c: 1.6 },
    { n: 'Matcha', h: [170, 160, 176, 155, 166, 165], hl: [125, 115, 130, 110, 120, 118] },
    { n: 'Kumo', h: [245, 255, 240, 260, 248, 250] },
    { n: 'Kaki', h: [55, 45, 65, 40, 55, 50] },
    { n: 'Kinari', k: 'quiet', h: [80, 75, 85, 70, 80, 78], c: .5 },
  ],
  src: `
float fibreW(vec2 q, float a, float s){
  vec2 r = rot(a)*q;
  float v = gn(vec2(r.x*.01, r.y*.16) + s) + .4*gn(vec2(r.x*.03, r.y*.3) + s*2.);
  return exp(-abs(v)*10.)*s01(-.2, .6, gn(r*.008 + s*3.));
}
vec3 wall(vec2 p){
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .25*p.x - .35*p.y));
  c = mix(c, u_pal[2], .5*exp(-dot(p - vec2(-.9, .8), p - vec2(-.9, .8))*.4));
  float form = fbm(p*2.6 + 4.);
  c.x += .026*form;
  c.yz *= 1. + .12*form;
  vec2 q = p/cssu();
  float fb = fibreW(q, .3, 1.) + fibreW(q, -.9, 7.) + fibreW(q, 1.6, 13.) + .7*fibreW(q*1.6, 2.4, 21.);
  float k = mix(.5, 1.4, u_opt)*mix(1., .75, u_dark);
  c.x += .04*sat(fb)*k;
  c.x += .004*gn(q*.08);
  vec2 cl = floor(q/70.);
  vec2 cp = (cl + .2 + .6*h22(cl + 5.))*70.;
  vec2 dq = rot(h21(cl + 2.)*6.28)*(q - cp);
  float fl = exp(-dot(dq*vec2(.3, 1.2), dq*vec2(.3, 1.2))/(2. + 6.*h21(cl + 8.)));
  c = mix(c, dim(u_pal[3], .2), fl*step(.62, h21(cl + 3.3))*.4*mix(.5, 1., u_opt));
  float fd = dot(p, normalize(vec2(.94, .34))) - .35;
  float side = s01(-.005, .005, fd);
  c.x += .016*side - .008;
  c.x += .025*exp(-abs(fd)/.012)*(1. - side) - .02*exp(-abs(fd)/.035)*side;
  c = mix(c, u_pal[3], .25*s01(.9, 2.3, length(p)));
  return c;
}`,
});

SW.add({
  id: 'dawn', name: 'Dawn', tier: 'minimal', grain: .3,
  desc: 'The first light over a calm sea: one soft glow on the horizon and its reflection on the water.',
  opt: { label: 'Glow', def: .5 },
  roles: [[.20, .04, .86, .04], [.30, .06, .90, .05], [.44, .08, .94, .05], [.26, .05, .84, .045], [.70, .08, .99, .03], [.17, .04, .80, .04]],
  packs: [
    { n: 'Peach', k: 'rich', h: [270, 330, 50, 280, 70, 260], c: 1.4 },
    { n: 'Lilac', h: [265, 300, 330, 270, 20, 255] },
    { n: 'Aqua', h: [240, 210, 180, 225, 100, 245] },
    { n: 'Honey', h: [250, 40, 75, 240, 90, 250] },
    { n: 'Pearl', k: 'quiet', h: [250, 280, 60, 245, 80, 250], c: .45 },
  ],
  src: `
vec3 wall(vec2 p){
  float hz = -.22;
  float t = sat((p.y - hz)/1.2);
  vec3 c = mix(u_pal[2], u_pal[1], sm1(t*2.2));
  c = mix(c, u_pal[0], sm1(t*1.5 - .4));
  vec2 sp = vec2(.38, hz + .04);
  float g = mix(.35, .85, u_opt);
  c = mix(c, u_pal[4], g*(.55*exp(-length((p - sp)*vec2(.55, 1.))*2.6) + .35*exp(-length(p - sp)*9.)));
  if (p.y < hz) {
    float dy = hz - p.y;
    vec3 s = mix(u_pal[3], u_pal[5], sm1(dy/.8));
    s = mix(s, mix(u_pal[2], u_pal[1], .3), .45*exp(-dy*5.));
    float col_ = exp(-abs(p.x - sp.x)*(2.2 + dy*3.))*exp(-dy*2.2);
    float st = .5 + .5*gn(vec2(p.x*3., p.y*55./(dy + .3)));
    s = mix(s, u_pal[4], g*.55*col_*(.6 + .4*st));
    s.x += .006*gn(vec2(p.x*2., p.y*40./(dy + .2)));
    c = mix(c, s, cover(p.y - hz));
  }
  return c;
}`,
});

SW.add({
  id: 'arc', name: 'Arc', tier: 'minimal', grain: .25,
  desc: 'Two great arcs and a small sun, tone on tone, each laid on the one beneath with a long soft shadow.',
  opt: { label: 'Shadow', def: .5 },
  roles: [[.25, .04, .90, .035], [.30, .05, .86, .045], [.35, .055, .82, .055], [.62, .07, .74, .08], [.28, .04, .88, .04], [.22, .04, .84, .04]],
  packs: [
    { n: 'Apricot', k: 'rich', h: [25, 18, 10, 40, 20, 18], hl: [60, 50, 40, 35, 55, 50], c: 1.4 },
    { n: 'Sea', h: [215, 205, 195, 30, 210, 210] },
    { n: 'Mauve', h: [320, 330, 340, 60, 325, 325] },
    { n: 'Moss', h: [165, 170, 176, 60, 165, 165], hl: [120, 130, 140, 60, 125, 125] },
    { n: 'Stone', k: 'quiet', h: [70, 60, 50, 40, 65, 60], c: .4 },
  ],
  src: `
void disc(inout vec3 col, vec2 p, vec2 c, float r, vec3 dc, float sk){
  float d = length(p - c) - r;
  float ds = length(p - c - vec2(.06, -.09)*sk*2.) - r;
  float sh = 1. - s01(-.05, .35*sk + .05, ds);
  float a = cover(d);
  col = dim(col, mix(.1, .18, u_dark)*sh*(1. - a)*sk*1.6);
  vec3 b = dc;
  b.x += .025*dot(normalize(p - c + 1e-4), vec2(-.6, .8))*s01(-.4, 0., d);
  b.x += .012*exp(-abs(d)/.015);
  col = mix(col, b, a);
}
vec3 wall(vec2 p){
  vec3 col = mix(u_pal[0], u_pal[5], sm1(.5 - .25*p.x - .35*p.y));
  col = mix(col, u_pal[4], .4*exp(-dot(p - vec2(-1.2, .9), p - vec2(-1.2, .9))*.4));
  float sk = mix(.25, 1., u_opt);
  disc(col, p, vec2(-.78, .42), .15, u_pal[3], sk*.6);
  disc(col, p, vec2(1.05, -1.0), 1.3, u_pal[1], sk);
  disc(col, p, vec2(1.05, -1.0), .82, u_pal[2], sk);
  return col;
}`,
});

SW.add({
  id: 'mist', name: 'Mist', tier: 'minimal', grain: .3,
  desc: 'Three faint ridges dissolving into morning mist, like an ink painting washed almost to nothing.',
  opt: { label: 'Mist', def: .55 },
  roles: [[.20, .04, .92, .03], [.26, .045, .86, .04], [.30, .05, .80, .05], [.34, .05, .74, .055], [.40, .05, .95, .025], [.17, .04, .88, .035]],
  packs: [
    { n: 'Jade', k: 'rich', h: [205, 190, 180, 170, 200, 215], hl: [190, 175, 165, 155, 80, 200], c: 1.4 },
    { n: 'Wisteria', h: [280, 285, 290, 295, 60, 275] },
    { n: 'Slate', h: [240, 235, 230, 225, 80, 245] },
    { n: 'Rose', h: [10, 0, 350, 340, 60, 20] },
    { n: 'Ash', k: 'quiet', h: [220, 220, 220, 220, 80, 220], c: .4 },
  ],
  src: `
float ridgeMi(float x, float i){
  return .15 - i*.32 + .14*fbm3(vec2(x*(.55 + .2*i) + i*5.3, i*2.)) + .05*sin(x*.8 + i);
}
vec3 wall(vec2 p){
  vec3 c = mix(u_pal[4], u_pal[0], sm1((p.y + .2)/1.2));
  c = mix(c, u_pal[5], .3*s01(-.3, 1., p.y));
  float m = mix(.08, .3, u_opt);
  for (int k = 0; k < 3; k++) {
    float i = float(k);
    float y = ridgeMi(p.x, i);
    float a = cover((p.y - y)*.6);
    float dep = max(y - p.y, 0.);
    vec3 r = mix(u_pal[1], u_pal[3], i/2.);
    float fade = s01(.0, m + .1*i, dep);
    r = mix(r, mix(u_pal[4], u_pal[0], .4), fade*.85);
    c = mix(c, r, a);
  }
  return c;
}`,
});

SW.add({
  id: 'pebble', name: 'Pebble', tier: 'minimal', grain: .4,
  desc: 'Five river stones on fine sand, seen from above in soft daylight: speckled, veined and resting in their own shadows.',
  opt: { label: 'Shadow', def: .5 },
  roles: [[.36, .04, .86, .035], [.40, .045, .82, .045], [.44, .05, .79, .05], [.48, .045, .76, .045], [.52, .04, .84, .04], [.26, .035, .92, .025]],
  packs: [
    { n: 'River', k: 'rich', h: [230, 200, 60, 30, 250, 250], hl: [230, 200, 60, 30, 250, 70], c: 1.5 },
    { n: 'Sage', h: [160, 150, 176, 140, 166, 200], hl: [140, 120, 160, 100, 150, 90] },
    { n: 'Blush', h: [15, 350, 40, 330, 20, 50] },
    { n: 'Dusk', h: [270, 290, 250, 310, 280, 260] },
    { n: 'Flint', k: 'quiet', h: [60, 230, 80, 220, 70, 70], c: .45 },
  ],
  src: `
float stR(float an, float sd){ return 1. + .055*sin(an*2. + sd) + .032*sin(an*3. + sd*2.3) + .018*sin(an*5. + sd*1.7); }
void stone(inout vec3 col, vec2 p, vec2 c, vec2 rr, float a, vec3 sc, float sd, float sk){
  float m = min(rr.x, rr.y);
  vec2 q = rot(-a)*(p - c)/rr;
  float k = stR(atan(q.y, q.x), sd);
  float dn = length(q)/k;
  float d = (dn - 1.)*m;
  vec2 qs = rot(-a)*(p - c - vec2(.05, -.065)*sk)/rr;
  float ds = (length(qs)/stR(atan(qs.y, qs.x), sd) - 1.)*m;
  float soft = 1. - s01(-.07, .05 + .17*sk, ds);
  float cont = exp(-max(d, 0.)/.014);
  float aa = pxu()*1.4;
  float a2 = 1. - smoothstep(-aa, aa, d);
  col = dim(col, (mix(.16, .26, u_dark)*soft*(.45 + sk) + mix(.22, .3, u_dark)*cont)*(1. - a2));
  float z = pow(sat(1. - dn*dn), .5);
  vec3 n = normalize(vec3(rot(a)*(q/max(k, .5))*(m/rr)*1.6, z + .04));
  float kw = sat(dot(n, normalize(vec3(-.55, .62, .56)))*.62 + .38);
  kw = kw*kw*(3. - 2.*kw);
  vec3 b = vec3(sc.x*(.62 + .45*kw), sc.yz*(1. + .3*(1. - kw)));
  b.x -= .05*pow(1. - z, 3.)*(1. - kw*.6);
  b.x += .045*fbm3(q*2.3 + sd*3.) + .015*gn(q*7. + sd);
  b.yz *= .88 + .24*(.5 + .5*fbm3(q*1.5 + sd*5.));
  vec2 g = p/cssu();
  b.x += .045*s01(.62, .82, gn(g*.38 + sd*9.)) - .05*s01(.6, .85, gn(g*.31 + sd*4.));
  float vd = abs(dot(q, vec2(cos(sd*2.), sin(sd*2.))) - .25*sin(sd*3.) + .06*gn(q*3. + sd));
  b.x += .05*exp(-vd/.03)*step(.5, fract(sd*.37));
  b.x += .028*pow(sat(dot(n, normalize(vec3(-.3, .4, .87)))), 9.);
  b.x += .03*(1. - kw)*pow(1. - z, 2.);
  col = mix(col, b, a2);
}
vec3 wall(vec2 p){
  vec3 col = mix(u_pal[5], dim(u_pal[5], .07), sat(.5 - p.y*.3 + p.x*.1));
  col.x += .03*exp(-dot(p - vec2(-.7, .6), p - vec2(-.7, .6))*.4);
  vec2 g = p/cssu();
  col.x += .016*(h21(floor(g*.7)) - .5) + .008*gn(g*.12) + .006*sin(p.y*22. + 3.*gn(p*2.));
  float sk = mix(.3, 1., u_opt);
  stone(col, p, vec2(.18, .02), vec2(.42, .3), .3, u_pal[0], 1.3, sk);
  stone(col, p, vec2(.8, -.3), vec2(.28, .21), -.4, u_pal[1], 4.1, sk);
  stone(col, p, vec2(-.32, -.38), vec2(.24, .17), .9, u_pal[2], 7.4, sk);
  stone(col, p, vec2(.64, .44), vec2(.17, .13), -.2, u_pal[3], 2.6, sk);
  stone(col, p, vec2(1.2, .12), vec2(.13, .1), .6, u_pal[4], 9.9, sk);
  return col;
}`,
});

SW.add({
  id: 'halo', name: 'Halo', tier: 'minimal', grain: .3,
  desc: 'A soft ring of light whose colour drifts slowly around it, held in a quiet field.',
  opt: { label: 'Glow', def: .5 },
  roles: [[.20, .04, .92, .025], [.40, .08, .84, .06], [.42, .08, .83, .065], [.44, .08, .84, .06], [.42, .08, .83, .065], [.24, .045, .89, .03]],
  packs: [
    { n: 'Opal', k: 'rich', h: [260, 330, 30, 200, 290, 270], c: 1.5 },
    { n: 'Ember', h: [30, 10, 50, 350, 20, 30] },
    { n: 'Lagoon', h: [220, 190, 250, 170, 230, 225] },
    { n: 'Orchid', h: [300, 280, 330, 260, 310, 295] },
    { n: 'Moon', k: 'quiet', h: [250, 260, 240, 270, 255, 250], c: .4 },
  ],
  src: `
vec3 wall(vec2 p){
  vec3 c = mix(u_pal[0], u_pal[5], sm1(.5 + .3*p.x - .3*p.y));
  vec2 o = vec2(.5, .02);
  vec2 q = p - o;
  float r = length(q);
  float an = atan(q.y, q.x);
  float t = .5 + .5*sin(an + .6);
  float u = .5 + .5*sin(an*2. - 1.1);
  vec3 rc = mix(mix(u_pal[1], u_pal[2], t), mix(u_pal[3], u_pal[4], t), u);
  float g = mix(.6, 1.15, u_opt);
  float ring = exp(-pow((r - .62)/.17, 2.));
  float core = exp(-pow((r - .62)/.05, 2.));
  c = mix(c, rc, sat(ring*.8*g));
  c.x += .03*core*g;
  c = mix(c, mix(u_pal[0], rc, .15), .5*exp(-r*r*6.)*(1. - ring));
  return c;
}`,
});

SW.add({
  id: 'drift', name: 'Drift', tier: 'minimal', grain: .4,
  desc: 'A gradient mesh of five quiet colours melting into each other under a fine film grain.',
  opt: { label: 'Softness', def: .5 },
  roles: [[.30, .06, .88, .05], [.34, .07, .86, .06], [.28, .06, .90, .045], [.36, .07, .85, .06], [.32, .06, .87, .055], [.26, .05, .92, .03]],
  packs: [
    { n: 'Sorbet', k: 'rich', h: [10, 330, 300, 270, 200, 285], hl: [20, 330, 70, 280, 200, 50], c: 1.4 },
    { n: 'Glacier', h: [220, 250, 190, 270, 205, 230] },
    { n: 'Meadow', h: [170, 150, 195, 130, 182, 175], hl: [120, 80, 160, 60, 140, 100] },
    { n: 'Plum', h: [320, 290, 350, 270, 10, 310] },
    { n: 'Paper', k: 'quiet', h: [60, 30, 90, 250, 200, 70], c: .45 },
  ],
  src: `
vec3 wall(vec2 p){
  float sf = mix(.35, .1, u_opt);
  vec2 q = p + .18*vec2(gn(p*.7 + 2.), gn(p*.7 + 9.));
  vec2 c0 = vec2(-1.1, .55), c1 = vec2(.2, .8), c2 = vec2(1.25, .35), c3 = vec2(-.4, -.65), c4 = vec2(.95, -.7);
  float w0 = 1./pow(dot(q - c0, q - c0) + sf, 2.), w1 = 1./pow(dot(q - c1, q - c1) + sf, 2.), w2 = 1./pow(dot(q - c2, q - c2) + sf, 2.);
  float w3 = 1./pow(dot(q - c3, q - c3) + sf, 2.), w4 = 1./pow(dot(q - c4, q - c4) + sf, 2.), w5 = .35;
  vec3 c = (u_pal[0]*w0 + u_pal[1]*w1 + u_pal[2]*w2 + u_pal[3]*w3 + u_pal[4]*w4 + u_pal[5]*w5)/(w0 + w1 + w2 + w3 + w4 + w5);
  return c;
}`,
});

SW.add({
  id: 'afternoon', name: 'Afternoon', tier: 'minimal', grain: .3,
  desc: 'Late sun through a window falling across a plaster wall, its panes softening and a few leaves in the light.',
  opt: { label: 'Leaves', def: .5 },
  roles: [[.26, .04, .88, .035], [.23, .045, .84, .04], [.58, .085, .96, .05], [.20, .04, .80, .04], [.30, .05, .92, .03], [.24, .045, .86, .04]],
  packs: [
    { n: 'Golden', k: 'rich', h: [262, 270, 62, 255, 262, 265], hl: [60, 50, 75, 40, 60, 55], c: 1.4 },
    { n: 'Terracotta', h: [20, 12, 50, 5, 20, 15], hl: [40, 30, 60, 25, 40, 35] },
    { n: 'Sage', h: [175, 170, 85, 180, 175, 172], hl: [120, 110, 85, 130, 120, 115] },
    { n: 'Blue Hour', h: [250, 260, 60, 270, 250, 255] },
    { n: 'Chalk', k: 'quiet', h: [80, 75, 85, 70, 80, 78], c: .4 },
  ],
  src: `
vec3 wall(vec2 p){
  vec3 c = mix(u_pal[0], u_pal[1], sm1(.5 + .3*p.x + .25*p.y));
  c = mix(c, u_pal[3], .4*s01(.6, 2.4, length(p - vec2(-.2, .1))));
  float pl = fbm(p/cssu()*.012) + .4*gn(p/cssu()*.06);
  c.x += .01*pl;
  vec2 o = vec2(-.15, .05);
  vec2 q = p - o;
  q = vec2(q.x - q.y*.42, q.y)*rot(.08);
  vec2 hs = vec2(.62, .5);
  float bl = .01 + .05*sat(.5 - q.y*.8);
  float win = s01(-hs.x - bl, -hs.x + bl, q.x)*s01(hs.x + bl, hs.x - bl, q.x)*s01(-hs.y - bl, -hs.y + bl, q.y)*s01(hs.y + bl, hs.y - bl, q.y);
  float mw = .035;
  float mull = s01(mw - bl, mw + bl, abs(q.x))*s01(mw*.8 - bl, mw*.8 + bl, abs(q.y + .05));
  float lv = mix(0., .75, u_opt);
  vec2 lq = q*2.2 + vec2(3., 1.);
  float leaf = s01(.05, .3, fbm(lq + .4*vec2(gn(lq*1.7), gn(lq*1.7 + 3.))))*s01(.0, .5, q.x + q.y*.6 + .3);
  float I = win*mull*(1. - lv*leaf*(.6 + .4*s01(.0, .5, bl*10.)));
  c = mix(c, u_pal[2], sat(I)*mix(.55, .6, u_dark));
  c.x += mix(.02, .035, u_dark)*I;
  return c;
}`,
});

SW.add({
  id: 'vellum', name: 'Vellum', tier: 'minimal', grain: .25,
  desc: 'Sheets of translucent vellum laid one over another, their long curved edges catching a soft shadow.',
  opt: { label: 'Depth', def: .5 },
  roles: [[.22, .04, .93, .025], [.25, .045, .90, .035], [.28, .05, .87, .045], [.31, .055, .84, .055], [.34, .06, .81, .065], [.20, .035, .95, .02]],
  packs: [
    { n: 'Petal', k: 'rich', h: [350, 0, 10, 20, 30, 340], c: 1.5 },
    { n: 'Seafoam', h: [190, 180, 170, 160, 150, 200] },
    { n: 'Iris', h: [265, 275, 285, 295, 305, 255] },
    { n: 'Butter', h: [85, 78, 70, 62, 55, 90] },
    { n: 'Fog', k: 'quiet', h: [240, 235, 230, 225, 220, 245], c: .4 },
  ],
  src: `
void sheet(inout vec3 col, vec2 p, vec2 c, float R, vec3 sc, float dk){
  float d = length(p - c) - R;
  float ds = length(p - c - vec2(.03, -.05)*dk) - R;
  float sh = 1. - s01(-.04, .1 + .14*dk, ds);
  float a = cover(d);
  col = dim(col, mix(.09, .16, u_dark)*sh*(1. - a)*(.4 + dk));
  vec3 b = mix(sc, col, .18);
  b.x += .01*exp(-abs(d)/.012);
  col = mix(col, b, a);
}
vec3 wall(vec2 p){
  vec3 col = mix(u_pal[0], u_pal[5], sm1(.5 + .3*p.x - .3*p.y));
  float dk = mix(.2, 1., u_opt);
  sheet(col, p, vec2(-2.6, 2.3), 2.55, u_pal[1], dk);
  sheet(col, p, vec2(3.2, 1.9), 3.0, u_pal[2], dk);
  sheet(col, p, vec2(-1.2, -3.1), 2.6, u_pal[3], dk);
  sheet(col, p, vec2(2.4, -2.9), 2.4, u_pal[4], dk);
  return col;
}`,
});
