// P Duck: the programmer's rubber duck, sculpted and glossy, afloat on still water.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;
  const WL = 0.675; // the water line, as a fraction of size

  // the duck in fractions of size, facing left
  function bodyPath(c, s) {
    c.moveTo(0.3 * s, 0.6 * s);
    c.bezierCurveTo(0.33 * s, 0.5 * s, 0.5 * s, 0.47 * s, 0.63 * s, 0.5 * s);
    c.bezierCurveTo(0.7 * s, 0.505 * s, 0.75 * s, 0.46 * s, 0.79 * s, 0.4 * s);
    c.bezierCurveTo(0.83 * s, 0.38 * s, 0.86 * s, 0.42 * s, 0.85 * s, 0.5 * s);
    c.bezierCurveTo(0.845 * s, 0.6 * s, 0.82 * s, 0.73 * s, 0.6 * s, 0.76 * s);
    c.bezierCurveTo(0.45 * s, 0.78 * s, 0.31 * s, 0.74 * s, 0.3 * s, 0.6 * s);
    c.closePath();
  }
  const HEAD = [0.385, 0.355, 0.15];
  function beakPath(c, s) {
    c.moveTo(0.27 * s, 0.355 * s);
    c.bezierCurveTo(0.22 * s, 0.34 * s, 0.15 * s, 0.355 * s, 0.135 * s, 0.39 * s);
    c.bezierCurveTo(0.13 * s, 0.415 * s, 0.17 * s, 0.43 * s, 0.21 * s, 0.428 * s);
    c.bezierCurveTo(0.24 * s, 0.428 * s, 0.27 * s, 0.43 * s, 0.29 * s, 0.42 * s);
    c.closePath();
  }

  // the duck sprite, lit from the upper left, in icon units
  function duck(o, s, band) {
    const tiny = band === 'tiny', small = band === 'small';
    // body and head share one glossy yellow vinyl
    const fillY = (cx, cy, r) => K.rad(o, cx - r * 0.35, cy - r * 0.45, r * 1.35, [[0, '#fff3a6'], [0.35, '#ffd83a'], [0.75, '#f5b511'], [1, '#d98a06']]);
    if (tiny) {
      o.fillStyle = '#ffcf2e';
      o.beginPath(); bodyPath(o, s); o.fill();
      o.beginPath(); K.circle(o, HEAD[0] * s, HEAD[1] * s, HEAD[2] * s); o.fill();
      o.fillStyle = '#ff7a1a'; o.beginPath(); beakPath(o, s); o.fill();
      o.fillStyle = '#1a1206'; o.fillRect(Math.round(0.34 * s), Math.round(0.3 * s), 1.5, 1.5);
      return;
    }
    o.fillStyle = fillY(0.55 * s, 0.6 * s, 0.32 * s);
    o.beginPath(); bodyPath(o, s); o.fill();
    o.save(); o.beginPath(); bodyPath(o, s); o.clip();
    // the underside turns away from the light
    o.fillStyle = K.lin(o, 0, 0.5 * s, 0, 0.78 * s, [[0, 'rgba(200,110,0,0)'], [0.6, 'rgba(200,110,0,0.18)'], [1, 'rgba(160,80,0,0.4)']]);
    o.fillRect(0, 0, s, s);
    if (!small) {
      // the wing: a soft moulded bulge
      o.fillStyle = K.rad(o, 0.6 * s, 0.6 * s, 0.14 * s, [[0, 'rgba(255,248,200,0.45)'], [0.7, 'rgba(255,248,200,0.08)'], [1, 'rgba(255,248,200,0)']]);
      o.fillRect(0, 0, s, s);
      o.strokeStyle = 'rgba(205,125,0,0.35)'; o.lineWidth = s * 0.008; o.lineCap = 'round';
      o.beginPath(); o.moveTo(0.47 * s, 0.6 * s); o.bezierCurveTo(0.52 * s, 0.68 * s, 0.66 * s, 0.69 * s, 0.73 * s, 0.6 * s); o.stroke();
    }
    o.restore();
    // head, with a soft shadow where it meets the body
    if (!small) K.blur(o, s * 0.015, (q) => { q.fillStyle = 'rgba(190,100,0,0.35)'; q.beginPath(); K.circle(q, HEAD[0] * s + s * 0.01, HEAD[1] * s + s * 0.03, HEAD[2] * s); q.fill(); });
    o.fillStyle = fillY(HEAD[0] * s, HEAD[1] * s, HEAD[2] * s);
    o.beginPath(); K.circle(o, HEAD[0] * s, HEAD[1] * s, HEAD[2] * s); o.fill();
    // beak: orange, its upper half lit
    o.fillStyle = K.lin(o, 0, 0.34 * s, 0, 0.43 * s, [[0, '#ffa64a'], [0.55, '#ff7a1a'], [1, '#e2560a']]);
    o.beginPath(); beakPath(o, s); o.fill();
    if (!small) {
      o.strokeStyle = 'rgba(170,60,0,0.55)'; o.lineWidth = s * 0.006; o.lineCap = 'round';
      o.beginPath(); o.moveTo(0.15 * s, 0.403 * s); o.bezierCurveTo(0.19 * s, 0.4 * s, 0.24 * s, 0.398 * s, 0.275 * s, 0.39 * s); o.stroke();
      o.fillStyle = 'rgba(255,240,220,0.7)';
      o.beginPath(); o.ellipse(0.2 * s, 0.363 * s, 0.035 * s, 0.009 * s, -0.12, 0, TAU); o.fill();
    }
    // eye: deep glossy black with a window highlight
    const ex = 0.345 * s, ey = 0.31 * s, er = s * (small ? 0.03 : 0.024);
    o.fillStyle = '#1a1206'; o.beginPath(); o.ellipse(ex, ey, er * 0.85, er, 0, 0, TAU); o.fill();
    o.fillStyle = 'rgba(255,255,255,0.95)'; o.beginPath(); K.circle(o, ex - er * 0.3, ey - er * 0.4, er * 0.32); o.fill();
    // specular highlights on the glossy vinyl
    if (!small) {
      o.fillStyle = K.rad(o, 0.33 * s, 0.25 * s, 0.07 * s, [[0, 'rgba(255,255,255,0.9)'], [1, 'rgba(255,255,255,0)']]);
      o.beginPath(); o.ellipse(0.33 * s, 0.255 * s, 0.07 * s, 0.035 * s, -0.5, 0, TAU); o.fill();
      o.fillStyle = K.rad(o, 0.52 * s, 0.505 * s, 0.09 * s, [[0, 'rgba(255,255,255,0.7)'], [1, 'rgba(255,255,255,0)']]);
      o.beginPath(); o.ellipse(0.52 * s, 0.51 * s, 0.09 * s, 0.022 * s, -0.12, 0, TAU); o.fill();
    }
  }

  function water(o, size, band, theme) {
    const { x, y, w, h } = K.box(size), rad = w * 0.235;
    if (band === 'medium' || band === 'large') {
      K.blur(o, size * 0.025, (q) => { q.fillStyle = theme === 'light' ? 'rgba(0,40,60,0.32)' : 'rgba(0,0,0,0.5)'; q.beginPath(); K.squircle(q, x + w * 0.02, y + h * 0.035, w * 0.96, h * 0.96, rad); q.fill(); });
    }
    o.fillStyle = K.lin(o, 0, y, 0, y + h, [[0, '#a6ecf6'], [0.45, '#55c6e3'], [1, '#1683b4']]);
    o.beginPath(); K.squircle(o, x, y, w, h, rad); o.fill();
    if (band === 'tiny') return;
    o.save(); o.beginPath(); K.squircle(o, x, y, w, h, rad); o.clip();
    // the far water catches the sky; soft long swells nearer
    if (band !== 'small') {
      for (let i = 0; i < 7; i++) {
        const yy = y + h * (0.12 + i * 0.12), a = 0.16 - i * 0.015;
        o.fillStyle = K.lin(o, x, 0, x + w, 0, [[0, 'rgba(255,255,255,0)'], [0.3 + 0.05 * (i % 3), 'rgba(255,255,255,' + a.toFixed(3) + ')'], [0.8, 'rgba(255,255,255,0)']]);
        o.beginPath(); o.ellipse(x + w * (0.45 + 0.08 * Math.sin(i * 2.1)), yy, w * 0.55, h * (0.008 + i * 0.002), 0, 0, TAU); o.fill();
      }
    }
    o.lineWidth = Math.max(1, size * 0.008);
    o.strokeStyle = K.lin(o, 0, y, 0, y + h, [[0, 'rgba(255,255,255,0.7)'], [0.4, 'rgba(255,255,255,0.15)'], [1, 'rgba(255,255,255,0.2)']]);
    o.beginPath(); K.squircle(o, x + o.lineWidth / 2, y + o.lineWidth / 2, w - o.lineWidth, h - o.lineWidth, rad - o.lineWidth / 2); o.stroke();
    o.restore();
    if (theme === 'light' && band !== 'small') { o.lineWidth = Math.max(1, size * 0.003); o.strokeStyle = 'rgba(0,60,90,0.3)'; o.beginPath(); K.squircle(o, x, y, w, h, rad); o.stroke(); }
  }

  function rings(ctx, size, band, cx, cy, list) {
    if (band === 'tiny') return;
    ctx.save(); ctx.lineWidth = band === 'small' ? 1 : Math.max(0.8, size * 0.005);
    for (const [r, a] of list) {
      if (a <= 0.002) continue;
      ctx.strokeStyle = 'rgba(255,255,255,' + a.toFixed(3) + ')';
      ctx.beginPath(); ctx.ellipse(cx, cy, r * size, r * size * 0.13, 0, 0, TAU); ctx.stroke();
    }
    ctx.restore();
  }

  MarrowIcons.register({
    letter: 'P',
    id: 'duck',
    name: 'Duck',
    meaning: 'Every programmer explains a bug to a rubber duck; this one answers back.',
    story: 'The rubber duck of debugging lore, remade as a designed object: sculpted, glossy sunflower vinyl with an orange bill, riding calm aqua water with its reflection beneath it. A friendly, unmistakably coder idea, rendered with the care of a product photograph.',
    swatch: ['#55c6e3', '#ffd83a', '#ff7a1a'],
    launch: { ms: 1150, note: 'The duck drops into the water, dips, and bobs up to float, while rings of ripple spread from where it landed and fade.' },
    idle: { ms: 4400, note: 'It rides a slow, gentle swell: a small bob and tilt, the rings around it breathing.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const px = Math.round(size * s0);
      const L = t.launch;
      const wl = size * WL;
      const { x, y, w, h } = K.box(size);
      ctx.save();
      const aW = K.ease.sine(K.seg(L, 0, 0.3)), eW = K.ease.mac(K.seg(L, 0, 0.4));
      ctx.save(); ctx.globalAlpha = aW;
      if (eW < 1) { const k = K.lerp(0.96, 1, eW); ctx.translate(size / 2, size / 2); ctx.scale(k, k); ctx.translate(-size / 2, -size / 2); }
      ctx.drawImage(K.cache('P-duck|water|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); water(o, size, band, theme); }), 0, 0, size, size);
      ctx.restore();
      ctx.beginPath(); K.squircle(ctx, x, y, w, h, w * 0.235); ctx.clip();
      // the duck sprite (scaled a little so it fills the plate well at small sizes)
      const sp = K.cache('P-duck|sprite|' + size + '|' + px, px, px, (o) => {
        o.setTransform(px / size, 0, 0, px / size, 0, 0);
        if (band === 'tiny' || band === 'small') { o.translate(size * 0.5, size * 0.5); o.scale(1.08, 1.08); o.translate(-size * 0.5, -size * 0.52); }
        else { o.translate(size * 0.52, size * 0.62); o.scale(0.86, 0.86); o.translate(-size * 0.5, -size * 0.6); }
        duck(o, size, band);
      });
      // motion: launch drop and bob; idle swell
      let dy = 0, rot = 0, aD = 1;
      if (L < 1) {
        const u = K.seg(L, 0.2, 1);
        const fall = K.seg(L, 0.12, 0.38);
        aD = K.ease.sine(K.seg(L, 0.12, 0.3));
        if (L < 0.38) dy = -size * 0.28 * (1 - fall * fall);           // falling, accelerating
        else { const v = K.seg(L, 0.38, 1); dy = size * 0.06 * Math.sin(8.6 * v) * Math.exp(-4 * v) * (1 - v); }
        rot = 0.08 * (1 - u) * Math.sin(7 * u) * Math.exp(-2.5 * u);
      } else if (t.idle > 0) {
        const ph = K.loop(t.idle, 4400);
        dy = size * 0.007 * Math.sin(TAU * ph);
        rot = 0.018 * Math.sin(TAU * ph);
      }
      const pivX = size * 0.55, pivY = wl;
      const place = (c) => { c.translate(pivX, pivY + dy); c.rotate(rot); c.translate(-pivX, -pivY); };
      // reflection: the duck flipped about the water line, broken into wavering strips
      if (band !== 'tiny') {
        ctx.save(); ctx.globalAlpha = aD * (band === 'small' ? 0.35 : 0.42);
        ctx.beginPath(); ctx.rect(0, wl, size, size - wl); ctx.clip();
        const strips = band === 'small' ? 4 : 14, sh = (size * 0.3) / strips;
        for (let i = 0; i < strips; i++) {
          const yy = wl + i * sh, off = Math.sin(i * 1.7) * size * 0.007 * (i / strips);
          ctx.save(); ctx.beginPath(); ctx.rect(0, yy, size, sh + 0.5); ctx.clip();
          ctx.translate(off, 2 * wl); ctx.scale(1, -1); place(ctx);
          ctx.drawImage(sp, 0, 0, size, size);
          ctx.restore();
        }
        ctx.globalAlpha = 1;
        // the reflection fades with depth and takes the water's colour
        ctx.fillStyle = K.lin(ctx, 0, wl, 0, wl + size * 0.3, [[0, 'rgba(30,140,190,0.25)'], [1, 'rgba(22,131,180,0.85)']]);
        ctx.globalAlpha = aD; ctx.fillRect(0, wl, size, size - wl);
        ctx.restore();
      }
      // the part under the surface: seen through the water, tinted and a little shifted
      ctx.save(); ctx.globalAlpha = aD;
      ctx.beginPath(); ctx.rect(0, wl, size, size - wl); ctx.clip();
      ctx.save(); place(ctx); ctx.translate(size * 0.004, size * 0.006); ctx.drawImage(sp, 0, 0, size, size); ctx.restore();
      ctx.restore();
      if (band !== 'tiny') {
        // the water tints the submerged part
        ctx.save(); ctx.globalAlpha = aD * 0.38;
        ctx.beginPath(); ctx.rect(0, wl, size, size - wl); ctx.clip();
        ctx.globalCompositeOperation = 'multiply';
        ctx.save(); place(ctx); ctx.translate(size * 0.004, size * 0.006);
        ctx.drawImage(K.cache('P-duck|tint|' + size + '|' + px, px, px, (o) => {
          o.drawImage(sp, 0, 0); o.globalCompositeOperation = 'source-in'; o.fillStyle = '#7fd2ee'; o.fillRect(0, 0, px, px);
        }), 0, 0, size, size);
        ctx.restore(); ctx.restore();
      }
      // the part above the surface
      ctx.save(); ctx.globalAlpha = aD;
      ctx.beginPath(); ctx.rect(0, 0, size, wl); ctx.clip();
      place(ctx); ctx.drawImage(sp, 0, 0, size, size);
      ctx.restore();
      // the water line where it meets the duck, and the rings around it
      const cx = size * 0.56;
      const still = [[0.3, 0.22]];
      if (L < 1) {
        const v = K.seg(L, 0.36, 1);
        const list = [];
        for (let i = 0; i < 3; i++) {
          const q = K.seg(v, i * 0.14, 0.75 + i * 0.1), r = K.lerp(0.18, 0.5 + i * 0.06, K.ease.out(q));
          list.push([r, 0.55 * Math.sin(Math.PI * q) * (1 - i * 0.2)]);
        }
        const settle = K.ease.sine(K.seg(L, 0.65, 1));
        for (const [r, a] of still) list.push([r, a * settle]);
        if (band !== 'small') rings(ctx, size, band, cx, wl + size * 0.005, list.map(([r, a]) => [r, a * aD])); else rings(ctx, size, band, cx, wl, still.map(([r, a]) => [r, a * settle * aD]));
      } else {
        const ph = t.idle > 0 ? K.loop(t.idle, 4400) : 0;
        if (band !== 'small') rings(ctx, size, band, cx, wl + size * 0.005, still.map(([r, a], i) => [r * (1 + 0.025 * Math.sin(TAU * ph + i)) - 0.025 * Math.sin(i) * r, a]));
        else rings(ctx, size, band, cx, wl, still);
      }
      if (band !== 'tiny') {
        ctx.save(); ctx.globalAlpha = aD;
        ctx.strokeStyle = 'rgba(255,255,255,0.55)'; ctx.lineWidth = band === 'small' ? 1 : size * 0.006;
        ctx.beginPath(); ctx.ellipse(cx, wl + size * 0.004, size * 0.23, size * 0.018, 0, 0.2, Math.PI - 0.2); ctx.stroke();
        ctx.restore();
      }
      ctx.restore();
    },
  });
})();
