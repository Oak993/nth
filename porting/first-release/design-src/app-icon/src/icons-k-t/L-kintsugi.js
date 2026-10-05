// L Kintsugi: a black raku tea bowl mended with a seam of gold that branches, rejoins and runs over the rim.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;
  const FY = 0.4; // the rim, seen from a little above, is an ellipse this flat

  // the seam inside the bowl, in units of the rim radius (centre 0,0, before the rim is flattened):
  // the main crack, a split that rejoins (the island), two short spurs
  const A = [-0.86, -0.42], P1 = [-0.6, -0.36], P2 = [-0.44, -0.18], S1 = [-0.2, -0.12];
  const UP = [[-0.05, -0.24], [0.14, -0.2]], LO = [[-0.03, -0.04], [0.16, 0.0]], J1 = [0.33, -0.05];
  const M2 = [[0.46, 0.2], [0.56, 0.5]], RIM = [0.7, 0.775];
  const SP1 = [P2, [-0.5, 0.08], [-0.42, 0.26]];
  const SP2 = [M2[1], [0.36, 0.58], [0.25, 0.62]];
  // the seam outside, in units of the rim radius from the rim centre (not flattened): over the lip and down the wall
  const EXT = [[0.66, 0.73 * FY], [0.6, 0.5], [0.56, 0.68], [0.47, 0.86], [0.42, 1.0]];

  function crack(pts, seed, amp) {
    const r = K.rng(seed), out = [pts[0]];
    for (let i = 1; i < pts.length; i++) {
      const a = pts[i - 1], b = pts[i], dx = b[0] - a[0], dy = b[1] - a[1], L = Math.hypot(dx, dy), nx = -dy / L, ny = dx / L;
      for (const f of [0.34, 0.67]) { const j = (r() - 0.5) * 2 * amp * L, g = (r() - 0.5) * 0.12; out.push([a[0] + dx * (f + g) + nx * j, a[1] + dy * (f + g) + ny * j]); }
      out.push(b);
    }
    return out;
  }
  const ROUGH = {
    head: crack([A, P1, P2, S1], 11, 0.1), up: crack([S1, UP[0], UP[1], J1], 23, 0.11), lo: crack([S1, LO[0], LO[1], J1], 37, 0.11),
    tail: crack([J1, M2[0], M2[1], RIM], 41, 0.09), sp1: crack(SP1, 53, 0.12), sp2: crack(SP2, 67, 0.12), ext: crack(EXT, 71, 0.1),
  };
  const SMOOTH = { head: [A, P1, P2, S1], up: [S1, UP[0], UP[1], J1], lo: [S1, LO[0], LO[1], J1], tail: [J1, M2[0], M2[1], RIM], sp1: SP1, sp2: SP2, ext: EXT };

  function lengths(pts, fy) { const d = [0]; for (let i = 1; i < pts.length; i++) d.push(d[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], (pts[i][1] - pts[i - 1][1]) * (fy || 1))); return d; }
  // flow: molten gold enters at the far rim and runs along every branch at once, then over the lip and down
  function flowOf(set, rough) {
    const f = {};
    const head = lengths(set.head, FY); f.head = { pts: set.head, d: head, d0: 0 };
    const h = head[head.length - 1];
    const up = lengths(set.up, FY), lo = lengths(set.lo, FY);
    f.up = { pts: set.up, d: up, d0: h }; f.lo = { pts: set.lo, d: lo, d0: h };
    const j = h + Math.max(up[up.length - 1], lo[lo.length - 1]);
    const tl = lengths(set.tail, FY); f.tail = { pts: set.tail, d: tl, d0: j };
    f.sp1 = { pts: set.sp1, d: lengths(set.sp1, FY), d0: head[rough ? 6 : 2], taper: true };
    f.sp2 = { pts: set.sp2, d: lengths(set.sp2, FY), d0: j + tl[rough ? 6 : 2], taper: true };
    f.ext = { pts: set.ext, d: lengths(set.ext), d0: j + tl[tl.length - 1], outside: true, taper: true, taperTo: 0.45 };
    let total = 0; for (const k in f) total = Math.max(total, f[k].d0 + f[k].d[f[k].d.length - 1]);
    f.total = total;
    return f;
  }
  const FLOW_R = flowOf(ROUGH, true), FLOW_S = flowOf(SMOOTH, false);

  function partial(p, D) {
    const local = D - p.d0; if (local <= 0) return null;
    const out = [p.pts[0]];
    for (let i = 1; i < p.pts.length; i++) {
      if (p.d[i] <= local) { out.push(p.pts[i]); continue; }
      const u = (local - p.d[i - 1]) / (p.d[i] - p.d[i - 1]), a = p.pts[i - 1], b = p.pts[i];
      out.push([a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u]);
      return { pts: out, front: out[out.length - 1], done: false, of: p.pts.length };
    }
    return { pts: out, front: null, done: true, of: p.pts.length };
  }

  // a seam path in screen space: inside paths are flattened onto the rim ellipse, the outside path is not
  function toScreen(p, inside, g) { return inside ? [g.cx + p[0] * g.Ri, g.cy + p[1] * g.Ri * FY] : [g.cx + p[0] * g.R, g.cy + p[1] * g.R]; }
  function strokePath(ctx, pts, w, g, inside, taper, of, taperTo) {
    if (pts.length < 2) return;
    const P = pts.map((p) => toScreen(p, inside, g));
    if (!taper) { ctx.lineWidth = w; ctx.beginPath(); P.forEach((p, i) => (i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]))); ctx.stroke(); return; }
    const n = of || P.length;
    for (let i = 1; i < P.length; i++) {
      ctx.lineWidth = w * K.lerp(1, taperTo || 0.28, i / (n - 1));
      ctx.beginPath(); ctx.moveTo(P[i - 1][0], P[i - 1][1]); ctx.lineTo(P[i][0], P[i][1]); ctx.stroke();
    }
  }

  function geo(size, band) {
    const tiny = band === 'tiny', small = band === 'small';
    const R = size * (tiny ? 0.47 : small ? 0.46 : 0.42);
    return { cx: size / 2, cy: size * (tiny || small ? 0.33 : 0.355), R, Ri: R * 0.93, footY: size * (tiny || small ? 0.9 : 0.82), footR: R * 0.5 };
  }
  // the bowl's outside: the front of the rim, the walls curving in to the foot
  function bodyPath(c, g) {
    const { cx, cy, R, footY, footR } = g;
    c.moveTo(cx - R, cy);
    c.bezierCurveTo(cx - R, cy + (footY - cy) * 0.62, cx - footR * 1.25, footY - (footY - cy) * 0.06, cx - footR, footY);
    c.ellipse(cx, footY, footR, footR * FY * 0.45, 0, Math.PI, 0, true);
    c.bezierCurveTo(cx + footR * 1.25, footY - (footY - cy) * 0.06, cx + R, cy + (footY - cy) * 0.62, cx + R, cy);
    c.ellipse(cx, cy, R, R * FY, 0, 0, Math.PI, false);
    c.closePath();
  }
  function rimPath(c, g, k) { c.ellipse(g.cx, g.cy, g.R * (k || 1), g.R * FY * (k || 1), 0, 0, TAU); }

  const GOLD = (ctx, g) => K.lin(ctx, g.cx - g.R, g.cy - g.R * 0.5, g.cx + g.R, g.footY, [[0, '#f9e7a8'], [0.28, '#d9a740'], [0.5, '#f6d888'], [0.72, '#b88326'], [1, '#f1cf7c']]);

  function seams(ctx, size, band, g, D, hot) {
    const flow = band === 'tiny' || band === 'small' ? FLOW_S : FLOW_R;
    const keys = band === 'tiny' ? ['head', 'up', 'tail', 'ext'] : band === 'small' ? ['head', 'up', 'lo', 'tail', 'ext'] : ['head', 'up', 'lo', 'tail', 'sp1', 'sp2', 'ext'];
    const w = band === 'tiny' ? 1.6 : band === 'small' ? Math.max(1.6, size * 0.06) : size * (band === 'medium' ? 0.024 : 0.019);
    const parts = [], fronts = [];
    for (const k of keys) { const p = partial(flow[k], D); if (p) { parts.push([p, flow[k]]); if (p.front) fronts.push(toScreen(p.front, !flow[k].outside, g)); } }
    const pass = (fn) => {
      for (const inside of [true, false]) {
        ctx.save();
        if (inside) { ctx.beginPath(); rimPath(ctx, g, 0.985); ctx.clip(); }
        for (const [p, f] of parts) if (!f.outside === inside) fn(p, f, inside);
        ctx.restore();
      }
    };
    ctx.save(); ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    if (band === 'medium' || band === 'large') {
      // the lacquer sits a little proud of the glaze: a dark seat and a shadow down and right
      pass((p, f, inside) => { ctx.save(); ctx.translate(w * 0.28, w * 0.42); ctx.strokeStyle = 'rgba(0,0,0,0.7)'; strokePath(ctx, p.pts, w * 1.15, g, inside, f.taper, p.of, f.taperTo); ctx.restore(); });
      pass((p, f, inside) => { ctx.strokeStyle = '#6b4a17'; strokePath(ctx, p.pts, w * 1.12, g, inside, f.taper, p.of, f.taperTo); });
    }
    pass((p, f, inside) => { ctx.strokeStyle = band === 'tiny' ? '#e7b951' : GOLD(ctx, g); strokePath(ctx, p.pts, w, g, inside, f.taper, p.of, f.taperTo); });
    if (band !== 'tiny') {
      pass((p, f, inside) => { ctx.save(); ctx.translate(-w * 0.16, -w * 0.2); ctx.strokeStyle = 'rgba(255,247,214,' + (band === 'small' ? 0.5 : 0.75) + ')'; strokePath(ctx, p.pts, w * (band === 'small' ? 0.36 : 0.3), g, inside, f.taper, p.of, f.taperTo); ctx.restore(); });
    }
    if (hot > 0) {
      ctx.globalCompositeOperation = 'lighter';
      pass((p, f, inside) => { ctx.strokeStyle = 'rgba(255,214,140,' + (0.65 * hot).toFixed(3) + ')'; strokePath(ctx, p.pts, w * 0.9, g, inside, f.taper, p.of, f.taperTo); });
    }
    ctx.restore();
    return { fronts, w };
  }

  function bowl(o, size, band, theme, g) {
    const lite = theme === 'light', tiny = band === 'tiny', small = band === 'small';
    const { cx, cy, R, footY, footR } = g;
    if (!tiny && !small) {
      K.blur(o, size * 0.03, (x) => { x.fillStyle = lite ? 'rgba(20,16,10,0.42)' : 'rgba(0,0,0,0.65)'; x.beginPath(); x.ellipse(cx, footY + size * 0.012, R * 0.82, size * 0.05, 0, 0, TAU); x.fill(); });
    }
    // the foot ring: unglazed clay
    if (!tiny) {
      o.fillStyle = K.lin(o, cx - footR, 0, cx + footR, 0, [[0, '#8a6a4c'], [0.4, '#b89270'], [1, '#5a4130']]);
      o.beginPath(); o.ellipse(cx, footY + size * 0.012, footR * 0.88, footR * FY * 0.5, 0, 0, TAU); o.fill();
    }
    // the outer wall: black raku glaze, lit from the upper left, a cool rim light on the right
    o.fillStyle = tiny ? K.lin(o, cx - R, cy, cx + R, footY, [[0, lite ? '#3a3a42' : '#5a5a64'], [0.5, '#1d1d22'], [1, '#0a0a0c']])
      : K.lin(o, cx - R, 0, cx + R, 0, [[0, '#1d1d22'], [0.18, '#3a3a42'], [0.32, '#141418'], [0.75, '#0a0a0c'], [0.94, '#2a2c34'], [1, '#121216']]);
    o.beginPath(); bodyPath(o, g); o.fill();
    if (!tiny) {
      o.save(); o.beginPath(); bodyPath(o, g); o.clip();
      // falls into shadow toward the foot
      o.fillStyle = K.lin(o, 0, cy, 0, footY, [[0, 'rgba(0,0,0,0)'], [0.6, 'rgba(0,0,0,0.2)'], [1, 'rgba(0,0,0,0.55)']]);
      o.fillRect(cx - R, cy, 2 * R, footY - cy + size * 0.05);
      if (!small) {
        // raku lustre: smoky copper and teal clouds in the glaze
        const r = K.rng(7), cols = ['rgba(150,96,52,', 'rgba(40,110,120,', 'rgba(110,70,120,'];
        for (let i = 0; i < 16; i++) {
          const x = cx + (r() - 0.5) * 1.8 * R, y = cy + r() * (footY - cy), rr = R * (0.12 + r() * 0.22), c = cols[i % 3];
          o.fillStyle = K.rad(o, x, y, rr, [[0, c + (0.12 + r() * 0.1).toFixed(3) + ')'], [1, c + '0)']]);
          o.fillRect(x - rr, y - rr, 2 * rr, 2 * rr);
        }
        // a tall window reflection on the glossy wall
        o.fillStyle = K.lin(o, cx - R * 0.72, 0, cx - R * 0.5, 0, [[0, 'rgba(255,255,255,0)'], [0.5, 'rgba(255,255,255,0.32)'], [1, 'rgba(255,255,255,0)']]);
        o.beginPath(); o.ellipse(cx - R * 0.61, cy + (footY - cy) * 0.42, R * 0.1, (footY - cy) * 0.36, 0.12, 0, TAU); o.fill();
      }
      o.restore();
    }
    // the inside, seen over the rim: the far wall catches light, the floor sits in glossy dark
    const Ri = g.Ri;
    o.fillStyle = K.rad(o, cx + R * 0.05, cy + R * FY * 0.25, R, [[0, '#0b0b0e'], [0.55, '#141418'], [0.85, '#24242b'], [1, '#34343c']]);
    o.beginPath(); rimPath(o, g, 0.985); o.fill();
    if (!tiny) {
      o.save(); o.beginPath(); rimPath(o, g, 0.985); o.clip();
      // the near wall's lip throws a shadow onto the far floor; the far wall glows faintly
      o.fillStyle = K.lin(o, 0, cy - R * FY, 0, cy + R * FY, [[0, 'rgba(160,165,185,0.22)'], [0.45, 'rgba(0,0,0,0)'], [1, 'rgba(0,0,0,0.6)']]);
      o.fillRect(cx - R, cy - R * FY, 2 * R, 2 * R * FY);
      if (!small) {
        const r = K.rng(19);
        for (let i = 0; i < 8; i++) {
          const x = cx + (r() - 0.5) * 1.6 * Ri, y = cy + (r() - 0.6) * 1.4 * Ri * FY, rr = Ri * (0.15 + r() * 0.2), c = i % 2 ? 'rgba(150,96,52,' : 'rgba(40,110,120,';
          o.fillStyle = K.rad(o, x, y, rr, [[0, c + '0.12)'], [1, c + '0)']]);
          o.fillRect(x - rr, y - rr, 2 * rr, 2 * rr);
        }
        // the soft gloss of the far inner wall
        o.fillStyle = K.rad(o, cx - R * 0.3, cy - R * FY * 0.55, R * 0.45, [[0, 'rgba(255,255,255,0.16)'], [1, 'rgba(255,255,255,0)']]);
        o.fillRect(cx - R, cy - R * FY, 2 * R, 2 * R * FY);
      }
      o.restore();
    }
    // the lip: a rounded rim, bright where it turns toward the light
    if (!tiny) {
      o.lineWidth = Math.max(1, size * (small ? 0.03 : 0.014));
      o.strokeStyle = K.lin(o, cx - R, 0, cx + R, 0, [[0, 'rgba(230,232,240,0.85)'], [0.35, 'rgba(120,122,135,0.5)'], [0.7, 'rgba(60,62,70,0.6)'], [1, 'rgba(170,175,190,0.6)']]);
      o.beginPath(); rimPath(o, g, 1 - o.lineWidth / (2 * R)); o.stroke();
      if (!small) {
        o.lineWidth = size * 0.005; o.strokeStyle = 'rgba(255,255,255,0.7)'; o.lineCap = 'round';
        o.beginPath(); o.ellipse(cx, cy, R * 0.995, R * FY * 0.995, 0, Math.PI * 1.08, Math.PI * 1.42); o.stroke();
      }
    } else {
      o.lineWidth = 1; o.strokeStyle = '#6a6a74'; o.beginPath(); rimPath(o, g, 1 - 0.5 / R); o.stroke();
    }
  }

  function glint(ctx, size, band, g, idle) {
    const flow = band === 'small' ? FLOW_S : FLOW_R;
    const w = band === 'small' ? Math.max(1.6, size * 0.06) : size * (band === 'medium' ? 0.024 : 0.019);
    const u = K.seg(K.loop(idle, 6000), 0.12, 0.72), env = Math.sin(Math.PI * u);
    if (env < 0.001) return;
    // the glint runs the main line: inside from the far rim, then over the lip and down the wall
    const pts = flow.head.pts.concat(flow.up.pts.slice(1), flow.tail.pts.slice(1)).map((p) => toScreen(p, true, g)).concat(flow.ext.pts.slice(1).map((p) => toScreen(p, false, g)));
    const d = lengths(pts), at = K.ease.inOut(u) * d[d.length - 1];
    let i = 1; while (i < d.length - 1 && d[i] < at) i++;
    const v = (at - d[i - 1]) / (d[i] - d[i - 1] || 1), x = pts[i - 1][0] + (pts[i][0] - pts[i - 1][0]) * v, y = pts[i - 1][1] + (pts[i][1] - pts[i - 1][1]) * v;
    const rr = size * 0.12;
    ctx.save(); ctx.globalCompositeOperation = 'lighter'; ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    ctx.strokeStyle = K.rad(ctx, x, y, rr, [[0, 'rgba(255,248,220,' + (0.85 * env).toFixed(3) + ')'], [0.4, 'rgba(255,220,150,' + (0.3 * env).toFixed(3) + ')'], [1, 'rgba(255,200,120,0)']]);
    for (const inside of [true, false]) {
      ctx.save(); if (inside) { ctx.beginPath(); rimPath(ctx, g, 0.985); ctx.clip(); }
      for (const k of ['head', 'up', 'lo', 'tail', 'sp1', 'sp2', 'ext']) if (!flow[k].outside === inside) strokePath(ctx, flow[k].pts, w * 0.75, g, inside, flow[k].taper, flow[k].pts.length, flow[k].taperTo);
      ctx.restore();
    }
    ctx.restore();
  }

  MarrowIcons.register({
    letter: 'L',
    id: 'kintsugi',
    name: 'Kintsugi',
    meaning: 'It finds what is broken and mends it in gold: your code comes back stronger, and more beautiful, than before.',
    story: 'A black raku tea bowl, glossy and smoky, mended the Japanese way with lacquer and gold. Inside, the seam splits and rejoins like a branch merging home; then it runs over the lip and down the wall. Black glaze, warm gold, a ring of bare clay at the foot.',
    swatch: ['#0c0c0f', '#2b2a30', '#e2b54a'],
    launch: { ms: 1100, note: 'The bowl rises out of the dark, then molten gold runs along the crack from the far rim, splits around the island, rejoins, spills over the lip and cools from white heat to gold.' },
    idle: { ms: 6000, note: 'Once a loop a soft glint of light travels the whole seam, inside and down the wall; the glaze stays still.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const g = geo(size, band);
      const L = t.launch;
      const px = Math.round(size * s0);
      const eB = K.ease.mac(K.seg(L, 0, 0.5)), aB = K.ease.sine(K.seg(L, 0, 0.42));
      const eFlow = K.ease.inOut(K.seg(L, 0.22, 0.9));
      const hot = 1 - K.ease.out(K.seg(L, 0.45, 1));
      ctx.save();
      ctx.globalAlpha = aB;
      if (eB < 1) { const k = K.lerp(0.95, 1, eB); ctx.translate(g.cx, g.footY + size * 0.03 * (1 - eB)); ctx.scale(k, k); ctx.translate(-g.cx, -g.footY); }
      const still = L >= 1;
      const c = K.cache('L-kintsugi|' + (still ? 'full|' : 'bowl|') + size + '|' + theme + '|' + px, px, px, (o) => {
        o.setTransform(px / size, 0, 0, px / size, 0, 0); bowl(o, size, band, theme, g);
        if (still) seams(o, size, band, g, 1e9, 0);
      });
      ctx.drawImage(c, 0, 0, size, size);
      ctx.globalAlpha = 1;
      if (still) {
        if (t.idle > 0 && band !== 'tiny') glint(ctx, size, band, g, t.idle);
        else if (!t.idle) K.cache('L-kintsugi|bowl|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); bowl(o, size, band, theme, g); });
      } else if (eFlow > 0) {
        const flow = band === 'tiny' || band === 'small' ? FLOW_S : FLOW_R;
        const res = seams(ctx, size, band, g, eFlow >= 1 ? 1e9 : eFlow * flow.total, band === 'tiny' ? 0 : hot);
        if (hot > 0 && res.fronts.length && band !== 'tiny') {
          ctx.globalCompositeOperation = 'lighter';
          for (const [x, y] of res.fronts) {
            const rr = res.w * 2.6;
            ctx.fillStyle = K.rad(ctx, x, y, rr, [[0, 'rgba(255,250,230,' + (0.95 * hot).toFixed(3) + ')'], [0.35, 'rgba(255,190,90,' + (0.5 * hot).toFixed(3) + ')'], [1, 'rgba(255,150,40,0)']]);
            ctx.fillRect(x - rr, y - rr, 2 * rr, 2 * rr);
          }
          ctx.globalCompositeOperation = 'source-over';
        }
      }
      ctx.restore();
    },
  });
})();
