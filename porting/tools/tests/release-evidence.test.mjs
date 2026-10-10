// PORT_CONTRACT §14 negative controls; fixtures are synthetic and never release evidence.
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { validateSemanticGate, validateRequirementReport, dimensionsFor, SCENARIO_CHECKS } from '../release-evidence.mjs';

const binding = { head: 'a'.repeat(40), baseline: 'b'.repeat(40), baseline_tree: 'c'.repeat(40), src_tree: 'd'.repeat(40), tests_tree: 'e'.repeat(40), inputs_sha256: 'f'.repeat(64), dirty: [] };
const records = [{ id: 'PR-TEST-001', status: 'IMPLEMENTED', verification_method: 'interaction, visual, persistence/restart, failure/cancellation', source_evidence: ['source:1'], windows_path: 'src/Example.cs' }];
function fixture() {
 const root = mkdtempSync(join(tmpdir(), 'marrow-gate-test-'));
 mkdirSync(join(root, 'src')); writeFileSync(join(root, 'src/Example.cs'), 'real code');
 writeFileSync(join(root, 'proof.txt'), 'synthetic proof');
 const receipt = { path: 'proof.txt', sha256: 'b5868aa99954444b6c7bdeaf04476f9f22cb5f99890bbbf4a36f763be8a98158' };
 // Derive the fixture hash, not a production acceptance value.
 const png=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aGZkAAAAASUVORK5CYII=','base64');
 const put=(path,bytes)=>{writeFileSync(join(root,path),bytes);return {path,sha256:createHash('sha256').update(bytes).digest('hex')};};
 const reference=put('reference.png',png),windows=put('windows.png',png),policy=put('policy.json',JSON.stringify({id:records[0].id,method:'fixture comparison',authority:'synthetic test fixture',metrics:{error:{max:0}}}));
 const conditions={logical_width:1,logical_height:1,scale_percent:100,theme:'fixture',state:'fixture'};
 const measurements=put('measurements.json',JSON.stringify({id:records[0].id,status:'PASS',reference_sha256:reference.sha256,windows_sha256:windows.sha256,policy_sha256:policy.sha256,metrics:{error:0},conditions}));
 const visual={scope:'source-defined',method:'fixture comparison',reference,windows,policy,measurements,reference_provenance:{baseline:binding.baseline,source:'source:1'},...conditions};
 return { root, binding, records, scope: [], ownerDecisions: [], receipt, visual, hash: () => createHash('sha256').update('real code').digest('hex') };
}
const { createHash } = await import('node:crypto');
function evidence(ctx, kind) {
 return { id: kind, kind, status: 'PASS', binding, command: ['synthetic', kind], exit_code: 0,
  started_at: '2026-10-07T00:00:00Z', finished_at: '2026-10-07T00:00:01Z',
  environment: { os: 'Windows 11', platform: 'win32', architecture: 'x64', tools: { dotnet: '10.0.401' } },
  artifacts: [{ path: 'proof.txt', sha256: createHash('sha256').update('synthetic proof').digest('hex') },...(kind==='visual'?[ctx.visual.windows]:[])],
  producer: 'author', assertions: records.map(r=>({id:r.id,verification_method:r.verification_method,source_evidence:r.source_evidence,implementation_paths:['src/Example.cs'],trace:Object.fromEntries(['entrypoint','handler','state_transition','side_effects','persistence','failure_cancellation','authority'].map(n=>[n,'synthetic trace'])),checks:[{name:'source-expected result',expected:'fixture result',actual:'fixture result',status:'PASS'}],visual:ctx.visual})) };
}
function manifest(ctx, gate) {
 const kinds = gate === 'C' ? ['production-review'] : ['unit','contract','interaction','visual','restart','failure','end-to-end'];
 return { schema: 'marrow/release-evidence/1', binding, evidence: [...kinds.map(k => evidence(ctx,k)), {...evidence(ctx,'negative-gate'), negative_cases: ['missing-evidence','unverified-requirement','controlled-regression','stale-revision','tampered-artifact']}],
  coverage: records.map(r => ({ id: r.id, verification_method: r.verification_method, status: 'PASS',
   implementation_paths: ['src/Example.cs'], evidence: kinds, review: 'Trace control to handler and actual state transition.' })),
  exemptions: [], lifecycle: {}, independent_review: null, negative_checks: ['negative-gate'] };
}
function check(gate, ctx, doc) { return validateSemanticGate(gate, doc, ctx); }
for (const gate of ['C','D']) {
 test(`${gate}: complete fixture passes; absent evidence, unknown/omitted IDs and failed status reject`, () => {
  const c=fixture(); try {
   const m=manifest(c,gate); assert.doesNotThrow(()=>check(gate,c,m));
   for(const mutate of [m=>m.evidence.pop(), m=>m.coverage=[], m=>m.coverage[0].id='PR-UNKNOWN-001', m=>m.coverage[0].status='NOT_RUN', m=>m.coverage.push({...m.coverage[0]})]) {
    const bad=structuredClone(m);mutate(bad);assert.throws(()=>check(gate,c,bad));
   }
  } finally { rmSync(c.root,{recursive:true,force:true}); }
 });
 test(`${gate}: stale revision/tree, baseline, command, missing/tampered proof and dirty code reject`, () => {
  const c=fixture();try {
   const m=manifest(c,gate);
   for(const mutate of [m=>m.binding.head='0'.repeat(40), m=>m.evidence[0].binding.src_tree='0'.repeat(40), m=>m.evidence[0].binding.baseline='0'.repeat(40), m=>m.evidence[0].command=[], m=>m.evidence[0].exit_code=1, m=>m.evidence[0].artifacts=[], m=>m.evidence[0].artifacts[0].path='../escape', m=>m.evidence[0].artifacts[0].sha256='0'.repeat(64)]) {
    const bad=structuredClone(m);mutate(bad);assert.throws(()=>check(gate,c,bad));
   }
   writeFileSync(join(c.root,'proof.txt'),'controlled regression');assert.throws(()=>check(gate,c,m));
   assert.throws(()=>check(gate,{...c,binding:{...binding,dirty:['src/Example.cs']}},m));
  } finally{rmSync(c.root,{recursive:true,force:true});}
 });
}
test('D requires relevant visual/restart/failure evidence, never just a unit receipt',()=>{
 const c=fixture();try{const m=manifest(c,'D');m.coverage[0].evidence=['interaction'];assert.throws(()=>check('D',c,m));}finally{rmSync(c.root,{recursive:true,force:true});}
});
test('E and F reject empty lifecycle/review, even with a green-looking C fixture',()=>{
 const c=fixture();try{for(const g of ['E','F'])assert.throws(()=>check(g,c,manifest(c,'C')));}finally{rmSync(c.root,{recursive:true,force:true});}
});
test('requirement coverage is reconciled to the ledger, not summary counts',()=>{
 const c=fixture();try{assert.throws(()=>validateRequirementReport({run:{binding},requirements:{}},c));}finally{rmSync(c.root,{recursive:true,force:true});}
});
test('method dimensions preserve interaction/visual/restart/cancellation obligations',()=>{
 assert.deepEqual(dimensionsFor(records[0]),['interaction','visual','restart','failure']);
 assert.ok(dimensionsFor({...records[0], verification_method:'unit test',persistence:'value is written to disk'}).includes('restart'));
});
test('E validates each lifecycle scenario against the same package hash and Windows 11 x64',()=>{
 const c=fixture();try{
  const names=['clean-install','launch','native-load','close-child-cleanup','second-launch','upgrade','same-version','downgrade-refusal','uninstall-data-retention'];
  const m=manifest(c,'C');const pe=Buffer.alloc(256);pe.write('MZ');pe.writeUInt32LE(128,0x3c);pe.write('PE\0\0',128);pe.writeUInt16LE(0x8664,132);writeFileSync(join(c.root,'fixture.exe'),pe);const artifact={path:'fixture.exe',sha256:createHash('sha256').update(pe).digest('hex')};
  const inspection={package_sha256:artifact.sha256,version:'1.7.0',runtime:'win-x64',runtime_version:'10.0.10',configuration:'Release',prerequisites:['vcredist143-x64'],product:'Marrow',app_id:'MarrowCode',data_identity:'Marrow Code',resources:['cpu','vulkan','vad','test-clip','LICENSE','THIRD_PARTY_NOTICES.md','TRADEMARK.md'].map(name=>({name,sha256:artifact.sha256}))};
  const bytes=JSON.stringify(inspection);writeFileSync(join(c.root,'inspection.json'),bytes);const inspected={path:'inspection.json',sha256:createHash('sha256').update(bytes).digest('hex')};m.evidence.push({...evidence(c,'package-build'),package_sha256:artifact.sha256,artifacts:[artifact,inspected]});
  m.package={configuration:'Release',runtime:'win-x64',version:'1.7.0',runtime_version:'10.0.10',artifact,inspection:inspected,build_evidence:['package-build']};
  for(const name of names){m.evidence.push({...evidence(c,'package-lifecycle'),id:name,scenario:name,package_sha256:artifact.sha256,scenario_result:{name,checks:Object.entries(SCENARIO_CHECKS[name]).map(([name,expected])=>({name,expected,actual:expected,status:"PASS"}))}});m.lifecycle[name]=[name];}
  assert.doesNotThrow(()=>check('E',c,m));
  for(const mutate of [m=>delete m.lifecycle.upgrade,m=>m.evidence.at(-1).package_sha256='0'.repeat(64),m=>m.evidence.at(-1).environment.platform='linux',m=>m.package.runtime='linux-x64']){const bad=structuredClone(m);mutate(bad);assert.throws(()=>check('E',c,bad));}
 }finally{rmSync(c.root,{recursive:true,force:true});}
});
test('F requires a different reviewer, complete IDs/deviations/ODs and resolved findings',()=>{
 const c=fixture();c.ownerDecisions=['OD-0001'];try{
  const m=manifest(c,'C');m.evidence.push({...evidence(c,'independent-review'),producer:'reviewer'});
  m.independent_review={reviewer:'reviewer',author:'author',findings:[],evidence:['independent-review'],reviewed_ids:records.map(r=>r.id),deviation_ids:[],owner_decisions:['OD-0001']};
  assert.doesNotThrow(()=>check('F',c,m));
  for(const mutate of [m=>m.independent_review.reviewer='author',m=>m.independent_review.reviewed_ids=[],m=>m.independent_review.owner_decisions=[],m=>m.independent_review.deviation_ids=['PR-UNKNOWN'],m=>m.independent_review.findings=[{status:'OPEN'}]]){const bad=structuredClone(m);mutate(bad);assert.throws(()=>check('F',c,bad));}
 }finally{rmSync(c.root,{recursive:true,force:true});}
});
test('requirement report accepts complete raw receipts and rejects a missing project, changed outcome or invented status', async()=>{
 const {testArguments,discoveryArguments,TEST_PROJECTS,deriveRequirements}=await import('../requirement-evidence.mjs');
 const c=fixture();try{
  mkdirSync(join(c.root,'porting'));writeFileSync(join(c.root,'porting/parity_matrix.json'),JSON.stringify({requirements:records}));writeFileSync(join(c.root,'porting/ui_contract.json'),JSON.stringify({records:[]}));writeFileSync(join(c.root,'porting/unknowns.md'),'No unknowns in synthetic fixture.');mkdirSync(join(c.root,'tests'));
  const run={binding,complete:true,exit_code:0,dir:'receipts',commands:[],artifacts:[],started_at:'2026-10-07T00:00:00Z',finished_at:'2026-10-07T00:00:01Z',environment:{os:'synthetic',platform:'linux',architecture:'x64',tools:{dotnet:'10.0.401',node:'22.16.0'}}};
  for(const [i,project] of TEST_PROJECTS.entries()) {
   const assembly=project.split('/').at(-1).replace('.csproj','.dll'),ns=assembly.replace('.dll',''),name=ns+'.Proof.Passes',dir=`receipts/project-${i}`;mkdirSync(join(c.root,dir),{recursive:true});
   writeFileSync(join(c.root,`tests/${i}.cs`),`namespace ${ns};\npublic class Proof\n{\n[Fact]\n${i===0?'[Trait("Req", "PR-TEST-001")]\n':''}public void Passes() {}\n}\n`);
   const xml=`<TestRun><Results><UnitTestResult testName="${name}" outcome="Passed"></UnitTestResult></Results><TestDefinitions><TestMethod codeBase="/${assembly}" /></TestDefinitions><Counters total="1" passed="1" /></TestRun>`;
   for(const [file,bytes] of [['results.trx',xml],['command.txt','synthetic run'],['discovery.txt',`The following Tests are available:\n    ${name}\n`]]) {const path=`${dir}/${file}`;writeFileSync(join(c.root,path),bytes);run.artifacts.push({path,sha256:createHash('sha256').update(bytes).digest('hex')});}
   run.commands.push({project,filter:null,exit_code:0,discovery_exit_code:0,command:['dotnet',...testArguments(project,dir)],discovery_command:['dotnet',...discoveryArguments(project)],log:`${dir}/command.txt`,trx:`${dir}/results.trx`,discovery:`${dir}/discovery.txt`});
  }
  const doc={schema:'marrow/requirement-verification/2',run,...deriveRequirements(c.root,records,run,true)};
  assert.doesNotThrow(()=>validateRequirementReport(doc,c));
  for(const mutate of [d=>d.run.commands.pop(),d=>d.run.commands[0].command.push('--filter','fake'),d=>d.run.commands[1].trx=d.run.commands[0].trx,d=>d.requirements['PR-TEST-001'].status='NO_TEST',d=>d.total=0,d=>d.run.artifacts.pop(),d=>d.run.binding.tests_tree='0'.repeat(40)]){const bad=structuredClone(doc);mutate(bad);assert.throws(()=>validateRequirementReport(bad,c));}
  writeFileSync(join(c.root,run.commands[0].trx),'controlled regression');assert.throws(()=>validateRequirementReport(doc,c));
 }finally{rmSync(c.root,{recursive:true,force:true});}
});
test('actual git binding detects unknown/ED/legal/fixture/test edits and an exact-revision change',async()=>{
 const {execFileSync}=await import('node:child_process');const {captureBinding,validateBinding}=await import('../evidence-binding.mjs');
 const root=mkdtempSync(join(tmpdir(),'marrow-binding-test-'));try{
  for(const dir of ['src','tests','porting','porting/first-release/design-src'])mkdirSync(join(root,dir),{recursive:true});
  writeFileSync(join(root,'porting/baseline.json'),JSON.stringify({current:{commit_sha:binding.baseline,tree_sha:binding.baseline_tree}}));
  const paths=['src/Example.cs','tests/Example.cs','porting/unknowns.md','porting/engineering_decisions.md','THIRD_PARTY_NOTICES.md','porting/first-release/design-src/reference.html'];for(const path of paths)writeFileSync(join(root,path),'original bytes');
  const git=(...args)=>execFileSync('git',args,{cwd:root,stdio:'pipe'});git('init');git('add','.');git('-c','user.name=Synthetic fixture','-c','user.email=fixture@example.invalid','commit','-m','fixture');
  const original=captureBinding(root);assert.doesNotThrow(()=>validateBinding(original,captureBinding(root)));
  for(const path of paths){writeFileSync(join(root,path),'controlled regression');assert.throws(()=>validateBinding(original,captureBinding(root)));writeFileSync(join(root,path),'original bytes');}
  assert.doesNotThrow(()=>validateBinding(original,captureBinding(root)));
  git('-c','user.name=Synthetic fixture','-c','user.email=fixture@example.invalid','commit','--allow-empty','-m','new revision');assert.throws(()=>validateBinding(original,captureBinding(root)));
 }finally{rmSync(root,{recursive:true,force:true});}
});
test('visual rejects real failed/over-limit measurement receipts and non-image capture files',()=>{
 const c=fixture();try{
  const m=manifest(c,'D'),e=m.evidence.find(e=>e.kind==='visual'),a=e.assertions[0];
  for(const mutate of [d=>d.status='FAIL',d=>d.metrics.error=1,d=>d.windows_sha256='0'.repeat(64),d=>d.conditions.theme='wrong']) {
   const bytes=JSON.stringify({id:records[0].id,status:'PASS',reference_sha256:c.visual.reference.sha256,windows_sha256:c.visual.windows.sha256,policy_sha256:c.visual.policy.sha256,metrics:{error:0},conditions:{logical_width:1,logical_height:1,scale_percent:100,theme:'fixture',state:'fixture'}});const d=JSON.parse(bytes);mutate(d);const changed=JSON.stringify(d);writeFileSync(join(c.root,'measurements.json'),changed);a.visual.measurements.sha256=createHash('sha256').update(changed).digest('hex');assert.throws(()=>check('D',c,m));
  }
  a.visual.windows={...e.artifacts[0]};assert.throws(()=>check('D',c,m));
 }finally{rmSync(c.root,{recursive:true,force:true});}
});
test('semantic receipts cannot credit a different requirement or changed source clauses',()=>{
 const c=fixture();try{for(const gate of ['C','D']){const m=manifest(c,gate);m.evidence[0].assertions[0].id='PR-UNKNOWN-001';assert.throws(()=>check(gate,c,m));}}finally{rmSync(c.root,{recursive:true,force:true});}
});
