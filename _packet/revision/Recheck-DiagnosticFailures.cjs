// Reproduce each observed diagnostic failure twice in the ordinary environment, one data case at a time.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const root='E:\\Droppy\\wt\\oc\\chatgpt-1';
if(process.cwd().toLowerCase()!==root.toLowerCase())throw Error('Wrong worktree');
const directory='_packet/revision/evidence/app-hang-diagnostic-2026-09-29T16-30-22-969Z-cc3d49';
const xml=fs.readFileSync(directory+'/diagnostic.trx','utf8');
const decode=s=>s.replace(/&quot;/g,'"').replace(/&apos;/g,"'").replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&amp;/g,'&');
const attr=s=>Object.fromEntries([...s.matchAll(/(\w+)="([^"]*)"/g)].map(m=>[m[1],decode(m[2])]));
const outcomes=[...xml.matchAll(/<UnitTestResult\b([^>]*)/g)].map(m=>attr(m[1]));
const definitions=[...xml.matchAll(/<UnitTest\b([^>]*)>([\s\S]*?)<\/UnitTest>/g)].map(m=>({attrs:attr(m[1]),method:attr(m[2].match(/<TestMethod\b([^>]*)/)[1])}));
const file='_packet/revision/diagnostic-failure-rechecks.json';
if(fs.existsSync(file))throw Error('Preserve existing isolated rechecks');
const state={started:new Date().toISOString(),diagnostic:directory+'/diagnostic-result.json',diagnostic_trx:directory+'/diagnostic.trx',
 diagnostic_counters:attr(xml.match(/<Counters\b([^>]*)/)[1]),results:[],completed:false};
const save=()=>fs.writeFileSync(file,JSON.stringify(state,null,2)+'\n');
save();console.log(JSON.stringify(state.diagnostic_counters));
for(const test of outcomes.filter(r=>r.outcome==='Failed')) {
 const definition=definitions.find(d=>d.attrs.id===test.testId);
 if(!definition)throw Error('No exact test definition for '+test.testName);
 const fqn=definition.method.className+'.'+definition.method.name;
 if(!/^[A-Za-z_][\w.]+$/.test(fqn))throw Error('Unresolved method identity '+fqn);
 let filter='FullyQualifiedName='+fqn;
 const route=test.testName.match(/\(route: (Models|Mcp),/);
 if(route)filter+='&DisplayName~route: '+route[1];
 const result={test:test.testName,filter,runs:[]};
 for(let n=1;n<=2;n++) {
  const p=cp.spawnSync(process.execPath,['./_packet/revision/ordinary-gate.cjs','--mode','test','--filter',filter,'--name','diagnostic-alone-'+state.results.length+'-'+n],
    {cwd:root,encoding:'utf8',windowsHide:true,maxBuffer:32*1024*1024});
  const output=(p.stdout||'')+(p.stderr||'');process.stdout.write(output);
  const owned=output.match(/^EVIDENCE=(.+)$/m)?.[1].trim();
  if(!owned||!owned.startsWith(path.join(root,'_packet','revision','evidence')+path.sep))throw Error('Missing owned recheck result');
  const receiptFile=path.join(owned,'tests.json'),receipt=JSON.parse(fs.readFileSync(receiptFile,'utf8'));
  if(receipt.total!==1||receipt.tests[0].name!==test.testName)throw Error('Isolated filter did not select the exact one failed data case');
  result.runs.push({path:path.relative(root,receiptFile).replace(/\\/g,'/'),exit:receipt.exit,total:receipt.total,passed:receipt.passed,failed:receipt.failed,error:receipt.tests[0].error});
 }
 result.reproduced_twice_alone=result.runs.every(r=>r.failed===1&&r.exit!==0);
 result.classification=result.reproduced_twice_alone?'REPRODUCED_TWICE_ALONE':result.runs.every(r=>r.passed===1&&r.exit===0)?'NOT_REPRODUCED_IN_TWO_ORDINARY_ISOLATED_RUNS':'MIXED_ISOLATED_RESULTS';
 state.results.push(result);save();
}
state.ended=new Date().toISOString();state.completed=true;save();
console.log('DIAGNOSTIC_RECHECKS_COMPLETE '+state.results.length);
