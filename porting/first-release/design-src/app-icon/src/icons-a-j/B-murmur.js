// B Murmur: a clear glass orb half full of a glowing liquid; seen from a little above, its surface carries the
// rings a voice makes.
(function () {
  const K = IconKit, PI = Math.PI;
  const LEVEL = 0.47;          // the still level, as a share of the orb's height from the bottom
  const TILT = 0.26;           // the surface ellipse's height / width (the view from a little above)

  function geom(size) {
    const band = K.band(size), b = K.box(size);
    const R = band === 'tiny' || band === 'small' ? b.w / 2 : b.w * 0.47;
    return { cx: size / 2, cy: band === 'tiny' || band === 'small' ? size / 2 : size * 0.48, R };
  }
  function surf(g, level) {
    const { cx, cy, R } = g;
    const y0 = cy + R - 2 * R * level;
    const rx = Math.sqrt(Math.max(0, R * R - (y0 - cy) * (y0 - cy))) * 0.985;
    return { y0, rx, ry: rx * TILT };
  }

  function ground(c, size, g, theme) {
    const { cx, cy, R } = g;
    K.blur(c, R * 0.1, (o) => { o.fillStyle = theme === 'light' ? 'rgba(70,20,80,.30)' : 'rgba(0,0,0,.6)'; o.beginPath(); o.ellipse(cx, cy + R * 0.99, R * 0.6, R * 0.09, 0, 0, 2 * PI); o.fill(); });
    K.blur(c, R * 0.16, (o) => { o.fillStyle = 'rgba(255,80,150,.38)'; o.beginPath(); o.ellipse(cx + R * 0.05, cy + R * 1.0, R * 0.55, R * 0.08, 0, 0, 2 * PI); o.fill(); });
  }
  // the empty glass: tinted and translucent, so the desktop shows through it
  function back(c, size, g, theme) {
    const { cx, cy, R } = g, band = K.band(size);
    c.beginPath(); K.circle(c, cx, cy, R);
    if (band === 'tiny' || band === 'small') c.fillStyle = theme === 'light' ? 'rgba(70,40,110,.30)' : 'rgba(150,120,210,.30)';
    else c.fillStyle = K.rad(c, cx, cy - R * 0.25, R * 1.15, [[0, theme === 'light' ? 'rgba(90,60,140,.12)' : 'rgba(150,120,220,.12)'], [0.75, theme === 'light' ? 'rgba(70,40,120,.22)' : 'rgba(130,100,210,.2)'], [1, theme === 'light' ? 'rgba(50,20,90,.5)' : 'rgba(170,140,255,.42)']]);
    c.fill();
  }
  function liquid(c, size, g, level, wob, ring, glowK) {
    const { cx, cy, R } = g, band = K.band(size);
    const s = surf(g, level);
    if (s.rx < 0.2) return;
    c.save(); c.beginPath(); K.circle(c, cx, cy, R * 0.985); c.clip();
    // the body: below the surface, with the surface's front edge as its top
    c.beginPath(); c.rect(cx - R, s.y0, 2 * R, cy + R - s.y0 + 1); c.ellipse(cx, s.y0, s.rx, s.ry * (1 + wob), 0, 0, PI);
    c.fillStyle = K.lin(c, cx, s.y0 - s.ry, cx, cy + R, [[0, '#ff4f8b'], [0.35, '#c1177f'], [0.75, '#5a1180'], [1, '#2a0752']]);
    c.fill();
    c.beginPath(); c.ellipse(cx, s.y0, s.rx, s.ry * (1 + wob), 0, 0, PI); c.fillStyle = '#ff4f8b'; c.fill();
    if (band === 'medium' || band === 'large') {
      // light inside the liquid: brightest just under the surface, and the caustic it focuses on the bottom of the glass
      K.blur(c, R * 0.14, (o) => { o.fillStyle = 'rgba(255,170,150,' + (0.55 * glowK).toFixed(3) + ')'; o.beginPath(); o.ellipse(cx, s.y0 + s.ry + R * 0.12, s.rx * 0.7, R * 0.16, 0, 0, 2 * PI); o.fill(); });
      K.blur(c, R * 0.05, (o) => { o.lineWidth = R * 0.07; o.strokeStyle = 'rgba(255,190,170,' + (0.75 * glowK).toFixed(3) + ')'; o.lineCap = 'round'; o.beginPath(); o.arc(cx, cy, R * 0.9, 0.3 * PI, 0.7 * PI); o.stroke(); });
    }
    // the light the liquid gives off, rising into the empty glass
    if (band === 'medium' || band === 'large') K.blur(c, R * 0.16, (o) => { o.fillStyle = 'rgba(255,110,160,' + (0.32 * glowK).toFixed(3) + ')'; o.beginPath(); o.ellipse(cx, s.y0 - R * 0.12, s.rx * 0.8, R * 0.2, 0, 0, 2 * PI); o.fill(); });
    // the surface, seen from above: a lighter plane with the rings a voice makes
    c.beginPath(); c.ellipse(cx, s.y0, s.rx, s.ry * (1 + wob), 0, 0, 2 * PI);
    c.fillStyle = K.rad(c, cx, s.y0, s.rx, [[0, '#ffd7b8'], [0.45, '#ff9a8c'], [1, '#ff5a8e']]);
    c.fill();
    if (band !== 'tiny') {
      c.save(); c.beginPath(); c.ellipse(cx, s.y0, s.rx, s.ry * (1 + wob), 0, 0, 2 * PI); c.clip();
      const n = band === 'small' ? 2 : 4;
      for (let i = 0; i < n; i++) {
        const u = (i + ring) / n;                  // rings travel outward; the phase loops seamlessly
        const a = Math.pow(Math.sin(PI * u), 1.5) * (band === 'small' ? 0.6 : 0.34);
        if (a <= 0.01) continue;
        c.strokeStyle = 'rgba(255,255,255,' + a.toFixed(3) + ')';
        c.lineWidth = band === 'small' ? 1 : Math.max(0.8, R * 0.012);
        c.beginPath(); c.ellipse(cx, s.y0, s.rx * u, s.ry * (1 + wob) * u, 0, 0, 2 * PI); c.stroke();
        if (band === 'large' || band === 'medium') {
          c.strokeStyle = 'rgba(190,30,110,' + (a * 0.35).toFixed(3) + ')';
          c.beginPath(); c.ellipse(cx, s.y0 + R * 0.012, s.rx * u, s.ry * (1 + wob) * u, 0, 0.1 * PI, 0.9 * PI); c.stroke();
        }
      }
      c.restore();
      // the meniscus: a bright rim where the surface meets the glass, front half
      c.strokeStyle = 'rgba(255,225,210,.9)'; c.lineWidth = band === 'small' ? 1 : Math.max(1, R * 0.016);
      c.beginPath(); c.ellipse(cx, s.y0, s.rx, s.ry * (1 + wob), 0, 0.02 * PI, 0.98 * PI); c.stroke();
    }
    c.restore();
  }
  function front(c, size, g, theme, gl) {
    const { cx, cy, R } = g, band = K.band(size);
    if (band === 'tiny' || band === 'small') {
      c.fillStyle = 'rgba(255,255,255,.95)'; c.beginPath(); c.ellipse(cx - R * 0.4, cy - R * 0.45, R * 0.24, R * 0.13, -0.7, 0, 2 * PI); c.fill();
      c.lineWidth = 1; c.strokeStyle = theme === 'light' ? 'rgba(50,15,70,.7)' : 'rgba(255,230,255,.55)';
      c.beginPath(); K.circle(c, cx, cy, R - 0.5); c.stroke();
      return;
    }
    c.save(); c.beginPath(); K.circle(c, cx, cy, R); c.clip();
    // the glass's thickness at the rim (Fresnel), then rim light top-left and bottom-right
    c.fillStyle = K.rad(c, cx, cy, R, [[0.82, 'rgba(255,255,255,0)'], [0.96, 'rgba(255,255,255,.10)'], [1, 'rgba(255,255,255,.22)']]);
    c.fillRect(cx - R, cy - R, 2 * R, 2 * R);
    K.blur(c, R * 0.02, (o) => {
      o.lineCap = 'round'; o.lineWidth = R * 0.035;
      o.strokeStyle = 'rgba(255,255,255,.85)'; o.beginPath(); o.arc(cx, cy, R * 0.955, 1.05 * PI, 1.45 * PI); o.stroke();
      o.lineWidth = R * 0.02; o.strokeStyle = 'rgba(255,200,230,.6)'; o.beginPath(); o.arc(cx, cy, R * 0.955, 0.05 * PI, 0.32 * PI); o.stroke();
    });
    // a broad, faint window reflection across the upper glass, and a small sun glint that drifts
    K.blur(c, R * 0.06, (o) => {
      o.fillStyle = K.lin(o, cx, cy - R, cx, cy - R * 0.25, [[0, 'rgba(255,255,255,.34)'], [1, 'rgba(255,255,255,0)']]);
      o.beginPath(); o.ellipse(cx + R * 0.05, cy - R * 0.58, R * 0.66, R * 0.32, 0.1, 0, 2 * PI); o.fill();
    });
    K.glow(c, 'rgba(255,255,255,.9)', R * 0.07, (o) => { o.fillStyle = '#fff'; o.beginPath(); o.ellipse(cx - R * 0.5 + R * 0.04 * gl, cy - R * 0.55 - R * 0.02 * gl, R * 0.04, R * 0.022, -0.75, 0, 2 * PI); o.fill(); });
    c.restore();
    c.lineWidth = Math.max(0.75, size * 0.0028);
    c.strokeStyle = theme === 'light' ? 'rgba(60,20,90,.42)' : 'rgba(255,240,255,.3)';
    c.beginPath(); K.circle(c, cx, cy, R - c.lineWidth / 2); c.stroke();
  }

  MarrowIcons.register({
    letter: 'B', id: 'murmur', name: 'Murmur',
    meaning: 'It listens: speak, and it answers with warmth.',
    story: 'A clear glass orb holds a glowing liquid, plum in its depth and coral at the surface. Seen from a little above, the surface carries the rings a voice makes. The orb you talk to, made into an object you could hold.',
    swatch: ['#5a1180', '#ff4f8b', '#ffd7b8'],
    launch: { ms: 1150, note: 'The glass orb swells into place; the liquid rises from the bottom, its surface rocks once and settles, and the first voice rings spread.' },
    idle: { ms: 4800, note: 'Rings travel slowly out across the surface, like a voice heard from far away, and the glint drifts on the glass.' },
    draw(ctx, size, theme, t) {
      const g = geom(size), band = K.band(size), L = t.launch;
      const phase = K.loop(t.idle, 4800);
      ctx.save();
      const sIn = K.ease.mac(K.seg(L, 0, 0.45)), sc = 0.84 + 0.16 * sIn;
      ctx.translate(g.cx, g.cy); ctx.scale(sc, sc); ctx.translate(-g.cx, -g.cy);
      ctx.globalAlpha = K.ease.out(K.seg(L, 0, 0.32));
      if (band === 'medium' || band === 'large') ground(ctx, size, g, theme);
      back(ctx, size, g, theme);
      ctx.globalAlpha = 1;
      const rise = K.ease.inOut(K.seg(L, 0.18, 0.78));
      const level = (band === 'tiny' ? 0.5 : LEVEL) * rise;
      // the surface rocks once after the pour (the ellipse breathes), then lies still
      const rock = L >= 1 ? 0 : Math.sin(PI * 2 * K.seg(L, 0.6, 1)) * (1 - K.seg(L, 0.6, 1)) * 0.35;
      const wob = rock + (band === 'large' || band === 'medium' ? 0.04 * Math.sin(2 * PI * phase) : 0);
      // the rings: none before the surface settles; they come in from the centre and then loop
      const ringIn = L >= 1 ? 1 : K.ease.out(K.seg(L, 0.66, 1));
      const ring = phase + (1 - ringIn) * -0.5;
      if (level > 0.002) {
        ctx.save(); ctx.globalAlpha = L >= 1 ? 1 : K.ease.out(K.seg(L, 0.18, 0.3));
        liquid(ctx, size, g, level, wob, ((ring % 1) + 1) % 1, L >= 1 ? 1 : K.ease.out(K.seg(L, 0.4, 1)));
        ctx.restore();
      }
      ctx.globalAlpha = K.ease.out(K.seg(L, 0.05, 0.5));
      front(ctx, size, g, theme, Math.sin(2 * PI * phase) + (L >= 1 ? 0 : -1.5 * (1 - K.ease.inOut(K.seg(L, 0.4, 1)))));
      ctx.restore();
    },
  });
})();
