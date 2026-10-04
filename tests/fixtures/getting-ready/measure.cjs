// Measures GR-2's fixture: the locked look I "Sheet" of work/design/decisions-2026-10-03/getting-ready (OD-0096, the OD-0098
// pick "Look I, Words W1, Download D3") in headless Chrome, for GettingReadySheetFixtureTests.
//   node tests/fixtures/getting-ready/measure.cjs <playwright-core dir> <getting-ready.html> > sheet.json
// The page's own frame function (window.__gr.frame) draws any instant of its plan; CSS transitions are switched off for the
// geometry (every number is read at rest) and read as computed values for the timing. Chrome: C:/Program Files/Google/Chrome.
const path = require('path');
const { chromium } = require(path.join(process.argv[2], 'playwright-core'));
const html = process.argv[3];
const r2 = (n) => Math.round(n * 100) / 100;
(async () => {
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe' });
  const out = { chrome: await browser.version(), window: { width: 960, height: 680, sidebar: 250 } };
  for (const theme of ['dark', 'light']) {
    const page = await browser.newPage({ viewport: { width: 1300, height: 1000 }, deviceScaleFactor: 1 });
    await page.route('**/*', (r) => (r.request().url().startsWith('file:') ? r.continue() : r.abort()));
    await page.goto('file:///' + html.split('\\').join('/'));
    await page.waitForTimeout(600);
    await page.evaluate(([theme]) => {
      const b = Array.from(document.querySelectorAll('button')).find((x) => /^I\s+Sheet/.test(x.textContent.trim()));
      b.click();
      document.querySelector('#stage').classList.toggle('light', theme === 'light');
    }, [theme]);
    await page.waitForTimeout(800);
    const motion = await page.evaluate(() => {
      const w = document.querySelector('.slot .grw');
      const first = w.querySelector('.vi .r');
      const tr = (el) => getComputedStyle(el).transition;
      return {
        row: tr(first), mark: Array.from(first.querySelector('.tr').children).map((c) => ({ cls: (c.getAttribute('class') || '').trim(), transition: tr(c) })),
        barFill: tr(w.querySelector('.vi .bar i')), spinnerAnimation: getComputedStyle(first.querySelector('.spn')).animation,
        titleStack: null,
      };
    });
    await page.addStyleTag({ content: '.slot{transition:none!important;opacity:1!important} .grw *{transition:none!important} .grw .spn{animation:none!important}' });
    const data = await page.evaluate(() => {
      const g = window.__gr;
      g.frame(0); // settle
      const w = document.querySelector('.slot .grw');
      const rect = (el) => { const wr = w.getBoundingClientRect(); const s = parseFloat(getComputedStyle(w.parentElement).getPropertyValue('--sc')) || 1; const r = el.getBoundingClientRect(); return { x: (r.left - wr.left) / s, y: (r.top - wr.top) / s, w: r.width / s, h: r.height / s }; };
      const cs = (el, ...p) => { const c = getComputedStyle(el); const o = {}; for (const k of p) o[k] = c[k]; return o; };
      const pick = (t, withRows) => {
        g.frame(t);
        const sheet = w.querySelector('.sheet');
        const rows = Array.from(w.querySelectorAll('.vi .r')).map((r) => ({
          id: r.dataset.s, st: r.dataset.st, box: rect(r), glyph: rect(r.querySelector('.gl')), text: rect(r.querySelector('.tx b')), mark: rect(r.querySelector('.tr')),
          label: r.querySelector('.tx b').textContent, color: getComputedStyle(r).color,
        }));
        return {
          t, h: parseFloat(w.style.getPropertyValue('--h')), p: parseFloat(w.style.getPropertyValue('--p')),
          sheet: rect(sheet), title: rect(sheet.querySelector('.t')), sub: rect(sheet.querySelector('.s')), list: rect(sheet.querySelector('.list')), bar: rect(sheet.querySelector('.bar')), barFill: rect(sheet.querySelector('.bar i')),
          rows: withRows ? rows : undefined, sheetStyle: cs(sheet, 'transform', 'opacity'), paneStyle: cs(w.querySelector('.pane'), 'filter', 'opacity'),
          titleText: sheet.querySelector('.t').textContent, subText: sheet.querySelector('.s').textContent,
        };
      };
      const plan = g.plan;
      const frames = {};
      frames.start = pick(0.001, true);
      frames.middle = pick(0.4, true);
      frames.workEnd = pick(plan.workEnd, true);
      const hand = [];
      for (let i = 0; i <= 20; i++) hand.push(pick(plan.handStart + 1.1 * i / 20));
      frames.hand = hand;
      const sheet = w.querySelector('.sheet');
      const vars = {};
      for (const k of ['--glass', '--glassE', '--glassHi', '--gshadow', '--tx', '--tx2', '--tx3', '--acc', '--good', '--ov10', '--spk']) vars[k] = getComputedStyle(w).getPropertyValue(k).trim();
      const first = w.querySelector('.vi .r');
      const css = {
        vars,
        sheet: cs(sheet, 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'borderBottomLeftRadius', 'borderBottomRightRadius', 'borderTopLeftRadius', 'backdropFilter', 'boxShadow', 'backgroundColor'),
        title: cs(sheet.querySelector('.t'), 'fontFamily', 'fontSize', 'lineHeight', 'fontWeight', 'letterSpacing'),
        sub: cs(sheet.querySelector('.s'), 'fontSize', 'lineHeight', 'color', 'marginTop', 'marginBottom'),
        list: cs(sheet.querySelector('.list'), 'gridTemplateColumns', 'columnGap', 'marginBottom'),
        row: cs(first, 'height', 'gap', 'transition', 'display', 'alignItems'),
        rowLabel: cs(first.querySelector('.tx b'), 'fontSize', 'fontWeight', 'lineHeight'),
        glyph: cs(first.querySelector('.gl'), 'width', 'height', 'strokeWidth'),
        mark: cs(first.querySelector('.tr'), 'width', 'height'),
        markChildren: Array.from(first.querySelector('.tr').children).map((c) => ({ cls: c.getAttribute('class'), transition: getComputedStyle(c).transition })),
        bar: cs(sheet.querySelector('.bar'), 'height', 'borderRadius', 'backgroundColor'),
        barFill: cs(sheet.querySelector('.bar i'), 'transition', 'backgroundColor', 'transformOrigin'),
        spinnerSpoke: cs(first.querySelector('.spn i'), 'width', 'height', 'left', 'top', 'transformOrigin'),
        spinner: cs(first.querySelector('.spn'), 'width', 'height', 'animationDuration', 'animationTimingFunction'),
        check: cs(first.querySelector('.ck'), 'width', 'height'),
      };
      return { frames, css, motion: null, plan: { HAND: 1.1, HOLD: 0.15, workEnd: plan.workEnd, handStart: plan.handStart } };
    });
    data.motion = motion;
    out[theme] = data;
    if (theme === 'dark') out.words = await page.evaluate(() => { const d = JSON.parse(document.getElementById('grdata').textContent); return { W1: d.words.W1, steps: d.steps, screenSteps: window.__gr.plan.screen }; });
    await page.close();
  }
  process.stdout.write(JSON.stringify(out, null, 1));
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
