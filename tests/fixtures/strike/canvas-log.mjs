// Records what the locked canvas's own Strike code draws (OD-0073, variant A2 = the board's key 'B'), for the port's
// exactness tests (EffortSliderStrikeTests). It loads the class from the locked file itself, runs strike() against a
// recording 2D context and writes canvas-log.json: per tube and time, every stroke (points, width, rgba) and every
// wash (centre, radii, alpha) in drawing order.
//   node tests/fixtures/strike/canvas-log.mjs [path to fast-mode-strike-A2-locked.dc.html]
// Source: E:/Droppy/work/design/fast-mode-strike-A2-locked.dc.html, sha256 3f8ad01a...7a4408.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const file = process.argv[2] || 'E:/Droppy/work/design/fast-mode-strike-A2-locked.dc.html';
const html = fs.readFileSync(file, 'utf8');
const sha = crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const script = html.slice(html.indexOf('class Component extends DCLogic'), html.lastIndexOf('</script>'));
const Component = new Function('class DCLogic {}\n' + script + '\nreturn Component;')();

const r4 = (v) => Math.round(v * 10000) / 10000;
function recorder() {
  const ops = [];
  let path = [], tx = 0, ty = 0, sx = 1, sy = 1;
  const ctx = {
    lineWidth: 1, strokeStyle: '', globalAlpha: 1, fillStyle: null, lineJoin: '', lineCap: '',
    beginPath() { path = []; },
    moveTo(x, y) { path.push([r4(x), r4(y)]); },
    lineTo(x, y) { path.push([r4(x), r4(y)]); },
    // a path stroked again (the glow's three strokes) is logged with `same` instead of its points again
    stroke() {
      const prev = ops.length ? ops[ops.length - 1] : null;
      const same = prev && prev.op === 'stroke' && prev.path === path;
      ops.push(same ? { op: 'stroke', width: r4(this.lineWidth), style: this.strokeStyle, same: 1, path } : { op: 'stroke', width: r4(this.lineWidth), style: this.strokeStyle, points: path.slice(), path });
    },
    createRadialGradient() { return { stops: [], addColorStop(o, c) { this.stops.push([o, c]); } }; },
    save() { tx = 0; ty = 0; sx = 1; sy = 1; this.globalAlpha = 1; },
    translate(x, y) { tx = x; ty = y; },
    scale(a, b) { sx = a; sy = b; },
    fillRect() { ops.push({ op: 'wash', x: r4(tx), y: r4(ty), rx: r4(sx * 100), ry: r4(sy * 100), alpha: r4(this.globalAlpha), color: this.fillStyle.stops[0][1] }); },
    restore() {},
  };
  return { ctx, ops };
}

// The board's tubes (meta.json of the reference frames): tile times are board time + off; g.w is the canvas's
// clientWidth (an integer: 395 for the 395.1875 px liquid), ex the knob centre.
const tubes = [
  { name: 'dark-claude-520x40-high', brand: 'claude', seed: 498, off: 3.655, w: 395, h: 40, knob: 36, top: false, start: 1.0736 },
  { name: 'dark-deepseek-300x26-max', brand: 'deepseek', seed: 595, off: 4.386, w: 300, h: 26, knob: 22, top: true, start: 0.4846 },
  { name: 'light-claude-520x40-high', brand: 'claude', seed: 692, off: 5.117, w: 395, h: 40, knob: 36, top: false, start: 2.0633 },
  { name: 'light-gemini-300x26-max', brand: 'gemini', seed: 789, off: 5.848, w: 300, h: 26, knob: 22, top: true, start: 1.5176 },
  // the port's own track: 34 high, the 42 px knob centred 21 px in from the liquid's end
  { name: 'port-claude-260x34', brand: 'claude', seed: 13, off: 0, w: 260, h: 34, knob: 42, top: true, start: 0 },
];
const TINT = {
  claude: { outer: '255,146,100', inner: '255,222,204', wash: '255,168,126' },
  deepseek: { outer: '116,142,255', inner: '224,231,255', wash: '146,168,255' },
  gemini: { outer: '190,160,255', inner: '242,234,255', wash: '206,184,246' },
};

const out = { source: file.replace(/\\/g, '/'), sha256: sha, tubes: [] };
for (const tube of tubes) {
  const c = new Component();
  const tl = { cache: {}, n: 0 };
  const g = { w: tube.w, h: tube.h, cy: tube.h / 2, xs: tube.h * 0.32, ex: tube.top ? tube.w - 2 - tube.knob / 2 : tube.w - 2, kr: tube.knob / 2,
    k: Math.max(0.62, Math.min(1, tube.h / 40)), tint: TINT[tube.brand], seed: tube.seed, tl };
  const frames = new Set();
  if (tube.start > 0) {
    for (let i = 0; i < 288; i += 3) frames.add(i);
    for (let i = 14; i <= 62; i++) frames.add(i);
  }
  const times = [...frames].sort((a, b) => a - b).map((i) => ({ frame: i, t: tube.start + tube.off + i / 60 }));
  // the port's track: two full cycles at 1/60, every 4th frame, plus a dense run through the first strike
  if (tube.start === 0) {
    for (let i = 0; i < 576; i += 4) times.push({ frame: i, t: i / 60 });
    for (let i = 0; i < 60; i++) times.push({ frame: 1000 + i, t: c.strikeAt(1, g) - 0.15 + i / 240 });
  }
  // the reduce-motion still: 35 ms into strike 1, no tile offset
  times.push({ frame: -1, t: c.strikeAt(1, g) + 0.035 });
  const entries = [];
  for (const { frame, t } of times) {
    if (tl.n > 48) { tl.cache = {}; tl.n = 0; }
    const { ctx, ops } = recorder();
    c.strike(ctx, g, t);
    for (const op of ops) delete op.path;
    entries.push({ frame, t, ops });
  }
  out.tubes.push({ ...tube, ex: g.ex, kr: g.kr, k: g.k, frames: entries });
}
fs.writeFileSync(path.join(here, 'canvas-log.json'), JSON.stringify(out));
console.log('wrote', out.tubes.map((t) => t.name + ':' + t.frames.length).join(' '));
