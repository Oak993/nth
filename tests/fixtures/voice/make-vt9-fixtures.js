// VT-9 fixtures (voice-SPEC V-13.1, V-13.2, V-T60): the numbers of the stylesheet recipe `Ya` of voice-glow.js, recorded by RUNNING the design source here, never the port:
//   porting/first-release/design-src/customization/voice-glow.js    voice-glow 0.2.1 (Jakub Antalik, MIT) with mountVoiceGlow
// A temporary copy of voice-glow.js gets one appended line (`export { Ya };`, so the module-scoped recipe can be called); no line of the package is touched. For each of the 13
// prop sets of VT-4's glow-config.json (its `style` is Ya's argument) it calls Ya, reads the stylesheet back with regular expressions and writes the NUMBERS the stylesheet carries:
// every lobe's width, height, offset, colour, alpha and fade; the masks; the wash; the inner light's edge depth, shadow and radius; the bloom's blur; the stroke ring; the
// epicentre; and the three layers' opacity factors and filter functions. VoiceGlowRecipeTests holds VoiceGlowRecipe to them.
//
//   node make-vt9-fixtures.js [--out <dir>] [--tmp <dir>]
//
// Writes glow-recipe.json beside this script (or into --out). It needs VT-4's glow-config.json (the sets' Ya arguments) beside it.
'use strict';
const fs = require('fs'), path = require('path'), os = require('os');
const { pathToFileURL } = require('url');

const DESIGN = path.join(__dirname, '..', '..', '..', 'porting', 'first-release', 'design-src', 'customization', 'voice-glow.js');
const arg = (name, dflt) => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : dflt; };
const OUT = arg('--out', __dirname);
const TMP = arg('--tmp', path.join(os.tmpdir(), 'vt9-fixtures'));
fs.mkdirSync(OUT, { recursive: true });
fs.mkdirSync(TMP, { recursive: true });

const VAR = String.raw`var\(--vb-[a-z0-9-]+\)`;
const Z = String.raw`var\(--vb-z-[a-z0-9]+, 1\)`;
const num = String.raw`(-?[\d.]+)`;
// a lobe: ellipse calc(Wpx * w * z) calc(Hpx * h * lK * z) at calc(50% + (cx + xK) * w * z) calc(100% + (Ypx + yK) * z), COLOUR 0%, transparent F%
const LOBE = new RegExp(String.raw`radial-gradient\(ellipse calc\(${num}px \* ${VAR} \* ${Z}\) calc\(${num}px \* ${VAR} \* var\(--vb-l(\d)-[a-z0-9]+\) \* ${Z}\) at calc\(50% \+ \(${VAR} \+ var\(--vb-x\d-[a-z0-9]+\)\) \* ${VAR} \* ${Z}\) calc\(100% \+ \(${num}px \+ var\(--vb-y\d-[a-z0-9]+\)\) \* ${Z}\), (rgba?\([^)]*\)) 0%, transparent ${num}%\)`, 'g');
// the mask ie(): ellipse calc(Wpx * w * mw * z) calc((Hpx * h + bh) * z) at ..., white 0%, rgba(255, 255, 255, 0.5) N%[, rgba(255, 255, 255, A) 85%], transparent 100%
const MASK = new RegExp(String.raw`radial-gradient\(ellipse calc\(${num}px \* ${VAR} \* ${VAR} \* ${Z}\) calc\(\(${num}px \* ${VAR} \+ ${VAR}\) \* ${Z}\) at calc\(50% \+ ${VAR} \* ${VAR} \* ${Z}\) calc\(100% \+ ${VAR} \* ${Z}\), white 0%, rgba\(255, 255, 255, 0\.5\) ${num}%(?:, rgba\(255, 255, 255, ${num}\) 85%)?, transparent 100%\)`, 'g');
// the stroke's wash Ze: ellipse calc(Wpx * w * z) calc(Hpx * h * z) at calc(50% + cx * w * z) calc(100% + (2px + cy) * z), COLOUR A 0%, COLOUR B P%, transparent Q%
const WASH = new RegExp(String.raw`radial-gradient\(ellipse calc\(${num}px \* ${VAR} \* ${Z}\) calc\(${num}px \* ${VAR} \* ${Z}\) at calc\(50% \+ ${VAR} \* ${VAR} \* ${Z}\) calc\(100% \+ \(2px \+ ${VAR}\) \* ${Z}\), rgba\((\d+), (\d+), (\d+), ${num}\) 0%, rgba\(\d+, \d+, \d+, ${num}\) ${num}%, transparent ${num}%\)`);
// the epicentre: ellipse calc(Wpx * w * z) calc((Hpx * h + bh) * z) at ..., white 0%, white A%, rgba(255, 255, 255, M) B%, transparent C%
const CORE = new RegExp(String.raw`radial-gradient\(ellipse calc\(${num}px \* ${VAR} \* ${Z}\) calc\(\(${num}px \* ${VAR} \+ ${VAR}\) \* ${Z}\) at calc\(50% \+ ${VAR} \* ${VAR} \* ${Z}\) calc\(100% \+ ${VAR} \* ${Z}\), white 0%, white ${num}%, rgba\(255, 255, 255, ${num}\) ${num}%, transparent ${num}%\)`);

function rules(css) {
  const list = [];
  css = css.replace(/\/\*[\s\S]*?\*\//g, '');
  for (const m of css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) list.push({ sel: m[1].trim().replace(/\s+/g, ' '), body: m[2] });
  return list;
}
const colour = (text) => { const m = text.match(/^rgba?\((\d+), (\d+), (\d+)(?:, ([\d.]+))?\)$/); return { rgb: [+m[1], +m[2], +m[3]], alpha: m[4] === undefined ? 1 : +m[4] }; };
function lobes(body) {
  return Array.from(body.matchAll(LOBE), (m) => ({ w: +m[1], h: +m[2], k: +m[3], y: +m[4], ...colour(m[5]), fade: +m[6] }));
}
function mask(body) {
  const m = new RegExp(MASK.source).exec(body);
  return m ? { w: +m[1], h: +m[2], half: +m[3], tail: m[4] === undefined ? 0 : +m[4] } : null;
}
const one = (re, text) => { const m = re.exec(text); return m ? m[1] : null; };
function filterOf(body) {
  const f = body.match(/filter: ([^;]*);/);
  if (!f) return null;
  const t = f[1];
  return {
    blur: (t.match(/blur\(calc\(([\d.]+)px \* /) || [])[1] === undefined ? null : +t.match(/blur\(calc\(([\d.]+)px \* /)[1],
    hueBase: +(t.match(/var\(--voice-hue-base, (-?[\d.]+)deg\)/) || [])[1],
    brightness: +(t.match(/brightness\(([\d.]+)\)/) || [])[1],
    saturate: +(t.match(/saturate\(([\d.]+)\)/) || [])[1],
    displaced: /url\(#/.test(t),
  };
}
const opacityFactor = (body, layer) => { const m = body.match(new RegExp(String.raw`opacity: calc\(var\(--vb-opacity-[a-z0-9]+, 1\) \* var\(--vb-glow-[a-z0-9]+\) \* ${num} \* var\(--voice-${layer}-opacity, 1\) \* var\(--voice-strength, 1\)\);`)); return m ? +m[1] : null; };

function facts(css) {
  const rs = rules(css);
  const after = rs.find((r) => r.sel.includes('::after') && /radial-gradient/.test(r.body) && r.sel.includes('[data-active]'));
  // the first ::before rule (the inner light, not its warp mirror) and the bloom rule with a background
  const before = rs.find((r) => r.sel.includes('::before') && /box-shadow/.test(r.body));
  const bloom = rs.find((r) => r.sel.startsWith('[data-voice-beam="') && r.sel.endsWith('[data-voice-beam-bloom],') === false && /\[data-voice-beam-bloom\]/.test(r.sel) && /background:/.test(r.body) && /z-index: 3/.test(r.body));
  const out = {};
  const wash = WASH.exec(after.body);
  out.stroke = {
    lobes: lobes(after.body), mask: mask(after.body),
    wash: { w: +wash[1], h: +wash[2], rgb: [+wash[3], +wash[4], +wash[5]], first: +wash[6], second: +wash[7], secondAt: +wash[8], endAt: +wash[9] },
    opacity: opacityFactor(after.body, 'stroke'), filter: filterOf(after.body),
    ringRadius: +one(/border-radius: calc\(([\d.]+)px \* /, after.body), padding: +one(/padding: calc\(([\d.]+)px \* /, after.body),
  };
  const shadow = before.body.match(/box-shadow: inset 0 0 calc\(([\d.]+)px \* var\(--vb-z-[a-z0-9]+, 1\)\) 1px rgba\((\d+), (\d+), (\d+), ([\d.]+)\);/);
  out.inner = {
    lobes: lobes(before.body), mask: mask(before.body),
    edge: +one(/linear-gradient\(white, transparent calc\(([\d.]+)px \* /, before.body),
    shadow: { blur: +shadow[1], rgba: [+shadow[2], +shadow[3], +shadow[4], +shadow[5]] },
    radius: +one(/border-radius: calc\(([\d.]+)px \* /, before.body),
    opacity: opacityFactor(before.body, 'inner'), filter: filterOf(before.body),
  };
  // the bloom: its background rule carries the lobes, the mask and (in the [data-active] rule) the blur and the opacity
  const bloomBg = rs.find((r) => /\[data-voice-beam-bloom\],/.test(r.sel) && /z-index: 3/.test(r.body));
  const bloomOn = rs.find((r) => /\[data-active\] \[data-voice-beam-bloom\]/.test(r.sel) && /filter:/.test(r.body));
  out.bloom = {
    lobes: lobes(bloomBg.body), mask: mask(bloomBg.body),
    opacity: opacityFactor(bloomOn.body, 'bloom'), filter: filterOf(bloomOn.body),
    radius: +one(/border-radius: calc\(([\d.]+)px \* /, rs.find((r) => /\[data-voice-beam-bloom\],/.test(r.sel) && /display: none/.test(r.body)).body),
  };
  const coreDiv = rs.find((r) => /\[data-voice-beam-core\] > div/.test(r.sel) && /radial-gradient/.test(r.body));
  if (coreDiv) {
    const m = CORE.exec(coreDiv.body);
    const wrapper = rs.find((r) => /\[data-voice-beam-core\]$/.test(r.sel) && /filter: blur/.test(r.body));
    const shown = rs.find((r) => /\[data-active\] \[data-voice-beam-core\]/.test(r.sel));
    const op = shown.body.match(/opacity: calc\(var\(--vb-opacity-[a-z0-9]+, 1\) \* min\(1, var\(--vb-glow-[a-z0-9]+\) \* ([\d.]+) \* ([\d.]+)\) \* var\(--voice-core-light-opacity, 1\)\);/);
    out.core = {
      w: +m[1], h: +m[2], solidAt: +m[3], midAlpha: +m[4], midAt: +m[5], endAt: +m[6],
      blur: +one(/filter: blur\(calc\(([\d.]+)px \* /, wrapper.body), opacityA: +op[1], opacityB: +op[2],
    };
  } else out.core = null;
  return out;
}

(async () => {
  const sets = JSON.parse(fs.readFileSync(path.join(__dirname, 'glow-config.json'), 'utf8')).resolved;
  const file = path.join(TMP, 'voice-glow.patched.mjs');
  fs.writeFileSync(file, fs.readFileSync(DESIGN, 'utf8') + '\nexport { Ya };\n');
  const { Ya } = await import(pathToFileURL(file).href);
  const result = { source: 'voice-glow.js Ya() read back from its stylesheet; the style arguments are glow-config.json\'s', sets: [] };
  for (const set of sets) {
    try { result.sets.push({ name: set.name, style: set.style, facts: facts(Ya(set.style)) }); } catch (e) { throw new Error(set.name + ': ' + e.message); }
  }
  const text = JSON.stringify(result, (k, v) => (typeof v === 'number' && !Number.isInteger(v) ? +v.toPrecision(13) : v)) + '\n';
  fs.writeFileSync(path.join(OUT, 'glow-recipe.json'), text);
  console.log('wrote glow-recipe.json', text.length, 'bytes,', result.sets.length, 'sets');
})().catch((e) => { console.error(e); process.exit(1); });
