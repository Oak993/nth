// D Aurora: a tile of smoked night glass with an aurora curtain moving inside it.
(function () {
  const K = IconKit, PI = Math.PI;

  function rect(size) { const b = K.box(size), band = K.band(size); return { x: b.x, y: b.y, w: b.w, h: b.h, r: b.w * (band === 'tiny' || band === 'small' ? 0.24 : 0.225) }; }
  // the curtain's foot: a slow S across the tile; ph shifts it (seamless in whole turns)
  function foot(p, u, ph, sway) {
    return p.y + p.h * (0.6 - 0.12 * Math.sin(PI * 1.5 * u + 0.35) + 0.045 * Math.sin(PI * 3.6 * u + 1.1) + sway * 0.025 * Math.sin(2 * PI * u * 1.5 + ph));
  }
  const RAYS = (() => { const r = K.rng(42), out = []; for (let i = 0; i < 200; i++) { const q = r(); out.push({ u: r(), h: 0.14 + 0.34 * q * q + 0.06 * r(), a: 0.2 + 0.8 * r(), w: 0.35 + 0.8 * r(), k: r() }); } return out.sort((a, b) => a.u - b.u); })();
  const STARS = (() => { const r = K.rng(8), out = []; for (let i = 0; i < 46; i++) out.push([r(), r() * 0.62, 0.3 + r() * 0.7, r()]); return out; })();

  function night(c, size, theme) {
    const p = rect(size), band = K.band(size);
    if (band === 'medium' || band === 'large') K.blur(c, size * 0.03, (o) => { o.fillStyle = theme === 'light' ? 'rgba(10,30,50,.32)' : 'rgba(0,0,0,.6)'; o.beginPath(); K.squircle(o, p.x + p.w * 0.03, p.y + p.h * 0.05, p.w * 0.94, p.h * 0.96, p.r); o.fill(); });
    c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r);
    c.fillStyle = K.lin(c, p.x, p.y, p.x, p.y + p.h, [[0, '#040818'], [0.5, '#06122a'], [0.8, '#071a2c'], [1, '#08222c']]);
    c.fill();
  }
  function stars(c, size, k, tw) {
    const p = rect(size); if (K.band(size) !== 'large' && K.band(size) !== 'medium') return;
    STARS.forEach((s, i) => {
      const a = k * s[2] * (0.75 + 0.25 * Math.cos(2 * PI * (tw + s[3])) ) * (i % 3 === 0 ? 1 : 0.6);
      c.fillStyle = 'rgba(220,235,255,' + (a * 0.85).toFixed(3) + ')';
      const r = size * (0.0022 + 0.0022 * s[2]);
      c.beginPath(); K.circle(c, p.x + p.w * (0.06 + 0.88 * s[0]), p.y + p.h * (0.06 + s[1]), r); c.fill();
    });
  }
  function curtain(c, size, reveal, ph, sway, bright) {
    const p = rect(size), band = K.band(size);
    c.save(); c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
    if (band === 'tiny' || band === 'small') {
      // one bright band with a soft top
      c.lineCap = 'round';
      const path = (o, dy) => { o.beginPath(); for (let i = 0; i <= 16; i++) { const u = i / 16, x = p.x + p.w * u, y = foot(p, u, ph, sway) + dy; if (i) o.lineTo(x, y); else o.moveTo(x, y); } };
      c.globalAlpha = reveal;
      c.strokeStyle = 'rgba(80,255,190,.45)'; c.lineWidth = band === 'tiny' ? 4 : 6; path(c, -(band === 'tiny' ? 2 : 3)); c.stroke();
      c.strokeStyle = '#8affd0'; c.lineWidth = band === 'tiny' ? 2 : 2.5; path(c, 0); c.stroke();
      c.restore(); return;
    }
    // the curtain: thin vertical rays rising from the foot, green at the foot, teal, then a violet fringe on top
    const draw = (o, wk, ak) => {
      RAYS.forEach((r) => {
        const show = K.clamp((reveal * 1.25 - r.u * 0.25 - 0.0) * 1.6 - r.u * 0.6, 0, 1);  // unfurls left to right
        if (show <= 0) return;
        const u = r.u, x = p.x + p.w * (0.02 + 0.96 * u);
        const fy = foot(p, u, ph, sway);
        const flick = 0.8 + 0.2 * Math.sin(2 * PI * (r.k + ph / (2 * PI)));
        const len = p.h * r.h * (0.5 + 0.5 * show) * (0.9 + 0.1 * flick);
        const a = r.a * show * bright * flick * ak;
        const g = o.createLinearGradient(x, fy, x, fy - len);
        g.addColorStop(0, 'rgba(150,255,200,' + (a * 0.8).toFixed(3) + ')');
        g.addColorStop(0.06, 'rgba(60,245,150,' + (a * 0.7).toFixed(3) + ')');
        g.addColorStop(0.4, 'rgba(30,190,160,' + (a * 0.32).toFixed(3) + ')');
        g.addColorStop(0.78, 'rgba(170,80,230,' + (a * 0.16).toFixed(3) + ')');
        g.addColorStop(1, 'rgba(150,90,255,0)');
        o.fillStyle = g;
        const w = p.w * 0.012 * r.w * wk;
        o.fillRect(x - w / 2, fy - len, w, len);
        // the light also hangs a little below the curtain's foot, so the foot is a glow, not an edge
        const dl = p.h * (0.07 + 0.09 * r.k);
        const g2 = o.createLinearGradient(x, fy, x, fy + dl);
        g2.addColorStop(0, 'rgba(90,250,170,' + (a * 0.42).toFixed(3) + ')'); g2.addColorStop(1, 'rgba(60,200,160,0)');
        o.fillStyle = g2; o.fillRect(x - w / 2, fy, w, dl);
      });
    };
    c.globalCompositeOperation = 'lighter';
    K.blur(c, size * 0.02, (o) => draw(o, 3, 0.55));      // the glow
    draw(c, 1, 0.7);                                      // the rays
    // the bright foot of the curtain
    K.blur(c, size * 0.006, (o) => {
      o.lineCap = 'round'; o.lineWidth = size * 0.01; const fg = o.createLinearGradient(p.x, 0, p.x + p.w, 0); for (let i = 0; i <= 12; i++) fg.addColorStop(i / 12, 'rgba(170,255,215,' + ((0.02 + 0.22 * RAYS[i * 13].a * RAYS[i * 13 + 1].a) * bright).toFixed(3) + ')'); o.strokeStyle = fg;
      o.beginPath(); const n = 40, end = K.clamp(reveal * 1.15, 0, 1);
      for (let i = 0; i <= n * end; i++) { const u = i / n, x = p.x + p.w * (0.02 + 0.96 * u), y = foot(p, u, ph, sway); if (i) o.lineTo(x, y); else o.moveTo(x, y); }
      o.stroke();
    });
    c.globalCompositeOperation = 'source-over';
    // the ground: a dark ridge of hills at the bottom, catching a little green
    c.fillStyle = K.lin(c, p.x, p.y + p.h * 0.78, p.x, p.y + p.h, [[0, '#04121a'], [1, '#02080d']]);
    c.beginPath(); c.moveTo(p.x, p.y + p.h);
    for (let i = 0; i <= 30; i++) { const u = i / 30; c.lineTo(p.x + p.w * u, p.y + p.h * (0.8 + 0.06 * Math.sin(u * 6.1 + 1) + 0.025 * Math.sin(u * 15.3) + 0.012 * Math.sin(u * 37))); }
    c.lineTo(p.x + p.w, p.y + p.h); c.closePath(); c.fill();
    c.strokeStyle = 'rgba(90,255,190,' + (0.25 * bright * reveal).toFixed(3) + ')'; c.lineWidth = size * 0.003; c.stroke();
    c.restore();
  }
  function glassTop(c, size, theme) {
    const p = rect(size), band = K.band(size);
    if (band === 'tiny' || band === 'small') {
      c.lineWidth = 1; c.strokeStyle = theme === 'light' ? 'rgba(0,20,40,.5)' : 'rgba(255,255,255,.22)';
      c.beginPath(); K.squircle(c, p.x + 0.5, p.y + 0.5, p.w - 1, p.h - 1, p.r); c.stroke(); return;
    }
    c.save(); c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
    // liquid glass: a bright refracted rim, strongest at the top edge, and a broad soft reflection
    const lw = size * 0.018;
    K.blur(c, size * 0.006, (o) => {
      o.lineWidth = lw; o.strokeStyle = K.lin(o, p.x, p.y, p.x + p.w * 0.6, p.y + p.h, [[0, 'rgba(255,255,255,.55)'], [0.4, 'rgba(255,255,255,.08)'], [0.8, 'rgba(160,255,220,.05)'], [1, 'rgba(160,255,220,.3)']]);
      o.beginPath(); K.squircle(o, p.x + lw * 0.3, p.y + lw * 0.3, p.w - lw * 0.6, p.h - lw * 0.6, p.r); o.stroke();
    });
    c.fillStyle = K.lin(c, p.x, p.y, p.x + p.w * 0.4, p.y + p.h * 0.55, [[0, 'rgba(255,255,255,.16)'], [0.6, 'rgba(255,255,255,.04)'], [1, 'rgba(255,255,255,0)']]);
    c.beginPath(); c.moveTo(p.x, p.y); c.lineTo(p.x + p.w, p.y); c.lineTo(p.x + p.w, p.y + p.h * 0.12); c.bezierCurveTo(p.x + p.w * 0.6, p.y + p.h * 0.3, p.x + p.w * 0.3, p.y + p.h * 0.36, p.x, p.y + p.h * 0.56); c.closePath(); c.fill();
    c.restore();
    c.lineWidth = Math.max(0.75, size * 0.003); c.strokeStyle = theme === 'light' ? 'rgba(0,20,40,.4)' : 'rgba(255,255,255,.18)';
    c.beginPath(); K.squircle(c, p.x + c.lineWidth / 2, p.y + c.lineWidth / 2, p.w - c.lineWidth, p.h - c.lineWidth, p.r); c.stroke();
  }

  MarrowIcons.register({
    letter: 'D', id: 'aurora', name: 'Aurora',
    meaning: 'Quiet intelligence, always moving: it keeps working while the night is still.',
    story: 'A tile of smoked night glass holds an aurora curtain, emerald at its foot, teal above, with a violet fringe, over a dark ridge and a few stars. The night-shift companion: calm, alive and far bigger than its frame.',
    swatch: ['#081a2e', '#3cf0aa', '#9a5aff'],
    launch: { ms: 1200, note: 'The night tile fades up with its stars; the aurora unfurls from left to right, rising ray by ray, and brightens.' },
    idle: { ms: 7200, note: 'The curtain sways and its rays shimmer as a slow wave of light runs along it.' },
    draw(ctx, size, theme, t) {
      const L = t.launch, band = K.band(size);
      const ph = 2 * PI * K.loop(t.idle, 7200);
      const sway = Math.sin(ph) ;
      ctx.save();
      const a = K.ease.out(K.seg(L, 0, 0.35));
      ctx.globalAlpha = a;
      if (band === 'medium' || band === 'large') K.layer(ctx, 'D-night-' + theme, size, (o) => night(o, size, theme)); else night(ctx, size, theme);
      stars(ctx, size, K.ease.out(K.seg(L, 0.1, 0.5)), ph / (2 * PI));
      ctx.globalAlpha = 1;
      const reveal = L >= 1 ? 1 : K.ease.inOut(K.seg(L, 0.18, 0.92));
      const bright = L >= 1 ? 1 : 0.6 + 0.4 * K.ease.out(K.seg(L, 0.5, 1));
      curtain(ctx, size, reveal, ph, sway, bright);
      ctx.globalAlpha = a;
      glassTop(ctx, size, theme);
      ctx.restore();
    },
  });
})();
