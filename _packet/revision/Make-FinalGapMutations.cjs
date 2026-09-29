const fs=require('node:fs');
if(process.cwd().toLowerCase()!=='e:\\droppy\\wt\\oc\\chatgpt-1')throw Error('Wrong worktree');
const window='src/Marrow.App/Window/WindowManager.cs';
const core='src/Marrow.Core/Support/Text.cs';
const native='Marrow.App.Tests.Window.Native.WindowManagerNativeTests.';
const title='Marrow.App.Tests.Window.WindowManagerTitleMutationTests.';
const rows=[];
function add(id,record,path,clause,killer,before,after) {
  rows.push({id,records:[record],path,clause,precision:'Own additional exact source replacement; not an unpublished verifier mutation',killer,replacements:[{before,after}]});
}
add('OWN-WIN009-09','PR-WIN-009',core,'Missing display metadata reaches the actual factory and managed peer with the unchanged Droppy Code fallback',
  title+'Missing_display_name_metadata_reaches_the_main_window_and_managed_peer_as_Droppy_Code',
  'return new(Value("CFBundleDisplayName", "Droppy Code"),','return new(Value("CFBundleDisplayName", "Marrow"),');
add('OWN-WIN009-10','PR-WIN-009',core,'Wrong-type metadata is rejected rather than converted to a display name',
  title+'Wrong_type_display_name_metadata_reaches_the_main_window_and_managed_peer_as_Droppy_Code',
  'metadata.TryGetValue(key, out var value) && value is string text ? text : fallback;',
  'metadata.TryGetValue(key, out var value) ? value?.ToString() ?? fallback : fallback;');
add('OWN-WIN009-11','PR-WIN-009',window,'The actual factory propagates the injected Unicode metadata name into native HWND text',
  native+'The_main_factory_publishes_the_injected_Unicode_display_name_to_its_own_HWND',
  'MakeWindow(AppInfo.Current.Name, Size.Main, resizable: true, MainContent, windowClass: () => new ThreadWindow())',
  'MakeWindow("Droppy Code", Size.Main, resizable: true, MainContent, windowClass: () => new ThreadWindow())');
add('OWN-THEME039-09','PR-THEME-039',window,'A restarted saved-frame profile still applies the initial process appearance before any second-process theme change',
  'Marrow.App.Tests.Window.WindowManagerRestartTests.Restart_restores_the_main_frame_from_SettingsStore_and_recomputes_Settings_placement',
  '(_, theme) => SetAppearance(theme), initial: true',
  '(_, theme) => SetAppearance(theme), initial: UserDefaults.Standard.Current.Dictionary("DroppyCodeMainWindow") is null');
add('OWN-WIN008-13','PR-WIN-008',window,'Construction realizes the HWND without presenting it before the first explicit Show',
  native+'The_factory_realizes_its_owned_native_handle_before_the_first_Show',
  'SetFrameAutosaveName(window, MainFrameName); // :70\n        return window;',
  'SetFrameAutosaveName(window, MainFrameName); // :70\n        window.Show();\n        return window;');
add('OWN-WIN007-09','PR-WIN-007',window,'ED-063 key-menu dispatch does not open the system popup for the nonspace mnemonic f',
  native+'A_nonspace_key_menu_mnemonic_does_not_open_the_system_menu',
  "if (lParam == (nint)' ' && TryShowSystemMenu(window, hWnd, lParam, keyboard: true))",
  'if (TryShowSystemMenu(window, hWnd, lParam, keyboard: true))');
for(const row of rows)for(const replacement of row.replacements) {
  const text=fs.readFileSync(row.path,'utf8');
  if(text.includes('\r\n')||text.split(replacement.before).length-1!==1)throw Error('Invalid exact anchor: '+row.id);
}
fs.writeFileSync('_packet/revision/final-gap-mutations.json',JSON.stringify(rows,null,2)+'\n');
console.log('Validated '+rows.length+' final gap mutation trials');
