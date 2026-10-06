// C Marrow: a medallion of ivory porcelain pierced like the lattice inside a bone, lit from within by an ember.
(function () {
  const K = IconKit, PI = Math.PI;

  // ------------------------------------------------------------------ the lattice (computed once, unit disc)
  function voronoi(sites, bound) {
    // each cell: the bounding polygon clipped by the half-planes nearer to its site than to every other site
    return sites.map((s, i) => {
      let poly = bound.slice();
      sites.forEach((o, j) => {
        if (i === j) return;
        const mx = (s[0] + o[0]) / 2, my = (s[1] + o[1]) / 2, nx = o[0] - s[0], ny = o[1] - s[1];
        const inside = (p) => (p[0] - mx) * nx + (p[1] - my) * ny <= 0;
        const out = [];
        for (let k = 0; k < poly.length; k++) {
          const a = poly[k], b = poly[(k + 1) % poly.length], ia = inside(a), ib = inside(b);
          if (ia) out.push(a);
          if (ia !== ib) {
            const da = (a[0] - mx) * nx + (a[1] - my) * ny, db = (b[0] - mx) * nx + (b[1] - my) * ny, u = da / (da - db);
            out.push([a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u]);
          }
        }
        poly = out;
      });
      return poly;
    });
  }
  function lattice(kind) {
    const rnd = K.rng(kind === 'large' ? 5 : kind === 'medium' ? 21 : 9);
    const sites = [[0.02, -0.03]];
    const rings = kind === 'small' ? [[0.52, 7]] : kind === 'medium' ? [[0.3, 6], [0.58, 12]] : [[0.24, 6], [0.45, 11], [0.66, 16]];
    rings.forEach(([r, n], ri) => {
      const off = rnd() * PI;
      for (let i = 0; i < n; i++) {
        const jx = kind === 'small' ? 0.7 : 0.35, jr = kind === 'small' ? 0.22 : 0.07 * (ri + 1) / rings.length;
        const a = off + (i / n) * 2 * PI + (rnd() - 0.5) * jx, rr = r + (rnd() - 0.5) * jr;
        sites.push([Math.cos(a) * rr, Math.sin(a) * rr]);
      }
    });
    const lim = kind === 'small' ? 0.78 : 0.8, bound = [];
    for (let i = 0; i < 64; i++) { const a = (i / 64) * 2 * PI; bound.push([Math.cos(a) * lim, Math.sin(a) * lim]); }
    const cells = voronoi(sites, bound);
    const wall = kind === 'small' ? 0.075 : kind === 'medium' ? 0.052 : 0.042;
    return cells.map((poly, i) => {
      let cx = 0, cy = 0; poly.forEach((p) => { cx += p[0]; cy += p[1]; }); cx /= poly.length; cy /= poly.length;
      let rm = 0; poly.forEach((p) => { rm += Math.hypot(p[0] - cx, p[1] - cy); }); rm /= poly.length;
      const f = Math.max(0.2, 1 - wall / rm);
      return { pts: poly.map((p) => [cx + (p[0] - cx) * f, cy + (p[1] - cy) * f]), d: Math.hypot(sites[i][0], sites[i][1]), c: [cx, cy] };
    });
  }
  const LAT = {};
  function lat(band) { const k = band === 'large' ? 'large' : band === 'medium' ? 'medium' : 'small'; return LAT[k] || (LAT[k] = lattice(k)); }
  // a rounded blob through the polygon's edge midpoints
  function blob(o, pts, cx, cy, R) {
    const n = pts.length, P = (i) => pts[(i + n) % n], M = (a, b) => [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
    const m0 = M(P(0), P(1));
    o.moveTo(cx + m0[0] * R, cy + m0[1] * R);
    for (let i = 1; i <= n; i++) { const c = P(i), m = M(c, P(i + 1)); o.quadraticCurveTo(cx + c[0] * R, cy + c[1] * R, cx + m[0] * R, cy + m[1] * R); }
    o.closePath();
  }

  function geom(size) {
    const band = K.band(size), b = K.box(size);
    return { cx: size / 2, cy: size / 2 - (band === 'large' || band === 'medium' ? size * 0.01 : 0), R: band === 'tiny' || band === 'small' ? b.w / 2 : b.w * 0.47 };
  }

  // the porcelain, holes cut out, shaded; cached
  function porcelain(o, size, g, theme) {
    const { cx, cy, R } = g, band = K.band(size), cells = lat(band);
    o.save();
    o.beginPath(); K.circle(o, cx, cy, R);
    cells.forEach((c) => blob(o, c.pts, cx, cy, R));
    o.fillStyle = K.rad(o, cx - R * 0.35, cy - R * 0.45, R * 1.7, [[0, '#fffdf8'], [0.45, '#f3eadb'], [0.85, '#dccbb2'], [1, '#c9b394']]);
    o.fill('evenodd');
    if (band === 'medium' || band === 'large') {
      // the thickness of each pore: a shadow on its upper wall, a lit lip on its lower edge, as if carved
      o.save(); o.beginPath(); K.circle(o, cx, cy, R); cells.forEach((c) => blob(o, c.pts, cx, cy, R)); o.clip('evenodd');
      K.blur(o, R * 0.012, (x) => {
        x.lineWidth = R * 0.02; x.strokeStyle = 'rgba(255,255,255,.95)';
        cells.forEach((c) => { x.save(); x.translate(0, R * 0.012); x.beginPath(); blob(x, c.pts, cx, cy, R); x.stroke(); x.restore(); });
        x.strokeStyle = 'rgba(120,70,40,.32)';
        cells.forEach((c) => { x.save(); x.translate(0, -R * 0.01); x.beginPath(); blob(x, c.pts, cx, cy, R); x.stroke(); x.restore(); });
      });
      o.restore();
      // the rim: a glazed bevel, bright on top, warm in the shadow
      o.save(); o.beginPath(); K.circle(o, cx, cy, R); o.clip();
      o.lineWidth = R * 0.05;
      o.strokeStyle = K.lin(o, cx, cy - R, cx, cy + R, [[0, 'rgba(255,255,255,.95)'], [0.5, 'rgba(255,255,255,.1)'], [1, 'rgba(150,100,60,.4)']]);
      o.beginPath(); K.circle(o, cx, cy, R - R * 0.025); o.stroke();
      // a glaze sheen
      o.fillStyle = K.rad(o, cx - R * 0.4, cy - R * 0.55, R * 0.7, [[0, 'rgba(255,255,255,.5)'], [1, 'rgba(255,255,255,0)']]);
      o.beginPath(); K.circle(o, cx, cy, R); cells.forEach((c) => blob(o, c.pts, cx, cy, R)); o.fill('evenodd');
      o.restore();
      if (band === 'large') {
        // bisque grain
        const rnd = K.rng(3); o.save(); o.beginPath(); K.circle(o, cx, cy, R); cells.forEach((c) => blob(o, c.pts, cx, cy, R)); o.clip('evenodd');
        for (let i = 0; i < 700; i++) { const a = rnd() * 2 * PI, r = Math.sqrt(rnd()) * R; o.fillStyle = rnd() < 0.5 ? 'rgba(120,90,60,.06)' : 'rgba(255,255,255,.18)'; o.fillRect(cx + Math.cos(a) * r, cy + Math.sin(a) * r, R * 0.006, R * 0.006); }
        o.restore();
      }
    }
    o.lineWidth = band === 'tiny' || band === 'small' ? 1 : Math.max(0.75, size * 0.003);
    o.strokeStyle = theme === 'light' ? 'rgba(90,55,30,.45)' : (band === 'tiny' || band === 'small' ? 'rgba(90,55,30,.5)' : 'rgba(255,240,220,.3)');
    o.beginPath(); K.circle(o, cx, cy, R - o.lineWidth / 2); o.stroke();
    o.restore();
  }
  // the ember behind the porcelain; heat 0..1, (hx, hy) the hot spot's drift
  function ember(c, size, g, heat, L, hx, hy) {
    const { cx, cy, R } = g, band = K.band(size), cells = lat(band);
    c.save(); c.beginPath(); K.circle(c, cx, cy, R * 0.97); c.clip();
    c.fillStyle = '#2a0408'; c.fillRect(cx - R, cy - R, 2 * R, 2 * R);
    const gx = cx + hx * R, gy = cy + hy * R;
    const grad = K.rad(c, gx, gy, R * 0.95, [[0, '#fff3c4'], [0.14, '#ffc35a'], [0.36, '#ff6a2a'], [0.62, '#d4142c'], [1, '#4a050f']]);
    cells.forEach((cl) => {
      // each pore lights in turn, from the centre outward
      const a = L >= 1 ? 1 : K.ease.out(K.seg(L, 0.16 + cl.d * 0.55, 0.42 + cl.d * 0.55));
      if (a <= 0) return;
      c.globalAlpha = a * (0.78 + 0.22 * heat);
      c.fillStyle = grad;
      c.beginPath(); blob(c, cl.pts, cx, cy, R * 1.005); c.fill();
    });
    c.restore();
  }

  MarrowIcons.register({
    letter: 'C', id: 'marrow', name: 'Marrow',
    meaning: 'The living core: warmth and intelligence at the heart of your work.',
    story: 'A medallion of ivory bisque porcelain, pierced like the lattice inside a bone, glows from within with an ember, gold at the heart and crimson toward the rim. The app’s name made literal: the marrow, the living part inside.',
    swatch: ['#f3eadb', '#ff6a2a', '#d4142c'],
    launch: { ms: 1150, note: 'The porcelain turns a few degrees into place; the ember catches at the centre and spreads pore by pore to the rim.' },
    idle: { ms: 5200, note: 'The ember breathes slowly, like a resting pulse, and its hot spot drifts a little within the lattice.' },
    draw(ctx, size, theme, t) {
      const g = geom(size), band = K.band(size), L = t.launch;
      const ph = K.loop(t.idle, 5200);
      const heat = Math.cos(2 * PI * ph);                     // 1 at the still frame
      const hx = 0.03 * Math.sin(2 * PI * ph), hy = -0.04 + 0.025 * (1 - Math.cos(2 * PI * ph));
      ctx.save();
      const rIn = K.ease.mac(K.seg(L, 0, 0.55));
      const rot = (1 - rIn) * -0.22, sc = 0.9 + 0.1 * rIn;
      ctx.globalAlpha = K.ease.out(K.seg(L, 0, 0.3));
      ctx.translate(g.cx, g.cy); ctx.rotate(rot); ctx.scale(sc, sc); ctx.translate(-g.cx, -g.cy);
      if (band === 'medium' || band === 'large') K.layer(ctx, 'C-shadow-' + theme, size, (o) => K.blur(o, g.R * 0.07, (x) => { x.fillStyle = theme === 'light' ? 'rgba(70,40,20,.32)' : 'rgba(0,0,0,.6)'; x.beginPath(); K.circle(x, g.cx, g.cy + g.R * 0.06, g.R * 0.98); x.fill(); }));
      ember(ctx, size, g, band === 'tiny' ? 1 : heat, L, hx, hy);
      if (band === 'medium' || band === 'large') K.layer(ctx, 'C-porc-' + theme, size, (o) => porcelain(o, size, g, theme)); else porcelain(ctx, size, g, theme);
      // the bloom: the ember's light spilling over the porcelain at the centre
      if (band === 'medium' || band === 'large') {
        const k = (L >= 1 ? 1 : K.ease.out(K.seg(L, 0.25, 1))) * (0.85 + 0.15 * heat);
        ctx.globalAlpha = k * 0.55;
        ctx.globalCompositeOperation = 'lighter';
        K.layer(ctx, 'C-bloom', size, (o) => {
          K.blur(o, g.R * 0.12, (x) => { x.fillStyle = K.rad(x, g.cx, g.cy - g.R * 0.04, g.R * 0.5, [[0, 'rgba(255,170,80,.75)'], [0.5, 'rgba(255,90,40,.3)'], [1, 'rgba(255,60,40,0)']]); x.beginPath(); K.circle(x, g.cx, g.cy, g.R * 0.6); x.fill(); });
        });
        ctx.globalCompositeOperation = 'source-over';
      }
      ctx.restore();
    },
  });
})();
