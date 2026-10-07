const fs=require('fs'),vm=require('vm'),path=require('path');
const page=fs.readFileSync(path.resolve(__dirname,'../../../porting/first-release/design-src/multi-account/multi-account.html'),'utf8');
const take=(from,to)=>page.slice(page.indexOf(from),page.indexOf(to,page.indexOf(from)));
const code=take('const T = { sb:', '// ---------------------------------------------------------------- an optional soft sound')+take('function geo(W, H)', 'function capsule(ctx')+take('function phaseVals(t)', '// ---------------------------------------------------------------- concept B: phase lock');
const cases=[];
for(const dark of [false,true])for(const rm of [false,true])for(const t of [0,1000,1300,1999,2000,2500,2999,3000,3400,3800,4400,4700,5000,5500]) {
  const paths=[],glows=[],ghosts=[];let points=[];
  const ctx={globalAlpha:1,lineWidth:1,beginPath(){points=[]},moveTo(x,y){points.push([x,y,1])},lineTo(x,y){points.push([x,y,0])},stroke(){paths.push({alpha:this.globalAlpha,width:this.lineWidth,points:points.slice()})},createLinearGradient(){return {addColorStop(){}}},save(){},restore(){this.globalAlpha=1}};
  const sandbox={M:{isDark:()=>dark},Math,ctx,W:420,H:160,t,dark:()=>dark,RM:()=>rm,glowAt:(ctx,p,r,c,a)=>{if(a>.01)glows.push([...p,r,...c,a])},ghost:(ctx,g,p,t,a,sc)=>ghosts.push([...p,a,sc])};
  vm.createContext(sandbox);vm.runInContext(code+'; drawA(ctx,geo(W,H),t,W,H);',sandbox);
  const out=[paths.length,glows.length,...ghosts[0]];
  for(const i of [...new Set([0,Math.floor((paths.length-1)/2),paths.length-1])].filter(i=>i>=0&&i<paths.length)) {
    const p=paths[i];out.push(p.alpha,p.width,p.points.length);
    for(const k of [...new Set([0,Math.floor((p.points.length-1)/2),p.points.length-1])].filter(k=>k>=0))out.push(...p.points[k]);
  }
  for(const g of glows)out.push(...g);
  cases.push({t,dark,rm,out});
}
// Reproduce for review into a separate scratch artifact. Never overwrite the locked C# reference.
process.stdout.write(JSON.stringify(cases,null,2)+'\n');
