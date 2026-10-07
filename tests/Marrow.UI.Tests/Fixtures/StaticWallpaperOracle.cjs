// Independent oracle: execute the approved page, never the C# implementation.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const dir = path.join(root, 'porting/first-release/design-src/wallpaper-library/static');
const policy = require(path.join(root, 'tools/wallpaper-fidelity/no-brown-policy.cjs'));
const colourContext = { module: { exports: {} }, Math };
vm.runInNewContext(policy.patch('static/sw-color.js', fs.readFileSync(path.join(dir, 'sw-color.js'), 'utf8')), colourContext);
const C = colourContext.module.exports;
const context = { window: {}, SWC: C, Float32Array, Math };
vm.runInNewContext(fs.readFileSync(path.join(dir, 'sw-gl.body.js'), 'utf8'), context);
const G = context.window.SWG;
const catalogue = JSON.parse(fs.readFileSync(path.join(dir, 'wallpapers.json'), 'utf8'));
const accents = ['#005FB8', '#C42B3A', '#D4501A', '#B88A00', '#5D6A7B'];
const palette = (p, dark) => Float32Array.from(p[dark ? 'dark' : 'light'].flatMap(C.hex2lab));
const rows = catalogue.wallpapers.map(w => ({
  id: w.id, name: w.name,
  dynamic: w.packs.flatMap((pack, pi) => [false, true].flatMap(dark => [0, 6, 12, 18, 3, 23.5].map(hour => {
    const s = G.dynState(w, palette(pack, dark), +dark, hour, 1, new Float32Array(18));
    return { pack: pi, dark, hour, pal: Array.from(s.pal), night: s.dark, light: s.light };
  }))),
  accent: accents.map(hex => ({ hex, ...C.resolvePack(w.roles, C.accentSpec(w.roles, w.hues, hex)) })),
  dynamicAccent: accents.flatMap(hex => {
    const pack = C.resolvePack(w.roles, C.accentSpec(w.roles, w.hues, hex));
    return [false, true].flatMap(dark => [0, 6, 12, 18, 3, 23.5].map(hour => {
      const s = G.dynState(w, palette(pack, dark), +dark, hour, 1, new Float32Array(18));
      return { hex, dark, hour, pal: Array.from(s.pal), night: s.dark, light: s.light };
    }));
  }),
}));
const roles = [[.459, .049, .8, .13], [.459, .05, .8, .13], [.46, .084, .8, .13], [.699, .084, .8, .13], [.7, .084, .8, .13], [.17, .06, .95, .02]];
const warm = [20, 20.01, 50, 78, 78.01, 119.99, 120].map(h => ({ h, roles, ...C.resolvePack(roles, { h: [h], c: 1 }) }));
const focus = ['riptide', 'knit'].flatMap(id => [0, .03, .04, .37, 1].map(f => {
  const wall = catalogue.wallpapers.find(w => w.id === id);
  const viewer = { wall, pal: palette(wall.packs[0], true), dark: 1, clock: 0, dyn: 0, palOut: new Float32Array(18),
    persp: 1, tilt: [1, 1], focus: f, px: [32, 20] };
  return { id, f, ...G.Viewer.prototype.state.call(viewer).x };
}));
// BLUR_FS's two separable 13-tap passes, independently evaluated on an 8-bit test texture.
// The last-pass dither is bounded separately in the test (one byte); grain is zero here.
const W = 32, H = 20, sigma = G.FOCUS_SIG * H, dream = 1;
const input = Array.from({ length: W * H * 4 }, (_, i) => {
  const x = (i >> 2) % W, y = Math.floor((i >> 2) / W), channel = i % 4;
  return channel === 3 ? 255 : channel === 0 ? (x < W / 2 ? 0 : 255) : channel === 1 ? (y < H / 2 ? 0 : 255) : (x === W / 2 && y === H / 2 ? 255 : 0);
});
function pass(src, horizontal, last) {
  const out = new Array(src.length);
  const sample = (x, y, channel) => {
    x = Math.max(0, Math.min(W - 1, x)); y = Math.max(0, Math.min(H - 1, y));
    const ix = Math.floor(x), iy = Math.floor(y), fx = x - ix, fy = y - iy;
    const at = (xx, yy) => src[(Math.min(H - 1, yy) * W + Math.min(W - 1, xx)) * 4 + channel] / 255;
    return (at(ix, iy) * (1 - fx) + at(ix + 1, iy) * fx) * (1 - fy) + (at(ix, iy + 1) * (1 - fx) + at(ix + 1, iy + 1) * fx) * fy;
  };
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    for (let c = 0; c < 3; c++) {
      let s = 0, ws = 0;
      for (let i = -6; i <= 6; i++) {
        const k = i * .5, weight = Math.exp(-.5 * k * k);
        s += weight * sample(x + (horizontal ? k * sigma : 0), y + (horizontal ? 0 : k * sigma), c); ws += weight;
      }
      let v = s / ws;
      if (last) v += ((1 - (1 - v) * (1 - v)) - v) * dream * .16;
      out[(y * W + x) * 4 + c] = Math.round(v * 255);
    }
    out[(y * W + x) * 4 + 3] = 255;
  }
  return out;
}
const blur = { W, H, sigma, dream, input, expected: pass(pass(input, true, false), false, true) };
const wall = catalogue.wallpapers.find(w => w.id === 'riptide');
const eng = { ...wall, grain: 0, opt: { def: 0 } };
const viewer = new G.Viewer({ getContext: () => null }, eng);
viewer.px = [32, 20]; viewer.H = 20; viewer.k = 1;
viewer.target({ pal: palette(wall.packs[0], true), dark: 1, grain: 0, opt: 0, focus: 0, dyn: 0, clock: 12, persp: 1, tilt: [0, 0] }); viewer.snap();
viewer.target({ pal: palette(wall.packs[1], false), dark: 0, grain: 1, opt: 1, focus: .37, dyn: 1, clock: 18, persp: 1, tilt: [1, 1] });
const motion = [];
for (const dt of [1 / 60, 1 / 60, .1, .2, .55, 1.5]) {
  viewer.tick(dt); const state = viewer.state();
  motion.push({ dt, palette: Array.from(state.pal), dark: state.dark, grain: viewer.grain, option: viewer.opt,
    light: state.x.light, tilt: state.x.tilt, focus: state.x.focus, sigma: state.x.blur, dream: state.x.dream });
}
const midnightView = new G.Viewer({ getContext: () => null }, eng); midnightView.px = [32, 20];
midnightView.target({ pal: palette(wall.packs[0], true), dark: 1, grain: 0, opt: 0, focus: 0, dyn: 1, clock: 23 + 59 / 60, persp: 0, tilt: [0, 0] }); midnightView.snap();
// sw-page.js's midnight key uses 24 when clock>12, preventing a backwards traversal through noon.
midnightView.target({ clock: 24 }); midnightView.tick(.16);
const midnightState = midnightView.state();
const midnight = { palette: Array.from(midnightState.pal), dark: midnightState.dark, light: midnightState.x.light };
fs.writeFileSync(path.join(__dirname, 'static-wallpaper-page.json'), JSON.stringify({ rows, warm, focus, blur, motion, midnight }) + '\n');
