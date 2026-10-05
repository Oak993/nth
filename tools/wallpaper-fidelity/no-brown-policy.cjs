// ED-NEW-wl-reconcile. Explicit derived-page correction; the approved snapshot stays byte-exact.
// The hue projection is the page's resolvePack ground rule, applied after Dynamic; light palettes stay authored.
const ground = `const groundHue = (L, C, hue) => { const h = ((hue % 360) + 360) % 360; return L < .46 && h > 20 && h < 120 ? C < .05 ? 262 : h <= 78 ? 8 + (h - 20) * .2 : 172 : hue; };`;
function replaceOnce(source, before, after) {
  if (source.split(before).length !== 2) throw new Error('No-brown policy source anchor changed: ' + before);
  return source.replace(before, after);
}
exports.patch = (name, source) => {
  if (name === 'static/sw-color.js') {
    source = replaceOnce(source, '  function shiftPal(pal, d, out) {', `  ${ground}\n  function shiftPal(pal, d, out) {`);
    source = replaceOnce(source, 'dn: (a.n - h.n) * k', 'dn: (a.n - h.n) * k, dk: dark');
    return replaceOnce(source, 'const r = lch2lab(c[0] + d.dl, c[1] * d.cr, c[2] + d.dh);',
      'const L = c[0] + d.dl, C = c[1] * d.cr, H = c[2] + d.dh;\n      const r = lch2lab(L, C, d.dk > .5 ? groundHue(L, C, H) : H);');
  }
  if (name === 'animated/lib-page.js') {
    source = replaceOnce(source, '  function applyLook(pal, L) {', `  ${ground}\n  function applyLook(pal, L, dark) {`);
    source = replaceOnce(source, 'const f = fitLab(pal[i * 3] + L.dL[i], C, Math.atan2(b, a) + L.dh[i] * D2R);',
      'const ll = pal[i * 3] + L.dL[i], h = Math.atan2(b, a) / D2R + L.dh[i];\n      const f = fitLab(ll, C, (dark > .5 ? groundHue(ll, C, h) : h) * D2R);');
    source = replaceOnce(source, 'pal: applyLook(pal, L)', 'pal: applyLook(pal, L, dark)');
    // WL-3's tested falling-edge correction: turn through green/blue instead of back through brown.
    source = replaceOnce(source, '  const wrapDeg = (d)', '  const turn = (target, hue, edge) => hue > edge ? ((target - hue) % 360 + 360) % 360 : wrapDeg(target - hue);\n  const wrapDeg = (d)');
    source = replaceOnce(source, 'wrapDeg(dw.ground_hue - hh)', 'turn(dw.ground_hue, hh, dw.band[2])');
    return replaceOnce(source, 'wrapDeg(dw.mid_hue - hh)', 'turn(dw.mid_hue, hh, dw.band[2])');
  }
  return source;
};
