// VT-8 (voice-SPEC V-T50, V-11.7): reference frames of the two voice orbs, rendered by the design's own voice-orbs.js (the Liquid Orb Editor export run in WebGL2)
// in headless Chrome, at fixed times and states, for the port's fidelity test to compare against (OrbFidelityTests, <= 2/255 at >= 99.5 % of pixels). The same
// pattern as tests/fixtures/composer-lights/ref.js. The committed PNGs in tests/fixtures/voice/ref-orbs/ are the lead's run on Windows (ANGLE D3D11); this script
// may be run anywhere to try it (a Linux run uses SwiftShader and is evidence only: the port's test compares against the Windows files).
//
//   node ref-orbs.js <out dir> [--angle d3d11|swiftshader|default] [--orbs <voice-orbs.js>] [--only <name substring>]
//   node ref-orbs.js --list                       prints the job names, one per line, without opening a browser
//
// It needs the `playwright` package (npm i playwright, or NODE_PATH) and a Chromium: playwright's own, or CHROME_PATH. Default --angle: d3d11 on Windows, swiftshader
// elsewhere. voice-orbs.js is the design source (porting/first-release/design-src/customization/voice-orbs.js, read-only); it is loaded as it is: only the two
// `export` lines are turned into a global (no line of the runtime or the shader is touched).
//
// THE JOBS (V-T50 matrix): 2 orbs x 9 states x 2 ball sizes x 2 themes = 72 frames. Every frame is the 72 x 72 view (V-11.3) at device scale 1, so the orb runs into a
// 144 px surface (2x below 128 device px) and the browser filters it down; the ball is 32 or 26 px; the page behind it is the composer card's colour in the theme.
// A state is the STEADY state of V-11.4's table (the integrators landed on their targets): the host's frame state is the table's row, and the motion phase is set
// to t * (speed * the preset's resting speed) as the integral of a constant speed would leave it (the export integrates phase += dt * values[3]).
'use strict';
const fs = require('fs'), path = require('path');

// ---- the matrix (OrbFidelityTests reads these arrays; keep the names in step) ----
const ORBS = ['siri-wave', 'voice-membrane'];
const STATE_NAMES = ['rest-0', 'rest-1.3', 'rest-4.0', 'hover', 'listen-quiet', 'listen-speech', 'listen-loud', 'processing', 'blocked'];
const BALLS = [32, 26];
const THEMES = ['dark', 'light'];
const VIEW = 72;
const CARD = { dark: '#1c1c1c', light: '#f4f4f4' };

// ---- V-11.4's constants K and table (voice-composer.js:15-25, 725-773) ----
const K = { restBase: 1.0, hoverSpeed: 2.0, voiceSpeed: 2.2, procSpeed: 4.6, errSpeed: 0.6, hoverAct: 0.3, hoverScale: 1.04, listenScale: 1.16, voiceScale: 0.04,
  procScale: 1.1, hoverGlow: 0.55, listenGlow: 0.45, voiceGlow: 0.4, procGlow: 0.75 };
// the three band sets of the Listening frames (V-T50): quiet, speech, loud; the voice follower v rests on `all`
const BANDS = {
  'listen-quiet': { low: 0.1, mid: 0.08, high: 0.04, all: 0.1 },
  'listen-speech': { low: 0.5, mid: 0.6, high: 0.35, all: 0.5 },
  'listen-loud': { low: 0.95, mid: 0.9, high: 0.8, all: 0.9 },
};
const REST_T = { 'rest-0': 0, 'rest-1.3': 1.3, 'rest-4.0': 4.0 };
const FIXED_T = 2.0; // the time of every state that is not a Rest time

function stateOf(name) {
  if (name in REST_T) return { t: REST_T[name], act: 0, speed: 1, scale: 1, glow: 0, lift: 0, desat: 0 };
  if (name === 'hover') return { t: FIXED_T, act: K.hoverAct, speed: K.hoverSpeed, scale: K.hoverScale, glow: K.hoverGlow, lift: 0.15, desat: 0 };
  if (name in BANDS) {
    const v = BANDS[name].all;
    return { t: FIXED_T, act: 1, speed: 1 + K.voiceSpeed * v, scale: K.listenScale + K.voiceScale * v, glow: K.listenGlow + K.voiceGlow * v, lift: 1, desat: 0, bands: BANDS[name] };
  }
  if (name === 'processing') return { t: FIXED_T, act: 1, speed: K.procSpeed, scale: K.procScale, glow: K.procGlow, lift: 0.8, desat: 0 };
  if (name === 'blocked') return { t: FIXED_T, act: 0, speed: K.errSpeed, scale: 1, glow: 0, lift: 0, desat: 1 };
  throw new Error('unknown state ' + name);
}

function jobs() {
  const list = [];
  for (const orb of ORBS) for (const state of STATE_NAMES) for (const ball of BALLS) for (const theme of THEMES)
    list.push({ name: `ref_${orb}_${state}_${ball}_${theme}`, orb, state, ball, theme });
  return list;
}

const args = process.argv.slice(2);
const flag = (n, d) => { const i = args.indexOf(n); return i >= 0 ? args[i + 1] : d; };
if (args.includes('--list')) { for (const j of jobs()) console.log(j.name); process.exit(0); }

const out = args.find((a, i) => !a.startsWith('--') && (i === 0 || !args[i - 1].startsWith('--')));
if (!out) { console.error('usage: node ref-orbs.js <out dir> [--angle d3d11|swiftshader|default] [--orbs <voice-orbs.js>] [--only <substring>]'); process.exit(2); }
const orbsPath = flag('--orbs', path.join(__dirname, '..', '..', '..', 'porting', 'first-release', 'design-src', 'customization', 'voice-orbs.js'));
const angle = flag('--angle', process.platform === 'win32' ? 'd3d11' : 'swiftshader');
const only = flag('--only', '');

function patched() {
  let src = fs.readFileSync(orbsPath, 'utf8');
  const edits = [['export const orbs =', 'window.__orbs ='], ['export default orbs;', '']];
  for (const [from, to] of edits) {
    if (src.split(from).length !== 2) throw new Error('voice-orbs.js: patch anchor not found exactly once: ' + from);
    src = src.replace(from, to);
  }
  return src;
}

(async () => {
  const { chromium } = require('playwright');
  fs.mkdirSync(out, { recursive: true });
  const launch = { args: ['--ignore-gpu-blocklist', '--enable-webgl'] };
  if (angle !== 'default') launch.args.push('--use-gl=angle', '--use-angle=' + angle);
  if (angle === 'swiftshader') launch.args.push('--enable-unsafe-swiftshader');
  if (process.env.CHROME_PATH) launch.executablePath = process.env.CHROME_PATH;
  const browser = await chromium.launch(launch);
  const page = await browser.newPage({ viewport: { width: VIEW, height: VIEW }, deviceScaleFactor: 1 });
  const errors = [];
  page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') errors.push(m.text()); });
  page.on('pageerror', (e) => errors.push(String(e)));
  await page.setContent('<!doctype html><html><body style="margin:0"><div id="host" style="position:absolute;left:0;top:0;width:72px;height:72px"></div></body></html>');
  await page.addScriptTag({ type: 'module', content: patched() });
  await page.waitForFunction(() => !!window.__orbs);
  let done = 0;
  for (const job of jobs()) {
    if (only && !job.name.includes(only)) continue;
    const st = stateOf(job.state);
    await page.evaluate(([job, st, card, view]) => {
      document.body.style.background = card;
      const host = document.getElementById('host');
      host.innerHTML = '';
      const canvas = document.createElement('canvas');
      host.appendChild(canvas);
      const def = window.__orbs.find((o) => o.id === job.orb);
      const orb = def.make(canvas, { size: view, ball: job.ball, dark: job.theme === 'dark', context: 'own' });
      // the steady state's phase: the integral of a constant speed from 0 to t (phase += dt * values[3], values[3] = speed * the resting speed)
      orb.phase = st.t * st.speed * def.idleSpeed;
      const s = { act: st.act, speed: st.speed, scale: st.scale, glow: st.glow, lift: st.lift, desat: st.desat };
      if (st.bands) s.bands = st.bands;
      orb.frame(st.t, s);
      window.__current = orb;
    }, [job, st, CARD[job.theme], VIEW]);
    await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))));
    await page.screenshot({ path: path.join(out, job.name + '.png'), clip: { x: 0, y: 0, width: VIEW, height: VIEW } });
    await page.evaluate(() => { window.__current.dispose(); window.__current = null; });
    done++;
    process.stdout.write('.');
  }
  console.log('\n' + done + ' frames; errors: ' + (errors.join(' | ') || 'none'));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
