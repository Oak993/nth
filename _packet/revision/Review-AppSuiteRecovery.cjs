const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const base='_packet/revision/evidence/';
const read=file=>JSON.parse(fs.readFileSync(file,'utf8'));
const decode=s=>s.replace(/&quot;/g,'"').replace(/&apos;/g,"'").replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&amp;/g,'&');
const attr=s=>Object.fromEntries([...s.matchAll(/(\w+)="([^"]*)"/g)].map(m=>[m[1],decode(m[2])]));
const walk=dir=>fs.readdirSync(dir,{withFileTypes:true}).flatMap(entry=>entry.isDirectory()?walk(path.join(dir,entry.name)):[path.join(dir,entry.name).replace(/\\/g,'/')]);
const diagnostic=[];
for(const name of ['app-hang-diagnostic-2026-09-29T16-52-10-244Z-e1b50c','app-hang-diagnostic-2026-09-29T17-04-44-568Z-2b7f1f']) {
 const dir=base+name,files=walk(dir),sequence=files.find(p=>/\/Sequence_[^/]+\.xml$/.test(p));
 if(!sequence)throw Error('No recorded sequence for '+name);
 const xml=fs.readFileSync(sequence,'utf8');
 const pending=[...xml.matchAll(/<Test\b([^>]*)/g)].map(m=>attr(m[1])).filter(t=>t.Completed==='False');
 const trx=fs.readFileSync(dir+'/diagnostic.trx','utf8');
 const tests=[...trx.matchAll(/<UnitTestResult\b([^>]*)/g)].map(m=>attr(m[1]));
 diagnostic.push({directory:dir,result:dir+'/diagnostic-result.json',sequence,pending,counters:attr(trx.match(/<Counters\b([^>]*)/)[1]),
  failed_tests:tests.filter(t=>t.outcome==='Failed').map(t=>t.testName),classification:'TIMED_OUT_DIAGNOSTIC_NOT_AN_ORDINARY_PASS'});
}
const selected=[
 ['Ownership','pending-test-alone-0-1-2026-09-29T16-58-56-373Z-5aebef','pending-test-alone-0-2-2026-09-29T16-58-59-221Z-ff3770'],
 ['Guard','pending-test-alone-1-1-2026-09-29T16-59-02-229Z-5aa2ba','pending-test-alone-1-2-2026-09-29T16-59-04-587Z-142d00'],
 ['Raw project note','pending-test-alone-2-1-2026-09-29T16-59-06-964Z-6e62a9','pending-test-alone-2-2-2026-09-29T16-59-08-764Z-cc5e87'],
 ['Not-ancestor data case','pending-test-alone-3-fixed-1-2026-09-29T16-59-24-764Z-462db7','pending-test-alone-3-fixed-2-2026-09-29T16-59-29-097Z-732632'],
];
const pendingRechecks=selected.map(([label,...dirs])=>({label,runs:dirs.map(dir=>{
 const file=base+dir+'/tests.json',r=read(file);if(r.total!==1||r.passed!==1||r.exit!==0)throw Error('Pending test did not pass exactly alone');
 return {evidence:file,test:r.tests[0].name,total:r.total,passed:r.passed,exit:r.exit};
}),classification:'PENDING_IN_PARALLEL_DIAGNOSTIC_BUT_PASSED_TWICE_ALONE_NOT_AN_INDIVIDUAL_FAILURE'}));
const rehearsal={failed_twice:[],passed_twice:[]};
for(const [key,names,expectedFailed] of [
 ['failed_twice',['third-diagnostic-failure-alone-1-2026-09-29T17-09-14-853Z-817db9','third-diagnostic-failure-alone-2-2026-09-29T17-09-48-343Z-1d784a'],true],
 ['passed_twice',['loaded-rehearsal-green-1-2026-09-29T17-11-34-329Z-7b6911','loaded-rehearsal-green-2-2026-09-29T17-11-36-191Z-c1ca94'],false],
])for(const dir of names) {
 const file=base+dir+'/tests.json',r=read(file);
 if(r.total!==1||(expectedFailed?r.failed!==1||r.exit===0:r.passed!==1||r.exit!==0))throw Error('Invalid rehearsal receipt');
 rehearsal[key].push({evidence:file,test:r.tests[0].name,error:r.tests[0].error,total:r.total,passed:r.passed,failed:r.failed});
}
const mixed=base+'sequential-fixture-mixed-ordinary-2026-09-29T17-11-37-924Z-358366/tests.json';
const gate=read(mixed);if(gate.exit!==0||gate.passed!==gate.total||!gate.total)throw Error('Mixed fixture validation did not pass');
const probe='_packet/staging/final-hang-review/TemporaryAppSuiteStackProbe.cs.txt';
if(fs.existsSync('tests/Marrow.App.Tests/Fixtures/TemporaryAppSuiteStackProbe.cs'))throw Error('Temporary probe is still compiled');
const sample=base+'app-hang-diagnostic-2026-09-29T17-04-44-568Z-2b7f1f/self-stack-132524.txt';
const result={reviewed_at:new Date().toISOString(),diagnostics:diagnostic,pending_rechecks:pendingRechecks,
 excluded_zero_selection:base+'pending-test-alone-3-1-2026-09-29T16-59-10-799Z-58832a/tests.json',rehearsal,
 mixed_gate:{evidence:mixed,total:gate.total,passed:gate.passed},
 temporary_self_probe:{retained_draft:probe,sha256:crypto.createHash('sha256').update(fs.readFileSync(probe)).digest('hex').toUpperCase(),removed_from_compiled_sources:true,
 sample,scope:'Self-only managed/native-wait stacks from the existing sampler, no dump or external process. The sample preceded the later stall; it does not prove a particular deadlock.'},
 fixture_policy:{file:'tests/Marrow.App.Tests/AssemblyParallelism.cs',scope:'Sequential xUnit collections only. All explicit concurrent tasks, merge waiters, child-process proofs, native input, assertions and timeouts remain. Not a claim that a production deadlock was identified or repaired.'},
 history_setup_repair:{file:'tests/Marrow.App.Tests/Runtime/ThreadRuntimePart9Tests.cs',
 source:'ThreadRuntime constructor starts LoadDocument at 703-716; FinishLoad/Install at 854-869 and 721-757 can normalize a newly rehearsed Running turn if setup remains incomplete.',
 change:'Await the existing production EnsureLoaded through the manual-loop fixture before RehearseTurn. All original Running/diff/path/user-row assertions remain.'}};
fs.writeFileSync('_packet/revision/app-suite-recovery.json',JSON.stringify(result,null,2)+'\n');
console.log(JSON.stringify({pendingCases:pendingRechecks.length,soloPasses:pendingRechecks.length*2,rehearsalRed:2,rehearsalGreen:2,mixed:gate.total},null,2));
