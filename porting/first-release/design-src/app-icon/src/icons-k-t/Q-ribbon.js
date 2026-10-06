// Q Ribbon: one satin ribbon folded into an M on deep plum.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;

  // the ribbon's centre line (fractions of size): left tail, up, fold, down to the valley, fold, up, fold, down to the right tail
  const PATH = [[0.255, 0.79], [0.255, 0.235], [0.5, 0.585], [0.745, 0.235], [0.745, 0.79]];
  const PATH_SMALL = [[0.2, 0.86], [0.2, 0.16], [0.5, 0.6], [0.8, 0.16], [0.8, 0.86]];

  const sub = (a, b) => [a[0] - b[0], a[1] - b[1]], add = (a, b) => [a[0] + b[0], a[1] + b[1]], mul = (a, k) => [a[0] * k, a[1] * k];
  const norm = (a) => { const l = Math.hypot(a[0], a[1]) || 1; return [a[0] / l, a[1] / l]; };
  function lineHit(p, d, q, e) { // intersection of p + t d and q + s e
    const den = d[0] * e[1] - d[1] * e[0]; if (Math.abs(den) < 1e-9) return p;
    const t = ((q[0] - p[0]) * e[1] - (q[1] - p[1]) * e[0]) / den; return add(p, mul(d, t));
  }

  // each straight run of the ribbon as a quad cut by the fold lines at its ends; tails end in a V notch
  function segments(size, band) {
    const P = (band === 'tiny' || band === 'small' ? PATH_SMALL : PATH).map((p) => [p[0] * size, p[1] * size]);
    const W = size * (band === 'tiny' ? 0.19 : band === 'small' ? 0.17 : 0.135);
    const segs = [];
    for (let i = 0; i < P.length - 1; i++) {
      const a = P[i], b = P[i + 1], d = norm(sub(b, a)), n = [-d[1], d[0]];
      // fold line at a joint: along the bisector of the two directions' normals
      const cut = (j) => {
        if (j === 0 || j === P.length - 1) return null;
        const d0 = norm(sub(P[j], P[j - 1])), d1 = norm(sub(P[j + 1], P[j]));
        return norm(sub(d1, d0)).map((v) => v) && norm([d0[0] + d1[0], d0[1] + d1[1]]);
      };
      const ends = [];
      for (const [pt, j, sgn] of [[a, i, -1], [b, i + 1, 1]]) {
        const c = cut(j);
        const l = add(pt, mul(n, W / 2)), r = add(pt, mul(n, -W / 2));
        if (!c) {
          // a tail: a V notch cut into the end
          const notch = mul(d, sgn * -W * 0.42);
          ends.push({ l, r, mid: add(pt, notch), tail: true });
        } else {
          // the fold line runs perpendicular to the bisector of the turn
          const f = [-c[1], c[0]];
          ends.push({ l: lineHit(l, d, pt, f), r: lineHit(r, d, pt, f), tail: false });
        }
      }
      segs.push({ a, b, d, n, W, ends, back: i % 2 === 1, len: Math.hypot(b[0] - a[0], b[1] - a[1]) });
    }
    return segs;
  }

  function quad(ctx, s) {
    const [e0, e1] = s.ends;
    ctx.beginPath();
    ctx.moveTo(e0.l[0], e0.l[1]); ctx.lineTo(e1.l[0], e1.l[1]);
    if (e1.tail) ctx.lineTo(e1.mid[0], e1.mid[1]);
    ctx.lineTo(e1.r[0], e1.r[1]); ctx.lineTo(e0.r[0], e0.r[1]);
    if (e0.tail) ctx.lineTo(e0.mid[0], e0.mid[1]);
    ctx.closePath();
  }

  const FRONT = ['#ffd3cf', '#f59aa9', '#e0607f'];
  const BACK = ['#d24a7c', '#a72a62', '#6e1442'];

  function ribbonSeg(ctx, size, band, s, reveal) {
    // reveal: 0..1 of this run drawn from its start
    if (reveal <= 0) return;
    ctx.save();
    if (reveal < 1) {
      const far = add(s.a, mul(s.d, s.len * reveal));
      const n = s.n, big = size;
      ctx.beginPath();
      const p0 = add(add(s.a, mul(s.d, -big)), mul(n, big)), p1 = add(add(far, mul(n, big)), [0, 0]), p2 = add(far, mul(n, -big)), p3 = add(add(s.a, mul(s.d, -big)), mul(n, -big));
      ctx.moveTo(p0[0], p0[1]); ctx.lineTo(p1[0], p1[1]); ctx.lineTo(p2[0], p2[1]); ctx.lineTo(p3[0], p3[1]); ctx.closePath(); ctx.clip();
    }
    const C = s.back ? BACK : FRONT;
    if (band === 'tiny') { ctx.fillStyle = s.back ? '#b8336f' : '#f7a3b3'; quad(ctx, s); ctx.fill(); ctx.restore(); return; }
    // satin: across the width it curves a little (darker at both edges), along its length a soft sheen
    const l = add(s.a, mul(s.n, s.W / 2)), r = add(s.a, mul(s.n, -s.W / 2));
    ctx.fillStyle = K.lin(ctx, l[0], l[1], r[0], r[1], [[0, C[2]], [0.3, C[1]], [0.55, C[0]], [0.8, C[1]], [1, C[2]]]);
    quad(ctx, s); ctx.fill();
    // a lengthwise shade: the run darkens toward the fold that tucks under the next run
    ctx.fillStyle = K.lin(ctx, s.a[0], s.a[1], s.b[0], s.b[1], [[0, 'rgba(60,0,30,0)'], [0.75, 'rgba(60,0,30,0)'], [1, 'rgba(60,0,30,' + (s.back ? 0.18 : 0.1) + ')']]);
    quad(ctx, s); ctx.fill();
    // the crease where this run folds over the last one catches a line of light
    if (band !== 'small' && !s.ends[0].tail && reveal > 0.02) {
      const e0 = s.ends[0];
      ctx.strokeStyle = s.back ? 'rgba(255,190,215,0.55)' : 'rgba(255,250,248,0.85)'; ctx.lineWidth = Math.max(0.6, size * 0.004); ctx.lineCap = 'round';
      ctx.beginPath(); ctx.moveTo(e0.l[0], e0.l[1]); ctx.lineTo(e0.r[0], e0.r[1]); ctx.stroke();
    }
    // crisp edge highlight along the lit long edge
    if (band !== 'small') {
      const e0 = s.ends[0], e1 = s.ends[1];
      ctx.strokeStyle = s.back ? 'rgba(255,170,205,0.35)' : 'rgba(255,245,240,0.7)'; ctx.lineWidth = Math.max(0.6, size * 0.003);
      ctx.beginPath(); ctx.moveTo(e0.l[0], e0.l[1]); ctx.lineTo(e1.l[0], e1.l[1]); ctx.stroke();
    }
    ctx.restore();
  }

  // the shadow a run casts on the one beneath it, near their shared fold
  function foldShadow(ctx, size, segs, i, a) {
    if (i === 0 || a <= 0) return;
    const s = segs[i], prev = segs[i - 1];
    ctx.save(); quad(ctx, prev); ctx.clip();
    K.blur(ctx, size * 0.014, (q) => { q.globalAlpha = a; q.fillStyle = 'rgba(30,0,15,0.8)'; q.translate(size * 0.008, size * 0.014); quad(q, s); q.fill(); });
    ctx.restore();
  }

  function plate(o, size, band, theme) {
    const { x, y, w, h } = K.box(size), rad = w * 0.235;
    if (band === 'medium' || band === 'large') {
      K.blur(o, size * 0.025, (q) => { q.fillStyle = theme === 'light' ? 'rgba(40,0,30,0.38)' : 'rgba(0,0,0,0.55)'; q.beginPath(); K.squircle(q, x + w * 0.02, y + h * 0.035, w * 0.96, h * 0.96, rad); q.fill(); });
    }
    o.fillStyle = K.lin(o, x, y, x + w * 0.6, y + h, [[0, '#4a1640'], [0.55, '#2c0b28'], [1, '#170516']]);
    o.beginPath(); K.squircle(o, x, y, w, h, rad); o.fill();
    if (band === 'tiny') return;
    o.save(); o.beginPath(); K.squircle(o, x, y, w, h, rad); o.clip();
    o.fillStyle = K.rad(o, x + w * 0.25, y + h * 0.15, w * 0.7, [[0, 'rgba(255,140,190,0.22)'], [1, 'rgba(255,140,190,0)']]);
    o.fillRect(x, y, w, h);
    if (band === 'large') {
      // velvet: a very fine grain
      const r = K.rng(29); o.globalAlpha = 0.06;
      for (let i = 0; i < 1400; i++) { o.fillStyle = r() < 0.5 ? '#000' : '#ff9ccf'; o.fillRect(x + r() * w, y + r() * h, size * 0.002, size * 0.002); }
      o.globalAlpha = 1;
    }
    o.lineWidth = Math.max(1, size * 0.008);
    o.strokeStyle = K.lin(o, 0, y, 0, y + h, [[0, 'rgba(255,200,230,0.35)'], [0.35, 'rgba(255,200,230,0.05)'], [1, 'rgba(255,200,230,0.12)']]);
    o.beginPath(); K.squircle(o, x + o.lineWidth / 2, y + o.lineWidth / 2, w - o.lineWidth, h - o.lineWidth, rad - o.lineWidth / 2); o.stroke();
    o.restore();
    if (theme === 'light' && band !== 'small') { o.lineWidth = Math.max(1, size * 0.003); o.strokeStyle = 'rgba(40,0,30,0.35)'; o.beginPath(); K.squircle(o, x, y, w, h, rad); o.stroke(); }
  }

  function topClip(ctx, size, band) {
    const P = band === 'tiny' || band === 'small' ? PATH_SMALL : PATH;
    const W = size * (band === 'tiny' ? 0.19 : band === 'small' ? 0.17 : 0.135);
    const y = P[1][1] * size - W * 0.42;
    ctx.beginPath(); ctx.rect(0, y, size, size - y); ctx.clip();
  }

  function ribbon(ctx, size, band, p, px) {
    // p: 0..1 how much of the ribbon has unfurled, by length
    const segs = segments(size, band);
    ctx.save(); topClip(ctx, size, band);
    const total = segs.reduce((a, s) => a + s.len, 0);
    let acc = 0;
    const big = band === 'medium' || band === 'large';
    const lay = (key, fn) => K.cache('Q-ribbon|' + key + '|' + size + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); topClip(o, size, band); fn(o); });
    if (big) {
      // the ribbon's soft shadow on the velvet, settling in as the ribbon completes
      const a = K.ease.sine(K.seg(p, 0.55, 1));
      if (a > 0) {
        ctx.save(); ctx.globalAlpha *= a;
        ctx.drawImage(lay('shadow', (o) => K.blur(o, size * 0.02, (q) => { q.globalAlpha = 0.5; q.fillStyle = '#0a0008'; q.translate(size * 0.008, size * 0.02); for (const s of segs) { quad(q, s); q.fill(); } })), 0, 0, size, size);
        ctx.restore();
      }
    }
    segs.forEach((s, i) => {
      const rv = K.clamp((p * total - acc) / s.len, 0, 1);
      acc += s.len;
      if (big && i > 0 && rv > 0) {
        ctx.save(); ctx.globalAlpha *= Math.min(1, rv * 4);
        ctx.drawImage(lay('fold' + i, (o) => foldShadow(o, size, segs, i, 1)), 0, 0, size, size);
        ctx.restore();
      }
      ribbonSeg(ctx, size, band, s, rv);
    });
    ctx.restore();
    return { segs, total };
  }

  // a satin sheen running along the ribbon: position u along its length, strength a
  function sheen(ctx, size, band, u, a) {
    if (a <= 0.002 || band === 'tiny') return;
    const segs = segments(size, band), total = segs.reduce((q, s) => q + s.len, 0);
    let acc = 0;
    ctx.save(); topClip(ctx, size, band); ctx.globalCompositeOperation = 'lighter';
    for (const s of segs) {
      const at = u * total - acc, span = size * 0.22;
      acc += s.len;
      if (at < -span || at > s.len + span) continue;
      const c = add(s.a, mul(s.d, at));
      const g0 = add(c, mul(s.d, -span)), g1 = add(c, mul(s.d, span));
      ctx.fillStyle = K.lin(ctx, g0[0], g0[1], g1[0], g1[1], [[0, 'rgba(255,220,235,0)'], [0.5, 'rgba(255,225,238,' + (0.38 * a).toFixed(3) + ')'], [1, 'rgba(255,220,235,0)']]);
      quad(ctx, s); ctx.fill();
    }
    ctx.restore();
  }

  // build the launch layers ahead of time (when the still is drawn), so the first launch frame never stalls
  function prewarm(size, band, theme, px) {
    const c = document.createElement('canvas'); c.width = c.height = 1;
    const x = c.getContext('2d'); x.setTransform(px / size, 0, 0, px / size, 0, 0);
    K.cache('Q-ribbon|plate|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); plate(o, size, band, theme); });
    ribbon(x, size, band, 1, px);
  }

  MarrowIcons.register({
    letter: 'Q',
    id: 'ribbon',
    name: 'Ribbon',
    meaning: 'Marrow, signed by hand: one continuous ribbon, folded with care into the letter that names the app.',
    story: 'A single length of satin ribbon, rose on its face and deep magenta on its back, folded three times into an M, its tails cut in a V, resting on plum velvet. The folds show both sides of the cloth, so the mark has real thickness and light. Rose satin, magenta, plum.',
    swatch: ['#2c0b28', '#f59aa9', '#a72a62'],
    launch: { ms: 1100, note: 'The ribbon unfurls from its left tail, up, folding down into the valley, up again and down to the right tail, its shadow settling onto the velvet behind it.' },
    idle: { ms: 6400, note: 'A soft satin sheen travels the length of the ribbon once, the way light slides over silk when it moves.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const px = Math.round(size * s0);
      const L = t.launch;
      ctx.save();
      const aP = K.ease.sine(K.seg(L, 0, 0.3)), eP = K.ease.mac(K.seg(L, 0, 0.4));
      if (L >= 1) {
        ctx.drawImage(K.cache('Q-ribbon|still|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); plate(o, size, band, theme); ribbon(o, size, band, 1, px); }), 0, 0, size, size);
        if (!t.idle) prewarm(size, band, theme, px);
        if (t.idle > 0) { const ph = K.loop(t.idle, 6400), u = K.seg(ph, 0.1, 0.75); sheen(ctx, size, band, K.lerp(-0.15, 1.15, K.ease.inOut(u)), Math.sin(Math.PI * u)); }
        ctx.restore(); return;
      }
      ctx.save(); ctx.globalAlpha = aP;
      if (eP < 1) { const k = K.lerp(0.96, 1, eP); ctx.translate(size / 2, size / 2); ctx.scale(k, k); ctx.translate(-size / 2, -size / 2); }
      ctx.drawImage(K.cache('Q-ribbon|plate|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); plate(o, size, band, theme); }), 0, 0, size, size);
      ctx.restore();
      const p = K.ease.inOut(K.seg(L, 0.16, 0.9));
      ctx.globalAlpha = K.ease.sine(K.seg(L, 0.12, 0.3));
      ribbon(ctx, size, band, p, px);
      ctx.restore();
    },
  });
})();
