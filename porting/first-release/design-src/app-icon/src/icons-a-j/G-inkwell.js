// G Inkwell: ultramarine ink blooming inside a cube of optical crystal.
(function () {
  const K = IconKit, PI = Math.PI;
  // the ink cloud: offsets from the cube centre in units of the edge, radius, colour, and when it forms (0..1 of the bloom)
  // the ink cloud, as a falling drop makes it: a thin stem, then a vortex ring that curls under itself, then wisps
  const INK = (() => {
    const r = K.rng(19), out = [];
    for (let i = 0; i < 9; i++) { const u = i / 8; out.push([0.015 * Math.sin(u * 7), -0.62 + 0.7 * u, 0.028 + 0.03 * u, i % 2 ? '#2a46ff' : '#1f37e8', u * 0.25]); }
    for (let i = 0; i < 18; i++) {
      const a = (i / 18) * 2 * PI, front = Math.sin(a) > 0;
      out.push([Math.cos(a) * 0.3, 0.12 + Math.sin(a) * 0.085, 0.07 + 0.03 * r(), front ? (i % 3 ? '#1426c4' : '#0c1a8a') : (i % 2 ? '#3d5cff' : '#2b3fe0'), 0.32 + 0.25 * Math.abs(Math.cos(a))]);
    }
    [[-0.27, 0.27, 0.11, '#5a3cf0'], [0.27, 0.27, 0.105, '#3550ff'], [-0.14, 0.33, 0.08, '#1b28a8'], [0.15, 0.34, 0.08, '#20249a']].forEach((b) => out.push([b[0], b[1], b[2], b[3], 0.62]));
    for (let i = 0; i < 10; i++) { const sgn = i % 2 ? 1 : -1, u = Math.floor(i / 2) / 4; out.push([sgn * (0.36 + 0.06 * u), 0.2 + 0.28 * u, 0.035 - 0.012 * u, sgn > 0 ? '#5b7cff' : '#7a55ff', 0.7 + 0.2 * u]); }
    return out;
  })();  function geom(size) {
    const band = K.band(size), b = K.box(size);
    const a = (band === 'tiny' || band === 'small' ? size * 0.5 : b.w * 0.47);
    return { cx: size / 2, cy: size / 2 + (band === 'tiny' || band === 'small' ? 0 : size * 0.01), a, s: a * 0.866 };
  }
  function hexPts(g) {
    const { cx, cy, a, s } = g;
    return { H0: [cx, cy - a], H1: [cx + s, cy - a / 2], H2: [cx + s, cy + a / 2], H3: [cx, cy + a], H4: [cx - s, cy + a / 2], H5: [cx - s, cy - a / 2], C: [cx, cy] };
  }
  function poly(o, pts) { o.moveTo(pts[0][0], pts[0][1]); for (let i = 1; i < pts.length; i++) o.lineTo(pts[i][0], pts[i][1]); o.closePath(); }
  // a slightly rounded hexagon: the cube's outline with chamfered corners
  function hexPath(o, g, k) {
    const P = hexPts(g), pts = [P.H0, P.H1, P.H2, P.H3, P.H4, P.H5], r = g.a * (k == null ? 0.08 : k);
    const n = pts.length;
    for (let i = 0; i < n; i++) {
      const p = pts[i], q = pts[(i + 1) % n], pr = pts[(i + n - 1) % n];
      const d1 = Math.hypot(p[0] - pr[0], p[1] - pr[1]), d2 = Math.hypot(q[0] - p[0], q[1] - p[1]);
      const a1 = [p[0] + (pr[0] - p[0]) * r / d1, p[1] + (pr[1] - p[1]) * r / d1], a2 = [p[0] + (q[0] - p[0]) * r / d2, p[1] + (q[1] - p[1]) * r / d2];
      if (i === 0) o.moveTo(a1[0], a1[1]); else o.lineTo(a1[0], a1[1]);
      o.quadraticCurveTo(p[0], p[1], a2[0], a2[1]);
    }
    o.closePath();
  }
  // the ink, drawn as smoke: a tapering stem, a vortex ring that curls under itself, and thin wisps below it
  function inkCloud(o, g, bloom, ph, offx, offy, sq) {
    const { cx, cy, a } = g;
    const X = (u) => cx + offx + a * u, Y = (v) => cy + offy + a * v * sq;
    const kS = K.ease.out(K.seg(bloom, 0, 0.35)), kR = K.ease.out(K.seg(bloom, 0.18, 0.75)), kW = K.ease.out(K.seg(bloom, 0.45, 1));
    const w1 = Math.sin(2 * PI * ph), w2 = Math.cos(2 * PI * ph);
    if (kS > 0) {
      // the stem, from the entry point down to the ring
      const top = -0.62, bot = top + (0.74) * kS;
      o.fillStyle = o.createLinearGradient(0, Y(top), 0, Y(bot));
      o.fillStyle.addColorStop(0, 'rgba(50,80,255,.55)'); o.fillStyle.addColorStop(1, 'rgba(26,44,220,.95)');
      o.beginPath(); o.moveTo(X(-0.012), Y(top));
      o.bezierCurveTo(X(-0.02 + 0.01 * w1), Y(top + 0.3 * kS), X(-0.05), Y(bot - 0.15 * kS), X(-0.075 * kS), Y(bot));
      o.lineTo(X(0.075 * kS), Y(bot));
      o.bezierCurveTo(X(0.05), Y(bot - 0.15 * kS), X(0.02 + 0.01 * w1), Y(top + 0.3 * kS), X(0.012), Y(top));
      o.closePath(); o.fill();
    }
    if (kR > 0) {
      const rx = 0.06 + 0.25 * kR, ry = 0.025 + 0.06 * kR, yc = 0.02 + 0.1 * kR, wr = 0.11 * kR;
      // a diffuse haze of ink around and under the ring, so it reads as a cloud in water, not a solid
      const hz = o.createRadialGradient(X(0), Y(yc + 0.12), 0, X(0), Y(yc + 0.12), a * 0.5 * kR);
      hz.addColorStop(0, 'rgba(36,58,230,.38)'); hz.addColorStop(0.6, 'rgba(70,60,230,.16)'); hz.addColorStop(1, 'rgba(70,60,230,0)');
      o.fillStyle = hz; o.beginPath(); o.ellipse(X(0), Y(yc + 0.12), a * 0.5 * kR, a * 0.36 * kR, 0, 0, 2 * PI); o.fill();
      // the ring's back half, lighter, then its body, then the denser front half
      o.lineCap = 'round';
      o.lineWidth = a * wr; o.strokeStyle = 'rgba(60,92,255,.75)';
      o.beginPath(); o.ellipse(X(0.01 * w2), Y(yc), a * rx, a * ry * (1 + 0.08 * w1), 0, PI, 2 * PI); o.stroke();
      o.strokeStyle = 'rgba(16,32,190,.92)';
      o.beginPath(); o.ellipse(X(0.01 * w2), Y(yc), a * rx, a * ry * (1 + 0.08 * w1), 0, 0, PI); o.stroke();
      o.lineWidth = a * wr * 0.45; o.strokeStyle = 'rgba(8,16,110,.85)';
      o.beginPath(); o.ellipse(X(0.01 * w2), Y(yc + 0.01), a * rx, a * ry * (1 + 0.08 * w1), 0, 0.15 * PI, 0.85 * PI); o.stroke();
      // the curl under the ring: two lobes rolling inward
      o.lineWidth = a * 0.07 * kR;
      [[-1, '#4a3ae8'], [1, '#2a48ff']].forEach(([sg, col]) => {
        o.strokeStyle = K.alpha(col, 0.8);
        o.beginPath(); o.moveTo(X(sg * rx), Y(yc + 0.02));
        o.bezierCurveTo(X(sg * (rx + 0.08 + 0.01 * w1)), Y(yc + 0.16 * kR), X(sg * (rx - 0.02)), Y(yc + 0.26 * kR), X(sg * (rx - 0.12 * kR)), Y(yc + 0.17 * kR));
        o.stroke();
      });
    }
    if (kW > 0) {
      // the wisps: thin threads that hang and curl from the lobes
      o.lineCap = 'round';
      [[-1, 0.0, '#7a5cff', 0.018], [1, 0.4, '#5d82ff', 0.016], [-1, 0.8, '#3d50ff', 0.012], [1, 1.3, '#8a6aff', 0.011]].forEach(([sg, p0, col, lw], i) => {
        const sway = 0.03 * Math.sin(2 * PI * ph + p0 * 4);
        const x0 = sg * (0.2 + 0.06 * (i >> 1)), y0 = 0.3;
        o.lineWidth = a * lw; o.strokeStyle = K.alpha(col, 0.8 * kW);
        o.beginPath(); o.moveTo(X(x0), Y(y0));
        o.bezierCurveTo(X(x0 + sg * 0.12 + sway), Y(y0 + 0.12 * kW), X(x0 - sg * 0.05 + sway), Y(y0 + 0.24 * kW), X(x0 + sg * (0.1 + 0.05 * (i & 1)) + sway), Y(y0 + 0.32 * kW));
        o.stroke();
      });
    }
  }
  function tendrils() {}
  function draw(ctx, size, theme, t) {
    const g = geom(size), band = K.band(size), L = t.launch, P = hexPts(g);
    const ph = K.loop(t.idle, 9000);
    ctx.save();
    const cubeIn = K.ease.mac(K.seg(L, 0, 0.45));
    const sc = 0.9 + 0.1 * cubeIn;
    ctx.translate(g.cx, g.cy); ctx.scale(sc, sc); ctx.translate(-g.cx, -g.cy);
    ctx.globalAlpha = K.ease.out(K.seg(L, 0, 0.3));
    if (band === 'medium' || band === 'large') {
      // the cube's shadow and the blue light it throws on the ground
      K.blur(ctx, g.a * 0.1, (o) => { o.fillStyle = theme === 'light' ? 'rgba(20,30,70,.3)' : 'rgba(0,0,0,.6)'; o.beginPath(); o.ellipse(g.cx, g.cy + g.a * 0.98, g.s * 0.95, g.a * 0.1, 0, 0, 2 * PI); o.fill(); });
    }
    // the crystal's body: barely tinted, so the ink and the desktop show through
    ctx.beginPath(); hexPath(ctx, g);
    ctx.fillStyle = theme === 'light' ? 'rgba(170,190,230,.16)' : 'rgba(190,210,255,.06)';
    ctx.fill();
    ctx.globalAlpha = 1;
    // the ink: a drop enters at the top (0.2..0.4) and blooms (0.35..1)
    const bloom = L >= 1 ? 1 : K.ease.out(K.seg(L, 0.32, 1));
    const drop = K.seg(L, 0.16, 0.36);
    if (band === 'tiny' || band === 'small') {
      ctx.save(); ctx.beginPath(); hexPath(ctx, g); ctx.clip();
      const k = bloom;
      ctx.fillStyle = K.rad(ctx, g.cx, g.cy + g.a * 0.05, g.a * 0.62 * (0.3 + 0.7 * k), [[0, '#2f4cff'], [0.7, '#1a2bd0'], [1, 'rgba(26,43,208,0)']]);
      ctx.beginPath(); K.circle(ctx, g.cx, g.cy + g.a * 0.05, g.a * 0.62); ctx.fill();
      ctx.restore();
    } else {
      // the faces see the ink from different angles: each face shifts it a little (refraction)
      const faces = [
        { pts: [P.H5, P.H0, P.H1, P.C], dx: 0, dy: -g.a * 0.06, sq: 0.9 },
        { pts: [P.H5, P.C, P.H3, P.H4], dx: g.a * 0.045, dy: 0, sq: 1 },
        { pts: [P.C, P.H1, P.H2, P.H3], dx: -g.a * 0.045, dy: 0, sq: 1 },
      ];
      faces.forEach((f) => {
        ctx.save(); ctx.beginPath(); hexPath(ctx, g); ctx.clip(); ctx.beginPath(); poly(ctx, f.pts); ctx.clip();
        if (drop > 0 && L < 0.5) {
          const dk = 1 - K.seg(L, 0.36, 0.5);
          ctx.fillStyle = 'rgba(40,70,255,' + (0.95 * dk).toFixed(3) + ')';
          ctx.beginPath(); K.circle(ctx, g.cx + f.dx, g.cy + g.a * (-1.2 + 0.58 * K.ease.inOut(drop)) + f.dy, g.a * 0.06); ctx.fill();
        }
        ctx.save(); ctx.globalAlpha = 0.6; K.blur(ctx, g.a * 0.06, (o) => inkCloud(o, g, bloom, ph, f.dx, f.dy, f.sq)); ctx.restore();
        K.blur(ctx, g.a * 0.014, (o) => inkCloud(o, g, bloom, ph, f.dx, f.dy, f.sq));
        ctx.restore();
      });
    }
    ctx.globalAlpha = K.ease.out(K.seg(L, 0, 0.35));
    // the faces: the top catches the most light, the right the least
    if (band !== 'tiny') {
      ctx.save(); ctx.beginPath(); hexPath(ctx, g); ctx.clip();
      ctx.beginPath(); poly(ctx, [P.H5, P.H0, P.H1, P.C]); ctx.fillStyle = K.lin(ctx, P.H0[0], P.H0[1], P.C[0], P.C[1], [[0, 'rgba(255,255,255,.2)'], [1, 'rgba(255,255,255,.06)']]); ctx.fill();
      ctx.beginPath(); poly(ctx, [P.H5, P.C, P.H3, P.H4]); ctx.fillStyle = K.lin(ctx, P.H5[0], P.H5[1], P.C[0], P.H3[1], [[0, 'rgba(255,255,255,.07)'], [1, 'rgba(255,255,255,0)']]); ctx.fill();
      ctx.beginPath(); poly(ctx, [P.C, P.H1, P.H2, P.H3]); ctx.fillStyle = K.lin(ctx, P.C[0], P.C[1], P.H2[0], P.H2[1], [[0, 'rgba(10,20,60,.05)'], [1, 'rgba(10,20,60,.18)']]); ctx.fill();
      ctx.restore();
    }
    // the edges: bright chamfers on the front, a refracted inner edge
    const lw = band === 'tiny' ? 1 : band === 'small' ? 1 : Math.max(1, g.a * 0.03);
    ctx.lineJoin = 'round';
    if (band === 'medium' || band === 'large') {
      K.blur(ctx, g.a * 0.01, (o) => {
        o.lineWidth = lw; o.strokeStyle = 'rgba(255,255,255,.85)';
        o.beginPath(); o.moveTo(P.H5[0], P.H5[1]); o.lineTo(P.C[0], P.C[1]); o.lineTo(P.H1[0], P.H1[1]); o.moveTo(P.C[0], P.C[1]); o.lineTo(P.H3[0], P.H3[1] - g.a * 0.06); o.stroke();
      });
      ctx.lineWidth = lw * 0.5; ctx.strokeStyle = 'rgba(255,255,255,.95)';
      ctx.beginPath(); ctx.moveTo(P.H5[0], P.H5[1]); ctx.lineTo(P.C[0], P.C[1]); ctx.lineTo(P.H1[0], P.H1[1]); ctx.stroke();
      // the outer rim: light along the top edges, shade along the bottom ones
      ctx.lineWidth = lw;
      ctx.strokeStyle = K.lin(ctx, g.cx, g.cy - g.a, g.cx, g.cy + g.a, [[0, 'rgba(255,255,255,.95)'], [0.5, 'rgba(220,230,255,.55)'], [1, 'rgba(150,170,230,.55)']]);
      ctx.beginPath(); hexPath(ctx, g); ctx.stroke();
      ctx.lineWidth = Math.max(0.6, size * 0.0025); ctx.strokeStyle = theme === 'light' ? 'rgba(20,30,80,.45)' : 'rgba(200,215,255,.3)';
      ctx.beginPath(); hexPath(ctx, { cx: g.cx, cy: g.cy, a: g.a * 1.012, s: g.s * 1.012 }); ctx.stroke();
      // glints at the front corner and the top
      K.glow(ctx, 'rgba(255,255,255,.9)', g.a * 0.08, (o) => { o.fillStyle = '#fff'; o.beginPath(); K.circle(o, P.C[0], P.C[1], g.a * 0.018); o.fill(); o.beginPath(); K.circle(o, P.H0[0], P.H0[1] + g.a * 0.05, g.a * 0.012); o.fill(); });
    } else {
      ctx.lineWidth = 1; ctx.strokeStyle = theme === 'light' ? 'rgba(20,30,90,.75)' : 'rgba(220,230,255,.85)';
      ctx.beginPath(); hexPath(ctx, { cx: g.cx, cy: g.cy, a: g.a - 0.6, s: g.s - 0.5 }, 0.1); ctx.stroke();
      if (band === 'small') { ctx.strokeStyle = 'rgba(255,255,255,.75)'; ctx.beginPath(); ctx.moveTo(P.H5[0] + 1, P.H5[1] + 0.5); ctx.lineTo(P.C[0], P.C[1]); ctx.lineTo(P.H1[0] - 1, P.H1[1] + 0.5); ctx.stroke(); }
    }
    ctx.restore();
  }

  MarrowIcons.register({
    letter: 'G', id: 'inkwell', name: 'Inkwell',
    meaning: 'Thought taking form: one prompt, and an idea blooms into shape.',
    story: 'A cube of optical crystal with ultramarine ink blooming inside it, deep navy at its heart and violet at the edges. Each face bends the cloud a little differently, the way real glass does. Ink is the writer’s material; crystal is the engineer’s.',
    swatch: ['#dfe6ff', '#2340ff', '#7f55ff'],
    launch: { ms: 1150, note: 'The clear cube grows into place; a drop of ink falls in through the top and blooms down and outward into its cloud.' },
    idle: { ms: 9000, note: 'The ink drifts and turns very slowly inside the crystal, never settling.' },
    draw,
  });
})();
