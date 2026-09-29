// Commit validated record-owned supplements without replacing the live working source.
// Shared NativeTests additions are staged as explicit Git index blobs, one record at a time.
const fs=require('node:fs'),cp=require('node:child_process'),crypto=require('node:crypto');
const root='E:\\Droppy\\wt\\oc\\chatgpt-1';
if(process.cwd().toLowerCase()!==root.toLowerCase())throw Error('Wrong worktree');
const git=(...args)=>cp.execFileSync('git',args,{cwd:root,encoding:'utf8',windowsHide:true,maxBuffer:32*1024*1024});
if(git('branch','--show-current').trim()!=='opencode/chatgpt-1')throw Error('Wrong branch');
if(git('diff','--cached','--name-only').trim())throw Error('Unexpected staged changes');
const hash=b=>crypto.createHash('sha256').update(b).digest('hex').toUpperCase();
const expected='8454D29637E5560CA84CD3C6A7BEED13C0A2050A1AC4EC6AF1403949B3AE42AE';
if(hash(fs.readFileSync('src/Marrow.App/Window/WindowManager.cs'))!==expected||git('diff','HEAD','--name-only','--','src').trim())throw Error('Production is not restored');
const native='tests/Marrow.App.Tests/Window/WindowManagerNativeTests.cs';
const full=fs.readFileSync(native,'utf8');
const original=git('show','fd6f10340:'+native);
const methods={
 'PR-WIN-007':'Actual_maximize_caption_click_then_a_second_click_restores_the_main_window',
 'PR-WIN-017':'A_second_real_resize_after_hide_and_reopen_reuses_the_same_registered_window_observer',
 'PR-WIN-008':'The_factory_realizes_its_owned_native_handle_before_the_first_Show',
 'PR-WIN-009':'The_main_factory_publishes_the_injected_Unicode_display_name_to_its_own_HWND',
};
const blocks={};
for(const [record,method] of Object.entries(methods)) {
 const marker='    public void '+method+'()';
 if(full.split(marker).length!==2||original.includes(marker))throw Error('Unexpected shared-source method: '+record);
 const at=full.indexOf(marker),start=full.lastIndexOf('    [Fact]\n',at),end=full.indexOf('    [Fact]\n',at);
 if(start<0||end<0)throw Error('Cannot isolate method '+method);
 blocks[record]=full.slice(start,end);
 if(!blocks[record].includes('[Trait("Req", "'+record+'")'))throw Error('Missing record tag');
}
let restoredBase=full;
for(const block of Object.values(blocks))restoredBase=restoredBase.replace(block,'');
restoredBase=restoredBase.replace('using Marrow.App.Window;\n','').replace('using Marrow.Core.Support;\n','')
 .replace('// STOP SETTINGS-NATIVE-SURFACE: painted fixture caption zones are not SettingsView\'s\n// production WindowChrome overlay, sidebar dragging, corner geometry or backdrop pixels.\n// STOP FACTORY-STRETCH: WindowManager.MakeWindow has no ResizeStretch.Attach/Apply binding;\n// real caption/resize-hook messages here do not verify the separate ED-101 composition remedy.\n','');
if(restoredBase!==original)throw Error('Shared file contains an unrelated edit');
const common=[
 '_packet/revision/ordinary-gate.cjs','_packet/revision/Record-MutationEvidence.cjs',
 '_packet/revision/Archive-RecordEvidence.cjs','_packet/revision/MutationFailureClassifier.cjs',
 '_packet/revision/Review-MutationFailures.cjs','_packet/revision/Make-FinalGapMutations.cjs',
 '_packet/revision/final-gap-mutations.json','_packet/revision/Make-LifecycleSupplement.cjs',
 '_packet/revision/lifecycle-mutations.json','_packet/revision/Finalize-Supplements.cjs',
];
const required={
 'PR-WIN-007':['REV-PR-WIN-007-03','REV-PR-WIN-007-04','OWN-WIN007-09'],
 'PR-WIN-017':['LIFETIME-WIN017-09'],
 'PR-WIN-008':['OWN-WIN008-13'],
 'PR-WIN-009':['OWN-WIN009-09','OWN-WIN009-10','OWN-WIN009-11'],
 'PR-WIN-006':['OWN-WIN006-13'],
 'PR-THEME-039':['OWN-THEME039-03','OWN-THEME039-09'],
};
const extra={
 'PR-WIN-017':['_packet/revision/retrospective-fixture-exclusions.json'],
 'PR-WIN-009':['tests/Marrow.App.Tests/Window/WindowManagerTitleMutationTests.cs','tests/Marrow.App.Tests/Fixtures/WindowManagerNativeTitle.cs'],
 'PR-THEME-039':['tests/Marrow.App.Tests/Window/WindowManagerThemeMutationTests.cs'],
};
const acceptedNative=new Set();
const logFile='_packet/revision/supplement-commits.json';
if(fs.existsSync(logFile))throw Error('Preserve existing supplement commit log; do not replay this script');
const commits=[];
for(const record of Object.keys(required)) {
 if(git('diff','--cached','--name-only').trim())throw Error('Unexpected staged files before '+record);
 cp.execFileSync(process.execPath,['./_packet/revision/Record-MutationEvidence.cjs',record],{cwd:root,stdio:'inherit',windowsHide:true});
 const recordFile='_packet/revision/records/'+record+'.json';
 const proof=JSON.parse(fs.readFileSync(recordFile,'utf8'));
 for(const id of required[record]) {
  const row=proof.mutations.find(m=>m.id===id);
  if(!row)throw Error('No accepted supplemental evidence for '+id);
  if(row.runs.find(r=>/-green$/.test(r.name)).end<'2026-09-29T15:46:00Z')throw Error('Stale sensitivity evidence for '+id);
 }
 cp.execFileSync(process.execPath,['./_packet/revision/Archive-RecordEvidence.cjs',record],{cwd:root,stdio:'inherit',windowsHide:true});
 const files=[recordFile,'_packet/revision/records/'+record+'.evidence-paths.txt','_packet/revision/records/'+record+'.archives.json',...(extra[record]||[])];
 if(record==='PR-WIN-007')files.push(...common);
 const archives=JSON.parse(fs.readFileSync('_packet/revision/records/'+record+'.archives.json','utf8')).archives;
 for(const entry of archives)files.push(entry.archive,entry.manifest);
 for(const file of files) {
  if(!fs.existsSync(file))throw Error('Missing explicit commit file '+file);
  if(/\.(cs|cjs|json|txt)$/.test(file)&&fs.readFileSync(file).includes(Buffer.from('\r\n')))throw Error('Unexpected source/index CRLF '+file);
 }
 git('add','--',...files);
 if(methods[record]) {
  acceptedNative.add(record);
  let stage=full;
  for(const [owner,block] of Object.entries(blocks))if(!acceptedNative.has(owner))stage=stage.replace(block,'');
  if(!acceptedNative.has('PR-WIN-009'))stage=stage.replace('using Marrow.Core.Support;\n','');
  if(!acceptedNative.has('PR-WIN-008')&&!acceptedNative.has('PR-WIN-009'))stage=stage.replace('using Marrow.App.Window;\n','');
  const blob=cp.execFileSync('git',['hash-object','-w','--stdin'],{cwd:root,encoding:'utf8',input:stage,windowsHide:true}).trim();
  git('update-index','--cacheinfo','100644,'+blob+','+native);
  files.push(native);
 }
 const staged=git('diff','--cached','--name-only').trim().split(/\r?\n/).filter(Boolean);
 if(staged.some(file=>!files.includes(file)))throw Error('Unexpected staged path');
 if(!staged.length)throw Error('No supplemental change for '+record);
 git('diff','--cached','--check');
 console.log(git('diff','--cached','--stat'));
 git('commit','-m','test: complete '+record+' supplemental mutation evidence','-m',
  'Validated exact Req-tagged mutant build, red twice alone, restored clean build and green. '+
  'Scope and unresolved clauses remain separate from record acceptance; no production delta added. '+
  (record==='PR-WIN-017'?'The earlier pointer-cleanup failure is explicitly excluded and retained; both accepted reds now assert missing activation on the second real resize.':''),
  '-m','Co-Authored-By: ChatGPT <noreply@openai.com>');
 commits.push({record,commit:git('rev-parse','HEAD').trim(),mutations:required[record],ordinary_gate:proof.ordinary_record_gate.evidence});
 fs.writeFileSync(logFile,JSON.stringify({commits},null,2)+'\n');
 console.log('COMMITTED '+record+' '+commits.at(-1).commit);
 if(fs.readFileSync(native,'utf8')!==full)throw Error('The working test file was changed by index staging');
}
if(git('show','HEAD:'+native)!==full)throw Error('Native test index does not match the validated full source after all record commits');
console.log('SUPPLEMENT_RECORD_COMMITS_COMPLETE');
