// Checks the share copy in headless Chrome, wrapped in the publisher's skeleton (as the wallpapers page's check.cjs
// does): no page errors; no sideways scroll at 1360 and 390 px; every tile drawn in dark and light; only tiles on
// screen drawn; the loop proof of all 24 (every pack, dark and light, both ends of the options) and the frame cost of
// all of them, read from the page (every pack and Match accent in dark and light, the options at both ends, the four times of
// day, full tilt and full focus); the premium options (Dynamic, Match accent, Perspective, Focus) each shown to ease, to move
// the picture, and to obey Reduce Motion; the large view with and without the glass; a pack change that crossfades; a speed change
// that does not jump the picture; the theme crossfade. Shots go to ../shots/. Usage: node check.cjs [quick]
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('E:/Droppy/work/tools/pw/node_modules/playwright-core');
const ROOT = process.env.WL_OUT ? path.resolve(process.env.WL_OUT) : path.resolve(__dirname, '..');
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
      await page.waitForFunction(() => window.__libDone, null, { timeout: 400000 }).catch(() => errs.push('the proofs and measurements did not finish in 400 s'));
      await page.waitForTimeout(600);
      const lib = await page.evaluate(() => { const L = window.__lib; return { done: L.done, max: L.max, checks: L.checks, hmin: L.hmin, renderer: L.renderer, n: window.WL_DATA.wallpapers.length, rows: Object.fromEntries(Object.entries(L.RES).map(([k, v]) => [k, { max: v.max, half: v.halfMin, halfDef: v.halfDef, ms: v.ms, msF: v.msFocus, base: v.base, checks: v.checks, dyn: v.dyn, ext: v.ext }])) }; });
      if (lib.done !== lib.n) errs.push('only ' + lib.done + ' of ' + lib.n + ' wallpapers proved');
      if (label === 'desk' && scheme === 'dark') {
        report.push('loop proof: ' + lib.done + ' of ' + lib.n + ' proved, ' + lib.checks + ' checks, max |frame(0) - frame(T)| = ' + lib.max + '/255; min max-difference at T/2 = ' + lib.hmin);
        report.push('renderer: ' + lib.renderer);
        const slow = Object.entries(lib.rows).filter(([k, v]) => v.halfDef < 8).map(([k, v]) => k + ' ' + v.halfDef);
        report.push('wallpapers whose frame at T/2 differs from frame 0 by under 8/255 at the default options (they move least): ' + (slow.join(', ') || 'none'));
        if (Object.entries(lib.rows).some(([k, v]) => v.halfDef < 3)) errs.push('a wallpaper does not visibly move: T/2 differs by under 3/255');
        report.push('per wallpaper (proof max, T/2 difference at default options and at the option extremes, ms per frame at 1280x800): ' + Object.entries(lib.rows).map(([k, v]) => k + ' ' + v.max + '/' + v.halfDef + ',' + v.half + '/' + (v.ms == null ? '-' : v.ms.toFixed(3))).join(', '));
        const ms = Object.values(lib.rows).map((v) => v.ms).filter((x) => x != null);
        report.push('frame cost range: ' + Math.min(...ms).toFixed(3) + ' to ' + Math.max(...ms).toFixed(3) + ' ms (flat-shader floor ' + Object.values(lib.rows)[0].base.toFixed(3) + ' ms)');
        const msF = Object.values(lib.rows).map((v) => v.msF).filter((x) => x != null);
        report.push('with Focus at 1 (a blur pass on top): ' + Math.min(...msF).toFixed(3) + ' to ' + Math.max(...msF).toFixed(3) + ' ms');
        const dynMax = Math.max(...Object.values(lib.rows).map((v) => Math.max(...Object.values(v.dyn || { x: 99 })))), extMax = Math.max(...Object.values(lib.rows).map((v) => (v.ext == null ? 99 : v.ext)));
        report.push('loop proof at the four times of day: max ' + dynMax + '/255; at full tilt and full focus (and Match accent): max ' + extMax + '/255');
        if (dynMax > 1 || extMax > 1) errs.push('loop proof over 1/255 for the premium options');
        for (const [k, v] of Object.entries(lib.rows)) { if (v.ms > 0.5) errs.push('frame cost over 0.5 ms: ' + k + ' ' + v.ms.toFixed(3)); if (v.msF > 0.6) errs.push('frame cost with Focus over 0.6 ms: ' + k + ' ' + v.msF.toFixed(3)); }
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
        const vlay = await page.evaluate(() => { const o = []; const s = document.querySelector('.vside').getBoundingClientRect(); if (s.right > innerWidth + 1 || s.left < -1) o.push('view panel outside ' + Math.round(s.left) + '..' + Math.round(s.right)); if (document.documentElement.scrollWidth > innerWidth + 1) o.push('sideways scroll with the view open'); for (const el of document.querySelectorAll('.vside .sw,.vside .kb,.vside .ac,.vside .pk,.vside input,.vside .ends,.vside .vnote')) { const r = el.getBoundingClientRect(); if (r.width && (r.right > innerWidth + 1 || r.left < -1)) o.push('panel control outside the viewport ' + (el.id || el.className) + ' ' + Math.round(r.left) + '..' + Math.round(r.right)); } return o; });
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
          // ---- the premium options: Dynamic, Match accent, Perspective, Focus
          {
            const bad = [], pre = [];
            const data = await page.evaluate(() => window.WL_DATA.wallpapers.map((w) => ({ id: w.id, ok: !!(w.dynamic && w.dynamic.light && w.dynamic.dark && w.dynamic.sun && w.match && w.match.off.length === 6 && w.roles.length === 6 && w.packs.length === 5), mode: w.perspective.mode, pdef: w.perspective.default, st: window.__ST[w.id].dynamic, ps: window.__ST[w.id].persp, f: window.__ST[w.id].focus })));
            for (const d of data) {
              if (!d.ok) bad.push(d.id + ' lacks option data');
              if (d.st) bad.push(d.id + ' Dynamic is not off by default');
              if (d.pdef !== (d.mode === 'layers') || d.ps !== d.pdef) bad.push(d.id + ' Perspective default is wrong');
            }
            const nLayers = data.filter((d) => d.mode === 'layers').length;
            // the palettes: Dynamic's looks for every wallpaper, both themes; dark leans night, light leans day; Match accent hues
            const pal = await page.evaluate(() => {
              const F = window.__fn, out = [];
              const dist = (a, b) => { let s = 0; for (let i = 0; i < 6; i++) s += Math.hypot(a[i * 3] - b[i * 3], a[i * 3 + 1] - b[i * 3 + 1], a[i * 3 + 2] - b[i * 3 + 2]); return s / 6; };
              const ac = window.WLGL.hex2lab('#005fb8'), ha = Math.atan2(ac[2], ac[1]) * 180 / Math.PI;
              for (const w of window.WL_DATA.wallpapers) {
                const r = { id: w.id, bad: [], range0: 0, range1: 0 };
                const pk = w.packs[0];
                for (const [dk, lean] of [[1, 0], [0, 12]]) {
                  const base = window.WLGL.packLab(pk, dk);
                  if (dist(F.palFor(w, pk, dk, lean).pal, base) > 2e-3) r.bad.push('theme ' + dk + ' does not lean ' + (dk ? 'night' : 'day'));
                  const ks = [6, 12, 18, 0].map((h) => F.palFor(w, pk, dk, h).pal);
                  let mx = 0;
                  for (let i = 0; i < 4; i++) for (let j = i + 1; j < 4; j++) mx = Math.max(mx, dist(ks[i], ks[j]));
                  r['range' + dk] = mx;
                  for (let h = 0; h < 24; h += 1.5) {
                    const p = F.palFor(w, pk, dk, h).pal;
                    for (let i = 0; i < 18; i++) if (!isFinite(p[i])) r.bad.push('non-finite at ' + h);
                    for (let i = 0; i < 6; i++) if (p[i * 3] < 0 || p[i * 3] > 1.02) r.bad.push('lightness out of range at ' + h);
                  }
                  if (mx < 0.012) r.bad.push('the four looks barely differ in theme ' + dk + ' (' + mx.toFixed(4) + ')');
                }
                const sunMoves = ['dawn', 'dusk'].some((k) => Math.abs(w.dynamic.sun[k][0]) > 1e-9);
                const s1 = F.look(w, 6, 1).sun, s2 = F.look(w, 18, 1).sun;
                if (sunMoves !== (Math.abs(s1[0] - s2[0]) > 0.1)) r.bad.push('the light track does not move as declared');
                const mp = F.matchOf(w);
                for (const th of ['dark', 'light']) {
                  const ho = w.match.hero, lab = mp[th].map((h) => window.WLGL.hex2lab(h));
                  const hh = Math.atan2(lab[ho][2], lab[ho][1]) * 180 / Math.PI, d = Math.abs(((hh - ha + 540) % 360) - 180);
                  if (d > 15) r.bad.push('Match accent hero hue ' + th + ' is ' + Math.round(d) + ' degrees from the accent');
                  lab.forEach((l, i) => { if (Math.hypot(l[1], l[2]) > 0.05) { const e = Math.abs(((Math.atan2(l[2], l[1]) * 180 / Math.PI - ha + 540) % 360) - 180); if (e > 95) r.bad.push('Match accent slot ' + i + ' ' + th + ' strays ' + Math.round(e) + ' degrees from the accent'); } });
                }
                out.push(r);
              }
              return out;
            });
            for (const r of pal) for (const b of r.bad) bad.push(r.id + ': ' + b);
            const rng = pal.map((r) => Math.min(r.range0, r.range1));
            pre.push(data.length + ' wallpapers carry dynamic, match and perspective data; Perspective is on by default for ' + nLayers + ' with layers and off for ' + (data.length - nLayers) + ' flat ones; the four looks differ by at least ' + Math.min(...rng).toFixed(3) + ' (mean OKLab distance, dark and light); dark leans night and light leans day on all; the Match accent hero hue is within 15 degrees of #005FB8 on all');
            // Dynamic on the open wallpaper: the switch eases the palette; each key look is distinct; the key chips and the scrubber move the clock
            await page.evaluate((id) => window.__open(id, null), 'dunes'); await page.waitForTimeout(900);
            const snap = () => page.evaluate((id) => ({ pal: Array.from(window.__RT[id].pal), sun: window.__RT[id].sun.slice() }), 'dunes');
            const dd = (a, b) => { let m = 0; for (let i = 0; i < 18; i++) m = Math.max(m, Math.abs(a[i] - b[i])); return m; };
            const p0 = await snap();
            await page.click('#vdynsw'); await page.click('#vkeys [data-key="dusk"]');
            await page.waitForTimeout(110);
            const pm = await snap();
            await page.waitForTimeout(1400);
            const p1 = await snap();
            const eased = dd(pm.pal, p0.pal) > 1e-3 && dd(pm.pal, p1.pal) > 1e-3 && dd(p1.pal, p0.pal) > 0.01;
            pre.push('Dynamic on at dusk: the palette eases (at 110 ms it is between the old and the new: ' + eased + '), the light moves from (' + p0.sun.map((x) => x.toFixed(2)) + ') to (' + p1.sun.map((x) => x.toFixed(2)) + ')');
            if (!eased) bad.push('Dynamic did not ease the palette');
            if (Math.abs(p1.sun[0] - p0.sun[0]) < 0.2) bad.push('Dynamic did not move the light of Dunes');
            await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-dynamic-dusk.png') });
            await page.evaluate(() => { const s = document.getElementById('vclock'); s.value = '9'; s.dispatchEvent(new Event('input', { bubbles: true })); });
            await page.waitForTimeout(900);
            const readout = await page.evaluate(() => document.getElementById('vdynv').textContent);
            if (!/^09:00/.test(readout)) bad.push('the clock readout is "' + readout + '", not 09:00');
            const p9 = await snap();
            if (dd(p9.pal, p1.pal) < 0.005) bad.push('the scrubber did not move the palette');
            for (const k of ['dawn', 'day', 'dusk', 'night']) { await page.click('#vkeys [data-key="' + k + '"]'); await page.waitForTimeout(700); await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-dynamic-' + k + '.png') }); }
            await page.click('#vkeys [data-key="now"]'); await page.waitForTimeout(500);
            await page.click('#vdynsw'); await page.waitForTimeout(1300);
            const poff = await snap();
            if (dd(poff.pal, p0.pal) > 0.01) bad.push('Dynamic off did not ease back to the plain pack');
            // Match accent: the picker eases the palette to the accent's family
            const q0 = await snap();
            await page.click('#vacc .ac:nth-child(4)');
            await page.waitForTimeout(110);
            const qm = await snap();
            await page.waitForTimeout(1400);
            const q1 = await snap();
            const qe = dd(qm.pal, q0.pal) > 1e-3 && dd(qm.pal, q1.pal) > 1e-3 && dd(q1.pal, q0.pal) > 0.02;
            pre.push('Match accent (gold, from the picker): the palette eases (' + qe + '), the pack reads "' + (await page.evaluate(() => document.getElementById('vpackname').textContent)) + '"');
            if (!qe) bad.push('the accent pick did not ease the palette');
            await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-match-gold.png') });
            await page.click('#vacc .ac:nth-child(1)'); await page.waitForTimeout(900);
            await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-match-blue.png') });
            // Perspective on a wallpaper with layers: the pointer tilts, eased; the picture moves a few pixels; Focus blurs
            await page.evaluate((id) => window.__open(id, null), 'tides'); await page.waitForTimeout(900);
            const sw0 = await page.getAttribute('#vpersp', 'aria-checked');
            if (sw0 !== 'true') bad.push('Perspective is not on by default for Tides');
            const box = await page.evaluate(() => { const r = document.querySelector('.vstage').getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });
            await page.mouse.move(box.x + box.w * 0.5, box.y + box.h * 0.5); await page.waitForTimeout(600);
            await page.mouse.move(box.x + box.w - 24, box.y + 24);
            await page.waitForTimeout(70);
            const tm = await page.evaluate(() => window.__RT.tides.tilt.slice());
            await page.waitForTimeout(1600);
            const tf = await page.evaluate(() => window.__RT.tides.tilt.slice());
            const te = Math.abs(tm[0]) > 0.02 && Math.abs(tm[0]) < Math.abs(tf[0]) - 0.02 && tf[0] > 0.9 && tf[1] > 0.9;
            pre.push('Perspective: at 70 ms the tilt is (' + tm.map((x) => x.toFixed(2)) + '), settling at (' + tf.map((x) => x.toFixed(2)) + '): eased ' + te);
            if (!te) bad.push('the tilt did not ease to the pointer');
            const px = await page.evaluate(() => {
              const E = window.__E, P = E.prog('tides'), W = 320, H = 200;
              const base = { pal: window.__RT.tides.pal, dark: window.__RT.tides.dark, opt: window.__RT.tides.opt };
              const a = E.read(P, W, H, 10, Object.assign({ tilt: [0, 0] }, base)), b = E.read(P, W, H, 10, Object.assign({ tilt: [1, 1] }, base));
              let sum = 0, mx = 0, n = 0;
              for (let i = 0; i < a.length; i += 4) { const d = Math.abs(a[i] - b[i]) + Math.abs(a[i + 1] - b[i + 1]) + Math.abs(a[i + 2] - b[i + 2]); sum += d; mx = Math.max(mx, d); n++; }
              const sharp = (s) => { const f = E.read(P, W, H, 10, Object.assign({ tilt: [0, 0], focus: s }, base)); let t = 0; for (let y = 1; y < H; y++) for (let x = 1; x < W; x++) { const i = (y * W + x) * 4, j = i - 4, k = i - W * 4; t += Math.abs(f[i + 1] - f[j + 1]) + Math.abs(f[i + 1] - f[k + 1]); } return t / (W * H); };
              return { mean: sum / n, mx, s0: sharp(0), s5: sharp(0.5), s1: sharp(1) };
            });
            pre.push('Perspective at full tilt moves the picture (mean change ' + px.mean.toFixed(2) + ' of 765, largest ' + px.mx + '), a few pixels and not a pan; Focus: picture sharpness ' + px.s0.toFixed(3) + ' at 0, ' + px.s5.toFixed(3) + ' at 0.5, ' + px.s1.toFixed(3) + ' at 1');
            if (px.mean < 0.02) bad.push('Perspective did not move the Tides layers');
            if (px.mean > 12) bad.push('Perspective moved the picture too much');
            if (!(px.s5 < px.s0 * 0.9 && px.s1 < px.s5)) bad.push('Focus did not soften the picture');
            await page.mouse.move(box.x - 40, box.y + box.h / 2); await page.waitForTimeout(300);
            // Focus: the slider eases the blur in
            await page.evaluate(() => { const s = document.getElementById('vfocus'); s.value = '1'; s.dispatchEvent(new Event('input', { bubbles: true })); });
            await page.waitForTimeout(100);
            const fm = await page.evaluate(() => window.__RT.tides.focus);
            await page.waitForTimeout(1500);
            const ff = await page.evaluate(() => window.__RT.tides.focus);
            pre.push('Focus slider to 1: at 100 ms the eased value is ' + fm.toFixed(2) + ', then ' + ff.toFixed(2));
            if (!(fm > 0.02 && fm < 0.98 && ff > 0.99)) bad.push('Focus did not ease');
            await page.screenshot({ path: path.join(SHOTS, 'desk-dark-view-focus1.png') });
            await page.evaluate(() => { const s = document.getElementById('vfocus'); s.value = '0'; s.dispatchEvent(new Event('input', { bubbles: true })); });
            await page.waitForTimeout(900);
            // a flat wallpaper: Perspective is off by default
            await page.evaluate((id) => window.__open(id, null), 'halo'); await page.waitForTimeout(700);
            if ((await page.getAttribute('#vpersp', 'aria-checked')) !== 'false') bad.push('Perspective is on by default for the flat Halo');
            await page.evaluate((id) => window.__open(id, null), pick); await page.waitForTimeout(700);
            report.push(...pre.map((x) => 'premium: ' + x));
            if (bad.length) errs.push('premium options: ' + bad.join('; '));
          }
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
  // Reduce Motion: Perspective is off and cannot be turned on, the tilt stays at zero whatever the pointer does
  {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 1000 }, colorScheme: 'dark', reducedMotion: 'reduce' });
    const page = await ctx.newPage();
    const errs = [];
    page.on('pageerror', (e) => errs.push('pageerror ' + e.message));
    await page.goto('http://127.0.0.1:8813/share/animated.html');
    await page.waitForTimeout(1500);
    await page.evaluate(() => window.__open('tides', null)); await page.waitForTimeout(900);
    const off = await page.evaluate(() => ({ sw: document.getElementById('vpersp').getAttribute('aria-checked'), dis: document.getElementById('vpersp').disabled, ok: window.__fn.tiltOK(), play: window.__G.play, note: document.getElementById('vperspnote').textContent }));
    const box = await page.evaluate(() => { const r = document.querySelector('.vstage').getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; });
    await page.mouse.move(box.x + box.w * 0.5, box.y + box.h * 0.5); await page.waitForTimeout(200);
    await page.mouse.move(box.x + box.w - 24, box.y + 24); await page.waitForTimeout(900);
    await page.click('#vpersp', { force: true, timeout: 2000 }).catch(() => {});
    await page.waitForTimeout(500);
    const tilt = await page.evaluate(() => window.__RT.tides.tilt.slice());
    const bad = [];
    if (off.sw !== 'false' || !off.dis || off.ok) bad.push('Perspective is not off and disabled under Reduce Motion (' + JSON.stringify(off) + ')');
    if (Math.abs(tilt[0]) + Math.abs(tilt[1]) > 1e-6) bad.push('the tilt moved under Reduce Motion: ' + tilt);
    report.push('Reduce Motion (emulated): Perspective switch off and disabled: ' + (off.sw === 'false' && off.dis) + '; tilt after the pointer moved to the corner: (' + tilt.map((x) => x.toFixed(3)) + '); the Motion switch starts ' + (off.play ? 'on' : 'off') + '; errors ' + errs.length + (bad.length ? ' ' + bad.join('; ') : ''));
    if (errs.length || bad.length) { report.push('REDUCE MOTION FAILED ' + errs.concat(bad).join(' | ')); }
    await ctx.close();
  }
  await browser.close(); srv.close();
  fs.writeFileSync(path.join(SHOTS, 'report.txt'), report.join('\n') + '\n');
  console.log(report.join('\n'));
  if (report.some((l) => /errors [1-9]|FAILED/.test(l))) process.exitCode = 1;
})();
