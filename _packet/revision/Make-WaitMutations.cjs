const fs=require('node:fs');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const source='src/Marrow.App/AppDelegate.cs';
const text=fs.readFileSync(source,'utf8');
const tests='Marrow.App.Tests.Window.WindowManagerTerminationWaitTests.';
const rows=[];
for(const record of ['PR-WIN-003','PR-WIN-008']) {
  const definitions=[
    ['The bound close must defer during a merge','if (Model is not { } model || !model.IsAnyHydraMergeRunning)','if (Model is not { } model || model.IsAnyHydraMergeRunning)','The_bound_main_close_stays_visible_and_waits_until_the_next_250_millisecond_merge_poll'],
    ['AppDelegate.swift:49 polls every 250ms, not earlier','SwiftTask.TrySleep(TimeSpan.FromMilliseconds(250))','SwiftTask.TrySleep(TimeSpan.FromMilliseconds(249))','The_bound_main_close_stays_visible_and_waits_until_the_next_250_millisecond_merge_poll'],
    ['AppDelegate.swift:49 polls every 250ms, not later','SwiftTask.TrySleep(TimeSpan.FromMilliseconds(250))','SwiftTask.TrySleep(TimeSpan.FromMilliseconds(251))','The_bound_main_close_stays_visible_and_waits_until_the_next_250_millisecond_merge_poll'],
    ['AppDelegate.swift:47 caps the main close wait at four minutes','SwiftDate.AddingTimeInterval(SwiftDate.Now, 4 * 60)','SwiftDate.AddingTimeInterval(SwiftDate.Now, 4 * 60 + 1)','The_bound_main_close_waits_exactly_four_minutes_for_a_merge_that_does_not_finish'],
  ];
  definitions.forEach(([clause,before,after,killer],index)=>{
    if(text.split(before).length-1!==1)throw Error('Non-unique wait mutation anchor');
    rows.push({id:'WAIT-'+record+'-'+(index+1),records:[record],path:source,clause,killer:tests+killer,replacements:[{before,after}]});
  });
}
fs.writeFileSync('_packet/revision/own-wait-mutations.json',JSON.stringify(rows,null,2)+'\n');
console.log('Validated '+rows.length+' wait mutations');
