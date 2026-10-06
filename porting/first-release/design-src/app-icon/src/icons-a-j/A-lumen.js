// A Lumen: a bead of water resting on a satin-silver plate engraved with lines of code; the bead magnifies the
// line under it and turns it to light.
(function () {
  const K = IconKit, PI = Math.PI;
  // rows of code: [indent level, length], in units of the plate width
  const ROWS = [[0, 0.40], [1, 0.46], [2, 0.30], [2, 0.44], [1, 0.26], [1, 0.40], [0, 0.18], [0, 0.34]];
  const ACTIVE = 3;
  // the tokens of each row under the water: [share of the row, colour]
  const KW = '#7c4dff', ID = '#1f8bff', ST = '#ff9420', FN = '#00b3a4', PU = '#8a9ab3';
  const TOKS = [[[0.3, KW], [0.7, ID]], [[0.25, KW], [0.45, FN], [0.3, PU]], [[0.4, ID], [0.6, ST]], [[0.22, KW], [0.38, ID], [0.4, ST]], [[0.5, FN], [0.5, PU]], [[0.3, ID], [0.7, FN]], [[1, KW]], [[0.4, ID], [0.6, ST]]];

  function plateRect(size) {
    const b = K.box(size), band = K.band(size);
    return { x: b.x, y: b.y, w: b.w, h: b.h, r: b.w * (band === 'tiny' || band === 'small' ? 0.24 : 0.225) };
  }
  function geom(size) {
    const p = plateRect(size), band = K.band(size);
    const R = p.w * (band === 'tiny' ? 0.3 : band === 'small' ? 0.29 : 0.265);
    return { p, R, cx: p.x + p.w * 0.55, cy: p.y + p.h * (band === 'tiny' || band === 'small' ? 0.5 : 0.475) };
  }
  // the rows' geometry: [x0, y, x1] in icon units
  function rows(p, size) {
    const band = K.band(size);
    if (band === 'tiny') return [[0.2, 0.3, 0.62], [0.32, 0.5, 0.8], [0.2, 0.7, 0.55]].map((r) => [p.x + p.w * r[0], p.y + p.h * r[1], p.x + p.w * r[2]]);
    if (band === 'small') return [[0.2, 0.26, 0.6], [0.3, 0.42, 0.78], [0.3, 0.58, 0.7], [0.2, 0.74, 0.5]].map((r) => [p.x + p.w * r[0], p.y + p.h * r[1], p.x + p.w * r[2]]);
    const top = 0.205, step = 0.085, x0 = 0.17, ind = 0.075;
    return ROWS.map((r, i) => { const a = p.x + p.w * (x0 + r[0] * ind); return [a, p.y + p.h * (top + i * step), a + p.w * r[1]]; });
  }
  function lineW(p, size) { const band = K.band(size); return band === 'tiny' ? 1.5 : band === 'small' ? Math.max(1.5, p.w * 0.06) : p.w * 0.026; }

  function plate(c, size, theme) {
    const p = plateRect(size), { x, y, w, h, r } = p, band = K.band(size);
    if (band === 'medium' || band === 'large') {
      K.blur(c, size * 0.03, (o) => { o.fillStyle = theme === 'light' ? 'rgba(30,50,80,.24)' : 'rgba(0,0,0,.55)'; o.beginPath(); K.squircle(o, x + w * 0.03, y + h * 0.05, w * 0.94, h * 0.96, r); o.fill(); });
    }
    c.beginPath(); K.squircle(c, x, y, w, h, r);
    c.fillStyle = K.lin(c, x, y, x, y + h, [[0, '#fbfcfe'], [0.5, '#e8edf3'], [1, '#cdd5e1']]);
    c.fill();
    c.save(); c.beginPath(); K.squircle(c, x, y, w, h, r); c.clip();
    if (band === 'medium' || band === 'large') {
      // satin sheen and a cool sky reflection from above
      c.fillStyle = K.lin(c, x, y, x + w, y + h, [[0, 'rgba(255,255,255,0)'], [0.3, 'rgba(255,255,255,.6)'], [0.46, 'rgba(255,255,255,0)']]);
      c.fillRect(x, y, w, h);
      c.fillStyle = K.rad(c, x + w * 0.5, y - h * 0.2, w, [[0, 'rgba(200,228,255,.5)'], [1, 'rgba(200,228,255,0)']]);
      c.fillRect(x, y, w, h);
      if (band === 'large') { const rnd = K.rng(11); for (let i = 0; i < 120; i++) { c.fillStyle = 'rgba(110,125,150,' + (0.012 + rnd() * 0.02).toFixed(3) + ')'; c.fillRect(x, y + rnd() * h, w, 0.3 + rnd() * 0.5); } }
    }
    // the engraved rows: a dark groove with a light lip under it
    const lw = lineW(p, size);
    c.lineCap = 'round';
    rows(p, size).forEach((rw) => {
      if (band === 'medium' || band === 'large') { c.strokeStyle = 'rgba(255,255,255,.9)'; c.lineWidth = lw; c.beginPath(); c.moveTo(rw[0], rw[1] + lw * 0.35); c.lineTo(rw[2], rw[1] + lw * 0.35); c.stroke(); }
      c.strokeStyle = band === 'tiny' ? '#9aa6b8' : 'rgba(96,110,135,.62)'; c.lineWidth = lw;
      c.beginPath(); c.moveTo(rw[0], rw[1]); c.lineTo(rw[2], rw[1]); c.stroke();
    });
    c.restore();
    // the rim
    if (band === 'medium' || band === 'large') {
      c.save(); c.beginPath(); K.squircle(c, x, y, w, h, r); c.clip();
      const lw2 = Math.max(1, size * 0.008);
      c.lineWidth = lw2; c.strokeStyle = K.lin(c, x, y, x, y + h, [[0, '#ffffff'], [0.5, 'rgba(255,255,255,.15)'], [1, 'rgba(100,115,140,.55)']]);
      c.beginPath(); K.squircle(c, x + lw2 / 2, y + lw2 / 2, w - lw2, h - lw2, r); c.stroke(); c.restore();
    }
    const lw3 = band === 'tiny' || band === 'small' ? 1 : Math.max(0.75, size * 0.004);
    c.lineWidth = lw3; c.strokeStyle = theme === 'light' ? 'rgba(40,55,80,.32)' : (band === 'tiny' || band === 'small' ? 'rgba(0,0,0,.3)' : 'rgba(255,255,255,.2)');
    c.beginPath(); K.squircle(c, x + lw3 / 2, y + lw3 / 2, w - lw3, h - lw3, r); c.stroke();
  }

  // the bead's shadow on the metal, with the bright caustic the bead focuses into it
  function shadow(c, size, g, k, br) {
    if (K.band(size) === 'tiny') return;
    const { cx, cy, R } = g;
    c.save(); c.globalAlpha = k;
    c.beginPath(); const p = plateRect(size); K.squircle(c, p.x, p.y, p.w, p.h, p.r); c.clip();
    K.blur(c, R * 0.14, (o) => { o.fillStyle = 'rgba(16,70,150,.30)'; o.beginPath(); o.ellipse(cx + R * 0.2, cy + R * 0.36, R * 1.04, R * 0.92, 0, 0, 2 * PI); o.fill(); });
    K.blur(c, R * 0.09, (o) => {
      o.fillStyle = K.rad(o, cx + R * 0.52, cy + R * 0.98, R * 0.5, [[0, 'rgba(255,255,255,.95)'], [0.4, 'rgba(160,232,255,.7)'], [1, 'rgba(120,210,255,0)']]);
      o.beginPath(); o.ellipse(cx + R * 0.5, cy + R * 0.98, R * (0.52 + 0.04 * br), R * (0.2 + 0.015 * br), -0.5, 0, 2 * PI); o.fill();
    });
    c.restore();
  }

  function bead(c, size, g, sx, sy, focus, sh) {
    const { cx, cy, R } = g, band = K.band(size), p = plateRect(size);
    const path = (o) => { o.beginPath(); o.ellipse(cx, cy, R * sx, R * sy, 0, 0, 2 * PI); };
    if (band === 'tiny' || band === 'small') {
      // clear water: a light centre, a deep rim; inside, two magnified tokens in colour
      path(c); c.fillStyle = K.rad(c, cx, cy, R, [[0, '#f4fbff'], [0.7, '#e2f1ff'], [0.88, '#8fb4dc'], [1, '#4f78aa']]); c.fill();
      // inside, two magnified code rows, left-aligned like code (one long blue row, one shorter amber one, indented)
      c.save(); path(c); c.clip(); c.lineCap = 'butt';
      const lw = band === 'tiny' ? 2 : Math.max(2, Math.round(R * 0.26));
      const y1 = K.px(cy - R * 0.22) + (lw % 2 ? 0.5 : 0), y2 = K.px(cy + R * 0.34) + (lw % 2 ? 0.5 : 0);
      c.lineWidth = lw;
      c.strokeStyle = '#1f7bff'; c.beginPath(); c.moveTo(cx - R, y1); c.lineTo(cx + R * 0.55, y1); c.stroke();
      c.strokeStyle = '#ff9420'; c.beginPath(); c.moveTo(cx - R * 0.4, y2); c.lineTo(cx + R * 0.3, y2); c.stroke();
      c.restore();
      if (band === 'small') { c.fillStyle = 'rgba(255,255,255,.95)'; c.beginPath(); c.ellipse(cx - R * 0.48, cy - R * 0.58, R * 0.2, R * 0.12, -0.6, 0, 2 * PI); c.fill(); }
      c.lineWidth = 1; c.strokeStyle = 'rgba(14,40,90,.75)'; path(c); c.stroke();
      return;
    }
    c.save(); path(c); c.clip();
    // what the bead sees: the plate, magnified 1.75x about its centre, a little lower (refraction)
    c.fillStyle = '#e6eef7'; c.fillRect(cx - R * 2, cy - R * 2, R * 4, R * 4);
    const M = 1.42;
    c.save();
    c.translate(cx, cy + R * 0.04); c.scale(M * sx, M * sy); c.translate(-cx, -cy);
    const lw = lineW(p, size), rs = rows(p, size);
    c.lineCap = 'round';
    // under the water the grey grooves read as code: each row splits into tokens in syntax colours
    K.blur(c, (1 - focus) * R * 0.1, (o) => {
      rs.forEach((rw, i) => {
        o.lineWidth = lw;
        const len = rw[2] - rw[0], toks = TOKS[i % TOKS.length];
        let a = rw[0];
        toks.forEach((tk) => {
          const b = a + len * tk[0];
          o.strokeStyle = K.mix('#5d6f8e', tk[1], focus);
          o.beginPath(); o.moveTo(a + lw * 0.2, rw[1]); o.lineTo(Math.max(a + lw * 0.2, b - lw * 0.9), rw[1]); o.stroke();
          a = b;
        });
      });
    });
    c.restore();
    // water: almost clear in the middle, deeper toward the rim (Fresnel), a dark refracted ring at the edge
    c.fillStyle = K.rad(c, cx, cy, R * Math.max(sx, sy), [[0, 'rgba(210,236,255,.06)'], [0.6, 'rgba(150,200,245,.14)'], [0.8, 'rgba(60,110,175,.26)'], [0.94, 'rgba(26,58,110,.62)'], [1, 'rgba(16,36,76,.85)']]);
    c.fillRect(cx - R * 2, cy - R * 2, R * 4, R * 4);
    // light focused through the lower belly
    K.blur(c, R * 0.08, (o) => {
      o.fillStyle = K.rad(o, cx + R * 0.15, cy + R * 0.72, R * 0.7, [[0, 'rgba(255,255,255,.95)'], [0.45, 'rgba(190,240,255,.55)'], [1, 'rgba(160,225,255,0)']]);
      o.beginPath(); o.ellipse(cx + R * 0.12, cy + R * 0.72, R * 0.6, R * 0.2, 0, 0, 2 * PI); o.fill();
    });
    // light passing through the lower rim
    K.blur(c, R * 0.04, (o) => { o.lineWidth = R * 0.07; o.strokeStyle = 'rgba(200,240,255,.8)'; o.lineCap = 'round'; o.beginPath(); o.arc(cx, cy, R * 0.9, 0.25 * PI, 0.8 * PI); o.stroke(); });
    // the sky window: a soft bright arc across the upper left
    K.blur(c, R * 0.05, (o) => {
      o.fillStyle = 'rgba(255,255,255,.82)';
      o.beginPath(); o.ellipse(cx - R * (0.36 - 0.03 * sh), cy - R * (0.5 + 0.02 * sh), R * 0.42, R * 0.16, -0.6, 0, 2 * PI); o.fill();
    });
    c.restore();
    // the hairline edge and the sun glint
    c.lineWidth = Math.max(0.6, size * 0.0028); c.strokeStyle = 'rgba(8,50,125,.6)'; path(c); c.stroke();
    K.glow(c, 'rgba(255,255,255,.95)', R * 0.1, (o) => { o.fillStyle = '#ffffff'; o.beginPath(); o.ellipse(cx - R * 0.58 + R * 0.03 * sh, cy - R * 0.42, R * 0.06, R * 0.045, -0.6, 0, 2 * PI); o.fill(); });
  }

  MarrowIcons.register({
    letter: 'A', id: 'lumen', name: 'Lumen',
    meaning: 'It brings your code into focus: calm, clear and exact.',
    story: 'A bead of water rests on a satin-silver plate engraved with lines of code. It magnifies the line under it and turns it to light, and throws a sky-blue caustic onto the metal. Clear water and brushed silver, in the app’s own glass language.',
    swatch: ['#e8edf3', '#1aa3ff', '#0b5cc2'],
    launch: { ms: 1100, note: 'The engraved plate rises in; a bead of water falls onto it, lands with a soft squash and settles, and the line beneath it sharpens into light.' },
    idle: { ms: 5600, note: 'The sky reflection drifts a hair across the bead and its caustic breathes on the metal.' },
    draw(ctx, size, theme, t) {
      const g = geom(size), band = K.band(size), L = t.launch;
      ctx.save();
      const pIn = K.ease.mac(K.seg(L, 0, 0.4));
      ctx.save();
      ctx.globalAlpha = pIn; ctx.translate(0, (1 - pIn) * size * 0.035);
      if (band === 'medium' || band === 'large') K.layer(ctx, 'A-plate-' + theme, size, (o) => plate(o, size, theme)); else plate(ctx, size, theme);
      ctx.restore();
      // the bead falls 0.14..0.52 (accelerating), squashes on landing, springs back to round
      const phase = K.loop(t.idle, 5600), w1 = Math.sin(2 * PI * phase), w2 = Math.sin(4 * PI * phase);
      let dy = 0, sx = 1, sy = 1, a = 1;
      if (L < 0.52) {
        const f = K.seg(L, 0.14, 0.52), ff = f * f;
        dy = -(1 - ff) * size * 0.6; sx = 0.84 + 0.1 * ff; sy = 1.16 - 0.08 * ff; a = K.ease.out(K.seg(L, 0.14, 0.3));
      } else {
        const sq = 1 - K.ease.spring(K.seg(L, 0.52, 1));
        sx = 1 + 0.14 * sq; sy = 1 - 0.16 * sq;
      }
      const focus = L >= 1 ? 1 : K.ease.inOut(K.seg(L, 0.55, 0.95));
      const ck = L >= 1 ? 1 : K.ease.out(K.seg(L, 0.5, 0.92));
      if (L >= 0.52) {
        const rp = K.seg(L, 0.52, 0.96);
        if (rp > 0 && rp < 1 && band !== 'tiny') {
          const p = plateRect(size);
          ctx.save(); ctx.beginPath(); K.squircle(ctx, p.x, p.y, p.w, p.h, p.r); ctx.clip();
          ctx.strokeStyle = 'rgba(70,160,255,' + (0.5 * (1 - rp)).toFixed(3) + ')'; ctx.lineWidth = Math.max(1, g.R * 0.06 * (1 - rp));
          const e = K.ease.out(rp);
          ctx.beginPath(); ctx.ellipse(g.cx, g.cy + g.R * 0.1, g.R * (1.05 + 0.9 * e), g.R * (1.0 + 0.8 * e), 0, 0, 2 * PI); ctx.stroke();
          ctx.restore();
        }
      }
      shadow(ctx, size, g, L < 0.52 ? 0 : ck, w2);
      ctx.globalAlpha = a;
      const base = g.cy + g.R;   // the bead sits on its base: squash keeps the base on the plate
      bead(ctx, size, { cx: g.cx, cy: base - g.R * sy + dy, R: g.R }, sx, sy, focus, w1);
      ctx.restore();
    },
  });
})();
