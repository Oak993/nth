// Static wallpaper library, tier 2 (second set): Balanced. Colourful but restrained: one real material or light each.
SW.add({
  id: 'tunnel', name: 'Tunnel', tier: 'balanced', grain: .3,
  desc: 'Sheets of coloured card cut with soft openings and stood one behind another, like a tunnel book lit from the far end.',
  opt: { label: 'Depth', def: .5 },
  roles: [[.30, .07, .74, .08], [.36, .09, .79, .09], [.43, .10, .84, .09], [.51, .11, .88, .08], [.60, .10, .93, .06], [.80, .08, .985, .025]],
  packs: [
    { n: 'Coral Reef', k: 'rich', h: [262, 290, 330, 10, 40, 70], c: 1.25 },
    { n: 'Evergreen', h: [235, 205, 175, 150, 120, 95] },
    { n: 'Twilight', h: [270, 285, 300, 320, 345, 30] },
    { n: 'Harbour', h: [250, 240, 228, 212, 195, 80] },
    { n: 'Pearl', k: 'quiet', h: [260, 270, 280, 300, 330, 60], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.072, h: -5.4, light: [-.55, .8] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.017, c: 1.102, h: 7.2, light: [-.15, .85] },
    night: { l: -.1, c: .82, h: -4.2, light: [.1, .9] },
  },
  persp: { def: 1, depth: .9, own: true },
  src: `
float holeT(vec2 p, float k){
  float t = k/5.;
  // the near sheets move most with the viewer (Perspective); the back sheet stays
  p += u_tilt*(1. - t);
  vec2 c = mix(vec2(-.2, .05), vec2(.62, -.08), sm1(t)) + vec2(0., .05*sin(t*3.));
  float r = mix(1.32, .26, pow(t, .85));
  vec2 q = rot(.25 + .5*t)*(p - c);
  q *= vec2(.78 + .1*t, 1.);
  float a = atan(q.y, q.x);
  return length(q) - r*(1. + .055*sin(2.*a + k*1.3) + .03*sin(3.*a - k*2.1));
}
vec3 wall(vec2 p){
  vec2 L = normalize(vec2(-.55, .83));
  float gap = mix(.025, .075, u_opt);
  vec3 col = u_pal[5];
  float done = 0.;
  float prevD = -1.;
  for (int j = 0; j < 6; j++) {
    float k = float(j);
    float d = j < 5 ? holeT(p, k) : 1.;
    if (done < .5 && d > 0.) {
      done = 1.;
      float t = k/5.;
      vec3 c = j < 5 ? ramp(t*.95) : u_pal[5];
      float inner = j < 5 ? s01(.7, -.2, d) : 1.;
      if (j == 5) { vec2 cc = p - vec2(.62, -.08); c = mix(u_pal[4], u_pal[5], exp(-dot(cc, cc)*9.)); }
      // light falls in from the far end: each sheet is brighter towards its opening
      c.x += .06*exp(-d/.25)*(.4 + .6*t);
      c.x -= .05*(1. - t)*s01(.0, 1.2, d);
      c.x += .004*gn(p/cssu()*.35 + k*13.) + .006*gn(p/cssu()*.07 + k*7.);
      if (j > 0) {
        float ds = holeT(p + L*gap*(1.2 - .2*k/5.), k - 1.);
        float sh = s01(-.05, .03, ds);
        float ao = exp(max(prevD, -1.)/.07*1.);
        c = dim(c, mix(.34, .45, u_dark)*sh + .16*ao);
      }
      if (j < 5) {
        float e = exp(-d/(cssu()*1.6));
        vec2 gr = vec2(holeT(p + vec2(.002, 0.), k) - d, holeT(p + vec2(0., .002), k) - d)/.002;
        float lit = dot(normalize(gr + 1e-5), -L);
        c.x += e*(.05 + .13*max(lit, 0.)) - e*.05*max(-lit, 0.);
      }
      col = c;
    }
    if (done < .5) prevD = d;
  }
  return col;
}`,
});

SW.add({
  id: 'celadon', name: 'Celadon', tier: 'balanced', grain: .25,
  desc: 'A glazed stoneware bowl seen from above on a linen cloth: the glaze pooling deep in the well, thinning pale on the rim, finely crazed.',
  opt: { label: 'Crazing', def: .5 },
  roles: [[.30, .03, .90, .02], [.42, .07, .78, .07], [.55, .08, .85, .07], [.66, .07, .91, .05], [.80, .04, .97, .02], [.35, .04, .62, .05]],
  packs: [
    { n: 'Celadon', k: 'rich', h: [250, 175, 165, 155, 140, 175], c: 1.25 },
    { n: 'Ash Blue', h: [262, 230, 222, 215, 205, 230] },
    { n: 'Rose Glaze', h: [262, 355, 5, 15, 25, 350] },
    { n: 'Sand', h: [262, 70, 75, 80, 85, 60] },
    { n: 'Porcelain', k: 'quiet', h: [240, 230, 220, 215, 210, 230], c: .35 },
  ],
  dyn: {
    dawn: { l: .008, c: 1.048, h: 3.6, light: [-.55, .8] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.011, c: 1.068, h: -4.8, light: [-.15, .85] },
    night: { l: -.1, c: .85, h: 2.8, light: [.1, .9] },
  },
  persp: { def: 1, depth: .6 },
  src: `
vec2 vor(vec2 x){
  vec2 n = floor(x), f = x - n;
  float d1 = 8., d2 = 8.;
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    vec2 g = vec2(float(i), float(j));
    vec2 o = h22(n + g);
    vec2 r = g + o - f;
    float d = dot(r, r);
    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
  }
  return vec2(sqrt(d1), sqrt(d2));
}
float bowlH(float r){
  float R = 1.18;
  float lip = exp(-pow((r - R)/.045, 2.))*.05;
  float well = r < R ? -.32*(1. - pow(r/R, 2.2)) : -.6*s01(R, R + .09, r);
  return well + lip;
}
vec3 wall(vec2 p){
  vec2 C = vec2(.92, -.28);
  vec2 d = p - C;
  float r = length(d);
  vec3 L = normalize(vec3(-.55, .65, .55));
  // linen cloth
  vec3 cloth = u_pal[0];
  vec2 w = p/cssu();
  float th = .5 + .5*sin(w.x*1.1 + .6*gn(w*.05))*.5 + .5*sin(w.y*1.1 + .6*gn(w*.05 + 3.))*.5;
  cloth.x += .012*(th - .5) + .008*gn(w*.02) + .03*sat(.5 - p.y*.3 - p.x*.2);
  // the bowl's shadow on the cloth
  float rs = length(p - C + vec2(.12, .16));
  cloth = dim(cloth, .32*(1. - s01(1.05, 1.5, rs)));
  cloth = dim(cloth, .15*(1. - s01(1.2, 1.3, r)));
  vec3 col = cloth;
  float R = 1.18 + .055;
  float a = cover(r - R);
  if (a > 0.) {
    float e = .002;
    float h0 = bowlH(r), h1 = bowlH(r + e);
    float dh = (h1 - h0)/e;
    vec2 dir = d/max(r, 1e-4);
    vec3 n = normalize(vec3(-dir*dh, 1.));
    // glaze thickness: pooled in the well, thin over the lip
    float thick = sat(.25 + .9*(1. - r/1.18)) * (1. - exp(-pow((r - 1.18)/.05, 2.))*.85);
    vec3 g = mix(u_pal[4], mix(u_pal[2], u_pal[1], sm1(1. - r/1.1)), sat(thick*1.2));
    float dif = sat(dot(n, L)*.8 + .3);
    g.x *= .72 + .36*dif;
    // the bowl shades its own far wall
    float self = s01(-.2, .9, dot(dir, -L.xy))*s01(.0, 1.1, r)*(r < 1.18 ? 1. : 0.);
    g = dim(g, .22*self);
    vec3 Rv = reflect(vec3(0., 0., -1.), n);
    float win = s01(.6, .95, dot(Rv, normalize(vec3(-.5, .6, .62))));
    g.x += .16*win*win + .05*pow(sat(dot(Rv, L)), 30.);
    vec2 v = vor(p/cssu()*.045 + 5.);
    float cr = 1. - s01(.0, .035 + .04*(1. - thick), v.y - v.x);
    g = mix(g, dim(g, .35), cr*mix(.0, .55, u_opt)*thick);
    g.x += .006*gn(p*60.);
    col = mix(col, g, a);
  }
  return col;
}`,
});

SW.add({
  id: 'harbour', name: 'Harbour', tier: 'balanced', grain: .3,
  desc: 'A harbour on still water: by night the quay lights stretch into long trails, by day a row of pastel houses at dawn, both broken into strokes by the slow swell.',
  opt: { label: 'Swell', def: .5 },
  roles: [[.17, .05, .76, .05], [.70, .13, .68, .085], [.66, .12, .72, .08], [.62, .12, .64, .085], [.72, .10, .76, .075], [.36, .07, .935, .035]],
  packs: [
    { n: 'Lantern', k: 'rich', h: [262, 70, 25, 340, 195, 285], hl: [240, 20, 75, 345, 200, 55], c: 1.15 },
    { n: 'Neon Dusk', h: [275, 325, 200, 290, 350, 285], hl: [270, 330, 205, 295, 355, 340] },
    { n: 'Seafront', h: [240, 50, 205, 15, 175, 245], hl: [230, 55, 205, 15, 175, 70] },
    { n: 'Lagoon', h: [220, 180, 160, 205, 135, 215], hl: [215, 180, 160, 205, 130, 150] },
    { n: 'Moonlight', k: 'quiet', h: [255, 230, 240, 260, 220, 250], hl: [250, 230, 240, 260, 220, 60], c: .4 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -9 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.028, c: 1.17, h: 12 },
    night: { l: -.14, c: .8, h: -7 },
  },
  persp: { def: 1, depth: .6 },
  src: `
vec3 lightC(float fi){
  float m = mod(fi, 4.);
  return m < .5 ? u_pal[1] : m < 1.5 ? u_pal[2] : m < 2.5 ? u_pal[3] : u_pal[4];
}
// the far shore: a row of narrow houses of different heights, each painted its own colour
float roofH(float x){ return .03 + .035*step(.55, h21(vec2(floor(x*3.3 + 7.), 3.))) + .015*step(.5, h21(vec2(floor(x*11.), 5.))) + .012*h21(vec2(floor(x*3.3 + 7.), 6.)); }
vec3 houseC(float x, float y){
  float id = floor(x*3.3 + 7.);
  vec3 c = lightC(id + floor(h21(vec2(id, 8.))*3.));
  // windows: a faint row of darker panes, and the sunlit face a touch brighter on its left
  float wx = fract(x*26.), wy = fract(y*95.);
  c.x -= .035*s01(.3, .4, wx)*s01(.75, .65, wx)*s01(.3, .4, wy)*s01(.8, .7, wy);
  c.x += .02*s01(.25, .0, fract(x*3.3 + 7.));
  return c;
}
vec3 wall(vec2 p){
  float hz = .58;
  float sw = mix(.35, 1.2, u_opt);
  float day = 1. - u_dark;
  vec2 sun = vec2(-.95, hz + .07);
  vec3 sky = mix(u_pal[5], u_pal[0], sm1((p.y - hz)/.55));
  sky = mix(sky, lift(u_pal[5], .05), day*.7*exp(-length((p - sun)*vec2(.55, 1.))*3.));
  vec3 col = sky;
  float qt = hz + roofH(p.x);
  vec3 quay = dim(u_pal[0], .25);
  float wid = mix(.05, .02, u_dark);
  if (p.y >= hz) {
    vec3 fac = mix(quay, houseC(p.x, p.y), day);
    fac = mix(fac, dim(mix(u_pal[0], u_pal[5], .4), .15), day*s01(hz + .007, hz + .003, p.y));
    col = mix(col, fac, cover(p.y - qt));
    for (int i = 0; i < 11; i++) {
      float fi = float(i);
      float xi = -1.95 + fi*.37 + .3*(h21(vec2(fi, 2.)) - .5) + .12*sin(fi*2.3);
      vec2 d = p - vec2(xi, hz + .022);
      float k = (.5 + .5*h21(vec2(fi, 9.)))*u_dark;
      col = mix(col, lightC(fi), sat(k*(exp(-dot(d, d)/.00006) + .25*exp(-length(d)/.03))));
    }
  } else {
    float dz = hz - p.y;
    float Z = 1./(dz + .025);
    vec2 wq = vec2(p.x*Z*.35, Z*2.4);
    float wv = gn(wq) + .5*gn(wq*vec2(2.1, 2.3) + 3.) + .25*gn(wq*vec2(4.3, 4.1) + 7.);
    float dx = sw*.045*wv*(.25 + dz*1.2);
    vec3 water = mix(u_pal[5], dim(u_pal[0], .05), sm1(dz/.9));
    water.x += .025*wv*s01(.0, .3, dz);
    float sharp = s01(.0, .6, dz);
    float facet = s01(mix(-.35, -.05, sharp), mix(.6, .35, sharp), wv);
    col = water;
    // by day the houses lie in the water, stretched and broken by the swell
    float xr = p.x + dx*.8;
    float rl = roofH(xr)*4.;
    float rm = s01(rl, rl*.55, dz + .25*rl*wv)*mix(.5, 1., facet)*mix(1., .35 + .65*facet, s01(.0, .2, dz));
    vec3 rc = mix(houseC(xr, hz + dz*.25), water, .2 + .4*s01(.0, rl, dz));
    col = mix(col, rc, day*.92*rm);
    // the glow of the sky under the sun, laid along the water
    col = mix(col, lift(u_pal[5], .03), day*.35*exp(-abs(p.x - sun.x)/(.25 + .5*dz))*exp(-dz*1.5));
    // the low sun's path, glittering on the facets towards it
    float path = exp(-abs(p.x + dx*.6 - sun.x)/(.03 + .22*dz))*pow(facet, 3.)*exp(-dz*.9);
    col = mix(col, lift(u_pal[5], .07), day*sat(path*1.8));
    // by night the lights of the quay make long trails
    vec3 acc = vec3(0.);
    float tot = 0.;
    for (int i = 0; i < 11; i++) {
      float fi = float(i);
      float xi = -1.95 + fi*.37 + .3*(h21(vec2(fi, 2.)) - .5) + .12*sin(fi*2.3);
      float w = (wid + .035*dz*sw)*(.6 + .8*h21(vec2(fi, 4.)));
      float k = exp(-pow((p.x + dx - xi)/w, 2.))*(.5 + .5*h21(vec2(fi, 9.)));
      acc += lightC(fi)*k; tot += k;
    }
    float tk = sat(tot)*mix(.85, 1., facet*facet)*mix(1., .2 + .8*facet, s01(.0, .25, dz));
    vec3 tc = acc/max(tot, 1e-4);
    tc = mix(tc, lift(tc, .05), facet*.5);
    col = mix(col, tc, tk*.9*u_dark*exp(-dz*.3));
  }
  return col;
}`,
});

SW.add({
  id: 'oculus', name: 'Oculus', tier: 'balanced', grain: .3,
  desc: 'Looking up into a coffered dome: the open eye at its crown full of sky, and the low sun through it laying one bright oval across the coffers.',
  opt: { label: 'Sun', def: .5 },
  roles: [[.25, .05, .62, .05], [.32, .06, .70, .06], [.44, .06, .80, .05], [.74, .06, .95, .05], [.62, .08, .84, .08], [.88, .06, .98, .03]],
  packs: [
    { n: 'Roman', k: 'rich', h: [262, 270, 285, 22, 225, 210], hl: [262, 270, 285, 62, 225, 210], c: 1.2 },
    { n: 'Terrace', h: [300, 320, 340, 15, 200, 220], hl: [300, 320, 340, 40, 200, 220] },
    { n: 'Celeste', h: [240, 232, 225, 200, 210, 200], hl: [240, 232, 225, 85, 210, 200] },
    { n: 'Sage', h: [190, 175, 160, 140, 205, 215], hl: [190, 175, 160, 95, 205, 215] },
    { n: 'Stucco', k: 'quiet', h: [250, 255, 260, 250, 225, 220], hl: [250, 255, 260, 70, 225, 220], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.072, h: -5.4 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.017, c: 1.102, h: 7.2 },
    night: { l: -.1, c: .82, h: -4.2 },
  },
  persp: { def: 1, depth: .6 },
  src: `
float cofM(vec2 c){ return min(min(c.x, 1. - c.x)*1.25, min(c.y, 1. - c.y)); }
vec3 wall(vec2 p){
  vec3 rd = normalize(vec3(p.x, p.y, 1.15));
  rd.yz = rot(-.85)*rd.yz;
  rd.xz = rot(-.68)*rd.xz;
  float ca = .965;
  float y = rd.y;
  vec3 sd = normalize(vec3(-.8, mix(.48, .62, u_opt), -.3));
  vec3 col;
  if (y > ca) {
    col = mix(u_pal[4], u_pal[5], s01(.6, 1., dot(rd, normalize(sd + vec3(0., 1.5, 0.)))));
    col = mix(col, lift(u_pal[3], .03), s01(ca + .003, ca, y));
  } else {
    float el = asin(clamp(y, -1., 1.));
    float az = atan(rd.x, rd.z);
    float rows = 5.;
    float e0 = .12, e1 = 1.2;
    float v = (el - e0)/(e1 - e0)*rows;
    float u = az/(2.*PI)*28.;
    vec2 cf = vec2(u - floor(u), v - floor(v));
    float inC = step(0., v)*step(v, rows);
    float aa = pxu()*rows/(e1 - e0)*1.2/max(cos(el)*.0 + 1., 1.);
    float m = cofM(cf);
    // three steps down into each coffer: rib, first bevel, ledge, second bevel, panel
    float s1 = s01(.09, .09 + aa, m), s2 = s01(.17, .17 + aa, m), s3 = s01(.23, .23 + aa, m), s4 = s01(.3, .3 + aa, m);
    float bevel = (s1 - s2) + (s3 - s4);
    float horiz = step(min(cf.x, 1. - cf.x)*1.25, min(cf.y, 1. - cf.y));
    float up = cf.y < .5 ? 1. : -1.;
    float side = cf.x < .5 ? 1. : -1.;
    float face = mix(up*.8, side*.35, horiz);
    float depth = (s1*.35 + s2*.25 + s3*.2 + s4*.2)*inC;
    float amb = .28 + .72*pow(sat(y), 1.4);
    vec3 c = mix(u_pal[0], u_pal[2], amb);
    c = mix(c, u_pal[1], .3*(1. - amb));
    c.x -= .05*depth*(1. - .3*amb);
    c.x += .035*face*bevel*inC*(.4 + amb);
    c.x += .007*gn(rd.xz*60. + rd.y*9.) + .004*gn(p/cssu()*.3);
    // the sun through the eye: follow the ray back towards the sun
    float tt = (ca - y)/sd.y;
    vec3 Q = rd + sd*tt;
    float rq = length(Q.xz)/sqrt(1. - ca*ca);
    float spot = s01(1.03, .95, rq);
    // the ribs cast short shadows into the coffers on the side away from the light
    vec2 sh = cf + vec2(.1, .14);
    float shadow = inC*s4*(1. - s01(.07, .12, cofM(sh - floor(sh))))*0. + inC*(1. - s01(.06, .1, cofM(fract(cf + vec2(.09, .13)))))*s1;
    vec3 sun = u_pal[3];
    sun.x += .06*face*bevel*inC - .02*depth;
    sun = dim(sun, .42*shadow);
    c = mix(c, sun, .85*spot);
    c = mix(c, mix(u_pal[2], u_pal[3], .5), .18*exp(-max(rq - 1., 0.)*1.3)*(1. - spot));
    col = mix(c, dim(c, .3), s01(ca - .01, ca, y));
  }
  return col;
}`,
});

SW.add({
  id: 'saltflat', name: 'Salt Flat', tier: 'balanced', grain: .3,
  desc: 'A salt flat under a skin of rain water, the dusk sky and its clouds doubled in it, the crust showing through in pale ridges at your feet.',
  opt: { label: 'Water', def: .5 },
  roles: [[.28, .07, .70, .08], [.50, .08, .86, .06], [.64, .09, .93, .05], [.78, .05, .97, .02], [.42, .06, .82, .04], [.90, .03, .99, .01]],
  packs: [
    { n: 'Uyuni', k: 'rich', h: [262, 300, 25, 60, 280, 70], c: 1.25 },
    { n: 'Sorbet', h: [285, 340, 20, 50, 300, 40] },
    { n: 'Clear Day', h: [245, 230, 215, 205, 235, 210] },
    { n: 'Lavender', h: [280, 300, 320, 340, 285, 320] },
    { n: 'Pewter', k: 'quiet', h: [245, 240, 235, 230, 240, 235], c: .35 },
  ],
  dyn: {
    dawn: { l: .022, c: 1.132, h: -9, light: [-1, .15] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .8] },
    dusk: { l: -.031, c: 1.187, h: 12, light: [1, .1] },
    night: { l: -.14, c: .78, h: -7, light: [.3, .8] },
  },
  persp: { def: 1, depth: .8 },
  src: `
vec3 skyS(vec2 p, float hz){
  float y = p.y - hz;
  vec3 c = mix(u_pal[2], u_pal[1], s01(.0, .3, y));
  c = mix(c, u_pal[0], s01(.2, 1.1, y));
  c = mix(c, u_pal[3], .5*exp(-y/.05));
  // soft clouds that flatten towards the horizon
  vec2 cq = vec2(p.x*.9, y*3.2/(y + .25));
  float cl = s01(.05, .6, fbm(cq*vec2(1.2, 2.4) + vec2(3., 1.)));
  vec3 cc = mix(u_pal[3], u_pal[4], s01(.0, .8, y));
  cc.x += .05*fbm3(cq*4.);
  return mix(c, cc, cl*.55*s01(.0, .06, y));
}
float hexE(vec2 x){
  vec2 n = floor(x), f = x - n;
  float d1 = 8., d2 = 8.;
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    vec2 g = vec2(float(i), float(j));
    vec2 r = g + .5 + .75*(h22(n + g) - .5) - f;
    float d = dot(r, r);
    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
  }
  return sqrt(d2) - sqrt(d1);
}
vec3 wall(vec2 p){
  float hz = -.08;
  if (p.y >= hz) return skyS(p, hz);
  float dz = hz - p.y;
  vec3 r = skyS(vec2(p.x, hz + dz), hz);
  r = dim(r, .06 + .08*s01(.0, .8, dz));
  float wt = mix(.25, .9, u_opt);
  // the crust in perspective: ridges thin out towards the horizon and stand clear of the water near you
  float Z = .7/(dz + .002);
  vec2 X = vec2(p.x*Z, Z*1.6)*1.3;
  float e = hexE(X);
  float rw = .03 + .03*gn(X*.7);
  float ridge = (1. - s01(rw*.4, rw, e))*s01(.05, .5, dz)*(.5 + .5*gn(X*.4 + 3.));
  float dry = s01(wt, wt - .35, .5 + .5*gn(X*.25) - dz*.3);
  vec3 salt = mix(u_pal[5], u_pal[4], .3);
  salt.x += .03*gn(X*3.);
  vec3 col = mix(r, salt, sat(ridge)*mix(.12, .5, dry));
  col = dim(col, .06*exp(-e/(rw*3.))*s01(.05, .5, dz)*(1. - dry));
  col = mix(col, r, s01(.03, .0, dz)*.0);
  return col;
}`,
});

SW.add({
  id: 'dewdrop', name: 'Dewdrop', tier: 'balanced', grain: .3,
  desc: 'A few drops of water resting on coloured card under a soft window light, each one holding a tiny bright window and a pool of light in its shadow.',
  opt: { label: 'Light', def: .5 },
  roles: [[.36, .09, .68, .10], [.46, .10, .76, .10], [.30, .08, .58, .10], [.92, .03, .99, .01], [.62, .10, .86, .07], [.22, .06, .48, .09]],
  packs: [
    { n: 'Persimmon', k: 'rich', h: [352, 0, 345, 20, 15, 340], hl: [32, 45, 22, 60, 60, 15], c: 1.3 },
    { n: 'Sage', h: [150, 140, 160, 120, 130, 170] },
    { n: 'Iris', h: [280, 290, 270, 300, 300, 265] },
    { n: 'Petrol', h: [215, 205, 225, 200, 195, 230] },
    { n: 'Paper', k: 'quiet', h: [250, 245, 255, 240, 240, 260], c: .35 },
  ],
  dyn: {
    dawn: { l: .01, c: 1.06, h: 4.5, light: [-.55, .8] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.014, c: 1.085, h: -6, light: [-.15, .85] },
    night: { l: -.1, c: .84, h: 3.5, light: [.1, .9] },
  },
  persp: { def: 1, depth: .7 },
  src: `
vec3 cardD(vec2 p){
  vec3 c = mix(u_pal[0], u_pal[1], s01(-1.3, 1.1, p.y*.6 - p.x*.35));
  c.x += .008*gn(p/cssu()*.25) + .004*gn(p/cssu()*.9);
  return c;
}
void drop(inout vec3 col, vec2 p, vec2 c, float R, float lt){
  vec2 Ld = normalize(vec2(-.6, .75));
  vec2 q = (p - c)/R;
  float r = length(q);
  // the shadow falls away from the light, with the light the drop focused gathered inside it
  vec2 qs = (p - c + Ld*R*.55)/R;
  float rs = length(qs*vec2(1., 1.));
  float sh = s01(1.05, .75, rs);
  vec2 qf = (p - c + Ld*R*.62)/R;
  float foc = exp(-dot(qf, qf)/.1);
  col = dim(col, .32*sh*(1. - s01(.95, 1.02, r)*0.));
  col = mix(col, lift(u_pal[4], .06), .7*foc*lt*s01(.9, 1., r));
  float a = s01(1. + pxu()*1.2/R, 1. - pxu()*1.2/R, r);
  if (a <= 0.) return;
  // the drop is a lens: the card under it seen through it, magnified and turned over
  float z = sqrt(max(1. - r*r, 0.));
  vec3 n = normalize(vec3(q, z*.8 + .05));
  vec3 inside = cardD(c - q*R*.55);
  inside = mix(inside, u_pal[4], .3*s01(.0, .8, dot(-q, Ld)));
  inside.x += .05*s01(.3, 1., dot(-q, Ld));
  // a dark ring at the rim where the drop turns the light away
  float rim = s01(.55, .97, r);
  inside = mix(inside, u_pal[2], .75*rim*rim);
  inside = dim(inside, .25*rim*s01(-.2, .8, dot(q, Ld)));
  // the window, reflected small and sharp, and a softer second reflection
  vec2 wq = q - Ld*.42;
  float win = s01(.11, .085, sdBox(rot(.3)*wq, vec2(.1, .07), .03));
  inside = mix(inside, u_pal[3], win*.95*lt);
  float soft = exp(-dot(q + Ld*.55, q + Ld*.55)/.02)*.35;
  inside = mix(inside, u_pal[3], soft*lt);
  col = mix(col, inside, a);
}
vec3 wall(vec2 p){
  vec3 col = cardD(p);
  float lt = mix(.55, 1., u_opt);
  drop(col, p, vec2(.95, -.32), .36, lt);
  drop(col, p, vec2(1.42, .3), .15, lt);
  drop(col, p, vec2(.38, -.75), .1, lt);
  drop(col, p, vec2(-1.2, .55), .07, lt);
  drop(col, p, vec2(1.25, -.85), .06, lt);
  return col;
}`,
});

SW.add({
  id: 'swell', name: 'Swell', tier: 'balanced', grain: .3,
  desc: 'Open water at the last of the light, seen from just above it: one long glassy swell lifting towards you and the sky laid along its back.',
  opt: { label: 'Swell', def: .5 },
  roles: [[.18, .05, .58, .08], [.30, .07, .70, .08], [.50, .08, .84, .06], [.70, .10, .93, .06], [.40, .06, .76, .07], [.88, .08, .99, .03]],
  packs: [
    { n: 'Dusk', k: 'rich', h: [265, 248, 300, 20, 230, 55], hl: [245, 230, 300, 25, 220, 70], c: 1.2 },
    { n: 'Pacific', h: [245, 225, 215, 195, 215, 90] },
    { n: 'Rose', h: [285, 295, 330, 15, 275, 40] },
    { n: 'Teal', h: [225, 205, 190, 170, 200, 95] },
    { n: 'Pewter', k: 'quiet', h: [250, 245, 240, 235, 245, 240], c: .35 },
  ],
  dyn: {
    dawn: { l: .022, c: 1.132, h: -9, light: [-1, .15] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .8] },
    dusk: { l: -.031, c: 1.187, h: 12, light: [1, .1] },
    night: { l: -.15, c: .78, h: -7, light: [.3, .8] },
  },
  persp: { def: 1, depth: .8 },
  src: `
vec3 skyW(vec3 d, vec3 sd){
  vec3 c = mix(u_pal[3], u_pal[2], s01(.0, .12, d.y));
  c = mix(c, u_pal[1], s01(.08, .5, d.y));
  c = mix(c, u_pal[0], s01(.4, 1., d.y));
  float s = sat(dot(d, sd));
  c = mix(c, u_pal[5], .7*pow(s, 40.) + .25*pow(s, 5.)*s01(.3, .0, d.y));
  return c;
}
float swH(vec2 x, float a){
  float h = a*(.6*sin(x.y*.42 + .3*x.x + .8*gn(x*.1)) + .25*sin(x.y*.9 - .45*x.x + 1.));
  return h + .05*gn(x*.7) + .015*gn(x*2.1 + 4.) + .005*gn(x*5.3);
}
vec3 wall(vec2 p){
  vec3 ro = vec3(0., 1.2, 0.);
  vec3 rd = normalize(vec3(p.x, p.y - .22, 2.));
  vec3 sd = normalize(vec3(.55, .06, 1.));
  float a = mix(.2, .65, u_opt);
  if (rd.y >= 0.) return skyW(rd, sd);
  float t = -ro.y/rd.y;
  // two refinement steps onto the swell
  for (int i = 0; i < 3; i++) { vec3 x = ro + rd*t; t = (swH(x.xz, a) - ro.y)/rd.y; }
  vec3 x = ro + rd*t;
  float e = .02 + .004*t;
  float h = swH(x.xz, a);
  vec3 n = normalize(vec3(-(swH(x.xz + vec2(e, 0.), a) - h)/e, 1., -(swH(x.xz + vec2(0., e), a) - h)/e));
  vec3 r = reflect(rd, n);
  r.y = abs(r.y);
  float fr = .04 + .96*pow(1. - sat(dot(-rd, n)), 5.);
  vec3 deep = mix(u_pal[4], u_pal[0], s01(.0, .6, -rd.y*3.));
  deep = mix(deep, u_pal[1], .25*sat(h/a + .5));
  vec3 c = mix(deep, skyW(r, sd), sat(fr*1.4 + .15));
  float fog = 1. - exp(-t*.025);
  c = mix(c, skyW(vec3(rd.x, .01, rd.z), sd), fog);
  return c;
}`,
});

SW.add({
  id: 'pinewood', name: 'Pinewood', tier: 'balanced', grain: .3,
  desc: 'Ridges of pine forest stepping back into morning fog, each row of trees paler than the one before, the valleys between them filled with mist.',
  opt: { label: 'Fog', def: .5 },
  roles: [[.28, .06, .72, .06], [.36, .07, .80, .05], [.46, .07, .86, .05], [.58, .06, .91, .04], [.72, .05, .95, .03], [.86, .05, .985, .02]],
  packs: [
    { n: 'Morning', k: 'rich', h: [235, 215, 200, 190, 300, 330], hl: [190, 175, 165, 160, 30, 60], c: 1.2 },
    { n: 'Lavender', h: [270, 275, 280, 290, 320, 340] },
    { n: 'Rose Fog', h: [320, 330, 340, 350, 10, 30] },
    { n: 'Teal', h: [215, 205, 195, 185, 175, 165] },
    { n: 'Silver', k: 'quiet', h: [245, 240, 235, 230, 225, 220], c: .35 },
  ],
  dyn: {
    dawn: { l: .018, c: 1.108, h: -8.1, light: [-.9, .25] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.025, c: 1.153, h: 10.8, light: [.9, .2] },
    night: { l: -.13, c: .8, h: -6.3, light: [.2, .85] },
  },
  persp: { def: 1, depth: 1, own: true },
  src: `
float treeline(float x, float k){
  float sc = 70. - 12.*k;
  float g = x*sc + k*13.;
  float id = floor(g), f = g - id;
  float best = -9.;
  for (int i = -1; i <= 1; i++) {
    float c = id + float(i);
    float cx = c + .5 + .35*(h21(vec2(c, k)) - .5);
    float hh = (.55 + .45*h21(vec2(c, k + 7.)));
    float dx = abs(g - cx);
    // a pine: a narrow cone with ragged tiers of branches
    float tier = .06*abs(sin((g - cx)*0. + hh*40. + c)) ;
    float prof = hh*(1. - dx/(.5 + .08*hh)) - .03*abs(sin((1. - dx)*7. + c));
    best = max(best, prof);
  }
  return best/sc*2.2;
}
float ridge(float x, float k){ return .32 - .27*k + .1*sin(x*(.8 + .25*k) + k*2.3) + .05*sin(x*2.1 - k) + .02*gn(vec2(x*2., k)); }
vec3 wall(vec2 p){
  vec3 sky = mix(u_pal[5], u_pal[4], s01(.0, .9, p.y));
  sky = mix(sky, u_pal[5], .5*exp(-length(p - vec2(-.9, .55))*1.4));
  vec3 col = sky;
  float fg = mix(.35, .95, u_opt);
  for (int j = 0; j < 5; j++) {
    float k = float(j);
    // each row moves with the viewer by its nearness (Perspective)
    vec2 pk = p + u_tilt*(k/4.);
    float base = ridge(pk.x, k);
    float top = base + treeline(pk.x, k)*(1. + .25*k);
    float a = cover(pk.y - top);
    float u = k/4.;
    vec3 c = mix(u_pal[3], u_pal[0], u);
    c.x += .02*gn(vec2(pk.x*60., pk.y*30.))*u;
    // mist lies in the hollows: the lower part of every row sinks into it
    float dep = sat((top - pk.y)/(.25 + .1*k));
    float fogA = mix(.9, .25, u)*fg + (1. - u)*.1;
    c = mix(c, mix(u_pal[4], u_pal[5], .4), sat(fogA*(.55 + .45*(1. - dep)) + fg*.5*dep*dep*(1. - u*.5)));
    c = mix(c, u_pal[5], .15*fg*exp(-length(p - vec2(-.9, .55))*1.2)*(1. - u));
    col = mix(col, c, a);
  }
  return col;
}`,
});

SW.add({
  id: 'leaf', name: 'Leaf', tier: 'balanced', grain: .25,
  desc: 'A single leaf held up to the light, close enough to see the fine net of its veins glowing through the green.',
  opt: { label: 'Veins', def: .5 },
  roles: [[.24, .05, .80, .04], [.34, .07, .88, .03], [.42, .10, .62, .11], [.52, .11, .72, .11], [.64, .10, .84, .09], [.78, .09, .93, .07]],
  packs: [
    { n: 'Spring', k: 'rich', h: [235, 220, 150, 135, 120, 105], c: 1.15 },
    { n: 'Maple', h: [280, 300, 5, 20, 40, 60] },
    { n: 'Ginkgo', h: [240, 230, 105, 95, 90, 85] },
    { n: 'Plum', h: [270, 260, 330, 340, 350, 10] },
    { n: 'Frost', k: 'quiet', h: [240, 230, 200, 190, 180, 170], c: .35 },
  ],
  dyn: {
    dawn: { l: .01, c: 1.06, h: 4.5, light: [-.55, .8] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.014, c: 1.085, h: -6, light: [-.15, .85] },
    night: { l: -.1, c: .85, h: 3.5, light: [.1, .9] },
  },
  persp: { def: 1, depth: .7 },
  src: `
vec2 vorL(vec2 x){
  vec2 n = floor(x), f = x - n;
  float d1 = 8., d2 = 8.;
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    vec2 g = vec2(float(i), float(j));
    vec2 r = g + h22(n + g) - f;
    float d = dot(r, r);
    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
  }
  return vec2(sqrt(d1), sqrt(d2));
}
vec3 wall(vec2 p){
  // soft daylight behind, out of focus
  vec3 bg = mix(u_pal[0], u_pal[1], s01(-1.2, 1., p.y*.6 - p.x*.4));
  bg = mix(bg, u_pal[1], .4*exp(-length(p - vec2(-1.2, .7))*1.2));
  // the leaf: a long pointed blade crossing the frame from the lower right
  vec2 o = vec2(1.9, -1.15);
  vec2 ax = normalize(vec2(-1., .62));
  vec2 q = p - o;
  float u = dot(q, ax), v = dot(q, vec2(-ax.y, ax.x));
  float Ll = 3.6;
  float s = sat(u/Ll);
  float wdt = 1.05*pow(s, .55)*pow(1. - s, .85)*1.9;
  v += .06*sin(u*1.3)*s;
  float edge = abs(v) - wdt;
  edge += .006*sin(u*90.)*s01(.0, .2, s);
  float a = s01(pxu(), -pxu(), edge)*step(0., u)*step(u, Ll - .02);
  float vn = mix(.4, 1., u_opt);
  // the colour of the light through the blade, brightest away from its edges
  float fromE = sat(-edge/max(wdt, .01));
  vec3 lam = mix(u_pal[2], u_pal[4], .3 + .5*fromE);
  lam = mix(lam, u_pal[3], .3*s01(.0, 1., fbm3(p*1.5 + 3.)));
  // midrib and the secondary veins sweeping out towards the tip
  float rib = exp(-abs(v)/(.012 + .02*(1. - s)));
  float ph = (u - abs(v)*1.15)*4.2;
  float sec = exp(-abs(fract(ph) - .5)/(.035 + .02*(1. - fromE)))*s01(.02, .08, abs(v));
  vec2 vr = vorL(vec2(u, v)*13.);
  float ter = exp(-(vr.y - vr.x)/.06)*.6;
  vec2 vr2 = vorL(vec2(u, v)*38. + 5.);
  float qua = exp(-(vr2.y - vr2.x)/.08)*.35;
  float veins = sat(rib + .8*sec + (ter + qua)*vn);
  vec3 c = mix(lam, u_pal[5], veins*.55*vn + rib*.25);
  // the areoles between the veins hold a little more colour
  c = mix(c, u_pal[2], .18*(1. - veins)*vn);
  c = dim(c, .25*s01(.0, -.03, edge)*s01(-.06, .0, edge));
  return mix(bg, c, a);
}`,
});

SW.add({
  id: 'stairwell', name: 'Stairwell', tier: 'balanced', grain: .3,
  desc: 'Looking straight up a spiral stair: the white rail winding turn over turn towards the skylight at the top, the plaster paling as it rises.',
  opt: { label: 'Turns', def: .5 },
  roles: [[.26, .05, .62, .06], [.34, .06, .70, .06], [.44, .07, .78, .06], [.56, .07, .86, .05], [.80, .05, .96, .03], [.92, .04, .99, .015]],
  packs: [
    { n: 'Terracotta', k: 'rich', h: [262, 280, 300, 350, 30, 80], hl: [270, 300, 20, 35, 60, 80], c: 1.2 },
    { n: 'Seafoam', h: [235, 215, 195, 180, 165, 150] },
    { n: 'Lilac', h: [270, 280, 290, 305, 320, 330] },
    { n: 'Mustard', h: [255, 245, 220, 95, 90, 85], hl: [255, 230, 95, 90, 85, 80] },
    { n: 'Plaster', k: 'quiet', h: [250, 255, 260, 265, 250, 240], c: .35 },
  ],
  dyn: {
    dawn: { l: .01, c: 1.06, h: -4.5, light: [-.55, .8] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.014, c: 1.085, h: 6, light: [-.15, .85] },
    night: { l: -.1, c: .84, h: -3.5, light: [.1, .9] },
  },
  persp: { def: 1, depth: .7 },
  src: `
vec3 wall(vec2 p){
  vec2 C = vec2(.62, .3);
  vec2 d = p - C;
  float r = length(d);
  float th = atan(d.y, d.x);
  float tight = mix(.95, 1.6, u_opt);
  // one turn of the stair per floor: each floor up is a ring closer to the skylight
  float lr = log(r);
  float s = th/(2.*PI) - lr*tight;
  float f = fract(s);
  float fw = (pxu()/r)*tight/(2.*PI)*2.5 + .002;
  float up = sat(-lr*.45 + .2);
  float rise = s01(1.4, -.3, lr);
  // the light comes down the well from the skylight: everything brightens and pales towards it
  vec3 plaster = mix(u_pal[1], u_pal[3], rise);
  float face = .5 + .5*cos(th - 2.3);
  // the underside of each flight, with the line of every step on it
  float st = fract(th/(2.*PI)*18. - lr*tight*18.);
  vec3 under = mix(plaster, u_pal[2], .25);
  under.x += .06*face*(1. - rise*.5) - .03*s01(.82, 1., st)*(1. - rise) + .015*s01(.0, .15, st)*(1. - rise);
  // the outer wall seen between the flights, deeper and in shade
  vec3 wallc = dim(mix(plaster, u_pal[0], .35*(1. - rise)), .08);
  wallc.x += .03*face;
  float uMask = s01(.06 - fw, .06 + fw, f)*s01(.52 + fw, .52 - fw, f);
  vec3 c = mix(wallc, under, uMask);
  // the rail: a pale band catching the skylight, with a soft shadow under it
  float rail = s01(.06 + fw, .06 - fw, f)*s01(.0 - fw, .0 + fw, f) + s01(.0 + .0, .0 - .0, f)*0.;
  float railB = 1. - s01(.0, .06 + fw, f);
  vec3 rc = mix(u_pal[4], u_pal[5], .5 + .5*face);
  c = mix(c, rc, sat(railB)*s01(.0, fw*2., f));
  c = dim(c, .2*exp(-(f - .06)/.04)*step(.06, f)*uMask);
  c.x += .006*gn(p/cssu()*.3);
  // the skylight and the haze around it
  float sky = exp(-r/.07);
  c = mix(c, mix(plaster, u_pal[5], .6), s01(.25, .02, r));
  c = mix(c, u_pal[5], sky);
  c = dim(c, .1*s01(1., 2.4, r));
  return c;
}`,
});

SW.add({
  id: 'rows', name: 'Rows', tier: 'balanced', grain: .3,
  desc: 'A field of lavender at the end of the day, its rows running away to a soft horizon and the low light catching the top of every row.',
  opt: { label: 'Sun height', def: .5 },
  roles: [[.26, .06, .60, .08], [.36, .09, .68, .10], [.46, .10, .76, .09], [.62, .08, .88, .06], [.78, .07, .95, .04], [.32, .05, .66, .07]],
  packs: [
    { n: 'Provence', k: 'rich', h: [262, 295, 300, 320, 40, 150], hl: [235, 295, 300, 320, 50, 120], c: 1.2 },
    { n: 'Heather', h: [262, 330, 335, 345, 20, 160], hl: [240, 330, 335, 345, 30, 140] },
    { n: 'Tea', h: [235, 160, 150, 140, 90, 170] },
    { n: 'Tulip', h: [275, 355, 5, 340, 30, 150] },
    { n: 'Dusk', k: 'quiet', h: [250, 265, 270, 280, 300, 250], c: .4 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -9, light: [-1, .15] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .8] },
    dusk: { l: -.028, c: 1.17, h: 12, light: [1, .1] },
    night: { l: -.14, c: .8, h: -7, light: [.3, .8] },
  },
  persp: { def: 1, depth: .8 },
  src: `
vec3 wall(vec2 p){
  float hz = .2;
  float sh = mix(.3, 1., u_opt);
  vec2 sun = vec2(-1.05, hz + mix(.04, .3, u_opt));
  float y = p.y - hz;
  vec3 sky = mix(u_pal[4], u_pal[3], s01(.0, .3, y));
  sky = mix(sky, u_pal[0], s01(.25, 1.1, y));
  sky = mix(sky, u_pal[4], .6*exp(-length((p - sun)*vec2(.5, 1.))*2.));
  sky = mix(sky, lift(u_pal[4], .05), cover(length(p - sun) - .05)*.9);
  if (y >= 0.) return sky;
  float dz = -y;
  float Z = .55/(dz + .004);
  // the rows run straight away from you, a little to the right of the frame
  float X = (p.x - .35)*Z;
  float rw = .9;
  float u = X/rw - floor(X/rw);
  float prof = sat(1. - pow(abs(u - .5)*2.08, 3.));
  // each row is a line of round bushes: lumpy along its length
  float bump = .5 + .5*gn(vec2(floor(X/rw)*3.1, Z*1.6));
  float top = prof*(.8 + .25*bump);
  float soil = 1. - s01(.0, .12, top);
  vec3 bush = mix(u_pal[1], u_pal[2], top);
  bush.x += (.05*gn(vec2(X*7., Z*11.)) + .03*gn(vec2(X*19., Z*30.)))*s01(.0, .3, dz);
  bush = mix(bush, u_pal[0], .35*(1. - top));
  // the low sun lights the side of each row that faces it, and the tops
  float face = sat((.5 - u)*2.)*top;
  bush = mix(bush, u_pal[3], sat(face*.55*sh + .2*top*sh)*(1. - .5*s01(.0, .6, dz)));
  vec3 ground = mix(u_pal[5], dim(u_pal[5], .2), .5 + .5*gn(vec2(X*3., Z*2.)));
  vec3 c = mix(bush, ground, soil*s01(.0, .15, dz));
  // far away the rows merge into one soft colour
  float far = exp(-dz*9.);
  c = mix(c, mix(u_pal[2], u_pal[4], .3), far*.9);
  c = mix(c, sky, exp(-dz*40.)*.8);
  return c;
}`,
});

SW.add({
  id: 'shoji', name: 'Shoji', tier: 'balanced', grain: .25,
  desc: 'Paper screens lit from the garden side, their slender wooden lattice crossing the glow and the soft shadow of a maple branch resting on the paper.',
  opt: { label: 'Shadow', def: .5 },
  roles: [[.18, .04, .40, .05], [.26, .05, .55, .05], [.36, .06, .80, .05], [.46, .07, .90, .04], [.56, .07, .96, .03], [.34, .05, .70, .06]],
  packs: [
    { n: 'Lantern', k: 'rich', h: [262, 255, 350, 8, 22, 300], hl: [40, 35, 60, 75, 85, 300], c: 1.2 },
    { n: 'Moss', h: [215, 200, 175, 165, 155, 160], hl: [215, 200, 120, 105, 95, 160] },
    { n: 'Sakura', h: [300, 320, 350, 10, 25, 330] },
    { n: 'Indigo', h: [262, 255, 245, 235, 225, 270] },
    { n: 'Rice', k: 'quiet', h: [250, 255, 260, 80, 85, 250], c: .3 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.072, h: 5.4, light: [-.9, .25] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.017, c: 1.102, h: -7.2, light: [.9, .2] },
    night: { l: -.1, c: .84, h: 4.2, light: [.2, .85] },
  },
  persp: { def: 0, depth: .4 },
  src: `
float branch(vec2 p){
  // a few curving twigs and clusters of palmate leaves, as a soft shadow
  float d = 9.;
  vec2 q = p - vec2(1.6, .9);
  for (int i = 0; i < 4; i++) {
    float fi = float(i);
    vec2 dir = normalize(vec2(-1., -.35 - .25*fi));
    float u = clamp(dot(q, dir), 0., 1.2 - .2*fi);
    vec2 c = dir*u + vec2(0., .06*sin(u*3. + fi));
    d = min(d, length(q - c) - .012*(1.3 - u*.5));
    for (int j = 0; j < 3; j++) {
      float fj = float(j);
      vec2 lc = dir*(.35 + .3*fj) + vec2(.05*sin(fi*3. + fj), .08*cos(fi + fj*2.));
      vec2 lq = q - lc;
      float a = atan(lq.y, lq.x) + fi + fj;
      float r = length(lq);
      float leaf = r - .1*(.55 + .45*pow(abs(cos(a*2.5)), .6));
      d = min(d, leaf);
    }
  }
  return d;
}
vec3 wall(vec2 p){
  float sh = mix(.35, 1., u_opt);
  // the glow of the paper, brightest where the garden light is strongest
  vec3 paper = mix(u_pal[2], u_pal[4], s01(-1.2, 1.2, p.x*.5 + p.y*.6 + .2));
  vec2 w = p/cssu();
  paper.x += .008*gn(w*.06) + .006*gn(w*.25)*0. + .01*fbm3(p*6.);
  // the shadow of the branch: soft, because it is a little way behind the paper
  float bd = branch(p);
  float s = s01(.06, -.03, bd)*sh;
  paper = mix(paper, mix(u_pal[1], u_pal[2], .5), .55*s);
  // the lattice: thin wooden bars, with the sliding panels' wider frames
  vec2 g = vec2((p.x + 2.)/.32, (p.y + 1.)/.26);
  vec2 f = abs(g - floor(g + .5));
  float bar = cssu()*1.6/.3;
  float lat = max(s01(bar*1.5, bar*.6, f.x), s01(bar*1.8, bar*.7, f.y));
  float frame = s01(.035, .025, abs(p.x - .55)) + s01(.035, .025, abs(p.x + 1.05));
  vec3 wood = mix(u_pal[5], u_pal[2], .3);
  vec3 c = mix(paper, wood, lat*.75);
  c = mix(c, dim(wood, .1), sat(frame));
  c = dim(c, .12*s01(.8, 2.2, length(p*vec2(.6, 1.))));
  return c;
}`,
});

SW.add({
  id: 'iceberg', name: 'Iceberg', tier: 'balanced', grain: .3,
  desc: 'Icebergs adrift on a glassy polar sea in the long dusk, their faceted sides glowing blue, each one doubled in the still water.',
  opt: { label: 'Haze', def: .5 },
  roles: [[.20, .05, .58, .07], [.30, .06, .70, .07], [.46, .08, .84, .06], [.62, .07, .92, .04], [.80, .05, .97, .02], [.70, .08, .90, .06]],
  packs: [
    { n: 'Polar', k: 'rich', h: [255, 240, 215, 210, 220, 330], hl: [240, 225, 205, 205, 215, 20], c: 1.25 },
    { n: 'Aqua', h: [230, 215, 195, 185, 190, 200] },
    { n: 'Lilac', h: [280, 270, 255, 250, 260, 320] },
    { n: 'Midnight', h: [265, 255, 235, 225, 230, 280] },
    { n: 'Mist', k: 'quiet', h: [245, 240, 230, 225, 230, 240], c: .35 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -9, light: [-1, .15] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .8] },
    dusk: { l: -.028, c: 1.17, h: 12, light: [1, .1] },
    night: { l: -.14, c: .78, h: -7, light: [.3, .8] },
  },
  persp: { def: 1, depth: .8 },
  src: `
float bergH(float x, vec3 b){
  // a tabular or rounded berg: broad, with a ragged faceted top
  float u = (x - b.x)/b.y;
  // steep calving faces at both ends, a broad top that tilts a little and is broken into steps
  float side = s01(1., .82 + .06*gn(vec2(x*8., b.z)), abs(u));
  float top = .78 + .14*u*(fract(b.z*13.) - .5)*2. + .06*floor(2.5 + 2.5*gn(vec2(x*2.2, b.z*5.)))/5. + .03*gn(vec2(x*14., b.z));
  return b.y*b.z*top*side*step(abs(u), 1.);
}
float bergF(float x, float y, vec3 b){
  // the berg's face: flat facets, each turned a little differently to the low light
  vec2 q = vec2(x*6., y*4.5) + b.z*9.;
  vec2 n = floor(q), f = q - n;
  float md = 8., id = 0.;
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    vec2 g = vec2(float(i), float(j));
    vec2 r = g + h22(n + g) - f;
    float d = dot(r, r);
    if (d < md) { md = d; id = h21(n + g); }
  }
  return id*1.6 - .8 + .15*gn(vec2(x*30., y*30.));
}
vec3 wall(vec2 p){
  float hz = -.05;
  float hzz = mix(.25, .85, u_opt);
  vec3 sky = mix(u_pal[5], u_pal[1], s01(.0, .45, p.y - hz));
  sky = mix(sky, u_pal[0], s01(.35, 1.1, p.y - hz));
  float dz = hz - p.y;
  bool water = dz > 0.;
  float yy = water ? hz + dz : p.y;
  vec3 col = sky;
  if (water) {
    col = mix(u_pal[5], u_pal[1], s01(.0, .45, dz));
    col = mix(col, u_pal[0], s01(.35, 1.1, dz));
    col = dim(col, .08 + .1*s01(.0, .8, dz));
    col.x += .01*gn(vec2(p.x*2., dz*80./(dz + .1)));
  }
  for (int k = 0; k < 4; k++) {
    float fk = float(k);
    vec3 b = k == 0 ? vec3(-1.15, .32, .55) : k == 1 ? vec3(.85, .55, .62) : k == 2 ? vec3(1.75, .22, .5) : vec3(-.15, .1, .6);
    float far = k == 1 ? 0. : k == 3 ? 1. : .5;
    float bb = hz;
    float h = bergH(p.x + (water ? .003*gn(vec2(p.y*90., fk)) : 0.), b);
    float up = yy - bb;
    float a = cover(up - h)*step(.001, h);
    float fct = bergF(p.x, yy, b);
    vec3 ice = mix(u_pal[3], u_pal[4], .3 + .45*s01(-.6, .8, fct));
    ice = mix(ice, u_pal[2], .25*s01(.1, -.6, fct) + .3*s01(.6*h, .0, up));
    ice.x += .025*gn(vec2(p.x*60., yy*4.));
    ice.x += .03*exp(-abs(h - up)/.01);
    ice = mix(ice, sky, hzz*(.15 + .5*far));
    if (water) ice = mix(dim(ice, .15), col, .25);
    col = mix(col, ice, a);
    // the drowned foot of the berg glowing turquoise just under the surface
    float foot = water ? exp(-dz/.05)*s01(1.25, .9, abs(p.x - b.x)/b.y)*(1. - a) : 0.;
    col = mix(col, u_pal[2], .45*foot*(1. - far*.6));
  }
  return col;
}`,
});

SW.add({
  id: 'rosette', name: 'Rosette', tier: 'balanced', grain: .25,
  desc: 'A succulent seen from straight above: thick spoon-shaped leaves spiralling out from its heart, dusted with a pale bloom and blushing at the tips.',
  opt: { label: 'Blush', def: .5 },
  roles: [[.24, .03, .80, .02], [.34, .05, .62, .06], [.48, .06, .72, .06], [.62, .06, .82, .05], [.60, .10, .76, .10], [.30, .03, .90, .015]],
  packs: [
    { n: 'Echeveria', k: 'rich', h: [250, 175, 165, 160, 350, 80], hl: [80, 175, 165, 160, 350, 80], c: 1.25 },
    { n: 'Lilac', h: [262, 285, 290, 295, 340, 70] },
    { n: 'Jade', h: [235, 155, 145, 135, 20, 90] },
    { n: 'Frost Blue', h: [250, 215, 210, 205, 300, 240] },
    { n: 'Ghost', k: 'quiet', h: [250, 220, 215, 210, 330, 250], c: .35 },
  ],
  dyn: {
    dawn: { l: .01, c: 1.06, h: 4.5, light: [-.55, .8] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.014, c: 1.085, h: -6, light: [-.15, .85] },
    night: { l: -.1, c: .84, h: 3.5, light: [.1, .9] },
  },
  persp: { def: 1, depth: .6 },
  src: `
vec3 wall(vec2 p){
  vec2 C = vec2(.72, -.12);
  // fine pale gravel all round
  vec2 w = p/cssu();
  vec3 col = mix(u_pal[0], u_pal[5], .45 + .2*gn(w*.05));
  col.x += .03*gn(w*.18) + .02*gn(w*.5);
  float bl = mix(.2, 1., u_opt);
  const float N = 34.;
  for (int k = 33; k >= 0; k--) {
    float i = float(k);
    float t = (i + .5)/N;
    float a = i*2.39996 + .3;
    vec2 dir = vec2(cos(a), sin(a));
    float L = .16 + 1.05*pow(t, .8);
    float W = L*(.42 - .1*t);
    vec2 q = p - C;
    float u = dot(q, dir)/L;
    float v = dot(q, vec2(-dir.y, dir.x));
    // a thick leaf: narrow at its base, broad near the end, drawn to a short point
    float prof = W*pow(sat(u), .55)*pow(sat(1. - u), .42)*1.45;
    float d = abs(v) - prof;
    float inL = step(0., u)*step(u, 1.);
    float a1 = s01(pxu()*1.2, -pxu()*1.2, d)*inL;
    // the soft shadow it lays on what lies under it
    float sh = s01(.06, -.01, d + .015)*inL*(1. - a1);
    col = dim(col, .35*sh);
    if (a1 > 0.) {
      float x = v/max(prof, 1e-3);
      // cupped: the midline sits low and the rims catch the light
      vec3 n = normalize(vec3(-dir.y*x*1.4 - dir.x*(u - .55)*1.2, dir.x*x*1.4 - dir.y*(u - .55)*1.2, 1.));
      float kk = sat(dot(n, normalize(vec3(-.5, .6, .65))));
      kk = kk*kk*(3. - 2.*kk);
      vec3 c = mix(u_pal[1], u_pal[3], .3 + .6*kk);
      c = mix(c, u_pal[2], .35*(1. - t));
      // the waxy bloom, paler on the older outer leaves
      c = mix(c, mix(u_pal[3], u_pal[5], .3), .3*t*s01(.2, 1., kk));
      // the blush at the tip and along the rim
      float tip = s01(.75, 1., u) + .35*s01(.75, 1., abs(x))*s01(.4, .9, u);
      c = mix(c, u_pal[4], sat(tip*bl*.85));
      c.x -= .05*s01(.15, .0, u)*(1. - t);
      c.x += .012*gn(q*40. + i);
      col = mix(col, c, a1);
    }
  }
  col = dim(col, .1*s01(.9, 2.2, length(p - C)));
  return col;
}`,
});

SW.add({
  id: 'moonrise', name: 'Moonrise', tier: 'balanced', grain: .3,
  desc: 'A full moon rising over a low far coast at dusk, warmed by the thick air near the horizon, a thin band of cloud drifting across it.',
  opt: { label: 'Moon height', def: .45 },
  roles: [[.20, .05, .62, .08], [.32, .07, .74, .07], [.48, .08, .86, .05], [.64, .06, .93, .03], [.86, .05, .98, .015], [.24, .04, .66, .05]],
  packs: [
    { n: 'Dusk', k: 'rich', h: [262, 280, 320, 20, 70, 255], hl: [240, 230, 300, 20, 70, 230], c: 1.2 },
    { n: 'Blue Hour', h: [255, 245, 235, 225, 80, 250] },
    { n: 'Rose', h: [285, 305, 335, 10, 50, 280] },
    { n: 'Teal', h: [235, 215, 200, 190, 90, 225] },
    { n: 'Silver', k: 'quiet', h: [250, 250, 250, 250, 80, 250], c: .35 },
  ],
  dyn: {
    dawn: { l: .02, c: 1.12, h: -9 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.028, c: 1.17, h: 12 },
    night: { l: -.14, c: .8, h: -7 },
  },
  persp: { def: 1, depth: .8 },
  src: `
vec3 wall(vec2 p){
  float hz = -.42;
  float y = p.y - hz;
  vec3 sky = mix(u_pal[3], u_pal[2], s01(.0, .25, y));
  sky = mix(sky, u_pal[1], s01(.15, .8, y));
  sky = mix(sky, u_pal[0], s01(.7, 1.5, y));
  vec2 mc = vec2(.85, hz + mix(.18, .85, u_opt));
  float R = .25;
  vec2 q = (p - mc)/R;
  float r = length(q);
  // the glow of the moon in the air
  sky = mix(sky, u_pal[4], .25*exp(-max(r - 1., 0.)*.9));
  // the moon: seas and highlands, the limb a touch darker, warmed and dimmed the lower it hangs
  float maria = s01(.05, .5, fbm3(q*1.3 + 2.) + .2*gn(q*3.));
  vec3 moon = mix(u_pal[4], mix(u_pal[4], u_pal[3], .55), maria*.55);
  moon.x -= .05*pow(r, 4.) - .02*gn(q*9.);
  float low = s01(.9, .2, mc.y - hz);
  moon = mix(moon, u_pal[3], .45*low);
  vec3 col = mix(sky, moon, s01(1. + pxu()/R*1.2, 1. - pxu()/R*1.2, r));
  // a thin band of cloud across the moon and the sky, lit from behind where it crosses the disc
  float cb = exp(-pow((p.y - mc.y + .05 + .06*sin(p.x*1.4))/.035, 2.))*s01(.15, .7, fbm3(vec2(p.x*1.6, p.y*9.)) + .3);
  vec3 cc = mix(u_pal[1], u_pal[2], .5);
  cc = mix(cc, u_pal[4], .45*exp(-max(r - .8, 0.)*2.5));
  col = mix(col, cc, cb*.8);
  // the far low coast and the sea below it, with the moon's path
  float coast = hz + .025 + .02*s01(-.3, .5, gn(vec2(p.x*1.5, 1.))) + .006*gn(vec2(p.x*12., 2.));
  vec3 land = mix(u_pal[5], u_pal[1], .25);
  col = mix(col, land, cover(p.y - coast)*step(hz - .002, p.y));
  if (p.y < hz) {
    float dz = hz - p.y;
    vec3 sea = mix(u_pal[2], u_pal[0], s01(.0, .6, dz));
    float path = exp(-abs(p.x - mc.x)/(.06 + .25*dz))*(.5 + .5*gn(vec2(p.x*8., dz*120./(dz + .05))));
    sea = mix(sea, u_pal[4], .55*path*exp(-dz*1.5));
    col = sea;
  }
  return col;
}`,
});

SW.add({
  id: 'hotspring', name: 'Hot Spring', tier: 'balanced', grain: .3,
  desc: 'A hot spring seen from the air: a deep blue eye ringed by bands of mineral colour, fine streamers running out over pale crust, a breath of steam.',
  opt: { label: 'Steam', def: .4 },
  roles: [[.26, .06, .50, .08], [.40, .07, .64, .08], [.54, .07, .76, .07], [.64, .08, .82, .08], [.60, .08, .74, .09], [.40, .03, .92, .02]],
  packs: [
    { n: 'Prismatic', k: 'rich', h: [245, 215, 175, 95, 20, 250], hl: [245, 215, 175, 95, 45, 80], c: 1.2 },
    { n: 'Opal', h: [260, 230, 195, 330, 300, 260] },
    { n: 'Jade', h: [215, 190, 165, 145, 120, 200] },
    { n: 'Rose', h: [250, 225, 200, 350, 330, 300] },
    { n: 'Silica', k: 'quiet', h: [240, 230, 215, 200, 190, 240], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.072, h: -5.4, light: [-.9, .25] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.017, c: 1.102, h: 7.2, light: [.9, .2] },
    night: { l: -.1, c: .84, h: -4.2, light: [.2, .85] },
  },
  persp: { def: 1, depth: .5 },
  src: `
vec3 wall(vec2 p){
  vec2 C = vec2(1.05, -.5);
  vec2 q = (p - C)*vec2(1., 1.18)/1.7;
  vec2 dq = normalize(q + 1e-5);
  float r = length(q) + .05*gn(dq*2.) + .025*gn(q*3.);
  // bands of colour from the deep eye outwards
  vec3 c = mix(u_pal[0], u_pal[1], s01(.12, .32, r));
  c = mix(c, u_pal[2], s01(.32, .4, r));
  c = mix(c, u_pal[3], s01(.42, .5, r));
  c = mix(c, u_pal[4], s01(.52, .62, r));
  // the mats run out in fine streamers over the crust, following the flow
  float rays = .5 + .3*gn(dq*26. + r*.6) + .15*gn(dq*70. + r*1.5) + .2*fbm3(p*2.);
  float mat = s01(1.35 + .3*gn(dq*3. + 4.), .6, r)*s01(.5, .7, r);
  vec3 crust = u_pal[5];
  crust.x += .03*fbm3(p*3.) + .015*gn(p/cssu()*.2);
  vec3 col = c;
  col = mix(col, mix(crust, u_pal[4], .4*rays), s01(.6, .7, r));
  col = mix(col, crust, s01(.85, 1.6, r - .25*mat*rays));
  // the eye is deep: darker and clearer towards its middle, with the rim of sinter catching the light
  col = dim(col, .25*s01(.25, .0, r));
  col = mix(col, u_pal[5], .3*exp(-pow((r - .345)/.012, 2.)));
  // steam drifting off the water
  float st = s01(.0, .8, fbm(p*1.2 + vec2(3., 1.)))*exp(-r*.9)*mix(.15, .7, u_opt);
  col = mix(col, mix(u_pal[5], u_pal[2], .2), st);
  return col;
}`,
});

SW.add({
  id: 'snowfield', name: 'Snowfield', tier: 'balanced', grain: .3,
  desc: 'Open snow at the blue hour, smoothed by the wind into long soft drifts, the sky still rose above the far edge and the shadows gone blue.',
  opt: { label: 'Drifts', def: .5 },
  roles: [[.18, .05, .62, .07], [.28, .06, .74, .06], [.38, .06, .86, .04], [.48, .05, .93, .025], [.58, .04, .975, .012], [.56, .07, .90, .06]],
  packs: [
    { n: 'Blue Hour', k: 'rich', h: [262, 250, 245, 240, 235, 340], hl: [245, 235, 230, 228, 225, 20], c: 1.25 },
    { n: 'Lilac', h: [285, 275, 270, 265, 260, 320] },
    { n: 'Polar', h: [235, 220, 210, 205, 200, 190] },
    { n: 'Rose', h: [300, 310, 320, 330, 340, 15] },
    { n: 'Pewter', k: 'quiet', h: [250, 248, 246, 244, 242, 260], c: .35 },
  ],
  dyn: {
    dawn: { l: .022, c: 1.132, h: -9, light: [-1, .15] },
    day: { l: 0, c: 1, h: 0, light: [-.4, .8] },
    dusk: { l: -.031, c: 1.187, h: 12, light: [1, .1] },
    night: { l: -.15, c: .78, h: -7, light: [.3, .8] },
  },
  persp: { def: 1, depth: .8 },
  src: `
float snH(vec2 x, float a){
  vec2 w = x + .6*vec2(gn(x*.15), gn(x*.15 + 4.));
  float h = .5*gn(vec2(w.x*.35, w.y*.9)) + .25*gn(vec2(w.x*.8, w.y*1.9) + 3.);
  // sastrugi: the wind leaves sharper lips on one side of each drift
  h += .08*s01(-.2, .5, gn(vec2(w.x*1.6, w.y*3.4) + 7.));
  return h*a;
}
vec3 wall(vec2 p){
  vec3 ro = vec3(0., .9, 0.);
  vec3 rd = normalize(vec3(p.x, p.y - .16, 2.));
  float a = mix(.25, .7, u_opt);
  float y = rd.y;
  vec3 sky = mix(mix(u_pal[5], u_pal[3], .45), u_pal[3], s01(-.02, .25, y));
  sky = mix(sky, u_pal[1], s01(.1, .5, y));
  sky = mix(sky, u_pal[0], s01(.35, .9, y));
  if (y >= 0.) return sky;
  float t = -ro.y/y;
  for (int i = 0; i < 3; i++) { vec3 x = ro + rd*t; t = (snH(x.xz, a) - ro.y)/y; }
  vec3 x = ro + rd*t;
  float e = .02 + .004*t;
  float h = snH(x.xz, a);
  vec3 n = normalize(vec3(-(snH(x.xz + vec2(e, 0.), a) - h)/e, 1., -(snH(x.xz + vec2(0., e), a) - h)/e));
  // lit only by the sky: the faces turned to the bright west keep a little rose
  float west = sat(dot(n, normalize(vec3(-.9, .45, .6))));
  float up = n.y;
  vec3 c = mix(u_pal[1], u_pal[3], s01(.6, 1., up));
  c = mix(c, u_pal[4], .5*s01(.85, 1., up));
  c = mix(c, u_pal[5], .3*west*west);
  // sparkle of the crust, at its real size
  c.x += .02*gn(vec2(x.x, x.z)*30.)*exp(-t*.15);
  float fog = 1. - exp(-t*.06);
  c = mix(c, mix(sky, u_pal[3], .3), fog*.95);
  return c;
}`,
});

SW.add({
  id: 'eclipse', name: 'Eclipse', tier: 'balanced', grain: .3,
  desc: 'The moment of totality: the moon black against the sun, the pearly corona streaming out round it, and the whole sky gone to twilight.',
  opt: { label: 'Corona', def: .5 },
  roles: [[.16, .04, .40, .08], [.26, .06, .54, .08], [.40, .07, .70, .07], [.70, .05, .90, .03], [.92, .03, .99, .01], [.62, .08, .80, .07]],
  packs: [
    { n: 'Totality', k: 'rich', h: [262, 255, 245, 230, 220, 15], c: 1.2 },
    { n: 'Violet', h: [285, 275, 265, 255, 250, 330] },
    { n: 'Teal', h: [235, 220, 205, 195, 190, 40] },
    { n: 'Rose', h: [300, 310, 320, 330, 350, 10] },
    { n: 'Graphite', k: 'quiet', h: [250, 250, 248, 245, 240, 260], c: .35 },
  ],
  dyn: {
    dawn: { l: .012, c: 1.072, h: -5.4 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.017, c: 1.102, h: 7.2 },
    night: { l: -.1, c: .82, h: -4.2 },
  },
  persp: { def: 1, depth: .5 },
  src: `
vec3 wall(vec2 p){
  vec2 C = vec2(.72, .3);
  float R = .2;
  vec2 q = p - C;
  float r = length(q)/R;
  vec2 dq = normalize(q + 1e-5);
  float co = mix(.5, 1.2, u_opt);
  // the twilight sky of totality, brighter all round the horizon
  vec3 sky = mix(u_pal[2], u_pal[1], s01(-1., -.2, p.y));
  sky = mix(sky, u_pal[0], s01(-.3, .9, p.y));
  sky = mix(sky, u_pal[5], .35*s01(-.6, -1., p.y));
  // the corona: soft streamers, longest along the sun's equator
  float eq = .6 + .4*abs(dot(dq, normalize(vec2(1., .25))));
  float st = .55 + .45*gn(dq*7.) + .25*gn(dq*19.);
  float cor = exp(-(r - 1.)/(.25 + .55*eq*st*co))*s01(.98, 1.02, r);
  float inner = exp(-(r - 1.)/.06)*s01(.98, 1.01, r);
  vec3 col = mix(sky, u_pal[3], sat(cor*.75));
  col = mix(col, u_pal[4], sat(inner*.9));
  // the chromosphere: a thin rose edge showing on one side, and a prominence
  float chrom = exp(-pow((r - 1.005)/.006, 2.))*s01(-.2, .6, dot(dq, normalize(vec2(-.8, -.4))));
  col = mix(col, u_pal[5], .8*chrom);
  vec2 pr = q/R - normalize(vec2(-.7, -.6))*1.02;
  col = mix(col, u_pal[5], .7*exp(-dot(pr, pr)/.0008));
  // the moon
  vec3 moon = dim(u_pal[0], .3);
  return mix(col, moon, s01(1. + pxu()/R*1.2, 1. - pxu()/R*1.2, r));
}`,
});

SW.add({
  id: 'terrazzo', name: 'Terrazzo', tier: 'balanced', grain: .2,
  desc: 'A polished terrazzo floor in the morning light: a few large chips of coloured marble scattered in a wide pale ground, ground flat and glossed, a window reflected softly across it.',
  opt: { label: 'Chips', def: .5 },
  roles: [[.34, .03, .90, .015], [.46, .07, .70, .08], [.54, .07, .78, .07], [.40, .06, .62, .07], [.64, .05, .84, .05], [.42, .03, .95, .01]],
  packs: [
    { n: 'Milano', k: 'rich', h: [262, 25, 160, 250, 70, 255], hl: [80, 25, 160, 250, 70, 80], c: 1.15 },
    { n: 'Sorbet', h: [300, 350, 20, 320, 60, 290], hl: [40, 350, 20, 320, 60, 40] },
    { n: 'Sea', h: [235, 200, 180, 250, 165, 240] },
    { n: 'Sage', h: [220, 140, 110, 160, 90, 200], hl: [100, 140, 110, 160, 90, 100] },
    { n: 'Stone', k: 'quiet', h: [250, 240, 230, 250, 220, 250], c: .3 },
  ],
  dyn: {
    dawn: { l: .008, c: 1.048, h: 3.6, light: [-.55, .8] },
    day: { l: 0, c: 1, h: 0, light: [-.35, .9] },
    dusk: { l: -.011, c: 1.068, h: -4.8, light: [-.15, .85] },
    night: { l: -.08, c: .85, h: 2.8, light: [.1, .9] },
  },
  persp: { def: 0, depth: .3 },
  src: `
// where chips may lie: open ground behind the middle of the window, chips gathering towards the right and the lower corner
float chipMask(vec2 p){
  float calm = s01(.3, 1.15, length((p - vec2(-.3, .2))*vec2(.72, 1.)));
  return (.15 + .85*calm)*(.8 + .25*gn(p*.9 + 3.));
}
// one layer of chips: broken stones from a jittered grid, each its own colour and grain
void chips(inout vec3 c, vec2 p, float sc, float dens, float seed){
  vec2 g = p*sc + seed;
  vec2 cell = floor(g);
  for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
    vec2 cc = cell + vec2(float(i), float(j));
    vec2 ctr = cc + .5 + .5*(h22(cc + seed) - .5);
    float on = step(1. - dens*chipMask((ctr - seed)/sc), h21(cc + seed*3.));
    vec2 q = rot(6.28*h21(cc + 1.3 + seed))*(g - ctr);
    float r = .22 + .2*h21(cc + 7.1 + seed);
    float d = sdBox(q, vec2(r, r*(.6 + .35*h21(cc + 2.2))), .015);
    d = max(d, dot(q, normalize(vec2(1., 1.3))) - r*.78);
    d = max(d, dot(q, normalize(vec2(-1.2, .4 + h21(cc + 5.5)))) - r*.86);
    float a = s01(pxu()*sc*1.1, -pxu()*sc*1.1, d)*on;
    float k = h21(cc + 9.9 + seed);
    vec3 sc3 = k < .3 ? u_pal[1] : k < .55 ? u_pal[2] : k < .78 ? u_pal[3] : u_pal[4];
    // marble inside each chip: a soft cloud and a fine vein, the way a ground stone shows its grain
    sc3.x += .04*(h21(cc + 4.4) - .5) + .035*fbm3(q*3.5 + cc) - .03*exp(-abs(gn(q*2.2 + cc*1.7))/.04);
    // a hairline of the darker cement where the stone meets the ground
    sc3 = mix(sc3, dim(u_pal[0], .2), .35*s01(-pxu()*sc*2.5, 0., d));
    c = mix(c, sc3, a);
  }
}
vec3 wall(vec2 p){
  float dn = mix(.35, .8, u_opt);
  vec3 c = u_pal[0];
  c.x += .008*gn(p/cssu()*.3) + .01*fbm3(p*3. + 2.);
  // fine aggregate in the ground, very faint
  vec2 w = p/cssu();
  float grit = step(.9, h21(floor(w*.45)))*(.4 + .6*chipMask(p));
  c = mix(c, u_pal[5], .16*grit);
  vec3 base = c;
  chips(c, p, 15., dn*.35, 2.);
  c = mix(base, c, .35);
  base = c;
  chips(c, p, 6.5, dn*.42, 5.);
  c = mix(base, c, .65);
  chips(c, p, 2.6, dn*.55, 11.);
  // the polish: a soft window reflected in the floor and a gentle falloff to the corners
  float win = s01(.35, .0, abs(p.x*.45 - p.y + .2) - .3)*s01(1.6, .4, abs(p.x + .3));
  c = lift(c, .04*win);
  c = dim(c, .1*s01(.7, 2.2, length(p*vec2(.6, 1.))));
  return c;
}`,
});

SW.add({
  id: 'lantern', name: 'Lantern', tier: 'balanced', grain: .3,
  desc: 'A paper lantern glowing in a dim room at dusk: fine ribs showing through the paper, the paper folded over a thin wire rim at each end, its warm light on the wall behind.',
  opt: { label: 'Glow', def: .5 },
  roles: [[.18, .04, .58, .06], [.28, .06, .70, .06], [.62, .10, .80, .10], [.78, .09, .90, .07], [.92, .05, .98, .025], [.38, .07, .76, .07]],
  packs: [
    { n: 'Washi', k: 'rich', h: [262, 280, 40, 65, 85, 320], hl: [240, 260, 40, 65, 85, 320], c: 1.2 },
    { n: 'Rose', h: [270, 290, 0, 20, 45, 300] },
    { n: 'Moon', h: [250, 245, 220, 210, 200, 260] },
    { n: 'Jade', h: [215, 200, 160, 140, 110, 190] },
    { n: 'Rice', k: 'quiet', h: [255, 260, 70, 75, 85, 260], c: .35 },
  ],
  dyn: {
    dawn: { l: .014, c: 1.084, h: 6.3 },
    day: { l: 0, c: 1, h: 0 },
    dusk: { l: -.02, c: 1.119, h: -8.4 },
    night: { l: -.12, c: .82, h: 4.9 },
  },
  persp: { def: 1, depth: .7 },
  src: `
float ell(vec2 q, vec2 r){ return (length(q/r) - 1.)*min(r.x, r.y); }
vec3 wall(vec2 p){
  float gw = mix(.6, 1.2, u_opt);
  vec2 C = vec2(.82, .1);
  float R = .5, sq = .86;
  float px = pxu()/R;
  // the room: a plain wall, warmed by the lantern's light where it is near
  vec3 col = mix(u_pal[0], u_pal[1], s01(-1.3, 1., p.y*.4 - p.x*.2));
  float dl = length((p - C)*vec2(.8, 1.));
  col = mix(col, u_pal[5], sat(.85*gw*exp(-dl*1.1)));
  col = mix(col, u_pal[2], sat(.45*gw*exp(-dl*2.)));
  vec2 q = (p - C)/R;
  q.y /= sq;
  // the paper is cut off at each end: a ring of wire holds the opening, seen a little from below
  float yc = .8, wc = sqrt(1. - yc*yc), tilt = .16;
  vec2 qt = q - vec2(0., yc), qb = q + vec2(0., yc);
  // the cord and the wire bail that carries the lantern from the top ring
  vec3 wire = dim(u_pal[0], .3);
  float cy = yc*sq*R + C.y;
  col = mix(col, wire, s01(.006, .002, abs(p.x - C.x))*step(cy + .09, p.y));
  vec2 bp = p - vec2(C.x, cy + .09);
  float bail = abs(abs(bp.x) - (wc*R)*sat(-bp.y/.09)) - .0015;
  col = mix(col, wire, s01(.003, .0, bail)*step(-.09, bp.y)*step(bp.y, 0.));
  float body = s01(px*1.4, -px*1.4, length(q) - 1.)*s01(px, -px, abs(q.y) - yc);
  // the openings: the top one shows the rim from below as a thin line, the bottom one its lit inside
  float capB = s01(px, -px, ell(qb, vec2(wc, wc*tilt)));
  float capT = s01(px, -px, ell(qt, vec2(wc, wc*tilt)))*step(0., qt.y);
  float a = max(body, max(capB, capT));
  if (a > 0.) {
    float r = length(q);
    float z = sqrt(max(1. - r*r, 0.));
    // brightest where the paper faces you and you see the bulb through one layer
    vec3 c = mix(u_pal[2], u_pal[3], s01(.0, .6, z));
    c = mix(c, u_pal[4], s01(.55, 1., z)*.9);
    c = dim(c, .1*s01(.0, -.9, q.y));
    float lat = asin(clamp(q.y, -1., 1.))/1.5708;
    float rib = exp(-pow((fract(lat*9. + .5) - .5)/.06, 2.));
    c = dim(c, (.06 + .14*(1. - z))*rib);
    c.x += .012*gn(q*vec2(40., 8.)) + .01*gn(p/cssu()*.3);
    // light falls off as the paper turns towards each opening, and less of it gets through the doubled fold
    float ay = abs(q.y);
    c = mix(c, mix(u_pal[2], u_pal[1], .3), .45*s01(.45, yc, ay));
    float fold = s01(yc - .075, yc - .055, ay);
    float az = atan(q.x, z + .001);
    float pleat = .5 + .5*sin(az*48.);
    c = mix(c, dim(u_pal[2], .18), fold*(.55 + .2*pleat));
    c.x += .02*fold*pleat*(1. - .5*abs(q.x)/wc);
    c = dim(c, .25*s01(.8, 1., r));
    // the bottom opening: the inside of the paper, lit, deepest at the back
    vec3 inside = mix(u_pal[3], u_pal[4], .6*s01(-.2, .8, -qb.y/(wc*tilt)));
    inside = dim(inside, .25*s01(.6, 1., length(qb/vec2(wc, wc*tilt))));
    c = mix(c, inside, capB*step(-yc, q.y));
    c = mix(c, inside, capB*s01(-yc + px, -yc - px, q.y));
    // the top opening is hidden from below: only the paper folded over the far side of its rim shows
    c = dim(c, .12*capT);
    // the wire rings: a thin dark line with a pin of light where it faces the room
    float wr = min(abs(ell(qt, vec2(wc, wc*tilt))), abs(ell(qb, vec2(wc, wc*tilt))));
    float wline = s01(px*1.6, px*.4, wr);
    float hi = s01(.3, .9, -q.x/wc)*s01(px*.8, 0., wr);
    vec3 wc3 = mix(wire, u_pal[4], .5*hi);
    c = mix(c, wc3, wline);
    col = mix(col, c, a);
  }
  return col;
}`,
});
