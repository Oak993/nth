// Checks the share copy in headless Chrome, wrapped in the publisher's skeleton (as the wallpapers page's check.cjs
// does): no page errors; no sideways scroll at 1360 and 390 px; every tile drawn in dark and light; only tiles on
// screen drawn; the loop proof of all 24 (every pack, dark and light, both ends of the options) and the frame cost of
// all 24, read from the page; the large view with and without the glass; a pack change that crossfades; a speed change
// that does not jump the picture; the theme crossfade. Shots go to ../shots/. Usage: node check.cjs [quick]
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('E:/Droppy/work/tools/pw/node_modules/playwright-core');
const ROOT = path.resolve(__dirname, '..');
const SHOTS = path.join(ROOT, 'shots'); fs.mkdirSync(SHOTS, { recursive: true });
const SKEL = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head><body>';
const quick = process.argv.includes('quick');
const srv = http.createServer((req, res) => {
  if (req.url === '/favicon.ico') { res.writeHead(204); res.end(); return; }
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]));
  if (!f.startsWith(ROOT) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(/[\\/]share[\\/]/.test(f) ? SKEL + fs.readFileSync(f, 'utf8') + '</body></html>' : fs.readFileSync(f));
});
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function layout(page) {
  return page.evaluate(() => {
    const out = [];
    if (document.documentElement.scrollWidth > innerWidth + 1) out.push('page scrolls sideways ' + document.documentElement.scrollWidth + '>' + innerWidth);
    for (const el of document.querySelectorAll('.dk-btn,.dk-tag,.dk-seg button,.sw,.vtier,.tier-h .tag')) {
      if (el.offsetParent && el.scrollWidth > el.clientWidth + 1) out.push('overflow ' + el.className + ' "' + el.textContent.trim().slice(0, 30) + '"');
    }
    for (const el of document.querySelectorAll('.tier,.tb,.tile,.stat,.dk-note')) {
      if (!el.offsetParent) continue;
      const r = el.getBoundingClientRect();
      if (r.right > innerWidth + 1 || r.left < -1) out.push('outside the viewport ' + el.className.split(' ')[0] + ' ' + Math.round(r.left) + '..' + Math.round(r.right));
    }
    const pg = document.querySelector('.dk-page').getBoundingClientRect();
    if (innerWidth <= 400 && (pg.left + 16 > document.querySelector('.tb').getBoundingClientRect().left + 0.5)) out.push('gutter under 16 px');
    return out;
  });
}

(async () => {
  await new Promise((r) => srv.listen(8813, r));
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  const report = [];
  const sizeKB = Math.round(fs.statSync(path.join(ROOT, 'share', 'animated.html')).size / 1024);
  report.push('share copy ' + sizeKB + ' KB (limit 16 MB); Chrome ' + browser.version());
  for (const [w, h, label] of [[1360, 1000, 'desk'], [390, 844, 'p390']]) {
    for (const scheme of ['dark', 'light']) {
      const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: label === 'desk' ? 1 : 2, colorScheme: scheme });
      const page = await ctx.newPage();
      const errs = [];
      page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
      page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') errs.push('console ' + m.type() + ' ' + m.text().slice(0, 300)); });
      page.on('requestfailed', (r) => errs.push('requestfailed ' + r.url()));
      await page.goto('http://127.0.0.1:8813/share/animated.html');
      await page.evaluate(() => Promise.race([document.fonts.ready, new Promise((r) => setTimeout(r, 4000))]));
      // count the tiles drawn, to show only those on screen are drawn
      await page.evaluate(() => { const E = window.__E; const f = E.tile.bind(E); window.__drawn = {}; E.tile = function (id, c) { if (c && c.canvas && c.canvas.closest('.tile')) window.__drawn[id] = (window.__drawn[id] || 0) + 1; return f.apply(null, arguments); }; });
      await page.waitForFunction(() => window.__libDone, null, { timeout: 120000 }).catch(() => errs.push('the proofs and measurements did not finish in 120 s'));
      await page.waitForTimeout(600);
      const lib = await page.evaluate(() => { const L = window.__lib; return { done: L.done, max: L.max, checks: L.checks, hmin: L.hmin, renderer: L.renderer, rows: Object.fromEntries(Object.entries(L.RES).map(([k, v]) => [k, { max: v.max, half: v.halfMin, halfDef: v.halfDef, ms: v.ms, base: v.base, checks: v.checks }])) }; });
      if (label === 'desk' && scheme === 'dark') {
        report.push('loop proof: ' + lib.done + ' of 24 proved, ' + lib.checks + ' checks, max |frame(0) - frame(T)| = ' + lib.max + '/255; min max-difference at T/2 = ' + lib.hmin);
        report.push('renderer: ' + lib.renderer);
        report.push('per wallpaper (proof max, T/2 difference at default options and at the option extremes, ms per frame at 1280x800): ' + Object.entries(lib.rows).map(([k, v]) => k + ' ' + v.max + '/' + v.halfDef + ',' + v.half + '/' + (v.ms == null ? '-' : v.ms.toFixed(3))).join(', '));
        const ms = Object.values(lib.rows).map((v) => v.ms).filter((x) => x != null);
        report.push('frame cost range: ' + Math.min(...ms).toFixed(3) + ' to ' + Math.max(...ms).toFixed(3) + ' ms (flat-shader floor ' + Object.values(lib.rows)[0].base.toFixed(3) + ' ms)');
        fs.writeFileSync(path.join(__dirname, 'measured.json'), JSON.stringify(lib, null, 1));
      }
      if (lib.max == null || lib.max > 1) errs.push('loop proof max ' + lib.max);
      const lay = await layout(page);

      // the header and each tier, scrolled into view and drawn
      await page.evaluate(() => window.scrollTo(0, 0)); await page.waitForTimeout(700);
      await page.screenshot({ path: path.join(SHOTS, label + '-' + scheme + '-top.png') });
      const offscreen = await page.evaluate(() => { const vis = window.__tiles.filter((t) => t.vis).map((t) => t.w.id); window.__drawn = {}; return vis; });
      await page.waitForTimeout(800);
      const drawn = await page.evaluate(() => Object.keys(window.__drawn));
      const extra = drawn.filter((id) => !offscreen.includes(id));
      report.push(label + ' ' + scheme + ': tiles on screen ' + offscreen.length + ', drawn in 0.8 s ' + drawn.length + (extra.length ? ' (off screen but drawn: ' + extra.join(',') + ')' : ' (none off screen)'));
      if (extra.length) errs.push('off-screen tiles drawn: ' + extra.join(','));
      for (const t of ['vibrant', 'balanced', 'minimal']) {
        const el = await page.$('#tier-' + t);
        await el.scrollIntoViewIfNeeded(); await page.waitForTimeout(900);
        await el.screenshot({ path: path.join(SHOTS, label + '-' + scheme + '-' + t + '.png') });
      }
      // every tile drawn (not blank)
      const blank = await page.evaluate(async () => {
        const out = [];
        for (const t of window.__tiles) {
          t.wrap.scrollIntoView({ block: 'center' });
          await new Promise((r) => setTimeout(r, 120));
          const c = t.ctx.getImageData(0, 0, t.cv.width, t.cv.height).data;
          let mn = 255, mx = 0;
          for (let i = 0; i < c.length; i += 4 * 97) { const l = (c[i] + c[i + 1] + c[i + 2]) / 3; mn = Math.min(mn, l); mx = Math.max(mx, l); }
          if (!t.cv.classList.contains('in') || mx - mn < 4) out.push(t.w.id + ' spread ' + Math.round(mx - mn));
        }
        return out;
      });
      if (blank.length) errs.push('blank or flat tiles: ' + blank.join(', '));

      // the large view
      if (!quick || (label === 'desk' && scheme === 'dark')) {
        const pick = label === 'desk' ? 'tides' : 'borealis';
        await page.evaluate((id) => document.querySelector('.tile[data-id="' + id + '"]').scrollIntoView({ block: 'center' }), pick);
        await page.waitForTimeout(400);
        await page.click('.tile[data-id="' + pick + '"]');
        await page.waitForTimeout(1300);
        await page.screenshot({ path: path.join(SHOTS, label + '-' + scheme + '-view-glass.png') });
        const vlay = await page.evaluate(() => { const o = []; const s = document.querySelector('.vside').getBoundingClientRect(); if (s.right > innerWidth + 1 || s.left < -1) o.push('view panel outside ' + Math.round(s.left) + '..' + Math.round(s.right)); if (document.documentElement.scrollWidth > innerWidth + 1) o.push('sideways scroll with the view open'); return o; });
        lay.push(...vlay);
        if (label === 'desk' && scheme === 'dark') {
          // the pack crossfade: the palette passes through values between the old and the new
          const pal0 = await page.evaluate((id) => Array.from(window.__RT[id].pal), pick);
          await page.click('#vpacks .pk:nth-child(3)');
          await page.waitForTimeout(120);
          const palMid = await page.evaluate((id) => Array.from(window.__RT[id].pal), pick);
          await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-pack-mid.png') });
          await page.waitForTimeout(1500);
          const pal1 = await page.evaluate((id) => Array.from(window.__RT[id].pal), pick);
          await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-pack3.png') });
          const between = pal0.every((v, i) => (palMid[i] - Math.min(v, pal1[i]) > -1e-4) && (Math.max(v, pal1[i]) - palMid[i] > -1e-4));
          const moved = palMid.some((v, i) => Math.abs(v - pal0[i]) > 1e-3) && palMid.some((v, i) => Math.abs(v - pal1[i]) > 1e-3);
          report.push('pack change: palette at 120 ms lies between the old and the new: ' + between + ', and is neither: ' + moved);
          if (!between || !moved) errs.push('the pack change did not crossfade');
          // the speed change: the phase keeps advancing continuously; the picture never jumps
          const before = await page.evaluate((id) => { const r = window.__RT[id]; return { ph: r.phase, sp: r.speedE }; }, pick);
          await page.evaluate(() => { const s = document.getElementById('vspeed'); s.value = '2.5'; s.dispatchEvent(new Event('input', { bubbles: true })); });
          // sampled once per animation frame: the largest step the phase took in one frame, against 2.5x of a frame
          const jumps = await page.evaluate(async (id) => {
            const r = window.__RT[id], T = window.WL_DATA.byId[id].period; let last = r.phase, mx = 0, frames = 0, sum = 0;
            const t0 = performance.now();
            while (performance.now() - t0 < 1500) {
              await new Promise((q) => requestAnimationFrame(q));
              let d = r.phase - last; if (d < -T / 2) d += T;
              mx = Math.max(mx, d); sum += d; frames++; last = r.phase;
            }
            return { maxStep: mx, mean: sum / frames, frames, speedE: r.speedE, secs: (performance.now() - t0) / 1000, total: sum };
          }, pick);
          report.push('speed 1x -> 2.5x: speed eased from ' + before.sp.toFixed(2) + ' to ' + jumps.speedE.toFixed(2) + '; over ' + jumps.frames + ' frames the phase advanced ' + jumps.total.toFixed(2) + ' s in ' + jumps.secs.toFixed(2) + ' s; the largest single-frame step was ' + (jumps.maxStep * 1000).toFixed(1) + ' ms of loop time (2.5x of a 60 Hz frame is 41.7 ms; capped frame 80 ms x 2.5 = 200 ms)');
          if (jumps.maxStep > 0.2 || jumps.total > jumps.secs * 2.5 + 0.05) errs.push('the speed change jumped the phase');
          await page.evaluate(() => { const s = document.getElementById('vspeed'); s.value = '1'; s.dispatchEvent(new Event('input', { bubbles: true })); });
          await page.click('#vglass'); await page.waitForTimeout(900);
          await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-noglass.png') });
          await page.click('#vglass'); await page.waitForTimeout(600);
          // the theme crossfade on the open view
          await page.evaluate(() => window.Kit.theme('light')); await page.waitForTimeout(140);
          await page.screenshot({ path: path.join(SHOTS, 'desk-theme-mid.png') });
          await page.waitForTimeout(1200);
          await page.screenshot({ path: path.join(SHOTS, 'desk-theme-light-view.png') });
          await page.evaluate(() => window.Kit.theme('auto')); await page.waitForTimeout(800);
          await page.click('#vreset'); await page.waitForTimeout(800);
          // the other views, one shot each, at desk only
          if (!quick) {
            for (const id of ['borealis', 'molten', 'spectra', 'lagoon', 'ribbon', 'inkwell', 'daybreak', 'drift']) {
              await page.evaluate((x) => window.__open(x, null), id); await page.waitForTimeout(900);
              await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-' + id + '.png') });
            }
          }
        }
        await page.keyboard.press('Escape'); await page.waitForTimeout(700);
        const closed = await page.evaluate(() => !document.getElementById('vw').classList.contains('on'));
        if (!closed) errs.push('Escape did not close the view');
      }
      // the whole page
      await page.evaluate(() => window.scrollTo(0, 0));
      const H = Math.min(12000, await page.evaluate(() => document.documentElement.scrollHeight));
      await page.setViewportSize({ width: w, height: H });
      await page.waitForTimeout(1800);
      await page.screenshot({ path: path.join(SHOTS, label + '-' + scheme + '-page.png') });
      const lay2 = await layout(page);
      report.push(label + ' ' + scheme + ': errors ' + errs.length + (errs.length ? ' ' + errs.join(' | ') : '') + '; layout ' + ([...new Set(lay.concat(lay2))].join(' | ') || 'ok'));
      await ctx.close();
    }
  }
  await browser.close(); srv.close();
  fs.writeFileSync(path.join(SHOTS, 'report.txt'), report.join('\n') + '\n');
  console.log(report.join('\n'));
})();
