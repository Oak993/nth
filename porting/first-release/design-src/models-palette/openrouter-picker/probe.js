const path = require('path');
const { chromium } = require(path.resolve(__dirname, '..', 'pw', 'node_modules', 'playwright-core'));
(async () => {
  const browser = await chromium.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  const page = await (await browser.newContext({ viewport: { width: 900, height: 720 } })).newPage();
  await page.goto(require('url').pathToFileURL(path.join(__dirname, 'Main.dc.html')).href);
  await page.waitForFunction(() => window.__dcReady === true);
  await page.waitForTimeout(800);
  await page.click('.field input');
  await page.keyboard.type('k');
  await page.waitForTimeout(800);
  const snap = () => page.evaluate(() => {
    const list = document.querySelector('.list');
    const folds = Array.from(list.children).map((f) => ({ c: f.className, h: Math.round(f.getBoundingClientRect().height), o: (+getComputedStyle(f).opacity).toFixed(2) }));
    return { t: Math.round(performance.now()), listH: Math.round(list.getBoundingClientRect().height), scrollTop: list.scrollTop, scrollH: list.scrollHeight, folds: folds.filter((f) => f.h > 0).map((f) => `${f.c === 'fold off' ? 'x' : 'o'}${f.h}@${f.o}`).join(' ') };
  });
  console.log(JSON.stringify(await snap()));
  await page.keyboard.type('i');
  for (let i = 0; i < 8; i++) { console.log(JSON.stringify(await snap())); await page.waitForTimeout(45); }
  await browser.close();
})();
