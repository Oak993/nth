// Checks the share copy in headless Chrome, wrapped in the publisher's skeleton (as the kit's pw.cjs does): no page
// errors, no sideways scroll at 1360 and 390 px, nothing escaping its box, every thumbnail drawn (not blank, no NaN
// black), the large view drawn, packs / option / grain / glass / theme transitions, and the single-render cost of all
// thirty at 2560 x 1600 written to measured.json (build_page.py puts it into the page). Shots go to ../shots/.
// Usage: node check.cjs [walls] [keep]   walls: also shoot every wallpaper large, dark and light, glass on and off.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('E:/Droppy/work/tools/pw/node_modules/playwright-core');
const ROOT = path.resolve(process.env.SW_ROOT || path.join(__dirname, '..'));
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
// ---------------------------------------------------------------- the four premium options (OPTIONS-FORMAT.md)
// Data and numbers, in the page, for every wallpaper: Dynamic (the four looks exist, differ, night is darker than day in both
// themes, the clock is smooth and cyclic), Match accent (ten accents, both themes: valid, in the accent's hue family, never flat
// or black), Perspective (bounded to 0.015 height units, off means still, bounded change of the picture), Focus (changes the
// picture, never flat or black). The numbers are means over 256 x 160 renders; thresholds are well inside what the 30 do.
async function optionChecks(page, report) {
  const r = await page.evaluate(() => {
    const E = [], N = { dyn: 0, acc: 0, per: 0, foc: 0 };
    const S = window.__sw, W = S.W, DATA = JSON.parse(document.getElementById('wallpapers').textContent);
    const hexRe = /^#[0-9a-f]{6}$/;
    const fin = (v) => typeof v === 'number' && isFinite(v);
    for (const m of DATA.options.warnings) E.push('data: ' + m);
    const hueOf = (hex) => SWC.lab2lch.apply(null, SWC.hex2lab(hex));
    const hd = (a, b) => Math.abs(SWC.wrap180(a - b));
    const baseBlack = {};
    const okImg = (id, tag, st) => {
      const s = S.stats(id, st); if (!s) { E.push(id + ' ' + tag + ': did not render'); return null; }
      const bk = id + !!(st && st.dark); if (!(bk in baseBlack)) baseBlack[bk] = S.stats(id, { dark: !!(st && st.dark) }).vdark;
      if (!fin(s.luma) || !fin(s.chroma)) E.push(id + ' ' + tag + ': NaN');
      if (s.range < 4) E.push(id + ' ' + tag + ': flat (range ' + s.range.toFixed(1) + ')');
      // black: a NaN shows as pure black; a deep-space wallpaper is nearly black by design (a dark teal ground rounds to 0,0,0 in 8 bits), so compare with the share of very dark pixels (sum of channels under 12) of its own default render
      if (s.black > Math.max(0.02, baseBlack[bk] + 0.06)) E.push(id + ' ' + tag + ': black ' + Math.round(s.black * 100) + '% (its default render has ' + Math.round(baseBlack[bk] * 100) + '% very dark)');
      return s;
    };
    for (const w of W) {
      const id = w.id;
      // ---- data
      if (w.dynGenerated) E.push(id + ': no dyn data of its own (a generic one is used)');
      for (const k of ['dawn', 'day', 'dusk', 'night']) { const e = w.dyn[k]; if (!e || !fin(e.l) || !fin(e.c) || !fin(e.h)) E.push(id + ': dyn.' + k + ' malformed'); }
      if (!(w.persp.depth >= 0 && w.persp.depth <= 1)) E.push(id + ': persp.depth out of range');
      if (w.persp.def === 1 && w.persp.depth < 0.3) E.push(id + ': persp is on by default but depth is below 0.3');
      if (!Array.isArray(w.hues.d) || w.hues.d.length !== 6 || !Array.isArray(w.roles) || w.roles.length !== 6) E.push(id + ': roles or hues missing');
      // ---- Dynamic: four looks, different, night darker than day, in both themes; the clock is cyclic and smooth
      for (const dark of [true, false]) {
        const tag = 'dynamic ' + (dark ? 'dark' : 'light'), at = (h) => ({ dark, dyn: true, clock: h }), s = {};
        for (const [k, h] of [['dawn', 6], ['day', 12], ['dusk', 18], ['night', 0]]) s[k] = okImg(id, tag + ' ' + k, at(h));
        if (!s.day || !s.night) continue;
        if (s.day.luma - s.night.luma < 6) E.push(id + ' ' + tag + ': night is not darker than day (' + s.night.luma.toFixed(1) + ' vs ' + s.day.luma.toFixed(1) + ')');
        let mn = 1e9, who = '';
        const hs = [['dawn', 6], ['day', 12], ['dusk', 18], ['night', 0]];
        for (let i = 0; i < 4; i++) for (let j = i + 1; j < 4; j++) { const d = S.diff(id, at(hs[i][1]), at(hs[j][1])); if (d.mean < mn) { mn = d.mean; who = hs[i][0] + '/' + hs[j][0]; } }
        if (mn < 1.5) E.push(id + ' ' + tag + ': looks ' + who + ' barely differ (mean ' + mn.toFixed(2) + ' of 255)');
        const c0 = S.diff(id, at(0), at(24)); if (c0.mean > 0.01) E.push(id + ' ' + tag + ': 00:00 and 24:00 differ');
        N.dyn++;
      }
      let prev = null, worst = [0, 0, 0, 0];
      for (let q = 0; q <= 96; q++) {
        const d = SWC.dynDelta(w.dyn, q * 0.25, 1, 1, w.light.def), cur = [d.dl, d.cr, d.dh, Math.hypot(d.dlx, d.dly)];
        if (prev) cur.forEach((v, i) => { worst[i] = Math.max(worst[i], Math.abs(v - prev[i])); });
        prev = cur;
      }
      if (worst[0] > 0.02 || worst[1] > 0.06 || worst[2] > 6 || worst[3] > 0.3) E.push(id + ': the clock jumps (per 15 min: dl ' + worst[0].toFixed(3) + ', cr ' + worst[1].toFixed(3) + ', dh ' + worst[2].toFixed(1) + ', light ' + worst[3].toFixed(2) + ')');
      // ---- Match accent: every preset, both themes
      for (const [nm, hex] of SWC.ACCENTS) {
        const Ha = hueOf(hex)[2], Ca = hueOf(hex)[1];
        for (const dark of [false, true]) {
          const pk = S.accPack(w, hex), h6 = pk[dark ? 'dark' : 'light'];
          if (h6.length !== 6 || !h6.every((h) => hexRe.test(h))) { E.push(id + ' accent ' + nm + ': bad palette'); continue; }
          // the anchor: one ramp role in the accent's hue (not for a grey accent, and not where the dark brown rule moves the hue)
          const lch = h6.slice(0, 5).map(hueOf);
          const near = lch.some((c) => c[1] > 0.015 && hd(c[2], Ha) < 14);
          const mud = dark && Ha > 20 && Ha < 120;
          if (Ca > 0.05 && !near && !mud) E.push(id + ' accent ' + nm + ' ' + (dark ? 'dark' : 'light') + ': no ramp role takes the accent hue ' + Math.round(Ha));
          const s = okImg(id, 'accent ' + nm + ' ' + (dark ? 'dark' : 'light'), { pack: w.packs.length, accent: hex, dark });
          if (s && nm === 'Blue') {
            if (s.chroma < 0.012) E.push(id + ' accent ' + nm + ' ' + (dark ? 'dark' : 'light') + ': the picture has no colour (chroma ' + s.chroma.toFixed(3) + ')');
            else if (hd(s.hue, Ha) > 80) E.push(id + ' accent ' + nm + ' ' + (dark ? 'dark' : 'light') + ': the picture is in hue ' + Math.round(s.hue) + ', not the accent family ' + Math.round(Ha));
          }
          N.acc++;
        }
      }
      // ---- Perspective: bounded, still when off, a bounded change of the picture
      let anyChange = false;
      for (const t of [[1, 1], [-1, 1], [1, -1], [-1, -1], [1, 0], [0, 1]]) {
        const s = S.stats(id, { persp: true, tilt: t }), off = S.stats(id, { persp: false, tilt: t });
        const L = Math.hypot(s.tilt ? s.tilt[0] : 0, s.tilt ? s.tilt[1] : 0);
        if (L > 0.015 + 1e-9 || L > SWG.AMP * w.persp.depth + 1e-9) E.push(id + ' perspective: u_tilt ' + L.toFixed(4) + ' exceeds the bound');
        if (L * 400 > 6.01) E.push(id + ' perspective: ' + (L * 400).toFixed(2) + ' px at 800 px high, over 6');
        if (off.tilt && (off.tilt[0] || off.tilt[1])) E.push(id + ' perspective: not still when off');
        const d = S.diff(id, { persp: true, tilt: [0, 0] }, { persp: true, tilt: t });
        if (d.mean > 8) E.push(id + ' perspective: the picture changes too much at a corner (mean ' + d.mean.toFixed(2) + ' of 255)');
        if (d.max > 0) anyChange = true;
      }
      if (!anyChange && w.persp.depth >= 0.3) E.push(id + ' perspective: the tilt changes nothing');
      N.per++;
      // ---- Focus
      const f1 = okImg(id, 'focus 1', { focus: 1 }), fd = S.diff(id, { focus: 0 }, { focus: 1 });
      if (fd.max < 2 || fd.mean < 0.3) E.push(id + ' focus: Focus 1 barely changes the picture (mean ' + fd.mean.toFixed(2) + ', max ' + fd.max + ')');
      N.foc++;
    }
    return { E, N, n: W.length };
  });
  report.push('options on ' + r.n + ' wallpapers: dynamic renders ' + r.N.dyn + ' (2 themes), accent renders ' + r.N.acc + ' (10 accents, 2 themes), perspective ' + r.N.per + ', focus ' + r.N.foc + '; problems ' + r.E.length + (r.E.length ? ': ' + r.E.slice(0, 40).join(' | ') : ''));
  return r.E;
}
// the controls: each change eases (busy), then settles to zero redraws; the pointer tilts within the bound; the folds open
async function optionUi(page, report, shots) {
  const E = [], q = (js) => page.evaluate(js);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.evaluate(() => window.__sw.select('riptide', false)); await page.waitForTimeout(1000);
  const settle = async (label, act) => {
    await q(() => { window.__t0 = window.__sw.V.st.n; });
    await act();
    await page.waitForTimeout(90);
    const mid = await q(() => ({ busy: window.__sw.V.st.busy, n: window.__sw.V.st.n - window.__t0 }));
    await page.waitForTimeout(1500);
    const n1 = await q(() => window.__sw.V.st.n); await page.waitForTimeout(700);
    const n2 = await q(() => window.__sw.V.st.n);
    if (n2 - n1 !== 0) E.push(label + ': ' + (n2 - n1) + ' redraws after it settled (0 expected)');
    if (!mid.busy && mid.n < 1) E.push(label + ': nothing eased');
    return mid;
  };
  // Dynamic on, a look chosen, the scrubber dragged
  await page.click('#dyn'); await page.waitForTimeout(700);
  if (await q(() => document.getElementById('dynFold').classList.contains('open')) !== true) E.push('dynamic: the clock fold did not open');
  await settle('dynamic switch', async () => { });
  await shots('x-dynamic-on');
  for (const k of [0, 1, 2, 3]) { await settle('look ' + k, async () => { await page.click('.lk[data-k="' + k + '"]'); }); await shots('x-look-' + k); }
  await settle('scrub', async () => { await q(() => { const r = document.getElementById('clock'); r.value = '9.4'; r.dispatchEvent(new Event('input', { bubbles: true })); }); });
  const clk = await q(() => document.getElementById('clk').textContent);
  if (!/09:2\d/.test(clk)) E.push('scrubber label "' + clk + '"');
  // Match accent: the pack, the picker
  await page.click('#packs .pk.ac'); await page.waitForTimeout(900);
  if (await q(() => document.getElementById('accFold').classList.contains('open')) !== true) E.push('accent: the picker did not open');
  const nm = await q(() => document.getElementById('accn').textContent);
  if (!/005FB8/i.test(nm)) E.push('accent: the default is not #005FB8 (' + nm + ')');
  await shots('x-accent-blue');
  await settle('accent change', async () => { await page.click('.acs[data-hex="#C42B3A"]'); });
  await shots('x-accent-red');
  await settle('custom accent', async () => { await q(() => { const i = document.getElementById('accc'); i.value = '#12a594'; i.dispatchEvent(new Event('input', { bubbles: true })); }); });
  // Focus
  await settle('focus', async () => { await q(() => { const r = document.getElementById('focus'); r.value = '0.8'; r.dispatchEvent(new Event('input', { bubbles: true })); }); });
  if (!((await q(() => window.__sw.V.st.u.blur)) > 0)) E.push('focus: no blur drawn at 0.8');
  await shots('x-focus');
  await q(() => { const r = document.getElementById('focus'); r.value = '0'; r.dispatchEvent(new Event('input', { bubbles: true })); }); await page.waitForTimeout(900);
  // Perspective: the pointer in a corner, then back
  const on = await q(() => document.getElementById('persp').getAttribute('aria-checked'));
  if (on !== 'true') E.push('perspective: riptide has depth and should be on by default (' + on + ')');
  await page.mouse.move(1300, 960); await page.waitForTimeout(900);
  const tl = await q(() => window.__sw.V.st.u.tilt);
  const L = Math.hypot(tl[0], tl[1]);
  if (!(L > 0.004 && L <= 0.015 + 1e-9)) E.push('perspective: the pointer in a corner gives u_tilt ' + L.toFixed(4) + ' (want 0.004 .. 0.015)');
  await settle('pointer back', async () => { await page.mouse.move(680, 500); await page.mouse.move(680, 500); });
  await page.mouse.move(1300, 960); await page.waitForTimeout(500);
  await page.click('#persp'); await page.waitForTimeout(1600);
  const off = await q(() => window.__sw.V.st.u.tilt); if (Math.hypot(off[0], off[1]) > 1e-9) E.push('perspective: the switch off leaves the picture displaced');
  await page.click('#persp'); await page.waitForTimeout(400);
  // Reset brings it all back
  await page.click('#reset'); await page.waitForTimeout(1200);
  const st = await q(() => ({ dyn: window.__sw.per('riptide').dyn, pack: window.__sw.per('riptide').pack, focus: window.__sw.per('riptide').focus }));
  if (st.dyn || st.pack !== 0 || st.focus !== 0) E.push('reset: ' + JSON.stringify(st));
  report.push('option controls: ' + (E.length ? 'PROBLEMS ' + E.join(' | ') : 'dynamic, looks, scrubber, accent, focus, perspective and reset all ease and settle to 0 redraws'));
  return E;
}
// Reduce Motion: Perspective is off and cannot be turned on; the picture never moves with the pointer
async function reduceMotionCheck(browser, report) {
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 1000 }, reducedMotion: 'reduce' });
  const page = await ctx.newPage(), E = [];
  page.on('pageerror', (e) => E.push('pageerror ' + e.message));
  await page.goto('http://127.0.0.1:' + PORT + '/share/static.html'); await page.waitForTimeout(1500);
  await page.evaluate(() => window.__sw.select('riptide', false)); await page.waitForTimeout(1000);
  const dis = await page.evaluate(() => { const b = document.getElementById('persp'); return [b.getAttribute('aria-disabled'), b.getAttribute('aria-checked'), document.getElementById('perspn').textContent]; });
  if (dis[0] !== 'true' || dis[1] !== 'false') E.push('Perspective is not off and disabled under Reduce Motion: ' + dis.join(' / '));
  await page.click('#persp', { force: true }); await page.mouse.move(1300, 950); await page.waitForTimeout(900); await page.mouse.move(100, 100); await page.waitForTimeout(900);
  const t = await page.evaluate(() => ({ tilt: window.__sw.V.st.u.tilt, p: window.__sw.V.st.persp }));
  if (Math.hypot(t.tilt[0], t.tilt[1]) > 1e-9 || t.p !== 0) E.push('the picture moved under Reduce Motion: ' + JSON.stringify(t));
  report.push('Reduce Motion: Perspective ' + (E.length ? 'PROBLEMS ' + E.join(' | ') : 'off and disabled (' + dis[2] + '), the picture does not move with the pointer'));
  await ctx.close();
  return E;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const problems = [];
let PORT = 0;

(async () => {
  await new Promise((r) => srv.listen(0, r)); PORT = srv.address().port;   // a free port, so two checks can run side by side
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  const report = [];
  const NW = JSON.parse(fs.readFileSync(path.join(__dirname, 'wallpapers.json'), 'utf8')).wallpapers.length;
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
      await page.goto('http://127.0.0.1:' + PORT + '/share/static.html');
      await page.evaluate(() => Promise.race([document.fonts.ready, new Promise((r) => setTimeout(r, 4000))]));
      await page.waitForTimeout(1200);
      const tag = label + '-' + scheme;
      await page.screenshot({ path: path.join(SHOTS, tag + '-top.png') });
      await (await page.$('#view')).screenshot({ path: path.join(SHOTS, tag + '-view.png') });
      const lay = await layout(page);
      // each tier, scrolled to, so its thumbnails draw
      for (const t of ['vibrant', 'balanced', 'minimal']) {
        const el = await page.$('#t-' + t); await el.scrollIntoViewIfNeeded(); await page.waitForTimeout(900);
        for (const card of await page.$$('#t-' + t + ' .card')) { await card.scrollIntoViewIfNeeded(); await page.waitForTimeout(70); }   // every thumbnail visible once, so it draws
        await page.waitForTimeout(500);
        await el.screenshot({ path: path.join(SHOTS, tag + '-tier-' + t + '.png') });
      }
      const th = await thumbsOk(page);
      report.push(tag + ': thumbnails drawn ' + th.seen + '/' + NW + (th.bad.length ? ' BAD ' + th.bad.join(', ') : ''));
      if (th.bad.length) errs.push('thumbs ' + th.bad.join(','));
      if (th.seen !== NW) errs.push('only ' + th.seen + ' of ' + NW + ' thumbnails drawn');

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
        // the four premium options: data and numbers for every wallpaper, then the controls
        problems.push(...(await optionChecks(page, report)));
        problems.push(...(await optionUi(page, report, async (n) => { await (await page.$('#view')).screenshot({ path: path.join(SHOTS, n + '.png') }); })));
        await page.evaluate(() => window.scrollTo(0, 0)); await page.waitForTimeout(300);
        // the cost of one 2560 x 1600 render of each
        await page.evaluate(() => document.getElementById('Cost').scrollIntoView());
        // measured twice; the slower median of the two is kept for each wallpaper (GPU clocks vary between runs)
        await page.evaluate(() => window.__measure());
        const m1 = await page.evaluate(() => window.__measured);
        await page.evaluate(() => window.__measure());
        measured = await page.evaluate(() => window.__measured);
        if (m1 && measured) for (const k of Object.keys(measured.rows)) if (m1.rows[k].med > measured.rows[k].med) measured.rows[k] = m1.rows[k];
        if (m1 && measured && measured.rowsOpt) for (const k of Object.keys(measured.rowsOpt)) if (m1.rowsOpt[k].med > measured.rowsOpt[k].med) measured.rowsOpt[k] = m1.rowsOpt[k];
        await (await page.$('#Cost')).screenshot({ path: path.join(SHOTS, 'cost-desk-dark.png') });
        if (measured) {
          const v = Object.entries(measured.rows).map(([k, r]) => [k, r.med]).sort((a, b) => a[1] - b[1]);
          report.push('2560x1600 single render on ' + measured.renderer + ': ' + v.map(([k, m]) => k + ' ' + m.toFixed(2)).join(', '));
          measured.chrome = browser.version();
          const ro = measured.rowsOpt || {}, over = (rows) => Object.entries(rows).filter(([k, r]) => r.med > 30).map(([k, r]) => k + ' ' + r.med.toFixed(1));
          const vo = Object.values(ro).map((r) => r.med).sort((a, b) => a - b);
          report.push('with every option on (Match accent, Dynamic at dusk, a corner tilt, Focus 1): ' + (vo.length ? vo[0].toFixed(2) + ' to ' + vo[vo.length - 1].toFixed(2) + ' ms' : 'not measured') + ' (budget 30)');
          const bad = over(measured.rows).concat(over(ro).map((x) => 'options ' + x));
          if (bad.length) problems.push('over the 30 ms budget: ' + bad.join(', '));
          if (Object.keys(measured.rows).length !== NW || Object.keys(ro).length !== NW) problems.push('cost measured for ' + Object.keys(measured.rows).length + '/' + Object.keys(ro).length + ' of ' + NW);
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
        // the panel with Dynamic and the accent picker open must still fit
        await page.evaluate(() => window.__sw.select('solstice', false)); await page.waitForTimeout(900);
        await page.click('#dyn'); await page.click('#packs .pk.ac'); await page.waitForTimeout(900);
        await (await page.$('#view')).screenshot({ path: path.join(SHOTS, tag + '-view-options.png') });
      }
      // whole page, made as tall as the page so every thumbnail is drawn
      await page.evaluate(() => window.scrollTo(0, 0));
      const H = Math.min(12000, await page.evaluate(() => document.documentElement.scrollHeight));
      await page.setViewportSize({ width: w, height: H });
      await page.waitForTimeout(2200);
      await page.screenshot({ path: path.join(SHOTS, tag + '-page.png') });
      const lay2 = await layout(page);
      for (const m of new Set(lay.concat(lay2))) problems.push(tag + ' layout: ' + m);
      if (errs.length) problems.push(tag + ': ' + errs.join(' | '));
      report.push(tag + ': errors ' + errs.length + (errs.length ? ' ' + errs.join(' | ') : '') + '; layout ' + ([...new Set(lay.concat(lay2))].join(' | ') || 'ok'));
      await ctx.close();
    }
  }
  // every wallpaper name is unique across both collections (the animated catalogue is read by importing catalog.py, which also
  // loads catalog_<tier>_2.py)
  {
    const cp = require('child_process'), animDir = path.resolve(__dirname, '..', '..', 'animated', 'src');
    const py = cp.spawnSync('python', ['-c', "import sys,json; sys.path.insert(0, sys.argv[1]); import catalog; print(json.dumps([[w['name'], w['id'], w['id'] in catalog.OPT24] for w in catalog.W]))", animDir], { encoding: 'utf8' });
    const old30 = new Set([...fs.readFileSync(path.join(__dirname, 'options-data-1.js'), 'utf8').matchAll(/^\s{4}(\w+): \{ dyn/gm)].map((m) => m[1]));
    const sNames = JSON.parse(fs.readFileSync(path.join(__dirname, 'wallpapers.json'), 'utf8')).wallpapers.map((w) => [w.name, w.id, old30.has(w.id)]);
    if (py.status !== 0) { problems.push('could not read the animated catalogue: ' + (py.stderr || '').slice(-200)); report.push('names across collections: the animated catalogue could not be read'); }
    else {
      const aNames = JSON.parse(py.stdout), seen = {}, clash = [], legacy = [];
      for (const [col, list] of [['static', sNames], ['animated', aNames]]) for (const [n, id, old] of list) {
        const k = n.trim().toLowerCase(), o = seen[k];
        if (!o) { seen[k] = { t: col + ' ' + id, old }; continue; }
        // two of the original 54 sharing a name stay (the owner loves them: never replaced); any clash with a new one is a problem
        (o.old && old ? legacy : clash).push('"' + n + '" (' + o.t + ' and ' + col + ' ' + id + ')');
      }
      report.push('names across collections: ' + sNames.length + ' static + ' + aNames.length + ' animated: ' + (clash.length ? 'CLASH with a new wallpaper: ' + clash.join(', ') : 'no new wallpaper clashes') + (legacy.length ? '; shared by two of the original 54 (kept, flagged): ' + legacy.join(', ') : ''));
      if (clash.length) problems.push('wallpaper name clash: ' + clash.join(', '));
    }
  }
  problems.push(...(await reduceMotionCheck(browser, report)));
  await browser.close(); srv.close();
  report.push(problems.length ? 'RESULT: FAIL, ' + problems.length + ' problem(s): ' + problems.slice(0, 30).join(' | ') : 'RESULT: PASS (' + NW + ' wallpapers)');
  if (problems.length) process.exitCode = 1;
  fs.writeFileSync(path.join(SHOTS, 'report.txt'), report.join('\n') + '\n');
  console.log(report.join('\n'));
})();
