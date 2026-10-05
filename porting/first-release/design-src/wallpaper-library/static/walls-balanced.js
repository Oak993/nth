// Static wallpaper library, tier 2: Balanced. Colourful but restrained: real light and material, moderate chroma.
SW.add({
  id: 'meadow', name: 'Meadow', tier: 'balanced', grain: .3,
  desc: 'Rolling hills fading into the haze, each slope turned to the low sun and the crests backlit.',
  opt: { label: 'Haze', def: .55 },
  roles: [[.2, .06, .80, .06], [.45, .08, .93, .035], [.38, .08, .80, .07], [.31, .09, .70, .10], [.24, .09, .60, .12], [.78, .09, .98, .05]],
  packs: [
    { n: 'Spring', k: 'rich', h: [262, 300, 235, 210, 190, 310], hl: [240, 80, 150, 140, 130, 90], c: 1.2 },
    { n: 'Lavender', h: [275, 330, 290, 282, 272, 340], hl: [270, 40, 295, 290, 285, 60] },
    { n: 'Autumn', h: [255, 30, 330, 345, 10, 40], hl: [230, 60, 65, 48, 32, 75] },
    { n: 'Seaside', h: [255, 25, 215, 205, 195, 45], hl: [255, 25, 205, 192, 180, 45] },
    { n: 'Fog', k: 'quiet', h: [240, 260, 220, 210, 200, 260], hl: [230, 70, 160, 150, 140, 80], c: .4 },
  ],
  src: `
float hillM(float x, float i){
  return -.02 - i*.2 + (.1 + .03*i)*sin(x*(.9 + .25*i) + i*2.1) + .06*sin(x*(2.1 + .3*i) - i*1.3) + .012*gn(vec2(x*3., i*7.));
}
vec3 wall(vec2 p){
  vec3 sky = mix(u_pal[1], u_pal[0], sm1((p.y + .1)/1.1));
  vec2 sp = vec2(-.75, .3);
  float sr = length(p - sp);
  sky = mix(sky, u_pal[5], .6*exp(-sr*1.7));
  vec3 col = sky;
  float haze = mix(.35, .9, u_opt);
  for (int k = 0; k < 5; k++) {
    float i = float(k);
    float e = .01;
    float y = hillM(p.x, i);
    float sl = (hillM(p.x + e, i) - y)/e;
    float a = cover((p.y - y)/sqrt(1. + sl*sl));
    float u = i/4.;
    vec3 m = mix(u_pal[2], u_pal[3], sm1(u*2.));
    m = mix(m, u_pal[4], sm1(u*2. - 1.));
    m = mix(m, u_pal[1], (1. - u)*(1. - u)*haze*.8);
    m = mix(m, u_pal[5], .22*(1. - u)*exp(-length(p - sp)*1.3));
    m.x += .045*clamp(sl*.9, -1., 1.)*(.35 + .65*u);
    float dep = max(y - p.y, 0.);
    m = dim(m, .12*s01(.0, .35, dep)*(.5 + u));
    m.x += .05*exp(-dep/.018)*(1. - u*.4);
    m.x += .01*u*gn(p*vec2(28., 80.));
    col = mix(col, m, a);
  }
  return col;
}`,
});

SW.add({
  id: 'dunes', name: 'Dunes', tier: 'balanced', grain: .35,
  desc: 'Wind-carved dunes seen from above, warm on every face that catches the low sun and cool in the lee.',
  opt: { label: 'Sun height', def: .4 },
  roles: [[.20, .05, .55, .07], [.30, .07, .66, .08], [.45, .08, .78, .08], [.62, .09, .90, .06], [.42, .06, .90, .04], [.18, .05, .80, .06]],
  packs: [
    { n: 'Sahara', k: 'rich', h: [285, 300, 48, 70, 60, 250], c: 1.25 },
    { n: 'Rose Sand', h: [300, 320, 10, 32, 20, 280] },
    { n: 'Mars', h: [12, 22, 36, 52, 40, 20] },
    { n: 'Dusk', h: [262, 282, 322, 42, 352, 262] },
    { n: 'White Sands', k: 'quiet', h: [240, 232, 222, 80, 60, 232], c: .45 },
  ],
  src: `
float duneH(vec2 q){
  vec2 w = q + .8*vec2(gn(q*.18 + 3.), gn(q*.18 + 8.));
  float r1 = 1. - abs(gn(w*vec2(.42, .3)));
  float r2 = 1. - abs(gn(w*vec2(.9, .7) + 5.));
  return r1*r1*.9 + r2*r2*.25;
}
vec3 wall(vec2 p){
  float hz = .3;
  vec3 sky = mix(u_pal[4], u_pal[5], sm1((p.y - hz)/(1. - hz)*1.3));
  vec2 sp = vec2(-1.05, hz + .12);
  sky = mix(sky, lift(u_pal[3], .04), .5*exp(-length((p - sp)*vec2(.6, 1.))*3.));
  if (p.y > hz + .003) return sky;
  float z = .6/(hz - p.y + .01);
  vec2 q = vec2(p.x*z, z*1.6);
  float e = .015 + .004*z;
  float h = duneH(q), hx = duneH(q + vec2(e, 0.)), hz2 = duneH(q + vec2(0., e));
  vec3 n = normalize(vec3(-(hx - h)/e*.9, 1., -(hz2 - h)/e*.9));
  float el = mix(.15, .6, u_opt);
  vec3 L = normalize(vec3(-.9, el, .35));
  float dif = dot(n, L);
  float t = sat(dif*1.15 + .1);
  vec3 c = mix(u_pal[0], u_pal[1], sm1(t*3.));
  c = mix(c, u_pal[2], sm1(t*3. - 1.));
  c = mix(c, u_pal[3], sm1(t*3. - 2.));
  c.x += .012*gn(vec2(q.x*14., q.y*5.))*exp(-z*.3);
  float fog = 1. - exp(-z*.06);
  c = mix(c, u_pal[4], fog);
  return mix(c, sky, s01(hz - .004, hz + .003, p.y));
}`,
});

SW.add({
  id: 'frost', name: 'Frost', tier: 'balanced', grain: .25,
  desc: 'Panes of frosted glass floating over soft colour, each blurring what lies beneath and lit along its rim.',
  opt: { label: 'Frost', def: .55 },
  roles: [[.56, .17, .78, .12], [.60, .16, .80, .11], [.50, .16, .74, .12], [.68, .15, .84, .10], [.50, .10, .80, .08], [.20, .05, .93, .02]],
  packs: [
    { n: 'Sorbet', k: 'rich', h: [22, 335, 285, 75, 0, 300] },
    { n: 'Lagoon', h: [200, 245, 170, 282, 0, 232] },
    { n: 'Tropic', h: [165, 30, 200, 80, 0, 222], hl: [95, 42, 145, 62, 0, 85] },
    { n: 'Orchid', h: [302, 332, 262, 352, 0, 292] },
    { n: 'Mist', k: 'quiet', h: [230, 280, 190, 320, 0, 250], c: .4 },
  ],
  src: `
vec3 fieldF(vec2 p, float b){
  vec3 c = mix(u_pal[5], dim(u_pal[5], .12), sat(.5 - p.y*.3));
  float k = 1./(1. + b*2.2);
  vec2 d0 = p - vec2(-.95, .42), d1 = p - vec2(.75, .62), d2 = p - vec2(.18, -.58), d3 = p - vec2(1.3, -.45);
  c = mix(c, u_pal[0], .92*exp(-dot(d0, d0)*1.7*k));
  c = mix(c, u_pal[1], .88*exp(-dot(d1, d1)*2.1*k));
  c = mix(c, u_pal[2], .88*exp(-dot(d2, d2)*1.9*k));
  c = mix(c, u_pal[3], .75*exp(-dot(d3, d3)*2.8*k));
  return c;
}
void pane(inout vec3 col, vec2 p, vec2 c, vec2 hs, float r, float ang, float lvl){
  vec2 q = rot(-ang)*(p - c);
  float d = sdBox(q, hs, r);
  vec2 qs = rot(-ang)*(p - c - vec2(.035, -.07));
  float sh = 1. - s01(-.1, .18, sdBox(qs, hs, r));
  float a = cover(d);
  col = dim(col, mix(.2, .32, u_dark)*sh*(1. - a));
  float e = .003;
  vec2 gr = vec2(sdBox(q + vec2(e, 0.), hs, r) - d, sdBox(q + vec2(0., e), hs, r) - d)/e;
  vec2 n = rot(ang)*gr;
  float rim = exp(min(d, 0.)/.06);
  float fr = mix(.3, 1., u_opt);
  vec3 g = mix(fieldF(p - n*.07*rim, fr*(.7 + lvl*.4)), col, .25);
  g = lift(g, mix(.035, .07, u_opt) + .02*lvl);
  g.x += .025*(q.y/hs.y);
  float lit = dot(n, normalize(vec2(-.6, .8)));
  float band = exp(min(d, 0.)/.005);
  g.x += band*(.1 + .22*max(lit, 0.)) + .045*exp(min(d, 0.)/.035)*max(lit, 0.);
  g.x -= band*.05*max(-lit, 0.);
  g.x += .01*fr*gn(p/cssu()*.4);
  col = mix(col, g, a);
}
vec3 wall(vec2 p){
  vec3 col = fieldF(p, 0.);
  pane(col, p, vec2(-.68, -.3), vec2(.4, .4), .4, 0., 0.);
  pane(col, p, vec2(.42, .06), vec2(.62, .48), .17, .12, 1.);
  pane(col, p, vec2(1.12, .66), vec2(.3, .11), .11, -.18, .5);
  return col;
}`,
});

SW.add({
  id: 'cascade', name: 'Cascade', tier: 'balanced', grain: .3,
  desc: 'Four soft ribbons of colour sweeping across the frame, each one round in section, lit from above and shading the one below.',
  opt: { label: 'Sheen', def: .5 },
  roles: [[.50, .12, .70, .12], [.56, .13, .75, .12], [.62, .12, .80, .11], [.68, .11, .85, .10], [.73, .10, .89, .08], [.17, .035, .93, .02]],
  packs: [
    { n: 'Lagoon', k: 'rich', h: [262, 236, 208, 182, 160, 255], c: 1.25 },
    { n: 'Blossom', h: [350, 10, 30, 330, 300, 340] },
    { n: 'Sunrise', h: [300, 330, 5, 35, 62, 290] },
    { n: 'Grove', h: [190, 168, 148, 128, 100, 200] },
    { n: 'Porcelain', k: 'quiet', h: [250, 235, 220, 270, 205, 245], c: .4 },
  ],
  src: `
float ribY(float x, float i){
  return -.8 + i*.47 + (.2 + .05*i)*sin(x*(.72 + .1*i) + i*1.9 + .3) + .08*sin(x*1.55 - i*2.4);
}
vec3 wall(vec2 p){
  vec3 col = mix(lift(u_pal[5], .03), dim(u_pal[5], .3), sat(.55 - .3*p.y + .12*p.x));
  col = mix(col, u_pal[2], .25*exp(-dot(p - vec2(-.6, .5), p - vec2(-.6, .5))*.5));
  float gl = mix(.25, 1., u_opt);
  vec3 K = normalize(vec3(-.35, .72, .6));
  vec3 H = normalize(K + vec3(0., 0., 1.));
  for (int k = 0; k < 4; k++) {
    float i = float(k);
    float e = .01;
    float y = ribY(p.x, i);
    float sl = (ribY(p.x + e, i) - y)/e;
    float nr = sqrt(1. + sl*sl);
    float w = .21 + .04*sin(p.x*.8 + i*3.1);
    float d = (p.y - y)/nr;
    float ds = (p.y + .06 - ribY(p.x - .04, i))/nr;
    float sh = 1. - s01(w*.55, w + .2, abs(ds));
    float a = 1. - smoothstep(-pxu()*1.2, pxu()*1.2, abs(d) - w);
    col = dim(col, mix(.3, .42, u_dark)*sh*(1. - a));
    float v = clamp(d/w, -1., 1.);
    vec2 ac = vec2(-sl, 1.)/nr;
    vec3 n = normalize(vec3(ac*v, sqrt(max(1. - v*v, 0.)) + .02));
    float kw = sat(dot(n, K)*.62 + .38);
    kw = kw*kw*(3. - 2.*kw);
    vec3 c = mix(u_pal[k], u_pal[k + 1], .5 + .5*sin(p.x*.55 + i*1.7));
    c = vec3(c.x*(.5 + .56*kw), c.yz*(1. + .5*kw*(1. - kw) - .1*kw*kw));
    c.x += .05*sat(-v)*(1. - kw)*s01(.4, 1., abs(v));
    float nh = sat(dot(n, H));
    c.x += gl*(.16*pow(nh, 40.) + .05*pow(nh, 6.));
    c.yz *= 1. - .4*gl*pow(nh, 40.);
    col = mix(col, c, a);
  }
  return col;
}`,
});

SW.add({
  id: 'folio', name: 'Folio', tier: 'balanced', grain: .3,
  desc: 'Large sheets of coloured paper laid over one another, each edge curling up into a soft roll that casts its shadow on the sheet below.',
  opt: { label: 'Curl', def: .5 },
  roles: [[.36, .07, .80, .06], [.46, .09, .86, .07], [.56, .10, .90, .07], [.66, .09, .93, .06], [.80, .05, .975, .025], [.24, .05, .74, .06]],
  packs: [
    { n: 'Coral', k: 'rich', h: [300, 340, 15, 45, 60, 285], c: 1.3 },
    { n: 'Harbour', h: [255, 232, 205, 185, 200, 262] },
    { n: 'Pistachio', h: [215, 175, 140, 110, 100, 230] },
    { n: 'Iris', h: [275, 295, 315, 340, 300, 268] },
    { n: 'Parchment', k: 'quiet', h: [250, 60, 70, 80, 80, 250], c: .4 },
  ],
  src: `
void sheet(inout vec3 col, vec2 p, vec2 C, float Rad, float R, vec3 face){
  float u = length(p - C) - Rad;
  vec2 dir = (p - C)/max(length(p - C), 1e-4);
  float lit = dot(-dir, normalize(vec2(-.5, .8)));
  float sh = exp(-max(u, 0.)/(.45*R + .05))*s01(-.03, .005, u)*(.65 - .35*lit);
  float a = 1. - smoothstep(-pxu()*1.2, pxu()*1.2, u);
  col = dim(col, mix(.3, .42, u_dark)*sh*(1. - a));
  vec3 c = face;
  c.x += .035*s01(.0, -1.6, u) - .02 - .03*dot(p, normalize(vec2(.6, -.8)))*.4;
  c.x += .004*gn(p/cssu()*.3);
  float ui = u + 2.*R;
  c = dim(c, .22*exp(-max(-ui, 0.)/.07)*s01(.0, -.004, ui));
  float s = clamp((u + R)/R, -1., 1.);
  float inR = 1. - smoothstep(-pxu()*1.2, pxu()*1.2, abs(u + R) - R);
  vec3 n = normalize(vec3(-dir*s, sqrt(max(1. - s*s, 0.)) + .02));
  float kw = sat(dot(n, normalize(vec3(-.45, .65, .6)))*.6 + .4);
  kw = kw*kw*(3. - 2.*kw);
  vec3 b = mix(face, u_pal[4], .55);
  b = vec3(b.x*(.62 + .45*kw), b.yz*(1. + .3*(1. - kw)));
  b.x += .07*pow(sat(dot(n, normalize(vec3(-.3, .5, .81)))), 18.);
  c = mix(c, b, inR);
  col = mix(col, c, a);
}
vec3 wall(vec2 p){
  vec3 col = mix(u_pal[0], dim(u_pal[0], .25), sat(.5 - .3*p.y + .2*p.x));
  col.x += .003*gn(p/cssu()*.3);
  float R = mix(.04, .13, u_opt);
  sheet(col, p, vec2(-2.7, -2.5), 3.25, R*1.1, u_pal[1]);
  sheet(col, p, vec2(3.0, 2.6), 3.05, R, u_pal[2]);
  sheet(col, p, vec2(2.9, -2.3), 2.25, R*.85, u_pal[3]);
  return col;
}`,
});

SW.add({
  id: 'ripple', name: 'Ripple', tier: 'balanced', grain: .25,
  desc: 'A still pond after three drops: rings spreading across the water and bending the colour of the sky in it.',
  opt: { label: 'Ripples', def: .5 },
  roles: [[.28, .08, .72, .08], [.33, .10, .76, .09], [.38, .11, .80, .09], [.44, .11, .84, .08], [.50, .10, .88, .07], [.85, .04, .99, .01]],
  packs: [
    { n: 'Lotus', k: 'rich', h: [252, 282, 312, 342, 12, 300], c: 1.3 },
    { n: 'Jade', h: [222, 196, 172, 150, 122, 180] },
    { n: 'Dusk', h: [262, 242, 218, 32, 52, 60] },
    { n: 'Koi', h: [212, 232, 22, 42, 62, 40] },
    { n: 'Pewter', k: 'quiet', h: [240, 230, 220, 210, 200, 220], c: .35 },
  ],
  src: `
float drop(vec2 p, vec2 c, float R, float k){
  float r = length(p - c);
  float x = r - R;
  return (sin(x*k)*exp(-x*x*16.) + .45*sin((r - R*.6)*k*1.15)*exp(-pow(r - R*.6, 2.)*30.))*exp(-r*.8);
}
float hR(vec2 p){
  return drop(p, vec2(.6, .18), .62, 34.) + .8*drop(p, vec2(-.85, -.38), .45, 40.) + .6*drop(p, vec2(.05, -.82), .3, 46.) + .05*gn(p*1.5);
}
vec3 wall(vec2 p){
  float e = .002;
  float A = mix(.0015, .006, u_opt);
  float h = hR(p);
  vec2 g = vec2(hR(p + vec2(e, 0.)) - h, hR(p + vec2(0., e)) - h)/e*A;
  float t = .5 + .14*(p.x + g.x*14.) - .16*(p.y + g.y*14.);
  vec3 c = ramp(t);
  vec3 n = normalize(vec3(-g*3., 1.));
  float sp = pow(sat(dot(n, normalize(vec3(-.4, .5, .77)))), 60.);
  c = mix(c, u_pal[5], sat(sp*.6));
  c.x += .05*(n.y - n.x)*2.;
  return c;
}`,
});

SW.add({
  id: 'contour', name: 'Contour', tier: 'balanced', grain: .2,
  desc: 'A quiet topographic map: hill-shaded terrain tinted by height, traced with fine contour lines.',
  opt: { label: 'Lines', def: .55 },
  roles: [[.22, .06, .84, .05], [.28, .08, .86, .06], [.34, .09, .88, .07], [.40, .09, .90, .06], [.46, .08, .93, .05], [.72, .06, .45, .08]],
  packs: [
    { n: 'Atlas', k: 'rich', h: [235, 210, 185, 160, 140, 222], hl: [205, 165, 118, 70, 32, 222], c: 1.4 },
    { n: 'Glacier', h: [252, 232, 212, 196, 182, 250] },
    { n: 'Desert', h: [22, 36, 50, 66, 82, 16] },
    { n: 'Orchid', h: [282, 302, 322, 342, 2, 292] },
    { n: 'Slate', k: 'quiet', h: [240, 230, 220, 210, 200, 240], c: .3 },
  ],
  src: `
float hC(vec2 p){ return fbm3(p*.42 + vec2(2., 7.))*1.1 + .08*gn(p*1.7) + .22*sin(p.x*.6 + 1.); }
vec3 wall(vec2 p){
  float e = .003;
  float h = hC(p);
  vec2 g = vec2(hC(p + vec2(e, 0.)) - h, hC(p + vec2(0., e)) - h)/e;
  vec3 c = ramp(sat(h*.62 + .5));
  vec3 n = normalize(vec3(-g*.5, 1.));
  c.x += .07*(dot(n, normalize(vec3(-.6, .7, .55))) - .72);
  float st = .07;
  float v = h/st;
  float dl = abs(fract(v + .5) - .5)*st/max(length(g), .02);
  float major = step(4.5, mod(floor(v + .5) + 50., 5.));
  float lw = cssu()*(.55 + .6*major);
  float line = 1. - s01(lw, lw + pxu()*1.3, dl);
  c = mix(c, u_pal[5], line*mix(.2, .6, u_opt)*(.7 + .3*major));
  return c;
}`,
});

SW.add({
  id: 'watercolor', name: 'Watercolor', tier: 'balanced', grain: .25,
  desc: 'Loose washes of pigment bleeding into cold-pressed paper, granulating in its tooth and darker where the colour pooled at each edge.',
  opt: { label: 'Bleed', def: .5 },
  roles: [[.28, .17, .72, .11], [.26, .18, .70, .12], [.30, .16, .78, .10], [.25, .17, .68, .12], [.29, .15, .74, .10], [.37, .025, .965, .014]],
  packs: [
    { n: 'Garden', k: 'rich', h: [10, 340, 85, 245, 160, 265], hl: [10, 340, 85, 245, 160, 80], c: 1.3 },
    { n: 'Ocean', h: [232, 200, 262, 182, 215, 265], hl: [232, 200, 262, 182, 215, 90] },
    { n: 'Autumn', h: [40, 22, 72, 352, 55, 265], hl: [40, 22, 72, 352, 55, 80] },
    { n: 'Lavender', h: [292, 322, 262, 342, 305, 265], hl: [292, 322, 262, 342, 305, 80] },
    { n: 'Ink', k: 'quiet', h: [252, 232, 272, 212, 245, 265], hl: [252, 232, 272, 212, 245, 80], c: .4 },
  ],
  src: `
float washW(vec2 p, vec2 c, float r, float seed, float bleed){
  vec2 q = p + (.2 + .15*bleed)*vec2(fbm3(p*1.1 + seed), fbm3(p*1.1 + seed + 5.));
  vec2 v = rot(seed)*(q - c);
  return (r - length(v*vec2(.72, 1.25)))/r + .32*fbm(p*1.8 + seed*1.7);
}
void glaze(inout vec3 col, vec2 p, vec3 pig, vec3 paper, float f, float tooth, float seed){
  float inside = s01(-.01, .05, f);
  float edge = exp(-max(f, 0.)/.035)*inside;
  float dens = inside*(mix(.42, .62, u_dark) + .2*fbm3(vec2(f*3., seed))) + .45*edge;
  float gran = s01(.0, .8, gn(p/cssu()*.42 + seed*7.) + .6*gn(p/cssu()*.13 + seed));
  float floc = fbm3(p*7. + seed*3.);
  dens *= .72 + .32*tooth + .4*gran*mix(.6, 1., u_dark) + .18*floc;
  dens = sat(dens);
  col = vec3(col.x*mix(1., pig.x/paper.x, dens), col.yz + (pig.yz - paper.yz)*dens);
}
vec3 wall(vec2 p){
  vec3 paper = u_pal[5];
  float tooth = fbm3(p/cssu()*.07);
  vec3 col = paper;
  col.x += .012*tooth;
  float b = u_opt;
  glaze(col, p, u_pal[0], paper, washW(p, vec2(-.85, .35), .78, 1.3, b), tooth, 1.);
  glaze(col, p, u_pal[1], paper, washW(p, vec2(.55, .5), .62, 4.1, b), tooth, 2.);
  glaze(col, p, u_pal[2], paper, washW(p, vec2(.95, -.45), .7, 7.7, b), tooth, 3.);
  glaze(col, p, u_pal[3], paper, washW(p, vec2(-.2, -.55), .55, 9.2, b), tooth, 4.);
  glaze(col, p, u_pal[4], paper, washW(p, vec2(-1.45, -.7), .45, 12.5, b), tooth, 5.);
  return col;
}`,
});

SW.add({
  id: 'arches', name: 'Arches', tier: 'balanced', grain: .3,
  desc: 'Nested arches in mid-century colour under a low sun, each band raised a little off the one inside it.',
  opt: { label: 'Shadow', def: .5 },
  roles: [[.44, .10, .58, .12], [.53, .11, .68, .11], [.62, .10, .77, .09], [.71, .09, .85, .07], [.80, .10, .76, .12], [.20, .03, .94, .025]],
  packs: [
    { n: 'Terracotta', k: 'rich', h: [32, 55, 78, 18, 62, 72], c: 1.25 },
    { n: 'Sage', h: [142, 120, 95, 162, 45, 90] },
    { n: 'Dusk', h: [300, 330, 10, 272, 48, 300] },
    { n: 'Coast', h: [222, 202, 182, 242, 32, 222] },
    { n: 'Clay', k: 'quiet', h: [40, 50, 62, 30, 52, 72], c: .45 },
  ],
  src: `
float archD(vec2 p, vec2 c){ vec2 q = p - c; return q.y > 0. ? length(q) : abs(q.x); }
vec3 bandC(float k){
  vec3 c = u_pal[0];
  c = mix(c, u_pal[1], step(.5, k));
  c = mix(c, u_pal[2], step(1.5, k));
  c = mix(c, u_pal[3], step(2.5, k));
  return c;
}
void arches(inout vec3 col, vec2 p, vec2 c, float r0, float w, float rev){
  float d = archD(p, c);
  float outer = r0 + 4.*w;
  float sk = mix(.12, .4, u_opt);
  float dsh = archD(p - vec2(.03, -.045), c);
  float sh = (1. - s01(outer - .03, outer + .14, dsh))*s01(r0 - .12, r0 + .03, dsh);
  float a = cover(d - outer)*(1. - cover(d - r0));
  col = dim(col, sk*sh*(1. - a));
  float x = (d - r0)/w;
  float k = clamp(floor(x), 0., 3.);
  float f = fract(x);
  float kk = rev > .5 ? 3. - k : k;
  float kp = rev > .5 ? 4. - k : k - 1.;
  vec3 bc = bandC(kk);
  vec3 prev = bandC(clamp(kp, 0., 3.));
  bc = mix(prev, bc, k > .5 ? s01(0., pxu()*1.5/w, f) : 1.);
  bc = dim(bc, sk*.5*exp(-f/.07)*step(.5, k));
  bc.x += .02*(f - .5);
  col = mix(col, bc, a);
}
vec3 wall(vec2 p){
  vec3 col = mix(u_pal[5], dim(u_pal[5], .1), sat(.5 - p.y*.35));
  float sd = length(p - vec2(-.42, .5));
  col = mix(col, u_pal[4], cover(sd - .2));
  arches(col, p, vec2(.62, -.42), .2, .2, 0.);
  arches(col, p, vec2(-1.08, -.78), .1, .12, 1.);
  return col;
}`,
});

SW.add({
  id: 'strata', name: 'Strata', tier: 'balanced', grain: .3,
  desc: 'Wind-swept sandstone: bands of mineral colour sweeping and folding, polished smooth with a soft relief.',
  opt: { label: 'Banding', def: .5 },
  roles: [[.32, .08, .62, .10], [.40, .10, .69, .11], [.48, .11, .76, .10], [.56, .10, .83, .08], [.64, .08, .90, .05], [.16, .04, .50, .06]],
  packs: [
    { n: 'Sandstone', k: 'rich', h: [25, 40, 55, 70, 85, 20], c: 1.35 },
    { n: 'Rosewood', h: [2, 16, 32, 350, 60, 350] },
    { n: 'Slate Blue', h: [222, 242, 262, 202, 82, 242] },
    { n: 'Jade', h: [152, 172, 192, 132, 82, 172] },
    { n: 'Bone', k: 'quiet', h: [55, 60, 65, 70, 75, 50], c: .4 },
  ],
  src: `
float sS(vec2 p){
  vec2 q = p*.9;
  q += .38*vec2(fbm3(q*.8), fbm3(q*.8 + 4.));
  return q.y*2.1 + .55*sin(q.x*1.25 + q.y*.8) + .28*fbm(q*1.2);
}
vec3 wall(vec2 p){
  float e = .003;
  float s = sS(p);
  vec2 g = vec2(sS(p + vec2(e, 0.)) - s, sS(p + vec2(0., e)) - s)/e;
  float bd = mix(.4, 1., u_opt);
  float rel = sin(s*7.)*.5 + sin(s*17. + 1.)*.2;
  vec3 c = ramp(.5 + .48*sin(s*1.15 + .3));
  c.x += bd*(.035*sin(s*7.) + .018*sin(s*17. + 1.) + .01*sin(s*41.));
  float gl = length(g);
  vec2 gd = g/max(gl, 1e-3);
  float sh = dot(gd, normalize(vec2(-.6, .8)))*cos(s*7.);
  c.x += .03*bd*sh;
  float groove = exp(-abs(fract(s*.6) - .5)*40.)*bd;
  c = mix(c, u_pal[5], .2*groove);
  c = dim(c, .1*s01(.5, 2.2, length(p*vec2(.7, 1.))));
  return c;
}`,
});
