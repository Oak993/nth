// ---------- Settings > Customization (OD-0085, OD-0089): the page as an interactive review board ----------
// The composer light is the playground's own (beam.js and logic.js above, unchanged but for the composer's width):
// this file only subclasses its Component to draw the Settings window around it. Three layouts of the same page
// share one state, so a setting made in one shows in the others.

const CZ_VARIANTS = [
  { id: 'A', t: 'Pinned preview' },
  { id: 'B', t: 'Stage' },
  { id: 'C', t: 'Compact' },
];
const CZ_NOTES = {
  A: ['The preview stays pinned at the top of the page, the style switch above it and the model and state switches under it, so every change is seen while the settings scroll beneath. The style’s settings follow in three short groups, the expert ones folded under Advanced.',
    'Best when a user tunes many settings in one go: nothing they change ever leaves the screen.'],
  B: ['The stage shows the end of a conversation above the composer, so the light is judged in context; the style is picked from three cards, and the state switch sits in the window\u2019s top bar like the other pages\u2019 controls.',
    'The stage stays pinned while every composer setting scrolls under it (style, look, pace, Advanced, the voice orb, After voice input, the working dots). As the page scrolls, the conversation folds away over the first 100 px, so the settings get the room; the stage lets go and scrolls away with the page at Speech, the first section that is not about the composer. Focusing a control with Tab keeps it clear of the stage.'],
  C: ['One card holds everything: the preview, the style, and the four or five settings most people want. Everything else waits under Advanced, which folds open in place.',
    'The calmest page; the whole essential set fits the window without scrolling.'],
};
// the sidebar, in the source's order (SettingsView.swift :4-15) with Customization after Appearance
const CZ_SIDE = [
  ['General', 'M7 4.6a2.4 2.4 0 1 1 0 4.8a2.4 2.4 0 1 1 0-4.8ZM7 1.2v1.6M7 11.2v1.6M1.2 7h1.6M11.2 7h1.6M2.9 2.9l1.1 1.1M10 10l1.1 1.1M2.9 11.1l1.1-1.1M10 4l1.1-1.1'],
  ['Hydra', 'M2 12c.6-3.6 2.4-6 5-6s4.4 2.4 5 6M4.5 7.2 3 3.5M9.5 7.2 11 3.5M7 6V2.2'],
  ['Appearance', 'M7 1.5a5.5 5.5 0 1 0 0 11c.9 0 1.3-.6 1.1-1.3-.3-1 .3-1.8 1.3-1.8h1.4a1.8 1.8 0 0 0 1.7-1.9A5.5 5.5 0 0 0 7 1.5ZM4.3 6.2v.1M6.4 4.2v.1M9.2 4.6v.1'],
  ['Customization', 'M5.6 1.6c.4 2.5 1.4 3.5 3.9 3.9-2.5.4-3.5 1.4-3.9 3.9-.4-2.5-1.4-3.5-3.9-3.9 2.5-.4 3.5-1.4 3.9-3.9ZM10.6 8.3c.2 1.3.7 1.8 2 2-1.3.2-1.8.7-2 2-.2-1.3-.7-1.8-2-2 1.3-.2 1.8-.7 2-2Z'],
  ['Providers', 'M4 4h6v6H4zM5.6 1.6v2.4M8.4 1.6v2.4M5.6 10v2.4M8.4 10v2.4M1.6 5.6H4M1.6 8.4H4M10 5.6h2.4M10 8.4h2.4'],
  ['MCP', 'M2.5 2.5h9v9h-9zM5.2 5.2h3.6v3.6H5.2z'],
  ['Models', 'M2 4h10M2 7h10M2 10h10M5 2.8v2.4M9.4 5.8v2.4M6 8.8v2.4'],
  ['Source control', 'M4 2v10M10 5.4a1.6 1.6 0 1 0 0-3.2a1.6 1.6 0 1 0 0 3.2ZM10 5.4c0 3.4-6 2-6 5.4'],
  ['Shortcuts', 'M5 5V3.6A1.4 1.4 0 1 0 3.6 5H5Zm0 0h4M5 5v4m4-4V3.6A1.4 1.4 0 1 1 10.4 5H9Zm0 0v4m0 0h1.4A1.4 1.4 0 1 1 9 10.4V9Zm0 0H5m0 0v1.4A1.4 1.4 0 1 1 3.6 9H5Z'],
  ['Archive', 'M1.6 3h10.8v2.6H1.6zM2.6 5.6V12h8.8V5.6M5.6 8h2.8'],
  ['About', 'M7 1.5a5.5 5.5 0 1 0 0 11a5.5 5.5 0 1 0 0-11ZM7 6.3V10M7 4.2v.2'],
];
const CZ_PAL = { colorful: 'Colourful', mono: 'Mono', ocean: 'Ocean', sunset: 'Sunset', forest: 'Forest', candy: 'Candy', ice: 'Ice', gold: 'Gold' };
const CZ_STYLE = { beam: 'Border beam', aura: 'Uiverse light', off: 'Off' };
const CZ_STYLE_D = { beam: 'A beam of colour travels the edge', aura: 'A soft two-tone glow turns around it', off: 'No light around the composer' };
const CZ_SHAPE_D = { md: 'A wide beam with an inner glow', line: 'A short streak glides along the edge', sm: 'A slimmer beam, a tighter glow', 'pulse-inner': 'Colour pools just inside the edge', 'pulse-outside': 'A glow spills out past the edge' };
const CZ_SCHEME_D = { original: 'Orange and blue', maker: 'The colour of the model in use', accent: 'Your accent colour', custom: 'Two colours you pick' };
const CZ_BEAM_KEYS = Object.keys(BEAM_DEFAULTS).filter((k) => k !== 'fit' && k !== 'css' && k !== 'faithful' && k !== 'active');
const CZ_AURA_KEYS = Object.keys(AURA_DEFAULTS);
const CZ_SAVE = 'marrow-customization-review';
// ---------- the voice orb (OD-0091): Siri Wave by default, Voice Membrane as the alternative ----------
// The WebGL ports of the two Liquid Orb Editor presets (E:/Droppy/work/voice/design/voice-orbs.js, the API of
// knob-orbs-1.js) were not there when this board was made: until window.__cz.useOrbs(registry) is called with that
// registry, each orb is a clearly marked Canvas 2D placeholder with the same states (rest, hover, listening, processing).
const CZ_ORBS = [{ id: 'siri', name: 'Siri Wave', rid: 'siri-wave', glow: '#8A6CFF' }, { id: 'membrane', name: 'Voice Membrane', rid: 'voice-membrane', glow: '#3FD3C2' }];
const CZ_VTEXT = 'Make the beam a little slower on the light theme';
function czPlaceholderOrb(canvas, kind) {
  const g = canvas.getContext('2d');
  let px = 0, dpr = 1, ph = 0, last = null;
  const resize = (p) => { px = p; dpr = Math.min(3, window.devicePixelRatio || 1); canvas.width = Math.round(p * dpr); canvas.height = Math.round(p * dpr); };
  const frame = (t, st) => {
    if (!px) resize(canvas.clientWidth || 20);
    const dt = last == null ? 0 : Math.min(0.1, Math.max(0, t - last)); last = t;
    const ch = st.reduceMotion ? 0 : st.charge || 0;
    ph += dt * (0.35 + 2.2 * ch) * (st.reduceMotion ? 0 : 1);
    const W = canvas.width, r = W / 2;
    g.setTransform(1, 0, 0, 1, 0, 0); g.clearRect(0, 0, W, W);
    g.save(); g.beginPath(); g.arc(r, r, r - 0.5 * dpr, 0, TAU); g.clip();
    if (kind === 'siri') {
      g.fillStyle = '#0A0B1E'; g.fillRect(0, 0, W, W);
      g.globalCompositeOperation = 'lighter';
      [['255,72,196', 0, 0.9], ['120,96,255', 2.1, -1.2], ['48,214,255', 4.2, 1.5]].forEach(([c, o, k]) => {
        const a = ph * k + o, x = r + Math.cos(a) * r * 0.36, y = r + Math.sin(a * 1.3) * r * 0.3;
        const gr = g.createRadialGradient(x, y, 0, x, y, r * (0.78 + 0.12 * Math.sin(ph * 1.7 + o)));
        gr.addColorStop(0, 'rgba(' + c + ',.95)'); gr.addColorStop(1, 'rgba(' + c + ',0)');
        g.fillStyle = gr; g.fillRect(0, 0, W, W);
      });
    } else {
      const bg = g.createRadialGradient(r * 0.8, r * 0.7, 0, r, r, r);
      bg.addColorStop(0, '#2CE0C8'); bg.addColorStop(0.55, '#1C6F9E'); bg.addColorStop(1, '#101A3A');
      g.fillStyle = bg; g.fillRect(0, 0, W, W);
      g.globalCompositeOperation = 'lighter';
      g.lineWidth = Math.max(1, r * 0.09);
      for (let i = 0; i < 3; i++) {
        g.beginPath();
        for (let s = 0; s <= 48; s++) {
          const a = s / 48 * TAU, rr = r * (0.34 + i * 0.18) * (1 + 0.08 * Math.sin(a * 3 + ph * (1.4 + i * 0.5)) + 0.05 * Math.sin(a * 5 - ph * 2));
          const x = r + Math.cos(a) * rr, y = r + Math.sin(a) * rr;
          if (s) g.lineTo(x, y); else g.moveTo(x, y);
        }
        g.strokeStyle = 'rgba(190,255,245,' + (0.32 - i * 0.07) + ')'; g.stroke();
      }
    }
    g.globalCompositeOperation = 'source-over';
    const hl = g.createRadialGradient(r * 0.62, r * 0.5, 0, r * 0.62, r * 0.5, r * 0.62);
    hl.addColorStop(0, 'rgba(255,255,255,.55)'); hl.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = hl; g.fillRect(0, 0, W, W);
    g.restore();
    g.beginPath(); g.arc(r, r, r - 0.75 * dpr, 0, TAU); g.lineWidth = dpr; g.strokeStyle = 'rgba(255,255,255,.35)'; g.stroke();
  };
  return { frame, resize, dispose() {}, setDark() {}, placeholder: true };
}
// the makers' colours (E:/Droppy/work/design/orb/liquid-palettes.json, effort High): [key, name, model, deep, mid, light,
// highlight, title on dark, title on light]; the first six in the knob gallery's order, then the rest as listed there
const CZ_MAKERS = /*@MAKERS@*/null;
// the locked accent palette (OD-0088, accent-palette-locked.json): [id, name, group, dark hex, light hex]
const CZ_ACC = /*@LOCKACC@*/null;
const UIF = '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';
// a CSS cubic-bezier timing function: y at progress x
function czBez(x1, y1, x2, y2, x) {
  const bx = (t) => 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t;
  const by = (t) => 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
  let lo = 0, hi = 1, t = x;
  for (let i = 0; i < 22; i++) { t = (lo + hi) / 2; if (bx(t) < x) lo = t; else hi = t; }
  return by(t);
}
const czLum = (h) => { const c = hex2(h).map((v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }); return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]; };
// a maker colour that would vanish on the window is eased toward the maker's own title colour (else white or ink)
// until it reads: lighter than .12 luminance on the dark window, darker than .42 on the light one
function czTone(h, dark, title) {
  const ok = (c) => (dark ? czLum(c) >= 0.12 : czLum(c) <= 0.42);
  if (ok(h)) return h;
  const to = ok(title) ? title : dark ? '#FFFFFF' : '#1C1C1E';
  for (let t = 0.1; t < 1; t += 0.05) { const c = mixHex(h, to, t); if (ok(c)) return c; }
  return to;
}

class CzComponent extends Component {
  constructor(props) {
    super(props);
    let saved = {};
    try { saved = JSON.parse(window.localStorage.getItem(CZ_SAVE) || '{}') || {}; } catch (e) { saved = {}; }
    Object.assign(this.state, {
      light: 'beam', mode: 'rest', compare: true, adv: false,
      variant: 'B',   // layout B, Stage, locked by the owner (2026-10-03)
      wd: 'accent', wdc: 'blue',
      maker: CZ_MAKERS.some((m) => m[0] === saved.maker) ? saved.maker : 'Claude', menu: null,
      orb: 'siri', afterVoice: 'leave', vmode: 'idle', vN: 0, orbHov: false,
      sc: { A: false, B: false, C: false },
    });
    if (saved.theme === 'dark' || saved.theme === 'light') this.state.dark = saved.theme === 'dark';
    this.allEls = {}; this.czRefs = {}; this.varAt = {};
    this.orbEls = {}; this.orbRefs = {}; this.orbReg = Array.isArray(window.__czOrbs) ? window.__czOrbs : null;   // the real orbs, when the module loaded them before the first render
    this.vt = 0; this.vlev = 0;
  }

  componentDidMount() {
    super.componentDidMount();
    try { window.__cz = this; } catch (e) {}
    document.addEventListener('pointerdown', (ev) => { if (this.state.menu && !(ev.target.closest && ev.target.closest('.czms'))) this.setMenu(null); }, true);
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => { this.pillReset = true; this.forceUpdate(); });
  }

  componentDidUpdate() {
    this.placePills();
    if (this.menuFocus) {
      this.menuFocus = false;
      const pop = document.querySelector('.czms.open .czpop'), it = pop && pop.querySelector('.pitem[aria-checked="true"]');
      const host = pop && pop.closest('.czscroll'), chip = pop && pop.parentNode.querySelector('.czmc'), list = pop && pop.querySelector('.czplist');
      if (host && chip && list) { const room = chip.getBoundingClientRect().top - host.getBoundingClientRect().top - 46 - 44; /* clear of the window's top bar */ list.style.maxHeight = Math.round(clamp(room, 112, 236)) + 'px'; }
      if (it) { const l = it.parentNode; l.scrollTop = Math.max(0, it.offsetTop - (l.clientHeight - it.offsetHeight) / 2); it.focus({ preventScroll: true }); }
    }
  }

  // ---------- the preview's model: every maker of liquid-palettes.json, its colours toned for the window ----------
  maker(k) { return CZ_MAKERS.find((m) => m[0] === (k || this.state.maker)) || CZ_MAKERS[0]; }
  model() {
    const M = this.maker();
    const d = [czTone(M[3], true, M[7]), czTone(M[5], true, M[7])], l = [czTone(M[3], false, M[8]), czTone(M[5], false, M[8])];
    return { id: M[0], name: M[1], maker: M[1], claude: M[0] === 'Claude', d: [d[0], d[1], d[1]], l: [l[0], l[1], l[1]] };
  }
  pickMaker(k) { if (k !== this.state.maker) { this.setState({ maker: k }); this.save(); } }
  stepMaker(n) { const i = CZ_MAKERS.findIndex((m) => m[0] === this.state.maker), L = CZ_MAKERS.length; this.pickMaker(CZ_MAKERS[(i + n + L) % L][0]); }
  setMenu(v) { if (v !== this.state.menu) { this.menuFocus = !!v; this.setState({ menu: v }); } }

  modelSwitch(vid) {
    const M = this.maker(), dark = this.dark(), open = this.state.menu === vid;
    const dot = (m) => czTone(m[3], dark, dark ? m[7] : m[8]);   // the chip's dot: the maker's deep stop, toned
    return {
      cls: 'czms' + (open ? ' open' : ''), open: open ? 'true' : 'false', popc: 'czpop' + (open ? ' open' : ''),
      lab: this.xf('mslab', M[1]), w: fmt(this.tw(M[1], 13, 400, UIF) + 1, 2), dot: dot(M),
      aria: 'Preview model: ' + M[1] + (M[2] ? ' (' + M[2] + ')' : ''),
      toggle: () => this.setMenu(open ? null : vid),
      prev: () => this.stepMaker(-1), next: () => this.stepMaker(1),
      key: (ev) => {
        if (ev.key === 'Escape' && this.state.menu) { ev.preventDefault(); this.setMenu(null); const c = ev.currentTarget.querySelector('.czmc'); if (c) c.focus(); return; }
        if (!open) return;
        const items = Array.from(ev.currentTarget.querySelectorAll('.pitem')), i = items.indexOf(document.activeElement);
        const go = { ArrowDown: i + 1, ArrowUp: i - 1, Home: 0, End: items.length - 1 }[ev.key];
        if (go === undefined) return;
        ev.preventDefault();
        const t = items[(go + items.length) % items.length];
        if (t) t.focus();
      },
      items: CZ_MAKERS.map((m) => ({
        name: m[1], model: m[2], dot: dot(m), on: m[0] === M[0] ? 'true' : 'false', chk: 'pcheck' + (m[0] === M[0] ? ' on' : ''),
        pick: () => { this.pickMaker(m[0]); this.setMenu(null); const c = document.querySelector('.czpage.on .czmc'); if (c) c.focus({ preventScroll: true }); },
      })),
    };
  }

  save() {
    try { window.localStorage.setItem(CZ_SAVE, JSON.stringify({ variant: this.state.variant, theme: this.state.dark ? 'dark' : 'light', maker: this.state.maker })); } catch (e) {}
  }

  // only the shown layout's composer (and the one fading out) is ticked; all three share one clock, so a switch never jumps
  tick(now) {
    const cur = this.state.variant, vis = {};
    for (const id in this.allEls) {
      const vid = id.slice(1);
      if ((vid === cur && this.state.page !== 'voice') || (this.varAt[vid] && now - this.varAt[vid] < 700)) vis[id] = this.allEls[id];
    }
    this.clockEls = vis;
    const dt = this.lastV == null ? 0 : Math.min(0.05, Math.max(0, (now - this.lastV) / 1000));
    this.lastV = now;
    this.voiceTick(dt);
    Component.prototype.tick.call(this, now);
    this.orbTick(now, dt, vis);
    this.stageTick(dt);
    this.dotsTick(now, vis);
  }

  // ComposerWorkingDots.swift as the playground draws it: 2 px dots on an 8 px pitch from 3 px in, up to 3 rows, each
  // row higher a third fainter; each dot pulses .08 -> .75 at 35 % (ease-out) -> .08 (ease-in) every 2.2 s, delayed
  // .045 s per column and .09 s per row; the box fades over .4 s with the run (CSS on .wdots). Drawn only while seen.
  dotsTick(now, vis) {
    const S = this.state, run = this.st() === 'running';
    if (run) this.dotsSeen = now;
    if (!run && !(this.dotsSeen && now - this.dotsSeen < 600)) return;
    const reduce = !!(this.mq && this.mq.matches), t = now / 1000;
    for (const id in vis) {
      const cv = this.dotEls && this.dotEls[id.slice(1)];
      if (!cv || !cv.isConnected) continue;
      const W = cv.clientWidth, H = cv.clientHeight, dpr = Math.min(3, window.devicePixelRatio || 1);
      if (!W || !H) continue;
      if (cv.width !== Math.round(W * dpr) || cv.height !== Math.round(H * dpr)) { cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr); }
      const g = cv.getContext('2d');
      g.setTransform(dpr, 0, 0, dpr, 0, 0); g.clearRect(0, 0, W, H);
      g.fillStyle = getComputedStyle(cv.closest('.pvw')).getPropertyValue('--wdc').trim() || '#4B9FFC';
      const cols = Math.ceil(W / 8) + 1;
      for (let r = 0; r < 3; r++) {
        const y = H - (3 + r * 8) - 1;
        if (y - 1 < 0) break;
        const ro = 1 - r / 3;
        for (let c = 0; c < cols; c++) {
          let a;
          if (reduce) a = 0.28;
          else {
            const u = (((t - (c * 0.045 + r * 0.09)) % 2.2) + 2.2) % 2.2 / 2.2;
            a = u < 0.35 ? 0.08 + 0.67 * czBez(0, 0, 0.58, 1, u / 0.35) : 0.75 - 0.67 * czBez(0.42, 0, 1, 1, (u - 0.35) / 0.65);
          }
          g.globalAlpha = a * ro;
          g.beginPath(); g.arc(3 + c * 8 + 1, y, 1, 0, TAU); g.fill();
        }
      }
      g.globalAlpha = 1;
    }
  }

  // the stage compacts as the page scrolls: the conversation above the composer folds away over the first 100 px,
  // the stage's room given back below it, so the settings arrive under it at the pace of the scroll
  stageTick(dt) {
    const el = this.scrollB;
    if (!el || !el.isConnected) { this.col = 0; return; }
    const pin = el.querySelector('.czpin');
    if (!pin) return;
    const reduce = !!(this.mq && this.mq.matches), to = Math.min(92, Math.max(0, el.scrollTop));   // 212 -> 120 px: room above the composer for the floating pill
    let c = this.col == null ? to : this.col;
    // a scroll by wheel, touchpad or drag moves a few px a frame: the stage follows it exactly; a jump (a focus
    // scroll, Home, End) is eased instead, so it never cuts
    c = reduce || Math.abs(to - c) <= 72 ? to : c + (to - c) * (1 - Math.exp(-dt / 0.06));
    const room = pin.nextElementSibling;
    if (c !== this.col || pin.__col !== c) {
      this.col = c; pin.__col = c;
      pin.style.setProperty('--col', c.toFixed(2) + 'px'); pin.style.setProperty('--colk', (c / 92).toFixed(4));
      if (room) room.style.marginTop = c.toFixed(2) + 'px';   // the room the stage gives back, below it (so it lets go at Speech, not before)
    }
  }

  // ---------- the voice orb in the preview: click to listen, click again (or wait) to stop; then the words arrive ----------
  st() {
    const v = this.state.vmode;
    if (v === 'insert' || v === 'shown' || v === 'sending') return 'typing';
    if (v === 'sent') return 'running';
    return Component.prototype.st.call(this);
  }
  voiceTap() {
    const v = this.state.vmode;
    if (v === 'listen') { this.vt = 0; this.setState({ vmode: 'process' }); }
    else if (v === 'idle' || v === 'shown') { this.vt = 0; this.setState({ vmode: 'listen', vN: 0 }); }
  }
  voiceTick(dt) {
    const S = this.state, v = S.vmode;
    if (v === 'idle') { this.vlev = 0; return; }
    this.vt += dt;
    if (v === 'listen') {
      // a speaking voice: syllables (fast) under phrases (slow), never quite silent while a phrase runs
      const t = this.vt, syl = Math.max(0, Math.sin(t * 13.7) * 0.6 + Math.sin(t * 7.3 + 1) * 0.4), phr = 0.5 + 0.5 * Math.sin(t * 1.9 - 1.2);
      this.vlev = clamp(0.15 + syl * phr * 0.9, 0, 1);
      if (this.vt > 4.2) { this.vt = 0; this.setState({ vmode: 'process' }); }
    } else if (v === 'process') {
      this.vlev = 0;
      if (this.vt > 1.1) { this.vt = 0; this.setState({ vmode: 'insert', vN: 0 }); }
    } else if (v === 'insert') {
      const n = Math.min(CZ_VTEXT.length, Math.floor(this.vt * 90));
      if (n !== S.vN) this.setState({ vN: n });
      if (n >= CZ_VTEXT.length) { this.vt = 0; this.setState({ vmode: S.afterVoice === 'send' ? 'sending' : 'shown' }); }
    } else if (v === 'sending') {
      if (this.vt > 0.55) { this.vt = 0; this.setState({ vmode: 'sent' }); }
    } else if (v === 'sent') {
      if (this.vt > 2.6) { this.vt = 0; this.setState({ vmode: 'idle' }); }
    }
  }
  orbFor(el, kind) {
    const k = CZ_ORBS.find((o) => o.id === kind);
    const reg = this.orbReg && this.orbReg.find((o) => o.id === k.rid);
    const want = (reg ? 'r:' : 'p:') + kind;
    if (el.__ok !== want) {
      if (el.__orb) el.__orb.dispose();
      el.__orb = reg ? reg.make(el, { dark: this.dark(), colors: 'preset' }) : czPlaceholderOrb(el, kind);
      el.__ok = want; el.__px = 0;
    }
    const px = Math.round(el.clientWidth || 0);
    if (px && px !== el.__px) { el.__px = px; el.__orb.resize(px); }
    if (el.__dk !== this.dark()) { el.__dk = this.dark(); el.__orb.setDark(this.dark()); }
    return el.__orb;
  }
  orbTick(now, dt, vis) {
    const S = this.state, reduce = !!(this.mq && this.mq.matches), t = now / 1000;
    // the orb's activation: hover .4, listening .45 to 1 with the voice, processing 1; up fast, down slow
    const act = S.vmode === 'listen' ? 0.45 + 0.55 * this.vlev : S.vmode === 'process' ? 1 : S.orbHov ? 0.4 : 0;
    const c0 = this.orbCharge == null ? act : this.orbCharge;
    this.orbCharge = c0 + (act - c0) * (1 - Math.exp(-dt / (act > c0 ? 0.08 : 0.35)));
    for (const id in vis) {
      const E = this.orbEls[id.slice(1)];
      if (!E) continue;
      if (E.hold && E.hold.isConnected) E.hold.style.setProperty('--lv', fmt(S.vmode === 'listen' ? this.vlev : 0, 3));
      for (const k of CZ_ORBS) {
        const c = E['c-' + k.id], tl = E['t-' + k.id];
        if (c && c.isConnected && (k.id === S.orb || now - (this.orbAt || -1e9) < 700)) this.orbFor(c, k.id).frame(t, { charge: this.orbCharge, reduceMotion: reduce });
        if (tl && tl.isConnected) this.orbFor(tl, k.id).frame(t, { charge: k.id === S.orb ? 0.12 : 0, reduceMotion: reduce });
      }
    }
  }
  // the real orbs: window.__cz.useOrbs(registry), with voice-orbs.js's default export ({ id, make } per orb)
  useOrbs(reg) { this.orbReg = Array.isArray(reg) ? reg : null; this.forceUpdate(); }
  orbRef(vid, key) {
    const k = vid + '|' + key;
    if (!this.orbRefs[k]) this.orbRefs[k] = (el) => { const E = this.orbEls[vid] || (this.orbEls[vid] = {}); E[key] = el; };
    return this.orbRefs[k];
  }
  setOrb(id) { if (id !== this.state.orb) { this.orbAt = performance.now(); this.setState({ orb: id }); } }
  orbRows(vid) {
    const S = this.state, k = CZ_ORBS.find((o) => o.id === S.orb);
    const orbs = this.row('Orbs', {
      t: 'Orb', d: this.dx('d-orb', k.id === 'siri' ? 'Siri Wave, the default' : 'Voice Membrane'), top: true,
      opts: CZ_ORBS.map((o) => ({ t: o.name, on: S.orb === o.id ? 'true' : 'false', tab: S.orb === o.id ? '0' : '-1', cls: 'czopt' + (S.orb === o.id ? ' on' : ''), pick: () => this.setOrb(o.id), ref: this.orbRef(vid, 't-' + o.id) })),
      key: (ev) => { const d = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[ev.key]; if (!d) return; ev.preventDefault(); const i = CZ_ORBS.findIndex((o) => o.id === S.orb); this.setOrb(CZ_ORBS[(i + d + 2) % 2].id); const host = ev.currentTarget; requestAnimationFrame(() => { const b = host.querySelector('.czopt.on'); if (b) b.focus(); }); },
    });
    const after = this.seg('After voice input', [{ id: 'leave', t: 'Leave the text in the composer' }, { id: 'send', t: 'Send it as the prompt' }], S.afterVoice, (id) => this.setState({ afterVoice: id }), { sm: true });
    const send = this.row('Seg', {
      t: 'After voice input', stack: true,
      d: this.dx('d-after', S.afterVoice === 'send' ? 'The words go out as your prompt as soon as they arrive' : 'The words wait in the composer until you press send'),
      segs: [{ cls: 'czsgw on', g: after }],
    });
    const hint = this.row('Note', { t: 'Click the orb in the preview to try it: it listens until you click it again, then the words arrive.' });
    const link = this.row('Link', { t: 'Speech model and language', d: this.dx('d-vlink', vmModel(vmParse(S.selected).m).name + ' \u00b7 ' + S.lang + ' \u00b7 ' + (S.engine === 'local' ? 'On this PC' : 'OpenAI cloud')), bt: 'Voice typing', go: () => this.setPage('voice') });
    this.vLink = link;
    return [orbs, send, hint];
  }
  orbVals(vid, o) {
    const S = this.state, v = S.vmode;
    o.orbCls = 'vorb' + (S.orbHov ? ' hov' : '') + (v === 'listen' ? ' listen' : '') + (v === 'process' ? ' proc' : '') + ' o-' + S.orb;
    o.orbAria = v === 'listen' ? 'Stop listening' : 'Start voice typing (' + CZ_ORBS.find((k) => k.id === S.orb).name + ')';
    o.orbPressed = v === 'listen' ? 'true' : 'false';
    o.orbTap = () => this.voiceTap();
    o.orbIn = () => { if (!this.state.orbHov) this.setState({ orbHov: true }); };
    o.orbOut = () => { if (this.state.orbHov) this.setState({ orbHov: false }); };
    o.orbKey = (ev) => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); this.voiceTap(); } };
    o.holdRef = this.orbRef(vid, 'hold');
    o.orbs = CZ_ORBS.map((k) => ({ cls: 'vorbc' + (k.id === S.orb ? ' on' : ''), ref: this.orbRef(vid, 'c-' + k.id) }));
    return o;
  }

  // the segmented controls' pill: measured from the picked button itself, so a label never leaves its capsule
  placePills() {
    const reset = this.pillReset; this.pillReset = false;
    document.querySelectorAll('.czseg').forEach((g) => {
      const pill = g.querySelector('.czpill'), b = g.querySelector('button.on');
      if (!pill) return;
      if (!b) { pill.style.opacity = '0'; return; }
      const l = b.offsetLeft, w = b.offsetWidth;
      if (!w) return;
      const key = l + ',' + w;
      if (pill.__k === key && !reset) return;
      const first = pill.__k == null || reset;
      pill.__k = key;
      if (first) pill.style.transition = 'none';
      pill.style.left = l + 'px'; pill.style.width = w + 'px'; pill.style.opacity = '1';
      if (first) { void pill.offsetWidth; pill.style.transition = ''; }
    });
  }

  // only the page on screen is mounted, and the one fading out until its fade has run (a full re-render of every
  // page cost about 13 ms; one page is about a quarter of that)
  shown(vid) {
    const S = this.state, act = vid === 'V' ? S.page === 'voice' : S.page !== 'voice' && S.variant === vid;
    return act || (this.leftAt && this.leftAt[vid] && performance.now() - this.leftAt[vid] < 480);
  }
  left(vid) {
    (this.leftAt || (this.leftAt = {}))[vid] = performance.now();
    clearTimeout(this.leftTimer);
    this.leftTimer = setTimeout(() => this.forceUpdate(), 520);
  }
  entering(vid) {
    if (!this.shown(vid)) this.state.sc = Object.assign({}, this.state.sc, { [vid]: false });
  }

  setVariant(v) {
    if (v !== 'B' || v === this.state.variant) return;   // B is locked; A and C are gone
    this.varAt[this.state.variant] = performance.now();
    if (this.state.page !== 'voice') this.left(this.state.variant);
    this.entering(v);
    this.setState({ variant: v });
    this.save();
  }
  setDark(d) { if (d !== this.state.dark) { this.setState({ dark: d }); this.save(); } }

  // ---------- controls ----------
  seg(label, opts, cur, pick, o) {
    o = o || {};
    return {
      label, cls: 'czseg' + (o.sm ? ' sm' : '') + (o.cls ? ' ' + o.cls : ''),
      opts: opts.map((x) => ({ t: x.t, on: x.id === cur ? 'true' : 'false', cls: x.id === cur ? 'on' : '', tab: x.id === cur ? '0' : '-1', pick: () => pick(x.id) })),
      key: (ev) => {
        const i = opts.findIndex((x) => x.id === cur);
        const d = ev.key === 'ArrowRight' || ev.key === 'ArrowDown' ? 1 : ev.key === 'ArrowLeft' || ev.key === 'ArrowUp' ? -1 : 0;
        if (!d) return;
        ev.preventDefault();
        const n = opts[(i + d + opts.length) % opts.length];
        pick(n.id);
        const host = ev.currentTarget;
        requestAnimationFrame(() => { const b = host.querySelector('button.on'); if (b) b.focus(); });
      },
    };
  }

  dx(key, text) { return this.xf(key, text || ''); }

  row(kind, o) { return Object.assign({ kind, ['is' + kind]: true, fold: false, dim: false }, o); }

  cap(t) { return this.row('Cap', { t }); }

  segRow(key, t, d, segs) {
    return this.row('Seg', { t, d: this.dx('d-' + key, d), segs: segs.map((s) => ({ cls: 'czsgw' + (s.on ? ' on' : ''), g: s.g })) });
  }

  slider(group, key, t, d, min, max, step, fmtv, o) {
    o = o || {};
    const cfg = this.state[group], raw = cfg[key];
    const isAuto = o.auto !== undefined && raw == null;
    const v = isAuto ? o.auto : raw;
    const f = clamp((v - min) / (max - min), 0, 1);
    const pct = (o.inv ? 1 - f : f) * 100;
    const id = group + '.' + key;
    const set = (nv) => this.setOpt(group, key, +(Math.round(clamp(nv, min, max) / step) * step).toFixed(4));
    const fromX = (ev) => {
      const r = ev.currentTarget.getBoundingClientRect();
      let u = clamp((ev.clientX - r.left - 9) / Math.max(1, r.width - 18), 0, 1);
      if (o.inv) u = 1 - u;
      set(min + u * (max - min));
    };
    const sgn = o.inv ? -1 : 1;
    return this.row('Slider', {
      t, d: this.dx('d-' + id, typeof d === 'function' ? d(isAuto) : d), dim: !!o.na, fold: !!o.fold,
      pct: fmt(pct, 3), vt: fmtv(v), min, max, v: fmt(v, 3),
      slc: 'czsl' + (this.state.drag === id ? ' drag' : ''),
      down: (ev) => {
        if (ev.button !== undefined && ev.button !== 0) return;
        ev.preventDefault();
        try { ev.currentTarget.setPointerCapture(ev.pointerId); } catch (e) {}
        ev.currentTarget.focus({ preventScroll: true });
        this.setState({ drag: id });
        fromX(ev);
      },
      move: (ev) => { if (this.state.drag === id) fromX(ev); },
      up: () => { if (this.state.drag === id) this.setState({ drag: null }); },
      key: (ev) => {
        const big = (max - min) / 10;
        const map = { ArrowLeft: -step, ArrowDown: -step, ArrowRight: step, ArrowUp: step, PageDown: -big, PageUp: big };
        if (ev.key in map) { ev.preventDefault(); set(v + sgn * map[ev.key]); }
        else if (ev.key === 'Home') { ev.preventDefault(); set(o.inv ? max : min); }
        else if (ev.key === 'End') { ev.preventDefault(); set(o.inv ? min : max); }
      },
      hasAuto: o.auto !== undefined,
      auc: 'czau' + (o.auto === undefined ? ' none' : isAuto ? ' on' : ''), auOn: isAuto ? 'true' : 'false', auTab: o.auto === undefined ? '-1' : '0',
      auTog: () => { if (o.auto === undefined) return; this.setOpt(group, key, isAuto ? o.auto : null); },
    });
  }

  toggle(group, key, t, d, o) {
    o = o || {};
    const on = o.forceOn ? true : !!this.state[group][key];
    return this.row('Toggle', {
      t, d: this.dx('d-' + group + '.' + key, d), dim: !!o.na, fold: !!o.fold,
      on: on ? 'true' : 'false', tgc: 'tog' + (on ? ' on' : '') + (o.na ? ' still' : ''),
      tog: () => { if (!o.na) this.setOpt(group, key, !on); },
    });
  }

  // ---------- the rows of each style ----------
  beamDetail(b) {
    return b.size === 'line' ? 'one sweep' : b.family === 'pulse' ? 'one breath' : 'one lap';
  }

  beamRows() {
    const b = this.state.beam, E = this.beamEff(b, this.st());
    const pulse = E.pulse, mono = b.color === 'mono', po = b.size === 'pulse-outside', line = b.size === 'line';
    const x2 = (v) => fmt(v, 2) + '×', deg = (v) => Math.round(v) + '°', s = (v) => fmt(v, 2) + ' s', px = (v) => fmt(v, 1) + ' px', pc = (v) => Math.round(v * 100) + '%';
    const fam = this.seg('Motion', [{ id: 'rotate', t: 'Rotate' }, { id: 'pulse', t: 'Pulse' }], b.family, (id) => { if (id !== b.family) this.setOpts('beam', { family: id, size: id === 'rotate' ? 'md' : 'pulse-inner' }); }, { sm: true });
    const pick = (id) => this.setOpt('beam', 'size', id);
    const rot = this.seg('Shape', [{ id: 'md', t: 'Large' }, { id: 'line', t: 'Line' }, { id: 'sm', t: 'Small' }], b.size, pick, { sm: true });
    const pul = this.seg('Shape', [{ id: 'pulse-inner', t: 'Inside' }, { id: 'pulse-outside', t: 'Outside' }], b.size, pick, { sm: true });
    const th = this.seg('Tuned for', [{ id: 'auto', t: 'Auto' }, { id: 'dark', t: 'Dark' }, { id: 'light', t: 'Light' }], b.theme, (id) => this.setOpt('beam', 'theme', id), { sm: true });
    const sw = PAL_NAMES.map((n) => {
      const c = BBT.G[n].border.map((x) => x.color);
      return { name: CZ_PAL[n], on: b.color === n ? 'true' : 'false', cls: 'czsw' + (b.color === n ? ' sel' : ''), bg: 'linear-gradient(135deg,' + c[0] + ',' + c[4] + ' 40%,' + c[6] + ' 70%,' + c[8] + ')', pick: () => this.setOpt('beam', 'color', n) };
    });
    const R = {};
    R.motion = this.segRow('beam.family', 'Motion', b.family === 'rotate' ? 'Travels around the edge' : 'Breathes in place', [{ on: true, g: fam }]);
    R.shape = this.segRow('beam.size', 'Shape', CZ_SHAPE_D[b.size], [{ on: b.family === 'rotate', g: rot }, { on: b.family === 'pulse', g: pul }]);
    R.colours = this.row('Swatches', { t: 'Colours', d: this.dx('d-beam.color', CZ_PAL[b.color]), sw });
    R.speed = this.slider('beam', 'duration', 'Speed', (a) => (a ? 'Auto: the designed pace' : 'Seconds for ' + this.beamDetail(b)), 0.5, 14, 0.05, s, { auto: E.autoDur, inv: true });
    R.bright = this.slider('beam', 'brightness', 'Brightness', (a) => (a ? 'Auto: set for the theme' : 'Lighter or deeper colours'), 0.2, 3, 0.05, x2, { auto: E.autoBr });
    R.strength = this.slider('beam', 'strength', 'Intensity', 'How strongly the light shows', 0, 1, 0.01, pc);
    R.respond = this.toggle('beam', 'respond', 'Follow the composer', 'Softer at rest, fuller as you type and while a turn runs');
    R.hue = this.slider('beam', 'hueShift', 'Colour shift', 'Turns every colour around the wheel', -180, 180, 1, deg);
    R.sat = this.slider('beam', 'saturation', 'Saturation', (a) => (a ? 'Auto: set for the theme' : 'Vivid or muted colours'), 0, 2.5, 0.05, x2, { auto: E.autoSat });
    R.drift = this.slider('beam', 'hueRange', 'Colour drift', pulse ? 'Pulse always runs through every hue' : mono || b.staticColors ? 'Off while the colours hold still' : line ? 'How far the colours wander (Line keeps it under 13°)' : 'How far the colours wander', 0, 180, 1, (v) => '±' + Math.round(line ? Math.min(13, v) : v) + '°', { na: pulse || mono || b.staticColors });
    R.still = this.toggle('beam', 'staticColors', 'Hold colours still', mono ? 'Mono always holds still' : 'The palette stops drifting', { na: mono, forceOn: mono });
    R.glow = this.slider('beam', 'glowSize', 'Glow size', 'Softness of every glow', 0.25, 3, 0.05, x2);
    R.edge = this.slider('beam', 'strokeX', 'Edge line', 'The thin line on the edge', 0, 2, 0.05, x2);
    R.inner = this.slider('beam', 'innerX', 'Inner glow', 'The wash inside the edge', 0, 2, 0.05, x2);
    R.bloom = this.slider('beam', 'bloomX', 'Outer glow', 'The bloom beyond the edge', 0, 2, 0.05, x2);
    R.radius = this.slider('beam', 'radius', 'Corner', (a) => (a ? 'Auto: follows the composer' : 'Rounder or squarer than the composer'), 0, 26, 1, (v) => v + ' px', { auto: CR });
    R.theme = this.segRow('beam.theme', 'Tuned for', b.theme === 'auto' ? 'Matches the app’s theme' : 'Always the ' + b.theme + ' recipe', [{ on: true, g: th }]);
    R.capPo = Object.assign(this.cap('Outside glow'), { fold: !po });
    R.core = this.slider('beam', 'coreBlur', 'Core softness', 'The bright glow near the edge', 0, 20, 0.5, px, { auto: E.autoCb, fold: !po });
    R.bblur = this.slider('beam', 'bloomBlur', 'Bloom softness', 'The wide glow around it', 0, 60, 0.5, px, { auto: E.autoBb, fold: !po });
    R.gx = this.slider('beam', 'glowX', 'Glow width', 'How far it spreads sideways', 0.35, 4, 0.01, x2, { auto: +clamp(CW / 350, 0.35, 4).toFixed(3), fold: !po });
    R.gy = this.slider('beam', 'glowY', 'Glow height', 'How far it spreads up and down', 0.35, 4, 0.01, x2, { auto: +clamp(CH / 140, 0.35, 4).toFixed(3), fold: !po });
    R.boost = this.slider('beam', 'boost', 'Glow boost', 'Extra brightness in the glow', 0.5, 2, 0.01, x2, { fold: !po });
    R.capLine = Object.assign(this.cap('Line'), { fold: !line });
    R.spike = this.slider('beam', 'spike', 'Flare', 'How much the streak flares as it goes', 0, 3, 0.05, x2, { fold: !line });
    return R;
  }

  auraRows() {
    const a = this.state.aura;
    const px = (v) => fmt(v, 1) + ' px', pc = (v) => Math.round(v) + '%';
    const scheme = this.seg('Colours', [{ id: 'original', t: 'Original' }, { id: 'maker', t: 'Model' }, { id: 'accent', t: 'Accent' }, { id: 'custom', t: 'Custom' }], a.scheme, (id) => this.setOpt('aura', 'scheme', id), { sm: true });
    const m = this.model(), x = this.accent();
    const R = {};
    R.colours = this.segRow('aura.scheme', 'Colours', a.scheme === 'maker' ? 'The model in use: ' + m.name : a.scheme === 'accent' ? 'Your accent: ' + x[1] : CZ_SCHEME_D[a.scheme], [{ on: true, g: scheme }]);
    R.custom = this.row('Colors', { t: 'Your colours', d: this.dx('d-aura.custom', 'One for each side of the glow'), fold: a.scheme !== 'custom', c1: a.c1, c2: a.c2, on1: (ev) => this.setOpt('aura', 'c1', ev.target.value), on2: (ev) => this.setOpt('aura', 'c2', ev.target.value) });
    R.speed = this.slider('aura', 'speed', 'Speed', 'Seconds for one turn', 3, 40, 0.5, (v) => fmt(v, 1) + ' s', { inv: true });
    R.breath = this.toggle('aura', 'breathing', 'Breathing', 'The glow slowly swells and settles');
    R.rest = this.slider('aura', 'rest', 'At rest', 'While the composer waits', 0, 100, 1, pc);
    R.typing = this.slider('aura', 'typing', 'While typing', 'As you write a message', 0, 100, 1, pc);
    R.running = this.slider('aura', 'running', 'While a turn runs', 'As the agent works', 0, 100, 1, pc);
    R.rim = this.toggle('aura', 'rim', 'Rim light', 'A bright line hugging the edge');
    R.dots = this.toggle('aura', 'dots', 'Halftone', 'The glow seen through fine dots');
    R.hs = this.slider('aura', 'haloSize', 'Halo size', 'How far the glow reaches', 0, 40, 1, px);
    R.hb = this.slider('aura', 'haloBlur', 'Halo softness', 'How soft its outer edge is', 4, 64, 1, px);
    R.rw = this.slider('aura', 'rimW', 'Rim width', a.rim ? 'Thickness of the rim light' : 'Turn on Rim light to use it', 0.5, 5, 0.25, px, { na: !a.rim });
    R.dc = this.slider('aura', 'dotCell', 'Dot spacing', a.dots ? 'Finer or coarser dots' : 'Turn on Halftone to use it', 2, 8, 0.5, px, { na: !a.dots });
    R.ds = this.slider('aura', 'dotStr', 'Dot strength', a.dots ? 'How visible the dots are' : 'Turn on Halftone to use it', 0, 100, 1, pc, { na: !a.dots });
    return R;
  }

  isDefault(style) {
    if (style === 'beam') return CZ_BEAM_KEYS.every((k) => this.state.beam[k] === BEAM_DEFAULTS[k]);
    if (style === 'aura') return CZ_AURA_KEYS.every((k) => this.state.aura[k] === AURA_DEFAULTS[k]);
    return true;
  }

  resetRow(style) {
    const def = this.isDefault(style);
    return this.row('Reset', {
      t: 'Defaults', d: this.dx('d-reset-' + style, def ? (style === 'beam' ? 'Border beam as nth ships it' : 'The Uiverse light as nth ships it') : 'Return every ' + CZ_STYLE[style] + ' setting to how it ships'),
      rc: 'czbtn' + (def ? ' idle' : ''), dis: def ? 'true' : 'false', tab: def ? '-1' : '0',
      go: () => { if (style === 'beam') this.setOpts('beam', Object.assign({}, BEAM_DEFAULTS)); else this.setOpts('aura', Object.assign({}, AURA_DEFAULTS)); },
    });
  }

  advRow(sub) {
    const open = this.state.adv;
    return this.row('Adv', {
      t: 'Advanced', d: this.dx('d-adv-' + sub, sub), open: open ? 'true' : 'false',
      advc: 'czmore' + (open ? ' open' : ''), lab: this.xf('advlab', open ? 'Hide' : 'Show'),
      tog: () => this.setState({ adv: !open }),
    });
  }

  // one card's rows: the dividers follow the rows that show, and every row folds open or shut
  card(rows, o) {
    o = o || {};
    let first = true;
    const out = rows.map((r) => {
      const shown = !r.fold && !o.fold;
      const c = Object.assign({}, r);
      c.fc = 'czf' + (r.fold ? ' off' : '') + (r.isPreview || r.fopen ? ' open' : '');   // an open menu is not clipped by its fold
      c.dvc = 'dv' + (first || r.isCap || r.afterCap ? ' none' : '');
      c.rowc = 'row' + (r.dim ? ' dim' : '') + (r.tall ? ' tall' : '') + (r.stack ? ' stack' : '') + (r.top ? ' top' : '');
      if (shown) first = false;
      return c;
    });
    // a row right after a caption has no divider
    for (let i = 1; i < out.length; i++) if (out[i - 1].isCap && !out[i - 1].fold) out[i].dvc = 'dv none';
    return out;
  }

  sec(id, t, rows, on, o) {
    o = o || {};
    return { id, t, hasT: !!t, fc: 'czf czsecf' + (on ? '' : ' off'), rows: this.card(rows), cardc: 'card' + (o.cls ? ' ' + o.cls : '') };
  }

  // A and B: the style's settings in short groups, Advanced folded
  // the working dots of a running turn (ComposerWorkingDots.swift): in the accent by default
  wdColour(id) {
    const S = this.state, dark = this.dark();
    if ((id || S.wd) === 'model') { const M = this.maker(); return czTone(M[4], dark, dark ? M[7] : M[8]); }
    const a = CZ_ACC.find((x) => x[0] === ((id || S.wd) === 'custom' ? S.wdc : 'win')) || CZ_ACC[0];
    return dark ? a[3] : a[4];
  }
  touchDots(patch) {
    if (this.state.vmode !== 'idle') this.state.vmode = 'idle';
    this.setState(patch);
    this.setMode('running');
  }
  dotsSec() {
    const S = this.state, dark = this.dark(), M = this.maker();
    const g = this.seg('Dot colour', [{ id: 'accent', t: 'Accent' }, { id: 'model', t: 'Model colour' }, { id: 'custom', t: 'Custom' }], S.wd, (id) => this.touchDots({ wd: id }), { sm: true });
    const cur = CZ_ACC.find((x) => x[0] === S.wdc) || CZ_ACC[1];
    const d = S.wd === 'accent' ? 'Your accent colour, as the rest of nth' : S.wd === 'model' ? 'The colour of the model in use: ' + M[1] : cur[1];
    const line = (grp) => CZ_ACC.filter((x) => x[2] === grp).map((x) => ({ name: x[1], on: S.wdc === x[0] ? 'true' : 'false', cls: 'czsw' + (S.wdc === x[0] ? ' sel' : ''), bg: dark ? x[3] : x[4], pick: () => this.touchDots({ wdc: x[0] }) }));
    const seg = this.row('Seg', { t: 'Dot colour', d: this.dx('d-wd', d), segs: [{ cls: 'czsgw on', g }] });
    const sw = this.row('AccSw', { t: 'Custom dot colour', fold: S.wd !== 'custom', classic: line('classic'), sig: line('signature') });
    const hint = this.row('Note', { t: 'The dots run along the bottom of the composer while a turn runs; touching this switches the preview to Running a turn.' });
    return this.sec('dots', 'Working dots', [seg, sw, hint], true);
  }
  // Speech: the first section that is not about the composer's look, so the pinned stage lets go here. A summary of
  // the Voice typing page, each row opening it.
  speechSecs() {
    this.vInit();
    const S = this.state, go = () => this.setPage('voice'), local = S.engine === 'local';
    const P = vmParse(S.selected), M = vmModel(P.m);
    const rows = [
      this.row('Link', { t: 'Speech model', d: this.dx('d-sp-model', local ? vmName(S.selected) : 'Not used while the cloud engine is on'), bt: 'Change', go }),
      this.row('Link', { t: 'Language', d: this.dx('d-sp-lang', this.voLangSummary ? this.voLangSummary() : S.lang === 'Auto-detect' ? 'Auto-detect: Whisper works out the language' : S.lang), bt: 'Change', go }),
      this.row('Link', { t: 'Engine', d: this.dx('d-sp-eng', local ? 'On this PC: your voice never leaves this computer' : 'OpenAI cloud, with your own key'), bt: 'Change', go }),
    ];
    return [this.sec('speech', 'Speech', rows, true)];
  }

  voiceSec(vid) { return Object.assign(this.sec('voice', 'Voice orb', this.orbRows(vid), true), { badge: !this.orbReg }); }

  sections(style, vid) {
    const B = this.beamRows(), U = this.auraRows();
    const isB = style === 'beam', isU = style === 'aura', adv = this.state.adv;
    const bAdv = this.card([B.hue, B.sat, B.drift, B.still, B.glow, B.edge, B.inner, B.bloom, B.radius, B.theme, B.capPo, B.core, B.bblur, B.gx, B.gy, B.boost, B.capLine, B.spike]);
    const uAdv = this.card([U.hs, U.hb, U.rw, U.dc, U.ds]);
    return [
      this.sec('b-look', 'Look', [B.motion, B.shape, B.colours], isB),
      this.sec('b-pace', 'Pace and strength', [B.speed, B.bright, B.strength, B.respond], isB),
      { id: 'b-adv', hasT: false, fc: 'czf czsecf' + (isB ? '' : ' off'), rows: this.card([this.advRow('Colour recipe, glow layers and corner')]).concat([{ isNest: true, fc: 'czf' + (adv ? '' : ' off'), dvc: 'dv', nest: bAdv }]), cardc: 'card' },
      this.sec('b-reset', '', [this.resetRow('beam')], isB),
      this.sec('u-look', 'Look', [U.colours, U.custom, U.rim, U.dots], isU),
      this.sec('u-motion', 'Motion', [U.speed, U.breath], isU),
      this.sec('u-int', 'Brightness by moment', [U.rest, U.typing, U.running], isU),
      { id: 'u-adv', hasT: false, fc: 'czf czsecf' + (isU ? '' : ' off'), rows: this.card([this.advRow('Halo, rim and dot sizes')]).concat([{ isNest: true, fc: 'czf' + (adv ? '' : ' off'), dvc: 'dv', nest: uAdv }]), cardc: 'card' },
      this.sec('u-reset', '', [this.resetRow('aura')], isU),
      this.sec('off', '', [this.row('Note', { t: 'The composer has no light. Your Border beam and Uiverse light settings are kept for when you turn one back on.' })], style === 'off'),
      this.voiceSec(vid),
      this.dotsSec(),
    ];
  }

  // C: one card, the essentials first and everything else under Advanced
  compactRows(style, preview) {
    const B = this.beamRows(), U = this.auraRows();
    const isB = style === 'beam', isU = style === 'aura', adv = this.state.adv;
    const f = (r, on) => Object.assign({}, r, { fold: r.fold || !on });
    const advB = this.card([B.strength, B.respond, B.hue, B.sat, B.drift, B.still, B.glow, B.edge, B.inner, B.bloom, B.radius, B.theme, B.capPo, B.core, B.bblur, B.gx, B.gy, B.boost, B.capLine, B.spike]);
    const advU = this.card([this.cap('Brightness by moment'), U.rest, U.typing, U.running, this.cap('Sizes'), U.hs, U.hb, U.rw, U.dc, U.ds]);
    const rows = this.card([
      preview,
      this.segRow('light', 'Light', CZ_STYLE_D[style], [{ on: true, g: this.styleSeg(true) }]),
      f(B.motion, isB), f(B.shape, isB), f(B.colours, isB), f(B.speed, isB), f(B.bright, isB),
      f(U.colours, isU), f(U.custom, isU), f(U.speed, isU), f(U.breath, isU), f(U.rim, isU), f(U.dots, isU),
      f(this.row('Note', { t: 'The composer has no light. Your Border beam and Uiverse light settings are kept for when you turn one back on.' }), style === 'off'),
      f(this.advRow(isB ? 'Intensity, colour recipe, glow layers' : 'Brightness by moment and sizes'), style !== 'off'),
    ]);
    rows.push({ isNest: true, fc: 'czf' + (adv && isB ? '' : ' off'), dvc: 'dv', nest: advB });
    rows.push({ isNest: true, fc: 'czf' + (adv && isU ? '' : ' off'), dvc: 'dv', nest: advU });
    rows.push(Object.assign(this.card([f(this.resetRow('beam'), isB)])[0], { dvc: 'dv' + (isB ? '' : ' none') }));
    rows.push(Object.assign(this.card([f(this.resetRow('aura'), isU)])[0], { dvc: 'dv' + (isU ? '' : ' none') }));
    return rows;
  }

  styleSeg(sm) {
    return this.seg('Light', [{ id: 'beam', t: 'Border beam' }, { id: 'aura', t: 'Uiverse light' }, { id: 'off', t: 'Off' }], this.state.light, (id) => this.setState({ light: id }), { sm });
  }
  stateSeg(sm) {
    return this.seg('Preview state', [{ id: 'rest', t: 'Rest' }, { id: 'typing', t: 'Typing' }, { id: 'running', t: 'Running a turn' }], this.state.mode, (id) => { if (this.state.vmode !== 'idle') this.setState({ vmode: 'idle' }); this.setMode(id); }, { sm });
  }

  inst(vid) {
    const id = 'v' + vid;
    const o = this.instance(id, this.state.light, this.st());
    if (!this.czRefs[id]) this.czRefs[id] = (el) => { this.allEls[id] = el; };
    o.clockRef = this.czRefs[id];
    if (!this.dotRefs) { this.dotRefs = {}; this.dotEls = {}; }
    if (!this.dotRefs[vid]) this.dotRefs[vid] = (el) => { this.dotEls[vid] = el; };
    o.dotRef = this.dotRefs[vid];
    return this.orbVals(vid, o);
  }

  scrollRef(vid) {
    return (ev) => {
      const sc = ev.currentTarget.scrollTop > 4;
      if (sc !== this.state.sc[vid]) this.setState({ sc: Object.assign({}, this.state.sc, { [vid]: sc }) });
    };
  }

  caption() {
    const S = this.state, b = S.beam, a = S.aura;
    if (S.light === 'beam') return 'Border beam · ' + (b.family === 'pulse' ? 'Pulse ' + (b.size === 'pulse-inner' ? 'inside' : 'outside') : { md: 'Large', line: 'Line', sm: 'Small' }[b.size]) + ' · ' + CZ_PAL[b.color];
    if (S.light === 'aura') return 'Uiverse light · ' + { original: 'Original', maker: this.maker()[1] + ' colours', accent: 'Accent', custom: 'Custom' }[a.scheme] + ' · ' + fmt(a.speed, 1) + ' s a turn';
    return 'No light';
  }

  renderVals() {
    const S = this.state, st = this.st(), dark = this.dark(), v = S.variant;
    this.instCfg = {};
    const acc = ACCENTS.find((x) => x[0] === 'win') || ACCENTS[0];
    const page = (vid) => 'czpage' + (v === vid ? ' on' : '');
    const scroll = (vid) => 'czscroll' + (S.sc[vid] ? ' sc' : '');
    const preview = (vid, o) => ({ inst: [this.inst(vid)], cls: 'pvw wbg' + (o && o.stage ? ' stage' : '') });
    const sA = this.shown('A'), sB = this.shown('B'), sC = this.shown('C');
    const prevC = sC ? this.row('Preview', { pv: preview('C'), state: this.stateSeg(true), ms: this.modelSwitch('C'), tall: true }) : null;
    const cards = ['beam', 'aura', 'off'].map((id) => ({
      t: CZ_STYLE[id], d: CZ_STYLE_D[id], g: 'czgl gl-' + id, on: S.light === id ? 'true' : 'false', tab: S.light === id ? '0' : '-1',
      cls: 'czsc' + (S.light === id ? ' on' : ''), pick: () => this.setState({ light: id }),
    }));
    const keys = [];
    for (const id in this.bbSlots) for (const k of this.bbSlots[id].keys) if (k && keys.indexOf(k) < 0) keys.push(k);
    keys.sort();
    return {
      boardCls: 'cz ' + (dark ? 'dark' : 'light'),
      winCls: 'czwin ' + (dark ? 'wd' : 'wl'), pac: dark ? CZ_ACC[0][3] : CZ_ACC[0][4],
      designCss: keys.map((k) => this.bbCss[k]).join('\n'),
      variantSeg: this.seg('Layout', CZ_VARIANTS.map((x) => ({ id: x.id, t: x.id + '  ' + x.t })), v, (id) => this.setVariant(id)),
      themeSeg: this.seg('Theme', [{ id: 'dark', t: 'Dark' }, { id: 'light', t: 'Light' }], dark ? 'dark' : 'light', (id) => this.setDark(id === 'dark')),
      notes: [{ cls: 'czvn on', id: 'B', t: 'Stage, locked', ps: CZ_NOTES.B }],
      side: CZ_SIDE.map(([name, d]) => ({ name, d, cls: 'witem' + (name === 'Customization' ? ' sel' : '') + (name === 'Customization' ? ' new' : '') })),
      titleCls: 'cztitle' + (S.sc[v] ? ' on' : ''),
      accCls: 'czacc' + (v === 'B' ? ' on' : ''),
      pageA: page('A'), pageB: page('B'), pageC: page('C'),
      scrollA: scroll('A'), scrollB: scroll('B'), scrollC: scroll('C'),
      onScrollA: this.scrollRef('A'), onScrollB: this.scrollRef('B'), onScrollC: this.scrollRef('C'),
      pinCls: 'czpin' + (S.sc.A ? ' sc' : ''),
      showA: sA, showB: sB, showC: sC,
      pvA: sA ? preview('A') : null, pvB: sB ? preview('B', { stage: true }) : null,
      styleSegA: this.styleSeg(true), stateSegA: this.stateSeg(true), stateSegB: this.stateSeg(true),
      cards, secsA: sA ? this.sections(S.light, 'A') : [], secsB: sB ? this.sections(S.light, 'B') : [], secsB2: sB ? this.speechSecs() : [],
      wdc: this.wdColour(), scrollRefB: this.scrollRefB || (this.scrollRefB = (el) => { this.scrollB = el; }),
      pinB: 'czpin pinb' + (S.sc.B ? ' sc' : ''),
      voiceC: sC ? this.card(this.orbRows('C')) : [], voiceBadge: !this.orbReg,
      rowsC: sC ? this.compactRows(S.light, prevC) : [],
      lightRowC: null,
      cap: this.xf('cap', this.caption()),
      typed: S.vmode === 'insert' || S.vmode === 'shown' || S.vmode === 'sending' ? CZ_VTEXT.slice(0, S.vN) : TYPED.slice(0, S.typedN),
      mdl: this.chipVals(),
      msA: this.modelSwitch('A'), msB: this.modelSwitch('B'),
      chipTap: () => this.stepMaker(1),
    };
  }
}
window.__DCComponent = CzComponent;
