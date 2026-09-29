const fs=require('node:fs');
const path=require('node:path');
const crypto=require('node:crypto');
const cp=require('node:child_process');
const root='E:\\Droppy\\wt\\oc\\chatgpt-1';
if(process.cwd().toLowerCase()!==root.toLowerCase())throw Error('Wrong worktree');
const record=process.argv[2];
if(!/^(PR|UI)-[A-Z]+-\d{3}$/.test(record||''))throw Error('A record ID is required');
const base=path.join(root,'_packet/revision/evidence');
const originalIds=new Set(JSON.parse(fs.readFileSync('_packet/revision/original-rechecks.json','utf8')).map(r=>r.id));
const successful=new Map(),excluded=[],gates=[];
for(const directory of fs.readdirSync(base,{withFileTypes:true}).filter(d=>d.isDirectory()).sort((a,b)=>a.name.localeCompare(b.name))) {
  const results=path.join(base,directory.name,'results.json');
  if(fs.existsSync(results)) {
    for(const row of JSON.parse(fs.readFileSync(results,'utf8'))) {
      if(!row.records.includes(record))continue;
      if(!row.runs.filter(r=>r.tests).every(r=>r.command.includes('&Req='+record)))continue;
      const evidence=path.relative(root,results).replace(/\\/g,'/');
      if(row.status!=='KILLED_TWICE_ALONE_AND_ORDINARY_GREEN'||!row.restored||!row.git_diff_unchanged) {
        excluded.push({id:row.id,status:row.status,error:row.error,evidence});continue;
      }
      const reds=row.runs.filter(r=>/-red-[12]$/.test(r.name));
      const green=row.runs.find(r=>/-green$/.test(r.name));
      const builds=row.runs.filter(r=>/-build$/.test(r.name));
      if(reds.length!==2||reds.some(r=>r.total!==1||r.failed!==1||r.exit===0)||!green||green.total!==1||green.passed!==1||green.exit!==0||builds.length!==2||builds.some(b=>!b.clean)||row.runs.some(r=>r.environment?.outer_opt_in_variables?.length!==0))throw Error('Invalid asserted evidence for '+row.id);
      successful.set(row.id,{...row,kind:originalIds.has(row.id)?'verifier-recheck':'additional',evidence});
    }
  }
  const testPath=path.join(base,directory.name,'tests.json');
  if(fs.existsSync(testPath)) {
    const gate=JSON.parse(fs.readFileSync(testPath,'utf8'));
    if(gate.command.includes("'Req="+record+"|FullyQualifiedName~TraceabilityTests'")&&gate.exit===0&&gate.total>0&&gate.total===gate.passed&&gate.environment.outer_opt_in_variables.length===0)
      gates.push({...gate,evidence:path.relative(root,testPath).replace(/\\/g,'/')});
  }
}
const own=[...successful.values()].filter(r=>r.kind==='additional');
if(own.length<8)throw Error('Only '+own.length+' additional ordinary mutation kills for '+record);
gates.sort((a,b)=>a.start.localeCompare(b.start));
if(!gates.length)throw Error('No passing ordinary record plus Traceability gate');
const source=fs.readFileSync('src/Marrow.App/Window/WindowManager.cs');
if(source.includes(Buffer.from('\r\n')))throw Error('Production newline churn');
const proof={record,status:'MUTATION_BATCH_COMPLETE',candidate:false,candidate_note:'Final clause-by-clause and full-suite acceptance is reported separately; a mutation count alone is not certification.',head_before_commit:cp.execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),window_manager_sha256:crypto.createHash('sha256').update(source).digest('hex').toUpperCase(),additional_count:own.length,verifier_recheck_count:successful.size-own.length,ordinary_record_gate:gates.at(-1),mutations:[...successful.values()],excluded_attempts:excluded};
fs.mkdirSync('_packet/revision/records',{recursive:true});
fs.writeFileSync('_packet/revision/records/'+record+'.json',JSON.stringify(proof,null,2)+'\n');
const dirs=[...new Set(proof.mutations.map(m=>path.dirname(m.evidence)).concat(path.dirname(proof.ordinary_record_gate.evidence)))];
fs.writeFileSync('_packet/revision/records/'+record+'.evidence-paths.txt',dirs.join('\n')+'\n');
console.log(JSON.stringify({record,additional:own.length,original:proof.verifier_recheck_count,gate:proof.ordinary_record_gate.total,excluded:excluded.length,evidence:dirs},null,2));
