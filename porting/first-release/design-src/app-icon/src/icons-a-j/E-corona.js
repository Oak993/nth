// E Corona: a total eclipse on a tile of black enamel: a black disc ringed by an icy corona and one diamond bead.
(function () {
  const K = IconKit, PI = Math.PI;
  const BEAD = -0.93;   // the diamond bead's angle (radians, about one o'clock)

  function rect(size) { const b = K.box(size), band = K.band(size); return { x: b.x, y: b.y, w: b.w, h: b.h, r: b.w * (band === 'tiny' || band === 'small' ? 0.24 : 0.225) }; }
  function geom(size) { const p = rect(size), band = K.band(size); return { p, cx: p.x + p.w / 2, cy: p.y + p.h / 2, R: p.w * (band === 'tiny' ? 0.27 : band === 'small' ? 0.26 : 0.235) }; }
  const RAYS = (() => { const r = K.rng(77), out = []; for (let i = 0; i < 150; i++) out.push({ a: r() * 2 * PI, l: 0.25 + Math.pow(r(), 2.2) * 1.1, w: 0.006 + r() * 0.03, k: 0.25 + r() * 0.75 }); return out; })();

  function plate(c, size, theme) {
    const p = rect(size), band = K.band(size);
    if (band === 'medium' || band === 'large') K.blur(c, size * 0.03, (o) => { o.fillStyle = theme === 'light' ? 'rgba(10,20,40,.34)' : 'rgba(0,0,0,.6)'; o.beginPath(); K.squircle(o, p.x + p.w * 0.03, p.y + p.h * 0.05, p.w * 0.94, p.h * 0.96, p.r); o.fill(); });
    c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r);
    c.fillStyle = K.rad(c, p.x + p.w * 0.5, p.y + p.h * 0.45, p.w * 0.75, [[0, '#141a26'], [0.6, '#0a0d14'], [1, '#040508']]);
    c.fill();
  }
  function rim(c, size, theme) {
    const p = rect(size), band = K.band(size);
    if (band === 'tiny' || band === 'small') {
      c.lineWidth = 1; c.strokeStyle = theme === 'light' ? 'rgba(0,0,0,.55)' : 'rgba(255,255,255,.2)';
      c.beginPath(); K.squircle(c, p.x + 0.5, p.y + 0.5, p.w - 1, p.h - 1, p.r); c.stroke(); return;
    }
    c.save(); c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
    const lw = size * 0.016;
    K.blur(c, size * 0.005, (o) => {
      o.lineWidth = lw; o.strokeStyle = K.lin(o, p.x, p.y, p.x + p.w, p.y + p.h, [[0, 'rgba(255,255,255,.42)'], [0.35, 'rgba(255,255,255,.06)'], [0.7, 'rgba(255,255,255,.03)'], [1, 'rgba(190,215,255,.22)']]);
      o.beginPath(); K.squircle(o, p.x + lw * 0.3, p.y + lw * 0.3, p.w - lw * 0.6, p.h - lw * 0.6, p.r); o.stroke();
    });
    // polished enamel: a faint reflected window across the upper left
    c.fillStyle = K.lin(c, p.x, p.y, p.x + p.w * 0.5, p.y + p.h * 0.5, [[0, 'rgba(255,255,255,.10)'], [1, 'rgba(255,255,255,0)']]);
    c.beginPath(); c.moveTo(p.x, p.y); c.lineTo(p.x + p.w * 0.75, p.y); c.bezierCurveTo(p.x + p.w * 0.5, p.y + p.h * 0.2, p.x + p.w * 0.25, p.y + p.h * 0.35, p.x, p.y + p.h * 0.62); c.closePath(); c.fill();
    c.restore();
    c.lineWidth = Math.max(0.75, size * 0.003); c.strokeStyle = theme === 'light' ? 'rgba(0,0,0,.45)' : 'rgba(255,255,255,.16)';
    c.beginPath(); K.squircle(c, p.x + c.lineWidth / 2, p.y + c.lineWidth / 2, p.w - c.lineWidth, p.h - c.lineWidth, p.r); c.stroke();
  }
  function corona(c, size, g, k, rot) {
    const { cx, cy, R } = g, band = K.band(size);
    if (k <= 0) return;
    c.save(); c.beginPath(); K.squircle(c, g.p.x, g.p.y, g.p.w, g.p.h, g.p.r); c.clip();
    c.globalCompositeOperation = 'lighter';
    if (band === 'tiny' || band === 'small') {
      c.globalAlpha = k;
      c.fillStyle = K.rad(c, cx, cy, R * 1.9, [[0.5, 'rgba(210,228,255,.95)'], [0.62, 'rgba(150,190,255,.45)'], [1, 'rgba(120,170,255,0)']]);
      c.beginPath(); K.circle(c, cx, cy, R * 1.9); c.fill();
      c.restore(); return;
    }
    // the smooth inner corona
    c.globalAlpha = k;
    c.fillStyle = K.rad(c, cx, cy, R * 2.1, [[0.47, 'rgba(200,218,255,.78)'], [0.52, 'rgba(160,192,255,.42)'], [0.68, 'rgba(120,160,255,.15)'], [1, 'rgba(110,150,255,0)']]);
    c.beginPath(); K.circle(c, cx, cy, R * 2.1); c.fill();
    // the streamers: fine wedges of light, longer and fainter outward
    const rays = (o, wk, ak) => {
      RAYS.forEach((r) => {
        const a = r.a + rot * (0.6 + 0.4 * r.k), L = R * (1 + r.l * (0.55 + 0.45 * k)), w = r.w * wk;
        const g = o.createRadialGradient(cx, cy, R * 0.98, cx, cy, L);
        g.addColorStop(0, 'rgba(225,236,255,' + (0.55 * r.k * ak).toFixed(3) + ')');
        g.addColorStop(0.35, 'rgba(170,200,255,' + (0.22 * r.k * ak).toFixed(3) + ')');
        g.addColorStop(1, 'rgba(140,180,255,0)');
        o.fillStyle = g;
        o.beginPath(); o.moveTo(cx + Math.cos(a - w) * R, cy + Math.sin(a - w) * R);
        o.lineTo(cx + Math.cos(a) * L, cy + Math.sin(a) * L);
        o.lineTo(cx + Math.cos(a + w) * R, cy + Math.sin(a + w) * R); o.closePath(); o.fill();
      });
    };
    K.blur(c, R * 0.06, (o) => rays(o, 2.2, 0.6 * k));
    rays(c, 1, 0.55 * k);
    c.restore();
  }
  function bead(c, size, g, k, pulse) {
    const { cx, cy, R } = g, band = K.band(size);
    if (k <= 0) return;
    const bx = cx + Math.cos(BEAD) * R * 1.02, by = cy + Math.sin(BEAD) * R * 1.02;
    c.save(); c.globalCompositeOperation = 'lighter'; c.globalAlpha = Math.min(1, k);
    if (band === 'tiny' || band === 'small') { c.restore(); return; }   // at 24 and 32 px a bead reads as a camera lens
    // only outside the disc: the bead is the sun showing past its edge
    c.beginPath(); c.rect(0, 0, size, size); K.circle(c, cx, cy, R); c.clip('evenodd');
    const s = (0.85 + 0.15 * pulse) * k;
    c.fillStyle = K.rad(c, bx, by, R * 0.6 * s, [[0, 'rgba(255,255,255,1)'], [0.2, 'rgba(250,252,255,.95)'], [0.45, 'rgba(200,222,255,.35)'], [1, 'rgba(150,190,255,0)']]);
    c.beginPath(); K.circle(c, bx, by, R * 0.6 * s); c.fill();
    // a short anamorphic streak through the bead
    c.fillStyle = K.lin(c, bx - R * 0.9 * s, by, bx + R * 0.9 * s, by, [[0, 'rgba(170,205,255,0)'], [0.5, 'rgba(225,238,255,.55)'], [1, 'rgba(170,205,255,0)']]);
    c.beginPath(); c.ellipse(bx, by, R * 0.9 * s, R * 0.022, 0, 0, 2 * PI); c.fill();
    c.restore();
  }
  function moon(c, size, g, dx, dy) {
    const { cx, cy, R } = g, band = K.band(size);
    const mx = cx + dx, my = cy + dy;
    c.beginPath(); K.circle(c, mx, my, R * 0.995);
    c.fillStyle = band === 'tiny' || band === 'small' ? '#05070b' : K.rad(c, mx - R * 0.3, my - R * 0.35, R * 1.2, [[0, '#10141c'], [1, '#020305']]);
    c.fill();
    if (band === 'medium' || band === 'large') { c.lineWidth = Math.max(0.6, size * 0.0025); c.strokeStyle = 'rgba(235,242,255,.75)'; c.beginPath(); K.circle(c, mx, my, R); c.stroke(); }
  }

  MarrowIcons.register({
    letter: 'E', id: 'corona', name: 'Corona',
    meaning: 'Total focus: everything else goes dark, and the work shines.',
    story: 'A total eclipse set in a tile of polished black enamel: a black disc ringed by an icy platinum corona, with one diamond bead of light at its edge. Rare, exact and a little awe-inspiring.',
    swatch: ['#0a0d14', '#dfeaff', '#8fb6ff'],
    launch: { ms: 1200, note: 'A bright sun sits on the black tile; the dark disc glides across it, the light narrows to a ring, the corona flares out and the diamond bead flashes, then settles.' },
    idle: { ms: 8000, note: 'The corona’s streamers turn a hair back and forth and the diamond bead breathes.' },
    draw(ctx, size, theme, t) {
      const g = geom(size), band = K.band(size), L = t.launch;
      const ph = K.loop(t.idle, 8000), rot = 0.05 * Math.sin(2 * PI * ph), pulse = Math.cos(2 * PI * ph * 2);
      ctx.save();
      const a = K.ease.out(K.seg(L, 0, 0.3));
      ctx.globalAlpha = a;
      if (band === 'medium' || band === 'large') K.layer(ctx, 'E-plate-' + theme, size, (o) => plate(o, size, theme)); else plate(ctx, size, theme);
      // the moon's path: from lower left to the centre
      const m = L >= 1 ? 1 : K.ease.mac(K.seg(L, 0.08, 0.72));
      const dx = -(1 - m) * g.R * 1.55, dy = (1 - m) * g.R * 0.75;
      // the sun, only while it is not yet covered
      if (L < 1) {
        const sk = 1 - K.ease.inOut(K.seg(L, 0.55, 0.78));
        ctx.save(); ctx.globalAlpha = a * sk; ctx.globalCompositeOperation = 'lighter';
        ctx.fillStyle = K.rad(ctx, g.cx, g.cy, g.R * 1.8, [[0, '#fffaf0'], [0.52, '#fff4dc'], [0.58, 'rgba(255,240,210,.5)'], [1, 'rgba(255,230,200,0)']]);
        ctx.beginPath(); K.circle(ctx, g.cx, g.cy, g.R * 1.8); ctx.fill();
        ctx.restore();
      }
      const ck = L >= 1 ? 1 : K.ease.out(K.seg(L, 0.6, 1));
      ctx.globalAlpha = 1;
      corona(ctx, size, g, ck, rot);
      ctx.save(); ctx.beginPath(); K.squircle(ctx, g.p.x, g.p.y, g.p.w, g.p.h, g.p.r); ctx.clip(); ctx.globalAlpha = a;
      moon(ctx, size, g, dx, dy);
      ctx.restore();
      // the bead flashes as the disc arrives, then settles to its still size
      const flash = L >= 1 ? 1 : K.seg(L, 0.66, 0.78) * (1 + 0.6 * (1 - K.ease.out(K.seg(L, 0.78, 1))));
      bead(ctx, size, g, flash, pulse);
      ctx.globalAlpha = a;
      rim(ctx, size, theme);
      ctx.restore();
    },
  });
})();
