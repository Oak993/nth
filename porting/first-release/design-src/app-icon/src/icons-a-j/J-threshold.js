// J Threshold: a wall of pale limestone with an arched doorway; through it, a dusk sky over still water, and its
// light spilling onto the stone floor.
(function () {
  const K = IconKit, PI = Math.PI;
  function rect(size) { const b = K.box(size), band = K.band(size); return { x: b.x, y: b.y, w: b.w, h: b.h, r: b.w * (band === 'tiny' || band === 'small' ? 0.24 : 0.225) }; }
  function arch(size) {
    const p = rect(size), band = K.band(size);
    const w = p.w * (band === 'tiny' ? 0.46 : band === 'small' ? 0.44 : 0.38), bot = p.y + p.h * (band === 'tiny' || band === 'small' ? 0.86 : 0.8);
    const top = p.y + p.h * (band === 'tiny' || band === 'small' ? 0.16 : 0.2);
    return { p, cx: p.x + p.w / 2, w, bot, top, rr: w / 2 };
  }
  function archPath(o, A, open) {
    const hw = (A.w / 2) * open, r = A.rr;
    // a round-headed opening; while it opens the head keeps its height and narrows
    const yS = A.top + r;
    o.moveTo(A.cx - hw, A.bot); o.lineTo(A.cx - hw, yS);
    o.ellipse(A.cx, yS, hw, r, 0, PI, 2 * PI);
    o.lineTo(A.cx + hw, A.bot); o.closePath();
  }

  function stone(c, size, theme) {
    const p = rect(size), band = K.band(size), A = arch(size);
    if (band === 'medium' || band === 'large') K.blur(c, size * 0.03, (o) => { o.fillStyle = theme === 'light' ? 'rgba(60,40,30,.3)' : 'rgba(0,0,0,.55)'; o.beginPath(); K.squircle(o, p.x + p.w * 0.03, p.y + p.h * 0.05, p.w * 0.94, p.h * 0.96, p.r); o.fill(); });
    c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r);
    c.fillStyle = K.lin(c, p.x, p.y, p.x, p.y + p.h, [[0, '#f6efe4'], [0.6, '#e6dccd'], [1, '#d3c5b1']]); c.fill();
    if (band === 'medium' || band === 'large') {
      c.save(); c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
      // the courses of stone and the voussoirs around the head of the arch
      c.strokeStyle = 'rgba(120,95,70,.16)'; c.lineWidth = Math.max(0.6, size * 0.0028);
      const yS = A.top + A.rr;
      for (let k = 1; k < 7; k++) {
        const y = p.y + p.h * (k / 7);
        if (y > yS && y < A.bot) { c.beginPath(); c.moveTo(p.x, y); c.lineTo(A.cx - A.w / 2 - p.w * 0.07, y); c.moveTo(A.cx + A.w / 2 + p.w * 0.07, y); c.lineTo(p.x + p.w, y); c.stroke(); }
        else if (y <= A.top - p.h * 0.08 || y >= A.bot) { c.beginPath(); c.moveTo(p.x, y); c.lineTo(p.x + p.w, y); c.stroke(); }
      }
      const R1 = A.rr + p.w * 0.075;
      c.beginPath(); c.ellipse(A.cx, yS, R1, R1, 0, PI, 2 * PI); c.stroke();
      for (let k = 1; k < 9; k++) { const a = PI + (k / 9) * PI; c.beginPath(); c.moveTo(A.cx + Math.cos(a) * A.rr, yS + Math.sin(a) * A.rr); c.lineTo(A.cx + Math.cos(a) * R1, yS + Math.sin(a) * R1); c.stroke(); }
      c.beginPath(); c.moveTo(A.cx - A.w / 2 - p.w * 0.075, yS); c.lineTo(A.cx - A.w / 2 - p.w * 0.075, A.bot); c.moveTo(A.cx + A.w / 2 + p.w * 0.075, yS); c.lineTo(A.cx + A.w / 2 + p.w * 0.075, A.bot); c.stroke();
      // the floor line
      c.strokeStyle = 'rgba(120,95,70,.22)'; c.beginPath(); c.moveTo(p.x, A.bot); c.lineTo(p.x + p.w, A.bot); c.stroke();
      if (band === 'large') { const r = K.rng(12); for (let i = 0; i < 900; i++) { c.fillStyle = r() < 0.5 ? 'rgba(120,95,70,.07)' : 'rgba(255,255,255,.3)'; c.fillRect(p.x + r() * p.w, p.y + r() * p.h, size * 0.004, size * 0.004); } }
      // daylight from the upper left on the wall
      c.fillStyle = K.rad(c, p.x + p.w * 0.2, p.y, p.w * 0.9, [[0, 'rgba(255,255,255,.4)'], [1, 'rgba(255,255,255,0)']]); c.fillRect(p.x, p.y, p.w, p.h);
      c.restore();
      const lw = Math.max(1, size * 0.008);
      c.save(); c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
      c.lineWidth = lw; c.strokeStyle = K.lin(c, p.x, p.y, p.x, p.y + p.h, [[0, 'rgba(255,255,255,.9)'], [0.5, 'rgba(255,255,255,.1)'], [1, 'rgba(110,80,55,.35)']]);
      c.beginPath(); K.squircle(c, p.x + lw / 2, p.y + lw / 2, p.w - lw, p.h - lw, p.r); c.stroke(); c.restore();
    }
    const lw2 = band === 'tiny' || band === 'small' ? 1 : Math.max(0.75, size * 0.003);
    c.lineWidth = lw2; c.strokeStyle = theme === 'light' ? 'rgba(70,50,35,.45)' : (band === 'tiny' || band === 'small' ? 'rgba(70,50,35,.5)' : 'rgba(255,245,230,.25)');
    c.beginPath(); K.squircle(c, p.x + lw2 / 2, p.y + lw2 / 2, p.w - lw2, p.h - lw2, p.r); c.stroke();
  }
  function sky(c, size, A, open, glow, drift) {
    const band = K.band(size), hz = A.top + (A.bot - A.top) * 0.66;
    c.save(); c.beginPath(); archPath(c, A, open); c.clip();
    c.fillStyle = K.lin(c, 0, A.top, 0, hz, [[0, '#2c2470'], [0.45, K.mix('#7a3d9c', '#8a3f98', 0.5 + 0.5 * drift)], [0.8, '#e0607e'], [1, '#ffa070']]);
    c.fillRect(A.cx - A.w, A.top - 1, A.w * 2, hz - A.top + 1);
    // the water: darker, with the sun's road of light
    c.fillStyle = K.lin(c, 0, hz, 0, A.bot, [[0, '#9a4a86'], [1, '#2e2266']]); c.fillRect(A.cx - A.w, hz, A.w * 2, A.bot - hz + 1);
    if (band !== 'tiny') {
      // the low sun on the horizon, half set
      c.save(); c.globalCompositeOperation = 'lighter';
      c.fillStyle = K.rad(c, A.cx, hz, A.w * 0.75, [[0, 'rgba(255,220,170,' + (0.95 * glow).toFixed(3) + ')'], [0.18, 'rgba(255,180,130,' + (0.6 * glow).toFixed(3) + ')'], [1, 'rgba(255,120,120,0)']]);
      c.fillRect(A.cx - A.w, A.top, A.w * 2, A.bot - A.top);
      c.restore();
      c.fillStyle = '#ffe3c0'; c.beginPath(); c.arc(A.cx, hz, A.w * 0.13, PI, 2 * PI); c.fill();
      if (band === 'medium' || band === 'large') {
        for (let i = 0; i < 6; i++) {
          const y = hz + (A.bot - hz) * (0.08 + i * 0.15), w = A.w * (0.12 + 0.05 * i) * (1 + 0.1 * Math.sin(2 * PI * drift * 0 + i));
          c.fillStyle = 'rgba(255,200,160,' + (0.55 - i * 0.07).toFixed(3) + ')';
          c.fillRect(A.cx - w / 2, y, w, Math.max(0.6, size * 0.004));
        }
      }
    }
    c.restore();
  }
  // the wall's thickness: the whole opening is first filled with the stone of its jamb, then the sky is seen through
  // a slightly smaller opening shifted right (we stand a little to the right of the door)
  function inner(A, open) { const d = K.band(A.size) === 'tiny' ? 0 : A.w * 0.085 * open; return { p: A.p, size: A.size, cx: A.cx + d / 2, w: A.w - d / Math.max(open, 0.001), top: A.top + d * 0.45, bot: A.bot, rr: (A.w - d) / 2 }; }
  function reveal(c, size, A, open) {
    if (K.band(size) === 'tiny') return;
    c.save(); c.beginPath(); archPath(c, A, open);
    c.fillStyle = K.lin(c, A.cx - A.w / 2, 0, A.cx - A.w / 2 + A.w * 0.1, 0, [[0, '#d2c3ad'], [1, '#a18d74']]);
    c.fill(); c.restore();
  }  function spill(c, size, A, k) {
    const band = K.band(size), p = A.p;
    if (k <= 0 || band === 'tiny') return;
    c.save(); c.beginPath(); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
    c.globalAlpha = k;
    const g = c.createLinearGradient(0, A.bot, 0, p.y + p.h);
    g.addColorStop(0, 'rgba(255,150,120,.5)'); g.addColorStop(0.7, 'rgba(255,170,130,.12)'); g.addColorStop(1, 'rgba(255,170,130,0)');
    c.fillStyle = g;
    c.beginPath(); c.moveTo(A.cx - A.w / 2, A.bot); c.lineTo(A.cx + A.w / 2, A.bot); c.lineTo(A.cx + A.w * 0.95, p.y + p.h); c.lineTo(A.cx - A.w * 0.55, p.y + p.h); c.closePath(); c.fill();
    // warm light on the jambs
    c.globalCompositeOperation = 'source-over';
    c.fillStyle = K.rad(c, A.cx, A.bot, A.w * 1.1, [[0, 'rgba(255,150,120,.28)'], [1, 'rgba(255,150,120,0)']]);
    c.fillRect(p.x, p.y, p.w, p.h);
    c.restore();
  }

  MarrowIcons.register({
    letter: 'J', id: 'threshold', name: 'Threshold',
    meaning: 'A doorway into your work: step through, and the hard part is already lit.',
    story: 'A wall of pale limestone with a round-headed doorway, its voussoirs drawn in fine joints. Through it, a dusk sky in violet and coral over still water, and its warm light spilling onto the stone floor. Architecture, calm and invitation.',
    swatch: ['#e6dccd', '#7a3d9c', '#ffa070'],
    launch: { ms: 1150, note: 'The limestone wall settles in; the doorway opens from a bright slit to its full width, the sun warms on the horizon and the light pours out across the floor.' },
    idle: { ms: 7000, note: 'The sky shifts by a shade and the low sun breathes on the water.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size), L = t.launch, A = arch(size);
      const ph = K.loop(t.idle, 7000), drift = Math.sin(2 * PI * ph), breathe = Math.cos(2 * PI * ph);
      ctx.save();
      const a = K.ease.out(K.seg(L, 0, 0.3));
      ctx.globalAlpha = a;
      if (band === 'medium' || band === 'large') K.layer(ctx, 'J-stone-' + theme, size, (o) => stone(o, size, theme)); else stone(ctx, size, theme);
      ctx.globalAlpha = 1;
      const open = L >= 1 ? 1 : 0.02 + 0.98 * K.ease.mac(K.seg(L, 0.2, 0.75));
      const glow = (L >= 1 ? 1 : K.ease.out(K.seg(L, 0.35, 0.9))) * (0.92 + 0.08 * breathe);
      ctx.globalAlpha = K.ease.out(K.seg(L, 0.15, 0.3));
      A.size = size;
      reveal(ctx, size, A, open);
      sky(ctx, size, inner(A, open), open, glow, 0.5 * drift);
      ctx.globalAlpha = 1;
      spill(ctx, size, A, (L >= 1 ? 1 : K.ease.out(K.seg(L, 0.45, 1))) * (0.9 + 0.1 * breathe));
      ctx.restore();
    },
  });
})();
