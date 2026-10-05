// Static Wallpapers: the library page. Reads the wallpapers from the page's own JSON block (the same data the app
// ports), draws the large view live with the glass (sw-gl.js Viewer) and every thumbnail with one shared renderer, and
// keeps the viewer's choices per viewer when the browser allows. Every change crossfades or eases (OD-0076). Nothing is
// sent anywhere.
(function () {
  const K = window.Kit, S = K.store, PAGE = 'swlib';
  const D = JSON.parse(document.getElementById('wallpapers').textContent);
  const C = D.contract;
  const $ = (s, r) => (r || document).querySelector(s);
  const $$ = (s, r) => Array.from((r || document).querySelectorAll(s));
  const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const W = D.wallpapers, byId = {};
  W.forEach((w) => { w.eng = { id: w.id, fs: C.head + C.lib + w.src + '\n' + C.main, grain: w.grain.def, opt: { def: w.option.def }, dyn: w.dyn, light: w.light, persp: w.persp, focus: w.focus }; byId[w.id] = w; });
  const TIER = {}; D.tiers.forEach((t) => { TIER[t.id] = t; });
  const dprNow = () => Math.min(window.devicePixelRatio || 1, 2);
  const fmt = (x) => (x == null ? '-' : x < 10 ? x.toFixed(2) : x.toFixed(1));
  const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
  const smooth = (x) => x * x * (3 - 2 * x);
  const RM = window.matchMedia ? matchMedia('(prefers-reduced-motion: reduce)') : { matches: false };

  // ------------------------------------------------------------------ state (a per-viewer convenience only)
  const readJ = (k, d) => { try { const v = JSON.parse(S.get(PAGE + '.' + k, 'null')); return v == null ? d : v; } catch (e) { return d; } };
  const now = new Date();
  const G = { dark: K.isDark(), sel: S.get(PAGE + '.sel', W[0].id), glass: S.get(PAGE + '.glass', '1') !== '0', tier: S.get(PAGE + '.tier', 'all'), per: readJ('per', {}),
    accent: /^#[0-9a-f]{6}$/i.test(S.get(PAGE + '.accent', '')) ? S.get(PAGE + '.accent', '') : D.options.accent.default,
    clock: now.getHours() + now.getMinutes() / 60, ptr: [0, 0], rm: !!RM.matches };
  if (!byId[G.sel]) G.sel = W[0].id;
  if (['all', 'vibrant', 'balanced', 'minimal'].indexOf(G.tier) < 0) G.tier = 'all';
  // a wallpaper's choices: pack (the last index, packs.length, is Match accent), its option, grain, Focus, and the two switches
  function per(id) {
    const w = byId[id], p = G.per[id] || {}, num = (v, d) => (typeof v === 'number' && isFinite(v) ? v : d);
    return { pack: p.pack >= 0 && p.pack <= w.packs.length ? p.pack : w.defaultPack, opt: num(p.opt, w.option.def), grain: num(p.grain, w.grain.def), focus: clamp(num(p.focus, w.focus.def), 0, 1),
      dyn: typeof p.dyn === 'boolean' ? p.dyn : false, persp: typeof p.persp === 'boolean' ? p.persp : w.persp.def === 1 };
  }
  function setPer(id, o) { G.per[id] = Object.assign(per(id), o); S.set(PAGE + '.per', JSON.stringify(G.per)); }
  // Match accent: a sixth pack, built here from the accent colour (the algorithm is in the data: options.accent.method)
  const accCache = {};
  function accPack(w, hex) {
    hex = hex || G.accent;
    const c = accCache[w.id + hex]; if (c) return c;
    const r = SWC.resolvePack(w.roles, SWC.accentSpec(w.roles, w.hues, hex));
    return (accCache[w.id + hex] = { name: 'Match accent', kind: 'accent', dark: r.dark, light: r.light });
  }
  const packOf = (w, i, hex) => (i >= w.packs.length ? accPack(w, hex) : w.packs[i]);
  function palFor(w, pi, dark, hex) {
    const pk = packOf(w, pi, hex), h6 = pk[dark ? 'dark' : 'light'], a = new Float32Array(18);
    h6.forEach((h, i) => a.set(SWC.hex2lab(h), i * 3)); return a;
  }
  // what one wallpaper is drawn with, from a wallpaper's choices p (per()), the theme and the page's clock, accent and pointer:
  // the palette (shifted by the clock when Dynamic is on), u_dark, and the uniforms of Perspective, light and Focus. H is the
  // target height in device pixels (the Focus blur is a share of it); ctx overrides clock, accent and the pointer.
  function buildSpec(w, p, dark, H, ctx) {
    ctx = ctx || {};
    let pal = palFor(w, p.pack, dark, ctx.accent), dk = dark ? 1 : 0;
    const x = SWG.wallX(w.eng);
    if (p.dyn) { const d = SWG.dynState(w.eng, pal, dk, ctx.clock != null ? ctx.clock : G.clock, 1, new Float32Array(18)); pal = d.pal; dk = d.dark; x.light = d.light; }
    if (ctx.tilt && p.persp) {
      let tx = ctx.tilt[0], ty = ctx.tilt[1]; const l = Math.hypot(tx, ty); if (l > 1) { tx /= l; ty /= l; }
      const f = SWG.AMP * w.persp.depth; x.tilt = [tx * f, -ty * f];
    }
    const fo = smooth(clamp(p.focus, 0, 1));
    if (w.focus.own) x.focus = fo; else { x.blur = fo > 0.004 ? fo * SWG.FOCUS_SIG * H : 0; x.dream = x.blur > 0 ? fo : 0; }
    return { pal, dark: dk, x };
  }
  const lab2css = (L, a, b) => 'rgb(' + SWC.lab2lin(L, a, b).map((v) => Math.round(clamp(SWC.l2s(clamp(v, 0, 1)), 0, 1) * 255)).join(',') + ')';
  const clockName = (h) => { h = ((h % 24) + 24) % 24; return h < 2 || h >= 22 ? 'Night' : h < 4.5 ? 'Before dawn' : h < 7.5 ? 'Dawn' : h < 10.5 ? 'Morning' : h < 13.5 ? 'Day' : h < 16.5 ? 'Afternoon' : h < 19.5 ? 'Dusk' : 'Evening'; };
  const clockStr = (h) => { h = ((h % 24) + 24) % 24; let m = Math.round(h * 60); m %= 1440; return String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0'); };

  // ------------------------------------------------------------------ the mock Marrow window over the large view
  const IC = {
    plus: '<path d="M8 3.5v9M3.5 8h9"/>',
    doc: '<path d="M4.5 2.5h5l2.5 2.5v8.5h-7.5z M9.5 2.5v2.5h2.5"/>',
    mic: '<rect x="6" y="2.2" width="4" height="7.2" rx="2"/><path d="M4 8a4 4 0 0 0 8 0M8 12v2"/>',
    img: '<rect x="2.5" y="3.5" width="11" height="9" rx="2"/><circle cx="6" cy="7" r="1"/><path d="M3 11.5l3.2-3 2.3 2 2-1.8 2.5 2.3"/>',
    set: '<path d="M3 5h10M3 11h10"/><circle cx="6" cy="5" r="1.6"/><circle cx="10.5" cy="11" r="1.6"/>',
  };
  const ico = (n) => '<svg viewBox="0 0 16 16" aria-hidden="true">' + IC[n] + '</svg>';
  const SEND = '<svg viewBox="0 0 18 18" aria-hidden="true"><path d="M9 14.5V4M4.8 8.2 9 4l4.2 4.2"/></svg>';
  const box = (cls, x, y, w, h, inner) => '<div class="' + cls + '" style="left:' + x + 'px;top:' + y + 'px;width:' + w + 'px;height:' + h + 'px">' + (inner || '') + '</div>';
  const panelHTML = (x, y, w, h) => box('pn', x, y, w, h, '<h4>Wallpaper</h4><div class="row"><span>Picture</span><span class="fade" data-mk="name"></span></div>'
    + '<div class="row"><span>Colours</span><span class="fade" data-mk="pack"></span></div><div class="row"><span>Follow theme</span><span><i class="mt"></i></span></div>');
  function layout(narrow) {
    const sh = [], h = [];
    const g = (x, y, w, hh, r, tint, extra) => sh.push(Object.assign({ x, y, w, h: hh, r, tint }, extra || {}));
    if (!narrow) {
      h.push(box('brand', 22, 22, 150, 24, '<i></i>Marrow'));
      [['New chat', 'plus', 74, 'new'], ['Release notes', 'doc', 120], ['Voice typing', 'mic', 166], ['Wallpapers', 'img', 212, 'on'], ['Settings', 'set', 502]].forEach(([t, ic, y, s]) => {
        g(16, y, 200, 38, 19, s === 'on' ? .3 : .15, s === 'new' ? { acc: .26 } : {});
        h.push(box('cap' + (s === 'on' ? ' on' : ''), 16, y, 200, 38, ico(ic) + '<span>' + t + '</span>'));
      });
      g(485, 20, 150, 32, 16, .16); h.push(box('ttl', 485, 20, 150, 32, 'Wallpapers'));
      g(536, 86, 330, 56, 26, .22, { acc: .3 }); h.push(box('bub u', 536, 86, 330, 56, 'Something calm behind the glass.'));
      g(252, 162, 424, 100, 26, .18);
      h.push(box('bub', 252, 162, 424, 100, 'This one is drawn once at the size of the window and kept, so the glass over it costs nothing while you read.'));
      g(618, 288, 258, 170, 28, .26, { blur: 1.5 }); h.push(panelHTML(618, 288, 258, 170));
      g(236, 482, 648, 58, 29, .2); h.push(box('cmpt', 236, 482, 560, 58, 'Ask Marrow anything'));
      g(834, 491, 40, 40, 20, .1, { acc: .62 }); h.push(box('snd', 834, 491, 40, 40, SEND));
      return { W: 900, H: 560, shapes: sh, html: h.join('') };
    }
    h.push(box('brand', 14, 22, 40, 22, '<i></i>'));
    [['plus', 70, 'new'], ['doc', 118], ['mic', 166], ['img', 214, 'on'], ['set', 494]].forEach(([ic, y, s]) => {
      g(8, y, 44, 44, 22, s === 'on' ? .3 : .15, s === 'new' ? { acc: .26 } : {});
      h.push(box('cap ic' + (s === 'on' ? ' on' : ''), 8, y, 44, 44, ico(ic)));
    });
    g(148, 18, 130, 32, 16, .16); h.push(box('ttl', 148, 18, 130, 32, 'Wallpapers'));
    g(96, 78, 270, 56, 26, .22, { acc: .3 }); h.push(box('bub u', 96, 78, 270, 56, 'Something calm behind the glass.'));
    g(66, 150, 300, 118, 26, .18);
    h.push(box('bub', 66, 150, 300, 118, 'This one is drawn once at the size of the window and kept, so the glass over it costs nothing while you read.'));
    g(94, 284, 272, 164, 28, .26, { blur: 1.5 }); h.push(panelHTML(94, 284, 272, 164));
    g(62, 484, 312, 58, 29, .2); h.push(box('cmpt', 62, 484, 230, 58, 'Ask Marrow'));
    g(328, 493, 40, 40, 20, .1, { acc: .62 }); h.push(box('snd', 328, 493, 40, 40, SEND));
    return { W: 380, H: 560, shapes: sh, html: h.join('') };
  }
  const LW = layout(false), LN = layout(true);

  // ------------------------------------------------------------------ the large view
  const vst = $('#vst'), mock = document.createElement('div');
  mock.className = 'mock'; vst.appendChild(mock);
  const V = { st: null, cv: null, narrow: null, size: null, L: LW };
  function fitView() {
    const w = vst.clientWidth; if (!w) return;
    const narrow = w < 520, L = narrow ? LN : LW;
    if (narrow !== V.narrow) { V.narrow = narrow; V.L = L; mock.innerHTML = L.html; mock.style.width = L.W + 'px'; mock.style.height = L.H + 'px'; if (V.st) V.st.setShapes(L.shapes); mockText(true); }
    const h = Math.round(w * L.H / L.W);
    vst.style.height = h + 'px';
    mock.style.setProperty('--mk', (w / L.W).toFixed(5));
    let dpr = dprNow();
    while (dpr > 1 && w * h * dpr * dpr > 3.6e6) dpr -= .25;
    V.size = [w, h, dpr];
    if (V.st) { V.st.resize(w, h, dpr, L.W, L.H); wake(); }
  }
  function viewTargets(st, id, snap) {
    const w = byId[id], p = per(id);
    st.target({ pal: palFor(w, p.pack, G.dark), dark: G.dark ? 1 : 0, grain: p.grain, opt: p.opt, glass: G.glass ? 1 : 0, focus: p.focus,
      dyn: p.dyn ? 1 : 0, clock: G.clock, persp: p.persp && !G.rm ? 1 : 0, tilt: G.ptr });
    if (snap) st.snap();
  }
  function attach(id) {
    if (!V.size) return;
    const cv = document.createElement('canvas');
    vst.insertBefore(cv, mock);
    const st = new SWG.Viewer(cv, byId[id].eng);
    if (!st.ok) {
      cv.remove();
      if (!V.fail) { V.fail = document.createElement('div'); V.fail.className = 'wfail'; vst.insertBefore(V.fail, mock); }
      V.fail.textContent = 'This browser did not give a WebGL2 context' + (st.err ? ' (' + st.err.slice(0, 120) + ')' : '') + ', so the wallpaper cannot be drawn here.';
      return;
    }
    const [w, h, dpr] = V.size;
    st.resize(w, h, dpr, V.L.W, V.L.H); st.setShapes(V.L.shapes);
    viewTargets(st, id, true); st.render();
    const old = V.cv, oldSt = V.st;
    V.cv = cv; V.st = st; V.id = id;
    requestAnimationFrame(() => requestAnimationFrame(() => cv.classList.add('in')));
    if (old) { old.classList.remove('in'); setTimeout(() => { old.remove(); oldSt.dispose(); }, 620); }
    wake();
  }
  let raf = 0, last = 0;
  function wake() { if (!raf) { last = performance.now(); raf = requestAnimationFrame(loop); } }
  function loop(now) {
    raf = 0;
    const dt = Math.min(.08, Math.max(.001, (now - last) / 1000)); last = now;
    const st = V.st; if (!st || !st.ok) return;
    if (st.tick(dt)) st.render();
    if (st.busy) raf = requestAnimationFrame(loop);
  }
  if (window.ResizeObserver) new ResizeObserver(() => fitView()).observe(vst);
  window.addEventListener('resize', fitView);

  // ------------------------------------------------------------------ the control panel
  const pbody = $('#pbody'), packsEl = $('#packs'), optR = $('#opt'), grR = $('#grain'), foR = $('#focus'), clR = $('#clock'), glBtn = $('#glass');
  const dynBtn = $('#dyn'), perBtn = $('#persp'), accFold = $('#accFold'), dynFold = $('#dynFold'), looksEl = $('#looks'), accsEl = $('#accs'), accIn = $('#accc');
  // a folding group: it opens and closes by an eased height and fade, and takes no focus while closed
  function fold(el, on) { el.classList.toggle('open', !!on); el.firstElementChild.inert = !on; }
  const KEYS4 = [['Dawn', 6], ['Day', 12], ['Dusk', 18], ['Night', 0]];
  function swatchBg(hex) {
    return 'conic-gradient(from 210deg,' + [hex[1], hex[2], hex[3], hex[4], hex[0], hex[1]].join(',') + ')';
  }
  // the accent swatch paints its five colours through registered properties (--a0 .. --a4), so a new accent crossfades
  function paintAccSwatch(b, w) {
    const pk = accPack(w), order = [1, 2, 3, 4, 0];
    ['l', 'd'].forEach((m) => { const i = b.querySelector('i.' + m); const hex = pk[m === 'l' ? 'light' : 'dark']; order.forEach((r, n) => i.style.setProperty('--a' + n, hex[r])); });
  }
  function setRange(r, v) { r.value = String(v); r.style.setProperty('--p', (v * 100).toFixed(1) + '%'); const o = r.parentNode.querySelector('output'); if (o) o.textContent = Math.round(v * 100); }
  function setClockUI(h) {
    h = clamp(h, 0, 24);
    clR.value = String(h); clR.style.setProperty('--p', (h / 24 * 100).toFixed(1) + '%');
    $('#clk').textContent = clockStr(h) + '  ' + clockName(h);
    clR.setAttribute('aria-valuetext', clockStr(h) + ', ' + clockName(h));
    let near = -1, best = 1.51;
    KEYS4.forEach(([n, kh], i) => { const d = Math.min(Math.abs(h - kh), Math.abs(h - kh - 24), Math.abs(h - kh + 24)); if (d < best) { best = d; near = i; } });
    $$('.lk', looksEl).forEach((b, i) => b.setAttribute('aria-pressed', String(i === near)));
  }
  // the four key looks of this wallpaper, as three-colour strips, and the scrubber's track: the same colours, round the clock
  function paintLooks() {
    const w = byId[G.sel], p = per(G.sel), pal = palFor(w, p.pack, G.dark), dk = G.dark ? 1 : 0, tmp = new Float32Array(18);
    const at = (h) => SWG.dynState(w.eng, pal, dk, h, 1, tmp).pal;
    const css = (pl, r) => lab2css(pl[r * 3], pl[r * 3 + 1], pl[r * 3 + 2]);
    $$('.lk', looksEl).forEach((b, i) => { const pl = at(KEYS4[i][1]); [1, 2, 3].forEach((r, n) => b.style.setProperty('--c' + n, css(pl, r))); });
    [0, 6, 12, 18, 24].forEach((h, n) => clR.style.setProperty('--k' + n, css(at(h), 2)));
  }
  function fillPacks() {
    const w = byId[G.sel], p = per(G.sel);
    packsEl.innerHTML = '';
    w.packs.concat([{ name: 'Match accent', kind: 'accent', dark: [], light: [] }]).forEach((pk, i) => {
      const b = document.createElement('button'), acc = pk.kind === 'accent';
      b.type = 'button'; b.className = 'pk' + (acc ? ' ac' : ''); b.dataset.i = i; b.setAttribute('aria-pressed', String(i === p.pack));
      b.setAttribute('aria-label', pk.name + (acc ? ' (your accent colour)' : pk.kind !== 'standard' ? ' (' + pk.kind + ')' : '')); b.title = pk.name;
      if (acc) { b.innerHTML = '<i class="l"></i><i class="d"></i><span class="kd">A</span>'; paintAccSwatch(b, w); }
      else b.innerHTML = '<i class="l" style="background:' + swatchBg(pk.light) + '"></i><i class="d" style="background:' + swatchBg(pk.dark) + '"></i>' + (pk.kind === 'quiet' ? '<span class="kd">Q</span>' : pk.kind === 'rich' ? '<span class="kd">R</span>' : '');
      packsEl.appendChild(b);
    });
    $('#packn').textContent = packOf(w, p.pack).name;
    fold(accFold, p.pack >= w.packs.length);
  }
  function fillAccents() {
    accsEl.innerHTML = '';
    SWC.ACCENTS.forEach(([n, hex]) => {
      const b = document.createElement('button');
      b.type = 'button'; b.className = 'acs'; b.dataset.hex = hex; b.style.background = hex; b.title = n + ' ' + hex; b.setAttribute('aria-label', n + ' ' + hex);
      accsEl.appendChild(b);
    });
    syncAccentUI();
  }
  function syncAccentUI() {
    let nm = G.accent.toUpperCase();
    $$('.acs', accsEl).forEach((b) => { const on = b.dataset.hex.toLowerCase() === G.accent.toLowerCase(); b.setAttribute('aria-pressed', String(on)); if (on) nm = b.title; });
    $('#accn').textContent = nm; accIn.value = G.accent.toLowerCase();
    accIn.parentNode.style.setProperty('--cu', G.accent);
  }
  function syncSwitches() {
    const p = per(G.sel);
    dynBtn.setAttribute('aria-checked', String(p.dyn)); fold(dynFold, p.dyn);
    const off = G.rm;
    perBtn.setAttribute('aria-checked', String(p.persp && !off)); perBtn.setAttribute('aria-disabled', String(off));
    const pn = $('#perspn'), t = off ? 'Off while Reduce Motion is on' : p.persp ? 'The picture drifts a few pixels with your pointer' : 'Still';
    if (pn.textContent !== t) { if (!pn.textContent) pn.textContent = t; else { pn.style.opacity = '0'; setTimeout(() => { pn.textContent = t; pn.style.opacity = '1'; }, 200); } }
  }
  function fillPanel() {
    const w = byId[G.sel], p = per(G.sel);
    $('#vname').textContent = w.name;
    const tg = $('#vtier'); tg.textContent = TIER[w.tier].name; tg.className = 'tag t-' + w.tier;
    $('#vpacks').textContent = (w.packs.length + 1) + ' colour packs';
    $('#vdesc').textContent = w.desc;
    $('#optl').textContent = w.option.label;
    fillPacks(); syncAccentUI();
    setRange(optR, p.opt); setRange(grR, p.grain); setRange(foR, p.focus);
    syncSwitches(); setClockUI(G.clock); paintLooks();
    $('#src').textContent = w.src;
    costLine();
    mockText(false);
  }
  function mockText(now) {
    const w = byId[G.sel], p = per(G.sel);
    $$('[data-mk]', mock).forEach((el) => {
      const t = el.dataset.mk === 'name' ? w.name : packOf(w, p.pack).name;
      if (el.textContent === t) return;
      if (now) { el.textContent = t; return; }
      el.style.opacity = '0'; setTimeout(() => { el.textContent = t; el.style.opacity = '1'; }, 220);
    });
  }
  // everything that follows a change of the wallpaper's choices: the view eases, the thumbnail redraws, the strips repaint
  function applyChoices(soon) {
    if (V.st) { viewTargets(V.st, G.sel); wake(); }
    thumbs[G.sel].refresh(soon);
  }
  packsEl.addEventListener('click', (e) => {
    const b = e.target.closest('.pk'); if (!b) return;
    const w = byId[G.sel], i = +b.dataset.i; setPer(G.sel, { pack: i });
    $$('.pk', packsEl).forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    $('#packn').textContent = packOf(w, i).name;
    fold(accFold, i >= w.packs.length);
    paintLooks(); mockText(false); applyChoices();
  });
  // the accent picker: ten presets and a custom colour; the change eases in as a palette crossfade (the page applies it at most once a frame)
  let accRaf = 0;
  function setAccent(hex) {
    G.accent = hex.toLowerCase(); S.set(PAGE + '.accent', G.accent); syncAccentUI();
    cancelAnimationFrame(accRaf);
    accRaf = requestAnimationFrame(() => {
      const w = byId[G.sel], b = $('.pk.ac', packsEl); if (b) paintAccSwatch(b, w);
      if (per(G.sel).pack >= w.packs.length) { paintLooks(); applyChoices(true); }
      Object.values(thumbs).forEach((t) => { if (t.w.id !== G.sel && per(t.w.id).pack >= t.w.packs.length) t.refresh(); });
    });
  }
  accsEl.addEventListener('click', (e) => { const b = e.target.closest('.acs'); if (b) setAccent(b.dataset.hex); });
  accIn.addEventListener('input', () => setAccent(accIn.value));
  optR.addEventListener('input', () => { const v = +optR.value; setRange(optR, v); setPer(G.sel, { opt: v }); if (V.st) { V.st.target({ opt: v }); wake(); } thumbs[G.sel].refresh(true); });
  grR.addEventListener('input', () => { const v = +grR.value; setRange(grR, v); setPer(G.sel, { grain: v }); if (V.st) { V.st.target({ grain: v }); wake(); } thumbs[G.sel].refresh(true); });
  foR.addEventListener('input', () => { const v = +foR.value; setRange(foR, v); setPer(G.sel, { focus: v }); if (V.st) { V.st.target({ focus: v }); wake(); } thumbs[G.sel].refresh(true); });
  // Dynamic: the switch, the clock scrubber, the four key looks and Now
  function setDyn(on) {
    setPer(G.sel, { dyn: on }); syncSwitches(); paintLooks(); applyChoices();
  }
  dynBtn.addEventListener('click', () => setDyn(!per(G.sel).dyn));
  function clockChanged(h, soon) {
    G.clock = h; setClockUI(h);
    if (V.st) { V.st.target({ clock: h }); wake(); }
    if (per(G.sel).dyn) thumbs[G.sel].refresh(soon);
  }
  clR.addEventListener('input', () => { tween.stop(); clockChanged(+clR.value, true); });
  clR.addEventListener('change', () => Object.values(thumbs).forEach((t) => { if (per(t.w.id).dyn) t.refresh(); }));
  // the scrubber's own thumb glides to a chosen time (the view eases on its own); under Reduce Motion it jumps
  const tween = { raf: 0, stop() { cancelAnimationFrame(this.raf); this.raf = 0; } };
  function glideClock(to) {
    tween.stop();
    const from = G.clock, t0 = performance.now(), dur = RM.matches ? 1 : 420;
    if (V.st) { V.st.target({ clock: to }); wake(); }
    const step = (t) => {
      const k = clamp((t - t0) / dur, 0, 1), e = 1 - Math.pow(1 - k, 3);
      G.clock = from + (to - from) * e; setClockUI(G.clock);
      if (k < 1) tween.raf = requestAnimationFrame(step); else { tween.raf = 0; clockChanged(to, false); Object.values(thumbs).forEach((x) => { if (per(x.w.id).dyn) x.refresh(); }); }
    };
    tween.raf = requestAnimationFrame(step);
  }
  looksEl.addEventListener('click', (e) => {
    const b = e.target.closest('.lk'); if (!b) return;
    const kh = KEYS4[+b.dataset.k][1];
    glideClock(kh === 0 ? (G.clock > 12 ? 24 : 0) : kh);
    if (!per(G.sel).dyn) setDyn(true);
  });
  $('#now').addEventListener('click', () => { const n = new Date(); glideClock(n.getHours() + n.getMinutes() / 60); if (!per(G.sel).dyn) setDyn(true); });
  // Perspective: the switch, the pointer, and Reduce Motion
  perBtn.addEventListener('click', () => { if (G.rm) return; setPer(G.sel, { persp: !per(G.sel).persp }); syncSwitches(); applyChoices(); });
  window.addEventListener('pointermove', (e) => {
    if (e.pointerType === 'touch') return;
    G.ptr = [(e.clientX / innerWidth) * 2 - 1, (e.clientY / innerHeight) * 2 - 1];
    if (V.st && !G.rm && per(G.sel).persp) { V.st.target({ tilt: G.ptr }); wake(); }
  }, { passive: true });
  document.addEventListener('mouseleave', () => { G.ptr = [0, 0]; if (V.st) { V.st.target({ tilt: G.ptr }); wake(); } });
  const onRM = () => { G.rm = !!RM.matches; syncSwitches(); if (V.st) { viewTargets(V.st, G.sel); wake(); } };
  if (RM.addEventListener) RM.addEventListener('change', onRM); else if (RM.addListener) RM.addListener(onRM);
  function setGlass(on) {
    G.glass = on; S.set(PAGE + '.glass', on ? '1' : '0');
    glBtn.setAttribute('aria-checked', String(on)); vst.dataset.glass = on ? '1' : '0';
    if (V.st) { V.st.target({ glass: on ? 1 : 0 }); wake(); }
  }
  glBtn.addEventListener('click', () => setGlass(!G.glass));
  $('#reset').addEventListener('click', () => {
    delete G.per[G.sel]; S.set(PAGE + '.per', JSON.stringify(G.per));
    const p = per(G.sel), w = byId[G.sel];
    $$('.pk', packsEl).forEach((x) => x.setAttribute('aria-pressed', String(+x.dataset.i === p.pack)));
    $('#packn').textContent = packOf(w, p.pack).name; fold(accFold, p.pack >= w.packs.length);
    setRange(optR, p.opt); setRange(grR, p.grain); setRange(foR, p.focus); syncSwitches(); paintLooks();
    mockText(false); applyChoices();
  });
  const code = $('#code'), codeBtn = $('#codeb');
  codeBtn.addEventListener('click', () => { const o = !code.classList.contains('open'); code.classList.toggle('open', o); codeBtn.setAttribute('aria-expanded', String(o)); codeBtn.textContent = o ? 'Hide shader' : 'Show shader'; });
  $('#copy').addEventListener('click', async (e) => {
    const b = e.currentTarget, w = byId[G.sel];
    const t = '// ' + w.name + ' (' + w.id + '), Marrow static wallpaper. Uniforms: ' + C.uniforms.map((u) => u.type + ' ' + u.name).join(', ') + '\n' + C.lib + w.src;
    try { await navigator.clipboard.writeText(t); b.textContent = 'Copied'; } catch (err) { b.textContent = 'Select it below'; if (!code.classList.contains('open')) codeBtn.click(); }
    setTimeout(() => { b.textContent = 'Copy shader'; }, 1600);
  });

  // ------------------------------------------------------------------ choosing a wallpaper
  function select(id, scroll) {
    if (!byId[id]) return;
    const changed = id !== G.sel;
    G.sel = id; S.set(PAGE + '.sel', id);
    $$('.card').forEach((c) => c.setAttribute('aria-current', String(c.dataset.id === id)));
    if (changed) {
      pbody.style.opacity = '0';
      setTimeout(() => { fillPanel(); pbody.style.opacity = '1'; }, 200);
      attach(id);
    }
    if (scroll) {
      const r = $('#view').getBoundingClientRect();
      if (r.top < 0 || r.top > innerHeight * .5) $('#view').scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start' });
    }
  }

  // ------------------------------------------------------------------ the library: thumbnails by one shared renderer
  const R = new SWG.Renderer();
  const thumbs = {};
  const queue = [];
  let qBusy = false;
  function pump() {
    if (qBusy) return; qBusy = true;
    requestAnimationFrame(function step() {
      const t0 = performance.now();
      while (queue.length && performance.now() - t0 < 10) { const t = queue.shift(); t.queued = false; t.draw(); }
      if (queue.length) requestAnimationFrame(step); else qBusy = false;
    });
  }
  function Thumb(card, w) {
    this.w = w; this.host = $('.thumb', card); this.cvs = [document.createElement('canvas'), document.createElement('canvas')];
    this.cvs.forEach((c) => this.host.appendChild(c)); this.cur = -1; this.vis = false; this.dirty = true; this.key = '';
    this.dots = $$('.dots i', card);
    if (window.IntersectionObserver) {
      new IntersectionObserver((es) => { es.forEach((e) => { this.vis = e.isIntersecting; if (this.vis && this.dirty) this.refresh(); }); }, { rootMargin: '200px' }).observe(this.host);
    } else { this.vis = true; this.refresh(); }
  }
  Thumb.prototype.refresh = function (soon) {
    this.dirty = true;
    const p = per(this.w.id), hex = packOf(this.w, p.pack)[G.dark ? 'dark' : 'light'];
    this.dots.forEach((d, i) => { d.style.backgroundColor = hex[[1, 2, 3][i]]; });
    if (!this.vis) return;
    if (soon) { clearTimeout(this.tm); this.tm = setTimeout(() => this.enqueue(), 120); } else this.enqueue();
  };
  Thumb.prototype.enqueue = function () { if (!this.queued) { this.queued = true; queue.push(this); pump(); } };
  Thumb.prototype.draw = function () {
    const cw = this.host.clientWidth; if (!cw) return;
    const dpr = dprNow(), W2 = Math.round(cw * dpr), H2 = Math.round(cw * 10 / 16 * dpr);
    const p = per(this.w.id);
    const key = [W2, H2, p.pack, p.pack >= this.w.packs.length ? G.accent : '', p.opt.toFixed(3), p.grain.toFixed(3), p.focus.toFixed(3), p.dyn ? G.clock.toFixed(2) : '-', G.dark].join('|');
    this.dirty = false;
    if (key === this.key) return;
    const nxt = this.cur === 0 ? 1 : 0, cv = this.cvs[nxt], sp = buildSpec(this.w, p, G.dark, H2);
    if (!R.draw(this.w.eng, cv, W2, H2, dpr*(this.w.thumbTexScale || 1), sp.pal, sp.dark, p.grain, p.opt, sp.x)) return;
    this.key = key;
    cv.classList.add('in'); if (this.cur >= 0) this.cvs[this.cur].classList.remove('in');
    this.host.appendChild(cv);
    this.cur = nxt;
  };
  const tiersEl = $('#tiers');
  D.tiers.forEach((t) => {
    const sec = document.createElement('section');
    sec.className = 'tier dk-glass'; sec.id = 't-' + t.id; sec.dataset.tier = t.id;
    const list = W.filter((w) => w.tier === t.id);
    sec.innerHTML = '<div class="th"><h2>' + esc(t.name) + '</h2><span class="tag t-' + t.id + '">' + list.length + ' wallpapers</span></div><p class="td">' + esc(t.desc) + '</p><div class="grid"></div>';
    const grid = $('.grid', sec);
    list.forEach((w) => {
      const c = document.createElement('button');
      c.type = 'button'; c.className = 'card'; c.dataset.id = w.id; c.setAttribute('aria-current', String(w.id === G.sel));
      c.setAttribute('aria-label', w.name + ', ' + t.name + ', ' + (w.packs.length + 1) + ' colour packs');
      c.innerHTML = '<span class="thumb"></span><span class="cm"><b>' + esc(w.name) + '</b><span class="dots"><i></i><i></i><i></i></span><span class="ms" data-ms="' + w.id + '">' + (w.cost ? fmt(w.cost.ms) + ' ms' : '') + '</span></span>';
      grid.appendChild(c);
      c.addEventListener('click', () => select(w.id, true));
      thumbs[w.id] = new Thumb(c, w);
    });
    tiersEl.appendChild(sec);
  });

  // ------------------------------------------------------------------ tier filter, theme
  function showTier(t, anim) {
    G.tier = t; S.set(PAGE + '.tier', t);
    $$('.tier').forEach((s) => {
      const on = t === 'all' || s.dataset.tier === t;
      if (on && s.classList.contains('off')) { s.style.opacity = '0'; s.classList.remove('off'); void s.offsetWidth; s.style.opacity = '1'; Object.values(thumbs).forEach((x) => { if (x.w.tier === s.dataset.tier) x.refresh(); }); }
      else if (!on && !s.classList.contains('off')) { if (anim) { s.style.opacity = '0'; setTimeout(() => { if (!(G.tier === 'all' || s.dataset.tier === G.tier)) s.classList.add('off'); }, 330); } else s.classList.add('off'); }
    });
    const n = t === 'all' ? W.length : W.filter((w) => w.tier === t).length;
    $('#cnt').textContent = n + ' of ' + W.length + ' shown';
  }
  K.seg($('#segTier'), [{ v: 'all', t: 'All' }, { v: 'vibrant', t: 'Vibrant' }, { v: 'balanced', t: 'Balanced' }, { v: 'minimal', t: 'Minimal' }], G.tier, (v) => showTier(v, true));
  K.seg($('#segT'), [{ v: 'auto', t: 'Auto' }, { v: 'light', t: 'Light' }, { v: 'dark', t: 'Dark' }], K.themeMode(), (v) => K.theme(v));
  function onTheme(d) {
    G.dark = d; document.documentElement.dataset.dk = d ? '1' : '0';
    if (V.st) { viewTargets(V.st, G.sel); wake(); }
    Object.values(thumbs).forEach((t) => t.refresh());
  }
  K.onTheme(onTheme);
  document.documentElement.dataset.dk = G.dark ? '1' : '0';

  // ------------------------------------------------------------------ render cost
  const live = {}, liveO = {};
  function costLine() {
    const w = byId[G.sel], el = $('#costl');
    const b = w.cost ? fmt(w.cost.ms) : null, l = live[w.id], lo = liveO[w.id];
    let t = 'One render at <b>2560 x 1600</b>: ';
    if (b != null) t += '<b>' + b + ' ms</b> on the build machine';
    if (l) t += (b != null ? ', ' : '') + '<b>' + fmt(l.med) + ' ms</b> on this device';
    if (b == null && !l) t += 'not measured yet';
    t += '. Budget 30 ms; then cached, 0 ms per frame.';
    const o = lo ? lo.med : w.cost && w.cost.msOpt;
    if (o != null) t += ' With every option on (Match accent, the clock, a tilt, full Focus): <b>' + fmt(o) + ' ms</b>.';
    el.innerHTML = t;
    const ms = Math.max(l ? l.med : w.cost ? w.cost.ms : 0, o || 0);
    $('#costbar').style.width = Math.min(100, ms / 30 * 100) + '%';
  }
  const ctab = $('#ctab');
  W.forEach((w) => {
    const r = document.createElement('div'); r.className = 'crow';
    r.innerHTML = '<span>' + esc(w.name) + '</span><b data-c="' + w.id + '">' + (w.cost ? fmt(w.cost.ms) : '-') + '</b><i><u style="width:' + (w.cost ? Math.min(100, w.cost.ms / 30 * 100) : 0) + '%"></u></i>';
    ctab.appendChild(r);
  });
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  async function measure() {
    const btn = $('#meas'), stat = $('#mstat');
    btn.disabled = true;
    const res = {}, resO = {};
    for (const w of W) {
      stat.textContent = 'Measuring ' + w.name + '...';
      await sleep(16);
      const p = per(w.id);
      const r = R.time(w.eng, 2560, 1600, palFor(w, p.pack, G.dark), G.dark ? 1 : 0, 11);
      if (!r) { stat.textContent = R.ok ? 'Could not compile ' + w.name + '.' : 'No WebGL2 here.'; break; }
      // and with every option on: Match accent, Dynamic at dusk, the pointer in a corner, Focus at full
      const po = { pack: w.packs.length, opt: w.option.def, grain: w.grain.def, focus: 1, dyn: true, persp: true }, so = buildSpec(w, po, G.dark, 1600, { clock: 18, tilt: [1, 1] });
      const ro = R.time(w.eng, 2560, 1600, so.pal, so.dark, 11, so.x);
      res[w.id] = r; live[w.id] = r; if (ro) { resO[w.id] = ro; liveO[w.id] = ro; }
      const cell = $('[data-c="' + w.id + '"]', ctab); cell.textContent = fmt(r.med);
      cell.parentNode.querySelector('u').style.width = Math.min(100, r.med / 30 * 100) + '%';
      const ms = $('[data-ms="' + w.id + '"]'); if (ms) ms.textContent = fmt(r.med) + ' ms';
    }
    const v = Object.values(res).map((r) => r.med);
    if (v.length === W.length) {
      const sum = $('#mlive');
      const vo = Object.values(resO).map((r) => r.med);
      sum.innerHTML = '<b>Measured just now on this device</b> (' + esc(R.renderer) + '): one render at 2560 x 1600 costs <b>' + fmt(Math.min.apply(null, v)) + ' to ' + fmt(Math.max.apply(null, v)) + ' ms</b>'
        + (vo.length ? ', and <b>' + fmt(Math.min.apply(null, vo)) + ' to ' + fmt(Math.max.apply(null, vo)) + ' ms</b> with every option on' : '') + ', against a budget of 30 ms.';
      stat.textContent = 'Measured all ' + W.length + '.';
      window.__measured = { renderer: R.renderer, rows: res, rowsOpt: resO };
    }
    costLine();
    btn.disabled = false;
  }
  $('#meas').addEventListener('click', measure);
  window.__measure = measure;

  // ------------------------------------------------------------------ start
  setGlass(G.glass);
  showTier(G.tier, false);
  fitView();
  fillAccents();
  fillPanel();
  attach(G.sel);
  // test hooks (check.cjs): a render of any wallpaper in any state, to compare numbers; the setters the panel uses
  function probe(id, st, W2, H2) {
    const w = byId[id], base = { pack: 0, opt: w.option.def, grain: 0, focus: w.focus.def, dyn: false, persp: false };
    const p = Object.assign(base, st || {}), cv = document.createElement('canvas');
    const sp = buildSpec(w, p, !!(st && st.dark), H2 || 160, { clock: st && st.clock, accent: st && st.accent, tilt: st && st.tilt });
    if (!R.draw(w.eng, cv, W2 || 256, H2 || 160, 1, sp.pal, sp.dark, p.grain, p.opt, sp.x)) return null;
    return { d: cv.getContext('2d').getImageData(0, 0, cv.width, cv.height).data, spec: sp };
  }
  // numbers of one render: mean luma, mean OKLab (a, b give the hue), flatness, black share
  function stats(id, st) {
    const r = probe(id, st); if (!r) return null;
    const d = r.d; let ls = 0, A = 0, B = 0, Ls = 0, mn = 255, mx = 0, black = 0, vd = 0, n = 0;
    for (let i = 0; i < d.length; i += 4 * 7) {
      const l = (d[i] * 0.299 + d[i + 1] * 0.587 + d[i + 2] * 0.114); ls += l; mn = Math.min(mn, l); mx = Math.max(mx, l); if (d[i] + d[i + 1] + d[i + 2] < 3) black++; if (d[i] + d[i + 1] + d[i + 2] < 12) vd++;
      const lab = SWC.lin2lab(SWC.s2l(d[i] / 255), SWC.s2l(d[i + 1] / 255), SWC.s2l(d[i + 2] / 255)); Ls += lab[0]; A += lab[1]; B += lab[2]; n++;
    }
    const hue = (Math.atan2(B / n, A / n) * 180 / Math.PI + 360) % 360;
    return { luma: ls / n, L: Ls / n, a: A / n, b: B / n, chroma: Math.hypot(A / n, B / n), hue, range: mx - mn, black: black / n, vdark: vd / n, tilt: r.spec.x.tilt, blur: r.spec.x.blur, light: r.spec.x.light };
  }
  // the difference of two renders: mean and largest absolute channel difference, 0..255
  function diff(id, a, b) {
    const x = probe(id, a), y = probe(id, b); if (!x || !y) return null;
    let s = 0, m = 0, n = 0;
    for (let i = 0; i < x.d.length; i += 4) for (let c = 0; c < 3; c++) { const e = Math.abs(x.d[i + c] - y.d[i + c]); s += e; if (e > m) m = e; n++; }
    return { mean: s / n, max: m };
  }
  window.__sw = { V, G, thumbs, R, select, W, byId, probe, stats, diff, buildSpec, accPack, packOf, palFor, per, setPer, setAccent, setDyn, glideClock, clockChanged, setClockUI, applyChoices, fillPanel };
})();
