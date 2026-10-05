// O Nautilus: a nacre spiral of growing chambers on warm linen.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;

  const B = Math.log(3) / TAU;          // each whorl is three times the last
  const TMAX = 0, TMIN = -5.2 * Math.PI;  // about two and a half turns, aperture at angle TMAX + ROT
  const ROT = 0.35;                     // turns the shell so the aperture opens to the lower right
  const r = (th) => Math.exp(B * (th - TMAX));
  const at = (th, rr) => [Math.cos(th + ROT) * rr, Math.sin(th + ROT) * rr];

  // the outer outline of the shell in unit space (outer radius 1 at the aperture)
  function outline(tEnd) {
    tEnd = tEnd == null ? TMAX : tEnd;
    const pts = [], N = 120;
    for (let i = 0; i <= N; i++) { const th = tEnd - TAU + (i / N) * TAU; pts.push(at(th, r(th))); }
    return pts;
  }
  // fit: centre the shell's bounding box in the plate
  const FIT = (() => {
    const p = outline(); let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
    for (const [x, y] of p) { x0 = Math.min(x0, x); y0 = Math.min(y0, y); x1 = Math.max(x1, x); y1 = Math.max(y1, y); }
    return { cx: (x0 + x1) / 2, cy: (y0 + y1) / 2, w: x1 - x0, h: y1 - y0 };
  })();
  const DELTA = TAU / 11;               // chambers per whorl
  const SEPTA = []; for (let th = TMAX - 1.55; th - TAU > TMIN + 0.4; th -= DELTA * (0.9 + 0.1 * Math.exp(B * th))) SEPTA.push(th);

  function geo(size, band) {
    const fitW = size * (band === 'tiny' ? 0.86 : band === 'small' ? 0.8 : 0.66);
    const S = fitW / Math.max(FIT.w, FIT.h);
    const ox = size / 2 - FIT.cx * S + (band === 'tiny' || band === 'small' ? 0 : size * 0.005), oy = size / 2 - FIT.cy * S;
    return { S, ox, oy, P: (th, rr) => { const [x, y] = at(th, rr); return [ox + x * S, oy + y * S]; } };
  }

  function pathPts(ctx, pts, g, close) {
    pts.forEach(([x, y], i) => { const X = g.ox + x * g.S, Y = g.oy + y * g.S; if (i) ctx.lineTo(X, Y); else ctx.moveTo(X, Y); });
    if (close) ctx.closePath();
  }
  function silhouette(ctx, g, tEnd) { ctx.beginPath(); pathPts(ctx, outline(tEnd), g, true); }

  function septumPts(th) {
    // from the inner whorl to the outer wall, bowed back toward the older chambers
    const a = r(th - TAU), b = r(th), out = [];
    for (let i = 0; i <= 10; i++) {
      const u = i / 10, rr = K.lerp(a, b, u), bow = Math.sin(Math.PI * u) * DELTA * 0.45;
      out.push(at(th - bow, rr));
    }
    return out;
  }
  function chamberPts(t0, t1) {
    // t1 < t0: back septum at t1, front septum at t0
    const pts = [], N = 10;
    for (let i = 0; i <= N; i++) { const th = K.lerp(t1, t0, i / N); pts.push(at(th, r(th))); }
    pts.push(...septumPts(t0).reverse());
    for (let i = N; i >= 0; i--) { const th = K.lerp(t1, t0, i / N); pts.push(at(th, r(th - TAU))); }
    pts.push(...septumPts(t1));
    return pts;
  }

  function plate(o, size, band, theme) {
    const { x, y, w, h } = K.box(size), rad = w * 0.235;
    if (band === 'medium' || band === 'large') {
      K.blur(o, size * 0.025, (q) => { q.fillStyle = theme === 'light' ? 'rgba(80,55,30,0.32)' : 'rgba(0,0,0,0.5)'; q.beginPath(); K.squircle(q, x + w * 0.02, y + h * 0.035, w * 0.96, h * 0.96, rad); q.fill(); });
    }
    o.fillStyle = K.lin(o, x, y, x + w, y + h, [[0, '#f6ede0'], [0.55, '#ead9c2'], [1, '#d8c1a2']]);
    o.beginPath(); K.squircle(o, x, y, w, h, rad); o.fill();
    if (band === 'tiny') return;
    o.save(); o.beginPath(); K.squircle(o, x, y, w, h, rad); o.clip();
    if (band === 'large') {
      // linen: a faint woven grain
      const rr = K.rng(5);
      o.globalAlpha = 0.05;
      for (let i = 0; i < 260; i++) { o.fillStyle = rr() < 0.5 ? '#8a6a48' : '#ffffff'; o.fillRect(x + rr() * w, y + rr() * h, size * 0.002, size * (0.006 + rr() * 0.01)); }
      o.globalAlpha = 1;
    }
    o.fillStyle = K.rad(o, x + w * 0.3, y + h * 0.2, w * 0.8, [[0, 'rgba(255,255,255,0.45)'], [1, 'rgba(255,255,255,0)']]);
    o.fillRect(x, y, w, h);
    o.lineWidth = Math.max(1, size * 0.007);
    o.strokeStyle = K.lin(o, 0, y, 0, y + h, [[0, 'rgba(255,255,255,0.9)'], [0.4, 'rgba(255,255,255,0.2)'], [1, 'rgba(150,110,70,0.25)']]);
    o.beginPath(); K.squircle(o, x + o.lineWidth / 2, y + o.lineWidth / 2, w - o.lineWidth, h - o.lineWidth, rad - o.lineWidth / 2); o.stroke();
    o.restore();
    if (theme === 'light' && band !== 'small') { o.lineWidth = Math.max(1, size * 0.003); o.strokeStyle = 'rgba(110,80,50,0.28)'; o.beginPath(); K.squircle(o, x, y, w, h, rad); o.stroke(); }
  }

  // the spiral wall as one tapering stroke, drawn up to tEnd
  function wall(ctx, size, band, g, tEnd, col) {
    const w0 = band === 'tiny' ? 1.5 : band === 'small' ? Math.max(1.5, size * 0.05) : band === 'hl' ? size * 0.005 : size * 0.016;
    const N = band === 'tiny' || band === 'small' ? 60 : 220;
    ctx.save(); ctx.lineCap = 'round'; ctx.strokeStyle = col;
    let prev = null;
    const t0 = band === 'tiny' ? TMIN + 1.7 * Math.PI : band === 'small' ? TMIN + 0.9 * Math.PI : TMIN;
    for (let i = 0; i <= N; i++) {
      const th = K.lerp(t0, TMAX, i / N); if (th > tEnd) break;
      const p = g.P(th, r(th));
      if (prev) {
        ctx.lineWidth = band === 'tiny' ? w0 : Math.max(band === 'small' ? 1.2 : 0.6, w0 * Math.pow(r(th), 0.55));
        ctx.beginPath(); ctx.moveTo(prev[0], prev[1]); ctx.lineTo(p[0], p[1]); ctx.stroke();
      }
      prev = p;
    }
    ctx.restore();
  }

  function shadow(ctx, size, g) {
    K.blur(ctx, size * 0.02, (q) => { q.fillStyle = 'rgba(90,60,30,0.35)'; q.save(); q.translate(size * 0.006, size * 0.018); silhouette(q, g, TMAX); q.fill(); q.restore(); });
  }

  // the shell body: nacre chambers, septa and the open body chamber; k(i) gives each chamber's reveal 0..1
  function shell(ctx, size, band, g, reveal) {
    const big = band === 'medium' || band === 'large';
    const tiny = band === 'tiny';
    if (big && !reveal.noShadow) shadow(ctx, size, g);
    // the pearl body
    ctx.fillStyle = tiny ? '#fbf6ef' : K.lin(ctx, g.ox - g.S, g.oy - g.S, g.ox + g.S, g.oy + g.S, [[0, '#fffdf9'], [0.5, '#f6eee5'], [1, '#e7d8c8']]);
    silhouette(ctx, g, reveal.tEnd); ctx.fill();
    if (!tiny) {
      ctx.save(); silhouette(ctx, g, reveal.tEnd); ctx.clip();
      // body chamber: the hollow, its pearly inside a shade deeper
      const b0 = SEPTA[0];
      if (reveal.tEnd > b0) {
        ctx.beginPath(); pathPts(ctx, chamberPts(Math.min(TMAX, reveal.tEnd), b0).slice(0, -11), g, true);
        ctx.fillStyle = K.rad(ctx, g.ox, g.oy, g.S * 1.05, [[0.25, '#d7bea6'], [0.55, '#e9d6c3'], [0.85, '#f5e9dc'], [1, '#fbf3ea']]);
        ctx.fill();
        if (big) {
          // the hollow: the previous whorl throws a soft shadow into it, the far wall shines
          ctx.save(); ctx.beginPath(); pathPts(ctx, chamberPts(TMAX, b0).slice(0, -11), g, true); ctx.clip();
          const inner = [], outer = [];
          for (let i = 0; i <= 30; i++) { const th = K.lerp(b0 - 0.2, TMAX + 0.05, i / 30); inner.push(at(th, r(th - TAU))); outer.push(at(th, r(th) * 0.965)); }
          K.blur(ctx, size * 0.018, (q) => { q.strokeStyle = 'rgba(120,80,50,0.45)'; q.lineWidth = size * 0.03; q.beginPath(); pathPts(q, inner, g, false); q.stroke(); });
          K.blur(ctx, size * 0.008, (q) => { q.strokeStyle = 'rgba(255,250,244,0.9)'; q.lineWidth = size * 0.012; q.beginPath(); pathPts(q, outer, g, false); q.stroke(); });
          ctx.restore();
        }
      }
      // the chambers, each lit from its younger wall and shaded into the older one
      SEPTA.forEach((t0, i) => {
        const t1 = SEPTA[i + 1] == null ? TMIN + TAU + 0.4 : SEPTA[i + 1];
        const a = reveal.k(t1); if (a <= 0) return;
        const pts = chamberPts(t0, t1);
        const m0 = g.P(t0, (r(t0) + r(t0 - TAU)) / 2), m1 = g.P(t1, (r(t1) + r(t1 - TAU)) / 2);
        ctx.globalAlpha = a;
        ctx.beginPath(); pathPts(ctx, pts, g, true);
        ctx.fillStyle = K.lin(ctx, m1[0], m1[1], m0[0], m0[1], [[0, '#fbf5ee'], [0.6, '#f1e5d8'], [1, big ? '#d9c4ae' : '#e5d4c2']]);
        ctx.fill();
        ctx.globalAlpha = 1;
      });
      // septa: thin nacre walls with a bright lip
      if (band !== 'small') {
        SEPTA.forEach((th) => {
          const a = reveal.k(th); if (a <= 0) return;
          const rr = r(th);
          ctx.globalAlpha = a;
          ctx.lineCap = 'round';
          ctx.strokeStyle = '#b89474'; ctx.lineWidth = Math.max(0.6, size * 0.011 * Math.pow(rr, 0.6));
          ctx.beginPath(); pathPts(ctx, septumPts(th), g, false); ctx.stroke();
          ctx.save(); ctx.translate(-ctx.lineWidth * 0.3, -ctx.lineWidth * 0.3);
          ctx.strokeStyle = 'rgba(255,255,255,0.9)'; ctx.lineWidth *= 0.4;
          ctx.beginPath(); pathPts(ctx, septumPts(th), g, false); ctx.stroke(); ctx.restore();
          if (band === 'large') {
            // the siphuncle: a small bead where the tube passes through each wall
            const p = g.P(th - DELTA * 0.18, K.lerp(r(th - TAU), rr, 0.42));
            ctx.fillStyle = '#c8a888'; ctx.beginPath(); K.circle(ctx, p[0], p[1], size * 0.006 * Math.pow(rr, 0.5)); ctx.fill();
          }
          ctx.globalAlpha = 1;
        });
      }
      ctx.restore();
    }
    // the outer wall, caramel where the shell is thick
    wall(ctx, size, band, g, reveal.tEnd, tiny ? '#9c6a42' : K.lin(ctx, g.ox - g.S, g.oy - g.S, g.ox + g.S, g.oy + g.S, [[0, '#c49064'], [0.5, '#a4714a'], [1, '#7e5232']]));
    if (big) {
      // a pearly highlight running along the inside of the wall
      ctx.save(); ctx.translate(-size * 0.0025, -size * 0.003);
      ctx.globalAlpha = 0.7; wall(ctx, size, 'hl', g, reveal.tEnd, 'rgba(255,246,232,0.55)'); ctx.restore();
    }
    // the aperture lip
    const lip = K.clamp((reveal.tEnd - (TMAX - 0.35)) / 0.35, 0, 1);
    if (lip > 0 && !tiny) {
      const a = g.P(TMAX, r(TMAX)), b = g.P(TMAX, r(TMAX - TAU));
      ctx.save(); ctx.globalAlpha *= lip; ctx.lineCap = 'round'; ctx.strokeStyle = '#a8774f'; ctx.lineWidth = band === 'small' ? 1.4 : size * 0.014;
      ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); ctx.restore();
    }
  }

  // nacre iridescence: a slow conic wash of pastel colour over the pearl, turned by rot
  function iris(ctx, size, band, g, rot, strength, tEnd) {
    if (band === 'tiny' || strength <= 0) return;
    ctx.save(); silhouette(ctx, g, tEnd); ctx.clip();
    const cg = ctx.createConicGradient(rot, g.ox, g.oy);
    [[0, '#ffc9dc'], [0.18, '#c9f1ff'], [0.36, '#d8ffe4'], [0.52, '#fff0c4'], [0.7, '#e4d2ff'], [0.86, '#ffd2e6'], [1, '#ffc9dc']].forEach(([f, c]) => cg.addColorStop(f, c));
    ctx.globalCompositeOperation = 'multiply';
    ctx.globalAlpha = (band === 'small' ? 0.3 : 0.36) * strength;
    ctx.fillStyle = cg;
    ctx.fillRect(size / 2 - FIT.w * g.S * 0.52, size / 2 - FIT.h * g.S * 0.52, FIT.w * g.S * 1.04, FIT.h * g.S * 1.04);
    ctx.globalCompositeOperation = 'source-over';
    ctx.globalAlpha = 1;
    ctx.restore();
  }

  MarrowIcons.register({
    letter: 'O',
    id: 'nautilus',
    name: 'Nautilus',
    meaning: 'Patient, iterative craft: every pass builds a larger, sound chamber on the last, loop by loop.',
    story: 'The cut face of a nautilus shell on warm linen: a pearl spiral divided into chambers that grow by the same ratio each turn, the oldest proof that steady iteration makes something beautiful. Pearl, nacre pastels and a caramel wall.',
    swatch: ['#ead9c2', '#fbf6ef', '#a8774f'],
    launch: { ms: 1150, note: 'The linen plate fades up and the shell grows from its centre outward, chamber after chamber, until the last turn opens at the lip.' },
    idle: { ms: 7200, note: 'The nacre slowly turns its colours like pearl under a moving light, and a soft sheen drifts across the shell.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const g = geo(size, band);
      const L = t.launch;
      const px = Math.round(size * s0);
      const lay = (key, fn) => { const c = K.cache('O-nautilus|' + key + '|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); fn(o); }); ctx.drawImage(c, 0, 0, size, size); };
      ctx.save();
      if (L >= 1) {
        // the still is the launch's last frame: the same three layers, so the hand-over is exact
        lay('plate', (o) => plate(o, size, band, theme));
        if (band === 'medium' || band === 'large') lay('shadow', (o) => shadow(o, size, g));
        lay('shell', (o) => shell(o, size, band, g, { tEnd: TMAX, k: () => 1, noShadow: true }));
        let rot = 0, sheen = 0;
        if (t.idle > 0) { const ph = K.loop(t.idle, 7200); rot = 0.5 * Math.sin(TAU * ph); sheen = ph; }
        iris(ctx, size, band, g, rot, 1, TMAX);
        if (sheen > 0 && band !== 'tiny') {
          const u = K.seg(sheen, 0.15, 0.75), env = Math.pow(Math.sin(Math.PI * u), 2);
          if (env > 0.001) {
            ctx.save(); silhouette(ctx, g, TMAX); ctx.clip();
            const x = K.lerp(-0.2, 1.2, K.ease.inOut(u)) * size;
            ctx.globalAlpha = 0.5 * env;
            ctx.fillStyle = K.lin(ctx, x - size * 0.22, 0, x + size * 0.22, size * 0.3, [[0, 'rgba(255,255,255,0)'], [0.5, 'rgba(255,255,255,0.85)'], [1, 'rgba(255,255,255,0)']]);
            ctx.fillRect(size / 2 - FIT.w * g.S * 0.52, size / 2 - FIT.h * g.S * 0.52, FIT.w * g.S * 1.04, FIT.h * g.S * 1.04);
            ctx.restore();
          }
        }
        ctx.restore(); return;
      }
      const aP = K.ease.sine(K.seg(L, 0, 0.3)), eP = K.ease.mac(K.seg(L, 0, 0.4));
      ctx.globalAlpha = aP;
      if (eP < 1) { const k = K.lerp(0.96, 1, eP); ctx.translate(size / 2, size / 2); ctx.scale(k, k); ctx.translate(-size / 2, -size / 2); }
      lay('plate', (o) => plate(o, size, band, theme));
      ctx.globalAlpha = 1;
      // growth: the spiral's end angle runs outward along an eased curve, sweeping the shell into being
      const grow = K.ease.inOut(K.seg(L, 0.12, 0.92));
      const tEnd = K.lerp(TMIN + TAU + 0.3, TMAX, grow);
      const aS = K.ease.sine(K.seg(L, 0.1, 0.3));
      if (band === 'medium' || band === 'large') {
        const openS = 1.22 + 3 * K.ease.inOut(K.seg(L, 0.9, 1));
        ctx.save(); ctx.globalAlpha = aS;
        ctx.translate(g.ox, g.oy); ctx.scale(openS, openS); ctx.translate(-g.ox, -g.oy);
        silhouette(ctx, g, tEnd + 0.15 + 2 * K.seg(L, 0.9, 1));
        ctx.translate(g.ox, g.oy); ctx.scale(1 / openS, 1 / openS); ctx.translate(-g.ox, -g.oy);
        ctx.clip(); lay('shadow', (o) => shadow(o, size, g)); ctx.restore();
      }
      // at the very end the sweep's clip opens right out, so the last frame is the still itself
      const open0 = 1.09 + 3 * K.ease.inOut(K.seg(L, 0.9, 1)) * 1.0;
      ctx.save(); ctx.globalAlpha = aS;
      ctx.translate(g.ox, g.oy); ctx.scale(open0, open0); ctx.translate(-g.ox, -g.oy);
      silhouette(ctx, g, tEnd + 0.15 + 2 * K.seg(L, 0.9, 1));
      ctx.translate(g.ox, g.oy); ctx.scale(1 / open0, 1 / open0); ctx.translate(-g.ox, -g.oy);
      ctx.clip();
      lay('shell', (o) => shell(o, size, band, g, { tEnd: TMAX, k: () => 1, noShadow: true }));
      ctx.restore();

      iris(ctx, size, band, g, 0, K.ease.sine(K.seg(L, 0.5, 1)), tEnd);
      ctx.restore();
    },
  });
})();
