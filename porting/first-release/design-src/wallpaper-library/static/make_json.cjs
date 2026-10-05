// Builds src/wallpapers.json, the port data: the shader contract (head, library, main), and per wallpaper its source,
// its option, its grain, its colour packs resolved to hex for dark and for light, and the data of the four premium options
// (Dynamic keys, light, Perspective, Focus; Match accent is generated at run time from the roles and the hues of pack 0).
// measured.json (written by check.cjs) adds the measured single-render cost at 2560 x 1600. The page reads the same JSON
// from its own block.
const fs = require('fs'), path = require('path'), vm = require('vm');
const HERE = __dirname;
const ctx = { window: {}, globalThis: null, module: undefined };
ctx.globalThis = ctx.window; vm.createContext(ctx);
// the shared files, the option data of the first thirty, then every tier file that exists: the first ten of each tier, and
// the second wave (walls-<tier>-2.js) written by the tier agents
const FILES = ['sw-color.js', 'sw-core.js', 'options-data-1.js'];
for (const t of ['vibrant', 'balanced', 'minimal']) for (const n of ['', '-2']) if (fs.existsSync(path.join(HERE, 'walls-' + t + n + '.js'))) FILES.push('walls-' + t + n + '.js');
for (const f of FILES) {
  const code = fs.readFileSync(path.join(HERE, f), 'utf8');
  vm.runInContext(/^sw-/.test(f) ? code : 'var SW = window.SW;\n' + code, ctx, { filename: f });
}
const SW = ctx.window.SW, SWC = ctx.window.SWC;
let measured = {};
try { measured = JSON.parse(fs.readFileSync(path.join(HERE, 'measured.json'), 'utf8')); } catch (e) { /* not measured yet */ }
const TIERS = [
  { id: 'vibrant', name: 'Vibrant', desc: 'Rich and bold: saturated colour, strong light and deep shadow.' },
  { id: 'balanced', name: 'Balanced', desc: 'Colourful but restrained: real materials and soft light, moderate colour.' },
  { id: 'minimal', name: 'Minimal', desc: 'Calm and low in contrast: one quiet idea, soft colour, nothing that pulls the eye.' },
];

// ---- the premium options: fill in, validate (a problem is a warning here and an error in check.cjs; the build goes on)
const warnings = [];
const KEYS = ['dawn', 'day', 'dusk', 'night'];
const RANGE = { l: [-0.18, 0.12], c: [0.5, 1.5], h: [-40, 40] };
const GENERIC_DYN = {
  dawn: { l: 0.03, c: 1.1, h: -8, light: [-0.9, 0.25] }, day: { l: 0, c: 1, h: 0, light: [-0.35, 0.9] },
  dusk: { l: -0.03, c: 1.15, h: 12, light: [0.9, 0.2] }, night: { l: -0.1, c: 0.75, h: -6, light: [0.2, 0.85] },
};
const num = (v) => typeof v === 'number' && isFinite(v);
function cleanDyn(id, d) {
  if (!d) { warnings.push(id + ': no dyn (the four time-of-day looks); a generic one is used'); return { keys: JSON.parse(JSON.stringify(GENERIC_DYN)), generated: true }; }
  const out = {};
  for (const k of KEYS) {
    const e = d[k];
    if (!e) { warnings.push(id + ': dyn.' + k + ' missing'); out[k] = JSON.parse(JSON.stringify(GENERIC_DYN[k])); continue; }
    const o = {};
    for (const f of ['l', 'c', 'h']) {
      if (!num(e[f]) || e[f] < RANGE[f][0] || e[f] > RANGE[f][1]) { warnings.push(id + ': dyn.' + k + '.' + f + ' = ' + e[f] + ' is outside ' + RANGE[f].join('..')); o[f] = Math.min(RANGE[f][1], Math.max(RANGE[f][0], num(e[f]) ? e[f] : f === 'c' ? 1 : 0)); } else o[f] = e[f];
    }
    if (e.light != null) {
      if (!Array.isArray(e.light) || e.light.length !== 2 || !e.light.every((x) => num(x) && Math.abs(x) <= 1.2)) warnings.push(id + ': dyn.' + k + '.light must be [x, y], each within +/-1.2');
      else o.light = e.light.slice();
    }
    out[k] = o;
  }
  const dl = out.night.l - out.day.l;
  if (dl > -0.04) warnings.push(id + ': night.l - day.l = ' + dl.toFixed(3) + ', the night look must be darker than the day look (at most -0.04, ideally -0.06 to -0.18)');
  return { keys: out, generated: false };
}
function cleanLight(id, l) {
  const o = { def: [-0.35, 0.9], own: false };
  if (l) {
    if (Array.isArray(l.def) && l.def.length === 2 && l.def.every(num)) o.def = l.def.slice(); else if (l.def != null) warnings.push(id + ': light.def must be [x, y]');
    o.own = !!l.own;
  }
  return o;
}
function cleanPersp(id, p) {
  const o = { def: 0, depth: 0.3, own: false };
  if (p) {
    if (p.def != null) o.def = p.def ? 1 : 0;
    if (p.depth != null) { if (!num(p.depth) || p.depth < 0 || p.depth > 1) warnings.push(id + ': persp.depth must be 0..1'); else o.depth = p.depth; }
    o.own = !!p.own;
  }
  return o;
}
function cleanFocus(id, f) {
  const o = { def: 0, own: false };
  if (f) {
    if (f.def != null) { if (!num(f.def) || f.def < 0 || f.def > 0.3) warnings.push(id + ': focus.def must be 0..0.3'); else o.def = f.def; }
    o.own = !!f.own;
  }
  return o;
}
// the hues of pack 0 per role, for Match accent: dark and light
function hues(w) {
  const pk = w.packs[0], d = [], l = [];
  const lch = (hex) => SWC.lab2lch.apply(null, SWC.hex2lab(hex))[2];
  if (pk.x) { pk.x.d.forEach((h) => d.push(Math.round(lch(h)))); pk.x.l.forEach((h) => l.push(Math.round(lch(h)))); }
  else for (let i = 0; i < 6; i++) { d.push(pk.h[i % pk.h.length]); l.push((pk.hl || pk.h)[i % (pk.hl || pk.h).length]); }
  return { d, l };
}

const ids = new Set();
const walls = SW.list.map((w) => {
  if (ids.has(w.id)) throw new Error('duplicate id ' + w.id); ids.add(w.id);
  if (w.roles.length !== 6) throw new Error(w.id + ' needs 6 roles');
  if (w.packs.length < 3 || w.packs.length > 5) throw new Error(w.id + ' needs 3 to 5 packs');
  if (!w.packs.some((p) => p.k === 'quiet') || !w.packs.some((p) => p.k === 'rich')) throw new Error(w.id + ' needs a quiet and a rich pack');
  for (const bad of [/#\s*(define|if|ifdef|include)/, /\btexture\s*\(/, /\bdFd[xy]\b|\bfwidth\b/, /<<|>>|[^&]&[^&]|\^|\|(?!\|)/, /\bswitch\b|\bwhile\b|\bdo\b/]) {
    if (bad.test(w.src)) throw new Error(w.id + ' uses something that does not port: ' + bad);
  }
  for (const u of ['u_tilt', 'u_light', 'u_focus']) if (new RegExp('uniform[^;]*\\b' + u + '\\b').test(w.src)) throw new Error(w.id + ' must not redeclare ' + u + ' (the head declares it)');
  const od = SW.opts[w.id] || {};
  const pick = (k) => (w[k] != null ? w[k] : od[k]);
  const dyn = cleanDyn(w.id, pick('dyn')), light = cleanLight(w.id, pick('light')), persp = cleanPersp(w.id, pick('persp')), focus = cleanFocus(w.id, pick('focus'));
  if (light.own && !/\bu_light\b/.test(w.src)) warnings.push(w.id + ': light.own is true but the src never reads u_light');
  if (persp.own && !/\bu_tilt\b/.test(w.src)) warnings.push(w.id + ': persp.own is true but the src never reads u_tilt');
  if (focus.own && !/\bu_focus\b/.test(w.src)) warnings.push(w.id + ': focus.own is true but the src never reads u_focus');
  const m = measured.rows && measured.rows[w.id];
  return {
    id: w.id, name: w.name, tier: w.tier, desc: w.desc,
    grain: { label: 'Grain', def: w.grain, min: 0, max: 1 },
    option: { label: w.opt.label, def: w.opt.def, min: 0, max: 1, uniform: 'u_opt' },
    defaultPack: 0,
    thumbTexScale: w.thumbTex || 1,
    roles: w.roles,
    hues: hues(w),
    dyn: dyn.keys, dynGenerated: dyn.generated, light, persp, focus,
    packs: w.packs.map((p) => { const r = SWC.resolvePack(w.roles, p); return { name: p.n, kind: p.k || 'standard', dark: r.dark, light: r.light }; }),
    src: w.src.trim() + '\n',
    cost: m ? { ms: +m.med.toFixed(2), msOpt: measured.rowsOpt && measured.rowsOpt[w.id] ? +measured.rowsOpt[w.id].med.toFixed(2) : null, px: [2560, 1600] } : null,
  };
});
const out = {
  format: 'marrow.static-wallpapers/2',
  contract: {
    entry: 'vec3 wall(vec2 p) returns the colour of the point p in OKLab (L, a, b).',
    coords: 'p is in height units: y runs from -1 at the bottom to +1 at the top of the target, x from -aspect to +aspect. The same composition at any size; more pixels only add detail.',
    uniforms: [
      { name: 'u_res', type: 'vec2', desc: 'the target size in device pixels' },
      { name: 'u_dpr', type: 'float', desc: 'device pixels per CSS pixel; micro texture (paper, weave, stars) is sized in CSS pixels' },
      { name: 'u_pal', type: 'vec3[6]', desc: 'the colour pack in OKLab: the pack\'s dark and light palettes mixed on the CPU for the theme (and for a pack crossfade); with Dynamic on, shifted for the hour on the CPU' },
      { name: 'u_dark', type: 'float', desc: '0 light .. 1 dark, eased, for the few things that are not colour (stars, shadow strength); with Dynamic on it also follows the clock' },
      { name: 'u_grain', type: 'float', desc: '0..1 film grain, applied by main() to OKLab L (0 while the engine blurs for Focus: the blur pass adds the grain after it)' },
      { name: 'u_opt', type: 'float', desc: '0..1 the wallpaper\'s own option (see option.label)' },
      { name: 'u_tilt', type: 'vec2', desc: 'Perspective: the picture displacement in height units (|u_tilt| <= 0.015, scaled by the wallpaper\'s depth and by the switch; zero when off or under Reduce Motion)' },
      { name: 'u_tg', type: 'float', desc: '1: main() shifts p by u_tilt; 0: the wallpaper layers itself (persp.own)' },
      { name: 'u_light', type: 'vec2', desc: 'the current direction toward the light; equals light.def when Dynamic is off or at the theme\'s home hour' },
      { name: 'u_light0', type: 'vec2', desc: 'the authored direction, light.def' },
      { name: 'u_lw', type: 'float', desc: 'the strength (0.028) of the brightness wash main() adds for u_light - u_light0; 0 when the wallpaper reads u_light itself (light.own)' },
      { name: 'u_focus', type: 'float', desc: '0..1 Focus, read only by wallpapers with focus.own; the others are blurred by the engine after the render' },
    ],
    rules: [
      'Maths only: no textures, no derivatives, loops with constant bounds, arrays indexed by constants or loop indices, no bit operations, no preprocessor in the wallpaper source.',
      'Render once at the window size (and again only when the size, theme, pack, option, clock or focus changes, or while the pointer moves the Perspective), then cache. Theme, pack, accent, clock, focus and switch changes ease or crossfade over 0.2 to 0.6 s.',
      'Skia SkSL: declare the uniforms, keep head out, use lib and the wallpaper source as written, and port main() to half4 main(float2 coord) with coord.y flipped so y is up. Focus is SkImageFilters.Blur on the rendered picture (sigma = Focus * 0.022 * height in px) with the grain added after it.',
      'Palettes are sRGB hex; convert to OKLab on the CPU (the page does it with the standard OKLab matrices).',
    ],
    head: SW.HEAD, lib: SW.LIB, main: SW.MAIN,
  },
  options: {
    dynamic: {
      keyTimes: { night: 0, dawn: 6, day: 12, dusk: 18 },
      keyFields: 'l: lightness shift added to OKLab L; c: chroma multiplier; h: hue shift in degrees; light: [x, y] direction toward the light (optional)',
      method: 'The look at a clock hour is interpolated between the two neighbouring keys with smoothstep (night 24h wraps to 0h). The palette is shifted by (look - home look) in OKLCH, where the home look is day for light mode and night for dark mode (blended by the eased dark amount), so the authored palette shows exactly at the home hour. u_dark follows the same way with night = 1, dawn = 0.45, dusk = 0.5, day = 0. u_light = light.def + (look.light - home.light). Out-of-gamut colours lose chroma, never hue or lightness.',
      default: false,
    },
    accent: {
      default: '#005FB8',
      method: 'A sixth pack built at run time: the anchor role (the ramp role, 0..4, whose L is closest to the accent\'s, per theme) takes the accent hue; every other role takes the accent hue plus half of the hue offset (capped at 60 degrees) that pack 0 has from the anchor role; every role keeps its own L and chroma, the chroma scaled by accent chroma / 0.16 (clamped 0.3..1.15). Same resolvePack, same gamut mapping and warm-dark rules.',
    },
    perspective: { amplitude: 0.015, note: 'height units at depth 1 (about 6 px at 800 px high); off under Reduce Motion; on by default where persp.def is 1' },
    focus: { sigma: 0.022, note: 'Gaussian sigma of Focus 1 as a share of the picture height; built in (two cheap passes) unless focus.own' },
    warnings,
  },
  tiers: TIERS,
  measured: measured.rows ? { renderer: measured.renderer, chrome: measured.chrome, px: [2560, 1600], note: 'median of 11 single renders, each finished (readPixels), after 3 warm-up draws; measured twice and the slower kept' } : null,
  wallpapers: walls,
};
fs.writeFileSync(path.join(HERE, 'wallpapers.json'), JSON.stringify(out, null, 1));
console.log('wallpapers.json:', walls.length, 'wallpapers,', walls.reduce((s, w) => s + w.packs.length, 0), 'packs,', Math.round(fs.statSync(path.join(HERE, 'wallpapers.json')).size / 1024), 'KB');
if (warnings.length) console.log('option warnings (' + warnings.length + '):\n  ' + warnings.slice(0, 40).join('\n  '));
