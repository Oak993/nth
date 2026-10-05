// M Geode: a split stone, plain outside, with a lit amethyst cavity within.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;

  // the stone's outline: a slightly wide pebble with a few slow lobes; k scales it, j adds a band's own wander
  function rOf(a, k, j) {
    let r = 1 + 0.045 * Math.sin(2 * a + 0.6) + 0.035 * Math.sin(3 * a + 1.9) + 0.018 * Math.sin(5 * a + 0.3);
    if (j) r += j * (0.03 * Math.sin(4 * a + 2.4) + 0.02 * Math.sin(7 * a + 1.1));
    return r * k;
  }
  function shape(ctx, cx, cy, R, k, j, sx, rough) {
    const N = rough ? 160 : 72, r = rough ? K.rng(rough) : null;
    for (let i = 0; i <= N; i++) {
      const a = (i / N) * TAU;
      let rr = rOf(a, k, j) * R;
      if (r && i < N) rr *= 1 + (r() - 0.5) * 0.012;
      const x = cx + Math.cos(a) * rr * sx, y = cy + Math.sin(a) * rr;
      if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y);
    }
    ctx.closePath();
  }

  const SX = 1.06; // a little wider than tall
  const FY = 0.64; // the cut face is tipped toward us: seen as an ellipse
  function faceT(c, cx, cy) { c.translate(cx, cy); c.scale(1, FY); c.translate(-cx, -cy); }
  // the half-stone under the cut face: its rounded, rough outer shell
  function shellPath(c, cx, cy, R, D, rough) {
    const N = 120, r = rough ? K.rng(rough) : null, pts = [];
    for (let i = 0; i <= N; i++) {
      const a = (i / N) * TAU; let rr = rOf(a, 1, 0) * R; if (r && i < N) rr *= 1 + (r() - 0.5) * 0.02;
      const x = cx + Math.cos(a) * rr * SX, y = cy + Math.sin(a) * rr * FY;
      // the lower half bulges down into the dome of the stone
      pts.push([x, y + (Math.sin(a) > 0 ? Math.pow(Math.sin(a), 0.6) * D : 0)]);
    }
    pts.forEach((p, i) => (i ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1]))); c.closePath();
  }
  const CAV = 0.6; // the cavity's scale inside the stone

  // the druse: a dense, irregular crowd of crystal points lining the cavity wall, each leaning in toward the light
  function crystals(o, cx, cy, R, band) {
    const rnd = K.rng(91), n = band === 'large' ? 210 : 90, list = [];
    for (let i = 0; i < n; i++) {
      const a = rnd() * TAU, d = 1 - 0.68 * Math.pow(rnd(), 1.4);
      list.push({ a, d, j: (rnd() - 0.5) * 0.5, k: 0.7 + rnd() * 0.6, l: 0.8 + rnd() * 0.5 });
    }
    list.sort((p, q) => q.d - p.d);
    const Lk = [-0.6, -0.8];
    for (const c of list) {
      const rw = rOf(c.a, CAV, 0.6) * R;
      const bx = cx + Math.cos(c.a) * rw * c.d * SX, by = cy + Math.sin(c.a) * rw * c.d;
      const da = c.a + Math.PI + c.j, dx = Math.cos(da), dy = Math.sin(da), px = -dy, py = dx;
      const sz = R * CAV * (0.05 + 0.1 * c.d) * c.k, len = sz * 1.9 * c.l;
      const b0 = [bx + px * sz, by + py * sz], b1 = [bx + px * sz * 0.5 - dx * sz * 0.55, by + py * sz * 0.5 - dy * sz * 0.55];
      const b2 = [bx - px * sz * 0.5 - dx * sz * 0.55, by - py * sz * 0.5 - dy * sz * 0.55], b3 = [bx - px * sz, by - py * sz];
      const T = [bx + dx * len, by + dy * len];
      const inner = 1 - c.d; // nearer the heart: lighter and pinker
      [[b0, b1], [b1, b2], [b2, b3]].forEach(([u, v], k) => {
        const mx = (u[0] + v[0]) / 2 - bx, my = (u[1] + v[1]) / 2 - by, ml = Math.hypot(mx, my) || 1;
        const lit = K.clamp(0.5 + 0.5 * (mx / ml * Lk[0] + my / ml * Lk[1]), 0, 1) * 0.75 + (k === 1 ? 0.0 : 0.12);
        const base = K.mix('#1d0638', '#4a1d80', K.clamp(0.15 + 0.5 * lit + 0.35 * inner, 0, 1));
        const tip = K.mix('#7d45cc', '#f6e2ff', K.clamp(0.15 + 0.55 * lit + 0.4 * inner, 0, 1));
        o.fillStyle = K.lin(o, bx, by, T[0], T[1], [[0, base], [1, tip]]);
        o.beginPath(); o.moveTo(u[0], u[1]); o.lineTo(v[0], v[1]); o.lineTo(T[0], T[1]); o.closePath(); o.fill();
      });
      if (band === 'large') {
        o.strokeStyle = 'rgba(255,236,255,' + (0.12 + 0.3 * inner).toFixed(3) + ')'; o.lineWidth = R * 0.005;
        o.beginPath(); o.moveTo(b1[0], b1[1]); o.lineTo(T[0], T[1]); o.lineTo(b2[0], b2[1]); o.stroke();
      }
    }
  }

  function stone(o, size, band, theme, cx, cy, R) {
    const lite = theme === 'light';
    const D = size * (band === 'tiny' ? 0.16 : band === 'small' ? 0.17 : 0.16);
    if (band === 'tiny') {
      o.fillStyle = '#5b5148'; o.beginPath(); shellPath(o, cx, cy, R, D); o.fill();
      o.save(); faceT(o, cx, cy);
      o.fillStyle = '#b3a99c'; o.beginPath(); shape(o, cx, cy, R, 1, 0, SX); o.fill();
      o.fillStyle = '#6a2cb0'; o.beginPath(); shape(o, cx, cy, R, 0.7, 0.6, SX); o.fill();
      o.restore();
      return;
    }
    if (band === 'medium' || band === 'large') {
      K.blur(o, size * 0.03, (x) => { x.fillStyle = lite ? 'rgba(30,20,10,0.38)' : 'rgba(0,0,0,0.6)'; x.beginPath(); x.ellipse(cx, cy + R * FY + D * 0.85, R * SX * 0.95, size * 0.06, 0, 0, TAU); x.fill(); });
    }
    // the outer shell: rough, dusty brown-grey stone, lit from the upper left, lumpy
    o.fillStyle = K.lin(o, cx - R, cy, cx + R * 0.6, cy + R * FY + D, [[0, '#7a6f63'], [0.45, '#4f463d'], [1, '#2a241f']]);
    o.beginPath(); shellPath(o, cx, cy, R, D, band === 'large' ? 9 : 0); o.fill();
    if (band === 'large' || band === 'medium') {
      o.save(); o.beginPath(); shellPath(o, cx, cy, R, D); o.clip();
      const r = K.rng(41);
      for (let i = 0; i < 18; i++) {
        const x = cx + (r() - 0.5) * 2 * R * SX, y = cy + R * FY * 0.4 + r() * (R * FY * 0.6 + D), rr = R * (0.08 + r() * 0.16);
        o.fillStyle = K.rad(o, x - rr * 0.3, y - rr * 0.3, rr, [[0, 'rgba(170,155,138,0.35)'], [0.6, 'rgba(120,108,95,0.1)'], [1, 'rgba(40,32,26,0)']]);
        o.fillRect(x - rr, y - rr, 2 * rr, 2 * rr);
      }
      for (let i = 0; i < (band === 'large' ? 700 : 200); i++) {
        o.fillStyle = r() < 0.6 ? 'rgba(20,15,10,0.35)' : 'rgba(210,198,180,0.22)';
        o.fillRect(cx + (r() - 0.5) * 2 * R * SX, cy + r() * (R * FY + D), size * 0.003, size * 0.003);
      }
      o.restore();
    }
    o.save(); faceT(o, cx, cy);
    // the cut and polished face of the rind
    o.fillStyle = K.rad(o, cx - R * 0.4, cy - R * 0.5, R * 1.7, [[0, '#9a9083'], [0.5, '#6d645a'], [1, '#4a423a']]);
    o.beginPath(); shape(o, cx, cy, R, 1, 0, SX, band === 'large' ? 5 : 0); o.fill();
    o.save(); o.beginPath(); shape(o, cx, cy, R, 1, 0, SX); o.clip();
    if (band === 'large' || band === 'medium') {
      const r = K.rng(3);
      for (let i = 0; i < (band === 'large' ? 900 : 260); i++) {
        const a = r() * TAU, d = Math.sqrt(r()) * R * 1.05;
        o.fillStyle = r() < 0.55 ? 'rgba(30,24,20,0.28)' : 'rgba(225,215,200,0.18)';
        o.fillRect(cx + Math.cos(a) * d * SX, cy + Math.sin(a) * d, size * 0.003, size * 0.003);
      }
    }
    // agate banding: chalcedony rings that follow the stone inward
    const bands = band === 'small' ? [[0.8, '#cfc8da', 0.3], [0.7, '#7e6f9c', 0.6]] :
      [[0.88, '#8f877e', 0.0], [0.86, '#b9b2c2', 0.15], [0.835, '#a49cb6', 0.3], [0.815, '#c8c0d4', 0.2], [0.79, '#9a8fb2', 0.45],
        [0.77, '#bdb2cf', 0.4], [0.745, '#8a7caa', 0.55], [0.72, '#d6cde4', 0.5], [0.69, '#a593c8', 0.6], [0.665, '#e4dcf0', 0.6], [0.64, '#6b4d98', 0.6], [0.625, '#3a1e62', 0.6]];
    for (const [k, c, j] of bands) {
      o.fillStyle = band === 'small' ? c : K.lin(o, cx - R, cy - R, cx + R, cy + R, [[0, K.mix(c, '#ffffff', 0.25)], [1, K.mix(c, '#3a3050', 0.25)]]);
      o.beginPath(); shape(o, cx, cy, R, k, j, SX); o.fill();
    }
    if (band !== 'small') {
      // the polish: a broad soft sheen over the face of the cut
      o.fillStyle = K.lin(o, cx - R, cy - R, cx + R * 0.4, cy + R * 0.4, [[0, 'rgba(255,255,255,0.28)'], [0.5, 'rgba(255,255,255,0.04)'], [1, 'rgba(255,255,255,0)']]);
      o.fillRect(cx - 2 * R, cy - 2 * R, 4 * R, 4 * R);
    }
    o.restore();
    // the cavity: deep violet, then the crystals
    o.save(); o.beginPath(); shape(o, cx, cy, R, CAV, 0.6, SX); o.clip();
    o.fillStyle = K.rad(o, cx, cy, R * CAV, [[0, '#5a2a9a'], [0.6, '#2a0c4e'], [1, '#160430']]);
    o.fillRect(cx - R, cy - R, 2 * R, 2 * R);
    if (band !== 'small') crystals(o, cx, cy, R, band);
    else {
      o.fillStyle = K.rad(o, cx, cy, R * CAV, [[0, '#d9b8ff'], [0.5, '#8648d6'], [1, '#3a1466']]);
      o.fillRect(cx - R, cy - R, 2 * R, 2 * R);
    }
    // the cavity's rim shades the outer crystals
    o.lineWidth = R * 0.08; o.strokeStyle = 'rgba(20,4,40,0.55)';
    o.beginPath(); shape(o, cx, cy, R, CAV, 0.6, SX); o.stroke();
    o.restore();
    // a crisp edge where the cut face meets the stone's side
    if (band !== 'small') {
      o.lineWidth = Math.max(1, size * 0.005);
      o.strokeStyle = 'rgba(255,248,235,0.45)';
      o.beginPath(); shape(o, cx, cy, R - o.lineWidth / 2, 1, 0, SX); o.stroke();
    }
    o.restore();
  }

  // the light inside: a glow at the heart of the cavity, washing the crystal tips
  function heart(ctx, size, band, cx, cy, R, a) {
    if (a <= 0) return;
    ctx.save(); faceT(ctx, cx, cy);
    ctx.beginPath(); shape(ctx, cx, cy, R, CAV, 0.6, SX); ctx.clip();
    ctx.globalCompositeOperation = 'lighter';
    const rr = R * CAV * (band === 'tiny' ? 0.7 : 0.95);
    ctx.fillStyle = K.rad(ctx, cx, cy, rr, [[0, 'rgba(255,215,255,' + (0.7 * a).toFixed(3) + ')'], [0.22, 'rgba(225,130,255,' + (0.42 * a).toFixed(3) + ')'], [0.55, 'rgba(160,70,255,' + (0.16 * a).toFixed(3) + ')'], [1, 'rgba(120,40,220,0)']]);
    ctx.fillRect(cx - rr, cy - rr, 2 * rr, 2 * rr);
    ctx.restore();
  }

  // a few facets that catch a glint now and then (positions are fixed; their timing is in the idle loop)
  const GLINTS = [[-0.34, -0.2, 0.08], [0.3, -0.28, 0.3], [0.12, 0.34, 0.52], [-0.22, 0.26, 0.74]];
  function glints(ctx, size, cx, cy, R, ph, lift) {
    ctx.save(); faceT(ctx, cx, cy); ctx.globalCompositeOperation = 'lighter';
    for (const [gx, gy, t0] of GLINTS) {
      const u = ph - t0, w = 0.16;
      if (u <= 0 || u >= w) continue;
      const e = Math.sin(Math.PI * u / w) * lift;
      const x = cx + gx * R * SX, y = cy + gy * R, r = size * 0.035;
      ctx.fillStyle = K.rad(ctx, x, y, r, [[0, 'rgba(255,250,255,' + (0.9 * e).toFixed(3) + ')'], [0.25, 'rgba(250,210,255,' + (0.35 * e).toFixed(3) + ')'], [1, 'rgba(240,180,255,0)']]);
      ctx.fillRect(x - r, y - r, 2 * r, 2 * r);
    }
    ctx.restore();
  }

  MarrowIcons.register({
    letter: 'M',
    id: 'geode',
    name: 'Geode',
    meaning: 'Crack the hard problem open and it is full of light: plain rock outside, brilliance within.',
    story: 'Half of a geode, its cut face tipped toward you: a rough, dusty stone shell, rings of milky agate, then a cavity crowded with amethyst points leaning toward a light deep inside. The breakthrough every hard problem hides. Stone, chalcedony, amethyst.',
    swatch: ['#6d645a', '#e6e1ec', '#7a3fc4'],
    launch: { ms: 1100, note: 'The stone settles into view, its crystal lining grows in ring by ring from the wall toward the centre, and the light at the heart blooms through the points.' },
    idle: { ms: 5600, note: 'The light at the heart breathes slowly, and now and then a single crystal facet catches a glint.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const cx = size / 2, cy = size * (band === 'tiny' ? 0.4 : band === 'small' ? 0.4 : 0.41);
      const R = band === 'tiny' ? (size / 2 - 0.5) / (SX * 1.06) : band === 'small' ? (size / 2 - 1) / (SX * 1.07) : size * 0.385;
      const L = t.launch;
      const px = Math.round(size * s0);
      const still = K.cache('M-geode|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); stone(o, size, band, theme, cx, cy, R); });
      ctx.save();
      if (L >= 1) {
        ctx.drawImage(still, 0, 0, size, size);
        let a = 1, ph = 0;
        if (t.idle > 0) { ph = K.loop(t.idle, 5600); a = 1 - 0.16 * (0.5 - 0.5 * Math.cos(TAU * ph)); }
        heart(ctx, size, band, cx, cy, R, a);
        if (t.idle > 0 && (band === 'medium' || band === 'large')) glints(ctx, size, cx, cy, R, ph, 1);
        ctx.restore(); return;
      }
      // stone first, risen from slightly below
      const eIn = K.ease.mac(K.seg(L, 0, 0.45)), aIn = K.ease.sine(K.seg(L, 0, 0.35));
      ctx.globalAlpha = aIn;
      if (eIn < 1) { const k = K.lerp(0.94, 1, eIn); ctx.translate(cx, cy + size * 0.03 * (1 - eIn)); ctx.scale(k, k); ctx.translate(-cx, -cy); }
      // the crystals grow in from the cavity wall: everything inside a shrinking hole is still empty violet dark
      const rev = K.ease.inOut(K.seg(L, 0.2, 0.78));
      ctx.drawImage(still, 0, 0, size, size);
      if (rev < 1) {
        ctx.save(); ctx.globalAlpha = 1; ctx.globalCompositeOperation = 'source-atop'; faceT(ctx, cx, cy);
        ctx.beginPath(); shape(ctx, cx, cy, R, CAV * (1 - rev) * 1.02, 0.6, SX); ctx.clip();
        ctx.fillStyle = K.rad(ctx, cx, cy, R * CAV, [[0, '#2a0b4a'], [1, '#14042a']]);
        ctx.fillRect(0, 0, size, size);
        ctx.restore();
        // a soft front where the crystals are forming
        if (band !== 'tiny' && rev > 0) {
          ctx.save(); ctx.globalCompositeOperation = 'lighter'; faceT(ctx, cx, cy);
          ctx.beginPath(); shape(ctx, cx, cy, R, CAV, 0.6, SX); ctx.clip();
          ctx.lineWidth = R * 0.06; ctx.strokeStyle = 'rgba(200,140,255,' + (0.35 * Math.sin(Math.PI * rev)).toFixed(3) + ')';
          ctx.beginPath(); shape(ctx, cx, cy, R, CAV * (1 - rev) * 1.02, 0.6, SX); ctx.stroke();
          ctx.restore();
        }
      }
      ctx.globalAlpha = 1;
      const hb = K.ease.out(K.seg(L, 0.55, 1));
      const flare = band === 'tiny' ? 0 : 0.35 * Math.pow(Math.sin(Math.PI * K.seg(L, 0.6, 1)), 2);
      heart(ctx, size, band, cx, cy, R, hb * aIn + flare);
      ctx.restore();
    },
  });
})();
