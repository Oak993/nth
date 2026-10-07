// Independent executable v22 makeInk vectors; never imports the C# implementation.
import fs from 'node:fs';
import crypto from 'node:crypto';
const source=fs.readFileSync('porting/first-release/design-src/models-palette/effort-liquid-locked-v22.dc.html');
const text=source.toString();const method=text.slice(text.indexOf('  makeInk(NX'),text.indexOf('  // The ink lives in a Web Worker'));
const make=new Function('return ({'+method+'}).makeInk')();
const rows=[];
for(const [nx,ny] of [[240,10],[480,20]]) {
 const f=make(nx,ny);
 for(let step=1;step<=180;step++) {f.step(1/60);if([1,60,180].includes(step))rows.push({nx,ny,step,mass:Array.from(f.d).reduce((a,b)=>a+b,0),cells:[[nx,Math.round(ny/2)],[nx-3,3],[nx-10,Math.round(ny/2)],[Math.round(nx/2),3]].map(([i,j])=>({i,j,d:f.d[f.IX(i,j)],u:f.u[f.IX(i,j)],v:f.v[f.IX(i,j)]}))});}
}
process.stdout.write(JSON.stringify({sha256:crypto.createHash('sha256').update(source).digest('hex'),rows},null,2)+'\n');
