// Runs the locked classic scripts verbatim. No C# renderer or rewritten drawing code.
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { Canvas } from 'skia-canvas';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../../..');
const source = 'porting/first-release/design-src/app-icon/src/';
const scripts = ['iconkit.js', 'icons-a-j/D-aurora.js'];
const sizes = [16, 24, 32, 48, 64, 256, 512];
const launchMs = [0, 120, 300, 540, 780, 1020, 1200];
const idleMs = [0, 900, 1800, 3600, 5400, 6300, 7200];
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const manifest = {
  backend: 'skia-canvas', version: '3.0.8', cpu: true, dpr: 1,
  launchDurationMs: 1200, idlePeriodMs: 7200, tolerancePerChannel: 2,
  sources: scripts.map(file => ({ path: source + file, sha256: hash(fs.readFileSync(path.join(root, source, file))) })),
  frames: [],
};
fs.mkdirSync(path.join(here, 'png'), { recursive: true });
for (const size of sizes) for (const theme of ['dark', 'light']) {
  const context = vm.createContext({ console, document: { createElement(tag) {
    if (tag !== 'canvas') throw new Error(`Unexpected element ${tag}`);
    const canvas = new Canvas(300, 150); canvas.gpu = false; return canvas;
  } } });
  context.window = context;
  for (const file of scripts) vm.runInContext(fs.readFileSync(path.join(root, source, file), 'utf8'), context, { filename: source + file });
  const icon = context.MarrowIcons.get('D');
  if (icon.launch.ms !== 1200 || icon.idle.ms !== 7200 || !context.IconKit.FILTER_OK)
    throw new Error('Locked module timing or filter support changed');
  const frames = [ { mode: 'still', ms: 0, launch: 1, idle: 0, still: true },
    ...launchMs.map(ms => ({ mode: 'launch', ms, launch: ms / icon.launch.ms, idle: 0, still: false })),
    ...idleMs.map(ms => ({ mode: 'idle', ms, launch: 1, idle: ms / 1000, still: false })) ];
  for (const frame of frames) {
    const canvas = new Canvas(size, size); canvas.gpu = false;
    context.MarrowIcons.render(canvas, icon, size, theme, frame, 1);
    const png = await canvas.toBuffer('png');
    const file = `png/${size}-${theme}-${frame.mode}-${frame.ms}.png`;
    fs.writeFileSync(path.join(here, file), png);
    manifest.frames.push({ size, theme, ...frame, file, sha256: hash(png) });
  }
}
fs.writeFileSync(path.join(here, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
console.log(`Generated ${manifest.frames.length} independent reference PNGs from the locked module.`);
