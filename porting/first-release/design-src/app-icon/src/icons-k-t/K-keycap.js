// K Keycap: one frosted, backlit keycap with a living caret inside.
(function () {
  const K = IconKit;
  const TAU = Math.PI * 2;

  function rr(ctx, x, y, w, h, r) { K.squircle(ctx, x, y, w, h, r, 0.7); }

  // geometry in fractions of size: the skirt (outer silhouette) and the top face, seen from the front and above
  function geo(size, band) {
    if (band === 'tiny') {
      return { sk: [1, 1.5, size - 2, size - 2.5, size * 0.24], tp: [size * 0.19, size * 0.13, size * 0.62, size * 0.58, size * 0.14] };
    }
    if (band === 'small') {
      return { sk: [1, 1.5, size - 2, size - 2.5, size * 0.22], tp: [size * 0.19, size * 0.11, size * 0.62, size * 0.6, size * 0.14] };
    }
    return {
      sk: [size * 0.125, size * 0.155, size * 0.75, size * 0.72, size * 0.17],
      tp: [size * 0.215, size * 0.17, size * 0.57, size * 0.545, size * 0.115],
    };
  }

  function body(o, size, band, theme) {
    const lite = theme === 'light';
    const g = geo(size, band), [sx, sy, sw, sh, sr] = g.sk, [tx, ty, tw, th, tr] = g.tp;
    if (band === 'tiny') {
      o.fillStyle = K.lin(o, 0, sy, 0, sy + sh, lite ? [[0, '#e9eef4'], [1, '#93a4b6']] : [[0, '#f2f5f8'], [1, '#aab8c6']]);
      o.beginPath(); rr(o, sx, sy, sw, sh, sr); o.fill();
      o.fillStyle = '#ffffff';
      o.beginPath(); rr(o, tx, ty, tw, th, tr); o.fill();
      return;
    }
    if (band === 'medium' || band === 'large') {
      // the backlight spilling onto the desk under the key, and the contact shadow
      const cx = size / 2, by = sy + sh;
      o.save();
      o.globalCompositeOperation = 'source-over';
      K.blur(o, size * 0.035, (x) => {
        x.fillStyle = lite ? 'rgba(20,40,70,0.30)' : 'rgba(0,0,0,0.55)';
        x.beginPath(); x.ellipse(cx, by - size * 0.012, sw * 0.47, size * 0.05, 0, 0, TAU); x.fill();
      });
      o.fillStyle = K.rad(o, cx, by - size * 0.02, sw * 0.62, [[0, lite ? 'rgba(40,170,255,0.30)' : 'rgba(60,190,255,0.42)'], [0.5, lite ? 'rgba(40,170,255,0.10)' : 'rgba(60,190,255,0.14)'], [1, 'rgba(60,190,255,0)']]);
      o.save(); o.translate(cx, by - size * 0.02); o.scale(1, 0.32); o.translate(-cx, -(by - size * 0.02));
      o.fillRect(cx - sw, by - size * 0.02 - sw, 2 * sw, 2 * sw); o.restore();
      o.restore();
    }
    // skirt: frosted polycarbonate, lit from below by the LED
    o.fillStyle = K.lin(o, 0, sy, 0, sy + sh, [[0, '#f6f8fb'], [0.55, '#e3e9f0'], [0.86, '#cdd8e3'], [1, '#bccad8']]);
    o.beginPath(); rr(o, sx, sy, sw, sh, sr); o.fill();
    o.save(); o.beginPath(); rr(o, sx, sy, sw, sh, sr); o.clip();
    // cylindrical side shading
    o.fillStyle = K.lin(o, sx, 0, sx + sw, 0, [[0, 'rgba(70,90,120,0.20)'], [0.18, 'rgba(70,90,120,0)'], [0.82, 'rgba(70,90,120,0)'], [1, 'rgba(70,90,120,0.26)']]);
    o.fillRect(sx, sy, sw, sh);
    // light transmitted through the lower skirt
    o.fillStyle = K.rad(o, size / 2, sy + sh * 1.05, sw * 0.6, [[0, 'rgba(90,205,255,0.55)'], [0.45, 'rgba(110,210,255,0.22)'], [1, 'rgba(120,210,255,0)']]);
    o.fillRect(sx, sy, sw, sh);
    // the front face catches a soft reflection of the room
    if (band !== 'small') {
      o.fillStyle = K.lin(o, 0, ty + th, 0, sy + sh, [[0, 'rgba(255,255,255,0)'], [0.35, 'rgba(255,255,255,0.35)'], [0.55, 'rgba(255,255,255,0)']]);
      o.fillRect(sx, ty + th, sw, sy + sh - ty - th);
    }
    o.restore();
    // the edge where the front skirt meets the top face: a soft shadow under the face lip
    if (band !== 'small') {
      K.blur(o, size * 0.012, (x) => {
        x.fillStyle = 'rgba(60,80,110,0.28)';
        x.beginPath(); rr(x, tx + tw * 0.02, ty + size * 0.012, tw * 0.96, th, tr); x.fill();
      });
    }
    // top face: a shallow concave dish
    o.fillStyle = K.rad(o, tx + tw * 0.5, ty + th * 0.6, tw * 0.7, [[0, '#e2ebf4'], [0.55, '#ebf1f7'], [1, '#fafcfe']]);
    o.beginPath(); rr(o, tx, ty, tw, th, tr); o.fill();
    o.save(); o.beginPath(); rr(o, tx, ty, tw, th, tr); o.clip();
    // dish: the far wall shades, the near wall lifts
    o.fillStyle = K.lin(o, 0, ty, 0, ty + th, [[0, 'rgba(95,120,155,0.30)'], [0.22, 'rgba(95,120,155,0)'], [0.78, 'rgba(255,255,255,0)'], [1, 'rgba(255,255,255,0.85)']]);
    o.fillRect(tx, ty, tw, th);
    // a frosted sheen in the upper left
    o.fillStyle = K.rad(o, tx + tw * 0.2, ty + th * 0.18, tw * 0.5, [[0, 'rgba(255,255,255,0.7)'], [1, 'rgba(255,255,255,0)']]);
    o.fillRect(tx, ty, tw, th);
    // the window the legend shines through: faint cyan tint around the caret
    o.fillStyle = K.rad(o, size * 0.5, ty + th * 0.5, tw * 0.45, [[0, 'rgba(90,200,255,0.30)'], [0.6, 'rgba(90,200,255,0.08)'], [1, 'rgba(90,200,255,0)']]);
    o.fillRect(tx, ty, tw, th);
    o.restore();
    // crisp rim on the top edge of the face and on the skirt top
    o.lineWidth = Math.max(1, size * 0.005);
    o.strokeStyle = K.lin(o, 0, ty, 0, ty + th, [[0, 'rgba(255,255,255,1)'], [0.5, 'rgba(255,255,255,0.2)'], [1, 'rgba(255,255,255,0.9)']]);
    o.beginPath(); rr(o, tx + 0.5, ty + 0.5, tw - 1, th - 1, tr); o.stroke();
    // light theme: a quiet contour so the white key holds its edge on a white desktop
    if (lite || band === 'small') {
      o.lineWidth = Math.max(1, size * 0.004);
      o.strokeStyle = lite ? 'rgba(60,80,110,0.30)' : 'rgba(255,255,255,0.0)';
      o.beginPath(); rr(o, sx + o.lineWidth / 2, sy + o.lineWidth / 2, sw - o.lineWidth, sh - o.lineWidth, sr); o.stroke();
    }
  }

  // the caret and the line of text before it, lit from inside; k: caret intensity, ignite: overall light 0..1
  function legend(ctx, size, band, k, ignite, theme) {
    const g = geo(size, band), [tx, ty, tw, th] = g.tp;
    const cx = tx + tw * 0.5, cy = ty + th * 0.5;
    if (band === 'tiny' || band === 'small') {
      const w = band === 'tiny' ? 2 : Math.max(2, Math.round(size * 0.085)), h = Math.round(th * 0.62);
      const x = Math.round(cx - w / 2), y = Math.round(cy - h / 2);
      ctx.globalAlpha = ignite * K.lerp(0.35, 1, k);
      ctx.fillStyle = band === 'tiny' ? '#0aa6f0' : K.lin(ctx, 0, y, 0, y + h, [[0, '#33c4ff'], [1, '#0a8fe0']]);
      ctx.fillRect(x, y, w, h);
      ctx.globalAlpha = 1;
      return;
    }
    const w = size * 0.042, h = th * 0.5, x = cx - w / 2, y = cy - h / 2;
    const a = ignite * K.lerp(0.22, 1, k);
    ctx.save();
    // glow inside the frosted cap
    ctx.globalCompositeOperation = theme === 'light' ? 'source-over' : 'lighter';
    ctx.fillStyle = K.rad(ctx, cx, cy, th * 0.62, [[0, 'rgba(70,200,255,' + (0.42 * a).toFixed(3) + ')'], [0.45, 'rgba(70,200,255,' + (0.12 * a).toFixed(3) + ')'], [1, 'rgba(70,200,255,0)']]);
    ctx.fillRect(cx - th, cy - th, 2 * th, 2 * th);
    ctx.globalCompositeOperation = 'source-over';
    // the caret: a clear window, cyan at the edge, white-hot at the core
    ctx.globalAlpha = a;
    K.glow(ctx, 'rgba(40,180,255,0.9)', size * 0.03, (x2) => {
      x2.fillStyle = K.lin(x2, 0, y, 0, y + h, [[0, '#4fd2ff'], [0.5, '#1eb2ff'], [1, '#0b8ee8']]);
      x2.beginPath(); rr(x2, x, y, w, h, w * 0.5); x2.fill();
    });
    // a bright filament down the middle, as if the light source sits right under the window
    ctx.fillStyle = K.lin(ctx, 0, y, 0, y + h, [[0, 'rgba(235,252,255,0.95)'], [1, 'rgba(190,240,255,0.6)']]);
    ctx.beginPath(); rr(ctx, x + w * 0.36, y + w * 0.45, w * 0.28, h - w * 0.9, w * 0.14); ctx.fill();
    ctx.restore();
  }

  MarrowIcons.register({
    letter: 'K',
    id: 'keycap',
    name: 'Keycap',
    meaning: 'Your keyboard, amplified: one key, lit from within, where you type and it types back.',
    story: 'A single keycap of frosted polycarbonate, the kind you can feel under your thumb, backlit in cyan so the light leaks through its skirt and onto the desk. Its only legend is a caret, the place where writing happens. Ice white, soft cyan, a little glow.',
    swatch: ['#f2f5f8', '#bccad8', '#2cbcff'],
    launch: { ms: 1000, note: 'The key settles out of the air, is pressed once with a soft travel, and the backlight blooms through the frosted cap as the caret lights.' },
    idle: { ms: 4800, note: 'The caret breathes like a slow blink, and a frost-soft sheen drifts once across the cap.' },
    draw(ctx, size, theme, t) {
      const band = K.band(size);
      const tr = ctx.getTransform(), s0 = Math.hypot(tr.a, tr.b) || 1;
      const L = t.launch;
      const eIn = K.ease.mac(K.seg(L, 0, 0.42)), aIn = K.ease.sine(K.seg(L, 0, 0.34));
      // one key press: down and back up, the press curve is a smooth half-sine
      const pu = K.seg(L, 0.38, 0.78), press = Math.sin(Math.PI * K.ease.inOut(pu));
      const ignite = K.ease.out(K.seg(L, 0.5, 0.95));
      const flash = Math.sin(Math.PI * K.seg(L, 0.5, 1)) * 0.6;
      ctx.save();
      const cx = size / 2, cy = size * 0.6;
      if (L < 1) {
        const lift = (1 - eIn) * size * 0.06 - press * size * 0.018;
        const k = K.lerp(1.05, 1, eIn) - press * 0.012;
        ctx.globalAlpha = aIn;
        ctx.translate(cx, cy - lift); ctx.scale(k, k * (1 - press * 0.012)); ctx.translate(-cx, -cy);
      }
      const px = Math.round(size * s0);
      const c = K.cache('K-keycap|' + size + '|' + theme + '|' + px, px, px, (o) => { o.setTransform(px / size, 0, 0, px / size, 0, 0); body(o, size, band, theme); });
      ctx.drawImage(c, 0, 0, size, size);
      let caret = 1, sheen = 0;
      if (L >= 1 && t.idle > 0) {
        const ph = K.loop(t.idle, 4800);
        caret = 0.5 + 0.5 * Math.cos(TAU * 2 * ph); // two soft blinks a loop, fully lit at the loop seam
        caret = 1 - (1 - caret) * 0.85;
        sheen = ph;
      }
      legend(ctx, size, band, L < 1 ? Math.min(1, ignite + flash * 0.4) : caret, L < 1 ? ignite : 1, theme);
      if (sheen > 0 && (band === 'medium' || band === 'large')) {
        const g = geo(size, band), [tx, ty, tw, th, trr] = g.tp;
        const u = K.seg(sheen, 0.1, 0.6), env = Math.sin(Math.PI * u);
        if (env > 0.001) {
          const x = K.lerp(tx - tw * 0.4, tx + tw * 1.4, K.ease.inOut(u));
          ctx.save(); ctx.beginPath(); rr(ctx, tx, ty, tw, th, trr); ctx.clip();
          ctx.globalAlpha = 0.55 * env;
          ctx.fillStyle = K.lin(ctx, x - tw * 0.25, ty, x + tw * 0.25, ty + th, [[0, 'rgba(255,255,255,0)'], [0.5, 'rgba(255,255,255,0.9)'], [1, 'rgba(255,255,255,0)']]);
          ctx.fillRect(tx, ty, tw, th);
          ctx.restore();
        }
      }
      ctx.restore();
    },
  });
})();
