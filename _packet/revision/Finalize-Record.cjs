// Commit only a completed ordinary-environment mutation batch and its explicit source files.
// Full-clause certification is a separate final-report decision.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const root='E:\\Droppy\\wt\\oc\\chatgpt-1';
if(process.cwd().toLowerCase()!==root.toLowerCase())throw Error('Wrong worktree');
const record=process.argv[2];
const fixtures='tests/Marrow.App.Tests/Fixtures/';
const tests='tests/Marrow.App.Tests/Window/';
const ownFiles={
  'PR-SETTINGS-002':['src/Marrow.App/Window/WindowManager.cs',tests+'WindowManagerNativeTests.cs',tests+'WindowManagerSettingsScreenTests.cs',tests+'WindowManagerChildEnvironmentTests.cs',...['WindowManagerNativeMonitorTopology','WindowManagerNativePlacementGeometry','WindowManagerNativePlacementExercise','WindowManagerNativeChildProtocol','WindowManagerProcessProtocol'].map(n=>fixtures+n+'.cs')],
  'PR-WIN-010':[tests+'WindowManagerSettingsSurfaceTests.cs',tests+'WindowManagerRestartTests.cs',fixtures+'WindowManagerProcessCollection.cs',...['WindowManagerRestartChild','WindowManagerRestartExpectations','WindowManagerRestartPhase','WindowManagerRestartProof','WindowManagerRestartProtocol','WindowManagerRestartScenario'].map(n=>fixtures+n+'.cs')],
  'PR-WIN-003':[tests+'WindowManagerProcessTests.cs',tests+'WindowManagerTerminationWaitTests.cs',fixtures+'WindowManagerProcessCollection.cs',fixtures+'WindowManagerProcessChild.cs',fixtures+'WindowManagerProcessProof.cs'],
  'PR-WIN-008':[], 'PR-WIN-017':[], 'PR-WIN-007':[], 'PR-WIN-011':[], 'UI-SETTINGS-001':[],
  'PR-WIN-006':[], 'PR-WIN-009':[tests+'WindowManagerTitleMutationTests.cs'], 'PR-THEME-039':[],
};
if(!Object.hasOwn(ownFiles,record))throw Error('No approved file list for this record');
const git=(...args)=>cp.execFileSync('git',args,{cwd:root,encoding:'utf8',windowsHide:true});
if(git('branch','--show-current').trim()!=='opencode/chatgpt-1')throw Error('Wrong branch');
if(git('diff','--cached','--name-only').trim())throw Error('Unexpected staged files');
for(const script of ['Record-MutationEvidence.cjs','Archive-RecordEvidence.cjs'])
  cp.execFileSync(process.execPath,['./_packet/revision/'+script,record],{cwd:root,stdio:'inherit'});
const proof=JSON.parse(fs.readFileSync('_packet/revision/records/'+record+'.json','utf8'));
const archives=JSON.parse(fs.readFileSync('_packet/revision/records/'+record+'.archives.json','utf8'));
const scripts=['Finalize-Record.cjs','ordinary-gate.cjs','Record-MutationEvidence.cjs','Archive-RecordEvidence.cjs','Make-OwnManifests.cjs','Make-OriginalRechecks.cjs','Make-WaitMutations.cjs'];
const manifests=['own-settings-native-mutations.json','original-rechecks.json','own-process-mutations.json','own-wait-mutations.json','own-headless-mutations.json'];
const selected=[...ownFiles[record],...scripts.concat(manifests).map(p=>'_packet/revision/'+p).filter(p=>fs.existsSync(p)),...['.json','.archives.json','.evidence-paths.txt'].map(s=>'_packet/revision/records/'+record+s),...archives.archives.flatMap(a=>[a.archive,a.manifest])];
for(const p of selected) {
  if(!fs.existsSync(p))throw Error('Selected file does not exist: '+p);
  if(/\.(cs|cjs|json|txt)$/.test(p)&&fs.readFileSync(p).includes(Buffer.from('\r\n')))throw Error('Selected source/manifest is not LF: '+p);
}
git('add','--',...selected);
git('diff','--cached','--check');
const summary=`Additional mutations: ${proof.additional_count}; verifier rechecks: ${proof.verifier_recheck_count}. Every accepted row has two isolated requirement-tagged failures, exact restoration and an ordinary green. Record and Traceability gate: ${proof.ordinary_record_gate.passed}/${proof.ordinary_record_gate.total}. Full-clause certification remains separate.`;
git('commit','-q','-m','test: verify '+record+' in the ordinary mutation gate','-m',summary,'-m','Co-Authored-By: ChatGPT <noreply@openai.com>');
console.log(git('log','-1','--format=%h %s%n%b'));
