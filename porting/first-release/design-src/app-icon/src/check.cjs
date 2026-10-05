// Checks the share copy of the app-icon page in headless Chrome, wrapped in the publisher's skeleton: no page errors,
// no failed requests, nothing sent; no sideways scroll at 1360 and 390 px in dark and light; nothing escaping its box;
// every icon draws at every size without throwing; the launch ends on the still icon and the idle loop closes; the pick
// writes "app-icon: X"; Reduce Motion gives a fade only. Shots go to ../shots/. Usage: node check.cjs [quick]
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('E:/Droppy/work/tools/pw/node_modules/playwright-core');
const ROOT = path.resolve(__dirname, '..');
const SHOTS = path.join(ROOT, 'shots'); fs.mkdirSync(SHOTS, { recursive: true });
const SKEL = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head><body>';
const quick = process.argv[2] === 'quick';
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
    for (const el of document.querySelectorAll('.dk-btn,.dk-pick,.dk-tag,.dk-seg button,.dk-qn')) if (el.scrollWidth > el.clientWidth + 1) out.push('overflow ' + el.className + ' "' + el.textContent.trim().slice(0, 30) + '"');
    for (const el of document.querySelectorAll('.dk-q,.ibar,.srow,.task,.start,.alt,.desk,.hero,.t256')) {
      const r = el.getBoundingClientRect();
      if (r.width && (r.right > innerWidth + 1 || r.left < -1)) out.push('outside the viewport ' + el.className.split(' ')[0] + ' ' + Math.round(r.left) + '..' + Math.round(r.right));
    }
    return [...new Set(out)].slice(0, 12);
  });
}
(async () => {
  await new Promise((r) => srv.listen(8798, r));
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  const report = [];
  report.push('share copy ' + Math.round(fs.statSync(path.join(ROOT, 'share', 'app-icon.html')).size / 1024) + ' KB (limit 16 MB)');
  for (const [w, h, label] of [[1360, 1000, 'desk'], [390, 844, 'p390']]) {
    for (const scheme of ['dark', 'light']) {
      const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: label === 'desk' ? 1 : 2, colorScheme: scheme });
      const page = await ctx.newPage();
      const errs = [], sent = [];
      page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
      page.on('console', (m) => { if (m.type() === 'error') errs.push('console ' + m.text()); });
      page.on('requestfailed', (r) => errs.push('requestfailed ' + r.url()));
      page.on('request', (r) => { const u = r.url(); if (!u.startsWith('http://127.0.0.1:8798/') && !u.startsWith('https://fonts.g') && !u.startsWith('data:')) sent.push(u); });
      await page.goto('http://127.0.0.1:8798/share/app-icon.html');
      await page.evaluate(() => Promise.race([document.fonts.ready, new Promise((r) => setTimeout(r, 4000))]));
      await page.waitForTimeout(400);
      // draw everything (the page draws lazily), and time it
      const t = await page.evaluate(() => { const a = performance.now(); window.__icons.reg.forEach((e) => { if (!e.done) window.__icons.draw(e); }); window.__icons.stages.forEach((s) => s.drawStill()); return Math.round(performance.now() - a); });
      const letters = await page.evaluate(() => window.__icons.mods().join(''));
      const lay = await layout(page);
      if (label === 'desk' && scheme === 'dark' && !quick) {
        report.push('letters ' + letters + '; drawing every canvas took ' + t + ' ms');
        // the contract, per module: launch end = still, idle loop closes, every size draws
        const c = await page.evaluate(() => {
          const out = [];
          MarrowIcons.all().forEach((m) => {
            const diff = (a, b, s) => { const c1 = document.createElement('canvas'), c2 = document.createElement('canvas'); MarrowIcons.render(c1, m, s, 'dark', a, 1); MarrowIcons.render(c2, m, s, 'dark', b, 1); const d1 = c1.getContext('2d').getImageData(0, 0, s, s).data, d2 = c2.getContext('2d').getImageData(0, 0, s, s).data; let mx = 0; for (let i = 0; i < d1.length; i++) mx = Math.max(mx, Math.abs(d1[i] - d2[i])); return mx; };
            const bad = [];
            for (const s of [16, 20, 24, 32, 40, 48, 64, 72, 96, 256, 512]) { try { MarrowIcons.render(document.createElement('canvas'), m, s, 'light', { launch: 0.5, idle: 0 }, 1); } catch (e) { bad.push(s + ' throws ' + e.message); } }
            for (const s of [16, 48, 256]) {
              const d0 = diff({ launch: 1, idle: 0 }, { launch: 0.99999, idle: 0 }, s); if (d0 > 6) bad.push('launch jumps at its end, ' + s + ' px, by ' + d0);
              if (m.idle) { const d1 = diff({ launch: 1, idle: 0 }, { launch: 1, idle: m.idle.ms / 1000 }, s); if (d1 > 3) bad.push('idle does not close at ' + s + ' px (' + d1 + ')'); }
            }
            if (!(m.launch.ms >= 800 && m.launch.ms <= 1200)) bad.push('launch ' + m.launch.ms + ' ms');
            // frame cost at the hero, device scale 2
            const cv = document.createElement('canvas'); const a = performance.now(); for (let i = 0; i < 20; i++) MarrowIcons.render(cv, m, 512, 'dark', { launch: i / 20, idle: 0 }, 2); const l = (performance.now() - a) / 20;
            const a2 = performance.now(); for (let i = 0; i < 20; i++) MarrowIcons.render(cv, m, 512, 'dark', { launch: 1, idle: i * 0.13 }, 2); const id = (performance.now() - a2) / 20;
            out.push(m.letter + ' ' + m.name + ': ' + (bad.length ? bad.join('; ') : 'ok') + ' (launch ' + l.toFixed(1) + ' ms, idle ' + id.toFixed(1) + ' ms per frame)');
          });
          return out;
        });
        report.push(...c);
        // shots: the comparison, then every card
        const hide = await page.addStyleTag({ content: '#bar{visibility:hidden}' });
        await (await page.$('#compare')).screenshot({ path: path.join(SHOTS, 'compare-dark.png') });
        for (const L of letters.split('')) await (await page.$('#ic-' + L)).screenshot({ path: path.join(SHOTS, 'card-' + L + '-dark.png') });
        // a launch caught mid-way, and its end
        await page.evaluate(() => document.getElementById('ic-A').scrollIntoView());
        await page.click('#ic-A .hctl .dk-btn.pri'); await page.waitForTimeout(450);
        await (await page.$('#ic-A .hero')).screenshot({ path: path.join(SHOTS, 'play-A-mid.png') });
        await page.waitForTimeout(1000);
        await (await page.$('#ic-A .hero')).screenshot({ path: path.join(SHOTS, 'play-A-end.png') });
        const op = await page.evaluate(() => [getComputedStyle(document.querySelector('#ic-A .hero .still')).opacity, getComputedStyle(document.querySelector('#ic-A .hero .live')).opacity].join('/'));
        report.push('after a launch, still/live opacity ' + op);
        // the idle loop: on, then off fades back
        await page.click('#ic-B .hctl .dk-btn:not(.pri)'); await page.waitForTimeout(1500);
        const on = await page.evaluate(() => document.querySelector('#ic-B .hctl .dk-btn:not(.pri)').getAttribute('aria-pressed'));
        await page.click('#ic-B .hctl .dk-btn:not(.pri)'); await page.waitForTimeout(700);
        const off = await page.evaluate(() => [document.querySelector('#ic-B .hctl .dk-btn:not(.pri)').getAttribute('aria-pressed'), getComputedStyle(document.querySelector('#ic-B .hero .live')).opacity].join('/'));
        report.push('idle B: pressed ' + on + ', after off ' + off);
        // the pick
        await page.click('#ic-C .dk-pick');
        report.push('pick C: bar reads "' + (await page.textContent('#pline')) + '"');
        await page.click('#ic-C .dk-pick');
        report.push('unpick: bar reads "' + (await page.textContent('#pline')) + '"');
        // Reduce Motion: the launch is a fade, idle is off
        await page.click('#segM button[data-v="reduced"]');
        await page.click('#ic-D .hctl .dk-btn.pri'); await page.waitForTimeout(150);
        const rm = await page.evaluate(() => [getComputedStyle(document.querySelector('#ic-D .hero .live')).opacity, document.querySelector('#ic-D .hctl .dk-btn:not(.pri)').disabled].join('/'));
        report.push('reduced motion: live opacity/idle disabled ' + rm);
        await page.click('#segM button[data-v="full"]');
        // the theme switch redraws the page-theme canvases
        await page.click('#segT button[data-v="light"]'); await page.waitForTimeout(700);
        await (await page.$('#ic-E')).screenshot({ path: path.join(SHOTS, 'card-E-pageLight.png') });
        await page.click('#segT button[data-v="auto"]'); await page.waitForTimeout(500);
        // play every launch in the comparison
        await page.evaluate(() => document.getElementById('compare').scrollIntoView());
        const paceFn = () => page.evaluate(() => new Promise((res) => { const ts = []; const f = (t) => { ts.push(t); if (ts.length < 70) requestAnimationFrame(f); else { const d = ts.slice(1).map((x, i) => x - ts[i]).sort((a, b) => a - b); res('median ' + d[d.length >> 1].toFixed(1) + ' ms, worst ' + d[d.length - 1].toFixed(1) + ' ms'); } }; requestAnimationFrame(f); }));
        await page.click('#compare .dk-btn.pri'); await page.waitForTimeout(500);
        await (await page.$('#compare .c256')).screenshot({ path: path.join(SHOTS, 'compare-playing.png') });
        await page.waitForTimeout(1400);
        // pacing, measured on a second run with no screenshot in the way
        const pace = paceFn(); await page.click('#compare .dk-btn.pri');
        report.push('frame pacing while every launch plays at 256 px: ' + (await pace));
        await page.evaluate(() => document.getElementById('ic-D').scrollIntoView());
        const pace2 = paceFn(); await page.click('#ic-D .hctl .dk-btn.pri');
        report.push('frame pacing of one hero launch (D, 512 px): ' + (await pace2));
        await page.click('#ic-D .hctl .dk-btn:not(.pri)'); const pace3 = paceFn();
        report.push('frame pacing of the hero idle loop (D): ' + (await pace3)); await page.click('#ic-D .hctl .dk-btn:not(.pri)');
        await page.waitForTimeout(600);
        await hide.evaluate((n) => n.remove());
      }
      if (label === 'desk' && !quick) await page.screenshot({ path: path.join(SHOTS, 'page-desk-' + scheme + '-top.png') });
      if (label === 'p390' && !quick) {
        await page.screenshot({ path: path.join(SHOTS, 'page-390-' + scheme + '-top.png') });
        const hide = await page.addStyleTag({ content: '#bar{visibility:hidden}' });
        await (await page.$('#ic-A')).screenshot({ path: path.join(SHOTS, 'p390-' + scheme + '-A.png') });
        await (await page.$('#compare')).screenshot({ path: path.join(SHOTS, 'p390-' + scheme + '-compare.png') });
        await hide.evaluate((n) => n.remove());
      }
      const lay2 = await layout(page);
      report.push(label + ' ' + scheme + ': errors ' + errs.length + (errs.length ? ' ' + errs.slice(0, 5).join(' | ') : '') + '; sent ' + sent.length + '; layout ' + ([...new Set(lay.concat(lay2))].join(' | ') || 'ok'));
      await ctx.close();
    }
  }
  await browser.close(); srv.close();
  fs.writeFileSync(path.join(SHOTS, 'report.txt'), report.join('\n') + '\n');
  console.log(report.join('\n'));
})();
