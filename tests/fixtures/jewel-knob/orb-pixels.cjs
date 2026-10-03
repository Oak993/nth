// Renders the locked Violet Ember orb (knob-orbs-1.js, the Jewel material of knob-variants.js) in headless Chrome's WebGL2
// and records its pixels, for EffortSliderJewelOrbTests' pixel comparison with the port's SkSL. The orb runs the same
// 60 fps script as expected.mjs's orbRun (a rest, a charge, a flare) on its own 96 x 96 context (a 48 px knob at 1x).
//   node tests/fixtures/jewel-knob/orb-pixels.cjs <playwright-core dir> <http base serving the locked folder> > orb-pixels.json
// Chrome: C:/Program Files/Google/Chrome/Application/chrome.exe, WebGL on SwiftShader.
const path = require('path');
const { chromium } = require(path.join(process.argv[2], 'playwright-core'));
const base = process.argv[3] || 'http://127.0.0.1:8765/';
(async () => {
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  const page = await browser.newPage();
  await page.route('**/*', (r) => (r.request().url().startsWith(base) ? r.continue() : r.abort()));
  await page.goto(base + 'NOTICES.md');
  const shots = await page.evaluate(async (base) => {
    const KV = await import(base + 'knob-variants.js');
    const PAL = await (await fetch(base + 'liquid-palettes.json')).json();
    const out = [];
    for (const [maker, dark] of [['Claude', true], ['OpenAI', true], ['OpenAI', false], ['DeepSeek', true]]) {
      const m = KV.material('jewel', KV.liquidOf(PAL, maker, 'dark'), 48);
      const canvas = document.createElement('canvas');
      document.body.appendChild(canvas);
      const orb = KV.ORB['violet-ember'].make(canvas, { dark, colors: m, context: 'own', dpr: 1 });
      orb.resize(48);
      const keep = { 18: 'rest', 39: 'charging', 48: 'flaring' };
      for (let i = 0; i <= 48; i++) {
        const t = i / 60;
        const c = t < 0.5 ? 0 : t < 0.7 ? 0.58 * ((t - 0.5) / 0.2) : t < 1.2 ? 0.58 : 0;
        const f = t < 0.7 ? 0 : t < 1.2 ? 0.24 * Math.exp(-(t - 0.7) / 0.3) : 0;
        orb.frame(t, { charge: c, flare: f, exitAngle: Math.PI * 0.9, reduceMotion: false });
        if (!keep[i]) continue;
        const c2 = document.createElement('canvas'); c2.width = canvas.width; c2.height = canvas.height;
        const x = c2.getContext('2d'); x.drawImage(canvas, 0, 0);
        const d = x.getImageData(0, 0, c2.width, c2.height).data; // unpremultiplied RGBA
        let s = ''; for (let j = 0; j < d.length; j++) s += String.fromCharCode(d[j]);
        out.push({ maker, dark, frame: i, state: keep[i], w: c2.width, h: c2.height, rgba: btoa(s) });
      }
      orb.dispose();
    }
    return out;
  }, base);
  process.stdout.write(JSON.stringify({ chrome: await browser.version(), shots }));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
