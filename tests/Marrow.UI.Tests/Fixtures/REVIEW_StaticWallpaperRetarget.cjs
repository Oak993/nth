// REVIEW (WL-4): independent oracle for a palette retarget during a running fade. It executes the approved page's own
// sw-gl.body.js Viewer, never the C# implementation, exactly as StaticWallpaperOracle.cjs does.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../../..');
const dir = path.join(root, 'porting/first-release/design-src/wallpaper-library/static');
const C = require(path.join(dir, 'sw-color.js'));
const context = { window: {}, SWC: C, Float32Array, Math };
vm.runInNewContext(fs.readFileSync(path.join(dir, 'sw-gl.body.js'), 'utf8'), context);
const G = context.window.SWG;
const catalogue = JSON.parse(fs.readFileSync(path.join(dir, 'wallpapers.json'), 'utf8'));
const wall = catalogue.wallpapers.find(w => w.id === 'riptide');
const palette = (p, dark) => Float32Array.from(p[dark ? 'dark' : 'light'].flatMap(C.hex2lab));
const eng = { ...wall, grain: 0, opt: { def: 0 } };
const viewer = new G.Viewer({ getContext: () => null }, eng);
viewer.px = [32, 20]; viewer.H = 20; viewer.k = 1;
const target = (pack) => viewer.target({ pal: palette(wall.packs[pack], true), dark: 1, grain: 0, opt: 0, focus: 0, dyn: 0, clock: 12, persp: 0, tilt: [0, 0] });
target(0); viewer.snap();
target(1); viewer.tick(.1);                     // a fade is running (palK = .1/.55)
const steps = [{ dt: .1, retarget: false, pal: Array.from(viewer.pal) }];
target(2);                                      // an accent-drag style retarget while the fade runs
for (const dt of [.05, .1, .1, .2]) { viewer.tick(dt); steps.push({ dt, retarget: steps.length === 1, pal: Array.from(viewer.state().pal) }); }
fs.writeFileSync(path.join(__dirname, 'REVIEW_static-retarget.json'), JSON.stringify({ id: wall.id, packs: [0, 1, 2].map(i => wall.packs[i].name), steps }) + '\n');
