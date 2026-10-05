// VT-11 fixture (voice-SPEC V-T76, V-14.8; V-7.5, V-7.6): the voice design's own simulated speaker and live words, recorded by RUNNING the design's arithmetic, never the port:
//   porting/first-release/design-src/voice-composer/voice-composer.js (a copy of E:/Droppy/work/voice/design/voice-composer.js)   SCRIPT, rng, SYN (lines 42-83), STREAM (221-240) and planFinal (304-324), copied here without a line of arithmetic changed;
//   only what they read from the page (S.source, sess) is replaced by plain objects.
// The live stream is what the dictation layer is fed in the design: `add` events (each also confirms the word before it: the design's applyEvent removes `.tent` from the previous word)
// and `rev` events (a revision of the word's text). The final pass is what planFinal gives for a second click at `tl` seconds into listening, with the design's own `at` times.
//
//   node make-dictation-stream.js [--out <dir>]
//
// Writes dictation-stream.json beside this script (or into --out).
'use strict';
const fs = require('fs'), path = require('path');
const arg = (name, dflt) => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : dflt; };
const OUT = arg('--out', __dirname);

// ---- voice-composer.js:42-83 (the numbers K are not needed here)
const SCRIPT = [
  ['Add', [1], 0], ['a', [0], 0], ['shortcut', [1, 0], 1], ['for', [0], 1], ['the', [0], 0], ['theme', [1], 1], ['switch,', [1], 1],
  ['|', 1.35],
  ['and', [0], 0], ['show', [1], 1], ['it', [0], 0], ['in', [0], 0], ['the', [0], 0], ['tooltip', [1, 0], 1], ['too.', [1], 1],
];
function rng(seed) { let s = seed >>> 0; return () => { s = (s * 1664525 + 1013904223) >>> 0; return s / 4294967296; }; }
const SYN = (() => {
  const r = rng(91), syl = [], words = [];
  let t = 0.45;
  for (const w of SCRIPT) {
    if (w[0] === '|') { t += w[1]; continue; }
    const [text, stress, fric] = w, w0 = t;
    stress.forEach((s, i) => {
      const d = (s ? 0.21 : 0.15) + r() * 0.06;
      syl.push({ s: t, d, a: (s ? 0.82 : 0.5) + r() * 0.16, f: i === 0 && fric ? 1 : 0 });
      t += d + 0.025;
    });
    words.push({ text, s: w0, e: t });
    t += 0.05 + r() * 0.06;
    if (text.endsWith(',')) t += 0.3;
  }
  return { syl, words, end: t };
})();

// ---- voice-composer.js:221-240
const STREAM = (() => {
  const r = rng(7), ev = [];
  const firstGuess = { theme: 'team', 'switch,': 'switch', tooltip: 'tool', 'too.': 'too' };
  let last = 0;
  SYN.words.forEach((w, i) => {
    // words arrive in the order spoken (a recogniser never reorders), at least 60 ms apart
    const at = last = Math.max(last + 0.06, w.e + 0.3 + r() * 0.4);
    const guess = firstGuess[w.text];
    ev.push({ t: at, kind: 'add', i, text: guess || w.text, spokenEnd: w.e });
    // a revision as more is heard: the next word's arrival, or 0.35 s on; the final full stop waits for the final pass
    if (guess && w.text !== 'too.') ev.push({ t: at + 0.35 + r() * 0.25, kind: 'rev', i, text: w.text, spokenEnd: w.e });
  });
  ev.sort((a, b) => a.t - b.t);
  return ev;
})();

// ---- voice-composer.js:304-324, with `sess` a plain object and the page's source fixed to the speaker (not a file)
function finalAt(tl) {
  const sess = { ev: 0, texts: [], final: [] };
  // the words written by tl: the design's streamTo (voice-composer.js:296-302) applies every event due by tl, in order
  while (sess.ev < STREAM.length && STREAM[sess.ev].t <= tl) { const e = STREAM[sess.ev++]; sess.texts[e.i] = e.text; }
  const now = tl; // listenT0 = 0
  const plan = [];
  for (; sess.ev < STREAM.length; sess.ev++) {
    const e = STREAM[sess.ev];
    if (e.spokenEnd > tl + 0.12) continue;   // not spoken before the click
    // the final pass hears the word whole: a word still to be written arrives in its final form
    plan.push(e.kind === 'add' ? Object.assign({}, e, { text: SYN.words[e.i].text }) : e);
  }
  // the last word's final form (e.g. the full stop) comes with the final pass
  SYN.words.forEach((w, i) => { if (w.e <= tl + 0.12 && sess.texts[i] !== undefined && sess.texts[i] !== w.text) plan.push({ kind: 'rev', i, text: w.text }); });
  let at = now + 0.18;
  sess.final = plan.map((e) => { const x = Object.assign({}, e, { at }); at += e.kind === 'add' ? 0.07 : 0.12; return x; });
  const duration = at - now;
  return { tl, written: sess.texts.filter((t) => t !== undefined), final: sess.final.map((e) => ({ kind: e.kind, i: e.i, text: e.text, at: e.at })), duration };
}

const clicks = [SYN.end + 0.4, 3.4].map((tl) => finalAt(tl));
const out = {
  note: 'recorded from voice-composer.js SCRIPT, SYN (rng 91), STREAM (rng 7) and planFinal by make-dictation-stream.js; times in seconds from the click that starts listening',
  words: SYN.words.map((w) => ({ text: w.text, s: w.s, e: w.e })),
  stream: STREAM.map((e) => ({ t: e.t, kind: e.kind, i: e.i, text: e.text, spokenEnd: e.spokenEnd })),
  clicks,
};
const text = JSON.stringify(out, (k, v) => (typeof v === 'number' && !Number.isInteger(v) ? +v.toPrecision(13) : v)) + '\n';
fs.mkdirSync(OUT, { recursive: true });
fs.writeFileSync(path.join(OUT, 'dictation-stream.json'), text);
console.log('wrote dictation-stream.json', text.length, 'bytes;', SYN.words.length, 'words,', STREAM.length, 'stream events, clicks at', clicks.map((c) => c.tl.toFixed(3)).join(', '));
