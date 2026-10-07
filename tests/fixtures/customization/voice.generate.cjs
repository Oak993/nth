// CZ-T75: execute the locked design's SCRIPT/SYN/STREAM/synthAt, independently of the port.
const fs = require('fs'), path = require('path'), vm = require('vm');
const source = path.resolve(__dirname, '../../../porting/first-release/design-src/voice-composer/voice-composer.js');
const js = fs.readFileSync(source, 'utf8');
const design = path.resolve(__dirname, '../../../porting/first-release/design-src/customization');
const cmp = fs.readFileSync(path.join(design, 'voicecmp.js'), 'utf8');
const opts = fs.readFileSync(path.join(design, 'voiceopts.js'), 'utf8');
const page = fs.readFileSync(path.join(design, 'cz.js'), 'utf8');
const css = fs.readFileSync(path.join(design, 'gen.py'), 'utf8');
const follow = cmp.match(/const vcFollow =[^;]+;/)[0];
const sizes = cmp.match(/const VC_W =[^;]+;/)[0];
const offsets = cmp.match(/out \? (\d+) : (\d+)\) \+ 'px; --cw:/);
const durationWidth = Number(css.match(/--cl ([.\d]+)s var\(--ez\)/)[1]);
const curve = css.match(/--ez:cubic-bezier\(([^)]+)\)/)[1];
const bezier = page.slice(page.indexOf('function czBez('), page.indexOf('const czLum ='));
const lightStep = opts.match(/V.lv = reduce \? tgt : vcFollow\(V.lv, tgt, dt, ([.\d]+), ([.\d]+)\);/);
const speaker = js.slice(js.indexOf('const SCRIPT ='), js.indexOf('// ------------------------------------------------------------------ audio file'));
const stream = js.slice(js.indexOf('const STREAM ='), js.indexOf('const FILE_WORDS ='));
const context = {};
const final = js.slice(js.indexOf('function planFinal('), js.indexOf('// ------------------------------------------------------------------', js.indexOf('function planFinal(')));
// Cut after the function; the following text is unrelated UI code.
const finalFunction = final.slice(0, final.indexOf('\n}\n') + 3);
vm.runInNewContext(speaker + stream + finalFunction + follow + sizes + bezier + `
const stop = SYN.end + .35, S = { source: 'synthetic' }, sess = {texts: [], ev: 0, final: []};
while (sess.ev < STREAM.length && STREAM[sess.ev].t <= stop) { const e = STREAM[sess.ev++]; sess.texts[e.i] = e.text; }
const duration = planFinal(0, stop);
function ez(x) { return czBez(${curve}, x); }
function lightSamples(from,target,times) { let v=from,previous=0; return times.map(t=>{v=vcFollow(v,target,t-previous,${lightStep[1]},${lightStep[2]}); previous=t; return {t,value:v<.003?0:v>.997?1:v};}); }
result = { ...SYN, stream: STREAM, stop, processing: Math.max(.75,duration+.3), final: sess.final,
 geometry: [0,.21,.42].map(t=>({t,width:VC_W.in-(VC_W.in-VC_W.out)*ez(t/${durationWidth}),left:${offsets[2]}+(${offsets[1]}-${offsets[2]})*ez(t/${durationWidth})})),
 light: [0,.35,1].map(target=>({target, down:lightSamples(1,target,[.17,.34,.51]), up:lightSamples(target,1,[.2,.4,.6])})),
 levels: [0,.45,.55,1,2,3,4,5,6,7].map(t => ({t, ...synthAt(t,{})})) };`, context);
fs.writeFileSync(path.join(__dirname, 'voice.json'), JSON.stringify(context.result, null, 2) + '\n');
