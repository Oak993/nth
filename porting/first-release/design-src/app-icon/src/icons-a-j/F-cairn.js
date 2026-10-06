// F Cairn: three river stones balanced in a cairn; the top one is a moonstone with a blue light inside.
(function () {
  const K = IconKit, PI = Math.PI;
  // the stones, in units of the safe box: centre, radii, tilt, superellipse power, a seed for the outline
  const STONES = [
    { x: 0.5, y: 0.79, rx: 0.43, ry: 0.145, tilt: -0.03, n: 2.5, seed: 3, kind: 'basalt' },
    { x: 0.53, y: 0.565, rx: 0.3, ry: 0.115, tilt: 0.06, n: 2.4, seed: 8, kind: 'sand' },
    { x: 0.485, y: 0.34, rx: 0.2, ry: 0.15, tilt: -0.08, n: 2.15, seed: 13, kind: 'moon' },
  ];
  const WOB = {};
  function wob(seed) { if (WOB[seed]) return WOB[seed]; const r = K.rng(seed), a = []; for (let k = 2; k <= 5; k++) a.push([k, (r() - 0.5) * 0.05 / (k - 1), r() * 2 * PI]); return (WOB[seed] = a); }
  function stonePath(o, s, b, sx, sy) {
    const cx = b.x + b.w * s.x, cy = b.y + b.h * s.y, rx = b.w * s.rx * sx, ry = b.h * s.ry * sy, w = wob(s.seed);
    const n = 48;
    for (let i = 0; i <= n; i++) {
      const t = (i / n) * 2 * PI, c = Math.cos(t), si = Math.sin(t);
      let r = Math.pow(Math.pow(Math.abs(c), s.n) + Math.pow(Math.abs(si), s.n), -1 / s.n);
      w.forEach(([k, a, p]) => { r *= 1 + a * Math.sin(k * t + p); });
      // flatter underneath, like a stone resting on another
      const fy = si > 0 ? 0.86 : 1;
      const x = c * r * rx, y = si * r * ry * fy;
      const X = cx + x * Math.cos(s.tilt) - y * Math.sin(s.tilt), Y = cy + x * Math.sin(s.tilt) + y * Math.cos(s.tilt);
      if (i) o.lineTo(X, Y); else o.moveTo(X, Y);
    }
    o.closePath();
  }
  function box(size) { const b = K.box(size), band = K.band(size); if (band === 'tiny' || band === 'small') return { x: 0, y: 0, w: size, h: size }; return b; }
  const PAL = {
    basalt: { hi: '#7d858f', mid: '#454b54', lo: '#1b1e23', rim: 'rgba(255,255,255,.18)' },
    sand: { hi: '#f1dcbb', mid: '#c8a37a', lo: '#7e5d3d', rim: 'rgba(255,240,215,.4)' },
    moon: { hi: '#eaf1f9', mid: '#b4c2d4', lo: '#5f6d84', rim: 'rgba(255,255,255,.6)' },
  };

  function stone(c, size, s, b, sx, sy, glow, sheen) {
    const band = K.band(size), P = PAL[s.kind];
    const cx = b.x + b.w * s.x, cy = b.y + b.h * s.y, rx = b.w * s.rx, ry = b.h * s.ry;
    if (s.kind === 'moon' && glow > 0 && band !== 'tiny') {
      // the moonstone's halo
      c.save(); c.globalCompositeOperation = 'lighter';
      c.fillStyle = K.rad(c, cx, cy, rx * 1.7, [[0.3, 'rgba(130,180,255,' + (0.3 * glow).toFixed(3) + ')'], [1, 'rgba(110,160,255,0)']]);
      c.beginPath(); K.circle(c, cx, cy, rx * 1.7); c.fill(); c.restore();
    }
    c.beginPath(); stonePath(c, s, b, sx, sy);
    c.fillStyle = K.rad(c, cx - rx * 0.35, cy - ry * 0.75, rx * 1.35, [[0, P.hi], [0.45, P.mid], [1, P.lo]]);
    c.fill();
    if (band === 'tiny') {
      if (s.kind === 'moon') { c.fillStyle = 'rgba(120,180,255,' + (0.85 * glow).toFixed(3) + ')'; c.beginPath(); c.ellipse(cx + rx * 0.1, cy + ry * 0.1, rx * 0.55, ry * 0.5, 0, 0, 2 * PI); c.fill(); }
      return;
    }
    c.save(); c.beginPath(); stonePath(c, s, b, sx, sy); c.clip();
    if (s.kind === 'basalt' && band === 'large') {
      const r = K.rng(31); for (let i = 0; i < 260; i++) { c.fillStyle = r() < 0.5 ? 'rgba(255,255,255,.07)' : 'rgba(0,0,0,.14)'; c.fillRect(cx + (r() - 0.5) * 2 * rx, cy + (r() - 0.5) * 2 * ry, size * 0.004, size * 0.004); }
    }
    if (s.kind === 'sand' && (band === 'large' || band === 'medium')) {
      // faint bedding lines in the sandstone
      c.strokeStyle = 'rgba(120,85,50,.09)'; c.lineWidth = Math.max(0.5, size * 0.004);
      for (let i = -2; i <= 2; i++) { c.beginPath(); c.moveTo(cx - rx * 1.1, cy + i * ry * 0.36 + ry * 0.1); c.bezierCurveTo(cx - rx * 0.3, cy + i * ry * 0.36 - ry * 0.12, cx + rx * 0.4, cy + i * ry * 0.36 + ry * 0.2, cx + rx * 1.1, cy + i * ry * 0.36); c.stroke(); }
    }
    if (s.kind === 'moon') {
      // adularescence: a blue light that floats inside the stone and drifts with the light
      c.globalCompositeOperation = 'lighter';
      const gx = cx + rx * (0.12 + 0.18 * sheen), gy = cy + ry * (0.05 - 0.1 * sheen);
      K.blur(c, rx * 0.12, (o) => { o.fillStyle = K.rad(o, gx, gy, rx * 0.85, [[0, 'rgba(110,165,255,' + (0.7 * glow).toFixed(3) + ')'], [0.5, 'rgba(80,130,255,' + (0.32 * glow).toFixed(3) + ')'], [1, 'rgba(90,140,255,0)']]); o.beginPath(); o.ellipse(gx, gy, rx * 0.85, ry * 0.8, -0.3, 0, 2 * PI); o.fill(); });
      c.globalCompositeOperation = 'source-over';
    }
    // a soft sheen on the top, and the darker belly
    if (band === 'large' || band === 'medium') {
      K.blur(c, rx * 0.08, (o) => { o.fillStyle = 'rgba(255,255,255,' + (s.kind === 'moon' ? 0.5 : s.kind === 'sand' ? 0.3 : 0.22) + ')'; o.beginPath(); o.ellipse(cx - rx * 0.3, cy - ry * 0.55, rx * 0.42, ry * 0.2, -0.1, 0, 2 * PI); o.fill(); });
      c.fillStyle = K.lin(c, cx, cy, cx, cy + ry, [[0, 'rgba(0,0,0,0)'], [1, 'rgba(0,0,0,.28)']]); c.fillRect(cx - rx * 1.2, cy, rx * 2.4, ry * 1.2);
    }
    c.restore();
    if (band === 'small') { c.lineWidth = 1; c.strokeStyle = 'rgba(0,0,0,.35)'; c.beginPath(); stonePath(c, s, b, sx, sy); c.stroke(); }
  }
  // the dark contact where an upper stone rests on the one below it
  function contact(c, size, upper, lower, b, k) {
    if (K.band(size) === 'tiny' || k <= 0) return;
    const cx = b.x + b.w * upper.x, cy = b.y + b.h * (upper.y + upper.ry * 0.86);
    c.save(); c.beginPath(); stonePath(c, lower, b, 1, 1); c.clip(); c.globalAlpha *= k;
    K.blur(c, b.w * 0.03, (o) => { o.fillStyle = 'rgba(0,0,0,.55)'; o.beginPath(); o.ellipse(cx, cy + b.h * 0.012, b.w * upper.rx * 0.95, b.h * 0.05, upper.tilt, 0, 2 * PI); o.fill(); });
    c.restore();
  }

  MarrowIcons.register({
    letter: 'F', id: 'cairn', name: 'Cairn',
    meaning: 'Steady, balanced progress: each step placed with care, and the way marked for whoever comes next.',
    story: 'Three river stones balanced in a cairn: dark speckled basalt, warm banded sandstone, and on top a moonstone with a blue light floating inside it. Natural materials and one point of light; a quiet, grounded companion.',
    swatch: ['#454b54', '#c8a37a', '#8fb8ff'],
    launch: { ms: 1100, note: 'The stones come down one at a time and settle with a small rock, base first; when the moonstone lands, its light wakes.' },
    idle: { ms: 6400, note: 'The blue light drifts slowly inside the moonstone and its halo breathes.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size), b = box(size), L = t.launch;
      const ph = K.loop(t.idle, 6400), sheen = Math.sin(2 * PI * ph), breathe = Math.cos(2 * PI * ph);
      ctx.save();
      // the ground shadow
      const g0 = K.ease.out(K.seg(L, 0, 0.3));
      if (band === 'medium' || band === 'large') {
        ctx.globalAlpha = g0;
        const s0 = STONES[0];
        K.blur(ctx, b.w * 0.035, (o) => { o.fillStyle = theme === 'light' ? 'rgba(40,30,20,.38)' : 'rgba(0,0,0,.7)'; o.beginPath(); o.ellipse(b.x + b.w * s0.x, b.y + b.h * (s0.y + s0.ry * 0.8), b.w * s0.rx * 0.95, b.h * 0.045, 0, 0, 2 * PI); o.fill(); });
        ctx.globalAlpha = 1;
      }
      const glow = (L >= 1 ? 1 : K.ease.out(K.seg(L, 0.78, 1))) * (band === 'tiny' ? 1 : 0.88 + 0.12 * breathe);
      STONES.forEach((s, i) => {
        // each stone falls in its own window and lands with a small rock
        const a0 = 0.05 + i * 0.22, a1 = a0 + 0.3;
        const f = K.seg(L, a0, a1);
        if (f <= 0) return;
        const land = K.seg(L, a1, Math.min(a1 + 0.22, 0.97));   // settles before the end, so the last frames are the still
        const dy = -(1 - K.ease.inOut(f)) * size * 0.22;
        const rock = L >= 1 ? 0 : Math.sin(land * PI * 2) * (1 - land) * 0.08 * (land > 0 ? 1 : 0);
        const sq = L >= 1 ? 0 : (land > 0 ? Math.sin(land * PI) * (1 - land) * 0.08 : 0);
        ctx.save();
        ctx.globalAlpha = K.ease.out(K.seg(f, 0, 0.35));
        if (i > 0) contact(ctx, size, s, STONES[i - 1], b, K.ease.out(K.seg(f, 0.6, 1)));
        const px = b.x + b.w * s.x, py = b.y + b.h * (s.y + s.ry * 0.86);
        if (dy || rock) { ctx.translate(px, py + dy); ctx.rotate(rock); ctx.translate(-px, -py); }
        stone(ctx, size, s, b, 1 + sq, 1 - sq, s.kind === 'moon' ? glow : 0, sheen);
        ctx.restore();
      });
      ctx.restore();
    },
  });
})();
