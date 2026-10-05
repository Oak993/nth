// CZ-5: the Working dots section and the dots' colour, measured from the locked design, not from the port. Run: node tests/fixtures/customization/dots.generate.cjs
// The real dotsSec() and wdColour() of the locked cz.js run on a board whose rendering seams are stubbed (as page.generate.cjs does): what the rows say
// (title, detail, fold, the swatch names and their order, the note) and the colour the dots wear come from cz.js.
// Two inputs are supplied here because their originals are outside the repository: CZ_MAKERS is built from design-src/orb/liquid-palettes.json exactly as
// stage.generate.cjs does (gen.py:60-68), and CZ_ACC (gen.py:93-94 reads E:/Droppy/work/design/accent-palette-locked.json) is read back from the port's own
// AccentPalette.cs, whose 22 rows were made from that file (the design's "win" is the port's "windows").
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const rel = 'porting/first-release/design-src/customization/cz.js';
const source = fs.readFileSync(path.join(root, rel), 'utf8');

const palette = JSON.parse(fs.readFileSync(path.join(root, 'porting/first-release/design-src/orb/liquid-palettes.json')));
const first = ['Claude', 'OpenAI', 'Gemini', 'DeepSeek', 'Grok', 'NVIDIA'];
const order = first.concat(Object.keys(palette).filter(k => !first.includes(k) && !k.startsWith('_')));
const nice = { OpenCodeGo: 'OpenCode Go', CommandCode: 'Command Code', NousResearch: 'Nous Research', LiquidAI: 'Liquid AI', Zai: 'Z.ai', MiniMax: 'MiniMax' };
const MAKERS = order.map(k => {
  let m = palette[k]._meta.model || ''; if (m.startsWith('(')) m = '';
  const hd = palette[k].dark.high, hl = palette[k].light.high;
  return [k, nice[k] || k, m, hd.deep, hd.mid, hd.light, hd.highlight, hd.title, hl.title];
});

const accentSource = fs.readFileSync(path.join(root, 'src/Marrow.UI/Port/AccentPalette.cs'), 'utf8');
const ACC = [...accentSource.matchAll(/new\((?:WindowsId|"(\w+)"), "([^"]+)", "(#[0-9A-F]{6})", "(#[0-9A-F]{6})", (true|false)\)/g)]
  .map(m => [m[1] || 'win', m[2], m[5] === 'true' ? 'signature' : 'classic', m[3], m[4]]);
if (ACC.length !== 22) throw new Error('expected 22 accents, read ' + ACC.length);

const hex2 = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16));
const mixHex = (a, b, t) => { const x = hex2(a), y = hex2(b); return '#' + x.map((v, i) => Math.round(v + (y[i] - v) * t).toString(16).padStart(2, '0')).join(''); };
const fmt = (v, n) => (Math.round(v * 10 ** n) / 10 ** n).toString();
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const context = vm.createContext({ window: {}, Component: class {}, BEAM_DEFAULTS: {}, AURA_DEFAULTS: {}, hex2, mixHex, fmt, clamp });
vm.runInContext(source.replace('/*@MAKERS@*/null', JSON.stringify(MAKERS)).replace('/*@LOCKACC@*/null', JSON.stringify(ACC)), context, { filename: rel });

let dark = true;
const board = Object.create(vm.runInContext('CzComponent', context).prototype);
Object.assign(board, { state: { wd: 'accent', wdc: 'blue', maker: 'Claude' }, xf: (_, text) => text || '', dark: () => dark });

const id = x => x === 'win' ? 'windows' : x;
const colours = [];
for (const isDark of [true, false]) {
  dark = isDark;
  for (const m of MAKERS) { board.state.maker = m[0]; colours.push({ mode: 'model', maker: m[0], dark: isDark, hex: board.wdColour('model').toLowerCase() }); }
  for (const a of ACC) { board.state.wdc = a[0]; colours.push({ mode: 'custom', custom: id(a[0]), dark: isDark, hex: board.wdColour('custom').toLowerCase() }); }
}

const cases = [];
dark = true;
for (const wd of ['accent', 'model', 'custom'])
  for (const maker of ['Claude', 'OpenAI', 'NVIDIA', 'OpenCodeGo'])
    for (const wdc of ['blue', 'win', 'teal', 'slate']) {
      Object.assign(board.state, { wd, wdc, maker });
      const sec = board.dotsSec();
      const [seg, sw, note] = sec.rows;
      cases.push({
        wd, wdc: id(wdc), maker, title: sec.t,
        segment: { t: seg.t, d: seg.d, label: seg.segs[0].g.label, opts: seg.segs[0].g.opts.map(o => ({ t: o.t, on: o.on })) },
        swatches: { t: sw.t, fold: sw.fold, classic: sw.classic.map(s => ({ name: s.name, on: s.on })), signature: sw.sig.map(s => ({ name: s.name, on: s.on })) },
        note: note.t,
      });
    }

fs.writeFileSync(path.join(__dirname, 'dots.json'), JSON.stringify({ source: rel, colours, cases }, null, 1) + '\n');
console.log('colours', colours.length, 'cases', cases.length);
