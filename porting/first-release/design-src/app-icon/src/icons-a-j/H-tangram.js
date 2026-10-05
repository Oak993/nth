// H Tangram: seven pieces of cast glass, ice blue to amethyst, that fit together into one square tile.
(function () {
  const K = IconKit, PI = Math.PI;
  // the classic seven pieces of a unit square, with their glass colours [light, deep]
  const PIECES = [
    { p: [[0, 0], [1, 0], [0.5, 0.5]], c: ['#6f9bff', '#1a3cc0'] },              // sapphire, large
    { p: [[0, 0], [0.5, 0.5], [0, 1]], c: ['#7ee0ff', '#1677c9'] },              // ruby, large
    { p: [[1, 0.5], [1, 1], [0.5, 1]], c: ['#b07cff', '#4b18b8'] },              // emerald, medium
    { p: [[0.5, 0.5], [0.75, 0.25], [0.75, 0.75]], c: ['#9ab8ff', '#3b55e0'] },  // amber, small
    { p: [[0, 1], [0.25, 0.75], [0.5, 1]], c: ['#5ac8f0', '#0d5fa8'] },          // amethyst, small
    { p: [[0.5, 0.5], [0.75, 0.75], [0.5, 1], [0.25, 0.75]], c: ['#c6d8ff', '#5a6cf0'] }, // aquamarine, square
    { p: [[0.75, 0.25], [1, 0], [1, 0.5], [0.75, 0.75]], c: ['#8f6dff', '#3220b0'] },     // tangerine, parallelogram
  ];
  // where each piece starts in the launch: a direction, a turn
  const FLY = [[0, -1, -0.5], [-1, 0, 0.6], [1, 1, -0.4], [1, -0.3, 0.9], [-1, 1, -0.8], [0, 1, 0.5], [1, 0, -0.7]];
  const ORDER = [1, 0, 2, 6, 5, 3, 4];

  function rect(size) { const b = K.box(size), band = K.band(size); return { x: b.x, y: b.y, w: b.w, h: b.h, r: b.w * (band === 'tiny' || band === 'small' ? 0.2 : 0.2) }; }
  function inset(pts, d) {
    // move every edge inward by d (the gap between pieces), for convex polygons
    const n = pts.length, lines = [];
    let area = 0; for (let i = 0; i < n; i++) { const a = pts[i], b = pts[(i + 1) % n]; area += a[0] * b[1] - b[0] * a[1]; }
    const sgn = area > 0 ? 1 : -1;
    for (let i = 0; i < n; i++) {
      const a = pts[i], b = pts[(i + 1) % n], dx = b[0] - a[0], dy = b[1] - a[1], L = Math.hypot(dx, dy);
      const nx = -dy / L * sgn, ny = dx / L * sgn;
      lines.push([a[0] + nx * d, a[1] + ny * d, dx, dy]);
    }
    const out = [];
    for (let i = 0; i < n; i++) {
      const l1 = lines[(i + n - 1) % n], l2 = lines[i];
      const den = l1[2] * l2[3] - l1[3] * l2[2];
      const t = ((l2[0] - l1[0]) * l2[3] - (l2[1] - l1[1]) * l2[2]) / den;
      out.push([l1[0] + l1[2] * t, l1[1] + l1[3] * t]);
    }
    return out;
  }
  // the solved square sits turned 45 degrees in the tray, as a diamond
  function map(q, P, band) { const S = P.w * (band === 'tiny' || band === 'small' ? 0.7 : 0.64), u = (q[0] - 0.5) * S, v = (q[1] - 0.5) * S, c = Math.SQRT1_2; return [P.x + P.w / 2 + (u - v) * c, P.y + P.h / 2 + (u + v) * c]; }
  function centroid(pts) { let x = 0, y = 0; pts.forEach((p) => { x += p[0]; y += p[1]; }); return [x / pts.length, y / pts.length]; }
  function roundPoly(o, pts, r) {
    const n = pts.length;
    for (let i = 0; i < n; i++) {
      const p = pts[i], q = pts[(i + 1) % n], pr = pts[(i + n - 1) % n];
      const d1 = Math.hypot(p[0] - pr[0], p[1] - pr[1]), d2 = Math.hypot(q[0] - p[0], q[1] - p[1]);
      const rr = Math.min(r, d1 / 3, d2 / 3);
      const a1 = [p[0] + (pr[0] - p[0]) * rr / d1, p[1] + (pr[1] - p[1]) * rr / d1], a2 = [p[0] + (q[0] - p[0]) * rr / d2, p[1] + (q[1] - p[1]) * rr / d2];
      if (i === 0) o.moveTo(a1[0], a1[1]); else o.lineTo(a1[0], a1[1]);
      o.quadraticCurveTo(p[0], p[1], a2[0], a2[1]);
    }
    o.closePath();
  }

  function tray(c, size, theme) {
    const p = rect(size), band = K.band(size);
    if (band === 'medium' || band === 'large') K.blur(c, size * 0.03, (o) => { o.fillStyle = theme === 'light' ? 'rgba(20,20,40,.3)' : 'rgba(0,0,0,.6)'; o.beginPath(); K.squircle(o, p.x + p.w * 0.03, p.y + p.h * 0.05, p.w * 0.94, p.h * 0.96, p.r); o.fill(); });
    c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r);
    c.fillStyle = K.lin(c, p.x, p.y, p.x, p.y + p.h, [[0, '#1a1d26'], [1, '#0b0d12']]); c.fill();
  }
  function piece(c, size, pc, pts, band, sweep) {
    const P = rect(size);
    const toXY = (q) => map(q, P, band);
    const gap = band === 'tiny' ? 0.05 : band === 'small' ? 0.045 : 0.022;
    const ip = inset(pts, gap / 2).map(toXY);
    const cen = centroid(ip);
    const rr = band === 'tiny' || band === 'small' ? 0.5 : size * 0.012;
    c.beginPath(); roundPoly(c, ip, rr);
    // cast glass: lit through from the upper left, deeper toward the far edge
    const ext = P.w * 0.55;
    c.fillStyle = K.lin(c, cen[0] - ext * 0.5, cen[1] - ext * 0.5, cen[0] + ext * 0.5, cen[1] + ext * 0.5, [[0, pc.c[0]], [1, pc.c[1]]]);
    c.fill();
    if (band === 'tiny' || band === 'small') return;
    c.save(); c.beginPath(); roundPoly(c, ip, rr); c.clip();
    // the glow inside the glass, then the bevel: a bright upper-left lip, a dark lower-right one
    c.globalCompositeOperation = 'lighter';
    c.fillStyle = K.rad(c, cen[0] - P.w * 0.02, cen[1] - P.w * 0.03, P.w * 0.22, [[0, 'rgba(255,255,255,.16)'], [1, 'rgba(255,255,255,0)']]);
    c.fillRect(P.x, P.y, P.w, P.h);
    c.globalCompositeOperation = 'source-over';
    const bw = size * 0.022;
    K.blur(c, size * 0.006, (o) => {
      o.lineWidth = bw; o.lineJoin = 'round';
      o.strokeStyle = 'rgba(255,255,255,.55)'; o.save(); o.translate(bw * 0.25, bw * 0.25); o.beginPath(); roundPoly(o, ip, rr); o.stroke(); o.restore();
      o.strokeStyle = 'rgba(0,0,0,.35)'; o.save(); o.translate(-bw * 0.25, -bw * 0.25); o.beginPath(); roundPoly(o, ip, rr); o.stroke(); o.restore();
    });
    // the idle light sweep: a soft diagonal band crossing the tile
    if (sweep > -0.4 && sweep < 1.4) {
      const sx = P.x + P.w * sweep, g = c.createLinearGradient(sx - P.w * 0.3, P.y, sx + P.w * 0.3, P.y + P.h * 0.4);
      g.addColorStop(0, 'rgba(255,255,255,0)'); g.addColorStop(0.5, 'rgba(255,255,255,.32)'); g.addColorStop(1, 'rgba(255,255,255,0)');
      c.fillStyle = g; c.fillRect(P.x, P.y, P.w, P.h);
    }
    c.restore();
    c.lineWidth = Math.max(0.6, size * 0.0025); c.strokeStyle = 'rgba(255,255,255,.4)'; c.beginPath(); roundPoly(c, ip, rr); c.stroke();
  }
  function rim(c, size, theme) {
    const p = rect(size), band = K.band(size);
    const lw = band === 'tiny' || band === 'small' ? 1 : Math.max(0.75, size * 0.003);
    c.lineWidth = lw; c.strokeStyle = theme === 'light' ? 'rgba(0,0,0,.45)' : 'rgba(255,255,255,.18)';
    c.beginPath(); K.squircle(c, p.x + lw / 2, p.y + lw / 2, p.w - lw, p.h - lw, p.r); c.stroke();
  }

  MarrowIcons.register({
    letter: 'H', id: 'tangram', name: 'Tangram',
    meaning: 'It takes a hard problem apart and fits every piece back where it belongs.',
    story: 'The seven pieces of a tangram in cast glass, from ice blue through sapphire to amethyst, fitted into one solved square that stands as a diamond on a dark tray. Each piece is lit through and bevelled like a real casting.',
    swatch: ['#1677c9', '#3b55e0', '#4b18b8'],
    launch: { ms: 1200, note: 'The pieces glide in from around the tile, turning as they come, and click into the square one after another.' },
    idle: { ms: 6000, note: 'A soft band of light passes diagonally across the glass, then the tile rests.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size), L = t.launch, P = rect(size);
      const ph = K.loop(t.idle, 6000);
      // the sweep crosses during the first 45% of the loop and is off the tile otherwise (the still has none)
      const sweep = ph < 0.45 ? -0.5 + 2 * K.ease.inOut(ph / 0.45) : -1;
      ctx.save();
      const a = K.ease.out(K.seg(L, 0, 0.3));
      ctx.globalAlpha = a;
      if (band === 'medium' || band === 'large') K.layer(ctx, 'H-tray-' + theme, size, (o) => tray(o, size, theme)); else tray(ctx, size, theme);
      ctx.save(); ctx.beginPath(); K.squircle(ctx, P.x, P.y, P.w, P.h, P.r); ctx.clip();
      ORDER.forEach((pi, k) => {
        const pc = PIECES[pi], f = L >= 1 ? 1 : K.ease.mac(K.seg(L, 0.06 + k * 0.075, 0.5 + k * 0.075));
        if (f <= 0) return;
        const cen = map(centroid(pc.p), P, band), cx = cen[0], cy = cen[1], fl = FLY[pi];
        ctx.save();
        ctx.globalAlpha = K.ease.out(K.seg(f, 0, 0.4));
        ctx.translate(cx + fl[0] * (1 - f) * size * 0.45, cy + fl[1] * (1 - f) * size * 0.45);
        ctx.rotate(fl[2] * (1 - f) * 1.4);
        const sc = 0.85 + 0.15 * f; ctx.scale(sc, sc);
        ctx.translate(-cx, -cy);
        piece(ctx, size, pc, pc.p, band, sweep);
        ctx.restore();
      });
      ctx.restore();
      ctx.globalAlpha = a;
      rim(ctx, size, theme);
      ctx.restore();
    },
  });
})();
