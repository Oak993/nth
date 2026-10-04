// Look Picks, Q-CZ-3: the colour picker that opens from the Uiverse light's Custom colour wells (customization SPEC
// CZ-7.1, CZ-12.6), drawn working inside Marrow's glass popover with the Mac's arrow tip (OD-0095: card 13 pt from the
// anchor, tip 12 pt high, 12 pt half base, 3 pt point radius, 4 pt shoulders). Every picker edits the same two colours;
// the wells and the light's preview follow at once (the well's fill eases 0.3 s, CZ-12.6).
//   A  Avalonia 12.1.2's ColorView, drawn from its Fluent template (Themes/Fluent/ColorView.xaml and friends, MIT):
//      350 px wide, a 48 px tab strip (Spectrum, Palette, Components), the 338 px tab control, the previewer.
//   B  Marrow's own glass picker: saturation-brightness square, hue strip, opacity strip, hex field, recent colours.
//   C  A colour wheel like macOS's (hue around, saturation outward), a brightness strip, opacity, hex.
//   D  The 22 accent swatches (Classic and Signature, accent-palette-locked.json) with a compact custom area.
(function () {
  const ACC = __ACCENTS__;      // [id, name, group, darkHex, lightHex]
  const FLUENT = __FLUENT__;    // FluentColorPalette, 6 colours x 8 shades, chart[colour][shade]
  const K = window.Kit;
  const $ = (s, r) => (r || document).querySelector(s);
  const clamp = (x, a, b) => (x < a ? a : x > b ? b : x);
  // ---- colour
  const hexRgb = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));
  const rgbHex = (c) => '#' + c.map((v) => clamp(Math.round(v), 0, 255).toString(16).padStart(2, '0')).join('').toUpperCase();
  function rgbHsv(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
    let h = 0;
    if (d > 0) h = mx === r ? ((g - b) / d) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
    h = (h * 60 + 360) % 360;
    return { h, s: mx ? d / mx : 0, v: mx };
  }
  function hsvRgb(h, s, v) {
    const f = (n) => { const k = (n + h / 60) % 6; return v - v * s * Math.max(0, Math.min(k, 4 - k, 1)); };
    return [f(5) * 255, f(3) * 255, f(1) * 255];
  }
  const hsvHex = (c) => rgbHex(hsvRgb(c.h, c.s, c.v));
  const fromHex = (hex, a) => Object.assign(rgbHsv(...hexRgb(hex)), { a: a == null ? 1 : a });
  const lum = (hex) => { const c = hexRgb(hex).map((v) => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }); return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]; };
  const rgba = (c, a) => { const q = hsvRgb(c.h, c.s, c.v).map(Math.round); return 'rgba(' + q.join(',') + ',' + (a == null ? c.a : a).toFixed(3) + ')'; };
  const mixHex = (h, w, t) => { const a = hexRgb(h), b = hexRgb(w); return rgbHex(a.map((x, i) => x + (b[i] - x) * t)); };
  // pointer position in an element's own (unscaled) CSS px, whatever transform the stage carries
  function local(e, el) { const r = el.getBoundingClientRect(); return [((e.clientX - r.left) / r.width) * el.offsetWidth, ((e.clientY - r.top) / r.height) * el.offsetHeight, el.offsetWidth, el.offsetHeight]; }
  function drag(el, onMove, onEnd) {
    el.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      e.preventDefault(); el.setPointerCapture(e.pointerId); onMove(local(e, el), false);
      const mv = (ev) => onMove(local(ev, el), false);
      const up = (ev) => { el.removeEventListener('pointermove', mv); el.removeEventListener('pointerup', up); el.removeEventListener('pointercancel', up); onMove(local(ev, el), true); if (onEnd) onEnd(); };
      el.addEventListener('pointermove', mv); el.addEventListener('pointerup', up); el.addEventListener('pointercancel', up);
    });
  }
  const H = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html != null) e.innerHTML = html; return e; };

  // ---- shared state: the two colours (AURA_DEFAULTS c1 #8E7CFF, c2 #38C9B4), which well is open, recent colours
  const S = { col: { c1: fromHex('#8E7CFF'), c2: fromHex('#38C9B4') }, active: 'c1', open: true, opt: 'B', recent: ['#8E7CFF', '#38C9B4'] };
  let pickers = {}, stage, pop, card, tip, wells = {}, prev;
  const cur = () => S.col[S.active];
  function set(c, commit) {
    S.col[S.active] = { h: c.h, s: c.s, v: c.v, a: c.a == null ? 1 : c.a };
    if (commit) { const hx = hsvHex(S.col[S.active]); S.recent = [hx].concat(S.recent.filter((x) => x !== hx)).slice(0, 8); }
    refresh(null);
  }
  function refresh(except) {
    for (const k of ['c1', 'c2']) { const c = S.col[k]; wells[k].style.backgroundColor = rgba(c); wells[k].title = hsvHex(c) + (c.a < 1 ? ' at ' + Math.round(c.a * 100) + ' %' : ''); }
    prev.style.setProperty('--au-c1', rgba(S.col.c1)); prev.style.setProperty('--au-c2', rgba(S.col.c2));
    const k1 = mixHex(hsvHex(S.col.c1), '#FFFFFF', 0.35), k2 = mixHex(hsvHex(S.col.c2), '#FFFFFF', 0.35), n1 = mixHex(hsvHex(S.col.c1), '#FFFFFF', 0.6), n2 = mixHex(hsvHex(S.col.c2), '#FFFFFF', 0.6);
    prev.style.setProperty('--au-k1', k1); prev.style.setProperty('--au-k2', k2); prev.style.setProperty('--au-n1', n1); prev.style.setProperty('--au-n2', n2);
    $('.cz-val', stage).textContent = (S.active === 'c1' ? 'One side' : 'The other') + ': ' + hsvHex(cur()) + (cur().a < 1 ? ' · ' + Math.round(cur().a * 100) + ' %' : '');
    for (const k in pickers) if (pickers[k] !== except) pickers[k].render(cur());
  }

  // ================================================================ A: Avalonia ColorView (Fluent template)
  const ICONS = __ICONS__;   // the three tab icons' PathGeometry from ColorView.xaml (Fluent UI System Icons, MIT)
  function pickerA() {
    const el = H('div', 'av');
    el.innerHTML =
      '<div class="av-tabs" role="tablist">' + ['spectrum', 'palette', 'components'].map((k, i) => '<button type="button" role="tab" class="av-tab" data-t="' + i + '" aria-label="' + k + '"><svg viewBox="0 0 20 20"><path d="' + ICONS[i] + '"/></svg><i class="pipe"></i></button>').join('') + '</div>' +
      '<div class="av-body"><div class="av-pane on" data-p="0"><div class="av-spec"><div class="av-cs vs"><div class="trk"></div><i class="th"></i></div><div class="av-box"><canvas></canvas><i class="sel"></i></div><div class="av-cs as"><div class="chk"></div><div class="trk"></div><i class="th"></i></div></div></div>' +
      '<div class="av-pane" data-p="1"><div class="av-pal"></div></div>' +
      '<div class="av-pane" data-p="2"><div class="av-cmp"><div class="av-top"><div class="av-model"><button type="button" class="on" data-m="rgb">RGB</button><button type="button" data-m="hsv">HSV</button></div><div class="av-hex"><span>#</span><input type="text" maxlength="9" spellcheck="false" aria-label="Hexadecimal Color"></div></div>' +
      [0, 1, 2, 3].map((i) => '<div class="av-row" data-r="' + i + '" style="grid-row:' + (i + 3) + '"><span class="lb"></span><input type="text" inputmode="numeric" class="num"><div class="av-cs hs"><div class="chk"></div><div class="trk"></div><i class="th"></i></div></div>').join('') + '</div></div></div>' +
      '<div class="av-prev"><div class="acc l"><i></i><i></i></div><div class="pc"><i></i></div><div class="acc r"><i></i><i></i></div></div>';
    let tab = 0, model = 'rgb', shape = 'box', tabsAll = true;
    const tabs = [...el.querySelectorAll('.av-tab')], panes = [...el.querySelectorAll('.av-pane')];
    tabs.forEach((b) => b.addEventListener('click', () => { tab = +b.dataset.t; layout(); }));
    function layout() {
      tabs.forEach((b, i) => { b.setAttribute('aria-selected', String(i === tab)); b.style.display = tabsAll || i === 0 ? '' : 'none'; });
      panes.forEach((p, i) => p.classList.toggle('on', i === tab));
    }
    // the palette tab: UniformGrid with PaletteColumnCount = ColorCount (6) columns, shade by shade
    const pal = $('.av-pal', el);
    for (let s = 0; s < 8; s++) for (let c = 0; c < 6; c++) { const b = H('button', 'pi'); b.type = 'button'; b.style.background = FLUENT[c][s]; b.dataset.hex = FLUENT[c][s]; b.title = FLUENT[c][s]; b.addEventListener('click', () => set(fromHex(b.dataset.hex, cur().a), true)); pal.appendChild(b); }
    // the spectrum: HueSaturation (hue across, full saturation at the top; in the ring hue around, saturation outward).
    // With value as the third component the spectrum is always drawn at value 1 (ColorSpectrum.cs:1719-1730).
    const box = $('.av-box', el), cv = $('canvas', box), sel = $('.sel', box);
    let drawnKey = '';
    function paintSpectrum(c) {
      const W = box.offsetWidth || 262, dpr = Math.min(2, devicePixelRatio || 1), key = shape + W + '|' + dpr;
      if (key === drawnKey) return; drawnKey = key;
      cv.width = Math.round(W * dpr); cv.height = Math.round(W * dpr);
      const g = cv.getContext('2d'), N = cv.width, im = g.createImageData(N, N);
      for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
        let hh, ss, inside = true;
        if (shape === 'box') { hh = (x / (N - 1)) * 359; ss = 1 - y / (N - 1); }
        else { const dx = x - N / 2 + 0.5, dy = y - N / 2 + 0.5, r = Math.hypot(dx, dy) / (N / 2); inside = r <= 1; hh = ((Math.atan2(dy, dx) * 180) / Math.PI + 360) % 360; ss = Math.min(1, r); }
        const o = (y * N + x) * 4;
        if (!inside) { im.data[o + 3] = 0; continue; }
        const q = hsvRgb(hh, ss, 1); im.data[o] = q[0]; im.data[o + 1] = q[1]; im.data[o + 2] = q[2]; im.data[o + 3] = 255;
      }
      g.putImageData(im, 0, 0);
      box.classList.toggle('ring', shape === 'ring');
    }
    drag(box, ([x, y, w, h], end) => {
      const c = cur();
      if (shape === 'box') set({ h: clamp(x / w, 0, 1) * 359, s: clamp(1 - y / h, 0, 1), v: c.v, a: c.a }, end);
      else { const dx = x - w / 2, dy = y - h / 2; set({ h: ((Math.atan2(dy, dx) * 180) / Math.PI + 360) % 360, s: clamp(Math.hypot(dx, dy) / (w / 2), 0, 1), v: c.v, a: c.a }, end); }
    });
    // vertical ColorSliders: the third component (value) and alpha; maximum at the top
    const vs = $('.av-cs.vs', el), as = $('.av-cs.as', el);
    drag(vs, ([, y, , h], end) => { const c = cur(); set({ h: c.h, s: c.s, v: clamp(1 - (y - 10) / (h - 20), 0, 1), a: c.a }, end); });
    drag(as, ([, y, , h], end) => { const c = cur(); set({ h: c.h, s: c.s, v: c.v, a: clamp(1 - (y - 10) / (h - 20), 0, 1) }, end); });
    // the components tab: RGB/HSV, the hex box, four rows (label, NumericUpDown without spinner, ColorSlider)
    const mb = [...el.querySelectorAll('.av-model button')];
    mb.forEach((b) => b.addEventListener('click', () => { model = b.dataset.m; mb.forEach((x) => x.classList.toggle('on', x === b)); render(cur()); }));
    const hexIn = $('.av-hex input', el);
    hexIn.addEventListener('change', () => { let v = hexIn.value.trim().replace(/^#/, ''); if (/^[0-9a-f]{6}([0-9a-f]{2})?$/i.test(v)) set(fromHex('#' + v.slice(0, 6), v.length === 8 ? parseInt(v.slice(6), 16) / 255 : cur().a), true); else render(cur()); });
    const rows = [...el.querySelectorAll('.av-row')];
    const comp = (i, c) => { if (i === 3) return [0, 255, Math.round(c.a * 255)]; if (model === 'rgb') return [0, 255, Math.round(hsvRgb(c.h, c.s, c.v)[i])]; return i === 0 ? [0, 359, Math.round(c.h)] : [0, 100, Math.round((i === 1 ? c.s : c.v) * 100)]; };
    function setComp(i, val, end) {
      const c = cur();
      if (i === 3) return set({ h: c.h, s: c.s, v: c.v, a: clamp(val / 255, 0, 1) }, end);
      if (model === 'rgb') { const q = hsvRgb(c.h, c.s, c.v); q[i] = clamp(val, 0, 255); const n = rgbHsv(...q); if (n.s === 0) n.h = c.h; return set(Object.assign(n, { a: c.a }), end); }
      const n = { h: c.h, s: c.s, v: c.v, a: c.a }; if (i === 0) n.h = clamp(val, 0, 359); else if (i === 1) n.s = clamp(val / 100, 0, 1); else n.v = clamp(val / 100, 0, 1); set(n, end);
    }
    rows.forEach((r, i) => {
      const sl = $('.av-cs', r), num = $('.num', r);
      drag(sl, ([x, , w], end) => { const [mn, mx] = comp(i, cur()); setComp(i, Math.round(mn + clamp((x - 10) / (w - 20), 0, 1) * (mx - mn)), end); });
      num.addEventListener('change', () => { const v = parseFloat(num.value); if (isFinite(v)) setComp(i, v, true); else render(cur()); });
    });
    function grad(i, c) {
      const steps = [];
      for (let k = 0; k <= 6; k++) {
        const u = k / 6;
        if (i === 3) steps.push(rgba(c, u));
        else if (model === 'rgb') { const q = hsvRgb(c.h, c.s, c.v); q[i] = u * 255; steps.push('rgb(' + q.map(Math.round).join(',') + ')'); }
        else if (i === 0) steps.push(rgbHex(hsvRgb(u * 359, 1, 1)));
        else steps.push(rgbHex(i === 1 ? hsvRgb(c.h, u, c.v) : hsvRgb(c.h, c.s, u)));
      }
      return 'linear-gradient(90deg,' + steps.join(',') + ')';
    }
    // :light-selector when the colour contrasts more with black than with white (ColorSpectrum.cs:1712-1716): L < 0.179
    const selCls = (c) => (lum(hsvHex(c)) < 0.179 && c.a > 0.5 ? 'lt' : 'dk');
    function render(c) {
      paintSpectrum(c);
      const W = box.offsetWidth || 262;
      let x, y;
      if (shape === 'box') { x = (c.h / 359) * W; y = (1 - c.s) * W; } else { const a = (c.h * Math.PI) / 180; x = W / 2 + Math.cos(a) * c.s * W / 2; y = W / 2 + Math.sin(a) * c.s * W / 2; }
      sel.style.transform = 'translate(' + (x - 8) + 'px,' + (y - 8) + 'px)'; sel.className = 'sel ' + selCls({ h: c.h, s: c.s, v: 1, a: 1 });
      const top = rgbHex(hsvRgb(c.h, c.s, 1));
      $('.trk', vs).style.background = 'linear-gradient(180deg,' + top + ',#000)';
      $('.trk', as).style.background = 'linear-gradient(180deg,' + rgba(c, 1) + ',' + rgba(c, 0) + ')';
      const hv = vs.offsetHeight || 266;
      $('.th', vs).style.transform = 'translateY(' + (1 - c.v) * (hv - 20) + 'px)'; $('.th', vs).className = 'th ' + selCls({ h: c.h, s: c.s, v: c.v, a: 1 });
      $('.th', as).style.transform = 'translateY(' + (1 - c.a) * (hv - 20) + 'px)'; $('.th', as).className = 'th ' + selCls(c);
      const hx = hsvHex(c) + (c.a < 1 ? Math.round(c.a * 255).toString(16).padStart(2, '0').toUpperCase() : '');
      if (document.activeElement !== hexIn) hexIn.value = hx.slice(1);
      rows.forEach((r, i) => {
        const [mn, mx, v] = comp(i, c), sl = $('.av-cs', r), wv = sl.offsetWidth || 180;
        $('.lb', r).textContent = i === 3 ? 'A' : model === 'rgb' ? 'RGB'[i] : 'HSV'[i];
        if (document.activeElement !== $('.num', r)) $('.num', r).value = v;
        $('.trk', sl).style.background = grad(i, c);
        $('.th', sl).style.transform = 'translateX(' + ((v - mn) / (mx - mn)) * (wv - 20) + 'px)';
        $('.th', sl).className = 'th ' + selCls(i === 3 ? c : model === 'rgb' ? { h: 0, s: 0, v: v / 255, a: 1 } : c);
      });
      // ColorPreviewer: accents at value -0.2, -0.1 | colour | +0.1, +0.2 (AccentColorConverter, ValueDelta 0.1)
      const acc = (k) => rgba({ h: c.h, s: c.s, v: clamp(Math.round((c.v + k * 0.1) * 100) / 100, 0, 1), a: c.a });
      const [l2, l1] = el.querySelectorAll('.acc.l i'), [r1, r2] = el.querySelectorAll('.acc.r i');
      l2.style.background = acc(-2); l1.style.background = acc(-1); r1.style.background = acc(1); r2.style.background = acc(2);
      $('.pc i', el).style.background = rgba(c);
      el.querySelectorAll('.pi').forEach((b) => { b.classList.toggle('on', b.dataset.hex === hsvHex(c)); b.style.setProperty('--ct', lum(b.dataset.hex) < 0.179 ? '#FFFFFF' : '#000000'); });
    }
    layout();
    return { el, render, w: 350, opts(o) { if (o.shape) { shape = o.shape; drawnKey = ''; } if (o.tabs != null) { tabsAll = o.tabs; if (!tabsAll) tab = 0; layout(); } render(cur()); } };
  }

  // ================================================================ B: Marrow glass picker
  function stripRow(cls) { return '<div class="mg-strip ' + cls + '"><div class="chk"></div><div class="trk"></div><i class="th"></i></div>'; }
  function hexRow() { return '<div class="mg-hexrow"><label class="mg-hex"><span>#</span><input type="text" maxlength="6" spellcheck="false" aria-label="Hex colour"></label><span class="mg-pct"></span><i class="mg-dot"></i></div>'; }
  function wireHex(el) {
    const inp = $('.mg-hex input', el);
    inp.addEventListener('change', () => { const v = inp.value.trim().replace(/^#/, ''); if (/^[0-9a-f]{6}$/i.test(v)) set(fromHex('#' + v, cur().a), true); else inp.value = hsvHex(cur()).slice(1); });
    return (c) => { if (document.activeElement !== inp) inp.value = hsvHex(c).slice(1); $('.mg-pct', el).textContent = Math.round(c.a * 100) + ' %'; $('.mg-dot', el).style.background = rgba(c); };
  }
  function wireStrip(st, kind) {
    drag(st, ([x, , w], end) => { const c = cur(), u = clamp((x - 8) / (w - 16), 0, 1); if (kind === 'hue') set({ h: u * 359.9, s: c.s, v: c.v, a: c.a }, end); else if (kind === 'alpha') set({ h: c.h, s: c.s, v: c.v, a: Math.round(u * 100) / 100 }, end); else if (kind === 'bri') set({ h: c.h, s: c.s, v: u, a: c.a }, end); });
    return (c) => {
      const w = st.offsetWidth || 236, trk = $('.trk', st);
      let u;
      if (kind === 'hue') { trk.style.background = 'linear-gradient(90deg,#f00,#ff0 17%,#0f0 33%,#0ff 50%,#00f 67%,#f0f 83%,#f00)'; u = c.h / 359.9; $('.th', st).style.background = rgbHex(hsvRgb(c.h, 1, 1)); }
      else if (kind === 'alpha') { trk.style.background = 'linear-gradient(90deg,' + rgba(c, 0) + ',' + rgba(c, 1) + ')'; u = c.a; $('.th', st).style.background = rgba(c); }
      else { trk.style.background = 'linear-gradient(90deg,#000,' + rgbHex(hsvRgb(c.h, c.s, 1)) + ')'; u = c.v; $('.th', st).style.background = rgbHex(hsvRgb(c.h, c.s, c.v)); }
      $('.th', st).style.transform = 'translateX(' + u * (w - 16) + 'px)';
    };
  }
  function recentRow(el) {
    const row = $('.mg-recent', el);
    return () => {
      const want = S.recent.join(',');
      if (row.dataset.k !== want) {
        row.dataset.k = want; row.innerHTML = '';
        S.recent.forEach((hx) => { const b = H('button', 'sw'); b.type = 'button'; b.title = hx; b.innerHTML = '<i style="background:' + hx + '"></i>'; b.addEventListener('click', () => set(fromHex(hx, cur().a), true)); row.appendChild(b); });
      }
      const now = hsvHex(cur());
      row.querySelectorAll('.sw').forEach((b) => b.classList.toggle('on', b.title === now));
    };
  }
  function pickerB() {
    const el = H('div', 'mg');
    el.innerHTML = '<div class="mg-sv"><div class="hue"></div><div class="wh"></div><div class="bk"></div><i class="th"></i></div>' + stripRow('hue') + stripRow('alpha') + hexRow() +
      '<div class="mg-cap">Recent</div><div class="mg-recent sws"></div>';
    const sv = $('.mg-sv', el);
    drag(sv, ([x, y, w, h], end) => { const c = cur(); set({ h: c.h, s: clamp(x / w, 0, 1), v: clamp(1 - y / h, 0, 1), a: c.a }, end); });
    const hue = wireStrip($('.mg-strip.hue', el), 'hue'), al = wireStrip($('.mg-strip.alpha', el), 'alpha'), hx = wireHex(el), rc = recentRow(el);
    function render(c) {
      $('.hue', sv).style.background = rgbHex(hsvRgb(c.h, 1, 1));
      const w = sv.offsetWidth || 236, h = sv.offsetHeight || 150;
      $('.th', sv).style.transform = 'translate(' + (c.s * w - 9) + 'px,' + ((1 - c.v) * h - 9) + 'px)'; $('.th', sv).style.background = hsvHex(c);
      hue(c); al(c); hx(c); rc();
    }
    return { el, render, w: 260 };
  }

  // ================================================================ C: a colour wheel like macOS's
  function pickerC() {
    const el = H('div', 'mw');
    el.innerHTML = '<div class="mw-wheel"><canvas></canvas><i class="th"></i></div>' + stripRow('bri') + stripRow('alpha') + hexRow();
    const wh = $('.mw-wheel', el), cv = $('canvas', wh);
    let key = '';
    function paint(c) {
      const W = wh.offsetWidth || 196, dpr = Math.min(2, devicePixelRatio || 1), k = W + '|' + c.v.toFixed(3) + '|' + dpr;
      if (k === key) return; key = k;
      const N = Math.round(W * dpr); cv.width = N; cv.height = N;
      const g = cv.getContext('2d'), im = g.createImageData(N, N);
      for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
        const dx = x - N / 2 + 0.5, dy = N / 2 - y - 0.5, r = Math.hypot(dx, dy) / (N / 2), o = (y * N + x) * 4;
        if (r > 1) continue;
        const q = hsvRgb(((Math.atan2(dy, dx) * 180) / Math.PI + 360) % 360, Math.min(1, r), c.v);
        const edge = clamp((1 - r) * N / 2 + 0.5, 0, 1);
        im.data[o] = q[0]; im.data[o + 1] = q[1]; im.data[o + 2] = q[2]; im.data[o + 3] = Math.round(edge * 255);
      }
      g.putImageData(im, 0, 0);
    }
    drag(wh, ([x, y, w, h], end) => { const c = cur(), dx = x - w / 2, dy = h / 2 - y; set({ h: ((Math.atan2(dy, dx) * 180) / Math.PI + 360) % 360, s: clamp(Math.hypot(dx, dy) / (w / 2), 0, 1), v: c.v, a: c.a }, end); });
    const bri = wireStrip($('.mg-strip.bri', el), 'bri'), al = wireStrip($('.mg-strip.alpha', el), 'alpha'), hx = wireHex(el);
    function render(c) {
      paint(c);
      const W = wh.offsetWidth || 196, a = (c.h * Math.PI) / 180;
      $('.th', wh).style.transform = 'translate(' + (W / 2 + Math.cos(a) * c.s * W / 2 - 8) + 'px,' + (W / 2 - Math.sin(a) * c.s * W / 2 - 8) + 'px)';
      $('.th', wh).style.background = hsvHex(c);
      bri(c); al(c); hx(c);
    }
    return { el, render, w: 260 };
  }

  // ================================================================ D: the 22 accent swatches and a compact custom area
  function pickerD() {
    const el = H('div', 'ms');
    const grp = (g, name) => '<div class="mg-cap">' + name + '</div><div class="sws" data-g="' + g + '">' + ACC.filter((a) => a[2] === g).map((a) => '<button type="button" class="sw" data-id="' + a[0] + '" title="' + a[1] + '" aria-label="' + a[1] + '"><i></i></button>').join('') + '</div>';
    el.innerHTML = grp('classic', 'Classic') + grp('signature', 'Signature') + '<div class="mg-cap">Custom</div>' + stripRow('hue') + stripRow('shade') + hexRow();
    const hexOf = (a) => (K.isDark() ? a[3] : a[4]);
    el.querySelectorAll('.sw').forEach((b) => b.addEventListener('click', () => { const a = ACC.find((x) => x[0] === b.dataset.id); set(fromHex(hexOf(a), cur().a), true); }));
    const hue = wireStrip($('.mg-strip.hue', el), 'hue'), hx = wireHex(el);
    // the shade strip: white, the pure hue, black (HSL lightness 1 to 0), a quick tint or shade of the hue
    const sh = $('.mg-strip.shade', el);
    const fromL = (h, L) => { const s = 1, v = L + s * Math.min(L, 1 - L), sv = v ? 2 * (1 - L / v) : 0; return { h, s: sv, v }; };
    const toL = (c) => c.v * (1 - c.s / 2);
    drag(sh, ([x, , w], end) => { const c = cur(), L = 1 - clamp((x - 8) / (w - 16), 0, 1); set(Object.assign(fromL(c.h, L), { a: c.a }), end); });
    function render(c) {
      el.querySelectorAll('.sw').forEach((b) => { const a = ACC.find((x) => x[0] === b.dataset.id); $('i', b).style.background = hexOf(a); b.classList.toggle('on', hexOf(a) === hsvHex(c)); });
      hue(c); hx(c);
      const w = sh.offsetWidth || 310;
      $('.trk', sh).style.background = 'linear-gradient(90deg,#fff,' + rgbHex(hsvRgb(c.h, 1, 1)) + ',#000)';
      $('.th', sh).style.transform = 'translateX(' + (1 - toL(c)) * (w - 16) + 'px)'; $('.th', sh).style.background = hsvHex(c);
    }
    return { el, render, w: 334 };
  }

  // ================================================================ the stage
  const NAMES = { A: 'Avalonia ColorView', B: 'Marrow glass picker', C: 'Colour wheel', D: 'Swatches + custom' };
  function build(root) {
    stage = root;
    root.innerHTML =
      '<div class="cz-in">' +
      '<div class="cz-prev" aria-hidden="true"><div class="au-halo"><div class="au-clip"><div class="au-sq"></div></div></div><div class="au-rim"><div class="au-clip"><div class="au-sq"></div></div></div>' +
      '<div class="cz-cmp"><span class="ph">Ask Claude to build, fix or explain.</span><div class="au-in"><div class="au-sq"></div></div></div></div>' +
      '<div class="cz-row"><div class="cz-t"><b>Your colours</b><span>One for each side of the glow</span></div><div class="cz-wells">' +
      '<button type="button" class="cz-well" data-k="c1" aria-label="First colour"><i></i><span>One side</span></button>' +
      '<button type="button" class="cz-well" data-k="c2" aria-label="Second colour"><i></i><span>The other</span></button></div></div>' +
      '<div class="cz-pop" role="dialog" aria-label="Colour picker"><div class="cz-pv"><div class="cz-card"></div><div class="cz-tip"></div><svg class="cz-tipl" viewBox="-17 -1 34 14" aria-hidden="true"><path d="M-16 12 Q-12 12 -9 9 L-2.1 2.1 Q0 0 2.1 2.1 L9 9 Q12 12 16 12"/></svg></div></div>' +
      '<p class="cz-val" aria-live="polite"></p></div>';
    prev = $('.cz-prev', root); pop = $('.cz-pop', root); card = $('.cz-card', root); tip = $('.cz-tip', root);
    root.querySelectorAll('.cz-well').forEach((b) => { wells[b.dataset.k] = $('i', b); b.addEventListener('click', () => { if (S.active === b.dataset.k) S.open = !S.open; else { S.active = b.dataset.k; S.open = true; } place(); refresh(null); }); });
    pickers = { A: pickerA(), B: pickerB(), C: pickerC(), D: pickerD() };
    for (const k in pickers) { const w = H('div', 'cz-pk'); w.dataset.o = k; w.appendChild(pickers[k].el); card.appendChild(w); }
    // the light's turn: the reference's speen (cubic-bezier .56,.15,.28,.86 per half turn), 12 s a turn, breathing over 4 s
    const bez = (x) => { const x1 = 0.56, y1 = 0.15, x2 = 0.28, y2 = 0.86; const cx = (t) => 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t, cy = (t) => 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t; let lo = 0, hi = 1, t = x; for (let i = 0; i < 24; i++) { t = (lo + hi) / 2; if (cx(t) < x) lo = t; else hi = t; } return cy(t); };
    let p = 0, bp = 0, last = null, vis = true;
    if (window.IntersectionObserver) new IntersectionObserver((es) => { vis = es[0].isIntersecting; }).observe(root);
    const spin = (now) => {
      requestAnimationFrame(spin);
      const dt = last == null ? 0 : Math.min(0.1, (now - last) / 1000); last = now;
      if (!vis) return;
      p += dt / 12; bp += dt / 4;
      const h = p * 2, k = Math.floor(h);
      prev.style.setProperty('--au-r', (10 + 180 * k + 180 * bez(h - k)).toFixed(2));
      prev.style.setProperty('--au-s', (1 - 0.25 * (1 - Math.cos(2 * Math.PI * bp)) / 2).toFixed(4));
    };
    requestAnimationFrame(spin);
    let rw = 0;
    new ResizeObserver(() => { if (root.clientWidth !== rw) { rw = root.clientWidth; place(); } }).observe(root);
    K.onTheme(() => refresh(null));
    choose(S.opt, true);
    place(true);
    refresh(null);
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => place());
  }
  // the popover below the wells, its tip on the open well; card size and place ease (OD-0076)
  function place(first) {
    const pk = pickers[S.opt], W = pk.w + 12, inn = $('.cz-in', stage), sc = inn.getBoundingClientRect().width / 400 || 1;
    const ir = inn.getBoundingClientRect(), wr = $('.cz-well[data-k="' + S.active + '"] i', stage).getBoundingClientRect(), rr = $('.cz-row', stage).getBoundingClientRect();
    const wx = (wr.left + wr.width / 2 - ir.left) / sc;
    const left = clamp(wx - W / 2, 8, 400 - W - 8);
    const top = (wr.bottom - ir.top) / sc + 13;   // OD-0095: the card's edge 13 pt from its anchor
    const sh = card.querySelector('.cz-pk[data-o="' + S.opt + '"]');
    const Hh = sh.firstChild.offsetHeight + 12;
    if (first) pop.classList.add('nt');
    pop.style.left = left + 'px'; pop.style.top = top + 'px'; pop.style.width = W + 'px';
    card.style.height = Hh + 'px';
    pop.style.setProperty('--tx', (wx - left) + 'px');
    pop.classList.toggle('open', S.open);
    const total = Math.max((rr.bottom - ir.top) / sc + 44, S.open ? top + Hh + 40 : 0);
    inn.style.height = total + 'px';
    stage.querySelectorAll('.cz-well').forEach((b) => { b.classList.toggle('on', S.open && b.dataset.k === S.active); b.setAttribute('aria-expanded', String(S.open && b.dataset.k === S.active)); });
    if (first) { void pop.offsetWidth; pop.classList.remove('nt'); }
    const s2 = Math.min(1, stage.clientWidth / 400);
    stage.style.setProperty('--fit', s2.toFixed(4));
    stage.style.height = (total * s2) + 'px';
  }
  function choose(o, first) {
    S.opt = o;
    card.querySelectorAll('.cz-pk').forEach((w) => { const on = w.dataset.o === o; w.classList.toggle('on', on); w.setAttribute('aria-hidden', String(!on)); w.inert = !on; });
    if (!first) { S.open = true; place(); }
    refresh(null);
  }
  window.CZ = { build, choose, optA: (o) => pickers.A.opts(o), names: NAMES };
})();
