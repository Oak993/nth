// IconKit and the MarrowIcons registry: the shared helpers every app-icon module uses (contract: ICON-MODULE.md).
// Classic script, no eval, no network, no storage, no timers.
(function () {
  const reg = [];
  const MarrowIcons = {
    register(m) {
      const need = ['letter', 'id', 'name', 'meaning', 'story', 'swatch', 'launch', 'draw'];
      for (const k of need) if (!(k in m)) throw new Error('icon module ' + (m.letter || '?') + ' lacks ' + k);
      const i = reg.findIndex((x) => x.letter === m.letter);
      if (i >= 0) reg[i] = m; else reg.push(m);
      reg.sort((a, b) => a.letter.localeCompare(b.letter));
    },
    all: () => reg.slice(),
    get: (letter) => reg.find((x) => x.letter === letter),
    // renders one frame of a module into a canvas at a design size and a device scale
    render(canvas, m, size, theme, t, dpr) {
      dpr = dpr || 1;
      const px = Math.round(size * dpr);
      if (canvas.width !== px) canvas.width = px;
      if (canvas.height !== px) canvas.height = px;
      const ctx = canvas.getContext('2d');
      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.clearRect(0, 0, px, px);
      ctx.setTransform(px / size, 0, 0, px / size, 0, 0);
      t = t || STILL;
      m.draw(ctx, size, theme || 'dark', { launch: t.launch == null ? 1 : t.launch, idle: t.idle || 0, still: !!t.still });
    },
  };
  const STILL = { launch: 1, idle: 0, still: true };
  MarrowIcons.STILL = STILL;

  // ------------------------------------------------------------------ numbers and colours
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const lerp = (a, b, u) => a + (b - a) * u;
  function hex(c) {
    c = c.replace('#', '');
    if (c.length === 3) c = c.split('').map((x) => x + x).join('');
    const n = parseInt(c.slice(0, 6), 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255, c.length === 8 ? parseInt(c.slice(6, 8), 16) / 255 : 1];
  }
  function mix(a, b, u) {
    const A = hex(a), B = hex(b);
    return 'rgba(' + Math.round(lerp(A[0], B[0], u)) + ',' + Math.round(lerp(A[1], B[1], u)) + ',' + Math.round(lerp(A[2], B[2], u)) + ',' + +lerp(A[3], B[3], u).toFixed(3) + ')';
  }
  function alpha(c, a) {
    if (c.startsWith('#')) { const A = hex(c); return 'rgba(' + A[0] + ',' + A[1] + ',' + A[2] + ',' + +(A[3] * a).toFixed(3) + ')'; }
    const m = c.match(/rgba?\(([^)]+)\)/);
    if (!m) return c;
    const p = m[1].split(',').map((x) => x.trim());
    return 'rgba(' + p[0] + ',' + p[1] + ',' + p[2] + ',' + +((p[3] == null ? 1 : +p[3]) * a).toFixed(3) + ')';
  }
  const seg = (p, a, b) => clamp((p - a) / (b - a), 0, 1);
  const ease = {
    out: (u) => 1 - Math.pow(1 - u, 3),
    inOut: (u) => (u < 0.5 ? 4 * u * u * u : 1 - Math.pow(-2 * u + 2, 3) / 2),
    expo: (u) => (u <= 0 ? 0 : u >= 1 ? 1 : (1 - Math.pow(2, -10 * u)) / (1 - Math.pow(2, -10))),
    back: (u) => { const c1 = 1.15, c3 = c1 + 1; return u >= 1 ? 1 : 1 + c3 * Math.pow(u - 1, 3) + c1 * Math.pow(u - 1, 2); },
    // a critically damped settle with one soft overshoot; exactly 1 at 1
    spring: (u) => { if (u >= 1) return 1; const v = 1 - Math.exp(-6.5 * u) * Math.cos(5.2 * u); const end = 1 - Math.exp(-6.5) * Math.cos(5.2); return v + (1 - end) * u; },
    // the macOS-like standard curve, cubic-bezier(.32,.72,0,1)
    mac: (u) => bez(0.32, 0.72, 0, 1, u),
    sine: (u) => 0.5 - 0.5 * Math.cos(Math.PI * u),
  };
  function bez(x1, y1, x2, y2, x) {
    if (x <= 0) return 0; if (x >= 1) return 1;
    let t = x;
    for (let i = 0; i < 8; i++) {
      const cx = 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t - x;
      const dx = 3 * x1 * (1 - t) * (1 - t) + 6 * (x2 - x1) * t * (1 - t) + 3 * (1 - x2) * t * t;
      if (Math.abs(cx) < 1e-5) break;
      t = clamp(t - cx / (dx || 1e-6), 0, 1);
    }
    return 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
  }
  function rng(seed) {
    let s = (seed >>> 0) || 1;
    return () => { s ^= s << 13; s >>>= 0; s ^= s >> 17; s ^= s << 5; s >>>= 0; return s / 4294967296; };
  }
  const loop = (seconds, ms) => (ms ? (((seconds * 1000) % ms) + ms) % ms / ms : 0);

  // ------------------------------------------------------------------ sizes
  const band = (s) => (s <= 20 ? 'tiny' : s <= 32 ? 'small' : s <= 96 ? 'medium' : 'large');
  function box(s) {
    const b = band(s);
    const m = b === 'tiny' || b === 'small' ? 1 : Math.round(s * 0.06 * 100) / 100;
    return { x: m, y: m, w: s - 2 * m, h: s - 2 * m };
  }
  let curScale = 1;
  const scaleOf = (ctx) => { const tr = ctx.getTransform(); return Math.hypot(tr.a, tr.b) || 1; };
  function px(v, ctx) { const s = ctx ? scaleOf(ctx) : curScale; return Math.round(v * s) / s; }

  // ------------------------------------------------------------------ paths and paints
  // a superellipse-like rounded rect: each corner is a cubic with control points pulled along the edge (n: smoothing)
  function squircle(ctx, x, y, w, h, r, n) {
    n = n == null ? 0.6 : n;
    r = Math.min(r, w / 2, h / 2);
    const e = r * (1 + n * 0.55);         // the corner starts this far from the vertex (continuous curvature look)
    const ex = Math.min(e, w / 2), ey = Math.min(e, h / 2);
    const k = 0.5 + n * 0.12;             // control-point pull
    ctx.moveTo(x + ex, y);
    ctx.lineTo(x + w - ex, y);
    ctx.bezierCurveTo(x + w - ex * (1 - k) * 0.6, y, x + w, y + ey * (1 - k) * 0.6, x + w, y + ey);
    ctx.lineTo(x + w, y + h - ey);
    ctx.bezierCurveTo(x + w, y + h - ey * (1 - k) * 0.6, x + w - ex * (1 - k) * 0.6, y + h, x + w - ex, y + h);
    ctx.lineTo(x + ex, y + h);
    ctx.bezierCurveTo(x + ex * (1 - k) * 0.6, y + h, x, y + h - ey * (1 - k) * 0.6, x, y + h - ey);
    ctx.lineTo(x, y + ey);
    ctx.bezierCurveTo(x, y + ey * (1 - k) * 0.6, x + ex * (1 - k) * 0.6, y, x + ex, y);
    ctx.closePath();
  }
  function circle(ctx, cx, cy, r) { ctx.moveTo(cx + r, cy); ctx.arc(cx, cy, Math.max(0, r), 0, Math.PI * 2); ctx.closePath(); }
  function lin(ctx, x0, y0, x1, y1, stops) { const g = ctx.createLinearGradient(x0, y0, x1, y1); stops.forEach((s) => g.addColorStop(s[0], s[1])); return g; }
  function rad(ctx, cx, cy, r, stops, fx, fy, fr) {
    const g = ctx.createRadialGradient(fx == null ? cx : fx, fy == null ? cy : fy, fr || 0, cx, cy, Math.max(0.001, r));
    stops.forEach((s) => g.addColorStop(s[0], s[1])); return g;
  }

  // ------------------------------------------------------------------ blur and glow
  const FILTER_OK = (() => { try { const c = document.createElement('canvas').getContext('2d'); c.filter = 'blur(2px)'; return c.filter === 'blur(2px)'; } catch (e) { return false; } })();
  const pool = [];
  function scratch(w, h) {
    let c = pool.pop() || document.createElement('canvas');
    if (c.width < w || c.height < h) { c.width = Math.max(c.width, w); c.height = Math.max(c.height, h); }
    const x = c.getContext('2d'); x.setTransform(1, 0, 0, 1, 0, 0); x.globalAlpha = 1; x.globalCompositeOperation = 'source-over'; if (FILTER_OK) x.filter = 'none'; x.shadowBlur = 0; x.shadowColor = 'rgba(0,0,0,0)'; x.clearRect(0, 0, c.width, c.height);
    return c;
  }
  function blur(ctx, r, fn) {
    if (!(r > 0.05)) { ctx.save(); fn(ctx); ctx.restore(); return; }
    // Everything fn draws goes into one small offscreen layer (a blurred picture needs no full resolution: about one
    // layer pixel per 2.5 px of blur), is blurred there in one pass, and is scaled back up with smoothing. One filter
    // pass per call however many shapes fn draws, at a fraction of the pixels; the layer composites with ctx's own
    // alpha, composite mode and clip, as the shapes would have.
    const s = scaleOf(ctx), rd = r * s;
    const cw = ctx.canvas.width, ch = ctx.canvas.height;
    const k = FILTER_OK ? Math.max(1, Math.min(8, rd / 2.5)) : Math.max(1, Math.min(16, rd / 1.6));
    const pad = 2, sw = Math.max(1, Math.ceil(cw / k)) + pad * 2, sh = Math.max(1, Math.ceil(ch / k)) + pad * 2;
    const tmp = scratch(sw, sh), tx = tmp.getContext('2d');
    const tr = ctx.getTransform();
    tx.setTransform(tr.a / k, tr.b / k, tr.c / k, tr.d / k, tr.e / k + pad, tr.f / k + pad);
    tx.globalCompositeOperation = ctx.globalCompositeOperation;
    fn(tx);
    let src = tmp;
    if (FILTER_OK) {
      const t2 = scratch(sw, sh), x2 = t2.getContext('2d');
      x2.filter = 'blur(' + (rd / k).toFixed(2) + 'px)'; x2.drawImage(tmp, 0, 0); x2.filter = 'none';
      pool.push(tmp); src = t2;
    }
    ctx.save(); ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.imageSmoothingEnabled = true; ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(src, 0, 0, sw, sh, -pad * k, -pad * k, sw * k, sh * k); ctx.restore();
    pool.push(src);
  }
  function glow(ctx, color, b, fn) {
    ctx.save(); const s = scaleOf(ctx); ctx.shadowColor = color; ctx.shadowBlur = b * s; ctx.shadowOffsetX = 0; ctx.shadowOffsetY = 0; fn(ctx); ctx.restore();
  }

  // ------------------------------------------------------------------ cache
  const store = new Map();
  function cache(key, w, h, fn) {
    let c = store.get(key);
    if (c) { store.delete(key); store.set(key, c); return c; }
    c = document.createElement('canvas'); c.width = Math.max(1, Math.round(w)); c.height = Math.max(1, Math.round(h));
    fn(c.getContext('2d'), c);
    store.set(key, c);
    if (store.size > 260) store.delete(store.keys().next().value);
    return c;
  }
  // draws a cached static layer: key, the icon size; fn(octx) draws in icon units like draw() itself
  function layer(ctx, key, size, fn) {
    // the scale is rounded up to a few steps, so a launch's scale animation reuses one cached layer
    const s0 = scaleOf(ctx), s = [0.5, 1, 1.5, 2, 3, 4, 6, 8].find((q) => q >= s0 - 1e-6) || s0, px2 = Math.round(size * s);
    const c = cache(key + '|' + size + '|' + px2, px2, px2, (o) => { o.setTransform(px2 / size, 0, 0, px2 / size, 0, 0); fn(o); });
    // drawn under the current transform, so a launch may move, scale or turn a cached layer
    ctx.drawImage(c, 0, 0, size, size);
  }

  window.MarrowIcons = MarrowIcons;
  window.IconKit = { band, box, px, squircle, circle, lin, rad, blur, glow, cache, layer, rng, clamp, lerp, mix, alpha, seg, ease, bez, loop, scale: scaleOf, FILTER_OK };
})();
