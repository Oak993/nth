// Border beam (border-beam 1.4.1, Copyright (c) 2026 Jakub Antalik, MIT): the engine, its tables and its clock curves
// are in beam.js, which gen.py places above this file.

// MARROW_DESIGNS: the presets registry for "Marrow designs" (defined below, after the helpers). Each entry appears
// in the panel's Marrow designs list and in the compare grid's fourth slot, and runs on the same state, theme and clock
// as the others. Shape (every field but id and name optional):
//   { id: 'a1', tag: 'A1', group: 'ambient' | 'responsive', name: 'Faithful', note: 'one line for the list',
//     engine: 'aura' | 'beam' | 'none',      // draw with the Uiverse aura or Border beam engine, or only your layers
//     aura: { ...AURA_DEFAULTS keys }, beam: { ...BEAM_DEFAULTS keys },   // the engine's settings for this design
//     speeds: { rest, typing, running },     // aura engine: seconds a turn per state (the rate eases between them)
//     rate: (st, clock) => turns per second, // aura engine: instead of speeds (e.g. settle at a home angle)
//     layers: { outer: ['halo'], inner: ['edge'] },  // empty <i> elements given these classes: outer ones sit
//                                    // around the composer, behind it; inner ones inside it, over its glass
//     css: '[data-design="a1"] .halo{...}',  // scoped by [data-design="<id>"]
//     vars: (env, st) => ({...}),    // per state, theme, model and accent; written on the light box. The registered
//                                    // --di --dg --do --dbd --dbs (numbers) and --dc1 --dc2 --dc3 (colours) transition there
//     tick: (m, env) => ({...}),     // per frame (m: the design's memory, env: st, dark, dt, e = eased time step, cx
//                                    // = caret x); returns custom properties written on the clock box (not transitioned)
//     port: 'how the port draws it cheaply', cost: '~0.05 ms GPU' }
// Per-frame clock variables on every instance: --t (seconds, eased by Pause), --brth (a 7 s breath, 0..1),
// --au-r, --au-s, --bb-a, --ring-a; state classes on the composer box: .cmp.typing, .cmp.running; theme: .board.light

// Uiverse aura: SelfMadeSystem's button (Uiverse.io, MIT) around the composer. "Original" restores its values.
const AURA_DEFAULTS = { scheme: 'original', c1: '#8E7CFF', c2: '#38C9B4', speed: 12, breathing: true, rest: 40, typing: 55, running: 75, haloSize: 2, haloBlur: 30, rim: true, rimW: 1.5, dots: true, dotCell: 3, dotStr: 67 };
const AURA_ORIGINAL = { scheme: 'original', speed: 8, breathing: true, rest: 50, typing: 50, running: 100, haloSize: 0, haloBlur: 32, rim: true, rimW: 2, dots: true, dotCell: 3, dotStr: 67 };
// Border beam: the package's own defaults (theme starts on Auto so the preview's Dark/Light is honoured;
// the package default is dark).
// fit: the playground's geometry switch (not a package prop): true = "Fitted to the composer", false = "As the package".
const BEAM_DEFAULTS = { family: 'rotate', size: 'md', color: 'colorful', theme: 'auto', active: true, duration: null, radius: null, brightness: null, hueShift: 0, strength: 1, saturation: null, glowSize: 1, hueRange: 30, staticColors: false, strokeX: 1, innerX: 1, bloomX: 1, coreBlur: null, bloomBlur: null, glowX: null, glowY: null, boost: 1, spike: 1, css: '', faithful: false, respond: false, fit: true };
const BEAM_CALM = { duration: 9, strength: 0.55, hueRange: 18, glowSize: 1.3, respond: true };
const DUR = { md: 1.96, sm: 1.96, line: 3.1, 'pulse-inner': 2.3, 'pulse-outside': 2.3 };
const TYPE_LABEL = { md: 'Large', sm: 'Small', line: 'Line', 'pulse-inner': 'Pulse Inner', 'pulse-outside': 'Pulse Outside' };
const PAL_NAMES = ['colorful', 'mono', 'ocean', 'sunset', 'forest', 'candy', 'ice', 'gold'];
const PAL_LABEL = { colorful: 'Colorful', mono: 'Mono', ocean: 'Ocean', sunset: 'Sunset', forest: 'Forest', candy: 'Candy', ice: 'Ice', gold: 'Gold' };
const STATES = ['rest', 'typing', 'running'];
const STATE_LABEL = { rest: 'at rest', typing: 'typing', running: 'running a turn' };
const TYPED = 'Add a keyboard shortcut for the theme switch';
const CW = 820, CH = 52, CR = 22;
const CFONT = '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';   // the composer's font
const AUTO_CYCLE = [['rest', 3.2], ['typing', 4.6], ['running', 5.6]];

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const hex2 = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));
const mixHex = (h, w, t) => { const a = hex2(h), b = hex2(w); return '#' + a.map((x, i) => Math.round(x + (b[i] - x) * t).toString(16).padStart(2, '0')).join(''); };
const fmt = (v, n) => (Math.round(v * Math.pow(10, n)) / Math.pow(10, n)).toString();
// cubic-bezier(.56,.15,.28,.86), the reference's speen timing, solved for y at x.
const bez = (x) => {
  const x1 = 0.56, y1 = 0.15, x2 = 0.28, y2 = 0.86;
  const cx = (t) => 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t;
  const cy = (t) => 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
  let lo = 0, hi = 1, t = x;
  for (let i = 0; i < 24; i++) { t = (lo + hi) / 2; if (cx(t) < x) lo = t; else hi = t; }
  return cy(t);
};

// ---------- the preview's model and accent (the designs read them) ----------
// MODELS: the chip's model; colours from the locked maker palette (OD-0047, OD-0061; OD-0063: a router wears its maker's
// colour): [heart, second stop, cool complement] per appearance. Gemini's maker colour is the Google gradient's heart.
const MODELS = [
  { id: 'opus', name: 'Opus', maker: 'Anthropic', claude: true, d: ['#CC6B47', '#E0875F', '#7C9BD4'], l: ['#B85A38', '#CC6B47', '#4C6FAE'] },
  { id: 'gpt', name: 'GPT-5', maker: 'OpenAI', d: ['#EEF0F5', '#D9DBE3', '#86AEE6'], l: ['#8D94A5', '#A9AEBB', '#5D86C2'] },
  { id: 'gemini', name: 'Gemini', maker: 'Google', d: ['#9C73CC', '#D96670', '#4285F5'], l: ['#8A5FC0', '#D96670', '#3B78E0'] },
  { id: 'deepseek', name: 'DeepSeek', maker: 'DeepSeek', d: ['#3D5CFA', '#6A82FF', '#58C6E2'], l: ['#2A45E0', '#3D5CFA', '#2B9CC0'] },
];
// ACCENTS: the accent picker of OD-0075 / OD-0081 (E:/Droppy/work/design/accent/palette.json), [id, name, dark, light].
const ACCENTS = /*@ACCENTS@*/null;

// The composer's outline (820 x 52, corner 22), walked by arc length so a light travels it at an even pace.
const PER = (() => { const W = 820, H = 52, R = 22, L1 = W - 2 * R, L2 = H - 2 * R, A = R * Math.PI / 2; return { W, H, R, L1, L2, A, P: 2 * L1 + 2 * L2 + 4 * A }; })();
const perim = (s) => { // clockwise from (R, 0); returns [x, y, normal x, normal y]
  const { W, H, R, L1, L2, A, P } = PER;
  s = ((s % P) + P) % P;
  const arc = (cx, cy, a) => [cx + R * Math.cos(a), cy + R * Math.sin(a), Math.cos(a), Math.sin(a)];
  if (s < L1) return [R + s, 0, 0, -1]; s -= L1;
  if (s < A) return arc(W - R, R, -Math.PI / 2 + s / R); s -= A;
  if (s < L2) return [W, R + s, 1, 0]; s -= L2;
  if (s < A) return arc(W - R, H - R, s / R); s -= A;
  if (s < L1) return [W - R - s, H, 0, 1]; s -= L1;
  if (s < A) return arc(R, H - R, Math.PI / 2 + s / R); s -= A;
  if (s < L2) return [0, H - R - s, -1, 0]; s -= L2;
  return arc(R, R, Math.PI + s / R);
};
const S_SEND = PER.L1 + PER.A + PER.L2 / 2;   // the right end, beside the send disc
const approach = (v, to, dt, tau) => v + (to - v) * (1 - Math.exp(-dt / tau));
const TAU = Math.PI * 2;
const ac = (env) => (env.dark ? env.accent[2] : env.accent[3]);
const px2 = (v) => fmt(v, 2);
const RING = '-webkit-mask:linear-gradient(#000 0 0) content-box,linear-gradient(#000 0 0);-webkit-mask-composite:xor;mask:linear-gradient(#000 0 0) content-box exclude,linear-gradient(#000 0 0)';
// a soft glow spot: radial-gradient at a point given by two per-frame properties, offset by o px
const spot = (w, h, X, Y, o, col, alpha) => 'radial-gradient(' + w + ' ' + h + ' at calc(var(' + X + ') * 1px + ' + o + 'px) calc(var(' + Y + ') * 1px + ' + o + 'px),color-mix(in srgb,' + col + ' calc(' + alpha + ' * 100%),transparent),transparent)';
// a soft pill-shaped falloff round the composer for a layer inset by I px: opaque at the composer's edge, gone reach px out
const PILL = (I, reach, webkit) => {
  const r = 26, c = I + r;
  const end = (x) => 'radial-gradient(circle at ' + x + ' 50%,#000 ' + r + 'px,rgba(0,0,0,.4) ' + (r + reach * 0.45) + 'px,transparent ' + (r + reach) + 'px)';
  const mid = 'linear-gradient(transparent ' + (I - reach) + 'px,rgba(0,0,0,.4) ' + (I - reach * 0.55) + 'px,#000 ' + I + 'px,#000 calc(100% - ' + I + 'px),rgba(0,0,0,.4) calc(100% - ' + (I - reach * 0.55) + 'px),transparent calc(100% - ' + (I - reach) + 'px)) center/calc(100% - ' + 2 * c + 'px) 100% no-repeat';
  return webkit ? end(c + 'px') + ',' + mid + ',' + end('calc(100% - ' + c + 'px)') : end(c + 'px') + ' add,' + mid + ' add,' + end('calc(100% - ' + c + 'px)');
};
const MASKED = (dots, I, reach) => '-webkit-mask:' + (dots ? dots + ',' : '') + PILL(I, reach, true) + ';-webkit-mask-composite:' + (dots ? 'source-in,' : '') + 'source-over,source-over;mask:' + (dots ? dots + ' intersect,' : '') + PILL(I, reach, false);
const DOTS = (r) => 'radial-gradient(circle,#000 0 ' + r + 'px,transparent ' + (r + 0.45) + 'px) 0 0/5px 5px';

// ---------- MARROW_DESIGNS: eight composer lights for OD-0083 ----------
const MARROW_DESIGNS = [
  // A1: the reference, kept as it is but slowed to a 26 s turn and held low until the composer is used.
  { id: 'a1', tag: 'A1', group: 'ambient', name: 'Faithful', note: 'The Uiverse two-tone turning once in 26 s; low at rest, brighter on focus and while a turn runs.',
    engine: 'aura', aura: { scheme: 'original', breathing: true, rest: 24, typing: 44, running: 62, haloSize: 2, haloBlur: 30, rim: true, rimW: 1.5, dots: true, dotCell: 3, dotStr: 60 },
    speeds: { rest: 26, typing: 24, running: 20 },
    port: 'The Uiverse plan unchanged (a baked halo alpha and rim ring, one shader for the turning two-tone and the halftone), the turn slowed and the intensity per state eased over .9 s.',
    cost: '~0.05 ms GPU' },
  // A2: the model's maker colour on one side and a cool complement on the other; a model change crossfades them.
  { id: 'a2', tag: 'A2', group: 'ambient', name: 'Maker light', note: 'The chip\u2019s maker colour with a cool complement; changing the model crossfades it over 1.2 s.',
    engine: 'aura', aura: { scheme: 'makercool', breathing: true, rest: 30, typing: 48, running: 66, haloSize: 3, haloBlur: 32, rim: true, rimW: 1.25, dots: false },
    speeds: { rest: 24, typing: 22, running: 18 },
    css: '.lopts[data-beam="d-a2"]{transition-duration:1.2s}',
    port: 'The Uiverse plan with no halftone; its two colours come from the model\u2019s maker palette (OD-0063) and glide in OKLab over 1.2 s when the model changes.',
    cost: '~0.04 ms GPU' },
  // A3: the user's accent, a single hue with a lighter sibling.
  { id: 'a3', tag: 'A3', group: 'ambient', name: 'Accent light', note: 'The user\u2019s accent, the Windows accent by default, in a wider and softer halo.',
    engine: 'aura', aura: { scheme: 'accent', breathing: true, rest: 28, typing: 46, running: 64, haloSize: 4, haloBlur: 34, rim: true, rimW: 1.25, dots: false },
    speeds: { rest: 28, typing: 26, running: 20 },
    css: '.lopts[data-beam="d-a3"]{transition-duration:1.2s}',
    port: 'The Uiverse plan with the accent brush as its colour (OD-0075); a change of accent glides over 1.2 s. One halo draw and one rim draw, uniforms only.',
    cost: '~0.04 ms GPU' },
  // A4: no halo at all, a fine band of light that walks the edge at an even pace.
  { id: 'a4', tag: 'A4', group: 'ambient', name: 'Rim only', note: 'No halo: a fine band of light walks the edge, once in 24 s at rest and in 10 s while a turn runs.',
    engine: 'none', layers: { outer: ['a4-glow'], inner: ['a4-in', 'a4-band'] },
    css: '[data-design="a4"] .a4-glow{inset:-20px;border-radius:42px;background:' + spot('84px', '16px', '--a4-1x', '--a4-1y', 20, 'var(--dc2)', 'var(--di) * .34') + ',' + spot('190px', '22px', '--a4-2x', '--a4-2y', 20, 'var(--dc2)', 'var(--di) * .15') + '}' +
      '[data-design="a4"] .a4-band{padding:1.25px;box-sizing:border-box;background:' + spot('44px', '30px', '--a4-1x', '--a4-1y', 0, 'var(--dc1)', 'var(--di)') + ',' + spot('110px', '30px', '--a4-2x', '--a4-2y', 0, 'var(--dc1)', 'var(--di) * .55') + ',' + spot('230px', '30px', '--a4-3x', '--a4-3y', 0, 'var(--dc1)', 'var(--di) * .22') + ';' + RING + '}' +
      '[data-design="a4"] .a4-in{background:' + spot('90px', '20px', '--a4-1x', '--a4-1y', 0, 'var(--dc1)', 'var(--di) * .07') + '}',
    vars: (env, st) => ({ '--di': { rest: 0.55, typing: 0.8, running: 1 }[st] * (env.dark ? 1 : 1.25), '--dc1': env.dark ? '#FFF4E8' : '#FFFFFF', '--dc2': env.dark ? '#FFD7B0' : '#E9945F' }),
    tick: (m, v) => {
      const lap = { rest: 24, typing: 20, running: 10 }[v.st];
      m.v = approach(m.v == null ? PER.P / lap : m.v, PER.P / lap, v.dt, 1.2);
      m.s = (m.s == null ? PER.L1 * 0.3 : m.s) + v.e * m.v;
      const o = {};
      [0, 46, 120].forEach((b, i) => { const p = perim(m.s - b); o['--a4-' + (i + 1) + 'x'] = px2(p[0]); o['--a4-' + (i + 1) + 'y'] = px2(p[1]); });
      return o;
    },
    port: 'One edge-ring texture (1.25 px, baked per size), lit by a shader around a point walked along the outline by arc length (three falloffs make the tail), and one soft bloom just outside. Uniforms: three points.',
    cost: '~0.03 ms GPU' },
  // B1: still at rest, it breathes; it turns only while a turn runs, and glides back to a home angle after.
  { id: 'b1', tag: 'B1', group: 'responsive', name: 'Breathing', note: 'No rotation at rest, a slow 7 s breath; the light turns only while a turn runs, then settles home.',
    engine: 'aura', aura: { scheme: 'maker', breathing: false, rest: 46, typing: 58, running: 72, haloSize: 6, haloBlur: 30, rim: true, rimW: 1.25, dots: false },
    rate: (st, c) => { const run = 1 / 11; if (st === 'running') return run; const h = c.p * 2; return Math.min(run, (Math.ceil(h - 1e-4) - h) / 2 * 0.9); },
    vars: () => ({ '--dbd': 0.42, '--dbs': 5 }),
    port: 'The Uiverse plan with the angle held: a 7 s breath scales the halo\u2019s alpha and spread through two uniforms; the angle moves only while a turn runs and eases to the next home angle after it ends.',
    cost: '~0.05 ms GPU' },
  // B2: almost nothing at rest; the light gathers at the caret while typing, runs round the rim on send, then orbits.
  { id: 'b2', tag: 'B2', group: 'responsive', name: 'Gathering', note: 'Almost nothing at rest; light gathers at the caret while typing, a pulse runs round the rim on send, then a slow orbit.',
    engine: 'none', layers: { outer: ['b2-out'], inner: ['b2-in', 'b2-ring'] },
    css: '[data-design="b2"] .b2-out{inset:-34px;border-radius:56px;background:' +
      'radial-gradient(calc(760px - 640px * var(--dg)) calc(34px - 6px * var(--dg)) at calc((410 + (var(--b2-cx) - 410) * var(--dg)) * 1px + 34px) 34px,color-mix(in srgb,var(--dc1) calc((var(--di) * .3 + var(--dg) * .42) * 100%),transparent),transparent),' +
      'radial-gradient(calc(760px - 600px * var(--dg)) 24px at calc((410 + (var(--b2-cx) - 410) * var(--dg)) * 1px + 34px) 86px,color-mix(in srgb,var(--dc2) calc(var(--dg) * 18%),transparent),transparent),' +
      spot('120px', '24px', '--b2-p1x', '--b2-p1y', 34, 'var(--dc1)', 'var(--b2-pa) * .42') + ',' + spot('120px', '24px', '--b2-p2x', '--b2-p2y', 34, 'var(--dc1)', 'var(--b2-pa) * .42') + ',' +
      spot('170px', '26px', '--b2-ox', '--b2-oy', 34, 'var(--dc2)', 'var(--do) * .3') + '}' +
      '[data-design="b2"] .b2-ring{padding:1.25px;box-sizing:border-box;background:' +
      'radial-gradient(calc(300px - 230px * var(--dg)) 30px at calc((410 + (var(--b2-cx) - 410) * var(--dg)) * 1px) 0,color-mix(in srgb,var(--dc1) calc(var(--dg) * 90%),transparent),transparent),' +
      'radial-gradient(calc(300px - 220px * var(--dg)) 30px at calc((410 + (var(--b2-cx) - 410) * var(--dg)) * 1px) 52px,color-mix(in srgb,var(--dc1) calc(var(--dg) * 40%),transparent),transparent),' +
      spot('64px', '30px', '--b2-p1x', '--b2-p1y', 0, 'var(--dc1)', 'var(--b2-pa)') + ',' + spot('64px', '30px', '--b2-p2x', '--b2-p2y', 0, 'var(--dc1)', 'var(--b2-pa)') + ',' +
      spot('150px', '30px', '--b2-p1tx', '--b2-p1ty', 0, 'var(--dc1)', 'var(--b2-pa) * .35') + ',' + spot('150px', '30px', '--b2-p2tx', '--b2-p2ty', 0, 'var(--dc1)', 'var(--b2-pa) * .35') + ',' +
      spot('120px', '30px', '--b2-ox', '--b2-oy', 0, 'var(--dc1)', 'var(--do) * .85') + ';' + RING + '}' +
      '[data-design="b2"] .b2-in{background:radial-gradient(calc(420px - 330px * var(--dg)) 24px at calc((410 + (var(--b2-cx) - 410) * var(--dg)) * 1px) 0,color-mix(in srgb,var(--dc1) calc(var(--dg) * 9%),transparent),transparent)}',
    vars: (env, st) => { const a = ac(env); return { '--di': { rest: 0.08, typing: 0.2, running: 0.1 }[st], '--dg': st === 'typing' ? 1 : 0, '--do': st === 'running' ? 1 : 0, '--dc1': a, '--dc2': mixHex(a, '#ffffff', env.dark ? 0.4 : 0.25) }; },
    tick: (m, v) => {
      const o = {};
      // a new message starts at the left: the caret jumps there, the light (still ungathered) follows it at once
      m.cx = m.cx == null || (v.st === 'typing' && m.prev !== 'typing') ? v.cx : approach(m.cx, v.cx, v.e, 0.09);
      o['--b2-cx'] = px2(m.cx);
      if (v.st === 'running' && m.prev != null && m.prev !== 'running') { m.pt = 0; m.os = S_SEND + PER.P / 2; }
      m.prev = v.st;
      if (m.os == null) m.os = S_SEND + PER.P / 2;
      let pa = 0, d = m.d || 0, dt2 = Math.min(d, 70);
      if (m.pt != null) {
        m.pt += v.e;
        const u = clamp(m.pt / 1.7, 0, 1);
        d = (1 - Math.pow(1 - u, 3)) * PER.P / 2;
        dt2 = Math.min(d, 70);
        pa = Math.pow(Math.sin(Math.PI * Math.pow(u, 0.55)), 1.4);
        if (u >= 1) m.pt = null;
        m.d = m.pt == null ? d : 0;   // after the run the points rest where the halves met (unlit) until the next send
      }
      const p1 = perim(S_SEND + d), p2 = perim(S_SEND - d), t1 = perim(S_SEND + d - dt2), t2 = perim(S_SEND - d + dt2);
      o['--b2-pa'] = fmt(pa, 4);
      o['--b2-p1x'] = px2(p1[0]); o['--b2-p1y'] = px2(p1[1]); o['--b2-p2x'] = px2(p2[0]); o['--b2-p2y'] = px2(p2[1]);
      o['--b2-p1tx'] = px2(t1[0]); o['--b2-p1ty'] = px2(t1[1]); o['--b2-p2tx'] = px2(t2[0]); o['--b2-p2ty'] = px2(t2[1]);
      m.os += v.e * PER.P / 16;
      const q = perim(m.os); o['--b2-ox'] = px2(q[0]); o['--b2-oy'] = px2(q[1]);
      return o;
    },
    port: 'Two baked textures (the edge ring and a soft outside band) lit by one shader from a few points: the caret\u2019s gather (width and strength eased on the CPU), the two send pulses walked by arc length, and the orbit. Nothing moves at rest, so no frames are drawn then.',
    cost: '~0.04 ms GPU' },
  // B3: two soft blobs drifting round the composer at different, slowly varying paces, in opposite directions.
  { id: 'b3', tag: 'B3', group: 'responsive', name: 'Aurora', note: 'Two soft blobs, mint and violet, drift round the composer at different paces, brisker while a turn runs.',
    engine: 'none', layers: { outer: ['b3-out'], inner: ['b3-in', 'b3-ring'] },
    css: '[data-design="b3"] .b3-out{inset:-60px;border-radius:82px;background:' +
      'radial-gradient(calc(var(--b3-1w) * 1px) calc(var(--b3-1h) * 1px) at calc(var(--b3-1x) * 1px + 60px) calc(var(--b3-1y) * 1px + 60px),color-mix(in srgb,var(--dc1) calc(var(--di) * 44%),transparent),color-mix(in srgb,var(--dc1) calc(var(--di) * 14%),transparent) 48%,transparent),' +
      'radial-gradient(calc(var(--b3-2w) * 1px) calc(var(--b3-2h) * 1px) at calc(var(--b3-2x) * 1px + 60px) calc(var(--b3-2y) * 1px + 60px),color-mix(in srgb,var(--dc2) calc(var(--di) * 44%),transparent),color-mix(in srgb,var(--dc2) calc(var(--di) * 14%),transparent) 48%,transparent)}' +
      '[data-design="b3"] .b3-ring{padding:1.25px;box-sizing:border-box;background:' +
      'radial-gradient(calc(var(--b3-1w) * .55px) 30px at calc(var(--b3-1rx) * 1px) calc(var(--b3-1ry) * 1px),color-mix(in srgb,var(--dc1) calc(var(--di) * 80%),transparent),transparent),' +
      'radial-gradient(calc(var(--b3-2w) * .55px) 30px at calc(var(--b3-2rx) * 1px) calc(var(--b3-2ry) * 1px),color-mix(in srgb,var(--dc2) calc(var(--di) * 80%),transparent),transparent);' + RING + '}' +
      '[data-design="b3"] .b3-in{background:' +
      'radial-gradient(calc(var(--b3-1w) * .6px) 40px at calc(var(--b3-1rx) * 1px) calc(var(--b3-1ry) * 1px),color-mix(in srgb,var(--dc1) calc(var(--di) * 7%),transparent),transparent),' +
      'radial-gradient(calc(var(--b3-2w) * .6px) 40px at calc(var(--b3-2rx) * 1px) calc(var(--b3-2ry) * 1px),color-mix(in srgb,var(--dc2) calc(var(--di) * 7%),transparent),transparent)}',
    vars: (env, st) => ({ '--di': { rest: 0.5, typing: 0.64, running: 0.85 }[st], '--dc1': env.dark ? '#5BE0B6' : '#1E9E7A', '--dc2': env.dark ? '#8E7DFF' : '#6A55E8' }),
    tick: (m, v) => {
      const k = { rest: 1, typing: 1.2, running: 1.9 }[v.st];
      m.k = approach(m.k == null ? k : m.k, k, v.dt, 1.4);
      m.t = (m.t || 0) + v.e;
      const t = m.t;
      m.s1 = (m.s1 == null ? PER.L1 * 0.22 : m.s1) + v.e * m.k * PER.P / 40 * (1 + 0.45 * Math.sin(TAU * t / 13.7));
      m.s2 = (m.s2 == null ? PER.L1 + PER.A * 2 + PER.L2 + PER.L1 * 0.3 : m.s2) - v.e * m.k * PER.P / 29 * (1 + 0.35 * Math.sin(TAU * t / 9.3 + 1.7));
      const o = {};
      const put = (n, s, w, h, off) => {
        const p = perim(s);
        o['--b3-' + n + 'x'] = px2(p[0] + p[2] * off); o['--b3-' + n + 'y'] = px2(p[1] + p[3] * off);
        o['--b3-' + n + 'rx'] = px2(p[0]); o['--b3-' + n + 'ry'] = px2(p[1]);
        o['--b3-' + n + 'w'] = px2(w); o['--b3-' + n + 'h'] = px2(h);
      };
      put(1, m.s1, 290 + 70 * Math.sin(TAU * t / 11.1), 64 + 10 * Math.sin(TAU * t / 7.9), 6 + 5 * Math.sin(TAU * t / 5.3));
      put(2, m.s2, 240 + 60 * Math.sin(TAU * t / 8.3 + 2), 58 + 9 * Math.sin(TAU * t / 6.1 + 1), 4 + 5 * Math.sin(TAU * t / 6.7 + 0.6));
      return o;
    },
    port: 'Two Gaussian blobs in one shader over a band around the composer (no texture, no blur pass), walked by arc length with slow incommensurate sines on the CPU, and the same two on the baked edge ring. Uniforms: two centres and sizes.',
    cost: '~0.06 ms GPU' },
  // B4: the halftone as the hero: the turning two-tone seen only as dots that grow where the light is strongest.
  { id: 'b4', tag: 'B4', group: 'responsive', name: 'Halftone glow', note: 'The dot screen is the light: the two-tone turns slowly beneath it and the dots swell where it is brightest.',
    engine: 'none', layers: { outer: ['b4-soft', 'b4-d1', 'b4-d2'], inner: [] },
    css: '[data-design="b4"] .b4-soft,[data-design="b4"] .b4-d1,[data-design="b4"] .b4-d2{inset:-26px;border-radius:0}' +
      // the reference's light seen through the composer: only the middle of its gradient (the turning square is 4x the box)
      '[data-design="b4"] .b4-soft{background:linear-gradient(calc(var(--b4-a) * 1deg),color-mix(in srgb,var(--dc1) 70%,transparent),transparent 50%,color-mix(in srgb,var(--dc2) 70%,transparent));opacity:calc(var(--di) * .22);' + MASKED('', 26, 26) + '}' +
      '[data-design="b4"] .b4-d1{background:linear-gradient(calc(var(--b4-a) * 1deg),var(--dc1),transparent 50%,var(--dc2));opacity:calc(var(--di) * .95);' + MASKED(DOTS(0.7), 26, 24) + '}' +
      '[data-design="b4"] .b4-d2{background:linear-gradient(calc(var(--b4-a) * 1deg),var(--dc1),transparent 32% 68%,var(--dc2));opacity:var(--di);' + MASKED(DOTS(1.3), 26, 13) + '}',
    vars: (env, st) => ({ '--di': { rest: 0.5, typing: 0.72, running: 1 }[st], '--dc1': env.dark ? '#FF6A2E' : '#E0501E', '--dc2': env.dark ? '#4C82FF' : '#2A5BE8' }),
    tick: (m, v) => {
      const lap = { rest: 34, typing: 26, running: 13 }[v.st];
      m.v = approach(m.v == null ? 1 / lap : m.v, 1 / lap, v.dt, 1.2);
      m.r = (m.r == null ? 0.03 : m.r) + v.e * m.v;
      // the reference's stretched turn: an even turn in the composer's own proportions, given as a screen angle
      const r = TAU * m.r, W = 880, H = 112;
      let a = Math.atan2(Math.cos(r) / W, -Math.sin(r) / H) * 180 / Math.PI;
      if (m.a != null) { while (a - m.a > 180) a -= 360; while (a - m.a < -180) a += 360; }
      m.a = a;
      return { '--b4-a': fmt(a, 3) };
    },
    port: 'One shader over a band around the composer: the turning two-tone (a dot product, as the Uiverse light) times a distance falloff, quantised to a 5 px dot grid whose dot radius grows with the light (a true halftone; the two dot sizes here become a continuous one). No textures, uniforms only.',
    cost: '~0.05 ms GPU' },
];

class Component extends DCLogic {
  constructor(props) {
    super(props);
    this.state = {
      dark: props.dark !== false,
      light: 'aura', mode: 'rest', auto: false, phase: 'rest', typedN: 0, paused: false, compare: false, plate: 'port',
      aura: Object.assign({}, AURA_DEFAULTS), beam: Object.assign({}, BEAM_DEFAULTS),
      design: MARROW_DESIGNS.length ? MARROW_DESIGNS[0].id : null, drag: null, model: 'opus', accent: 'win',
    };
    this.propDark = props.dark !== false;
    this.xs = {};
    this.clockEls = {};
    this.clockRefs = {};
    this.clk = {};
    this.dm = {};       // per design memory (shared by the single preview and the compare slot, so they stay in step)
    this.dLast = {};    // per instance: the design shown last, ticked on while its layers fade out
    this.singleKey = null;
    this.k = 1; this.autoT = 0; this.typeT = 0; this.ringA = 0; this.ringV = 0; this.t = 0;
    this.cmpAt = -1e9;
    this.bbSlots = {}; this.bbCss = {}; this.bbSeq = 0;
    this.tick = this.tick.bind(this);
  }

  componentDidMount() {
    try { window.__composerLights = this; } catch (e) {}   // the checks in beam-fix/ drive the board through this
    const ready = () => this.forceUpdate();
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(ready);
    try { this.mq = window.matchMedia('(prefers-reduced-motion: reduce)'); } catch (e) { this.mq = null; }
    this.last = performance.now();
    this.raf = requestAnimationFrame(this.tick);
  }

  componentWillUnmount() { cancelAnimationFrame(this.raf); }

  st() { return this.state.auto ? this.state.phase : this.state.mode; }
  dark() { return this.state.dark; }

  // ---------- the frame clock: every motion is integrated, so a speed change or a pause never jumps ----------
  tick(now) {
    this.raf = requestAnimationFrame(this.tick);
    const dt = Math.min(0.05, Math.max(0, (now - this.last) / 1000));
    this.last = now;
    const reduced = !!(this.mq && this.mq.matches);
    const target = this.state.paused || reduced ? 0 : 1;
    if (reduced) this.k = 0;
    else { this.k += (target - this.k) * (1 - Math.exp(-dt / 0.22)); if (Math.abs(this.k - target) < 0.002) this.k = target; }
    const e = dt * this.k;
    this.t += e;

    // the auto cycle and the typing run on the same eased time, so Pause holds them too
    if (this.state.auto) {
      this.autoT += e;
      const total = AUTO_CYCLE.reduce((s, x) => s + x[1], 0);
      let u = this.autoT % total, ph = 'rest';
      for (const [p, d] of AUTO_CYCLE) { if (u < d) { ph = p; break; } u -= d; }
      if (ph !== this.state.phase) this.enter(ph);
    }
    if (this.st() === 'typing') {
      this.typeT += e;
      const n = Math.min(TYPED.length, Math.floor(this.typeT / 0.055));
      if (n !== this.state.typedN) this.setState({ typedN: n });
    }
    const running = this.st() === 'running';
    this.ringV += ((running ? 1 : 0) - this.ringV) * (1 - Math.exp(-dt / 0.35));
    this.ringA = (this.ringA + e * 330 * this.ringV) % 360;

    const showSingle = !this.state.compare || now - this.cmpAt < 900;
    const showStrips = this.state.compare || now - this.cmpAt < 900;
    for (const id in this.clockEls) {
      const el = this.clockEls[id];
      if (!el || !el.isConnected) continue;
      if (id === 'single' ? !showSingle : !showStrips) continue;
      const inst = this.instCfg[id];
      if (!inst) continue;
      const c = this.advance(inst.key, inst.aura, inst.beam, dt, e, now, inst.rate);
      const w = (n, v) => { if (el.__v === undefined) el.__v = {}; if (el.__v[n] !== v) { el.__v[n] = v; el.style.setProperty(n, v); } };
      w('--t', fmt(this.t, 3));
      w('--brth', fmt((1 - Math.cos(TAU * this.t / 7)) / 2, 4));
      // design layers: the shown design, and the one shown before it while its layers fade out
      const dr = this.dLast[id] || (this.dLast[id] = { cur: null, prev: null, at: 0 });
      if (dr.cur !== inst.design) { if (dr.cur) { dr.prev = dr.cur; dr.at = now; } dr.cur = inst.design; }
      const run = inst.design ? [inst.design] : [];
      if (dr.prev && dr.prev !== inst.design && now - dr.at < 1400) run.push(dr.prev);
      for (const d of run) { const dv = this.designTick(d, dt, e, now); for (const k in dv) w(k, dv[k]); }
      w('--ring-a', fmt(this.ringA, 2));
      w('--au-r', fmt(c.auR, 3)); w('--au-s', fmt(c.auS, 4));
      w('--bb-a', fmt(c.a, 3)); w('--bb-hw', fmt(c.hw, 5)); w('--bb-hw8', fmt(c.hw8, 5)); w('--bb-hpi', fmt(c.hpi, 3)); w('--bb-hpo', fmt(c.hpo, 3));
      w('--bl-x', fmt(c.lx, 5)); w('--bl-w', fmt(c.lw, 5)); w('--bl-h', fmt(c.lh, 5)); w('--bl-e', fmt(c.le, 5)); w('--bl-s1', fmt(c.ls1, 5)); w('--bl-s2', fmt(c.ls2, 5));
      if (inst.pulse) for (const p of ['pi', 'po']) { const o = c[p]; for (let i = 0; i < BB_OSC_NAMES.length; i++) w('--' + p + '-' + BB_OSC_NAMES[i], fmt(o[i], 4)); }
      // fitted: the windows that travel the composer's perimeter, rebuilt only when the angle or the radius moves
      if (inst.fitNeed) {
        const N = inst.fitNeed, r = c.rr = c.rr == null ? N.r : c.rr + (N.r - c.rr) * (1 - Math.exp(-dt / 0.22));
        const sig = fmt(c.a, 2) + '|' + fmt(r, 2) + '|' + N.sig;
        if (el.__fsig !== sig) {
          el.__fsig = sig;
          const G = bbPerim(CW, CH, r), ph = c.a / 360;
          if (N.w1) w('--fw1', bbRingConic(G, BB_PROF.w1, ph, '0,0,0'));
          if (N.wi) w('--fwi', bbInnerPieces(G, BB_PROF.w1, ph));
          if (N.wi2) w('--fwi2', bbInnerPieces(G, BB_PROF.w2, ph));
          for (const th of N.th) {
            w('--fhl-' + th, bbRingConic(G, th === 'dark' ? BB_PROF.hld : BB_PROF.hll, ph, th === 'dark' ? '255,255,255' : '0,0,0'));
            w('--fbl-' + th, bbRingConic(G, th === 'dark' ? BB_PROF.bld : BB_PROF.bll, ph, th === 'dark' ? '255,255,255' : '0,0,0'));
          }
        }
      }
    }
    this.lastFrame = now;
  }

  advance(key, aura, beam, dt, e, now, rate) {
    let c = this.clk[key];
    if (!c) c = this.clk[key] = { stamp: -1, p: 0, bp: 0, amp: aura.breathing ? 1 : 0, a: 0, hs: 0, hpi: 0, hpo: 0, tau: 0, lp: 0, tm: this.beamTheme(beam) === 'light' ? 1 : 0, pi: [], po: [] };
    if (c.stamp === now) return c;
    c.stamp = now;
    // aura: the reference's speen (10 -> 190 -> 370 deg, eased per half) and woah (scale 1 -> .75 -> 1 over 4 s)
    // the turn's rate eases to its target (a speed change or a state change never jerks the light)
    const target = rate ? rate(this.st(), c) : 1 / Math.max(0.5, aura.speed);
    c.rate = c.rate == null ? target : c.rate + (target - c.rate) * (1 - Math.exp(-dt / 0.9));
    c.p += e * c.rate;
    const h = c.p * 2, k = Math.floor(h);
    c.auR = 10 + 180 * k + 180 * bez(h - k);
    c.amp += ((aura.breathing ? 1 : 0) - c.amp) * (1 - Math.exp(-dt / 0.4));
    c.bp += e / 4;
    c.auS = 1 - 0.25 * c.amp * (1 - Math.cos(2 * Math.PI * c.bp)) / 2;
    // beam: every phase is integrated on the eased time, then put through the package's own curves
    const size = beam.size, dur = Math.max(0.3, this.beamDur(beam));
    const stat = beam.staticColors || beam.color === 'mono';
    const [pB, pS, pS2] = bbLinePeriods(dur);
    const T = typeof window !== 'undefined' && window.__bbT != null ? +window.__bbT : null;   // test hook: absolute seconds
    if (T != null) {
      const TH = window.__bbTH != null ? +window.__bbTH : T;                                   // the hue clock, if apart
      c.ap = T / dur; c.hs = TH / 12; c.hs8 = TH / 8; c.lp = T / dur; c.lbp = T / pB; c.lsp = T / pS; c.ls2p = T / pS2; c.tau = T * 2.3 / dur;
      c.hpi = stat ? 0 : bbFrac(T / 16) * 360; c.hpo = stat ? 0 : bbFrac(T / 14) * 360; c.tm = this.beamTheme(beam) === 'light' ? 1 : 0;
    } else {
      c.ap = (c.ap || 0) + e / dur; c.hs += e / 12; c.hs8 = (c.hs8 || 0) + e / 8;
      c.lp += e / dur; c.lbp = (c.lbp || 0) + e / pB; c.lsp = (c.lsp || 0) + e / pS; c.ls2p = (c.ls2p || 0) + e / pS2;
      const toWhole = (v) => v + (Math.round(v / 360) * 360 - v) * (1 - Math.exp(-dt / 0.5));
      c.hpi = stat ? toWhole(c.hpi) : (c.hpi + e * 360 / 16) % 360;
      c.hpo = stat ? toWhole(c.hpo) : (c.hpo + e * 360 / 14) % 360;
      c.tau += e * 2.3 / dur;
      c.tm += ((this.beamTheme(beam) === 'light' ? 1 : 0) - c.tm) * (1 - Math.exp(-dt / 0.3));
    }
    c.a = bbFrac(c.ap) * 360;                          // beam-spin: linear, one turn per duration
    c.hw = bbSwing(c.hs);                              // beam-hue-shift: 12 s, ease-in-out each way
    c.hw8 = bbSwing(c.hs8);                            // Line's bloom: 8 s
    for (const p of ['pi', 'po']) {
      const sz = p === 'pi' ? 'pulse-inner' : 'pulse-outside', td = BB_OSC[sz + '/dark'], tl = BB_OSC[sz + '/light'];
      for (let i = 0; i < td.length; i++) c[p][i] = bbOscAt(td[i], c.tau) * (1 - c.tm) + bbOscAt(tl[i], c.tau) * c.tm;
    }
    // Line: travel and edge fade (linear keyframes), breathe and the two spikes (ease-in-out keyframes)
    c.lx = bbKf(BB_TRAVEL_X, c.lp, false); c.lw = bbKf(BB_TRAVEL_W, c.lp, false); c.le = bbKf(BB_EDGE, c.lp, false);
    c.lh = bbKf(BB_BREATHE, c.lbp, true); c.ls1 = bbKf(BB_SPIKE, c.lsp, true); c.ls2 = bbKf(BB_SPIKE2, c.ls2p, true);
    return c;
  }

  // one design's per-frame values, once a frame however many instances show it
  designTick(d, dt, e, now) {
    if (!d.tick) return null;
    const m = this.dm[d.id] || (this.dm[d.id] = { stamp: -1, out: {} });
    if (m.stamp === now) return m.out;
    m.stamp = now;
    m.out = d.tick(m, { st: this.st(), dark: this.dark(), dt, e, t: this.t, cx: this.caretX() }) || {};
    return m.out;
  }

  // the caret's x inside the composer (the placeholder's left inset plus the typed text's width)
  caretX() {
    if (this.st() !== 'typing') return this.lastCx == null ? 15 : this.lastCx;
    const n = this.state.typedN;
    if (this.cxN !== n) { this.cxN = n; this.cxV = 15 + this.tw(TYPED.slice(0, n), 15, 400, CFONT); }
    this.lastCx = this.cxV;
    return this.cxV;
  }

  model() { return MODELS.find((x) => x.id === this.state.model) || MODELS[0]; }
  accent() { return ACCENTS.find((x) => x[0] === this.state.accent) || ACCENTS[0]; }
  env() { return { dark: this.dark(), model: this.model(), accent: this.accent() }; }

  enter(ph) {
    const patch = { phase: ph };
    if (ph === 'typing') { this.typeT = 0; patch.typedN = 0; }
    this.setState(patch);
  }

  setMode(m) {
    if (m === 'auto') {
      if (this.state.auto) return;
      this.autoT = 0; this.typeT = 0;
      this.setState({ auto: true, phase: 'rest' });
      return;
    }
    const patch = { auto: false, mode: m };
    if (m === 'typing' && this.st() !== 'typing') { this.typeT = 0; patch.typedN = 0; }
    this.setState(patch);
  }

  setOpt(group, key, v) { this.setState({ [group]: Object.assign({}, this.state[group], { [key]: v }) }); }
  setOpts(group, patch) { this.setState({ [group]: Object.assign({}, this.state[group], patch) }); }

  // two-slot crossfade for a changing label
  xf(key, text) {
    let x = this.xs[key];
    if (!x) x = this.xs[key] = { t: [text, ''], on: 0 };
    if (x.t[x.on] !== text) { x.on = 1 - x.on; x.t[x.on] = text; }
    return { a: { t: x.t[0], cls: x.on === 0 ? 'on' : '' }, b: { t: x.t[1], cls: x.on === 1 ? 'on' : '' } };
  }

  tw(text, size, weight, family) {
    if (!this.ctx) this.ctx = document.createElement('canvas').getContext('2d');
    this.ctx.font = (weight || 500) + ' ' + size + 'px ' + (family || 'Geist, system-ui, sans-serif');
    return Math.ceil(this.ctx.measureText(text).width);
  }

  seg(label, opts, cur, pick, o) {
    o = o || {};
    const size = o.sm ? 11.5 : 12, pad = o.sm ? 16 : 24;
    let ws = opts.map((x) => this.tw(x.t, size) + pad);
    if (o.fill) { const extra = (o.fill - 4 - ws.reduce((a, b) => a + b, 0)) / ws.length; if (extra > 0) ws = ws.map((w) => Math.floor(w + extra)); }
    let left = 2, pl = 2, pw = ws[0];
    const out = opts.map((x, i) => {
      const on = x.id === cur;
      if (on) { pl = left; pw = ws[i]; }
      left += ws[i];
      return { t: x.t, w: ws[i], on, cls: on ? 'on' : '', pick: () => pick(x.id) };
    });
    return { label, pl, pw, opts: out };
  }

  // ---------- light parameters ----------
  beamTheme(b) { return b.theme === 'auto' ? (this.dark() ? 'dark' : 'light') : b.theme; }

  auraColors(a) {
    const dark = this.dark();
    if (a.scheme === 'original') return { c1: '#ff5500', c2: '#0055ff', k1: '#ff9955', k2: '#5599ff', n1: '#ffcc99', n2: '#99ccff' };
    let c1, c2;
    const mk = dark ? this.model().d : this.model().l;
    if (a.scheme === 'maker') { c1 = mk[0]; c2 = mk[1]; }                  // the chip's model: its maker's liquid stops
    else if (a.scheme === 'makercool') { c1 = mk[0]; c2 = mk[2]; }         // the maker colour and a cool complement
    else if (a.scheme === 'accent') { const x = this.accent(); c1 = dark ? x[2] : x[3]; c2 = mixHex(c1, '#ffffff', 0.45); } // the accent picker
    else { c1 = a.c1; c2 = a.c2; }
    return { c1, c2, k1: mixHex(c1, '#ffffff', 0.35), k2: mixHex(c2, '#ffffff', 0.35), n1: mixHex(c1, '#ffffff', 0.6), n2: mixHex(c2, '#ffffff', 0.6) };
  }

  auraVars(a, st) {
    const dark = this.dark(), c = this.auraColors(a);
    const hs = a.haloSize, hb = a.haloBlur, rw = a.rimW;
    const v = {
      '--au-c1': c.c1, '--au-c2': c.c2, '--au-k1': c.k1, '--au-k2': c.k2, '--au-n1': c.n1, '--au-n2': c.n2,
      '--au-i': fmt(a[st] / 100, 3), '--au-ro': a.rim ? 1 : 0, '--au-do': a.dots ? fmt(a.dotStr / 100, 3) : 0, '--au-tm': dark ? 1 : 1.2,
      '--au-hs': hs + 'px', '--au-hb': hb + 'px', '--au-rw': rw + 'px', '--au-dd': a.dotCell + 'px',
      '--au-sx': fmt(4 * (CW + 2 * hs) / 400, 4), '--au-sy': fmt(4 * (CH + 2 * hs) / 400, 4),
      '--au-rsx': fmt(4 * (CW + 2 * rw) / 400, 4), '--au-rsy': fmt(4 * (CH + 2 * rw) / 400, 4),
    };
    return v;
  }

  // the duration in effect: the slider, else the package's, else (fitted) the one that keeps the reference speed
  beamDur(b) {
    if (b.duration != null) return b.duration;
    return DUR[b.size] * (b.fit ? bbFitDur(b.size, CW, CH, b.radius == null ? CR : b.radius) : 1);
  }

  // the package's resolved props (the component's own fallbacks), and the layer opacity products it writes
  beamEff(b, st) {
    const th = this.beamTheme(b), size = b.size, pr = BBT.pe[size][th];
    const mono = b.color === 'mono', mul = mono && size !== 'line' ? 0.5 : 1;   // the package halves Mono except on Line
    const resp = b.respond ? { rest: 0.55, typing: 0.8, running: 1 }[st] : 1;
    const str = clamp(b.strength, 0, 1) * resp;
    const autoBr = pr.brightness != null ? pr.brightness : 1.3;
    const pulse = size === 'pulse-inner' || size === 'pulse-outside';
    const stat = mono || b.staticColors;
    const r2 = (v) => +(+v).toFixed(2);
    const dark = th === 'dark', gs = b.glowSize;
    const autoDur = DUR[size] * (b.fit ? bbFitDur(size, CW, CH, b.radius == null ? CR : b.radius) : 1);
    return {
      th, size, pr, pulse, stat,
      so: r2(pr.strokeOpacity * mul) * b.strokeX * str, io: r2(pr.innerOpacity * mul) * b.innerX * str, bo: r2(pr.bloomOpacity * mul) * b.bloomX * str,
      br: b.brightness == null ? autoBr : b.brightness, sat: b.saturation == null ? pr.saturation : b.saturation, autoBr, autoSat: pr.saturation,
      hr: size === 'line' ? Math.min(13, b.hueRange) : b.hueRange, r: b.radius == null ? CR : b.radius,
      blur: bbR(8, gs),
      cb: b.coreBlur == null ? bbR(dark ? 3 : 6, gs) : b.coreBlur, bb: b.bloomBlur == null ? bbR(dark ? 22.5 : 15, gs) : b.bloomBlur,
      autoCb: bbR(dark ? 3 : 6, gs), autoBb: bbR(dark ? 22.5 : 15, gs),
      sx: b.glowX == null ? +clamp(CW / 350, 0.35, 4).toFixed(3) : b.glowX, sy: b.glowY == null ? +clamp(CH / 140, 0.35, 4).toFixed(3) : b.glowY,
      dur: b.duration == null ? autoDur : b.duration, autoDur,
    };
  }

  beamVars(b, st) {
    const E = this.beamEff(b, st);
    return {
      '--bb-so': fmt(E.so, 4), '--bb-io': fmt(E.io, 4), '--bb-bo': fmt(E.bo, 4), '--bb-br': fmt(E.br, 3), '--bb-sat': fmt(E.sat, 3),
      '--bb-hb': fmt(b.hueShift, 2), '--bb-hr': fmt(E.hr, 2), '--bb-blur': fmt(E.blur, 3), '--bb-r': E.r + 'px',
      '--bb-cb': fmt(E.cb, 2), '--bb-bb': fmt(E.bb, 2), '--bb-sx': fmt(E.sx, 3), '--bb-sy': fmt(E.sy, 3), '--bb-boost': fmt(b.boost, 3), '--bb-spk': fmt(b.spike, 3),
    };
  }

  // the structural half of the beam (what its stylesheet is generated from); the rest are eased properties
  beamCfg(b) {
    const th = this.beamTheme(b);
    return { size: b.size, color: b.color, th, stat: b.staticColors || b.color === 'mono', fit: !!b.fit, bad: !!b.faithful, W: CW, H: CH };
  }

  // three slots per composer: a structural change goes into the slot faded out longest and cross-fades in
  beamSlots(id, b) {
    const cfg = this.beamCfg(b), key = bbKey(cfg);
    if (!this.bbCss[key]) this.bbCss[key] = bbSheet(cfg);
    let s = this.bbSlots[id];
    if (!s) s = this.bbSlots[id] = { keys: [key, null, null], cfgs: [cfg, null, null], at: [0, -1, -2], on: 0 };
    if (s.keys[s.on] !== key) {
      let j = -1;
      for (let i = 0; i < 3; i++) if (i !== s.on && s.keys[i] === key) j = i;      // coming back to a slot still fading
      if (j < 0) { j = s.on === 0 ? 1 : 0; for (let i = 0; i < 3; i++) if (i !== s.on && s.at[i] < s.at[j]) j = i; }
      s.keys[j] = key; s.cfgs[j] = cfg; s.at[j] = ++this.bbSeq; s.on = j;
    }
    const fitNeed = { r: b.radius == null ? CR : b.radius, th: [], w1: false, wi: false, wi2: false, sig: '' };
    for (const k of s.cfgs) {
      if (!k || !k.fit || (k.size !== 'md' && k.size !== 'sm')) continue;
      fitNeed.w1 = true; if (k.size === 'md') fitNeed.wi = true; else fitNeed.wi2 = true;
      if (fitNeed.th.indexOf(k.th) < 0) fitNeed.th.push(k.th);
    }
    fitNeed.sig = [fitNeed.w1, fitNeed.wi, fitNeed.wi2].join('') + fitNeed.th.join('');
    return {
      slots: s.keys.map((k, i) => ({ cls: 'bbs' + (k ? ' bbk-' + k : '') + (i === s.on ? ' on' : '') })),
      fitNeed: fitNeed.w1 ? fitNeed : null,
    };
  }

  design(id) { return MARROW_DESIGNS.find((d) => d.id === id) || null; }

  // what one composer shows: its light, its settings and its clock
  instance(id, light, st) {
    let aura = this.state.aura, beam = this.state.beam, engine = 'none', key = 'u', d = null;
    if (light === 'aura') engine = 'aura';
    else if (light === 'beam') engine = 'beam';
    else if (light === 'design') {
      d = this.design(this.state.design) || MARROW_DESIGNS[0] || null;
      if (d) {
        // every design shares one clock, so going from one aura-drawn design to another never jumps the angle
        engine = d.engine || 'none'; key = 'd';
        aura = Object.assign({}, AURA_DEFAULTS, d.aura || {});
        if (d.speeds) aura.speed = d.speeds[st];
        beam = Object.assign({}, BEAM_DEFAULTS, d.beam || {});
      }
    }
    // the single preview hands its turn over when it changes clock (unseen strips take the phase, so nothing jumps)
    if (id === 'single' && key !== this.singleKey) {
      const from = this.clk[this.singleKey];
      if (from && !this.state.compare && performance.now() - this.cmpAt > 900) {
        const to = this.clk[key];
        if (to) { to.p = from.p; to.bp = from.bp; to.amp = from.amp; to.rate = from.rate; } else this.clk[key] = Object.assign({}, from, { pi: from.pi.slice(), po: from.po.slice() });
      }
      this.singleKey = key;
    }
    const vars = Object.assign({}, this.auraVars(aura, st), this.beamVars(beam, st));
    if (d && d.state) vars['--mi'] = d.state[st];
    if (d && d.vars) Object.assign(vars, d.vars(this.env(), st));
    const pulse = beam.size === 'pulse-inner' || beam.size === 'pulse-outside';
    const bs = this.beamSlots(id, beam), nowT = performance.now();
    if (!this.prevPulse) { this.prevPulse = {}; this.prevBeam = {}; }
    if (engine === 'beam') this.prevBeam[id] = nowT;
    const beamShown = engine === 'beam' || (this.prevBeam[id] && nowT - this.prevBeam[id] < 900);
    this.instCfg[id] = { key, aura, beam, design: d, rate: d && d.rate ? d.rate : null, pulse: pulse || (this.prevPulse[id] && nowT - this.prevPulse[id] < 900), fitNeed: beamShown ? bs.fitNeed : null };
    if (pulse) this.prevPulse[id] = nowT;
    if (!this.clockRefs[id]) this.clockRefs[id] = (el) => { this.clockEls[id] = el; };
    const cls = 'lopts E-' + engine + (beam.active ? ' A-on' : '') + ' P-' + this.state.plate;
    const dgs = MARROW_DESIGNS.map((x) => {
      const on = d && d.id === x.id;
      const L = x.layers || {};
      return { id: x.id, oc: 'dg' + (on ? ' on' : ''), ic: 'dg' + (on ? ' on' : ''), outer: L.outer || [], inner: L.inner || [] };
    });
    const arc = st === 'running' ? '10.5 44' : '31 44';
    return {
      clockRef: this.clockRefs[id], cls, beamId: d ? 'd-' + d.id : 'u', slots: bs.slots,
      vars: Object.keys(vars).map((k) => k + ': ' + vars[k]).join('; '),
      dgs, arc, cmpCls: 'cmp' + (st === 'typing' ? ' typing' : '') + (st === 'running' ? ' running' : ''),
      design: d,
    };
  }

  beamCode(b) {
    const D = BEAM_DEFAULTS, out = [];
    if (b.size !== 'md') out.push('size="' + b.size + '"');
    if (b.color !== 'colorful') out.push('colorVariant="' + b.color + '"');
    out.push('theme="' + b.theme + '"');
    if (b.duration != null) out.push('duration={' + fmt(b.duration, 2) + '}');
    if (b.strength !== 1) out.push('strength={' + fmt(b.strength, 2) + '}');
    if (!b.active) out.push('active={false}');
    if (b.radius != null) out.push('borderRadius={' + b.radius + '}');
    if (b.brightness != null) out.push('brightness={' + fmt(b.brightness, 2) + '}');
    if (b.saturation != null) out.push('saturation={' + fmt(b.saturation, 2) + '}');
    if (b.glowSize !== 1) out.push('glowSize={' + fmt(b.glowSize, 2) + '}');
    if (b.hueRange !== D.hueRange) out.push('hueRange={' + b.hueRange + '}');
    if (b.staticColors) out.push('staticColors');
    if (b.css) out.push('css={`' + b.css + '`}');
    const st = [];
    if (b.hueShift) st.push("'--beam-hue-base': '" + b.hueShift + "deg'");
    if (b.strokeX !== 1) st.push("'--beam-stroke-opacity': " + fmt(b.strokeX, 2));
    if (b.innerX !== 1) st.push("'--beam-inner-opacity': " + fmt(b.innerX, 2));
    if (b.bloomX !== 1) st.push("'--beam-bloom-opacity': " + fmt(b.bloomX, 2));
    if (b.coreBlur != null) st.push("'--beam-core-blur': '" + fmt(b.coreBlur, 1) + "px'");
    if (b.bloomBlur != null) st.push("'--beam-bloom-blur': '" + fmt(b.bloomBlur, 1) + "px'");
    if (b.glowX != null) st.push("'--pulse-glow-sx': " + fmt(b.glowX, 2));
    if (b.glowY != null) st.push("'--pulse-glow-sy': " + fmt(b.glowY, 2));
    if (b.boost !== 1) st.push("'--pulse-glow-boost': " + fmt(b.boost, 2));
    if (b.spike !== 1) st.push("'--beam-spike-mul': " + fmt(b.spike, 2));
    if (st.length) out.push('style={{ ' + st.join(', ') + ' }}');
    return '<BorderBeam ' + out.join(' ') + '>';
  }

  // ---------- panel rows ----------
  head(t, sub) { return { cls: 'row', isHead: true, t, sub: sub || '' }; }
  note(t, cls) { return { cls: 'row' + (cls ? ' ' + cls : ''), isNote: true, t }; }

  slider(group, key, t, sub, min, max, step, fmtv, o) {
    o = o || {};
    const cfg = this.state[group], raw = cfg[key];
    const isAuto = o.auto !== undefined && raw == null;
    const v = isAuto ? o.auto : raw;
    const pct = clamp((v - min) / (max - min), 0, 1) * 100;
    const id = group + '.' + key;
    const set = (nv) => this.setOpt(group, key, Math.round(clamp(nv, min, max) / step) * step);
    const fromX = (ev) => { const r = ev.currentTarget.getBoundingClientRect(); set(min + clamp((ev.clientX - r.left) / r.width, 0, 1) * (max - min)); };
    return {
      cls: 'row' + (o.na ? ' na' : ''), isSlider: true, t, sub: sub || '', min, max, v: fmt(v, 3), pct: fmt(pct, 3), vt: fmtv(v),
      slCls: 'sl' + (this.state.drag === id ? ' drag' : ''), ticks: (o.ticks || []).map((x) => fmt(clamp((x - min) / (max - min), 0, 1) * 100, 2)),
      down: (ev) => { ev.preventDefault(); try { ev.currentTarget.setPointerCapture(ev.pointerId); } catch (e) {} ev.currentTarget.focus({ preventScroll: true }); this.setState({ drag: id }); fromX(ev); },
      move: (ev) => { if (this.state.drag === id) fromX(ev); },
      up: () => { if (this.state.drag === id) this.setState({ drag: null }); },
      key: (ev) => {
        const big = (max - min) / 10, cur = v;
        const map = { ArrowLeft: -step, ArrowDown: -step, ArrowRight: step, ArrowUp: step, PageDown: -big, PageUp: big };
        if (ev.key in map) { ev.preventDefault(); set(cur + map[ev.key]); }
        else if (ev.key === 'Home') { ev.preventDefault(); set(min); }
        else if (ev.key === 'End') { ev.preventDefault(); set(max); }
      },
      auCls: 'au' + (o.auto === undefined ? ' none' : isAuto ? ' on' : ''), auOn: isAuto, auT: 'Auto',
      auTog: () => { if (o.auto === undefined) return; this.setOpt(group, key, isAuto ? o.auto : null); },
    };
  }

  toggle(group, key, t, sub, tx, o) {
    o = o || {};
    const on = !!this.state[group][key];
    return { cls: 'row' + (o.na ? ' na' : ''), isToggle: true, t, sub: sub || '', tx: tx || '', on, tgCls: 'tg' + (on ? ' on' : ''), tog: () => this.setOpt(group, key, !on) };
  }

  segRow(t, sub, segs, cls) { return { cls: 'row' + (cls ? ' ' + cls : ''), isSeg: true, t, sub: sub || '', segs }; }

  auraRows() {
    const a = this.state.aura;
    const px = (v) => fmt(v, 1) + ' px', pc = (v) => Math.round(v) + '%';
    const scheme = this.seg('Colours', [{ id: 'original', t: 'Original' }, { id: 'maker', t: 'Maker' }, { id: 'accent', t: 'Accent' }, { id: 'custom', t: 'Custom' }], a.scheme, (id) => this.setOpt('aura', 'scheme', id), { sm: true });
    const schemeSub = { original: 'orange and blue', maker: 'the chip\u2019s maker', accent: 'Windows accent', custom: 'your pair' }[a.scheme];
    return [
      { cls: 'row', isBtns: true, btns: [{ t: 'Calm defaults', go: () => this.setOpts('aura', Object.assign({}, AURA_DEFAULTS, { scheme: a.scheme, c1: a.c1, c2: a.c2 })) }, { t: 'Original values', go: () => this.setOpts('aura', AURA_ORIGINAL) }] },
      this.head('Colour'),
      this.segRow('Colours', schemeSub, [Object.assign({ cls: 'sgw on' }, scheme)]),
      { cls: 'row' + (a.scheme === 'custom' ? '' : ' fold'), isColors: true, t: 'Custom pair', sub: 'the two tones', c1: a.c1, c2: a.c2, on1: (ev) => this.setOpt('aura', 'c1', ev.target.value), on2: (ev) => this.setOpt('aura', 'c2', ev.target.value) },
      this.head('Motion'),
      this.slider('aura', 'speed', 'Speed', 'one turn', 3, 40, 0.5, (v) => fmt(v, 1) + ' s', { ticks: [8] }),
      this.toggle('aura', 'breathing', 'Breathing', 'woah', 'scale 1 to .75 and back, 4 s'),
      this.head('Intensity', '100% = the reference pressed'),
      this.slider('aura', 'rest', 'At rest', '', 0, 100, 1, pc, { ticks: [50] }),
      this.slider('aura', 'typing', 'Typing', '', 0, 100, 1, pc),
      this.slider('aura', 'running', 'Running a turn', '', 0, 100, 1, pc),
      this.head('Halo', 'spin-blur'),
      this.slider('aura', 'haloSize', 'Size', 'lit area past the edge', 0, 40, 1, px, { ticks: [0] }),
      this.slider('aura', 'haloBlur', 'Blur', 'spin-blur, 2em = 32', 4, 64, 1, px, { ticks: [32] }),
      this.head('Rim'),
      this.toggle('aura', 'rim', 'Rim light', 'spin-intense, -inside', 'a tight band on the edge'),
      this.slider('aura', 'rimW', 'Width', '', 0.5, 5, 0.25, px, { na: !a.rim, ticks: [2] }),
      this.head('Halftone'),
      this.toggle('aura', 'dots', 'Halftone', 'the dotted backdrop', 'the light seen through a dot screen'),
      this.slider('aura', 'dotCell', 'Density', 'dot spacing', 2, 8, 0.5, px, { na: !a.dots, ticks: [3] }),
      this.slider('aura', 'dotStr', 'Strength', 'screen opacity', 0, 100, 1, pc, { na: !a.dots, ticks: [67] }),
      this.note('From Uiverse.io by SelfMadeSystem (MIT). Its gradient stops and colours, the 8 s eased turn, the 4 s breathing to .75, the 3 px halftone at #111111aa and its .5 / 1 opacities are its exact values (ticks on the sliders); any of them carried into the port needs its notice in THIRD_PARTY_NOTICES.'),
    ];
  }

  beamRows() {
    const b = this.state.beam, E = this.beamEff(b, this.st());
    const pulse = E.pulse, mono = b.color === 'mono';
    const famSeg = this.seg('Family', [{ id: 'rotate', t: 'Rotate' }, { id: 'pulse', t: 'Pulse' }], b.family, (id) => { if (id !== b.family) this.setOpts('beam', { family: id, size: id === 'rotate' ? 'md' : 'pulse-inner' }); }, { sm: true });
    const pickSize = (id) => this.setOpt('beam', 'size', id);
    const rot = this.seg('Type', [{ id: 'md', t: 'Large' }, { id: 'line', t: 'Line' }, { id: 'sm', t: 'Small' }], b.size, pickSize, { sm: true });
    const pul = this.seg('Type', [{ id: 'pulse-inner', t: 'Pulse Inner' }, { id: 'pulse-outside', t: 'Pulse Outside' }], b.size, pickSize, { sm: true });
    const themeSeg = this.seg('Theme', [{ id: 'dark', t: 'Dark' }, { id: 'light', t: 'Light' }, { id: 'auto', t: 'Auto' }], b.theme, (id) => this.setOpt('beam', 'theme', id), { sm: true });
    const fitSeg = this.seg('Geometry', [{ id: 'pkg', t: 'As the package' }, { id: 'fit', t: 'Fitted to the composer' }], b.fit ? 'fit' : 'pkg', (id) => this.setOpt('beam', 'fit', id === 'fit'), { sm: true, fill: 348 });
    const fitSub = b.fit ? 'mapped to the 820 x 52 pill' : 'stock, tuned for a 348 x 122 card';
    const sw = PAL_NAMES.map((n) => { const c = BBT.G[n].border.map((x) => x.color); return { name: PAL_LABEL[n], on: b.color === n, cls: 'swb' + (b.color === n ? ' on' : ''), bg: 'linear-gradient(135deg,' + c[0] + ',' + c[4] + ' 40%,' + c[6] + ' 70%,' + c[8] + ')', pick: () => this.setOpt('beam', 'color', n) }; });
    const x2 = (v) => fmt(v, 2) + 'x', deg = (v) => Math.round(v) + '\u00b0', s = (v) => fmt(v, 2) + ' s', px = (v) => fmt(v, 1) + ' px';
    const po = b.size === 'pulse-outside';
    return [
      { cls: 'row', isBtns: true, btns: [{ t: 'Library defaults', go: () => this.setOpts('beam', Object.assign({}, BEAM_DEFAULTS, { family: b.family, size: b.size, color: b.color, fit: b.fit })) }, { t: 'Calm for the composer', go: () => this.setOpts('beam', BEAM_CALM) }] },
      this.head('Geometry', fitSub),
      { cls: 'row', isWide: true, seg: fitSeg },
      this.head('Playground', 'libraries.dev/beam'),
      this.segRow('Family', '', [Object.assign({ cls: 'sgw on' }, famSeg)]),
      this.segRow('Type', b.family === 'rotate' ? 'Line: the site\u2019s pill pick' : 'size', [Object.assign({ cls: 'sgw' + (b.family === 'rotate' ? ' on' : '') }, rot), Object.assign({ cls: 'sgw' + (b.family === 'pulse' ? ' on' : '') }, pul)]),
      { cls: 'row', isSwatches: true, t: 'Color', sub: this.xf('bbcol', PAL_LABEL[b.color]), sw },
      this.toggle('beam', 'active', 'Active', 'Play / Pause', 'fades in .6 s, out .5 s'),
      this.head('Studio rows', 'locked on the site'),
      this.slider('beam', 'duration', 'Duration', pulse ? 'breathing speed' : b.size === 'line' ? 'one travel' : 'one turn', 0.5, 14, 0.02, s, { auto: E.autoDur, ticks: [DUR[b.size]] }),
      this.slider('beam', 'radius', 'Corner radius', 'auto: the composer', 0, 32, 1, (v) => v + ' px', { auto: CR, ticks: [16] }),
      this.slider('beam', 'brightness', 'Brightness', '', 0.2, 3, 0.05, x2, { auto: E.autoBr, ticks: [E.autoBr] }),
      this.slider('beam', 'hueShift', 'Hue shift', '--beam-hue-base', -180, 180, 1, deg, { ticks: [0] }),
      this.head('Package props'),
      this.segRow('Theme', 'package default: dark', [Object.assign({ cls: 'sgw on' }, themeSeg)]),
      this.slider('beam', 'strength', 'Strength', 'glow layers', 0, 1, 0.01, (v) => fmt(v, 2), { ticks: [1] }),
      this.slider('beam', 'saturation', 'Saturation', '', 0, 2.5, 0.05, x2, { auto: E.autoSat, ticks: [E.autoSat] }),
      this.slider('beam', 'glowSize', 'Glow size', 'every blur radius', 0.25, 3, 0.05, x2, { ticks: [1] }),
      this.slider('beam', 'hueRange', 'Hue range', pulse ? 'pulse turns all 360' : b.size === 'line' ? 'line caps at 13' : 'swing over 12 s', 0, 180, 1, (v) => '\u00b1' + Math.round(b.size === 'line' ? Math.min(13, v) : v) + '\u00b0', { na: pulse || mono || b.staticColors, ticks: [30] }),
      this.toggle('beam', 'staticColors', 'Static colours', mono ? 'always on for Mono' : '', 'no hue animation', { na: mono }),
      this.head('CSS hooks'),
      this.slider('beam', 'strokeX', 'Stroke', '--beam-stroke-opacity', 0, 2, 0.05, x2, { ticks: [1] }),
      this.slider('beam', 'innerX', 'Inner', '--beam-inner-opacity', 0, 2, 0.05, x2, { ticks: [1] }),
      this.slider('beam', 'bloomX', 'Bloom', '--beam-bloom-opacity', 0, 2, 0.05, x2, { ticks: [1] }),
      this.slider('beam', 'coreBlur', 'Core blur', 'Pulse Outside', 0, 20, 0.5, px, { auto: E.autoCb, na: !po }),
      this.slider('beam', 'bloomBlur', 'Bloom blur', 'Pulse Outside', 0, 60, 0.5, px, { auto: E.autoBb, na: !po }),
      this.slider('beam', 'glowX', 'Glow X', '--pulse-glow-sx', 0.35, 4, 0.01, x2, { auto: +clamp(CW / 350, 0.35, 4).toFixed(3), na: !po }),
      this.slider('beam', 'glowY', 'Glow Y', '--pulse-glow-sy', 0.35, 4, 0.01, x2, { auto: +clamp(CH / 140, 0.35, 4).toFixed(3), na: !po }),
      this.slider('beam', 'boost', 'Glow boost', '--pulse-glow-boost', 0.5, 2, 0.01, x2, { ticks: [1], na: !po }),
      this.slider('beam', 'spike', 'Spikes', '--beam-spike-mul, Line', 0, 3, 0.05, x2, { ticks: [1], na: b.size !== 'line' }),
      { cls: 'row', isText: true, t: 'Extra CSS', sub: 'css, {id} = this beam', v: b.css, ph: '[data-beam="{id}"] .bbh{...}', on: (ev) => this.setOpt('beam', 'css', ev.target.value) },
      this.toggle('beam', 'faithful', 'As 1.4.1 ships', 'a known defect', 'Forest, Candy, Ice and Gold lose Small\u2019s inner glow and Line\u2019s inner glow and bloom (double-comma colours)'),
      this.head('For the composer', 'not in the package'),
      this.toggle('beam', 'respond', 'Follow the state', 'Marrow addition', 'strength x.55 at rest, x.8 typing, x1 running'),
      { cls: 'row', isCode: true, code: this.xf('bbcode', this.beamCode(b)) },
      this.note('border-beam 1.4.1 by Jakub Antalik (MIT). As the package: its stylesheet generator is ported with its tables, so every layer, stop, colour, opacity, blur and curve is its own (checked pixel against pixel with the real package on the site\u2019s 348 x 122 card); only the clock is the playground\u2019s. Fitted to the composer keeps those values and maps the geometry to the pill: blobs edge by edge, windows that travel the perimeter evenly, the inner wash keyed to the nearest edge, Line scaled from the site\u2019s search bar, and an automatic duration that keeps each type\u2019s speed. Carrying any of it needs its notice in THIRD_PARTY_NOTICES. Studio (paid) was not used.'),
    ];
  }

  designRows() {
    if (!MARROW_DESIGNS.length) return [{ cls: 'row', isEmpty: true, t: 'No Marrow designs yet', sub: 'The composer lights drawn for Marrow arrive here as they are made, and fill the fourth slot of the comparison.' }];
    const S = this.state, dark = this.dark(), m = this.model(), x = this.accent();
    const modelSeg = this.seg('Model', MODELS.map((o) => ({ id: o.id, t: o.name })), S.model, (id) => this.setState({ model: id }), { sm: true });
    const sw = ACCENTS.map((o) => ({ name: o[1], on: S.accent === o[0], cls: 'swb' + (S.accent === o[0] ? ' on' : ''), bg: dark ? o[2] : o[3], pick: () => this.setState({ accent: o[0] }) }));
    const row = (d) => ({ cls: 'row', isDesign: true, tag: d.tag || '', t: d.name, sub: d.note || '', on: S.design === d.id, dCls: 'drow' + (S.design === d.id ? ' on' : ''), pick: () => this.setState({ design: d.id }) });
    return [
      this.head('Preview', 'what the designs read'),
      this.segRow('Model', 'in the chip', [Object.assign({ cls: 'sgw on' }, modelSeg)]),
      { cls: 'row sw11', isSwatches: true, t: 'Accent', sub: this.xf('daccent', x[1]), sw },
      this.head('Ambient', 'always there, quiet'),
      ...MARROW_DESIGNS.filter((d) => d.group !== 'responsive').map(row),
      this.head('Responsive', 'they answer the composer'),
      ...MARROW_DESIGNS.filter((d) => d.group === 'responsive').map(row),
      this.note('Every design runs on the preview\u2019s state (rest, typing, running a turn), theme, model and accent, and on the same clock as the other lights; each change eases over .9 to 1.2 s. The chip in the composer also switches the model.'),
    ];
  }

  // the model chip: the name crossfades and its box eases to the new width; the mark crossfades with it
  chipVals() {
    const m = this.model(), dark = this.dark();
    return {
      name: this.xf('chipName', m.name), w: fmt(this.tw(m.name, 14, 400, CFONT), 2),
      claudeCls: 'mk' + (m.claude ? '' : ' off'), dotCls: 'mdot' + (m.claude ? ' off' : ''), dot: (dark ? m.d : m.l)[0],
      aria: 'Model: ' + m.name + ' (' + m.maker + '). Click to switch the preview\u2019s model',
    };
  }

  renderVals() {
    if (this.props.dark !== undefined && (this.props.dark !== false) !== this.propDark) { this.propDark = this.props.dark !== false; this.state.dark = this.propDark; }
    const S = this.state, st = this.st(), dark = this.dark();
    this.instCfg = {};
    const lightName = { aura: 'Uiverse aura', beam: 'Border beam', design: 'Marrow designs', off: 'Off' };
    const d = this.design(S.design) || MARROW_DESIGNS[0] || null;
    const b = S.beam;
    const beamSub = TYPE_LABEL[b.size] + ', ' + PAL_LABEL[b.color] + ', ' + fmt(this.beamEff(b, st).dur, 2) + ' s' + (b.fit ? ', fitted' : ', as the package') + (b.active ? '' : ', paused');
    const auraSub = { original: 'orange and blue', maker: 'maker colour', accent: 'accent', custom: 'custom pair' }[S.aura.scheme] + ', ' + fmt(S.aura.speed, 1) + ' s a turn';
    const capLight = S.light === 'design' ? (d ? d.tag + ' ' + d.name : 'Marrow designs') : lightName[S.light];
    const one = this.instance('single', S.light, st);
    const stripDefs = [
      { id: 's1', light: 'aura', title: 'Uiverse aura', sub: auraSub },
      { id: 's2', light: 'beam', title: 'Border beam', sub: beamSub },
      { id: 's3', light: 'off', title: 'Off', sub: 'the composer as the port draws it today' },
      { id: 's4', light: 'design', title: 'Marrow design', sub: d ? d.tag + ' ' + d.name + ', pick another in the panel' : 'empty slot, waiting for the designs' },
    ];
    const strips = stripDefs.map((x, n) => Object.assign(this.instance(x.id, x.light, st), { n, num: String(n + 1), title: x.title, sub: this.xf('strip' + n, x.sub) }));
    const lightSeg = this.seg('Light', [{ id: 'aura', t: 'Uiverse aura' }, { id: 'beam', t: 'Border beam' }, { id: 'design', t: 'Marrow designs' }, { id: 'off', t: 'Off' }], S.light, (id) => this.setState({ light: id }), { fill: 348 });
    const plateSeg = this.seg('Composer', [{ id: 'port', t: 'As the port' }, { id: 'glass', t: 'Glass plate' }], S.plate, (id) => this.setState({ plate: id }), { sm: true });
    const stateSeg = this.seg('State', [{ id: 'rest', t: 'Rest' }, { id: 'typing', t: 'Typing' }, { id: 'running', t: 'Running a turn' }, { id: 'auto', t: 'Auto' }], S.auto ? 'auto' : S.mode, (id) => this.setMode(id));
    const themeSeg = this.seg('Appearance', [{ id: 'dark', t: 'Dark' }, { id: 'light', t: 'Light' }], dark ? 'dark' : 'light', (id) => this.setState({ dark: id === 'dark' }));
    const panes = [
      { cls: 'pane' + (S.light === 'aura' ? ' on' : ''), rows: this.auraRows() },
      { cls: 'pane' + (S.light === 'beam' ? ' on' : ''), rows: this.beamRows() },
      { cls: 'pane' + (S.light === 'design' ? ' on' : ''), rows: this.designRows() },
      { cls: 'pane' + (S.light === 'off' ? ' on' : ''), rows: [this.note('No light: the composer exactly as the port draws it today, for comparison. Nothing is drawn and no frame is requested.')] },
    ];
    const notes = [
      { key: 'aura', t: 'Uiverse aura in the port', cost: '~0.05 ms GPU', ps: [
        'Baked once per composer size: the halo\'s soft rounded-rect alpha (the blur folded in, A8, ~900 x 130 px) and the rim\'s ring. The light itself is no texture: one SkSL shader takes the reference\'s gradient along an axis turned by the angle (a dot product), times the baked alpha, the halftone (fract of the pixel against the dot radius) and the state intensity.',
        'One halo draw and one rim draw; per frame only uniforms change (angle, breathing scale, intensity, colours). No blur at run time: ~120k px x ~20 ALU, 0.03 to 0.06 ms GPU at 1x, ~0.1 ms at 2x, CPU ~2 us. Paused or reduce motion: one still, no frames.',
      ] },
      { key: 'beam', t: 'Border beam in the port', cost: '0.05 to 0.2 ms GPU', ps: [
        'Rotate and Line: the blobs never move, so one RGBA image per palette, theme and size holds the stroke, the 28 px inner band and the pre-blurred 8 px bloom. Each frame one shader samples it under the conic window (atan2 about the centre minus the angle) or Line\'s moving ellipse, with hue, saturation and brightness folded into one colour matrix.',
        'Pulse: the blobs drift, so the shader sums 9 to 16 Gaussian ellipses (a wider Gaussian is Pulse Outside\'s 22.5 px bloom); 17 cosines on the CPU. Rotate ~0.05 ms, pulse 0.1 to 0.2 ms GPU at 2x. The package\'s per-layer CSS blurs and its 30 fps JavaScript loop are what the port avoids.',
      ] },
      { key: 'design', t: 'Marrow designs in the port', cost: d ? d.tag + ' ' + d.cost : 'budget 0.2 ms', ps: [
        (d ? d.tag + ' ' + d.name + ': ' + d.port : 'Each design states its own draw plan.'),
        'The rule for all: masks and textures baked once per size or palette change, one or two shader draws a frame, uniforms only, no run-time blur, under 0.2 ms GPU at 2x, frames only while something moves (ED-237), one still under reduce motion. Off draws nothing.',
      ] },
    ].map((c, n) => ({ cls: 'nc' + (S.light === c.key || (c.key === 'design' && S.light === 'off') ? ' on' : ''), t: c.t, cost: this.xf('ncost' + n, c.cost), ps: c.ps.map((x, i) => this.xf('np' + n + '-' + i, x)) }));
    // the beam stylesheets of every slot in use (generated once per structural key)
    const keys = [];
    for (const id in this.bbSlots) for (const k of this.bbSlots[id].keys) if (k && keys.indexOf(k) < 0) keys.push(k);
    keys.sort();
    const designCss = MARROW_DESIGNS.map((x) => x.css || '').join('\n') + '\n' + keys.map((k) => this.bbCss[k]).join('\n') + '\n' + (b.css ? b.css.split('{id}').join('u') : '');
    return {
      boardCls: 'board' + (dark ? '' : ' light') + (S.paused ? ' paused' : ''),
      intro: 'One playground for the ambient light around the composer: the Uiverse aura, Border beam with every option its playground and package offer, and the Marrow designs as they are drawn, on the real composer at rest, typing and running a turn. Every change is transitioned; Compare shows them side by side on the same state.',
      stateSeg, themeSeg, lightSeg, plateSeg,
      paused: S.paused, pauseTog: () => this.setState({ paused: !S.paused }), pauseIc: S.paused ? 'off' : '', playIc: S.paused ? '' : 'off', pauseLab: this.xf('pause', S.paused ? 'Play' : 'Pause'),
      compare: S.compare, cmpBtnCls: 'tbtn' + (S.compare ? ' on' : ''), compareTog: () => { this.cmpAt = performance.now(); this.setState({ compare: !S.compare }); },
      capLight: this.xf('capLight', capLight), capState: this.xf('capState', STATE_LABEL[st] + (S.auto ? ' (auto)' : '')),
      lightSub: this.xf('lightSub', S.light === 'aura' ? 'after SelfMadeSystem, Uiverse.io' : S.light === 'beam' ? (S.beam.fit ? 'border-beam 1.4.1, fitted to the composer' : 'border-beam 1.4.1, as the package') : S.light === 'design' ? 'the presets registry' : 'nothing drawn'),
      singleCls: 'single' + (S.compare ? ' off' : ''), gridCls: 'grid' + (S.compare ? ' on' : ''),
      single: [one], strips, panes, notes,
      typed: TYPED.slice(0, S.typedN), designCss,
      mdl: this.chipVals(), chipTap: () => { const i = MODELS.findIndex((o) => o.id === S.model); this.setState({ model: MODELS[(i + 1) % MODELS.length].id }); },
    };
  }
}
