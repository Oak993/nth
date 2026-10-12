// Calculi (ED-NEW-calculi-wu1-1): writes nth-beads-reference.json, the reference engine's own samples for the cases that
// NthBeadsEngineTests replays against the C# port. Run from anywhere:
//   node tests/Marrow.UI.Tests/Nth/Fixtures/nth-beads-reference.js [path to work/design/working-motion/reference]
// session() below is reference/stress.js's STRESS.session line for line, with three additions that default to the original:
// the entrance and exit lists (the shipped pair A: Scriptio or Ortus in, Occasus, error or stop out), a quiet engine, and
// random line reflows. It first proves it is the original by matching STRESS.session sample for sample with the full lists.
const path = require('path'), fs = require('fs');
const REF = process.argv[2] || 'E:/Droppy/work/design/working-motion/reference';
const K = require(path.join(REF, 'engine.js')), STRESS = require(path.join(REF, 'stress.js'));
const STATES = STRESS.STATES;
// ED-NEW-calculi-wu1-3: Machina's left stroke runs in [0, .56] of the cycle (the reference's [-.06, .5] left it 0.17 units off
// Pedes at t = 0 and T). The port carries the fix, so the fixture is made with it; stress.js 200 40 passes every gate with it.
{ const st = v => (v <= 0 || v >= 1) ? 0 : Math.sin(Math.PI * v) ** 2, make = K.MOT.machina.make;
  K.MOT.machina.make = (n, v) => { const m = make(n, v), T = m.T, A = 1.42 * (1 + .08 * v.b), RT = 1.64;
    m.at = (t, k) => { const u = t / T, pL = st(u / .56), pR = st((u - .44) / .56);
      if (k === 0) return [...K.wire(A * pL), 1, 1]; if (k === 2) return [...K.wire(K.LW - A * pR), 1, 1]; return [.22 * (pR - pL), RT - .16 * (pL + pR), 1, 1]; };
    return m; }; }
const rng = seed => { let a = seed >>> 0; return () => { a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; };
const FULL = { enters: ['scriptio', 'ortus', 'condensatio'], exits: ['occasus', 'reditus', 'recessus', 'fail', 'stop'] };
const PAIR_A = { enters: ['scriptio', 'ortus'], exits: ['occasus', 'fail', 'stop'] };

function session(seed, secs, calmP, FS, opt) {
  FS = FS || 240; opt = opt || {}; const L = opt.lists || PAIR_A, NE = L.enters.length, NX = L.exits.length;
  const r = rng(seed * 7919 + 1), e = new K.Engine(seed, { quiet: !!opt.quiet }); e.state = STATES[1 + Math.floor(r() * 7)];
  e.calm = r() < calmP; const sc = r(), early = sc < .12, reenter = sc >= .12 && sc < .42;
  const en = early && r() < .7 ? 'scriptio' : L.enters[Math.floor(r() * NE)], ex = L.exits[Math.floor(r() * NX)], ex2 = L.exits[Math.floor(r() * NX)], en2 = L.enters[Math.floor(r() * NE)];
  const doExit = (x, at) => { if (x === 'fail') e.fail(at); else if (x === 'stop') e.stop(at); else e.finish(x, [2.6, 3.4, .3], at); };
  const ev = []; let toggled = false;
  const randomEvents = (t, until) => { while (t < until) { const p = r();
      if (p < .45) { const s = STATES[Math.floor(r() * 8)]; ev.push([t, at => e.setState(s, at)]); }
      else if (p < .9) ev.push([t, at => e.step(at)]);
      else if (!toggled && calmP > 0 && r() < .3) { toggled = true; ev.push([t, at => e.setCalm(!e.calm, at)]); }
      t += .15 + r() * r() * 4; } };
  const exitAt = early ? .05 + r() * .5 : secs - 2.5; randomEvents(.3 + r() * 2, exitAt); ev.push([exitAt, at => doExit(ex, at)]);
  let end = secs, lastExit = exitAt, lastKind = ex, reAt = null;
  if (reenter) { reAt = exitAt + .05 + r() * .9; ev.push([reAt, at => e.enter(en2, { at })]); randomEvents(reAt + .3 + r(), reAt + 3);
    lastExit = reAt + 3.2; lastKind = ex2; ev.push([lastExit, at => doExit(ex2, at)]); end = Math.max(end, lastExit + 3); }
  if (opt.reflow) { const q = rng(seed * 31 + 7); for (let t = .4 + q(); t < end - .5; t += .3 + q() * 2.5) { const dx = (q() - .5) * 2, dy = (q() - .5) * 4; ev.push([t, at => e.reflow(dx, dy, at)]); } }
  ev.sort((a, b) => a[0] - b[0]);
  const hand = [], gone = []; e.on('move', () => hand.push(e.t0)); e.on('gone', t => gone.push(t)); e.enter(en, { at: 0 });
  const N = Math.round(end * FS), rec = []; let j = 0;
  for (let i = 0; i <= N; i++) { const t = i / FS; while (j < ev.length && ev[j][0] <= t) { ev[j][1](ev[j][0]); j++; }
    e.update(t); const f = e.frame();
    if (!f) { rec.push(null); continue; }
    let sl = 0; if (f.stroke) for (const s of f.stroke) sl += Math.max(0, s[1] - s[0]);
    rec.push({ t, kind: f.kind, name: f.name, flight: f.flight, calm: e.calm, state: e.state, stroke: sl, flow: f.flow, b: f.beads.map(b => [b.x, b.y, b.s, b.o, b.hidden]) }); }
  return { rec, seed, en, ex: lastKind, en2, early, reenter, calm: e.calm, FS, handoffs: hand, gone };
}

// 1. This session() is STRESS.session: same samples with the full lists, no quiet engine and no reflow.
for (const seed of [1, 2, 3, 7, 11, 19]) {
  const a = STRESS.session(K, seed, 12, .25, 240), b = session(seed, 12, .25, 240, { lists: FULL });
  if (a.rec.length !== b.rec.length) throw new Error('length differs for seed ' + seed);
  for (let i = 0; i < a.rec.length; i++) { const x = a.rec[i], y = b.rec[i]; if (!x !== !y) throw new Error('presence differs ' + seed + '@' + i);
    if (x && JSON.stringify(x.b) !== JSON.stringify(y.b)) throw new Error('samples differ for seed ' + seed + ' at ' + i); }
}

// 2. The cases: plain, Reduce Motion, an exit asked during the entrance, a new turn while leaving, quiet engines and reflows.
const SECS = 16, STRIDE = 24; // samples kept at 10 Hz of the 240 Hz run; every hand-off and gone time is kept exactly
const want = [['plain', 4, {}], ['calm', 3, {}], ['early', 3, {}], ['reenter', 4, {}], ['quiet', 3, { quiet: true }], ['reflow', 3, { reflow: true }]];
const cases = [], used = new Set();
for (const [tag, n, opt] of want) {
  let got = 0;
  for (let seed = 1; got < n && seed < 5000; seed++) {
    if (used.has(seed + tag)) continue; const S = session(seed, SECS, .25, 240, opt);
    const fits = tag === 'calm' ? S.calm && !S.early : tag === 'early' ? S.early : tag === 'reenter' ? S.reenter && !S.calm : tag === 'plain' ? !S.calm && !S.early && !S.reenter : true;
    if (!fits) continue; used.add(seed + tag); got++;
    const r6 = x => +x.toPrecision(10);
    const frames = [];
    for (let i = 0; i < S.rec.length; i += STRIDE) { const f = S.rec[i];
      frames.push(f ? [i, f.name, f.hidden ? 1 : 0, r6(f.stroke), f.flow ? r6(f.flow[0]) : 0, f.flow ? r6(f.flow[1]) : 0, ...f.b.flatMap(b => [r6(b[0]), r6(b[1]), r6(b[2]), r6(b[3]), b[4] ? 1 : 0])] : [i]); }
    cases.push({ tag, seed, quiet: !!opt.quiet, reflow: !!opt.reflow, secs: SECS, calmP: .25, en: S.en, en2: S.en2, ex: S.ex, early: S.early, reenter: S.reenter,
      samples: S.rec.length, handoffs: S.handoffs.map(r6), gone: S.gone.map(r6), frames });
  }
}
const out = path.join(__dirname, 'nth-beads-reference.json');
fs.writeFileSync(out, JSON.stringify({ source: 'work/design/working-motion/reference (engine.js, stress.js)', stride: STRIDE, fs: 240, cases }));
console.log('wrote', cases.length, 'cases,', fs.statSync(out).size, 'bytes');
