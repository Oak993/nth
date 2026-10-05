// Checks the share copy in headless Chrome, wrapped in the publisher's skeleton (as the kit's pw.cjs does): no page
// errors, no sideways scroll at 1360 and 390 px, nothing escaping its box, every thumbnail drawn (not blank, no NaN
// black), the large view drawn, packs / option / grain / glass / theme transitions, and the single-render cost of all
// thirty at 2560 x 1600 written to measured.json (build_page.py puts it into the page). Shots go to ../shots/.
// Usage: node check.cjs [walls] [keep]   walls: also shoot every wallpaper large, dark and light, glass on and off.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('E:/Droppy/work/tools/pw/node_modules/playwright-core');
const ROOT = path.resolve(__dirname, '..');
const SHOTS = path.join(ROOT, 'shots'); fs.mkdirSync(SHOTS, { recursive: true });
const SKEL = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head><body>';
const walls = process.argv.includes('walls'), keep = process.argv.includes('keep');
const srv = http.createServer((req, res) => {
  if (req.url === '/favicon.ico') { res.writeHead(204); res.end(); return; }
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]));
  if (!f.startsWith(ROOT) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(/[\\/]share[\\/]/.test(f) ? SKEL + fs.readFileSync(f, 'utf8') + '</body></html>' : fs.readFileSync(f));
});

async function layout(page) {
  return page.evaluate(() => {
    const out = [];
    if (document.documentElement.scrollWidth > innerWidth + 1) out.push('page scrolls sideways ' + document.documentElement.scrollWidth + '>' + innerWidth);
    for (const el of document.querySelectorAll('.dk-btn,.dk-tag,.dk-seg button,.tag,.swt,.cm b')) {
      if (el.scrollWidth > el.clientWidth + 1) out.push('overflow ' + el.className + ' "' + el.textContent.trim().slice(0, 30) + '"');
    }
    for (const el of document.querySelectorAll('.tb,.view,.tier:not(.off),.costcard,.dk-note,.card')) {
      const r = el.getBoundingClientRect();
      if (r.width && (r.right > innerWidth + 1 || r.left < -1)) out.push('outside the viewport ' + el.className.split(' ')[0] + ' ' + Math.round(r.left) + '..' + Math.round(r.right));
    }
    for (const el of document.querySelectorAll('.mock .cap,.mock .bub,.mock .pn,.mock .ttl,.mock .cmpt,.mock .row span')) {
      if (el.scrollWidth > el.clientWidth + 1 || el.scrollHeight > el.clientHeight + 1) out.push('mock text overflows "' + el.textContent.trim().slice(0, 24) + '"');
    }
    return out;
  });
}
// every drawn thumbnail: not blank, not flat, and no run of pure black (a NaN in the shader shows as black)
async function thumbsOk(page) {
  return page.evaluate(() => {
    const bad = [], seen = [];
    for (const [id, t] of Object.entries(window.__sw.thumbs)) {
      if (t.cur < 0) continue;
      const cv = t.cvs[t.cur], ctx = cv.getContext('2d');
      const d = ctx.getImageData(0, 0, cv.width, cv.height).data;
      let mn = 255, mx = 0, black = 0, n = 0;
      for (let i = 0; i < d.length; i += 4 * 37) { const l = (d[i] + d[i + 1] + d[i + 2]) / 3; mn = Math.min(mn, l); mx = Math.max(mx, l); if (d[i] + d[i + 1] + d[i + 2] < 3) black++; n++; }
      seen.push(id);
      if (mx - mn < 2) bad.push(id + ' flat');
      if (black / n > .02) bad.push(id + ' black ' + Math.round(black / n * 100) + '%');
    }
    return { seen: seen.length, bad };
  });
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

(async () => {
  await new Promise((r) => srv.listen(8799, r));
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  const report = [];
  const sizeKB = Math.round(fs.statSync(path.join(ROOT, 'share', 'static.html')).size / 1024);
  report.push('share copy ' + sizeKB + ' KB (limit 16 MB); Chrome ' + browser.version());
  let measured = null;
  for (const [w, h, label] of [[1360, 1000, 'desk'], [390, 844, 'p390']]) {
    for (const scheme of ['dark', 'light']) {
      const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: label === 'desk' ? 1 : 2, colorScheme: scheme });
      const page = await ctx.newPage();
      const errs = [];
      page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
      page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') errs.push('console ' + m.type() + ' ' + m.text()); });
      page.on('requestfailed', (r) => errs.push('requestfailed ' + r.url()));
      await page.goto('http://127.0.0.1:8799/share/static.html');
      await page.evaluate(() => Promise.race([document.fonts.ready, new Promise((r) => setTimeout(r, 4000))]));
      await page.waitForTimeout(1200);
      const tag = label + '-' + scheme;
      await page.screenshot({ path: path.join(SHOTS, tag + '-top.png') });
      await (await page.$('#view')).screenshot({ path: path.join(SHOTS, tag + '-view.png') });
      const lay = await layout(page);
      // each tier, scrolled to, so its thumbnails draw
      for (const t of ['vibrant', 'balanced', 'minimal']) {
        const el = await page.$('#t-' + t); await el.scrollIntoViewIfNeeded(); await page.waitForTimeout(900);
        await el.screenshot({ path: path.join(SHOTS, tag + '-tier-' + t + '.png') });
      }
      const th = await thumbsOk(page);
      report.push(tag + ': thumbnails drawn ' + th.seen + '/30' + (th.bad.length ? ' BAD ' + th.bad.join(', ') : ''));
      if (th.bad.length) errs.push('thumbs ' + th.bad.join(','));

      if (label === 'desk' && scheme === 'dark') {
        // interactions: a pack crossfades (the view stays busy for a while, then settles), the glass fades, a theme change crossfades
        await page.evaluate(() => window.scrollTo(0, 0)); await page.waitForTimeout(300);
        await page.click('.card[data-id="riptide"]'); await page.waitForTimeout(900);
        const n0 = await page.evaluate(() => window.__sw.V.st.n);
        await page.click('#packs .pk[data-i="1"]');
        await page.waitForTimeout(120); await (await page.$('#vst')).screenshot({ path: path.join(SHOTS, 'x-pack-mid.png') });
        const mid = await page.evaluate(() => window.__sw.V.st.busy);
        await page.waitForTimeout(1500); await (await page.$('#vst')).screenshot({ path: path.join(SHOTS, 'x-pack-end.png') });
        const n1 = await page.evaluate(() => window.__sw.V.st.n);
        await page.waitForTimeout(800);
        const n2 = await page.evaluate(() => window.__sw.V.st.n);
        report.push('pack switch: busy mid-way ' + mid + ', frames drawn for the crossfade ' + (n1 - n0) + ', frames after it settled ' + (n2 - n1) + ' (0 expected)');
        await page.click('#glass'); await page.waitForTimeout(150); await (await page.$('#vst')).screenshot({ path: path.join(SHOTS, 'x-glass-mid.png') });
        await page.waitForTimeout(900); await (await page.$('#vst')).screenshot({ path: path.join(SHOTS, 'x-glass-off.png') });
        await page.click('#glass'); await page.waitForTimeout(900);
        await page.click('#segT button[data-v="light"]'); await page.waitForTimeout(160); await page.screenshot({ path: path.join(SHOTS, 'x-theme-mid.png') });
        await page.waitForTimeout(1200); await page.screenshot({ path: path.join(SHOTS, 'x-theme-light.png') });
        await page.click('#segT button[data-v="auto"]'); await page.waitForTimeout(1200);
        await page.click('#packs .pk[data-i="0"]'); await page.waitForTimeout(900);
        await page.click('#segTier button[data-v="minimal"]'); await page.waitForTimeout(700);
        report.push('tier filter minimal: ' + (await page.textContent('#cnt')));
        await page.click('#segTier button[data-v="all"]'); await page.waitForTimeout(700);
        await page.click('#codeb'); await page.waitForTimeout(600);
        await (await page.$('#view')).screenshot({ path: path.join(SHOTS, 'x-shader-open.png') });
        await page.click('#codeb'); await page.waitForTimeout(500);
        // the cost of one 2560 x 1600 render of each
        await page.evaluate(() => document.getElementById('Cost').scrollIntoView());
        // measured twice; the slower median of the two is kept for each wallpaper (GPU clocks vary between runs)
        await page.evaluate(() => window.__measure());
        const m1 = await page.evaluate(() => window.__measured);
        await page.evaluate(() => window.__measure());
        measured = await page.evaluate(() => window.__measured);
        if (m1 && measured) for (const k of Object.keys(measured.rows)) if (m1.rows[k].med > measured.rows[k].med) measured.rows[k] = m1.rows[k];
        await (await page.$('#Cost')).screenshot({ path: path.join(SHOTS, 'cost-desk-dark.png') });
        if (measured) {
          const v = Object.entries(measured.rows).map(([k, r]) => [k, r.med]).sort((a, b) => a[1] - b[1]);
          report.push('2560x1600 single render on ' + measured.renderer + ': ' + v.map(([k, m]) => k + ' ' + m.toFixed(2)).join(', '));
          measured.chrome = browser.version();
          if (!keep) fs.writeFileSync(path.join(__dirname, 'measured.json'), JSON.stringify(measured, null, 1));
        }
        if (walls) {
          const ids = await page.evaluate(() => window.__sw.W.map((w) => w.id));
          await page.evaluate(() => window.scrollTo(0, 0));
          for (const sch of ['dark', 'light']) {
            await page.click('#segT button[data-v="' + sch + '"]'); await page.waitForTimeout(900);
            for (const id of ids) {
              await page.evaluate((i) => window.__sw.select(i, false), id); await page.waitForTimeout(950);
              await (await page.$('#vst')).screenshot({ path: path.join(SHOTS, 'w-' + id + '-' + sch + '.png') });
            }
          }
          await page.click('#glass'); await page.waitForTimeout(600);
          for (const id of ids) { await page.evaluate((i) => window.__sw.select(i, false), id); await page.waitForTimeout(800); await (await page.$('#vst')).screenshot({ path: path.join(SHOTS, 'bare-' + id + '-light.png') }); }
          await page.click('#glass'); await page.click('#segT button[data-v="auto"]'); await page.waitForTimeout(600);
          await page.evaluate(() => window.__sw.select('riptide', false));
        }
      }
      if (label === 'p390') {
        await page.evaluate(() => window.scrollTo(0, 0)); await page.waitForTimeout(400);
      }
      // whole page, made as tall as the page so every thumbnail is drawn
      await page.evaluate(() => window.scrollTo(0, 0));
      const H = Math.min(12000, await page.evaluate(() => document.documentElement.scrollHeight));
      await page.setViewportSize({ width: w, height: H });
      await page.waitForTimeout(2200);
      await page.screenshot({ path: path.join(SHOTS, tag + '-page.png') });
      const lay2 = await layout(page);
      report.push(tag + ': errors ' + errs.length + (errs.length ? ' ' + errs.join(' | ') : '') + '; layout ' + ([...new Set(lay.concat(lay2))].join(' | ') || 'ok'));
      await ctx.close();
    }
  }
  await browser.close(); srv.close();
  fs.writeFileSync(path.join(SHOTS, 'report.txt'), report.join('\n') + '\n');
  console.log(report.join('\n'));
})();
