using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :1-2859; the file is unchanged from B2 1fbf81e,
// so B2 and B3 line numbers are the same.
// Part 1 of 6 (PR-CHAT-020, 036, 077-082, 088-102, 104) plus the AttachmentStrip and AttachmentThumbnail tests
// (UI-CHAT-009) an earlier merge brought; part 2 of 6 (PR-CHAT-105 to 116, 123, 143 to 153). Sites these parts cannot
// verify yet are listed in the STOP notes at the end.
[Collection("Headless")]
public sealed class TimelineRowsTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-CHAT-033")] // App/Views/Chat/TimelineRows.swift:2356-2369
    public void Checklist_symbols_share_the_first_text_baseline_with_their_steps()
    {
        headless.OnUiThread(() => { using var actor = new TestRuntime(); actor.OnMain(() => {
            var row = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps(("first line\nsecond line", TodoStep.Status.Active))))) { Width = 350 };
            var window = Show(row);
            var label = TextNamed(row, "first line\nsecond line");
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(image => image.SymbolName == "circle.dotted.circle");
            var a = label.TranslatePoint(default, row)!.Value.Y + label.TextLayout.TextLines[0].Baseline;
            var b = glyph.TranslatePoint(default, row)!.Value.Y + TextBlock.GetBaselineOffset(glyph);
            Assert.Equal(a, b, 5);
            Close(window);
        }); });
    }
    [Fact]
    [Trait("Req", "PR-CHAT-154")] // App/Views/Chat/TimelineRows.swift:1958-1973
    public void Head_tool_steps_have_two_wrapping_lines_with_a_constrained_text_column()
    {
        headless.OnUiThread(() => { using var actor = new TestRuntime(); actor.OnMain(() => {
            var entry = ToolEntry(Call(ToolCall.Kind.Read, string.Join(" ", Enumerable.Repeat("a long source path", 15))));
            var view = Assert.IsAssignableFrom<Control>(InvokeStatic(typeof(HydraHeadStepsPopover), "Step", entry));
            view.Width = 300;
            var window = Show(view);
            var label = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(view));
            Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
            Assert.Equal(2, label.MaxLines);
            Assert.True(label.Bounds.Width <= 300 - TimelineMetrics.IconWidth - TimelineMetrics.IconSpacing);
            Close(window);
        }); });
    }

    [Fact]
    [Trait("Req", "UI-GIT-047")] // App/Views/Chat/TimelineRows.swift:2777-2798
    public void Git_confirmation_UI_is_close_first_with_default_Revert_pending_only_small_size_STOP_5() => The_revert_disc_asks_in_a_300_point_popover_where_only_Revert_reverts();
    [Fact]
    [Trait("Req", "UI-GIT-048")] // App/Views/Chat/TimelineRows.swift:2800-2816
    public void Git_review_UI_captures_the_clicked_button_only_after_hover() => The_review_control_hands_its_own_anchor_to_the_review_once_the_pointer_has_been_there_and_does_nothing_before();
    [Fact]
    [Trait("Req", "UI-GIT-049")] // App/Views/Chat/TimelineRows.swift:130-156
    public void Git_pencil_UI_prefetches_and_toggles_without_moving_the_message() { Settling_the_pointer_on_the_pencil_starts_the_revert_preview(); The_pencil_opens_the_edit_on_the_runtime_and_a_second_tap_closes_it(); }
    [Fact]
    [Trait("Req", "UI-GIT-050")] // App/Views/Chat/TimelineRows.swift:216-228, :172-190
    public void Git_edit_item_UI_is_third_in_a_new_group_and_posts_only_an_intent() => Edit_from_here_posts_only_the_confirmation_intent_and_a_non_revertable_menu_cannot_post_it();
    [Fact]
    [Trait("Req", "UI-GIT-051")] // App/Views/Chat/TimelineRows.swift:191-196
    public void Git_dialog_UI_intent_preserves_the_two_way_revert_site_at_STOP_2() => Edit_from_here_posts_only_the_confirmation_intent_and_a_non_revertable_menu_cannot_post_it();

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-002")] // App/Views/Chat/TimelineRows.swift:1044-1079
    public void A_new_merge_stage_keeps_the_sizing_slot_and_replaces_only_the_shimmer_identity()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var motion = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            HydraMergeRow row = null!; AvaloniaWindow window = null!; Control oldSlot = null!;
            Control StageSlot(string text)
            {
                var shown = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(label => label.Text == text && BrushColor(label.Foreground) == Resolved(ChromeTokens.PrimaryText.Opacity(0.9), label));
                return shown.GetLogicalAncestors().OfType<Control>().First(parent => parent.GetType().Name == "MorphSlot");
            }
            h.Main.OnMain(() => { h.Runtime.IsHydraMerging = true; h.Runtime.HydraMergeStage = HydraMergeStage.Pushing.Words; row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging()); window = Show(row); oldSlot = StageSlot("Pushing the branch\u2026"); });
            h.Main.OnMain(() => h.Runtime.HydraMergeStage = HydraMergeStage.Opening.Words);
            h.Main.OnMain(() => { Settle(window); Assert.Same(oldSlot, StageSlot("Opening the merge request\u2026")); Close(window); });
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-004")] // App/Views/Chat/TimelineRows.swift:821-841, :844-915, :966-971
    public void The_track_inherits_Reduce_Motion_and_is_hidden_from_accessibility()
    {
        headless.OnUiThread(() => { using var actor = new TestRuntime(); actor.OnMain(() => {
            var track = new HydraMergeTrack(0, 7, HydraMergeTrack.Mode.Running);
            var host = new Border { Child = track };
            EnvironmentValues.SetAccessibilityReduceMotion(host, true);
            var window = Show(host);
            var body = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(track));
            body.AnimatableData = (0.5, (0.5, 0));
            var mark = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(body));
            Assert.Equal(0, Canvas.GetTop(mark));
            Assert.Equal(1, Assert.IsType<ScaleTransform>(mark.RenderTransform).ScaleX);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(track));
            Close(window);
        }); });
    }
    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-005")] // App/Views/Chat/TimelineRows.swift:1099-1120, :1142-1155
    public void Outcome_title_UI_is_verbatim_one_line_with_a_conditional_secondary_chevron() => An_outcome_that_is_not_a_merge_settles_at_once_with_no_flourish_and_an_outcome_with_nothing_behind_it_is_disabled();
    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-006")] // App/Views/Chat/TimelineRows.swift:1121-1126, :1157-1161
    public void Merge_Open_UI_is_a_separate_caption_semibold_browser_control() => A_note_that_leads_with_an_address_gets_an_Open_link_beside_the_pill_that_opens_it_in_the_browser();

    [Theory]
    [InlineData("https://gitlab.com/g/p/-/merge_requests/7\nNo files line.", false)]
    [InlineData("https://gitlab.com/g/p/-/merge_requests/7\n1 file (+2 \u22121) landed on `main` from `head`.", true)]
    [InlineData("1 file (+2 \u22121) landed on `main` from `head`.", false)]
    [Trait("Req", "UI-HYDRAMERGE-007")] // App/Views/Chat/TimelineRows.swift:1111-1120, :607-640
    public void Outcome_popover_requires_a_leading_request_URL_and_a_parseable_files_line(string body, bool merge)
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var presenter = new RecordingPresenter(); using var seam = Presentation.Presenter.Override(presenter);
            HydraMergeRow row = null!; AvaloniaWindow window = null!;
            h.Main.OnMain(() => { row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra opened !7 but could not merge it.\n" + body })); window = Show(row); Click(LogicalQuery.SelfAndDescendants<Button>(row).Single(button => ToolTip.GetTip(button) as string == "Show the message")); });
            h.Main.OnMain(() =>
            {
                var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
                Assert.Equal(PlacementMode.Bottom, popup.Placement);
                // RecordingPresenter never opens a native popup. Deliver the framework Opened notification so
                // PlainControls.Popover's real lazy content builder runs, without creating a native window.
                var opened = Assert.IsAssignableFrom<System.Delegate>(typeof(Popup).GetField("Opened", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(popup));
                opened.DynamicInvoke(popup, System.EventArgs.Empty);
                var content = Assert.IsAssignableFrom<Control>(popup.Child);
                Assert.True(Popovers.GetIsCapsuleButtonBorderShape(content));
                if (merge) Assert.IsType<HydraMergePopover>(content);
                else { var report = Assert.IsType<HydraReportPopover>(content); Assert.Equal(460, report.Width); }
                Close(window);
            });
        });
    }

    [Theory]
    [InlineData(Notice.Level.Info)]
    [InlineData(Notice.Level.Warning)]
    [InlineData(Notice.Level.Error)]
    [Trait("Req", "UI-RUNTIME-001")] // App/Views/Chat/TimelineRows.swift:2388-2404
    public void Long_notice_text_wraps_inside_the_row_and_its_symbol_uses_the_first_text_baseline(Notice.Level level)
    {
        headless.OnUiThread(() => { using var actor = new TestRuntime(); actor.OnMain(() => {
            var text = string.Join(" ", Enumerable.Repeat("The source notice stays within its row.", 12));
            var row = new NoticeRow(NoticeEntry(level, text)) { Width = 300 };
            var window = Show(row);
            var label = Assert.Single(LogicalQuery.SelfAndDescendants<SelectableTextBlock>(row));
            var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(row));
            var left = label.TranslatePoint(default, row)!.Value.X;
            Assert.True(left + label.Bounds.Width <= row.Bounds.Width - (level == Notice.Level.Info ? 0 : 14) + 0.001);
            Assert.True(label.TextLayout.TextLines.Count > 1);
            var y1 = glyph.TranslatePoint(default, row)!.Value.Y + TextBlock.GetBaselineOffset(glyph);
            var y2 = label.TranslatePoint(default, row)!.Value.Y + label.TextLayout.TextLines[0].Baseline;
            Assert.Equal(y2, y1, 5);
            Assert.Equal(FontWeight.Normal, label.FontWeight);
            Close(window);
        }); });
    }
    [Fact]
    [Trait("Req", "UI-RUNTIME-002")] // App/Views/Chat/TimelineRows.swift:2385-2431
    public void Runtime_warning_UI_is_a_selectable_theme_warning_pill() => A_warning_or_error_notice_sits_in_a_full_width_pill_tinted_at_ten_percent_of_its_theme_status_colour();
    [Fact]
    [Trait("Req", "UI-RUNTIME-003")] // App/Views/Chat/TimelineRows.swift:2385-2431
    public void Runtime_error_UI_has_the_same_geometry_but_theme_danger() => A_warning_or_error_notice_sits_in_a_full_width_pill_tinted_at_ten_percent_of_its_theme_status_colour();
    [Fact]
    [Trait("Req", "UI-RUNTIME-004")] // App/Views/Chat/TimelineRows.swift:2433-2456
    public void Runtime_end_UI_is_caption_secondary_only_without_a_reply() => The_standalone_turn_end_line_shows_only_when_the_turn_produced_no_reply();

    [Fact]
    [Trait("Req", "UI-CHAT-039")] // App/Views/Chat/TimelineRows.swift:2777-2798
    public void Revert_panel_UI_keeps_300_point_width_16_insets_and_default_Revert() => The_revert_disc_asks_in_a_300_point_popover_where_only_Revert_reverts();
    [Fact]
    [Trait("Req", "UI-CHAT-040")] // App/Views/Chat/TimelineRows.swift:2800-2816
    public void Review_disc_UI_uses_its_own_lazy_anchor_and_ignores_anchorless_clicks() => The_review_control_hands_its_own_anchor_to_the_review_once_the_pointer_has_been_there_and_does_nothing_before();
    [Fact]
    [Trait("Req", "UI-CHAT-041")] // App/Views/Chat/TimelineRows.swift:2829-2858
    public void File_row_UI_uses_middle_truncation_and_Binary_instead_of_stats() => The_files_popover_lists_twelve_files_then_and_1_more_file_or_and_N_more_files();
    [Fact]
    [Trait("Req", "UI-CHAT-042")] // App/Views/Chat/TimelineRows.swift:2850-2856
    public void File_overflow_UI_keeps_the_exact_singular_and_plural_lines() => The_files_popover_lists_twelve_files_then_and_1_more_file_or_and_N_more_files();
    [Fact]
    [Trait("Req", "UI-CHAT-048")] // App/Views/Chat/TimelineRows.swift:241-329, :340-387
    public void Report_pill_UI_has_overlapping_18_point_glyphs_and_failed_head_names() { A_heads_report_pill_names_who_is_done_and_names_a_failed_or_stopped_head_with_its_outcome(); A_heads_report_pill_overlaps_the_heads_glyphs_and_sits_in_the_tinted_pill_with_96_points_clear_on_the_trailing_side(); }
    [Fact]
    [Trait("Req", "UI-CHAT-049")] // App/Views/Chat/TimelineRows.swift:304-311
    public void Report_Open_UI_is_a_sibling_with_the_exact_tooltip() => A_note_that_leads_with_an_address_gets_an_Open_link_beside_the_pill_that_opens_it_in_the_browser();
    [Fact]
    [Trait("Req", "UI-CHAT-050")] // App/Views/Chat/TimelineRows.swift:607-639
    public void Report_panel_UI_is_fixed_460_with_16_14_padding_and_ten_point_spacing() => The_report_popover_is_460_wide_with_a_460_point_scroller_at_the_callout_style_with_12_point_markdown();
    [Fact]
    [Trait("Req", "UI-CHAT-051")] // App/Views/Chat/TimelineRows.swift:537-595
    public void Brief_UI_uses_the_straight_apostrophe_and_cooking_shimmer() => A_brief_reads_as_cooking_until_its_turn_has_output_and_then_as_the_brief();
    [Fact]
    [Trait("Req", "UI-CHAT-052")] // App/Views/Chat/TimelineRows.swift:438-476, :398-418
    public void Finished_head_UI_keeps_the_status_suffix_and_leading_stack() { A_finished_heads_pill_names_its_outcome_beside_its_glyph_with_the_report_one_tap_away(); The_finished_heads_row_draws_one_leading_aligned_pill_per_named_head_found_among_this_threads_helpers(); }
    [Fact]
    [Trait("Req", "UI-CHAT-053")] // App/Views/Chat/TimelineRows.swift:937-1162
    public void Merge_outcome_UI_updates_its_title_even_when_both_phases_are_outcomes()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            HydraMergeRow row = null!; AvaloniaWindow window = null!;
            h.Main.OnMain(() => { row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra merge held.\nfirst details" })); window = Show(row); Assert.Contains("Hydra merge held", Texts(row)); });
            h.Main.OnMain(() => row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra merge failed.\nsecond details" })));
            h.Main.OnMain(() => { Settle(window); Assert.Contains("Hydra merge failed", Texts(row)); Close(window); });
        });
    }
    [Fact]
    [Trait("Req", "UI-CHAT-054")] // App/Views/Chat/TimelineRows.swift:652-783
    public void Delegation_UI_draws_tasks_projects_and_a_single_sent_wave() { The_delegation_card_says_Summoning_heads_while_streaming_then_names_the_count_or_the_one_head(); The_delegation_card_lists_each_head_with_its_glyph_task_and_other_project_and_once_sent_checks_and_sweeps(); }
    [Fact]
    [Trait("Req", "UI-CHAT-055")] // App/Views/Chat/TimelineRows.swift:1877-2064
    public void Head_panel_UI_has_the_source_section_labels_and_runtime_updates() => Head_steps_mount_at_440_with_a_520_cap_and_update_from_the_heads_own_runtime();
    [Fact]
    [Trait("Req", "UI-CHAT-056")] // App/Views/Chat/TimelineRows.swift:1766-1793, :1723-1755
    public void Departure_UI_is_a_head_badge_never_Delegating_Subagent() { A_call_sending_out_a_head_draws_as_a_head_badge_that_reads_Sending_out_Sent_out_or_Could_not_send_out(); A_call_whose_head_is_linked_reads_Sending_out_or_Sent_out_the_heads_name_and_the_title(); }
    [Fact]
    [Trait("Req", "UI-CHAT-057")] // App/Views/Chat/TimelineRows.swift:2242-2254, :1772-1793
    public void MCP_UI_uses_the_exact_middle_dot_separator_and_catalog_badge() { An_mcp_call_to_a_catalog_server_draws_as_a_pill_badge_with_the_servers_glyph_and_label(); An_mcp_title_without_the_exact_separator_an_empty_side_an_unknown_server_or_another_kind_is_no_badge(); }
    [Fact]
    [Trait("Req", "UI-CHAT-059")] // App/Views/Chat/TimelineRows.swift:2631-2663
    public void Owned_turn_steps_card_has_fourteen_point_insets_and_twenty_point_bottom_gap()
    {
        headless.OnUiThread(() => { using var h = RuntimeHarness(); using var frames = FrameClock.Source.Override(new ManualFrameSource()); h.Main.OnMain(() => {
            var turn = System.Guid.NewGuid(); var content = new ValueList<TimelineEntry>([ReplyEntry("```\nanswer\n```", turn: turn)]);
            var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), new ValueList<TimelineEntry>([UserEntry("question", turn)]), content, null, canUndo: false, chronological: false);
            // STOP #3 is the external expanded derivation only. A real folded Derived still tests the owned frame.
            var derived = Invoke(block, "DerivedFor", false);
            var steps = Assert.IsAssignableFrom<Control>(Invoke(block, "Steps", derived, false));
            Assert.Contains(LogicalQuery.SelfAndDescendants<Border>(steps), b => b.Padding == new Thickness(14));
            Assert.Equal(20, steps.Margin.Bottom);
        }); });
    }
    [Fact]
    [Trait("Req", "UI-CHAT-061")] // App/Views/Chat/TimelineRows.swift:1546-1553
    public void Narration_UI_is_twelve_point_dimmed_markdown_indented_twenty_four_points() => With_every_step_showing_there_is_no_earlier_line_and_narration_is_dimmed_12_point_markdown_after_the_icon_column();
    [Fact]
    [Trait("Req", "UI-CHAT-063")] // App/Views/Chat/TimelineRows.swift:1837-1840
    public void Declined_UI_is_caption_in_the_theme_warning_colour() => A_failed_call_shows_its_exit_code_in_danger_and_a_declined_one_Declined_in_warning();
    [Fact]
    [Trait("Req", "UI-CHAT-064")] // App/Views/Chat/TimelineRows.swift:1833-1836
    public void Exit_UI_is_lowercase_exit_and_monospaced_digits_in_danger() => A_failed_call_shows_its_exit_code_in_danger_and_a_declined_one_Declined_in_warning();
    [Fact]
    [Trait("Req", "UI-CHAT-070")] // App/Views/Chat/TimelineRows.swift:1906-1909
    public void No_steps_UI_has_its_full_stop_and_disappears_after_a_tool() => Head_steps_mount_at_440_with_a_520_cap_and_update_from_the_heads_own_runtime();
    [Fact]
    [Trait("Req", "UI-CHAT-071")] // App/Views/Chat/TimelineRows.swift:680-687
    public void Next_brief_UI_has_the_horizontal_ellipsis_only_while_streaming() => The_delegation_card_says_Summoning_heads_while_streaming_then_names_the_count_or_the_one_head();
    [Fact]
    [Trait("Req", "UI-CHAT-072")] // App/Views/Chat/TimelineRows.swift:2839-2842
    public void Binary_UI_replaces_the_diff_stat_in_the_trailing_column() => The_files_popover_lists_twelve_files_then_and_1_more_file_or_and_N_more_files();
    [Fact]
    [Trait("Req", "UI-GIT-044")] // App/Views/Chat/TimelineRows.swift:2718-2827
    public void Git_card_UI_has_the_asset_singular_title_and_fixed_control_gap() { The_file_card_reads_Edited_1_file_or_Edited_N_files_beside_the_badge_and_the_totals_with_a_chevron(); The_round_controls_follow_a_20_point_gap_revert_only_when_undoable_on_a_hugging_quaternary_pill(); }
    [Fact]
    [Trait("Req", "UI-GIT-045")] // App/Views/Chat/TimelineRows.swift:2829-2858
    public void Git_file_popover_UI_has_the_twelve_row_cut_and_padding() => The_files_popover_lists_twelve_files_then_and_1_more_file_or_and_N_more_files();
    [Fact]
    [Trait("Req", "UI-GIT-046")] // App/Views/Chat/TimelineRows.swift:2770-2776
    public void Git_revert_UI_is_only_present_when_rewindable_and_never_focusable() => The_round_controls_follow_a_20_point_gap_revert_only_when_undoable_on_a_hugging_quaternary_pill();

    [Fact]
    [Trait("Req", "UI-CHAT-011")] // App/Views/Chat/TimelineRows.swift:219, :31-34
    public void Copy_message_UI_keeps_its_symbol_and_Copied_confirmation() => Copy_message_writes_the_plain_text_and_Copy_as_markdown_the_raw_text_and_both_confirm_Copied();
    [Fact]
    [Trait("Req", "UI-CHAT-012")] // App/Views/Chat/TimelineRows.swift:220
    public void Markdown_copy_UI_preserves_the_raw_prose_and_lowercase_label() => Copy_message_writes_the_plain_text_and_Copy_as_markdown_the_raw_text_and_both_confirm_Copied();
    [Fact]
    [Trait("Req", "UI-CHAT-013")] // App/Views/Chat/TimelineRows.swift:222-226
    public void Edit_menu_UI_has_the_divider_and_is_not_destructive() => Right_clicking_a_sent_message_offers_the_two_copies_and_when_revertable_a_divider_then_Edit_from_here();
    [Fact]
    [Trait("Req", "UI-CHAT-014")] // App/Views/Chat/TimelineRows.swift:191-196
    public void Edit_dialog_intent_stays_on_the_row_pending_STOP_2() => Edit_from_here_posts_only_the_confirmation_intent_and_a_non_revertable_menu_cannot_post_it();
    [Fact]
    [Trait("Req", "UI-CHAT-019")] // App/Views/Chat/TimelineRows.swift:1353-1390
    public void Reply_hover_UI_keeps_its_reserved_22_point_line_and_caption() { A_reply_renders_its_markdown_and_keeps_a_hidden_22_point_line_with_the_turn_label_in_caption_secondary_text(); Hovering_a_settled_reply_fades_the_line_in_and_a_streaming_reply_never_shows_it(); }
    [Fact]
    [Trait("Req", "UI-CHAT-020")] // App/Views/Chat/TimelineRows.swift:1401-1405, :38-54
    public void Code_copy_UI_requires_a_closed_fence_and_confirms_Copied() { The_last_code_block_is_the_last_closed_fence_and_an_unclosed_fence_yields_none(); A_replys_menu_is_the_two_copies_then_the_last_code_block_when_there_is_one_then_Add_to_reply_in_its_own_group(); }
    [Fact]
    [Trait("Req", "UI-CHAT-021")] // App/Views/Chat/TimelineRows.swift:1406-1410
    public void Add_quote_UI_has_its_own_group_and_does_not_add_twice() { A_replys_menu_is_the_two_copies_then_the_last_code_block_when_there_is_one_then_Add_to_reply_in_its_own_group(); Add_to_reply_quotes_the_reply_into_the_draft_once(); }
    [Fact]
    [Trait("Req", "UI-CHAT-022")] // App/Views/Chat/TimelineRows.swift:1463-1509
    public void Work_group_UI_opens_once_with_the_source_spacing_and_chevron() => A_group_the_agent_moved_on_from_starts_as_one_summary_line_and_opening_it_reveals_the_end_while_closing_does_not();
    [Fact]
    [Trait("Req", "UI-CHAT-023")] // App/Views/Chat/TimelineRows.swift:1522-1544
    public void Earlier_steps_UI_hides_all_but_the_last_two_until_opened() => Only_the_two_latest_steps_show_under_an_earlier_steps_line_that_reveals_them_all();
    [Fact]
    [Trait("Req", "UI-CHAT-024")] // App/Views/Chat/TimelineRows.swift:1657-1853
    public void Tool_UI_preserves_statuses_action_help_and_chevrons() { The_status_icon_is_a_spinner_a_red_cross_a_raised_hand_or_the_kinds_glyph_in_a_16_point_column(); A_failed_call_shows_its_exit_code_in_danger_and_a_declined_one_Declined_in_warning(); A_popover_row_has_a_static_down_chevron_and_an_output_row_a_right_chevron_turning_down_when_open(); }
    [Fact]
    [Trait("Req", "UI-CHAT-025")] // App/Views/Chat/TimelineRows.swift:2141-2164
    public void Output_UI_is_inline_selectable_and_capped_at_thirty_lines() => Output_over_thirty_lines_shows_its_first_thirty_and_a_link_that_shows_all_of_it_then_Show_less();
    [Fact]
    [Trait("Req", "UI-CHAT-026")] // App/Views/Chat/TimelineRows.swift:2156-2162
    public void Full_output_UI_link_switches_to_Show_less() => Output_over_thirty_lines_shows_its_first_thirty_and_a_link_that_shows_all_of_it_then_Show_less();
    [Fact]
    [Trait("Req", "UI-CHAT-027")] // App/Views/Chat/TimelineRows.swift:2120-2126
    public void Detail_UI_is_secondary_monospace_with_twelve_display_lines() => Thirty_lines_show_whole_with_no_link_and_the_detail_is_capped_at_twelve_lines_in_secondary_monospace();
    [Fact]
    [Trait("Req", "UI-CHAT-028")] // App/Views/Chat/TimelineRows.swift:2127-2140
    public void Diff_UI_has_the_path_insets_continuous_ten_point_plate_and_no_line_numbers() => Each_edit_with_a_diff_shows_its_path_above_its_lines_on_a_rounded_plate();
    [Fact]
    [Trait("Req", "UI-CHAT-029")] // App/Views/Chat/TimelineRows.swift:2263-2337
    public void Plan_badge_UI_keeps_all_four_states_and_the_inner_14_point_spinner() { A_plans_badge_title_caption_and_attention_follow_its_state(); A_plan_with_empty_markdown_has_no_chevron_and_cannot_be_opened(); }
    [Fact]
    [Trait("Req", "UI-CHAT-030")] // App/Views/Chat/TimelineRows.swift:2279-2313
    public void Plan_heading_is_chat_headline_semibold_not_regular()
    {
        headless.OnUiThread(() => { using var h = RuntimeHarness(); h.Main.OnMain(() => {
            var entry = PlanEntry(ProposedPlan.State.Proposed);
            var plan = ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value;
            var detail = Assert.IsAssignableFrom<Control>(Invoke(new PlanCard(entry, h.Runtime), "Detail", plan));
            Assert.Equal(FontWeight.SemiBold, TextNamed(detail, "Plan").FontWeight); // ChatFont.swift:7; ED-186.
        }); });
        The_plan_popover_is_520_wide_with_the_markdown_in_a_420_point_scroller_and_the_two_buttons_only_while_proposed();
    }
    [Fact]
    [Trait("Req", "UI-CHAT-031")] // App/Views/Chat/TimelineRows.swift:2296-2300
    public void Implement_plan_UI_closes_first_and_remains_greyed_while_busy() { Dismiss_closes_the_popover_and_dismisses_the_plan_and_Implement_plan_closes_it_and_implements(); The_plan_buttons_stay_greyed_while_the_thread_is_busy_with_the_reason_and_carry_no_tip_when_free(); }
    [Fact]
    [Trait("Req", "UI-CHAT-032")] // App/Views/Chat/TimelineRows.swift:2301-2305
    public void Dismiss_UI_uses_the_nondestructive_glass_action() => Dismiss_closes_the_popover_and_dismisses_the_plan_and_Implement_plan_closes_it_and_implements();
    [Fact]
    [Trait("Req", "UI-CHAT-033")] // App/Views/Chat/TimelineRows.swift:2343-2382
    public void Checklist_UI_keeps_status_glyphs_strikes_and_identity() { A_checklist_is_a_card_headed_N_of_M_done_with_a_glyph_and_line_per_step(); A_checklist_with_no_steps_draws_nothing_and_an_update_rewrites_the_same_rows_in_place(); }
    [Fact]
    [Trait("Req", "UI-CHAT-034")] // App/Views/Chat/TimelineRows.swift:2385-2431
    public void Notice_UI_uses_theme_status_colours_not_system_red_and_orange() { An_info_notice_is_a_bare_line_of_secondary_selectable_text(); A_warning_or_error_notice_sits_in_a_full_width_pill_tinted_at_ten_percent_of_its_theme_status_colour(); }
    [Fact]
    [Trait("Req", "UI-CHAT-035")] // App/Views/Chat/TimelineRows.swift:2433-2456
    public void Standalone_duration_UI_is_absent_after_a_reply() => The_standalone_turn_end_line_shows_only_when_the_turn_produced_no_reply();
    [Fact]
    [Trait("Req", "UI-CHAT-036")] // App/Views/Chat/TimelineRows.swift:2598-2701
    public void Folded_header_UI_has_the_rule_and_both_body_branches_have_identity_transition() { A_finished_turn_opens_folded_to_its_prompt_a_summary_line_a_rule_and_its_answer(); Both_finished_turn_body_branches_remain_opaque_and_leave_immediately_under_the_fold_animation(); }
    [Fact]
    [Trait("Req", "UI-CHAT-037")] // App/Views/Chat/TimelineRows.swift:2735-2827
    public void File_card_UI_hugs_its_content_with_a_twenty_point_control_gap() => The_round_controls_follow_a_20_point_gap_revert_only_when_undoable_on_a_hugging_quaternary_pill();
    [Fact]
    [Trait("Req", "UI-CHAT-038")] // App/Views/Chat/TimelineRows.swift:2770-2799, :2463-2478
    public void Revert_disc_UI_is_a_flat_28_point_overlay_control() => A_muted_round_glyph_is_a_28_point_overlay_disc_whose_glyph_and_fill_come_up_under_the_pointer();

    [Fact]
    [Trait("Req", "PR-CHAT-154")] // App/Views/Chat/TimelineRows.swift:2008-2064
    public void Head_steps_keep_every_tool_notice_and_nonblank_reply_and_only_briefs_after_the_first()
    {
        using var actor = new TestRuntime();
        actor.OnMain(() =>
        {
            var first = UserEntry(" ", null);
            var brief = UserEntry("steer it", null);
            var reply = ReplyEntry("answer");
            var tool = ToolEntry(Call(ToolCall.Kind.Read, "a"));
            var notice = NoticeEntry(Notice.Level.Warning, "careful");
            var all = new[] { first, UserEntry("\n ", null), brief, ReplyEntry("\t"), reply, tool, notice, PlanEntry(ProposedPlan.State.Proposed) };
            Assert.Equal(new[] { brief, reply, tool, notice }, Assert.IsType<ValueList<TimelineEntry>>(InvokeStatic(typeof(HydraHeadStepsPopover), "Steps", (object)all)));
            var start = System.DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
            var head = Head(0, HydraHeadInfo.Status.Running, start) with { ToolCalls = 1 };
            Assert.Equal("Working\u2026 \u00b7 1 tool", InvokeStatic(typeof(HydraHeadStepsPopover), "StatusLine", head));
            Assert.Equal("Failed \u00b7 2 tools \u00b7 1m 4s", InvokeStatic(typeof(HydraHeadStepsPopover), "StatusLine", head with { HydraHeadInfoStatus = HydraHeadInfo.Status.Failed, ToolCalls = 2, FinishedAt = start.AddSeconds(64) }));
            Assert.Equal("Stopped", InvokeStatic(typeof(HydraHeadStepsPopover), "StatusLine", head with { HydraHeadInfoStatus = HydraHeadInfo.Status.Stopped, ToolCalls = 0 }));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-154")] // App/Views/Chat/TimelineRows.swift:1884-2006
    public void Head_steps_mount_at_440_with_a_520_cap_and_update_from_the_heads_own_runtime()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var (store, app, lead) = TeamHarness(Head(0, HydraHeadInfo.Status.Completed) with { Summary = "report text" });
            using (store)
            {
            ThreadRuntime runtime = null!;
            HydraHeadStepsPopover view = null!;
            AvaloniaWindow window = null!;
            store.Main.OnMain(() =>
            {
                var id = app.Threads.Single(thread => thread.IsHydraHead).Id;
                runtime = app.Runtime(id);
                view = new HydraHeadStepsPopover(id);
                var host = new Border { Child = view };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                window = Show(host);
                Assert.Equal(440, ((Control)view).Width);
                Assert.Contains("Steps", Texts(view));
                Assert.Contains("No steps yet.", Texts(view));
                Assert.Contains("Report", Texts(view));
                Assert.Contains("report text", Texts(view));
                Assert.Contains(LogicalQuery.SelfAndDescendants<Decorator>(view), d => d.Padding == new Thickness(16));
                Assert.Equal(22, Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(view)).Size);
                var scroll = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverScroll>(view));
                Assert.True(Assert.IsType<ScrollViewer>(scroll.Child).Height <= 520);
            });
            store.Main.OnMain(() =>
            {
                runtime.Rehearse(new ProviderEvent.ToolStarted("head-read", Call(ToolCall.Kind.Read, "Read head", ToolCall.Status.Running)));
            });
            store.Main.OnMain(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Settle(window);
                Assert.Contains("Reading head", Texts(view));
                Assert.DoesNotContain("No steps yet.", Texts(view));
                var label = TextNamed(view, "Reading head");
                Assert.Equal(2, label.MaxLines);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
                Close(window);
            });
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-155")] // App/Views/Chat/TimelineRows.swift:1564-1577, :2682-2686
    public void Heads_list_is_an_unfolded_leading_stack_with_eight_point_spacing_and_no_header()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Agent, "Hank: first")), ToolEntry(Call(ToolCall.Kind.Agent, "Walter: second"))]);
                var view = new HydraHeadsList(entries, h.Runtime);
                var window = Show(view);
                var stack = Assert.IsType<StackPanel>(view.Child);
                Assert.Equal(8, stack.Spacing);
                Assert.Equal(HorizontalAlignment.Left, stack.HorizontalAlignment);
                Assert.Equal(2, stack.Children.Count);
                Assert.All(stack.Children, child => Assert.IsType<ToolRow>(child));
                Assert.Contains("Sent out Hank: first", Texts(view));
                Assert.DoesNotContain("chevron.right", Symbols(view));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-161")] // App/Views/Chat/TimelineRows.swift:1406-1407, :56-62
    public void A_menu_can_outlive_its_message_row_without_retaining_the_row_or_entry()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                (System.Collections.Generic.IReadOnlyList<RowAction> Menu, System.WeakReference Row, System.WeakReference Entry) Create()
                {
                    var entry = ReplyEntry("released message");
                    var row = new AssistantMessageRow(entry, null, h.Runtime);
                    var menu = AssistantMessageRow.MessageActions("released message", null, h.Runtime);
                    return (menu, new System.WeakReference(row), new System.WeakReference(entry));
                }
                var kept = Create();
                System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
                Assert.False(kept.Row.IsAlive);
                Assert.False(kept.Entry.IsAlive);
                Assert.Equal(new[] { "Copy message", "Copy as markdown", "Add to reply" }, kept.Menu.Select(action => action.Title));
                System.GC.KeepAlive(kept.Menu);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-166")] // App/Views/Chat/TimelineRows.swift:633, :2394, :2152
    public void Selection_is_owned_by_each_notice_report_and_output_block()
    {
        An_info_notice_is_a_bare_line_of_secondary_selectable_text();
        Output_over_thirty_lines_shows_its_first_thirty_and_a_link_that_shows_all_of_it_then_Show_less();
        The_report_popover_is_460_wide_with_a_460_point_scroller_at_the_callout_style_with_12_point_markdown();
    }

    [Fact]
    [Trait("Req", "PR-CHAT-170")] // App/Views/Chat/TimelineRows.swift:2242-2254
    public void Chat_MCP_contact_is_a_catalog_read_not_a_deletion_path() => An_mcp_title_of_catalog_server_middle_dot_tool_resolves_to_the_entry_and_its_label();

    [Fact]
    [Trait("Req", "PR-COMPOSER-211")] // App/Views/Chat/TimelineRows.swift:2117-2119
    public void Every_tool_kind_can_mount_the_resolved_image_above_its_detail()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var path = System.IO.Path.Combine(h.Project.Path, "screenshot.png");
                System.IO.File.WriteAllBytes(path, []);
                foreach (var kind in new[] { ToolCall.Kind.Read, ToolCall.Kind.Command })
                {
                    var detail = new ToolDetailView(Call(kind, path) with { Detail = "image detail" }, h.Project.Path);
                    Assert.Single(LogicalQuery.SelfAndDescendants<ToolImagePreview>(detail));
                    Assert.Equal(new[] { typeof(ToolImagePreview), typeof(SelectableTextBlock) }, Assert.IsType<StackPanel>(detail.Child).Children.Select(child => child.GetType()));
                }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-GIT-122")] // App/Views/Chat/TimelineRows.swift:2695, :2770-2799
    public void File_revert_has_one_confirmation_with_only_Cancel_and_Revert() => The_revert_disc_asks_in_a_300_point_popover_where_only_Revert_reverts();
    [Fact]
    [Trait("Req", "PR-GIT-123")] // App/Views/Chat/TimelineRows.swift:184-206
    public void Message_revert_posts_the_intent_before_the_STOP_2_dialog() => Edit_from_here_posts_only_the_confirmation_intent_and_a_non_revertable_menu_cannot_post_it();
    [Fact]
    [Trait("Req", "PR-GIT-151")] // App/Views/Chat/TimelineRows.swift:2707-2714, :2739-2740
    public void Legacy_changes_and_singular_file_titles_follow_the_source() { An_older_turn_with_files_but_no_per_file_summary_works_its_changes_out_and_stores_them_while_one_with_none_never_does(); The_file_card_reads_Edited_1_file_or_Edited_N_files_beside_the_badge_and_the_totals_with_a_chevron(); }
    [Fact]
    [Trait("Req", "PR-GIT-152")] // App/Views/Chat/TimelineRows.swift:2829-2857
    public void File_list_is_twelve_then_the_literal_overflow_line() => The_files_popover_lists_twelve_files_then_and_1_more_file_or_and_N_more_files();
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-088")] // App/Views/Chat/TimelineRows.swift:802-808
    public void Merge_outcome_splits_once_drops_one_stop_and_keeps_the_leading_URL_separate()
    {
        var value = new HydraMergeOutcome(new UserMessage { Text = "Hydra merged..\n HTTPS://example.org/a \n details " });
        Assert.Equal("Hydra merged.", value.Title); Assert.Equal("HTTPS://example.org/a \n details", value.Body);
        Assert.NotNull(value.Link); Assert.Equal("details", value.Details);
        Assert.Equal("Hydra", new HydraMergeOutcome(new UserMessage { Text = "" }).Title);
    }
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // App/Views/Chat/TimelineRows.swift:937-952
    public void Silent_merge_completion_removes_the_pill() => The_merge_pill_shows_the_stage_under_way_with_an_ellipsis_and_goes_once_the_merge_is_over_without_a_note();
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-090")] // App/Views/Chat/TimelineRows.swift:1111-1120
    public void Generic_merge_report_uses_the_460_point_fallback() => The_report_popover_is_460_wide_with_a_460_point_scroller_at_the_callout_style_with_12_point_markdown();
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-101")] // App/Views/Chat/TimelineRows.swift:954-958, :1056-1079
    public void Stage_words_have_one_ellipsis_and_no_clock() => The_merge_pill_shows_the_stage_under_way_with_an_ellipsis_and_goes_once_the_merge_is_over_without_a_note();
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-102")] // App/Views/Chat/TimelineRows.swift:963-965, :821-841
    public void Stage_track_has_seven_source_stages() { Assert.Equal(7, HydraMergeStageExtensions.AllCases.Count); The_track_is_drawn_from_its_three_values_dots_lit_as_the_head_reaches_them_and_the_head_arcing_between_dots(); }
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-103")] // App/Views/Chat/TimelineRows.swift:844-915
    public void Track_geometry_is_one_three_value_drawing() => The_track_is_drawn_from_its_three_values_dots_lit_as_the_head_reaches_them_and_the_head_arcing_between_dots();
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-104")] // App/Views/Chat/TimelineRows.swift:1020-1027
    public void Failure_outcome_has_no_success_flourish() => An_outcome_that_is_not_a_merge_settles_at_once_with_no_flourish_and_an_outcome_with_nothing_behind_it_is_disabled();
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-105")] // App/Views/Chat/TimelineRows.swift:1095-1128
    public void Outcome_title_without_details_is_disabled_and_has_no_chevron() => An_outcome_that_is_not_a_merge_settles_at_once_with_no_flourish_and_an_outcome_with_nothing_behind_it_is_disabled();
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-106")] // App/Views/Chat/TimelineRows.swift:1012-1019
    public void Merge_pill_uses_the_shared_insets_and_accessibility_title() => The_merge_pill_shows_the_stage_under_way_with_an_ellipsis_and_goes_once_the_merge_is_over_without_a_note();
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-107")] // App/Views/Chat/TimelineRows.swift:972-1010
    public void Morph_slot_is_clipped_with_negative_returned_padding()
    {
        headless.OnUiThread(() => { using var h = RuntimeHarness(); using var frames = FrameClock.Source.Override(new ManualFrameSource()); h.Main.OnMain(() => {
            h.Runtime.IsHydraMerging = true;
            var row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging()); var window = Show(row);
            Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(row), c => c.ClipToBounds && c.Margin == new Thickness(-2, -6));
            Close(window);
        }); });
    }
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-108")] // App/Views/Chat/TimelineRows.swift:1028-1033
    public void Merge_report_is_a_task_keyed_to_its_body_not_the_stage() => A_reports_markdown_is_warmed_as_the_pill_appears_per_head_for_a_batch();
    [Fact]
    [Trait("Req", "UI-CHAT-007")] // App/Views/Chat/TimelineRows.swift:88-129, :1167-1207
    public void Sent_bubble_uses_the_exact_path_and_insets() { The_bubble_path_rounds_three_corners_and_draws_the_tail_through_the_exact_control_points(); A_sent_message_is_a_trailing_bubble_with_14_18_and_9_point_insets_96_points_clear_on_the_leading_side_tinted_at_14_percent(); }
    [Fact]
    [Trait("Req", "UI-CHAT-008")] // App/Views/Chat/TimelineRows.swift:92-117
    public void Quote_chip_input_is_leading_only_while_the_ChipChrome_site_is_STOP_1() => The_inherited_quote_reader_consumes_only_leading_blocks_and_rejoins_the_lifted_command();
    [Fact]
    [Trait("Req", "UI-CHAT-010")] // App/Views/Chat/TimelineRows.swift:130-161
    public void Pencil_is_22_square_on_a_reserved_line() => A_revertable_message_keeps_a_22_point_hover_line_with_a_22_point_pencil_that_shows_only_on_hover();

    private static Attachment Photo(string path) => new() { Name = "photo.png", Path = path, MimeType = "image/png" };

    private static AvaloniaWindow Show(Control control)
    {
        var window = new AvaloniaWindow { Content = control, Width = 800, Height = 600 };
        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static Border Plate(Control thumbnail) =>
        LogicalQuery.SelfAndDescendants<Border>(thumbnail).First(border => border.ClipToBounds);

    private static void OnMain(HeadlessSession headless, System.Action body) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(body);
    });

    // A real offline ThreadRuntime inside a headless UI test (FollowUpEditorTests' note): the login environment is
    // loaded once on a pool thread first, so the harness's own Load().Wait() on the UI thread returns at once.
    private static ThreadRuntimePart12Harness RuntimeHarness()
    {
        System.Threading.Tasks.Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
        return new ThreadRuntimePart12Harness();
    }

    private static string[] Texts(Control control) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    private static string[] Symbols(Control control) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(control).Select(image => image.SymbolName ?? "").ToArray();

    private static Color Resolved(SwiftColor color, Control on) => color.Resolve(on.ActualThemeVariant);

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static ToolCall Call(ToolCall.Kind kind, string title, ToolCall.Status status = ToolCall.Status.Completed) =>
        new() { ToolCallKind = kind, Title = title, ToolCallStatus = status };

    private static TimelineEntry ToolEntry(ToolCall call, System.Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Tool(call)));

    private static TimelineEntry ReplyEntry(string text, bool streaming = false, System.Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = streaming })));

    private static TimelineEntry UserEntry(string text, System.Guid? turn, ValueList<Attachment>? attachments = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = text, Attachments = attachments ?? ValueList<Attachment>.Empty })));

    private static TurnSummary Summary(System.Guid turn, double seconds = 5) => new()
    {
        TurnID = turn,
        Status = TurnStatus.Completed,
        Duration = seconds,
        FilesChanged = 0,
        Additions = 0,
        Deletions = 0,
    };

    private static void Hover(AvaloniaWindow window, Control control)
    {
        // The circular source hit shapes exclude the top-left corner. Enter the actual control's centre.
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        window.MouseMove(point, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // Drains what the test posted to Avalonia and closes its window while the test's main actor is still bound, so no
    // layout or input job is left for the headless session's reset.
    private static void Close(AvaloniaWindow window)
    {
        Settle(window);
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // Code-only markdown: renders through MarkdownBlockView, so these tests do not depend on the sibling
    // ProseRunView unit, which is still MARROW_STUB.
    private const string CodeOnlyReply = "```\nlet x = 1\n```";

    // ---------------------------------------------------------------- AttachmentStrip / AttachmentThumbnail

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1237, :1292-1304
    public void A_photo_thumbnail_is_a_56_point_plate_rounded_12_and_clipped()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewSlot());
            var window = Show(thumbnail);
            var plate = Plate(thumbnail);
            Assert.Equal(56, plate.Width);
            Assert.Equal(56, plate.Height);
            Assert.Equal(new CornerRadius(12), plate.CornerRadius);
            Assert.True(plate.ClipToBounds);
            Close(window);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1237, :1256-1262
    public void A_caller_size_sizes_the_plate()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewCoordinator(), size: 48);
            var window = Show(thumbnail);
            Assert.Equal(48, Plate(thumbnail).Width);
            Assert.Equal(48, Plate(thumbnail).Height);
            Close(window);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1307-1323
    public void A_file_that_is_neither_photo_nor_movie_shows_its_icon_inset_by_eight_hundredths_and_is_named()
    {
        OnMain(headless, () =>
        {
            var file = new Attachment { Name = "notes.txt", Path = @"C:\no-such-folder\notes.txt", MimeType = "text/plain" };
            var thumbnail = new AttachmentThumbnail(file, new AttachmentPreviewSlot());
            var window = Show(thumbnail);
            var plate = Plate(thumbnail);
            var icon = Assert.Single(LogicalQuery.SelfAndDescendants<Image>(plate));
            Assert.Equal(new Thickness(56 * 0.08), icon.Margin);
            Assert.Equal(Stretch.Uniform, icon.Stretch);
            Assert.Equal("notes.txt", AutomationProperties.GetName(plate));
            Close(window);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1305-1306
    public void A_movie_uses_the_video_thumbnail()
    {
        OnMain(headless, () =>
        {
            var movie = new Attachment { Name = "clip.mp4", Path = @"C:\no-such-folder\clip.mp4", MimeType = "video/mp4" };
            var thumbnail = new AttachmentThumbnail(movie, new AttachmentPreviewSlot());
            Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentVideoThumbnail>(thumbnail));
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1270-1271, :1295-1301
    public void A_photo_already_in_memory_starts_out_drawn_and_a_missing_one_leaves_the_plate_empty()
    {
        OnMain(headless, () =>
        {
            var drawnPath = @"C:\no-such-folder\seen.png";
            ThumbnailMemory.Store(new DecodedBitmap(2, 2, 8, new byte[16]), drawnPath, 56);
            var seen = new AttachmentThumbnail(Photo(drawnPath), new AttachmentPreviewSlot());
            Assert.Single(LogicalQuery.SelfAndDescendants<Image>(Plate(seen)));

            var missing = new AttachmentThumbnail(Photo(@"C:\no-such-folder\never-seen.png"), new AttachmentPreviewSlot());
            var window = Show(missing);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Image>(Plate(missing)));
            Close(window);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1327
    public void A_thumbnail_button_never_takes_focus()
    {
        OnMain(headless, () =>
        {
            var thumbnail = new AttachmentThumbnail(Photo(@"C:\no-such-folder\photo.png"), new AttachmentPreviewSlot());
            Assert.False(LogicalQuery.SelfAndDescendants<Button>(thumbnail).Single().Focusable);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-009")] // App/Views/Chat/TimelineRows.swift:1222-1225
    public void The_strip_lays_thumbnails_6_apart_each_with_its_name_as_tooltip()
    {
        OnMain(headless, () =>
        {
            var a = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
            var b = new Attachment { Name = "b.txt", Path = @"C:\no-such-folder\b.txt", MimeType = "text/plain" };
            var strip = new AttachmentStrip(new ValueList<Attachment>([a, b]));
            var window = Show(strip);
            var row = LogicalQuery.SelfAndDescendants<StackPanel>(strip).First();
            Assert.Equal(6, row.Spacing);
            var thumbnails = LogicalQuery.SelfAndDescendants<AttachmentThumbnail>(strip).ToList();
            Assert.Equal(2, thumbnails.Count);
            Assert.Equal("a.txt", ToolTip.GetTip(thumbnails[0]));
            Assert.Equal("b.txt", ToolTip.GetTip(thumbnails[1]));
            Close(window);
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-020 TimelineMetrics

    [Fact]
    [Trait("Req", "PR-CHAT-020")] // App/Views/Chat/TimelineRows.swift:8-28
    public void The_timeline_geometry_is_the_eight_named_constants()
    {
        Assert.Equal(20, TimelineMetrics.RowSpacing);
        Assert.Equal(22, TimelineMetrics.HoverLineHeight);
        Assert.Equal(16, TimelineMetrics.IconWidth);
        Assert.Equal(8, TimelineMetrics.IconSpacing);
        Assert.Equal(12, TimelineMetrics.PillLeading);
        Assert.Equal(14, TimelineMetrics.PillTrailing);
        Assert.Equal(8, TimelineMetrics.PillVertical);
        Assert.Equal(18, TimelineMetrics.PillRadius);
    }

    // ---------------------------------------------------------------- PR-CHAT-036 finishedHeads

    private static ChatThread HeadThread(Project project, HydraHeadInfo head) => AppModelSamples.Thread(project) with { Hydra = head };

    private static HydraHeadInfo Head(long index, HydraHeadInfo.Status status, System.DateTimeOffset startedAt, System.DateTimeOffset? finishedAt = null, System.Guid? batch = null) =>
        new HydraHeadInfo(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with
        {
            HydraHeadInfoStatus = status,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            BatchID = batch,
        };

    [Fact]
    [Trait("Req", "PR-CHAT-036")] // App/Views/Chat/TimelineRows.swift:423-426
    public void With_no_running_head_no_finished_head_is_shown()
    {
        using var runtime = new TestRuntime(); // ChatThread.init reads the defaults store
        var project = AppModelSamples.Project("p", @"C:\no-such-folder\p");
        var t0 = System.DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var threads = new[]
        {
            HeadThread(project, Head(1, HydraHeadInfo.Status.Completed, t0, t0.AddSeconds(5))),
            HeadThread(project, Head(2, HydraHeadInfo.Status.Failed, t0, t0.AddSeconds(6))),
        };
        Assert.Empty(HydraHeadsWorkingRow.FinishedHeads(threads));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-036")] // App/Views/Chat/TimelineRows.swift:428-432
    public void A_finished_head_counts_when_its_batch_still_has_a_head_at_work_whatever_its_time()
    {
        using var runtime = new TestRuntime(); // ChatThread.init reads the defaults store
        var project = AppModelSamples.Project("p", @"C:\no-such-folder\p");
        var t0 = System.DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var batch = System.Guid.NewGuid();
        var otherBatch = System.Guid.NewGuid();
        var sameBatchEarly = Head(1, HydraHeadInfo.Status.Completed, t0.AddSeconds(-100), t0.AddSeconds(-50), batch);
        var otherBatchLate = Head(2, HydraHeadInfo.Status.Completed, t0, t0.AddSeconds(50), otherBatch);
        var running = Head(3, HydraHeadInfo.Status.Running, t0, batch: batch);
        var threads = new[] { HeadThread(project, sameBatchEarly), HeadThread(project, otherBatchLate), HeadThread(project, running) };
        var shown = HydraHeadsWorkingRow.FinishedHeads(threads);
        Assert.Equal([1L], shown.Select(head => head.Index));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-036")] // App/Views/Chat/TimelineRows.swift:427, :430
    public void A_head_with_no_batch_counts_when_it_finished_at_or_after_the_earliest_running_start_in_the_callers_order()
    {
        using var runtime = new TestRuntime(); // ChatThread.init reads the defaults store
        var project = AppModelSamples.Project("p", @"C:\no-such-folder\p");
        var t0 = System.DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var threads = new[]
        {
            HeadThread(project, Head(1, HydraHeadInfo.Status.Stopped, t0, t0.AddSeconds(30))),
            HeadThread(project, Head(2, HydraHeadInfo.Status.Running, t0.AddSeconds(20))),
            HeadThread(project, Head(3, HydraHeadInfo.Status.Completed, t0, t0.AddSeconds(10))),
            HeadThread(project, Head(4, HydraHeadInfo.Status.Running, t0.AddSeconds(10))),
            HeadThread(project, Head(5, HydraHeadInfo.Status.Failed, t0, t0.AddSeconds(9))),
            HeadThread(project, Head(6, HydraHeadInfo.Status.Completed, t0, null)),
        };
        // since = the earliest running start (t0 + 10); 1 (t0 + 30) and 3 (exactly t0 + 10) count, 5 (t0 + 9) and 6
        // (no finish time, so distantPast) do not; the running heads themselves never count.
        Assert.Equal([1L, 3L], HydraHeadsWorkingRow.FinishedHeads(threads).Select(head => head.Index));
    }

    // ---------------------------------------------------------------- PR-CHAT-077 UserBubble

    [Fact]
    [Trait("Req", "PR-CHAT-077")] // App/Views/Chat/TimelineRows.swift:1167-1206
    public void The_bubble_path_rounds_three_corners_and_draws_the_tail_through_the_exact_control_points()
    {
        var geometry = Assert.IsType<PathGeometry>(new UserBubble().Path(new Rect(0, 0, 200, 50)));
        var figure = Assert.Single(geometry.Figures!);
        Assert.True(figure.IsClosed);
        Assert.True(figure.IsFilled);
        Assert.Equal(new Point(18, 0), figure.StartPoint);
        var segments = figure.Segments!.ToList();
        // :1204 closeSubpath(): the figure is closed; the last arc already ends on the start point.
        Assert.Equal(10, segments.Count);
        Assert.Equal(new Point(178, 0), Assert.IsType<LineSegment>(segments[0]).Point);
        var topRight = Assert.IsType<ArcSegment>(segments[1]);
        Assert.Equal(new Point(196, 18), topRight.Point);
        Assert.Equal(new Size(18, 18), topRight.Size);
        Assert.Equal(SweepDirection.Clockwise, topRight.SweepDirection);
        Assert.False(topRight.IsLargeArc);
        Assert.Equal(new Point(196, 39), Assert.IsType<LineSegment>(segments[2]).Point);
        var tip = Assert.IsType<BezierSegment>(segments[3]);
        Assert.Equal((new Point(196, 49), new Point(200, 50), new Point(200, 50)), (tip.Point1, tip.Point2, tip.Point3));
        var curl = Assert.IsType<BezierSegment>(segments[4]);
        Assert.Equal((new Point(196, 50.5), new Point(192, 49), new Point(189, 46)), (curl.Point1, curl.Point2, curl.Point3));
        var ease = Assert.IsType<BezierSegment>(segments[5]);
        Assert.Equal((new Point(184, 50), new Point(180, 50), new Point(175, 50)), (ease.Point1, ease.Point2, ease.Point3));
        Assert.Equal(new Point(18, 50), Assert.IsType<LineSegment>(segments[6]).Point);
        var bottomLeft = Assert.IsType<ArcSegment>(segments[7]);
        Assert.Equal(new Point(0, 32), bottomLeft.Point);
        Assert.Equal(SweepDirection.Clockwise, bottomLeft.SweepDirection);
        Assert.Equal(new Point(0, 18), Assert.IsType<LineSegment>(segments[8]).Point);
        var topLeft = Assert.IsType<ArcSegment>(segments[9]);
        Assert.Equal(new Point(18, 0), topLeft.Point);
        Assert.Equal(SweepDirection.Clockwise, topLeft.SweepDirection);
        Assert.Equal(18, UserBubble.CornerRadius);
        Assert.Equal(4, UserBubble.Tail);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-077")] // App/Views/Chat/TimelineRows.swift:118-129, :163-164
    public void A_sent_message_is_a_trailing_bubble_with_14_18_and_9_point_insets_96_points_clear_on_the_leading_side_tinted_at_14_percent()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var row = new UserMessageRow(UserEntry("hello there", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var window = Show(row);
                var text = Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row));
                Assert.Equal("hello there", text.Source);
                Assert.True(text.HugsContent);
                var inset = Assert.IsType<Border>(text.Parent);
                Assert.Equal(new Thickness(14, 9, 14 + UserBubble.Tail, 9), inset.Padding);
                var shape = Assert.Single(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(row));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.14), shape), BrushColor(shape.Fill));
                var outer = LogicalQuery.SelfAndDescendants<Border>(row).First(border => border.Padding.Left == 96);
                Assert.Equal(-(TimelineMetrics.HoverLineHeight + 6), outer.Margin.Bottom);
                var column = LogicalQuery.SelfAndDescendants<StackPanel>(outer).First(stack => stack.Spacing == 6);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, column.HorizontalAlignment);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-078 quotes

    [Fact]
    [Trait("Req", "PR-CHAT-078")] // App/Views/Chat/TimelineRows.swift:92-93, :118-122; App/Runtime/ThreadRuntime.swift:71-99
    public void A_message_whose_quote_lines_are_not_at_the_start_shows_no_chips_and_keeps_them_in_the_bubble()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                const string message = "look at this\n> not a leading quote";
                var row = new UserMessageRow(UserEntry(message, System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var window = Show(row);
                Assert.DoesNotContain("text.quote", Symbols(row));
                Assert.Equal(message, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row)).Source);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-079 attachments over the bubble

    [Fact]
    [Trait("Req", "PR-CHAT-079")] // App/Views/Chat/TimelineRows.swift:88-91
    public void A_message_with_attachments_shows_the_strip_above_the_bubble_and_one_without_shows_none()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var file = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
                var with = new UserMessageRow(UserEntry("see file", System.Guid.NewGuid(), new ValueList<Attachment>([file])), h.Runtime, canRevert: false);
                var without = new UserMessageRow(UserEntry("no file", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var panel = new StackPanel();
                panel.Children.Add(with);
                panel.Children.Add(without);
                var window = Show(panel);
                var column = LogicalQuery.SelfAndDescendants<StackPanel>(with).First(stack => stack.Spacing == 6);
                var strip = Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentStrip>(with));
                var bubbleText = Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(with));
                var stripIndex = column.Children.IndexOf(column.Children.First(child => LogicalQuery.SelfAndDescendants<AttachmentStrip>(child).Contains(strip)));
                var bubbleIndex = column.Children.IndexOf(column.Children.First(child => LogicalQuery.SelfAndDescendants<InlineText>(child).Contains(bubbleText)));
                Assert.True(stripIndex < bubbleIndex);
                Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, column.Children[stripIndex].HorizontalAlignment);
                Assert.Empty(LogicalQuery.SelfAndDescendants<AttachmentStrip>(without));
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-080 the pencil

    private static Control HoverLine(Control row) =>
        LogicalQuery.SelfAndDescendants<Control>(row).Single(control => control.Height == TimelineMetrics.HoverLineHeight && double.IsNaN(control.Width));

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // App/Views/Chat/TimelineRows.swift:130-142, :158-161
    public void A_revertable_message_keeps_a_22_point_hover_line_with_a_22_point_pencil_that_shows_only_on_hover()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var row = new UserMessageRow(UserEntry("edit me", System.Guid.NewGuid()), h.Runtime, canRevert: true);
                var window = Show(row);
                var line = HoverLine(row);
                Assert.Equal(0, line.Opacity);
                var pencil = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(line));
                Assert.Equal("Edit message", ToolTip.GetTip(pencil));
                Assert.Contains("pencil", Symbols(pencil));
                Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(pencil), control => control.Width == 22 && control.Height == 22);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // App/Views/Chat/TimelineRows.swift:131, :158-161
    public void Hovering_the_row_shows_the_line_and_a_message_that_cannot_revert_has_no_pencil()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            UserMessageRow? row = null;
            UserMessageRow? locked = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                row = new UserMessageRow(UserEntry("edit me", System.Guid.NewGuid()), h.Runtime, canRevert: true);
                locked = new UserMessageRow(UserEntry("keep me", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var noTurn = new UserMessageRow(UserEntry("no turn", null), h.Runtime, canRevert: true);
                var panel = new StackPanel { Spacing = 60 };
                panel.Children.Add(row);
                panel.Children.Add(locked);
                panel.Children.Add(noTurn);
                window = Show(panel);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(HoverLine(locked)));
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(HoverLine(noTurn)));
                Hover(window, LogicalQuery.SelfAndDescendants<InlineText>(row).Single());
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal(1, HoverLine(row!).Opacity);
                Assert.Equal(0, HoverLine(locked!).Opacity);
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // App/Views/Chat/TimelineRows.swift:87, :132-134, :141
    public void The_pencil_opens_the_edit_on_the_runtime_and_a_second_tap_closes_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var turn = System.Guid.NewGuid();
            var file = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
            UserMessageRow? row = null;
            AvaloniaWindow? window = null;
            // The thread's history lands first: installing it drops an edit whose turn it does not hold
            // (ThreadRuntime.swift:172-182), which is the runtime's rule, not the row's.
            h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
            h.Main.OnMain(() =>
            {
                row = new UserMessageRow(UserEntry("edit me", turn, new ValueList<Attachment>([file])), h.Runtime, canRevert: true);
                window = Show(row);
                Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(HoverLine(row))));
                var edit = Assert.IsType<PromptEdit>(h.Runtime.MessageEdit);
                Assert.Equal(turn, edit.Id);
                Assert.Equal("edit me", edit.Text);
                Assert.Equal([file], edit.Attachments);
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                // :197, :208-212 the edit opening syncs the editor popover, which presents a MessageEditor on the edit,
                // hung from the pencil's anchor (:148-155).
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                Assert.IsType<MessageEditor>(shown.Popup.Child);
                var pencil = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(HoverLine(row!)));
                Assert.Same(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(pencil.Parent!).Single(), shown.Popup.PlacementTarget);
                // Editing: the line stays up without a hover.
                Assert.Equal(1, HoverLine(row!).Opacity);
                Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(HoverLine(row!))));
                Assert.Null(h.Runtime.MessageEdit);
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal(0, HoverLine(row!).Opacity);
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-080")] // App/Views/Chat/TimelineRows.swift:143-147
    public void Settling_the_pointer_on_the_pencil_starts_the_revert_preview()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var turn = System.Guid.NewGuid();
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                var row = new UserMessageRow(UserEntry("edit me", turn), h.Runtime, canRevert: true);
                window = Show(row);
                Assert.Null(h.Runtime.CachedRevertPreview(turn));
                var pencil = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(HoverLine(row)));
                pencil.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, pencil, new Pointer(1, PointerType.Mouse, true), window, new Point(1, 1), 0, new PointerPointProperties(), KeyModifiers.None));
            });
            h.Main.OnMain(() =>
            {
                // The preview of an unknown turn fails at once (ThreadRuntime.swift:1805-1807); either way it was asked for.
                Assert.NotNull(h.Runtime.CachedRevertPreview(turn));
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-081 the sent message's menu

    private static (Popup Popup, RowActionItems Items) RightClickMenu(RecordingPresenter presenter, AvaloniaWindow window, Control on)
    {
        var point = on.TranslatePoint(new Point(4, 4), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var popup = presenter.Calls.Last(call => call.Presented).Popup;
        Assert.NotNull(popup.Child);
        return (popup, Assert.Single(LogicalQuery.SelfAndDescendants<RowActionItems>(popup.Child)));
    }

    // A PopoverItem's title is a MiddleTruncatingText, whose Text is set only when measured; its FullText is the title.
    private static string Title(PopoverItem item) => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item).First().FullText;

    private static string[] ItemTitles(RowActionItems items) =>
        LogicalQuery.SelfAndDescendants<PopoverItem>(items).Select(Title).ToArray();

    [Fact]
    [Trait("Req", "PR-CHAT-081")] // App/Views/Chat/TimelineRows.swift:172-178, :216-228; UI/Chrome/Popovers.swift:354-363
    public void Right_clicking_a_sent_message_offers_the_two_copies_and_when_revertable_a_divider_then_Edit_from_here()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            h.Main.OnMain(() =>
            {
                var plain = new UserMessageRow(UserEntry("hello", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                var revertable = new UserMessageRow(UserEntry("hello again", System.Guid.NewGuid()), h.Runtime, canRevert: true);
                var panel = new StackPanel { Spacing = 60 };
                panel.Children.Add(plain);
                panel.Children.Add(revertable);
                var window = Show(panel);

                var (_, first) = RightClickMenu(presenter, window, LogicalQuery.SelfAndDescendants<InlineText>(plain).Single());
                Assert.Equal(["Copy message", "Copy as markdown"], ItemTitles(first));
                Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverDivider>(first));

                var (_, second) = RightClickMenu(presenter, window, LogicalQuery.SelfAndDescendants<InlineText>(revertable).Single());
                Assert.Equal(["Copy message", "Copy as markdown", "Edit from here"], ItemTitles(second));
                var stack = Assert.IsType<StackPanel>(second.Child);
                Assert.IsType<PopoverDivider>(stack.Children[2]);
                Assert.Equal(
                    ["doc.on.doc", "number", "arrow.uturn.backward"],
                    LogicalQuery.SelfAndDescendants<PopoverItem>(second).Select(item => Symbols(item).First()));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-081")] // App/Views/Chat/TimelineRows.swift:31-34, :219-220; UI/Markdown/SelectableMessageText.swift:9-19
    public void Copy_message_writes_the_plain_text_and_Copy_as_markdown_the_raw_text_and_both_confirm_Copied()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            AvaloniaWindow? window = null;
            AvaloniaWindow? menu = null;
            PopoverItem[] items = [];
            System.Threading.Tasks.Task<string?>? read = null;
            h.Main.OnMain(() =>
            {
                _ = AppCurrentEvent.CurrentWindow; // starts the app-wide event watch before the click
                var row = new UserMessageRow(UserEntry("- one\n- two", System.Guid.NewGuid()), h.Runtime, canRevert: false);
                window = Show(row);
                var (popup, actions) = RightClickMenu(presenter, window, LogicalQuery.SelfAndDescendants<InlineText>(row).Single());
                // The recorded popup never opens headless; its content is hosted here so the items react.
                var content = popup.Child!;
                popup.Child = null;
                menu = Show(content);
                items = LogicalQuery.SelfAndDescendants<PopoverItem>(actions).ToArray();
                Click(LogicalQuery.SelfAndDescendants<Button>(items[0]).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(menu!);
                Assert.True(items[0].IsConfirmed);
                Assert.Equal("Copied", Title(items[0]));
                read = window!.Clipboard!.TryGetTextAsync();
            });
            h.Main.OnMain(() =>
            {
                // MessageText.plain: each item on its own line behind a U+2022 BULLET and two spaces; a line that
                // starts with a bullet is not reflowed (MarkdownView.swift:1344-1353, lines shorter than 45).
                Assert.Equal("\U00002022  one\n\U00002022  two", read!.GetAwaiter().GetResult());
                Click(LogicalQuery.SelfAndDescendants<Button>(items[1]).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(menu!);
                Assert.Equal("Copied", Title(items[1]));
                read = window!.Clipboard!.TryGetTextAsync();
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("- one\n- two", read!.GetAwaiter().GetResult());
                Close(menu!);
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-088 the reply row

    private static AssistantMessageRow ReplyRow(ThreadRuntime runtime, string text, bool streaming, TurnSummary? summary) =>
        new(ReplyEntry(text, streaming), summary, runtime);

    [Fact]
    [Trait("Req", "PR-CHAT-088")] // App/Views/Chat/TimelineRows.swift:1363-1379, :1388
    public void A_reply_renders_its_markdown_and_keeps_a_hidden_22_point_line_with_the_turn_label_in_caption_secondary_text()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var row = ReplyRow(h.Runtime, CodeOnlyReply, streaming: false, Summary(turn));
                var window = Show(row);
                var markdown = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(row));
                Assert.Equal(CodeOnlyReply, markdown.Text);
                Assert.False(markdown.IsStreaming);
                var line = HoverLine(row);
                Assert.Equal(0, line.Opacity);
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(line).Single();
                Assert.Equal("Worked for " + RelativeTime.Duration(System.TimeSpan.FromSeconds(5)), label.Text);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, label.FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
                var outer = LogicalQuery.SelfAndDescendants<Control>(row).First(control => control.Margin.Bottom == -(TimelineMetrics.HoverLineHeight + 2));
                Assert.NotNull(outer);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-088")] // App/Views/Chat/TimelineRows.swift:1378, :1380-1382
    public void Hovering_a_settled_reply_fades_the_line_in_and_a_streaming_reply_never_shows_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            AssistantMessageRow? settled = null;
            AssistantMessageRow? streaming = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                settled = ReplyRow(h.Runtime, CodeOnlyReply, streaming: false, Summary(System.Guid.NewGuid()));
                streaming = ReplyRow(h.Runtime, "still typing", streaming: true, Summary(System.Guid.NewGuid()));
                var panel = new StackPanel { Spacing = 60 };
                panel.Children.Add(settled);
                panel.Children.Add(streaming);
                window = Show(panel);
                Hover(window, LogicalQuery.SelfAndDescendants<MarkdownView>(settled).Single());
            });
            // easeOut over 0.12 s on the frame clock: part way at 60 ms, fully in by 120 ms.
            frames.Advance(System.TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(60);
            frames.Advance(System.TimeSpan.FromMilliseconds(60));
            var partway = HoverLine(settled!).Opacity;
            Assert.InRange(partway, 0.01, 0.99);
            h.Main.AdvanceMilliseconds(60);
            frames.Advance(System.TimeSpan.FromMilliseconds(60));
            Assert.Equal(1, HoverLine(settled!).Opacity);
            h.Main.OnMain(() => Hover(window!, LogicalQuery.SelfAndDescendants<MarkdownView>(streaming!).Single()));
            h.Main.AdvanceMilliseconds(200);
            frames.Advance(System.TimeSpan.FromMilliseconds(200));
            Assert.Equal(0, HoverLine(streaming!).Opacity);
            h.Main.OnMain(() => Close(window!));
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-089 the reply's menu

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // App/Views/Chat/TimelineRows.swift:38-54
    public void The_last_code_block_is_the_last_closed_fence_and_an_unclosed_fence_yields_none()
    {
        Assert.Null(TimelineRows.LastCodeBlock("no code here"));
        Assert.Null(TimelineRows.LastCodeBlock("text\n```swift\nlet a = 1\n"));
        Assert.Equal("let a = 1\nlet b = 2", TimelineRows.LastCodeBlock("```swift\nlet a = 1\nlet b = 2\n```"));
        Assert.Equal("second", TimelineRows.LastCodeBlock("```\nfirst\n```\nprose\n```js\nsecond\n```\n```\nopen"));
        Assert.Equal("", TimelineRows.LastCodeBlock("```\n```"));
        // A three-backtick prefix is all the scan tests: an indented fence is text.
        Assert.Null(TimelineRows.LastCodeBlock("  ```\ncode\n  ```"));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // App/Views/Chat/TimelineRows.swift:1394-1412
    public void A_replys_menu_is_the_two_copies_then_the_last_code_block_when_there_is_one_then_Add_to_reply_in_its_own_group()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var bare = AssistantMessageRow.MessageActions("plain answer", null, h.Runtime);
                Assert.Equal(["Copy message", "Copy as markdown", "Add to reply"], bare.Select(item => item.Title));
                Assert.Equal(["doc.on.doc", "number", "arrow.turn.down.left"], bare.Select(item => item.Symbol));
                Assert.Equal(["Copied", "Copied", "Added"], bare.Select(item => item.Confirms));
                Assert.Equal([false, false, true], bare.Select(item => item.StartsGroup));

                var coded = AssistantMessageRow.MessageActions("answer", "let x = 1", h.Runtime);
                Assert.Equal(["Copy message", "Copy as markdown", "Copy last code block", "Add to reply"], coded.Select(item => item.Title));
                Assert.Equal("chevron.left.forwardslash.chevron.right", coded[2].Symbol);
                Assert.Equal("Copied", coded[2].Confirms);
                Assert.False(coded[2].StartsGroup);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // App/Views/Chat/TimelineRows.swift:1406-1410
    public void Add_to_reply_quotes_the_reply_into_the_draft_once()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var add = AssistantMessageRow.MessageActions("the answer", null, h.Runtime).Last();
                add.Action();
                Assert.Equal(["the answer"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
                add.Action();
                Assert.Single(h.Runtime.Draft.Quotes);
                AssistantMessageRow.MessageActions("another", null, h.Runtime).Last().Action();
                Assert.Equal(["the answer", "another"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-089")] // App/Views/Chat/TimelineRows.swift:1383, :1415-1429
    public void Right_clicking_a_reply_offers_its_menu_with_the_last_code_block()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            h.Main.OnMain(() =>
            {
                var row = ReplyRow(h.Runtime, CodeOnlyReply, streaming: false, null);
                var window = Show(row);
                var (_, items) = RightClickMenu(presenter, window, LogicalQuery.SelfAndDescendants<MarkdownView>(row).Single());
                Assert.Equal(["Copy message", "Copy as markdown", "Copy last code block", "Add to reply"], ItemTitles(items));
                var stack = Assert.IsType<StackPanel>(items.Child);
                Assert.IsType<PopoverDivider>(stack.Children[3]);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-091 WorkGroupSummary

    [Fact]
    [Trait("Req", "PR-CHAT-091")] // App/Views/Chat/TimelineRows.swift:1588-1610, :1622-1633
    public void The_summary_names_the_kinds_in_a_fixed_order_present_tense_while_running_lowering_only_later_first_letters()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var entries = new[]
            {
                ToolEntry(Call(ToolCall.Kind.Command, "npm test", ToolCall.Status.Running)),
                ToolEntry(Call(ToolCall.Kind.Edit, "Edit file")),
                ToolEntry(Call(ToolCall.Kind.Command, "ls")),
            };
            Assert.Equal("Edited files, running commands", WorkGroupSummary.Text(entries));

            var all = new[]
            {
                ToolEntry(Call(ToolCall.Kind.Other, "x")),
                ToolEntry(Call(ToolCall.Kind.Agent, "x")),
                ToolEntry(Call(ToolCall.Kind.Mcp, "x")),
                ToolEntry(Call(ToolCall.Kind.Web, "x")),
                ToolEntry(Call(ToolCall.Kind.Search, "x")),
                ToolEntry(Call(ToolCall.Kind.Command, "x")),
                ToolEntry(Call(ToolCall.Kind.Read, "x")),
                ToolEntry(Call(ToolCall.Kind.Edit, "x")),
            };
            Assert.Equal(
                "Edited files, read files, ran commands, searched, browsed, called tools, delegated tasks, used tools",
                WorkGroupSummary.Text(all));

            var running = all.Select(entry => ToolEntry(((TimelineItem.Content.Tool)entry.Item.TimelineItemContent).Value with { ToolCallStatus = ToolCall.Status.Running })).ToArray();
            Assert.Equal(
                "Editing files, reading files, running commands, searching, browsing, calling tools, delegating tasks, using tools",
                WorkGroupSummary.Text(running));

            Assert.Equal("Searched", WorkGroupSummary.Text([ToolEntry(Call(ToolCall.Kind.Search, "grep"))]));
            Assert.Equal("Worked", WorkGroupSummary.Text([ReplyEntry("words")]));
            Assert.Equal("Worked", WorkGroupSummary.Text([]));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-091")] // App/Views/Chat/TimelineRows.swift:1612-1620
    public void The_summary_glyph_is_the_first_present_kind_in_the_same_order_and_a_wrench_when_none()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            Assert.Equal("terminal", WorkGroupSummary.Symbol([ToolEntry(Call(ToolCall.Kind.Search, "a")), ToolEntry(Call(ToolCall.Kind.Command, "b"))]));
            Assert.Equal("pencil", WorkGroupSummary.Symbol([ToolEntry(Call(ToolCall.Kind.Read, "a")), ToolEntry(Call(ToolCall.Kind.Edit, "b"))]));
            Assert.Equal("person.2", WorkGroupSummary.Symbol([ToolEntry(Call(ToolCall.Kind.Other, "a")), ToolEntry(Call(ToolCall.Kind.Agent, "b"))]));
            Assert.Equal("wrench.and.screwdriver", WorkGroupSummary.Symbol([ReplyEntry("words")]));
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-093..096 ToolPresentation

    [Fact]
    [Trait("Req", "PR-CHAT-093")] // App/Views/Chat/TimelineRows.swift:2189-2220
    public void A_tool_label_is_the_verb_and_the_subject_with_a_leading_kind_word_removed()
    {
        Assert.Equal("Edited file", ToolPresentation.Label(Call(ToolCall.Kind.Edit, "Edit file")));
        Assert.Equal("Editing Main.swift", ToolPresentation.Label(Call(ToolCall.Kind.Edit, "Write Main.swift", ToolCall.Status.Running)));
        Assert.Equal("Edited", ToolPresentation.Label(Call(ToolCall.Kind.Edit, "apply_patch")));
        Assert.Equal("Ran npm run build", ToolPresentation.Label(Call(ToolCall.Kind.Command, "Bash npm run build")));
        Assert.Equal("Ran npm run build", ToolPresentation.Label(Call(ToolCall.Kind.Command, "npm run build")));
        Assert.Equal("Read README.md", ToolPresentation.Label(Call(ToolCall.Kind.Read, "CAT README.md")));
        // Only the kind's own roots are dropped: "read" is not an edit word.
        Assert.Equal("Edited Read me", ToolPresentation.Label(Call(ToolCall.Kind.Edit, "Read me")));
        // One split only, so the subject keeps its own spacing and casing.
        Assert.Equal("Searched  Two  Spaces", ToolPresentation.Label(Call(ToolCall.Kind.Search, "grep  Two  Spaces")));
        Assert.Equal("Browsed https://a.test", ToolPresentation.Label(Call(ToolCall.Kind.Web, "WebFetch https://a.test")));
        Assert.Equal("Calling", ToolPresentation.Label(Call(ToolCall.Kind.Mcp, "call", ToolCall.Status.Running)));
        Assert.Equal("Delegated", ToolPresentation.Label(Call(ToolCall.Kind.Agent, "Task")));
        Assert.Equal("Used", ToolPresentation.Label(Call(ToolCall.Kind.Other, "use")));
        Assert.Equal("Used", ToolPresentation.Label(Call(ToolCall.Kind.Other, "")));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-093")] // App/Views/Chat/TimelineRows.swift:2208-2220
    public void The_verb_is_present_tense_only_while_running()
    {
        var kinds = new[] { ToolCall.Kind.Command, ToolCall.Kind.Read, ToolCall.Kind.Edit, ToolCall.Kind.Search, ToolCall.Kind.Web, ToolCall.Kind.Mcp, ToolCall.Kind.Agent, ToolCall.Kind.Other };
        Assert.Equal(
            ["Running", "Reading", "Editing", "Searching", "Browsing", "Calling", "Delegating", "Using"],
            kinds.Select(kind => ToolPresentation.Verb(Call(kind, "x", ToolCall.Status.Running))));
        foreach (var status in new[] { ToolCall.Status.Completed, ToolCall.Status.Failed, ToolCall.Status.Declined })
        {
            Assert.Equal(
                ["Ran", "Read", "Edited", "Searched", "Browsed", "Called", "Delegated", "Used"],
                kinds.Select(kind => ToolPresentation.Verb(Call(kind, "x", status))));
        }
    }

    [Fact]
    [Trait("Req", "PR-CHAT-094")] // App/Views/Chat/TimelineRows.swift:2222-2233
    public void Each_tool_kind_has_its_fixed_glyph()
    {
        Assert.Equal("terminal", ToolPresentation.Symbol(ToolCall.Kind.Command));
        Assert.Equal("doc.text", ToolPresentation.Symbol(ToolCall.Kind.Read));
        Assert.Equal("pencil", ToolPresentation.Symbol(ToolCall.Kind.Edit));
        Assert.Equal("magnifyingglass", ToolPresentation.Symbol(ToolCall.Kind.Search));
        Assert.Equal("globe", ToolPresentation.Symbol(ToolCall.Kind.Web));
        Assert.Equal("puzzlepiece.extension", ToolPresentation.Symbol(ToolCall.Kind.Mcp));
        Assert.Equal("person.2", ToolPresentation.Symbol(ToolCall.Kind.Agent));
        Assert.Equal("wrench.and.screwdriver", ToolPresentation.Symbol(ToolCall.Kind.Other));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-095")] // App/Views/Chat/TimelineRows.swift:2235-2240
    public void A_diff_stat_shows_only_when_the_edits_add_up_to_something()
    {
        Assert.Null(ToolPresentation.Stats(Call(ToolCall.Kind.Edit, "e")));
        var empty = Call(ToolCall.Kind.Edit, "e") with { Edits = new ValueList<FileEdit>([new FileEdit("a.txt"), new FileEdit("b.txt")]) };
        Assert.Null(ToolPresentation.Stats(empty));
        var real = Call(ToolCall.Kind.Edit, "e") with
        {
            Edits = new ValueList<FileEdit>([new FileEdit("a.txt", null, 3, 1), new FileEdit("b.txt", null, 0, 4)]),
        };
        Assert.Equal((3, 5), ToolPresentation.Stats(real));
        var deletionsOnly = Call(ToolCall.Kind.Edit, "e") with { Edits = new ValueList<FileEdit>([new FileEdit("a.txt", null, 0, 2)]) };
        Assert.Equal((0, 2), ToolPresentation.Stats(deletionsOnly));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-096")] // App/Views/Chat/TimelineRows.swift:2242-2254
    public void An_mcp_title_of_catalog_server_middle_dot_tool_resolves_to_the_entry_and_its_label()
    {
        var parts = ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "Context7 \U000000B7 resolve-library-id")); // U+00B7 MIDDLE DOT
        Assert.NotNull(parts);
        Assert.Equal("context7", parts.Value.Entry.Id);
        Assert.Equal("resolve-library-id", parts.Value.Tool);
        Assert.Equal("Context7: resolve-library-id", ToolPresentation.McpLabel(parts.Value.Entry, parts.Value.Tool, running: true));
        Assert.Equal("Context7: resolve-library-id", ToolPresentation.McpLabel(parts.Value.Entry, parts.Value.Tool, running: false));
        // Both sides are trimmed of whitespace and newlines.
        var trimmed = ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "\n context7  \U000000B7 \tquery-docs \n")); // U+00B7 MIDDLE DOT
        Assert.Equal(("context7", "query-docs"), (trimmed!.Value.Entry.Id, trimmed.Value.Tool));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-096")] // App/Views/Chat/TimelineRows.swift:2243-2248
    public void An_mcp_title_without_the_exact_separator_an_empty_side_an_unknown_server_or_another_kind_is_no_badge()
    {
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "Context7\U000000B7resolve"))); // U+00B7, no spaces
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "Context7 - resolve")));
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "   \U000000B7 resolve"))); // U+00B7 MIDDLE DOT
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "Context7 \U000000B7   "))); // U+00B7 MIDDLE DOT
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Mcp, "No Such Server \U000000B7 tool"))); // U+00B7 MIDDLE DOT
        Assert.Null(ToolPresentation.McpParts(Call(ToolCall.Kind.Other, "Context7 \U000000B7 resolve-library-id"))); // U+00B7 MIDDLE DOT
    }

    // ---------------------------------------------------------------- PR-CHAT-102 DiffDetailCache

    [Fact]
    [Trait("Req", "PR-CHAT-102")] // App/Views/Chat/TimelineRows.swift:2171-2182
    public void A_diff_is_parsed_once_per_path_and_text()
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            const string diff = "@@ -1 +1 @@\n-old\n+new\n";
            var first = DiffDetailCache.File(diff, "a.txt");
            Assert.Same(first, DiffDetailCache.File(diff, "a.txt"));
            Assert.Equal("a.txt", first.Path);
            Assert.NotSame(first, DiffDetailCache.File(diff, "b.txt"));
            Assert.NotSame(first, DiffDetailCache.File(diff + "+more\n", "a.txt"));
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-097..101 ToolRow

    private static ToolRow ToolRowFor(ThreadRuntime runtime, ToolCall call, System.Guid? turn = null) =>
        new(ToolEntry(call, turn), runtime);

    [Fact]
    [Trait("Req", "PR-CHAT-097")] // App/Views/Chat/TimelineRows.swift:1674-1677, :1707-1709
    public void A_call_with_nothing_to_open_is_a_line_of_text_not_a_control_and_has_no_chevron()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var row = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Search, "grep foo"));
                var window = Show(row);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(row));
                Assert.Contains("Searched foo", Texts(row));
                Assert.DoesNotContain("chevron.down", Symbols(row));
                Assert.DoesNotContain("chevron.right", Symbols(row));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-097")] // App/Views/Chat/TimelineRows.swift:1695-1700, :1710-1713, :1847-1853
    public void A_call_with_output_expands_in_place_and_asks_the_timeline_to_reveal_the_end()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var reveals = 0;
            ToolRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                row = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "ls") with { Output = "a\nb" });
                var host = new Border { Child = row };
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
                window = Show(host);
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row));
                Assert.Equal("Show the output", ToolTip.GetTip(button));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(row));
                Click(button);
                Assert.Equal(1, reveals);
            });
            h.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<ToolDetailView>(row!));
                var button = LogicalQuery.SelfAndDescendants<Button>(row!).First();
                Assert.Equal("Hide the output", ToolTip.GetTip(button));
                Click(button);
                Assert.Equal(1, reveals);
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Show the output", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(row!).First()));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-097")] // App/Views/Chat/TimelineRows.swift:1664-1669, :1690-1692, :1821-1828, :1849
    public void A_call_with_a_diff_never_unfolds_and_opens_the_changes_on_its_label_once_the_pointer_has_been_there()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var turn = System.Guid.NewGuid();
            var edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1), new FileEdit("", null, 0, 0)]);
            ToolRow? row = null;
            AvaloniaWindow? window = null;
            long before = 0;
            h.Main.OnMain(() =>
            {
                row = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Edit, "Edit a.swift") with { Edits = edits, Output = "done" }, turn);
                window = Show(row);
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row));
                Assert.Equal("Show this change", ToolTip.GetTip(button));
                before = h.Runtime.DiffOpenRequest;
                // No anchor yet: the pointer has not been on the label, so a click opens nothing.
                Click(button);
                Assert.Equal(before, h.Runtime.DiffOpenRequest);
                Hover(window, LogicalQuery.SelfAndDescendants<TextBlock>(row).First(block => block.Text == "Edited a.swift"));
            });
            h.Main.OnMain(() =>
            {
                Click(LogicalQuery.SelfAndDescendants<Button>(row!).First());
                Assert.Equal(before + 1, h.Runtime.DiffOpenRequest);
                Assert.Equal(turn, h.Runtime.DiffSelection);
                Assert.Equal(["src/a.swift"], h.Runtime.DiffFocusEdits.Select(edit => edit.Path));
                Assert.Equal(PreviewPanelEdge.MinY, h.Runtime.DiffAnchorEdge);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(row!));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-097")] // App/Views/Chat/TimelineRows.swift:1670-1673, :1688-1689, :1829-1831, :1848; :1862-1867
    public void A_call_that_sent_out_a_head_opens_the_heads_steps_popover_and_never_its_raw_output()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource()); // the head's glyph animates while running
            ToolRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                var entry = ToolEntry(Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Running) with { Output = "raw tool output" });
                h.Runtime.RehearseHydraHead(entry.Id, System.Guid.NewGuid());
                row = new ToolRow(entry, h.Runtime);
                var host = new Border { Child = row };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
                window = Show(host);
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row));
                Assert.Equal("Show the head's steps", ToolTip.GetTip(button));
                Assert.Contains("chevron.down", Symbols(row));
                Assert.DoesNotContain("chevron.right", Symbols(row));
                Assert.Empty(presenter.Calls);
                Click(button);
            });
            h.Main.OnMain(() =>
            {
                Assert.Single(presenter.Calls, call => call.Presented);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(row!));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-098")] // App/Views/Chat/TimelineRows.swift:1799-1810
    public void A_popover_row_has_a_static_down_chevron_and_an_output_row_a_right_chevron_turning_down_when_open()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            ToolRow? output = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                var diff = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Edit, "Edit a") with { Edits = new ValueList<FileEdit>([new FileEdit("a", "d", 1, 0)]) });
                output = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "ls") with { Detail = "details" });
                var panel = new StackPanel();
                panel.Children.Add(diff);
                panel.Children.Add(output);
                window = Show(panel);
                var down = LogicalQuery.SelfAndDescendants<SwiftImage>(diff).Single(image => image.SymbolName == "chevron.down");
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, down), BrushColor(down.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold).Resolve().FontSize, SwiftImage.GetFont(down)!.Resolve().FontSize);
                Assert.DoesNotContain("chevron.right", Symbols(diff));
                var right = LogicalQuery.SelfAndDescendants<SwiftImage>(output).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(0, Assert.IsType<RotateTransform>(right.RenderTransform).Angle);
                Assert.DoesNotContain("chevron.down", Symbols(output));
                // The chevron follows the row's text in the same run, not the trailing edge.
                var run = Assert.IsType<StackPanel>(right.Parent);
                Assert.Contains(run.Children, child => child is TextBlock text && text.Text == "Ran ls");
                Click(LogicalQuery.SelfAndDescendants<Button>(output).Single());
            });
            // The snappy 0.24 s turn runs on the frame clock; two seconds see it settled.
            frames.Advance(System.TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(2000);
            frames.Advance(System.TimeSpan.FromMilliseconds(2000));
            h.Main.OnMain(() =>
            {
                var right = LogicalQuery.SelfAndDescendants<SwiftImage>(output!).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(90, Assert.IsType<RotateTransform>(right.RenderTransform).Angle);
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-099")] // App/Views/Chat/TimelineRows.swift:2067-2091, :1784
    public void The_status_icon_is_a_spinner_a_red_cross_a_raised_hand_or_the_kinds_glyph_in_a_16_point_column()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var running = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x", ToolCall.Status.Running));
                var failed = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x", ToolCall.Status.Failed));
                var declined = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x", ToolCall.Status.Declined));
                var completed = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x"));
                var photo = new ToolStatusIcon(Call(ToolCall.Kind.Read, "x"), symbol: "photo");
                var panel = new StackPanel();
                foreach (var icon in new Control[] { running, failed, declined, completed, photo }) panel.Children.Add(icon);
                var window = Show(panel);
                foreach (var icon in new Control[] { running, failed, declined, completed, photo })
                    Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(icon), control => control.Width == TimelineMetrics.IconWidth);
                Assert.Single(LogicalQuery.SelfAndDescendants<ProgressBar>(running));
                Assert.Empty(Symbols(running));
                var cross = LogicalQuery.SelfAndDescendants<SwiftImage>(failed).Single();
                Assert.Equal("xmark.circle.fill", cross.SymbolName);
                Assert.Equal(Resolved(ChromeTokens.Danger, cross), BrushColor(cross.Foreground));
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(declined).Single();
                Assert.Equal("hand.raised.slash", hand.SymbolName);
                Assert.Equal(Resolved(ChromeTokens.Warning, hand), BrushColor(hand.Foreground));
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(completed).Single();
                Assert.Equal("doc.text", glyph.SymbolName);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, glyph), BrushColor(glyph.Foreground));
                Assert.Equal("photo", LogicalQuery.SelfAndDescendants<SwiftImage>(photo).Single().SymbolName);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-100")] // App/Views/Chat/TimelineRows.swift:1832-1841
    public void A_failed_call_shows_its_exit_code_in_danger_and_a_declined_one_Declined_in_warning()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var failed = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed) with { ExitCode = 2 });
                var noCode = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed));
                var declined = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "rm -rf /", ToolCall.Status.Declined));
                var panel = new StackPanel();
                panel.Children.Add(failed);
                panel.Children.Add(noCode);
                panel.Children.Add(declined);
                var window = Show(panel);
                var exit = LogicalQuery.SelfAndDescendants<TextBlock>(failed).Single(block => block.Text == "exit 2");
                Assert.Equal(Resolved(ChromeTokens.Danger, exit), BrushColor(exit.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, exit.FontSize);
                Assert.DoesNotContain(Texts(noCode), text => text.StartsWith("exit", System.StringComparison.Ordinal));
                Assert.DoesNotContain("Declined", Texts(noCode));
                var word = LogicalQuery.SelfAndDescendants<TextBlock>(declined).Single(block => block.Text == "Declined");
                Assert.Equal(Resolved(ChromeTokens.Warning, word), BrushColor(word.Foreground));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-096")] // App/Views/Chat/TimelineRows.swift:1772-1795, :1812-1820
    public void An_mcp_call_to_a_catalog_server_draws_as_a_pill_badge_with_the_servers_glyph_and_label()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var row = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Mcp, "Context7 \U000000B7 resolve-library-id")); // U+00B7 MIDDLE DOT
                var plain = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Mcp, "Unknown \U000000B7 thing")); // U+00B7 MIDDLE DOT
                var panel = new StackPanel();
                panel.Children.Add(row);
                panel.Children.Add(plain);
                var window = Show(panel);
                Assert.Single(LogicalQuery.SelfAndDescendants<MCPGlyph>(row));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolStatusIcon>(row));
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(block => block.Text == "Context7: resolve-library-id");
                var pill = LogicalQuery.SelfAndDescendants<Border>(row).Single(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), pill), BrushColor(pill.Background));
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), label), BrushColor(label.Foreground));
                Assert.Empty(LogicalQuery.SelfAndDescendants<MCPGlyph>(plain));
                Assert.Single(LogicalQuery.SelfAndDescendants<ToolStatusIcon>(plain));
                Assert.Contains("Called Unknown \U000000B7 thing", Texts(plain)); // U+00B7 MIDDLE DOT
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-101 ToolDetailView

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    [Fact]
    [Trait("Req", "PR-CHAT-101")] // App/Views/Chat/TimelineRows.swift:2103-2113, :2141-2162
    public void Output_over_thirty_lines_shows_its_first_thirty_and_a_link_that_shows_all_of_it_then_Show_less()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            ToolDetailView? view = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = "\n\n" + Lines(31) + "\n\n" });
                window = Show(view);
                Assert.Contains(Lines(30), Texts(view));
                Assert.DoesNotContain(Lines(31), Texts(view));
                var link = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(view));
                Assert.Contains("Show full output (31 lines)", Texts(link));
                Click(link);
            });
            h.Main.OnMain(() =>
            {
                Assert.Contains(Lines(31), Texts(view!));
                Assert.Contains("Show less", Texts(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(view!))));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-101")] // App/Views/Chat/TimelineRows.swift:2120-2126, :2145-2147, :2156
    public void Thirty_lines_show_whole_with_no_link_and_the_detail_is_capped_at_twelve_lines_in_secondary_monospace()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = Lines(30), Detail = "the detail" });
                var window = Show(view);
                Assert.Contains(Lines(30), Texts(view));
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(view));
                var detail = LogicalQuery.SelfAndDescendants<TextBlock>(view).Single(block => block.Text == "the detail");
                Assert.Equal(12, detail.MaxLines);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, detail), BrushColor(detail.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption, design: SwiftFont.Design.Monospaced).Resolve().FontFamily, detail.FontFamily);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-101")] // App/Views/Chat/TimelineRows.swift:2127-2139
    public void Each_edit_with_a_diff_shows_its_path_above_its_lines_on_a_rounded_plate()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var call = Call(ToolCall.Kind.Edit, "e") with
                {
                    Edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1), new FileEdit("src/b.swift", "", 0, 0)]),
                };
                // Inspected without a layout pass: the edits are built with the view, and DiffLinesView's own font
                // (App.Views.Diff.DiffInspector's) does not resolve headless.
                var view = new ToolDetailView(call);
                Assert.Single(LogicalQuery.SelfAndDescendants<DiffLinesView>(view));
                var path = LogicalQuery.SelfAndDescendants<TextBlock>(view).Single(block => block.Text == "src/a.swift");
                Assert.Equal(new Thickness(10, 6), path.Margin);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, path), BrushColor(path.Foreground));
                var plate = Assert.IsType<Border>(Assert.IsType<StackPanel>(path.Parent).Parent);
                Assert.Equal(new CornerRadius(10), plate.CornerRadius);
                Assert.True(plate.ClipToBounds);
                Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.45), plate), BrushColor(plate.Background));
                Assert.DoesNotContain("src/b.swift", Texts(view));
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-090 WorkGroup, PR-CHAT-092 WorkSteps

    [Fact]
    [Trait("Req", "PR-CHAT-090")] // App/Views/Chat/TimelineRows.swift:1463-1465
    public void A_lone_tool_renders_as_its_row_alone()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var group = new WorkGroup(new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Search, "grep x"))]), h.Runtime);
                var window = Show(group);
                Assert.Single(LogicalQuery.SelfAndDescendants<ToolRow>(group));
                Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(group));
                Assert.DoesNotContain("Searched", Texts(group));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-090")] // App/Views/Chat/TimelineRows.swift:1466-1502
    public void A_group_the_agent_moved_on_from_starts_as_one_summary_line_and_opening_it_reveals_the_end_while_closing_does_not()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var reveals = 0;
            WorkGroup? group = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                var entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Edit, "Edit a")), ToolEntry(Call(ToolCall.Kind.Command, "ls"))]);
                group = new WorkGroup(entries, h.Runtime, startsCollapsed: true);
                var host = new Border { Child = group };
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
                window = Show(host);
                var header = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(group));
                Assert.Equal("Show these steps", ToolTip.GetTip(header));
                Assert.Equal("Show these steps", AutomationProperties.GetName(header));
                Assert.Contains("Edited files, ran commands", Texts(header));
                Assert.Equal(["pencil", "chevron.right"], Symbols(header));
                var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(header).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(header).Single(image => image.SymbolName == "pencil");
                Assert.Equal(TimelineMetrics.IconWidth, glyph.Width);
                Assert.Empty(LogicalQuery.SelfAndDescendants<WorkSteps>(group));
                Click(header);
                Assert.Equal(1, reveals);
            });
            h.Main.OnMain(() =>
            {
                var header = LogicalQuery.SelfAndDescendants<Button>(group!).First();
                Assert.Equal("Hide these steps", ToolTip.GetTip(header));
                Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(group!));
                Click(header);
                Assert.Equal(1, reveals);
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Show these steps", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(group!).First()));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-090")] // App/Views/Chat/TimelineRows.swift:1503-1507
    public void Arriving_reply_text_folds_the_group_once_and_a_group_the_reader_reopened_never_snaps_shut_on_its_own()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var entries = new ValueList<TimelineEntry>([]);
            WorkGroup? group = null;
            AvaloniaWindow? window = null;
            string Help() => (string)ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(group!).First())!;
            h.Main.OnMain(() =>
            {
                entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ToolEntry(Call(ToolCall.Kind.Read, "Read b"))]);
                group = new WorkGroup(entries, h.Runtime);
                window = Show(group);
                Assert.Equal("Hide these steps", Help());
                group.Update(entries, startsCollapsed: true); // reply text arrived after the group
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Show these steps", Help());
                Click(LogicalQuery.SelfAndDescendants<Button>(group!).First()); // the reader opens it again
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Hide these steps", Help());
                group!.Update(entries, startsCollapsed: true); // a later render, the flag unchanged
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Hide these steps", Help());
                group!.Update(entries, startsCollapsed: false);
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal("Hide these steps", Help());
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-090")] // App/Views/Chat/TimelineRows.swift:1455-1461, :1499-1501
    public void The_live_group_starts_open_with_its_steps()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ToolEntry(Call(ToolCall.Kind.Read, "Read b", ToolCall.Status.Running))]);
                var group = new WorkGroup(entries, h.Runtime);
                var window = Show(group);
                var header = LogicalQuery.SelfAndDescendants<Button>(group).First();
                Assert.Equal("Hide these steps", ToolTip.GetTip(header));
                Assert.Contains("Reading files", Texts(header));
                Assert.Single(LogicalQuery.SelfAndDescendants<WorkSteps>(group));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(group).Count());
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-092")] // App/Views/Chat/TimelineRows.swift:1522-1545
    public void Only_the_two_latest_steps_show_under_an_earlier_steps_line_that_reveals_them_all()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var reveals = 0;
            var showsAll = false;
            WorkSteps? steps = null;
            WorkSteps? four = null;
            h.Main.OnMain(() =>
            {
                var three = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ToolEntry(Call(ToolCall.Kind.Read, "Read b")), ToolEntry(Call(ToolCall.Kind.Read, "Read c"))]);
                steps = new WorkSteps(three, h.Runtime, new Binding<bool>(() => showsAll, value => showsAll = value));
                four = new WorkSteps(new ValueList<TimelineEntry>([.. three, ToolEntry(Call(ToolCall.Kind.Read, "Read d"))]), h.Runtime, Binding<bool>.Constant(false));
                var host = new StackPanel();
                host.Children.Add(steps);
                host.Children.Add(four);
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals++ });
                var window = Show(host);
                Assert.Contains("1 earlier step", Texts(steps));
                Assert.Equal(["Read b", "Read c"], LogicalQuery.SelfAndDescendants<ToolRow>(steps).SelectMany(row => Texts(row)).Where(text => text.StartsWith("Read ", System.StringComparison.Ordinal)));
                Assert.Contains("2 earlier steps", Texts(four));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(four).Count());
                var more = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(steps), button => Texts(button).Contains("1 earlier step"));
                Assert.Contains("ellipsis", Symbols(more));
                Click(more);
                Assert.True(showsAll);
                Assert.Equal(1, reveals);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-092")] // App/Views/Chat/TimelineRows.swift:1523, :1545-1553
    public void With_every_step_showing_there_is_no_earlier_line_and_narration_is_dimmed_12_point_markdown_after_the_icon_column()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var entries = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ReplyEntry("now the build", streaming: true), ToolEntry(Call(ToolCall.Kind.Command, "make"))]);
                var steps = new WorkSteps(entries, h.Runtime, Binding<bool>.Constant(true));
                var window = Show(steps);
                Assert.DoesNotContain(Texts(steps), text => text.Contains("earlier step", System.StringComparison.Ordinal));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<ToolRow>(steps).Count());
                var narration = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(steps));
                Assert.Equal("now the build", narration.Text);
                Assert.Equal(12, MarkdownView.GetMarkdownPointSize(narration));
                Assert.True(MarkdownView.GetMarkdownDimmed(narration));
                Assert.Equal(TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, narration.Margin.Left);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-104 TurnFinishedBlock

    private static Border[] Rules(Control block) =>
        LogicalQuery.SelfAndDescendants<Border>(block).Where(border => border.Height == 1 && border.Opacity == 0.6).ToArray();

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // App/Views/Chat/TimelineRows.swift:2589-2630, :2664-2680, :2507-2508
    public void A_finished_turn_opens_folded_to_its_prompt_a_summary_line_a_rule_and_its_answer()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var user = UserEntry("do it", turn);
                var content = new ValueList<TimelineEntry>([ReplyEntry("```\nplan\n```"), ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), ReplyEntry(CodeOnlyReply, turn: turn)]);
                var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), new ValueList<TimelineEntry>([user]), content, null, canUndo: false, chronological: false);
                var window = Show(block);
                var stack = Assert.IsType<StackPanel>(block.Child);
                Assert.IsType<UserMessageRow>(stack.Children[0]);
                Assert.Equal(TimelineMetrics.RowSpacing, stack.Children[0].Margin.Bottom);
                var header = Assert.IsType<Button>(stack.Children[1]);
                Assert.Equal("Show this turn's steps", ToolTip.GetTip(header));
                Assert.Equal("Show this turn's steps", AutomationProperties.GetName(header));
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(header).Single();
                Assert.Equal("Worked for " + RelativeTime.Duration(System.TimeSpan.FromSeconds(5)), label.Text);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
                var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(header).Single();
                Assert.Equal("chevron.right", chevron.SymbolName);
                Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
                // :2625-2629 a hairline at 0.6 opacity with half a row gap on each side, after the header.
                var rule = Assert.Single(Rules(block));
                Assert.Equal(new Thickness(0, TimelineMetrics.RowSpacing / 2), rule.Margin);
                Assert.True(stack.Children.IndexOf(stack.Children.First(child => LogicalQuery.SelfAndDescendants<Border>(child).Contains(rule))) > 1);
                // Only the reply after the last tool call is the answer; the narration before it stays folded away.
                var answers = LogicalQuery.SelfAndDescendants<FoldedAnswerView>(block).ToList();
                var answer = Assert.Single(answers);
                Assert.Equal(CodeOnlyReply, LogicalQuery.SelfAndDescendants<MarkdownView>(answer).Single().Text);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolRow>(block));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // App/Views/Chat/TimelineRows.swift:2597, :2624
    public void A_finished_turn_with_nothing_to_show_has_no_rule()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var content = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Command, "make"), turn)]);
                var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty, content, null, canUndo: false, chronological: false);
                var window = Show(block);
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(block));
                Assert.Empty(Rules(block));
                Assert.Empty(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(block));
                Close(window);
            });
        });
    }

    // ================================================================ part 2 of 6
    // PR-CHAT-105 to 116, 123, 143 to 153. The private members the Swift keeps private (a view's popover body, its
    // static labels) are reached by reflection, as AppModelHydraMergeTests does, so their text is read from the port
    // itself and compared with the Swift literals.

    private static object? Invoke(object target, string name, params object?[] arguments) =>
        (target.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("no private method " + name)).Invoke(target, arguments);

    private static object? InvokeStatic(System.Type type, string name, params object?[] arguments) =>
        (type.GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException("no private static method " + name)).Invoke(null, arguments);

    [Fact]
    [Trait("Req", "PR-CHAT-082")] // App/Views/Chat/TimelineRows.swift:184-190, :214-228
    public void Edit_from_here_posts_only_the_confirmation_intent_and_a_non_revertable_menu_cannot_post_it()
    {
        OnMain(headless, () =>
        {
            var requests = new MessageMenuRequests();
            var confirming = new Binding<bool>(() => requests.ConfirmRevert, value => requests.ConfirmRevert = value);
            var allowed = Assert.IsAssignableFrom<IReadOnlyList<RowAction>>(InvokeStatic(typeof(UserMessageRow), "MessageActions", "prompt", true, confirming));
            Assert.False(requests.ConfirmRevert);
            var edit = Assert.Single(allowed, action => action.Title == "Edit from here");
            Assert.True(edit.StartsGroup);
            edit.Action();
            Assert.True(requests.ConfirmRevert);
            requests.ConfirmRevert = false;
            var disallowed = Assert.IsAssignableFrom<IReadOnlyList<RowAction>>(InvokeStatic(typeof(UserMessageRow), "MessageActions", "prompt", false, confirming));
            Assert.DoesNotContain(disallowed, action => action.Title == "Edit from here");
            Assert.False(requests.ConfirmRevert);
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // App/Views/Chat/TimelineRows.swift:2631-2680; PR-CHAT-104.windows_path (identity swap)
    public void Both_finished_turn_body_branches_remain_opaque_and_leave_immediately_under_the_fold_animation()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var clock = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty,
                    new ValueList<TimelineEntry>([ReplyEntry(CodeOnlyReply, turn: turn)]), null, false, false);
                var derived = Invoke(block, "DerivedFor", false);
                var answer = Assert.IsAssignableFrom<Control>(Invoke(block, "Answer", derived));
                var steps = Assert.IsAssignableFrom<Control>(Invoke(block, "Steps", derived, false));
                foreach (var branch in new[] { answer, steps })
                {
                    var host = new Panel();
                    var window = Show(host);
                    Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () => SwiftTransition.Insert(host, 0, branch));
                    Assert.Equal(1, branch.Opacity);
                    Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () => SwiftTransition.Remove(host, branch));
                    Assert.Empty(host.Children);
                    Close(window);
                }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-078")] // App/Runtime/ThreadRuntime.swift:71-99; App/Views/Chat/TimelineRows.swift:92
    public void The_inherited_quote_reader_consumes_only_leading_blocks_and_rejoins_the_lifted_command()
    {
        var (quotes, body) = ReplyQuote.Peel("/ask\n\n> first\n> second\n\n> third\n\n\nanswer\n> still body");
        Assert.Equal(["first\nsecond", "third"], quotes.Select(quote => quote.Text));
        Assert.Equal("/ask answer\n> still body", body);
        var (none, unchanged) = ReplyQuote.Peel("answer\n> not leading");
        Assert.Empty(none);
        Assert.Equal("answer\n> not leading", unchanged);
    }

    private static TimelineEntry PlanEntry(ProposedPlan.State state, string markdown = "```\nstep one\n```", System.Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = markdown, ProposedPlanState = state })));

    private static TimelineItem.Content.Todos Steps(params (string Text, TodoStep.Status Status)[] steps) =>
        new(new ValueList<TodoStep>(steps.Select(step => new TodoStep { Text = step.Text, TodoStepStatus = step.Status })));

    private static TimelineEntry NoticeEntry(Notice.Level level, string message, System.Guid? turn = null) =>
        new(new TimelineItem(turn, new TimelineItem.Content.Notice(new Notice { NoticeLevel = level, Message = message })));

    private static FileChangeSummary Changes(int count) =>
        new(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);

    private static UserMessage Report(string text, params long[] heads) =>
        new() { Text = text, HydraHeads = new ValueList<long>(heads) };

    private static TextBlock TextNamed(Control root, string text) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(root).Single(block => block.Text == text);

    private static double FontSize(SwiftFont font) => font.Resolve().FontSize;

    // Shows the window and presses a key in it, so the badge popovers find the window the current event is in
    // (WindowRectAnchor.Target, ChatBadgeTests' precedent).
    private static AvaloniaWindow ShowForPopover(Control control)
    {
        var window = Show(control);
        _ = AppCurrentEvent.CurrentWindow;
        window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
        Settle(window);
        return window;
    }

    // A lead with head threads under it and the lead's runtime: ThreadRuntimePart12Harness's setup with the heads added.
    private static (AppModelHarness Store, AppModel App, ThreadRuntime Runtime) TeamHarness(params HydraHeadInfo[] heads)
    {
        System.Threading.Tasks.Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
        var store = new AppModelHarness();
        var project = AppModelSamples.Project("Team", store.NewFolder("team"));
        var lead = AppModelSamples.Thread(project);
        var team = heads.Select(head => AppModelSamples.Thread(project) with { ParentThreadID = lead.Id, Hydra = head }).ToArray();
        store.WriteLibrary([project], [lead, .. team]);
        AppModelHarness.WriteDocument(lead.Id);
        var app = store.MakeModel();
        store.PrepareBootstrap(app);
        var runtime = store.Main.OnMain(() => new ThreadRuntime(lead.Id, app));
        return (store, app, runtime);
    }

    private static HydraHeadInfo Head(long index, HydraHeadInfo.Status status) =>
        new HydraHeadInfo(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with { HydraHeadInfoStatus = status };

    // ---------------------------------------------------------------- PR-CHAT-105 Derived

    [Fact]
    [Trait("Req", "PR-CHAT-105")] // App/Views/Chat/TimelineRows.swift:2547-2579, :2664-2680
    public void A_folded_turns_answer_is_the_replies_after_its_last_tool_with_plans_and_warnings_below_and_info_folded_away()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                const string before = "```\nnarration\n```";
                const string first = "```\nfirst answer\n```";
                const string second = "```\nsecond answer\n```";
                var content = new ValueList<TimelineEntry>(
                [
                    ReplyEntry(before, turn: turn),
                    ToolEntry(Call(ToolCall.Kind.Command, "make"), turn),
                    ReplyEntry(first, turn: turn),
                    PlanEntry(ProposedPlan.State.Accepted, turn: turn),
                    ReplyEntry(second, turn: turn),
                    NoticeEntry(Notice.Level.Warning, "careful", turn),
                    NoticeEntry(Notice.Level.Info, "fyi", turn),
                    NoticeEntry(Notice.Level.Error, "boom", turn),
                ]);
                var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty, content, null, canUndo: false, chronological: false);
                var window = Show(block);
                // :2566-2573 the tool ends the answer so far; :2560-2565 a plan and a notice do not.
                Assert.Equal([first, second], LogicalQuery.SelfAndDescendants<FoldedAnswerView>(block).Select(view => LogicalQuery.SelfAndDescendants<MarkdownView>(view).Single().Text));
                Assert.Single(LogicalQuery.SelfAndDescendants<PlanCard>(block));
                // :2562-2565 only warnings and errors are collected; the info notice stays behind the chevron.
                var notices = LogicalQuery.SelfAndDescendants<NoticeRow>(block).ToList();
                Assert.Equal(2, notices.Count);
                Assert.Contains("careful", Texts(notices[0]));
                Assert.Contains("boom", Texts(notices[1]));
                Assert.DoesNotContain("fyi", Texts(block));
                // :2666-2677 the answers, then the plans, then the notices, in one column.
                var column = Assert.IsType<StackPanel>(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(block).First().Parent);
                Assert.Equal([typeof(FoldedAnswerView), typeof(FoldedAnswerView), typeof(PlanCard), typeof(NoticeRow), typeof(NoticeRow)], column.Children.Select(child => child.GetType()));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-105")] // App/Views/Chat/TimelineRows.swift:2551-2559, :2578-2579, :2597
    public void A_turn_that_ended_on_a_tool_shows_its_last_reply_a_blank_reply_is_none_and_a_plan_alone_is_a_response()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var endedOnTool = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty,
                    new ValueList<TimelineEntry>([ReplyEntry(CodeOnlyReply, turn: turn), ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn), ReplyEntry(" \n\t ", turn: turn)]),
                    null, canUndo: false, chronological: false);
                var blankOnly = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty,
                    new ValueList<TimelineEntry>([ReplyEntry("   \n", turn: turn)]), null, canUndo: false, chronological: false);
                var planOnly = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty,
                    new ValueList<TimelineEntry>([PlanEntry(ProposedPlan.State.Dismissed, turn: turn)]), null, canUndo: false, chronological: false);
                var panel = new StackPanel();
                panel.Children.Add(endedOnTool);
                panel.Children.Add(blankOnly);
                panel.Children.Add(planOnly);
                var window = Show(panel);
                // :2578 no reply after the last tool: the last words it said, not the blank one after it.
                var answer = Assert.Single(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(endedOnTool));
                Assert.Equal(CodeOnlyReply, LogicalQuery.SelfAndDescendants<MarkdownView>(answer).Single().Text);
                // :2551-2559 a reply of whitespace alone is no response at all.
                Assert.Empty(Rules(blankOnly));
                Assert.Empty(LogicalQuery.SelfAndDescendants<FoldedAnswerView>(blankOnly));
                // :2579 a plan is a response of its own.
                Assert.Single(Rules(planOnly));
                Assert.Single(LogicalQuery.SelfAndDescendants<PlanCard>(planOnly));
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-106 memo

    [Fact]
    [Trait("Req", "PR-CHAT-106")] // App/Views/Chat/TimelineRows.swift:2511-2521, :2590-2591, :2703-2706
    public void The_turns_derivation_is_read_back_while_its_entries_and_fold_are_the_same_and_stored_once_the_body_has_run()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty,
                    new ValueList<TimelineEntry>([ReplyEntry(CodeOnlyReply, turn: turn)]), null, canUndo: false, chronological: false);
                var stored = typeof(TurnFinishedBlock).GetField("derived", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                // Nothing is derived before the body has run.
                Assert.Null(stored.GetValue(block));
                var window = Show(block);
                Assert.NotNull(stored.GetValue(block));
                var first = Invoke(block, "DerivedFor", false);
                var again = Invoke(block, "DerivedFor", false);
                Assert.Same(first, again);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-107 legacy changes

    [Fact]
    [Trait("Req", "PR-CHAT-107")] // App/Views/Chat/TimelineRows.swift:2707-2714; App/Runtime/ThreadRuntime.swift:3751-3757
    public void An_older_turn_with_files_but_no_per_file_summary_works_its_changes_out_and_stores_them_while_one_with_none_never_does()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var store = new AppModelHarness();
            var project = AppModelSamples.Project("Legacy", store.NewFolder("legacy turn"));
            var thread = AppModelSamples.Thread(project);
            store.WriteLibrary([project], [thread]);
            var withFiles = System.Guid.NewGuid();
            var withNone = System.Guid.NewGuid();
            DiskWriter.Shared.WriteSynchronously(
                new ThreadDocument(thread.Id)
                {
                    Turns = new ValueList<TurnRecord>(
                    [
                        new TurnRecord { Id = withFiles, Index = 0, Status = TurnStatus.Completed },
                        new TurnRecord { Id = withNone, Index = 1, Status = TurnStatus.Completed },
                    ]),
                    Items = new ValueList<TimelineItem>(
                    [
                        new TimelineItem(withFiles, new TimelineItem.Content.User(new UserMessage { Text = "first" })),
                        new TimelineItem(withNone, new TimelineItem.Content.User(new UserMessage { Text = "second" })),
                    ]),
                },
                Storage.ThreadURL(thread.Id));
            var model = store.MakeModel();
            store.PrepareBootstrap(model);
            var runtime = store.Main.OnMain(() => model.Runtime(thread.Id));
            store.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
            FileChangeSummary? Stored(System.Guid turn)
            {
                FileChangeSummary? Read() => Assert.IsType<TimelineItem.Content.TurnEnd>(runtime.Entries.Single(entry => entry.TurnID == turn && entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd).Item.TimelineItemContent).Value.Changes;
                return MainActor.IsIsolated ? Read() : store.Main.OnMain(Read);
            }
            AvaloniaWindow? window = null;
            store.Main.OnMain(() =>
            {
                var content = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Command, "make"))]);
                var files = new TurnFinishedBlock(runtime, withFiles, Summary(withFiles) with { FilesChanged = 2 }, ValueList<TimelineEntry>.Empty, content, null, canUndo: false, chronological: false);
                var none = new TurnFinishedBlock(runtime, withNone, Summary(withNone), ValueList<TimelineEntry>.Empty, content, null, canUndo: false, chronological: false);
                var panel = new StackPanel();
                panel.Children.Add(files);
                panel.Children.Add(none);
                window = Show(panel);
            });
            // :2709-2713 the diff is parsed once, off the main thread, and the summary written back to the turn's end.
            store.Main.Loop.RunUntil(() => Stored(withFiles) is not null, AppModelHarness.Bound);
            store.Main.RunUntilIdle();
            // :2708 no files changed and no edits: nothing is worked out.
            Assert.Null(Stored(withNone));
            store.Main.OnMain(() =>
            {
                Close(window!);
                runtime.CancelPersistence();
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-108 to 110 TurnFileCard

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // App/Views/Chat/TimelineRows.swift:2735-2765
    public void The_file_card_reads_Edited_1_file_or_Edited_N_files_beside_the_badge_and_the_totals_with_a_chevron()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
            var one = new TurnFileCard(Changes(1), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var three = new TurnFileCard(Changes(3), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var panel = new StackPanel();
            panel.Children.Add(one);
            panel.Children.Add(three);
            var window = Show(panel);
            Assert.Contains("Edited 1 file", Texts(one));
            var title = TextNamed(three, "Edited 3 files");
            Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), title.FontSize);
            Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium).Resolve().FontWeight, title.FontWeight);
            Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), title), BrushColor(title.Foreground));
            var badge = LogicalQuery.SelfAndDescendants<SwiftImage>(three).Single(image => image.Width == 18 && image.Height == 18);
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, badge), BrushColor(badge.Foreground));
            var stat = Assert.Single(LogicalQuery.SelfAndDescendants<DiffStatLabel>(three));
            Assert.Equal(40, stat.Additions);
            Assert.Equal(7, stat.Deletions);
            var titleButton = LogicalQuery.SelfAndDescendants<Button>(three).First();
            Assert.True(titleButton.IsEnabled);
            Assert.Equal("Show the files this turn changed", ToolTip.GetTip(titleButton));
            var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(titleButton).Single(image => image.SymbolName == "chevron.right");
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, chevron), BrushColor(chevron.Foreground));
            Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold)), SwiftImage.GetFont(chevron)!.Resolve().FontSize);
            Assert.Equal("Edited 3 files", AutomationProperties.GetName(three));
            Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // App/Views/Chat/TimelineRows.swift:2753-2760
    public void A_card_with_no_files_has_a_disabled_title_and_no_chevron()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                var card = new TurnFileCard(new FileChangeSummary(ValueList<FileChangeSummary.File>.Empty, 0, 0), canUndo: false, onRevert: () => { }, onReview: _ => { });
                var window = Show(card);
                Assert.Contains("Edited 0 files", Texts(card));
                var titleButton = LogicalQuery.SelfAndDescendants<Button>(card).First();
                Assert.False(titleButton.IsEnabled);
                Assert.DoesNotContain("chevron.right", Symbols(card));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // App/Views/Chat/TimelineRows.swift:2766-2827
    public void The_round_controls_follow_a_20_point_gap_revert_only_when_undoable_on_a_hugging_quaternary_pill()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
            var undoable = new TurnFileCard(Changes(2), canUndo: true, onRevert: () => { }, onReview: _ => { });
            var fixedTurn = new TurnFileCard(Changes(2), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var panel = new StackPanel();
            panel.Children.Add(undoable);
            panel.Children.Add(fixedTurn);
            var window = Show(panel);
            Assert.Equal(["arrow.uturn.backward", "plusminus"], LogicalQuery.SelfAndDescendants<MutedRoundGlyph>(undoable).Select(glyph => Symbols(glyph).Single()));
            Assert.Equal(["plusminus"], LogicalQuery.SelfAndDescendants<MutedRoundGlyph>(fixedTurn).Select(glyph => Symbols(glyph).Single()));
            var buttons = LogicalQuery.SelfAndDescendants<Button>(undoable).ToList();
            var revert = buttons.Single(button => LogicalQuery.SelfAndDescendants<MutedRoundGlyph>(button).Any(glyph => Symbols(glyph).Contains("arrow.uturn.backward")));
            var review = buttons.Single(button => LogicalQuery.SelfAndDescendants<MutedRoundGlyph>(button).Any(glyph => Symbols(glyph).Contains("plusminus")));
            Assert.Equal("Revert this turn's files and conversation", ToolTip.GetTip(revert));
            Assert.Equal("Show this turn's changes", ToolTip.GetTip(review));
            Assert.False(revert.Focusable);
            Assert.False(review.Focusable);
            // :2768 a fixed 20 by 1 gap, not a spacer, right after the title.
            var row = Assert.IsType<StackPanel>(buttons[0].Parent is StackPanel ? buttons[0].Parent : buttons[0].Parent!.Parent);
            Assert.Equal(8, row.Spacing);
            var gap = row.Children[1];
            Assert.Equal(20, gap.Width);
            Assert.Equal(1, gap.Height);
            // :2815-2819 padded 12 leading, 6 trailing and 6 vertical on .quaternary at 0.32, hugging its content.
            var pill = LogicalQuery.SelfAndDescendants<Border>(undoable).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, 6, 6, 6));
            Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
            Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), pill), BrushColor(pill.Background));
            Assert.Equal(HorizontalAlignment.Left, pill.HorizontalAlignment);
            Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // App/Views/Chat/TimelineRows.swift:2463-2478
    public void A_muted_round_glyph_is_a_28_point_overlay_disc_whose_glyph_and_fill_come_up_under_the_pointer()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            MutedRoundGlyph? glyph = null;
            AvaloniaWindow? window = null;
            runtime.OnMain(() =>
            {
                glyph = new MutedRoundGlyph("plusminus");
                var host = new StackPanel { Margin = new Thickness(20) };
                host.Children.Add(glyph);
                window = Show(host);
                var disc = LogicalQuery.SelfAndDescendants<Border>(glyph).Single(border => border.Width == 28 && border.Height == 28);
                Assert.Equal(new CornerRadius(14), disc.CornerRadius);
                var image = LogicalQuery.SelfAndDescendants<SwiftImage>(glyph).Single();
                Assert.Equal("plusminus", image.SymbolName);
                Assert.Equal(FontSize(ChromeTokens.IconFont), SwiftImage.GetFont(image)!.Resolve().FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, image), BrushColor(image.Foreground));
                Assert.Equal(Resolved(ChromeTokens.Overlay(0.1), disc), BrushColor(disc.Background));
                Hover(window, disc);
            });
            // Chrome.hover, an ease-out of 0.1 s on the frame clock.
            frames.Advance(System.TimeSpan.Zero);
            runtime.AdvanceMilliseconds(1000);
            frames.Advance(System.TimeSpan.FromMilliseconds(1000));
            runtime.OnMain(() =>
            {
                var disc = LogicalQuery.SelfAndDescendants<Border>(glyph!).Single(border => border.Width == 28 && border.Height == 28);
                var image = LogicalQuery.SelfAndDescendants<SwiftImage>(glyph!).Single();
                Assert.Equal(Resolved(ChromeTokens.PrimaryText, image), BrushColor(image.Foreground));
                Assert.Equal(Resolved(ChromeTokens.Overlay(0.18), disc), BrushColor(disc.Background));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // App/Views/Chat/TimelineRows.swift:2796-2812
    public void The_review_control_hands_its_own_anchor_to_the_review_once_the_pointer_has_been_there_and_does_nothing_before()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var reviewed = new List<Control>();
            TurnFileCard? card = null;
            AvaloniaWindow? window = null;
            runtime.OnMain(() =>
            {
                card = new TurnFileCard(Changes(2), canUndo: false, onRevert: () => { }, onReview: reviewed.Add);
                var host = new StackPanel { Margin = new Thickness(20) };
                host.Children.Add(card);
                window = Show(host);
                var review = LogicalQuery.SelfAndDescendants<Button>(card).Last();
                Click(review);
                Assert.Empty(reviewed);
                Assert.Empty(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(card));
                Hover(window, review);
            });
            runtime.OnMain(() =>
            {
                Settle(window!);
                var anchor = Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(card!));
                Click(LogicalQuery.SelfAndDescendants<Button>(card!).Last());
                Assert.Same(anchor, Assert.Single(reviewed));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-109")] // App/Views/Chat/TimelineRows.swift:2829-2858
    public void The_files_popover_lists_twelve_files_then_and_1_more_file_or_and_N_more_files()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            runtime.OnMain(() =>
            {
            string[] Lines(int count)
            {
                var card = new TurnFileCard(Changes(count), canUndo: false, onRevert: () => { }, onReview: _ => { });
                return Texts(Assert.IsAssignableFrom<Control>(Invoke(card, "FilesPopover")));
            }
            var twelve = Lines(12);
            Assert.Equal(12, twelve.Count(text => text.StartsWith("src/file", System.StringComparison.Ordinal)));
            Assert.DoesNotContain(twelve, text => text.StartsWith("and ", System.StringComparison.Ordinal));
            var thirteen = Lines(13);
            Assert.Equal(12, thirteen.Count(text => text.StartsWith("src/file", System.StringComparison.Ordinal)));
            Assert.DoesNotContain("src/file13.swift", thirteen);
            Assert.Contains("and 1 more file", thirteen);
            Assert.Contains("and 8 more files", Lines(20));

            var binary = new TurnFileCard(
                new FileChangeSummary(new ValueList<FileChangeSummary.File>([new("logo.png", 0, 0, true), new("a.swift", 3, 2, false)]), 3, 2),
                canUndo: false, onRevert: () => { }, onReview: _ => { });
            var popover = Assert.IsAssignableFrom<Control>(Invoke(binary, "FilesPopover"));
            var word = TextNamed(popover, "Binary");
            Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption)), word.FontSize);
            var path = TextNamed(popover, "logo.png");
            Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout)), path.FontSize);
            Assert.Equal(1, path.MaxLines);
            var stat = Assert.Single(LogicalQuery.SelfAndDescendants<DiffStatLabel>(popover));
            Assert.Equal(3, stat.Additions);
            Assert.Equal(2, stat.Deletions);
            // :2849-2850 each row padded 12 horizontally and 7 vertically.
            var rows = Assert.IsType<StackPanel>(LogicalQuery.SelfAndDescendants<StackPanel>(popover).First(panel => panel.Orientation == Orientation.Vertical));
            Assert.Equal(0, rows.Spacing);
            Assert.All(rows.Children, control => Assert.Equal(new Thickness(12, 7), control.Margin));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // App/Views/Chat/TimelineRows.swift:2770-2798, :2695
    public void The_revert_disc_asks_in_a_300_point_popover_where_only_Revert_reverts()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var reverts = 0;
            TurnFileCard? card = null;
            AvaloniaWindow? window = null;
            runtime.OnMain(() =>
            {
                card = new TurnFileCard(Changes(2), canUndo: true, onRevert: () => reverts++, onReview: _ => { });
                window = Show(card);
                var revert = LogicalQuery.SelfAndDescendants<Button>(card).Single(button => ToolTip.GetTip(button) as string == "Revert this turn's files and conversation");
                Click(revert);
            });
            runtime.OnMain(() =>
            {
            Settle(window!);
            Assert.Single(presenter.Calls, call => call.Presented);

            var confirm = Assert.IsAssignableFrom<Control>(Invoke(card!, "RevertConfirmation"));
            var sheet = LogicalQuery.SelfAndDescendants<Control>(confirm).First(control => control.Width == 300);
            var heading = TextNamed(confirm, "Revert up to this point?");
            Assert.Equal(FontSize(SwiftFont.System(14, SwiftFont.Weight.Semibold)), heading.FontSize);
            Assert.Equal(SwiftFont.System(14, SwiftFont.Weight.Semibold).Resolve().FontWeight, heading.FontWeight);
            var message = TextNamed(confirm, "Files go back to how they were before this turn, and the turn leaves the conversation.");
            Assert.Equal(FontSize(SwiftFont.System(SwiftFont.TextStyle.Callout)), message.FontSize);
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, message), BrushColor(message.Foreground));
            Assert.Equal(TextWrapping.Wrap, message.TextWrapping);
            Assert.Contains(LogicalQuery.SelfAndDescendants<Decorator>(sheet), decorator => decorator.Padding == new Thickness(16));
            var cancel = LogicalQuery.SelfAndDescendants<Button>(confirm).Single(button => Texts(button).Contains("Cancel"));
            var go = LogicalQuery.SelfAndDescendants<Button>(confirm).Single(button => Texts(button).Contains("Revert"));
            Assert.True(go.IsDefault);
            Assert.False(cancel.IsDefault);
            Click(cancel);
            Assert.Equal(0, reverts);
            Click(go);
            Assert.Equal(1, reverts);
            Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-111 to 113 PlanCard

    [Fact]
    [Trait("Req", "PR-CHAT-111")] // App/Views/Chat/TimelineRows.swift:2263-2278, :2318-2337
    public void A_plans_badge_title_caption_and_attention_follow_its_state()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var states = new[] { ProposedPlan.State.Drafting, ProposedPlan.State.Proposed, ProposedPlan.State.Accepted, ProposedPlan.State.Dismissed };
                var cards = states.Select(state => new PlanCard(PlanEntry(state), h.Runtime)).ToArray();
                var panel = new StackPanel();
                foreach (var card in cards) panel.Children.Add(card);
                var window = Show(panel);
                string[] Words(PlanCard card) => Texts(card).Where(text => text.Length != 0).ToArray();
                Assert.Equal(["Writing a plan"], Words(cards[0]));
                Assert.Equal(["Plan ready", "Implement or dismiss"], Words(cards[1]));
                Assert.Equal(["Plan", "Approved"], Words(cards[2]));
                Assert.Equal(["Plan", "Dismissed"], Words(cards[3]));
                // :2335-2337 only a proposed plan draws the attention dots.
                Assert.Equal([false, true, false, false], cards.Select(card => LogicalQuery.SelfAndDescendants<DotFieldControl>(card).Any()));
                // :2272-2276 the spinner while drafting, the clipboard in the tint otherwise.
                var spinner = Assert.Single(LogicalQuery.SelfAndDescendants<WorkingSpinner>(cards[0]));
                Assert.Equal(14, spinner.Width);
                Assert.DoesNotContain("list.bullet.clipboard", Symbols(cards[0]));
                var clipboard = LogicalQuery.SelfAndDescendants<SwiftImage>(cards[1]).Single(image => image.SymbolName == "list.bullet.clipboard");
                Assert.Equal(Resolved(ChromeTokens.Accent, clipboard), BrushColor(clipboard.Foreground));
                Assert.All(cards, card => Assert.Contains("chevron.right", Symbols(card)));
                Assert.All(cards, card => Assert.True(LogicalQuery.SelfAndDescendants<Button>(card).First().IsEnabled));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-111")] // App/Views/Chat/TimelineRows.swift:2268-2270
    public void A_plan_with_empty_markdown_has_no_chevron_and_cannot_be_opened()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var card = new PlanCard(PlanEntry(ProposedPlan.State.Drafting, markdown: ""), h.Runtime);
                var window = Show(card);
                Assert.DoesNotContain("chevron.right", Symbols(card));
                Assert.False(LogicalQuery.SelfAndDescendants<Button>(card).First().IsEnabled);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-111")] // App/Views/Chat/TimelineRows.swift:2263-2264
    public void A_plan_card_follows_its_entry_as_the_plan_moves_from_drafting_to_proposed()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var entry = PlanEntry(ProposedPlan.State.Drafting);
            PlanCard? card = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                card = new PlanCard(entry, h.Runtime);
                window = Show(card);
                Assert.Contains("Writing a plan", Texts(card));
                entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "```\nstep one\n```", ProposedPlanState = ProposedPlan.State.Proposed }) };
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Contains("Plan ready", Texts(card!));
                Assert.Contains("Implement or dismiss", Texts(card!));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-112")] // App/Views/Chat/TimelineRows.swift:2279-2313
    public void The_plan_popover_is_520_wide_with_the_markdown_in_a_420_point_scroller_and_the_two_buttons_only_while_proposed()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var proposed = PlanEntry(ProposedPlan.State.Proposed);
                var card = new PlanCard(proposed, h.Runtime);
                var plan = ((TimelineItem.Content.Plan)proposed.Item.TimelineItemContent).Value;
                var detail = Assert.IsAssignableFrom<Control>(Invoke(card, "Detail", plan));
                var window = Show(detail);
                var sheet = LogicalQuery.SelfAndDescendants<Control>(detail).First(control => control.Width == 520);
                Assert.Contains(LogicalQuery.SelfAndDescendants<Decorator>(sheet), decorator => decorator.Padding == new Thickness(16));
                var heading = TextNamed(detail, "Plan");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Headline)), heading.FontSize);
                // :2281-2284 the header's one copy button. Markdown's code block has its own copy control (:1220).
                Assert.Single(LogicalQuery.SelfAndDescendants<CopyButton>((Control)heading.Parent!));
                Assert.Single(LogicalQuery.SelfAndDescendants<CopyButton>(detail), copy => copy.GetLogicalAncestors().OfType<MarkdownView>().Any());
                var scroll = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverScroll>(detail));
                var markdown = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(scroll));
                Assert.Equal("```\nstep one\n```", markdown.Text);
                Assert.Equal(new Thickness(16, 0), markdown.Margin);
                Assert.True(Assert.IsType<ScrollViewer>(scroll.Child).Height <= 420);
                Assert.Equal(["Implement plan", "Dismiss"], LogicalQuery.SelfAndDescendants<Button>(detail).Select(button => Texts(button).FirstOrDefault(text => text.Length != 0)).Where(text => text is "Implement plan" or "Dismiss"));
                Close(window);

                var accepted = PlanEntry(ProposedPlan.State.Accepted);
                var done = Assert.IsAssignableFrom<Control>(Invoke(new PlanCard(accepted, h.Runtime), "Detail", ((TimelineItem.Content.Plan)accepted.Item.TimelineItemContent).Value));
                Assert.DoesNotContain("Implement plan", Texts(done));
                Assert.DoesNotContain("Dismiss", Texts(done));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-112")] // App/Views/Chat/TimelineRows.swift:2290-2308
    public void The_plan_buttons_stay_greyed_while_the_thread_is_busy_with_the_reason_and_carry_no_tip_when_free()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            Control? detail = null;
            AvaloniaWindow? window = null;
            Panel Row() => (Panel)LogicalQuery.SelfAndDescendants<Button>(detail!).First(button => Texts(button).Contains("Implement plan")).GetLogicalAncestors().OfType<Panel>()
                .First(panel => LogicalQuery.SelfAndDescendants<Button>(panel).Count() == 2);
            h.Main.OnMain(() =>
            {
                var entry = PlanEntry(ProposedPlan.State.Proposed);
                detail = Assert.IsAssignableFrom<Control>(Invoke(new PlanCard(entry, h.Runtime), "Detail", ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value));
                window = Show(detail);
                Assert.True(Row().IsEnabled);
                Assert.Null(ToolTip.GetTip(Row()));
                h.Runtime.RehearseTurn("busy");
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var row = Row();
                Assert.False(row.IsEnabled);
                Assert.Equal("Wait for the current turn to finish", ToolTip.GetTip(row));
                Assert.True(ToolTip.GetShowOnDisabled(row));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<Button>(row).Count());
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-112")] // App/Views/Chat/TimelineRows.swift:2296-2305
    public void Dismiss_closes_the_popover_and_dismisses_the_plan_and_Implement_plan_closes_it_and_implements()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var store = new AppModelHarness();
            var project = AppModelSamples.Project("Plans", store.NewFolder("plans"));
            var thread = AppModelSamples.Thread(project);
            store.WriteLibrary([project], [thread]);
            var turn = System.Guid.NewGuid();
            var planItem = new TimelineItem(turn, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = "```\nstep one\n```", ProposedPlanState = ProposedPlan.State.Proposed }));
            DiskWriter.Shared.WriteSynchronously(
                new ThreadDocument(thread.Id)
                {
                    Turns = new ValueList<TurnRecord>([new TurnRecord { Id = turn, Index = 0, Status = TurnStatus.Completed }]),
                    Items = new ValueList<TimelineItem>([new TimelineItem(turn, new TimelineItem.Content.User(new UserMessage { Text = "plan it" })), planItem]),
                },
                Storage.ThreadURL(thread.Id));
            var model = store.MakeModel();
            store.PrepareBootstrap(model);
            var runtime = store.Main.OnMain(() => model.Runtime(thread.Id));
            store.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
            store.Main.OnMain(() =>
            {
                var entry = runtime.Entries.Single(candidate => candidate.Id == planItem.Id);
                var card = new PlanCard(entry, runtime);
                var shown = typeof(PlanCard).GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(card)!;
                var isShowing = shown.GetType().GetProperty("IsShowingPlan")!;
                isShowing.SetValue(shown, true);
                var detail = Assert.IsAssignableFrom<Control>(Invoke(card, "Detail", ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value));
                Click(LogicalQuery.SelfAndDescendants<Button>(detail).Single(button => Texts(button).Contains("Dismiss")));
                Assert.Equal(false, isShowing.GetValue(shown));
                Assert.Equal(ProposedPlan.State.Dismissed, ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value.ProposedPlanState);
                runtime.CancelPersistence();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-113")] // App/Views/Chat/TimelineRows.swift:2287
    public void A_drafting_plan_renders_with_the_streaming_markdown_and_a_proposed_one_with_the_finished_prose()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                bool Streams(ProposedPlan.State state)
                {
                    var entry = PlanEntry(state);
                    var detail = Assert.IsAssignableFrom<Control>(Invoke(new PlanCard(entry, h.Runtime), "Detail", ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value));
                    return LogicalQuery.SelfAndDescendants<MarkdownView>(detail).Single().IsStreaming;
                }
                Assert.True(Streams(ProposedPlan.State.Drafting));
                Assert.False(Streams(ProposedPlan.State.Proposed));
                Assert.False(Streams(ProposedPlan.State.Accepted));
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-114 TodoListRow

    [Fact]
    [Trait("Req", "PR-CHAT-114")] // App/Views/Chat/TimelineRows.swift:2343-2382
    public void A_checklist_is_a_card_headed_N_of_M_done_with_a_glyph_and_line_per_step()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
            var entry = new TimelineEntry(new TimelineItem(null, Steps(("Read the code", TodoStep.Status.Done), ("Write the fix", TodoStep.Status.Active), ("Run tests", TodoStep.Status.Pending))));
            var row = new TodoListRow(entry);
            var window = Show(row);
            var header = TextNamed(row, "1 of 3 done");
            Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption)), header.FontSize);
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, header), BrushColor(header.Foreground));
            Assert.Equal(["checklist", "checkmark.circle.fill", "circle.dotted.circle", "circle"], Symbols(row));
            var glyphs = LogicalQuery.SelfAndDescendants<SwiftImage>(row).ToList();
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, glyphs[0]), BrushColor(glyphs[0].Foreground));
            Assert.Equal(Resolved(ChromeTokens.Accent, glyphs[1]), BrushColor(glyphs[1].Foreground));
            Assert.Equal(Resolved(ChromeTokens.Accent, glyphs[2]), BrushColor(glyphs[2].Foreground));
            Assert.Equal(Resolved(SwiftColor.Tertiary, glyphs[3]), BrushColor(glyphs[3].Foreground));
            var done = TextNamed(row, "Read the code");
            Assert.Equal(Resolved(SwiftColor.Secondary, done), BrushColor(done.Foreground));
            var strike = Assert.Single(done.TextDecorations!);
            Assert.Equal(TextDecorationLocation.Strikethrough, strike.Location);
            Assert.Equal(Resolved(SwiftColor.Secondary, done), BrushColor(strike.Stroke));
            var active = TextNamed(row, "Write the fix");
            Assert.Equal(Resolved(SwiftColor.Primary, active), BrushColor(active.Foreground));
            Assert.True(active.TextDecorations is null || active.TextDecorations.Count == 0);
            Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout)), active.FontSize);
            var card = LogicalQuery.SelfAndDescendants<Border>(row).First(border => border.Padding == new Thickness(14));
            Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), card.CornerRadius);
            Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), card), BrushColor(card.Background));
            Assert.Equal(HorizontalAlignment.Left, card.HorizontalAlignment);
            Assert.Equal(96, row.Padding.Right);
            Assert.Equal(7, Assert.IsType<StackPanel>(card.Child).Spacing);
            Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-114")] // App/Views/Chat/TimelineRows.swift:2344, :2369-2371
    public void A_checklist_with_no_steps_draws_nothing_and_an_update_rewrites_the_same_rows_in_place()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            TodoListRow? row = null;
            TextBlock? before = null;
            AvaloniaWindow? window = null;
            runtime.OnMain(() =>
            {
                var empty = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps())));
                var entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
                row = new TodoListRow(entry);
                var panel = new StackPanel();
                panel.Children.Add(empty);
                panel.Children.Add(row);
                window = Show(panel);
                Assert.Empty(LogicalQuery.SelfAndDescendants<TextBlock>(empty));
                Assert.Empty(LogicalQuery.SelfAndDescendants<Border>(empty));
                before = TextNamed(row, "One");
                entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Active)) };
            });
            runtime.OnMain(() =>
            {
                Settle(window!);
                Assert.Contains("1 of 2 done", Texts(row!));
                Assert.Same(before, TextNamed(row!, "One"));
                Assert.Equal(["checklist", "checkmark.circle.fill", "circle.dotted.circle"], Symbols(row!));
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-115 NoticeRow

    [Fact]
    [Trait("Req", "PR-CHAT-115")] // App/Views/Chat/TimelineRows.swift:2388-2430
    public void An_info_notice_is_a_bare_line_of_secondary_selectable_text()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            runtime.OnMain(() =>
            {
                var row = new NoticeRow(NoticeEntry(Notice.Level.Info, "Context compacted"));
                var window = Show(row);
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single();
                Assert.Equal("info.circle", glyph.SymbolName);
                Assert.Equal(Resolved(SwiftColor.Secondary, glyph), BrushColor(glyph.Foreground));
                var message = Assert.Single(LogicalQuery.SelfAndDescendants<SelectableTextBlock>(row));
                Assert.Equal("Context compacted", message.Text);
                Assert.Equal(Resolved(SwiftColor.Secondary, message), BrushColor(message.Foreground));
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout)), message.FontSize);
                var pill = LogicalQuery.SelfAndDescendants<Border>(row).First();
                Assert.Equal(default, pill.Padding);
                Assert.Equal(Resolved(SwiftColor.Clear, pill), BrushColor(pill.Background));
                Assert.Equal(HorizontalAlignment.Stretch, pill.HorizontalAlignment);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-115")] // App/Views/Chat/TimelineRows.swift:2388-2430
    public void A_warning_or_error_notice_sits_in_a_full_width_pill_tinted_at_ten_percent_of_its_theme_status_colour()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            runtime.OnMain(() =>
            {
                var warning = new NoticeRow(NoticeEntry(Notice.Level.Warning, "Low on context"));
                var error = new NoticeRow(NoticeEntry(Notice.Level.Error, "The provider failed"));
                var panel = new StackPanel();
                panel.Children.Add(warning);
                panel.Children.Add(error);
                var window = Show(panel);
                foreach (var (row, symbol, color, text) in new[] { (warning, "exclamationmark.triangle", ChromeTokens.Warning, "Low on context"), (error, "exclamationmark.octagon", ChromeTokens.Danger, "The provider failed") })
                {
                    var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single();
                    Assert.Equal(symbol, glyph.SymbolName);
                    Assert.Equal(Resolved(color, glyph), BrushColor(glyph.Foreground));
                    var message = LogicalQuery.SelfAndDescendants<SelectableTextBlock>(row).Single();
                    Assert.Equal(text, message.Text);
                    Assert.Equal(Resolved(SwiftColor.Primary, message), BrushColor(message.Foreground));
                    var pill = LogicalQuery.SelfAndDescendants<Border>(row).First();
                    Assert.Equal(new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical), pill.Padding);
                    Assert.Equal(Resolved(color.Opacity(0.1), pill), BrushColor(pill.Background));
                    Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                    Assert.Equal(HorizontalAlignment.Stretch, pill.HorizontalAlignment);
                }
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-116 TurnEndRow

    [Fact]
    [Trait("Decision", "ED-197")] // App/Views/Chat/TimelineRows.swift:2448-2455; UI/Common/Components.swift:37-40; PR-CHAT-116
    [Trait("Oracle", "O-65")]
    public void The_B3_duration_rounds_half_seconds_away_from_zero_before_the_narrow_unit_formatter()
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        Assert.Equal("Worked for 3s", TurnEndRow.Label(Summary(System.Guid.NewGuid(), 2.5)));
        Assert.Equal("Worked for 0s", TurnEndRow.Label(Summary(System.Guid.NewGuid(), -2.5)));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-116")] // App/Views/Chat/TimelineRows.swift:2448-2455; UI/Common/Components.swift:36-41
    public void The_turn_end_line_reads_Worked_for_Stopped_after_or_Failed_after_and_the_formatted_duration()
    {
        var turn = System.Guid.NewGuid();
        Assert.Equal("Worked for 1m 4s", TurnEndRow.Label(Summary(turn, 64)));
        Assert.Equal("Worked for 5s", TurnEndRow.Label(Summary(turn, 5) with { Status = TurnStatus.Running }));
        Assert.Equal("Stopped after 59s", TurnEndRow.Label(Summary(turn, 59) with { Status = TurnStatus.Interrupted }));
        Assert.Equal("Failed after 2h 15m", TurnEndRow.Label(Summary(turn, 8100) with { Status = TurnStatus.Failed }));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-116")] // App/Views/Chat/TimelineRows.swift:2439-2445
    public void The_standalone_turn_end_line_shows_only_when_the_turn_produced_no_reply()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            runtime.OnMain(() =>
            {
                var turn = System.Guid.NewGuid();
                var silent = new TurnEndRow(Summary(turn, 64), hasReply: false);
                var replied = new TurnEndRow(Summary(turn, 64), hasReply: true);
                var panel = new StackPanel();
                panel.Children.Add(silent);
                panel.Children.Add(replied);
                var window = Show(panel);
                var line = TextNamed(silent, "Worked for 1m 4s");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption)), line.FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, line), BrushColor(line.Foreground));
                Assert.Equal(HorizontalAlignment.Stretch, line.HorizontalAlignment);
                Assert.Empty(LogicalQuery.SelfAndDescendants<TextBlock>(replied));
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-123 reveal call sites

    [Fact]
    [Trait("Req", "PR-CHAT-123")] // App/Views/Chat/TimelineRows.swift:1473, :1527-1528, :1697-1698
    public void The_reveal_fires_only_on_open_outside_the_work_groups_animation_and_inside_the_steps_and_the_tool_rows()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var reveals = new List<(string Site, bool Animated)>();
            var follows = 0;
            var site = "";
            var showsAll = false;
            WorkGroup? group = null;
            ToolRow? tool = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                group = new WorkGroup(new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Edit, "Edit a")), ToolEntry(Call(ToolCall.Kind.Command, "ls"))]), h.Runtime, startsCollapsed: true);
                var three = new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a")), ToolEntry(Call(ToolCall.Kind.Read, "Read b")), ToolEntry(Call(ToolCall.Kind.Read, "Read c"))]);
                var steps = new WorkSteps(three, h.Runtime, new Binding<bool>(() => showsAll, value => showsAll = value));
                tool = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Command, "ls") with { Detail = "details" });
                var host = new StackPanel();
                host.Children.Add(group);
                host.Children.Add(steps);
                host.Children.Add(tool);
                ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox
                {
                    Action = () => reveals.Add((site, Transaction.Current is not null)),
                    Follow = () => follows++,
                });
                window = Show(host);
                site = "group";
                Click(LogicalQuery.SelfAndDescendants<Button>(group).First());
                site = "steps";
                Click(LogicalQuery.SelfAndDescendants<Button>(steps).Single(button => Texts(button).Contains("1 earlier step")));
                site = "tool";
                Click(LogicalQuery.SelfAndDescendants<Button>(tool).Single());
            });
            h.Main.OnMain(() =>
            {
                // Closing never reveals.
                site = "closing";
                Click(LogicalQuery.SelfAndDescendants<Button>(group!).First());
                Click(LogicalQuery.SelfAndDescendants<Button>(tool!).First());
            });
            h.Main.OnMain(() =>
            {
                Assert.Equal([("group", false), ("steps", true), ("tool", true)], reveals);
                Assert.Equal(0, follows);
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-143 to 146 HydraReportRow

    [Fact]
    [Trait("Req", "PR-CHAT-143")] // App/Views/Chat/TimelineRows.swift:340-373
    public void A_heads_report_pill_names_who_is_done_and_names_a_failed_or_stopped_head_with_its_outcome()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                string Title(UserMessage message)
                {
                    var row = new HydraReportRow(message);
                    var window = Show(row);
                    var title = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(block => block.MaxLines == 1).Text!;
                    Close(window);
                    return title;
                }
                Assert.Equal("Hank is done", Title(Report("## Hank: tidy\nall good", 0)));
                Assert.Equal("Hank and Walter are done", Title(Report("## Hank: tidy\n## Walter: build", 0, 1)));
                Assert.Equal("Hank, Walter and Ada are done", Title(Report("## Hank: a\n## Walter: b\n## Ada: c", 0, 1, 2)));
                Assert.Equal("Hank and Ada are done, Walter failed", Title(Report("## Hank: a\n## Walter: b (Failed: tests)\n## Ada: c", 0, 1, 2)));
                Assert.Equal("Ada stopped", Title(Report("  ## Ada: c (stopped by you)", 2)));
                // The outcome marker needs its opening parenthesis, and is read from the colon on.
                Assert.Equal("Hank is done", Title(Report("## Hank: failed once, fixed", 0)));
                // :350-351 one line, capped at 140 characters.
                var many = Enumerable.Range(0, 25).Select(i => (long)i).ToArray();
                var names = many.Select(i => HydraRoster.Persona(i).Name).ToList();
                var full = string.Join(", ", names.Take(24)) + " and " + names[24] + " are done";
                Assert.True(full.Length > 140);
                Assert.Equal(full[..139] + "\U00002026", Title(Report("report", many))); // U+2026 HORIZONTAL ELLIPSIS
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-143")] // App/Views/Chat/TimelineRows.swift:258-329
    public void A_heads_report_pill_overlaps_the_heads_glyphs_and_sits_in_the_tinted_pill_with_96_points_clear_on_the_trailing_side()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                var row = new HydraReportRow(Report("## Hank: a\nbody\n## Walter: b\nbody", 0, 1));
                var window = Show(row);
                var glyphs = LogicalQuery.SelfAndDescendants<HydraGlyph>(row).ToList();
                Assert.Equal(["Hank", "Walter"], glyphs.Select(glyph => glyph.Persona.Name));
                Assert.All(glyphs, glyph => Assert.Equal(18, glyph.Size));
                Assert.Equal(-4, Assert.IsType<StackPanel>(glyphs[0].Parent).Spacing);
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraMarkImage>(row));
                var title = TextNamed(row, "Hank and Walter are done");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), title.FontSize);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), title), BrushColor(title.Foreground));
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
                var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, chevron), BrushColor(chevron.Foreground));
                var button = LogicalQuery.SelfAndDescendants<Button>(row).Single();
                Assert.True(button.IsEnabled);
                Assert.Equal("Show the message", ToolTip.GetTip(button));
                var pill = LogicalQuery.SelfAndDescendants<Border>(row).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Assert.Equal(HorizontalAlignment.Left, pill.HorizontalAlignment);
                Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                Assert.Equal("Hank and Walter are done", AutomationProperties.GetName(row));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-143")] // App/Views/Chat/TimelineRows.swift:248-250, :264-268, :288-292, :332-334
    public void A_note_from_Hydra_shows_its_first_line_less_one_stop_beside_the_mark_and_with_nothing_behind_it_is_disabled_with_no_tip()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            runtime.OnMain(() =>
            {
                Assert.Equal("Held the heads back", HydraReportRow.WithoutTrailingStop("Held the heads back."));
                Assert.Equal("Wait..", HydraReportRow.WithoutTrailingStop("Wait..."));
                Assert.Equal("No stop", HydraReportRow.WithoutTrailingStop("No stop"));
                var note = new HydraReportRow(Report("Held the heads back.\n\n  The lead is busy.  ", []) with { HydraHeads = null });
                var bare = new HydraReportRow(Report("Just a title.") with { HydraHeads = ValueList<long>.Empty });
                var panel = new StackPanel();
                panel.Children.Add(note);
                panel.Children.Add(bare);
                var window = Show(panel);
                Assert.Contains("Held the heads back", Texts(note));
                var mark = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(note));
                Assert.Equal(18, mark.Width);
                Assert.Equal(18, mark.Height);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, mark), BrushColor(LogicalQuery.SelfAndDescendants<SwiftImage>(mark).Single().Foreground));
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraGlyph>(note));
                var noteButton = LogicalQuery.SelfAndDescendants<Button>(note).Single();
                Assert.True(noteButton.IsEnabled);
                Assert.Equal("Show the message", ToolTip.GetTip(noteButton));
                Assert.Contains("chevron.right", Symbols(note));
                // Nothing behind the title: a disabled control, no chevron and no tooltip at all.
                Assert.Contains("Just a title", Texts(bare));
                var bareButton = LogicalQuery.SelfAndDescendants<Button>(bare).Single();
                Assert.False(bareButton.IsEnabled);
                Assert.Null(ToolTip.GetTip(bareButton));
                Assert.DoesNotContain("chevron.right", Symbols(bare));
                // An empty note reads Hydra.
                var empty = new HydraReportRow(Report("") with { HydraHeads = null });
                var emptyWindow = Show(empty);
                Assert.Contains("Hydra", Texts(empty));
                Close(emptyWindow);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-143")] // App/Views/Chat/TimelineRows.swift:257-261, :293-303, :607-639
    public void Tapping_a_notes_pill_opens_the_note_in_the_report_popover()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            HydraReportRow? row = null;
            AvaloniaWindow? window = null;
            runtime.OnMain(() =>
            {
                row = new HydraReportRow(Report("Held back.\n" + CodeOnlyReply) with { HydraHeads = null });
                window = ShowForPopover(row);
                Click(LogicalQuery.SelfAndDescendants<Button>(row).Single());
            });
            runtime.OnMain(() =>
            {
                Settle(window!);
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                var popover = Assert.Single(LogicalQuery.SelfAndDescendants<HydraReportPopover>(shown.Popup.Child!));
                Assert.Equal(460, popover.Width);
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-144")] // App/Views/Chat/TimelineRows.swift:375-387
    public void A_leading_address_is_a_first_line_of_http_or_https_and_nothing_else_and_the_rest_is_the_body_past_it()
    {
        Assert.Equal("https://gitlab.com/g/p/-/merge_requests/7", HydraReportRow.LeadingURL("https://gitlab.com/g/p/-/merge_requests/7\nMerged.")!.AbsoluteString);
        Assert.NotNull(HydraReportRow.LeadingURL("HTTP://Example.com/x"));
        Assert.Equal("https://x.example/a", HydraReportRow.LeadingURL("  https://x.example/a \t\nrest")!.AbsoluteString);
        Assert.Null(HydraReportRow.LeadingURL("https://x.example/a and more\nrest"));
        Assert.Null(HydraReportRow.LeadingURL("ftp://x.example/a"));
        Assert.Null(HydraReportRow.LeadingURL("see https://x.example/a"));
        Assert.Null(HydraReportRow.LeadingURL(""));
        Assert.Equal("Merged.\n\nAll four heads.", HydraReportRow.WithoutFirstLine("https://x.example/a\n  Merged.\n\nAll four heads.  \n"));
        Assert.Equal("", HydraReportRow.WithoutFirstLine("https://x.example/a"));
        Assert.Equal("b", HydraReportRow.WithoutFirstLine("a\r\nb"));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-144")] // App/Views/Chat/TimelineRows.swift:252-257, :304-311
    public void A_note_that_leads_with_an_address_gets_an_Open_link_beside_the_pill_that_opens_it_in_the_browser()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var opened = new List<string>();
            using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
            runtime.OnMain(() =>
            {
                var linkOnly = new HydraReportRow(Report("Hydra merged the team's work.\nhttps://gitlab.com/g/p/-/merge_requests/7") with { HydraHeads = null });
                var withMore = new HydraReportRow(Report("Hydra merged the team's work.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThree heads landed.") with { HydraHeads = null });
                var panel = new StackPanel();
                panel.Children.Add(linkOnly);
                panel.Children.Add(withMore);
                var window = Show(panel);
                var buttons = LogicalQuery.SelfAndDescendants<Button>(linkOnly).ToList();
                Assert.Equal(2, buttons.Count);
                var open = buttons.Single(button => Texts(button).Contains("Open"));
                Assert.Equal("Open in the browser", ToolTip.GetTip(open));
                var word = TextNamed(open, "Open");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption, SwiftFont.Weight.Semibold)), word.FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, word), BrushColor(word.Foreground));
                // The link is a sibling of the pill's button, never inside it.
                var pillButton = buttons.Single(button => !ReferenceEquals(button, open));
                Assert.DoesNotContain(open, LogicalQuery.SelfAndDescendants<Button>(pillButton));
                // A note that is nothing but an address has no popover.
                Assert.False(pillButton.IsEnabled);
                Assert.DoesNotContain("chevron.right", Symbols(linkOnly));
                Assert.Contains("chevron.right", Symbols(withMore));
                Assert.True(LogicalQuery.SelfAndDescendants<Button>(withMore).Single(button => !Texts(button).Contains("Open")).IsEnabled);
                Click(open);
                Assert.Equal(["https://gitlab.com/g/p/-/merge_requests/7"], opened);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-145")] // App/Views/Chat/TimelineRows.swift:317-324
    public void A_reports_markdown_is_warmed_as_the_pill_appears_per_head_for_a_batch()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var nonce = System.Guid.NewGuid().ToString("N");
            var note = "Held back.\n```\nnote " + nonce + "\n```";
            var noteBody = "```\nnote " + nonce + "\n```";
            var batch = "## Hank: a\n```\nhank " + nonce + "\n```\n## Walter: b\n```\nwalter " + nonce + "\n```";
            var heads = HydraReportDigest.Parse(batch).Heads.Select(head => head.Body).ToList();
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                Assert.Equal(2, heads.Count);
                Assert.Equal([noteBody], MarkdownView.WarmTexts([noteBody]));
                Assert.Equal(heads, MarkdownView.WarmTexts(heads));
                var panel = new StackPanel();
                panel.Children.Add(new HydraReportRow(Report(note) with { HydraHeads = null }));
                panel.Children.Add(new HydraReportRow(Report(batch, 0, 1)));
                window = Show(panel);
            });
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([noteBody, .. heads]).Count == 0, AppModelHarness.Bound);
            h.Main.OnMain(() =>
            {
                // The batch warmed head by head, not as one text.
                Assert.Equal([batch], MarkdownView.WarmTexts([batch]));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-146")] // App/Views/Chat/TimelineRows.swift:607-639
    public void The_report_popover_is_460_wide_with_a_460_point_scroller_at_the_callout_style_with_12_point_markdown()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            runtime.OnMain(() =>
            {
                const string text = "```\none\n```\n\n```\ntwo\n```";
                var popover = new HydraReportPopover(text);
                var window = Show(popover);
                Assert.Equal(460, popover.Width);
                var scroll = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverScroll>(popover));
                Assert.True(Assert.IsType<ScrollViewer>(scroll.Child).Height <= 460);
                var blocks = LogicalQuery.SelfAndDescendants<MarkdownBlockView>(popover).ToList();
                Assert.Equal(MarkdownView.Blocks(text).Count, blocks.Count);
                var stack = Assert.IsType<StackPanel>(blocks[0].Parent);
                Assert.Equal(10, stack.Spacing);
                Assert.Equal(12, MarkdownView.GetMarkdownPointSize(blocks[0]));
                Assert.False(MarkdownView.GetMarkdownDimmed(blocks[0]));
                Assert.Equal(FontSize(SwiftFont.System(SwiftFont.TextStyle.Callout)), TextElement.GetFontSize(stack));
                Assert.Equal(new Thickness(16, 14), Assert.IsAssignableFrom<Decorator>(stack.Parent).Padding);
                Assert.Equal(300, new HydraReportPopover(text, width: 300).Width);
                Close(window);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-147 HydraBriefRow

    [Fact]
    [Trait("Req", "PR-CHAT-147")] // App/Views/Chat/TimelineRows.swift:537-595
    public void A_brief_reads_as_cooking_until_its_turn_has_output_and_then_as_the_brief()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            HydraBriefRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
            h.Main.OnMain(() =>
            {
                h.Runtime.RehearseTurn("Build the thing");
                var entry = h.Runtime.Entries.Last(candidate => candidate.TimelineEntryKind == TimelineEntry.Kind.User);
                var message = ((TimelineItem.Content.User)entry.Item.TimelineItemContent).Value;
                row = new HydraBriefRow(message, entry, h.Runtime);
                window = Show(row);
                var title = TextNamed(row, "Hank is starting to cook\U00002026"); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), title.FontSize);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), title), BrushColor(title.Foreground));
                Assert.True(Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(row)).IsRunning);
                var button = LogicalQuery.SelfAndDescendants<Button>(row).Single();
                Assert.Equal("Show the brief", ToolTip.GetTip(button));
                Assert.False(button.Focusable);
                Assert.Equal("Hank is starting to cook\U00002026", AutomationProperties.GetName(button)); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(HorizontalAlignment.Right, LogicalQuery.SelfAndDescendants<StackPanel>(row).First().HorizontalAlignment);
                Assert.Equal(new Thickness(96, 0, 0, 0), row.Padding);
                h.Runtime.Rehearse(new ProviderEvent.ToolStarted("tool-1", Call(ToolCall.Kind.Read, "Read a", ToolCall.Status.Running)));
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Contains("Hank's brief", Texts(row!));
                Assert.False(Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(row!)).IsRunning);
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-148 HydraShimmer

    [Fact]
    [Trait("Req", "PR-CHAT-148")] // App/Views/Chat/TimelineRows.swift:480-520
    public void The_shimmer_is_a_band_a_third_as_wide_as_the_text_masked_to_it_and_nothing_under_reduced_motion()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var genie = new FakeGeniePlatform();
            using var reduce = ChromeTokens.ReduceMotionPlatform.Override(genie);
            runtime.OnMain(() =>
            {
                var plain = new TextBlock { Text = "Merging" };
                genie.AccessibilityDisplayShouldReduceMotion = true;
                Assert.Same(plain, new HydraShimmer().Body(plain));
                genie.AccessibilityDisplayShouldReduceMotion = false;
                var text = new TextBlock { Text = "Pushing the branch", Width = 150 };
                var shimmer = new HydraShimmer().Body(text);
                var window = Show(shimmer);
                var band = LogicalQuery.SelfAndDescendants<Border>(shimmer).Single(border => border.Background is LinearGradientBrush);
                Assert.Equal(50, band.Width);
                var gradient = (LinearGradientBrush)band.Background!;
                Assert.Equal([0.0, 0.5, 1.0], gradient.GradientStops.Select(stop => stop.Offset));
                Assert.Equal(
                    [Resolved(ChromeTokens.PrimaryText.Opacity(0), band), Resolved(ChromeTokens.PrimaryText.Opacity(0.9), band), Resolved(ChromeTokens.PrimaryText.Opacity(0), band)],
                    gradient.GradientStops.Select(stop => stop.Color));
                var layer = (Control)band.Parent!;
                Assert.False(layer.IsHitTestVisible);
                Assert.Same(text, Assert.IsType<VisualBrush>(layer.OpacityMask).Visual);
                // A narrow text still gets a 24-point band.
                var narrow = new TextBlock { Text = "Go", Width = 30 };
                var small = new HydraShimmer().Body(narrow);
                var smallWindow = Show(small);
                Assert.Equal(24, LogicalQuery.SelfAndDescendants<Border>(small).Single(border => border.Background is LinearGradientBrush).Width);
                Close(smallWindow);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Decision", "ED-117")] // App/Views/Chat/TimelineRows.swift:499-512
    [Trait("Oracle", "O-34")]
    public void The_shimmer_band_is_offset_by_the_reference_time_modulo_one_point_eight_seconds_at_each_thirtieth_of_a_second()
    {
        // Rests on ED-117 (3), the date an animation timeline passes at a frame (O-34), so no Req trait.
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            using var reduce = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            Control? shimmer = null;
            AvaloniaWindow? window = null;
            double Expected()
            {
                var phase = SwiftDate.ReferenceSeconds(SwiftDate.Now) % 1.8 / 1.8;
                return -50 + phase * (150 + 50);
            }
            double Offset() => Assert.IsType<TranslateTransform>(LogicalQuery.SelfAndDescendants<Border>(shimmer!).Single(border => border.Background is LinearGradientBrush).RenderTransform).X;
            runtime.OnMain(() =>
            {
                var text = new TextBlock { Text = "Pushing the branch", Width = 150 };
                shimmer = new HydraShimmer().Body(text);
                window = Show(shimmer);
                Assert.Equal(Expected(), Offset(), 6);
            });
            runtime.AdvanceMilliseconds(700);
            frames.Advance(System.TimeSpan.FromMilliseconds(700));
            runtime.OnMain(() =>
            {
                Assert.Equal(Expected(), Offset(), 6);
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-149 HydraDelegationBlock

    [Fact]
    [Trait("Req", "PR-CHAT-149")] // App/Views/Chat/TimelineRows.swift:652-707, :745-753
    public void The_delegation_card_says_Summoning_heads_while_streaming_then_names_the_count_or_the_one_head()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                const string hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
                const string anon = "{\"task\": \"Write tests\", \"prompt\": \"Write them.\"}";
                string[] Words(string json, bool sent = false)
                {
                    var block = new HydraDelegationBlock(json, sent);
                    var window = Show(block);
                    var words = Texts(block);
                    Close(window);
                    return words;
                }
                // Streaming: the first brief has closed, the block has not.
                var streaming = Words("[\n" + hank + ",\n{\"task\": \"Wri");
                Assert.Equal("Summoning heads", streaming[0]);
                Assert.Contains("Tidy the popover", streaming);
                Assert.Contains("Writing the next brief\U00002026", streaming); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(["Summoning heads"], Words("[\n{\"task\": \"a"));
                Assert.Equal(["Summoning heads"], Words("not json at all"));
                Assert.Equal("Summoning Hank", Words("[\n" + hank + "\n]")[0]);
                Assert.Equal("Summoning a head", Words("[\n" + anon + "\n]")[0]);
                Assert.Equal("Summoning 2 heads", Words("[\n" + hank + ",\n" + anon + "\n]")[0]);
                Assert.Equal("Summoned 2 heads", Words("[\n" + hank + ",\n" + anon + "\n]", sent: true)[0]);
                Assert.DoesNotContain("Writing the next brief\U00002026", Words("[\n" + hank + ",\n" + anon + "\n]")); // U+2026 HORIZONTAL ELLIPSIS
                // Sent while still streaming: the block is done being written.
                Assert.Equal("Summoned Hank", Words("[\n" + hank + ",\n{\"ta", sent: true)[0]);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-149")] // App/Views/Chat/TimelineRows.swift:652-741, :762-766
    public void The_delegation_card_lists_each_head_with_its_glyph_task_and_other_project_and_once_sent_checks_and_sweeps()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                const string json = "[\n{\"task\": \"Tidy the popover\", \"prompt\": \"p\", \"name\": \"Walter\", \"project\": \"/Users/me/code/website\"},\n{\"task\": \"Write tests\", \"prompt\": \"p\", \"project\": \"docs\"}\n]";
                var block = new HydraDelegationBlock(json, sent: true);
                var window = Show(block);
                var marks = LogicalQuery.SelfAndDescendants<HydraMarkImage>(block).ToList();
                Assert.Equal(2, marks.Count);
                Assert.All(marks, mark => Assert.Equal(16, mark.Width));
                Assert.All(marks, mark => Assert.Equal(Resolved(ChromeTokens.SecondaryText, mark), BrushColor(LogicalQuery.SelfAndDescendants<SwiftImage>(mark).Single().Foreground)));
                var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(block));
                Assert.Equal("Walter", glyph.Persona.Name);
                Assert.Equal(16, glyph.Size);
                var task = TextNamed(block, "Tidy the popover");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption)), task.FontSize);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.75), task), BrushColor(task.Foreground));
                var project = TextNamed(block, "website");
                Assert.Contains("docs", Texts(block));
                var tag = (Control)project.Parent!;
                Assert.Equal("in website", AutomationProperties.GetName(tag));
                Assert.Equal(2, tag.Margin.Left);
                Assert.Contains("arrow.right", Symbols(tag));
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Medium)), project.FontSize);
                var title = TextNamed(block, "Summoned 2 heads");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), title.FontSize);
                var check = LogicalQuery.SelfAndDescendants<SwiftImage>(block).Single(image => image.SymbolName == "checkmark.circle.fill");
                Assert.Equal("sent", AutomationProperties.GetName(check));
                Assert.Single(LogicalQuery.SelfAndDescendants<DotFieldSweep>(block));
                var pill = LogicalQuery.SelfAndDescendants<Border>(block).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                Close(window);

                var unsent = new HydraDelegationBlock(json);
                var unsentWindow = Show(unsent);
                Assert.DoesNotContain("checkmark.circle.fill", Symbols(unsent));
                Assert.Empty(LogicalQuery.SelfAndDescendants<DotFieldSweep>(unsent));
                Close(unsentWindow);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-150, 151 merge pill and track

    [Fact]
    [Trait("Req", "PR-CHAT-150")] // App/Views/Chat/TimelineRows.swift:937-1019, :1044-1094; Core/Models/Hydra.swift:509-529
    public void The_merge_pill_shows_the_stage_under_way_with_an_ellipsis_and_goes_once_the_merge_is_over_without_a_note()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            HydraMergeRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = HydraMergeStage.Pushing;
                h.Runtime.HydraMergeStage = HydraMergeStage.Pushing.Words;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = Show(row);
                Assert.Contains("Pushing the branch\U00002026", Texts(row)); // U+2026 HORIZONTAL ELLIPSIS
                var pill = LogicalQuery.SelfAndDescendants<Border>(row).Single(border => AutomationProperties.GetName(border) is not null);
                Assert.Equal("Merging the team's work: Pushing the branch\U00002026", AutomationProperties.GetName(pill)); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical), pill.Padding);
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                var body = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(row));
                Assert.Equal(3, body.AnimatableData.Position);
                Assert.DoesNotContain(Texts(row), text => text.Contains("Open", System.StringComparison.Ordinal));
                h.Runtime.HydraMergeStage = null;
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Contains("Merging\U00002026", Texts(row!)); // U+2026 HORIZONTAL ELLIPSIS
                h.Runtime.IsHydraMerging = false;
            });
            // :949-952 removal uses the glide spring. Complete its source-defined transition before asserting absence.
            frames.Advance(System.TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(2000);
            frames.Advance(System.TimeSpan.FromMilliseconds(2000));
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraMergeTrack>(row!));
                Assert.Empty(Texts(row!));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-150")] // App/Views/Chat/TimelineRows.swift:937-952, :1020-1027, :1036-1042, :1099-1161
    public void When_the_note_lands_the_pill_morphs_to_its_title_and_link_and_a_successful_merge_flourishes_for_1100_milliseconds()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            using var browser = Marrow.Services.Port.Workspace.Open.Override(_ => true);
            HydraMergeRow? row = null;
            AvaloniaWindow? window = null;
            (double Position, (double Done, double Resting) Outcome) Track() => LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(row!).Single().AnimatableData;
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = HydraMergeStage.Opening;
                h.Runtime.HydraMergeStage = HydraMergeStage.Opening.Words;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = Show(row);
                Assert.Equal((4.0, (0.0, 0.0)), Track());
                h.Runtime.IsHydraMerging = false;
                h.Runtime.HydraMergeStep = null;
                row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra merged the team's work.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThree heads landed." }));
            });
            void Frames(double milliseconds)
            {
                frames.Advance(System.TimeSpan.Zero);
                h.Main.AdvanceMilliseconds(milliseconds);
                frames.Advance(System.TimeSpan.FromMilliseconds(milliseconds));
            }
            // Under 1.1 s: the outcome has landed and the flourish is still on.
            Frames(1000);
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Contains("Hydra merged the team's work", Texts(row!));
                Assert.DoesNotContain(Texts(row!), text => text.EndsWith("…", System.StringComparison.Ordinal)); // U+2026 HORIZONTAL ELLIPSIS
                var pill = LogicalQuery.SelfAndDescendants<Border>(row!).Single(border => AutomationProperties.GetName(border) is not null);
                Assert.Equal("Hydra merged the team's work", AutomationProperties.GetName(pill));
                var open = LogicalQuery.SelfAndDescendants<Button>(row!).Single(button => Texts(button).Contains("Open"));
                Assert.Equal("Open in the browser", ToolTip.GetTip(open));
                var title = LogicalQuery.SelfAndDescendants<Button>(row!).Single(button => Texts(button).Contains("Hydra merged the team's work"));
                Assert.Equal("Show the message", ToolTip.GetTip(title));
                Assert.Contains("chevron.right", Symbols(title));
                // :1036-1042 done: the head on the last dot, every dot lit, not yet settled.
                Assert.Equal((6.0, (1.0, 0.0)), Track());
            });
            // Past 1.1 s the flourish ends and the head settles to its resting size.
            h.Main.AdvanceMilliseconds(200);
            Frames(2000);
            h.Main.OnMain(() =>
            {
                Assert.Equal((6.0, (1.0, 1.0)), Track());
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-150")] // App/Views/Chat/TimelineRows.swift:1020-1027
    public void An_outcome_that_is_not_a_merge_settles_at_once_with_no_flourish_and_an_outcome_with_nothing_behind_it_is_disabled()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            HydraMergeRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = Show(row);
                h.Runtime.IsHydraMerging = false;
                row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "The merge stopped." }));
            });
            frames.Advance(System.TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(2000);
            frames.Advance(System.TimeSpan.FromMilliseconds(2000));
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal((6.0, (1.0, 1.0)), LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(row!).Single().AnimatableData);
                var title = LogicalQuery.SelfAndDescendants<Button>(row!).Single();
                Assert.False(title.IsEnabled);
                Assert.Null(ToolTip.GetTip(title));
                Assert.DoesNotContain("chevron.right", Symbols(row!));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-151")] // App/Views/Chat/TimelineRows.swift:844-916
    public void The_track_is_drawn_from_its_three_values_dots_lit_as_the_head_reaches_them_and_the_head_arcing_between_dots()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            runtime.OnMain(() =>
            {
                var body = new HydraMergeTrackBody(position: 2, done: 0, rest: 0, count: 7, hops: true);
                var window = Show(new StackPanel { Children = { body } });
                // :868-870 seven 4-point dots 3 apart: 46 wide, 20 high.
                Assert.Equal(46, body.Width);
                Assert.Equal(20, body.Height);
                var dots = LogicalQuery.SelfAndDescendants<Panel>(body).Where(panel => panel.Width == 4 && panel.Height == 4).ToList();
                Assert.Equal(7, dots.Count);
                Assert.Equal([0.0, 7, 14, 21, 28, 35, 42], dots.Select(Canvas.GetLeft));
                Assert.All(dots, dot => Assert.Equal(16, Canvas.GetTop(dot)));
                double Lit(Panel dot) => ((Control)dot.Children[1]).Opacity;
                Assert.Equal([1.0, 1, 1, 0, 0, 0, 0], dots.Select(Lit));
                Assert.Equal(Resolved(ChromeTokens.Overlay(0.28), dots[0]), BrushColor(((Avalonia.Controls.Shapes.Ellipse)dots[0].Children[0]).Fill));
                Assert.Equal(Resolved(ChromeTokens.Accent, dots[0]), BrushColor(((Avalonia.Controls.Shapes.Ellipse)dots[0].Children[1]).Fill));
                var head = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(body));
                Assert.Equal(12, head.Width);
                Assert.Equal(10, Canvas.GetLeft(head), 9);
                Assert.Equal(0, Canvas.GetTop(head), 9);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, head), BrushColor(LogicalQuery.SelfAndDescendants<SwiftImage>(head).Single().Foreground));
                // Half way between two dots the head is at the top of its 4-point arc; 2.7 lights the fourth dot early.
                body.AnimatableData = (2.5, (0, 0));
                Assert.Equal(-4, Canvas.GetTop(head), 9);
                Assert.Equal(2.5 * 7 + 2 - 6, Canvas.GetLeft(head), 9);
                body.AnimatableData = (2.7, (0, 0));
                Assert.Equal([1.0, 1, 1, 1, 0, 0, 0], dots.Select(Lit));
                // The outcome: the wave lights the dots left to right and the head pops once.
                body.AnimatableData = (6, (0.5, 0));
                Assert.Equal(1.18, Assert.IsType<ScaleTransform>(head.RenderTransform).ScaleX, 9);
                body.AnimatableData = (0, (0.5625, 0));
                Assert.Equal([1.0, 1, 1, 1, 0.5, 0, 0], dots.Select(Lit));
                // Settled: the dots faded, the head at 18 points pinned to the leading edge, the frame shrunk to it.
                body.AnimatableData = (6, (1, 1));
                Assert.Equal(18, head.Width);
                Assert.Equal(0, Canvas.GetLeft(head), 9);
                Assert.Equal(1, Canvas.GetTop(head), 9);
                Assert.Equal(18, body.Width);
                Assert.All(dots, dot => Assert.Equal(0, ((Control)dot.Parent!).Opacity));
                Assert.Equal(1, Assert.IsType<ScaleTransform>(head.RenderTransform).ScaleX, 9);
                // Without hops (reduced motion) there is no arc and no pop.
                var still = new HydraMergeTrackBody(position: 2.5, done: 0.5, rest: 0, count: 7, hops: false);
                var stillWindow = Show(new StackPanel { Children = { still } });
                var stillHead = LogicalQuery.SelfAndDescendants<HydraMarkImage>(still).Single();
                Assert.Equal(0, Canvas.GetTop(stillHead), 9);
                Assert.Equal(1, Assert.IsType<ScaleTransform>(stillHead.RenderTransform).ScaleX, 9);
                Close(stillWindow);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-151")] // App/Views/Chat/TimelineRows.swift:821-840
    public void The_track_glides_to_a_new_step_from_where_the_head_is_and_eases_into_the_outcome()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            HydraMergeTrack? track = null;
            AvaloniaWindow? window = null;
            HydraMergeTrackBody Body() => LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(track!).Single();
            void Frames(double milliseconds)
            {
                frames.Advance(System.TimeSpan.Zero);
                runtime.AdvanceMilliseconds(milliseconds);
                frames.Advance(System.TimeSpan.FromMilliseconds(milliseconds));
            }
            runtime.OnMain(() =>
            {
                track = new HydraMergeTrack(step: 0, count: 7, mode: HydraMergeTrack.Mode.Running);
                window = Show(new StackPanel { Children = { track } });
                Assert.Equal((0.0, (0.0, 0.0)), Body().AnimatableData);
                track.Update(step: 3, count: 7, mode: HydraMergeTrack.Mode.Running);
            });
            Frames(100);
            var midway = runtime.OnMain(() => Body().AnimatableData.Position);
            Assert.InRange(midway, 0.01, 2.99);
            // Retargeted mid-flight: the head goes on from where it is, not from the ground.
            runtime.OnMain(() => track!.Update(step: 5, count: 7, mode: HydraMergeTrack.Mode.Running));
            frames.Advance(System.TimeSpan.Zero);
            Assert.InRange(runtime.OnMain(() => Body().AnimatableData.Position), midway - 0.01, 5);
            Frames(2000);
            Assert.Equal(5, runtime.OnMain(() => Body().AnimatableData.Position));
            runtime.OnMain(() => track!.Update(step: 6, count: 7, mode: HydraMergeTrack.Mode.Resting));
            Frames(2000);
            runtime.OnMain(() =>
            {
                Assert.Equal((6.0, (1.0, 1.0)), Body().AnimatableData);
                Close(window!);
            });
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-152 finished heads

    [Fact]
    [Trait("Req", "PR-CHAT-152")] // App/Views/Chat/TimelineRows.swift:438-476
    public void A_finished_heads_pill_names_its_outcome_beside_its_glyph_with_the_report_one_tap_away()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                var failed = new FinishedHeadPill(Head(0, HydraHeadInfo.Status.Failed));
                var stopped = new FinishedHeadPill(Head(1, HydraHeadInfo.Status.Stopped));
                var done = new FinishedHeadPill(Head(2, HydraHeadInfo.Status.Completed));
                var panel = new StackPanel();
                panel.Children.Add(failed);
                panel.Children.Add(stopped);
                panel.Children.Add(done);
                var window = Show(panel);
                string Words(Control pill) => string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(pill).Single().Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text));
                Assert.Equal("Hank failed", Words(failed));
                Assert.Equal("Walter was stopped", Words(stopped));
                Assert.Equal("Ada finished working", Words(done));
                Assert.Equal("Hank failed", AutomationProperties.GetName(failed));
                var runs = LogicalQuery.SelfAndDescendants<TextBlock>(failed).Single().Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
                Assert.Equal("Hank", runs[0].Text);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.75), failed), BrushColor(runs[0].Foreground));
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), runs[1].FontSize);
                var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(failed));
                Assert.Equal(18, glyph.Size);
                Assert.Equal(HydraHeadInfo.Status.Failed, glyph.Status);
                var button = LogicalQuery.SelfAndDescendants<Button>(failed).Single();
                Assert.Equal("Show the report", ToolTip.GetTip(button));
                Assert.Contains("chevron.right", Symbols(failed));
                var pill = LogicalQuery.SelfAndDescendants<Border>(failed).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-152")] // App/Views/Chat/TimelineRows.swift:398-418; App/AppModel.swift:396-400
    public void The_finished_heads_row_draws_one_leading_aligned_pill_per_named_head_found_among_this_threads_helpers()
    {
        headless.OnUiThread(() =>
        {
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var (store, app, runtime) = TeamHarness(Head(0, HydraHeadInfo.Status.Failed), Head(1, HydraHeadInfo.Status.Completed), Head(2, HydraHeadInfo.Status.Running));
            using (store)
            {
                store.Main.OnMain(() =>
                {
                    var row = new HydraHeadsWorkingRow(new ValueList<int>([1, 0, 5]), runtime);
                    var host = new Border { Child = row };
                    Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                    var window = Show(host);
                    // STOP #4 is only the external helpers(of:) lookup. Test the owned :404-418 body with real
                    // children of this fixture's lead, not a replacement implementation of AppModel.Helpers.
                    row.ApplyTeam(app.Children(runtime.ThreadID));
                    var pills = LogicalQuery.SelfAndDescendants<FinishedHeadPill>(row).ToList();
                    Assert.Equal(["Walter finished working", "Hank failed"], pills.Select(pill => AutomationProperties.GetName(pill)));
                    var column = LogicalQuery.SelfAndDescendants<StackPanel>(row).First();
                    Assert.Equal(6, column.Spacing);
                    Assert.Equal(HorizontalAlignment.Left, column.HorizontalAlignment);
                    Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                    Close(window);
                });
            }
        });
    }

    // ---------------------------------------------------------------- PR-CHAT-153 agent rows

    [Fact]
    [Trait("Req", "PR-CHAT-153")] // App/Views/Chat/TimelineRows.swift:1723-1755, :1766-1793, :1812-1820
    public void A_call_sending_out_a_head_draws_as_a_head_badge_that_reads_Sending_out_Sent_out_or_Could_not_send_out()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var sending = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Running));
                var couldNot = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Failed));
                var generic = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Agent, "Subagent"));
                var unnamed = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Agent, "Refactor: the store"));
                var panel = new StackPanel();
                foreach (var row in new[] { sending, couldNot, generic, unnamed }) panel.Children.Add(row);
                var window = Show(panel);
                Assert.Contains("Sending out Pip: the merge popover", Texts(sending));
                Assert.Contains("Could not send out Pip: the merge popover", Texts(couldNot));
                Assert.Contains("Sent out a head", Texts(generic));
                Assert.Contains("Sent out a head: Refactor: the store", Texts(unnamed));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ProgressBar>(sending));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolStatusIcon>(sending));
                var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(sending));
                Assert.Equal("Pip", glyph.Persona.Name);
                Assert.True(glyph.IsRunning);
                Assert.Null(glyph.Status);
                Assert.Equal(HydraHeadInfo.Status.Failed, LogicalQuery.SelfAndDescendants<HydraGlyph>(couldNot).Single().Status);
                Assert.Equal(HydraHeadInfo.Status.Completed, LogicalQuery.SelfAndDescendants<HydraGlyph>(generic).Single().Status);
                Assert.Equal("Hank", LogicalQuery.SelfAndDescendants<HydraGlyph>(generic).Single().Persona.Name);
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(sending).Single(block => block.Text == "Sending out Pip: the merge popover");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), label.FontSize);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), label), BrushColor(label.Foreground));
                var pill = LogicalQuery.SelfAndDescendants<Border>(sending).Single(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), pill), BrushColor(pill.Background));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-153")] // App/Views/Chat/TimelineRows.swift:1766-1788
    public void A_call_whose_head_is_linked_reads_Sending_out_or_Sent_out_the_heads_name_and_the_title()
    {
        headless.OnUiThread(() =>
        {
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var (store, app, runtime) = TeamHarness(Head(3, HydraHeadInfo.Status.Running));
            using (store)
            {
                store.Main.OnMain(() =>
                {
                    var headID = app.Threads.Single(thread => thread.IsHydraHead).Id;
                    var running = ToolEntry(Call(ToolCall.Kind.Agent, "Tidy the popover", ToolCall.Status.Running));
                    var finished = ToolEntry(Call(ToolCall.Kind.Agent, "Tidy the popover"));
                    runtime.RehearseHydraHead(running.Id, headID);
                    runtime.RehearseHydraHead(finished.Id, headID);
                    var panel = new StackPanel();
                    panel.Children.Add(new ToolRow(running, runtime));
                    panel.Children.Add(new ToolRow(finished, runtime));
                    var host = new Border { Child = panel };
                    Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                    var window = Show(host);
                    Assert.Contains("Sending out Otto: Tidy the popover", Texts(panel.Children[0]));
                    Assert.Contains("Sent out Otto: Tidy the popover", Texts(panel.Children[1]));
                    Assert.All(LogicalQuery.SelfAndDescendants<HydraGlyph>(panel), glyph => Assert.Equal("Otto", glyph.Persona.Name));
                    Close(window);
                });
            }
        });
    }
}

// STOP App.Views.Chat.TimelineRows #1 (part 1, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:110 (B3 52bc792) `.modifier(ChipChrome())`
// Requirements: PR-CHAT-078
// What is unclear or missing: the quote chips wear ChipChrome (App/Views/Composer/ComposerView.swift:962-970), whose
// C# Body is still MARROW_STUB in src/Marrow.App/Views/Composer/ComposerView.cs (App.Views.Composer.ComposerView unit).
// UserMessageRow calls it as written, so a sent message that leads with a quote throws that unit's MARROW_STUB until it
// lands; the chips' glyph, excerpt, 260-point cap, tooltip and accessibility label are written but cannot be rendered.
// Readings or options: none; the modifier is the other unit's.
// Needed to proceed: unit App.Views.Composer.ComposerView (ChipChrome.Body).
// Done so far: the no-leading-quote path (no chips, whole text in the bubble) is tested above and passes.
//
// STOP App.Views.Chat.TimelineRows #2 (part 1, 2026-09-26)
// Kind: platform gap
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:191-196 (B3 52bc792) `.confirmationDialog("Edit from this
// message?", isPresented: $isConfirmingRevert) { Button("Revert and keep file changes") ...; Button("Revert files too",
// role: .destructive) ... } message: { Text(...) }`
// Requirements: PR-CHAT-082 (and PR-GIT-123 in part 3)
// What is unclear or missing: the port has no seam for a SwiftUI confirmationDialog. Presentation.Presenter carries
// popovers (R-VIEW-9) and App/Port/AlertPresenter.cs carries NSAlert.runModal with plain buttons only: neither has a
// destructive role or the system Cancel a confirmation dialog adds, and no ED or PA says how a confirmation dialog
// presents on Windows. The intent path is written (menu -> MessageMenuRequests -> isConfirmingRevert, :184-190) and so
// is revert(restoreFiles:) (:202-206); the presentation itself stays MARROW_STUB and throws when Edit from here is chosen.
// Readings or options: (a) a modal Avalonia window like WindowsAlertPresenter with the title, the message, the two
// buttons (the second in Chrome.danger) and Cancel; (b) a popover on the row through Presentation.Presenter.
// Needed to proceed: a decision (ED or PA) on the confirmationDialog seam.
// Done so far: the menu offers Edit from here with its divider (tested above); nothing is presented.
//
// STOP App.Views.Chat.TimelineRows #3 (part 1, 2026-09-26)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:2580-2585 (B3 52bc792) `TimelineGroup.build(content,
// showReasoning: false)`, read when the finished turn is expanded (:2631-2663)
// Requirements: PR-CHAT-104 (the expanded half)
// What is unclear or missing: TimelineGroup.Build is still MARROW_STUB in src/Marrow.App/Views/Chat/ThreadTimeline.cs
// (App.Views.Chat.ThreadTimeline unit). The expanded card is written as the Swift writes it, but opening a finished
// turn throws that unit's MARROW_STUB until it lands, so the chevron's opening, the filled 14-point card and its
// bottom gap are untested.
// Needed to proceed: unit App.Views.Chat.ThreadTimeline (TimelineGroup.Build).
// Done so far: the folded turn (header, tooltip, rule, answer selection) is tested above and passes.
//
// STOP App.Views.Chat.TimelineRows #4 (part 2, 2026-09-27)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:401-403; App/AppModel.swift:396-400 (B3 52bc792).
// Records: PR-CHAT-152; UI-CHAT-052.
// Missing: AppModel.Helpers has the B2 predicate !IsHydraHead, whereas B3 helpers(of:) includes every
// unarchived head, in its panel or not. Using that API would silently render no finished heads.
// Needed: the App.AppModel owner must port the B3 per-parent helper-cell predicate and newest-first order.
// Only the environment lookup is unwritten. ApplyTeam tests the :404-418 dictionary, requested order,
// missing-index omission, pill rendering and layout against actual fixture children without faking Helpers.
//
// STOP App.Views.Chat.TimelineRows #5 (part 5, 2026-09-27)
// Kind: platform gap
// Swift: DroppyCode/App/Views/Chat/TimelineRows.swift:2796 `.controlSize(.small)` on the confirmation buttons.
// Records: UI-CHAT-039; UI-GIT-047.
// Missing: an approved SwiftUI small-control-size adapter for the glass button recipe. No pixel size is in Swift,
// and the existing recipe exposes only the normal capsule metrics. Needed: a foundation mapping/ED for small size.
// Only that modifier is unwritten; exact title/body, width, padding, glass styles, default Revert and close-first
// actions are implemented and tested. Generic .green is allowed by ED-092 (1), with the existing AppTheme
// FrameworkGreen mapping precedent; its visual appearance remains UNVERIFIED under OD-0001.
