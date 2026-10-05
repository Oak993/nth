// Checks the share copy in headless Chrome (wrapped in the publisher's skeleton, as the kit's pw.cjs does): no page
// errors, no sideways scroll at 390 px and at desktop, nothing escaping its capsule; which notes fit one line in the
// composer (820 px, the design's) per word set; and shots into ../shots/: every note of every set, every countdown
// look's frames, every CUDA look in every state, a played countdown and a played download, and the whole page at
// 390 px in dark and light. Usage: node check.cjs [quick]
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('E:/Droppy/work/tools/pw/node_modules/playwright-core');
const ROOT = path.resolve(__dirname, '..');
const SHOTS = path.join(ROOT, 'shots'); fs.mkdirSync(SHOTS, { recursive: true });
const SKEL = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head><body>';
const quick = process.argv[2] === 'quick';
const srv = http.createServer((req, res) => {
  if (req.url === '/favicon.ico') { res.writeHead(204); res.end(); return; }   // the browser's own request, not the page's
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]));
  if (!f.startsWith(ROOT) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(/[\\/]share[\\/]/.test(f) ? SKEL + fs.readFileSync(f, 'utf8') + '</body></html>' : fs.readFileSync(f));
});
const SETS = ['A', 'B', 'C', 'D'];
const CASES = ['blocked', 'nomic', 'unplug', 'busy', 'dl', 'load', 'avx', 'engine', 'nokey', 'refused', 'quota', 'nonet'];
const LOOKS = ['A', 'B', 'C', 'D'];
const STATES = ['offer', 'dl', 'done', 'fail', 'none'];

async function layout(page) {
  return page.evaluate(() => {
    const out = [];
    if (document.documentElement.scrollWidth > innerWidth + 1) out.push('page scrolls sideways ' + document.documentElement.scrollWidth + '>' + innerWidth);
    for (const el of document.querySelectorAll('.dk-btn,.dk-pick,.dk-tag,.dk-seg button,.dk-qn,.ol')) {
      if (el.scrollWidth > el.clientWidth + 1) out.push('overflow ' + el.className + ' "' + el.textContent.trim().slice(0, 30) + '"');
    }
    for (const el of document.querySelectorAll('.dk-q,.stagecard,.dk-picks')) {
      const r = el.getBoundingClientRect();
      if (r.right > innerWidth + 1 || r.left < -1) out.push('outside the viewport ' + el.className.split(' ')[0] + ' ' + Math.round(r.left) + '..' + Math.round(r.right));
    }
    return out;
  });
}

(async () => {
  await new Promise((r) => srv.listen(8797, r));
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  const report = [];
  const sizeKB = Math.round(fs.statSync(path.join(ROOT, 'share', 'voice-notes.html')).size / 1024);
  report.push('share copy ' + sizeKB + ' KB (limit 16 MB)');
  for (const [w, h, label] of [[1360, 1000, 'desk'], [390, 844, 'p390']]) {
    for (const scheme of ['dark', 'light']) {
      const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: label === 'desk' ? 1 : 2, colorScheme: scheme });
      const page = await ctx.newPage();
      const errs = [];
      page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
      page.on('console', (m) => { if (m.type() === 'error') errs.push('console ' + m.text()); });
      page.on('requestfailed', (r) => errs.push('requestfailed ' + r.url()));
      await page.goto('http://127.0.0.1:8797/share/voice-notes.html');
      await page.evaluate(() => Promise.race([document.fonts.ready, new Promise((r) => setTimeout(r, 4000))]));
      await page.waitForTimeout(500);
      const lay = await layout(page);
      // element shots: the pinned picks bar would cover the lower part of a stage, so it is hidden for them
      const hideBar = await page.addStyleTag({ content: '#bar{visibility:hidden}' });
      const shotsFor = label === 'desk' && scheme === 'dark' && !quick;
      if (shotsFor) {
        // which notes fit one line, at the design's composer width (the dark frame; both frames are the same size)
        const fitRows = [];
        for (const s of SETS) {
          await page.click('#segNS button[data-v="' + s + '"]');
          const cut = [], cutN = [];
          for (const c of CASES) {
            await page.click('#segNC button[data-v="' + c + '"]');
            await page.click('#segND button[data-v="empty"]');
            await page.waitForTimeout(450);
            const r = await page.evaluate(() => {
              const sp = document.querySelector('#stN .cell .lay.note.on>span');
              const nt = document.querySelector('#stN .cell .ntc>div.on');
              return { field: sp.scrollWidth > sp.clientWidth + 1, line: nt.scrollWidth > nt.clientWidth + 1 };
            });
            if (r.field && c !== 'blocked') cut.push(c);
            if (r.line && c !== 'blocked') cutN.push(c);
            await (await page.$('#stN')).screenshot({ path: path.join(SHOTS, 'notes-' + s + '-' + c + '.png') });
          }
          for (const c of ['unplug', 'quota']) {
            await page.click('#segNC button[data-v="' + c + '"]');
            await page.click('#segND button[data-v="text"]');
            await page.waitForTimeout(500);
            await (await page.$('#stN')).screenshot({ path: path.join(SHOTS, 'notes-' + s + '-' + c + '-draft.png') });
          }
          await page.click('#segND button[data-v="empty"]');
          fitRows.push('set ' + s + ': cut short in the empty composer ' + (cut.length ? cut.length + ' (' + cut.join(', ') + ')' : 'none') + '; in the notice line ' + (cutN.length ? cutN.length + ' (' + cutN.join(', ') + ')' : 'none'));
        }
        report.push(...fitRows);
        for (const v of LOOKS) {
          await page.click('#segCL button[data-v="' + v + '"]');
          await page.waitForTimeout(700);
          await (await page.$('#frC')).screenshot({ path: path.join(SHOTS, 'count-' + v + '-frames.png') });
          const ws = await page.evaluate(() => Array.from(document.querySelectorAll('#frC .vpill')).map((p) => p.style.width + ' "' + p.querySelector('.plab').textContent + '"'));
          report.push('countdown ' + v + ' frames: ' + ws.slice(0, 4).join(' | '));
        }
        for (const v of LOOKS) {
          await page.click('#segUL button[data-v="' + v + '"]');
          for (const s of STATES) {
            await page.click('#segUS button[data-v="' + s + '"]');
            await page.waitForTimeout(500);
            await (await page.$('#stU')).screenshot({ path: path.join(SHOTS, 'cuda-' + v + '-' + s + '.png') });
          }
        }
        // a played countdown (3x): before it, at 0:30-ish, in the last ten, finishing
        await page.click('#segCL button[data-v="B"]');
        await page.click('#segCS button[data-v="3"]');
        await page.click('#playC');
        for (const [ms, tag] of [[1400, 'p1'], [4200, 'p2'], [9000, 'p3'], [11800, 'p4']]) {
          await page.waitForTimeout(ms - (tag === 'p1' ? 0 : [1400, 4200, 9000, 11800][['p1', 'p2', 'p3', 'p4'].indexOf(tag) - 1]));
          await (await page.$('#stC')).screenshot({ path: path.join(SHOTS, 'count-play-B-' + tag + '.png') });
        }
        await page.waitForTimeout(1500);
        const after = await page.textContent('#playCL');
        report.push('countdown play ended, button reads "' + after + '"');
        // a played download (look A)
        await page.click('#segUL button[data-v="A"]');
        await page.click('#playU');
        await page.waitForTimeout(4200);
        await (await page.$('#stU')).screenshot({ path: path.join(SHOTS, 'cuda-play-A-mid.png') });
        await page.waitForTimeout(4500);
        await (await page.$('#stU')).screenshot({ path: path.join(SHOTS, 'cuda-play-A-end.png') });
        const st = await page.evaluate(() => document.querySelector('#segUS button[aria-pressed="true"]').textContent);
        report.push('download play ended in state "' + st + '"');
        // picks: take all recommended writes the line
        await page.click('#all');
        report.push('picks line: ' + (await page.textContent('#line')));
        await page.click('#clear');
      }
      if (label === 'desk') await page.screenshot({ path: path.join(SHOTS, 'page-desk-' + scheme + '.png'), fullPage: true });
      if (label === 'p390') {
        await page.screenshot({ path: path.join(SHOTS, 'page-390-' + scheme + '.png'), fullPage: true });
        await (await page.$('#Notes')).screenshot({ path: path.join(SHOTS, 'p390-' + scheme + '-notes.png') });
        await (await page.$('#Countdown')).screenshot({ path: path.join(SHOTS, 'p390-' + scheme + '-countdown.png') });
        await (await page.$('#CUDA')).screenshot({ path: path.join(SHOTS, 'p390-' + scheme + '-cuda.png') });
      }
      await hideBar.evaluate((n) => n.remove());
      const lay2 = await layout(page);
      report.push(label + ' ' + scheme + ': errors ' + errs.length + (errs.length ? ' ' + errs.join(' | ') : '') + '; layout ' + ([...new Set(lay.concat(lay2))].join(' | ') || 'ok'));
      await ctx.close();
    }
  }
  await browser.close(); srv.close();
  fs.writeFileSync(path.join(SHOTS, 'report.txt'), report.join('\n') + '\n');
  console.log(report.join('\n'));
})();
