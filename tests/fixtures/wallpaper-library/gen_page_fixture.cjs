// Runs the snapshot page's own colour code (look, applyLook, palFor, matchOf, fitLab, lab2hex) from
// porting/first-release/design-src/wallpaper-library/animated/lib-page.js and lib-gl.js, with the recorded ED-NEW-wl-reconcile policy patch, and writes the numbers
// the app's C# port must reproduce. Usage: node gen_page_fixture.cjs > page_fixture.json
const fs = require('fs'), path = require('path'), vm = require('vm');
const dir = path.join(__dirname, '..', '..', '..', 'porting', 'first-release', 'design-src', 'wallpaper-library', 'animated');
const lib = JSON.parse(fs.readFileSync(path.join(dir, 'wallpapers.json'), 'utf8'));
const glSrc = fs.readFileSync(path.join(dir, 'lib-gl.js'), 'utf8').split('\n').slice(1).join('\n');
const policy = require('../../../tools/wallpaper-fidelity/no-brown-policy.cjs');
const pageLines = policy.patch('animated/lib-page.js', fs.readFileSync(path.join(dir, 'lib-page.js'), 'utf8')).split('\n');
const from = pageLines.findIndex((l) => l.startsWith('  const clamp = '));
const to = pageLines.findIndex((l) => l.startsWith('  const hhmm = '));
const lab2hexFrom = pageLines.findIndex((l) => l.startsWith('  function lab2hex('));
const lab2hexTo = pageLines.findIndex((l) => l.startsWith('  function swatch('));
const window = { WL_DATA: { byId: {} } };
const ctx = vm.createContext({ window, console, Float32Array, Math, parseFloat, isFinite });
vm.runInContext(glSrc, ctx);
const code = pageLines.slice(from, to).concat(pageLines.slice(lab2hexFrom, lab2hexTo)).join('\n');
vm.runInContext('var S = { get: () => "" }, K = { isDark: () => 1 }, osReduce = false, WLGL = window.WLGL;\n(function(){\n' + code
  + '\nglobalThis.PG = { G, matchOf, look, applyLook, palFor, lab2hex, fitLab };\n})();', ctx);
const { G, matchOf, palFor } = ctx.PG;
const ids = ['borealis', 'molten', 'afterglow', 'lumen', 'stillwater', 'daybreak', 'dunes', 'iris'].filter((i) => lib.wallpapers.some((w) => w.id === i));
const pick = [lib.wallpapers[0].id, lib.wallpapers[17].id, lib.wallpapers[35].id, lib.wallpapers[53].id, lib.wallpapers[71].id].concat(ids.slice(0, 2));
const accents = ['#005fb8', '#ffb900', '#f7630c', '#107c10', '#e3008c', '#8764b8', '#a80000'];
const clocks = [0, 1.5, 5.25, 6, 9, 12, 14.75, 18, 21, 23.99];
const out = { clocks, accents, walls: [] };
for (const id of [...new Set(pick)]) {
  const w = lib.wallpapers.find((x) => x.id === id);
  const e = { id, match: {}, looks: [] };
  for (const a of accents) { G.accent = a; const m = matchOf(w); e.match[a] = { dark: m.dark, light: m.light }; }
  const packs = [w.packs[0], w.packs[2]];
  G.accent = '#005fb8';
  for (const pk of packs.concat([matchOf(w)])) {
    for (const dark of [0, 1]) {
      const row = { pack: pk.id, dark, off: Array.from(palFor(w, pk, dark, null).pal), clocks: [] };
      for (const c of clocks) { const r = palFor(w, pk, dark, c); row.clocks.push({ c, pal: Array.from(r.pal), sun: r.sun }); }
      e.looks.push(row);
    }
  }
  // the page's matchOf over an accent hue sweep (every 15 degrees at lightness 0.62, chroma 0.15), to prove the port equals it off the dark_warm falling edge
  e.sweep = {};
  for (let h = 0; h < 360; h += 15) {
    const L = 0.62, A = 0.15 * Math.cos(h * Math.PI / 180), B = 0.15 * Math.sin(h * Math.PI / 180);
    const c3 = (x) => x * x * x, l = c3(L + 0.3963377774 * A + 0.2158037573 * B), m = c3(L - 0.1055613458 * A - 0.0638541728 * B), sc = c3(L - 0.0894841775 * A - 1.291485548 * B);
    const rgb = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * sc, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * sc, -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * sc];
    const hex = '#' + rgb.map((v) => { v = Math.max(0, Math.min(1, v)); v = v <= 0.0031308 ? v * 12.92 : 1.055 * Math.pow(v, 1 / 2.4) - 0.055; return ('0' + Math.round(v * 255).toString(16)).slice(-2); }).join('');
    G.accent = hex; const m2 = matchOf(w); e.sweep[hex] = { dark: m2.dark, light: m2.light };
  }
  out.walls.push(e);
}
process.stdout.write(JSON.stringify(out));
