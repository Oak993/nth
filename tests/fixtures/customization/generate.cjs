// CZ-1: run the read-only locked cz.js, not a C# implementation, to make numeric/text fixtures.
// The board's external composer-lights helpers are supplied per CZ-12.10/11; their files are not shipped here.
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), crypto = require('node:crypto');
const root = path.resolve(__dirname, '../../..');
const rel = 'porting/first-release/design-src/customization/cz.js';
const source = fs.readFileSync(path.join(root, rel), 'utf8');
const context = vm.createContext({ window: {}, Component: class {}, BEAM_DEFAULTS: {}, AURA_DEFAULTS: {},
  hex2: h => [1,3,5].map(i => parseInt(h.slice(i,i+2),16)),
  mixHex: (a,b,t) => '#' + [1,3,5].map(i => Math.round(parseInt(a.slice(i,i+2),16)*(1-t)+parseInt(b.slice(i,i+2),16)*t).toString(16).padStart(2,'0')).join(''),
  fmt: (v,n) => (Math.round(v*10**n)/10**n).toString(), clamp: (v,a,b) => Math.max(a,Math.min(b,v)) });
vm.runInContext(source, context, { filename: rel });
const fmt = [];
for (const v of [-0,-0.005,-0.5,-1.5,-2.675,0.005,0.5,1.005,2.675,3.69,14,0.000001,0.0000001,1e20,1e21])
 for (const n of [0,1,2,4,8]) fmt.push({v,n,text:context.fmt(v,n)});
const tone = [];
for (const hex of ['#000000','#FFFFFF','#1F30C6','#A14827','#FAFCFF','#06060B','#8C65BA','#123456','#FEEDAB'])
 for (const title of ['#FFFFFF','#000000','#1C1C1E','#B95D3A']) for (const dark of [true,false])
  tone.push({hex,title,dark,text:vm.runInContext(`czTone('${hex}',${dark},'${title}')`, context)});
const ranges = [[.5,14,.05],[.2,3,.05],[0,1,.01],[-180,180,1],[0,2.5,.05],[0,180,1],[.25,3,.05],[0,2,.05],[0,26,1],[0,20,.5],[0,60,.5],[.35,4,.01],[.5,2,.01],[0,3,.05],[3,40,.5],[0,100,1],[0,40,1],[4,64,1],[.5,5,.25],[2,8,.5]];
const snap = ranges.flatMap(([min,max,step]) => [min-1,min,min+step/2,(min+max)/2,max,max+1].map(v => ({min,max,step,v,result:+(Math.round(Math.max(min,Math.min(max,v))/step)*step).toFixed(4)})));
// Extract all literal UI text from the locked tables (CZ-5..8); placeholders are kept for runtime interpolation.
const spec = fs.readFileSync(path.join(root,'porting/first-release/customization-SPEC.md'),'utf8');
const tables = spec.slice(spec.indexOf('## 5. Style'),spec.indexOf('## 9. Voice orb'));
const texts = [...new Set([...tables.matchAll(/`([^`]+)`/g)].map(m=>m[1]))];
const constants = vm.runInContext('({style:CZ_STYLE,description:CZ_STYLE_D,palettes:CZ_PAL,shape:CZ_SHAPE_D})',context);
const output = {source:rel,sha256:crypto.createHash('sha256').update(source).digest('hex'),fmt,tone,snap,texts,constants};
fs.writeFileSync(path.join(__dirname,'expected.json'),JSON.stringify(output,null,2)+'\n');
