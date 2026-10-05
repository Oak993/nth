// Voice Composer review page: the composer's voice orb (OD-0091), its states, the voice glow on the composer and the
// Border beam light giving way. One frame clock drives everything; every change is eased (OD-0076).
import { orbs } from './voice-orbs.js';
import { BEAM_CSS, makeBeam } from './voice-beam.js';
import { mountVoiceGlow } from './voice-glow.js';

const $ = (id) => document.getElementById(id);
const ORB = { siri: orbs.find((o) => o.key === 'siri'), membrane: orbs.find((o) => o.key === 'voiceWave') };
const store = {
  get(k, d) { try { const v = localStorage.getItem('vc.' + k); return v == null ? d : v; } catch (e) { return d; } },
  set(k, v) { try { localStorage.setItem('vc.' + k, v); } catch (e) { /* storage blocked: the page works without it */ } },
};

// ------------------------------------------------------------------ the numbers (shown in the rail and the table)
const K = {
  restBase: 1.0,                                                         // rest is the source's Idle, at its own speed
  restAct: 0,                                                            // and its own look (the export's idle seed)
  hoverSpeed: 2.0, voiceSpeed: 2.2, procSpeed: 4.6, errSpeed: 0.6,       // multiples of the resting speed
  hoverAct: 0.3, hoverScale: 1.04, listenScale: 1.16, voiceScale: 0.04, procScale: 1.1, pressScale: 0.94,
  hoverGlow: 0.55, listenGlow: 0.45, voiceGlow: 0.4, procGlow: 0.75,
  actUp: 0.22, actDown: 0.65,                                            // the source's activation and settle (s)
  vAttack: 0.12, vRelease: 0.45,                                         // the speed follower on the voice
  popResp: 0.3, popDamp: 0.62, settleResp: 0.32, settleDamp: 0.78, hoverResp: 0.22, hoverDamp: 0.8,
  stagger: 0.026, sendHold: 3.0, finalMin: 0.75,
};
const MODELS = [
  { id: 'opus', name: 'Opus', claude: true, liquid: '#CC6B47', dot: '#CC6B47' },
  { id: 'gpt', name: 'GPT-5', liquid: '#D9DBE3', dot: '#D9DBE3' },
  { id: 'gemini', name: 'Gemini', liquid: '#9C73CC', dot: 'linear-gradient(135deg,#9C73CC,#4285F5)' },
  { id: 'deepseek', name: 'DeepSeek', liquid: '#3D5CFA', dot: '#3D5CFA' },
];
// how listening shows: A the cancel cross alone; B the orb opens sideways into a status pill inside the composer;
// C the orb lifts into a status pill floating above the composer (owner, 2026-10-03)
const VARIANTS = {
  a: { name: 'Cross', pill: null },
  b: { name: 'Pill in the composer', pill: 'inline' },
  c: { name: 'Floating pill', pill: 'float' },
};
const PILL = { inline: { H: 40, top: 6, font: 13.5 }, float: { H: 44, top: -52, font: 15 } };
const LABELS = { listening: 'Listening\u2026', transcribing: 'Transcribing\u2026', finishing: 'Finishing\u2026' };

// ------------------------------------------------------------------ the simulated voice: syllables, words, pauses
// A dictation of one line, spoken at a natural pace: stressed syllables louder, function words light, the level
// dipping between words, a comma pause and one long thinking pause (the orb must fall back to its resting speed).
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
const hash = (n) => { const x = Math.sin(n * 127.1) * 43758.5453; return x - Math.floor(x); };
const vnoise = (t) => { const i = Math.floor(t), f = t - i, u = f * f * (3 - 2 * f); return hash(i) * (1 - u) + hash(i + 1) * u; };
function synthAt(t, out) {
  let lv = 0, hi = 0;
  for (const s of SYN.syl) {
    const u = (t - s.s) / s.d;
    if (u < -0.2 || u > 1.3) continue;
    const sh = u < 0 ? 0 : u < 0.28 ? Math.sin(Math.PI / 2 * u / 0.28) ** 2 : u < 1 ? Math.cos(Math.PI / 2 * (u - 0.28) / 0.72) ** 1.4 : 0;
    lv = Math.max(lv, s.a * sh);
    if (s.f) hi = Math.max(hi, 0.85 * Math.exp(-(((t - s.s + 0.01) / 0.045) ** 2)));
  }
  const trem = 1 + 0.07 * Math.sin(2 * Math.PI * 5.6 * t) + 0.1 * (vnoise(t * 9) - 0.5);
  lv = Math.max(0, Math.min(1, lv * trem + 0.012 + 0.01 * vnoise(t * 23)));
  out.all = lv; out.low = Math.min(1, lv * 0.85); out.mid = lv; out.high = Math.min(1, hi + 0.22 * lv);
  return out;
}

// ------------------------------------------------------------------ audio file (decoded here; no microphone)
const FILE = { buf: null, name: '', ctx: null, an: null, node: null, spec: null, wave: null, playing: false };
async function loadFile(f) {
  try {
    if (!FILE.ctx) FILE.ctx = new (window.AudioContext || window.webkitAudioContext)();
    const data = await f.arrayBuffer();
    FILE.buf = await FILE.ctx.decodeAudioData(data);
    FILE.name = f.name;
    $('fileLab').textContent = f.name.length > 22 ? f.name.slice(0, 20) + '…' : f.name;
    setSource('file');
  } catch (e) {
    $('fileLab').textContent = 'That file could not be read';
    setTimeout(() => { $('fileLab').textContent = 'Use an audio file'; }, 2400);
  }
}
function fileStart() {
  if (!FILE.buf || !FILE.ctx) return;
  fileStop();
  FILE.ctx.resume().catch(() => {});
  const an = FILE.ctx.createAnalyser();
  an.fftSize = 2048; an.smoothingTimeConstant = 0.65;
  const node = FILE.ctx.createBufferSource();
  node.buffer = FILE.buf;
  node.connect(an); an.connect(FILE.ctx.destination);
  node.onended = () => { if (FILE.node === node) FILE.playing = false; };
  node.start();
  Object.assign(FILE, { an, node, playing: true, spec: new Uint8Array(an.frequencyBinCount), wave: new Float32Array(an.fftSize) });
}
function fileStop() { if (FILE.node) { try { FILE.node.stop(); } catch (e) { /* already ended */ } FILE.node.disconnect(); } FILE.node = null; FILE.playing = false; }
// orb-audio.ts read(): RMS of the waveform for the level, band averages of the spectrum
function fileRead(out) {
  out.all = out.low = out.mid = out.high = 0;
  if (!FILE.playing || !FILE.an) return out;
  const an = FILE.an;
  an.getByteFrequencyData(FILE.spec); an.getFloatTimeDomainData(FILE.wave);
  let s = 0; for (let i = 0; i < FILE.wave.length; i++) s += FILE.wave[i] * FILE.wave[i];
  const rms = Math.sqrt(s / FILE.wave.length);
  out.all = Math.min(1, Math.max(0, rms - 0.004) * 5.5);
  const sr = FILE.ctx.sampleRate, n = FILE.spec.length;
  const avg = (a, b) => { const bin = (hz) => Math.max(0, Math.min(n - 1, Math.round(hz / (sr / 2) * n))); const i0 = bin(a), i1 = bin(b); let m = 0; for (let i = i0; i <= i1; i++) m += FILE.spec[i]; return out.all === 0 ? 0 : Math.min(1, m / (i1 - i0 + 1) / 255 * 2); };
  out.low = avg(30, 200); out.mid = avg(200, 2000); out.high = avg(2000, 16000);
  return out;
}

// ------------------------------------------------------------------ easing helpers
function spring(o, target, dt, resp, zeta) {   // SwiftUI spring(response:dampingFraction:), as the Jewel knob page
  const w = (2 * Math.PI) / resp, k = w * w, c = 2 * zeta * w, n = Math.max(1, Math.ceil(dt / 0.004)), h = dt / n;
  for (let i = 0; i < n; i++) { o.v += (-k * (o.p - target) - c * o.v) * h; o.p += o.v * h; }
  if (Math.abs(o.p - target) < 1e-4 && Math.abs(o.v) < 1e-3) { o.p = target; o.v = 0; }
}
const follow = (cur, target, dt, up, down) => cur + (target - cur) * (1 - Math.exp(-dt / Math.max(1e-3, target > cur ? up : down)));
const smooth = (x) => (x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x));
const easeOutCubic = (x) => (x <= 0 ? 0 : x >= 1 ? 1 : 1 - Math.pow(1 - x, 3));
// the source's two state ramps: activation (0.22 s, ease-out cubic) and settle (0.65 s, smoothstep), each starting
// from whatever is shown
function ramp(v0) { return { from: v0, to: v0, t0: 0, dur: 1, val: v0 }; }
function rampTo(r, to, now) {
  if (Math.abs(r.to - to) < 1e-6) return;
  r.from = r.val; r.to = to; r.t0 = now; r.dur = to > r.from ? K.actUp : K.actDown; r.up = to > r.from;
}
function rampAt(r, now) { const x = (now - r.t0) / r.dur; r.val = r.from + (r.to - r.from) * (r.up ? easeOutCubic(x) : smooth(x)); return r.val; }

// ------------------------------------------------------------------ page state
const S = {
  variant: VARIANTS[store.get('variant', 'b')] ? store.get('variant', 'b') : 'b',
  orbSize: store.get('orbSize', '32') === '26' ? 26 : 32, attach: store.get('attach', 'in') === 'out' ? 'out' : 'in',
  label: 'listening', speechAt: -9, lastWordAt: -9, flt: { p: 0, v: 0 },
  style: store.get('style', 'siri') === 'membrane' ? 'membrane' : 'siri',
  after: store.get('after', 'leave') === 'send' ? 'send' : 'leave',
  colour: store.get('colour', 'original') === 'model' ? 'model' : 'original',
  model: 'opus', mic: 'allowed', source: 'synthetic', sizeState: 'listening', lightVoice: 'away',
  mode: 'rest', hover: false, hoverSim: false, pressed: false,
  listenT0: 0, procT0: 0, procDur: 1, errT0: 0, landT0: 0, landDur: 0, cancelT0: -9, doneT0: -9,
  words: [], spoken: [], ghostShown: 0, hasText: false, running: false, sendT0: 0,
  raw: 0, bandsRaw: { all: 0, low: 0, mid: 0, high: 0 }, bands: { all: 0, low: 0, mid: 0, high: 0 }, v: 0,
  speed: 1, glow: 0, lift: 0, desat: 0, scale: { p: 1, v: 0 }, D: { p: 26, v: 0 }, act: ramp(0.15),
  beamVis: 1, ringA: 0, ringV: 0, flow: null,
};
S.D.p = S.orbSize;
const dark = () => {
  const a = document.documentElement.getAttribute('data-theme');
  if (a === 'dark') return true; if (a === 'light') return false;
  return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
};
const mqReduce = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
const reduced = () => !!(mqReduce && mqReduce.matches);

// ------------------------------------------------------------------ segmented controls
const segs = [];
function seg(el, opts, cur, pick) {
  el.innerHTML = '<span class="pill"></span>' + opts.map((o) => `<button type="button" data-v="${o.v}" aria-pressed="false">${o.em ? '<em>' + o.em + '</em>' : ''}${o.t}</button>`).join('');
  const pill = el.querySelector('.pill');
  const S2 = { el, cur, place() {
    const b = el.querySelector(`button[data-v="${S2.cur}"]`);
    el.querySelectorAll('button').forEach((x) => { const on = x === b; x.classList.toggle('on', on); x.setAttribute('aria-pressed', on ? 'true' : 'false'); });
    if (b) { pill.style.left = b.offsetLeft + 'px'; pill.style.width = b.offsetWidth + 'px'; }
  }, set(v) { S2.cur = v; S2.place(); } };
  el.addEventListener('click', (e) => { const b = e.target.closest('button'); if (!b) return; S2.set(b.dataset.v); pick(b.dataset.v); });
  segs.push(S2); S2.place();
  return S2;
}

// ------------------------------------------------------------------ the composer
const cmp = $('cmp'), field = $('field'), win = $('win');
// the generated beam sheet goes after the page's own style, as the playground's board style follows its base rules
document.body.insertAdjacentHTML('beforeend', '<style id="beamcss">' + BEAM_CSS + '</style>');
const beam = makeBeam($('lightbox'));
const glow = mountVoiceGlow($('vglow'), {
  type: 'default', scale: 0.7, glowWidth: 1.6, lobeSpacing: 2.1, rangeWidth: 1.9, processingTravel: 2.05,
  borderRadius: 22, theme: dark() ? 'dark' : 'light', level: () => (S.prewarm ? 0.75 : S.raw), active: false, processing: false, reducedMotion: reduced(),
});
// Pre-warm: the glow's layers (blurred gradients, the SVG warp, the band canvas) are raised once at load, unseen,
// so the first click on the orb does not pay their first raster (measured: a 50 ms frame without this).
// It is driven at a speaking level meanwhile, so the band canvas draws its blurred strokes too.
$('vglow').style.opacity = '0.002';
S.prewarm = true;
glow.update({ active: true });
setTimeout(() => { glow.update({ processing: true }); }, 700);
setTimeout(() => { S.prewarm = false; glow.update({ active: false, processing: false }); setTimeout(() => { $('vglow').style.opacity = ''; }, 800); }, 1300);
// the working dots (gen.py wdots(): columns over the pill's 820 px, 3 rows; delays already 20 periods in, so the
// wave is running the moment the dots fade in)
$('wdots').innerHTML = [0, 1, 2].map((r) => `<b style="opacity:${(1 - r / 3).toFixed(4)}">` +
  Array.from({ length: Math.ceil(820 / 8) + 1 }, (_, c) => `<i style="left:${3 + c * 8}px;bottom:${3 + r * 8}px;animation-delay:${(c * 0.045 + r * 0.09 - 44).toFixed(3)}s"></i>`).join('') + '</b>').join('');
// the Claude mark in the chip (the board's: twelve strokes)
$('mk').innerHTML = Array.from({ length: 12 }, (_, i) => { const a = i * Math.PI / 6 + 0.13; return `<path d="M${(8 + 2 * Math.cos(a)).toFixed(2)} ${(8 + 2 * Math.sin(a)).toFixed(2)} L${(8 + 6.6 * Math.cos(a)).toFixed(2)} ${(8 + 6.6 * Math.sin(a)).toFixed(2)}"></path>`; }).join('');

const layers = { hint: $('layHint'), listen: $('layListen'), text: $('layText'), note: $('layNote') };
let layerOn = 'hint';
function setLayer(name) {
  if (name === layerOn) return;
  layerOn = name;
  for (const k in layers) layers[k].classList.toggle('on', k === name);
}
function note(html) { layers.note.innerHTML = html; setLayer('note'); }
const say = (msg) => { $('live').textContent = msg; };

// ------------------------------------------------------------------ live words (OD-0091, third amendment)
// The transcript is written while the user speaks: each word arrives 0.3 to 0.7 s after it is spoken, the newest word
// stays tentative (lighter) until the next one confirms it, and now and then the last word is revised as more is
// heard (its old text lifts away, the new one rises in, the word's width eases). A short final pass after the second
// click brings in what was spoken but not yet written and settles the text.
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
const FILE_WORDS = 'Words from your file would be written here as Whisper hears them'.split(' ');
const tin = document.createElement('span'); tin.className = 'tin'; layers.text.appendChild(tin);
const caret = document.createElement('i'); caret.className = 'caret';
const measureCtx = document.createElement('canvas').getContext('2d');
let fieldFont = '';
function textWidth(t) { if (!fieldFont) fieldFont = getComputedStyle(field).font; measureCtx.font = fieldFont; return measureCtx.measureText(t).width; }
const sess = { spans: [], texts: [], ev: 0, final: [] };   // this dictation's words, as written
function caretOn() { tin.appendChild(caret); }
function caretOff() { if (caret.parentNode) caret.remove(); }
function updateTyping() {
  const words = tin.querySelectorAll('.w:not(.out)').length;
  S.hasText = words > 0;
  cmp.classList.toggle('typing', S.hasText);
  S.scrollDirty = true;
}
function addWord(text, tentative) {
  if (tin.querySelector('.w')) tin.insertBefore(document.createTextNode(' '), caret.parentNode ? caret : null);
  const w = document.createElement('span');
  w.className = 'w' + (tentative ? ' tent' : '');
  const t = document.createElement('span'); t.className = 't'; t.textContent = text;
  w.appendChild(t);
  tin.insertBefore(w, caret.parentNode ? caret : null);
  // its first frame is the hidden state, so it rises in (on the next frame: no forced layout)
  requestAnimationFrame(() => requestAnimationFrame(() => w.classList.add('in')));
  return w;
}
function reviseWord(w, text) {
  const oldT = w.querySelector('.t:not(.old)');
  if (!oldT || oldT.textContent === text) return;
  const from = w.getBoundingClientRect().width / stageScale, to = textWidth(text);
  w.style.width = from.toFixed(2) + 'px';
  oldT.classList.add('old');
  const t = document.createElement('span'); t.className = 't new'; t.textContent = text;
  w.appendChild(t);
  void w.offsetWidth;
  w.style.width = to.toFixed(2) + 'px';
  t.classList.remove('new');
  setTimeout(() => { oldT.remove(); w.style.width = ''; S.scrollDirty = true; }, 340);
}
function confirmAll(stagger) {
  sess.spans.forEach((w, i) => { if (w.classList.contains('tent')) { w.style.transitionDelay = `0s, 0s, ${(i * (stagger || 0)).toFixed(3)}s, 0s`; w.classList.remove('tent'); } });
}
function applyEvent(e) {
  if (e.kind === 'add') {
    const prev = sess.spans[sess.spans.length - 1];
    if (prev) prev.classList.remove('tent');
    if (layerOn !== 'text') setLayer('text');
    caretOn();
    sess.spans[e.i] = addWord(e.text, true);
    S.lastWordAt = performance.now() / 1000;
    sess.texts[e.i] = e.text;
  } else if (sess.spans[e.i]) { reviseWord(sess.spans[e.i], e.text); sess.texts[e.i] = e.text; }
  updateTyping();
}
// listening: write what has been heard by tl (seconds since the click)
function streamTo(tl) {
  if (S.source === 'file') {
    const n = Math.min(FILE_WORDS.length, Math.floor(Math.max(0, tl - 0.6) / 0.42));
    while (sess.ev < n) applyEvent({ kind: 'add', i: sess.ev, text: FILE_WORDS[sess.ev++] });
    return;
  }
  while (sess.ev < STREAM.length && STREAM[sess.ev].t <= tl) applyEvent(STREAM[sess.ev++]);
}
// the final pass: what was spoken but not yet written arrives quickly; open revisions and the punctuation settle
function planFinal(now, tl) {
  const plan = [];
  if (S.source === 'file') {
    const spoken = Math.min(FILE_WORDS.length, Math.floor(Math.max(0, tl - 0.6) / 0.42) + 2);
    for (let i = sess.ev; i < spoken; i++) plan.push({ kind: 'add', i, text: FILE_WORDS[i] });
    sess.ev = spoken;
  } else {
    for (; sess.ev < STREAM.length; sess.ev++) {
      const e = STREAM[sess.ev];
      if (e.spokenEnd > tl + 0.12) continue;   // not spoken before the click
      // the final pass hears the word whole: a word still to be written arrives in its final form
      plan.push(e.kind === 'add' ? Object.assign({}, e, { text: SYN.words[e.i].text }) : e);
    }
    // the last word's final form (e.g. the full stop) comes with the final pass
    SYN.words.forEach((w, i) => { if (w.e <= tl + 0.12 && sess.texts[i] !== undefined && sess.texts[i] !== w.text) plan.push({ kind: 'rev', i, text: w.text }); });
  }
  let at = now + 0.18;
  sess.final = plan.map((e) => { const x = Object.assign({}, e, { at }); at += e.kind === 'add' ? 0.07 : 0.12; return x; });
  return at - now;
}
function withdraw() {
  // this dictation's words leave from the end, one after another; the text that was there before stays
  const list = sess.spans.filter(Boolean).reverse();
  list.forEach((w, k) => {
    w.style.transitionDelay = `${(k * 0.03).toFixed(3)}s, ${(k * 0.03).toFixed(3)}s, 0s, 0s`;
    w.classList.add('out');
    setTimeout(() => {
      const sp = w.previousSibling;
      if (sp && sp.nodeType === 3) sp.remove();
      w.remove();
      if (!tin.querySelector('.w')) { const lead = tin.firstChild; if (lead && lead.nodeType === 3) lead.remove(); }
      S.scrollDirty = true;
    }, 340 + k * 30);
  });
  const total = 340 + list.length * 30;
  sess.spans = []; sess.texts = []; sess.final = [];
  updateTypingAfter(total);
  return total;
}
function updateTypingAfter(ms) {
  setTimeout(() => {
    updateTyping();
    if (!S.hasText && S.mode !== 'listening' && S.mode !== 'processing' && layerOn !== 'note') { caretOff(); setLayer('hint'); }
  }, ms);
}
function clearText() {
  tin.textContent = ''; layers.text.classList.remove('sent'); tin.style.transform = '';
  sess.spans = []; sess.texts = []; sess.final = []; sess.ev = 0;
  S.hasText = false; cmp.classList.remove('typing');
}

// ------------------------------------------------------------------ the two orbs (both compiled at load; a style change
// cross-fades the canvases, a colour change cross-fades inside the orb: OD-0076)
const CV = 72;
function orbColours() { if (S.colour !== 'model') return 'preset'; const m = MODELS.find((x) => x.id === S.model); return { liquid: m.liquid }; }
const main = {
  siri: { cv: $('orbA'), inst: null, vis: 1 },
  membrane: { cv: $('orbB'), inst: null, vis: 0 },
};
for (const k of ['siri', 'membrane']) {
  main[k].inst = ORB[k].make(main[k].cv, { size: CV, ball: S.D.p, dark: dark(), colors: orbColours(), context: 'own' });
  main[k].vis = k === S.style ? 1 : 0;
  main[k].cv.style.opacity = String(main[k].vis);
}
function swapMain() {
  const from = main[S.style === 'siri' ? 'membrane' : 'siri'].inst, to = main[S.style].inst;
  to.phase = from.phase;   // the new orb takes up the wave where the old one is
}
// ------------------------------------------------------------------ states
function voiceStart() { if (S.source === 'file') fileStart(); }
function voiceStop() { if (S.source === 'file') fileStop(); }
function setLive(on) {
  const pill = VARIANTS[S.variant].pill;
  cmp.classList.toggle('live', on);
  $('xBtn').tabIndex = on && !pill ? 0 : -1;
  $('pX').tabIndex = on && pill ? 0 : -1;
  $('vpill').classList.toggle('open', on && !!pill);
  if (on) setLabel('listening');
  S.fieldDirty = true;
}
// the pill's label: Listening... in silence, Transcribing... while speech is being recognised (it leaves only after
// 0.6 s of quiet, so short gaps between words never flicker it), Finishing... in the final pass; a crossfade, the
// pill's width easing to the new label
function setLabel(k) {
  if (S.label === k && $('vpill').dataset.lab === k) return;
  S.label = k; $('vpill').dataset.lab = k;
  document.querySelectorAll('#vpill .pl').forEach((x) => x.classList.toggle('on', x.dataset.k === k));
  S.fieldDirty = true;
}
const labW = {};
function labelWidth(k, size) {
  const key = k + size;
  if (labW[key] == null) { measureCtx.font = `600 ${size}px ${getComputedStyle(document.querySelector('#vpill .pl')).fontFamily}`; labW[key] = Math.ceil(measureCtx.measureText(LABELS[k]).width); }
  return labW[key];
}
function startListening(now) {
  if (S.mode === 'listening' || S.mode === 'processing') return;
  if (S.mode === 'error') { S.noteUntil = 0; afterNote(now); }
  if (S.mic === 'blocked') { showError(now); return; }
  if (S.running) finishSend();
  S.mode = 'listening'; S.listenT0 = now;
  sess.spans = []; sess.texts = []; sess.ev = 0; sess.final = [];
  voiceStart();
  setLive(true);
  glow.update({ active: true, processing: false });
  if (S.hasText || VARIANTS[S.variant].pill) { caretOn(); setLayer('text'); } else setLayer('listen');
  $('orbBtn').setAttribute('aria-label', 'Stop and finish the text'); $('orbBtn').setAttribute('aria-pressed', 'true');
  say('Listening. Your words are written into the composer as you speak. Click the orb again to finish, or press Escape to cancel.');
}
function stopListening(now) {
  if (S.mode !== 'listening') return;
  const tl = now - S.listenT0;
  voiceStop();
  const dur = planFinal(now, tl);
  if (!sess.spans.filter(Boolean).length && !sess.final.length) { cancel(now, 'No speech was heard'); return; }
  S.mode = 'processing'; S.procT0 = now;
  S.procDur = Math.max(K.finalMin, dur + 0.3);
  setLabel('finishing');
  glow.update({ active: true, processing: true });
  $('orbBtn').setAttribute('aria-label', 'Finishing the text'); $('orbBtn').setAttribute('aria-pressed', 'false');
  say('Finishing the text.');
}
function done(now) {
  S.mode = 'landing'; S.landT0 = now; S.doneT0 = now; S.landDur = 0.5;
  confirmAll(0.025);
  setLive(false);
  glow.update({ active: false, processing: false });
  caretOn(); setLayer('text');
  updateTyping();
  $('orbBtn').setAttribute('aria-label', 'Start voice input');
  const text = tin.textContent.trim();
  say('Done: ' + text + (S.after === 'send' ? ' Sending.' : ' The text is in the composer; send it when you are ready.'));
}
function cancel(now, msg) {
  if (S.mode !== 'listening' && S.mode !== 'processing') return;
  voiceStop();
  S.mode = 'rest'; S.cancelT0 = now;
  setLive(false);
  glow.update({ active: false, processing: false });
  const had = sess.spans.filter(Boolean).length;
  const ms = withdraw();
  if (!had || msg) {   // nothing was written: say so briefly; otherwise the words leaving are the answer
    note(`<span>${msg || 'Voice input cancelled'}</span>`);
    S.noteUntil = now + 1.6;
  }
  $('orbBtn').setAttribute('aria-label', 'Start voice input'); $('orbBtn').setAttribute('aria-pressed', 'false');
  say(msg || 'Voice input cancelled; the words were removed.');
}
function showError(now) {
  S.mode = 'error'; S.errT0 = now;
  note('<svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="8" cy="8" r="6.2"></circle><path d="M8 4.8v3.6M8 10.9v.1"></path></svg><span>Microphone blocked. Allow Marrow in <span class="lnk">Privacy &amp; security &gt; Microphone</span></span>');
  S.noteUntil = now + 5.2;
  say('The microphone is blocked. Allow Marrow in Windows Settings, Privacy and security, Microphone.');
}
function afterNote(now) {
  if (S.hasText) { setLayer('text'); caretOn(); } else setLayer('hint');
  if (S.mode === 'error') S.mode = 'rest';
}
function sendNow(now) {
  if (!S.hasText || S.running) return;
  S.running = true; S.sendT0 = now;
  cmp.classList.add('running');
  caretOff();
  layers.text.classList.add('sent');
  $('ringA').style.strokeDasharray = '10.5 44';
  setTimeout(() => { clearText(); setLayer('hint'); }, 300);
  say('Sent.');
}
function finishSend() {
  S.running = false; cmp.classList.remove('running'); $('ringA').style.strokeDasharray = '31 44';
}
function orbClick(now) {
  stopFlowIfUser();
  if (S.mode === 'listening') stopListening(now);
  else if (S.mode === 'processing') return;
  else startListening(now);
}

// ------------------------------------------------------------------ the scripted flow (Play the whole flow)
let userAct = true;
function stopFlowIfUser() { if (userAct && S.flow) stopFlow(); }
function stopFlow() { S.flow = null; $('playLab').textContent = 'Play the whole flow'; S.hoverSim = false; }
function playFlow() {
  if (S.flow) { stopFlow(); return; }
  const now = performance.now() / 1000;
  resetAll(now);
  const listenFor = S.source === 'file' && FILE.buf ? Math.min(12, FILE.buf.duration + 0.4) : SYN.end + 0.35;
  S.flow = { t0: now, steps: [
    [0.5, () => { S.hoverSim = true; }],
    [1.5, (t) => startListening(t)],
    [1.5 + listenFor, (t) => stopListening(t)],
    [-1, null],
  ], i: 0, listenFor };
  $('playLab').textContent = 'Stop the flow';
}
function flowTick(now) {
  const F = S.flow; if (!F) return;
  while (F.i < F.steps.length) {
    const [at, fn] = F.steps[F.i];
    if (at < 0) {   // wait for the final pass (and a send) to finish, then let go of the hover
      if (S.mode === 'rest' && !S.running && (F.endAt || (F.endAt = now + 1.4)) && now > F.endAt) { stopFlow(); }
      else if (S.mode !== 'rest' || S.running) F.endAt = 0;
      return;
    }
    if (now - F.t0 < at) return;
    userAct = false; fn(now); userAct = true; F.i++;
  }
}
function resetAll(now) {
  voiceStop(); glow.update({ active: false, processing: false });
  if (S.running) finishSend();
  setLive(false);
  clearText(); caretOff(); setLayer('hint');
  S.mode = 'rest'; S.hoverSim = false; S.noteUntil = 0; S.cancelAt = 0;
  $('orbBtn').setAttribute('aria-label', 'Start voice input'); $('orbBtn').setAttribute('aria-pressed', 'false');
}

// ------------------------------------------------------------------ the rail (every state, each one a click away)
const RAIL = [
  { id: 'rest', t: 'Rest', d: 'Slow, a quiet crest', c: `${K.restBase}x Idle speed`, go: (n) => { stopFlow(); resetAll(n); } },
  { id: 'hover', t: 'Hover', d: 'Speeds up and glows', c: `${K.hoverSpeed}x, ${K.hoverScale}, glow ${K.hoverGlow}`, go: (n) => { stopFlow(); resetAll(n); S.hoverSim = true; } },
  { id: 'listening', t: 'Listening', d: 'Words written as you speak; the pill says so', c: `1.0-${(1 + K.voiceSpeed).toFixed(1)}x, ${K.listenScale}`, go: (n) => { stopFlow(); resetAll(n); startListening(n); } },
  { id: 'processing', t: 'Final pass', d: 'The fastest; the text settles', c: `${K.procSpeed}x, ${K.procScale}`, go: (n) => { stopFlow(); resetAll(n); startListening(n); S.listenT0 = n - 2.6; streamTo(2.6); stopListening(n); } },
  { id: 'done', t: 'Done', d: 'The text stays; the orb settles', c: `settle ${K.actDown} s, spring ${K.settleResp} s`, go: (n) => { stopFlow(); resetAll(n); startListening(n); S.listenT0 = n - SYN.end - 1.2; streamTo(SYN.end + 1.2); stopListening(n); } },
  { id: 'cancel', t: 'Cancel', d: 'The cross or Esc; words withdrawn', c: 'nothing is kept', go: (n) => { stopFlow(); resetAll(n); startListening(n); S.cancelAt = n + 2.9; } },
  { id: 'error', t: 'Blocked', d: 'Microphone not allowed', c: 'colour drains, a note', go: (n) => { stopFlow(); resetAll(n); showError(n); } },
];
$('rail').innerHTML = RAIL.map((r) => `<button class="st" type="button" data-id="${r.id}"><b><i></i>${r.t}</b><span>${r.d}</span><code>${r.c}</code></button>`).join('');
$('rail').addEventListener('click', (e) => { const b = e.target.closest('.st'); if (!b) return; const r = RAIL.find((x) => x.id === b.dataset.id); userAct = false; r.go(performance.now() / 1000); userAct = true; });
function shownState(now) {
  if (S.mode === 'error') return 'error';
  if (S.mode === 'listening') return 'listening';
  if (S.mode === 'processing') return 'processing';
  if (S.mode === 'landing' || now - S.doneT0 < 1.6) return 'done';
  if (now - S.cancelT0 < 1.6) return 'cancel';
  if (S.hover || S.hoverSim) return 'hover';
  return 'rest';
}

$('tbl').innerHTML = [
  ['Rest', 'Nothing', `The preset's Idle profile (dim colours, warp x0.5, exposure x0.68) lifted ${Math.round(K.restAct * 100)} % toward Thinking, at ${K.restBase}x the Idle speed (Siri ${(ORB.siri.idleSpeed * K.restBase).toFixed(3)}, Membrane ${(ORB.membrane.idleSpeed * K.restBase).toFixed(3)} a second)`, 'Border beam on', '-'],
  ['Hover', 'Pointer over the orb', `Speed ${K.hoverSpeed}x, ${Math.round(K.hoverAct * 100)} % toward the Thinking look, scale ${K.hoverScale}, outer glow ${K.hoverGlow} in the preset's glow colour`, 'Unchanged', 'In 0.12 s, out 0.25 s; the scale on the knob’s lift spring (0.22 s, damping 0.8)'],
  ['Listening', 'Click (or Enter, Space)', `Pops to ${K.listenScale}x with a slight overshoot, full Thinking look; speed 1x in pauses up to ${(1 + K.voiceSpeed).toFixed(1)}x on loud syllables; scale breathes +${K.voiceScale} with the voice; the source's audio rules`, 'The orb opens to its side into the status pill (B, C) with Listening\u2026 / Transcribing\u2026, its cross at the far end (A: a cross slides out from behind the orb). Words are written as they are heard (0.3 to 0.7 s behind the voice), the newest one lighter until the next confirms it, now and then revised in place; the composer is in its typing state (caret, send button lit). The voice glow rises from the bottom edge with the same voice; the Border beam gives way', `Pop spring ${K.popResp} s, damping ${K.popDamp}; activation ${K.actUp} s ease-out; speed follower ${K.vAttack} s up, ${K.vRelease} s down; cross 0.42 s; a word rises in 0.4 s, a revision 0.3 s`],
  ['Final pass', 'Second click', `Speed ${K.procSpeed}x (the highest), scale ${K.procScale}, glow ${K.procGlow}; the audio response falls away`, 'Words spoken but not yet written arrive (70 ms apart), open revisions and the full stop settle; the pill reads Finishing…; the glow gathers into a sweep from corner to corner (1.1 s a pass)', `At least ${K.finalMin} s here (Whisper's real time to be measured)`],
  ['Done', 'Final pass complete', 'Settles to Rest: scale on the knob’s release spring, the look on the source’s 0.65 s settle, the speed eased down', 'The text stays in the composer, unsent (the default); with "Send it as the prompt" the app’s send follows. The pill folds back into the orb (A: the cross slides back in); the beam returns', 'Tentative words ink in 25 ms apart; beam back in 0.6 s'],
  ['Cancel', 'The cross, or Esc, while listening or in the final pass', 'Settles to Rest', 'This dictation’s words leave from the end, 30 ms apart; text that was there before stays; the cross slides back', 'Each word 0.3 s; glow fades 0.5 s'],
  ['Blocked', 'Click when Windows denies the microphone', `No pop; the colour drains (desaturated, a little dimmer), speed ${K.errSpeed}x`, 'A calm note in the field says where to allow it; it clears after 5 s', 'Drain 0.35 s, return 0.6 s'],
].map((r) => '<tr>' + r.map((c) => `<td>${c}</td>`).join('') + '</tr>').join('');

// ------------------------------------------------------------------ settings card and sizes card
const extra = [];   // { inst, kind, hover? }
function buildTiles() {
  extra.filter((x) => x.kind === 'tile').forEach((x) => x.inst.dispose());
  for (let i = extra.length - 1; i >= 0; i--) if (extra[i].kind === 'tile') extra.splice(i, 1);
  $('tiles').innerHTML = [['siri', 'Siri Wave', 'A white crest and four colour bands', true], ['membrane', 'Voice Membrane', 'One broad magenta membrane', false]]
    .map(([k, n, d, def]) => `<button class="tile${S.style === k ? ' on' : ''}" type="button" role="radio" aria-checked="${S.style === k}" data-k="${k}"><canvas></canvas><span><b>${n}</b><small>${d}</small>${def ? '<span class="def">DEFAULT</span>' : ''}</span></button>`).join('');
  $('tiles').querySelectorAll('.tile').forEach((b) => {
    const cv = b.querySelector('canvas');
    const rec = { kind: 'tile', key: b.dataset.k, hover: false, inst: ORB[b.dataset.k].make(cv, { size: 44, ball: 32, dark: dark(), colors: orbColours(), context: 'own' }), sp: 1, gl: 0, ac: 0 };
    b.addEventListener('pointerenter', () => { rec.hover = true; });
    b.addEventListener('pointerleave', () => { rec.hover = false; });
    extra.push(rec);
  });
}
$('tiles').addEventListener('click', (e) => {
  const b = e.target.closest('.tile'); if (!b) return;
  S.style = b.dataset.k; store.set('style', S.style); segOrb.set(S.style);
  $('tiles').querySelectorAll('.tile').forEach((x) => { const on = x === b; x.classList.toggle('on', on); x.setAttribute('aria-checked', on); });
  swapMain();
});
const SIZES = [20, 26, 32, 40];
function buildSizes() {
  extra.filter((x) => x.kind === 'size').forEach((x) => x.inst.dispose());
  for (let i = extra.length - 1; i >= 0; i--) if (extra[i].kind === 'size') extra.splice(i, 1);
  const g = $('sizes');
  g.innerHTML = '<span></span>' + SIZES.map((s) => `<span class="ch">${s} px</span>`).join('') +
    ['siri', 'membrane'].map((k) => `<span class="rh">${k === 'siri' ? 'Siri Wave' : 'Membrane'}</span>` + SIZES.map((s) => `<div class="cell"><canvas data-k="${k}" data-s="${s}"></canvas></div>`).join('')).join('');
  g.querySelectorAll('canvas').forEach((cv) => {
    const s = +cv.dataset.s;
    extra.push({ kind: 'size', key: cv.dataset.k, inst: ORB[cv.dataset.k].make(cv, { size: Math.round(s * 1.6), ball: s, dark: dark(), colors: orbColours(), context: 'shared' }), sc: { p: 1, v: 0 }, sp: 1, gl: 0, ac: 0 });
  });
}

// ------------------------------------------------------------------ controls
seg($('segSize'), [{ v: '32', t: '32 px' }, { v: '26', t: '26 px' }], String(S.orbSize), (v) => { S.orbSize = +v; store.set('orbSize', v); });
seg($('segAttach'), [{ v: 'in', t: 'Inside' }, { v: 'out', t: 'Outside' }], S.attach, (v) => { S.attach = v; store.set('attach', v); applyVariant(); });
const segVariant = seg($('segVariant'), [{ v: 'a', em: 'A', t: 'Cross' }, { v: 'b', em: 'B', t: 'Pill in the composer' }, { v: 'c', em: 'C', t: 'Floating pill' }], S.variant, (v) => {
  if (S.mode === 'listening' || S.mode === 'processing') cancel(performance.now() / 1000);
  S.variant = v; store.set('variant', v); applyVariant();
});
const segOrb = seg($('segOrb'), [{ v: 'siri', t: 'Siri Wave' }, { v: 'membrane', t: 'Voice Membrane' }], S.style, (v) => {
  S.style = v; store.set('style', v);
  $('tiles').querySelectorAll('.tile').forEach((x) => { const on = x.dataset.k === v; x.classList.toggle('on', on); x.setAttribute('aria-checked', on); });
  swapMain();
});
seg($('segTheme'), [{ v: 'dark', t: 'Dark' }, { v: 'light', t: 'Light' }], dark() ? 'dark' : 'light', (v) => { document.documentElement.setAttribute('data-theme', v); applyTheme(); });
seg($('segMic'), [{ v: 'allowed', t: 'Allowed' }, { v: 'blocked', t: 'Blocked' }], S.mic, (v) => { S.mic = v; });
function buildAfter() {
  $('after').innerHTML = [['leave', 'Leave the text in the composer', 'default'], ['send', 'Send it as the prompt', '']]
    .map(([v, t, d]) => `<button class="radio${S.after === v ? ' on' : ''}" type="button" role="radio" aria-checked="${S.after === v}" data-v="${v}"><i></i>${t}${d ? ' <span>' + d + '</span>' : ''}</button>`).join('');
}
buildAfter();
$('after').addEventListener('click', (e) => {
  const b = e.target.closest('.radio'); if (!b) return;
  S.after = b.dataset.v; store.set('after', S.after);
  $('after').querySelectorAll('.radio').forEach((x) => { const on = x === b; x.classList.toggle('on', on); x.setAttribute('aria-checked', on); });
});
seg($('segSizeState'), [{ v: 'rest', t: 'Rest' }, { v: 'hover', t: 'Hover' }, { v: 'listening', t: 'Listening' }, { v: 'processing', t: 'Transcribing' }], S.sizeState, (v) => { S.sizeState = v; });

let fieldDirty = true;
function applyVariant() {
  const v = VARIANTS[S.variant];
  win.classList.toggle('v-out', S.attach === 'out');
  win.classList.toggle('v-pill', !!VARIANTS[S.variant].pill);
  $('vpill').classList.toggle('float', VARIANTS[S.variant].pill === 'float');
  fieldDirty = true;
}
function applyTheme() {
  const d = dark();
  for (const k of ['siri', 'membrane']) main[k].inst.setDark(d);
  for (const x of extra) x.inst.setDark(d);
  beam.setTheme(d ? 'dark' : 'light');
  glow.update({ theme: d ? 'dark' : 'light' });
}
function recolourAll() {
  for (const k of ['siri', 'membrane']) main[k].inst.recolor(orbColours());
  for (const x of extra) x.inst.recolor(orbColours());
}
let mnOn = 'A';
function applyModel() {
  const m = MODELS.find((x) => x.id === S.model);
  const a = $('mnA'), b = $('mnB'), inn = mnOn === 'A' ? b : a, out = mnOn === 'A' ? a : b;
  inn.textContent = m.name; inn.classList.remove('off'); out.classList.add('off'); mnOn = mnOn === 'A' ? 'B' : 'A';
  $('mn').style.width = inn.offsetWidth + 'px';
  $('mk').classList.toggle('off', !m.claude);
  $('mdot').classList.toggle('off', !!m.claude);
  $('mdot').style.background = m.dot;
  fieldDirty = true;
}
$('fileIn').addEventListener('change', (e) => { const f = e.target.files && e.target.files[0]; if (f) loadFile(f); });
function setSource(src) {
  S.source = src;
  $('srcLab').textContent = src === 'file' ? 'Level from ' + (FILE.name.length > 18 ? FILE.name.slice(0, 16) + '…' : FILE.name) + ' · words simulated' : 'Simulated voice';
}
$('playFlow').addEventListener('click', playFlow);

// the orb button
const orbBtn = $('orbBtn');
orbBtn.addEventListener('pointerenter', () => { S.hover = true; });
orbBtn.addEventListener('pointerleave', () => { S.hover = false; S.pressed = false; });
orbBtn.addEventListener('pointerdown', () => { if (S.mode !== 'processing') S.pressed = true; });
orbBtn.addEventListener('pointerup', () => { S.pressed = false; });
orbBtn.addEventListener('click', () => orbClick(performance.now() / 1000));
document.addEventListener('keydown', (e) => {
  if (e.key !== 'Escape') return;
  if (S.mode === 'listening' || S.mode === 'processing') { stopFlow(); cancel(performance.now() / 1000); }
});
$('send').addEventListener('click', () => { stopFlowIfUser(); sendNow(performance.now() / 1000); });
$('xBtn').addEventListener('click', () => { stopFlowIfUser(); cancel(performance.now() / 1000); });
$('pX').addEventListener('click', () => { stopFlowIfUser(); cancel(performance.now() / 1000); });
seg($('segLight'), [{ v: 'away', t: 'Gives way' }, { v: 'stay', t: 'Stays on' }], S.lightVoice, (v) => { S.lightVoice = v; });
document.querySelectorAll('.attach').forEach((b) => b.addEventListener('click', () => { /* the file dialog opens in the app */ }));

// ------------------------------------------------------------------ stage scale (the 940 px window fits any width)
let stageScale = 1;
function fit() {
  const w = $('stagewrap').clientWidth;
  stageScale = Math.min(1, w / 940);
  $('stage').style.transform = stageScale < 1 ? `scale(${stageScale})` : '';
  $('stagewrap').style.height = Math.round(330 * stageScale) + 'px';
  segs.forEach((s) => s.place());
  fieldDirty = true;
}
if (window.ResizeObserver) new ResizeObserver(fit).observe($('stagewrap')); else window.addEventListener('resize', fit);
fit();

// ------------------------------------------------------------------ the frame clock
const meterBars = Array.from(document.querySelectorAll('.src .meter i'));
const railBtns = Array.from(document.querySelectorAll('.st'));
let lastNow = performance.now() / 1000, lastDom = 0, lastRail = '';
const synthBuf = { all: 0, low: 0, mid: 0, high: 0 }, sizeBands = { all: 0, low: 0, mid: 0, high: 0 }, sizeSm = { all: 0, low: 0, mid: 0, high: 0 };
let sizeV = 0;
window.__vc = { S, K, frames: [], work: [], glow, beam };   // for the checks (frame times, JS time per frame)

function tick() {
  requestAnimationFrame(tick);
  const tw0 = performance.now();
  const now = tw0 / 1000;
  const dt = Math.min(0.05, Math.max(0, now - lastNow));
  lastNow = now;
  const rm = reduced();
  if (window.__vc.rec) { window.__vc.frames.push(dt * 1000); if (dt > 0.02) (window.__vc.long || (window.__vc.long = [])).push([+(dt * 1000).toFixed(1), S.mode, +(now - (S.flow ? S.flow.t0 : 0)).toFixed(2), layerOn]); }

  flowTick(now);
  if (S.cancelAt && now >= S.cancelAt) { S.cancelAt = 0; if (S.mode === 'listening') cancel(now); }
  if (S.mode === 'processing') {
    while (sess.final.length && sess.final[0].at <= now) applyEvent(sess.final.shift());
    if (now - S.procT0 >= S.procDur) { while (sess.final.length) applyEvent(sess.final.shift()); done(now); }
  }
  if (S.mode === 'landing' && now - S.landT0 >= S.landDur) {
    S.mode = 'rest';
    if (S.after === 'send') S.sendAt = now + 0.35;
  }
  if (S.sendAt && now >= S.sendAt) { S.sendAt = 0; sendNow(now); }
  if (S.running && now - S.sendT0 > K.sendHold) finishSend();
  if (S.noteUntil && now >= S.noteUntil) { S.noteUntil = 0; afterNote(now); }

  // ---- the voice
  const listening = S.mode === 'listening';
  const tl = now - S.listenT0;
  if (listening) { if (S.source === 'file') fileRead(S.bandsRaw); else synthAt(tl, S.bandsRaw); }
  else S.bandsRaw.all = S.bandsRaw.low = S.bandsRaw.mid = S.bandsRaw.high = 0;
  S.raw = S.bandsRaw.all;
  for (const b of ['all', 'low', 'mid', 'high']) {   // orb-audio.ts read(): attack 0.07 s, release 0.24 s
    S.bands[b] = follow(S.bands[b], S.bandsRaw[b], dt, 0.07, 0.24);
    if (S.bands[b] < 1e-4) S.bands[b] = 0;
  }
  S.v = follow(S.v, S.bands.all, dt, K.vAttack, K.vRelease);
  if (listening) streamTo(tl);

  // ---- the orb's targets
  const hov = (S.hover || S.hoverSim) && (S.mode === 'rest' || S.mode === 'landing');
  let tSpeed = 1, tAct = K.restAct, tScale = 1, tGlow = 0, tLift = 0, tDes = 0, resp = K.settleResp, damp = K.settleDamp;
  if (S.mode === 'listening') {
    tSpeed = 1 + K.voiceSpeed * S.v; tAct = 1; tScale = K.listenScale + K.voiceScale * S.v; tGlow = K.listenGlow + K.voiceGlow * S.v; tLift = 1;
    resp = K.popResp; damp = K.popDamp;
  } else if (S.mode === 'processing') {
    tSpeed = K.procSpeed; tAct = 1; tScale = K.procScale; tGlow = K.procGlow; tLift = 0.8; resp = K.popResp; damp = 0.75;
  } else if (S.mode === 'error') {
    tSpeed = K.errSpeed; tDes = 1; tGlow = hov ? 0.15 : 0;
  } else if (hov) {
    tSpeed = K.hoverSpeed; tAct = K.hoverAct; tScale = K.hoverScale; tGlow = K.hoverGlow; tLift = 0.15; resp = K.hoverResp; damp = K.hoverDamp;
  }
  if (S.pressed) { tScale = K.pressScale * (S.mode === 'listening' ? K.listenScale : 1); resp = 0.18; damp = 0.8; }
  rampTo(S.act, tAct, now);
  const act = rampAt(S.act, now);
  S.speed = follow(S.speed, tSpeed, dt, S.mode === 'listening' ? 0.05 : 0.2, S.mode === 'listening' ? 0.05 : 0.5);
  S.glow = follow(S.glow, tGlow, dt, 0.12, 0.25);
  S.lift = follow(S.lift, tLift, dt, 0.12, 0.3);
  S.desat = follow(S.desat, tDes, dt, 0.35 / 3, 0.6 / 3);
  spring(S.scale, tScale, dt, resp, damp);
  // the button's size (variant A/B 26, C 32) eases on a spring, the slot and the ball together
  const tD = S.orbSize;
  if (S.D.p !== tD || S.D.v !== 0) {
    spring(S.D, tD, dt, 0.34, 0.86);
    $('orbslot').style.width = $('orbslot').style.height = S.D.p.toFixed(2) + 'px';
    fieldDirty = true;
  }
  if (S.mode === 'listening') {
    if (S.v > 0.16 || now - S.lastWordAt < 0.35) S.speechAt = now;
    setLabel(now - S.speechAt < 0.6 ? 'transcribing' : 'listening');
  }
  // C: the orb lifts out of its slot into the floating pill and settles back when it folds
  const lifted = VARIANTS[S.variant].pill === 'float' && cmp.classList.contains('live');
  spring(S.flt, lifted ? 1 : 0, dt, lifted ? 0.36 : 0.34, lifted ? 0.8 : 0.86);
  const ty = (S.flt.p * (PILL.float.top + PILL.float.H / 2 - 26)).toFixed(2);
  if ($('orbmove').__ty !== ty) { $('orbmove').__ty = ty; $('orbmove').style.transform = Math.abs(+ty) < 0.01 ? '' : `translateY(${ty}px)`; }
  const orbState = { act, speed: S.speed * K.restBase, scale: S.scale.p, glow: S.glow, lift: S.lift, bands: listening ? S.bands : null, desat: S.desat, reduceMotion: rm };
  for (const k of ['siri', 'membrane']) {
    const m = main[k], target = k === S.style ? 1 : 0;
    m.vis = follow(m.vis, target, dt, 0.12, 0.12);
    if (Math.abs(m.vis - target) < 0.004) m.vis = target;
    const op = m.vis.toFixed(3);
    if (m.cv.__op !== op) { m.cv.__op = op; m.cv.style.opacity = op; }
    if (m.vis <= 0) continue;
    const o = m.inst;
    if (o.__d !== S.D.p) { o.__d = S.D.p; o.resize(CV, S.D.p); }
    o.frame(now, orbState);
  }

  // ---- the composer's lights: the beam gives way while the voice is on
  const voiceOn = S.mode === 'listening' || S.mode === 'processing' || S.mode === 'landing';
  S.beamVis = follow(S.beamVis, voiceOn && S.lightVoice === 'away' ? 0 : 1, dt, 0.2, 0.17);
  if (Math.abs(S.beamVis - (S.beamShown || 0)) > 0.002 || S.beamVis === 0 || S.beamVis === 1) {
    const o = S.beamVis < 0.003 ? 0 : S.beamVis > 0.997 ? 1 : S.beamVis;
    if (S.beamShown !== o) { S.beamShown = o; $('lightbox').style.opacity = o.toFixed(3); }
  }
  if (S.beamVis > 0.003) beam.frame(dt, rm ? 0 : dt);

  // ring spin while a sent turn runs
  S.ringV = follow(S.ringV, S.running ? 1 : 0, dt, 0.35, 0.35);
  if (S.ringV > 0.002) { S.ringA = (S.ringA + dt * 330 * S.ringV) % 360; $('ringA').parentNode.style.transform = `rotate(${S.ringA.toFixed(1)}deg)`; }

  // ---- the cards
  synthAt((now % (SYN.end + 1.2)), synthBuf);
  for (const b of ['all', 'low', 'mid', 'high']) sizeSm[b] = follow(sizeSm[b], synthBuf[b], dt, 0.07, 0.24);
  sizeV = follow(sizeV, sizeSm.all, dt, K.vAttack, K.vRelease);
  for (const x of extra) {
    if (x.kind === 'tile') {
      x.sp = follow(x.sp, x.hover ? K.hoverSpeed : 1, dt, 0.2, 0.5);
      x.gl = follow(x.gl, x.hover ? K.hoverGlow : 0, dt, 0.12, 0.25);
      x.ac = follow(x.ac, x.hover ? K.hoverAct : K.restAct, dt, 0.1, 0.25);
      x.inst.frame(now, { act: x.ac, speed: x.sp * K.restBase, glow: x.gl, scale: 1, reduceMotion: rm });
    } else {
      const st = S.sizeState;
      const ts = st === 'listening' ? 1 + K.voiceSpeed * sizeV : st === 'processing' ? K.procSpeed : st === 'hover' ? K.hoverSpeed : 1;
      const ta = st === 'rest' ? K.restAct : st === 'hover' ? K.hoverAct : 1;
      const tg = st === 'listening' ? K.listenGlow + K.voiceGlow * sizeV : st === 'processing' ? K.procGlow : st === 'hover' ? K.hoverGlow : 0;
      const tsc = st === 'listening' ? K.listenScale + K.voiceScale * sizeV : st === 'processing' ? K.procScale : st === 'hover' ? K.hoverScale : 1;
      x.sp = follow(x.sp, ts, dt, st === 'listening' ? 0.05 : 0.2, st === 'listening' ? 0.05 : 0.5);
      x.ac = follow(x.ac, ta, dt, 0.08, 0.25); x.gl = follow(x.gl, tg, dt, 0.12, 0.25);
      spring(x.sc, tsc, dt, 0.3, 0.7);
      x.inst.frame(now, { act: x.ac, speed: x.sp * K.restBase, glow: x.gl, scale: x.sc.p, lift: st === 'rest' ? 0 : 0.6, bands: st === 'listening' ? sizeSm : null, reduceMotion: rm });
    }
  }

  // ---- the field's right edge follows the tools (variant, model name, orb size)
  if (fieldDirty || S.fieldDirty) {
    fieldDirty = false; S.fieldDirty = false;
    // at rest the text runs to 12 px before the orb; while live it stops 10 px before the cross (and, in B, the timer)
    const tl0 = $('tools').offsetLeft, D = S.D.p, live = cmp.classList.contains('live'), cx = tl0 + D / 2;
    const pill = VARIANTS[S.variant].pill;
    let right = 820 - (tl0 - 12);
    // the pill: anchored at the orb, it opens to the left (the orb at its right end, the label, the cross at its far
    // end); its left and width ease together, so its right end never moves
    const vp = $('vpill');
    if (pill) {
      const P = PILL[pill], W = P.H + 8 + labelWidth(S.label, P.font) + 8 + 24 + 8, w = live ? W : P.H;
      vp.style.top = P.top + 'px'; vp.style.height = P.H + 'px';
      // B opens to the left (the toolbar is on the right); C opens to the right, above the toolbar, so it stays clear
      // of the chat's content above the composer (the files strip is centred)
      vp.style.left = (pill === 'float' ? cx - P.H / 2 : cx + P.H / 2 - w).toFixed(1) + 'px'; vp.style.width = w.toFixed(1) + 'px';
      vp.style.setProperty('--fs', P.font + 'px');
      if (live && pill === 'inline') right = 820 - (cx + P.H / 2 - W - 10);
    } else if (live) right = 820 - (cx - (D * K.listenScale / 2 + 7) - 24 - 10);
    field.style.right = Math.round(right) + 'px';
    S.fieldW = 820 - (S.attach === 'out' ? 14 : 48) - Math.round(right);   // the width it is easing to
    $('orbslot').style.setProperty('--xo', (-(D * K.listenScale / 2 + 7)).toFixed(1) + 'px');
    S.scrollDirty = true;
  }

  // ---- text readouts at about 20 Hz
  if (now - lastDom > 0.05) {
    lastDom = now;
    if (S.scrollDirty) {
      // the line follows its end, as an editor keeps the caret in view: eased, never jumping
      S.scrollDirty = false;
      const over = Math.max(0, tin.offsetWidth + 4 - (S.fieldW || field.clientWidth));
      const tx = over > 0 ? `translateX(${-over}px)` : '';
      if (tin.style.transform !== tx) tin.style.transform = tx;
    }
    const lv = S.raw;
    meterBars.forEach((b, i) => { const k = [0.55, 0.85, 1, 0.8, 0.6][i]; b.style.transform = `scaleY(${Math.max(0.12, Math.min(1, lv * k * 1.25 * (0.85 + 0.3 * vnoise(now * 7 + i * 3)))).toFixed(3)})`; });
    $('srcLv').textContent = lv.toFixed(2);
    const st = shownState(now);
    if (st !== lastRail) { lastRail = st; railBtns.forEach((b) => b.classList.toggle('on', b.dataset.id === st)); }
    const idle = ORB[S.style].idleSpeed;
    $('read').innerHTML = `<span>state <b>${st}</b></span><span>speed <b>${S.speed.toFixed(2)}x</b> (${(idle * K.restBase * S.speed).toFixed(3)}/s)</span><span>scale <b>${S.scale.p.toFixed(3)}</b></span><span>voice <b>${S.v.toFixed(2)}</b></span><span>look <b>${act.toFixed(2)}</b> Idle to Thinking</span><span>beam <b>${S.beamVis.toFixed(2)}</b></span>`;
  }
  if (window.__vc.rec) { const w = performance.now() - tw0; window.__vc.work.push(w); if (w > 3) (window.__vc.longjs || (window.__vc.longjs = [])).push([+w.toFixed(1), S.mode, layerOn]); }
}

// ------------------------------------------------------------------ boot
applyVariant();
$('orbslot').style.width = $('orbslot').style.height = S.D.p + 'px';
buildTiles();
buildSizes();
applyTheme();
if (window.matchMedia) window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { applyTheme(); segs[2].set(dark() ? 'dark' : 'light'); });
const sizeMn = () => { const vis = mnOn === 'A' ? $('mnA') : $('mnB'); $('mn').style.width = vis.offsetWidth + 'px'; fieldDirty = true; };
sizeMn();
if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => { segs.forEach((s) => s.place()); sizeMn(); });
requestAnimationFrame(tick);
