// S Dragonfly: a dragonfly with wings of clear veined glass, hovering over a sage-green pond.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;
  const HEADING = -0.62; // the body leans so the head points up and to the right

  // wing roots and shapes in body space (x along the body toward the head, y across), units of size
  const WINGS = [
    { id: 'fl', root: [0.215, -0.02], tip: [0.25, -0.37], chord: 0.088, side: -1 },
    { id: 'fr', root: [0.215, 0.02], tip: [0.25, 0.37], chord: 0.088, side: 1 },
    { id: 'hl', root: [0.175, -0.024], tip: [0.12, -0.355], chord: 0.105, side: -1 },
    { id: 'hr', root: [0.175, 0.024], tip: [0.12, 0.355], chord: 0.105, side: 1 },
  ];

  function wingPath(c, w) {
    // long and narrow: a straight leading edge, the trailing edge widest a third of the way out, a rounded tip
    const [rx, ry] = w.root, [tx, ty] = w.tip;
    const L = Math.hypot(tx - rx, ty - ry), ux = (tx - rx) / L, uy = (ty - ry) / L;
    const back = w.side > 0 ? -1 : 1, nx = -uy * back, ny = ux * back;
    const N = 22, lead = [], trail = [];
    for (let i = 0; i <= N; i++) {
      const u = i / N;
      const tw = w.chord * Math.min(1, u * 5) * Math.pow(1 - u * 0.9, 0.4) * Math.sqrt(Math.max(0, 1 - Math.pow(u, 6)));
      const lw = -0.006 * Math.sin(Math.PI * u);
      lead.push([rx + ux * L * u + nx * lw, ry + uy * L * u + ny * lw]);
      trail.push([rx + ux * L * u + nx * tw, ry + uy * L * u + ny * tw]);
    }
    c.moveTo(lead[0][0], lead[0][1]);
    for (const p of lead) c.lineTo(p[0], p[1]);
    for (let i = N; i >= 0; i--) c.lineTo(trail[i][0], trail[i][1]);
    c.closePath();
  }

  function toWorld(c, size) { c.translate(size * 0.48, size * 0.53); c.rotate(HEADING); c.scale(size * 0.94, size * 0.94); c.translate(-0.06, 0); }

  function wing(o, size, band, w) {
    const tiny = band === 'tiny', small = band === 'small';
    o.save(); toWorld(o, size);
    if (tiny || small) {
      o.fillStyle = 'rgba(232,248,255,0.78)'; o.beginPath(); wingPath(o, w); o.fill();
      o.strokeStyle = 'rgba(40,70,90,0.55)'; o.lineWidth = (tiny ? 1 : 1) / size; o.stroke();
      o.restore(); return;
    }
    // clear glass: a faint frost, an iridescent wash, veins, a bright rim
    o.beginPath(); wingPath(o, w);
    o.fillStyle = K.lin(o, w.root[0], w.root[1], w.tip[0], w.tip[1], [[0, 'rgba(255,255,255,0.42)'], [0.5, 'rgba(235,250,255,0.2)'], [1, 'rgba(255,255,255,0.32)']]);
    o.fill();
    o.save(); o.beginPath(); wingPath(o, w); o.clip();
    const cg = o.createConicGradient(0.3 * w.side, w.root[0], w.root[1]);
    [[0, 'rgba(255,170,220,0.32)'], [0.2, 'rgba(160,230,255,0.32)'], [0.4, 'rgba(190,255,200,0.28)'], [0.6, 'rgba(255,240,170,0.3)'], [0.8, 'rgba(200,170,255,0.32)'], [1, 'rgba(255,170,220,0.32)']].forEach(([f, c]) => cg.addColorStop(f, c));
    o.fillStyle = cg; o.fillRect(-0.6, -0.6, 1.2, 1.2);
    // veins: longitudinal veins fanning from the root, cross veins between them
    o.strokeStyle = 'rgba(40,60,70,0.42)'; o.lineCap = 'round';
    const L = Math.hypot(w.tip[0] - w.root[0], w.tip[1] - w.root[1]);
    const ux = (w.tip[0] - w.root[0]) / L, uy = (w.tip[1] - w.root[1]) / L, nx = -uy, ny = ux;
    const back = w.side > 0 ? 1 : -1; // which normal points toward the trailing edge
    const lines = [];
    for (let k = 0; k < 5; k++) {
      const off = (k / 4) * w.chord * -back;
      const pts = [];
      for (let i = 0; i <= 16; i++) {
        const u = i / 16, bulge = Math.min(1, u * 5) * Math.pow(1 - u * 0.9, 0.4) * Math.sqrt(Math.max(0, 1 - Math.pow(u, 6))) * 0.92;
        pts.push([w.root[0] + ux * L * u * (1 - 0.06 * k) + nx * off * bulge, w.root[1] + uy * L * u * (1 - 0.06 * k) + ny * off * bulge]);
      }
      lines.push(pts);
      o.lineWidth = (k === 0 ? 0.0032 : 0.0016) * (band === 'large' ? 1 : 1.4);
      o.beginPath(); pts.forEach((p, i) => (i ? o.lineTo(p[0], p[1]) : o.moveTo(p[0], p[1]))); o.stroke();
    }
    if (band === 'large') {
      o.lineWidth = 0.0009; o.strokeStyle = 'rgba(40,60,70,0.3)';
      const r = K.rng(w.id.charCodeAt(0) * 7 + w.id.charCodeAt(1));
      for (let k = 0; k < lines.length - 1; k++) for (let i = 1; i < 16; i += 1) {
        if (r() < 0.25) continue;
        const a = lines[k][i], b = lines[k + 1][Math.min(16, i + (r() < 0.5 ? 0 : 1))];
        o.beginPath(); o.moveTo(a[0], a[1]); o.lineTo(b[0], b[1]); o.stroke();
      }
    }
    // the pterostigma: a small dark cell near the tip on the leading edge
    o.fillStyle = 'rgba(30,40,50,0.85)';
    const px = w.root[0] + ux * L * 0.86, py = w.root[1] + uy * L * 0.86;
    o.save(); o.translate(px, py); o.rotate(Math.atan2(uy, ux)); o.fillRect(-0.018, back > 0 ? 0 : -0.011, 0.036, 0.011); o.restore();
    o.restore();
    o.lineWidth = 0.0024; o.strokeStyle = 'rgba(255,255,255,0.75)';
    o.beginPath(); wingPath(o, w); o.stroke();
    o.restore();
  }

  function body(o, size, band) {
    const tiny = band === 'tiny', small = band === 'small';
    o.save(); toWorld(o, size);
    // abdomen: slim, segmented, metallic sapphire fading to teal
    const segs = tiny ? 1 : 10, x0 = 0.135, x1 = -0.43;
    if (tiny || small) {
      o.strokeStyle = '#1f4f8a'; o.lineCap = 'round'; o.lineWidth = (tiny ? 2 : Math.max(2, size * 0.075)) / size;
      o.beginPath(); o.moveTo(x0, 0); o.lineTo(x1, 0); o.stroke();
      o.fillStyle = '#1b6b8a'; o.beginPath(); o.ellipse(0.22, 0, 0.1, 0.06, 0, 0, TAU); o.fill();
      o.restore(); return;
    }
    const tube = [];
    for (let i = 0; i <= 24; i++) { const u = i / 24; tube.push([K.lerp(x0, x1, u), K.lerp(0.024, 0.012, Math.pow(u, 0.8)) * (u > 0.94 ? 0.7 + 0.3 * (1 - u) / 0.06 : 1)]); }
    o.beginPath(); tube.forEach(([x, wd], i) => (i ? o.lineTo(x, -wd) : o.moveTo(x, -wd))); for (let i = tube.length - 1; i >= 0; i--) o.lineTo(tube[i][0], tube[i][1]); o.closePath();
    o.fillStyle = K.lin(o, 0, -0.024, 0, 0.024, [[0, '#9fe7ff'], [0.3, '#2a8fd6'], [0.7, '#123e7a'], [1, '#081a38']]);
    o.fill();
    o.strokeStyle = 'rgba(6,20,45,0.7)'; o.lineWidth = 0.0035;
    for (let i = 1; i < segs; i++) { const u = i / segs, xx = K.lerp(x0, x1, u), wd = K.lerp(0.024, 0.012, Math.pow(u, 0.8)); o.beginPath(); o.moveTo(xx, -wd); o.lineTo(xx, wd); o.stroke(); }
    // thorax: a glossy oval
    o.fillStyle = K.rad(o, 0.19, -0.02, 0.09, [[0, '#b9f3e6'], [0.35, '#2fa59a'], [0.8, '#0f4e5e'], [1, '#08263a']]);
    o.beginPath(); o.ellipse(0.195, 0, 0.07, 0.04, 0, 0, TAU); o.fill();
    // compound eyes: two large glossy domes
    for (const s of [-1, 1]) {
      o.fillStyle = K.rad(o, 0.282, s * 0.026 - 0.01, 0.045, [[0, '#d8fff4'], [0.3, '#39c6a4'], [0.75, '#0d5a5a'], [1, '#062c34']]);
      o.beginPath(); K.circle(o, 0.282, s * 0.026, 0.032); o.fill();
    }
    o.fillStyle = 'rgba(255,255,255,0.85)';
    for (const s of [-1, 1]) { o.beginPath(); K.circle(o, 0.276, s * 0.026 - 0.012, 0.007); o.fill(); }
    // a highlight running down the abdomen
    o.strokeStyle = 'rgba(220,250,255,0.55)'; o.lineWidth = 0.004; o.lineCap = 'round';
    o.beginPath(); o.moveTo(0.12, -0.012); o.lineTo(-0.38, -0.006); o.stroke();
    o.restore();
  }

  function plate(o, size, band, theme) {
    const { x, y, w, h } = K.box(size), rad = w * 0.235;
    if (band === 'medium' || band === 'large') {
      K.blur(o, size * 0.025, (q) => { q.fillStyle = theme === 'light' ? 'rgba(20,40,30,0.35)' : 'rgba(0,0,0,0.5)'; q.beginPath(); K.squircle(q, x + w * 0.02, y + h * 0.035, w * 0.96, h * 0.96, rad); q.fill(); });
    }
    o.fillStyle = K.lin(o, x, y, x + w * 0.4, y + h, [[0, '#a9c9b4'], [0.5, '#6f9a86'], [1, '#3f6a5c']]);
    o.beginPath(); K.squircle(o, x, y, w, h, rad); o.fill();
    if (band === 'tiny') return;
    o.save(); o.beginPath(); K.squircle(o, x, y, w, h, rad); o.clip();
    // mist and the light on the water
    o.fillStyle = K.rad(o, x + w * 0.72, y + h * 0.2, w * 0.6, [[0, 'rgba(255,250,225,0.45)'], [1, 'rgba(255,250,225,0)']]);
    o.fillRect(x, y, w, h);
    o.fillStyle = K.rad(o, x + w * 0.2, y + h * 0.9, w * 0.55, [[0, 'rgba(30,70,80,0.35)'], [1, 'rgba(30,70,80,0)']]);
    o.fillRect(x, y, w, h);
    o.lineWidth = Math.max(1, size * 0.008);
    o.strokeStyle = K.lin(o, 0, y, 0, y + h, [[0, 'rgba(255,255,255,0.55)'], [0.4, 'rgba(255,255,255,0.1)'], [1, 'rgba(255,255,255,0.15)']]);
    o.beginPath(); K.squircle(o, x + o.lineWidth / 2, y + o.lineWidth / 2, w - o.lineWidth, h - o.lineWidth, rad - o.lineWidth / 2); o.stroke();
    o.restore();
    if (theme === 'light' && band !== 'small') { o.lineWidth = Math.max(1, size * 0.003); o.strokeStyle = 'rgba(20,50,40,0.3)'; o.beginPath(); K.squircle(o, x, y, w, h, rad); o.stroke(); }
  }

  function shadow(o, size) {
    K.blur(o, size * 0.018, (q) => {
      q.save(); q.translate(size * 0.035, size * 0.075); toWorld(q, size); q.fillStyle = 'rgba(20,45,40,0.28)';
      for (const w of WINGS) { q.beginPath(); wingPath(q, w); q.fill(); }
      q.fillStyle = 'rgba(15,35,30,0.5)'; q.beginPath(); q.ellipse(-0.1, 0, 0.3, 0.025, 0, 0, TAU); q.fill(); q.beginPath(); q.ellipse(0.25, 0, 0.09, 0.06, 0, 0, TAU); q.fill();
      q.restore();
    });
  }

  // the wing root in world coordinates, for turning a wing layer about it
  function rootWorld(size, w) {
    const c = Math.cos(HEADING), s = Math.sin(HEADING), bx = (w.root[0] - 0.06) * size * 0.94, by = w.root[1] * size * 0.94;
    return [size * 0.48 + bx * c - by * s, size * 0.53 + bx * s + by * c];
  }

  MarrowIcons.register({
    letter: 'S',
    id: 'dragonfly',
    name: 'Dragonfly',
    meaning: 'Quick, precise and aware of everything around it: it sees the whole codebase at once and moves exactly where it means to.',
    story: 'A dragonfly hovering over a sage-green pond, its four wings clear glass with fine dark veins and a breath of iridescence, its body metallic sapphire and its great eyes teal. The wings are the same liquid glass the app is drawn in. Sage, sapphire, clear glass.',
    swatch: ['#6f9a86', '#2a8fd6', '#e8f8ff'],
    launch: { ms: 1100, note: 'The dragonfly darts in toward the viewer with its wings a blur, then the wingbeat slows and stills, the wings open flat and its shadow settles on the water.' },
    idle: { ms: 4800, note: 'It hovers: a slight rise and fall, its wings trembling by a degree, and a rainbow sheen sliding over the glass of the wings.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const px = Math.round(size * s0);
      const lay = (key, fn, th) => K.cache('S-dragonfly|' + key + '|' + size + '|' + (th || '') + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); fn(o); });
      const L = t.launch;
      const big = band === 'medium' || band === 'large';
      ctx.save();
      const aP = K.ease.sine(K.seg(L, 0, 0.3)), eP = K.ease.mac(K.seg(L, 0, 0.4));
      ctx.save(); ctx.globalAlpha = aP;
      if (eP < 1) { const k = K.lerp(0.96, 1, eP); ctx.translate(size / 2, size / 2); ctx.scale(k, k); ctx.translate(-size / 2, -size / 2); }
      ctx.drawImage(lay('plate', (o) => plate(o, size, band, theme), theme), 0, 0, size, size);
      ctx.restore();
      const { x, y, w, h } = K.box(size);
      ctx.beginPath(); K.squircle(ctx, x, y, w, h, w * 0.235); ctx.clip();
      // motion
      let k = 1, dx = 0, dy = 0, flap = 0, blur = 0, a = 1;
      const ph = L >= 1 && t.idle > 0 ? K.loop(t.idle, 4800) : null;
      if (L < 1) {
        const u = K.ease.mac(K.seg(L, 0.1, 0.72));
        a = K.ease.sine(K.seg(L, 0.1, 0.3));
        k = K.lerp(0.55, 1, u); dx = -(1 - u) * size * 0.22; dy = (1 - u) * size * 0.18;
        const f = K.seg(L, 0.1, 0.92), damp = Math.pow(1 - f, 2);
        flap = 0.42 * damp * Math.sin(TAU * 4.5 * f);
        blur = damp;
      } else if (ph != null) {
        dy = -size * 0.008 * Math.sin(TAU * ph);
        flap = 0.018 * Math.sin(TAU * 3 * ph);
      }
      const cx = size * 0.48, cy = size * 0.53;
      // shadow on the water: it does not rise with the dragonfly, it sharpens as it lands
      if (big) {
        ctx.save(); ctx.globalAlpha = a * K.lerp(0.4, 1, k * k) * (ph != null ? 1 + 0.12 * Math.sin(TAU * ph) : 1);
        ctx.translate(cx, cy); ctx.scale(k, k); ctx.translate(-cx, -cy);
        ctx.drawImage(lay('shadow', (o) => shadow(o, size)), 0, 0, size, size);
        ctx.restore();
      }
      ctx.save(); ctx.globalAlpha = a;
      ctx.translate(cx + dx, cy + dy); ctx.scale(k, k); ctx.translate(-cx, -cy);
      const ghosts = blur > 0.02 && band !== 'tiny' ? 3 : 1;
      for (const wg of WINGS) {
        const c = lay('wing-' + wg.id, (o) => wing(o, size, band, wg));
        const [rx, ry] = rootWorld(size, wg);
        for (let g = 0; g < ghosts; g++) {
          const spread = ghosts > 1 ? (g - 1) * 0.22 * blur : 0;
          const ang = (flap + spread) * -wg.side;
          ctx.save(); ctx.globalAlpha = a * (ghosts > 1 ? (g === 1 ? 1 - 0.4 * blur : 0.35 * blur) : 1);
          ctx.translate(rx, ry); ctx.rotate(ang); ctx.translate(-rx, -ry);
          ctx.drawImage(c, 0, 0, size, size);
          ctx.restore();
        }
      }
      ctx.drawImage(lay('body', (o) => body(o, size, band)), 0, 0, size, size);
      // idle: an iridescent sheen sliding across the wings
      if (ph != null && big) {
        const u = K.seg(ph, 0.15, 0.7), env = Math.sin(Math.PI * u);
        if (env > 0.002) {
          ctx.save(); toWorld(ctx, size);
          ctx.beginPath(); for (const wg of WINGS) wingPath(ctx, wg); ctx.clip();
          ctx.globalCompositeOperation = 'lighter';
          const sx = K.lerp(-0.5, 0.6, K.ease.inOut(u));
          ctx.fillStyle = K.lin(ctx, sx - 0.15, -0.4, sx + 0.15, 0.4, [[0, 'rgba(255,255,255,0)'], [0.4, 'rgba(180,240,255,' + (0.3 * env).toFixed(3) + ')'], [0.6, 'rgba(255,200,240,' + (0.3 * env).toFixed(3) + ')'], [1, 'rgba(255,255,255,0)']]);
          ctx.fillRect(-0.6, -0.6, 1.2, 1.2);
          ctx.restore();
        }
      }
      ctx.restore();
      ctx.restore();
    },
  });
})();
