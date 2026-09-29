// Archive and commit the additional clause-owned proofs, without editing source or tests.
const fs=require('node:fs'),cp=require('node:child_process'),crypto=require('node:crypto');
const root='E:\\Droppy\\wt\\oc\\chatgpt-1';
if(process.cwd().toLowerCase()!==root.toLowerCase())throw Error('Wrong worktree');
const git=(...args)=>cp.execFileSync('git',args,{cwd:root,encoding:'utf8',windowsHide:true,maxBuffer:32*1024*1024});
if(git('branch','--show-current').trim()!=='opencode/chatgpt-1')throw Error('Wrong branch');
if(git('diff','--cached','--name-only').trim()||git('diff','HEAD','--name-only','--','src').trim())throw Error('Staged or production changes present');
const expected='8454D29637E5560CA84CD3C6A7BEED13C0A2050A1AC4EC6AF1403949B3AE42AE';
if(crypto.createHash('sha256').update(fs.readFileSync('src/Marrow.App/Window/WindowManager.cs')).digest('hex').toUpperCase()!==expected)throw Error('Unexpected WindowManager bytes');
const records={'PR-WIN-003':['OWN-WIN003-13','OWN-WIN003-14','OWN-WIN003-15'],'PR-WIN-006':['OWN-WIN006-14','OWN-WIN006-15','OWN-WIN006-16']};
const commits=[];
for(const [record,ids] of Object.entries(records)) {
 cp.execFileSync(process.execPath,['./_packet/revision/Record-MutationEvidence.cjs',record],{cwd:root,stdio:'inherit',windowsHide:true});
 const file='_packet/revision/records/'+record+'.json';
 const proof=JSON.parse(fs.readFileSync(file,'utf8'));
 for(const id of ids) {
  const row=proof.mutations.find(m=>m.id===id);
  if(!row||row.runs.find(r=>/-green$/.test(r.name)).end<'2026-09-29T16:12:00Z')throw Error('No current accepted proof for '+id);
  if(proof.ordinary_record_gate.start<row.runs.find(r=>/-green$/.test(r.name)).end)throw Error('Gate predates restored proof');
 }
 cp.execFileSync(process.execPath,['./_packet/revision/Archive-RecordEvidence.cjs',record],{cwd:root,stdio:'inherit',windowsHide:true});
 const files=[file,'_packet/revision/records/'+record+'.archives.json','_packet/revision/records/'+record+'.evidence-paths.txt'];
 if(record==='PR-WIN-003')files.push('_packet/revision/Make-CandidateClauseProofs.cjs','_packet/revision/candidate-clause-mutations.json','_packet/revision/Finalize-CandidateEvidence.cjs');
 const archive=JSON.parse(fs.readFileSync('_packet/revision/records/'+record+'.archives.json','utf8'));
 for(const entry of archive.archives)files.push(entry.archive,entry.manifest);
 if(git('diff','--cached','--name-only').trim())throw Error('Unexpected staged changes');
 git('add','--',...files);git('diff','--cached','--check');
 git('commit','-m','test: complete '+record+' clause-owned candidate proofs','-m',
  'Existing unchanged Facts kill each additional clause control twice alone under this exact Req filter, with clean mutant/restored builds, exact restoration and ordinary greens. '+
  'Shared source substitutions are separate record-owned trials, not globally unique defects. Final full-suite acceptance remains separate.',
  '-m','Co-Authored-By: ChatGPT <noreply@openai.com>');
 commits.push({record,commit:git('rev-parse','HEAD').trim(),ids,gate:proof.ordinary_record_gate.evidence});
 console.log('COMMITTED '+record+' '+commits.at(-1).commit);
}
fs.writeFileSync('_packet/revision/candidate-proof-commits.json',JSON.stringify({commits},null,2)+'\n');
