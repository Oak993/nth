using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :97-114, :401-418,
// :1969-1973, :2580-2663. Dependency closure is separate from whole-record visual proof.
[Collection("Headless")]
public sealed class TimelineRowsUiAuditTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Earlier STOPs 1,3,4,6: dependencies now on impl/b2; records still have the precise residual sites below.
    public void Landed_quote_group_helper_and_wrapped_text_dependencies_are_exercised()
    {
        var earlier = new TimelineRowsTests(headless);
        earlier.Leading_quotes_render_with_the_real_ChipChrome_excerpt_and_help();
        earlier.Expanded_turns_use_real_groups_and_exclude_the_separate_head_list();
        earlier.Finished_heads_mount_from_the_live_AppModel_helper_cells_without_manual_injection();
        earlier.Head_tool_steps_have_two_wrapping_lines_with_a_constrained_text_column();
    }
}

// STOP opencode-App.Views.Chat.TimelineRows #1
// Kind: external foundation/oracle. Missing: the approved continuous corner curve. ED-181's current
// src/Marrow.UI/Port/SwiftShapes.cs:39,48,63 still uses circular arcs for .continuous. Needed: that foundation's
// oracle-backed continuous curve, not another locally invented rounded path. The legacy tests retain all
// source radius/inset/type/text/state checks, and TimelineRowsUiPointerTests adds real menu input.
// Exact TimelineRows.cs sites (Swift source sites and records):
// UserMessageRow quote ChipChrome :97-114, UI-CHAT-008.
// AssistantMessageRow.MessageActions -> PopoverItem's continuous hover surface :1401-1410, UI-CHAT-020/UI-CHAT-021.
// ToolRow badge -> Pill :1772-1793, UI-CHAT-024/UI-CHAT-056/UI-CHAT-057.
// ToolDetailView output plate :2153-2155, UI-CHAT-025; per-file diff plate :2127-2140, UI-CHAT-028.
// PlanCard -> ChatBadge :2263-2337, UI-CHAT-029; PlanCard.Detail/GlassCommand :2279-2313, UI-CHAT-030/UI-CHAT-031/UI-CHAT-032.
// TodoListRow :2343-2382, UI-CHAT-033. NoticeRow warning/error pill :2385-2431, UI-CHAT-034/UI-RUNTIME-002/UI-RUNTIME-003.
// TurnFinishedBlock steps plate :2631-2663, UI-CHAT-036/UI-CHAT-059.
// TurnFileCard pill :2817-2825, UI-CHAT-037/UI-GIT-044.
// HydraReportRow.Pill :313-316, UI-CHAT-048; HydraBriefRow.Pill :579-582, UI-CHAT-051.
// FinishedHeadPill.Pill :464-467, UI-CHAT-052; HydraMergeRow.Pill :1012-1017, UI-CHAT-053/UI-HYDRAMERGE-002.
// HydraDelegationBlock.Pill :691-694, UI-CHAT-054.
// UserMessageRow.UserActions -> PopoverItem continuous hover surface :216-228, UI-GIT-050.
// 2026-10-04 (unit blitz-timelinerows-cd, OD-0098): the sites above (and the tool badge, the notice pills and the merge pill) are
// pinned, each with the radius and the .continuous style the source states, in TimelineRowsCdTests.Every_pill_card_and_plate_of_the_
// timeline_rows_is_a_continuous_rounded_rectangle_of_the_radius_the_source_states; ChatBadge's pill and its clip and the delegation
// card's wave clip (:698), which bypassed SwiftShapes, now go through it. Only the curve itself stays open: those records are CORNER-ONLY.
//
// STOP opencode-App.Views.Chat.TimelineRows #2
// Kind: external symbol/native-style oracle. Missing: measured reference metrics/drawing for the requested
// native borderless pencil / round symbol glyphs and native mini progress indicator (ED-182, O-63, OD-0001).
// Lucide is installed, but ED-182 expressly leaves these optical values at O-63; no Mac reference is available.
// Needed: the shared symbol/native-style foundation's approved oracle measurements. Actual Windows hover/click
// input now covers the pencil and review disc, so missing fabricated-event tests are not this STOP.
// UserMessageRow pencil :130-161, UI-CHAT-010/UI-GIT-049.
// WorkGroup header glyph/chevron :1478-1489, UI-CHAT-022; WorkSteps ellipsis :1532-1535, UI-CHAT-023.
// MutedRoundGlyph :2467-2477; TurnFileCard revert/review :2770-2816,
// UI-CHAT-038/UI-CHAT-040/UI-GIT-046/UI-GIT-048.
// NoticeRow info.circle :2389-2399, UI-RUNTIME-001.
//
// STOP opencode-App.Views.Chat.TimelineRows #3
// Kind: external small-control mapping. GlassCommand(small:true) :2796 (C# GlassCommand :156-159)
// still uses normal capsule metrics. UI-CHAT-039/UI-GIT-047 require .controlSize(.small).
// Missing: an approved small-size adapter/metric ruling in the shared glass-button foundation.
// Needed: that ruling/adapter; ED-180's material deferral supplies no small-control dimensions.
// The glass renderer now exists on impl/b2: do not repeat an obsolete missing-renderer claim.
//
// STOP opencode-App.Views.Chat.TimelineRows #4
// Kind: external native link-style oracle. ToolDetailView toggle :2156-2162 (C# :2614-2618), UI-CHAT-026.
// Missing: reference-backed SwiftUI .buttonStyle(.link) underline/hover style. The site itself names
// OD-0001 UNVERIFIED, and no approved native link-style adapter resolves it on impl/b2.
// Needed: the foundation/owner's reference-backed link mapping. Real pointer toggling now passes locally.
// RESOLVED 2026-10-04 (OD-0088, ED-253: the link style draws in the view's tint, the theme's accent, with the pointing hand;
// OD-0098): UI-CHAT-026 is proven in TimelineRowsCdTests.The_Show_full_output_link_is_drawn_in_the_accent_with_the_pointing_hand_in_
// both_of_its_states and TimelineRowsFinToolDetailTests.
//
// STOP opencode-App.Views.Chat.TimelineRows #5
// Kind: external formatter oracle. TimelineRows.Duration / TurnEndRow.Label :2448-2455, UI-CHAT-035/UI-RUNTIME-004;
// AssistantMessageRow.Body duration :1368-1372, UI-CHAT-019. Missing: reference-runtime narrow Duration.UnitsFormatStyle
// results in the current locale (O-65, ED-197/ED-086). The ported formatter has landed, but ED-197 requires
// Decision/Oracle classification for its unverified strings. Needed: approved pinned-runtime formatter results.
// RESOLVED 2026-10-04 for UI-CHAT-035 and UI-RUNTIME-004 (OD-0098; ED-197, Oracle O-65): the formatter is swift-foundation's own
// algorithm (SwiftFormatters.DurationUnits, boundary tests in SwiftFormattersSecondTests), six of its strings are SOURCE FACT in the
// reference video (TimelineRowsCaptureTests), and TimelineRowsCdTests.The_turn_duration_reads_the_reference_runtimes_narrow_units_
// at_every_boundary_the_code_names holds those and the boundaries (1m, 59m 59s, 1h, 1h 2m, rounding). UI-CHAT-019 stays with its own record.
//
// STOP opencode-App.Views.Chat.TimelineRows #6
// Kind: external native text layout/selection oracle. The remaining records delegate native layout to SwiftUI,
// AppKit/TextKit, or the shared Markdown/Popover rendering. No pinned-state reference captures exist here (OD-0001).
// Needed: approved captures/native layout measurements from the pinned runtime; the tour PNGs are only approximate
// and cannot replace this evidence (OD-0003). The inherited tests retain the explicit Windows values and local states.
// UserMessageRow bubble intrinsic Markdown sizing/body type and selection :118-129, UI-CHAT-007.
// ToolDetailView selectable .lineLimit(12) native cap/wrap/selection :2120-2126, UI-CHAT-027.
// WorkSteps.Narration compact/dimmed Markdown / native selection :1546-1553, UI-CHAT-061.
// TurnFileCard.FilesPopover exact native single-line middle elision :2833-2837, UI-CHAT-041/UI-GIT-045.
// HydraReportRow.Open / HydraMergeRow.Open native link styling :304-311/:1121-1126,
// UI-CHAT-049/UI-HYDRAMERGE-006.
// HydraReportPopover native popover chrome and selection/scroll :607-639, UI-CHAT-050/UI-HYDRAMERGE-007.
// HydraHeadStepsPopover two-line wrapped middle placement :1969-1973 (ED-217 explicitly UNVERIFIED), UI-CHAT-055.
// HydraMergeRow outcome single-line tail elision/native popover :1099-1120/:1142-1155, UI-HYDRAMERGE-005.
// 2026-10-04 (OD-0098): the layout those sites delegate to the framework is read from the values the code states (line limits,
// truncation modes, text selection, the popovers' widths and insets) and proven headless: UI-CHAT-027, -041/UI-GIT-045, -049/
// UI-HYDRAMERGE-006, -050/UI-HYDRAMERGE-007, -055, -061 and UI-HYDRAMERGE-005 are closed (UI-CHAT-007 is another unit's).
//
// STOP opencode-App.Views.Chat.TimelineRows #7
// Kind: external native animation oracle. HydraMergeTrack's .smooth duration 0.5/0.45 :821-841 and the
// coordinator's 1100ms flourish :1020-1027, UI-HYDRAMERGE-004. Its mark is the bundled hydra-mark asset,
// not an SF Symbol; no symbol-metrics blocker is alleged for it. The source's instantaneous drawing formula
// is implemented and tested, but native SwiftUI interpolation/settlement and frame cadence are not present
// in the source. F.SwiftAnimation uses ED-117 completion/cadence choices; ED-117(7) requires O-34/Decision
// classification for those expectations, never a Req. Needed: reference-backed native animation results,
// under OD-0001, before whole duration/trajectory parity can be credited. Existing Oracle tests stay.
// 2026-10-04 (OD-0098): UI-HYDRAMERGE-004 states its durations in the code (:838-839 .smooth 0.5 s and 0.45 s); the curve is the
// documented smooth spring of F.SwiftAnimation (ED-117, an ENGINEERING DECISION), the drawing's three values, its durations and its
// settle are proven in TimelineRowsFinMergeRowTests: the record is closed.
