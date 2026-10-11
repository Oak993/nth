// Runs the approved canvas's own pane solver (E:/Droppy/work/design/web-surfaces/index.html, script section
// "1. pane workspace": K, MINW, MAXW, MINH, RANK, DEFC, NAMES, S, ratio, doCollapse, doRestore, chatTerm, lay, solve)
// under node, so PaneLayoutCanvasTests can compare PaneLayout.Solve with it. Nothing is re-implemented here: the
// statements are spliced from the page as written; only S is reset per case.
// Usage: node canvas-solve.mjs <index.html> <cases.json>   (cases: [{name, W, H, panes, side, rat, coll, pre, tabSel}])
import { readFileSync } from 'node:fs';
const [html, casesPath] = process.argv.slice(2);
const lines = readFileSync(html, 'utf8').split(/\r?\n/);
const pick = (prefix) => {
  const i = lines.findIndex((l) => l.startsWith(prefix));
  if (i < 0) throw new Error('missing ' + prefix);
  return i;
};
const one = (prefix) => lines[pick(prefix)];
const block = (prefix, endPrefix) => lines.slice(pick(prefix), pick(endPrefix)).join('\n');
const src = [
  one('const clamp='),
  one('const K='),
  one('const MINW='),
  one('const minW='),
  one('const RANK='),
  one('const DEFC='),
  one('const NAMES='),
  one('const DEF='),
  one('const S='),
  one('const ratio='),
  block('function doCollapse', 'function chatTerm'),
  block('function chatTerm', 'let O=null'),
  'return {S, solve, K};',
].join('\n');
const api = new Function('$', src)(() => null);
const cases = JSON.parse(readFileSync(casesPath, 'utf8'));
const out = {};
for (const c of cases) {
  Object.assign(api.S, { panes: c.panes, side: !!c.side, term: false, rat: c.rat || {}, coll: Object.assign({ ws: false, side: false }, c.coll || {}), pre: {}, tabSel: c.tabSel || {}, sel: 'chat' });
  const o = api.solve(c.W, c.H);
  out[c.name] = {
    mode: o.mode,
    r: o.r,
    g: o.g.map((g) => ({ key: g.key, axis: g.axis, x: g.x, y: g.y, len: g.len, now: g.now, min: g.min, max: g.max, label: g.label, text: g.text })),
    rails: o.rails.map((r) => ({ key: r.key, x: r.x, y: r.y, w: r.w, h: r.h, ids: r.ids })),
    group: Object.fromEntries(Object.entries(o.group).map(([k, v]) => [k, { key: v.key, ids: v.ids, sel: v.sel }])),
    hidden: [...o.hidden],
    ids: o.ids || null,
  };
}
console.log(JSON.stringify(out, null, 1));
