// Reconcile first-release acceptance inventory to the committed plan/checklist and later owner rulings.
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
export function requiredReleaseScope(root) {
 const records=[],ids=new Set(),method='source/owner contract, interaction, visual, persistence/restart, failure/cancellation, end-to-end';
 const add=(id,title,source)=>{if(ids.has(id))return;ids.add(id);records.push({id,kind:'release-unit',title,status:'SPECIFIED',verification_method:method,source_evidence:[source]});};
 readFileSync(resolve(root,'porting/first-release/BUILD-PLAN.md'),'utf8').split(/\r?\n/).forEach((line,i)=>{
  const m=line.match(/^\| \*\*((?:SA|VT|GR|WV|CZ|UE|SK|ES|CB|ST)-[0-9]+[a-f]?|D-VN)\*\*([^|]*)/);
  if(m)add('FR-'+m[1],m[2].trim(),`porting/first-release/BUILD-PLAN.md:${i+1}`);
 });
 let section=null,n=0;
 readFileSync(resolve(root,'porting/first-release/multi-account-CHECKLIST.md'),'utf8').split(/\r?\n/).forEach((line,i)=>{
  const m=line.match(/^## (\d+)\./);if(m){section=m[1];n=0;}else if(/^## /.test(line))section=null;
  if(section&&/^\s*- /.test(line))add(`FR-MA-${section}-${++n}`,line.replace(/^\s*- /,'').slice(0,180),`porting/first-release/multi-account-CHECKLIST.md:${i+1}`);
 });
 for(const [id,title,od] of [
  ['SK-18','Import skills from other apps','OD-0118'],['UE-6','Exact-version What\u2019s new','OD-0111'],['ICON-D','Aurora D identity assets','OD-0112'],
  ['WALLPAPERS','Built-in and animated wallpaper','OD-0109'],['MA-STANDBY','Every installed service: real history load, no AI turn, deletion, unchanged settings and credentials','OD-0124'],
  ['VOICE-CHROME-ORACLE','Same-condition locked Chrome references; unchanged V-T133 detector','OD-0125'],['CUSTOMIZATION-FADE','Approved 0.1 s curve at 60 Hz with no extra margin','OD-0126']])add('FR-'+id,title,`porting/owner_decisions.md#${od}`);
 return records;
}
