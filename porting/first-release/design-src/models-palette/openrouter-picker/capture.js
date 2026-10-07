// Drives the OpenRouter picker board like a person: types "k", then "i", then deletes both,
// sampling the card's edges on every animation frame, and saves slowed mid-transition stills.
const path = require('path');
const fs = require('fs');
const { chromium } = require(path.resolve(__dirname, '..', 'pw', 'node_modules', 'playwright-core'));

const HERE = __dirname;
const OUT = path.join(HERE, 'out');
const URL = 'file:///' + path.join(HERE, 'Main.dc.html').replace(/\\/g, '/');
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const CLIP = { x: 280, y: 40, width: 380, height: 680 };

async function sample(page, ms) {
  return page.evaluate((ms) => new Promise((done) => {
    const pop = document.querySelector('.pop'), chip = document.querySelector('.chip');
    const out = [], t0 = performance.now();
    const tick = () => {
      const r = pop.getBoundingClientRect(), c = chip.getBoundingClientRect();
      out.push({ t: +(performance.now() - t0).toFixed(1), top: +r.top.toFixed(2), bottom: +r.bottom.toFixed(2), h: +r.height.toFixed(2), chipTop: c.top, chipW: +c.width.toFixed(2), count: document.querySelector('.count .on').textContent });
      if (performance.now() - t0 < ms) requestAnimationFrame(tick); else done(out);
    };
    requestAnimationFrame(tick);
  }), ms);
}

function judge(name, s, dir) {
  const tops = s.map((x) => x.top);
  const travel = tops[tops.length - 1] - tops[0];
  let monotonic = true, maxStep = 0, moving = 0;
  for (let i = 1; i < tops.length; i++) {
    const d = tops[i] - tops[i - 1];
    if (dir * d < -0.01) monotonic = false;
    maxStep = Math.max(maxStep, Math.abs(d));
    if (Math.abs(d) > 0.01) moving++;
  }
  const first = s.findIndex((x, i) => i > 0 && Math.abs(x.top - s[i - 1].top) > 0.01);
  let last = -1;
  for (let i = s.length - 1; i > 0; i--) if (Math.abs(s[i].top - s[i - 1].top) > 0.01) { last = i; break; }
  const dur = first >= 0 && last >= 0 ? s[last].t - s[first - 1].t : 0;
  const overlap = s.some((x) => x.bottom > x.chipTop - 9.5);
  // What a 60 Hz display would show: the largest jump between consecutive 16.7 ms frames.
  const at = (t) => { let i = s.findIndex((x) => x.t >= t); if (i < 0) return s[s.length - 1].top; if (i === 0) return s[0].top; const a = s[i - 1], b = s[i]; return a.top + (b.top - a.top) * ((t - a.t) / (b.t - a.t || 1)); };
  let max60 = 0;
  for (let t = 16.667; t <= s[s.length - 1].t; t += 16.667) max60 = Math.max(max60, Math.abs(at(t) - at(t - 16.667)));
  const r = { name, travel: +travel.toFixed(1), frames: s.length, movingFrames: moving, durationMs: +dur.toFixed(0), maxStepPx: +maxStep.toFixed(1), maxStepShare: travel ? +(maxStep / Math.abs(travel)).toFixed(2) : 0, max60HzStepPx: +max60.toFixed(1), max60HzShare: travel ? +(max60 / Math.abs(travel)).toFixed(2) : 0, monotonic, overlapsChip: overlap, countEnd: s[s.length - 1].count };
  console.log(JSON.stringify(r));
  return r;
}

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await chromium.launch({ executablePath: CHROME, headless: true });
  const ctx = await browser.newContext({ viewport: { width: 900, height: 720 }, deviceScaleFactor: 2 });
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  await page.goto(URL);
  await page.waitForFunction(() => window.__dcReady === true);
  await page.evaluate(() => Promise.race([document.fonts.ready, new Promise((r) => setTimeout(r, 4000))]));
  await page.waitForTimeout(600);
  await page.screenshot({ path: path.join(OUT, '0-open.png'), clip: CLIP });
  const results = [];
  results.push({ name: 'open', count: await page.textContent('.count .on') });

  await page.click('.field input');
  await page.keyboard.type('k');
  results.push(judge('type k', await sample(page, 700), 1));
  await page.screenshot({ path: path.join(OUT, '1-k.png'), clip: CLIP });

  // Slow the next transition tenfold and photograph it in flight.
  const cdp = await ctx.newCDPSession(page);
  await cdp.send('Animation.enable');
  await cdp.send('Animation.setPlaybackRate', { playbackRate: 0.1 });
  await page.keyboard.type('i');
  for (let i = 0; i <= 8; i++) {
    await page.screenshot({ path: path.join(OUT, `2-ki-slow-${String(i).padStart(2, '0')}.png`), clip: CLIP });
    await page.waitForTimeout(450);
  }
  await cdp.send('Animation.setPlaybackRate', { playbackRate: 1 });
  await page.waitForTimeout(800);
  await page.screenshot({ path: path.join(OUT, '3-ki.png'), clip: CLIP });
  results.push({ name: 'after ki', count: await page.textContent('.count .on') });

  // The same "ki" step at full speed, for the frame record.
  await page.keyboard.press('Backspace');
  await page.waitForTimeout(800);
  await page.keyboard.type('i');
  results.push(judge('type i (k -> ki)', await sample(page, 700), 1));

  await page.keyboard.press('Backspace');
  await page.keyboard.press('Backspace');
  results.push(judge('delete both (ki -> all)', await sample(page, 800), -1));
  await page.screenshot({ path: path.join(OUT, '4-cleared.png'), clip: CLIP });

  // The reverse, slowed: from "ki" back to every model.
  await page.keyboard.type('ki');
  await page.waitForTimeout(800);
  await cdp.send('Animation.setPlaybackRate', { playbackRate: 0.1 });
  await page.keyboard.press('Backspace');
  await page.keyboard.press('Backspace');
  for (let i = 0; i <= 8; i++) {
    await page.screenshot({ path: path.join(OUT, `4-open-slow-${String(i).padStart(2, '0')}.png`), clip: CLIP });
    await page.waitForTimeout(450);
  }
  await cdp.send('Animation.setPlaybackRate', { playbackRate: 1 });
  await page.waitForTimeout(800);

  // Pick another model: the chip's name crossfades and its width eases.
  await page.click('.field input');
  await page.keyboard.press('Control+A');
  await page.keyboard.type('grok code');
  await page.waitForTimeout(500);
  await page.keyboard.press('Enter');
  const chip = await sample(page, 500);
  const widths = chip.map((x) => x.chipW);
  results.push({ name: 'pick Grok Code Fast 1', chipWidthStart: widths[0], chipWidthEnd: widths[widths.length - 1], chipWidthFrames: new Set(widths).size });
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(OUT, '5-picked.png'), clip: CLIP });

  results.push({ name: 'errors', errors });
  fs.writeFileSync(path.join(OUT, 'results.json'), JSON.stringify(results, null, 2));
  await browser.close();
})();
