// CZ-1: run the read-only locked cz.js, not a C# implementation, to make numeric/text fixtures.
// The board's external composer-lights helpers are supplied per CZ-12.10/11; their files are not shipped here.
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm'), crypto = require('node:crypto');
const root = path.resolve(__dirname, '../../..');
const rel = 'porting/first-release/design-src/customization/cz.js';
const source = fs.readFileSync(path.join(root, rel), 'utf8');
const context = vm.createContext({ window: {}, Component: class {}, BEAM_DEFAULTS: {}, AURA_DEFAULTS: {},
  hex2: h => [1,3,5].map(i => parseInt(h.slice(i,i+2),16)),
  // the playground's own mixHex (composer-lights/playground/src/logic.js:44), which the page calls: x + (b - x) * t, rounded half up
  mixHex: (a,b,t) => { const p = h => [1,3,5].map(i => parseInt(h.slice(i,i+2),16)), x = p(a), y = p(b); return '#' + x.map((v,i) => Math.round(v + (y[i] - v) * t).toString(16).padStart(2,'0')).join(''); },
  fmt: (v,n) => (Math.round(v*10**n)/10**n).toString(), clamp: (v,a,b) => Math.max(a,Math.min(b,v)) });
vm.runInContext(source, context, { filename: rel });
const fmt = [];
for (const v of [-0,-0.005,-0.5,-1.5,-2.675,0.005,0.5,1.005,2.675,3.69,14,0.000001,0.0000001,1e20,1e21])
 for (const n of [0,1,2,4,8]) fmt.push({v,n,text:context.fmt(v,n)});
const tone = [];
for (const hex of ['#000000','#FFFFFF','#1F30C6','#A14827','#FAFCFF','#06060B','#8C65BA','#123456','#FEEDAB','#00FFA5','#2DFFA5','#B4CF0E'])
 for (const title of ['#FFFFFF','#000000','#1C1C1E','#B95D3A']) for (const dark of [true,false])
  tone.push({hex,title,dark,text:vm.runInContext(`czTone('${hex}',${dark},'${title}')`, context)});
const ranges = [[.5,14,.05],[.2,3,.05],[0,1,.01],[-180,180,1],[0,2.5,.05],[0,180,1],[.25,3,.05],[0,2,.05],[0,26,1],[0,20,.5],[0,60,.5],[.35,4,.01],[.5,2,.01],[0,3,.05],[3,40,.5],[0,100,1],[0,40,1],[4,64,1],[.5,5,.25],[2,8,.5]];
const snapExpression = source.match(/const set = \(nv\) => this\.setOpt\(group, key, (.+)\);/)[1];
const designSnap = vm.runInContext(`(nv,min,max,step) => ${snapExpression}`, context);
const snap = ranges.flatMap(([min,max,step]) => [min-1,min,min+step/2,(min+max)/2,max,max+1].map(v => ({min,max,step,v,result:designSnap(v,min,max,step)})));
// Extract all literal UI text from the locked tables (CZ-5..8); placeholders are kept for runtime interpolation.
const spec = fs.readFileSync(path.join(root,'porting/first-release/customization-SPEC.md'),'utf8');
const tables = spec.slice(spec.indexOf('## 5. Style'),spec.indexOf('## 9. Voice orb'));
const texts = [...new Set([...tables.matchAll(/`([^`]+)`/g)].map(m=>m[1].replace(/\s*\n\s*/g,' ')))];
const constants = vm.runInContext('({style:CZ_STYLE,description:CZ_STYLE_D,palettes:CZ_PAL,shape:CZ_SHAPE_D})',context);
const paletteRel = 'porting/first-release/design-src/orb/liquid-palettes.json';
const paletteBytes = fs.readFileSync(path.join(root,paletteRel));
const palette = JSON.parse(paletteBytes);
const first = ['Claude','OpenAI','Gemini','DeepSeek','Grok','NVIDIA'];
const order = first.concat(Object.keys(palette).filter(k => !first.includes(k) && !k.startsWith('_')));
const nice = {OpenCodeGo:'OpenCode Go',CommandCode:'Command Code',NousResearch:'Nous Research',LiquidAI:'Liquid AI',Zai:'Z.ai',MiniMax:'MiniMax'};
const makers = order.map(id => {
  const m = {id,name:nice[id] || id,model:(palette[id]._meta.model || '').startsWith('(') ? '' : (palette[id]._meta.model || '')};
  for (const theme of ['dark','light']) for (const stop of ['deep','mid','light','highlight','title']) m[theme+'.'+stop] = palette[id][theme].high[stop];
  return m;
});
for (const m of makers) for (const dark of [true,false]) for (const stop of ['deep','mid','light','highlight']) {
  const theme = dark ? 'dark' : 'light', hex = m[theme+'.'+stop], title = m[theme+'.title'];
  tone.push({maker:m.id,stop,hex,title,dark,text:vm.runInContext(`czTone('${hex}',${dark},'${title}')`,context)});
}
const paletteSha256 = crypto.createHash('sha256').update(paletteBytes).digest('hex');
const border = JSON.parse(fs.readFileSync(path.join(root,'src/Marrow.Core/Models/ComposerLights/border-beam-tables.json'),'utf8'));
const swatches = Object.entries(constants.palettes).map(([id,name]) => ({id,name,colors:[0,4,6,8].map(i=>border.G[id].border[i].color)}));
// Execute the real row-building methods for every textual branch, while capturing their view models.
// Rendering helpers are seams only: titles, details and branches still come from cz.js.
context.CW = 472; context.CH = 46; context.CR = 22;
context.BBT = border; context.PAL_NAMES = Object.keys(constants.palettes);
const designTexts = vm.runInContext(`(() => {
  const found = new Set();
  const text = v => { if (typeof v === 'string' && v) found.add(v); return v; };
  const board = Object.create(CzComponent.prototype);
  Object.assign(board, {
    state: {beam:{},aura:{},adv:false}, st: () => ({}),
    beamEff: b => ({pulse:b.family==='pulse'}),
    model: () => ({name:'{maker name}'}), accent: () => ['', '{accent name}'],
    dx: (_,v) => text(v), row: (_,v) => {text(v.t); return v;},
    seg: (t,options) => {text(t); options.forEach(o => text(o.t)); return {};},
    segRow: (_,t,d) => {text(t);text(d);return {};},
    slider: (_,__,t,d) => {text(t); if(typeof d==='function'){text(d(true));text(d(false));}else text(d);return {};},
    toggle: (_,__,t,d) => {text(t);text(d);return {};}, cap: t => {text(t);return {};},
    card: v => v, sec: (_,t) => text(t), advRow: d => {text(d);return {};},
    voiceSec: () => ({}), dotsSec: () => ({}), isDefault: () => true
  });
  for (const family of ['rotate','pulse']) for(const size of ['md','line','sm','pulse-inner','pulse-outside'])
    for(const color of ['colorful','mono']) for(const staticColors of [true,false]) for(const theme of ['auto','dark','light']) {
      board.state.beam={family,size,color,staticColors,theme}; board.beamRows();
    }
  for(const scheme of ['original','maker','accent','custom']) for(const rim of [true,false]) for(const dots of [true,false]) {
    board.state.aura={scheme,rim,dots}; board.auraRows();
  }
  for(const def of [true,false]) {board.isDefault=()=>def; board.resetRow('beam'); board.resetRow('aura');}
  board.sections('off','B');
  Object.values(CZ_STYLE).forEach(text); Object.values(CZ_STYLE_D).forEach(text);
  return [...found];
})()`, context);
const output = {designTexts,swatches,paletteSource:paletteRel,paletteSha256,makers,source:rel,sha256:crypto.createHash('sha256').update(source).digest('hex'),fmt,tone,snap,texts,constants};
fs.writeFileSync(path.join(__dirname,'expected.json'),JSON.stringify(output,null,2)+'\n');

// Generated catalogue is source data, not a re-computation of the design's colours.
const cs = `// PORT-ONLY CZ-1: generated from ${paletteRel} by tests/fixtures/customization/generate.cjs.
using System.Collections.ObjectModel;
using System.Text.Json;
namespace Marrow.Core.Port.Settings;

public static class MakerLiquids
{
    public const string SourceSha256 = "${paletteSha256}";
    private const string Json = """
${JSON.stringify(makers)}
""";
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> All { get; } = Load();
    public static IReadOnlyDictionary<string, string> For(string id) => All.First(m => m["id"] == id);
    private static IReadOnlyList<IReadOnlyDictionary<string, string>> Load()
    {
        using var doc = JsonDocument.Parse(Json);
        return Array.AsReadOnly(doc.RootElement.EnumerateArray().Select(row =>
            (IReadOnlyDictionary<string, string>)new ReadOnlyDictionary<string, string>(row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal))).ToArray());
    }
}
`;
if (process.argv.includes('--catalog')) fs.writeFileSync(path.join(root,'src/Marrow.Core/Port/Settings/MakerLiquids.cs'),cs);
