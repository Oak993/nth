// CZ-6: the sticky stage, measured from the locked design, not from the port. Run: node tests/fixtures/customization/sticky.generate.cjs
// - the compaction traces come from the real CzComponent.stageTick() of the locked cz.js, run over a stand-in scroll element (scrollTop, the pin and the room under it), frame by frame;
// - the constants the port restates (92, 72, the time constant, the hairline's 4, the text's 1.8 and 14, the stage's 212, the hairline's 0.25 s, the scroll padding) are parsed from cz.js and gen.py.
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const dir = 'porting/first-release/design-src/customization/';
const source = fs.readFileSync(path.join(root, dir + 'cz.js'), 'utf8');
const gen = fs.readFileSync(path.join(root, dir + 'gen.py'), 'utf8');

const context = vm.createContext({ window: {}, Component: class {}, BEAM_DEFAULTS: {}, AURA_DEFAULTS: {}, hex2: () => [0, 0, 0], mixHex: () => '#000000', fmt: String, clamp: (v, a, b) => Math.max(a, Math.min(b, v)) });
vm.runInContext(source.replace('/*@MAKERS@*/null', '[]'), context, { filename: dir + 'cz.js' });
const Czc = vm.runInContext('CzComponent', context);

// one board over a stand-in scroll element; `reduce` is the reduced-motion media query
function board(reduce) {
  const props = {};
  const room = { style: { marginTop: '' } };
  const pin = { __col: undefined, nextElementSibling: room, style: { setProperty: (k, v) => { props[k] = v; } } };
  const el = { scrollTop: 0, isConnected: true, querySelector: (s) => (s === '.czpin' ? pin : null) };
  const b = Object.assign(Object.create(Czc.prototype), { scrollB: el, mq: { matches: reduce }, col: undefined });
  return { el, props, room, tick: (top, dt) => { el.scrollTop = top; b.stageTick(dt); return { col: b.col, colk: +props['--colk'], room: parseFloat(room.style.marginTop) }; } };
}

// a trace: the scroll offset at each frame (dt seconds apart); the first frame starts the board at the first offset
function trace(name, reduce, dt, offsets) {
  const b = board(reduce);
  return { name, reduce, dt, frames: offsets.map(top => Object.assign({ top }, b.tick(top, dt))) };
}
const D60 = 1 / 60, D120 = 1 / 120;
const traces = [
  // wheel steps: every step within 72 of where the compaction is, followed exactly
  trace('wheel down then up', false, D60, [0, 30, 60, 92, 150, 400, 92, 60, 30, 0]),
  // a step of exactly 72 is still exact, 73 is eased
  trace('step of 72', false, D60, [0, 72, 0]),
  // a jump from rest to a far offset (End, a focus scroll): the first eased frame is the design's
  trace('jump from rest at 60 Hz', false, D60, [0, 300, 300, 300, 300]),
  trace('jump from rest at 120 Hz', false, D120, [0, 300, 300, 300, 300]),
  // a jump back to the top (Home) from the compacted stage
  trace('jump back at 60 Hz', false, D60, [300, 0, 0, 0, 0]),
  trace('reduce motion', true, D60, [0, 300, 10, 0, 300]),
];

const css = gen.match(/\.czpin\.pinb \.pvchat\{opacity:clamp\(0,calc\(1 - var\(--colk\) \* ([0-9.]+)\),1\);transform:translateY\(calc\(var\(--colk\) \* -(\d+)px\)\)\}/);
const constants = {
  maxCompaction: +source.match(/Math\.min\((\d+), Math\.max\(0, el\.scrollTop\)\)/)[1],
  exactRange: +source.match(/Math\.abs\(to - c\) <= (\d+)/)[1],
  tau: +source.match(/Math\.exp\(-dt \/ ([0-9.]+)\)/)[1],
  colkDivisor: +source.match(/\(c \/ (\d+)\)\.toFixed/)[1],
  chatFade: +css[1], chatLift: +css[2],
  stageRest: +gen.match(/\.czpin\.pinb \.pvw\.stage\{--pvh:calc\((\d+)px - var\(--col\)\)/)[1],
  hairlineOffset: +source.match(/scrollTop > (\d+)/)[1],
  hairlineSeconds: +gen.match(/\.czpin::after\{[^}]*transition:opacity ([0-9.]+)s ease\}/)[1],
  scrollPaddingTop: +gen.match(/scroll-padding-top:(\d+)px/)[1],
  gapBelow: +gen.match(/\.czpin\.pinb\{[^}]*margin-bottom:(\d+)px/)[1],
};
fs.writeFileSync(path.join(__dirname, 'sticky.json'), JSON.stringify({ source: dir + 'cz.js', constants, traces }, null, 2) + '\n');
