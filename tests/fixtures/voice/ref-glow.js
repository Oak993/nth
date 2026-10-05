// VT-9 (voice-SPEC V-T60, V-13.2): reference frames of the voice glow, rendered by the design's own voice-glow.js (voice-glow 0.2.1 and the plain-DOM mount
// mountVoiceGlow) in headless Chrome, at fixed states, for the port's fidelity test to compare against (VoiceGlowFidelityTests). The same pattern as
// tests/fixtures/voice/ref-orbs.js and tests/fixtures/composer-lights/ref.js. The committed PNGs in tests/fixtures/voice/ref-glow/ are the run on Windows;
// this script may be run anywhere to try it.
//
//   node ref-glow.js <out dir> [--glow <voice-glow.js>] [--only <name substring>] [--list]
//
// It needs the `playwright` package (npm i playwright, or NODE_PATH) and a Chromium: playwright's own, or CHROME_PATH. voice-glow.js is the design source
// (porting/first-release/design-src/customization/voice-glow.js, read-only); it is loaded as it is, with ONE line appended (a global that exposes
// mountVoiceGlow); no line of the package is touched.
//
// THE JOBS: 7 states x 2 themes x 2 composer sizes = 28 frames, then the band canvas alone (the other layers hidden) in 4 of the states x 2 sizes x 2 themes = 16 more (`ref_band_...`). The element is the composer's glow host (voiceopts.js:194: position absolute, inset 0, radius 22,
// the configuration of V-13.3) at 720 x 96 or 480 x 150 CSS px, device scale 1, on the composer card's colour (#1c1c1c dark, #f4f4f4 light). The page's rAF is
// replaced by a manual clock, so every frame is the driver stepping at exactly 1000/60 ms (the deterministic input of VoiceGlowDriver); the CSS fade-in is
// finished at once (its opacity is 1). A state is a SCRIPT of segments, each a number of frames at a constant raw level, with the processing flag; the frame
// is the page after the last segment. The script is written next to the PNGs as manifest.json, which the port's test replays frame for frame.
'use strict';
const fs = require('fs'), path = require('path');

// ---- the matrix (VoiceGlowReferenceScriptTests reads these arrays; keep the names in step) ----
const SIZES = [[720, 96], [480, 150]];
const THEMES = ['dark', 'light'];
const FPS_MS = 1000 / 60;
// state name -> segments [frames, level, processing]. Levels are the raw level `all` (V-5.3, unsmoothed): the driver smooths it (attack 0.325 s, release 0.86 s).
const STATES = {
  'idle': [[120, 0, false]],
  'level-0.2': [[180, 0.2, false]],
  'level-0.6': [[180, 0.6, false]],
  'level-1.0': [[180, 1.0, false]],
  // listening at 0.6 for 3 s, then processing (the level source is 0 there: voiceopts.js:195), frames at 0.8 s, 1.35 s and 1.9 s of the sweep
  'proc-0.8': [[180, 0.6, false], [48, 0, true]],
  'proc-1.35': [[180, 0.6, false], [81, 0, true]],
  'proc-1.9': [[180, 0.6, false], [114, 0, true]],
};
// the band canvas alone (every other layer hidden by the page's own CSS selectors), for the states where it is drawn big enough to judge a change of its blur or its colours
const BAND_STATES = ['level-0.2', 'level-0.6', 'level-1.0', 'proc-0.8'];
const HIDE_OTHER_LAYERS = '[data-voice-beam]::before,[data-voice-beam-warp],[data-voice-beam]::after,[data-voice-beam-bloom],[data-voice-beam-core]{display:none !important}';
const CARD = { dark: '#1c1c1c', light: '#f4f4f4' };
// V-13.3 (voiceopts.js:194)
const PROPS = { type: 'default', scale: 0.7, glowWidth: 1.6, lobeSpacing: 2.1, rangeWidth: 1.9, processingTravel: 2.05, borderRadius: 22, reducedMotion: false };

function jobs() {
  const list = [];
  for (const state of Object.keys(STATES)) for (const [w, h] of SIZES) for (const theme of THEMES)
    list.push({ name: `ref_glow_${state}_${w}x${h}_${theme}`, state, width: w, height: h, theme, script: STATES[state].map(([frames, level, processing]) => ({ frames, level, processing })) });
  for (const state of BAND_STATES) for (const [w, h] of SIZES) for (const theme of THEMES)
    list.push({ name: `ref_band_${state}_${w}x${h}_${theme}`, state, width: w, height: h, theme, layers: 'band', script: STATES[state].map(([frames, level, processing]) => ({ frames, level, processing })) });
  return list;
}

const args = process.argv.slice(2);
const flag = (n, d) => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : d; };
if (args.includes('--list')) { for (const j of jobs()) console.log(j.name); process.exit(0); }
const out = args.find((a, i) => !a.startsWith('--') && (i === 0 || !args[i - 1].startsWith('--')));
if (!out) { console.error('usage: node ref-glow.js <out dir> [--glow <voice-glow.js>] [--only <substring>]'); process.exit(2); }
const glowPath = flag('--glow', path.join(__dirname, '..', '..', '..', 'porting', 'first-release', 'design-src', 'customization', 'voice-glow.js'));
const only = flag('--only', '');

(async () => {
  const { chromium } = require('playwright');
  fs.mkdirSync(out, { recursive: true });
  const launch = { args: ['--ignore-gpu-blocklist'] };
  if (process.env.CHROME_PATH) launch.executablePath = process.env.CHROME_PATH;
  const browser = await chromium.launch(launch);
  const errors = [];
  const manifest = { source: 'ref-glow.js', fps: 60, props: PROPS, card: CARD, jobs: [] };
  let done = 0;
  for (const job of jobs()) {
    if (only && !job.name.includes(only)) continue;
    const page = await browser.newPage({ viewport: { width: job.width, height: job.height }, deviceScaleFactor: 1 });
    page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') errors.push(m.text()); });
    page.on('pageerror', (e) => errors.push(String(e)));
    await page.setContent(`<!doctype html><html><body style="margin:0;background:${CARD[job.theme]}"><div id="host" style="position:absolute;left:0;top:0;width:${job.width}px;height:${job.height}px;border-radius:22px"></div></body></html>`);
    if (job.layers === 'band') await page.addStyleTag({ content: HIDE_OTHER_LAYERS });
    // the manual frame clock, before the package loads
    await page.addScriptTag({ content: `(() => { let seq = 0; const q = new Map();
      window.requestAnimationFrame = (cb) => { q.set(++seq, cb); return seq; };
      window.cancelAnimationFrame = (id) => { q.delete(id); };
      window.__tick = (ts) => { const cbs = Array.from(q.values()); q.clear(); for (const cb of cbs) cb(ts); };
    })();` });
    const src = fs.readFileSync(glowPath, 'utf8') + '\nwindow.__voiceGlow = { mountVoiceGlow };\n';
    await page.addScriptTag({ type: 'module', content: src });
    await page.waitForFunction(() => !!window.__voiceGlow, null, { polling: 50 });
    await page.evaluate(([job, props]) => {
      window.__level = 0;
      window.__g = window.__voiceGlow.mountVoiceGlow(document.getElementById('host'), Object.assign({}, props, { theme: job.theme, level: () => window.__level, active: true, processing: false }));
      window.__ts = 0;
      for (const a of document.getAnimations()) a.finish();
    }, [job, PROPS]);
    for (const seg of job.script) {
      await page.evaluate(([seg, step]) => {
        window.__g.update({ processing: seg.processing });
        window.__level = seg.level;
        for (let i = 0; i < seg.frames; i++) { window.__ts += step; window.__tick(window.__ts); }
        for (const a of document.getAnimations()) a.finish();
      }, [seg, FPS_MS]);
    }
    await page.waitForTimeout(150);
    await page.screenshot({ path: path.join(out, job.name + '.png'), clip: { x: 0, y: 0, width: job.width, height: job.height } });
    manifest.jobs.push(job);
    await page.close();
    done++;
    process.stdout.write('.');
  }
  fs.writeFileSync(path.join(out, 'manifest.json'), JSON.stringify(manifest, null, 1) + '\n');
  console.log('\n' + done + ' frames; errors: ' + (errors.join(' | ') || 'none'));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
