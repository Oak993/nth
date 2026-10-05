// Animated wallpapers: the library. Live tiles in three tiers (only the tiles on screen are drawn), a large
// view with the mock Marrow window and its liquid glass, a compact control panel per wallpaper (colour pack, speed,
// motion and one more option), and in the page itself the loop proof of every pack and the frame cost of every
// wallpaper. Every change eases: the palette crossfades in OKLab, the options and the speed ease, the theme crossfades.
// The wallpaper clock is a phase per wallpaper, advanced by dt times the eased speed and wrapped at the loop, so a speed
// change never moves the picture. Choices are kept per viewer when the browser allows it. Nothing is sent anywhere.
(function () {
  'use strict';
  const K = window.Kit, S = K.store;
  const LIB = window.WL_DATA;
  const WALLS = LIB.wallpapers;
  const $ = (s, r) => (r || document).querySelector(s);
  const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const osReduce = !!(window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const TIERS = [
    { id: 'vibrant', name: 'Vibrant', tag: 'bold', blurb: 'Rich, saturated, bold colour, for those who want the window to glow.' },
    { id: 'balanced', name: 'Balanced', tag: 'restrained', blurb: 'Colourful but restrained: the colour is there, the drama is not.' },
    { id: 'minimal', name: 'Minimal', tag: 'calm', blurb: 'Calm, slow and low in contrast, yet still colourful: soft hue drifts, quiet gradients, gentle light.' },
  ];
  const E = new WLGL.Engine();
  WALLS.forEach((w) => E.warm(w.id));

  // ------------------------------------------------------------------ colour: OKLab and OKLCH, gamut-fitted by lowering chroma only (as the build does for the named packs)
  const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
  const D2R = Math.PI / 180, TAU = Math.PI * 2;
  function labLin(L, a, b) {
    const l = Math.pow(L + 0.3963377774 * a + 0.2158037573 * b, 3), m = Math.pow(L - 0.1055613458 * a - 0.0638541728 * b, 3), s = Math.pow(L - 0.0894841775 * a - 1.291485548 * b, 3);
    return [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s, -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s];
  }
  const inGamut = (L, a, b) => { const r = labLin(L, a, b); return r[0] >= -0.0005 && r[0] <= 1.0005 && r[1] >= -0.0005 && r[1] <= 1.0005 && r[2] >= -0.0005 && r[2] <= 1.0005; };
  function fitLab(L, C, h) {
    L = clamp(L, 0, 1); C = Math.max(0, C);
    const cs = Math.cos(h), sn = Math.sin(h);
    let c = C;
    if (!inGamut(L, C * cs, C * sn)) { let lo = 0, hi = C; for (let i = 0; i < 16; i++) { const mid = (lo + hi) / 2; if (inGamut(L, mid * cs, mid * sn)) lo = mid; else hi = mid; } c = lo; }
    return [L, c * cs, c * sn];
  }

  // ------------------------------------------------------------------ Match accent: the sixth pack, built from the system accent
  const ACCENTS = [['#005FB8', 'Blue'], ['#00B7C3', 'Teal'], ['#107C10', 'Green'], ['#FFB900', 'Gold'], ['#F7630C', 'Orange'], ['#E3008C', 'Pink'], ['#8764B8', 'Purple']];
  const loadAccent = () => { const a = S.get('anim.accent', ''); return /^#[0-9a-f]{6}$/i.test(a) ? a.toLowerCase() : '#005fb8'; };
  const G = { dark: K.isDark() ? 1 : 0, play: !osReduce, open: null, accent: loadAccent(), clock: null };
  try { const c = parseFloat(S.get('anim.clock', '')); if (isFinite(c)) G.clock = clamp(c, 0, 24); } catch (e) { G.clock = null; }
  const MC = {};
  function matchOf(w) {
    const c = MC[w.id];
    if (c && c.accent === G.accent) return c.pack;
    const ac = WLGL.hex2lab(G.accent), Ca = Math.hypot(ac[1], ac[2]), ha = Math.atan2(ac[2], ac[1]) / D2R;
    const m = w.match, ck = m.ck * clamp(Ca / 0.15, 0.55, 1.3);
    const pack = { id: 'match', name: 'Match accent' };
    [['dark', 0, 1], ['light', 2, 3]].forEach(([th, li, ci]) => {
      const mid = w.roles.reduce((s, r) => s + r[li], 0) / 6;
      pack[th] = w.roles.map((r, i) => {
        let L = mid + (r[li] - mid) * m.lk + (th === 'dark' ? m.dl[i] : 0), C = r[ci] * ck * m.cm[i] * (th === 'dark' ? m.dc[i] : 1), h = ha + m.off[i];
        if (th === 'dark') {
          // warm hues turn brown or olive when dark: grounds go toward plum, mid tones toward rose, the lights keep the hue (m.dark_warm)
          const dw = m.dark_warm, hh = wrapDeg(h), ss = (a0, a1, x) => { const t = clamp((x - a0) / (a1 - a0), 0, 1); return t * t * (3 - 2 * t); };
          const wt = ss(dw.band[0], dw.band[1], hh) * (1 - ss(dw.band[2], dw.band[3], hh));
          if (wt > 0) {
            if (r[0] < dw.ground_below) { h = hh + wt * wrapDeg(dw.ground_hue - hh); C *= 1 + (dw.kc - 1) * wt; L += dw.dl * wt; }
            else if (r[0] < dw.mid_below) { h = hh + dw.mid_amount * wt * wrapDeg(dw.mid_hue - hh); C *= 1 + (dw.kc - 1) * wt; L += dw.dl * wt; }
            else { L += dw.bright_dl * wt; C *= 1 + (dw.bright_kc - 1) * wt; }
          }
        }
        return lab2hex(fitLab(L, C, h * D2R));
      });
    });
    MC[w.id] = { accent: G.accent, pack };
    return pack;
  }
  const wrapDeg = (d) => ((d + 540) % 360) - 180;
  const warmth = (h) => Math.max(0, 1 - Math.abs(wrapDeg(h - 65)) / 80);
  const packsOf = (w) => w.packs.concat([matchOf(w)]);
  const findPack = (w, id) => (id === 'match' ? matchOf(w) : (w.packs.find((p) => p.id === id) || w.packs[0]));

  // ------------------------------------------------------------------ Dynamic: the four looks through the day
  // keys at 06:00 dawn, 12:00 day, 18:00 dusk, 24:00 night; between two keys the look is their smoothstep blend; the
  // `light` set has an empty day look, the `dark` set an empty night look, and the theme value mixes the two sets
  const SEGS = [['night', 'dawn'], ['dawn', 'day'], ['day', 'dusk'], ['dusk', 'night']];
  const KEYT = { dawn: 6, day: 12, dusk: 18, night: 0 };
  const clockNow = () => { const d = new Date(); return d.getHours() + d.getMinutes() / 60; };
  const clockOf = () => (G.clock == null ? clockNow() : G.clock);
  function look(w, c, dk) {
    c = ((c % 24) + 24) % 24;
    const seg = SEGS[Math.min(3, Math.floor(c / 6))], u = (c % 6) / 6, s = u * u * (3 - 2 * u), D = w.dynamic;
    const o = { dL: [], kC: [], dh: [], tx: 0, ty: 0, sun: [0, 0] };
    const lin = (set, f, i) => (1 - s) * D[set][seg[0]][f][i] + s * D[set][seg[1]][f][i];
    const lg = (set, i) => Math.exp((1 - s) * Math.log(D[set][seg[0]].kC[i]) + s * Math.log(D[set][seg[1]].kC[i]));
    for (let i = 0; i < 6; i++) {
      o.dL.push((1 - dk) * lin('light', 'dL', i) + dk * lin('dark', 'dL', i));
      o.kC.push(Math.pow(lg('light', i), 1 - dk) * Math.pow(lg('dark', i), dk));
      o.dh.push((1 - dk) * lin('light', 'dh', i) + dk * lin('dark', 'dh', i));
    }
    const tv = (set) => {
      const A = D[set][seg[0]].tint, B = D[set][seg[1]].tint;
      return [(1 - s) * A[1] * Math.cos(A[0] * D2R) + s * B[1] * Math.cos(B[0] * D2R), (1 - s) * A[1] * Math.sin(A[0] * D2R) + s * B[1] * Math.sin(B[0] * D2R)];
    };
    const tl = tv('light'), td = tv('dark');
    o.tx = (1 - dk) * tl[0] + dk * td[0]; o.ty = (1 - dk) * tl[1] + dk * td[1];
    const sa = D.sun[seg[0]], sb = D.sun[seg[1]];
    o.sun = [(1 - s) * sa[0] + s * sb[0], (1 - s) * sa[1] + s * sb[1]];
    return o;
  }
  // the palette (OKLab x 6) after a look: the tint mixes each colour toward a colour of the tint hue (chroma 0.12) in the a/b plane,
  // as a photo filter does, and gives back 60% of the chroma that took away; then chroma is multiplied, hue turned and lightness added
  function applyLook(pal, L) {
    const out = new Float32Array(18), amt = Math.min(1, Math.hypot(L.tx, L.ty)), ht = Math.atan2(L.ty, L.tx), tA = 0.12 * Math.cos(ht), tB = 0.12 * Math.sin(ht);
    for (let i = 0; i < 6; i++) {
      let a = pal[i * 3 + 1], b = pal[i * 3 + 2];
      const C0 = Math.hypot(a, b) * L.kC[i];
      if (amt > 1e-6) { a += amt * (tA - a); b += amt * (tB - b); }
      const Cm = Math.hypot(a, b), C = Cm + 0.6 * Math.max(0, C0 - Cm);
      const f = fitLab(pal[i * 3] + L.dL[i], C, Math.atan2(b, a) + L.dh[i] * D2R);
      out[i * 3] = f[0]; out[i * 3 + 1] = f[1]; out[i * 3 + 2] = f[2];
    }
    return out;
  }
  // the palette a pack shows at a darkness, with the day's look at clock hour c (null = Dynamic off), and the light offset
  function palFor(w, pk, dark, c) {
    const pal = WLGL.packLab(pk, dark);
    if (c == null) return { pal, sun: [0, 0] };
    const L = look(w, c, dark);
    return { pal: applyLook(pal, L), sun: L.sun };
  }
  const hhmm = (c) => { const m = Math.round(((c % 24) + 24) % 24 * 60) % 1440; return ('0' + Math.floor(m / 60)).slice(-2) + ':' + ('0' + (m % 60)).slice(-2); };
  const keyName = (c) => { c = ((c % 24) + 24) % 24; const d = (k) => { const x = Math.abs(c - KEYT[k]); return Math.min(x, 24 - x); }; return ['dawn', 'day', 'dusk', 'night'].sort((p, q) => d(p) - d(q))[0]; };

  // ------------------------------------------------------------------ state: the viewer's choices and the live values
  function loadState(w) {
    let s = null;
    try { s = JSON.parse(S.get('anim.' + w.id, 'null')); } catch (e) { s = null; }
    const d = w.defaults;
    s = s && typeof s === 'object' ? s : {};
    return {
      pack: s.pack === 'match' || w.packs.some((p) => p.id === s.pack) ? s.pack : d.pack,
      speed: typeof s.speed === 'number' ? clamp(s.speed, 0.25, 2.5) : d.speed,
      motion: typeof s.motion === 'number' ? clamp(s.motion, 0, 1) : d.motion,
      second: typeof s.second === 'number' ? clamp(s.second, 0, 1) : d.second,
      focus: typeof s.focus === 'number' ? clamp(s.focus, 0, 1) : d.focus,
      dynamic: typeof s.dynamic === 'boolean' ? s.dynamic : d.dynamic,
      persp: typeof s.persp === 'boolean' ? s.persp : d.perspective,
    };
  }
  const ST = {}, RT = {}, RES = {};
  // Perspective only runs when the OS does not ask for reduced motion and the page's Motion switch is on
  const tiltOK = () => !osReduce && G.play;
  WALLS.forEach((w) => {
    const st = ST[w.id] = loadState(w);
    const tg = target(w);
    RT[w.id] = { phase: (w.period * 0.137) % w.period, speedE: G.play ? st.speed : 0, opt: [st.motion, st.second], pal: Float32Array.from(tg.pal), dark: G.dark, sun: tg.sun.slice(), tilt: [0, 0], focus: st.focus, dirty: true };
    RES[w.id] = { proofs: {}, max: null, half: null, ms: null, base: null };
  });
  const save = (id) => S.set('anim.' + id, JSON.stringify(ST[id]));
  const packOf = (w) => findPack(w, ST[w.id].pack);

  // the palette and the light the wallpaper is heading for: its pack, the theme, the accent and the time of day, cached until one of them changes
  function target(w) {
    const st = ST[w.id], rt = RT[w.id] || (RT[w.id] = {});
    const pk = findPack(w, st.pack), c = st.dynamic ? clockOf() : null;
    const key = pk.id + '|' + G.dark + '|' + (c == null ? '-' : Math.round(c * 60)) + '|' + (pk.id === 'match' ? G.accent : '');
    if (rt.tkey !== key) { rt.tkey = key; rt.tgt = palFor(w, pk, G.dark, c); }
    return rt.tgt;
  }

  // one step of easing for one wallpaper; returns whether it still moves or eases
  function step(w, dt) {
    const st = ST[w.id], rt = RT[w.id];
    const tg = target(w), tgt = tg.pal;
    const aP = 1 - Math.exp(-dt / 0.25), aO = 1 - Math.exp(-dt / 0.3), aS = 1 - Math.exp(-dt / 0.18), aT = 1 - Math.exp(-dt / 0.35);
    let easing = false;
    for (let i = 0; i < 18; i++) {
      const d = tgt[i] - rt.pal[i];
      if (Math.abs(d) > 1e-4) { rt.pal[i] += d * aP; easing = true; } else rt.pal[i] = tgt[i];
    }
    for (let j = 0; j < 2; j++) {
      const d = tg.sun[j] - rt.sun[j];
      if (Math.abs(d) > 1e-4) { rt.sun[j] += d * aP; easing = true; } else rt.sun[j] = tg.sun[j];
    }
    const dd = G.dark - rt.dark;
    if (Math.abs(dd) > 1e-3) { rt.dark += dd * aP; easing = true; } else rt.dark = G.dark;
    const ot = [st.motion, st.second];
    for (let j = 0; j < 2; j++) {
      const d = ot[j] - rt.opt[j];
      if (Math.abs(d) > 1e-3) { rt.opt[j] += d * aO; easing = true; } else rt.opt[j] = ot[j];
    }
    const df = st.focus - rt.focus;
    if (Math.abs(df) > 1e-3) { rt.focus += df * aO; easing = true; } else rt.focus = st.focus;
    const tt = G.open === w.id && st.persp && tiltOK() ? V.tilt : [0, 0];
    for (let j = 0; j < 2; j++) {
      const d = tt[j] - rt.tilt[j];
      if (Math.abs(d) > 2e-3) { rt.tilt[j] += d * aT; easing = true; } else rt.tilt[j] = tt[j];
    }
    const sT = G.play ? st.speed : 0;
    const ds = sT - rt.speedE;
    if (Math.abs(ds) > 1e-3) { rt.speedE += ds * aS; easing = true; } else rt.speedE = sT;
    const moving = rt.speedE > 1e-4;
    if (moving) rt.phase = (rt.phase + dt * rt.speedE) % w.period;
    if (easing || moving) rt.dirty = true;
    return easing || moving;
  }
  const uni = (w) => { const rt = RT[w.id]; return { pal: rt.pal, dark: rt.dark, opt: rt.opt, tilt: rt.tilt, sun: rt.sun, focus: rt.focus }; };

  // ------------------------------------------------------------------ the swatch of a pack: its most colourful three, on its ground
  function lab2hex(L) {
    const l = Math.pow(L[0] + 0.3963377774 * L[1] + 0.2158037573 * L[2], 3), m = Math.pow(L[0] - 0.1055613458 * L[1] - 0.0638541728 * L[2], 3), s = Math.pow(L[0] - 0.0894841775 * L[1] - 1.291485548 * L[2], 3);
    const r = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s, -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s];
    return '#' + r.map((c) => { c = clamp(c, 0, 1); c = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.pow(c, 1 / 2.4) - 0.055; return ('0' + Math.round(c * 255).toString(16)).slice(-2); }).join('');
  }
  function swatch(pk, dark) {
    const cols = (dark ? pk.dark : pk.light).map((h) => WLGL.hex2lab(h));
    const order = cols.map((c, i) => [Math.hypot(c[1], c[2]), i]).sort((a, b) => b[0] - a[0]).slice(0, 3).map((x) => x[1]).sort((a, b) => a - b);
    const hx = order.map((i) => lab2hex(cols[i]));
    const g = lab2hex(cols[0]);
    return 'radial-gradient(circle at 32% 30%, rgba(255,255,255,.35), transparent 42%), conic-gradient(from 200deg, ' + hx[0] + ', ' + hx[1] + ', ' + hx[2] + ', ' + hx[0] + '), ' + g;
  }

  // ------------------------------------------------------------------ the tiles
  const tiles = [];
  const fmt = (x, d) => (x == null ? '-' : x.toFixed(d));
  function metaText(w) {
    const r = RES[w.id];
    const proof = r.max == null ? 'proving' : 'Δ ' + r.max + '/255';
    const cost = r.ms == null ? '' : ' · ' + fmt(r.ms, r.ms < 0.1 ? 3 : 2) + ' ms';
    return w.period + ' s loop · <span class="' + (r.max != null && r.max <= 1 ? 'ok' : '') + '">' + proof + '</span>' + cost;
  }
  function buildTiers() {
    const host = $('#tiers');
    TIERS.forEach((T) => {
      const sec = document.createElement('section');
      sec.className = 'tier dk-glass'; sec.dataset.tier = T.id; sec.id = 'tier-' + T.id; sec.setAttribute('aria-labelledby', 'th-' + T.id);
      sec.innerHTML = '<div class="tier-h"><h2 id="th-' + T.id + '">' + T.name + '</h2><span class="tag">' + T.tag + ' · ' + WALLS.filter((w) => w.tier === T.id).length + '</span><p>' + esc(T.blurb) + '</p></div><div class="grid"></div>';
      host.appendChild(sec);
      const grid = $('.grid', sec);
      WALLS.filter((w) => w.tier === T.id).forEach((w) => {
        const el = document.createElement('div');
        el.className = 'tile'; el.tabIndex = 0; el.setAttribute('role', 'button'); el.dataset.id = w.id;
        el.setAttribute('aria-label', w.name + ', ' + T.name + '. Open the large view.');
        el.innerHTML = '<div class="tc"><canvas aria-hidden="true"></canvas><span class="open">Open</span></div>'
          + '<div class="tcap"><div class="tn"><b>' + esc(w.name) + '</b><span class="dots" role="radiogroup" aria-label="Colour pack"></span></div><span class="tm"></span></div>';
        grid.appendChild(el);
        const t = { w, el, cv: $('canvas', el), wrap: $('.tc', el), vis: false, ctx: null, size: [0, 0] };
        t.ctx = t.cv.getContext('2d', { alpha: false });
        const dots = $('.dots', el);
        packsOf(w).forEach((pk) => {
          const b = document.createElement('button');
          b.type = 'button'; b.className = 'dot'; b.dataset.pack = pk.id; b.setAttribute('role', 'radio'); b.title = pk.name; b.setAttribute('aria-label', pk.name);
          b.addEventListener('click', (e) => { e.stopPropagation(); setPack(w, pk.id); });
          dots.appendChild(b);
        });
        el.addEventListener('click', () => openView(w.id, el));
        el.addEventListener('keydown', (e) => { if ((e.key === 'Enter' || e.key === ' ') && e.target === el) { e.preventDefault(); openView(w.id, el); } });
        tiles.push(t);
        if (window.ResizeObserver) new ResizeObserver(() => fitTile(t)).observe(t.wrap);
        if (window.IntersectionObserver) {
          new IntersectionObserver((es) => es.forEach((en) => { t.vis = en.isIntersecting; if (t.vis) { RT[w.id].dirty = true; wake(); } }), { rootMargin: '60px' }).observe(t.wrap);
        } else t.vis = true;
        fitTile(t);
      });
    });
  }
  const tileDpr = () => Math.min(window.devicePixelRatio || 1, 1.5);
  function fitTile(t) {
    const r = t.wrap.getBoundingClientRect();
    if (!r.width) return;
    const w = Math.max(2, Math.round(r.width * tileDpr())), h = Math.max(2, Math.round(r.height * tileDpr()));
    if (w !== t.size[0] || h !== t.size[1]) { t.size = [w, h]; t.cv.width = w; t.cv.height = h; RT[t.w.id].dirty = true; wake(); }
  }
  function renderTiles() {
    tiles.forEach((t) => {
      const w = t.w;
      $$('.dot', t.el).forEach((b) => {
        const pk = findPack(w, b.dataset.pack);
        const bg = swatch(pk, G.dark);
        if (b.__bg !== bg) { b.style.background = bg; b.__bg = bg; }
        b.setAttribute('aria-checked', String(ST[w.id].pack === pk.id));
      });
      const m = metaText(w);
      const tm = $('.tm', t.el);
      if (tm.__m !== m) { tm.innerHTML = m; tm.__m = m; }
    });
  }

  // ------------------------------------------------------------------ the loop: one requestAnimationFrame for everything on screen
  let raf = 0, last = 0, frameN = 0;
  function wake() { if (!raf) { last = performance.now(); raf = requestAnimationFrame(loop); } }
  function loop(now) {
    raf = 0;
    if (document.hidden) return;
    const dt = Math.min(0.08, Math.max(0.001, (now - last) / 1000)); last = now;
    let busy = false;
    const viewId = G.open;
    WALLS.forEach((w) => { if (step(w, dt)) busy = true; });
    if (!viewId) {
      for (const t of tiles) {
        const rt = RT[t.w.id];
        if (!t.vis || !rt.dirty || !t.size[0]) continue;
        if (E.tile(t.w.id, t.ctx, t.size[0], t.size[1], rt.phase, uni(t.w))) { rt.dirty = false; if (!t.cv.classList.contains('in')) t.cv.classList.add('in'); }
        else busy = true;
      }
    } else if (V.size[0]) {
      const w = LIB.byId[viewId], rt = RT[viewId];
      const gT = V.glass ? 1 : 0;
      const dg = gT - V.glassE;
      if (Math.abs(dg) > 2e-3) { V.glassE += dg * (1 - Math.exp(-dt / 0.2)); busy = true; rt.dirty = true; } else if (V.glassE !== gT) { V.glassE = gT; rt.dirty = true; }
      if (rt.dirty || V.dirty) {
        const g = { k: V.size[0] / V.L.W, n: V.n, box: V.box, prm: V.prm, acc: V.acc, glass: V.glassE * V.glassE * (3 - 2 * V.glassE) };
        const ok = V.glassE > 1e-3 ? E.glassView(viewId, V.ctx, V.size[0], V.size[1], rt.phase, uni(w), g) : E.tile(viewId, V.ctx, V.size[0], V.size[1], rt.phase, uni(w));
        if (ok) { rt.dirty = false; V.dirty = false; } else busy = true;
      }
      busy = true;
    }
    frameN++;
    if (busy || tiles.some((t) => t.vis && RT[t.w.id].dirty)) raf = requestAnimationFrame(loop);
  }
  document.addEventListener('visibilitychange', () => { if (!document.hidden) wake(); });

  // ------------------------------------------------------------------ choices
  function setPack(w, id) { ST[w.id].pack = id; save(w.id); renderTiles(); renderView(); wake(); }
  function setOpt(w, k, v) { ST[w.id][k] = v; save(w.id); renderView(); wake(); }
  function setClock(c) {
    G.clock = c == null ? null : clamp(c, 0, 24);
    S.set('anim.clock', G.clock == null ? '' : String(G.clock));
    renderView(); wake();
  }
  // picking an accent also selects Match accent for the open wallpaper, so the pick is seen at once
  function setAccent(hex) {
    G.accent = hex; S.set('anim.accent', hex);
    const w = LIB.byId[G.open];
    if (w) { ST[w.id].pack = 'match'; save(w.id); }
    renderTiles(); renderView(); wake();
  }
  // the live clock moves on by itself while Dynamic is on
  setInterval(() => { if (G.clock == null && WALLS.some((w) => ST[w.id].dynamic)) { renderView(); wake(); } }, 20000);

  // ------------------------------------------------------------------ the mock window and its glass (layouts from the wallpapers page)
  const IC = {
    plus: '<path d="M8 3.5v9M3.5 8h9"/>', doc: '<path d="M4.5 2.5h5l2.5 2.5v8.5h-7.5z M9.5 2.5v2.5h2.5"/>',
    mic: '<rect x="6" y="2.2" width="4" height="7.2" rx="2"/><path d="M4 8a4 4 0 0 0 8 0M8 12v2"/>',
    img: '<rect x="2.5" y="3.5" width="11" height="9" rx="2"/><circle cx="6" cy="7" r="1"/><path d="M3 11.5l3.2-3 2.3 2 2-1.8 2.5 2.3"/>',
    set: '<path d="M3 5h10M3 11h10"/><circle cx="6" cy="5" r="1.6"/><circle cx="10.5" cy="11" r="1.6"/>',
  };
  const ico = (n) => '<svg viewBox="0 0 16 16" aria-hidden="true">' + IC[n] + '</svg>';
  const SEND = '<svg viewBox="0 0 18 18" aria-hidden="true"><path d="M9 14.5V4M4.8 8.2 9 4l4.2 4.2"/></svg>';
  const box = (cls, x, y, w, h, inner) => '<div class="' + cls + '" style="left:' + x + 'px;top:' + y + 'px;width:' + w + 'px;height:' + h + 'px">' + (inner || '') + '</div>';
  const panel = (x, y, w, h) => box('pn', x, y, w, h, '<h4>Wallpaper</h4><div class="row"><span>Name</span><span data-m="name"></span></div><div class="row"><span>Colours</span><span data-m="pack"></span></div><div class="row"><span>Speed</span><span><i class="trk"></i></span></div>');
  function layoutWide() {
    const sh = [], h = [];
    const g = (x, y, w, hh, r, tint, extra) => sh.push(Object.assign({ x, y, w, h: hh, r, tint }, extra || {}));
    h.push(box('brand', 22, 22, 150, 24, '<i></i>Marrow'));
    [['New chat', 'plus', 74, 'new'], ['Release notes', 'doc', 120], ['Voice typing', 'mic', 166], ['Wallpapers', 'img', 212, 'on'], ['Settings', 'set', 502]].forEach(([t, ic, y, s]) => {
      g(16, y, 200, 38, 19, s === 'on' ? 0.3 : 0.15, s === 'new' ? { acc: 0.26 } : {});
      h.push(box('cap' + (s === 'on' ? ' on' : ''), 16, y, 200, 38, ico(ic) + '<span>' + t + '</span>'));
    });
    g(485, 20, 150, 32, 16, 0.16); h.push(box('ttl', 485, 20, 150, 32, 'Wallpapers'));
    g(520, 86, 346, 56, 26, 0.22, { acc: 0.3 }); h.push(box('bub u', 520, 86, 346, 56, 'Make the window feel alive.'));
    g(252, 162, 424, 100, 26, 0.18); h.push(box('bub', 252, 162, 424, 100, 'This wallpaper moves slowly behind the glass. The glass bends it at the rim, with a faint colour fringe.'));
    g(618, 288, 258, 170, 28, 0.26, { blur: 1.5 }); h.push(panel(618, 288, 258, 170));
    g(236, 482, 648, 58, 29, 0.2); h.push(box('cmpt', 236, 482, 560, 58, 'Ask Marrow anything'));
    g(834, 491, 40, 40, 20, 0.1, { acc: 0.62 }); h.push(box('snd', 834, 491, 40, 40, SEND));
    return { W: 900, H: 562, shapes: sh, html: h.join('') };
  }
  function layoutNarrow() {
    const sh = [], h = [];
    const g = (x, y, w, hh, r, tint, extra) => sh.push(Object.assign({ x, y, w, h: hh, r, tint }, extra || {}));
    h.push(box('brand', 14, 18, 40, 22, '<i></i>'));
    [['plus', 54, 'new'], ['doc', 98], ['img', 142, 'on'], ['set', 178]].forEach(([ic, y, s]) => {
      g(8, y, 40, 40, 20, s === 'on' ? 0.3 : 0.15, s === 'new' ? { acc: 0.26 } : {});
      h.push(box('cap ic' + (s === 'on' ? ' on' : ''), 8, y, 40, 40, ico(ic)));
    });
    g(150, 14, 120, 30, 15, 0.16); h.push(box('ttl', 150, 14, 120, 30, 'Wallpapers'));
    g(150, 58, 200, 44, 22, 0.22, { acc: 0.3 }); h.push(box('bub u', 150, 58, 200, 44, 'Make it feel alive.'));
    g(62, 114, 288, 64, 22, 0.18); h.push(box('bub', 62, 114, 288, 64, 'This wallpaper moves slowly behind the glass.'));
    g(62, 186, 230, 44, 22, 0.2); h.push(box('cmpt', 62, 186, 190, 44, 'Ask Marrow'));
    g(300, 188, 40, 40, 20, 0.1, { acc: 0.62 }); h.push(box('snd', 300, 188, 40, 40, SEND));
    return { W: 360, H: 225, shapes: sh, html: h.join('') };
  }
  const LW = layoutWide(), LN = layoutNarrow();
  function packShapes(L) {
    const N = WLGL.MAXN, b = new Float32Array(N * 4), p = new Float32Array(N * 4), a = new Float32Array(N * 4);
    L.shapes.slice(0, N).forEach((s, i) => {
      b.set([s.x + s.w / 2, s.y + s.h / 2, s.w / 2, s.h / 2], i * 4);
      p.set([Math.min(s.r, s.w / 2, s.h / 2), s.tint, s.blur == null ? 1 : s.blur, 1], i * 4);
      a.set([s.acc || 0, 0, 0, 0], i * 4);
    });
    return { box: b, prm: p, acc: a, n: Math.min(N, L.shapes.length) };
  }

  // ------------------------------------------------------------------ the large view
  const V = { el: null, glass: true, glassE: 1, size: [0, 0], L: LW, dirty: true, tilt: [0, 0] };
  function buildView() {
    const el = V.el = $('#vw');
    V.stage = $('.vstage', el); V.cv = $('canvas', V.stage); V.ctx = V.cv.getContext('2d', { alpha: false });
    V.mock = $('.mock', V.stage);
    V.glass = S.get('anim.glass', '1') !== '0'; V.glassE = V.glass ? 1 : 0;
    $('#vglass').setAttribute('aria-checked', String(V.glass));
    V.mock.classList.toggle('off', !V.glass);
    $('#vglass').addEventListener('click', () => { V.glass = !V.glass; S.set('anim.glass', V.glass ? '1' : '0'); $('#vglass').setAttribute('aria-checked', String(V.glass)); V.mock.classList.toggle('off', !V.glass); V.dirty = true; wake(); });
    $('#vclose').addEventListener('click', closeView);
    $('.vbk', el).addEventListener('click', closeView);
    $('#vprev').addEventListener('click', () => nav(-1));
    $('#vnext').addEventListener('click', () => nav(1));
    document.addEventListener('keydown', (e) => {
      if (!G.open) return;
      if (e.key === 'Escape') { e.preventDefault(); closeView(); }
      else if ((e.key === 'ArrowRight' || e.key === 'ArrowLeft') && !(e.target && e.target.matches && e.target.matches('input'))) { e.preventDefault(); nav(e.key === 'ArrowRight' ? 1 : -1); }
    });
    const bindRange = (sel, key, fmtV) => {
      const r = $(sel);
      r.addEventListener('input', () => { const w = LIB.byId[G.open]; if (!w) return; setOpt(w, key, parseFloat(r.value)); });
      r.__fmt = fmtV;
    };
    bindRange('#vspeed', 'speed');
    bindRange('#vmotion', 'motion');
    bindRange('#vsecond', 'second');
    bindRange('#vfocus', 'focus');
    $('#vreset').addEventListener('click', () => { const w = LIB.byId[G.open]; if (!w) return; Object.assign(ST[w.id], { pack: w.defaults.pack, speed: w.defaults.speed, motion: w.defaults.motion, second: w.defaults.second, focus: w.defaults.focus, dynamic: w.defaults.dynamic, persp: w.defaults.perspective }); save(w.id); renderTiles(); renderView(); wake(); });
    // Dynamic: the switch, the clock scrubber and the key looks
    $('#vdynsw').addEventListener('click', () => { const w = LIB.byId[G.open]; if (!w) return; ST[w.id].dynamic = !ST[w.id].dynamic; save(w.id); renderView(); renderTiles(); wake(); });
    $('#vclock').addEventListener('input', () => setClock(parseFloat($('#vclock').value)));
    const keys = $('#vkeys');
    [['dawn', 'Dawn'], ['day', 'Day'], ['dusk', 'Dusk'], ['night', 'Night'], ['now', 'Now']].forEach(([k, t]) => {
      const b = document.createElement('button');
      b.type = 'button'; b.className = 'kb'; b.dataset.key = k; b.textContent = t;
      b.addEventListener('click', () => setClock(k === 'now' ? null : KEYT[k]));
      keys.appendChild(b);
    });
    // Perspective
    $('#vpersp').addEventListener('click', () => { const w = LIB.byId[G.open]; if (!w || !tiltOK()) return; ST[w.id].persp = !ST[w.id].persp; save(w.id); renderView(); wake(); });
    V.stage.addEventListener('pointermove', (e) => {
      const r = V.stage.getBoundingClientRect(); if (!r.width) return;
      V.tilt = [clamp((e.clientX - (r.left + r.width / 2)) / (r.width / 2), -1, 1), clamp(-(e.clientY - (r.top + r.height / 2)) / (r.height / 2), -1, 1)];
      wake();
    });
    V.stage.addEventListener('pointerleave', () => { V.tilt = [0, 0]; wake(); });
    // Match accent: the picker
    const acc = $('#vacc');
    ACCENTS.forEach(([hex, name]) => {
      const b = document.createElement('button');
      b.type = 'button'; b.className = 'ac'; b.dataset.hex = hex.toLowerCase(); b.title = name; b.setAttribute('aria-label', name + ' accent'); b.setAttribute('role', 'radio'); b.style.background = hex;
      b.addEventListener('click', () => setAccent(hex.toLowerCase()));
      acc.insertBefore(b, $('#vacccustom').parentNode);
    });
    $('#vacccustom').addEventListener('input', (e) => setAccent(e.target.value.toLowerCase()));
    $('#vprove').addEventListener('click', () => { const w = LIB.byId[G.open]; if (w) proveNow(w, true); });
    $('#vcopy').addEventListener('click', async () => {
      const w = LIB.byId[G.open]; if (!w) return;
      const b = $('#vcopy'); const text = w.glsl;
      try { await navigator.clipboard.writeText(text); b.textContent = 'Copied'; }
      catch (e) { b.textContent = 'Select the text'; try { const r = document.createRange(); r.selectNodeContents($('#vcode')); const s = getSelection(); s.removeAllRanges(); s.addRange(r); } catch (e2) { /* nothing to select with */ } }
      setTimeout(() => { b.textContent = 'Copy the shader'; }, 1600);
    });
    if (window.ResizeObserver) new ResizeObserver(() => fitView()).observe(V.stage);
    window.addEventListener('resize', () => { if (G.open) fitView(); });
  }
  function fitView() {
    const r = V.stage.getBoundingClientRect();
    if (!r.width) return;
    const L = r.width < 520 ? LN : LW;
    if (L !== V.L || !V.box) {
      V.L = L; V.mock.innerHTML = L.html; V.mock.style.width = L.W + 'px'; V.mock.style.height = L.H + 'px';
      Object.assign(V, packShapes(L)); fillMock();
    }
    V.mock.style.setProperty('--mk', (r.width / L.W).toFixed(5));
    let dpr = Math.min(window.devicePixelRatio || 1, 2);
    while (dpr > 1 && r.width * r.height * dpr * dpr > 2.6e6) dpr -= 0.25;
    const w = Math.round(r.width * dpr), h = Math.round(r.height * dpr);
    if (w !== V.size[0] || h !== V.size[1]) { V.size = [w, h]; V.cv.width = w; V.cv.height = h; }
    V.dirty = true; wake();
  }
  function fillMock() {
    const w = LIB.byId[G.open]; if (!w) return;
    $$('[data-m="name"]', V.mock).forEach((e) => { e.textContent = w.name; });
    $$('[data-m="pack"]', V.mock).forEach((e) => { e.textContent = packOf(w).name; });
    $$('.trk', V.mock).forEach((e) => e.style.setProperty('--tw', Math.round((ST[w.id].speed - 0.25) / 2.25 * 100) + '%'));
  }
  const xfmt = (x) => (Math.round(x * 100) / 100).toFixed(2).replace(/0$/, '').replace(/\.0$/, '') + 'x';
  function setRange(r, v, min, max) { r.value = String(v); r.style.setProperty('--p', ((v - min) / (max - min) * 100).toFixed(1) + '%'); }
  function renderView() {
    const w = LIB.byId[G.open]; if (!w) return;
    const st = ST[w.id], o2 = w.options[1], r = RES[w.id];
    const T = TIERS.find((x) => x.id === w.tier);
    V.el.dataset.tier = w.tier;
    $('#vtier').textContent = T.name;
    $('#vtier').style.color = 'var(--tier' + T.name[0] + ')';
    $('#vname').textContent = w.name;
    $('#vdesc').textContent = w.desc;
    const packs = $('#vpacks');
    if (packs.__id !== w.id) {
      packs.__id = w.id; packs.innerHTML = '';
      packsOf(w).forEach((pk, i) => {
        const b = document.createElement('button');
        b.type = 'button'; b.className = 'pk' + (pk.id === 'match' ? ' mt' : ''); b.dataset.pack = pk.id; b.setAttribute('role', 'radio'); b.title = pk.name + (i === 0 ? ' (default)' : pk.id === 'match' ? ' (from the accent colour)' : ''); b.setAttribute('aria-label', pk.name);
        if (pk.id === w.defaults.pack) b.innerHTML = '<span class="def" aria-hidden="true"></span>';
        b.addEventListener('click', () => setPack(w, pk.id));
        packs.appendChild(b);
      });
    }
    $$('.pk', packs).forEach((b) => {
      const pk = findPack(w, b.dataset.pack);
      const bg = swatch(pk, G.dark);
      if (b.__bg !== bg) { b.style.background = bg; b.__bg = bg; }
      b.setAttribute('aria-checked', String(st.pack === pk.id));
    });
    $$('#vacc .ac').forEach((b) => b.setAttribute('aria-checked', String(st.pack === 'match' && b.dataset.hex === G.accent)));
    const cu = $('#vacccustom'); if (cu.value !== G.accent) cu.value = G.accent;
    $('.accust', V.el).setAttribute('aria-checked', String(st.pack === 'match' && !ACCENTS.some(([h]) => h.toLowerCase() === G.accent)));
    $('#vpackname').textContent = packOf(w).name + (st.pack === w.defaults.pack ? ' · default' : '');
    setRange($('#vspeed'), st.speed, 0.25, 2.5); $('#vspeedv').textContent = xfmt(st.speed);
    setRange($('#vmotion'), st.motion, 0, 1); $('#vmotionv').textContent = Math.round(st.motion * 100) + '%';
    $('#vmotionlo').textContent = w.options[0].lo; $('#vmotionhi').textContent = w.options[0].hi;
    $('#vsecondl').textContent = o2.label;
    setRange($('#vsecond'), st.second, 0, 1); $('#vsecondv').textContent = Math.round(st.second * 100) + '%';
    $('#vsecond').setAttribute('aria-label', o2.label);
    $('#vsecondlo').textContent = o2.lo; $('#vsecondhi').textContent = o2.hi;
    setRange($('#vfocus'), st.focus, 0, 1); $('#vfocusv').textContent = st.focus < 0.005 ? 'Sharp' : Math.round(st.focus * 100) + '%';
    const cl = clockOf();
    $('#vdynsw').setAttribute('aria-checked', String(st.dynamic)); $('#vdyn').classList.toggle('open', st.dynamic);
    $('#vdynv').textContent = st.dynamic ? hhmm(cl) + ' · ' + keyName(cl) : 'Off';
    $('#vclock').disabled = !st.dynamic; $$('#vkeys button').forEach((b) => { b.disabled = !st.dynamic; b.setAttribute('aria-pressed', String(b.dataset.key === 'now' ? G.clock == null : G.clock != null && Math.abs(G.clock - KEYT[b.dataset.key]) < 0.01)); });
    setRange($('#vclock'), cl, 0, 24);
    const ok = tiltOK(), pe = $('#vpersp');
    pe.setAttribute('aria-checked', String(st.persp && ok)); pe.disabled = !ok;
    $('#vperspnote').textContent = ok ? (w.perspective.mode === 'layers' ? 'The layers shift a few pixels with the pointer.' : 'A flat picture: the whole picture pans a few pixels.') : 'Off: Reduce Motion is on, or Motion is paused.';
    $('#vloop').textContent = w.period + ' s';
    $('#vloops').textContent = st.speed === 1 ? 'at 1x, the default speed' : 'at 1x; ' + (w.period / st.speed).toFixed(1) + ' s at ' + xfmt(st.speed);
    const pp = r.proofs[st.pack];
    const pb = $('#vproof');
    if (pp) {
      pb.textContent = 'Δ ' + Math.max(pp.d, pp.l) + '/255'; pb.className = Math.max(pp.d, pp.l) <= 1 ? 'ok' : '';
      $('#vproofs').textContent = 'frame 0 vs frame T, dark and light, this pack' + (pp.custom ? ' and these options' : '') + '; at T/2 it differs by ' + Math.max(pp.hd, pp.hl);
    } else { pb.textContent = 'proving'; pb.className = ''; $('#vproofs').textContent = 'frame 0 vs frame T'; }
    $('#vcost').textContent = r.ms == null ? 'measuring' : fmt(r.ms, r.ms < 0.1 ? 3 : 2) + ' ms';
    $('#vcosts').textContent = r.ms == null ? 'at 1280 x 800' : 'per frame at 1280 x 800, ' + (r.custom ? 'these settings' : 'default pack') + '; the budget is 1 ms';
    $('#vpacksall').textContent = w.packs.length + ' packs, all proved: max Δ ' + (r.max == null ? '-' : r.max + '/255');
    const code = $('#vcode');
    if (code.__id !== w.id) { code.__id = w.id; code.textContent = w.glsl; }
    fillMock();
  }
  let lastTile = null;
  function openView(id, fromEl) {
    const w = LIB.byId[id]; if (!w) return;
    const first = !G.open;
    lastTile = fromEl || lastTile;
    G.open = id;
    RT[id].dirty = true; V.dirty = true;
    renderView();
    V.el.classList.add('on'); V.el.setAttribute('aria-hidden', 'false');
    document.documentElement.style.overflow = 'hidden';
    fitView();
    if (first && fromEl) {
      // the stage grows out of the tile it was opened from
      const a = $('.tc', fromEl).getBoundingClientRect(), b = V.stage.getBoundingClientRect();
      if (b.width && a.width) {
        V.stage.style.transition = 'none';
        V.stage.style.transform = 'translate(' + (a.left - b.left) + 'px,' + (a.top - b.top) + 'px) scale(' + (a.width / b.width) + ',' + (a.height / b.height) + ')';
        V.stage.style.borderRadius = '40px';
        void V.stage.offsetWidth;
        V.stage.style.transition = 'transform .52s cubic-bezier(.32,.72,0,1), border-radius .52s cubic-bezier(.32,.72,0,1)';
        V.stage.style.transform = 'none'; V.stage.style.borderRadius = '';
      }
      setTimeout(() => { try { $('#vclose').focus({ preventScroll: true }); } catch (e) { /* focus unsupported */ } }, 60);
    }
    if (RES[id].max == null || RES[id].ms == null) proveNow(w, false);
    wake();
  }
  function closeView() {
    if (!G.open) return;
    const t = lastTile && document.contains(lastTile) ? lastTile : $('.tile[data-id="' + G.open + '"]');
    const a = t ? $('.tc', t).getBoundingClientRect() : null, b = V.stage.getBoundingClientRect();
    if (a && b.width && a.width && a.bottom > 0 && a.top < innerHeight) {
      V.stage.style.transition = 'transform .42s cubic-bezier(.32,.72,0,1), border-radius .42s ease';
      V.stage.style.transform = 'translate(' + (a.left - b.left) + 'px,' + (a.top - b.top) + 'px) scale(' + (a.width / b.width) + ',' + (a.height / b.height) + ')';
    }
    V.el.classList.remove('on'); V.el.setAttribute('aria-hidden', 'true');
    document.documentElement.style.overflow = '';
    const id = G.open; G.open = null;
    tiles.forEach((x) => { RT[x.w.id].dirty = true; });
    setTimeout(() => { if (!G.open) { V.stage.style.transition = 'none'; V.stage.style.transform = 'none'; } }, 460);
    if (t) { try { t.focus({ preventScroll: true }); } catch (e) { /* focus unsupported */ } }
    renderTiles();
    wake();
    return id;
  }
  function nav(d) {
    if (!G.open) return;
    const i = WALLS.findIndex((w) => w.id === G.open);
    const n = WALLS[(i + d + WALLS.length) % WALLS.length];
    V.cv.classList.add('fade');
    $('#vname').style.opacity = '0'; $('#vdesc').style.opacity = '0';
    setTimeout(() => {
      lastTile = $('.tile[data-id="' + n.id + '"]');
      openView(n.id, null);
      requestAnimationFrame(() => { V.cv.classList.remove('fade'); $('#vname').style.opacity = ''; $('#vdesc').style.opacity = ''; });
    }, 200);
  }

  // ------------------------------------------------------------------ the loop proofs and the frame cost, measured here
  const PW = 256, PH = 160;
  // one loop proof of a state in both themes; x holds the extra uniforms (tilt, focus); clk is the clock hour of the look, or null
  function proofState(w, pk, opt, clk, x) {
    const one = (dk) => { const pf = palFor(w, pk, dk, clk); return E.proof(w.id, Object.assign({ pal: pf.pal, dark: dk, opt, sun: pf.sun }, x || {}), PW, PH); };
    const d = one(1), l = one(0);
    if (!d || !l) return null;
    return { d: d.dT, l: l.dT, hd: d.dH, hl: l.dH };
  }
  const proofPack = (w, pk, opt) => proofState(w, pk, opt, null, null);
  function proveAll(w) {
    const r = RES[w.id], def = [w.defaults.motion, w.defaults.second];
    let mx = 0, hdef = 999, n = 0;
    const take = (p) => { mx = Math.max(mx, p.d, p.l); n += 2; };
    // the five named packs and Match accent, with the default options
    for (const pk of packsOf(w)) {
      const p = proofPack(w, pk, def); if (!p) return false;
      r.proofs[pk.id] = p; take(p); hdef = Math.min(hdef, p.hd, p.hl);
    }
    let hmin = hdef;
    // the options at both ends, on the default pack
    for (const o of [[0, 0], [1, 1]]) { const p = proofPack(w, w.packs[0], o); take(p); hmin = Math.min(hmin, p.hd, p.hl); }
    // Dynamic: the four key looks and the light track, on the default pack
    r.dyn = {};
    for (const k of ['dawn', 'day', 'dusk', 'night']) { const p = proofState(w, w.packs[0], def, KEYT[k], null); take(p); r.dyn[k] = Math.max(p.d, p.l); }
    // Perspective at full tilt and Focus at its extremes, together with the other options at their ends
    const px = proofState(w, w.packs[0], [1, 1], 18, { tilt: [1, 1], focus: 1 }); take(px); r.ext = Math.max(px.d, px.l);
    const py = proofState(w, matchOf(w), [0, 0], 0, { tilt: [-1, 1], focus: 0.5 }); take(py); r.ext = Math.max(r.ext, py.d, py.l);
    r.max = mx; r.halfMin = hmin; r.halfDef = hdef; r.checks = n;
    return true;
  }
  function measure(w, custom) {
    const st = ST[w.id];
    const pk = custom ? packOf(w) : w.packs[0];
    const opt = custom ? [st.motion, st.second] : [w.defaults.motion, w.defaults.second];
    const pf = palFor(w, pk, G.dark, custom && st.dynamic ? clockOf() : null);
    const s = { pal: pf.pal, dark: G.dark, opt, sun: pf.sun, tilt: custom && st.persp && tiltOK() ? [0.5, 0.5] : [0, 0], focus: custom ? st.focus : 0 };
    const b = E.bench(w.id, s, 48);
    if (!b) return false;
    RES[w.id].ms = b.ms; RES[w.id].base = b.base; RES[w.id].custom = !!custom;
    const f = E.bench(w.id, Object.assign({}, s, { focus: 1 }), 24);
    if (f) RES[w.id].msFocus = f.ms;
    return true;
  }
  function proveNow(w, custom) {
    if (!E.prog(w.id)) { setTimeout(() => proveNow(w, custom), 120); return; }
    if (custom) {
      const st = ST[w.id];
      const p = proofState(w, packOf(w), [st.motion, st.second], st.dynamic ? clockOf() : null, { tilt: st.persp && tiltOK() ? [1, 1] : [0, 0], focus: st.focus });
      if (p) { p.custom = true; RES[w.id].proofs[st.pack] = p; }
    } else if (RES[w.id].max == null) proveAll(w);
    measure(w, custom);
    renderView(); renderTiles(); renderStats(); wake();
  }
  // the queue: every wallpaper, in page order, once its program is ready; a pause between them keeps the page smooth
  const queue = WALLS.slice();
  function runQueue() {
    if (!E.ok) { renderStats(); return; }
    if (G.open) { setTimeout(runQueue, 300); return; }
    const i = queue.findIndex((w) => E.prog(w.id));
    if (i < 0) { if (queue.length) setTimeout(runQueue, 100); return; }
    const w = queue.splice(i, 1)[0];
    if (RES[w.id].max == null) proveAll(w);
    if (RES[w.id].ms == null) measure(w, false);
    renderTiles(); renderStats(); wake();
    if (queue.length) setTimeout(runQueue, 70);
    else window.__libDone = true;
  }

  // ------------------------------------------------------------------ the summary at the top
  function renderStats() {
    const done = WALLS.filter((w) => RES[w.id].max != null);
    const costs = WALLS.map((w) => RES[w.id].ms).filter((x) => x != null);
    const packs = WALLS.reduce((s, w) => s + w.packs.length, 0);
    $('#sPacks').textContent = String(packs);
    if (!E.ok) { $('#sProof').textContent = 'no WebGL2'; $('#sProofs').textContent = 'this browser gave no WebGL2 context, so nothing can be drawn or proved here'; return; }
    const mx = done.length ? Math.max.apply(null, done.map((w) => RES[w.id].max)) : null;
    const checks = done.reduce((s, w) => s + RES[w.id].checks, 0);
    const hmin = done.length ? Math.min.apply(null, done.map((w) => RES[w.id].halfDef)) : null;
    $('#sProof').textContent = mx == null ? 'proving' : 'Δ ' + mx + '/255';
    $('#sProof').style.color = mx != null && mx <= 1 ? 'var(--lib-good)' : '';
    $('#sProofs').textContent = done.length + ' of ' + WALLS.length + ' proved, ' + checks + ' checks: frame 0 vs frame T, every pack, dark and light' + (hmin != null ? '; at T/2, with the default options, each differs by at least ' + hmin + ' (the minimal tier moves least)' : '');
    if (costs.length) {
      const lo = Math.min.apply(null, costs), hi = Math.max.apply(null, costs);
      $('#sCost').textContent = fmt(lo, 2) + ' to ' + fmt(hi, 2) + ' ms';
      $('#sCosts').textContent = costs.length + ' of ' + WALLS.length + ' measured at 1280 x 800 on ' + gpuName(E.renderer) + '; budget 1 ms';
    }
    window.__lib = { RES, done: done.length, max: mx, checks, costs, renderer: E.renderer, hmin };
  }
  function gpuName(r) {
    const m = /(NVIDIA GeForce [^(,]+|AMD Radeon[^(,]+|Intel\(R\)[^(,]+|Apple [^,)]+|SwiftShader)/.exec(r || '');
    return m ? m[1].trim() : (r || 'this GPU');
  }

  // ------------------------------------------------------------------ the toolbar
  function buildToolbar() {
    K.seg($('#segT'), [{ v: 'auto', t: 'Auto' }, { v: 'light', t: 'Light' }, { v: 'dark', t: 'Dark' }], K.themeMode(), (v) => K.theme(v));
    K.seg($('#segTier'), [{ v: 'all', t: 'All' }].concat(TIERS.map((T) => ({ v: T.id, t: T.name }))), 'all', (v) => showTier(v));
    const play = $('#play');
    play.setAttribute('aria-checked', String(G.play));
    play.addEventListener('click', () => { G.play = !G.play; play.setAttribute('aria-checked', String(G.play)); renderView(); wake(); });
    K.onTheme((d) => { G.dark = d ? 1 : 0; renderTiles(); renderView(); wake(); });
  }
  function showTier(v) {
    $$('.tier').forEach((s) => {
      const on = v === 'all' || s.dataset.tier === v;
      if (on) { s.hidden = false; requestAnimationFrame(() => s.classList.remove('off')); }
      else { s.classList.add('off'); setTimeout(() => { if (s.classList.contains('off')) s.hidden = true; }, 360); }
    });
    setTimeout(() => tiles.forEach(fitTile), 380);
  }

  buildTiers();
  buildView();
  buildToolbar();
  renderTiles();
  renderStats();
  wake();
  setTimeout(runQueue, 250);
  window.__E = E; window.__ST = ST; window.__RT = RT; window.__G = G; window.__open = openView; window.__close = closeView; window.__tiles = tiles; window.__V = V;
  window.__fn = { palFor, look, matchOf, packsOf, target, step, tiltOK, keyName, hhmm, setClock, setAccent, KEYT };
})();
