// Records what the locked Jewel knob page computes (OD-0090), for the port's exactness tests (EffortSliderJewelTests):
// the Jewel material of several makers (knob-variants.js liquidOf / material / mixMaterial / strikeTint / mixTint), the
// uniforms the Violet Ember orb sends to its shader frame by frame (knob-orbs-1.js makeOrb().frame, run against a
// recording WebGL2 stub), the page's knob springs (jewel-knob.html spring(), makeSlider / stepSlider, tick()'s cap on a
// frame's step), the bolt's exit (stepSlider's tl.exAt), the frame gate (strike-engine.js frameGate) and its reading of
// the strike's signal (jewel-knob.html part(), shape(), rawSignal(), share(); strike-engine.js plan0 / orbAim / pulseI),
// fed with the port's fast-mode strikes (A2 at strikeAt, each with the locked Mix rule's charge: 0.14 + 0.12 H(131)).
//   node tests/fixtures/jewel-knob/expected.mjs [the locked folder] > tests/fixtures/jewel-knob/expected.json
// Source: E:/Droppy/work/design/jewel-knob-locked/ (the sha256 of every file read is written into the output).
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { pathToFileURL } from 'node:url';

const dir = process.argv[2] || 'E:/Droppy/work/design/jewel-knob-locked';
const files = ['jewel-knob.html', 'knob-variants.js', 'knob-orbs-1.js', 'strike-engine.js', 'liquid-palettes.json'];
const sha = Object.fromEntries(files.map((f) => [f, crypto.createHash('sha256').update(fs.readFileSync(path.join(dir, f))).digest('hex')]));
const KV = await import(pathToFileURL(path.join(dir, 'knob-variants.js')).href);
const Component = (await import(pathToFileURL(path.join(dir, 'strike-engine.js')).href)).default;
const PAL = JSON.parse(fs.readFileSync(path.join(dir, 'liquid-palettes.json'), 'utf8'));
const html = fs.readFileSync(path.join(dir, 'jewel-knob.html'), 'utf8');
const cut = (from, to) => { const a = html.indexOf(from), b = html.indexOf(to, a); if (a < 0 || b < 0) throw new Error('not found: ' + from); return html.slice(a, b); };
const out = { source: sha };

// ---- materials (knob-variants.js), at the app's knob size (48 px: the 'm' recipe)
const MAKERS = ['Claude', 'OpenAI', 'Gemini', 'DeepSeek', 'Grok', 'Zai', 'Pi', 'Cursor'];
out.materials = {};
for (const m of MAKERS) {
  const q = KV.liquidOf(PAL, m, 'dark');
  out.materials[m] = { liquid: { C: q.C, achro: q.achro, multi: q.multi, blue: q.blue, Lm: q.Lm, Ld: q.Ld }, material: KV.material('jewel', q, 48), tint: KV.strikeTint(q) };
}
out.materials140 = KV.material('jewel', KV.liquidOf(PAL, 'Claude', 'dark'), 140);
out.materials24 = KV.material('jewel', KV.liquidOf(PAL, 'Claude', 'dark'), 24);
const mc = KV.material('jewel', KV.liquidOf(PAL, 'Claude', 'dark'), 48), mo = KV.material('jewel', KV.liquidOf(PAL, 'OpenAI', 'dark'), 48);
out.mix = [0.25, 0.5, 0.75].map((u) => ({ u, material: KV.mixMaterial(mc, mo, u), tint: KV.mixTint(KV.strikeTint(KV.liquidOf(PAL, 'Claude', 'dark')), KV.strikeTint(KV.liquidOf(PAL, 'OpenAI', 'dark')), u) }));
// to a blue liquid, whose material takes a smaller share of the lightning (fx 0.36 / 0.15 against 0.58 / 0.24)
const md = KV.material('jewel', KV.liquidOf(PAL, 'DeepSeek', 'dark'), 48);
out.mixBlue = [0.25, 0.5, 0.75].map((u) => ({ u, material: KV.mixMaterial(mc, md, u) }));

// ---- the orb's uniforms per frame (knob-orbs-1.js), through a recording WebGL2 stub
function stubGL(log) {
  const cap = {
    getShaderParameter: () => true, getProgramParameter: () => true, getUniformLocation: (p, n) => n, getExtension: () => null,
    uniform1f: (l, a) => { log[l] = a; }, uniform2f: (l, a, b) => { log[l] = [a, b]; }, uniform3f: (l, a, b, c) => { log[l] = [a, b, c]; },
    uniform4f: (l, a, b, c, d) => { log[l] = [a, b, c, d]; }, uniform1fv: (l, v) => { log[l] = Array.from(v); }, uniform3fv: (l, v) => { log[l] = Array.from(v); },
    uniform1i: (l, a) => { log[l] = a; },
  };
  return new Proxy({}, { get: (t, k) => (k in cap ? cap[k] : typeof k === 'string' && /^[A-Z_0-9]+$/.test(k) ? 1 : () => ({})) });
}
function orbRun(material, dpr, script, dark = true) {
  const log = {}, gl = stubGL(log);
  const canvas = { width: 0, height: 0, style: {}, clientWidth: 48, getContext: () => gl };
  const orb = KV.ORB['violet-ember'].make(canvas, { dark, colors: material, context: 'own', dpr });
  orb.resize(48);
  const frames = [];
  for (const [t, s, keep] of script) { orb.frame(t, s); if (keep) frames.push({ t, s, u: JSON.parse(JSON.stringify(log)) }); }
  return { size: [canvas.width, canvas.height], frames };
}
// 60 fps for 3 s: rest, a charge build (0.5 to 0.7 s), the strike's flare (0.7 to 1.2 s), the charge let go at 1.2 s
const script = [];
for (let i = 0; i <= 180; i++) {
  const t = i / 60;
  const c = t < 0.5 ? 0 : t < 0.7 ? 0.58 * ((t - 0.5) / 0.2) : t < 1.2 ? 0.58 : 0;
  const f = t < 0.7 ? 0 : t < 1.2 ? 0.24 * Math.exp(-(t - 0.7) / 0.3) : 0;
  script.push([t, { charge: c, flare: f, exitAngle: Math.PI * 0.9, reduceMotion: false }, i % 6 === 0]);
}
out.orb = orbRun(mc, 1, script);
out.orbDpr2 = orbRun(mc, 2, script.slice(0, 2).map(([t, s]) => [t, s, true]));
out.orbLight = orbRun(mc, 1, script.slice(0, 2).map(([t, s]) => [t, s, true]), false);
out.orbReduce = orbRun(mc, 1, script.map(([t, s, k]) => [t, { ...s, reduceMotion: true }, k]));

// ---- the page's springs and its slider (jewel-knob.html spring(), makeSlider, stepSlider), DOM-free
const spring = new Function(cut('function spring(o, target, dt, resp, zeta)', 'const grid =') + '\nreturn spring;')();
function slider(W, kn, n, index) {
  const stopX = (i) => kn / 2 + (i * (W - kn)) / (n - 1);
  const S = { index, held: false, finger: 0, grab: 0, off: { p: 0, v: 0 }, sc: { p: 1, v: 0 }, resp: 0.32, zeta: 0.78, x: stopX(index) };
  const clampX = (x) => Math.min(W - kn / 2, Math.max(kn / 2, x));
  const nearest = (x) => Math.round(((clampX(x) - kn / 2) / (W - kn)) * (n - 1));
  const retarget = (target) => { S.off.p = S.x - target; };
  return {
    S, stopX,
    down(px) { const onKnob = Math.abs(px - S.x) <= kn / 2; S.grab = onKnob ? px - S.x : 0; S.held = true; S.finger = clampX(px - S.grab); retarget(S.finger); S.resp = 0.22; S.zeta = 0.8; S.index = nearest(S.finger); },
    move(px) { if (!S.held) return; S.finger = clampX(px - S.grab); S.index = nearest(S.finger); },
    up() { if (!S.held) return; S.held = false; retarget(stopX(S.index)); S.resp = 0.32; S.zeta = 0.78; },
    key(d) { const i = Math.min(n - 1, Math.max(0, S.index + d)); if (i === S.index) return; retarget(stopX(i)); S.resp = 0.2; S.zeta = 1; S.index = i; },
    step(dt) {
      const target = S.held ? S.finger : stopX(S.index);
      spring(S.off, 0, dt, S.resp, S.zeta);
      spring(S.sc, S.held ? 1.06 : 1, dt, S.held ? 0.22 : 0.32, S.held ? 0.8 : 0.78);
      S.x = Math.min(W - kn / 2 + 3, Math.max(kn / 2 - 3, target + S.off.p));
      return { x: S.x, scale: S.sc.p, lift: Math.max(0, Math.min(1, (S.sc.p - 1) / 0.06)), index: S.index };
    },
  };
}
// tick()'s cap on the slider's step (jewel-knob.html: dt = min(CAP, max(0, t - sliderT))), read from the page
const capMatch = html.match(/const dt = sliderT === null \? 0 : Math\.min\(([\d.]+), Math\.max\(0, t - sliderT\)\);/);
if (!capMatch) throw new Error('not found: tick() dt cap');
const DT_CAP = +capMatch[1];
// a step's frames come 1/60 s apart unless it gives its own frame length (dt, s: a long frame, as after a stall)
function run(W, events) {
  const s = slider(W, 48, 4, events.index);
  const rec = [];
  let t = 0;
  for (const e of events.steps) {
    if (e.down != null) s.down(e.down);
    if (e.move != null) s.move(e.move);
    if (e.up) s.up();
    if (e.key) s.key(e.key);
    const frame = e.dt ?? 1 / 60;
    for (let i = 0; i < (e.frames || 0); i++) { t += frame; rec.push({ t: +t.toFixed(6), ...s.step(Math.min(DT_CAP, Math.max(0, frame))) }); }
  }
  return rec;
}
out.slider = {
  W: 298,
  dtCap: DT_CAP,
  trackPress: run(298, { index: 0, steps: [{ down: 200, frames: 36 }, { up: true, frames: 60 }] }),
  knobPress: run(298, { index: 0, steps: [{ down: 34, frames: 30 }, { move: 150, frames: 30 }, { up: true, frames: 60 }] }),
  key: run(298, { index: 1, steps: [{ key: 1, frames: 30 }] }),
  // one frame 0.2 s long after a press on the track (a stall): the springs move by the cap, not the whole gap
  longFrame: run(298, { index: 0, steps: [{ down: 200, frames: 1, dt: 0.2 }] }),
  // held at 200 for a second, released, one frame 1 s long, then the springs settle onto the stop
  longRelease: run(298, { index: 0, steps: [{ down: 200, frames: 60 }, { up: true, frames: 1, dt: 1 }, { frames: 90 }] }),
  // the near end, on a 600 px track: from the last stop, pressed on the track at its start, the spring overshoots the
  // first stop (24) and is held 3 px past it
  nearEnd: run(600, { index: 3, steps: [{ down: 2, frames: 60 }] }),
  // pressed 23.5 px from the knob's centre (inside its 24 px radius): it lifts where it is and keeps the grip
  knobEdge: run(298, { index: 0, steps: [{ down: 47.5, frames: 20 }, { move: 150, frames: 10 }] }),
};

// ---- the bolt's exit (jewel-knob.html stepSlider, run as it is on a stub of its elements: tl.exAt, set from the
// knob's centre x on the tube), at each stop of the app's 298 px track with its 48 px knob
const STOPS = new Function(cut('const STOPS = ', '\n') + '\nreturn STOPS;')();
const stopX = new Function('STOPS', cut('const stopX = (T, i)', '\n') + '\nreturn stopX;')(STOPS);
const LIFT = new Function(cut('const LIFT = ', '\n') + '\nreturn LIFT;')();
const f2 = new Function(cut('const f2 = (v)', '\n') + '\nreturn f2;')();
function exitOf(T, index) {
  const zap = {}, tl = {};
  const stepSlider = new Function('spring', 'stopX', 'placeScenes', 'E', 'f2', 'LIFT', cut('function stepSlider(S, dt)', 'function build()') + '\nreturn stepSlider;')(
    spring, stopX, () => {}, { state0: new Map([[zap, tl]]) }, f2, LIFT);
  const S = { T, index, held: false, finger: 0, grab: 0, off: { p: 0, v: 0 }, sc: { p: 1, v: 0 }, resp: 0.32, zeta: 0.78,
    kw: { style: {} }, lift: null, liq: { style: {} }, zap, x: stopX(T, index), shown: null };
  stepSlider(S, 0);
  return { index, x: S.x, ex: tl.exAt };
}
out.exit = [0, 1, 2, 3].map((i) => exitOf({ W: 298, kn: 48 }, i));
const EX = out.exit[3].ex; // the knob at its top stop, 274: the page's bolt leaves at 272

// ---- the frame gate (strike-engine.js frameGate, which jewel-knob.html's loop calls on every display refresh and draws
// only when it passes), on the frames the port's rig delivers: the first, then each gap (whole ticks of 100 ns) after it.
// Each run records the frames drawn (0 the first).
function gateRun(name, gaps) {
  const G = new Component({ dark: true });
  let t = 0;
  const draws = [];
  if (G.frameGate(t)) draws.push(0);
  gaps.forEach((ticks, i) => { t += ticks / 1e4; if (G.frameGate(t)) draws.push(i + 1); });
  return { name, gaps, draws };
}
const even = (hz, seconds) => Array.from({ length: Math.round(hz * seconds) }, () => Math.round(1e7 / hz));
const jittered = (hz, seconds, ms, seed) => {
  let x = seed >>> 0;
  const rnd = () => { x = (Math.imul(x, 1103515245) + 12345) >>> 0; return (x >>> 8) / 16777216; };
  return Array.from({ length: Math.round(hz * seconds) }, () => Math.round((1e3 / hz + (rnd() * 2 - 1) * ms) * 1e4));
};
out.gate = [
  ...[60, 75, 90, 120, 144, 165, 240].map((hz) => gateRun(`${hz} Hz`, even(hz, 2))),
  gateRun('239 Hz', even(239, 4)),
  gateRun('60 Hz, frames 16, 17 and 17 ms apart', Array.from({ length: 60 }, (_, i) => [160000, 170000, 170000][i % 3])),
  gateRun('239 Hz, each frame up to 0.4 ms early or late', jittered(239, 4, 0.4, 7)),
  gateRun('144 Hz, each frame up to 0.6 ms early or late', jittered(144, 2, 0.6, 11)),
  gateRun('240 Hz, then a 100 ms stall, then a frame 1 ms after the last', [...even(240, 1), 1000000, ...even(240, 0.5), 10000, ...even(240, 0.5)]),
  // neither the stall (50 ms or more) nor frames under 2 ms apart are refreshes: the gate keeps the 240 Hz it measured
  gateRun('240 Hz, then a 300 ms stall, then frames 1.5 ms apart', [...even(240, 0.5), 3000000, ...Array.from({ length: 60 }, () => 15000)]),
  // nor is a gap of 80 ms (a bound above 80 ms would measure it and set the refresh to 11.75 ms)
  gateRun('240 Hz, then an 80 ms stall, then frames 1.5 ms apart', [...even(240, 0.5), 800000, ...Array.from({ length: 60 }, () => 15000)]),
  // the bound itself, 50 ms (strike-engine.js:60 d < 50), told from both sides by a stall of each: a gap of 55 ms is not a
  // refresh (a bound of 60 or 120 would measure it: 47 frames drawn where the page draws 38) and a gap of 45 ms is (a bound
  // of 40 would not measure it, and the draws then differ from the page's). The frames after each, 1.5 ms apart, are not
  // refreshes either (under 2 ms), so what the stall set is what is drawn from then on. The pair pins the knob's bound in
  // (45, 55]; a gap of exactly 50 ms is not asked (the page's time is a float sum of 100 ns ticks, the port's a whole count
  // of them: the two round differently at the bound itself).
  gateRun('240 Hz, then a 45 ms stall, then frames 1.5 ms apart', [...even(240, 0.5), 450000, ...Array.from({ length: 60 }, () => 15000)]),
  gateRun('240 Hz, then a 55 ms stall, then frames 1.5 ms apart', [...even(240, 0.5), 550000, ...Array.from({ length: 60 }, () => 15000)]),
  // the lower bound, 2 ms (d > 2): frames 2.5 ms apart are refreshes the gate measures (a bound of 3 ms would take them for
  // nothing and keep the 16.7 ms it started with, and draw otherwise)
  gateRun('60 Hz, then frames 2.5 ms apart', [...even(60, 0.5), ...Array.from({ length: 200 }, () => 25000)]),
  gateRun('60 Hz, then 240 Hz (the window moved to a faster display)', [...even(60, 1), ...even(240, 1)]),
  gateRun('240 Hz, then 60 Hz (the window moved to a slower display)', [...even(240, 1), ...even(60, 1)]),
];

// ---- the knob behind the gate (jewel-knob.html:867-871: the loop asks frameGate on every display refresh and runs tick()
// on the refreshes it passes; tick(), :813, steps the slider by the time since the last DRAWN frame, at most the cap), on a
// 240 Hz display: the rig's first frame (1/60 s), 240 refreshes (the refresh measured), a press on the track at 200, 40
// refreshes held, the release, 360 refreshes (1.5 s: settled on the stop). Each refresh after the press is recorded, drawn
// or not (the knob as shown).
function gated(W, index, events) {
  const G = new Component({ dark: true });
  const s = slider(W, 48, 4, index);
  let t = 0, sliderT = null;
  let shown = { x: s.S.x, scale: s.S.sc.p, lift: 0, index: s.S.index };
  const rec = [];
  for (const e of events) {
    if (e.down != null) s.down(e.down);
    if (e.up) s.up();
    for (let i = 0; i < e.frames; i++) {
      t += e.ticks / 1e4;
      const drawn = G.frameGate(t);
      if (drawn) {
        const tt = t / 1000;
        const dt = sliderT === null ? 0 : Math.min(DT_CAP, Math.max(0, tt - sliderT));
        sliderT = tt;
        shown = s.step(dt);
      }
      if (e.keep) rec.push({ ticks: e.ticks, drawn, ...shown, index: s.S.index });
    }
  }
  return rec;
}
out.slider.hz240 = gated(298, 0, [{ ticks: 166667, frames: 1 }, { ticks: 41667, frames: 240 }, { down: 200, ticks: 41667, frames: 40, keep: true },
  { up: true, ticks: 41667, frames: 360, keep: true }]);

// ---- the strike's signal as the knob reads it (jewel-knob.html), on the port's A2 strikes with the Mix charge
const E = new Component({ dark: true });
const H = (k, S, s) => E.hash(k, S * 13 + s);
const seed = 13 * 97 + 13, W = 298, h = 34;
const g = { w: W, h, cy: h / 2, xs: h * 0.32, ex: EX, kr: 24, k: Math.max(0.62, Math.min(1, h / 40)), seed, tl: { cache: {}, n: 0 } };
const plans = new Map();
const planOf = (k) => { if (!plans.has(k)) plans.set(k, Object.assign({}, E.plan0(g, k), { charge: 0.14 + 0.12 * H(k, seed, 131) })); return plans.get(k); };
const E2 = Object.create(E);
E2.orbList = (tl, gg, T) => { const list = [], k0 = Math.floor(T / 2.4); for (let k = k0 - 2; k <= k0 + 1; k++) { const B = planOf(k), a = T - B.at; if (a >= -B.lead - B.charge && a <= 1.9) list.push([B, a]); } return list; };
const sig = new Function('E', 'reduce', 'RAW', cut('const ss = (a, b, x)', '// ---- the loop') + '\nreturn { part, shape, rawSignal, signal, share };')(E2, false, false);
const tl = { g, off: 0 };
const st = { th: Math.PI }, raw = { th: Math.PI };
out.signal = { seed, geometry: { w: W, h, knob: W - 24, ex: EX, radius: 24 }, strikes: [], frames: [] };
for (let k = 0; k <= 4; k++) { const B = planOf(k); out.signal.strikes.push({ k, at: B.at, lead: B.lead, charge: B.charge, th: E.orbGeo(g, B).th, flick: B.flick }); }
// orbGeo's exit angle for knob radii from 16 to 30 px (strikes 0 to 3), so the rim test (radius less half a pixel) is pinned
out.signal.exits = [];
for (let kr = 16; kr <= 30; kr += 0.125) for (let k = 0; k <= 3; k++) {
  const g2 = { ...g, kr, tl: { cache: {}, n: 0 } };
  out.signal.exits.push({ kr, k, th: E.orbGeo(g2, Object.assign({}, E.plan0(g2, k))).th });
}
for (let i = 0; i <= 600; i++) {
  const t = i / 60;
  sig.signal(tl, t, st);
  const r = sig.rawSignal(tl, t, raw);
  const m = { charge: 0, flare: 0, exitAngle: 0, reduceMotion: false };
  sig.share(mc, st, m);
  out.signal.frames.push({ t, rawCharge: r.charge, rawFlare: r.flare, charge: st.charge, flare: st.flare, exit: st.exitAngle, phase: st.p, shared: [m.charge, m.flare] });
}

// ---- the bolt's ink on light liquids (jewel-knob.html inkOf, mixTint2; the owner, 2026-10-03: the lightning and the
// bubbles must contrast with every maker's liquid)
const inkFns = new Function('strikeTint', 'mixTint', cut('const sRGBlin = (v)', 'E.TINT = {') + ';return { inkOf, tintOf, mixTint2 };')(KV.strikeTint, KV.mixTint);
out.ink = {};
for (const m of ['Claude', 'OpenAI', 'Gemini', 'DeepSeek', 'Meta', 'Grok', 'Cursor', 'OpenCode', 'OpenCodeGo', 'Zai', 'Devin', 'Copilot', 'CommandCode', 'Pi'])
  out.ink[m] = inkFns.inkOf(KV.liquidOf(PAL, m, 'dark'));
out.inkMix = [0.25, 0.5].map((u) => ({ u, tint: inkFns.mixTint2(inkFns.tintOf(KV.liquidOf(PAL, 'Claude', 'dark')), inkFns.tintOf(KV.liquidOf(PAL, 'OpenAI', 'dark')), u) }));

// ---- bubble bursts (OD-0078 as the locked engine draws them: strike-engine.js events / near / process / contact on the
// v22 bubbles, riseOf / laneAt / bubNow), on the port's fast mode: A2 strikes (lvl 0: plan0 at strikeAt), the bubbles at
// real time (no simmer clock), the port's tube (298 x 34, the knob at 274: the bolt leaves at the page's exAt, 272),
// irregular frames with a few long gaps
function burstRun(strikeSeed, bubbleSeed, lanes, seconds) {
  const B = new Component({ dark: true });
  const tl = { seed: strikeSeed, off: 0, lvl: 0, bubKey: `${bubbleSeed},${lanes},0,0`, cache: {}, n: 0 };
  const gg = { w: 298, h: 34, cy: 17, xs: 34 * 0.32, ex: EX, kr: 24, k: Math.max(0.62, Math.min(1, 34 / 40)), seed: strikeSeed, tl };
  B.makeBubbles(tl, gg);
  const bursts = [];
  // with the burst's own number and turn (contact(): n = floor(hash(977 i + rise, seed) 1e6), rot = (n % 628) / 100),
  // which place its ring's arcs and its glints
  tl.onBurst = (k, i, te, st) => { const b = tl.bursts[tl.bursts.length - 1]; bursts.push({ k, i, t: te, x: st.x, y: st.y, r: st.r, a: st.a, rise: st.k, n: b.n, rot: b.rot }); };
  let t = 0, x = 12345;
  const rnd = () => { x = (Math.imul(x, 1103515245) + 12345) >>> 0; return (x >>> 8) / 16777216; };
  const frames = [];
  while (t < seconds) {
    const r = rnd();
    t += r < 0.01 ? 0.5 : 0.008 + 0.05 * rnd();
    frames.push(+t.toFixed(6));
    B.process(tl, gg, +t.toFixed(6));
  }
  return { strikeSeed, bubbleSeed, lanes, frames, bursts };
}
out.bursts = [burstRun(1274, 13, 9, 240), burstRun(97 * 5 + 13, 5, 9, 240)];

// ---- the crossfade (jewel-knob.html stepFade): smoothstep over 0.6 s
const ease = new Function(cut('const ease = (x)', 'let liquid') + '\nreturn ease;')();
out.fade = [0, 0.1, 0.3, 0.45, 0.6, 0.7].map((t) => ({ t, u: ease(t / 0.6) }));
process.stdout.write(JSON.stringify(out));
