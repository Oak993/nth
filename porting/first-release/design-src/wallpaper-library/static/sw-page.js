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
  W.forEach((w) => { w.eng = { id: w.id, fs: C.head + C.lib + w.src + '\n' + C.main, grain: w.grain.def, opt: { def: w.option.def } }; byId[w.id] = w; });
  const TIER = {}; D.tiers.forEach((t) => { TIER[t.id] = t; });
  const dprNow = () => Math.min(window.devicePixelRatio || 1, 2);
  const fmt = (x) => (x == null ? '-' : x < 10 ? x.toFixed(2) : x.toFixed(1));

  // ------------------------------------------------------------------ state (a per-viewer convenience only)
  const readJ = (k, d) => { try { const v = JSON.parse(S.get(PAGE + '.' + k, 'null')); return v == null ? d : v; } catch (e) { return d; } };
  const G = { dark: K.isDark(), sel: S.get(PAGE + '.sel', W[0].id), glass: S.get(PAGE + '.glass', '1') !== '0', tier: S.get(PAGE + '.tier', 'all'), per: readJ('per', {}) };
  if (!byId[G.sel]) G.sel = W[0].id;
  if (['all', 'vibrant', 'balanced', 'minimal'].indexOf(G.tier) < 0) G.tier = 'all';
  function per(id) {
    const w = byId[id], p = G.per[id] || {};
    return { pack: p.pack >= 0 && p.pack < w.packs.length ? p.pack : w.defaultPack, opt: typeof p.opt === 'number' ? p.opt : w.option.def, grain: typeof p.grain === 'number' ? p.grain : w.grain.def };
  }
  function setPer(id, o) { G.per[id] = Object.assign(per(id), o); S.set(PAGE + '.per', JSON.stringify(G.per)); }
  function palFor(w, pi, dark) {
    const hex = w.packs[pi][dark ? 'dark' : 'light'], a = new Float32Array(18);
    hex.forEach((h, i) => a.set(SWC.hex2lab(h), i * 3)); return a;
  }

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
    st.target({ pal: palFor(w, p.pack, G.dark), dark: G.dark ? 1 : 0, grain: p.grain, opt: p.opt, glass: G.glass ? 1 : 0 });
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
  const pbody = $('#pbody'), packsEl = $('#packs'), optR = $('#opt'), grR = $('#grain'), glBtn = $('#glass');
  function swatchBg(hex) {
    return 'conic-gradient(from 210deg,' + [hex[1], hex[2], hex[3], hex[4], hex[0], hex[1]].join(',') + ')';
  }
  function setRange(r, v) { r.value = String(v); r.style.setProperty('--p', (v * 100).toFixed(1) + '%'); const o = r.parentNode.querySelector('output'); if (o) o.textContent = Math.round(v * 100); }
  function fillPanel() {
    const w = byId[G.sel], p = per(G.sel);
    $('#vname').textContent = w.name;
    const tg = $('#vtier'); tg.textContent = TIER[w.tier].name; tg.className = 'tag t-' + w.tier;
    $('#vpacks').textContent = w.packs.length + ' colour packs';
    $('#vdesc').textContent = w.desc;
    $('#optl').textContent = w.option.label;
    packsEl.innerHTML = '';
    w.packs.forEach((pk, i) => {
      const b = document.createElement('button');
      b.type = 'button'; b.className = 'pk'; b.dataset.i = i; b.setAttribute('aria-pressed', String(i === p.pack));
      b.setAttribute('aria-label', pk.name + (pk.kind !== 'standard' ? ' (' + pk.kind + ')' : '')); b.title = pk.name;
      b.innerHTML = '<i class="l" style="background:' + swatchBg(pk.light) + '"></i><i class="d" style="background:' + swatchBg(pk.dark) + '"></i>' + (pk.kind === 'quiet' ? '<span class="kd">Q</span>' : pk.kind === 'rich' ? '<span class="kd">R</span>' : '');
      packsEl.appendChild(b);
    });
    $('#packn').textContent = w.packs[p.pack].name;
    setRange(optR, p.opt); setRange(grR, p.grain);
    $('#src').textContent = w.src;
    costLine();
    mockText(false);
  }
  function mockText(now) {
    const w = byId[G.sel], p = per(G.sel);
    $$('[data-mk]', mock).forEach((el) => {
      const t = el.dataset.mk === 'name' ? w.name : w.packs[p.pack].name;
      if (el.textContent === t) return;
      if (now) { el.textContent = t; return; }
      el.style.opacity = '0'; setTimeout(() => { el.textContent = t; el.style.opacity = '1'; }, 220);
    });
  }
  packsEl.addEventListener('click', (e) => {
    const b = e.target.closest('.pk'); if (!b) return;
    const i = +b.dataset.i; setPer(G.sel, { pack: i });
    $$('.pk', packsEl).forEach((x) => x.setAttribute('aria-pressed', String(x === b)));
    $('#packn').textContent = byId[G.sel].packs[i].name;
    if (V.st) { viewTargets(V.st, G.sel); wake(); }
    mockText(false); thumbs[G.sel].refresh();
  });
  optR.addEventListener('input', () => { const v = +optR.value; setRange(optR, v); setPer(G.sel, { opt: v }); if (V.st) { V.st.target({ opt: v }); wake(); } thumbs[G.sel].refresh(true); });
  grR.addEventListener('input', () => { const v = +grR.value; setRange(grR, v); setPer(G.sel, { grain: v }); if (V.st) { V.st.target({ grain: v }); wake(); } thumbs[G.sel].refresh(true); });
  function setGlass(on) {
    G.glass = on; S.set(PAGE + '.glass', on ? '1' : '0');
    glBtn.setAttribute('aria-checked', String(on)); vst.dataset.glass = on ? '1' : '0';
    if (V.st) { V.st.target({ glass: on ? 1 : 0 }); wake(); }
  }
  glBtn.addEventListener('click', () => setGlass(!G.glass));
  $('#reset').addEventListener('click', () => {
    const w = byId[G.sel]; delete G.per[G.sel]; S.set(PAGE + '.per', JSON.stringify(G.per));
    const p = per(G.sel);
    $$('.pk', packsEl).forEach((x) => x.setAttribute('aria-pressed', String(+x.dataset.i === p.pack)));
    $('#packn').textContent = w.packs[p.pack].name;
    setRange(optR, p.opt); setRange(grR, p.grain);
    if (V.st) { viewTargets(V.st, G.sel); wake(); }
    mockText(false); thumbs[G.sel].refresh();
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
    const p = per(this.w.id), hex = this.w.packs[p.pack][G.dark ? 'dark' : 'light'];
    this.dots.forEach((d, i) => { d.style.backgroundColor = hex[[1, 2, 3][i]]; });
    if (!this.vis) return;
    if (soon) { clearTimeout(this.tm); this.tm = setTimeout(() => this.enqueue(), 120); } else this.enqueue();
  };
  Thumb.prototype.enqueue = function () { if (!this.queued) { this.queued = true; queue.push(this); pump(); } };
  Thumb.prototype.draw = function () {
    const cw = this.host.clientWidth; if (!cw) return;
    const dpr = dprNow(), W2 = Math.round(cw * dpr), H2 = Math.round(cw * 10 / 16 * dpr);
    const p = per(this.w.id);
    const key = [W2, H2, p.pack, p.opt.toFixed(3), p.grain.toFixed(3), G.dark].join('|');
    this.dirty = false;
    if (key === this.key) return;
    const nxt = this.cur === 0 ? 1 : 0, cv = this.cvs[nxt];
    if (!R.draw(this.w.eng, cv, W2, H2, dpr*(this.w.thumbTexScale || 1), palFor(this.w, p.pack, G.dark), G.dark ? 1 : 0, p.grain, p.opt)) return;
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
      c.setAttribute('aria-label', w.name + ', ' + t.name + ', ' + w.packs.length + ' colour packs');
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
  const live = {};
  function costLine() {
    const w = byId[G.sel], el = $('#costl');
    const b = w.cost ? fmt(w.cost.ms) : null, l = live[w.id];
    let t = 'One render at <b>2560 x 1600</b>: ';
    if (b != null) t += '<b>' + b + ' ms</b> on the build machine';
    if (l) t += (b != null ? ', ' : '') + '<b>' + fmt(l.med) + ' ms</b> on this device';
    if (b == null && !l) t += 'not measured yet';
    t += '. Budget 30 ms; then cached, 0 ms per frame.';
    el.innerHTML = t;
    const ms = l ? l.med : w.cost ? w.cost.ms : 0;
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
    const res = {};
    for (const w of W) {
      stat.textContent = 'Measuring ' + w.name + '...';
      await sleep(16);
      const p = per(w.id);
      const r = R.time(w.eng, 2560, 1600, palFor(w, p.pack, G.dark), G.dark ? 1 : 0, 11);
      if (!r) { stat.textContent = R.ok ? 'Could not compile ' + w.name + '.' : 'No WebGL2 here.'; break; }
      res[w.id] = r; live[w.id] = r;
      const cell = $('[data-c="' + w.id + '"]', ctab); cell.textContent = fmt(r.med);
      cell.parentNode.querySelector('u').style.width = Math.min(100, r.med / 30 * 100) + '%';
      const ms = $('[data-ms="' + w.id + '"]'); if (ms) ms.textContent = fmt(r.med) + ' ms';
    }
    const v = Object.values(res).map((r) => r.med);
    if (v.length === W.length) {
      const sum = $('#mlive');
      sum.innerHTML = '<b>Measured just now on this device</b> (' + esc(R.renderer) + '): one render at 2560 x 1600 costs <b>' + fmt(Math.min.apply(null, v)) + ' to ' + fmt(Math.max.apply(null, v)) + ' ms</b>, against a budget of 30 ms.';
      stat.textContent = 'Measured all ' + W.length + '.';
      window.__measured = { renderer: R.renderer, rows: res };
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
  fillPanel();
  attach(G.sel);
  window.__sw = { V, G, thumbs, R, select, W };
})();
