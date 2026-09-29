const fs=require('node:fs'),cp=require('node:child_process'),crypto=require('node:crypto');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const base='_packet/revision/evidence/',sha=bytes=>crypto.createHash('sha256').update(bytes).digest('hex').toUpperCase();
const read=file=>JSON.parse(fs.readFileSync(file,'utf8'));
const fact='Marrow.App.Tests.AppModelHarnessLifetimeTests.Teardown_finishes_a_real_queued_write_before_retiring_its_manual_clock_and_scratch_directory';
const redFolders=['resume-writer-lifetime-red-1-2026-09-29T19-20-38-011Z-8964fd','resume-writer-lifetime-red-2-2026-09-29T19-20-39-734Z-837ee0'];
const greenFolders=['resume-writer-lifetime-green-1-2026-09-29T19-21-39-987Z-6bd6d7','resume-writer-lifetime-green-2-2026-09-29T19-21-41-662Z-8d88b4'];
const gates=['resume-writer-coordinator-gate-1-2026-09-29T19-21-43-269Z-7c067e','resume-writer-coordinator-gate-2-2026-09-29T19-21-58-660Z-8e024d'];
function ordinary(r){if(r.environment.outer_opt_in_variables.length||r.environment.api_key_variables_remaining.length)throw Error('Nonordinary environment');}
for(const folder of redFolders){const r=read(base+folder+'/tests.json');ordinary(r);if(r.exit!==1||r.total!==1||r.failed!==1||r.tests[0].name!==fact||!r.tests[0].error.includes('The harness retired its clock/directory before the actual queued write completed.'))throw Error('Wrong deterministic lifetime failure');}
for(const folder of greenFolders){const r=read(base+folder+'/tests.json');ordinary(r);if(r.exit!==0||r.total!==1||r.passed!==1||r.failed!==0||r.tests[0].name!==fact)throw Error('Missing restored lifetime green');}
for(const folder of gates){const r=read(base+folder+'/tests.json');ordinary(r);if(r.exit!==0||r.total!==14||r.passed!==14||r.failed!==0)throw Error('Coordinator ordinary gate failed');}
const build=base+'resume-writer-lifetime-fixed-build-2026-09-29T19-21-35-410Z-f2e71e/build.json';
if(!read(build).clean||read(build).exit!==0)throw Error('Fixed build is not clean');
const capture=base+'owned-live-stack-2026-09-29T19-14-55-450Z-215a72';
const request=read(capture+'/request.json'),supervisor=read(capture+'/supervisor-result.json');
if(request.target_pid!==45788||!request.coordinator_stopped||request.head!=='0f8b8b519e5d045d487b6971212b9e29ca429e1e'||supervisor.ordinary_pass!==false||supervisor.capture_exit!==1||!supervisor.within_bound)throw Error('Owned diagnostic identity differs');
for(let i=1;i<=2;i++){
 const r=read(capture+'/capture-'+i+'.json'),raw=fs.readFileSync(capture+'/capture-'+i+'.nettrace'),text=fs.readFileSync(capture+'/capture-'+i+'-stacks.txt');
 if(sha(raw)!==r.raw_sha256||sha(text)!==r.report_sha256||!r.parse.ended||r.parse.error!==null||r.parse.consumed!==raw.length||r.parse.unresolved_frames!==0||r.errors.length!==1||r.errors[0]!=='read EPIPE')throw Error('Preserve complete trace and its collector warning');
 for(const exact of ['AppModelHarness.WriteLibrary','HydraMergeProofFixture.Start','DiskWriter.WriteSynchronously','DiskWriter+<>c__DisplayClass11_0.<EncodeAndWrite>b__1','FileOps.Wait'])if(!text.toString().includes(exact))throw Error('Captured chain is incomplete');
}
const abort=read('_packet/revision/final-app-fourth-hang-abort.json');
if(abort.status!=='OWNED_ABORT_COMPLETE_NOT_A_SUITE_PASS'||!abort.cleanup_complete)throw Error('Owned process cleanup was not verified');
const files=['tests/Marrow.App.Tests/Fixtures/AppModelFixtures.cs','tests/Marrow.App.Tests/AppModelHarnessLifetimeTests.cs'];
for(const file of files)if(fs.readFileSync(file,'utf8').includes('\r'))throw Error('Preserve LF sources');
const folders=fs.readdirSync(base,{withFileTypes:true}).filter(d=>d.isDirectory()&&d.name.startsWith('resume-writer-')).map(d=>base+d.name);
folders.push(capture,base+'final-solution-build-2026-09-29T18-53-59-185Z-a794d1',base+'final-app-ordinary-1-2026-09-29T18-54-07-447Z-ceef41',base+'final-app-ordinary-2-2026-09-29T19-02-21-633Z-b8776f');
cp.execFileSync(process.execPath,['_packet/revision/Archive-SelectedEvidence.cjs',...folders],{stdio:'inherit',windowsHide:true});
const result={status:'HARNESS_LIFETIME_REGRESSION_REPAIRED_FINAL_ORDINARY_SUITES_PENDING',recorded_at:new Date().toISOString(),
 head_before_commit:cp.execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),
 diagnostic_directory:capture,diagnostic_scope:'Pinned owned existing App testhost45788 only. Coordinator stopped before diagnostic attachment. No C# probe, new package/project, application or provider was used.',
 captures:[1,2].map(i=>({receipt:capture+'/capture-'+i+'.json',raw:capture+'/capture-'+i+'.nettrace',stacks:capture+'/capture-'+i+'-stacks.txt'})),
 collector_limit:'Both bounded IPC captures returned read EPIPE and collector exit1 at closure. Both raw nettrace files nevertheless contain the complete end marker, consume every byte, have no parse error and resolve every sampled managed frame. The warning and exit status are preserved, not relabeled as a clean tool run or ordinary test pass.',
 observed:'Two independent snapshots show thread80148 waiting in SerialQueue.Sync via DiskWriter.WriteSynchronously, AppModelHarness.WriteLibrary and HydraMergeProofFixture.Start in the Settlement_only_comes_from_legacy_held_empty_work_or_every_share_already_on_main_without_strays Theory. Thread121944 is the earlier asynchronous DiskWriter.EncodeAndWrite callback blocked in FileOps.Wait. Current setup has not created its AppModel yet.',
 not_observed:'The Theory argument, earlier producer test, destination path, original Win32 exception and concrete TimeProvider object were not captured. Retained manual-clock ownership is a source-backed inference, not inspected local-variable evidence. Earlier unsampled stalls are not retroactively assigned this exact cause.',
 source_mechanism:['Storage.cs shared FIFO writer and asynchronous context','Executors.cs captures and restores the enqueueing ExecutionContext','FileOps.cs blocks on an ambient clock timer during transient I/O retry','AppModelHarness previously restored its scopes and deleted its directory without a writer fence'],
 regression:{fact,red_twice:redFolders.map(f=>base+f+'/tests.json'),green_twice:greenFolders.map(f=>base+f+'/tests.json'),
 oracle:'Hold a real non-delete-sharing handle to the owned file, observe the first actual manual retry timer, release the handle, dispose the fixture and require the asynchronous writer success size before deleted scratch and a subsequent harness library round trip. Pre-fix failures always rescue the original clock/fence in finally, so they cannot poison later tests.'},
 repair:'Only AppModelHarness teardown changes: a bounded background existing writer fence while the disposing thread advances only its own Runtime.Time; no actor pump or System-clock substitution. Fence before FileIndex cleanup and again in its finally. Keep original scopes/directories until completion. Original tests, timestamps, queue semantics and product code are unchanged.',
 bounds:'Each fixture drain has the existing120-second real-time bound. The regression has5-second setup/fence and10-second unconditional recovery bounds. These are not disabled assertions or enlarged existing test-body timeouts.',
 clean_build:build,ordinary_coordinator_gates:gates.map(f=>base+f+'/tests.json'),abort_receipt:'_packet/revision/final-app-fourth-hang-abort.json',
 source_hashes:files.map(file=>({name:file,sha256:sha(fs.readFileSync(file))})),
 fixture_original_sha256:'4C4EADC6AF0067CEDA790236EB5610467A8992B8346B5B0F6BE0E13DAEF0D68E',
 worker_review:'_packet/staging/resume-live-hang/NOTES.md',
 archives:folders.map(folder=>read('_packet/revision/evidence-archives/'+folder.split('/').at(-1)+'.zip.json'))};
fs.writeFileSync('_packet/revision/resume-writer-lifetime.json',JSON.stringify(result,null,2)+'\n');
console.log('Recorded bounded diagnostic, red/red/green/green and14/14twice; final ordinary campaign remains pending.');
