// Own additional negative control for the actual retained Settings state, not only window identity.
const fs=require('node:fs');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const path='src/Marrow.App/Window/WindowManager.cs';
const before='if (page is { } requested) SettingsNavigation.Shared.RequestedPage = requested; // :33';
const after='SettingsNavigation.Shared.RequestedPage = page ?? SettingsPage.General; // mutant resets the same retained view on an ordinary reopen';
const text=fs.readFileSync(path,'utf8');
if(text.includes('\r\n')||text.split(before).length!==2)throw Error('Current restored anchor is not exact');
const rows=[{
 id:'OWN-WIN003-16',records:['PR-WIN-003'],path,
 clause:'Closing Settings and reopening without a requested page retains actual view state, not just the same window and content objects',
 precision:'Own exact source replacement, forces General only for an ordinary page-null reopen while preserving explicit requests and cached instances',
 killer:'Marrow.App.Tests.Window.WindowManagerNextTests.The_default_Settings_view_keeps_search_and_page_then_consumes_a_requested_page_on_reopen',
 replacements:[{before,after}],
}];
fs.writeFileSync('_packet/revision/resume-state-mutations.json',JSON.stringify(rows,null,2)+'\n');
console.log('Validated one additional actual-state control; this does not execute or claim a kill.');
