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
  const api = { lch2hex, hex2lab, lab2lin, lin2lab, resolvePack, s2l, l2s };
  if (typeof module !== 'undefined' && module.exports) module.exports = api; else root.SWC = api;
})(typeof window !== 'undefined' ? window : globalThis);
