// Checks the Welcome voice page with the decisions-2026-10-03 Playwright helpers (pw.cjs: launch, open, overflowCheck)
// over a static server of decisions-2026-10-04 that wraps share/ copies and review/index.html in the publisher's
// skeleton (as pw.cjs's serve does for its own folder). One headless browser at a time.
//   node check.cjs [pages|phone|frames|states|review|all]
// Checks: page errors and failed requests, label overflow and sideways scroll (dark 100 %, light 125 %, share copy,
// 390 px), every place's question folding in and each answer swap sampled per frame (the box height and the next
// block's top must move one way with no step over 12 % of the travel), stills of every place in every state, both
// themes, and the review page (frames loaded, overlay, a pick).
const http = require('http'), fs = require('fs'), path = require('path');
const { launch, open, overflowCheck } = require('E:/Droppy/work/design/decisions-2026-10-03/_tools/pw.cjs');
const ROOT = path.resolve(__dirname, '..', '..');           // decisions-2026-10-04
const OUT = path.resolve(__dirname, '..', 'shots', 'page');
fs.mkdirSync(OUT, { recursive: true });
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css', '.png': 'image/png', '.json': 'application/json' };
const SKEL_A = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover"><style>:root{color-scheme:light}body{margin:0;font:14px system-ui,sans-serif;background:#fafafa}img{max-width:100%}[hidden]{display:none!important}</style></head><body>';
function serve(port) {
  const srv = http.createServer((req, res) => {
    const p = decodeURIComponent(req.url.split('?')[0]);
    if (p === '/favicon.ico') { res.writeHead(204); res.end(); return; }   // the browser's own request, not the page's
    const f = path.join(ROOT, p);
    if (!f.startsWith(ROOT) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('nf'); return; }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(f)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
    if (/[\\/]share[\\/][^\\/]+\.html$/.test(f) || /[\\/]review[\\/]index\.html$/.test(f)) { res.end(SKEL_A + fs.readFileSync(f, 'utf8') + '</body></html>'); return; }
    fs.createReadStream(f).pipe(res);
  });
  return new Promise((r) => srv.listen(port, () => r(srv)));
}
const only = process.argv[2] || 'all';
const want = (k) => only === 'all' || only === k;
(async () => {
  const srv = await serve(8805);
  const browser = await launch();
  const R = [];
  const base = 'http://localhost:8805/welcome-voice/';
  if (want('pages')) {
    for (const [file, scale, scheme] of [['welcome-voice.html', 1, 'dark'], ['welcome-voice.html', 1.25, 'light'], ['share/welcome-voice.html', 1, 'light'], ['share/welcome-voice.html', 1, 'dark']]) {
      const { ctx, page, errs } = await open(browser, base + file, { scale, scheme });
      await page.waitForTimeout(1200);
      const tag = file.replace(/[\/.]/g, '_') + '_' + scale + '_' + scheme;
      await page.screenshot({ path: path.join(OUT, tag + '.png'), fullPage: true });
      const st = await page.evaluate(() => ({ title: document.title, bg: getComputedStyle(document.body).backgroundColor }));
      R.push(tag + ': "' + st.title + '" body ' + st.bg + ' errors ' + JSON.stringify(errs) + ' overflow ' + JSON.stringify(await overflowCheck(page, 'body')));
      await ctx.close();
    }
  }
  if (want('phone')) {
    for (const scheme of ['dark', 'light']) {
      const { ctx, page, errs } = await open(browser, base + 'share/welcome-voice.html', { w: 390, h: 900, scheme });
      await page.waitForTimeout(1000);
      await page.screenshot({ path: path.join(OUT, 'phone-' + scheme + '.png'), fullPage: true });
      R.push('phone 390 ' + scheme + ' errors ' + JSON.stringify(errs) + ' overflow ' + JSON.stringify(await overflowCheck(page, 'body')));
      await ctx.close();
    }
  }
  if (want('frames')) {
    // per place: the question folding in, then the Yes swap and (slow PC) the slow-PC swap, sampled on every frame
    const { ctx, page, errs } = await open(browser, base + 'welcome-voice.html', { w: 1280, h: 1000, scheme: 'dark' });
    await page.addStyleTag({ content: '.dk-picks{display:none!important}' });
    const letters = await page.evaluate(() => Array.from(document.querySelectorAll('#segV button')).map((b) => b.dataset.v));
    for (const L of letters) {
      await page.click('#segV button[data-v="' + L + '"]');
      await page.click('#segA button[data-v="yes"]');
      await page.click('#segM button[data-v="slow"]');
      await page.waitForTimeout(600);
      const issues = [];
      for (const [label, key] of [['fold in', 'qIn'], ['Yes', 'tAns'], ['slow-PC question', 'slowIn'], ['the pick', 'slowAns']]) {
        const r = await page.evaluate(async (key) => {
          const g = window.__wv; g.frame(g.plan[key] - 0.02);
          await new Promise((res) => setTimeout(res, 800));
          const w = document.querySelector('#slots .slot:last-child .grw');
          const q = w.querySelector('.vq'), stk = q.querySelector('.stk2'), fin = q.querySelector('.fin');
          // the block after the question in flow (or the question's own box for overlays)
          const nxt = { A: '.wact2', B: '.wbtn2', C: '.wprov2', D: '.vqD .fin', E: '.vqE .fin', F: '.wact2' }[q.dataset.place];
          const nx = w.querySelector(nxt);
          g.frame(g.plan[key] + 0.001);
          const rows = [];
          await new Promise((res) => { const t0 = performance.now(); const f = (n) => { const b = nx.getBoundingClientRect(), fb = fin.getBoundingClientRect(); rows.push([n - t0, b.top, fb.height, fb.top, +getComputedStyle(q.querySelector('.fold')).opacity]); if (n - t0 < 900) requestAnimationFrame(f); else res(); }; requestAnimationFrame(f); });
          return rows;
        }, key);
        for (const [k, nm] of [[1, 'next block top'], [2, 'question height'], [3, 'question top']]) {
          const xs = r.map((x) => x[k]), total = xs[xs.length - 1] - xs[0];
          let maxStep = 0, back = 0; const backs = [];
          // the step per 60 Hz frame: a long frame of the headless browser (two vsyncs) is divided by its length
          for (let i = 1; i < xs.length; i++) { const d = xs[i] - xs[i - 1], fr = Math.max(1, (r[i][0] - r[i - 1][0]) / 16.67); maxStep = Math.max(maxStep, Math.abs(d) / fr); if (Math.abs(total) > 1 && Math.abs(d) > 0.3 && Math.sign(d) !== Math.sign(total)) { back++; backs.push(nm + ' ' + d.toFixed(1) + ' at ' + r[i][0].toFixed(0) + ' ms'); } }
          if (Math.abs(total) > 3 && maxStep / Math.abs(total) > 0.12) issues.push(label + ': ' + nm + ' step ' + maxStep.toFixed(1) + ' of ' + total.toFixed(1));
          if (back) issues.push(label + ': ' + nm + ' reverses ' + back + 'x (' + backs.join(', ') + ')');
          if (Math.abs(total) <= 3 && maxStep > 3) issues.push(label + ': ' + nm + ' jumps ' + maxStep.toFixed(1) + ' px');
        }
        let mo = 0; for (let i = 1; i < r.length; i++) mo = Math.max(mo, Math.abs(r[i][4] - r[i - 1][4]) / Math.max(1, (r[i][0] - r[i - 1][0]) / 16.67));
        if (mo > 0.2) issues.push(label + ': opacity step ' + mo.toFixed(2));
      }
      R.push('place ' + L + ': ' + (issues.length ? issues.join('; ') : 'fold-in, Yes, slow-PC question and pick all eased (no step > 12 % of travel per 60 Hz frame, no reversal, opacity steps <= 0.2)'));
    }
    R.push('frames errors ' + JSON.stringify(errs));
    await ctx.close();
  }
  if (want('states')) {
    // stills: every place in each state, dark and light, plus the composer states
    for (const scheme of ['dark', 'light']) {
      const { ctx, page, errs } = await open(browser, base + 'welcome-voice.html', { w: 1280, h: 1000, scheme });
      await page.addStyleTag({ content: '.dk-picks{display:none!important}' });
      const letters = await page.evaluate(() => Array.from(document.querySelectorAll('#segV button')).map((b) => b.dataset.v));
      for (const L of letters) {
        await page.click('#segV button[data-v="' + L + '"]');
        for (const [ans, mach, at, tag] of [['yes', 'slow', 'qIn', 'ask'], ['yes', 'slow', 'tAns', 'yes'], ['no', 'slow', 'tAns', 'no'], ['yes', 'slow', 'slowIn', 'slow'], ['yes', 'slow', 'slowAns', 'picked'], ['yes', 'pc', 'ready', 'ready']]) {
          await page.click('#segA button[data-v="' + ans + '"]');
          await page.click('#segM button[data-v="' + mach + '"]');
          await page.waitForTimeout(450);
          await page.evaluate((at) => { const g = window.__wv; g.frame(g.plan[at] + (at === 'tAns' ? 1.5 : at === 'qIn' ? 0.9 : 0.9)); }, at);
          await page.waitForTimeout(800);
          await (await page.$('#stage')).screenshot({ path: path.join(OUT, L + '-' + tag + '-' + scheme + '.png') });
        }
      }
      for (const c of ['dl', 'nodl', 'ask', 'listen']) {
        await page.click('#segC button[data-v="' + c + '"]');
        await page.waitForTimeout(600);
        await (await page.$('#cmStage')).screenshot({ path: path.join(OUT, 'orb-' + c + '-' + scheme + '.png') });
      }
      R.push('states ' + scheme + ' errors ' + JSON.stringify(errs));
      await ctx.close();
    }
    // a real-time play, slow PC, place A: frame pacing of the page
    const { ctx, page, errs } = await open(browser, base + 'welcome-voice.html?autoplay=1', { scheme: 'dark' });
    const gaps = await page.evaluate(() => new Promise((res) => { const ts = []; const f = (n) => { ts.push(n); if (ts.length < 240) requestAnimationFrame(f); else res(ts.slice(1).map((x, i) => x - ts[i])); }; requestAnimationFrame(f); }));
    gaps.sort((a, b) => a - b);
    R.push('play pacing p50 ' + gaps[120].toFixed(1) + ' ms, p99 ' + gaps[236].toFixed(1) + ' ms, max ' + gaps[gaps.length - 1].toFixed(1) + ' ms; errors ' + JSON.stringify(errs));
    await ctx.close();
  }
  if (want('review')) {
    const RO = path.resolve(__dirname, '..', 'shots', 'review');
    fs.mkdirSync(RO, { recursive: true });
    for (const [w, scale, scheme] of [[1280, 1, 'dark'], [1280, 1.25, 'light'], [400, 1, 'light'], [400, 1, 'dark']]) {
      const { ctx, page, errs } = await open(browser, base + 'review/index.html', { w, h: 900, scale, scheme });
      const H = await page.evaluate(() => document.documentElement.scrollHeight);
      for (let y = 0; y < H; y += 700) { await page.evaluate((y) => scrollTo(0, y), y); await page.waitForTimeout(w === 400 ? 140 : 240); }
      await page.waitForTimeout(1500);
      const st = await page.evaluate(() => ({ frames: document.querySelectorAll('.rv-bf').length, loaded: document.querySelectorAll('.rv-bf.on').length, wide: document.documentElement.scrollWidth > innerWidth + 1, title: document.title, bg: getComputedStyle(document.body).backgroundColor }));
      const ov = await overflowCheck(page, '.dk-page');
      const tag = w + '_' + scale + '_' + scheme;
      let extra = '';
      await page.evaluate(() => scrollTo(0, 0)); await page.waitForTimeout(300);
      await page.screenshot({ path: path.join(RO, 'top-' + tag + '.png') });
      if (w === 1280 && scale === 1) {
        await page.evaluate(() => document.querySelector('.rv-ex[data-t="light"]').click());
        await page.waitForTimeout(1600);
        await page.screenshot({ path: path.join(RO, 'overlay-light.png') });
        await page.evaluate(() => document.querySelector('#rvOvTheme button[data-v="dark"]').click());
        await page.waitForTimeout(700);
        await page.screenshot({ path: path.join(RO, 'overlay-dark.png') });
        await page.keyboard.press('Escape'); await page.waitForTimeout(400);
        const closed = await page.evaluate(() => !document.getElementById('rvOv').classList.contains('open'));
        await page.evaluate(() => document.querySelector('.rv-op .dk-pick').click());
        await page.waitForTimeout(200);
        const line = await page.evaluate(() => document.getElementById('rvLine').textContent);
        extra = ' overlay closed ' + closed + '; line "' + line + '"';
        await page.evaluate(() => document.querySelector('.rv-card').scrollIntoView()); await page.waitForTimeout(600);
        await page.screenshot({ path: path.join(RO, 'card-' + tag + '.png') });
      }
      if (w === 400 && scheme === 'light') await page.screenshot({ path: path.join(RO, 'phone-full.png'), fullPage: true });
      R.push('review ' + tag + ': "' + st.title + '" frames ' + st.loaded + '/' + st.frames + ' loaded, sideways ' + st.wide + ', body ' + st.bg + ', errors ' + JSON.stringify(errs) + ', overflow ' + JSON.stringify(ov) + extra);
      await ctx.close();
    }
  }
  fs.writeFileSync(path.join(OUT, 'report-' + only + '.txt'), R.join('\n') + '\n');
  console.log(R.join('\n'));
  await browser.close(); srv.close();
})();
