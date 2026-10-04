// CZ-3: the stage and the preview, measured from the locked design, not from the port. Run: node tests/fixtures/customization/stage.generate.cjs
// - the foot caption comes from the real CzComponent.caption() in the locked cz.js (the board's helpers are the same seams generate.cjs uses);
// - the model switch's names, model ids and aria text come from the real modelSwitch();
// - the geometry is parsed from the locked gen.py (PVW/PVH/PVHS, the pinned block's paddings and margins, the foot's minimum height) and from the
//   playground's composer css (bottom inset), and the typing string and pace from the playground's logic.js, which gen.py loads.
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const rel = 'porting/first-release/design-src/customization/cz.js';
const source = fs.readFileSync(path.join(root, rel), 'utf8');
const gen = fs.readFileSync(path.join(root, 'porting/first-release/design-src/customization/gen.py'), 'utf8');
const voicecmp = fs.readFileSync(path.join(root, 'porting/first-release/design-src/customization/voicecmp.js'), 'utf8');
const playground = 'E:/Droppy/work/design/composer-lights/playground/src/';
const logic = fs.readFileSync(playground + 'logic.js', 'utf8');
const pgGen = fs.readFileSync(playground + 'gen.py', 'utf8');

const paletteBytes = fs.readFileSync(path.join(root, 'porting/first-release/design-src/orb/liquid-palettes.json'));
const palette = JSON.parse(paletteBytes);
const first = ['Claude', 'OpenAI', 'Gemini', 'DeepSeek', 'Grok', 'NVIDIA'];
const order = first.concat(Object.keys(palette).filter(k => !first.includes(k) && !k.startsWith('_')));
const nice = { OpenCodeGo: 'OpenCode Go', CommandCode: 'Command Code', NousResearch: 'Nous Research', LiquidAI: 'Liquid AI', Zai: 'Z.ai', MiniMax: 'MiniMax' };
// gen.py:60-68: [key, name, model, deep, mid, light, highlight, title on dark, title on light]
const MAKERS = order.map(k => {
  let m = palette[k]._meta.model || ''; if (m.startsWith('(')) m = '';
  const hd = palette[k].dark.high, hl = palette[k].light.high;
  return [k, nice[k] || k, m, hd.deep, hd.mid, hd.light, hd.highlight, hd.title, hl.title];
});

const hex2 = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
const mixHex = (a, b, t) => { const x = hex2(a), y = hex2(b); return '#' + x.map((v, i) => Math.round(v + (y[i] - v) * t).toString(16).padStart(2, '0')).join(''); };
const fmt = (v, n) => (Math.round(v * 10 ** n) / 10 ** n).toString();
const context = vm.createContext({ window: {}, Component: class {}, BEAM_DEFAULTS: {}, AURA_DEFAULTS: {}, hex2, mixHex, fmt, clamp: (v, a, b) => Math.max(a, Math.min(b, v)) });
vm.runInContext(source.replace('/*@MAKERS@*/null', JSON.stringify(MAKERS)), context, { filename: rel });

// ---- the foot caption: every style, every shape, every palette, every Uiverse scheme ----
const captions = [];
const board = (state) => Object.assign(Object.create(vm.runInContext('CzComponent', context).prototype), { state, maker: () => MAKERS.find(m => m[0] === state.maker) });
const palettes = vm.runInContext('Object.keys(CZ_PAL)', context);
for (const [family, size] of [['rotate', 'md'], ['rotate', 'line'], ['rotate', 'sm'], ['pulse', 'pulse-inner'], ['pulse', 'pulse-outside']])
  for (const color of palettes)
    captions.push({ style: 'beam', family, size, color, text: board({ light: 'beam', beam: { family, size, color }, aura: {}, maker: 'Claude' }).caption() });
for (const scheme of ['original', 'maker', 'accent', 'custom'])
  for (const maker of ['Claude', 'OpenAI', 'OpenCodeGo', 'Zai', 'NousResearch'])
    for (const speed of [3, 3.69, 4.25, 12, 12.04, 13.95, 40])
      captions.push({ style: 'aura', scheme, maker, speed, text: board({ light: 'aura', beam: {}, aura: { scheme, speed }, maker }).caption() });
captions.push({ style: 'off', text: board({ light: 'off', beam: {}, aura: {}, maker: 'Claude' }).caption() });

// ---- the model switch: names, model ids, aria, order ----
const switchFor = (maker, dark) => {
  const b = Object.assign(Object.create(vm.runInContext('CzComponent', context).prototype), {
    state: { maker, menu: null }, xf: (k, t) => ({ a: { t }, b: { t: '' } }), tw: () => 100, dark: () => dark, setMenu() {}, save() {}, setState() {},
  });
  return b.modelSwitch('B');
};
const makers = MAKERS.map(m => ({ id: m[0], name: m[1], model: m[2], aria: switchFor(m[0], true).aria, dotDark: switchFor(m[0], true).items.find(i => i.name === m[1]).dot, dotLight: switchFor(m[0], false).items.find(i => i.name === m[1]).dot }));

// ---- geometry ----
const num = (re, text = gen) => { const m = text.match(re); if (!m) throw new Error('not found: ' + re); return +m[1]; };
const [, pvw, pvh, pvhs] = gen.match(/PVW, PVH, PVHS = CWV \+ (\d+), (\d+), (\d+)/).map(Number);
const cwv = num(/CWV = (\d+)/);
const vcw = JSON.parse(voicecmp.match(/const VC_W = (\{[^}]*\})/)[1].replace(/(\w+):/g, '"$1":'));
const pinPadTop = num(/\.czpin\{[^}]*padding:(\d+)px 24px 18px/);
const pinPadBottom = num(/\.czpin\.pinb\{[^}]*padding-bottom:(\d+)px/);
const pinMarginBottom = num(/\.czpin\.pinb\{[^}]*margin-bottom:(\d+)px/);
const footMin = num(/\.czpin\.pinb \.pvfoot\{min-height:(\d+)px/);
const heading = { lineHeight: num(/\.sh\{[^}]*line-height:(\d+)px/), marginBottom: num(/\.sh\{[^}]*margin-bottom:(\d+)px/) };
const pv = gen.match(/\.pv\{[^}]*padding:(\d+)px (\d+)px 0\}/);
const stageRadius = num(/\.pvw\{[^}]*border-radius:(\d+)px/);
const cmp = pgGen.match(/\.cmp\{position:absolute;left:(\d+)px;bottom:(\d+)px;width:(\d+)px;height:(\d+)px;border-radius:(\d+)px/).slice(1).map(Number);
const geometry = {
  composerWidth: { inside: vcw.in, outside: vcw.out }, composerLeft: { inside: 12, outside: 56 }, stage: { width: cwv + 24, height: pvhs, radius: stageRadius },
  card: { padLeft: +pv[1], padTop: +pv[1], padRight: +pv[2], padBottom: 0 }, footMin, heading, pin: { padTop: pinPadTop, padBottom: pinPadBottom, marginBottom: pinMarginBottom },
  playgroundComposer: { left: cmp[0], bottom: cmp[1], width: cmp[2], height: cmp[3], radius: cmp[4] }, compactMax: source.match(/Math\.min\(92, Math\.max\(0, el\.scrollTop\)\)/) ? 92 : 0,
  // the pinned block at rest in the design's page, then in the port's (60 top): 59 + heading 18 + 10 + card 12 + 212 + 46 + 14
  restDesign: pinPadTop + heading.lineHeight + heading.marginBottom + +pv[1] + pvhs + footMin + pinPadBottom,
};
const typing = { text: logic.match(/const TYPED = '([^']*)'/)[1], secondsPerChar: +logic.match(/Math\.floor\(this\.typeT \/ ([0-9.]+)\)/)[1] };
const placeholder = pgGen.match(/Ask Claude for anything[^<'"]*/)[0];
const chatHtml = [...gen.match(/STAGE_CHAT = \(([\s\S]*?)\)\n/)[1].matchAll(/'((?:[^'\\]|\\.)*)'/g)].map(m => m[1]).join('');
const chat = { html: chatHtml };
fs.writeFileSync(path.join(__dirname, 'stage.json'), JSON.stringify({ source: rel, paletteSha256: require('node:crypto').createHash('sha256').update(paletteBytes).digest('hex'), captions, makers, geometry, typing, placeholder, chat }, null, 2) + '\n');
