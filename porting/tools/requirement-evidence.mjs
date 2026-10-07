// Derive requirement status from immutable ledger records, test traits and actual TRX outcomes.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { resolve, relative } from 'node:path';
import { json, validateArtifact, validateBinding, validateRunMetadata } from './evidence-binding.mjs';
import { isDeepStrictEqual } from 'node:util';
export const TEST_PROJECTS = ['Core','Services','UI','App'].map(n => `tests/Marrow.${n}.Tests/Marrow.${n}.Tests.csproj`);
export function testArguments(project, output, filter=null) {
 const argv=['test',project,'-c','Debug','-p:EnableWindowsTargeting=true','--logger','trx;LogFileName=results.trx','--results-directory',output,'--nologo'];
 if(filter)argv.push('--filter',filter);return argv;
}
export function discoveryArguments(project) { return ['test',project,'-c','Debug','-p:EnableWindowsTargeting=true','--no-build','--no-restore','--list-tests','--nologo']; }
export function ledgerRecords(root) {
 const rs = [...json(root,'porting/parity_matrix.json').requirements, ...json(root,'porting/ui_contract.json').records];
 const ids = new Set();
 for (const r of rs) { if(!r.id || ids.has(r.id)) throw Error(`duplicate/missing ledger ID ${r.id}`); ids.add(r.id); }
 return rs;
}
export function exempt(record) { return ['RETIRED','NOT_APPLICABLE_WITH_EVIDENCE'].includes(record.status); }
export function walk(root, suffix) {
 const out=[];
 for(const name of readdirSync(root).sort()) {
  if(['bin','obj','.git','node_modules'].includes(name)) continue;
  const path=resolve(root,name); if(statSync(path).isDirectory())out.push(...walk(path,suffix));else if(name.endsWith(suffix))out.push(path);
 }
 return out;
}
export function traits(root) {
 const byReq=new Map(), unknown=[];const ids=new Set(ledgerRecords(root).map(r=>r.id));
 for(const path of walk(resolve(root,'tests'),'.cs')) {
  const text=readFileSync(path,'utf8'),ns=(text.match(/^\s*namespace\s+([\w.]+)/m)||[])[1]||'';
  let cls=null,pending=[],isTest=false;
  for(const line of text.split(/\r?\n/)) {
   const c=line.match(/\b(?:class|record)\s+(\w+)/);if(c&&!/^\s*\/\//.test(line))cls=c[1];
   if(/^\s*\[/.test(line)) {
    for(const m of line.matchAll(/Trait\(\s*"Req"\s*,\s*"([^"]+)"\s*\)/g))pending.push(m[1]);
    if(/\b(Fact|Theory|AvaloniaFact|AvaloniaTheory|WindowsOnlyFact|WindowsOnlyTheory|WindowsShimFact|WindowsGuidanceFact|RefOrbsTheory|RefGlowTheory)\b/.test(line))isTest=true;
    continue;
   }
   const m=line.match(/^\s*public\s+(?:async\s+)?(?:void|Task(?:<[^>]+>)?)\s+(\w+)\s*\(/);
   if(m) {
    if(isTest&&cls)for(const id of pending){if(!ids.has(id))unknown.push(`${id} in ${relative(root,path)}`);if(!byReq.has(id))byReq.set(id,new Set());byReq.get(id).add(`${ns}.${cls}.${m[1]}`);}
    pending=[];isTest=false;
   }else if(line.trim()&&!/^\s*\/\//.test(line)){pending=[];isTest=false;}
  }
 }
 return {byReq,unknown};
}
const decode=s=>s.replace(/&quot;/g,'"').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&apos;/g,"'").replace(/&amp;/g,'&');
export function trxResults(root, artifacts) {
 const results=new Map();let passed=0,failed=0,skipped=0,total=0;
 for(const a of artifacts.filter(a=>a.path.endsWith('.trx'))) {
  const xml=readFileSync(validateArtifact(root,a),'utf8');let count=0;
  for(const m of xml.matchAll(/<UnitTestResult\b([^>]+)>/g)) {
   const attrs=Object.fromEntries([...m[1].matchAll(/(\w+)="([^"]*)"/g)].map(x=>[x[1],decode(x[2])]));
   if(!attrs.testName||!attrs.outcome)throw Error(`malformed TRX result in ${a.path}`);
   if(!results.has(attrs.testName))results.set(attrs.testName,[]);results.get(attrs.testName).push(attrs.outcome);
   count++;total++;if(attrs.outcome==='Passed')passed++;else if(attrs.outcome==='Failed')failed++;else skipped++;
  }
  if(!count)throw Error(`no results in ${a.path}`);
  const counters=xml.match(/<Counters\b([^>]+)\/?\s*>/);
  const attrs=Object.fromEntries([...(counters?.[1]||'').matchAll(/(\w+)="([^"]*)"/g)].map(x=>[x[1],Number(x[2])]));
  if(attrs.total!==count)throw Error(`TRX counters/results differ: ${a.path}`);
 }
 return {results,passed,failed,skipped,total};
}
export function deriveRequirements(root, records, run, bound) {
 const {byReq,unknown}=traits(root), {results,...totals}=trxResults(root,run.artifacts||[]);
 const requirements={},counts={};
 const unknowns=readFileSync(resolve(root,'porting/unknowns.md'),'utf8');
 const open=new Set(unknowns.split(/^## /m).filter(b=>/\*\*Status:\*\*\s*(OPEN|INVESTIGATING|BLOCKED_ON_OWNER|BLOCKED_ON_ACCESS)/.test(b)).map(b=>(b.match(/^(UNK-\d+)/)||[])[1]));
 for(const r of records) {
  const tests=[...(byReq.get(r.id)||[])].sort().map(name=>({test:name,outcomes:[...results].filter(([n])=>n===name||n.startsWith(name+'(')).flatMap(([,out])=>out)}));
  let status;
  if(exempt(r))status=r.status;
  else if(r.blocked_by)status='BLOCKED';
  else if(!tests.length)status='NO_TEST';
  else if(tests.some(t=>t.outcomes.some(o=>o!=='Passed')))status='FAILING';
  else if(tests.some(t=>!t.outcomes.length))status='NOT_RUN';
  else if(!bound)status='UNBOUND';
  else if(open.has(r.depends_on_unknown)||['UNK-0003','UNK-0011'].includes(r.depends_on_unknown))status='PARTIAL';
  else status='VERIFIED';
  requirements[r.id]={status,tests};counts[status]=(counts[status]||0)+1;
 }
 return {requirements,counts,verified:counts.VERIFIED||0,applicable:records.filter(r=>!exempt(r)).length,total:records.length,unknown_ids_in_tests:unknown,test_totals:totals};
}
export function validateRequirementReport(doc, ctx) {
 if(doc?.schema!=='marrow/requirement-verification/2')throw Error('missing requirement evidence schema v2; rerun req_verify.mjs');
 validateBinding(doc.run?.binding,ctx.binding);
 if(!doc.run.complete||doc.run.exit_code!==0)throw Error('requirement run is partial or failed');
 const run=doc.run;validateRunMetadata(run);
 if(!Array.isArray(run.commands)||run.commands.length!==TEST_PROJECTS.length)throw Error('not every solution test project ran');
 const files=new Map((run.artifacts||[]).map(a=>[a.path,a]));
 if(files.size!==(run.artifacts||[]).length)throw Error('duplicate run artifact');
 for(const a of run.artifacts||[])validateArtifact(ctx.root,a);
 for(const [i,project] of TEST_PROJECTS.entries()) {
  const c=run.commands[i],dir=`${run.dir}/project-${i}`;
  if(c.project!==project||c.exit_code!==0||c.discovery_exit_code!==0||c.filter||!isDeepStrictEqual(c.command,['dotnet',...testArguments(project,dir)])||!isDeepStrictEqual(c.discovery_command,['dotnet',...discoveryArguments(project)]))throw Error(`wrong/partial command for ${project}`);
  for(const [field,name] of [['log','command.txt'],['trx','results.trx'],['discovery','discovery.txt']])if(c[field]!==`${dir}/${name}`||!files.has(c[field]))throw Error(`missing hash-linked ${field} for ${project}`);
  const trx=readFileSync(validateArtifact(ctx.root,files.get(c.trx)),'utf8');
  const assembly=project.split('/').at(-1).replace('.csproj','.dll');
  const codeBases=[...trx.matchAll(/<TestMethod\b[^>]*codeBase="([^"]+)"/g)].map(m=>decode(m[1]));
  if(!codeBases.length||codeBases.some(p=>p.split(/[\\/]/).at(-1)!==assembly))throw Error(`wrong assembly in ${c.trx}`);
  const discovery=readFileSync(validateArtifact(ctx.root,files.get(c.discovery)),'utf8').split(/The following Tests are available:/)[1];
  if(!discovery)throw Error(`no discovery list for ${project}`);
  const discovered=discovery.split(/\r?\n/).map(l=>l.trim()).filter(Boolean).sort();
  const actual=[...trx.matchAll(/<UnitTestResult\b[^>]*testName="([^"]+)"/g)].map(m=>decode(m[1])).sort();
  if(!isDeepStrictEqual(discovered,actual))throw Error(`discovered tests/results differ for ${project}`);
 }
 const expected=deriveRequirements(ctx.root,ctx.records,doc.run,true);
 if(!isDeepStrictEqual(doc.requirements,expected.requirements)||!isDeepStrictEqual(doc.counts,expected.counts)||doc.total!==expected.total||doc.applicable!==expected.applicable||doc.verified!==expected.verified)throw Error('requirement report disagrees with ledgers, test traits or raw outcomes');
 if(expected.unknown_ids_in_tests.length)throw Error(`unknown test IDs: ${expected.unknown_ids_in_tests.slice(0,5).join(', ')}`);
 if(expected.test_totals.failed||expected.test_totals.skipped||!expected.test_totals.passed)throw Error('tests failed, skipped or never ran');
 const outstanding=ctx.records.filter(r=>!exempt(r)&&expected.requirements[r.id].status!=='VERIFIED');
 if(outstanding.length)throw Error(`VERIFIED ${expected.verified}/${expected.applicable}; outstanding ${outstanding.length}: ${outstanding.slice(0,12).map(r=>r.id).join(', ')}`);
 return `VERIFIED ${expected.verified}/${expected.applicable}; raw TRX hashes and exact inputs match`;
}
