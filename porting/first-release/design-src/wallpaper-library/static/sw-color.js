// Static wallpaper library: colour. OKLab / OKLCH conversions, gamut mapping by chroma reduction (the hue and the
// lightness are kept, only the chroma gives way), and the pack resolver: a wallpaper names the lightness and chroma of
// each palette role for dark and for light; a pack names the hues and how rich they are. The resolved palettes are
// what wallpapers.json carries (hex, so the app can port them as plain data). Used by the page and by the build.
(function (root) {
  const s2l = (c) => (c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4));
  const l2s = (c) => (c <= 0.0031308 ? 12.92 * c : 1.055 * Math.pow(c, 1 / 2.4) - 0.055);
  function lab2lin(L, a, b) {
    let l = L + 0.3963377774 * a + 0.2158037573 * b;
    let m = L - 0.1055613458 * a - 0.0638541728 * b;
    let s = L - 0.0894841775 * a - 1.291485548 * b;
    l = l * l * l; m = m * m * m; s = s * s * s;
    return [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s, -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s];
  }
  function lin2lab(r, g, b) {
    let l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
    let m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
    let s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;
    l = Math.cbrt(l); m = Math.cbrt(m); s = Math.cbrt(s);
    return [0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s, 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s, 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s];
  }
  const inG = (c) => c[0] >= -1e-5 && c[0] <= 1 + 1e-5 && c[1] >= -1e-5 && c[1] <= 1 + 1e-5 && c[2] >= -1e-5 && c[2] <= 1 + 1e-5;
  // OKLCH (L 0..1, C, h in degrees) to an in-gamut sRGB hex: the chroma is reduced until the colour fits
  function lch2hex(L, C, h) {
    L = Math.min(0.999, Math.max(0.001, L));
    const hr = h * Math.PI / 180, ca = Math.cos(hr), sa = Math.sin(hr);
    let lo = 0, hi = Math.max(0, C);
    if (!inG(lab2lin(L, hi * ca, hi * sa))) {
      for (let i = 0; i < 24; i++) { const mid = (lo + hi) / 2; if (inG(lab2lin(L, mid * ca, mid * sa))) lo = mid; else hi = mid; }
      hi = lo;
    }
    const rgb = lab2lin(L, hi * ca, hi * sa).map((v) => Math.round(Math.min(1, Math.max(0, l2s(Math.min(1, Math.max(0, v))))) * 255));
    return '#' + rgb.map((v) => v.toString(16).padStart(2, '0')).join('');
  }
  function hex2lab(hex) {
    const n = parseInt(hex.slice(1), 16);
    return lin2lab(s2l(((n >> 16) & 255) / 255), s2l(((n >> 8) & 255) / 255), s2l((n & 255) / 255));
  }
  // a wallpaper's roles: [[Ldark, Cdark, Llight, Clight], ...]. A pack: {n, k, h: hues per role, c: chroma scale,
  // hl: hues for light (else h), cl: chroma scale for light (else c), dL / lL: lightness shifts, x: explicit hex}
  function resolvePack(roles, pk) {
    if (pk.x) return { dark: pk.x.d.slice(), light: pk.x.l.slice() };
    const dark = [], light = [];
    roles.forEach((r, i) => {
      const h = pk.h[i % pk.h.length], hl = (pk.hl || pk.h)[i % (pk.hl || pk.h).length];
      const cr = pk.cr ? pk.cr[i % pk.cr.length] : 1;
      // a dark warm colour (orange to olive) reads as brown mud (dark palettes only). Below L 0.46 such a hue is moved, keeping its
      // chroma: a near-neutral goes to a cool slate, an orange or gold to a wine red, an olive to a deep teal.
      const hw = ((h % 360) + 360) % 360, Ld = r[0] + (pk.dL || 0), Cd = r[1] * (pk.c == null ? 1 : pk.c) * cr;
      let hd = h;
      if (Ld < 0.46 && hw > 20 && hw < 120) hd = Cd < 0.05 ? 262 : hw <= 78 ? 8 + (hw - 20) * 0.2 : 172;
      // a quiet (low-chroma) warm colour in the middle of the range reads as taupe: it becomes a dusty mauve
      else if (Ld < 0.7 && Cd < 0.085 && hw > 20 && hw < 120) hd = 318;
      dark.push(lch2hex(Ld, Cd, hd));
      light.push(lch2hex(r[2] + (pk.lL || 0), r[3] * (pk.cl == null ? (pk.c == null ? 1 : pk.c) : pk.cl) * cr, hl));
    });
    return { dark, light };
  }
  // ---- the premium options: colour half (the data is in OPTIONS-FORMAT.md)
  const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
  const wrap180 = (d) => { d = ((d % 360) + 540) % 360 - 180; return d; };
  const lab2lch = (L, a, b) => [L, Math.hypot(a, b), (Math.atan2(b, a) * 180 / Math.PI + 360) % 360];
  // OKLCH to OKLab, in gamut by chroma reduction, not quantised (the run-time twin of lch2hex)
  function lch2lab(L, C, h) {
    L = clamp(L, 0.001, 0.999);
    const hr = h * Math.PI / 180, ca = Math.cos(hr), sa = Math.sin(hr);
    let lo = 0, hi = Math.max(0, C);
    if (!inG(lab2lin(L, hi * ca, hi * sa))) {
      for (let i = 0; i < 22; i++) { const mid = (lo + hi) / 2; if (inG(lab2lin(L, mid * ca, mid * sa))) lo = mid; else hi = mid; }
      hi = lo;
    }
    return [L, hi * ca, hi * sa];
  }
  // Dynamic: four looks on the clock. night 0:00, dawn 6:00, day 12:00, dusk 18:00, night 24:00; each leg eased (smoothstep),
  // so a key look holds at its own hour. n is how night it is (1 night, 0 day) for u_dark.
  const KEYN = ['night', 'dawn', 'day', 'dusk'], NIGHTNESS = { night: 1, dawn: .45, day: 0, dusk: .5 };
  const ss = (t) => t * t * (3 - 2 * t);
  function keyLook(k) { const l = k.light; return { l: k.l, c: k.c, h: k.h, lx: l ? l[0] : null, ly: l ? l[1] : null }; }
  // the look at a clock hour, from the wallpaper's keys: {l, c, h, lx, ly, n}; keys without a light fall back to def
  function lookAt(keys, hours, def) {
    const t = ((hours % 24) + 24) % 24, i = Math.floor(t / 6) % 4, f = ss((t - i * 6) / 6);
    const A = keys[KEYN[i]], B = keys[KEYN[(i + 1) % 4]];
    const la = A.light || def, lb = B.light || def;
    return { l: A.l + (B.l - A.l) * f, c: A.c + (B.c - A.c) * f, h: A.h + (B.h - A.h) * f, lx: la[0] + (lb[0] - la[0]) * f, ly: la[1] + (lb[1] - la[1]) * f,
      n: NIGHTNESS[KEYN[i]] + (NIGHTNESS[KEYN[(i + 1) % 4]] - NIGHTNESS[KEYN[i]]) * f };
  }
  // the home look of a theme: day for light, night for dark (dark is 0 .. 1), so the authored palette shows exactly at home
  function homeLook(keys, dark, def) {
    const d = lookAt(keys, 12, def), n = lookAt(keys, 0, def);
    return { l: d.l + (n.l - d.l) * dark, c: d.c + (n.c - d.c) * dark, h: d.h + (n.h - d.h) * dark, lx: d.lx + (n.lx - d.lx) * dark, ly: d.ly + (n.ly - d.ly) * dark, n: dark };
  }
  // the change a clock hour makes to a themed palette, scaled by k (0 off .. 1 on): {dl, cr, dh, dlx, dly, dn}
  function dynDelta(keys, hours, dark, k, def) {
    const a = lookAt(keys, hours, def), h = homeLook(keys, dark, def);
    return { dl: (a.l - h.l) * k, cr: Math.pow(a.c / Math.max(.2, h.c), k), dh: (a.h - h.h) * k, dlx: (a.lx - h.lx) * k, dly: (a.ly - h.ly) * k, dn: (a.n - h.n) * k };
  }
  // applies that change to 6 OKLab colours (18 floats) into out
  function shiftPal(pal, d, out) {
    for (let i = 0; i < 6; i++) {
      const c = lab2lch(pal[i * 3], pal[i * 3 + 1], pal[i * 3 + 2]);
      const r = lch2lab(c[0] + d.dl, c[1] * d.cr, c[2] + d.dh);
      out[i * 3] = r[0]; out[i * 3 + 1] = r[1]; out[i * 3 + 2] = r[2];
    }
    return out;
  }
  // Match accent: a pack spec for resolvePack from an accent colour (hex). base = the hues {d: [6], l: [6]} of the wallpaper's first pack
  const ACCENTS = [['Blue', '#005FB8'], ['Violet', '#6B4FD8'], ['Pink', '#C2358C'], ['Red', '#C42B3A'], ['Orange', '#D4501A'], ['Gold', '#B88A00'], ['Green', '#2E8B3E'], ['Teal', '#00877C'], ['Cyan', '#0E86B0'], ['Graphite', '#5D6A7B']];
  function accentSpec(roles, base, hex) {
    const a = lab2lch.apply(null, hex2lab(hex)), La = a[0], Ca = a[1], Ha = a[2];
    const cs = clamp(Ca / 0.16, 0.3, 1.15);
    const side = (mode) => {
      let m = 0, best = 9;
      for (let i = 0; i < 5; i++) { const d = Math.abs(roles[i][mode * 2] - La); if (d < best) { best = d; m = i; } }
      const hb = base ? base[mode ? 'l' : 'd'] : null;
      return roles.map((r, i) => {
        const o = hb ? clamp(wrap180(hb[i % hb.length] - hb[m % hb.length]) * 0.5, -60, 60) : (i - m) * 14;
        return ((Ha + o) % 360 + 360) % 360;
      });
    };
    return { n: 'Match accent', k: 'accent', h: side(0), hl: side(1), c: cs };
  }
  const api = { lch2hex, hex2lab, lab2lin, lin2lab, resolvePack, s2l, l2s, lab2lch, lch2lab, lookAt, homeLook, dynDelta, shiftPal, accentSpec, ACCENTS, KEYN, wrap180 };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.SWC = api;
})(typeof window !== 'undefined' ? window : globalThis);
