// T Weave: silk cords braided into one strand on deep teal, bound at the end with a gold cap.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;
  const TILT = -0.42; // the braid leans so it runs from upper left to lower right

  const CORDS = [
    { base: '#c99a3a', mid: '#f0c860', hi: '#fff1c2', dark: '#7a5514' },   // gold
    { base: '#d9d1c2', mid: '#f6f1e6', hi: '#ffffff', dark: '#9a907c' },   // ivory
    { base: '#d8624f', mid: '#f48a72', hi: '#ffd6c8', dark: '#8c2c22' },   // coral
  ];

  // strand k at parameter t (0 top .. 1 bottom), in braid space (x across, y along, z toward the viewer)
  function strand(k, t, band) {
    const turns = band === 'tiny' ? 1.5 : band === 'small' ? 2 : 2.6;
    const fan = 1 - K.ease.inOut(K.clamp(t / 0.28, 0, 1)); // the loose ends at the top fan apart
    const a = t * turns * TAU + (k * TAU) / 3;
    const A = 0.075;
    const xb = A * Math.cos(a), zb = Math.sin(2 * a);
    const spread = (k - 1) * 0.135;
    return [K.lerp(xb, spread, fan), K.lerp(-0.33, 0.3, t), K.lerp(zb, 0, fan) + (k - 1) * 0.01 * fan];
  }

  function toWorld(c, size) { c.translate(size * 0.5, size * 0.5); c.rotate(TILT); c.scale(size * 1.08, size * 1.08); }

  const N_OF = (band) => (band === 'tiny' ? 40 : band === 'small' ? 60 : 200);
  function width(size, band) { return band === 'tiny' ? 2 / size : band === 'small' ? Math.max(2, size * 0.075) / size : 0.058; }

  // one cord as a continuous tube: outline, body, lit side, sheen line; twist marks on large
  function cord(o, size, band, k) {
    const C = CORDS[k], W = width(size, band), N = N_OF(band), pts = [];
    for (let i = 0; i <= N; i++) pts.push(strand(k, i / N, band));
    const path = (dx) => { o.beginPath(); pts.forEach((p, i) => (i ? o.lineTo(p[0] + dx, p[1]) : o.moveTo(p[0] + dx, p[1]))); };
    o.save(); toWorld(o, size); o.lineCap = 'round'; o.lineJoin = 'round';
    if (band === 'tiny') { o.strokeStyle = C.mid; o.lineWidth = W; path(0); o.stroke(); o.restore(); return; }
    o.strokeStyle = C.dark; o.lineWidth = W * 1.06; path(0); o.stroke();
    o.strokeStyle = C.base; o.lineWidth = W * 0.9; path(0); o.stroke();
    if (band !== 'small') {
      o.strokeStyle = C.mid; o.lineWidth = W * 0.55; path(-W * 0.12); o.stroke();
      o.strokeStyle = K.alpha(C.hi, 0.85); o.lineWidth = W * 0.15; path(-W * 0.22); o.stroke();
    }
    if (band === 'large') {
      // the twist of the silk: fine slanted lines across the cord
      o.lineWidth = 0.003; o.strokeStyle = K.alpha(C.dark, 0.32);
      for (let i = 1; i < N; i += 2) {
        const a = pts[i - 1], b = pts[i + 1], dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy) || 1, nx = -dy / l, ny = dx / l, m = pts[i], h = W * 0.42;
        o.beginPath(); o.moveTo(m[0] + nx * h - dx * 1.1, m[1] + ny * h - dy * 1.1); o.lineTo(m[0] - nx * h + dx * 1.1, m[1] - ny * h + dy * 1.1); o.stroke();
      }
    }
    o.restore();
  }

  // the whole braid as one layer: cut across its length into thin strips; in each strip the cords are laid
  // back to front by their depth there, so every over-and-under is right
  function braidLayer(o, size, band, px) {
    // each cord on its own temporary canvas (used only while the braid layer is built, then let go)
    const cords = [0, 1, 2].map((k) => { const c = document.createElement('canvas'); c.width = c.height = px; const q = c.getContext('2d'); q.setTransform(px / size, 0, 0, px / size, 0, 0); cord(q, size, band, k); return c; });
    const S = band === 'tiny' ? 24 : band === 'small' ? 40 : 90;
    const y0 = -0.45, y1 = 0.4;
    for (let i = 0; i < S; i++) {
      const ya = K.lerp(y0, y1, i / S), yb = K.lerp(y0, y1, (i + 1) / S);
      const t = K.clamp((((ya + yb) / 2) + 0.33) / 0.63, 0, 1);
      const order = [0, 1, 2].map((k) => ({ k, z: strand(k, t, band)[2] })).sort((p, q) => p.z - q.z);
      o.save(); toWorld(o, size); o.beginPath();
      o.rect(-0.6, i === 0 ? -1 : ya - 0.0005, 1.2, (i === S - 1 ? 1 : yb + 0.0005) - (i === 0 ? -1 : ya - 0.0005)); o.restore();
      o.save(); o.clip(); o.setTransform(1, 0, 0, 1, 0, 0);
      for (const { k } of order) o.drawImage(cords[k], 0, 0);
      o.restore();
    }
  }

  function braid(ctx, size, band, px) {
    const c = K.cache('T-weave|braid|' + size + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); braidLayer(o, size, band, px); });
    ctx.drawImage(c, 0, 0, size, size);
  }

  // the gold cap that binds the braid's end
  function cap(ctx, size, band, a) {
    if (a <= 0) return;
    ctx.save(); toWorld(ctx, size); ctx.globalAlpha = a;
    const y = 0.3, w = band === 'tiny' ? 3 / size : 0.075, h = band === 'tiny' ? 3 / size : 0.085;
    if (band === 'tiny') { ctx.fillStyle = '#f0c860'; ctx.fillRect(-w / 2, y - h * 0.3, w, h); ctx.restore(); return; }
    ctx.fillStyle = K.lin(ctx, -w, 0, w, 0, [[0, '#6e4a10'], [0.3, '#f6d27a'], [0.45, '#fff4cc'], [0.7, '#c08a2a'], [1, '#5a3a0a']]);
    ctx.beginPath(); K.squircle(ctx, -w, y - h * 0.25, 2 * w, h, w * 0.35, 0.4); ctx.fill();
    if (band !== 'small') {
      ctx.strokeStyle = 'rgba(90,60,10,0.7)'; ctx.lineWidth = 0.004;
      for (const f of [0.22, 0.78]) { ctx.beginPath(); ctx.moveTo(-w, y - h * 0.25 + h * f); ctx.lineTo(w, y - h * 0.25 + h * f); ctx.stroke(); }
    }
    ctx.restore();
  }

  function plate(o, size, band, theme) {
    const { x, y, w, h } = K.box(size), rad = w * 0.235;
    if (band === 'medium' || band === 'large') {
      K.blur(o, size * 0.025, (q) => { q.fillStyle = theme === 'light' ? 'rgba(0,30,30,0.38)' : 'rgba(0,0,0,0.55)'; q.beginPath(); K.squircle(q, x + w * 0.02, y + h * 0.035, w * 0.96, h * 0.96, rad); q.fill(); });
    }
    o.fillStyle = K.lin(o, x, y, x + w * 0.5, y + h, [[0, '#0f4d4f'], [0.55, '#08302f'], [1, '#041a1b']]);
    o.beginPath(); K.squircle(o, x, y, w, h, rad); o.fill();
    if (band === 'tiny') return;
    o.save(); o.beginPath(); K.squircle(o, x, y, w, h, rad); o.clip();
    o.fillStyle = K.rad(o, x + w * 0.3, y + h * 0.18, w * 0.7, [[0, 'rgba(120,220,200,0.18)'], [1, 'rgba(120,220,200,0)']]);
    o.fillRect(x, y, w, h);
    o.lineWidth = Math.max(1, size * 0.008);
    o.strokeStyle = K.lin(o, 0, y, 0, y + h, [[0, 'rgba(200,255,240,0.3)'], [0.35, 'rgba(200,255,240,0.05)'], [1, 'rgba(200,255,240,0.1)']]);
    o.beginPath(); K.squircle(o, x + o.lineWidth / 2, y + o.lineWidth / 2, w - o.lineWidth, h - o.lineWidth, rad - o.lineWidth / 2); o.stroke();
    o.restore();
    if (theme === 'light' && band !== 'small') { o.lineWidth = Math.max(1, size * 0.003); o.strokeStyle = 'rgba(0,30,30,0.35)'; o.beginPath(); K.squircle(o, x, y, w, h, rad); o.stroke(); }
  }

  function shadow(o, size, band) {
    K.blur(o, size * 0.02, (q) => {
      q.save(); q.translate(size * 0.012, size * 0.03); toWorld(q, size); q.lineCap = 'round';
      q.strokeStyle = 'rgba(0,10,10,0.55)'; q.lineWidth = 0.13;
      q.beginPath(); q.moveTo(0, -0.18); q.lineTo(0, 0.32); q.stroke();
      q.lineWidth = 0.05;
      for (const k of [0, 2]) { q.beginPath(); for (let i = 0; i <= 10; i++) { const p = strand(k, i / 10 * 0.3, band); i ? q.lineTo(p[0], p[1]) : q.moveTo(p[0], p[1]); } q.stroke(); }
      q.restore();
    });
  }

  MarrowIcons.register({
    letter: 'T',
    id: 'weave',
    name: 'Weave',
    meaning: 'Many providers, one thread of work: separate strands of thought braided into a single, stronger line.',
    story: 'Three silk cords, gold, ivory and coral, come in loose at the top and are plaited into one tight braid bound with a gold cap, lying on deep teal. Each model you bring keeps its colour, and together they make something none could alone. Silk, gold and teal.',
    swatch: ['#08302f', '#f0c860', '#f48a72'],
    launch: { ms: 1150, note: 'The three loose cords run in from the top and plait themselves together down the length of the braid; the gold cap closes over the end.' },
    idle: { ms: 6000, note: 'A slow sheen of light runs down the silk from the loose ends to the cap.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const px = Math.round(size * s0);
      const lay = (key, fn) => K.cache('T-weave|' + key + '|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); fn(o); });
      const L = t.launch;
      const big = band === 'medium' || band === 'large';
      ctx.save();
      if (L >= 1) {
        ctx.drawImage(lay('still', (o) => { plate(o, size, band, theme); if (big) shadow(o, size, band); braid(o, size, band, px); cap(o, size, band, 1); }), 0, 0, size, size);
        if (!t.idle) {
          // build the launch layers now, while the still is showing
          lay('plate', (o) => plate(o, size, band, theme)); if (big) lay('shadow', (o) => shadow(o, size, band));
          K.cache('T-weave|scratch|' + px, px, px, () => {});
        }
        if (t.idle > 0 && band !== 'tiny') {
          const ph = K.loop(t.idle, 6000), u = K.seg(ph, 0.1, 0.72), env = Math.sin(Math.PI * u);
          if (env > 0.002) {
            ctx.save(); toWorld(ctx, size);
            const yy = K.lerp(-0.45, 0.4, K.ease.inOut(u));
            ctx.beginPath(); ctx.rect(-0.25, -0.4, 0.5, 0.75); ctx.clip();
            ctx.globalCompositeOperation = 'lighter';
            ctx.fillStyle = K.lin(ctx, 0, yy - 0.12, 0, yy + 0.12, [[0, 'rgba(255,240,210,0)'], [0.5, 'rgba(255,240,210,' + (0.22 * env).toFixed(3) + ')'], [1, 'rgba(255,240,210,0)']]);
            // only over the cords: stroke the braid's footprint with the sheen
            ctx.strokeStyle = ctx.fillStyle; ctx.lineCap = 'round'; ctx.lineWidth = 0.05;
            for (let k = 0; k < 3; k++) { ctx.beginPath(); for (let i = 0; i <= 80; i++) { const p = strand(k, i / 80, band); i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]); } ctx.stroke(); }
            ctx.restore();
          }
        }
        ctx.restore(); return;
      }
      const aP = K.ease.sine(K.seg(L, 0, 0.3)), eP = K.ease.mac(K.seg(L, 0, 0.4));
      ctx.save(); ctx.globalAlpha = aP;
      if (eP < 1) { const k = K.lerp(0.96, 1, eP); ctx.translate(size / 2, size / 2); ctx.scale(k, k); ctx.translate(-size / 2, -size / 2); }
      ctx.drawImage(lay('plate', (o) => plate(o, size, band, theme)), 0, 0, size, size);
      ctx.restore();
      const tEnd = K.ease.inOut(K.seg(L, 0.14, 0.86));
      const aB = K.ease.sine(K.seg(L, 0.1, 0.26));
      if (big) { ctx.save(); ctx.globalAlpha = K.ease.sine(K.seg(L, 0.5, 1)); ctx.drawImage(lay('shadow', (o) => shadow(o, size, band)), 0, 0, size, size); ctx.restore(); }
      if (tEnd > 0) {
        // the braid forms from the loose ends downward: the finished braid, revealed along its length with a soft edge
        const m = K.cache('T-weave|scratch|' + px, px, px, () => {});
        const mx = m.getContext('2d');
        mx.setTransform(1, 0, 0, 1, 0, 0); mx.clearRect(0, 0, px, px); mx.globalCompositeOperation = 'source-over';
        mx.setTransform(px / size, 0, 0, px / size, 0, 0); braid(mx, size, band, px);
        mx.globalCompositeOperation = 'destination-in';
        mx.save(); toWorld(mx, size);
        const yEnd = K.lerp(-0.42, 0.66, tEnd), soft = 0.09;
        mx.fillStyle = K.lin(mx, 0, yEnd - soft, 0, yEnd, [[0, 'rgba(0,0,0,1)'], [1, 'rgba(0,0,0,0)']]);
        mx.fillRect(-1, -1, 2, 2);
        mx.restore(); mx.globalCompositeOperation = 'source-over';
        ctx.save(); ctx.globalAlpha = aB; ctx.drawImage(m, 0, 0, size, size); ctx.restore();
      }
      cap(ctx, size, band, K.ease.sine(K.seg(L, 0.78, 1)));
      ctx.restore();
    },
  });
})();
