// VT-4 fixtures (voice-SPEC V-T27, V-T28, V-T29; V-11.1, V-11.2, V-11.4, V-13.1, V-13.2): the numbers the Core ports must equal,
// recorded by RUNNING the real design sources on this machine, never by running the port:
//   E:/Droppy/work/voice/design/voice-orbs.js       the orb export runtime and the state layer (makeOrb.frame), run with a recording WebGL2 stub
//   E:/Droppy/work/voice/design/voice-glow.js       voice-glow 0.2.1's driver (ka) and mountVoiceGlow's prop resolution, run with a small DOM stub
//   E:/Droppy/work/voice/design/voice-composer.js   the motion constants K, the easing helpers and the orb's per-frame targets and integrators
//   E:/Droppy/work/voice/reference/orb-exports/seeds.json   copied byte for byte
// The design files are used as they are. A temporary copy of voice-orbs.js and voice-glow.js gets a few lines that only expose or record
// module-level names (an `export { ... }` line, and one assignment at the top of Fn and Ya); no line of arithmetic is touched.
//
//   node make-vt4-fixtures.js [--out <dir>] [--tmp <dir>]
//
// Writes seeds.json, orb-runtime.json, orb-motion.json, glow-driver.json, glow-config.json beside this script (or into --out).
'use strict';
const fs = require('fs'), path = require('path'), os = require('os'), crypto = require('crypto');
const { pathToFileURL } = require('url');

const DESIGN = 'E:/Droppy/work/voice/design';
const SEEDS_JSON = 'E:/Droppy/work/voice/reference/orb-exports/seeds.json';
const arg = (name, dflt) => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : dflt; };
const OUT = arg('--out', __dirname);
const TMP = arg('--tmp', path.join(os.tmpdir(), 'vt4-fixtures'));
fs.mkdirSync(OUT, { recursive: true });
fs.mkdirSync(TMP, { recursive: true });

// ------------------------------------------------------------------------------------------------ helpers
// a float32 value as the shortest decimal that parses back to the same float32 (the fixtures stay small; the C# test casts to float)
function f32(v) {
  if (!Number.isFinite(v)) return String(v);
  if (v === 0) return Object.is(v, -0) ? -0 : 0;
  for (let p = 1; p <= 9; p++) { const s = v.toPrecision(p); if (Math.fround(+s) === v) return +s; }
  return v;
}
const f32s = (arr) => Array.from(arr, f32);
function write(name, obj) {
  // doubles are kept to 13 significant digits (the tests compare with a 1e-10 relative tolerance; the files stay small)
  const text = JSON.stringify(obj, (k, v) => (typeof v === 'number' && !Number.isInteger(v) ? +v.toPrecision(13) : v)) + '\n';
  fs.writeFileSync(path.join(OUT, name), text);
  console.log('wrote', name, fs.statSync(path.join(OUT, name)).size, 'bytes');
}
// a number that JSON cannot carry travels as a word (the C# test reads it back)
const jnum = (v) => (Number.isNaN(v) ? 'NaN' : v === Infinity ? 'Infinity' : v === -Infinity ? '-Infinity' : v);
function patched(file, edits, tail) {
  let src = fs.readFileSync(path.join(DESIGN, file), 'utf8');
  for (const [from, to] of edits) {
    if (src.split(from).length !== 2) throw new Error(file + ': patch anchor not found exactly once: ' + from);
    src = src.replace(from, to);
  }
  const out = path.join(TMP, file.replace(/\.js$/, '.patched.mjs'));
  fs.writeFileSync(out, src + '\n' + tail + '\n');
  return import(pathToFileURL(out).href);
}
// a deterministic generator for scripted inputs (never used for expected values)
function rng(seed) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }

(async () => {
  // ------------------------------------------------------------------------------------------------ seeds.json, byte for byte
  const seedBytes = fs.readFileSync(SEEDS_JSON);
  fs.writeFileSync(path.join(OUT, 'seeds.json'), seedBytes);
  const seedFile = JSON.parse(seedBytes.toString('utf8'));
  const sha256 = crypto.createHash('sha256').update(seedBytes).digest('hex');

  // ------------------------------------------------------------------------------------------------ the orb export runtime
  globalThis.window = { devicePixelRatio: 1 };
  const O = await patched('voice-orbs.js', [], 'export { applyAudioUniforms, sampleTransition, srgbToLinear, linearToSrgb, mixSrgb, SEEDS, STYLE, audioRules, audioFlowStrengths };');
  const KEYS = { siri: 'siri-wave', voiceWave: 'voice-membrane' };
  // the design's SEEDS literal must equal seeds.json float for float (its header says so; this proves it on every run)
  for (const k of Object.keys(KEYS)) for (const st of ['idle', 'thinking']) {
    const a = O.SEEDS[k][st], b = seedFile[KEYS[k]].stateSeeds[st];
    if (a.length !== 136 || b.length !== 136) throw new Error('seed length');
    for (let i = 0; i < 136; i++) if (a[i] !== b[i]) throw new Error(`seeds differ: ${k} ${st} [${i}]`);
    if (O.SEEDS.background[k] !== seedFile[KEYS[k]].background) throw new Error('background differs: ' + k);
  }
  const sampleXs = [0, 1e-9, 0.0031308, 0.0031309, 0.01, 0.04045, 0.0404501, 0.1, 0.2, 0.3333333333, 0.5, 0.6, 0.75, 0.9, 0.99999, 1];
  const orbRuntime = {
    source: 'voice-orbs.js (the export runtime) run in node; see make-vt4-fixtures.js',
    seedsSha256: sha256,
    style: O.STYLE,
    audioRules: O.audioRules,
    audioFlowStrengths: O.audioFlowStrengths,
    srgb: sampleXs.map((x) => ({ x, toLinear: O.srgbToLinear(x), toSrgb: O.linearToSrgb(x) })),
    mix: [[0.1, 0.9, 0], [0.1, 0.9, 0.25], [0.1, 0.9, 0.5], [0.02, 0.7, 0.8], [0.5, 0.5, 0.3], [1, 0, 1], [0, 1, 0.12345]].map(([a, b, p]) => ({ from: a, to: b, p, value: O.mixSrgb(a, b, p) })),
    transitions: [], audio: [], frames: [],
  };
  for (const k of Object.keys(KEYS)) {
    const idle = new Float32Array(O.SEEDS[k].idle), think = new Float32Array(O.SEEDS[k].thinking);
    for (const p of [0, 0.07, 0.22, 0.5, 0.82, 1]) {
      const d = new Float32Array(136);
      O.sampleTransition(idle, think, p, d);
      orbRuntime.transitions.push({ orb: KEYS[k], progress: p, values: f32s(d) });
    }
    const base = new Float32Array(136);
    O.sampleTransition(idle, think, 0.4, base);
    const cases = [
      { all: 0, low: 0, mid: 0, high: 0 }, { all: 0.5, low: 0.2, mid: 0.6, high: 0.1 }, { all: 1, low: 1, mid: 1, high: 1 },
      { all: 1.7, low: -0.4, mid: 2, high: 0.03 }, { all: NaN, low: Infinity, mid: -Infinity, high: 0.5 }, { all: 0.0123, low: 0.9, mid: 0.0005, high: 0.77 },
    ];
    for (const b of cases) {
      const v = new Float32Array(base);
      O.applyAudioUniforms(v, b);
      orbRuntime.audio.push({ orb: KEYS[k], bands: { all: jnum(b.all), low: jnum(b.low), mid: jnum(b.mid), high: jnum(b.high) }, values: f32s(v) });
    }
    // a preset whose flow style is not audio-driven stays untouched: style 9 -> 5 (not in audioFlowStrengths)
    const other = new Float32Array(base); other[15] = 5;
    const o2 = new Float32Array(other); O.applyAudioUniforms(o2, { all: 1, low: 1, mid: 1, high: 1 });
    orbRuntime.audio.push({ orb: KEYS[k], note: 'style 5 has no audio strength', style: 5, bands: { all: 1, low: 1, mid: 1, high: 1 }, values: f32s(o2) });
  }

  // makeOrb.frame() with a recording WebGL2 stub: the state sample, the audio rules, the integrated phase, the framing, the glow
  function glStub(rec) {
    const t = {
      getShaderParameter: () => true, getProgramParameter: () => true, isContextLost: () => false, getActiveUniformBlockParameter: () => 544,
      createShader: () => ({}), createProgram: () => ({}), createBuffer: () => ({}), getUniformBlockIndex: () => 0, getUniformLocation: () => ({}),
      getExtension: () => null,
      bufferSubData: (_t, _o, v) => { rec.values = Array.from(v); },
      uniform4f: (_l, a, b, c, d) => { rec.mrw = [a, b, c, d]; },
    };
    return new Proxy(t, { get: (o, p) => (p in o ? o[p] : typeof p === 'string' && /^[A-Z][A-Z_0-9]*$/.test(p) ? 1 : () => {}) });
  }
  const orbConfigs = [
    { name: 'siri, 32 px canvas, 26 px ball, dpr 1 (2x supersample)', key: 'siri', size: 32, ball: 26, dpr: 1, dark: true },
    { name: 'membrane, 72 px canvas, 32 px ball, dpr 2', key: 'voiceWave', size: 72, ball: 32, dpr: 2, dark: true },
    { name: 'siri, source radius (no ball), dpr 1', key: 'siri', size: 72, ball: 0, dpr: 1, dark: false },
    { name: 'membrane, 72 px canvas, 32 px ball, dpr 1.25, edge source, no supersample', key: 'voiceWave', size: 72, ball: 32, dpr: 1.25, dark: false, edge: 'source', supersample: false },
    { name: 'siri, 40 px canvas, 30 px ball, dpr 3 (capped at 3)', key: 'siri', size: 40, ball: 30, dpr: 3.7, dark: true },
  ];
  const script = [
    { t: 10.0, s: { act: 0 } },
    { t: 10.0167, s: { act: 0.05, speed: 1, scale: 1, glow: 0, lift: 0, desat: 0 } },
    { t: 10.0333, s: { act: 0.3, speed: 2, scale: 1.04, glow: 0.55, lift: 0.15 } },
    { t: 10.05, s: { act: 0.6, speed: 3.2, scale: 1.16, glow: 0.7, lift: 1, bands: { all: 0.5, low: 0.4, mid: 0.6, high: 0.2 } } },
    { t: 10.0667, s: { act: 1, speed: 3.9, scale: 1.19, glow: 0.84, lift: 1, bands: { all: 0.9, low: 0.8, mid: 0.9, high: 0.5 } } },
    { t: 10.2, s: { act: 1, speed: 4.6, scale: 1.1, glow: 0.75, lift: 0.8, bands: { all: 1, low: 1, mid: 1, high: 1 } } },
    { t: 10.35, s: { act: 0.7, speed: 4.6, scale: 1.1, glow: 0.75, lift: 0.8, desat: 0.4 } },
    { t: 10.4, s: { act: 0.7, speed: 4.6, scale: 1.1, glow: 0.75, lift: 0.8, reduceMotion: true } },
    { t: 10.45, s: { act: 0.7, speed: 0.6, scale: 1, glow: 0, lift: 0, desat: 1, reduceMotion: true } },
    { t: 10.5, s: { act: 0.2, speed: 0.6, scale: 0.94, glow: 0.15, desat: 1, bands: { all: 0.1, low: 0.1, mid: 0.1, high: 0.1 } } },
    { t: 12.5, s: { act: 0, speed: 1, scale: 1, glow: 0, lift: 0, desat: 0 } },   // a gap over 0.1 s: the frame delta is capped at 0.1
    { t: 12.4, s: { act: 0.1, speed: 1.3 } },                                     // time going backwards: a delta of 0
    { t: 12.42, s: { act: 0.1 } },                                               // no speed: the export's own values[3]
    { t: 12.44, s: { act: 3, speed: -2, glow: -1, lift: 7, desat: -3 } },        // out-of-range inputs: act clamps, speed and glow floor at 0, lift and desat clamp
  ];
  for (const cfg of orbConfigs) {
    globalThis.window.devicePixelRatio = cfg.dpr;
    const orb = O.orbs.find((o) => o.key === cfg.key);
    const rec = {}, canvas = { width: 0, height: 0, style: {}, clientWidth: cfg.size, getContext: () => glStub(rec), addEventListener() {} };
    const opts = { size: cfg.size, dark: cfg.dark, context: 'own' };
    if (cfg.ball) opts.ball = cfg.ball;
    if (cfg.edge) opts.edge = cfg.edge;
    if (cfg.supersample === false) opts.supersample = false;
    const inst = orb.make(canvas, opts);
    const frames = [];
    for (const f of script) {
      inst.frame(f.t, f.s);
      const s = JSON.parse(JSON.stringify(f.s));
      frames.push({ t: f.t, state: s, values: f32s(rec.values), mrw: rec.mrw, phase: inst.phase, canvasWidth: canvas.width });
    }
    orbRuntime.frames.push({
      name: cfg.name, orb: KEYS[cfg.key], sizePx: cfg.size, ballPx: cfg.ball, dpr: cfg.dpr, dark: cfg.dark,
      edgeSource: cfg.edge === 'source', supersample: cfg.supersample !== false, frames,
    });
  }
  write('orb-runtime.json', orbRuntime);

  // ------------------------------------------------------------------------------------------------ OrbMotion: voice-composer.js
  {
    const src = fs.readFileSync(path.join(DESIGN, 'voice-composer.js'), 'utf8');
    const cut = (from, to, fromIdx = 0) => {
      const a = src.indexOf(from, fromIdx); if (a < 0) throw new Error('anchor missing: ' + from);
      const b = to === null ? src.length : src.indexOf(to, a); if (b < 0) throw new Error('anchor missing: ' + to);
      return [a, b];
    };
    const [ka, kb] = cut('const K = {', '\n};'), kBlock = src.slice(ka, kb + 3);
    const [ha, hb] = cut('function spring(o, target, dt, resp, zeta)', '// ------------------------------------------------------------------ page state'), helpers = src.slice(ha, hb);
    const [ta, tb0] = cut('  const hov = (S.hover', 'spring(S.scale, tScale, dt, resp, damp);'), targets = src.slice(ta, tb0 + 'spring(S.scale, tScale, dt, resp, damp);'.length);
    const [da, db] = cut('  const tD = S.orbSize;', "  if (S.mode === 'listening') {\n    if (S.v > 0.16"), dBlock = src.slice(da, db);
    const vLine = /^\s*S\.v = follow\(S\.v, S\.bands\.all.*$/m.exec(src)[0];
    const body = `${kBlock}\nconst $ = () => ({ style: {} }); let fieldDirty = false;\n${helpers}\n` +
      `return { K, ramp, spring, follow, step(S, now, dt) { ${vLine}\n${targets}\n${dBlock}\n return act; } };`;
    const motion = new Function(body)();
    const K = motion.K;
    // a scripted run: segments of constant mode / hover / pressed / size, with the smoothed `all` band changing every frame
    const segs = [
      { mode: 'rest', n: 20, hover: false }, { mode: 'rest', n: 30, hover: true }, { mode: 'rest', n: 12, hover: true, pressed: true },
      { mode: 'rest', n: 18, hover: true }, { mode: 'rest', n: 24, hover: false },
      { mode: 'listening', n: 60, hover: true, voice: true }, { mode: 'listening', n: 10, hover: true, pressed: true, voice: true },
      { mode: 'listening', n: 50, hover: false, voice: true }, { mode: 'listening', n: 30, hover: false, voice: false },
      { mode: 'processing', n: 50, hover: true }, { mode: 'processing', n: 40, hover: false },
      { mode: 'landing', n: 20, hover: true }, { mode: 'landing', n: 20, hover: false },
      { mode: 'rest', n: 40, hover: false },
      { mode: 'rest', n: 30, hover: false, size: 32 }, { mode: 'rest', n: 30, hover: false, size: 26 },
      { mode: 'error', n: 60, hover: false }, { mode: 'rest', n: 50, hover: false },
    ];
    const r = rng(7), DTS = [1 / 60, 1 / 60, 1 / 60, 1 / 144, 1 / 60, 0.02, 1 / 60, 0.033, 1 / 60, 0.05];
    const S = { hover: false, hoverSim: false, pressed: false, mode: 'rest', v: 0, bands: { all: 0 }, act: motion.ramp(0), speed: 1, glow: 0, lift: 0, desat: 0,
      scale: { p: 1, v: 0 }, D: { p: 26, v: 0 }, orbSize: 26 };
    let now = 100, size = 26, phase = 0;
    const steps = [];
    for (const g of segs) {
      if (g.size) size = g.size;
      for (let i = 0; i < g.n; i++) {
        const dt = DTS[Math.floor(r() * DTS.length)];
        now += dt;
        phase += dt;
        let all = 0;
        if (g.voice) { all = Math.max(0, Math.min(1, 0.5 + 0.45 * Math.sin(phase * 7.3) * Math.sin(phase * 1.9) + (r() - 0.5) * 0.1)); all = Math.round(all * 1000) / 1000; }
        else if (g.mode === 'listening') all = 0;
        S.mode = g.mode; S.hover = !!g.hover; S.pressed = !!g.pressed; S.orbSize = size; S.bands.all = all;
        const act = motion.step(S, now, dt);
        steps.push({ now, dt, mode: g.mode, hover: !!g.hover, pressed: !!g.pressed, size, all,
          act, v: S.v, speed: S.speed, scale: S.scale.p, scaleV: S.scale.v, glow: S.glow, lift: S.lift, desat: S.desat, d: S.D.p, dV: S.D.v });
      }
    }
    const dts = [1 / 60, 1 / 60, 0.05, 1 / 144, 0.003, 0.02, 0.004, 0.0041, 0.016, 0.033, 0.05, 0.05];
    const springs = [[1, 0, 1.16, 0.3, 0.62], [1, 0, 1.04, 0.22, 0.8], [1.16, 2, 0.94 * 1.16, 0.18, 0.8], [26, 0, 32, 0.34, 0.86], [1, 0, 1.1, 0.3, 0.75], [1, 0, 1, 0.32, 0.78]]
      .map(([p0, v0, target, resp, zeta]) => {
        const o = { p: p0, v: v0 }, samples = [];
        for (let i = 0; i < 160; i++) { motion.spring(o, target, dts[i % dts.length], resp, zeta); if (i % 8 === 0 || i > 150) samples.push({ i, p: o.p, v: o.v }); }
        return { p0, v0, target, resp, zeta, dts, samples };
      });
    const follow = [[0, 1, 0.016, 0.12, 0.45], [1, 0, 0.016, 0.12, 0.45], [0.3, 0.3, 0.05, 0.2, 0.5], [0, 1, 0.05, 0.05, 0.05], [0.9, 0.2, 0.033, 0.2, 0.5], [0.5, 0.6, 0.001, 0, 0]]
      .map(([cur, target, dt, up, down]) => ({ cur, target, dt, up, down, value: motion.follow(cur, target, dt, up, down) }));
    write('orb-motion.json', {
      source: 'voice-composer.js: the K table, the easing helpers and the per-frame orb targets, executed from the design file itself',
      K, springs, follow, steps,
    });
  }

  // ------------------------------------------------------------------------------------------------ the voice glow
  const realToFixed = Number.prototype.toFixed;
  let fixedLog = [];
  Number.prototype.toFixed = function (d) { const out = realToFixed.call(this, d); fixedLog.push([out, +this]); return out; };
  let rafCb = null;
  globalThis.requestAnimationFrame = (cb) => { rafCb = cb; return 1; };
  globalThis.cancelAnimationFrame = () => { rafCb = null; };
  const G = await patched('voice-glow.js', [
    ['function Fn(n, e, a, r) {', 'function Fn(n, e, a, r) { globalThis.__vgCfg = e;'],
    ['function Ya(n) {', 'function Ya(n) { globalThis.__vgYa = n;'],
  ], 'export { ua as __states, we as __lobes };');
  globalThis.window = { devicePixelRatio: 1 };

  function makeNode(name, reg) {
    const n = { name, attrs: {}, kids: [], style: { setProperty() {} }, textContent: '',
      setAttribute(k, v) { this.attrs[k] = v; }, removeAttribute(k) { delete this.attrs[k]; }, appendChild(c) { this.kids.push(c); return c; },
      remove() {}, getContext() { return null; } };
    if (reg) reg.push(n);
    return n;
  }
  let nodeReg = [];
  globalThis.document = { createElement: (t) => makeNode(t, nodeReg), createElementNS: (_ns, t) => makeNode(t, nodeReg) };

  function makeHost(w, h, rec) {
    const props = {};
    let cur = []; let firstStroke = null;
    const ctx = {
      filter: '', lineCap: '', lineJoin: '', globalCompositeOperation: '', strokeStyle: '', lineWidth: 0,
      setTransform() {}, clearRect() {}, createLinearGradient: () => ({ addColorStop() {} }),
      beginPath() { cur = []; }, moveTo(x, y) { cur.push([x, y]); }, lineTo(x, y) { cur.push([x, y]); },
      stroke() { if (firstStroke === null) firstStroke = cur.slice(); },
    };
    const canvas = { width: 0, height: 0, getContext: () => ctx };
    const displace = { scale: { set baseVal(v) { rec.displace = v; } } };
    const noiseShift = { dx: { set baseVal(v) { rec.dx = v; } }, dy: { set baseVal(v) { rec.dy = v; } } };
    const filter = { setAttribute() {} };
    const el = {
      clientWidth: w, clientHeight: h, firstChild: null, attrs: {},
      parentNode: { insertBefore() {} },
      style: {
        setProperty(name, value) {
          let raw = null;
          const bare = String(value).replace(/(px|deg)$/, '');
          for (let i = fixedLog.length - 1; i >= 0; i--) if (fixedLog[i][0] === bare) { raw = fixedLog[i][1]; break; }
          fixedLog = [];
          props[name] = { text: String(value), raw };
        },
      },
      setAttribute(k, v) { this.attrs[k] = v; }, removeAttribute(k) { delete this.attrs[k]; }, hasAttribute(k) { return k in this.attrs; },
      addEventListener() {}, appendChild() {}, removeChild() {},
      querySelector(sel) {
        if (sel.includes('feDisplacementMap')) return displace;
        if (sel.includes('feOffset')) return noiseShift;
        if (sel.includes('svg filter')) return filter;
        if (sel.includes('band-halo')) return null;
        if (sel.includes('data-voice-beam-band')) return canvas;
        return null;
      },
    };
    return { el, props, takeStroke() { const s = firstStroke; firstStroke = null; return s; } };
  }
  const tickAt = (ms) => { const cb = rafCb; rafCb = null; if (cb) cb(ms); };
  const num = (p, name) => { const v = p[name]; return v ? (v.raw !== null ? v.raw : +v.text.replace(/(px|deg)$/, '')) : null; };

  // --- configuration resolution (mountVoiceGlow's `resolve`)
  const MARROW = { type: 'default', scale: 0.7, glowWidth: 1.6, lobeSpacing: 2.1, rangeWidth: 1.9, processingTravel: 2.05, borderRadius: 22 };
  const propSets = [
    { name: 'Marrow dark', props: { ...MARROW, theme: 'dark', active: true, processing: false, reducedMotion: false } },
    { name: 'Marrow light', props: { ...MARROW, theme: 'light', active: true, processing: true, reducedMotion: false } },
    { name: 'Marrow dark reduced motion', props: { ...MARROW, theme: 'dark', processing: false, reducedMotion: true } },
    { name: 'package defaults, dark', props: { theme: 'dark' } },
    { name: 'package defaults, light', props: { theme: 'light' } },
    { name: 'pill, dark', props: { type: 'pill', theme: 'dark' } },
    { name: 'pill, light', props: { type: 'pill', theme: 'light' } },
    { name: 'mobile, dark', props: { type: 'mobile', theme: 'dark', borderRadius: 40 } },
    { name: 'mobile, light', props: { type: 'mobile', theme: 'light' } },
    { name: 'mono variant', props: { colorVariant: 'mono', theme: 'dark' } },
    { name: 'band colours (hex, rgb, junk) and colours list', props: { theme: 'dark', bandColors: { core: '#fff', above: 'rgb(10, 20, 30)', mid: 'rgba(1,2,3,0.5)', below: 'tomato' }, colors: ['#ff0000', 'rgb(0, 128, 255)', 'nope'] } },
    { name: 'clamps', props: { theme: 'dark', scale: 0.01, threshold: 5, sensitivity: -1, attack: -2, release: -1, idle: 4, bandSkew: 3, bandTail: 9, bandTailPosition: 3, bandAberration: 4,
      processingDuration: 0, processingLevel: 3, processingEase: 0, processingCurve: 0.1, cornerFollow: 9, hueRange: -4, hueDuration: 0.1, bandCurve: 0.1, bandSpread: 0, bandTailCurve: 0.1, breatheDuration: 0.01, lobeSpacing: 0, coreLight: 9, distortion: 7, strength: 4 } },
    { name: 'explicit values', props: { theme: 'light', type: 'pill', scale: 1.4, glowSize: 2, strokeOpacity: 0.5, innerOpacity: 0.6, bloomOpacity: 0.7, distortion: 0, coreLight: 1, strength: 0.3,
      hueRange: 10, hueDuration: 3, brightness: 1.3, saturation: 0.9, rangeHeight: 1.5, rangeWidth: 1.1, glowWidth: 0.9, glowHeight: 1.1, strokeScale: 1.1, innerScale: 1.2, innerHeight: 1.3, bloomScale: 1.4, bloomHeight: 1.5,
      coreSize: 0.5, coreLightWidth: 1.2, coreLightHeight: 1.3, softness: 0.5, sensitivity: 2, threshold: 0.1, attack: 0.2, release: 0.5, idle: 0.3, breatheDuration: 3, reach: 2, spread: 0.5, bands: false, flow: 10, bend: 30,
      bandStrength: 1, bandWidth: 1, bandPosition: 0.5, bandCurve: 1, bandSpread: 0.5, bandSkew: -0.2, bandOffset: -5, bandTail: 0.3, bandTailPosition: 0.5, bandTailCurve: 2, bandTailOverflow: 10, bandAberration: 0.5,
      processingDuration: 2, processingLevel: 0.4, processingEase: 0.3, processingTravel: 1, processingCurve: 3, cornerFollow: 0.2, staticColors: true, distortionDetail: 4, borderRadius: 8, colorVariant: 'colorful' } },
  ];
  const glowConfig = [];
  for (const ps of propSets) {
    nodeReg = []; fixedLog = []; globalThis.__vgCfg = null; globalThis.__vgYa = null;
    const rec = {}, host = makeHost(640, 80, rec);
    const g = G.mountVoiceGlow(host.el, { ...ps.props, level: () => 0 });
    const turb = nodeReg.find((n) => n.name === 'feTurbulence');
    glowConfig.push({
      name: ps.name, props: ps.props, cfg: JSON.parse(JSON.stringify(globalThis.__vgCfg)), style: JSON.parse(JSON.stringify(globalThis.__vgYa)),
      strength: +host.props['--voice-strength'].text, turbulenceFrequency: turb ? turb.attrs.baseFrequency : null, attrs: host.el.attrs,
    });
    g.dispose(); tickAt(0);
  }
  write('glow-config.json', { source: 'voice-glow.js mountVoiceGlow resolve(), captured from the Fn and Ya calls', lobes: G.__lobes, resolved: glowConfig });

  // --- the driver, frame by frame
  const FRAME_MS = [16.667, 16.667, 16.667, 20, 16.667, 33.333, 16.667, 16.667, 50, 16.667, 16.667, 100, 16.667, 16.667, 25, 16.667];   // none sustained above 22 ms: the page's jank governor never engages
  const levelAt = (i) => {
    // silence, a spoken phrase with word gaps, a loud burst, silence
    const t = i / 60;
    if (i < 20) return 0;
    if (i < 130) { const w = Math.max(0, Math.sin(t * 11) * Math.sin(t * 2.7)); return Math.round(Math.min(1, 0.04 + 0.9 * w * w) * 1000) / 1000; }
    if (i < 150) return 1;
    if (i < 170) return 0.35;
    return 0;
  };
  const glowRuns = [
    { name: 'Marrow dark 720x96: speech, then processing for 150 frames, then off', theme: 'dark', width: 720, height: 96, frames: 250, reducedMotion: false, processOn: 150, processOff: 230 },
    { name: 'Marrow light 640x72: the same with reduced motion', theme: 'light', width: 640, height: 72, frames: 150, reducedMotion: true, processOn: 90, processOff: 140 },
    { name: 'Marrow dark 480x64, radius 22, narrow composer (corner follow)', theme: 'dark', width: 480, height: 64, frames: 190, reducedMotion: false, processOn: 80, processOff: 150 },
    { name: 'pill type, dark 300x40, radius 20', theme: 'dark', width: 300, height: 40, frames: 120, reducedMotion: false, processOn: 60, processOff: 100, type: 'pill', borderRadius: 20 },
  ];
  const driverRuns = [];
  for (const run of glowRuns) {
    nodeReg = []; fixedLog = []; globalThis.__vgCfg = null;
    const rec = {}, host = makeHost(run.width, run.height, rec);
    let level = 0, frame = 0;
    const props = { ...MARROW, theme: run.theme, reducedMotion: run.reducedMotion, processing: false, level: () => level };
    if (run.type) { props.type = run.type; delete props.glowWidth; delete props.lobeSpacing; delete props.rangeWidth; delete props.processingTravel; props.scale = 0.7; }
    if (run.borderRadius) props.borderRadius = run.borderRadius;
    const g = G.mountVoiceGlow(host.el, props);
    const state = G.__states.get(host.el);
    let ms = 1000;
    const rows = [];
    const cfgs = [{ at: 0, cfg: JSON.parse(JSON.stringify(globalThis.__vgCfg)) }];
    for (let i = 0; i < run.frames; i++) {
      if (i === run.processOn) { g.update({ processing: true }); cfgs.push({ at: i, cfg: JSON.parse(JSON.stringify(globalThis.__vgCfg)) }); }
      if (i === run.processOff) { g.update({ processing: false }); cfgs.push({ at: i, cfg: JSON.parse(JSON.stringify(globalThis.__vgCfg)) }); }
      ms += FRAME_MS[i % FRAME_MS.length];
      level = levelAt(i);
      fixedLog = []; rec.displace = undefined; rec.dx = undefined; rec.dy = undefined; host.takeStroke();   // clears the last frame's path
      Object.keys(host.props).forEach((k) => delete host.props[k]);
      tickAt(ms);
      const p = host.props;
      const lobes = [];
      for (let k = 0; k < 7; k++) lobes.push({ x: num(p, `--vb-x${k}-${g.id}`), l: num(p, `--vb-l${k}-${g.id}`), y: num(p, `--vb-y${k}-${g.id}`) });
      const row = {
        i, ms, level,
        state: { level: state.level, bands: state.bands.slice(), phase: state.phase, scanA: state.scanA, scanT: state.scanT, t: state.t, warp: state.warp },
        vars: { level: num(p, `--vb-level-${g.id}`), cx: num(p, `--vb-cx-${g.id}`), mw: num(p, `--vb-mw-${g.id}`), bh: num(p, `--vb-bh-${g.id}`), bendA: num(p, `--vb-bendA-${g.id}`),
          cy: num(p, `--vb-cy-${g.id}`), glow: num(p, `--vb-glow-${g.id}`), h: num(p, `--vb-h-${g.id}`), w: num(p, `--vb-w-${g.id}`), hue: num(p, `--vb-hue-${g.id}`) },
        lobes,
        displace: rec.displace === undefined ? null : rec.displace, dx: rec.dx === undefined ? null : rec.dx, dy: rec.dy === undefined ? null : rec.dy,
        warpOff: host.el.hasAttribute('data-voice-warp'),
      };
      const stroke = host.takeStroke();
      if (stroke && (i % 8 === 0 || i === run.frames - 1)) row.band = stroke.map(([x, y]) => [x, y]);
      rows.push(row);
    }
    driverRuns.push({ name: run.name, widthPx: run.width, heightPx: run.height, theme: run.theme, props: { ...props, level: undefined }, cfgChanges: cfgs, firstMs: 1000, rows });
    g.dispose(); tickAt(0);
  }
  Number.prototype.toFixed = realToFixed;
  write('glow-driver.json', { source: 'voice-glow.js ka() (the driver), stepped by a manual requestAnimationFrame; values read from the state map, style.setProperty (raw doubles via toFixed) and the stubs', lobeCount: 7, runs: driverRuns });
})().catch((e) => { console.error(e); process.exit(1); });
