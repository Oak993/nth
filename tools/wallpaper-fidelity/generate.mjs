// WL-5: developer-only oracle generation. Never import this tool from a test.
import { readFile, writeFile, mkdir, readdir, rm } from 'node:fs/promises';
import { resolve, relative, dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { chromium } from 'playwright-core';
import { PNG } from 'pngjs';
import policy from './no-brown-policy.cjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const sourceRoot = join(root, 'porting/first-release/design-src/wallpaper-library');
const output = join(root, 'tests/fixtures/wallpapers/reference');
const verify = process.argv.includes('--verify');
if (process.argv.slice(2).some(a => a !== '--verify')) throw new Error('Usage: node generate.mjs [--verify]');
const sha = data => createHash('sha256').update(data).digest('hex');
const text = path => readFile(path, 'utf8');
const sources = {};
async function snapshot(path) {
  const bytes = await readFile(join(sourceRoot, path));
  sources[path] = sha(bytes);
  return policy.patch(path, bytes.toString('utf8').replaceAll('\r\n', '\n')); // Python snapshot builders use universal newlines
}

// pagekit is outside the approved snapshot. This inert publisher shell supplies only the Kit surface needed
// to initialise the original page hosts. No shader, palette function or render method is rewritten.
const shell = `
window.requestAnimationFrame = () => 0;
window.setTimeout = () => 0;
window.Kit = { store: { get: (key, fallback) => fallback, set: () => {} },
  isDark: () => true, themeMode: () => 'dark', theme: () => {}, onTheme: () => {}, seg: () => {} };
`;
async function pageSources(collection) {
  const json = await snapshot(`${collection}/wallpapers.json`);
  const data = JSON.parse(json);
  const build = await snapshot(`${collection}/build_page.py`);
  await snapshot(`${collection}/check.cjs`);
  let body = /BODY = r"""([\s\S]*?)"""/.exec(build)?.[1];
  if (!body) throw new Error(`${collection}: missing snapshot BODY`);
  const scripts = [];
  if (collection === 'static') {
    scripts.push(await snapshot('static/sw-color.js'));
    const engine = (await snapshot('static/sw-gl.body.js')).replace('/*@@GLASS@@*/', await snapshot('static/glass-src.txt'));
    const notice = (await snapshot('static/glass-head.txt')).replace(' and the eased dark / light / glass / Reduce Motion mixes.', ' and the eased theme, pack and glass mixes.');
    scripts.push(notice + engine); // exactly build_page.py's engine() assembly
    scripts.push(await snapshot('static/sw-page.js'));
    body += `<script type="application/json" id="wallpapers">${json.replaceAll('</', '<\\/')}</script>`;
  } else {
    await snapshot('animated/gl_build.py');
    const common = await snapshot('animated/common.glsl');
    if (common !== data.common) throw new Error('common.glsl differs from approved inlined common');
    // The expanded catalogue contains the new walls inline; the original 24 also have standalone sources.
    for (const name of (await readdir(join(sourceRoot, 'animated/walls'))).sort()) {
      const glsl = await snapshot(`animated/walls/${name}`);
      const w = data.wallpapers.find(w => w.id === name.slice(0, -5));
      // Historical filenames meridian/nacre/fields are present, but the approved catalogue names were renamed.
      if (w && w.glsl !== glsl) throw new Error(`animated/walls/${name} differs from the approved catalogue`);
    }
    const template = await snapshot('animated/lib-gl.js');
    const originalNotice = await snapshot('animated/_glass_notice.txt');
    const start = originalNotice.indexOf('* Wallpapers behind');
    const end = originalNotice.indexOf('*\n * The glass shader');
    const notice = originalNotice.slice(0, start) + '* Animated wallpaper library: the WebGL2 engine. One shared context draws every visible tile and copies it into the\n'
      + ' * tile\'s own canvas; the large view draws the wallpaper into a texture and refracts it through the mock window\'s\n'
      + ' * glass, the way the two reference repos do. The same engine runs the loop proofs and the frame-cost measurement.\n '
      + originalNotice.slice(end);
    const glass = (await snapshot('animated/_glass.glsl')).replaceAll('${MAXN}', '16');
    scripts.push(`window.WL_DATA = ${json}; window.WL_DATA.byId = {}; window.WL_DATA.wallpapers.forEach(w => window.WL_DATA.byId[w.id] = w);`);
    scripts.push(template.replace('@@NOTICE@@', notice).replace('@@GLASS@@', glass)); // gl_build.py's built_gl()
    scripts.push(await snapshot('animated/lib-page.js'));
    for (const [tag, id] of [['SPEED', 'vspeed'], ['MOTION', 'vmotion'], ['SECOND', 'vsecond'], ['FOCUS', 'vfocus']]) {
      // Only publisher markup is needed: the original host must find the same slider DOM IDs.
      body = body.replace(`@@${tag}@@`, `<div><label id="${id}l"></label><output id="${id}v"></output><input id="${id}" type="range"><span id="${id}lo"></span><span id="${id}hi"></span></div>`);
    }
  }
  return { data, body, scripts };
}

function compactPng(rgba) {
  // RGB8, lossless adaptive PNG filters and DEFLATE level 9; no colour quantisation, resize or metadata.
  return PNG.sync.write({ width: 160, height: 100, data: rgba }, {
    colorType: 2, inputColorType: 6, inputHasAlpha: true, bitDepth: 8,
    filterType: -1, deflateLevel: 9, deflateStrategy: 0,
  });
}

// The page's own animation/timing is suspended. Poll program linking with Node's timers, so a
// cold KHR_parallel_shader_compile never waits on the inert page requestAnimationFrame shell.
async function readyProgram(page, id) {
  const deadline = Date.now() + 30_000;
  for (;;) {
    const status = await page.evaluate(id => ({ ready: !!__E.prog(id) && __E.ready(__E.blur), error: __E.err }), id);
    if (status.error) throw new Error(`${id}: ${status.error}`);
    if (status.ready) return;
    if (Date.now() >= deadline) throw new Error(`${id}: shader did not link within 30 seconds`);
    await new Promise(done => setTimeout(done, 10));
  }
}

const browser = await chromium.launch({
  executablePath: process.env.WALLPAPER_CHROMIUM || '/usr/bin/chromium', headless: true,
  args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--disable-gpu-sandbox', '--no-sandbox', '--force-color-profile=srgb'],
});
const manifest = {
  schemaVersion: 1, decision: 'ED-NEW-wl-reconcile', policy: { file: 'tools/wallpaper-fidelity/no-brown-policy.cjs', sha256: sha(await readFile(join(root, 'tools/wallpaper-fidelity/no-brown-policy.cjs'))) }, width: 160, height: 100, dpr: 1,
  encoding: 'lossless RGB8 sRGB PNG, top-left origin',
  dither: 'fixed snapshot pixel-coordinate hashes (enabled); no random seed or wall clock',
  coverage: 'default, quiet, rich, dynamic-dusk, match-accent, full-focus; both themes; animated t=0 and T/2',
  animatedQuietPolicy: 'explicit quiet pack; otherwise lowest mean OKLab chroma across the twelve pack colours (Molten/Mercury, Velvet/Platinum, Mist/Fog)',
  animatedRichPolicy: 'highest mean OKLab chroma across all six dark and six light pack colours; stable catalogue-order tie break',
  browser: browser.version(), sources, renders: [],
};
// Store the flags explicitly; avoid relying on Playwright internals for provenance.
manifest.launchArgs = ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--disable-gpu-sandbox', '--no-sandbox', '--force-color-profile=srgb'];
const files = new Set();
let pngBytes = 0;
try {
  if (!verify) await mkdir(output, { recursive: true });
  for (const collection of ['static', 'animated']) {
    const { data, body, scripts } = await pageSources(collection);
    const expectedCount = collection === 'static' ? 90 : 72;
    if (data.wallpapers.length !== expectedCount || new Set(data.wallpapers.map(w => w.id)).size !== expectedCount) throw new Error(`${collection}: invalid approved inventory`);
    const context = await browser.newContext({ viewport: { width: 320, height: 200 }, deviceScaleFactor: 1, colorScheme: 'dark', timezoneId: 'UTC' });
    await context.route('**/*', route => route.abort()); // no remote requests, fonts, services or localhost dependency
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.setContent(`<!doctype html><html><head><meta charset="utf-8"></head><body>${body}</body></html>`);
    await page.addScriptTag({ content: shell });
    for (const script of scripts) await page.addScriptTag({ content: script });
    const renderer = await page.evaluate(kind => kind === 'static' ? window.__sw?.R.renderer : window.__E?.renderer, collection);
    if (!/SwiftShader/i.test(renderer || '')) throw new Error(`${collection}: SwiftShader required, got ${renderer}; ${errors.join('; ')}`);
    if (errors.length) throw new Error(errors.join('; '));
    manifest[`${collection}Renderer`] = renderer;
    for (const w of data.wallpapers) {
      const selection = await page.evaluate(({ collection, id }) => {
        const w = collection === 'static' ? __sw.byId[id] : WL_DATA.byId[id];
        const chroma = p => ['dark', 'light'].flatMap(th => p[th]).reduce((sum, hex) => {
          const lab = WLGL.hex2lab(hex); return sum + Math.hypot(lab[1], lab[2]);
        }, 0) / 12;
        return collection === 'static'
          ? { default: w.defaultPack, quiet: w.packs.findIndex(p => p.kind === 'quiet'), rich: w.packs.findIndex(p => p.kind === 'rich') }
          : { default: w.packs.findIndex(p => p.id === w.defaults.pack), quiet: w.packs.some(p => p.id === 'quiet') ? w.packs.findIndex(p => p.id === 'quiet') : w.packs.map((p, i) => ({ i, c: chroma(p) })).sort((a, b) => a.c - b.c || a.i - b.i)[0].i, rich: w.packs.map((p, i) => ({ i, c: chroma(p) })).sort((a, b) => b.c - a.c || a.i - b.i)[0].i };
      }, { collection, id: w.id });
      if (Object.values(selection).some(i => i < 0)) throw new Error(`${collection}/${w.id}: missing required pack`);
      for (const variant of ['default', 'quiet', 'rich', 'dynamic-dusk', 'match-accent', 'full-focus', 'perspective', 'intermediate-focus', 'authored-focus']) {
        const packIndex = selection[variant] ?? selection.default;
        const pack = w.packs[packIndex];
        for (const dark of [true, false]) for (const time of collection === 'animated' ? [0, w.period / 2] : [0]) for (const sample of variant === 'dynamic-dusk' || variant === 'match-accent' ? [0, 1] : [0]) {
          const input = {
            collection, wallpaper: w.id, name: w.name, variant, dark, packIndex,
            packId: variant === 'match-accent' ? (collection === 'static' ? 'Match accent' : 'match') : (pack.id ?? pack.name),
            packName: variant === 'match-accent' ? 'Match accent' : pack.name,
            time, period: w.period ?? 0, speed: 1, dynamic: variant === 'dynamic-dusk', clockHours: variant === 'dynamic-dusk' && sample === 1 ? 15.25 : 18,
            accent: variant === 'match-accent' && sample === 1 ? '#D4501A' : '#005FB8',
            focus: variant === 'full-focus' ? 1 : variant === 'intermediate-focus' ? .37 : variant === 'authored-focus' ? (collection === 'static' ? w.focus.def : w.focus.default) : 0,
            perspective: variant === 'perspective' || (collection === 'static' ? w.persp.def === 1 : w.defaults.perspective),
            tilt: variant === 'perspective' ? [.65, -.4] : [0, 0], grain: collection === 'static' ? w.grain.def : 0,
            options: collection === 'static' ? { grain: w.grain.def, option: w.option.def }
              : Object.fromEntries(w.options.map(o => [o.id, o.default])),
          };
          if (collection === 'animated') await readyProgram(page, w.id);
          const result = await page.evaluate(input => {
            let rgba, uniforms;
            if (input.collection === 'static') {
              const w = __sw.byId[input.wallpaper];
              const r = __sw.probe(input.wallpaper, {
                dark: input.dark, pack: input.variant === 'match-accent' ? w.packs.length : input.packIndex,
                grain: input.grain, opt: input.options.option, dyn: input.dynamic, clock: input.clockHours,
                accent: input.accent, focus: input.focus, persp: input.perspective, tilt: input.tilt,
              }, 160, 100);
              if (!r) throw new Error(JSON.stringify(__sw.R.err));
              rgba = r.d; uniforms = { palette: Array.from(r.spec.pal), dark: r.spec.dark, ...r.spec.x };
            } else {
              const w = WL_DATA.byId[input.wallpaper]; __G.accent = input.accent;
              const pk = input.variant === 'match-accent' ? __fn.matchOf(w) : w.packs[input.packIndex];
              const p = __fn.palFor(w, pk, input.dark ? 1 : 0, input.dynamic ? input.clockHours : null);
              const s = { pal: p.pal, dark: input.dark ? 1 : 0, opt: w.options.map(o => input.options[o.id]), sun: p.sun, focus: input.focus, tilt: input.tilt };
              const px = __E.read(__E.prog(w.id), 160, 100, input.time, s); // source host's own fixed dither + Focus pass
              rgba = new Uint8Array(px.length);
              for (let y = 0; y < 100; y++) rgba.set(px.subarray((99 - y) * 640, (100 - y) * 640), y * 640);
              uniforms = { palette: Array.from(s.pal), dark: s.dark, options: s.opt, sun: s.sun, focus: s.focus, tilt: s.tilt, sigma: __E.sigma(100, s.focus) };
            }
            if (![...rgba].every((v, i) => i % 4 !== 3 || v === 255)) throw new Error('Non-opaque reference');
            // Base64 transport only: PNG encoding and compression happen in Node.
            let binary = ''; for (const byte of rgba) binary += String.fromCharCode(byte);
            return { pixels: btoa(binary), uniforms };
          }, input);
          const pixels = Buffer.from(result.pixels, 'base64');
          const png = compactPng(pixels);
          const hash = sha(png), file = `${hash}.png`;
          const entry = { ...input, uniforms: result.uniforms, file, sha256: hash, rgbaSha256: sha(pixels), bytes: png.length };
          manifest.renders.push(entry);
          if (!files.has(file)) {
            files.add(file); pngBytes += png.length;
            if (!verify) await writeFile(join(output, file), png);
          }
        }
      }
      if (errors.length) throw new Error(errors.join('; '));
      console.log(`${collection}/${w.id}: ${manifest.renders.length} cases, ${(pngBytes / 1e6).toFixed(2)} MB unique PNGs`);
    }
    await context.close();
  }
  manifest.coverage = 'full risk matrix: every default/quiet/rich pack, dusk and 15:15 Dynamic, blue and orange accent, Focus 0/.37/1/authored default, non-zero Perspective; both themes; animated t=0 and T/2';
  manifest.omitted = 'none';
  manifest.counts = { staticWallpapers: 90, animatedWallpapers: 72, renders: manifest.renders.length, uniquePngs: files.size, pngBytes };
  const serialized = JSON.stringify(manifest, null, 2) + '\n';
  if (Buffer.byteLength(serialized) + pngBytes > 100_000_000) throw new Error(`Full matrix exceeds 100 MB (${pngBytes} PNG bytes); preserve coverage and report before changing matrix`);
  if (verify) {
    for (const file of files) if (sha(await readFile(join(output, file))) !== file.slice(0, -4)) throw new Error(`PNG hash differs: ${file}`);
    if (await text(join(output, 'manifest.json')) !== serialized) throw new Error('Manifest differs (including browser version/source hashes)');
  } else {
    await writeFile(join(output, 'manifest.json'), serialized);
    for (const file of await readdir(output)) if (/^[a-f0-9]{64}\.png$/.test(file) && !files.has(file)) await rm(join(output, file));
  }
  const actualFiles = await readdir(output);
  if (actualFiles.length !== files.size + 1) throw new Error('Unexpected files in reference folder');
  console.log(JSON.stringify(manifest.counts));
  console.log(`${verify ? 'Verified byte-identical' : 'Generated'} ${relative(root, output)} (${pngBytes + Buffer.byteLength(serialized)} total bytes)`);
} finally { await browser.close(); }
