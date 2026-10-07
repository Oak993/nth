const fs = require('fs'), http = require('http'), crypto = require('crypto');
const {chromium} = require('C:/Users/omera/AppData/Local/Temp/claude/E--Droppy/c13e7b6a-fc79-4b04-a170-a4b2f43ca682/scratchpad/pw/node_modules/playwright-core');
const source = 'E:/Droppy/work/design/customization/customization.html';
const out = process.argv[2];
if (!out || !fs.existsSync(out)) throw new Error('Pass an existing empty capture directory. Locked evidence is never overwritten.');
for (const name of ['claude-dark.png','claude-light.png','chrome-claude.json']) {
  if (fs.existsSync(out+'/'+name)) throw new Error('Refusing to overwrite '+name);
}
const html = fs.readFileSync(source);
const wrap = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><style>:root{color-scheme:light;padding:env(safe-area-inset-top) 0 env(safe-area-inset-bottom)}body{margin:0;font:14px system-ui;background:#fafafa}img{max-width:100%}[hidden]{display:none!important}</style></head><body>' + html.toString() + '</body></html>';
const server = http.createServer((req,res) => {res.writeHead(200, {'content-type':'text/html; charset=utf-8','content-security-policy':"default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: blob:; connect-src 'self'"});res.end(wrap);});
server.listen(0,'127.0.0.1',async()=>{
  let browser;
  try {
    browser = await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',args:['--use-angle=d3d11','--enable-gpu','--disable-background-networking']});
    const evidence = {source,sha256:crypto.createHash('sha256').update(html).digest('hex'),chrome:browser.version(),viewport:{width:1440,height:1000},dpr:1,appearances:{}};
    for(const dark of [true,false]) {
      const theme = dark?'dark':'light';
      const context = await browser.newContext({viewport:evidence.viewport,deviceScaleFactor:1,colorScheme:theme});
      await context.route('**/*',route => route.request().url().startsWith('http://127.0.0.1:') ? route.continue() : route.abort());
      const page=await context.newPage();
      await page.goto('http://127.0.0.1:'+server.address().port+'/');
      await page.waitForFunction(()=>window.__dcReady===true && !!window.__cz);
      await page.getByRole('radiogroup',{name:'Theme',exact:true}).getByRole('radio',{name:theme==='dark'?'Dark':'Light',exact:true}).click();
      await page.waitForTimeout(900);
      await page.locator('.czpage.on .czms .czmc[aria-label^="Preview model:"]').click();
      await page.waitForTimeout(500);
      evidence.appearances[theme]=await page.evaluate(()=>{
        const chip=document.querySelector('.czpage.on .czms .czmc[aria-label^="Preview model:"] .czdot');
        const menu=document.querySelector('.czpage.on .czms.open .pitem[aria-checked="true"] .czdot');
        const record=el=>{const r=el.getBoundingClientRect();return {css:getComputedStyle(el).backgroundColor,center:{x:Math.floor(r.x+r.width/2),y:Math.floor(r.y+r.height/2)},bounds:{x:r.x,y:r.y,width:r.width,height:r.height}};};
        const appearance=el=>{const s=getComputedStyle(el);const r=el.getBoundingClientRect();return {className:el.className,background:s.backgroundColor,color:s.color,bounds:{x:r.x,y:r.y,width:r.width,height:r.height}};};
        const composer=document.querySelector('.czpage.on .pvw .cmp');
        return {stateDark:window.__cz.dark(),board:document.querySelector('.cz').className,window:appearance(document.querySelector('.czwin')),heading:appearance(document.querySelector('.czhdr h1')),composer:composer?appearance(composer):null,composerHtml:composer?composer.outerHTML.slice(0,600):null,chip:record(chip),menu:record(menu),model:window.__cz.model(),maker:window.__cz.maker()};
      });
      await page.screenshot({path:out+'/claude-'+theme+'.png'});
      await context.close();
    }
    fs.writeFileSync(out+'/chrome-claude.json',JSON.stringify(evidence,null,2)+'\n');
    console.log(JSON.stringify(evidence,null,2));
  } catch(e){console.error(e);process.exitCode=1;} finally{if(browser)await browser.close();server.close();}
});
