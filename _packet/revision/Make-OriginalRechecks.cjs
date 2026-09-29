// Recheck previously reproduced verifier survivors against the strengthened ordinary-gate tests.
const fs = require('node:fs');
const path = require('node:path');
const root = 'E:\\Droppy\\wt\\oc\\chatgpt-1';
if (process.cwd().toLowerCase() !== root.toLowerCase()) throw Error('Wrong worktree');
const mutations=JSON.parse(fs.readFileSync('_packet/evidence/window-mutations/original-mutations.json','utf8'));
const p='Marrow.App.Tests.Window.';
const n=p+'Native.WindowManagerNativeTests.';
const screen=p+'WindowManagerSettingsScreenTests.';
const presentation=p+'WindowManagerSettingsPresentationTests.';
const surface=p+'WindowManagerSettingsSurfaceTests.';
const killers={
  A08:n+'Factory_hooks_capture_the_caption_press_and_complete_a_real_minimize_click',
  H04:n+'A_real_edge_drag_activates_only_the_factory_created_resize_state_and_clears_it_on_release',
  A13:n+'Release_over_a_different_caption_control_cancels_the_original_command',
  A14:n+'Factory_hooks_capture_the_caption_press_and_complete_a_real_minimize_click',
  'A16b-A17-context':n+'A_real_caption_right_click_opens_a_menu_with_each_windows_native_capabilities',
  'A16b-A17-key':n+'Space_key_menu_is_anchored_to_its_factory_window_instead_of_decoding_the_character_as_coordinates',
  'A18-items':n+'A_real_caption_right_click_opens_a_menu_with_each_windows_native_capabilities',
  'A19-cancel':n+'Cancel_mode_and_real_capture_transfer_clear_the_pending_caption_press',
  A09:n+'Settings_maximize_is_rejected_at_caption_acceptance_and_at_system_command_dispatch',
  A10:n+'Settings_maximize_is_rejected_at_caption_acceptance_and_at_system_command_dispatch',
  A01:presentation+'Settings_factory_itself_installs_fixed_sizing_and_native_chrome_properties',
  A03:presentation+'Settings_factory_itself_installs_fixed_sizing_and_native_chrome_properties',
  A07:presentation+'Settings_factory_itself_installs_fixed_sizing_and_native_chrome_properties',
  B12:screen+'Reopening_requeries_the_main_windows_current_monitor',
  B18:screen+'Workarea_edges_exclude_taskbars_on_the_main_monitor',
  B15:presentation+'Visible_inactive_settings_receives_explicit_activation',
  B14:presentation+'Cached_show_centers_before_the_platform_show_request',
  B13:screen+'Mixed_DPI_centering_uses_both_parent_coordinates_and_rounds_before_pixel_conversion',
  F01:p+'WindowManagerTitleMutationTests.The_main_window_and_its_accessibility_peer_use_the_supplied_display_name_not_a_literal',
  F02:p+'WindowManagerTitleMutationTests.The_unmodified_main_factory_hosts_one_real_RootView_with_the_managers_model_and_window_environment',
  S01:p+'WindowManagerProcessTests.Closing_main_after_FinishLaunching_exits_the_child_process_and_Settings_is_retained',
  E11:p+'WindowManagerFrameMutationTests.A_positive_offscreen_oversized_saved_frame_is_applied_before_presentation_without_clamping',
  E12:p+'WindowManagerFrameMutationTests.A_positive_offscreen_oversized_saved_frame_is_applied_before_presentation_without_clamping',
  E13:p+'WindowManagerFrameMutationTests.A_positive_offscreen_oversized_saved_frame_is_applied_before_presentation_without_clamping',
  E14:p+'WindowManagerFrameMutationTests.A_positive_offscreen_oversized_saved_frame_is_applied_before_presentation_without_clamping',
  I01:surface+'Actual_Settings_content_has_26_point_clip_and_full_window_backdrop',
  I02:surface+'A_live_opacity_change_reaches_the_actual_retained_Settings_backdrop',
  I03:surface+'Sidebar_background_is_a_hit_tested_caption_region',
};
for(const m of mutations) {
  m.killer=killers[m.id];
  m.clause='Verifier '+m.id+': '+m.precision;
  if(m.id==='A10') {
    m.replacements=[{before:'SC_MAXIMIZE => !window.CanMaximize || SetWindowState(window, WindowState.Maximized),',after:'SC_MAXIMIZE => SetWindowState(window, WindowState.Maximized),'}];
    m.precision+='; rebased to the minimal denied-command-consumed correction';
  }
  if(!m.killer) throw Error('No killer for '+m.id);
  const text=fs.readFileSync(path.join(root,m.path),'utf8');
  for(const replacement of m.replacements) {
    const count=text.split(replacement.before).length-1;
    if(count!==1)throw Error(m.id+' matches '+count);
  }
}
fs.writeFileSync('_packet/revision/original-rechecks.json',JSON.stringify(mutations,null,2)+'\n');
console.log('Validated '+mutations.length+' exact original/rebased replacements');
