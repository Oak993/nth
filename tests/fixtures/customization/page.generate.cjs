// CZ-4: the Customization page's rows, measured from the locked design, not from the port. Run: node tests/fixtures/customization/page.generate.cjs
// The real beamRows(), auraRows(), resetRow(), advRow() and sections() of the locked cz.js run on a board whose rendering seams are stubbed: what a row says
// (title, detail, formatted value, dimmed, folded, where it sits in its section) comes from cz.js; only the preview's Auto values (beamEff) and the maker / accent
// names are supplied here, and the port's tests supply the same numbers to the page.
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const rel = 'porting/first-release/design-src/customization/cz.js';
const source = fs.readFileSync(path.join(root, rel), 'utf8');
const border = JSON.parse(fs.readFileSync(path.join(root, 'src/Marrow.Core/Models/ComposerLights/border-beam-tables.json'), 'utf8'));

const hex2 = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
const mixHex = (a, b, t) => { const x = hex2(a), y = hex2(b); return '#' + x.map((v, i) => Math.round(v + (y[i] - v) * t).toString(16).padStart(2, '0')).join(''); };
const fmt = (v, n) => (Math.round(v * 10 ** n) / 10 ** n).toString();
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const context = vm.createContext({ window: {}, Component: class {}, BEAM_DEFAULTS: {}, AURA_DEFAULTS: {}, hex2, mixHex, fmt, clamp, CW: 472, CH: 46, CR: 22, BBT: border });
vm.runInContext(source, context, { filename: rel });
context.PAL_NAMES = Object.keys(vm.runInContext('CZ_PAL', context));

// the Auto values the preview's beamEff gives (the port computes its own from BorderBeamResolved; the text only needs that a value is shown)
const AUTO = { autoDur: 3.69, autoBr: 1.3, autoSat: 1, autoCb: 3, autoBb: 22.5 };
const BEAM = { family: 'rotate', size: 'md', color: 'colorful', theme: 'auto', duration: null, radius: null, brightness: null, hueShift: 0, strength: 1, saturation: null, glowSize: 1, hueRange: 30,
  staticColors: false, strokeX: 1, innerX: 1, bloomX: 1, coreBlur: null, bloomBlur: null, glowX: null, glowY: null, boost: 1, spike: 1, respond: false };
const AURA = { scheme: 'original', c1: '#8E7CFF', c2: '#38C9B4', speed: 12, breathing: true, rest: 40, typing: 55, running: 75, haloSize: 2, haloBlur: 30, rim: true, rimW: 1.5, dots: true, dotCell: 3, dotStr: 67 };

const board = Object.create(vm.runInContext('CzComponent', context).prototype);
Object.assign(board, {
  state: { beam: { ...BEAM }, aura: { ...AURA }, adv: false, light: 'beam', wd: 'accent', wdc: 'blue', maker: 'Claude', drag: null },
  st: () => 'rest', beamEff: b => ({ pulse: b.family === 'pulse', ...AUTO }),
  model: () => ({ name: board.makerName }), accent: () => ['', board.accentName],
  xf: (_, text) => text || '', voiceSec: () => ({ id: 'voice', rows: [] }), dotsSec: () => ({ id: 'dots', rows: [] }),
  makerName: 'OpenAI', accentName: 'Blue',
});

const clean = r => {
  const o = { kind: r.kind, t: r.t };
  for (const k of ['d', 'vt', 'dim', 'fold', 'auOn', 'hasAuto', 'on', 'min', 'max', 'v', 'dis', 'c1', 'c2']) if (r[k] !== undefined && typeof r[k] !== 'function') o[k] = r[k];
  if (r.sw) o.sw = r.sw.map(s => ({ name: s.name, on: s.on, bg: s.bg }));
  if (r.segs) o.segs = r.segs.map(s => ({ on: s.cls.includes(' on'), label: s.g.label, opts: s.g.opts.map(x => ({ t: x.t, on: x.on })) }));
  return o;
};
const rowsOf = R => Object.fromEntries(Object.entries(R).map(([k, r]) => [k, clean(r)]));

// ---- Border beam: every family / type / palette / hold-still / theme branch, and the value formats ----
const beams = [];
const addBeam = (name, over) => { board.state.beam = { ...BEAM, ...over }; beams.push({ name, beam: board.state.beam, rows: rowsOf(board.beamRows()), reset: { default: clean((board.isDefault = () => true, board.resetRow('beam'))), changed: clean((board.isDefault = () => false, board.resetRow('beam'))) } }); };
for (const [family, size] of [['rotate', 'md'], ['rotate', 'line'], ['rotate', 'sm'], ['pulse', 'pulse-inner'], ['pulse', 'pulse-outside']])
  for (const color of ['colorful', 'mono'])
    for (const staticColors of [false, true]) addBeam(`${family}/${size}/${color}/${staticColors ? 'still' : 'drift'}`, { family, size, color, staticColors });
for (const theme of ['dark', 'light']) addBeam('theme ' + theme, { theme });
addBeam('palette ocean', { color: 'ocean' });
addBeam('values', { duration: 0.55, brightness: 1.35, strength: 0.37, hueShift: -45, saturation: 1.15, hueRange: 100, glowSize: 1.25, strokeX: 0.75, innerX: 1.05, bloomX: 1.5, radius: 30, boost: 1.25, spike: 2.5, respond: true });
addBeam('values duration 3.69', { duration: 3.69 });
addBeam('values duration 13.95', { duration: 13.95 });
addBeam('values line drift', { family: 'rotate', size: 'line', hueRange: 100 });
addBeam('values pulse outside', { family: 'pulse', size: 'pulse-outside', coreBlur: 7.5, bloomBlur: 33.5, glowX: 1.35, glowY: 0.5 });
board.isDefault = () => true;

// ---- Uiverse light ----
const auras = [];
const addAura = (name, over) => { board.state.aura = { ...AURA, ...over }; auras.push({ name, aura: board.state.aura, rows: rowsOf(board.auraRows()), reset: { default: clean((board.isDefault = () => true, board.resetRow('aura'))), changed: clean((board.isDefault = () => false, board.resetRow('aura'))) } }); };
for (const scheme of ['original', 'maker', 'accent', 'custom'])
  for (const rim of [true, false])
    for (const dots of [true, false]) addAura(`${scheme}/${rim ? 'rim' : 'no rim'}/${dots ? 'dots' : 'no dots'}`, { scheme, rim, dots });
addAura('values', { speed: 3.5, rest: 12, typing: 55, running: 100, haloSize: 7, haloBlur: 33, rimW: 2.25, dotCell: 4.5, dotStr: 20 });
board.isDefault = () => true;

// ---- the sections of each style: which rows, in which card, folded or not, with which dividers ----
const outline = (style, adv, over) => {
  board.state.beam = { ...BEAM, ...(over || {}) }; board.state.aura = { ...AURA, ...(over || {}) }; board.state.adv = adv; board.isDefault = () => true;
  return board.sections(style, 'B').filter(s => s.id !== 'voice' && s.id !== 'dots').map(s => ({
    id: s.id, title: s.t || '', shown: !s.fc.includes(' off'),
    rows: s.rows.map(r => r.isNest ? { nest: true, shown: !r.fc.includes(' off'), rows: r.nest.map(q => ({ kind: q.kind, t: q.t, fold: !!q.fold, dv: !q.dvc.includes('none') })) } : { kind: r.kind, t: r.t || '', fold: !!r.fold, dv: !r.dvc.includes('none') }),
  }));
};
const sections = {
  beam: outline('beam', false), beamAdvanced: outline('beam', true), aura: outline('aura', false), auraAdvanced: outline('aura', true), off: outline('off', false),
  beamLine: outline('beam', true, { size: 'line', family: 'rotate' }), beamPulseOutside: outline('beam', true, { size: 'pulse-outside', family: 'pulse' }),
};

const constants = vm.runInContext('({ palettes: CZ_PAL, style: CZ_STYLE, description: CZ_STYLE_D, shape: CZ_SHAPE_D, scheme: CZ_SCHEME_D })', context);
fs.writeFileSync(path.join(__dirname, 'page.json'), JSON.stringify({ source: rel, auto: AUTO, makerName: board.makerName, accentName: board.accentName, beams, auras, sections, constants }, null, 1) + '\n');
