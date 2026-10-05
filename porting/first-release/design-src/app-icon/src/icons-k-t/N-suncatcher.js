// N Suncatcher: a cut crystal hanging in morning light, scattering small rainbows over a pale wall.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;

  // a hexagonal bipyramid: a short crown, a long pavilion, six girdle corners
  const TOP = 1.05, BOT = 1.75, SIDES = 6;
  function verts(yaw) {
    const v = [[0, -TOP, 0]];
    for (let i = 0; i < SIDES; i++) { const a = yaw + (i / SIDES) * TAU; v.push([Math.cos(a), 0, Math.sin(a)]); }
    v.push([0, BOT, 0]);
    // tip toward the viewer a little so the crown shows
    const p = -0.32, cp = Math.cos(p), sp = Math.sin(p);
    return v.map(([x, y, z]) => [x, y * cp - z * sp, y * sp + z * cp]);
  }
  function faces(v) {
    const f = [];
    for (let i = 0; i < SIDES; i++) { const a = 1 + i, b = 1 + ((i + 1) % SIDES); f.push({ q: [0, a, b], top: true, i }); f.push({ q: [a, SIDES + 1, b], top: false, i }); }
    for (const F of f) {
      const [A, B, C] = F.q.map((k) => v[k]);
      const ux = B[0] - A[0], uy = B[1] - A[1], uz = B[2] - A[2], wx = C[0] - A[0], wy = C[1] - A[1], wz = C[2] - A[2];
      let nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx; const l = Math.hypot(nx, ny, nz) || 1;
      nx /= l; ny /= l; nz /= l;
      // outward: away from the centre
      const cx = (A[0] + B[0] + C[0]) / 3, cy = (A[1] + B[1] + C[1]) / 3, cz = (A[2] + B[2] + C[2]) / 3;
      if (nx * cx + ny * cy + nz * cz < 0) { nx = -nx; ny = -ny; nz = -nz; }
      F.n = [nx, ny, nz];
    }
    return f;
  }

  const SPEC = ['#ff3b5c', '#ff8a2a', '#ffd84a', '#4ee08a', '#2fb6ff', '#6a5cff'];
  // where the rainbows land on the wall: dx, dy (fractions of size from the crystal), angle, length, delay
  // all thrown away from the window (upper left), lying at nearly the same slant, of different sizes
  const FLECKS = [
    [0.24, -0.02, -0.38, 1.25, 0.0], [0.31, 0.17, -0.3, 0.95, 0.14], [0.14, 0.3, -0.45, 1.1, 0.26],
    [0.3, -0.2, -0.33, 0.7, 0.36], [-0.04, 0.4, -0.4, 0.75, 0.46], [0.36, 0.34, -0.36, 0.6, 0.52],
  ];
  const FLECKS_SMALL = [[0.29, 0.02, -0.38, 1.3, 0], [0.18, 0.3, -0.4, 1.1, 0.2]];

  function wall(o, size, band, theme) {
    const { x, y, w, h } = K.box(size), rad = w * 0.235;
    const lite = theme === 'light';
    if (band === 'medium' || band === 'large') {
      K.blur(o, size * 0.025, (q) => { q.fillStyle = lite ? 'rgba(70,50,30,0.32)' : 'rgba(0,0,0,0.5)'; q.beginPath(); K.squircle(q, x + w * 0.02, y + h * 0.035, w * 0.96, h * 0.96, rad); q.fill(); });
    }
    o.fillStyle = K.lin(o, x, y, x + w * 0.5, y + h, [[0, '#fffaf2'], [0.6, '#f4ece1'], [1, '#e6dbcd']]);
    o.beginPath(); K.squircle(o, x, y, w, h, rad); o.fill();
    if (band === 'tiny') return;
    o.save(); o.beginPath(); K.squircle(o, x, y, w, h, rad); o.clip();
    // the window light falling across the wall, warm and slanted
    o.save(); o.translate(x + w * 0.2, y); o.rotate(-0.42);
    o.fillStyle = K.lin(o, 0, 0, w * 0.75, 0, [[0, 'rgba(255,236,200,0)'], [0.2, 'rgba(255,236,200,0.55)'], [0.8, 'rgba(255,236,200,0.5)'], [1, 'rgba(255,236,200,0)']]);
    o.fillRect(-w * 0.1, -h * 0.2, w * 0.75, h * 1.6); o.restore();
    o.fillStyle = K.lin(o, 0, y, 0, y + h, [[0, 'rgba(120,90,60,0)'], [0.7, 'rgba(120,90,60,0.05)'], [1, 'rgba(120,90,60,0.14)']]);
    o.fillRect(x, y, w, h);
    o.lineWidth = Math.max(1, size * 0.007);
    o.strokeStyle = K.lin(o, 0, y, 0, y + h, [[0, 'rgba(255,255,255,1)'], [0.4, 'rgba(255,255,255,0.3)'], [1, 'rgba(140,110,80,0.22)']]);
    o.beginPath(); K.squircle(o, x + o.lineWidth / 2, y + o.lineWidth / 2, w - o.lineWidth, h - o.lineWidth, rad - o.lineWidth / 2); o.stroke();
    o.restore();
    if (lite && band !== 'small') { o.lineWidth = Math.max(1, size * 0.003); o.strokeStyle = 'rgba(110,85,60,0.3)'; o.beginPath(); K.squircle(o, x, y, w, h, rad); o.stroke(); }
  }

  // one rainbow fleck as a sprite: a short soft band of spectrum
  function fleckSprite(o, W, H) {
    // a short rainbow: the colours run along its length, its edges soft
    const draw = (q) => {
      const g = q.createLinearGradient(W * 0.12, 0, W * 0.88, 0);
      SPEC.forEach((c, i) => g.addColorStop(i / (SPEC.length - 1), c));
      q.fillStyle = g;
      q.beginPath(); q.ellipse(W / 2, H / 2, W * 0.42, H * 0.2, 0, 0, TAU); q.fill();
    };
    K.blur(o, H * 0.06, draw);
    o.globalCompositeOperation = 'destination-in';
    o.fillStyle = K.lin(o, 0, 0, W, 0, [[0, 'rgba(0,0,0,0)'], [0.2, 'rgba(0,0,0,1)'], [0.8, 'rgba(0,0,0,1)'], [1, 'rgba(0,0,0,0)']]);
    o.fillRect(0, 0, W, H);
    o.globalCompositeOperation = 'source-over';
  }

  function flecks(ctx, size, band, s0, cx, cy, prog, ph) {
    const list = band === 'tiny' || band === 'small' ? FLECKS_SMALL : FLECKS;
    if (band === 'tiny') return;
    if (false) {
      list.forEach(([dx, dy], k) => {
        const u = K.ease.out(K.seg(prog, k * 0.2, 0.8 + k * 0.2)); if (u <= 0) return;
        const x = Math.round(cx + dx * size * u) - 2, y = Math.round(cy + dy * size * u) - 1;
        ctx.globalAlpha = u;
        ['#ff4d5e', '#ffd34a', '#3aa8ff'].forEach((c, i) => { ctx.fillStyle = c; ctx.fillRect(x + i * 1.5, y, 1.5, 3); });
        ctx.globalAlpha = 1;
      });
      return;
    }
    const sw = size * 0.17, sh = size * 0.06;
    const W = Math.max(8, Math.round(sw * s0)), H = Math.max(4, Math.round(sh * s0));
    const spr = K.cache('N-suncatcher|fleck|' + W + 'x' + H, W, H, (o) => fleckSprite(o, W, H));
    for (const [dx, dy, ang, len, delay] of list) {
      const u = K.ease.mac(K.seg(prog, delay * 0.6, 0.55 + delay * 0.6)); if (u <= 0) continue;
      // idle: the turning crystal walks each rainbow a little around it
      const wob = ph == null ? 0 : 0.09 * (Math.sin(TAU * ph + dx * 7) - Math.sin(dx * 7));
      const ca = Math.cos(wob), sa = Math.sin(wob);
      const ddx = dx * ca - dy * sa, ddy = dx * sa + dy * ca;
      const x = cx + ddx * size * u, y = cy + ddy * size * u;
      ctx.save(); ctx.translate(x, y); ctx.rotate(ang + wob * 0.5); ctx.scale(len * K.lerp(0.4, 1, u), K.lerp(0.6, 1, u));
      ctx.globalAlpha = (band === 'small' ? 0.95 : 0.78) * u * (ph == null ? 1 : 1 - 0.3 * (0.5 - 0.5 * Math.cos(TAU * ph)) * (0.5 + 0.5 * Math.sin(dy * 9 + dx * 5)));
      ctx.globalCompositeOperation = 'multiply';
      ctx.drawImage(spr, -sw / 2, -sh / 2, sw, sh);
      ctx.globalCompositeOperation = 'screen'; ctx.globalAlpha *= 0.35;
      ctx.drawImage(spr, -sw / 2, -sh / 2, sw, sh);
      ctx.restore();
    }
  }

  function crystal(ctx, size, band, cx, cy, r, yaw, glow) {
    const v = verts(yaw), F = faces(v);
    const P = (k) => [cx + v[k][0] * r, cy + v[k][1] * r];
    const L = [-0.55, -0.55, 0.63]; // light from the upper left, in front
    ctx.save(); ctx.lineJoin = 'round';
    if (band === 'tiny') {
      // a diamond in two halves: lit left, shaded right
      const t = [cx, Math.round(cy - r * 1.25)], b = [cx, Math.round(cy + r * 1.6)], lx = Math.round(cx - r), rx = Math.round(cx + r);
      ctx.fillStyle = '#aebfd6'; ctx.beginPath(); ctx.moveTo(t[0], t[1]); ctx.lineTo(lx, cy); ctx.lineTo(b[0], b[1]); ctx.closePath(); ctx.fill();
      ctx.fillStyle = K.lin(ctx, cx, t[1], cx + r, b[1], [[0, '#e2559a'], [0.5, '#5b63f0'], [1, '#1590e0']]);
      ctx.beginPath(); ctx.moveTo(t[0], t[1]); ctx.lineTo(rx, cy); ctx.lineTo(b[0], b[1]); ctx.closePath(); ctx.fill();
      ctx.restore(); return;
    }
    // the faces, sorted back to front
    const order = F.filter((f) => f.n[2] <= 0).concat(F.filter((f) => f.n[2] > 0)).map((f) => ({ f }));
    for (const { f } of order) {
      const pts = f.q.map(P);
      const front = f.n[2] > 0;
      const d = f.n[0] * L[0] + f.n[1] * L[1] + f.n[2] * L[2];
      let base;
      if (!front) base = 'rgba(160,185,215,0.22)';
      else {
        // crystal: strong contrast between faces that reflect the bright window and those that show the darker room
        const k = K.clamp(0.5 + 1.1 * d, 0, 1);
        base = K.mix('#4c6488', '#ffffff', k * k * (3 - 2 * k));
      }
      const minx = Math.min(...pts.map((p) => p[0])), maxx = Math.max(...pts.map((p) => p[0])), miny = Math.min(...pts.map((p) => p[1])), maxy = Math.max(...pts.map((p) => p[1]));
      ctx.beginPath(); ctx.moveTo(pts[0][0], pts[0][1]); ctx.lineTo(pts[1][0], pts[1][1]); ctx.lineTo(pts[2][0], pts[2][1]); ctx.closePath();
      if (front) {
        ctx.fillStyle = K.lin(ctx, minx, miny, maxx, maxy, [[0, base], [1, K.mix(base.startsWith('#') ? base : '#ffffff', '#5a6f92', 0.35)]]);
      } else ctx.fillStyle = base;
      ctx.fill();
      if (front && band !== 'small') {
        // fire: a thin wash of spectral colour across faces turned toward the side
        const hue = SPEC[(f.i + (f.top ? 0 : 3)) % SPEC.length];
        ctx.save(); ctx.clip();
        ctx.globalAlpha = 0.5 * glow * (0.3 + 0.7 * Math.abs(f.n[0]));
        ctx.fillStyle = K.lin(ctx, minx, maxy, maxx, miny, [[0, 'rgba(255,255,255,0)'], [0.5, hue], [1, 'rgba(255,255,255,0)']]);
        ctx.fillRect(minx, miny, maxx - minx, maxy - miny);
        ctx.restore();
      }
    }
    // edges: the far ones seen faintly through the stone, the near ones bright
    ctx.lineCap = 'round';
    if (band !== 'small') {
      ctx.lineWidth = Math.max(0.6, size * 0.0025); ctx.strokeStyle = 'rgba(70,90,125,0.28)';
      for (const f of F) { if (f.n[2] > 0) continue; const pts = f.q.map(P); ctx.beginPath(); ctx.moveTo(pts[0][0], pts[0][1]); ctx.lineTo(pts[1][0], pts[1][1]); ctx.lineTo(pts[2][0], pts[2][1]); ctx.closePath(); ctx.stroke(); }
    }
    ctx.lineWidth = band === 'small' ? 1 : Math.max(0.8, size * 0.004);
    for (const f of F) {
      if (f.n[2] <= 0) continue;
      const pts = f.q.map(P);
      ctx.strokeStyle = band === 'small' ? 'rgba(80,100,130,0.5)' : 'rgba(255,255,255,0.85)';
      ctx.beginPath(); ctx.moveTo(pts[0][0], pts[0][1]); ctx.lineTo(pts[1][0], pts[1][1]); ctx.lineTo(pts[2][0], pts[2][1]); ctx.closePath(); ctx.stroke();
    }
    if (band !== 'small') {
      // a dark outline so the clear crystal holds against the pale wall
      ctx.strokeStyle = 'rgba(60,75,100,0.55)'; ctx.lineWidth = Math.max(0.8, size * 0.003);
      const hull = [P(0)]; for (let i = 1; i <= SIDES; i++) hull.push(P(i)); hull.push(P(SIDES + 1));
      const left = hull.slice().sort((a, b) => a[0] - b[0])[0], right = hull.slice().sort((a, b) => b[0] - a[0])[0];
      ctx.beginPath(); ctx.moveTo(P(0)[0], P(0)[1]); ctx.lineTo(right[0], right[1]); ctx.lineTo(P(SIDES + 1)[0], P(SIDES + 1)[1]); ctx.lineTo(left[0], left[1]); ctx.closePath(); ctx.stroke();
      // a small hot glint where the crown meets the girdle
      const tp = P(0), gx = tp[0] - r * 0.32, gy = tp[1] + r * 0.62, gr = r * 0.16;
      ctx.globalCompositeOperation = 'lighter';
      ctx.fillStyle = K.rad(ctx, gx, gy, gr, [[0, 'rgba(255,255,255,' + (0.9 * glow).toFixed(3) + ')'], [1, 'rgba(255,255,255,0)']]);
      ctx.fillRect(gx - gr, gy - gr, 2 * gr, 2 * gr);
    }
    ctx.restore();
  }

  MarrowIcons.register({
    letter: 'N',
    id: 'suncatcher',
    name: 'Suncatcher',
    meaning: 'One light in, colour everywhere: a single prompt scattered into every answer the models can give.',
    story: 'A cut crystal hangs on a thread in a morning window and throws small rainbows across a pale wall. It is the quiet magic of the app: you bring one plain beam of intent, it returns the whole spectrum. Warm white, clear crystal, scattered colour.',
    swatch: ['#f4ece1', '#ffffff', '#2fb6ff'],
    launch: { ms: 1100, note: 'The crystal lowers into the light on its thread and comes to rest; then rainbows slide out from it and settle across the wall one by one.' },
    idle: { ms: 8000, note: 'The crystal turns a few degrees and back on its thread, its facets trading light, and the rainbows on the wall drift with it.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const px = Math.round(size * s0);
      const L = t.launch;
      const cx = size * 0.5, cy = size * (band === 'tiny' ? 0.5 : band === 'small' ? 0.5 : 0.46);
      const r = size * (band === 'tiny' ? 0.25 : band === 'small' ? 0.2 : 0.155);
      const ph = L >= 1 && t.idle > 0 ? K.loop(t.idle, 8000) : null;
      ctx.save();
      const aW = K.ease.sine(K.seg(L, 0, 0.3)), eW = K.ease.mac(K.seg(L, 0, 0.4));
      ctx.save(); ctx.globalAlpha = aW;
      if (eW < 1) { const k = K.lerp(0.96, 1, eW); ctx.translate(size / 2, size / 2); ctx.scale(k, k); ctx.translate(-size / 2, -size / 2); }
      ctx.drawImage(K.cache('N-suncatcher|wall|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); wall(o, size, band, theme); }), 0, 0, size, size);
      ctx.restore();
      const { x, y, w, h } = K.box(size);
      ctx.beginPath(); K.squircle(ctx, x, y, w, h, w * 0.235); ctx.clip();
      // the crystal lowers on its thread; a small sway that dies away
      const eC = K.ease.mac(K.seg(L, 0.08, 0.62)), aC = K.ease.sine(K.seg(L, 0.08, 0.36));
      const drop = (1 - eC) * size * 0.16;
      const yaw0 = 0.28, yaw = yaw0 + (1 - eC) * 0.9 + (ph == null ? 0 : 0.32 * Math.sin(TAU * ph));
      const prog = K.seg(L, 0.45, 1);
      flecks(ctx, size, band, s0, cx, cy, prog, ph);
      ctx.globalAlpha = aC;
      if (band !== 'tiny' && band !== 'small') {
        // thread and the crystal's faint shadow on the wall
        ctx.strokeStyle = 'rgba(120,110,100,0.5)'; ctx.lineWidth = Math.max(0.6, size * 0.0025);
        ctx.beginPath(); ctx.moveTo(cx, y); ctx.lineTo(cx, cy - drop - r * TOP * 0.9); ctx.stroke();
        ctx.fillStyle = K.rad(ctx, cx + size * 0.06, cy - drop + size * 0.07, r * 1.4, [[0, 'rgba(90,70,50,0.16)'], [1, 'rgba(90,70,50,0)']]);
        ctx.save(); ctx.translate(cx + size * 0.06, cy - drop + size * 0.07); ctx.scale(0.6, 1.1); ctx.translate(-(cx + size * 0.06), -(cy - drop + size * 0.07));
        ctx.fillRect(cx - r * 2, cy - drop - r * 2, r * 5, r * 5); ctx.restore();
      }
      crystal(ctx, size, band, cx, cy - drop, r, yaw, K.ease.out(K.seg(L, 0.35, 0.8)));
      ctx.restore();
    },
  });
})();
