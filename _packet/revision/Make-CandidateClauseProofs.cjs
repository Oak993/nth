// Additional record-owned selections for candidate clauses, using existing compiled Facts only.
const fs=require('node:fs');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const read=p=>JSON.parse(fs.readFileSync(p,'utf8'));
const lifecycle=read('_packet/revision/own-process-mutations.json');
const frame=read('_packet/revision/own-headless-mutations.json');
const rows=[];
for(const [source,id] of [['OWN-WIN008-01','OWN-WIN003-13'],['OWN-WIN008-02','OWN-WIN003-14'],['OWN-WIN008-05','OWN-WIN003-15']]) {
 const row=lifecycle.find(r=>r.id===source);
 rows.push({...row,id,records:['PR-WIN-003'],precision:'Exact shared lifecycle substitution, now independently selected under Req=PR-WIN-003; not a globally new defect',source_trial:source});
}
rows.push({id:'OWN-WIN006-14',records:['PR-WIN-006'],path:'src/Marrow.App/Window/WindowManager.cs',
 clause:'Fresh profile centers even though there is no saved frame; restoring a valid frame is preserved by this negative control',
 precision:'Own exact source replacement, tested through the dedicated absent-frame Fact',
 killer:'Marrow.App.Tests.Window.WindowManagerFrameMutationTests.An_absent_saved_frame_centers_the_new_main_window_and_then_installs_autosave',
 replacements:[{before:'if (!SetFrameUsingName(window, MainFrameName)) WindowCentering.Center(window, FrameSizeOf(window)); // :68, ED-225',after:'_ = SetFrameUsingName(window, MainFrameName); // mutant omits only absent-profile centering'}]});
for(const [source,id,clause] of [
 ['OWN-WIN006-03','OWN-WIN006-15','A second process restores the actual persisted X value rather than Y'],
 ['OWN-WIN006-07','OWN-WIN006-16','Production store serialization receives the independent move event before any resize can rescue it'],
]) {
 const row=frame.find(r=>r.id===source);
 rows.push({...row,id,clause,killer:'Marrow.App.Tests.Window.WindowManagerRestartTests.Restart_restores_the_main_frame_from_SettingsStore_and_recomputes_Settings_placement',
  precision:'Exact shared frame substitution, independently selected against the authenticated two-child SettingsStore restart Fact',source_trial:source});
}
for(const row of rows)for(const r of row.replacements) {
 const text=fs.readFileSync(row.path,'utf8');
 if(text.includes('\r\n')||text.split(r.before).length!==2)throw Error('Invalid exact anchor: '+row.id);
}
fs.writeFileSync('_packet/revision/candidate-clause-mutations.json',JSON.stringify(rows,null,2)+'\n');
console.log('Validated '+rows.length+' record-owned candidate clause trials');
