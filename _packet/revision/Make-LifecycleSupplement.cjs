const fs=require('node:fs');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const path='src/Marrow.App/Window/WindowManager.cs';
const text=fs.readFileSync(path,'utf8');
const native='Marrow.App.Tests.Window.Native.WindowManagerNativeTests.';
const own=JSON.parse(fs.readFileSync('_packet/revision/own-settings-native-mutations.json','utf8'));
const rows=own.filter(row=>['REV-PR-WIN-007-03','REV-PR-WIN-007-04'].includes(row.id)).map(row=>({...row,killer:native+'Actual_maximize_caption_click_then_a_second_click_restores_the_main_window'}));
rows.push({id:'OWN-WIN006-13',records:['PR-WIN-006'],path,clause:'Swift:70 autosave remains installed after a previously saved main frame was restored',killer:'Marrow.App.Tests.Window.WindowManagerFrameMutationTests.A_restored_main_window_continues_to_autosave_each_later_move_and_resize',replacements:[{before:'SetFrameAutosaveName(window, MainFrameName); // :70',after:'if (UserDefaults.Standard.Current.Dictionary(MainFrameName) is null) SetFrameAutosaveName(window, MainFrameName); // :70'}]});
rows.push({id:'LIFETIME-WIN017-09',records:['PR-WIN-017'],path,clause:'Swift:129-139 retained window observer remains registered after hide and reopen',killer:native+'A_second_real_resize_after_hide_and_reopen_reuses_the_same_registered_window_observer',replacements:[{before:'Win32Properties.AddWndProcHookCallback(window, WndProc);',after:'var stoppedAfterHide = false;\n        window.PropertyChanged += (_, change) => { if (change.Property == Avalonia.Visual.IsVisibleProperty && !window.IsVisible) stoppedAfterHide = true; };\n        Win32Properties.AddWndProcHookCallback(window, (nint handle, uint message, nint wParam, nint lParam, ref bool handled) =>\n            stoppedAfterHide ? 0 : WndProc(handle, message, wParam, lParam, ref handled));'}]});
const headless=JSON.parse(fs.readFileSync('_packet/revision/own-headless-mutations.json','utf8'));
const theme=headless.find(row=>row.id==='OWN-THEME039-03');
if(!theme)throw Error('Missing existing theme mutation');
rows.push({...theme,killer:'Marrow.App.Tests.Window.WindowManagerRestartTests.Restart_restores_the_main_frame_from_SettingsStore_and_recomputes_Settings_placement'});
for(const row of rows)for(const replacement of row.replacements)
  if(text.split(replacement.before).length-1!==1)throw Error('Nonunique anchor '+row.id);
fs.writeFileSync('_packet/revision/lifecycle-mutations.json',JSON.stringify(rows,null,2)+'\n');
console.log('Validated '+rows.length+' lifecycle mutation trials');
