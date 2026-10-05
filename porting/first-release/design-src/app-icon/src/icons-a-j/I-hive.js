// I Hive: a walnut tile inlaid with seven cells of amber glass in fine brass frames, each lit with honey light.
(function () {
  const K = IconKit, PI = Math.PI;
  function rect(size) { const b = K.box(size), band = K.band(size); return { x: b.x, y: b.y, w: b.w, h: b.h, r: b.w * (band === 'tiny' || band === 'small' ? 0.24 : 0.225) }; }
  // the seven cells: the centre, then the ring clockwise from the top right
  function cells(size) {
    const p = rect(size), band = K.band(size);
    const rc = p.w * (band === 'tiny' ? 0.165 : band === 'small' ? 0.16 : 0.148), cx = p.x + p.w / 2, cy = p.y + p.h / 2;
    const out = [[cx, cy]];
    for (let i = 0; i < 6; i++) { const a = -PI / 3 + i * PI / 3; out.push([cx + Math.cos(a) * rc * Math.sqrt(3), cy + Math.sin(a) * rc * Math.sqrt(3)]); }
    return { rc, pts: out };
  }
  function hex(o, x, y, r) { for (let i = 0; i < 6; i++) { const a = -PI / 2 + i * PI / 3, X = x + Math.cos(a) * r, Y = y + Math.sin(a) * r; if (i) o.lineTo(X, Y); else o.moveTo(X, Y); } o.closePath(); }
  function roundHex(o, x, y, r, k) {
    const P = []; for (let i = 0; i < 6; i++) { const a = -PI / 2 + i * PI / 3; P.push([x + Math.cos(a) * r, y + Math.sin(a) * r]); }
    for (let i = 0; i < 6; i++) {
      const p = P[i], q = P[(i + 1) % 6], pr = P[(i + 5) % 6];
      const a1 = [p[0] + (pr[0] - p[0]) * k, p[1] + (pr[1] - p[1]) * k], a2 = [p[0] + (q[0] - p[0]) * k, p[1] + (q[1] - p[1]) * k];
      if (i) o.lineTo(a1[0], a1[1]); else o.moveTo(a1[0], a1[1]);
      o.quadraticCurveTo(p[0], p[1], a2[0], a2[1]);
    }
    o.closePath();
  }

  function walnut(c, size, theme) {
    const p = rect(size), band = K.band(size);
    if (band === 'medium' || band === 'large') K.blur(c, size * 0.03, (o) => { o.fillStyle = theme === 'light' ? 'rgba(40,20,5,.34)' : 'rgba(0,0,0,.6)'; o.beginPath(); K.squircle(o, p.x + p.w * 0.03, p.y + p.h * 0.05, p.w * 0.94, p.h * 0.96, p.r); o.fill(); });
    c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r);
    c.fillStyle = K.lin(c, p.x, p.y, p.x + p.w, p.y + p.h, [[0, '#4a2c1a'], [0.5, '#2f1b10'], [1, '#1c0f08']]); c.fill();
    if (band === 'medium' || band === 'large') {
      c.save(); c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
      // the grain: long wavy lines with a cathedral figure in the middle
      const r = K.rng(4), n = band === 'large' ? 70 : 26;
      for (let i = 0; i < n; i++) {
        const y0 = p.y + p.h * ((i + 0.5 * r()) / n), amp = p.h * (0.012 + 0.006 * r()), f = 1.6, ph = 1 + i * 0.05;
        c.strokeStyle = r() < 0.5 ? 'rgba(20,8,2,' + (0.2 + 0.25 * r()).toFixed(3) + ')' : 'rgba(150,95,55,' + (0.08 + 0.12 * r()).toFixed(3) + ')';
        c.lineWidth = size * (0.0015 + 0.004 * r());
        c.beginPath();
        for (let k = 0; k <= 24; k++) { const u = k / 24, x = p.x + p.w * u, y = y0 + amp * Math.sin(u * PI * f + ph) + p.h * 0.05 * Math.exp(-Math.pow((u - 0.45) * 2.6, 2)) * (i / n - 0.5); if (k) c.lineTo(x, y); else c.moveTo(x, y); }
        c.stroke();
      }
      // an oiled sheen
      c.fillStyle = K.lin(c, p.x, p.y, p.x + p.w * 0.6, p.y + p.h * 0.6, [[0, 'rgba(255,220,180,.16)'], [1, 'rgba(255,220,180,0)']]); c.fillRect(p.x, p.y, p.w, p.h);
      c.restore();
      const lw = Math.max(1, size * 0.008);
      c.save(); c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
      c.lineWidth = lw; c.strokeStyle = K.lin(c, p.x, p.y, p.x, p.y + p.h, [[0, 'rgba(255,220,180,.35)'], [0.5, 'rgba(255,220,180,.05)'], [1, 'rgba(0,0,0,.3)']]);
      c.beginPath(); K.squircle(c, p.x + lw / 2, p.y + lw / 2, p.w - lw, p.h - lw, p.r); c.stroke(); c.restore();
    }
    const lw2 = band === 'tiny' || band === 'small' ? 1 : Math.max(0.75, size * 0.003);
    c.lineWidth = lw2; c.strokeStyle = theme === 'light' ? 'rgba(30,15,5,.5)' : 'rgba(255,230,200,.18)';
    c.beginPath(); K.squircle(c, p.x + lw2 / 2, p.y + lw2 / 2, p.w - lw2, p.h - lw2, p.r); c.stroke();
  }
  // one cell: the recess, the amber glass and its light, the brass frame
  function cell(c, size, x, y, rc, frame, lit) {
    const band = K.band(size), r = rc * 0.985;
    if (frame <= 0) return;
    c.save(); c.globalAlpha *= K.ease.out(K.seg(frame, 0, 0.5));
    const s = 0.7 + 0.3 * K.ease.mac(frame);
    c.translate(x, y); c.scale(s, s); c.translate(-x, -y);
    const inner = r * (band === 'tiny' ? 0.78 : band === 'small' ? 0.8 : 0.86);
    // the glass: smoky when unlit, honey gold when lit
    c.beginPath(); roundHex(c, x, y, inner, 0.12);
    c.fillStyle = K.rad(c, x - inner * 0.15, y - inner * 0.2, inner * 1.15, [[0, K.mix('#5a3412', '#fff4c4', lit)], [0.3, K.mix('#3f220b', '#ffb627', lit)], [0.75, K.mix('#24130a', '#c25e00', lit)], [1, K.mix('#160a04', '#5e2400', lit)]]);
    c.fill();
    if (band === 'medium' || band === 'large') {
      c.save(); c.beginPath(); roundHex(c, x, y, inner, 0.12); c.clip();
      // the honey's meniscus and a glint on the glass
      c.fillStyle = 'rgba(255,255,255,' + (0.1 + 0.18 * lit).toFixed(3) + ')';
      c.beginPath(); c.ellipse(x - inner * 0.25, y - inner * 0.48, inner * 0.45, inner * 0.16, -0.35, 0, 2 * PI); c.fill();
      c.strokeStyle = 'rgba(120,50,0,' + (0.35 * lit).toFixed(3) + ')'; c.lineWidth = inner * 0.06;
      c.beginPath(); roundHex(c, x, y, inner * 1.02, 0.12); c.stroke();
      c.restore();
    }
    // the brass frame
    const fw = r - inner + (band === 'tiny' ? 0.4 : 0);
    c.lineWidth = Math.max(band === 'tiny' ? 1 : 1, fw);
    c.lineJoin = 'round';
    c.strokeStyle = K.lin(c, x - r, y - r, x + r, y + r, [[0, '#ffe7a8'], [0.45, '#c8923a'], [1, '#6e4614']]);
    c.beginPath(); roundHex(c, x, y, (r + inner) / 2, 0.12); c.stroke();
    c.restore();
  }

  MarrowIcons.register({
    letter: 'I', id: 'hive', name: 'Hive',
    meaning: 'Many agents, one result: each does its part, and the whole fills with light.',
    story: 'A tile of oiled walnut inlaid with seven cells of amber glass in fine brass frames, like a honeycomb made by a jeweller. Each cell holds honey light; the warm, crafted one of the set.',
    swatch: ['#2f1b10', '#ffc23a', '#c8923a'],
    launch: { ms: 1150, note: 'The brass frames rise out of the walnut from the centre outward; then the cells light one by one, centre first, as if each agent finished its task.' },
    idle: { ms: 6000, note: 'A slow wave of warmth runs around the ring of cells and fades, like work passing from hand to hand.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size), L = t.launch, C = cells(size);
      const ph = K.loop(t.idle, 6000), env = Math.pow(Math.sin(PI * ph), 2);
      ctx.save();
      const a = K.ease.out(K.seg(L, 0, 0.3));
      ctx.globalAlpha = a;
      if (band === 'medium' || band === 'large') K.layer(ctx, 'I-walnut-' + theme, size, (o) => walnut(o, size, theme)); else walnut(ctx, size, theme);
      ctx.globalAlpha = 1;
      C.pts.forEach((pt, i) => {
        const d = i === 0 ? 0 : 1;
        const frame = L >= 1 ? 1 : K.seg(L, 0.06 + d * 0.12 + i * 0.015, 0.45 + d * 0.12 + i * 0.015);
        const lit0 = L >= 1 ? 1 : K.ease.inOut(K.seg(L, 0.42 + i * 0.07, 0.62 + i * 0.07));
        const wave = i === 0 ? 0 : 0.22 * env * Math.cos(2 * PI * (ph * 2 - i / 6));
        cell(ctx, size, pt[0], pt[1], C.rc, frame, K.clamp(lit0 * (0.94 + (i === 0 ? 0.06 : 0)) + wave * lit0, 0, 1));
      });
      // the honey light on the walnut around the cluster
      if (band === 'medium' || band === 'large') {
        const k = L >= 1 ? 1 : K.ease.out(K.seg(L, 0.5, 1));
        ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.globalAlpha = 0.32 * k;
        const p = rect(size);
        ctx.beginPath(); K.squircle(ctx, p.x, p.y, p.w, p.h, p.r); ctx.clip();
        ctx.fillStyle = K.rad(ctx, p.x + p.w / 2, p.y + p.h / 2, p.w * 0.5, [[0.4, 'rgba(255,170,60,.5)'], [1, 'rgba(255,140,40,0)']]);
        ctx.fillRect(p.x, p.y, p.w, p.h);
        ctx.restore();
      }
      ctx.restore();
    },
  });
})();
