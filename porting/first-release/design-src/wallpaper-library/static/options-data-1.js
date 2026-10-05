// Static wallpaper library: the premium-option data (OPTIONS-FORMAT.md) of the first thirty wallpapers, kept apart from their
// shaders so the shaders stay as the owner loved them. SW.options({ id: { dyn, light, persp, focus } }); an entry that carries
// the same field inline wins. The second wave (walls-<tier>-2.js) carries its data inline.
//
// dyn(s, hs, ln, cn, lit): the four looks of one wallpaper.
//   s   how much the day moves it (0.4 a calm material .. 1.2 a sky): scales the dawn and dusk lift and richness
//   hs  the hue turn at dawn and dusk, +1 or -1 (a blue, violet or pink picture turns toward magenta with +1; a warm one toward red with -1)
//   ln  how much darker the night is than the day (0.07 .. 0.16)
//   cn  the night's chroma multiplier (0.7 .. 0.88)
//   lit the light direction through the day: 'sky' (low left at dawn, high at noon, low right at dusk), 'top' (a studio key, nearly
//       still), 'low' (a low sun: stronger sweep), or null (a flat picture has no light to move)
(function () {
  const LIT = {
    sky: { dawn: [-0.9, 0.25], day: [-0.35, 0.9], dusk: [0.9, 0.2], night: [0.2, 0.85] },
    top: { dawn: [-0.55, 0.8], day: [-0.35, 0.9], dusk: [-0.15, 0.85], night: [0.1, 0.9] },
    low: { dawn: [-1.0, 0.15], day: [-0.4, 0.8], dusk: [1.0, 0.1], night: [0.3, 0.8] },
  };
  const r3 = (x) => Math.round(x * 1000) / 1000;
  function dyn(s, hs, ln, cn, lit) {
    const L = lit ? LIT[lit] : {};
    const k = (l, c, h, key) => Object.assign({ l: r3(l), c: r3(c), h: r3(h) }, L[key] ? { light: L[key].slice() } : {});
    return {
      dawn: k(0.02 * s, 1 + 0.12 * s, -9 * hs * Math.min(1, s), 'dawn'),
      day: k(0, 1, 0, 'day'),
      dusk: k(-0.028 * s, 1 + 0.17 * s, 12 * hs * Math.min(1, s), 'dusk'),
      night: k(-ln, cn, -7 * hs * Math.min(1, s), 'night'),
    };
  }
  const P = (def, depth) => ({ def, depth });
  SW.options({
    // ---- vibrant
    riptide: { dyn: dyn(0.9, 1, 0.12, 0.8, 'low'), light: { def: [-0.55, 0.8] }, persp: P(1, 0.7) },
    corolla: { dyn: dyn(0.8, -1, 0.11, 0.82, 'top'), persp: P(1, 0.8) },
    prism: { dyn: dyn(0.7, 1, 0.12, 0.8, 'top'), persp: P(1, 0.6) },
    solstice: { dyn: dyn(1.2, -1, 0.14, 0.78, 'low'), persp: { def: 1, depth: 1, own: true } },
    northlight: { dyn: dyn(0.8, 1, 0.1, 0.85, 'top'), persp: P(1, 0.9) },
    satin: { dyn: dyn(0.6, 1, 0.1, 0.82, 'top'), persp: P(0, 0.4) },
    orbit: { dyn: dyn(0.55, 1, 0.1, 0.84, 'top'), persp: P(1, 0.9) },
    atoll: { dyn: dyn(0.8, -1, 0.1, 0.8, 'sky'), persp: P(1, 0.5) },
    radiant: { dyn: dyn(0.9, -1, 0.12, 0.8, null), persp: P(1, 0.6) },
    spectrum: { dyn: dyn(0.6, 1, 0.14, 0.75, null), persp: P(1, 0.5) },
    // ---- balanced
    meadow: { dyn: dyn(1.2, -1, 0.13, 0.78, 'low'), persp: P(1, 1) },
    barchan: { dyn: dyn(1.1, -1, 0.12, 0.8, 'low'), persp: P(1, 0.5) },
    frost: { dyn: dyn(0.6, 1, 0.1, 0.84, 'top'), persp: P(1, 0.8) },
    cascade: { dyn: dyn(0.8, 1, 0.1, 0.82, 'top'), persp: P(1, 0.7) },
    folio: { dyn: dyn(0.6, -1, 0.09, 0.85, 'sky'), persp: P(1, 0.8) },
    ripple: { dyn: dyn(1, 1, 0.12, 0.8, 'sky'), persp: P(1, 0.5) },
    contour: { dyn: dyn(0.5, 1, 0.09, 0.85, null), persp: P(0, 0.3) },
    watercolor: { dyn: dyn(0.5, -1, 0.08, 0.86, null), persp: P(0, 0.3) },
    arches: { dyn: dyn(1, -1, 0.11, 0.8, 'low'), persp: P(1, 0.7) },
    strata: { dyn: dyn(0.7, -1, 0.1, 0.82, 'sky'), persp: P(0, 0.4) },
    // ---- minimal
    flax: { dyn: dyn(0.4, 1, 0.08, 0.88, 'sky'), persp: P(0, 0.3) },
    washi: { dyn: dyn(0.4, -1, 0.08, 0.88, 'sky'), persp: P(0, 0.3) },
    dawn: { dyn: dyn(1.1, -1, 0.12, 0.78, 'low'), persp: P(1, 0.6) },
    arc: { dyn: dyn(0.6, 1, 0.09, 0.85, 'top'), persp: P(1, 0.6) },
    inkwash: { dyn: dyn(0.8, 1, 0.1, 0.82, 'sky'), persp: { def: 1, depth: 0.7, own: true } },
    pebble: { dyn: dyn(0.5, -1, 0.09, 0.86, 'sky'), persp: P(0, 0.4) },
    aureole: { dyn: dyn(0.7, 1, 0.1, 0.84, null), persp: P(0, 0.3) },
    confluence: { dyn: dyn(0.7, 1, 0.1, 0.84, null), persp: P(0, 0.3) },
    afternoon: { dyn: dyn(1, -1, 0.1, 0.82, 'low'), persp: P(1, 0.4) },
    vellum: { dyn: dyn(0.5, 1, 0.08, 0.86, 'top'), persp: P(1, 0.5) },
  });
})();
