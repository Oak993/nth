// Record and archive the observed fixture repair without relabeling diagnostic or aborted suites.
const fs=require('node:fs'),cp=require('node:child_process'),crypto=require('node:crypto');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const base='_packet/revision/evidence/';
const sha=b=>crypto.createHash('sha256').update(b).digest('hex').toUpperCase();
const popup='Marrow.App.Tests.Views.Settings.SettingsProviderProofsTests.The_provider_fixture_hosts_a_real_tooltip_and_preserves_pointer_delivery';
function receipt(directory,file='tests.json') { const path=base+directory+'/'+file;return{path,value:JSON.parse(fs.readFileSync(path,'utf8'))}; }
function uniqueFinished(prefix,file='tests.json') {
 const matching=fs.readdirSync(base,{withFileTypes:true}).filter(d=>d.isDirectory()&&d.name.startsWith(prefix)&&fs.existsSync(base+d.name+'/'+file));
 if(matching.length!==1)throw Error('Expected exactly one completed receipt for '+prefix);
 return receipt(matching[0].name,file);
}
const reds=['resume-popup-regression-red-1-2026-09-29T18-41-01-022Z-c49fee','resume-popup-regression-red-2-2026-09-29T18-41-03-131Z-6e1855'].map(d=>receipt(d));
for(const {value:r} of reds)if(r.exit===0||r.total!==1||r.failed!==1||r.tests[0].name!==popup||!r.tests[0].error.includes('Unable to create IPopupImpl and no overlay layer'))throw Error('Exact missing-layer regression not verified');
const gates=[uniqueFinished('resume-provider-original-newline-gate-1-'),uniqueFinished('resume-provider-original-newline-gate-2-')];
for(const {value:r} of gates)if(r.exit!==0||r.total!==26||r.passed!==26||r.failed!==0||r.environment.outer_opt_in_variables.length||r.environment.api_key_variables_remaining.length)throw Error('Ordinary complete provider class gate failed');
const build=uniqueFinished('resume-provider-original-newline-build-','build.json');
if(!build.value.clean||build.value.exit!==0)throw Error('Repair build not clean');
const iconDiagnostics=['resume-icon-hit-diagnostic-1-2026-09-29T18-43-35-261Z-67f3d9','resume-icon-hit-diagnostic-2-2026-09-29T18-43-38-672Z-cf6dff','resume-icon-hit-diagnostic-3-2026-09-29T18-43-41-917Z-0070d7'];
for(const d of iconDiagnostics){const r=receipt(d).value,raw=fs.readFileSync(base+d+'/tests.trx','utf8');if(r.exit===0||r.total!==1||r.failed!==1||!raw.includes('ICON_HIT_DIAGNOSTIC asset=provider-pi; point=27, 728; client=780, 6000;')||!raw.includes('after_render=Border'))throw Error('Before/after render observation missing');}
const source='tests/Marrow.App.Tests/Views/Settings/SettingsProviderProofsTests.cs';
if(fs.readFileSync(source,'utf8').includes('ICON_HIT_DIAGNOSTIC')||fs.existsSync('tests/Marrow.App.Tests/Fixtures/TemporaryResumeStackProbe.cs'))throw Error('Remove temporary instrumentation');
const originalSource=cp.execFileSync('git',['show','HEAD:'+source]).toString('utf8'),currentSource=fs.readFileSync(source,'utf8');
if(!originalSource.includes('\r\n')||originalSource.replace(/\r\n/g,'').includes('\n')||currentSource.replace(/\r\n/g,'').includes('\n'))throw Error('Preserve this existing file\'s original uniform CRLF style');
const folders=fs.readdirSync(base,{withFileTypes:true}).filter(d=>d.isDirectory()&&/^(resume-popup-|resume-provider-|resume-icon-|resume-stall-observer-build-)/.test(d.name)).map(d=>base+d.name);
folders.push(base+'app-hang-diagnostic-2026-09-29T18-28-19-851Z-a4720f',base+'final-app-ordinary-1-2026-09-29T17-14-47-178Z-e197e5',base+'final-app-ordinary-2-2026-09-29T17-24-03-670Z-d4f6c5');
cp.execFileSync(process.execPath,['_packet/revision/Archive-SelectedEvidence.cjs',...folders],{stdio:'inherit',windowsHide:true});
const result={
 recorded_at:new Date().toISOString(),head_before_commit:cp.execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),
 status:'FIXTURE_REPAIR_VALIDATED_IN_ORDINARY_CLASS_GATES_FINAL_SUITE_PENDING',
 original_failure:base+'final-app-ordinary-1-2026-09-29T17-14-47-178Z-e197e5/tests.json',
 original_scope:'One failed popup open during the final Deepseek close click: 3632/3633, not a balance-value failure. The original Fact passed twice alone before the repair, so preserve its intermittent classification.',
 popup_regression:{fact:popup,red_twice:reds.map(r=>r.path),actual_exception:'Unable to create IPopupImpl and no overlay layer is found for the target control',repair:'Install the existing pinned Avalonia popup-layer window-template pattern. Keep the zoom-tested content layer and all real input.'},
 icon_scene:{first_class_failure:base+'resume-provider-class-gate-2026-09-29T18-42-00-216Z-7def58/tests.json',diagnostics:iconDiagnostics.map(d=>base+d+'/tests.trx'),observation:'Three failing reads at (27,728) for provider-pi became Border hits after a full window render, without moving the target or changing its 18x18 geometry. The pre-render assertion remained red in those diagnostic builds.',repair:'Refresh the full window scene after the icon-only bitmap render before the existing real hit-test/pointer assertions, matching PointerClick input preparation.'},
 clean_build:build.path,ordinary_class_gates:gates.map(r=>r.path),
 assertions:'No existing assertion, expected value, provider behavior or production implementation was removed or changed. A real tooltip/pointer fixture regression was added.',
 newlines:{provider_test:'Uniform CRLF preserved from repository HEAD, rather than converting 679 pre-existing lines to LF. The final diff is 40 added lines, zero removed lines.',new_helper:'LF',production_window_manager:'Unchanged intended LF bytes'},
 earlier_stall:{abort:'_packet/revision/final-app-third-hang-abort.json',diagnostic:base+'app-hang-diagnostic-2026-09-29T18-28-19-851Z-a4720f/diagnostic-result.json',outcome:'Diagnostic passed 3633/3633; no stalled self-stack or unfinished Fact. Earlier ordinary stall cause remains unidentified; this is not an ordinary pass or a claimed hang repair.',observer_copy:'_packet/staging/resume-popup/TemporaryResumeStackProbe.cs.txt',observer_sha256:'26DFFB7EC1D9C3186EFF1CCC04420BBEF21526F07A14C887C2B0C86BB400B95B',compiled_observer_removed:true},
 source_hashes:[source,'tests/Marrow.App.Tests/Fixtures/SettingsProviderPopupLayers.cs'].map(name=>({name,sha256:sha(fs.readFileSync(name))})),
 archives:folders.map(folder=>JSON.parse(fs.readFileSync('_packet/revision/evidence-archives/'+folder.split('/').at(-1)+'.zip.json','utf8'))),
};
fs.writeFileSync('_packet/revision/resume-fixture-repair.json',JSON.stringify(result,null,2)+'\n');
console.log('Fixture repair evidence recorded; full ordinary suite remains pending.');
