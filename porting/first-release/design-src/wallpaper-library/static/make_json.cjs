// Builds src/wallpapers.json, the port data: the shader contract (head, library, main), and per wallpaper its source,
// its option, its grain and its colour packs resolved to hex for dark and for light. measured.json (written by
// check.cjs) adds the measured single-render cost at 2560 x 1600. The page reads the same JSON from its own block.
const fs = require('fs'), path = require('path'), vm = require('vm');
const HERE = __dirname;
const ctx = { window: {}, globalThis: null, module: undefined };
ctx.globalThis = ctx.window; vm.createContext(ctx);
for (const f of ['sw-color.js', 'sw-core.js', 'walls-vibrant.js', 'walls-balanced.js', 'walls-minimal.js']) {
  const code = fs.readFileSync(path.join(HERE, f), 'utf8');
  vm.runInContext(f.startsWith('walls') ? 'var SW = window.SW;\n' + code : code, ctx, { filename: f });
}
const SW = ctx.window.SW, SWC = ctx.window.SWC;
let measured = {};
try { measured = JSON.parse(fs.readFileSync(path.join(HERE, 'measured.json'), 'utf8')); } catch (e) { /* not measured yet */ }
const TIERS = [
  { id: 'vibrant', name: 'Vibrant', desc: 'Rich and bold: saturated colour, strong light and deep shadow.' },
  { id: 'balanced', name: 'Balanced', desc: 'Colourful but restrained: real materials and soft light, moderate colour.' },
  { id: 'minimal', name: 'Minimal', desc: 'Calm and low in contrast: one quiet idea, soft colour, nothing that pulls the eye.' },
];
const ids = new Set();
const walls = SW.list.map((w) => {
  if (ids.has(w.id)) throw new Error('duplicate id ' + w.id); ids.add(w.id);
  if (w.roles.length !== 6) throw new Error(w.id + ' needs 6 roles');
  if (w.packs.length < 3 || w.packs.length > 5) throw new Error(w.id + ' needs 3 to 5 packs');
  if (!w.packs.some((p) => p.k === 'quiet') || !w.packs.some((p) => p.k === 'rich')) throw new Error(w.id + ' needs a quiet and a rich pack');
  for (const bad of [/#\s*(define|if|ifdef|include)/, /\btexture\s*\(/, /\bdFd[xy]\b|\bfwidth\b/, /<<|>>|[^&]&[^&]|\^|\|(?!\|)/, /\bswitch\b|\bwhile\b|\bdo\b/]) {
    if (bad.test(w.src)) throw new Error(w.id + ' uses something that does not port: ' + bad);
  }
  const m = measured.rows && measured.rows[w.id];
  return {
    id: w.id, name: w.name, tier: w.tier, desc: w.desc,
    grain: { label: 'Grain', def: w.grain, min: 0, max: 1 },
    option: { label: w.opt.label, def: w.opt.def, min: 0, max: 1, uniform: 'u_opt' },
    defaultPack: 0,
    thumbTexScale: w.thumbTex || 1,
    packs: w.packs.map((p) => { const r = SWC.resolvePack(w.roles, p); return { name: p.n, kind: p.k || 'standard', dark: r.dark, light: r.light }; }),
    src: w.src.trim() + '\n',
    cost: m ? { ms: +m.med.toFixed(2), px: [2560, 1600] } : null,
  };
});
const out = {
  format: 'marrow.static-wallpapers/1',
  contract: {
    entry: 'vec3 wall(vec2 p) returns the colour of the point p in OKLab (L, a, b).',
    coords: 'p is in height units: y runs from -1 at the bottom to +1 at the top of the target, x from -aspect to +aspect. The same composition at any size; more pixels only add detail.',
    uniforms: [
      { name: 'u_res', type: 'vec2', desc: 'the target size in device pixels' },
      { name: 'u_dpr', type: 'float', desc: 'device pixels per CSS pixel; micro texture (paper, weave, stars) is sized in CSS pixels' },
      { name: 'u_pal', type: 'vec3[6]', desc: 'the colour pack in OKLab: the pack\'s dark and light palettes mixed on the CPU for the theme (and for a pack crossfade)' },
      { name: 'u_dark', type: 'float', desc: '0 light .. 1 dark, eased, for the few things that are not colour (stars, shadow strength)' },
      { name: 'u_grain', type: 'float', desc: '0..1 film grain, applied by main() to OKLab L' },
      { name: 'u_opt', type: 'float', desc: '0..1 the wallpaper\'s own option (see option.label)' },
    ],
    rules: [
      'Maths only: no textures, no derivatives, loops with constant bounds, arrays indexed by constants or loop indices, no bit operations, no preprocessor in the wallpaper source.',
      'Render once at the window size (and again only when the size, theme, pack or option changes), then cache. Theme and pack changes crossfade: ease u_pal and u_dark over about 0.5 s.',
      'Skia SkSL: declare the uniforms, keep head out, use lib and the wallpaper source as written, and port main() to half4 main(float2 coord) with coord.y flipped so y is up.',
      'Palettes are sRGB hex; convert to OKLab on the CPU (the page does it with the standard OKLab matrices).',
    ],
    head: SW.HEAD, lib: SW.LIB, main: SW.MAIN,
  },
  tiers: TIERS,
  measured: measured.rows ? { renderer: measured.renderer, chrome: measured.chrome, px: [2560, 1600], note: 'median of 11 single renders, each finished (readPixels), after 3 warm-up draws; measured twice and the slower kept' } : null,
  wallpapers: walls,
};
fs.writeFileSync(path.join(HERE, 'wallpapers.json'), JSON.stringify(out, null, 1));
console.log('wallpapers.json:', walls.length, 'wallpapers,', walls.reduce((s, w) => s + w.packs.length, 0), 'packs,', Math.round(fs.statSync(path.join(HERE, 'wallpapers.json')).size / 1024), 'KB');
