// Exact additional mutation descriptions. Running this only writes manifests, never production.
const fs = require('node:fs');
const path = require('node:path');
const root = 'E:\\Droppy\\wt\\oc\\chatgpt-1';
if (process.cwd().toLowerCase() !== root.toLowerCase()) throw Error('Wrong worktree');
const wm = 'src/Marrow.App/Window/WindowManager.cs';
const sv = 'src/Marrow.App/Views/Settings/SettingsView.cs';
const tw = 'src/Marrow.App/Window/ThreadWindow.cs';
const palette = 'src/Marrow.App/Views/Palette/CommandPalette.cs';
const p = 'Marrow.App.Tests.Window.';
const screen = p + 'WindowManagerSettingsScreenTests.';
const present = p + 'WindowManagerSettingsPresentationTests.';
const opener = p + 'WindowManagerSettingsOpenerTests.';
const surface = p + 'WindowManagerSettingsSurfaceTests.';
const native = p + 'Native.WindowManagerNativeTests.';
const highRisk = p + 'WindowManagerHighRiskTests.Real_user32_nonclient_button_messages_drive_close_minimise_and_maximise_and_keep_Settings_zoom_inert';
const mutations = [];
function add(record, source, clause, before, after, killer) {
  const count = mutations.filter(m => m.records[0] === record).length + 1;
  mutations.push({id:'REV-'+record+'-'+String(count).padStart(2,'0'), records:[record], path:source, clause, precision:'Own additional exact replacement, not a copied verifier survivor', killer, replacements:[{before,after}]});
}
const fixed = present+'Settings_factory_itself_installs_fixed_sizing_and_native_chrome_properties';
const retained = present+'Settings_movement_close_and_reopen_do_not_write_frame_preferences';
const shortcuts = opener+'Default_and_remapped_shortcuts_reopen_the_same_page_and_search';
const realView = surface+'Actual_Settings_content_has_26_point_clip_and_full_window_backdrop';
const workarea = screen+'Workarea_edges_exclude_taskbars_on_the_main_monitor';
const mixed = screen+'Mixed_DPI_centering_uses_both_parent_coordinates_and_rounds_before_pixel_conversion';
const outer = screen+'Outer_frame_extents_are_used_for_both_parent_and_settings';
const half = screen+'A_positive_half_point_center_rounds_up_before_the_workarea_clamp';
const small = screen+'Smaller_workarea_preserves_outer_min_without_resizing_settings';

add('PR-SETTINGS-001',wm,'Swift:21 fixed content height','Settings = new(780, 580);','Settings = new(780, 579);',fixed);
add('PR-SETTINGS-001',wm,'Swift:21 fixed content width','Settings = new(780, 580);','Settings = new(779, 580);',fixed);
add('PR-SETTINGS-001',wm,'Swift:16,34 independent Settings cache','var window = settingsWindow ?? MakeSettingsWindow();','var window = mainWindow ?? MakeSettingsWindow();',retained);
add('PR-SETTINGS-001',wm,'Swift:35 Settings cache retains Settings rather than main','settingsWindow = window; // :35','settingsWindow = mainWindow; // :35',retained);
add('PR-SETTINGS-001',wm,'Swift:98 retained content is not discarded','window.Hide();','window.Content = null; window.Hide();',retained);
add('PR-SETTINGS-001',wm,'OD-0008 C3 Settings shortcut reaches factory','_ = MenuKeyEquivalents.Attach(window, shortcut => new AppCommands(Model).PerformKeyEquivalent(shortcut));','_ = window; // mutation: omit shortcut dispatch',shortcuts);
add('PR-SETTINGS-001',wm,'Swift:81 correct secondary window type','windowClass: () => new SecondaryWindow()','windowClass: () => new ThreadWindow()',fixed);
add('PR-SETTINGS-001',wm,'Swift:98 close hides, not mere invisible paint','window.Hide();','window.Opacity = 0;',retained);

add('PR-SETTINGS-002',wm,'Swift:46 converts the main x coordinate using its scale','main.Position.X / scale','main.Position.X',mixed);
add('PR-SETTINGS-002',wm,'Swift:46 converts the main y coordinate using its scale','main.Position.Y / scale','main.Position.Y',mixed);
add('PR-SETTINGS-002',wm,'Swift:46 parent frame includes decorations','var parentSize = main.FrameSize ?? FrameSizeOf(main);','var parentSize = main.ClientSize;',outer);
add('PR-SETTINGS-002',wm,'Swift:48 centers with half child width','parent.Center.X - frame.Width / 2','parent.Center.X - frame.Width / 3',half);
add('PR-SETTINGS-002',wm,'Swift:49 centers with half child height','parent.Center.Y - frame.Height / 2','parent.Center.Y - frame.Height / 3',half);
add('PR-SETTINGS-002',wm,'Swift:50 work area retains its left origin','new Rect(working.Left, up - working.Bottom, working.Width, working.Height)','new Rect(working.Right, up - working.Bottom, working.Width, working.Height)',workarea);
add('PR-SETTINGS-002',wm,'Swift:50 missing main screen falls back to primary','(main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary)','(main.Screens.ScreenFromWindow(main))',screen+'Missing_main_screen_uses_primary_workarea');
add('PR-SETTINGS-002',wm,'Swift:46-54 use main DPI, not the child previous monitor DPI','var scale = main.DesktopScaling;','var scale = window.DesktopScaling;',mixed);

add('PR-WIN-010',wm,'Swift:81 cannot inherit main minimum width','WindowCentering.Center(window, FrameSizeOf(window)); // :82','window.MinWidth = Size.MainMinimum.Width; WindowCentering.Center(window, FrameSizeOf(window)); // :82',fixed);
add('PR-WIN-010',wm,'Swift:81 cannot inherit an excessive minimum height','WindowCentering.Center(window, FrameSizeOf(window)); // :82','window.MinHeight = 860; WindowCentering.Center(window, FrameSizeOf(window)); // :82',fixed);
add('PR-WIN-010',wm,'Swift:81 hosts the Settings factory','Size.Settings, resizable: false, SettingsContent,','Size.Settings, resizable: false, MainContent,',realView);
add('PR-WIN-010',wm,'Swift:80-84 does not attach frame autosave','WindowCentering.Center(window, FrameSizeOf(window)); // :82','SetFrameAutosaveName(window, "MutatedSettingsFrame"); WindowCentering.Center(window, FrameSizeOf(window)); // :82',retained);
add('PR-WIN-010',tw,'SecondaryWindow Escape leaves an editing field open','FocusManager?.GetFocusedElement() is not TextBox)','true)',opener+'Secondary_keyboard_close_preserves_state_and_field_Escape_keeps_the_window_open');
add('PR-WIN-010',tw,'SecondaryWindow close yields to an attached sheet','if (AttachedSheet is null && KeyCode(e) is { } keyCode &&','if (KeyCode(e) is { } keyCode &&',opener+'Secondary_close_chord_yields_while_a_sheet_is_attached');
add('PR-WIN-010',tw,'SecondaryWindow Ctrl-W uses the defined physical chord','new KeyChord(0x11, ChordModifiers.Control)) // :59','new KeyChord(0x11, ChordModifiers.Control | ChordModifiers.Shift)) // :59',opener+'Secondary_keyboard_close_preserves_state_and_field_Escape_keeps_the_window_open');
add('PR-WIN-010',tw,'SecondaryWindow Escape outside fields closes it','KeyCode(e) == 0x01 &&','KeyCode(e) == 0x02 &&',opener+'Secondary_keyboard_close_preserves_state_and_field_Escape_keeps_the_window_open');

add('PR-WIN-011',wm,'Swift:46 parent is main, not Settings','var parentSize = main.FrameSize ?? FrameSizeOf(main);','var parentSize = FrameSizeOf(window);',outer);
add('PR-WIN-011',wm,'Swift:47 child frame includes decorations','var frame = FrameSizeOf(window); // :47','var frame = new Avalonia.Size(window.Width, window.Height); // :47',outer);
add('PR-WIN-011',wm,'Swift:48 positive half-point rounds away from zero','SwiftNumber.Rounded(parent.Center.X - frame.Width / 2)','Math.Floor(parent.Center.X - frame.Width / 2)',half);
add('PR-WIN-011',wm,'Swift:49 negative logical coordinate does not ceil','SwiftNumber.Rounded(parent.Center.Y - frame.Height / 2)','Math.Ceiling(parent.Center.Y - frame.Height / 2)',mixed);
add('PR-WIN-011',wm,'Swift:51 outer min wins for an undersized work area','SwiftMin(SwiftMax(x, working.Left), working.Right - frame.Width)','SwiftMax(SwiftMin(x, working.Right - frame.Width), working.Left)',small);
add('PR-WIN-011',wm,'Swift:52 upward-space outer min wins for an undersized area','SwiftMin(SwiftMax(y, working.Top), working.Bottom - frame.Height)','SwiftMax(SwiftMin(y, working.Bottom - frame.Height), working.Top)',small);
add('PR-WIN-011',wm,'Swift:36 already-visible Settings is recentered too','CenterOverMainWindow(window); // :36','if (!window.IsVisible) CenterOverMainWindow(window); // :36',present+'Visible_inactive_settings_receives_explicit_activation');
add('PR-WIN-011',wm,'Swift:54 converts logical x back to native pixels','SwiftNumber.Rounded(centered.X * scale)','SwiftNumber.Rounded(centered.X)',mixed);

add('UI-SETTINGS-001',sv,'SettingsView.swift:120 clips the content','frame.ClipToBounds = true;','frame.ClipToBounds = false;',realView);
add('UI-SETTINGS-001',sv,'SettingsView.swift:118 backdrop is behind controls','Children = { backdrop, new WindowChromeProbe(), grid }','Children = { grid, new WindowChromeProbe(), backdrop }',realView);
add('UI-SETTINGS-001',sv,'SettingsView.swift:119 configuration probe cannot intercept input','Children = { backdrop, new WindowChromeProbe(), grid }','Children = { backdrop, new WindowChromeProbe { IsHitTestVisible = true }, grid }',realView);
add('UI-SETTINGS-001',sv,'SettingsView.swift:122 fixed view width','Width = 780; Height = 580; // :122','Width = 781; Height = 580; // :122',realView);
add('UI-SETTINGS-001',sv,'SettingsView.swift:112 sidebar drag region spans 212 points','sidebar.Width = 212;','sidebar.Width = 211;',surface+'Sidebar_background_is_a_hit_tested_caption_region');
add('UI-SETTINGS-001',sv,'SettingsView.swift:122 fixed view height','Width = 780; Height = 580; // :122','Width = 780; Height = 581; // :122',realView);
add('UI-SETTINGS-001',sv,'SettingsView.swift:123-127 requested page replaces retained selection','state.Page = page;\n            SettingsNavigation.Shared.RequestedPage = null;','state.Page = SettingsPage.General;\n            SettingsNavigation.Shared.RequestedPage = null;',p+'WindowManagerNextTests.The_default_Settings_view_keeps_search_and_page_then_consumes_a_requested_page_on_reopen');
add('UI-SETTINGS-001',palette,'CommandPalette.swift:176-178 Settings action is connected','() => WindowManager.Shared.ShowSettings()));','() => { }));',opener+'Palette_row_click_closes_palette_then_reopens_retained_Settings');

add('PR-WIN-007',wm,'ED-063 minimize caption routes to minimize, not close','8 => SC_MINIMIZE,','8 => SC_CLOSE,',native+'Factory_hooks_capture_the_caption_press_and_complete_a_real_minimize_click');
add('PR-WIN-007',wm,'ED-063 close caption routes to close, not minimize','20 => SC_CLOSE,','20 => SC_MINIMIZE,',native+'Actual_close_clicks_request_main_termination_and_retain_the_hidden_Settings_window');
add('PR-WIN-007',wm,'ED-063 maximized caption toggles to restore','9 when window.WindowState == WindowState.Maximized => SC_RESTORE,','9 when window.WindowState == WindowState.Maximized => SC_MAXIMIZE,',highRisk);
add('PR-WIN-007',wm,'ED-063 normal maximize caption routes to maximize','9 => SC_MAXIMIZE,','9 => SC_MINIMIZE,',highRisk);
add('PR-WIN-007',wm,'ED-063 minimize command sets minimized state','SC_MINIMIZE when window.CanMinimize => SetWindowState(window, WindowState.Minimized),','SC_MINIMIZE when window.CanMinimize => SetWindowState(window, WindowState.Normal),',native+'Factory_hooks_capture_the_caption_press_and_complete_a_real_minimize_click');
add('PR-WIN-007',wm,'ED-063 captured client release is re-hit-tested','if (message == WM_LBUTTONUP) wParam = CaptionHitAtClientPoint(hWnd, lParam);','if (message == WM_LBUTTONUP) wParam = 0;',native+'Factory_hooks_capture_the_caption_press_and_complete_a_real_minimize_click');
add('PR-WIN-007',wm,'ED-063 menu always enables close','SetMenuItemState(menu, SC_CLOSE, true);','SetMenuItemState(menu, SC_CLOSE, false);',native+'A_real_caption_right_click_opens_a_menu_with_each_windows_native_capabilities');
add('PR-WIN-007',wm,'ED-063 normal window has no restore menu action','SetMenuItemState(menu, SC_RESTORE, window.WindowState != WindowState.Normal);','SetMenuItemState(menu, SC_RESTORE, window.WindowState == WindowState.Normal);',native+'A_real_caption_right_click_opens_a_menu_with_each_windows_native_capabilities');

for (const record of ['PR-SETTINGS-002','PR-WIN-011'])
  add(record,wm,'Swift:46 centers over the actual native main frame, even when requested content height differs','var parentSize = main.FrameSize ?? FrameSizeOf(main);','var parentSize = FrameSizeOf(main);',native+'Settings_reopening_uses_the_actual_main_monitor_workarea_and_native_frame_DPI');

for (const m of mutations) {
  const text = fs.readFileSync(path.join(root,m.path),'utf8');
  for (const r of m.replacements) {
    const count = text.split(r.before).length-1;
    if (count !== 1) throw Error(m.id + ' exact matches ' + count);
  }
}
fs.mkdirSync(path.join(root,'_packet/revision'),{recursive:true});
fs.writeFileSync(path.join(root,'_packet/revision/own-settings-native-mutations.json'),JSON.stringify(mutations,null,2)+'\n');
console.log(JSON.stringify(Object.fromEntries([...new Set(mutations.flatMap(m=>m.records))].map(r=>[r,mutations.filter(m=>m.records.includes(r)).length]))));
