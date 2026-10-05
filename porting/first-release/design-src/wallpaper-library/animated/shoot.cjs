// Dev shots: node shoot.cjs <id,id,...|tier> [mode]  mode: times (default) | packs | opts | proof | bench
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('E:/Droppy/work/tools/pw/node_modules/playwright-core');
const ROOT = path.resolve(__dirname, '..');
const OUT = path.join(ROOT, 'shots', 'dev'); fs.mkdirSync(OUT, { recursive: true });
const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json' };
const srv = http.createServer((req, res) => {
  const f = path.join(ROOT, decodeURIComponent(req.url.split('?')[0]));
  if (!f.startsWith(ROOT) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end(); return; }
  res.writeHead(200, { 'Content-Type': (types[path.extname(f)] || 'application/octet-stream') + '; charset=utf-8' }); res.end(fs.readFileSync(f));
});
(async () => {
  await new Promise(r => srv.listen(8811, r));
  const data = JSON.parse(fs.readFileSync(path.join(__dirname, 'wallpapers.json'), 'utf8'));
  let ids = process.argv[2] || 'all';
  const tiers = ['vibrant', 'balanced', 'minimal'];
  if (process.argv[3] === 'dark') ids = [];
  else ids = ids === 'all' ? data.wallpapers.map(w => w.id) : tiers.includes(ids) ? data.wallpapers.filter(w => w.tier === ids).map(w => w.id) : ids.split(',');
  const mode = process.argv[3] || 'times';
  const W = +(process.argv[4] || 640), H = Math.round(W * 10 / 16);
  const b = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  const p = await (await b.newContext({ viewport: { width: 800, height: 600 } })).newPage();
  p.on('console', m => { if (m.type() === 'error') console.log('console:', m.text().slice(0, 900)); });
  p.on('pageerror', e => console.log('pageerror', e.message));
  await p.goto('http://127.0.0.1:8811/src/_dev/dev.html');
  await p.evaluate(() => window.devReady);
  if (mode === 'dark') {
    const pairs = process.argv[2].split(',').map(x => x.split(':'));
    const url = await p.evaluate(([pp, w2, h2]) => darkSheet(pp, w2, h2), [pairs, W, H]);
    if (!url.startsWith('data:')) console.log(url.slice(0, 900)); else fs.writeFileSync(path.join(OUT, 'dark-' + (process.argv[5] || 'sheet') + '.png'), Buffer.from(url.split(',')[1], 'base64'));
    await b.close(); srv.close(); return;
  }
  if (mode === 'packs2') {
    for (let i = 0; i < ids.length; i += 2) {
      const pair = ids.slice(i, i + 2);
      const url = await p.evaluate(([i2, w2, h2]) => packSheet(i2, w2, h2), [pair, W, H]);
      if (!url.startsWith('data:')) { console.log(url.slice(0, 900)); continue; }
      fs.writeFileSync(path.join(OUT, 'packs-' + pair.join('-') + '.png'), Buffer.from(url.split(',')[1], 'base64'));
    }
    await b.close(); srv.close(); return;
  }
  if (mode.startsWith('sheet')) {
    const t = +(process.argv[5] || 0), pk = +(process.argv[6] || 0);
    const url = await p.evaluate(([i, w2, h2, tt, pp]) => tierSheet(i, w2, h2, tt, pp), [ids, W, H, t, pk]);
    if (!url.startsWith('data:')) console.log(url.slice(0, 1500));
    else fs.writeFileSync(path.join(OUT, 'sheet-' + (process.argv[2]) + '-' + mode + '.png'), Buffer.from(url.split(',')[1], 'base64'));
    await b.close(); srv.close(); return;
  }
  for (const id of ids) {
    const w = data.wallpapers.find(x => x.id === id);
    if (mode === 'proof') { console.log(id + '\n' + await p.evaluate(i => proofs(i), id)); continue; }
    if (mode === 'bench') { console.log(await p.evaluate(i => bench(i), id)); continue; }
    let rows;
    if (mode === 'times') rows = [{ t: 0 }, { t: 0.33 }];
    else if (mode === 'packs') rows = w.packs.map(pk => ({ t: 0, pack: pk.id }));
    else if (mode === 'opts') rows = [{ t: 0, opt: [0, 0] }, { t: 0, opt: [1, 1] }];
    else if (mode === 'strip') rows = [0, .25, .5, .75].map(t => ({ t }));
    const url = await p.evaluate(([i, r, w2, h2]) => sheet(i, r, w2, h2), [id, rows, W, H]);
    if (!url.startsWith('data:')) { console.log(id, url.slice(0, 1500)); continue; }
    fs.writeFileSync(path.join(OUT, id + '-' + mode + '.png'), Buffer.from(url.split(',')[1], 'base64'));
    console.log('shot', id, mode);
  }
  await b.close(); srv.close();
})();
