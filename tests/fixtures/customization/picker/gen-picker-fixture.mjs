// Generates picker.json: what porting/first-release/design-src/look-picks/lp-pickers.js (variant B, Q-CZ-3) computes, for the
// C# picker tests to compare against. Usage: node gen-picker-fixture.mjs > picker.json (run from this folder).
// The colour maths and the recent-list expression are taken from the design's own text and evaluated; the strip and square
// formulas live inside its pointer handlers, so each is guarded by a check that the design still contains it verbatim.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const src = readFileSync(join(here, '../../../../porting/first-release/design-src/look-picks/lp-pickers.js'), 'utf8');

function between(from, to) {
  const a = src.indexOf(from), b = src.indexOf(to, a);
  if (a < 0 || b < 0) throw new Error('design text not found: ' + from);
  return src.slice(a, b);
}
function need(text) { if (!src.includes(text)) throw new Error('the design no longer contains: ' + text); }

// ---- the colour maths, evaluated from the design
const clampLine = src.match(/^  const clamp = .*$/m)[0];
const maths = between('  // ---- colour', '  // pointer position');
const lib = new Function(clampLine + '\n' + maths + '\nreturn { clamp, hexRgb, rgbHex, rgbHsv, hsvRgb, hsvHex, fromHex, rgba, mixHex };')();
const { clamp, hexRgb, rgbHex, rgbHsv, hsvRgb, hsvHex, fromHex, rgba, mixHex } = lib;

// ---- the recent list, evaluated from the design's set()
const recentExpr = src.match(/S\.recent = (\[hx\]\.concat\(S\.recent\.filter\(\(x\) => x !== hx\)\)\.slice\(0, 8\));/)[1];
const commit = new Function('S', 'hx', 'return ' + recentExpr + ';');
const initial = JSON.parse(src.match(/recent: (\[[^\]]*\])/)[1].replace(/'/g, '"'));

// ---- the formulas inside the handlers (guarded)
need("drag(sv, ([x, y, w, h], end) => { const c = cur(); set({ h: c.h, s: clamp(x / w, 0, 1), v: clamp(1 - y / h, 0, 1), a: c.a }, end); });");
need("u = clamp((x - 8) / (w - 16), 0, 1); if (kind === 'hue') set({ h: u * 359.9");
need("a: Math.round(u * 100) / 100 }");
need("$('.th', sv).style.transform = 'translate(' + (c.s * w - 9) + 'px,' + ((1 - c.v) * h - 9) + 'px)'");
need("u = c.h / 359.9;");
need("u = c.a;");
need("'translateX(' + u * (w - 16) + 'px)'");
need("const w = sv.offsetWidth || 236, h = sv.offsetHeight || 150;");
const square = (x, y, w, h) => ({ s: clamp(x / w, 0, 1), v: clamp(1 - y / h, 0, 1) });
const squareThumb = (s, v, w, h) => [s * w - 9, (1 - v) * h - 9];
const stripU = (x, w) => clamp((x - 8) / (w - 16), 0, 1);
const hueAt = (x, w) => stripU(x, w) * 359.9;
const alphaAt = (x, w) => Math.round(stripU(x, w) * 100) / 100;
const stripThumb = (u, w) => u * (w - 16);

// ---- the hex field: B takes six digits; A's field (the same design) takes six or eight, the eighth pair the alpha as a byte / 255
need("if (/^[0-9a-f]{6}$/i.test(v)) set(fromHex('#' + v, cur().a), true)");
need("/^[0-9a-f]{6}([0-9a-f]{2})?$/i.test(v)) set(fromHex('#' + v.slice(0, 6), v.length === 8 ? parseInt(v.slice(6), 16) / 255 : cur().a), true)");
const hexB = (text) => { const v = text.trim().replace(/^#/, ''); return /^[0-9a-f]{6}$/i.test(v) ? { rgb: '#' + v.toUpperCase() } : null; };
const hexA = (text) => { const v = text.trim().replace(/^#/, ''); return /^[0-9a-f]{6}([0-9a-f]{2})?$/i.test(v) ? { rgb: '#' + v.slice(0, 6).toUpperCase(), alpha: v.length === 8 ? parseInt(v.slice(6), 16) / 255 : null } : null; };

// ---- the arrow tip: the design's own numbers
const header = src.match(/card (\d+) pt from the\n\/\/ anchor, tip (\d+) pt high, (\d+) pt half base, (\d+) pt point radius, (\d+) pt shoulders/);
const tipPath = src.match(/<path d="(M-16 12[^"]*)"/)[1];
const viewBox = src.match(/viewBox="(-17 -1 34 14)"/)[1];
const nums = tipPath.match(/-?\d+(\.\d+)?/g).map(Number);
const ys = [], xs = [];
for (let i = 0; i < nums.length; i += 2) { xs.push(nums[i]); ys.push(nums[i + 1]); }
const out = {
  source: 'porting/first-release/design-src/look-picks/lp-pickers.js',
  popover: { cardGap: +header[1], tipHeight: +header[2], tipHalfBase: +header[3], apexRadius: +header[4], shoulderRadius: +header[5], pickerWidth: 260, popoverExtra: 12 },
  tip: { path: tipPath, viewBox, apexY: Math.min(...ys), baseY: Math.max(...ys), sideStart: [xs[2], ys[2]], sideEnd: [xs[3], ys[3]] },
  initialRecent: initial,
  colours: ['#000000', '#FFFFFF', '#FF0000', '#00FF00', '#0000FF', '#8E7CFF', '#38C9B4', '#CC6B47', '#4B9FFC', '#808080', '#123456', '#FEDCBA', '#7F7F80'].map((hex) => {
    const c = fromHex(hex);
    return { hex, rgb: hexRgb(hex), hsv: [c.h, c.s, c.v], back: hsvHex(c), rgba: rgba(fromHex(hex, 0.37)), mix35: mixHex(hex, '#FFFFFF', 0.35) };
  }),
  hsv: [[0, 1, 1], [60, 1, 1], [120, 0.5, 0.5], [180, 0.25, 0.75], [240, 1, 0.1], [300, 0.9, 0.9], [359.9, 1, 1], [30, 0.3, 0.3], [90, 0, 0.6], [200, 0.77, 0.42]]
    .map(([h, s, v]) => ({ h, s, v, rgb: hsvRgb(h, s, v), hex: rgbHex(hsvRgb(h, s, v)) })),
  rgbHex: [[0, 0, 0], [255, 255, 255], [12.5, 13.5, 254.5], [-5, 300, 127.4], [0.49, 0.5, 254.6]].map((c) => ({ rgb: c, hex: rgbHex(c) })),
  square: { width: 236, height: 150, points: [[0, 0], [236, 150], [118, 75], [-10, 20], [300, 200], [59, 37.5], [177, 112.5], [1, 149]].map(([x, y]) => ({ x, y, ...square(x, y, 236, 150) })),
    thumbs: [[0, 1], [1, 0], [0.5, 0.5], [0.25, 0.8], [0.9, 0.1]].map(([s, v]) => ({ s, v, thumb: squareThumb(s, v, 236, 150) })) },
  strips: { width: 236,
    xs: [0, 8, 9, 60, 118, 120, 150, 200, 228, 236, 300, -4].map((x) => ({ x, u: stripU(x, 236), hue: hueAt(x, 236), alpha: alphaAt(x, 236) })),
    thumbs: [0, 0.25, 0.5, 0.37, 1].map((u) => ({ u, left: stripThumb(u, 236) })),
    hueThumb: [0, 90, 180, 270, 359.9].map((h) => ({ h, u: h / 359.9, left: stripThumb(h / 359.9, 236) })) },
  hex: ['8e7cff', '#8E7CFF', ' #38c9b4 ', '12345', '1234567', 'GGGGGG', '', '#', '8E7CFF80', '#8e7cff00', '8E7CFFFF', '8E7CFF8', '#12345678'].map((text) => ({ text, b: hexB(text), a: hexA(text) })),
  recent: (() => {
    const steps = ['#112233', '#8E7CFF', '#445566', '#112233', '#AAAAAA', '#BBBBBB', '#CCCCCC', '#DDDDDD', '#EEEEEE', '#FFFFFF', '#000000', '#445566'];
    let r = initial, seq = [];
    for (const hx of steps) { r = commit({ recent: r }, hx); seq.push({ hx, after: r }); }
    return { initial, steps: seq };
  })(),
};
process.stdout.write(JSON.stringify(out, null, 1) + '\n');
