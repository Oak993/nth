// R Nib: a platinum fountain-pen nib that has just written a line of light on midnight ink.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;
  const ROT = Math.PI / 4; // the nib leans so its point travels down and to the left

  // the written line, in fractions of size: a short flowing wave that ends at the nib's point
  const LINE = [[0.1, 0.735], [0.17, 0.79], [0.26, 0.795], [0.34, 0.74], [0.4, 0.655]];
  const LINE_SMALL = [[0.06, 0.76], [0.14, 0.84], [0.26, 0.86], [0.37, 0.79], [0.45, 0.66]];
  function curve(pts, n) {
    // a Catmull-Rom spline through the points, sampled evenly in parameter
    const out = [];
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(pts.length - 1, i + 2)];
      for (let k = 0; k < n; k++) {
        const t = k / n, t2 = t * t, t3 = t2 * t;
        const f = (a, b, c, d) => 0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
        out.push([f(p0[0], p1[0], p2[0], p3[0]), f(p0[1], p1[1], p2[1], p3[1])]);
      }
    }
    out.push(pts[pts.length - 1]);
    // cumulative length for arc-length progress
    const d = [0]; for (let i = 1; i < out.length; i++) d.push(d[i - 1] + Math.hypot(out[i][0] - out[i - 1][0], out[i][1] - out[i - 1][1]));
    return { pts: out, d, L: d[d.length - 1] };
  }
  const C_BIG = curve(LINE, 16), C_SMALL = curve(LINE_SMALL, 8);
  function pointAt(c, u) {
    const at = u * c.L; let i = 1; while (i < c.d.length - 1 && c.d[i] < at) i++;
    const v = (at - c.d[i - 1]) / (c.d[i] - c.d[i - 1] || 1), a = c.pts[i - 1], b = c.pts[i];
    return { p: [a[0] + (b[0] - a[0]) * v, a[1] + (b[1] - a[1]) * v], i };
  }

  function plate(o, size, band, theme) {
    const { x, y, w, h } = K.box(size), rad = w * 0.235;
    if (band === 'medium' || band === 'large') {
      K.blur(o, size * 0.025, (q) => { q.fillStyle = theme === 'light' ? 'rgba(0,10,40,0.38)' : 'rgba(0,0,0,0.55)'; q.beginPath(); K.squircle(q, x + w * 0.02, y + h * 0.035, w * 0.96, h * 0.96, rad); q.fill(); });
    }
    o.fillStyle = K.lin(o, x, y, x + w * 0.5, y + h, [[0, '#17264d'], [0.5, '#0b1530'], [1, '#050a18']]);
    o.beginPath(); K.squircle(o, x, y, w, h, rad); o.fill();
    if (band === 'tiny') return;
    o.save(); o.beginPath(); K.squircle(o, x, y, w, h, rad); o.clip();
    // a pool of cool light where the writing is
    o.fillStyle = K.rad(o, x + w * 0.3, y + h * 0.78, w * 0.5, [[0, 'rgba(70,150,255,0.16)'], [1, 'rgba(70,150,255,0)']]);
    o.fillRect(x, y, w, h);
    o.lineWidth = Math.max(1, size * 0.008);
    o.strokeStyle = K.lin(o, 0, y, 0, y + h, [[0, 'rgba(190,210,255,0.32)'], [0.35, 'rgba(190,210,255,0.05)'], [1, 'rgba(190,210,255,0.1)']]);
    o.beginPath(); K.squircle(o, x + o.lineWidth / 2, y + o.lineWidth / 2, w - o.lineWidth, h - o.lineWidth, rad - o.lineWidth / 2); o.stroke();
    o.restore();
    if (theme === 'light' && band !== 'small') { o.lineWidth = Math.max(1, size * 0.003); o.strokeStyle = 'rgba(0,10,40,0.35)'; o.beginPath(); K.squircle(o, x, y, w, h, rad); o.stroke(); }
  }

  // the nib in local units: point at the origin, body up the -y axis, length 1
  function nibPath(c) {
    c.moveTo(0, 0);
    c.bezierCurveTo(0.1, -0.12, 0.2, -0.3, 0.255, -0.52);
    c.bezierCurveTo(0.28, -0.62, 0.3, -0.7, 0.29, -0.76);  // the shoulder
    c.lineTo(0.255, -0.8);
    c.lineTo(0.255, -1.0);
    c.quadraticCurveTo(0, -0.93, -0.255, -1.0);             // the curved base where it enters the section
    c.lineTo(-0.255, -0.8);
    c.lineTo(-0.29, -0.76);
    c.bezierCurveTo(-0.3, -0.7, -0.28, -0.62, -0.255, -0.52);
    c.bezierCurveTo(-0.2, -0.3, -0.1, -0.12, 0, 0);
    c.closePath();
  }

  function nib(o, size, band, sheen) {
    const tiny = band === 'tiny', small = band === 'small';
    const Ln = size * (tiny ? 0.62 : small ? 0.58 : 0.42);
    o.save(); o.scale(Ln, Ln);
    if (tiny) { o.fillStyle = '#d9dee6'; o.beginPath(); nibPath(o); o.fill(); o.restore(); return; }
    if (!small) {
      // shadow on the page
      K.blur(o, 0.03, (q) => { q.save(); q.translate(0.05, 0.06); q.fillStyle = 'rgba(0,0,10,0.6)'; q.beginPath(); nibPath(q); q.fill(); q.restore(); });
    }
    if (!small) {
      // the pen's section and barrel: black lacquer running off past the plate edge, a platinum ring between
      o.fillStyle = K.lin(o, -0.34, 0, 0.34, 0, [[0, '#05060a'], [0.3, '#3a3f4c'], [0.42, '#8a90a0'], [0.55, '#2a2e38'], [1, '#05060a']]);
      o.beginPath(); o.moveTo(-0.27, -0.97); o.lineTo(-0.33, -1.6); o.lineTo(0.33, -1.6); o.lineTo(0.27, -0.97); o.closePath(); o.fill();
      o.fillStyle = K.lin(o, -0.36, 0, 0.36, 0, [[0, '#5d6573'], [0.35, '#ffffff'], [0.6, '#b9c0ca'], [1, '#4c5360']]);
      o.fillRect(-0.335, -1.62, 0.67, 0.06);
      o.fillStyle = K.lin(o, -0.4, 0, 0.4, 0, [[0, '#020305'], [0.3, '#2c303a'], [0.4, '#6a7080'], [0.52, '#1c1f27'], [1, '#020305']]);
      o.beginPath(); o.moveTo(-0.37, -1.62); o.lineTo(-0.4, -2.6); o.lineTo(0.4, -2.6); o.lineTo(0.37, -1.62); o.closePath(); o.fill();
    }
    // polished platinum, curved across its width: bright band left of centre, darker flanks
    o.fillStyle = K.lin(o, -0.3, 0, 0.3, 0, [[0, '#5d6573'], [0.18, '#c9d0da'], [0.36, '#ffffff'], [0.5, '#dfe4eb'], [0.7, '#8a929f'], [0.88, '#b9c0ca'], [1, '#4c5360']]);
    o.beginPath(); nibPath(o); o.fill();
    o.save(); o.beginPath(); nibPath(o); o.clip();
    // along the length: brighter near the point where it bends toward the page
    o.fillStyle = K.lin(o, 0, 0, 0, -1, [[0, 'rgba(255,255,255,0.25)'], [0.45, 'rgba(255,255,255,0)'], [1, 'rgba(20,30,50,0.35)']]);
    o.fillRect(-0.4, -1.05, 0.8, 1.1);
    if (!small) {
      // engraving: two fine scrolls near the base and a line following each shoulder
      o.strokeStyle = 'rgba(40,48,62,0.55)'; o.lineWidth = 0.008;
      o.beginPath(); o.moveTo(-0.2, -0.86); o.quadraticCurveTo(0, -0.8, 0.2, -0.86); o.stroke();
      o.beginPath(); o.moveTo(-0.17, -0.8); o.quadraticCurveTo(0, -0.745, 0.17, -0.8); o.stroke();
      o.beginPath(); o.moveTo(-0.215, -0.5); o.bezierCurveTo(-0.17, -0.3, -0.09, -0.16, -0.02, -0.06); o.stroke();
      o.beginPath(); o.moveTo(0.215, -0.5); o.bezierCurveTo(0.17, -0.3, 0.09, -0.16, 0.02, -0.06); o.stroke();
    }
    // a moving specular glint (idle): a soft band across the nib
    if (sheen > 0) {
      o.globalCompositeOperation = 'lighter';
      const y = K.lerp(-1.05, 0.05, sheen.pos);
      o.fillStyle = K.lin(o, 0, y - 0.12, 0, y + 0.12, [[0, 'rgba(255,255,255,0)'], [0.5, 'rgba(255,255,255,' + (0.35 * sheen.a).toFixed(3) + ')'], [1, 'rgba(255,255,255,0)']]);
      o.fillRect(-0.4, -1.05, 0.8, 1.1);
      o.globalCompositeOperation = 'source-over';
    }
    o.restore();
    // the slit and the breather hole
    o.strokeStyle = '#0d1220'; o.lineWidth = small ? 0.035 : 0.014; o.lineCap = 'round';
    o.beginPath(); o.moveTo(0, -0.01); o.lineTo(0, -0.43); o.stroke();
    o.fillStyle = '#0d1220'; o.beginPath(); o.ellipse(0, -0.47, small ? 0.06 : 0.045, small ? 0.07 : 0.055, 0, 0, TAU); o.fill();
    if (!small) {
      // the edge of the metal catches a rim of light
      o.strokeStyle = 'rgba(255,255,255,0.45)'; o.lineWidth = 0.008;
      o.beginPath(); nibPath(o); o.stroke();
    }
    o.restore();
  }

  // the line of light, drawn from its start to u (0..1); hot: how fresh the leading part is
  function light(ctx, size, band, u, hot, pulse) {
    if (u <= 0) return;
    const c = band === 'tiny' || band === 'small' ? C_SMALL : C_BIG;
    const end = pointAt(c, u);
    const pts = c.pts.slice(0, end.i).concat([end.p]).map(([x, y]) => [x * size, y * size]);
    ctx.save(); ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    if (band === 'tiny') {
      ctx.strokeStyle = '#66c6ff'; ctx.lineWidth = 1.6;
      ctx.beginPath(); pts.forEach((p, i) => (i ? ctx.lineTo(p[0], p[1]) : ctx.moveTo(p[0], p[1]))); ctx.stroke(); ctx.restore(); return;
    }
    const wMax = size * (band === 'small' ? 0.065 : 0.022);
    // calligraphic: thin at the start, swelling toward the point
    const n = pts.length;
    ctx.globalCompositeOperation = 'lighter';
    const passes = band === 'small' ? [[1, 'rgba(120,200,255,0.85)']] : [[3.2, 'rgba(60,140,255,0.10)'], [1.9, 'rgba(90,180,255,0.22)'], [1, 'rgba(170,225,255,0.9)'], [0.42, 'rgba(255,255,255,0.95)']];
    for (const [k, col] of passes) {
      ctx.strokeStyle = col;
      for (let i = 1; i < n; i++) {
        const f = i / (c.pts.length - 1);
        ctx.lineWidth = wMax * k * K.lerp(0.25, 1, Math.pow(f, 0.7));
        ctx.beginPath(); ctx.moveTo(pts[i - 1][0], pts[i - 1][1]); ctx.lineTo(pts[i][0], pts[i][1]); ctx.stroke();
      }
    }
    // a pulse of light travelling the line (idle)
    if (pulse && pulse.a > 0.002 && band !== 'small') {
      const pp = pointAt(c, pulse.pos).p, x = pp[0] * size, y = pp[1] * size, r = size * 0.07;
      ctx.fillStyle = K.rad(ctx, x, y, r, [[0, 'rgba(210,240,255,' + (0.5 * pulse.a).toFixed(3) + ')'], [1, 'rgba(120,190,255,0)']]);
      ctx.fillRect(x - r, y - r, 2 * r, 2 * r);
    }
    // the freshest light at the point is hotter
    if (hot > 0.002) {
      const x = end.p[0] * size, y = end.p[1] * size, r = size * 0.06;
      ctx.fillStyle = K.rad(ctx, x, y, r, [[0, 'rgba(255,255,255,' + (0.9 * hot).toFixed(3) + ')'], [0.4, 'rgba(140,200,255,' + (0.35 * hot).toFixed(3) + ')'], [1, 'rgba(80,150,255,0)']]);
      ctx.fillRect(x - r, y - r, 2 * r, 2 * r);
    }
    ctx.restore();
  }

  MarrowIcons.register({
    letter: 'R',
    id: 'nib',
    name: 'Nib',
    meaning: 'It writes with you: a fine instrument that turns intent into a clean, living line.',
    story: 'A polished platinum fountain-pen nib, engraved and slit like the real thing, has just drawn a single flowing line, and the line is light. Writing is the oldest craft of putting thought into form; code is its newest. Platinum, midnight ink, a line of blue-white light.',
    swatch: ['#0b1530', '#dfe4eb', '#7fc8ff'],
    launch: { ms: 1150, note: 'The nib glides in, touches the page and writes: the line of light flows out behind its point in one smooth stroke and the nib comes to rest at the end of it.' },
    idle: { ms: 5600, note: 'A soft pulse of light travels along the written line toward the nib, and a glint slides down the metal.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const px = Math.round(size * s0);
      const L = t.launch;
      const c = band === 'tiny' || band === 'small' ? C_SMALL : C_BIG;
      const lay = (key, fn) => K.cache('R-nib|' + key + '|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); fn(o); });
      const tip = (u) => { const p = pointAt(c, u).p; return [p[0] * size, p[1] * size]; };
      // the pen, drawn once into a layer twice the icon's size with its point at the centre, then placed and turned
      // the pen, drawn once upright into a narrow layer with its point near the bottom, then placed and turned
      const LW = 0.56, LH = 1.12, TX = 0.28, TY = 1.04;
      const pen = K.cache('R-nib|pen|' + size + '|' + px, Math.ceil(px * LW), Math.ceil(px * LH), (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); o.translate(size * TX, size * TY); nib(o, size, band, 0); });
      const placePen = (X, Y) => { ctx.save(); ctx.translate(X, Y); ctx.rotate(ROT); ctx.drawImage(pen, -size * TX, -size * TY, Math.ceil(px * LW) * size / px, Math.ceil(px * LH) * size / px); ctx.restore(); };
      ctx.save();
      const { x, y, w, h } = K.box(size);
      if (L >= 1) {
        ctx.drawImage(lay('plate', (o) => plate(o, size, band, theme)), 0, 0, size, size);
        ctx.save(); ctx.beginPath(); K.squircle(ctx, x, y, w, h, w * 0.235); ctx.clip();
        let pulse = null, sh = 0;
        if (t.idle > 0) {
          const ph = K.loop(t.idle, 5600), u = K.seg(ph, 0.08, 0.6), v = K.seg(ph, 0.45, 0.9);
          pulse = { pos: K.ease.inOut(u), a: Math.sin(Math.PI * u) };
          sh = { pos: K.ease.inOut(v), a: Math.sin(Math.PI * v) };
        }
        light(ctx, size, band, 1, 0, pulse);
        const T = tip(1);
        placePen(T[0], T[1]);
        if (sh && sh.a > 0.002 && band !== 'tiny') {
          // a glint sliding down the metal, laid over the nib only
          const Ln = size * (band === 'small' ? 0.58 : 0.42);
          ctx.save(); ctx.translate(T[0], T[1]); ctx.rotate(ROT); ctx.scale(Ln, Ln);
          ctx.beginPath(); nibPath(ctx); ctx.clip();
          ctx.globalCompositeOperation = 'lighter';
          const yy = K.lerp(-1.05, 0.05, sh.pos);
          ctx.fillStyle = K.lin(ctx, 0, yy - 0.12, 0, yy + 0.12, [[0, 'rgba(255,255,255,0)'], [0.5, 'rgba(255,255,255,' + (0.35 * sh.a).toFixed(3) + ')'], [1, 'rgba(255,255,255,0)']]);
          ctx.fillRect(-0.4, -1.05, 0.8, 1.1);
          ctx.restore();
        }
        ctx.restore(); ctx.restore(); return;
      }
      const aP = K.ease.sine(K.seg(L, 0, 0.3)), eP = K.ease.mac(K.seg(L, 0, 0.4));
      ctx.save(); ctx.globalAlpha = aP;
      if (eP < 1) { const k = K.lerp(0.96, 1, eP); ctx.translate(size / 2, size / 2); ctx.scale(k, k); ctx.translate(-size / 2, -size / 2); }
      ctx.drawImage(lay('plate', (o) => plate(o, size, band, theme)), 0, 0, size, size);
      ctx.restore();
      ctx.beginPath(); K.squircle(ctx, x, y, w, h, w * 0.235); ctx.clip();
      // the nib glides in from the upper right to the start of the line, then writes it
      const arrive = K.ease.mac(K.seg(L, 0.06, 0.34));
      const write = K.ease.inOut(K.seg(L, 0.3, 0.95));
      const hot = 1 - K.ease.sine(K.seg(L, 0.75, 1));
      light(ctx, size, band, write, write > 0 ? hot : 0, null);
      const T0 = tip(0), T1 = tip(Math.max(0.0001, write));
      const from = [T0[0] + size * 0.3, T0[1] - size * 0.32];
      const nx = write > 0 ? T1[0] : K.lerp(from[0], T0[0], arrive), ny = write > 0 ? T1[1] : K.lerp(from[1], T0[1], arrive);
      ctx.save(); ctx.globalAlpha = K.ease.sine(K.seg(L, 0.06, 0.24));
      placePen(nx, ny);
      ctx.restore();
      ctx.restore();
    },
  });
})();
