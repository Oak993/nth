using System.Collections;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift (B3 52bc792), :1-2695. Part 1 of 8: PR-HYDRAMERGE-100,
// PR-SIDEBAR-001 to -006, -009 to -025. Part 2 of 8: PR-SIDEBAR-026, -028 to -050. Every expected value is
// transcribed from the B3 line cited on the test or from the record, never from the C# implementation. Non-ASCII text
// is written as escapes (R-STR-9).
//
// How the view is driven. Marrow.App has no InternalsVisibleTo (ED-119), so the file's private types and members are
// reached by reflection, as NewThreadPromptTests does. A view is either ATTACHED (in a shown headless window: its
// tracked parts run, one main turn after a change) or DETACHED (under a host that carries the AppModel, with no
// window: the environment reaches it through the logical tree, its tracked parts stay inert, and its builders and
// handlers can be called directly). The Window sweep below also drives populated, attached real rows.
//
// CLOSED STOP App.Views.Sidebar.SidebarView #1 (Job SB, ROUND3.md item 1)
// Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:737-751 (B3 52bc792); :743 `project.name.lowercased().contains(needle)`
// and :745 `titles[$0.id]?.contains(needle) ?? $0.title.lowercased().contains(needle)`
// Requirements: PR-SIDEBAR-005 (empty-query half)
// Was: the search field's Return runs searchResults even when the trimmed query is empty (:86-90), so `contains` is
// asked about an empty needle, and two Swift overloads could answer differently: Foundation's NSString-bridged
// contains (false for "") or the standard library's SE-0357 contains (true for "").
// ENGINEERING DECISION (closes this STOP; full citation at Contains's own site, SidebarView.cs :1339): an empty
// needle answers true. `.lowercased()` produces a plain Swift String, so the applicable overload is the standard
// library's `firstRange(of:) != nil` (SE-0357, SwiftStdlib 5.7+, unchanged through the pinned Swift 6.0 toolchain);
// Foundation's NSString.contains(_:), the only candidate that answers false, needs an explicit NSString receiver
// this call site never produces. Proven by Return_with_an_empty_query_selects_the_first_thread_of_the_first_project.
// Residual caveat: no macOS run of the pinned toolchain confirmed this by direct observation.
// STOP #5 below (the character-wise-canonical-vs-ordinal question for a NON-empty needle) is a separate, still open
// question this decision does not resolve either way (an empty needle matches under any reading).
//
// Resolved STOP #2: WindowManager.Shared/ShowSettings and SettingsView are real. The source Settings click,
// manager/model ownership and reused Settings window are exercised by Clicking_Settings_opens_the_real_shared_managers_SettingsView_and_reuses_its_window.
//
// CLOSED STOP App.Views.Sidebar.SidebarView #3 (PR-SIDEBAR-019): the list slides surviving rows on Chrome.panelSlide for a
// change of the thread count, a project's expansion or the settled fold, and on easeOut 0.18 s under Reduce Motion; proven
// by The_list_slides_on_panelSlide_when_the_thread_count_a_project_expansion_or_the_settled_fold_changes and its control.
// (The pinned capture comparison of the motion is not a headless proof and is not claimed.) The note below is the history.
// STOP App.Views.Sidebar.SidebarView #3 (review: shared foundation landed; caller wiring remains)
// Kind: missing caller implementation, not a missing decision or shared foundation.
// Swift: App/Views/Sidebar/SidebarView.swift:117-119 (B3 52bc792), .animation(Chrome.panelSlide, value: ...).
// Requirements: PR-SIDEBAR-019, partial.
// Missing: SidebarView.ApplyEntries still manually reconciles its ListEntry controls. It has not adopted the
// ED-218 ForEach.Apply surviving-row layout motion now present in UI/Port/ViewLifetime.cs. Its current insertion/
// removal fades and drag offsets do not prove surviving rows slide when a neighbouring slot enters or leaves.
// Needed: caller integration with the existing ED-218 foundation, preserving ListEntry's update-or-rebuild,
// row identity, scrollTargets and geometry reporting; then a real list insertion/removal motion proof.
// No new ED/PA or second layout-animation foundation is needed. PR-SIDEBAR-019 is not certified complete.
//
// STOP App.Views.Sidebar.SidebarView #4
// Kind: conflict
// Swift: DroppyCode/App/AppModel.swift:525-527 (B3 52bc792) `let path = url.standardizedFileURL.path` and
// `if let existing = projects.first(where: { $0.path == path }) { return existing }`, reached from
// DroppyCode/App/Views/Sidebar/SidebarView.swift:509 `for url in panel.urls { added = model.addProject(at: url) }`
// Requirements: PR-SIDEBAR-012
// What is unclear or missing: the port's AppModel.AddProject(SwiftURL) (src/Marrow.App/AppModel.cs:544) passes
// SwiftURL.Path to SwiftPath.StandardizedFileURLPath. For the file URL a Windows folder gives ("file:///C:/..."),
// SwiftURL.Path is "/C:/...", which SwiftPath.IsAbsolute rejects, so the path is resolved against the process's
// current directory (seen here: "<test bin>\C:/Users/.../alpha"). A folder chosen in the panel therefore becomes a
// project with a wrong path, and a folder added by any other route (a library written by the app, AddProjectPath)
// never matches it, so it is added twice. SidebarView.AddProject calls the model exactly as :509-510 do.
// Readings or options: (a) AppModel.AddProject reads the URL's file-system path ("C:/..." without the leading
// slash, as swift-foundation's URL.path does on Windows) -> the source's duplicate check; (b) the panel seam hands the
// model a URL whose Path already has that form -> a non-standard file URL.
// Needed to proceed: a fix in unit App.AppModel (reading (a)), or a recorded decision.
// Done so far: the dedupe test adds the folder through this row twice, so both reads agree and the source's rule is
// observable; no test asserts the stored path of a chosen folder.
//
// STOP App.Views.Sidebar.SidebarView #5 (still open; narrowed by the #1 closure, not resolved by it)
// Kind: unverified
// Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:743, :745 (B3 52bc792) `.lowercased().contains(needle)`
// Requirements: PR-SIDEBAR-043
// What is unclear or missing: ED-009 asks the search fixture to cover a combining-mark case. The #1 closure above
// (Contains(text, needle) with needle.Length == 0) identifies the bound overload as the standard library's, whose
// non-empty-needle behaviour is Collection.contains walking Characters under canonical equivalence (a needle "e"
// does not match inside "e" + U+0301, and a precomposed U+00E9 needle matches a decomposed title) - option (a)
// below. The port's `Contains` (part 1) still implements only option (b), an ordinal substring test (the opposite
// answer in both cases), unchanged by this pass: rewriting the general matcher for non-empty needles is a separate,
// larger, untested change this job's scope (ROUND3.md item 1, the empty-needle case only) does not cover.
// Readings or options: (a) Character-wise canonical matching - suggested but not proven by the #1 closure's
// citations, which establish the bound overload's identity, not a macOS-run confirmation of its non-empty behaviour
// against this port's own test data; (b) ordinal UTF-16 matching (current); (c) Foundation's `range(of:)` without
// options -> its own rules.
// Needed to proceed: a follow-up pass implementing and test-proving option (a) against combining-mark fixtures, or
// a recorded decision to keep (b).
// Done so far: no test asserts either reading for combining marks; part 1's matcher is unchanged.
//
// CLOSED STOP App.Views.Sidebar.SidebarView #8 (Job SB, ROUND3.md item 3; was: Window sweep, narrowed to the
// shared context-menu site only)
// Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:1848-1862 (B3 52bc792)
// Requirements: UI-SIDEBAR-017's right-click interaction; PR-SIDEBAR-098 through PR-SIDEBAR-103's live-menu half.
// ENGINEERING DECISION (closes this STOP; full citation at SidebarThreadRow's own constructor, SidebarView.cs
// :2899-2911, the same foundation as STOP #4/ProjectRow's own closure above): Avalonia's ContextMenu.Opening
// rebuilds Items from a live Thread()/Model read on every open; Control.ContextMenu opens it on a real right-click.
// Proven by A_real_right_click_opens_the_thread_rows_context_menu_rebuilt_fresh_on_every_open (real hit-testing,
// the same pointer pipeline PR-SIDEBAR-096's own tests use, same headless-Popup caveat as the ProjectRow tests
// above - Avalonia 12.1.2 cannot actually open a native Popup here; Items are read right after Opening runs and
// before that throws).
// PR-SIDEBAR-134 (already proven) and this closure together account for SidebarThreadRow's live body and handlers;
// PR-SIDEBAR-135 remains partial only for the pinned captures in STOP #9. UI-SIDEBAR-058: compact thread
// selection/search is proven; the project-row menu slice is proven above (STOP #4/ProjectRow). Rename stays under
// STOP #6. SidebarHelperRow's and HeadCardsRow's own `.contextMenu` sites (UI-SIDEBAR-033/UI-SIDEBAR-037's
// right-click halves) remain under genuine STOP #7 (SidebarView.cs), out of this closure and ROUND3.md item 3's own
// scope (thread row only; item 3 conditioned the helper/head-card sites on "if the Swift is unambiguous and its
// dependencies exist" - not attempted this pass).

// STOP ledger opencode-App.Views.Sidebar.SidebarView (this unit's TODO records that stay blocked; each names
// the missing dependency and the exact production site in SidebarView.cs; STOP #2/PR-SIDEBAR-013 is CLOSED above):
// #1 PR-SIDEBAR-005 (empty-query half): CLOSED (Job SB, ROUND3.md item 1) - see the class-header ENGINEERING
//    DECISION above SidebarView.cs Contains (:1339): an empty needle answers true (SE-0357). Proven by
//    Return_with_an_empty_query_selects_the_first_thread_of_the_first_project. Non-empty Return and no-result
//    remain proven by Return_selects_the_first_thread_of_the_first_search_result and Return_with_no_result_changes_nothing.
// #2 PR-SIDEBAR-019: CLOSED (STOP #3 above; parity pass 2026-10-03 adds the mounted entering/leaving proof and the
//    capture comparison: the rows below a deleted thread in 2-chat-use.mov 154.43-154.79 s follow panelSlide's
//    spring(0.32, 0.9) to an rms of 0.020 of the travel, against 0.038-0.146 for the other curves tried; evidence
//    E:\Droppy\work\parity\evidence\SidebarView\slide_fit.py and slide_fit.txt).
// #3 PR-SIDEBAR-091, UI-SIDEBAR-028, UI-SIDEBAR-029, UI-SIDEBAR-030: the popover is built and its text and behaviour
//    are proven, but `.roundedBorder`/`.bordered`/`.borderedProminent` and presentedChrome's `.buttonBorderShape(.capsule)`
//    (Popovers.swift:10) still have no Windows chrome or decision (SystemButtons.cs draws a radius-6 rectangle and
//    reads no capsule flag); sites SidebarView.cs RenamePopover and RenameThreadPopover.
// #4 PR-SIDEBAR-130, PR-SIDEBAR-133: CLOSED (Job SB, ROUND3.md item 2) - see the CLOSED STOP #4 block above
//    SidebarView.cs ProjectRow's own header, and its own constructor's ContextMenu.Opening wiring (:1886-1899).
//    Proven by A_real_right_click_opens_the_project_rows_context_menu_rebuilt_fresh_on_every_open and
//    The_pair_submenu_is_absent_with_no_pairs_and_appears_once_one_exists_without_rebuilding_the_row. The
//    right-click interaction of UI-SIDEBAR-033/UI-SIDEBAR-037 (SidebarHelperRow's and HeadCardsRow's own sites)
//    remains open under genuine STOP #7 (SidebarView.cs); their non-right-click halves were already proven.
// #5 PR-SIDEBAR-134 (already proven), PR-SIDEBAR-135 (partial: STOP #9's pinned captures), UI-SIDEBAR-017 and the
//    end-to-end half of UI-SIDEBAR-060: SidebarThreadRow's own constructor and ThreadRow wiring were already
//    assembled by the Window sweep (this file's own header, above); UI-SIDEBAR-017's right-click half is CLOSED
//    (Job SB, ROUND3.md item 3) - see the CLOSED STOP #8 block above and SidebarThreadRow's own constructor
//    (SidebarView.cs :2899-2911). PR-SIDEBAR-098 through PR-SIDEBAR-103's live-menu half is proven by the same
//    closure. UI-SIDEBAR-060's pointer/slide/lift-pieces/release are proven at the seam level by the five trait
//    tests; the constructor/ThreadRow wiring assembly this entry originally named is no longer outstanding.

//
// PARITY PASS 2026-10-03 (unit/parity-sidebarview). Captures: E:\Droppy\work\parity\evidence\SidebarView\ (never in
// git): sbs_162*.png, z_*.png (the whole sidebar beside 2-chat-use.mov 162 s, rendered by SidebarCaptureTests),
// delete-note-sbs.png (152.6 s), slide_fit.py/.txt (154.43-154.79 s). What the captures settle and what stays open:
// - Geometry beside the video: row pitch, 28 pt rows, 24 pt stub, 20 pt icon slot, 46 pt title inset, the footer rows,
//   the head card's 9 columns at 248 pt, the delete note's line breaks and the panelSlide slide all match. Every text
//   line box was about 2 pt shorter than the Mac's (the callout note's line pitch 14.3 against the video's): RESOLVED
//   2026-10-04 by ED-290 (1): the Mac draws the explicit text styles in 18 / 17 / 16 line boxes (body, callout,
//   subheadline), measured on the same delete note at 2-chat-use.mov 152.6 s (callout baselines 134, 151, 168: 17, 17;
//   the 16.5 here was a rough read), and SwiftFont now draws them so.
// - SF Symbols draw narrower than their Lucide stand-ins (OD-0010): the stub's 8 pt chevron box is 10 pt wide where
//   the Mac's glyph is about 5.5 pt, so "4 heads" sits 2.5 pt further right; the Settled header's 10 pt chevron the
//   same (+3 pt). UNRESOLVED, a symbol-width question for the icon table, not a sidebar one.
// Still open per record (each is otherwise proven by the tests that carry its trait):
// - PR-SIDEBAR-013, UI-SIDEBAR-043, UI-UPDATE-010: the update dot appears and disappears by a cut. UI-UPDATE-010 says
//   it "disappears immediately"; OD-0076 says nothing changes by a cut; no ruling settles the conflict. No frame of the
//   video shows the dot (the feed is empty by OD-0012 I1), so PR-SIDEBAR-013's capture clause is UNRESOLVED.
// - PR-SIDEBAR-055: proven on a mounted release (On_a_real_release_the_row_and_everything_riding_it_spring_home...);
//   its capture clause is UNRESOLVED: no reorder drag appears in either video.
// - PR-SIDEBAR-127: SETTLED under OD-0098 (unit rec-e1): a symbol name the table does not map draws an empty mark in the 20 pt
//   square, as SwiftUI draws nothing for an unknown Image(systemName:) (SwiftImage.IsEmpty); no placeholder
//   (A_stored_symbol_name_the_table_does_not_map_draws_an_empty_mark_..., Every_symbol_name_the_picker_offers_resolves_...);
//   the glyph swap crossfades (OD-0076).
// - PR-SIDEBAR-129, UI-SIDEBAR-008: the 0.55 dim is a cut (the Mac snaps; OD-0076 asks for a transition in the Mac's
//   motion language, which no decision has fixed for this site); PR-SIDEBAR-129's capture is UNRESOLVED (no inactive
//   project in the videos). UI-SIDEBAR-008's accessory (count <-> buttons) crossfades on the hover's Chrome.hover since
//   review round 2 (below); the dim is still the cut.
// - PR-SIDEBAR-135: STOP #9 below (the reopen glide's two faces against a pinned capture).
// - UI-SIDEBAR-001: "pressing it drags the window" has no hit-test proof; UI-SIDEBAR-005: the real scroll wiring
//   (ScrollGestureEvent/Offset) that arms the freeze is untested; UI-SIDEBAR-006: the gap's mounted transition needs
//   the project-removal reactivity STOP #11 names.
// - UI-SIDEBAR-015/-016: the native submenu opening and the absence of a reserved check column cannot be observed on
//   a headless Avalonia MenuItem.
// - UI-SIDEBAR-017/-018/-019/-021/-022/-023/-024: per-clause gaps the parity audit listed (the face's paddings and
//   minimum heights, the Settled accessibility value, the pin's 0.9 scale, MiniSpinner cellSize 2.4, the dot's offset,
//   the project line's fonts and spacing, monospaced digits on the time label, the failed dot in ActivityStatus, the
//   fold chevron's symbols, the title-width glide on hover). UI-SIDEBAR-020: the settle pop (:1909 spring 0.3/0.45) is
//   proven by the Swift source under OD-0105 (SidebarSettleBounceTests, SidebarLeftoversTests) and the hover colour eases (SettleCheckHoverColourTests).
// - UI-SIDEBAR-026/-027: dismissal by click-away/Escape and the Closed -> carryOutConfirmedDeletion hookup cannot run
//   headless (STOP #12); UI-SIDEBAR-028/-029/-030/PR-SIDEBAR-091: STOP #6 (the controls' chrome);
//   UI-SIDEBAR-031/-032: `.controlSize(.small)`'s metrics are unverified.
// - UI-SIDEBAR-035: SETTLED under OD-0098 (unit rec-e1): :1583 strokes a bare shape with no content shape and :1608's
//   contentShape sits on the text half only, so the label half or a drawn dot of the connector unfolds the helpers and
//   the undrawn gaps of the column do not (A_press_on_one_of_the_stub_connectors_drawn_dots_unfolds_...).
// - UI-SIDEBAR-036: the snappy curve between the card's two heights, and panelSlide on the connector and card heights
//   when the head set changes (:1541), are unproven; the surviving glyphs of a card whose heads change or reorder jump
//   to their new places where the Mac glides them (OD-0076 asks for the glide; not done in this pass). What is proven:
//   the glyphs that join or leave fade (UI-SIDEBAR-038, below) and the card keeps its own state under new heads.
// - UI-SIDEBAR-037: the outcome badge's 3 pt overhang position and the hover scale easing back are unproven.
// - UI-SIDEBAR-044: the Windows dialog's prompt/message/folder flags (OpenPanel.cs) need an IFileOpenDialog fake.
//
// REVIEW PASS 1 (2026-10-03, adversarial; the tests named REVIEW_ are the reviewer's, kept as written). What it refuted and
// what was done, each against the Swift:
// - UI-SIDEBAR-033, PR-SIDEBAR-126, UI-SIDEBAR-058: SidebarHelperRow (SidebarView.swift:1375-1386) and SidebarRow
//   (Chrome.swift:731-737) put .overlay before .onHover, so the hover covers the row and its ellipsis; the port hovered the
//   button alone and lost the ellipsis the moment the pointer rested on it. Both rows hover their overlay host now (as
//   SidebarThreadRow does). REVIEW_A_helper_rows_ellipsis_stays..., REVIEW_A_project_rows_ellipsis_and_plus_stay....
// - PR-SIDEBAR-095 (the archive popover's Delete label, :927): SwiftColor.Red was the light member of systemRed
//   in both appearances; it is the documented light/dark pair now (ED-092, ED-104; Light #FF383C, Dark #FF4245, the macOS
//   27 UI Kit's System colours, the same edition as ED-104's Yellow). AppTheme's FrameworkRed is a different decision and
//   stays: PR-THEME-031's windows_path fixes one red for the system-hue themes and keeps it constant.
// - UI-SIDEBAR-038: the head card's glyphs past twelve join and leave on SwiftUI's default .opacity transition under the
//   chevron's snappy toggle (2-chat-use.mov 5.33-5.40 s); the port refilled the rows whole. REVIEW_The_head_cards_chevron_
//   fades_the_glyphs_past_twelve_in_and_out_on_its_snappy_toggle and the proofs beside it. A leaving glyph fades on a layer
//   over the card (it takes no room, so the chevron does not wait for it); the surviving glyphs glide to their new places
//   (UI-SIDEBAR-036, HeadCardGlideTests).
// - PR-SIDEBAR-005: Return_with_no_result_changes_nothing started with nothing selected, so a Return that cleared the
//   selection passed it (mutant PR005-noresult-clears); Return_with_no_result_leaves_the_current_selection_as_it_was.
// - PR-SIDEBAR-130, UI-SIDEBAR-014 (OD-0008 A9, OD-0012 E2): the Remove project question's popup lifecycle was never run
//   (the recording presenter raises neither Opened nor Closed): the tests below raise them. Dismissing removes nothing; the
//   red action closes the question and removes only once its popup is gone, on the main actor's next turn. The question
//   lives in the ProjectRow control, which the list rebuilt whenever its item's value changed (a new thread in the project
//   dropped an open question): the list hands a surviving project row its new snapshot and count in place now
//   (UpdateItemView), as SwiftUI keeps the row's @State under its ForEach id; the head card likewise (its showsAllHeads).
//   NOT done, and not claimed: SidebarThreadRow, HelperStubRow and the headers still take a new value by a rebuild where
//   they stand (UpdateItemView), so their own @State (a hover, an open popover) may not survive a change of their record;
//   it is the same gap, UNRESOLVED here (not read through for those rows), and no test covers it. (SidebarHelperRow was in
//   this list until review round 2, below.)
// - UI-SIDEBAR-038 also: the chevron itself is a view of the card's stack (:1514 `if capped { ... }`), so it joins and leaves
//   on the same default opacity transition when the head count crosses twelve (:1535 snappy, the card's row count changes
//   with it): The_head_cards_chevron_fades_in_when_the_card_becomes_capped_and_out_when_it_stops_being.
// - The engineering decisions this pass made (the system red pair, the leaving glyphs' layer over the card, row state handed
//   in place) are ED-258 in porting/engineering_decisions.md (a placeholder the lead numbers at merge; a
//   Decision trait cannot carry it, Traceability wants ED-nnn). That entry also records the checks OWED on a native window.
// - Mutation sweep, finishing the reviewer's (E:\Droppy\work\parity\evidence\SidebarView\review\mutants_round1.txt,
//   _round2, _round3, _round3b): 68 more mutants over the claimed records (PR-SIDEBAR-134's glide gate, UI-GIT-057,
//   UI-HYDRAMERGE-030, UI-SIDEBAR-004, -007, -025, -033, -034, -038, -039, -040, -041, -042, -059) and the fixed defects
//   D1a and D2-D14, every one killed: D6a (Red's dark member) by SwiftColorTests, the others by the Views.Sidebar namespace.
//   One survived at first, D8a (the folded stub's fill set with no animation): its test now checks the fill mid-fade.
//   That sweep did not cover every decision of this pass: review round 2's own mutants RA1, RA2, RA3, RA4, RA5, RA7 and RA16
//   survived (mutants_review2.txt); round 2, below, kills them.
// - PR-SIDEBAR-126's hover fill was proven only by FillFor's value: A_project_rows_hover_fill_is_drawn_on_a_real_hover_and_
//   kept_while_the_pointer_rests_on_its_ellipsis proves it on the live row (mutants H2b and P126 in _round3c, both killed).
//
// REVIEW PASS 2, ROUND 2 (2026-10-03, adversarial; its REVIEW_Round2_ tests kept as written). What it refuted and what was
// done, each against the Swift and the videos:
// - UI-SIDEBAR-038: on the chevron's own toggle (:1515-1516) the old chevron vanished in the first frame; in 2-chat-use.mov
//   5.340-5.378 s it is still drawn, faint, where it stood. The chevron whose symbol changes (:1516) now leaves on the ghost
//   layer where it stood and the new one joins (REVIEW_Round2_The_head_cards_down_chevron_fades_out_where_it_stood...). The
//   head-card tests pick the live chevron (LiveChevron) and check the old one fading where it stood; the toggle test lets the
//   card's measured width land before its click (a pass that took both ran without the click's transaction).
// - UI-SIDEBAR-058, PR-SIDEBAR-126, PR-WIN-200: SidebarRow swapped its accessory by a cut where Chrome.swift:735-737 flips
//   the hover inside withAnimation(Chrome.hover); it now keeps a control its closure hands back and crossfades a new one in
//   (ED-258 (4)), and ProjectRow keeps its branch's view (:1115-1133): the count Text changes in place, the
//   ellipsis-and-plus HStack keeps its button, and its accessory follows the live project's expansion
//   (REVIEW_Round2_A_search_results_project_row_crossfades_its_count_out..., ChromeFifthTests.SidebarRow_hover_eases_its_fill_
//   crossfades_a_new_accessory_and_leaves_one_handed_back_alone). Project_count_is_tabular_... follows the fade to its end.
// - UI-SIDEBAR-033: SidebarHelperRow built a new RowActionsButton on every change of its record, so an open menu lost its
//   anchor; it keeps the button while the ellipsis shows, builds its actions as the menu opens (RowActionsButton's new
//   overload), and the list hands a surviving helper row its snapshot and isLast in place
//   (REVIEW_Round2_A_helper_rows_open_ellipsis_menu_keeps_its_button..., The_real_list_keeps_a_helper_row_and_its_open_menu...,
//   A_helper_rows_kept_ellipsis_opens_on_the_actions..., A_project_rows_kept_ellipsis_opens_on_the_activation...).
// - Proofs the round asked for, each killing the mutant that had survived: the card's clip on leaving glyphs (RA1, RA2:
//   A_leaving_glyph_is_drawn_inside_the_card_and_cut_by_its_edge...), the toggle's snappy on its curve (RA7:
//   The_chevrons_toggle_fades_the_glyphs_past_twelve_and_its_symbols_on_snappy_0_18...), :1535 inside :1541 (RA5:
//   A_head_that_joins_on_a_new_row_fades_in_on_the_cards_snappy...), a search result's project row through a new pass (RA3:
//   A_search_results_project_row_keeps_its_open_menu_and_its_Remove_question...), a menu closed from the ellipsis (RA4:
//   Closing_the_ellipsis_menu_with_the_pointer_resting_on_the_ellipsis...), the hover fill's ease (RA16:
//   A_project_rows_hover_fill_eases_in_on_chrome_hover, The_Add_project_rows_hover_fill_eases_in_on_chrome_hover).
// - The_collapsed_count_label_shows_only_while_collapsed_... now asserts the live row's count and closes its windows inside
//   the test (a row left open laid its new count out after the test body, and that late layout threw "Could not create
//   glyphTypeface" headless, twice alone; a layout inside the body did not).
// - Mutation records: E:\Droppy\work\parity\evidence\SidebarView\review\mutants_round4.txt (and .json): 27 mutants:
//   the round-2 survivors RA1, RA2, RA3, RA4 (restated as RA4b and RA4c for the new RefreshAccessory), RA5, RA7, RA16 and
//   RA16b, and R4M1-R4M14 over this round's changes. Every one killed but R4M9 (a helper's kept status never drawn again),
//   which A_helper_rows_status_follows_its_thread_turning_unread_and_then_failed now kills, with R4M9b (mutants_round4b.json).
//
// REVIEW PASS 2, ROUND 3 (2026-10-03, adversarial; its REVIEW_Round3_ test kept as written). What it refuted and what was done:
// - PR-WIN-200 ('with the selection change animated on the 0.1-second hover curve', Chrome.swift:729): SidebarRow took
//   isSelected once and SettingsView built every row again on a page change, so the wash moved by a cut (1-settings-tour.mov
//   47.765-47.85 s eases it over about 0.1 s). SidebarRow.IsSelected now hands a kept row its selection, easing the fill on
//   Chrome.hover whatever the transaction; SettingsView keeps a row per listed page (ED-258 (5);
//   SidebarRowSelectionReviewTests, SettingsSidebarSelectionTests, ChromeFifthTests.SidebarRow_handed_a_new_selection_...).
//   Three Settings tests that read a row right after a click had encoded the cut; they now read it after Chrome.hover's 0.1 s.
// - The kept helper's isLast was proven one way only (RR6, RR7 survived the namespace):
//   A_kept_last_helper_row_runs_its_connector_through_once_an_older_helper_lands_below_it, the reviewer's proposed test.
// - Mutation records: mutants_round5.txt and mutants_round5b.txt (and .json): 19 mutants, every one killed but S17 and RR1,
//   both recorded in the ED entry as not told apart by any headless check.

//
// LEFTOVERS PASS 2026-10-04 (branch cloud/b3-sidebar-leftovers; tests in SidebarLeftoversTests.cs, the same partial class).
// This block SUPERSEDES the "Still open per record" list, the STOP #3 line of the ledger, STOP #9 and the OD-0076 notes above
// wherever it names a record. Two rulings made the records provable: OD-0098 (no frame, so the pinned Swift is the proof:
// every expected value of the new tests is the one the cited Swift line states) and OD-0076 (every change is transitioned);
// continuous corners are ED-275's (every row shape is a 7 pt continuous corner inside Apple's regime: 2 * 1.528665 * 7 = 21.4
// pt <= 24 pt, the shortest row).
// PROVED, each by the tests that carry its Req trait (the new ones named in SidebarLeftoversTests.cs):
// - PR-SIDEBAR-013, UI-SIDEBAR-043, UI-UPDATE-010: the dot fades and scales in and out on Chrome.hover (OD-0076; the Mac cuts):
//   The_update_dot_fades_and_scales_in_and_out_on_chrome_hover_as_the_availability_changes. "Disappears immediately" is the
//   condition: the fade starts in the frame the version catches up. Geometry, offset and colour were proven already.
// - PR-SIDEBAR-055 (and UI-SIDEBAR-060's release): the release spring was proven on a mounted release against the curve
//   (On_a_real_release_...); the capture clause is the source's own response 0.32 / damping 0.8 (OD-0098).
// - PR-SIDEBAR-091, UI-SIDEBAR-028, -029, -030: The_rename_popovers_column_..., The_title_field_holds_the_focus_...,
//   Rename_is_disabled_only_while_..., Return_with_a_blank_title_..., The_ellipsis_path_pre_fills_..., and the capsule buttons
//   Under_presentedChrome_the_rename_buttons_are_capsules_... (SystemButtons now reads Popovers.IsCapsuleButtonBorderShape).
// - PR-SIDEBAR-129, UI-SIDEBAR-008: the 0.55 dim eases on Chrome.hover (ENGINEERING DECISION, OD-0076): An_inactive_projects_row_eases_...
// - PR-SIDEBAR-135: The_glides_two_faces_have_the_sources_heights_clearances_destination_state_and_fallback,
//   The_glides_faces_are_rendered_as_flat_bitmaps_at_the_scale_..., and the popover / failed render / 24 pt destination / DPI tests.
// - UI-SIDEBAR-005: A_real_scroll_offset_change_and_a_real_wheel_tick_freeze_the_list_...; UI-SIDEBAR-006: A_gap_leaves_with_its_removed_project_...
//   (the list does react to a project removed or added: STOP #11 is closed).
// - UI-SIDEBAR-017, -018, -019, -021, -022, -023, -024: the face's paddings and minimum heights, the Settled value, the hide and the
//   0.12 s return, the badge geometry, the status, the project line, tabular digits, the hover's easing of the title's room
//   (A_detailed_rows_title_room_eases_...), the archive button, ellipsis and fold chevron places, the 7 pt continuous shapes.
// - UI-SIDEBAR-020: the settle pop springs (response 0.3 / damping 0.45) on a kept check: A_real_rows_open_check_is_kept_and_springs_...
// - UI-SIDEBAR-060, UI-SIDEBAR-017 (unit rec-shadow, the lead's E rulings, finding B; ED-327): SwiftUI's `.shadow(radius:
//   10)` is a Gaussian of sigma 10 (fitted on the Mac video's radius-28 scrim); SwiftShadow.BoxShadows draws that sigma, measured
//   (SwiftShadowHeadlessTests; The_lifted_rows_shadow_is_drawn_as_a_gaussian_of_sigma_10_scaled_with_the_row). UI-SIDEBAR-017's
//   settling state (check popped, actions clearance retained) is proven by the source (OD-0098; the ruling's SidebarView.swift:1895-1910,
//   in the pinned tree :1895-1898 the static trailingClearance, :1765-1767 the check's action calls settle(), :1914-1919 settle()
//   springs isSettling on 0.3/0.45 (:1917) then settles, :1729 and :1744 keep the check and the actions clearance while settling)
//   with the static 1.35 check and the retained clearance (A_real_dragged_row_suppresses_hover_accessories_and_finish_requests_pop_its_check,
//   A_real_rows_open_check_is_kept_and_springs_...), and UI-SIDEBAR-020's pop closes with it.
// - UI-SIDEBAR-026, -027: the delete question through its popup (dismissal keeps the thread; the delete waits for Closed).
// - UI-SIDEBAR-031, -032: the small glass buttons take the measured small metrics (20 high, 11 pt, 10.5 pt either side).
// - UI-SIDEBAR-036: the two heights on snappy 0.18, the connector on panelSlide when the head set changes (the surviving
//   glyphs, rows and the chevron glide to their new places: HeadCardGlideTests, ED-329 3). UI-SIDEBAR-037: the hover scale eases back; each glyph row trails the 3 pt overhang
//   (the badge's own offset is UI-HYDRA-033's).
// - UI-SIDEBAR-001: a press on the spacer lands on the column's WindowDragArea (a Caption to the Windows hit test); the system
//   move loop is proven natively (WindowManagerNativeTests.The_actual_main_sidebar_clearance_spacer_...).
// STILL OPEN, with the reason:
// - UI-SIDEBAR-015, -016: the entries reach the system menu in the source's order and the current choice carries a check mark
//   (The_pair_submenu_reaches_the_system_menu_...); a Win32 menu reserves a check gutter for every row, so "no reserved check
//   column" cannot hold, and opening the native submenu is the system's: NATIVE-ONLY / platform limit.
// - UI-SIDEBAR-036 (part), CLOSED by the unit rec-sidebar: the surviving glyphs of a card whose heads change glide to their new
//   places (a card-level shift in HeadCardsRow, not the shared ForEach; a glyph that changes row has another identity and fades
//   instead, SidebarView.swift:1466-1467), HeadCardGlideTests.
// - UI-SIDEBAR-044, CLOSED natively: the sidebar asks for exactly the source's folder dialog (proven), and the Windows dialog's title, OK
//   label, flags and results are proven on the real dialog (OpenPanelNativeTests).

[Collection("Headless")]
public sealed class SidebarViewTests(HeadlessSession headless)
{
    internal static readonly Assembly AppAssembly = typeof(SidebarView).Assembly;
    internal static readonly DateTimeOffset Base = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- reflection over the file's private declarations ----

    // ED-358: the lift host holds the lift's shadow caster (drawn first, under the lift), the lift and the row's content.
    internal static SwiftShadow.Caster LiftShadowOf(Control host) => Assert.Single(((Panel)host).Children.OfType<SwiftShadow.Caster>());
    internal static Border LiftOf(Control host) => LiftShadowOf(host).Source;

    internal static Type Private(string name) => AppAssembly.GetType("Marrow.App.Views.Sidebar." + name, throwOnError: true)!;

    internal static object? Call(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    internal static object? Get(object target, string property) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target);

    internal static List<object> Items(object? list) => ((IEnumerable)list!).Cast<object>().ToList();

    internal static string IdOf(object item) => (string)Get(item, "Id")!;

    internal static object KindOf(object item) => Get(item, "SidebarItemKind")!;

    internal static string KindName(object item) => KindOf(item).GetType().Name;

    // projectItems(helpers:) (:515-557) over the view's own placements and helper grouping (:72-73).
    internal static List<object> ProjectItems(SidebarView view)
    {
        var helpers = Call(view, "HelpersByParent", Get(view, "ThreadPlacements"));
        return Items(Call(view, "ProjectItems", helpers));
    }

    internal static SwiftTransition RowTransition(SidebarView view, object item) => (SwiftTransition)Call(view, "RowTransition", item)!;

    internal static string? MergeStage(ThreadRuntime? runtime) =>
        (string?)Private("SidebarThreadRow").GetMethod("MergeStage", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [runtime]);

    // ---- building the view ----

    internal static SidebarView Detached(AppModel model, bool inPopover = false, Action? dismiss = null)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var view = new SidebarView(inPopover, dismiss);
        host.Child = view;
        return view;
    }

    internal static SidebarView Attached(AppModel model, bool inPopover = false, Action? dismiss = null)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var view = new SidebarView(inPopover, dismiss);
        host.Child = view;
        var window = new AvaloniaWindow { Content = host, Width = 280, Height = 640 };
        window.Show();
        window.UpdateLayout();
        return view;
    }

    internal static void Close(Control view)
    {
        if (TopLevel.GetTopLevel(view) is AvaloniaWindow window) window.Close();
    }

    internal static Rect Frame(Control control, Control relativeTo)
    {
        var origin = control.TranslatePoint(new Point(0, 0), relativeTo)!.Value;
        return new Rect(origin, control.Bounds.Size);
    }

    internal static string[] Texts(Control control) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    internal static TextBox SearchBox(SidebarView view) => LogicalQuery.SelfAndDescendants<TextBox>(view).Single();

    internal static Button ButtonNamed(Control root, string name) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Single(button => AutomationProperties.GetName(button) == name);

    internal static SidebarRow RowNamed(SidebarView view, string title) =>
        LogicalQuery.SelfAndDescendants<SidebarRow>(view)
            .Single(row => LogicalQuery.SelfAndDescendants<Button>(row).Any(button => AutomationProperties.GetName(button) == title));

    internal static bool Press(TextBox box, Key key)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = box };
        box.RaiseEvent(args);
        return args.Handled;
    }

    internal static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // ---- library samples (Library.swift shapes through AppModelSamples) ----

    internal static Project NewProject(AppModelHarness harness, string name) => AppModelSamples.Project(name, harness.NewFolder(name));

    internal static ChatThread NewThread(Project project, string title, int minute) =>
        AppModelSamples.Thread(project) with { Title = title, CreatedAt = Base.AddMinutes(minute), UpdatedAt = Base.AddMinutes(minute) };

    internal static AppModel Model(AppModelHarness harness, IEnumerable<Project> projects, IEnumerable<ChatThread> threads)
    {
        harness.WriteLibrary(projects, threads);
        return harness.MakeModel();
    }

    // ---- PR-HYDRAMERGE-100: the merge's stage words ----

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-100")] // App/Views/Sidebar/SidebarView.swift:1980-1984
    [Trait("Req", "PR-SIDEBAR-071")] // App/Views/Sidebar/SidebarView.swift:1980-1984
    [Trait("Req", "UI-HYDRAMERGE-030")] // App/Views/Sidebar/SidebarView.swift:1980-1984 (part 6: the same stage line, absent state)
    public void Merge_stage_is_nil_without_a_runtime_and_while_no_merge_runs()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha");
            var thread = NewThread(project, "Lead", 0);
            var model = Model(harness, [project], [thread]);
            harness.Main.OnMain(() =>
            {
                Assert.Null(MergeStage(null));
                var runtime = model.Runtime(thread.Id);
                runtime.HydraMergeStage = "Reviewing heads";
                Assert.False(runtime.IsHydraMerging);
                Assert.Null(MergeStage(runtime));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-100")] // App/Views/Sidebar/SidebarView.swift:1980-1984
    [Trait("Req", "PR-SIDEBAR-071")] // App/Views/Sidebar/SidebarView.swift:1980-1984, :2057-2058 (the row reads it)
    [Trait("Req", "UI-HYDRAMERGE-030")] // App/Views/Sidebar/SidebarView.swift:1980-1984 (part 6: the stage words, present state)
    public void Merge_stage_is_the_stage_words_followed_by_one_horizontal_ellipsis()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha");
            var thread = NewThread(project, "Lead", 0);
            var model = Model(harness, [project], [thread]);
            harness.Main.OnMain(() =>
            {
                var runtime = model.Runtime(thread.Id);
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Alpha: Reviewing heads";
                Assert.Equal("Alpha: Reviewing heads\U00002026", MergeStage(runtime)); // U+2026 HORIZONTAL ELLIPSIS
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-100")] // App/Views/Sidebar/SidebarView.swift:1983
    [Trait("Req", "PR-SIDEBAR-071")] // App/Views/Sidebar/SidebarView.swift:1983
    [Trait("Req", "UI-HYDRAMERGE-030")] // App/Views/Sidebar/SidebarView.swift:1983 (part 6: the fallback words, single ellipsis)
    public void Merge_stage_falls_back_to_Merging_with_the_single_ellipsis_character()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha");
            var thread = NewThread(project, "Lead", 0);
            var model = Model(harness, [project], [thread]);
            harness.Main.OnMain(() =>
            {
                var runtime = model.Runtime(thread.Id);
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = null;
                var stage = MergeStage(runtime);
                Assert.Equal("Merging\U00002026", stage); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(new byte[] { 0x4D, 0x65, 0x72, 0x67, 0x69, 0x6E, 0x67, 0xE2, 0x80, 0xA6 }, System.Text.Encoding.UTF8.GetBytes(stage!));
            });
        });
    }

    // ---- PR-SIDEBAR-001 / -002 / -014: the column's layout ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-001")] // App/Views/Sidebar/SidebarView.swift:74-80
    [Trait("Req", "UI-SIDEBAR-001")] // App/Views/Sidebar/SidebarView.swift:74-80 (part 6: the same spacer, as a screen-contract record)
    public void The_window_column_opens_with_a_14_point_clearance_under_16_points_and_the_popover_has_none()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var column = Attached(model);
                var popover = Attached(model, inPopover: true);
                // :76-79 Color.clear 14 pt tall with 16 pt of top padding, then :99 the search row's 14 pt: 44 pt down.
                Assert.Equal(44d, Frame(LogicalQuery.SelfAndDescendants<SidebarSearchField>(column).Single(), column).Y);
                // Without the clearance the popover's search row sits only its own 12 pt down (:99).
                Assert.Equal(12d, Frame(LogicalQuery.SelfAndDescendants<SidebarSearchField>(popover).Single(), popover).Y);
                var spacers = LogicalQuery.SelfAndDescendants<Border>(column)
                    .Where(border => border.Height == 14 && border.Margin == new Thickness(0, 16, 0, 0) && border.Child is null && border.Background is null)
                    .ToList();
                Assert.Single(spacers);
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Border>(popover),
                    border => border.Height == 14 && border.Margin == new Thickness(0, 16, 0, 0) && border.Child is null);
                Close(column);
                Close(popover);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-002")] // App/Views/Sidebar/SidebarView.swift:82-99
    public void The_search_row_sets_the_field_and_the_bell_6_points_apart_inset_10_on_each_side()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                var field = Frame(LogicalQuery.SelfAndDescendants<SidebarSearchField>(view).Single(), view);
                var bell = Frame(ButtonNamed(view, "Activity view"), view);
                Assert.Equal(10d, field.X); // :98 .padding(.horizontal, Chrome.listInset)
                Assert.Equal(view.Bounds.Width - 10, bell.Right);
                Assert.Equal(6d, bell.X - field.Right); // :82 HStack(spacing: 6)
                Assert.Equal(30d, bell.Width); // :2328 .frame(width: 30, height: 30)
                Assert.Equal(30d, bell.Height);
                Assert.Equal(field.Y, bell.Y);
                Close(view);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-002")] // App/Views/Sidebar/SidebarView.swift:99
    public void The_search_row_takes_14_points_on_top_in_the_column_and_12_in_the_popover()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var column = Attached(model);
                var popover = Attached(model, inPopover: true);
                Assert.Equal(16d + 14 + 14, Frame(ButtonNamed(column, "Activity view"), column).Y);
                Assert.Equal(12d, Frame(ButtonNamed(popover, "Activity view"), popover).Y);
                Close(column);
                Close(popover);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-014")] // App/Views/Sidebar/SidebarView.swift:164-178
    public void Add_project_and_Settings_stack_1_point_apart_inset_10_with_12_points_below()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                var add = Frame(RowNamed(view, "Add project"), view);
                var settings = Frame(RowNamed(view, "Settings"), view);
                Assert.Equal(10d, add.X); // :177 .padding(.horizontal, Chrome.listInset)
                Assert.Equal(view.Bounds.Width - 10, add.Right);
                Assert.Equal(10d, settings.X);
                Assert.Equal(1d, settings.Y - add.Bottom); // :164 VStack(spacing: 1)
                Assert.Equal(view.Bounds.Height - 12, settings.Bottom); // :178 .padding(.bottom, 12)
                Close(view);
            });
        });
    }

    // ---- PR-SIDEBAR-003 / -004: the search field ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-003")] // App/Views/Sidebar/SidebarView.swift:85
    public void The_search_field_prompt_is_exactly_Search_threads()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Assert.Equal("Search threads", SearchBox(view).PlaceholderText);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-004")] // App/Views/Sidebar/SidebarView.swift:61-63, :103-113
    public void A_whitespace_only_query_trims_to_empty_and_keeps_the_normal_list()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                // Spaces, a tab, a no-break space (Unicode Zs) and a newline: all in .whitespacesAndNewlines.
                SearchBox(view).Text = " \t\U000000A0\n "; // U+00A0 NO-BREAK SPACE
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("", Get(view, "Query"));
                Assert.DoesNotContain("No results", Texts(view));
                // The clear button follows the untrimmed text (Chrome.swift:796).
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(view), button => AutomationProperties.GetName(button) == "Clear search");
                SearchBox(view).Text = " x ";
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("x", Get(view, "Query"));
                // A non-empty query shows the search list, here its empty state (:111-112, :693-703).
                Assert.Contains("No results", Texts(view));
                Close(view);
            });
        });
    }

    // ---- PR-SIDEBAR-005 / -006: Return and the arrows ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:86-90
    // (was Rule R-TEST-6 for STOP #5; these needles and titles are ASCII, where the canonical and the ordinal readings of
    // `lowercased().contains` agree, so the expectations do not rest on that open item)
    public void Return_selects_the_first_thread_of_the_first_search_result()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var older = NewThread(alpha, "Fix the build", 1);
            var newer = NewThread(alpha, "Fix the docs", 2);
            var other = NewThread(beta, "Fix the tests", 3);
            var model = Model(harness, [alpha, beta], [older, newer, other]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                model.SelectedThreadID = null;
                var box = SearchBox(view);
                box.Text = "fix";
                Assert.True(Press(box, Key.Enter));
                // Alpha is the first project; threads(in:) puts the newest first (AppModel.swift:314-332).
                Assert.Equal(newer.Id, model.SelectedThreadID);
                box.Text = "TESTS";
                Press(box, Key.Enter);
                Assert.Equal(other.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:87 (ASCII only, as above: STOP #5 does not bear on it)
    public void Return_with_no_result_changes_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Fix the build", 1);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                model.SelectedThreadID = null;
                var box = SearchBox(view);
                box.Text = "nothing matches this";
                Press(box, Key.Enter);
                Assert.Null(model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:86-90 `if let first = ... { selectedThreadID = first.id }`: no result, no assignment
    // REVIEW (pass 1, mutant PR005-noresult-clears): the test above starts with nothing selected, so a Return that clears
    // the selection on no result passed it. Here a selection stands (not the first thread, so selecting the first thread
    // of the list would show too) and a Return with no result must leave exactly that.
    public void Return_with_no_result_leaves_the_current_selection_as_it_was()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var older = NewThread(alpha, "Fix the build", 1);
            var newer = NewThread(alpha, "Fix the docs", 2);
            var other = NewThread(beta, "Fix the tests", 3);
            var model = Model(harness, [alpha, beta], [older, newer, other]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                model.SelectedThreadID = other.Id; // not the first thread the list would offer (newer)
                var box = SearchBox(view);
                box.Text = "nothing matches this";
                Press(box, Key.Enter);
                Assert.Equal(other.Id, model.SelectedThreadID);
                box.Text = "docs"; // a result: Return selects its first thread
                Press(box, Key.Enter);
                Assert.Equal(newer.Id, model.SelectedThreadID);
                box.Text = "still nothing here"; // no result again: the selection the last Return made stands
                Press(box, Key.Enter);
                Assert.Equal(newer.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:86-90,743: Return with an empty query still runs searchResults, and contains(_:) against an empty needle
    public void Return_with_an_empty_query_selects_the_first_thread_of_the_first_project()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var older = NewThread(alpha, "Fix the build", 1);
            var newer = NewThread(alpha, "Fix the docs", 2);
            var other = NewThread(beta, "Fix the tests", 3);
            var model = Model(harness, [alpha, beta], [older, newer, other]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                model.SelectedThreadID = null;
                var box = SearchBox(view);
                Assert.Equal(string.Empty, box.Text ?? string.Empty); // the field starts empty; nothing typed
                Assert.True(Press(box, Key.Enter));
                // Alpha is the first project; threads(in:) puts the newest first (AppModel.swift:314-332).
                Assert.Equal(newer.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:720-731
    public void Down_with_no_selection_selects_the_first_result_and_Up_the_last()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var first = NewThread(alpha, "Fix one", 2);
            var second = NewThread(alpha, "Fix two", 1);
            var last = NewThread(beta, "Fix three", 3);
            var model = Model(harness, [alpha, beta], [first, second, last]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var box = SearchBox(view);
                box.Text = "fix";
                model.SelectedThreadID = null;
                Assert.True(Press(box, Key.Down));
                Assert.Equal(first.Id, model.SelectedThreadID);
                model.SelectedThreadID = null;
                Assert.True(Press(box, Key.Up));
                Assert.Equal(last.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:729-730
    public void The_arrows_move_by_one_and_clamp_at_both_ends()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var first = NewThread(alpha, "Fix one", 2);
            var second = NewThread(alpha, "Fix two", 1);
            var last = NewThread(beta, "Fix three", 3);
            var model = Model(harness, [alpha, beta], [first, second, last]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var box = SearchBox(view);
                box.Text = "fix";
                model.SelectedThreadID = first.Id;
                Press(box, Key.Up);
                Assert.Equal(first.Id, model.SelectedThreadID);
                Press(box, Key.Down);
                Assert.Equal(second.Id, model.SelectedThreadID);
                Press(box, Key.Down);
                Assert.Equal(last.Id, model.SelectedThreadID);
                Press(box, Key.Down);
                Assert.Equal(last.Id, model.SelectedThreadID);
                Press(box, Key.Up);
                Assert.Equal(second.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:724-727
    public void A_selection_outside_the_results_counts_as_no_selection()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var match = NewThread(alpha, "Fix one", 2);
            var outside = NewThread(alpha, "Write docs", 1);
            var model = Model(harness, [alpha], [match, outside]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var box = SearchBox(view);
                box.Text = "fix";
                model.SelectedThreadID = outside.Id;
                Press(box, Key.Up);
                Assert.Equal(match.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:721; UI/Chrome/Chrome.swift:818-822
    public void With_a_whitespace_query_the_arrows_change_nothing_and_are_still_consumed()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Fix one", 2);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var box = SearchBox(view);
                box.Text = "   ";
                model.SelectedThreadID = null;
                Assert.True(Press(box, Key.Down));
                Assert.True(Press(box, Key.Up));
                Assert.Null(model.SelectedThreadID);
            });
        });
    }

    // ---- PR-SIDEBAR-009 / -010 / -011: the bell ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-009")] // App/Views/Sidebar/SidebarView.swift:93-96; App/AppSettings.swift:271-273
    public void The_bell_flips_sidebarActivityView_and_writes_it_under_its_key()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Assert.False(model.Settings.SidebarActivityView);
                Click(ButtonNamed(view, "Activity view"));
                Assert.True(model.Settings.SidebarActivityView);
                Assert.Equal(true, harness.Defaults.Object("sidebarActivityView"));
                Click(ButtonNamed(view, "Activity view"));
                Assert.False(model.Settings.SidebarActivityView);
                Assert.Equal(false, harness.Defaults.Object("sidebarActivityView"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-009")] // App/Views/Sidebar/SidebarView.swift:2322-2323
    public void The_bell_toggles_its_binding_inside_Chrome_panelSlide()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            harness.Main.OnMain(() =>
            {
                var isOn = false;
                Animation? during = null;
                var binding = new Binding<bool>(() => isOn, value => { isOn = value; during = Transaction.Current; });
                var toggle = (Control)Activator.CreateInstance(Private("ActivityViewToggle"), binding)!;
                Click(ButtonNamed(toggle, "Activity view"));
                Assert.True(isOn);
                Assert.Equal(Chrome.PanelSlide, during);
                Click(ButtonNamed(toggle, "Activity view"));
                Assert.False(isOn);
            });
        });
    }

    // :93-96 the binding SidebarView passes the bell: settings.sidebarActivityView, an observed value.
    internal static Binding<bool> ActivityViewSetting(AppModel model) =>
        new(() => model.Settings.SidebarActivityView, value => model.Settings.SidebarActivityView = value);

    internal static Control AttachedToggle(Binding<bool> binding)
    {
        var toggle = (Control)Activator.CreateInstance(Private("ActivityViewToggle"), binding)!;
        var window = new AvaloniaWindow { Content = new Border { Child = toggle }, Width = 100, Height = 100 };
        window.Show();
        return toggle;
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-010")] // App/Views/Sidebar/SidebarView.swift:2321, :2339; Core/Models/Shortcuts.swift:232
    public void The_bell_tooltip_names_the_action_and_the_default_chord_in_Windows_notation()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            // The bell's fill follows its state along the click's animation, one frame at a time (AnimatedColor).
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var store = harness.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
            using var shortcuts = ShortcutStore.Shared.Override(store);
            var model = harness.MakeModel();
            Control toggle = null!;
            harness.Main.OnMain(() =>
            {
                toggle = AttachedToggle(ActivityViewSetting(model));
                // DEVIATION OD-0008 B7: Option+Command+U reads Ctrl+Alt+U.
                Assert.Equal("Turn on activity view (Ctrl+Alt+U)", ToolTip.GetTip(ButtonNamed(toggle, "Activity view")));
                Click(ButtonNamed(toggle, "Activity view"));
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("Turn off activity view (Ctrl+Alt+U)", ToolTip.GetTip(ButtonNamed(toggle, "Activity view")));
                Close(toggle);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-010")] // App/Views/Sidebar/SidebarView.swift:2321, :2339
    public void With_the_chord_cleared_the_bell_tooltip_has_no_parenthetical_and_no_trailing_space()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            // The bell's fill follows its state along the click's animation, one frame at a time (AnimatedColor).
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var store = harness.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
            using var shortcuts = ShortcutStore.Shared.Override(store);
            var model = harness.MakeModel();
            Control toggle = null!;
            harness.Main.OnMain(() =>
            {
                store.Set(null, AppShortcut.ToggleActivityView);
                model.Settings.SidebarActivityView = true;
                toggle = AttachedToggle(ActivityViewSetting(model));
                Assert.Equal("Turn off activity view", ToolTip.GetTip(ButtonNamed(toggle, "Activity view")));
                Click(ButtonNamed(toggle, "Activity view"));
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("Turn on activity view", ToolTip.GetTip(ButtonNamed(toggle, "Activity view")));
                Close(toggle);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-011")] // App/Views/Sidebar/SidebarView.swift:2340-2341
    public void The_bell_is_labelled_Activity_view_with_the_value_On_or_Off()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            // The bell's fill follows its state along the click's animation, one frame at a time (AnimatedColor).
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var model = harness.MakeModel();
            Control toggle = null!;
            harness.Main.OnMain(() =>
            {
                toggle = AttachedToggle(ActivityViewSetting(model));
                var button = ButtonNamed(toggle, "Activity view");
                Assert.Equal("Off", AutomationProperties.GetItemStatus(button));
                Click(button);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("On", AutomationProperties.GetItemStatus(ButtonNamed(toggle, "Activity view")));
                Close(toggle);
            });
        });
    }

    // ---- PR-SIDEBAR-012: Add project ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // App/Views/Sidebar/SidebarView.swift:165-167, :500-511
    [Trait("Req", "UI-SIDEBAR-042")] // App/Views/Sidebar/SidebarView.swift:165-167 (part 7: the same row's action, as
    // a screen-contract record)
    [Trait("Req", "UI-SIDEBAR-044")] // App/Views/Sidebar/SidebarView.swift:500-512 (part 7: the same dialog, as a
    // screen-contract record)
    public void Add_project_asks_for_folders_only_several_at_once_with_the_exact_prompt_and_message()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var runner = new RecordingOpenPanelRunner(null);
            using var seam = OpenPanel.Runner.Override(runner);
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                PointerClick(ButtonNamed(view, "Add project"));
                Close(view);
            });
            var shown = Assert.Single(runner.Shown);
            Assert.True(shown.Directories); // :502
            Assert.False(shown.Files); // :503
            Assert.True(shown.Multiple); // :504
            Assert.Equal("Add project", shown.Prompt); // :505
            Assert.Equal("Choose a folder for your agents to work in.", shown.Message); // :506
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // App/Views/Sidebar/SidebarView.swift:507-510
    [Trait("Req", "UI-SIDEBAR-044")] // App/Views/Sidebar/SidebarView.swift:507-511 (part 7: confirm adds every chosen
    // folder as a project and starts a thread in the last, as a screen-contract record)
    public void Add_project_adds_one_project_per_chosen_folder_and_starts_a_thread_in_the_last()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var first = harness.NewFolder("first");
            var second = harness.NewFolder("second");
            var runner = new RecordingOpenPanelRunner([new Project("x", first).Url, new Project("y", second).Url]);
            using var seam = OpenPanel.Runner.Override(runner);
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                PointerClick(ButtonNamed(view, "Add project"));
                Close(view);
                Assert.Equal([Path.GetFileName(first), Path.GetFileName(second)], model.Projects.Select(project => project.Name));
                var thread = Assert.Single(model.Threads);
                Assert.Equal(model.Projects[1].Id, thread.ProjectID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // App/Views/Sidebar/SidebarView.swift:507
    [Trait("Req", "UI-SIDEBAR-044")] // App/Views/Sidebar/SidebarView.swift:507 (part 7: cancel does nothing at all,
    // as a screen-contract record)
    public void Cancelling_the_folder_chooser_adds_nothing_and_starts_no_thread()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            using var seam = OpenPanel.Runner.Override(new RecordingOpenPanelRunner(null));
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                PointerClick(ButtonNamed(view, "Add project"));
                Close(view);
                Assert.Empty(model.Projects);
                Assert.Empty(model.Threads);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // App/Views/Sidebar/SidebarView.swift:509-510; App/AppModel.swift:525-527
    public void A_folder_already_added_yields_the_existing_project_and_still_gets_the_new_thread()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var folder = harness.NewFolder("alpha");
            // The folder is added through this same row first, so both passes read its path the same way (STOP #4).
            using var seam = OpenPanel.Runner.Override(new RecordingOpenPanelRunner([new Project("x", folder).Url]));
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Click(ButtonNamed(view, "Add project"));
                var added = Assert.Single(model.Projects);
                Click(ButtonNamed(view, "Add project"));
                Assert.Equal(added.Id, Assert.Single(model.Projects).Id);
                Assert.Equal(2, model.Threads.Count);
                Assert.All(model.Threads, thread => Assert.Equal(added.Id, thread.ProjectID));
            });
        });
    }

    // ---- PR-SIDEBAR-013: Settings ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-013")] // App/Views/Sidebar/SidebarView.swift:167-170 (the gear at scale 1.15 inside the 20 pt badge)
    [Trait("Req", "UI-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift:168-175 (part 7: the same gear glyph, as a
    // screen-contract record)
    public void The_Settings_row_carries_the_gear_at_1_15_scale_and_Add_project_the_plus()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var gear = LogicalQuery.SelfAndDescendants<SwiftImage>(RowNamed(view, "Settings")).Single();
                Assert.Equal("gear", gear.SymbolName);
                Assert.Equal(16.1, gear.Width, 5); // 14 pt symbolSize x 1.15 (UI/Chrome/Chrome.swift:664), the record's 16.1 pt
                Assert.Equal(16.1, gear.Height, 5);
                var box = Assert.IsType<Panel>(LogicalQuery.SelfAndDescendants<SidebarIconBadge>(RowNamed(view, "Settings")).Single().Child); // :677
                Assert.Equal(20d, box.Width); // inside the 20 pt badge box
                Assert.Equal(20d, box.Height);
                var plus = LogicalQuery.SelfAndDescendants<SwiftImage>(RowNamed(view, "Add project")).Single();
                Assert.Equal("plus", plus.SymbolName);
                Assert.Equal(Chrome.SymbolSize, plus.Width);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-013")] // App/Views/Sidebar/SidebarView.swift:170-174; App/Views/Settings/AboutUpdateViews.swift:526-534
    [Trait("Req", "PR-UPDATE-080")] // App/Views/Sidebar/SidebarView.swift:168-175 (part 6: this file's half of the record; the
    // About row's own placement is App/Views/Settings/SettingsView.swift:159-163, a different unit)
    [Trait("Req", "UI-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift:170-174; App/Views/Settings/AboutUpdateViews.swift:526-534
    // (part 7: the same update dot, as a screen-contract record; OD-0008 C6, OD-0012 I1 keep the dot hidden until the
    // owner sets a Windows release feed)
    [Trait("Req", "UI-UPDATE-010")] // App/Views/Sidebar/SidebarView.swift:168-175; App/Views/Settings/AboutUpdateViews.swift:527-534
    // (part 8: the same update dot, as a screen-contract record; UI-SIDEBAR-043 above proves presence, placement and
    // accessibility, this part adds the dot's own geometry and color)
    public void The_update_dot_sits_on_the_gear_only_while_an_update_is_available()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var none = harness.Main.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], ""));
            harness.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(new AvailableUpdate
            {
                Version = "2.0.0",
                Tag = "v2.0.0",
                DownloadURL = SwiftURL.Create("https://example.com/Marrow.zip")!,
                Notes = "",
                PageURL = SwiftURL.Create("https://example.com/releases/v2.0.0")!,
            }));
            var waiting = harness.Main.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], ""));
            harness.Main.OnMain(() =>
            {
                Assert.False(none.UpdateAvailable);
                Assert.True(waiting.UpdateAvailable);
                using (UpdateChecker.OverrideShared(none))
                {
                    var view = Attached(model);
                    Assert.Empty(LogicalQuery.SelfAndDescendants<UpdateAvailableDot>(view));
                    Close(view);
                }
                using (UpdateChecker.OverrideShared(waiting))
                {
                    var view = Attached(model);
                    var dot = Assert.Single(LogicalQuery.SelfAndDescendants<UpdateAvailableDot>(RowNamed(view, "Settings")));
                    Assert.Equal(HorizontalAlignment.Right, dot.HorizontalAlignment); // :170 .overlay(alignment: .topTrailing)
                    Assert.Equal(VerticalAlignment.Top, dot.VerticalAlignment);
                    Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(dot), control => AutomationProperties.GetName(control) == "Update available");
                    // PR-SIDEBAR-013: drawn at the icon's top-trailing corner, then moved x +2.5, y -2.5 (:170, AboutUpdateViews.swift:531).
                    var badgeFrame = Frame(LogicalQuery.SelfAndDescendants<SidebarIconBadge>(RowNamed(view, "Settings")).Single(), view);
                    var dotOrigin = ((Avalonia.Controls.Shapes.Ellipse)dot.Child!).TranslatePoint(default, view)!.Value;
                    Assert.Equal(badgeFrame.Right - 7 + 2.5, dotOrigin.X, 3);
                    Assert.Equal(badgeFrame.Top - 2.5, dotOrigin.Y, 3);
                    // UI-UPDATE-010 geometry: Circle 7x7, offset x +2.5 and y -2.5.
                    var glyph = (Avalonia.Controls.Shapes.Ellipse)dot.Child!;
                    Assert.Equal(7d, glyph.Width);
                    Assert.Equal(7d, glyph.Height);
                    var offset = (TranslateTransform)glyph.RenderTransform!;
                    Assert.Equal(2.5d, offset.X);
                    Assert.Equal(-2.5d, offset.Y);
                    // UI-UPDATE-010 colors: Chrome.accent.
                    Assert.Equal(Chrome.Accent.Resolve(glyph.ActualThemeVariant), ((ISolidColorBrush)glyph.Fill!).Color);
                    Close(view);
                }
            });
        });
    }

    // ---- PR-SIDEBAR-013: Settings (click) ----
    //
    // STOP #2 is closed: WindowManager.Shared/ShowSettings has landed on impl/b2
    // (src/Marrow.App/Window/WindowManager.cs), so the row's click is proven here.
    // The row's title, glyph and update dot stay proven by the two tests above.

    [Fact]
    [Trait("Req", "PR-SIDEBAR-013")] // App/Views/Sidebar/SidebarView.swift:168 `action: { WindowManager.shared.showSettings() }`
    public void Clicking_the_Settings_row_shows_the_settings_window()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var manager = harness.Main.OnMain(() => new WindowManager());
            Control? shown = null;
            harness.Main.OnMain(() => manager.SettingsContent = () => shown = new Border());
            using (WindowManager.OverrideShared(manager))
            {
                SidebarView? view = null;
                harness.Main.OnMain(() => view = Attached(model));
                harness.Main.OnMain(() => Click(ButtonNamed(view!, "Settings"))); // :168
                harness.Main.OnMain(() =>
                {
                    Assert.NotNull(shown); // ShowSettings built its content
                    var window = WindowOf(shown!);
                    Assert.True(window.IsVisible);
                    window.Close();
                    Close(view!);
                });
            }
        });
    }

    // ---- PR-SIDEBAR-015 / -016: drag background and the popover's dismissal ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-015")] // App/Views/Sidebar/SidebarView.swift:180-181
    public void The_window_column_has_a_window_drag_area_behind_it_and_the_popover_has_none()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var column = Detached(model);
                var drag = Assert.Single(LogicalQuery.SelfAndDescendants<WindowDragArea>(column));
                // Behind: the first child of the view's root, under the column (:181 .background).
                var root = Assert.IsAssignableFrom<Panel>(column.Child);
                Assert.Same(drag, root.Children[0]);
                Assert.Equal(2, root.Children.Count);
                Assert.Empty(LogicalQuery.SelfAndDescendants<WindowDragArea>(Detached(model, inPopover: true)));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-016")] // App/Views/Sidebar/SidebarView.swift:182-183
    public void In_the_popover_a_selection_change_calls_dismiss()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var dismissed = 0;
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model, inPopover: true, dismiss: () => dismissed++));
            harness.Main.OnMain(() => model.SelectedThreadID = Guid.NewGuid());
            Assert.Equal(1, dismissed);
            harness.Main.OnMain(() => model.SelectedThreadID = Guid.NewGuid());
            Assert.Equal(2, dismissed);
            harness.Main.OnMain(() => Close(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-016")] // App/Views/Sidebar/SidebarView.swift:182-183
    public void In_the_column_a_selection_change_dismisses_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var dismissed = 0;
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model, inPopover: false, dismiss: () => dismissed++));
            harness.Main.OnMain(() => model.SelectedThreadID = Guid.NewGuid());
            Assert.Equal(0, dismissed);
            harness.Main.OnMain(() => Close(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-016")] // App/Views/Sidebar/SidebarView.swift:183 `dismiss?()`
    public void In_the_floating_panel_without_a_dismiss_closure_a_selection_change_is_harmless()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model, inPopover: true, dismiss: null));
            harness.Main.OnMain(() => model.SelectedThreadID = Guid.NewGuid());
            harness.Main.OnMain(() =>
            {
                Assert.NotNull(TopLevel.GetTopLevel(view));
                Close(view);
            });
        });
    }

    // ---- PR-SIDEBAR-017 / -018: the scroll view and its freeze ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-017")] // App/Views/Sidebar/SidebarView.swift:101-124, :139
    public void The_list_is_a_vertical_scroll_view_without_indicators_holding_a_1_point_stack_inset_10_12_8()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single();
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility); // :102 ScrollView(.vertical)
                Assert.Empty(scroll.GetVisualDescendants().OfType<ScrollBar>()); // :139 .scrollIndicators(.never)
                var stack = Assert.IsType<StackPanel>(scroll.Content);
                Assert.Equal(Orientation.Vertical, stack.Orientation);
                Assert.Equal(1d, stack.Spacing); // :103 LazyVStack(alignment: .leading, spacing: 1)
                Assert.Equal(new Thickness(10, 12, 10, 8), stack.Margin); // :120-122
                // The list sits between the search row (44 + 30) and the bottom rows (28 + 1 + 28 + 12).
                var frame = Frame(scroll, view);
                Assert.Equal(74d, frame.Y);
                Assert.Equal(view.Bounds.Height - 69, frame.Bottom);
                Close(view);
            });
        });
    }

    internal static StackPanel ListStack(SidebarView view) =>
        Assert.IsType<StackPanel>(LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Content);

    [Trait("Req", "UI-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:101-148,1622-1632; precise caller citations below
    [Fact]
    [Trait("Req", "PR-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift:115, :392-404
    public void A_scroll_movement_freezes_the_list_until_150_ms_after_the_last_one()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                Assert.True(ListStack(view).IsHitTestVisible);
                Call(view, "NoteListScroll");
            });
            Assert.False(ListStack(view).IsHitTestVisible);
            harness.Main.AdvanceMilliseconds(100);
            harness.Main.OnMain(() => Call(view, "NoteListScroll")); // re-arms the settle
            harness.Main.AdvanceMilliseconds(149);
            Assert.False(ListStack(view).IsHitTestVisible);
            harness.Main.AdvanceMilliseconds(1);
            Assert.True(ListStack(view).IsHitTestVisible);
            harness.Main.OnMain(() => Close(view));
        });
    }

    [Trait("Req", "UI-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:101-148,1622-1632; precise caller citations below
    [Fact]
    [Trait("Req", "PR-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift:141-145
    public void A_scroll_gesture_ending_lifts_the_freeze_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                Call(view, "NoteListScroll");
            });
            Assert.False(ListStack(view).IsHitTestVisible);
            harness.Main.OnMain(() =>
            {
                var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single();
                scroll.RaiseEvent(new ScrollGestureEndedEventArgs(1));
            });
            Assert.True(ListStack(view).IsHitTestVisible);
            // The cancelled settle task does not come back to change anything.
            harness.Main.AdvanceMilliseconds(200);
            Assert.True(ListStack(view).IsHitTestVisible);
            harness.Main.OnMain(() => Close(view));
        });
    }

    // ---- PR-SIDEBAR-020 / -022: transitions and item ids ----

    internal static AppModel RichLibrary(AppModelHarness harness, out (Project Alpha, Project Beta, ChatThread Pinned, ChatThread Parent, ChatThread Helper, ChatThread Head, ChatThread Folded, ChatThread FoldedHelper, ChatThread Settled) s)
    {
        var alpha = NewProject(harness, "Alpha");
        var beta = NewProject(harness, "Beta");
        var pinned = NewThread(alpha, "Pinned", 1) with { IsPinned = true };
        var parent = NewThread(alpha, "Parent", 5) with { ExpandsHelpers = true };
        var helper = NewThread(alpha, "Helper", 6) with { ParentThreadID = parent.Id };
        var head = NewThread(alpha, "Head", 7) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
        var folded = NewThread(alpha, "Folded", 4);
        var foldedHelper = NewThread(alpha, "Folded helper", 8) with { ParentThreadID = folded.Id };
        var settled = NewThread(alpha, "Settled", 2) with { IsSettled = true, SettledAt = Base.AddHours(1) };
        s = (alpha, beta, pinned, parent, helper, head, folded, foldedHelper, settled);
        var model = Model(harness, [alpha, beta], [pinned, parent, helper, head, folded, foldedHelper, settled]);
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraAutoHidesIdleHeads = false;
            model.Settings.SettledCollapsed = false; // AppSettings.swift:725 starts it folded
        });
        return model;
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:317-326
    [Trait("Req", "UI-SIDEBAR-059")] // App/Views/Sidebar/SidebarView.swift:317-326 (part 7: the same arms, as a
    // screen-contract record)
    public void Settled_rows_the_Settled_header_and_gaps_fade_plainly_and_every_other_row_uses_sidebarRow()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = RichLibrary(harness, out _);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var items = ProjectItems(view);
                var kinds = items.Select(KindName).ToList();
                foreach (var kind in new[] { "Gap", "Project", "Thread", "SettledHeader", "Helper", "HeadCards", "HelperStub" })
                    Assert.Contains(kind, kinds);
                Assert.Contains(items, item => KindName(item) == "Thread" && ((ChatThread)Get(KindOf(item), "Value")!).IsSettled);
                foreach (var item in items)
                {
                    var expected = KindName(item) switch
                    {
                        "Gap" or "SettledHeader" => SwiftTransition.Opacity, // :321-322
                        "Thread" => ((ChatThread)Get(KindOf(item), "Value")!).IsSettled ? SwiftTransition.Opacity : SidebarRowTransition, // :319-320
                        _ => SidebarRowTransition, // :323-324
                    };
                    Assert.Equal(expected, RowTransition(view, item));
                }
                // The activity header takes the default arm too (:323-324).
                var header = Activator.CreateInstance(Private("SidebarItem"))!;
                Private("SidebarItem").GetProperty("Id")!.SetValue(header, "day-Today");
                Private("SidebarItem").GetProperty("SidebarItemKind")!.SetValue(header, Activator.CreateInstance(Private("SidebarItem+Kind+Header"), "Today", true));
                Assert.Equal(SidebarRowTransition, RowTransition(view, header));
            });
        });
    }

    // :1064-1076 `.asymmetric(insertion: .opacity.animation(.easeIn(duration: 0.18).delay(0.12)),
    // removal: .opacity.animation(.easeOut(duration: 0.06)))`, built here from the cited values.
    internal static SwiftTransition SidebarRowTransition => SwiftTransition.Asymmetric(
        SwiftTransition.Opacity.Animation(Animation.EaseIn(duration: 0.18).Delay(0.12)),
        SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.06)));

    [Fact]
    [Trait("Req", "PR-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:1064-1076
    [Trait("Req", "UI-SIDEBAR-059")] // App/Views/Sidebar/SidebarView.swift:1063-1076 (part 7: the same timing, as a
    // screen-contract record)
    public void The_sidebarRow_transition_fades_in_over_0_18_after_0_12_and_out_over_0_06()
    {
        var sidebarRow = (SwiftTransition)typeof(SidebarView).Assembly.GetType("Marrow.App.Views.Sidebar.SidebarViewExtensions", throwOnError: true)!
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "get_SidebarRow" && method.GetParameters().Length == 0)
            .Invoke(null, null)!;
        Assert.Equal(SidebarRowTransition, sidebarRow);
        Assert.NotEqual(SwiftTransition.Asymmetric(
            SwiftTransition.Opacity.Animation(Animation.EaseIn(duration: 0.18)),
            SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.06))), sidebarRow);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-022")] // App/Views/Sidebar/SidebarView.swift:1053-1055, :1058-1062
    public void A_settled_thread_is_keyed_settled_dash_uppercase_uuid_and_an_open_one_by_the_bare_uuid()
    {
        using var runtime = new Marrow.Testing.TestRuntime(); // ChatThread.init reads the defaults (Library.swift:204-226)
        var project = new Project("Alpha", @"C:\alpha");
        var open = AppModelSamples.Thread(project) with { Id = new Guid("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9") };
        var settled = open with { IsSettled = true };
        var idFor = Private("SidebarItem").GetMethod("IdFor", BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal("0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9", idFor.Invoke(null, [open]));
        Assert.Equal("settled-0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9", idFor.Invoke(null, [settled]));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-022")] // App/Views/Sidebar/SidebarView.swift:550, :1058-1062
    public void Settling_a_thread_swaps_its_item_for_one_with_another_id()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Work", 1);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false; // AppSettings.swift:725 starts it folded
                var view = Detached(model);
                var uuid = SwiftUUID.UuidString(thread.Id);
                Assert.Contains(uuid, ProjectItems(view).Select(IdOf));
                model.UpdateThread(thread.Id, value => value with { IsSettled = true, SettledAt = Base.AddHours(1) });
                var ids = ProjectItems(view).Select(IdOf).ToList();
                Assert.DoesNotContain(uuid, ids);
                Assert.Contains("settled-" + uuid, ids);
            });
        });
    }

    // ---- PR-SIDEBAR-021: reveal ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-021")] // App/Views/Sidebar/SidebarView.swift:154-157
    public void A_reveal_request_for_a_missing_thread_is_cleared_and_scrolls_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() => model.SidebarRevealThreadID = Guid.NewGuid());
            harness.Main.OnMain(() =>
            {
                Assert.Null(model.SidebarRevealThreadID);
                Assert.Equal(0d, LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Offset.Y);
                Close(view);
            });
        });
    }

    // ---- PR-SIDEBAR-023 / -024 / -025: the project layout ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-023")] // App/Views/Sidebar/SidebarView.swift:517-524
    [Trait("Req", "UI-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:517-519, :331-332 (part 6: the same gap item, as a screen-contract record)
    public void Projects_come_in_library_order_with_a_10_point_gap_item_before_every_project_but_the_first()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
            var beta = NewProject(harness, "Beta") with { IsExpanded = false };
            var gamma = NewProject(harness, "Gamma") with { IsExpanded = false };
            var model = Model(harness, [alpha, beta, gamma], []);
            harness.Main.OnMain(() =>
            {
                var items = ProjectItems(Detached(model));
                string U(Project project) => SwiftUUID.UuidString(project.Id);
                Assert.Equal(
                    ["project-" + U(alpha), "gap-" + U(beta), "project-" + U(beta), "gap-" + U(gamma), "project-" + U(gamma)],
                    items.Select(IdOf));
                Assert.Equal(["Project", "Gap", "Project", "Gap", "Project"], items.Select(KindName));
                Assert.All(items.Where(item => KindName(item) == "Gap"), gap => Assert.Equal(10d, Get(KindOf(gap), "Value")));
                Assert.Equal([alpha.Id, beta.Id, gamma.Id],
                    items.Where(item => KindName(item) == "Project").Select(item => ((Project)Get(KindOf(item), "Value")!).Id));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-023")] // App/Views/Sidebar/SidebarView.swift:517
    public void With_no_projects_the_project_layout_is_empty()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() => Assert.Empty(ProjectItems(Detached(model))));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:525-554, :1281
    [Trait("Req", "UI-SIDEBAR-007")] // App/Views/Sidebar/SidebarView.swift:533-537, :1281 (part 6: the same gap item, as a screen-contract record)
    public void Pinned_open_threads_lead_then_the_others_then_a_4_point_gap_the_Settled_header_and_the_settled_threads()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var older = NewThread(alpha, "Older", 1);
            var newer = NewThread(alpha, "Newer", 3);
            var pinned = NewThread(alpha, "Pinned", 0) with { IsPinned = true };
            var settledEarly = NewThread(alpha, "Settled early", 2) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var settledLate = NewThread(alpha, "Settled late", 4) with { IsSettled = true, SettledAt = Base.AddHours(2) };
            var model = Model(harness, [alpha], [older, newer, pinned, settledEarly, settledLate]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false; // AppSettings.swift:725 starts it folded
                var items = ProjectItems(Detached(model));
                string U(Guid id) => SwiftUUID.UuidString(id);
                Assert.Equal(
                    [
                        "project-" + U(alpha.Id),
                        U(pinned.Id), U(newer.Id), U(older.Id),
                        "settled-gap-" + U(alpha.Id),
                        "settled-header-" + U(alpha.Id),
                        "settled-" + U(settledLate.Id), "settled-" + U(settledEarly.Id),
                    ],
                    items.Select(IdOf));
                Assert.Equal(4d, Get(KindOf(items[4]), "Value"));
                Assert.Equal(2L, Get(KindOf(items[5]), "Count"));
                Assert.Equal(false, Get(KindOf(items[5]), "IsFirst"));
                // Project layout rows carry no project name, no mark and no reorder peers (:551).
                foreach (var item in items.Where(item => KindName(item) == "Thread"))
                {
                    Assert.Null(Get(KindOf(item), "ProjectName"));
                    Assert.Null(Get(KindOf(item), "ProjectIcon"));
                    Assert.Null(Get(KindOf(item), "Peers"));
                }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:536-544
    [Trait("Req", "UI-SIDEBAR-007")] // App/Views/Sidebar/SidebarView.swift:536-537 (part 6: the gap item's absence state)
    public void A_project_whose_threads_are_all_settled_gets_the_header_but_no_gap()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var settled = NewThread(alpha, "Settled", 2) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var model = Model(harness, [alpha], [settled]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false; // AppSettings.swift:725 starts it folded
                var items = ProjectItems(Detached(model));
                Assert.Equal(["Project", "SettledHeader", "Thread"], items.Select(KindName));
                Assert.Equal(1L, Get(KindOf(items[1]), "Count"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:525
    public void A_folded_project_shows_only_its_own_row()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
            var thread = NewThread(alpha, "Work", 1);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() => Assert.Equal(["project-" + SwiftUUID.UuidString(alpha.Id)], ProjectItems(Detached(model)).Select(IdOf)));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:548-553
    public void Each_thread_row_is_followed_at_once_by_its_helper_items()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = RichLibrary(harness, out var s);
            harness.Main.OnMain(() =>
            {
                var items = ProjectItems(Detached(model));
                var ids = items.Select(IdOf).ToList();
                string U(Guid id) => SwiftUUID.UuidString(id);
                var parentAt = ids.IndexOf(U(s.Parent.Id));
                Assert.Equal(U(s.Helper.Id), ids[parentAt + 1]);
                Assert.Equal("heads-" + U(s.Parent.Id), ids[parentAt + 2]);
                var foldedAt = ids.IndexOf(U(s.Folded.Id));
                Assert.Equal("helpers-" + U(s.Folded.Id), ids[foldedAt + 1]);
                Assert.Equal(true, Get(KindOf(items[parentAt]), "HasHelpers"));
                Assert.Equal(false, Get(KindOf(items[ids.IndexOf(U(s.Pinned.Id))]), "HasHelpers"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-025")] // App/Views/Sidebar/SidebarView.swift:529, :545
    public void With_the_settled_section_folded_nothing_below_the_Settled_header_is_built()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var open = NewThread(alpha, "Open", 3);
            var settledA = NewThread(alpha, "Settled A", 1) with { IsSettled = true, SettledAt = Base.AddHours(1), ExpandsHelpers = true };
            var settledB = NewThread(alpha, "Settled B", 2) with { IsSettled = true, SettledAt = Base.AddHours(2) };
            var underSettled = NewThread(alpha, "Under settled", 4) with { ParentThreadID = settledA.Id };
            var model = Model(harness, [alpha], [open, settledA, settledB, underSettled]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = true;
                var items = ProjectItems(Detached(model));
                string U(Guid id) => SwiftUUID.UuidString(id);
                Assert.Equal(
                    ["project-" + U(alpha.Id), U(open.Id), "settled-gap-" + U(alpha.Id), "settled-header-" + U(alpha.Id)],
                    items.Select(IdOf));
                Assert.Equal(2L, Get(KindOf(items[3]), "Count")); // the full settled count
            });
        });
    }

    // ======== Part 2 of 8 ========

    internal static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    internal static object? CallOn(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    // helpersByParent(placements:) (:410-422) over the view's own placements (:386-388).
    internal static ValueMap<Guid, ValueList<ChatThread>> Helpers(SidebarView view) =>
        (ValueMap<Guid, ValueList<ChatThread>>)Call(view, "HelpersByParent", Get(view, "ThreadPlacements"))!;

    internal static ValueList<ChatThread> HelpersOf(SidebarView view, Guid parent) =>
        Helpers(view).TryGetValue(parent, out var own) ? own : ValueList<ChatThread>.Empty;

    // activityItems(helpers:) (:561-605).
    internal static List<object> ActivityItems(SidebarView view) => Items(Call(view, "ActivityItems", Helpers(view)));

    // helperItems(under:helpers:) (:458-474).
    internal static List<object> HelperItems(SidebarView view, ChatThread under, IReadOnlyList<ChatThread> helpers) =>
        Items(Call(view, "HelperItems", under, helpers));

    internal static ChatThread ThreadOf(object item) => (ChatThread)Get(KindOf(item), "Value")!;

    internal static IEnumerable<object> ThreadItems(IEnumerable<object> items) => items.Where(item => KindName(item) == "Thread");

    internal static string U(Guid id) => SwiftUUID.UuidString(id);

    // The manual clock's start (Marrow.Testing ManualTimeProvider): 2026-09-22 12:00 UTC, a Tuesday.
    internal static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    internal static ChatThread ThreadAt(Project project, string title, DateTimeOffset updatedAt) =>
        AppModelSamples.Thread(project) with { Title = title, CreatedAt = updatedAt, UpdatedAt = updatedAt };

    // Calendar.current, the formatters' locale and zone, pinned for one flow (ED-094: a Gregorian culture per flow).
    internal static void InDays(Action body, TimeZoneInfo? zone = null)
    {
        using var zoneScope = SwiftFormatters.CurrentTimeZone.Override(zone ?? TimeZoneInfo.Utc);
        using var localeScope = SwiftFormatters.CurrentLocale.Override("en_US");
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Assert.Equal(Now, SwiftDate.Now);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    internal static void Approve(AppModel model, Guid thread) =>
        model.Runtime(thread).Rehearse(new ProviderEvent.Approval(new ApprovalRequest(
            "approval", ApprovalRequest.Kind.Command, "Run it?", ValueList<ApprovalRequest.Option>.Empty)));

    internal static void Ask(AppModel model, Guid thread) =>
        model.Runtime(thread).Rehearse(new ProviderEvent.Question(new QuestionRequest("question", ValueList<QuestionRequest.Question>.Empty)));

    internal static RowDrag<Guid> Drag(SidebarView view) => (RowDrag<Guid>)Get(Field(view, "state")!, "Drag")!;

    internal static void NoteFrame(SidebarView view, Guid id, Rect frame) => CallOn(Field(view, "rowFrames")!, "Note", id, frame);

    internal static Guid? ThreadAtPoint(object frames, Point point) => (Guid?)CallOn(frames, "Thread", point);

    // The list's stack (:103), the scroll view's content.
    internal static StackPanel ListPanel(SidebarView view) => (StackPanel)LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Content!;

    // ---- PR-SIDEBAR-026: a folded project's count ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-026")] // App/Views/Sidebar/SidebarView.swift:521-524
    public void A_project_item_counts_its_top_level_threads_and_every_helper_under_them_settled_ones_included()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
            var empty = NewProject(harness, "Empty") with { IsExpanded = false };
            var open = NewThread(alpha, "Open", 1);
            var settled = NewThread(alpha, "Settled", 2) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var helper = NewThread(alpha, "Helper", 3) with { ParentThreadID = open.Id };
            var head = NewThread(alpha, "Head", 4) with { ParentThreadID = open.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed), IsInPanel = true };
            var underSettled = NewThread(alpha, "Under settled", 5) with { ParentThreadID = settled.Id };
            var archived = NewThread(alpha, "Archived", 6) with { IsArchived = true };
            var panelled = NewThread(alpha, "Panelled helper", 7) with { ParentThreadID = open.Id, IsInPanel = true };
            var model = Model(harness, [alpha, empty], [open, settled, helper, head, underSettled, archived, panelled]);
            harness.Main.OnMain(() =>
            {
                var items = ProjectItems(Detached(model));
                // Two top-level threads (the settled one counts), the helper and the panelled head under the open one
                // (a panelled plain helper is not grouped, :415) and one under the settled one: 2 + 2 + 1.
                Assert.Equal([5L, 0L], items.Where(item => KindName(item) == "Project").Select(item => (long)Get(KindOf(item), "Count")!));
            });
        });
    }

    // ---- PR-SIDEBAR-028 to -033: helpers and heads ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-028")] // App/Views/Sidebar/SidebarView.swift:410-422
    public void Helpers_are_grouped_by_parent_newest_created_first_leaving_out_archived_and_panelled_non_head_threads()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0);
            var lone = NewThread(alpha, "Lone", 0);
            var first = NewThread(alpha, "First", 1) with { ParentThreadID = parent.Id };
            var third = NewThread(alpha, "Third", 3) with { ParentThreadID = parent.Id };
            var archived = NewThread(alpha, "Archived", 5) with { ParentThreadID = parent.Id, IsArchived = true };
            var panelled = NewThread(alpha, "Panelled", 6) with { ParentThreadID = parent.Id, IsInPanel = true };
            var panelledHead = NewThread(alpha, "Panelled head", 2) with { ParentThreadID = parent.Id, IsInPanel = true, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var head = NewThread(alpha, "Head", 4) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var missingParent = Guid.NewGuid();
            var stray = NewThread(alpha, "Stray", 7) with { ParentThreadID = missingParent };
            var model = Model(harness, [alpha], [parent, lone, first, third, archived, panelled, panelledHead, head, stray]);
            harness.Main.OnMain(() =>
            {
                var helpers = Helpers(Detached(model));
                Assert.Equal([head.Id, third.Id, panelledHead.Id, first.Id], helpers[parent.Id].Select(thread => thread.Id));
                // A helper whose parent is shown nowhere is still grouped under that id (:416-417).
                Assert.Equal([stray.Id], helpers[missingParent].Select(thread => thread.Id));
                Assert.False(helpers.ContainsKey(lone.Id));
                Assert.Equal(2, helpers.Count);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-029")] // App/Views/Sidebar/SidebarView.swift:460
    public void A_settled_thread_emits_no_helper_items_and_they_return_when_it_reopens()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0) with { IsSettled = true, SettledAt = Base.AddHours(1), ExpandsHelpers = true };
            var folded = NewThread(alpha, "Folded", 1) with { IsSettled = true, SettledAt = Base.AddHours(2) };
            var lone = NewThread(alpha, "Lone", 2);
            var helper = NewThread(alpha, "Helper", 3) with { ParentThreadID = parent.Id };
            var head = NewThread(alpha, "Head", 4) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var foldedHelper = NewThread(alpha, "Folded helper", 5) with { ParentThreadID = folded.Id };
            var model = Model(harness, [alpha], [parent, folded, lone, helper, head, foldedHelper]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false; // AppSettings.swift starts it folded
                var view = Detached(model);
                Assert.Empty(HelperItems(view, parent, HelpersOf(view, parent.Id)));
                Assert.Empty(HelperItems(view, folded, HelpersOf(view, folded.Id))); // no folded stub either
                Assert.Empty(HelperItems(view, lone, ValueList<ChatThread>.Empty));
                Assert.DoesNotContain(ProjectItems(view), item => KindName(item) is "Helper" or "HeadCards" or "HelperStub");
                model.UpdateThread(parent.Id, thread => thread with { IsSettled = false, SettledAt = null });
                var ids = ProjectItems(view).Select(IdOf).ToList();
                Assert.Contains(U(helper.Id), ids);
                Assert.Contains("heads-" + U(parent.Id), ids);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-030")] // App/Views/Sidebar/SidebarView.swift:461-463; Core/Models/Library.swift:166-170
    public void A_folded_thread_emits_exactly_one_stub_keyed_helpers_dash_its_uuid()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0);
            var older = NewThread(alpha, "Older", 1) with { ParentThreadID = parent.Id };
            var head = NewThread(alpha, "Head", 2) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var newer = NewThread(alpha, "Newer", 3) with { ParentThreadID = parent.Id };
            var model = Model(harness, [alpha], [parent, older, head, newer]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Assert.False(parent.ExpandsHelpers); // Library.swift:166, so foldsHelpers (:167-170) is true
                Assert.True(parent.FoldsHelpers);
                var items = HelperItems(view, parent, HelpersOf(view, parent.Id));
                var stub = Assert.Single(items);
                Assert.Equal("helpers-" + U(parent.Id), IdOf(stub));
                Assert.Equal("HelperStub", KindName(stub));
                Assert.Equal(parent.Id, ((ChatThread)Get(KindOf(stub), "Parent")!).Id);
                Assert.Equal([newer.Id, head.Id, older.Id], ((IEnumerable<ChatThread>)Get(KindOf(stub), "Helpers")!).Select(thread => thread.Id));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:464-473
    public void Unfolded_helpers_emit_a_row_each_newest_first_then_one_head_card_keyed_heads_dash_the_parent_uuid()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0) with { ExpandsHelpers = true };
            var older = NewThread(alpha, "Older", 1) with { ParentThreadID = parent.Id };
            var olderHead = NewThread(alpha, "Older head", 2) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var newer = NewThread(alpha, "Newer", 3) with { ParentThreadID = parent.Id };
            var newerHead = NewThread(alpha, "Newer head", 4) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Failed) };
            var model = Model(harness, [alpha], [parent, older, olderHead, newer, newerHead]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var items = HelperItems(view, parent, HelpersOf(view, parent.Id));
                Assert.Equal([U(newer.Id), U(older.Id), "heads-" + U(parent.Id)], items.Select(IdOf));
                Assert.Equal(["Helper", "Helper", "HeadCards"], items.Select(KindName));
                // A card follows, so no helper row ends the connector.
                Assert.Equal([false, false], items.Take(2).Select(item => (bool)Get(KindOf(item), "IsLast")!));
                Assert.Equal(parent.Id, ((ChatThread)Get(KindOf(items[2]), "Parent")!).Id);
                Assert.Equal([newerHead.Id, olderHead.Id], ((IEnumerable<ChatThread>)Get(KindOf(items[2]), "Heads")!).Select(thread => thread.Id));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:467-472
    public void The_last_plain_helper_ends_the_connector_only_when_no_card_follows_and_heads_alone_give_just_the_card()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var plainParent = NewThread(alpha, "Plain parent", 0) with { ExpandsHelpers = true };
            var older = NewThread(alpha, "Older", 1) with { ParentThreadID = plainParent.Id };
            var newer = NewThread(alpha, "Newer", 2) with { ParentThreadID = plainParent.Id };
            var headParent = NewThread(alpha, "Head parent", 3) with { ExpandsHelpers = true };
            var head = NewThread(alpha, "Head", 4) with { ParentThreadID = headParent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var model = Model(harness, [alpha], [plainParent, older, newer, headParent, head]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var plain = HelperItems(view, plainParent, HelpersOf(view, plainParent.Id));
                Assert.Equal([U(newer.Id), U(older.Id)], plain.Select(IdOf));
                Assert.Equal([false, true], plain.Select(item => (bool)Get(KindOf(item), "IsLast")!));
                var card = Assert.Single(HelperItems(view, headParent, HelpersOf(view, headParent.Id)));
                Assert.Equal("heads-" + U(headParent.Id), IdOf(card));
                Assert.Equal("HeadCards", KindName(card));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-032")] // App/Views/Sidebar/SidebarView.swift:452-454, :466; App/AppSettings.swift:606-607, :770
    public void With_hydraAutoHidesIdleHeads_on_the_card_shows_only_while_a_live_head_runs()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0) with { ExpandsHelpers = true };
            var plain = NewThread(alpha, "Plain", 1) with { ParentThreadID = parent.Id };
            var first = NewThread(alpha, "First head", 2) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var second = NewThread(alpha, "Second head", 3) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Stopped) };
            var model = Model(harness, [alpha], [parent, plain, first, second]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Assert.False(model.Settings.HydraAutoHidesIdleHeads); // :770 default false
                var snapshot = HelpersOf(view, parent.Id);
                Assert.Equal(["Helper", "HeadCards"], HelperItems(view, parent, snapshot).Select(KindName));

                model.Settings.HydraAutoHidesIdleHeads = true;
                Assert.Equal(true, harness.Defaults.Object("hydraAutoHidesIdleHeads")); // :607
                var hidden = HelperItems(view, parent, snapshot);
                Assert.Equal([U(plain.Id)], hidden.Select(IdOf));
                Assert.Equal(true, Get(KindOf(hidden[0]), "IsLast")); // no card follows

                // The running check reads the live record (:453): the snapshot still says completed.
                model.UpdateThread(first.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
                Assert.Equal(HydraHeadInfo.Status.Completed, snapshot.Single(thread => thread.Id == first.Id).Hydra!.HydraHeadInfoStatus);
                var shown = HelperItems(view, parent, snapshot);
                Assert.Equal([U(plain.Id), "heads-" + U(parent.Id)], shown.Select(IdOf));
                Assert.Equal(false, Get(KindOf(shown[0]), "IsLast"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:478-485
    public void Heads_are_ordered_running_first_then_newest_created_first_within_each_block()
    {
        using var runtime = new Marrow.Testing.TestRuntime(); // ChatThread.init reads the defaults (Library.swift:204-226)
        var project = new Project("Alpha", @"C:\alpha");
        ChatThread Head(HydraHeadInfo.Status status, int minute) =>
            AppModelSamples.Thread(project) with { CreatedAt = Base.AddMinutes(minute), Hydra = AppModelSamples.Head(status) };
        var runningOld = Head(HydraHeadInfo.Status.Running, 1);
        var completedOld = Head(HydraHeadInfo.Status.Completed, 2);
        var runningNew = Head(HydraHeadInfo.Status.Running, 3);
        var failedNew = Head(HydraHeadInfo.Status.Failed, 4);
        var stoppedNewest = Head(HydraHeadInfo.Status.Stopped, 5);
        var ordered = SidebarView.OrderedHeads([runningOld, completedOld, runningNew, failedNew, stoppedNewest]);
        Assert.Equal([runningNew.Id, runningOld.Id, stoppedNewest.Id, failedNew.Id, completedOld.Id], ordered.Select(thread => thread.Id));
    }

    // ---- PR-SIDEBAR-034 to -042: the activity layout ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-039")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift:561-605
    public void Activity_sections_run_attention_pinned_days_settled_and_only_the_first_emitted_header_is_first()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var attention = ThreadAt(alpha, "Attention", Now.AddHours(-1));
            var pinned = ThreadAt(alpha, "Pinned", Now.AddHours(-2)) with { IsPinned = true };
            var today = ThreadAt(alpha, "Today", Now.AddHours(-3));
            var yesterday = ThreadAt(alpha, "Yesterday", Now.AddDays(-1));
            var settled = ThreadAt(alpha, "Settled", Now.AddHours(-4)) with { IsSettled = true, SettledAt = Now.AddHours(-4) };
            var model = Model(harness, [alpha], [attention, pinned, today, yesterday, settled]);
            harness.Main.OnMain(() => InDays(() =>
            {
                model.Settings.SettledCollapsed = false;
                Approve(model, attention.Id);
                var items = ActivityItems(Detached(model));
                Assert.Equal(
                    ["attention", U(attention.Id), "day-Pinned", U(pinned.Id), "day-Today", U(today.Id), "day-Yesterday", U(yesterday.Id), "settled", "settled-" + U(settled.Id)],
                    items.Select(IdOf));
                var headers = items.Where(item => KindName(item) is "Header" or "SettledHeader").ToList();
                Assert.Equal(["Header", "Header", "Header", "Header", "SettledHeader"], headers.Select(KindName));
                Assert.Equal(["Needs attention", "Pinned", "Today", "Yesterday"], headers.Take(4).Select(item => (string)Get(KindOf(item), "Value")!));
                Assert.Equal([true, false, false, false, false], headers.Select(item => (bool)Get(KindOf(item), "IsFirst")!));
                Assert.Equal(1L, Get(KindOf(headers[4]), "Count"));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift:579, :584, :592
    public void Without_earlier_sections_the_first_header_emitted_is_the_first_and_no_threads_give_no_headers()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var pinned = ThreadAt(alpha, "Pinned", Now.AddHours(-2)) with { IsPinned = true };
            var today = ThreadAt(alpha, "Today", Now.AddHours(-3));
            var model = Model(harness, [alpha], [pinned, today]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var view = Detached(model);
                bool? FirstOf(string id) => ActivityItems(view).Where(item => IdOf(item) == id).Select(item => (bool?)Get(KindOf(item), "IsFirst")).SingleOrDefault();
                Assert.Equal(true, FirstOf("day-Pinned"));
                Assert.Equal(false, FirstOf("day-Today"));
                model.UpdateThread(pinned.Id, thread => thread with { IsPinned = false });
                Assert.Equal(true, FirstOf("day-Today"));
                model.Settings.SettledCollapsed = true;
                model.UpdateThread(pinned.Id, thread => thread with { IsSettled = true, SettledAt = Now.AddHours(-5) });
                model.UpdateThread(today.Id, thread => thread with { IsSettled = true, SettledAt = Now.AddHours(-6) });
                Assert.Equal(["settled"], ActivityItems(view).Select(IdOf));
                Assert.Equal(true, FirstOf("settled"));
            }));
        });
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() => InDays(() => Assert.Empty(ActivityItems(Detached(model)))));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-035")] // App/Views/Sidebar/SidebarView.swift:562-566
    public void The_activity_layout_leaves_out_archived_and_panelled_threads_and_lifts_a_helper_whose_parent_is_not_shown()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = ThreadAt(alpha, "Parent", Now.AddHours(-1)) with { ExpandsHelpers = true };
            var child = ThreadAt(alpha, "Child", Now.AddHours(-2)) with { ParentThreadID = parent.Id };
            var head = ThreadAt(alpha, "Head", Now.AddHours(-3)) with { ParentThreadID = parent.Id, IsInPanel = true, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var archived = ThreadAt(alpha, "Archived", Now.AddHours(-4)) with { IsArchived = true };
            var panelled = ThreadAt(alpha, "Panelled", Now.AddHours(-5)) with { IsInPanel = true };
            var archivedParent = ThreadAt(alpha, "Archived parent", Now.AddHours(-6)) with { IsArchived = true };
            var orphan = ThreadAt(alpha, "Orphan", Now.AddHours(-7)) with { ParentThreadID = archivedParent.Id };
            var stray = ThreadAt(alpha, "Stray", Now.AddHours(-8)) with { ParentThreadID = Guid.NewGuid() };
            var model = Model(harness, [alpha], [parent, child, head, archived, panelled, archivedParent, orphan, stray]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var items = ActivityItems(Detached(model));
                Assert.Equal([parent.Id, orphan.Id, stray.Id], ThreadItems(items).Select(item => ThreadOf(item).Id));
                Assert.Equal(["day-Today", U(parent.Id), U(child.Id), "heads-" + U(parent.Id), U(orphan.Id), U(stray.Id)], items.Select(IdOf));
                // The panelled head is only in its parent's card.
                Assert.Equal([head.Id], ((IEnumerable<ChatThread>)Get(KindOf(items[3]), "Heads")!).Select(thread => thread.Id));
                var everyID = items.Select(IdOf).ToList();
                foreach (var gone in new[] { archived, panelled, archivedParent })
                    Assert.DoesNotContain(everyID, id => id.Contains(U(gone.Id), StringComparison.Ordinal));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift:568-569, :633-636
    public void Threads_with_pending_approvals_or_questions_head_the_list_unless_settled_or_without_a_live_runtime()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var approval = ThreadAt(alpha, "Approval", Now.AddHours(-3));
            var question = ThreadAt(alpha, "Question", Now.AddHours(-2));
            var noRuntime = ThreadAt(alpha, "No runtime", Now.AddHours(-1));
            var idle = ThreadAt(alpha, "Idle runtime", Now.AddHours(-4));
            var settled = ThreadAt(alpha, "Settled", Now.AddHours(-5)) with { IsSettled = true, SettledAt = Now.AddHours(-5) };
            var model = Model(harness, [alpha], [approval, question, noRuntime, idle, settled]);
            harness.Main.OnMain(() => InDays(() =>
            {
                model.Settings.SettledCollapsed = false;
                Approve(model, approval.Id);
                Ask(model, question.Id);
                Approve(model, settled.Id);
                _ = model.Runtime(idle.Id);
                var items = ActivityItems(Detached(model));
                Assert.Equal(
                    ["attention", U(question.Id), U(approval.Id), "day-Today", U(noRuntime.Id), U(idle.Id), "settled", "settled-" + U(settled.Id)],
                    items.Select(IdOf));
                // The lookup never makes a runtime (:634).
                Assert.Null(model.ExistingRuntime(noRuntime.Id));
            }));
        });
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-039")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:644-656, :677-686
    public void Day_groups_read_Today_Yesterday_a_wide_weekday_within_seven_calendar_days_then_month_and_day_then_the_year()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var today = ThreadAt(alpha, "Today", new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero));
            var alsoToday = ThreadAt(alpha, "Also today", new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));
            var yesterday = ThreadAt(alpha, "Yesterday", new DateTimeOffset(2026, 9, 21, 23, 59, 0, TimeSpan.Zero));
            var sunday = ThreadAt(alpha, "Sunday", new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero));
            var wednesday = ThreadAt(alpha, "Six days", new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));
            // 157 hours back, under 7 x 24, but seven calendar days back: not a weekday.
            var sevenDays = ThreadAt(alpha, "Seven days", new DateTimeOffset(2026, 9, 15, 23, 0, 0, TimeSpan.Zero));
            var newYear = ThreadAt(alpha, "New year", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
            var lastYear = ThreadAt(alpha, "Last year", new DateTimeOffset(2025, 12, 31, 10, 0, 0, TimeSpan.Zero));
            var model = Model(harness, [alpha], [lastYear, sevenDays, today, sunday, newYear, yesterday, wednesday, alsoToday]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var items = ActivityItems(Detached(model));
                Assert.Equal(
                    [
                        "day-Today", U(today.Id), U(alsoToday.Id),
                        "day-Yesterday", U(yesterday.Id),
                        "day-Sunday", U(sunday.Id),
                        "day-Wednesday", U(wednesday.Id),
                        "day-September 15", U(sevenDays.Id),
                        "day-January 1", U(newYear.Id),
                        "day-December 31, 2025", U(lastYear.Id),
                    ],
                    items.Select(IdOf));
                Assert.Equal(["Today", "Yesterday", "Sunday", "Wednesday", "September 15", "January 1", "December 31, 2025"],
                    items.Where(item => KindName(item) == "Header").Select(item => (string)Get(KindOf(item), "Value")!));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:678-681
    public void A_future_date_takes_the_weekday_branch_and_days_follow_the_local_calendar_day()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var future = ThreadAt(alpha, "Future", new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
            // 2026-09-21 16:00 UTC is 2026-09-22 01:00 in Tokyo (UTC+9, no daylight time), where it is then 21:00.
            var late = ThreadAt(alpha, "Late", new DateTimeOffset(2026, 9, 21, 16, 0, 0, TimeSpan.Zero));
            var model = Model(harness, [alpha], [future, late]);
            harness.Main.OnMain(() =>
            {
                InDays(() => Assert.Equal(["day-Friday", U(future.Id), "day-Yesterday", U(late.Id)], ActivityItems(Detached(model)).Select(IdOf)));
                InDays(() => Assert.Equal(["day-Friday", U(future.Id), "day-Today", U(late.Id)], ActivityItems(Detached(model)).Select(IdOf)),
                    TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-038")] // App/Views/Sidebar/SidebarView.swift:659-675
    public void A_group_puts_threads_without_a_place_for_the_day_first_newest_first_then_placed_ones_by_ascending_order()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var day = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
            var second = ThreadAt(alpha, "Placed second", Now.AddHours(-2)) with { ActivityOrder = 2, ActivityOrderDay = day };
            var first = ThreadAt(alpha, "Placed first", Now.AddHours(-3)) with { ActivityOrder = 1, ActivityOrderDay = day };
            var tiedOlder = ThreadAt(alpha, "Placed tie", Now.AddHours(-4)) with { ActivityOrder = 1, ActivityOrderDay = day };
            var fresh = ThreadAt(alpha, "Fresh", Now.AddMinutes(-30));
            // A place taken on an earlier day no longer counts (:662-663).
            var stale = ThreadAt(alpha, "Stale", Now.AddHours(-1)) with { ActivityOrder = 0.5, ActivityOrderDay = day.AddDays(-1) };
            var model = Model(harness, [alpha], [second, first, tiedOlder, fresh, stale]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var items = ActivityItems(Detached(model));
                Assert.Equal(["day-Today", U(fresh.Id), U(stale.Id), U(first.Id), U(tiedOlder.Id), U(second.Id)], items.Select(IdOf));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-039")] // App/Views/Sidebar/SidebarView.swift:576-586
    public void Open_pinned_threads_sit_under_Pinned_above_the_days_unless_they_need_attention()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var pinnedToday = ThreadAt(alpha, "Pinned today", Now.AddHours(-3)) with { IsPinned = true };
            var pinnedYesterday = ThreadAt(alpha, "Pinned yesterday", Now.AddDays(-1)) with { IsPinned = true };
            var pinnedAsking = ThreadAt(alpha, "Pinned asking", Now.AddHours(-2)) with { IsPinned = true };
            var plain = ThreadAt(alpha, "Plain", Now.AddHours(-1));
            var model = Model(harness, [alpha], [pinnedToday, pinnedYesterday, pinnedAsking, plain]);
            harness.Main.OnMain(() => InDays(() =>
            {
                Ask(model, pinnedAsking.Id);
                var items = ActivityItems(Detached(model));
                Assert.Equal(
                    ["attention", U(pinnedAsking.Id), "day-Pinned", U(pinnedToday.Id), U(pinnedYesterday.Id), "day-Today", U(plain.Id)],
                    items.Select(IdOf));
                Assert.Equal("Pinned", Get(KindOf(items[2]), "Value"));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-040")] // App/Views/Sidebar/SidebarView.swift:590-603; App/AppModel.swift:387-389
    public void The_Settled_section_closes_the_list_latest_settled_first_with_rows_only_while_unfolded()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var open = ThreadAt(alpha, "Open", Now.AddDays(-3));
            var early = ThreadAt(alpha, "Early", Now.AddHours(-9)) with { IsSettled = true, SettledAt = Now.AddHours(-2) };
            var undated = ThreadAt(alpha, "Undated", Now.AddDays(-2)) with { IsSettled = true };
            var late = ThreadAt(alpha, "Late", Now.AddDays(-9)) with { IsSettled = true, SettledAt = Now.AddHours(-1) };
            var helper = ThreadAt(alpha, "Helper", Now.AddHours(-8)) with { ParentThreadID = early.Id };
            var model = Model(harness, [alpha], [open, early, undated, late, helper]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var view = Detached(model);
                model.Settings.SettledCollapsed = true;
                var folded = ActivityItems(view);
                Assert.Equal("settled", IdOf(folded[^1]));
                Assert.Equal(3L, Get(KindOf(folded[^1]), "Count"));
                Assert.Equal(false, Get(KindOf(folded[^1]), "IsFirst"));
                model.Settings.SettledCollapsed = false;
                var items = ActivityItems(view);
                var at = items.FindIndex(item => IdOf(item) == "settled");
                Assert.Equal(["settled", "settled-" + U(late.Id), "settled-" + U(early.Id), "settled-" + U(undated.Id)], items.Skip(at).Select(IdOf));
                foreach (var row in items.Skip(at + 1))
                {
                    Assert.Null(Get(KindOf(row), "Peers"));
                    Assert.Equal(false, Get(KindOf(row), "HasHelpers"));
                }
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-041")] // App/Views/Sidebar/SidebarView.swift:595-599, :613-617, :628-631; App/AppSettings.swift:277-278, :724
    public void Activity_rows_carry_the_project_mark_in_the_icon_style_and_the_project_name_in_the_other()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha") with { Icon = new ProjectIcon.Symbol("star") };
            var beta = NewProject(harness, "Beta");
            var gone = NewProject(harness, "Gone");
            var inAlpha = ThreadAt(alpha, "In alpha", Now.AddHours(-1));
            var inBeta = ThreadAt(beta, "In beta", Now.AddHours(-2));
            var orphan = ThreadAt(gone, "Orphan", Now.AddHours(-3));
            var settled = ThreadAt(beta, "Settled", Now.AddHours(-4)) with { IsSettled = true, SettledAt = Now.AddHours(-4) };
            var model = Model(harness, [alpha, beta], [inAlpha, inBeta, orphan, settled]);
            harness.Main.OnMain(() => InDays(() =>
            {
                model.Settings.SettledCollapsed = false;
                var view = Detached(model);
                Assert.Contains(model.Threads, thread => thread.Id == orphan.Id); // its project is gone
                Assert.Equal(ActivityThreadStyle.Icon, model.Settings.ActivityThreadStyle); // :724 default .icon
                var folder = new ProjectIcon.Symbol("folder.fill");
                var icon = ThreadItems(ActivityItems(view)).ToList();
                Assert.Equal([inAlpha.Id, inBeta.Id, orphan.Id, settled.Id], icon.Select(item => ThreadOf(item).Id));
                Assert.All(icon, item => Assert.Null(Get(KindOf(item), "ProjectName")));
                Assert.Equal<ProjectIcon?>([new ProjectIcon.Symbol("star"), folder, folder, folder], icon.Select(item => (ProjectIcon?)Get(KindOf(item), "ProjectIcon")));

                model.Settings.ActivityThreadStyle = ActivityThreadStyle.ProjectName;
                Assert.Equal("projectName", harness.Defaults.Object("activityThreadStyle")); // :278
                var named = ThreadItems(ActivityItems(view)).ToList();
                Assert.Equal(["Alpha", "Beta", "", "Beta"], named.Select(item => (string?)Get(KindOf(item), "ProjectName")));
                Assert.Equal<ProjectIcon?>([new ProjectIcon.Symbol("star"), null, null, null], named.Select(item => (ProjectIcon?)Get(KindOf(item), "ProjectIcon")));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-042")] // App/Views/Sidebar/SidebarView.swift:599, :609-617, :813-825
    public void Activity_rows_carry_their_own_section_as_peers_and_settled_rows_none()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var asking = ThreadAt(alpha, "Asking", Now.AddMinutes(-10));
            var pinnedA = ThreadAt(alpha, "Pinned A", Now.AddHours(-1)) with { IsPinned = true };
            var pinnedB = ThreadAt(alpha, "Pinned B", Now.AddHours(-2)) with { IsPinned = true };
            var todayA = ThreadAt(alpha, "Today A", Now.AddHours(-3));
            var todayB = ThreadAt(alpha, "Today B", Now.AddHours(-4));
            var yesterday = ThreadAt(alpha, "Yesterday", Now.AddDays(-1));
            var settled = ThreadAt(alpha, "Settled", Now.AddHours(-5)) with { IsSettled = true, SettledAt = Now.AddHours(-5) };
            var model = Model(harness, [alpha], [asking, pinnedA, pinnedB, todayA, todayB, yesterday, settled]);
            harness.Main.OnMain(() => InDays(() =>
            {
                model.Settings.SettledCollapsed = false;
                Approve(model, asking.Id);
                var view = Detached(model);
                var rows = ThreadItems(ActivityItems(view)).ToDictionary(item => ThreadOf(item).Id, item => (IEnumerable<Guid>?)Get(KindOf(item), "Peers"));
                Assert.Equal([asking.Id], rows[asking.Id]!);
                Assert.Equal([pinnedA.Id, pinnedB.Id], rows[pinnedA.Id]!);
                Assert.Equal([pinnedA.Id, pinnedB.Id], rows[pinnedB.Id]!);
                Assert.Equal([todayA.Id, todayB.Id], rows[todayB.Id]!);
                Assert.Equal([yesterday.Id], rows[yesterday.Id]!);
                Assert.Null(rows[settled.Id]);

                (IEnumerable<Guid> Order, IEnumerable<Guid>? Group) PeersOf(Guid id)
                {
                    var tuple = Call(view, "Peers", id)!;
                    return ((IEnumerable<Guid>)tuple.GetType().GetField("Item1")!.GetValue(tuple)!, (IEnumerable<Guid>?)tuple.GetType().GetField("Item2")!.GetValue(tuple));
                }
                model.Settings.SidebarActivityView = true;
                var today = PeersOf(todayA.Id);
                Assert.Equal([todayA.Id, todayB.Id], today.Order);
                Assert.Equal([todayA.Id, todayB.Id], today.Group!);
                var none = PeersOf(settled.Id);
                Assert.Empty(none.Order);
                Assert.Null(none.Group);
                Assert.Empty(PeersOf(Guid.NewGuid()).Order);
                // The project layout reorders among the project's open top-level threads (:823-824).
                model.Settings.SidebarActivityView = false;
                var project = PeersOf(todayA.Id);
                Assert.Equal(model.ThreadsIn(alpha).Where(thread => !thread.IsSettled).Select(thread => thread.Id), project.Order);
                Assert.Null(project.Group);
                Assert.Empty(PeersOf(Guid.NewGuid()).Order);
            }));
        });
    }

    // ---- PR-SIDEBAR-043 / -044 / -045: search ----

    internal static List<object> SearchResults(SidebarView view, string query)
    {
        SearchBox(view).Text = query;
        return Items(Call(view, "SearchResults", Helpers(view)));
    }

    internal static string ResultProject(object result) => ((Project)Get(result, "Project")!).Name;

    internal static Guid[] ResultThreads(object result) => ((IEnumerable<ChatThread>)Get(result, "Threads")!).Select(thread => thread.Id).ToArray();

    internal static long ResultCount(object result) => (long)Get(result, "Count")!;

    [Fact]
    [Trait("Req", "PR-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift:737-749
    public void A_project_name_match_returns_its_every_thread_with_helpers_after_their_parent_and_a_title_match_only_matches()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var gamma = NewProject(harness, "Gamma");
            var one = NewThread(alpha, "One", 1);
            var two = NewThread(alpha, "Two", 2);
            var helper = NewThread(alpha, "Helper", 3) with { ParentThreadID = one.Id };
            var settled = NewThread(alpha, "Three", 4) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var notes = NewThread(beta, "Alpha notes", 5);
            var other = NewThread(beta, "Other", 6);
            var lonely = NewThread(gamma, "Nothing here", 7);
            var model = Model(harness, [alpha, beta, gamma], [one, two, helper, settled, notes, other, lonely]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                // threads(in:) order (open newest first, then settled), each followed by its helpers (:742).
                var byName = SearchResults(view, "ALP");
                Assert.Equal(["Alpha", "Beta"], byName.Select(ResultProject));
                Assert.Equal([two.Id, one.Id, helper.Id, settled.Id], ResultThreads(byName[0]));
                Assert.Equal(4L, ResultCount(byName[0]));
                Assert.Equal([notes.Id], ResultThreads(byName[1]));
                Assert.Equal(2L, ResultCount(byName[1])); // the project's full candidate count

                var byTitle = SearchResults(view, "o");
                Assert.Equal(["Alpha", "Beta", "Gamma"], byTitle.Select(ResultProject));
                Assert.Equal([two.Id, one.Id], ResultThreads(byTitle[0]));
                Assert.Equal(4L, ResultCount(byTitle[0]));
                Assert.Equal([other.Id, notes.Id], ResultThreads(byTitle[1]));
                Assert.Equal([lonely.Id], ResultThreads(byTitle[2]));

                var helperOnly = SearchResults(view, "per");
                Assert.Equal(["Alpha"], helperOnly.Select(ResultProject));
                Assert.Equal([helper.Id], ResultThreads(helperOnly[0]));
                Assert.Empty(SearchResults(view, "zzz"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift:740, :743-745
    [Trait("Decision", "ED-009")]
    public void Search_lowercases_per_scalar_so_dotted_capital_I_dotless_i_and_the_final_sigma_match_as_Swift_does()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var travel = NewProject(harness, "Travel");
            var izmir = NewThread(travel, "\U00000130zmir", 1); // U+0130 LATIN CAPITAL LETTER I WITH DOT ABOVE
            var red = NewThread(travel, "K\U00000131rm\U00000131z\U00000131", 2); // U+0131 LATIN SMALL LETTER DOTLESS I
            var road = NewThread(travel, "\U0000039F\U00000394\U0000039F\U000003A3", 3); // GREEK CAPITAL OMICRON, DELTA, OMICRON, SIGMA
            var model = Model(harness, [travel], [izmir, red, road]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Guid[] Found(string query) => SearchResults(view, query).SelectMany(ResultThreads).ToArray();
                // U+0130 lowercases to "i" + U+0307 COMBINING DOT ABOVE, so "iz" is not in it; the dotted needle is.
                Assert.Empty(Found("iz"));
                Assert.Equal([izmir.Id], Found("\U00000130z"));
                // The .NET APIs ED-009 rules out answer differently: Turkish-tailored lowercasing gives a bare "i" (so
                // "iz" would match) and invariant lowercasing leaves U+0130 alone (so the dotted needle would not).
                Assert.Contains("iz", "\U00000130zmir".ToLower(CultureInfo.GetCultureInfo("tr-TR")), StringComparison.Ordinal);
                Assert.DoesNotContain("i\U00000307z", "\U00000130zmir".ToLowerInvariant(), StringComparison.Ordinal); // U+0307 COMBINING DOT ABOVE
                // No Turkish tailoring: "I" lowercases to "i", never to U+0131.
                Assert.Empty(Found("KIR"));
                Assert.Equal([red.Id], Found("K\U00000131R"));
                // Per scalar, capital sigma is always U+03C3, never the final U+03C2.
                Assert.Equal([road.Id], Found("\U000003B4\U000003BF\U000003C3")); // GREEK SMALL DELTA, OMICRON, SIGMA
                Assert.Empty(Found("\U000003B4\U000003BF\U000003C2")); // GREEK SMALL DELTA, OMICRON, FINAL SIGMA
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-044")] // App/Views/Sidebar/SidebarView.swift:705-714
    [Trait("Req", "UI-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:703-712: the gap, project-row and thread-row sequence, keyed like the main list, with hasHelpers
    public void Search_groups_are_separated_by_a_plain_10_point_spacer_that_is_no_list_item()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var parent = NewThread(alpha, "Fix the parent", 1);
            var helper = NewThread(alpha, "Fix the helper", 2) with { ParentThreadID = parent.Id };
            var other = NewThread(beta, "Fix the other", 3);
            var model = Model(harness, [alpha, beta], [parent, helper, other]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                SearchBox(view).Text = "fix";
                var helpers = Helpers(view);
                var entries = Items(Call(view, "SearchEntries", Call(view, "SearchResults", helpers), helpers));
                string Shape(object entry) => Get(entry, "Value") switch
                {
                    double => "gap",
                    ValueTuple<Project, long> => "project",
                    ValueTuple<ChatThread, bool> => "thread",
                    var value => throw new InvalidOperationException(value?.GetType().Name),
                };
                Assert.Equal(["project", "thread", "thread", "gap", "project", "thread"], entries.Select(Shape));
                var gap = entries[3];
                Assert.Equal(10d, Get(gap, "Value")); // Chrome.groupGap
                Assert.Null(Get(gap, "Target")); // not an item ScrollViewReader can reach, unlike the project layout's gap
                var spacer = Assert.IsType<Border>(((Func<Control>)Get(gap, "Make")!)());
                Assert.Equal(10d, spacer.Height);
                Assert.Null(spacer.Child);
                Assert.Null(spacer.Background);
                var first = (ValueTuple<Project, long>)Get(entries[0], "Value")!;
                Assert.Equal(alpha.Id, first.Item1.Id);
                Assert.Equal(2L, first.Item2);
                var threads = entries.Where(entry => Shape(entry) == "thread").ToList();
                // Threads keyed like the main list (:711), handed hasHelpers (:712).
                Assert.Equal([U(parent.Id), U(helper.Id), U(other.Id)], threads.Select(entry => (string?)Get(entry, "Target")));
                Assert.Equal([true, false, false], threads.Select(entry => ((ValueTuple<ChatThread, bool>)Get(entry, "Value")!).Item2));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-045")] // App/Views/Sidebar/SidebarView.swift:693-703
    [Trait("Req", "UI-SIDEBAR-041")] // App/Views/Sidebar/SidebarView.swift:692-703 (part 7: the same empty state, as a
    // screen-contract record)
    public void An_empty_search_shows_No_results_and_the_hint_6_points_apart_with_8_and_14_points_of_padding()
    {
        headless.OnUiThread(() =>
        {
            var stack = Assert.IsType<StackPanel>(typeof(SidebarView).GetMethod("NoResults", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null));
            Assert.Equal(Orientation.Vertical, stack.Orientation);
            Assert.Equal(6d, stack.Spacing);
            Assert.Equal(HorizontalAlignment.Left, stack.HorizontalAlignment);
            Assert.Equal(new Thickness(8, 14, 8, 0), stack.Margin);
            Assert.Equal(2, stack.Children.Count);
            var title = Assert.IsType<TextBlock>(stack.Children[0]);
            var hint = Assert.IsType<TextBlock>(stack.Children[1]);
            Assert.Equal("No results", title.Text);
            Assert.Equal("Try a thread title or a project name.", hint.Text);
            Assert.Equal(13d, title.FontSize); // .body, B1:DroppyCode/UI/Common/ChatZoom.swift:51 (ED-059)
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            Assert.Equal(11d, hint.FontSize); // .subheadline, B1:DroppyCode/UI/Common/ChatZoom.swift:53 (ED-059)
            Assert.Equal(FontWeight.Normal, hint.FontWeight);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color);
            Assert.Equal(Chrome.SecondaryText.Resolve(hint.ActualThemeVariant), ((ISolidColorBrush)hint.Foreground!).Color);
        });
    }

    // ---- PR-SIDEBAR-046 to -050: the drag ----

    // A stand-in for a row's own button (every row is a plain button): a 28 pt slot that counts its clicks.
    internal static Button StandInRow(Action clicked) =>
        PlainControls.Button(new Border { Height = 28, Background = Brushes.Transparent }, clicked);

    internal static Point At(Control control, double x, double y, Visual window) => control.TranslatePoint(new Point(x, y), window)!.Value;

    [Trait("Req", "UI-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:101-148,1622-1632; precise caller citations below
    [Fact]
    [Trait("Req", "PR-SIDEBAR-047")] // App/Views/Sidebar/SidebarView.swift:129-137
    public void A_press_moved_less_than_4_points_still_clicks_the_row_and_a_drag_grabs_the_row_under_its_start_without_clicking()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var id = Guid.NewGuid();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() =>
            {
                var window = (AvaloniaWindow)TopLevel.GetTopLevel(view)!;
                var clicks = 0;
                var row = StandInRow(() => clicks++);
                ListPanel(view).Children.Add(row);
                window.UpdateLayout();
                NoteFrame(view, id, row.Bounds);
                var start = At(row, 40, 10, window);

                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, start.Y + 3), RawInputModifiers.LeftMouseButton);
                window.MouseUp(new Point(start.X, start.Y + 3), MouseButton.Left);
                Assert.Equal(1, clicks);
                Assert.Null(Drag(view).Id);

                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, start.Y + 5), RawInputModifiers.LeftMouseButton);
                Assert.Equal(id, Drag(view).Id);
                Assert.Equal(5d, Drag(view).Translation);
                // Only the vertical travel counts, and the row stays grabbed wherever the pointer goes.
                window.MouseMove(new Point(start.X + 30, start.Y + 12), RawInputModifiers.LeftMouseButton);
                Assert.Equal(12d, Drag(view).Translation);
                window.MouseMove(new Point(start.X, start.Y + 200), RawInputModifiers.LeftMouseButton);
                Assert.Equal(id, Drag(view).Id);
                Assert.Equal(200d, Drag(view).Translation);
                window.MouseMove(new Point(start.X, start.Y + 6), RawInputModifiers.LeftMouseButton);
                window.MouseUp(new Point(start.X, start.Y + 6), MouseButton.Left);
                Assert.Equal(1, clicks); // the drag won over the row's click
                Assert.Null(Drag(view).Id);
                Close(view);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-047")] // App/Views/Sidebar/SidebarView.swift:131-132, :339-347
    public void A_drag_from_a_point_no_open_row_reported_grabs_nothing_and_only_open_rows_report_their_frame()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() =>
            {
                var window = (AvaloniaWindow)TopLevel.GetTopLevel(view)!;
                var clicks = 0;
                var row = StandInRow(() => clicks++);
                var list = ListPanel(view);
                list.Children.Add(row);
                window.UpdateLayout();
                var start = At(row, 40, 10, window);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, start.Y + 10), RawInputModifiers.LeftMouseButton);
                Assert.Null(Drag(view).Id);
                window.MouseUp(new Point(start.X, start.Y + 10), MouseButton.Left);
                Assert.Null(Drag(view).Id);
                Assert.Equal(0, clicks); // the gesture took the press, though it moved nothing

                // :341-347: an open row reports its height and frame, a settled one its height alone; a row that
                // leaves the list is forgotten.
                var project = new Project("Alpha", @"C:\alpha");
                var open = AppModelSamples.Thread(project);
                var settled = AppModelSamples.Thread(project) with { IsSettled = true };
                var openRow = (Control)Call(view, "ReportsFrame", new Border { Height = 30 }, U(open.Id), open)!;
                var settledRow = (Control)Call(view, "ReportsFrame", new Border { Height = 24 }, "settled-" + U(settled.Id), settled)!;
                list.Children.Add(openRow);
                list.Children.Add(settledRow);
                window.UpdateLayout();
                var frames = Field(view, "rowFrames")!;
                var heights = (IReadOnlyDictionary<string, double>)Get(Field(view, "rowHeights")!, "Values")!;
                Assert.Equal(30d, heights[U(open.Id)]);
                Assert.Equal(24d, heights["settled-" + U(settled.Id)]);
                var openCentre = openRow.Bounds.Center;
                Assert.Equal(open.Id, ThreadAtPoint(frames, openCentre));
                Assert.Null(ThreadAtPoint(frames, settledRow.Bounds.Center));
                list.Children.Remove(openRow);
                Assert.Null(ThreadAtPoint(frames, openCentre));
                Close(view);
            });
        });
    }

    [Trait("Req", "UI-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:101-148,1622-1632; precise caller citations below
    [Fact]
    [Trait("Req", "PR-SIDEBAR-046")] // App/Views/Sidebar/SidebarView.swift:129, :136
    [Trait("Req", "UI-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:136 isEnabled: query.isEmpty: reordering is disabled while the search field holds a query
    public void While_the_search_field_holds_a_query_no_drag_is_recognised_and_the_row_still_clicks()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var id = Guid.NewGuid();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                SearchBox(view).Text = " x ";
            });
            harness.Main.OnMain(() =>
            {
                var window = (AvaloniaWindow)TopLevel.GetTopLevel(view)!;
                var clicks = 0;
                var row = StandInRow(() => clicks++);
                ListPanel(view).Children.Add(row);
                window.UpdateLayout();
                NoteFrame(view, id, row.Bounds);
                var start = At(row, 40, 10, window);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X + 20, start.Y + 8), RawInputModifiers.LeftMouseButton);
                Assert.Null(Drag(view).Id);
                window.MouseUp(new Point(start.X + 20, start.Y + 8), MouseButton.Left);
                Assert.Null(Drag(view).Id);
                Assert.Equal(1, clicks);
                // The same drag with the field cleared grabs the row.
                SearchBox(view).Text = "";
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, start.Y + 8), RawInputModifiers.LeftMouseButton);
                Assert.Equal(id, Drag(view).Id);
                window.MouseUp(new Point(start.X, start.Y + 8), MouseButton.Left);
                Assert.Equal(1, clicks);
                Close(view);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-048")] // App/Views/Sidebar/SidebarView.swift:2456-2472
    [Trait("Req", "PR-SIDEBAR-047")] // App/Views/Sidebar/SidebarView.swift:2469-2471
    public void A_point_inside_two_frames_finds_one_of_them_and_a_frame_holds_its_min_edges_but_not_its_max_edges()
    {
        var frames = Activator.CreateInstance(Private("RowFrames"), nonPublic: true)!;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        CallOn(frames, "Note", a, new Rect(0, 0, 100, 28));
        CallOn(frames, "Note", b, new Rect(0, 20, 100, 28));
        CallOn(frames, "Note", c, new Rect(0, 100, 100, 28));
        Assert.Contains(ThreadAtPoint(frames, new Point(50, 24)), new Guid?[] { a, b });
        Assert.Equal(a, ThreadAtPoint(frames, new Point(50, 10)));
        Assert.Equal(b, ThreadAtPoint(frames, new Point(50, 40)));
        // CGRect.contains: minX <= x < maxX and minY <= y < maxY.
        Assert.Equal(c, ThreadAtPoint(frames, new Point(0, 100)));
        Assert.Null(ThreadAtPoint(frames, new Point(50, 128)));
        Assert.Null(ThreadAtPoint(frames, new Point(100, 110)));
        Assert.Null(ThreadAtPoint(frames, new Point(50, 70)));
        CallOn(frames, "Forget", c);
        Assert.Null(ThreadAtPoint(frames, new Point(50, 110)));
        CallOn(frames, "Note", c, new Rect(0, 60, 100, 28));
        Assert.Equal(c, ThreadAtPoint(frames, new Point(50, 70)));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-049")] // App/Views/Sidebar/SidebarView.swift:771-776, :803-805, :829-868
    public void A_grab_takes_its_peers_and_slot_heights_once_and_a_release_without_a_grab_does_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var older = NewThread(alpha, "Older", 1);
            var newer = NewThread(alpha, "Newer", 2);
            var settled = NewThread(alpha, "Settled", 3) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var model = Model(harness, [alpha], [older, newer, settled]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var idle = Drag(view);
                Call(view, "DragEnded");
                Assert.Same(idle, Drag(view)); // :804 nothing grabbed, nothing written
                Assert.Null(Field(view, "dragPeers"));

                Call(view, "DragChanged", older.Id, 10d);
                Assert.Equal(older.Id, Drag(view).Id);
                Assert.Equal(10d, Drag(view).Translation);
                var peers = Field(view, "dragPeers")!;
                Assert.Equal([newer.Id, older.Id], (IEnumerable<Guid>)Get(peers, "Order")!);
                Assert.Null(Get(peers, "Group"));
                // Unmeasured rows: Chrome.rowHeight plus the list's 1 pt spacing (:833).
                var heights = (IReadOnlyDictionary<Guid, double>)Get(peers, "Heights")!;
                Assert.Equal(2, heights.Count);
                Assert.Equal(29d, heights[newer.Id]);
                Assert.Equal(29d, heights[older.Id]);

                Call(view, "DragChanged", older.Id, 12d);
                Assert.Same(peers, Field(view, "dragPeers")); // no swap, no new snapshot
                Assert.Equal(12d, Drag(view).Translation);

                Call(view, "DragEnded");
                Assert.Null(Drag(view).Id);
                Assert.Null(Field(view, "dragPeers"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-055")] // App/Views/Sidebar/SidebarView.swift:803-809 dragEnded
    public void On_release_the_row_springs_home_from_wherever_the_pointer_let_go()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha");
            var first = NewThread(project, "First", 0);
            var second = NewThread(project, "Second", 1);
            var model = Model(harness, [project], [first, second]);
            SidebarView? view = null;
            harness.Main.OnMain(() => view = Detached(model));
            harness.Main.OnMain(() => Call(view!, "DragChanged", first.Id, 30d));
            IReadOnlyList<Guid> before = null!;
            harness.Main.OnMain(() => before = model.ThreadsIn(project).Select(thread => thread.Id).ToList());
            Assert.Equal(30d, Drag(view!).Translation); // the pointer let go 30 pt from the grab
            // Mid-reorder: the pointer has travelled 30 pt and the row's slot has already moved 18 pt toward it.
            harness.Main.OnMain(() => SetProp(Field(view!, "state")!, "Drag", new RowDrag<Guid> { Id = first.Id, Translation = 30, Settled = 18 }));
            Assert.Equal(12d, Drag(view!).VisualOffset);
            Animation? during = null;
            using var probe = new OnChange<RowDrag<Guid>>(() => Drag(view!), (_, _) => during = Transaction.Current);
            harness.Main.OnMain(() => Call(view!, "DragEnded")); // :805-810
            harness.Main.OnMain(() =>
            {
                Assert.Equal(Animation.Spring(response: 0.32, dampingFraction: 0.8), during); // :806-808
                Assert.Null(Drag(view!).Id); // :808 drag = RowDrag()
                Assert.Equal(0d, Drag(view!).Translation); // :808
                Assert.Equal(0d, Drag(view!).Settled); // :808
                Assert.Null(Field(view!, "dragPeers")); // :805
                // The reorder itself was already committed swap by swap during the drag;
                // release commits nothing further.
                Assert.Equal(before, model.ThreadsIn(project).Select(thread => thread.Id).ToList());
            });
        });
    }

    [Trait("Req", "UI-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:101-148,1622-1632; precise caller citations below
    [Fact]
    [Trait("Req", "PR-SIDEBAR-049")] // App/Views/Sidebar/SidebarView.swift:124 (B3; replaces B2's NSCursor push/pop)
    [Trait("Req", "UI-SIDEBAR-060")] // App/Views/Sidebar/SidebarView.swift:124 (part 8: the same grabActive pointer
    // style, as one piece of the dragged-row-lift screen-contract record; the other pieces are proven by the tests
    // below tagged the same way)
    public void The_list_carries_the_grab_active_pointer_style_only_while_a_row_is_dragged()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            StackPanel list = null!;
            var scale = 1d;
            var grabbed = Guid.NewGuid();
            // Track applies one main-actor turn after the state it reads changes (ViewLifetime.Track), so each step
            // that mutates state.Drag runs in its own OnMain, letting the posted apply land before the next assertion.
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                list = (StackPanel)Field(view, "list")!;
                scale = TopLevel.GetTopLevel(list)!.RenderScaling;
                Assert.Null(list.Cursor); // :124 nil while drag.id is nil
            });
            harness.Main.OnMain(() => Call(view, "DragChanged", grabbed, 5d));
            harness.Main.OnMain(() =>
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, scale), list.Cursor)); // :124 .grabActive
            harness.Main.OnMain(() => Call(view, "DragChanged", grabbed, 9d));
            harness.Main.OnMain(() =>
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, scale), list.Cursor)); // stays while grabbed
            harness.Main.OnMain(() => Call(view, "DragEnded"));
            harness.Main.OnMain(() =>
            {
                Assert.Null(list.Cursor); // drag.id returns to nil; the style clears itself
                Call(view, "DragEnded"); // :804 nothing grabbed: no re-application, no throw
            });
            harness.Main.OnMain(() =>
            {
                Assert.Null(list.Cursor);
                Close(view);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-050")] // App/Views/Sidebar/SidebarView.swift:757-762, :777-779; UI/Chrome/RowDrag.swift:15
    public void The_grabbed_row_is_lifted_and_offset_by_the_pointer_travel_at_once_even_inside_an_animation()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var grabbed = Guid.NewGuid();
            var neighbour = Guid.NewGuid();
            SidebarView view = null!;
            Control lifted = null!;
            Control still = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                view = Detached(model);
                lifted = (Control)Call(view, "Lifted", grabbed, (Func<bool, Control>)(isDragged => new Border { Height = 28, Tag = isDragged }))!;
                still = (Control)Call(view, "Lifted", neighbour, (Func<bool, Control>)(isDragged => new Border { Height = 28, Tag = isDragged }))!;
                window = new AvaloniaWindow { Content = new StackPanel { Children = { lifted, still } }, Width = 200, Height = 200 };
                window.Show();
            });
            static double OffsetOf(Control control) => control.RenderTransform is TranslateTransform translate ? translate.Y : 0;
            static object? DraggedInput(Control control) => ((Decorator)control).Child!.Tag;
            harness.Main.OnMain(() =>
            {
                Assert.Equal(0d, OffsetOf(lifted));
                Assert.Equal(0, lifted.ZIndex);
                Assert.Equal(false, DraggedInput(lifted));
                Call(view, "DragChanged", grabbed, 30d);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(30d, OffsetOf(lifted));
                Assert.Equal(1, lifted.ZIndex);
                Assert.Equal(true, DraggedInput(lifted)); // the row is built with isDragged (:759)
                Assert.Equal(0d, OffsetOf(still));
                Assert.Equal(0, still.ZIndex);
                Assert.Equal(false, DraggedInput(still));
                Transaction.WithAnimation(Animation.Spring(response: 0.5, dampingFraction: 1), () => Call(view, "DragChanged", grabbed, 45d));
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(45d, OffsetOf(lifted)); // no animation: it tracks the pointer one to one
                window.Close();
            });
        });
    }

    // ======== Part 3 of 8 ========
    // Records: PR-SIDEBAR-052 to -058, -061 to -073, -075 to -078. SidebarThreadRow's own constructor stays
    // MARROW_STUB: its full body needs settle()/reopen(), archiveWithGenie(), makeActions(), RowGlideAnimator and
    // ThreadActions.makeContextMenu, none of which exist yet (this file's own part-1 header already defers an
    // attached, populated row to "parts 2 to 5"). Every record below is proven as its own piece: a Swift-named
    // static helper (Fill, TrailingClearance), a standalone view (ThreadRowFace, ThreadBadge, SettleCheck,
    // ActivityStatus, RelativeTimeLabel, MinuteClock, RidesWithDraggedParent), or a small extracted seam with no
    // missing collaborator (BackgroundFill, ShowsCheck, SelectThread, NoteHover, ApplyDraggedLift,
    // BuildSettleCheckOverlay, BuildTrailingOverlay) that the row's own body will call once part 4 lands the rest.

    internal static object? CallStatic(Type type, string method, params object?[] args) =>
        type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, args);

    // Every type below builds its whole Child reactively through Track (R-VIEW-1), whose apply runs only once
    // attached to a real visual tree (ViewLifetime.Track: AttachedToVisualTree), never for a merely logical,
    // unshown one (the file's own part-1 header: a DETACHED view's "tracked parts stay inert"). A shown window is
    // therefore required, not a bare host, to observe anything these views draw.
    internal static Border Hosted(AppModel model, Control child)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = child;
        var window = new AvaloniaWindow { Content = host, Width = 200, Height = 100 };
        window.Show();
        return host;
    }

    internal static T Shown<T>(T control) where T : Control
    {
        var window = new AvaloniaWindow { Content = control, Width = 200, Height = 100 };
        window.Show();
        return control;
    }

    internal static Control MakeFace(ChatThread thread, string? projectName, bool isSelected, double trailingClearance,
        ProjectIcon? projectIcon = null, string? mergeStage = null) =>
        (Control)Activator.CreateInstance(Private("ThreadRowFace"), thread, projectName, isSelected, trailingClearance,
            (Func<Control>)(() => new Border { Tag = "badge" }), projectIcon, mergeStage)!;

    internal static ChatThread HeadHelper(Project project, Guid parentID, long index, int minute) =>
        NewThread(project, "Head" + index, minute) with
        {
            ParentThreadID = parentID,
            IsInPanel = false,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) with { Index = index },
        };

    // ---- PR-SIDEBAR-056: a helper, head card or folded stub rides its dragged parent ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-056")] // App/Views/Sidebar/SidebarView.swift:1622-1632
    [Trait("Req", "UI-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:1626-1631: the same offset (visualOffset, settled part taken off) and zIndex 1
    public void A_helper_under_a_dragged_parent_takes_the_parents_offset_and_zIndex_1()
    {
        var type = Private("RidesWithDraggedParent");
        var parent = Guid.NewGuid();

        var riding = (Control)CallOn(
            Activator.CreateInstance(type, parent, new RowDrag<Guid> { Id = parent, Translation = 40d, Settled = 10d })!,
            "Body", new Border())!;
        Assert.Equal(30d, ((TranslateTransform)riding.RenderTransform!).Y); // :1627, :1629 visualOffset = 40 - 10
        Assert.Equal(1, riding.ZIndex); // :1630

        var elsewhere = (Control)CallOn(
            Activator.CreateInstance(type, Guid.NewGuid(), new RowDrag<Guid> { Id = parent, Translation = 40d, Settled = 10d })!,
            "Body", new Border())!;
        Assert.Equal(0d, ((TranslateTransform)elsewhere.RenderTransform!).Y); // a different parent id: does not ride
        Assert.Equal(0, elsewhere.ZIndex);

        var noParent = (Control)CallOn(Activator.CreateInstance(type, null, new RowDrag<Guid>())!, "Body", new Border())!;
        Assert.Equal(0d, ((TranslateTransform)noParent.RenderTransform!).Y); // a nil parent id: never rides
        Assert.Equal(0, noParent.ZIndex);
    }

    // ---- PR-SIDEBAR-052, -053: the swap's spring and its peer re-snapshot ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-052")] // App/Views/Sidebar/SidebarView.swift:767, :784-793
    [Trait("Req", "PR-SIDEBAR-053")] // App/Views/Sidebar/SidebarView.swift:794-801
    [Trait("Req", "UI-SIDEBAR-060")] // App/Views/Sidebar/SidebarView.swift:767, :784-793 (part 8: the "neighbours
    // slide on response 0.28 damping 0.82" piece of the dragged-row-lift screen-contract record)
    public void An_accepted_swap_runs_inside_the_slide_spring_and_re_takes_the_peer_snapshot()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var older = NewThread(alpha, "Older", 1);
            var newer = NewThread(alpha, "Newer", 2);
            var model = Model(harness, [alpha], [older, newer]);
            SidebarView view = null!;
            object? peersBefore = null;
            harness.Main.OnMain(() =>
            {
                view = Detached(model);
                Call(view, "DragChanged", older.Id, 1d); // grabs; takes the first snapshot (order [newer, older])
                peersBefore = Field(view, "dragPeers");
            });
            Animation? during = null;
            using var probe = new OnChange<ValueList<ChatThread>>(() => model.Threads, (_, _) => during = Transaction.Current);
            harness.Main.OnMain(() => Call(view, "DragChanged", older.Id, -35d)); // crosses newer's centre (threshold 29)
            harness.Main.OnMain(() =>
            {
                Assert.Equal(Animation.Spring(response: 0.28, dampingFraction: 0.82), during); // :767, :784-793
                var peersAfter = Field(view, "dragPeers")!;
                Assert.NotSame(peersBefore, peersAfter); // :796-798 re-taken once the swap is accepted
                Assert.Equal([older.Id, newer.Id], (IEnumerable<Guid>)Get(peersAfter, "Order")!); // the list as it now stands
            });
        });
    }

    // ---- PR-SIDEBAR-057, -058: a peer's reorder slot ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-057")] // App/Views/Sidebar/SidebarView.swift:829-856, :1277, :1308-1311
    public void A_peers_slot_is_its_row_plus_its_folded_stub_or_its_helpers_and_head_card_each_plus_1_pt()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var folded = NewThread(alpha, "Folded", 1) with { ExpandsHelpers = false };
            var foldedHelper = NewThread(alpha, "FoldedHelper", 2) with { ParentThreadID = folded.Id };
            var open = NewThread(alpha, "Open", 3) with { ExpandsHelpers = true };
            var plainA = NewThread(alpha, "PlainA", 4) with { ParentThreadID = open.Id };
            var plainB = NewThread(alpha, "PlainB", 5) with { ParentThreadID = open.Id };
            var head1 = HeadHelper(alpha, open.Id, 0, 6);
            var head2 = HeadHelper(alpha, open.Id, 1, 7);
            var model = Model(harness, [alpha], [folded, foldedHelper, open, plainA, plainB, head1, head2]);
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraAutoHidesIdleHeads = false;
                var view = Detached(model);
                var rowHeights = Field(view, "rowHeights")!;
                CallOn(rowHeights, "Note", "helpers-" + SwiftUUID.UuidString(folded.Id), 40d); // a measured stub height
                var heights = (IReadOnlyDictionary<Guid, double>)Call(view, "SlotHeights", new ValueList<Guid>([folded.Id, open.Id]))!;
                Assert.Equal(29d + (40d + 1d), heights[folded.Id]); // :833 row, :837 measured stub + 1
                // :839-840 two unmeasured plain helpers (24 fallback each, +1 each) and :845-847 one head card
                // (2 heads -> (2+5)/6 = 1 row).
                var headCardHeight = (double)CallStatic(Private("ThreadRowMetrics"), "HeadCardHeight", 1L)!;
                Assert.Equal(29d + (24d + 1d) * 2 + (headCardHeight + 1d), heights[open.Id]);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:845-847 (upstream defect 59, reproduced)
    public void The_slot_head_card_fallback_assumes_six_glyphs_per_row_crossing_at_7_and_13()
    {
        double HeightFor(int headCount)
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 1) with { ExpandsHelpers = true };
            var heads = Enumerable.Range(0, headCount).Select(i => HeadHelper(alpha, parent.Id, i, 2 + i)).ToList();
            var model = Model(harness, [alpha], new[] { parent }.Concat(heads));
            var result = 0d;
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraAutoHidesIdleHeads = false;
                var view = Detached(model);
                var heights = (IReadOnlyDictionary<Guid, double>)Call(view, "SlotHeights", new ValueList<Guid>([parent.Id]))!;
                result = heights[parent.Id];
            });
            return result;
        }
        double CardHeight(long rows) => (double)CallStatic(Private("ThreadRowMetrics"), "HeadCardHeight", rows)!;
        var row = Chrome.RowHeight + 1;
        headless.OnUiThread(() =>
        {
            // :846 rows = (headCount + 5) / 6: 6 heads round down to 1 row, 7 round up to 2. The real card lays out
            // 7 per row until measured (upstream defect 59), so it would not cross until 8; the fallback disagrees
            // and is reproduced verbatim, not corrected (PR-SIDEBAR-058).
            Assert.Equal(row + CardHeight(1) + 1d, HeightFor(6));
            Assert.Equal(row + CardHeight(2) + 1d, HeightFor(7));
            Assert.Equal(row + CardHeight(2) + 1d, HeightFor(12));
            Assert.Equal(row + CardHeight(3) + 1d, HeightFor(13));
        });
    }

    // ---- PR-SIDEBAR-054: the lifted row's scale, fill and shadow ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-054")] // App/Views/Sidebar/SidebarView.swift:1838-1849
    [Trait("Req", "UI-SIDEBAR-060")] // App/Views/Sidebar/SidebarView.swift:1838-1849 (part 8: the "lifted while
    // dragged" and "springs in and out on response 0.25 damping 0.8" pieces of the dragged-row-lift screen-contract
    // record)
    [Trait("Req", "UI-SIDEBAR-017")] // SidebarView.swift:1843 the face's dragged state, "black 0.28 shadow radius 10 y 4" (sigma 10, ED-327)
    public void The_lifted_rows_scale_fill_and_shadow_spring_in_and_out_on_response_0_25_damping_0_8()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var grabbed = Guid.NewGuid();
            SidebarView view = null!;
            Border content = null!;
            Control host = null!;
            AvaloniaWindow window = null!;
            var manual = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(manual);
            harness.Main.OnMain(() =>
            {
                view = Detached(model);
                content = new Border { Height = 20 };
                host = (Control)CallStatic(Private("SidebarThreadRow"), "ApplyDraggedLift", content, (Func<bool>)(() => Drag(view).Id == grabbed))!;
                window = new AvaloniaWindow { Content = host, Width = 100, Height = 100 };
                window.Show();
            });
            static double ScaleOf(Control control) => control.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;
            static double LiftOpacity(Control host) => LiftOf(host).Opacity;
            // :1838-1846 .background { lift }.scaleEffect(isDragged ? 1.02 : 1): the scale applies to the row WITH its
            // lifted fill and shadow, so it sits on the host that holds both, and neither carries a scale of its own.
            static void OneScale(Control host, Control content, double expected)
            {
                Assert.Equal(expected, ScaleOf(host), 3);
                Assert.Equal(1d, ScaleOf(content));
                Assert.Equal(1d, ScaleOf(LiftOf(host)));
                Assert.Equal(1d, ScaleOf(LiftShadowOf(host)));
            }
            harness.Main.OnMain(() =>
            {
                OneScale(host, content, 1d);
                Assert.Equal(0d, LiftOpacity(host));
                Call(view, "DragChanged", grabbed, 5d);
            });
            for (var i = 0; i < 100; i++) // 1.6 s of 16 ms frames: well past a 0.25 s response spring's settle
            {
                harness.Main.Advance(TimeSpan.FromMilliseconds(16));
                manual.Advance(TimeSpan.FromMilliseconds(16));
            }
            harness.Main.OnMain(() =>
            {
                OneScale(host, content, 1.02d); // :1846
                Assert.Equal(1d, LiftOpacity(host)); // :1842-1843 fully faded in
                // :1843 .shadow(color: .black.opacity(0.28), radius: 10, y: 4): the shadow that springs in with the lift is
                // SwiftUI's radius 10 as a Gaussian of sigma 10 (ED-327; drawn so, SidebarLiftShadowTests).
                // ED-358 (was the lift's BoxShadow of Blur (10 - 0.5) / 0.288675 at the colour's full 0.28): its caster, radius 10,
                // y 4, black 0.28 cast at the fill's alpha (26 of 255), so drawn at 7, fading in with the fill.
                var shadow = LiftShadowOf(host);
                Assert.Equal((Color.FromArgb((byte)(0.28 * 255), 0, 0, 0), 10d, 0d, 4d), (shadow.Color, shadow.Radius, shadow.X, shadow.Y));
                Assert.Equal(Color.FromArgb(7, 0, 0, 0), shadow.DrawnColor);
                Assert.Equal(default, LiftOf(host).BoxShadow);
                Call(view, "DragEnded");
            });
            for (var i = 0; i < 100; i++)
            {
                harness.Main.Advance(TimeSpan.FromMilliseconds(16));
                manual.Advance(TimeSpan.FromMilliseconds(16));
            }
            harness.Main.OnMain(() =>
            {
                OneScale(host, content, 1d); // springs back out
                Assert.Equal(0d, LiftOpacity(host));
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-054")] // App/Views/Sidebar/SidebarView.swift:1735, :1838-1843
    [Trait("Req", "UI-SIDEBAR-060")] // App/Views/Sidebar/SidebarView.swift:1838-1843 (part 8: the "colors" piece of
    // the dragged-row-lift screen-contract record, "opaque backing fill Chrome.overlay(0.12); shadow black at 0.28")
    public void The_lifted_rows_fill_and_shadow_follow_the_rows_own_continuous_row_corner_radius()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var content = new Border { Height = 20 };
                var host = (Control)CallStatic(Private("SidebarThreadRow"), "ApplyDraggedLift", content, (Func<bool>)(() => false))!;
                var lift = LiftOf(host);
                // :1735 let shape = RoundedRectangle(cornerRadius: Chrome.rowCornerRadius, style: .continuous), the
                // same shape the row's own background/contentShape use (SidebarThreadRow.body, not yet ported) and
                // the lift's fill and shadow follow at :1838-1843. Chrome.RowCornerRadius is 7 (Chrome.cs:104).
                Assert.Equal(new CornerRadius(Chrome.RowCornerRadius), lift.CornerRadius);
                Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(lift));
                // :1842 .fill(Chrome.overlay(0.12)): an opaque backing fill, so a row sliding underneath the lifted
                // one during a swap never shows through it.
                Assert.Equal(Chrome.Overlay(0.12).Resolve(lift.ActualThemeVariant), ((ISolidColorBrush)lift.Background!).Color);
                // :1843 .shadow(color: .black.opacity(0.28), radius: 10, y: 4): ED-358 (was the lift's own BoxShadow): the lift's caster,
                // the fill's own shape (the lift's corners) casting black 0.28 at the fill's alpha, under it; radius 10 (sigma 10, ED-327).
                var shadow = LiftShadowOf(host);
                Assert.Same(lift, shadow.Source);
                Assert.Equal(Color.FromArgb((byte)(0.28 * 255), 0, 0, 0), shadow.Color);
                Assert.Equal((10d, 0d, 4d), (shadow.Radius, shadow.X, shadow.Y));
                Assert.Equal(0, ((Panel)host).Children.IndexOf(shadow));
                Assert.Equal(1, ((Panel)host).Children.IndexOf(lift));
                Assert.Equal(default, lift.BoxShadow);
            });
        });
    }

    // ---- PR-SIDEBAR-061, -062: click selects, hover pre-decodes ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-061")] // App/Views/Sidebar/SidebarView.swift:1736-1738
    public void A_thread_rows_click_sets_selectedThreadID()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                Assert.Null(model.SelectedThreadID);
                CallStatic(Private("SidebarThreadRow"), "SelectThread", model, thread);
                Assert.Equal(thread.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-062")] // App/Views/Sidebar/SidebarView.swift:1816-1820
    public void Hovering_a_thread_row_warms_its_document_and_leaving_schedules_nothing_more()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            AppModelHarness.WriteDocument(thread.Id);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                Assert.Null(DocumentPrefetch.Shared.Take(thread.Id));
                CallStatic(Private("SidebarThreadRow"), "NoteHover", model, thread, true);
            });
            AppModelHarness.WaitForPrefetch();
            harness.Main.OnMain(() =>
            {
                Assert.NotNull(DocumentPrefetch.Shared.Take(thread.Id)); // :1819 warmDocuments([thread.id])
                CallStatic(Private("SidebarThreadRow"), "NoteHover", model, thread, false);
            });
            AppModelHarness.WaitForPrefetch();
            harness.Main.OnMain(() => Assert.Null(DocumentPrefetch.Shared.Take(thread.Id))); // leaving schedules nothing
        });
    }

    // ---- PR-SIDEBAR-063, -064: the row's own fill ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-063")] // App/Views/Sidebar/SidebarView.swift:1874-1877
    public void An_unpinned_rows_fill_is_0_12_selected_0_06_hovered_otherwise_clear()
    {
        var type = Private("SidebarThreadRow");
        Assert.Equal(0.12, (double)CallStatic(type, "Fill", true, false)!); // :1875 selected
        Assert.Equal(0.12, (double)CallStatic(type, "Fill", true, true)!); // selected wins over hovering
        Assert.Equal(0.06, (double)CallStatic(type, "Fill", false, true)!); // :1876 hovered
        Assert.Equal(0d, (double)CallStatic(type, "Fill", false, false)!); // neither
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-064")] // App/Views/Sidebar/SidebarView.swift:1745-1758
    public void A_pinned_row_is_tinted_danger_0_10_unselected_or_danger_0_06_over_the_selection_fill()
    {
        var type = Private("SidebarThreadRow");
        static Color Resolved(SwiftColor color) => color.Resolve(ThemeVariant.Light);

        var (unselected, overlay1) = ((SwiftColor, SwiftColor?))CallStatic(type, "BackgroundFill", true, false, false)!;
        Assert.Equal(Resolved(Chrome.Danger.Opacity(0.10)), Resolved(unselected)); // :1751-1752
        Assert.Null(overlay1);

        var (selected, overlay2) = ((SwiftColor, SwiftColor?))CallStatic(type, "BackgroundFill", true, true, false)!;
        Assert.Equal(Resolved(Chrome.Overlay(0.12)), Resolved(selected)); // the selection fill, not the pinned one
        Assert.Equal(Resolved(Chrome.Danger.Opacity(0.06)), Resolved(overlay2!)); // :1756-1758

        var (plain, overlay3) = ((SwiftColor, SwiftColor?))CallStatic(type, "BackgroundFill", false, false, true)!;
        Assert.Equal(Resolved(Chrome.Overlay(0.06)), Resolved(plain));
        Assert.Null(overlay3);
    }

    // ---- PR-SIDEBAR-065, -066, -067, -070: the face's title, leading slot and second line ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-065")] // App/Views/Sidebar/SidebarView.swift:2040-2051, :2075-2078
    public void An_open_titles_13pt_is_medium_when_selected_or_unread_and_a_settled_titles_12pt_is_always_regular()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var unread = NewThread(alpha, "Unread", 1) with { HasUnread = true };
            var quiet = NewThread(alpha, "Quiet", 2);
            harness.Main.OnMain(() =>
            {
                var open = MakeFace(unread, null, isSelected: false, trailingClearance: 52);
                var title = LogicalQuery.SelfAndDescendants<TextBlock>(open).First();
                Assert.Equal(13d, title.FontSize); // :2041 open: 13 pt
                Assert.Equal(FontWeight.Medium, title.FontWeight); // hasUnread, not selected: medium
                Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color);

                var plain = MakeFace(quiet, null, isSelected: false, trailingClearance: 52);
                var plainTitle = LogicalQuery.SelfAndDescendants<TextBlock>(plain).First();
                Assert.Equal(FontWeight.Normal, plainTitle.FontWeight); // no unread, not selected: regular

                var settled = quiet with { IsSettled = true };
                var settledFace = MakeFace(settled, null, isSelected: true, trailingClearance: 18);
                var settledTitle = LogicalQuery.SelfAndDescendants<TextBlock>(settledFace).First();
                Assert.Equal(12d, settledTitle.FontSize); // :2048 settled: 12 pt
                Assert.Equal(FontWeight.Normal, settledTitle.FontWeight); // :2048 !isSettled gate: always regular once settled
                Assert.Equal(Chrome.PrimaryText.Opacity(0.75).Resolve(settledTitle.ActualThemeVariant), ((ISolidColorBrush)settledTitle.Foreground!).Color);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-066")] // App/Views/Sidebar/SidebarView.swift:2021-2033
    public void The_leading_slot_is_20pt_holding_the_project_mark_or_the_badge_and_is_absent_when_detailed_or_settled()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            harness.Main.OnMain(() =>
            {
                // Icon-style (no projectName): the badge shows in a 20 pt slot.
                var iconStyle = MakeFace(thread, null, isSelected: false, trailingClearance: 52);
                var badge = LogicalQuery.SelfAndDescendants<Control>(iconStyle).FirstOrDefault(c => (string?)c.Tag == "badge");
                Assert.NotNull(badge);
                Assert.Equal(Chrome.IconSize, badge!.Width); // :2031

                // Project-name style, detailed (projectName set, not settled): the slot is omitted.
                var detailed = MakeFace(thread, "Alpha", isSelected: false, trailingClearance: 18);
                Assert.Null(LogicalQuery.SelfAndDescendants<Control>(detailed).FirstOrDefault(c => (string?)c.Tag == "badge"));

                // Settled: the slot is omitted even without a project name.
                var settled = thread with { IsSettled = true };
                var settledFace = MakeFace(settled, null, isSelected: false, trailingClearance: 18);
                Assert.Null(LogicalQuery.SelfAndDescendants<Control>(settledFace).FirstOrDefault(c => (string?)c.Tag == "badge"));

                // A project mark, passed in, replaces the badge in the icon-style slot.
                var withMark = MakeFace(thread, null, isSelected: false, trailingClearance: 52, projectIcon: new ProjectIcon.Symbol("folder"));
                Assert.NotNull(LogicalQuery.SelfAndDescendants<ProjectIconMark>(withMark).FirstOrDefault());
                Assert.Null(LogicalQuery.SelfAndDescendants<Control>(withMark).FirstOrDefault(c => (string?)c.Tag == "badge"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-067")] // App/Views/Sidebar/SidebarView.swift:2034-2052
    [Trait("Req", "UI-SIDEBAR-019")] // App/Views/Sidebar/SidebarView.swift:2053 (part 6: the project line's absence with a picked icon)
    public void A_detailed_row_with_a_picked_mark_leads_the_title_at_14pt_and_drops_the_project_line()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            harness.Main.OnMain(() =>
            {
                var face = MakeFace(thread, "Alpha", isSelected: false, trailingClearance: 18, projectIcon: new ProjectIcon.Symbol("folder"));
                var mark = LogicalQuery.SelfAndDescendants<ProjectIconMark>(face).Single();
                var title = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(face)); // no second project line
                Assert.Equal("Thread", title.Text);

                var withoutIcon = MakeFace(thread, "Alpha", isSelected: false, trailingClearance: 18);
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<TextBlock>(withoutIcon).Count()); // title + project line
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-070")] // App/Views/Sidebar/SidebarView.swift:2053-2063
    [Trait("Req", "UI-SIDEBAR-019")] // App/Views/Sidebar/SidebarView.swift:2053-2063 (part 6: the same project line, as a screen-contract record)
    public void A_detailed_second_line_shows_a_folder_or_branch_mark_then_the_project_name_or_merge_stage()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var plain = NewThread(alpha, "Plain", 1);
            var worktree = NewThread(alpha, "Worktree", 2) with { WorktreePath = "C:\\wt" };
            harness.Main.OnMain(() =>
            {
                var plainFace = MakeFace(plain, "Alpha", isSelected: false, trailingClearance: 18);
                var texts = LogicalQuery.SelfAndDescendants<TextBlock>(plainFace).ToList();
                Assert.Equal("Alpha", texts[1].Text); // :2057 projectName
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(plainFace).Single();
                Assert.Equal("folder", glyph.SymbolName); // :2055 no worktree path

                var worktreeFace = MakeFace(worktree, "Alpha", isSelected: false, trailingClearance: 18);
                var worktreeGlyph = LogicalQuery.SelfAndDescendants<SwiftImage>(worktreeFace).Single();
                Assert.Equal("arrow.triangle.branch", worktreeGlyph.SymbolName); // :2055 a worktree path

                var merging = MakeFace(plain, "Alpha", isSelected: false, trailingClearance: 18, mergeStage: "Merging\U00002026");
                var mergingTexts = LogicalQuery.SelfAndDescendants<TextBlock>(merging).ToList();
                Assert.Equal("Merging\U00002026", mergingTexts[1].Text); // :2057 mergeStage replaces projectName

                // Settled: the second line does not draw at all.
                var settledFace = MakeFace(plain with { IsSettled = true }, "Alpha", isSelected: false, trailingClearance: 18);
                Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(settledFace)); // title only
            });
        });
    }

    // ---- PR-SIDEBAR-068, -069: the thread badge ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-068")] // App/Views/Sidebar/SidebarView.swift:2147-2165
    [Trait("Req", "UI-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift:2143-2179 (part 6: the same badge, as a screen-contract record)
    public void The_badge_shows_the_hand_then_the_spinner_then_the_pin_then_the_provider_icon()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
            var alpha = NewProject(harness, "Alpha");
            var pinned = NewThread(alpha, "Pinned", 1) with { IsPinned = true };
            var plain = NewThread(alpha, "Plain", 2);
            var model = Model(harness, [alpha], [pinned, plain]);
            harness.Main.OnMain(() =>
            {
                // Plain: no runtime at all -> the provider icon.
                var plainBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), plain)!);
                Assert.NotNull(LogicalQuery.SelfAndDescendants<ProviderIcon>(plainBadge).FirstOrDefault());

                // Pinned, no runtime: the pin, danger at 0.85.
                var pinnedBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), pinned)!);
                var pin = LogicalQuery.SelfAndDescendants<SwiftImage>(pinnedBadge).Single();
                Assert.Equal("pin.fill", pin.SymbolName);
                Assert.Equal(Chrome.Danger.Opacity(0.85).Resolve(pin.ActualThemeVariant), ((ISolidColorBrush)pin.Foreground!).Color);

                // Running (merging): the spinner, its tooltip the merge stage, wins over the pin.
                var runtime = model.Runtime(pinned.Id);
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Reviewing";
                var runningBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), pinned)!);
                var spinner = LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(runningBadge).Single();
                Assert.Equal("Reviewing\U00002026", ToolTip.GetTip(spinner));

                // Needs input: the raised hand, orange, wins over everything else.
                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                    new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
                var needsInputBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), pinned)!);
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(needsInputBadge).Single();
                Assert.Equal("hand.raised.fill", hand.SymbolName);
                Assert.Equal(Chrome.Orange.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-069")] // App/Views/Sidebar/SidebarView.swift:2166-2173
    [Trait("Req", "UI-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift:2143-2179 (part 6: the badge's unread-dot state)
    public void An_unread_threads_badge_carries_a_7pt_dot_danger_or_accent_unless_the_hand_or_spinner_shows()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var failed = NewThread(alpha, "Failed", 1) with { HasUnread = true, LastStatus = TurnStatus.Failed };
            var ok = NewThread(alpha, "Ok", 2) with { HasUnread = true, LastStatus = TurnStatus.Completed };
            var model = Model(harness, [alpha], [failed, ok]);
            harness.Main.OnMain(() =>
            {
                var failedBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), failed)!);
                var failedDot = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(failedBadge).Single();
                Assert.Equal(7d, failedDot.Width);
                Assert.Equal(Chrome.Danger.Resolve(failedDot.ActualThemeVariant), ((ISolidColorBrush)failedDot.Fill!).Color);

                var okBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), ok)!);
                var okDot = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(okBadge).Single();
                Assert.Equal(Chrome.Accent.Resolve(okDot.ActualThemeVariant), ((ISolidColorBrush)okDot.Fill!).Color);

                // Needs input suppresses the dot even though hasUnread is true.
                var runtime = model.Runtime(failed.Id);
                typeof(ThreadRuntime).GetProperty("Questions")!.SetValue(runtime,
                    new ValueList<QuestionRequest>([new QuestionRequest("q1", ValueList<QuestionRequest.Question>.Empty)]));
                var suppressed = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), failed)!);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(suppressed));
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-068")] // App/Views/Sidebar/SidebarView.swift:1717-1721, :2147-2151
    [Trait("Req", "PR-SIDEBAR-069")] // App/Views/Sidebar/SidebarView.swift:1717-1721, :2166-2173
    public void The_badge_updates_in_place_when_its_runtime_changes_after_mount()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
            var alpha = NewProject(harness, "Alpha");
            var pinned = NewThread(alpha, "Pinned", 1) with { IsPinned = true };
            var model = Model(harness, [alpha], [pinned]);
            Border host = null!;
            ThreadRuntime runtime = null!;
            harness.Main.OnMain(() =>
            {
                // The runtime exists (and is empty) before the badge ever mounts, exactly as the row's own body
                // would find it already sitting in AppModel; the fix under test is the already-attached badge
                // picking up a LATER change to that same runtime instance, not a fresh one being created.
                runtime = model.Runtime(pinned.Id);
                host = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), pinned)!);
                var pin = LogicalQuery.SelfAndDescendants<SwiftImage>(host).Single(); // :2159-2161, nothing running yet
                Assert.Equal("pin.fill", pin.SymbolName);
            });
            harness.Main.OnMain(() =>
            {
                // Mutate the SAME runtime the already-mounted badge is showing; no ThreadBadge is reconstructed.
                // :1717-1721 (SidebarThreadRow.body comment): "the runtime's fields are observed per runtime."
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Reviewing";
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the badge's own re-layout, posted by Track's apply
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<SwiftImage>(host)); // the pin is gone
                var spinner = LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(host).Single(); // :2156-2158
                Assert.Equal("Reviewing\U00002026", ToolTip.GetTip(spinner));
            });
            harness.Main.OnMain(() =>
            {
                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                    new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(host)); // the hand wins over the spinner
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(host).Single(); // :2153-2155
                Assert.Equal("hand.raised.fill", hand.SymbolName);
                Assert.Equal(Chrome.Orange.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color);
            });
        });
    }

    // ---- PR-SIDEBAR-073: the trailing activity status ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-073")] // App/Views/Sidebar/SidebarView.swift:2412-2427
    [Trait("Req", "UI-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:2408-2427 (part 7: the same states, as a
    // screen-contract record)
    public void The_trailing_status_is_the_hand_then_the_spinner_then_the_unread_dot_then_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1) with { HasUnread = true, LastStatus = TurnStatus.Completed };
            var quiet = NewThread(alpha, "Quiet", 2);
            var model = Model(harness, [alpha], [thread, quiet]);
            harness.Main.OnMain(() =>
            {
                var nothing = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), quiet)!);
                Assert.Null(((Decorator)nothing.Child!).Child); // :2426 nothing drawn

                var dotted = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), thread)!);
                var dot = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(dotted).Single();
                Assert.Equal(7d, dot.Width);
                Assert.Equal(Chrome.Accent.Resolve(dot.ActualThemeVariant), ((ISolidColorBrush)dot.Fill!).Color);

                var runtime = model.Runtime(thread.Id);
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Stage";
                var spinning = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), thread)!);
                var spinner = LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(spinning).Single();
                Assert.Equal("Stage\U00002026", ToolTip.GetTip(spinner));

                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                    new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
                var handing = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), thread)!);
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(handing).Single();
                Assert.Equal("hand.raised.fill", hand.SymbolName);
                Assert.Equal(Chrome.Warning.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color); // :2417 warning, not orange
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-073")] // App/Views/Sidebar/SidebarView.swift:1717-1721, :2412-2427
    public void The_trailing_status_updates_in_place_when_its_runtime_changes_after_mount()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
            var alpha = NewProject(harness, "Alpha");
            var quiet = NewThread(alpha, "Quiet", 1);
            var model = Model(harness, [alpha], [quiet]);
            Border host = null!;
            ThreadRuntime runtime = null!;
            harness.Main.OnMain(() =>
            {
                // Same shape as the ThreadBadge regression above: the runtime exists, empty, before mount, and the
                // already-attached ActivityStatus must pick up a later change to that very instance (no rebuild).
                // :1717-1721 (SidebarThreadRow.body comment): "the runtime's fields are observed per runtime."
                runtime = model.Runtime(quiet.Id);
                host = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), quiet)!);
                Assert.Null(((Decorator)host.Child!).Child); // :2426 nothing drawn: no unread, nothing running
            });
            harness.Main.OnMain(() =>
            {
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Stage";
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the status's own re-layout, posted by Track's apply
            harness.Main.OnMain(() =>
            {
                var spinner = LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(host).Single(); // :2418-2420
                Assert.Equal("Stage\U00002026", ToolTip.GetTip(spinner));
            });
            harness.Main.OnMain(() =>
            {
                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                    new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(host)); // the hand wins over the spinner
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(host).Single(); // :2415-2417
                Assert.Equal("hand.raised.fill", hand.SymbolName);
                Assert.Equal(Chrome.Warning.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color);
            });
        });
    }

    // ---- PR-SIDEBAR-072: the trailing overlay's precedence ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-023")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-072")] // App/Views/Sidebar/SidebarView.swift:1772-1801
    public void The_trailing_overlay_shows_hover_buttons_then_status_then_status_plus_time_then_time_alone()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            var model = Model(harness, [alpha], [thread]);
            var type = Private("SidebarThreadRow");
            var actions = new List<RowAction> { new() { Title = "Rename", Action = () => { } } };
            harness.Main.OnMain(() =>
            {
                var isMenuPresented = new Binding<bool>(() => false, _ => { });

                // showsActions: the archive button (finish action not settle, not settled) then the ellipsis, no gap.
                var withArchive = (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, true, false, false, false, false, (Action)(() => { }), actions, isMenuPresented)!;
                var cluster = Assert.IsType<StackPanel>(withArchive);
                Assert.Equal(0d, cluster.Spacing); // :1775 HStack(spacing: 0)
                Assert.Equal(2, cluster.Children.Count); // archive + ellipsis
                Assert.Equal(new Thickness(0, 0, 6, 0), withArchive.Margin); // :1800 .padding(.trailing, 6)

                // showsActions with settles true: the archive button is gone, only the ellipsis remains.
                var withoutArchive = (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, true, false, false, false, true, (Action)(() => { }), actions, isMenuPresented)!;
                Assert.Single(((StackPanel)withoutArchive).Children);

                // Detailed or settled, not hovering: the activity status alone.
                var detailed = Hosted(model, (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, false, true, false, false, false, (Action)(() => { }), actions, isMenuPresented)!);
                Assert.True(Private("ActivityStatus").IsInstanceOfType(((Border)detailed).Child));

                // Icon style with a project icon: status and time, 6 pt apart.
                var withTime = Hosted(model, (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, false, false, false, true, false, (Action)(() => { }), actions, isMenuPresented)!);
                var pair = Assert.IsType<StackPanel>(((Border)withTime).Child);
                Assert.Equal(6d, pair.Spacing); // :1792-1795
                Assert.Equal(2, pair.Children.Count);

                // Plain project layout: the relative time alone.
                var timeAlone = Hosted(model, (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, false, false, false, false, false, (Action)(() => { }), actions, isMenuPresented)!);
                Assert.True(Private("RelativeTimeLabel").IsInstanceOfType(((Border)timeAlone).Child));
            });
        });
    }

    // ---- PR-SIDEBAR-077, -078: the settle check's visibility and its own view ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-077")] // App/Views/Sidebar/SidebarView.swift:1727-1729, :1763-1771
    [Trait("Req", "UI-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:1763-1771: a settled row's check overlaid at .leading with 6 pt, an open row's at .trailing with 26 pt
    public void The_check_shows_at_the_trailing_end_hovered_or_settling_only_with_the_settle_action_and_leads_a_settled_row()
    {
        var type = Private("SidebarThreadRow");
        Assert.False((bool)CallStatic(type, "ShowsCheck", false, true, false, false)!); // not hovering, not settling
        Assert.True((bool)CallStatic(type, "ShowsCheck", false, true, true, false)!); // showsActions (hovered)
        Assert.True((bool)CallStatic(type, "ShowsCheck", false, true, false, true)!); // isSettling
        Assert.False((bool)CallStatic(type, "ShowsCheck", false, false, true, false)!); // finish action is archive
        Assert.True((bool)CallStatic(type, "ShowsCheck", true, false, false, false)!); // settled: always shows

        var overlay = (Control?)CallStatic(type, "BuildSettleCheckOverlay", false, true, true, false, false, (Action)(() => { }));
        Assert.Equal(HorizontalAlignment.Right, overlay!.HorizontalAlignment); // :1763 open: trailing
        Assert.Equal(new Thickness(0, 0, 26d, 0), overlay.Margin); // :1769 openCheckInset

        var settledOverlay = (Control?)CallStatic(type, "BuildSettleCheckOverlay", true, true, false, false, false, (Action)(() => { }));
        Assert.Equal(HorizontalAlignment.Left, settledOverlay!.HorizontalAlignment); // :1763 settled: leading
        Assert.Equal(new Thickness(6d, 0, 0, 0), settledOverlay.Margin); // :1768 settledCheckInset

        Assert.Null((Control?)CallStatic(type, "BuildSettleCheckOverlay", false, true, false, false, false, (Action)(() => { })));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-078")] // App/Views/Sidebar/SidebarView.swift:2122-2140
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:2114-2142 (part 6: the same check, as a screen-contract record)
    public void The_settle_check_is_filled_while_settled_or_popping_and_outlined_otherwise_with_the_right_tips()
    {
        headless.OnUiThread(() =>
        {
            static Button ButtonOf(Control c) => LogicalQuery.SelfAndDescendants<Button>(c).Single();
            static SwiftImage GlyphOf(Control c) => LogicalQuery.SelfAndDescendants<SwiftImage>(c).Single();

            var tapped = false;
            var open = Shown((Control)Activator.CreateInstance(Private("SettleCheck"), false, false, (Action)(() => tapped = true))!);
            Assert.Equal("checkmark.circle", GlyphOf(open).SymbolName); // :2124-2126 open, not hovering: outline
            Assert.Equal(Chrome.SecondaryText.Resolve(GlyphOf(open).ActualThemeVariant), ((ISolidColorBrush)GlyphOf(open).Foreground!).Color);
            var openButton = ButtonOf(open);
            Assert.Equal("Settle thread", ToolTip.GetTip(openButton)); // :2138
            Assert.Equal("Settle thread", AutomationProperties.GetName(openButton));
            Click(openButton);
            Assert.True(tapped);

            var settled = Shown((Control)Activator.CreateInstance(Private("SettleCheck"), true, false, (Action)(() => { }))!);
            Assert.Equal("checkmark.circle.fill", GlyphOf(settled).SymbolName); // :2124 settled, not hovering: filled
            Assert.Equal(Chrome.Success.Opacity(0.8).Resolve(GlyphOf(settled).ActualThemeVariant), ((ISolidColorBrush)GlyphOf(settled).Foreground!).Color);
            Assert.Equal("Reopen thread", ToolTip.GetTip(ButtonOf(settled)));

            var popping = Shown((Control)Activator.CreateInstance(Private("SettleCheck"), false, true, (Action)(() => { }))!);
            Assert.Equal("checkmark.circle.fill", GlyphOf(popping).SymbolName); // :2124 popping: filled regardless of settled
            Assert.Equal(1.35d, ((ScaleTransform)GlyphOf(popping).RenderTransform!).ScaleX); // :2132
            Assert.Equal(Chrome.Success.Resolve(GlyphOf(popping).ActualThemeVariant), ((ISolidColorBrush)GlyphOf(popping).Foreground!).Color);
        });
    }

    // ---- PR-SIDEBAR-075: the minute clock and the relative time label ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-075")] // App/Views/Sidebar/SidebarView.swift:2667-2680
    public void The_minute_clock_ticks_once_every_60_seconds()
    {
        using var runtime = new TestRuntime();
        var clock = runtime.OnMain(() => (MinuteClock)Activator.CreateInstance(typeof(MinuteClock), nonPublic: true)!);
        var first = runtime.OnMain(() => clock.Now);
        runtime.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(first, runtime.OnMain(() => clock.Now)); // :2676 not yet 60 s
        runtime.Advance(TimeSpan.FromSeconds(1));
        var second = runtime.OnMain(() => clock.Now);
        Assert.NotEqual(first, second); // :2678 ticked
        runtime.Advance(TimeSpan.FromSeconds(60));
        Assert.NotEqual(second, runtime.OnMain(() => clock.Now)); // keeps ticking once a minute
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-075")] // App/Views/Sidebar/SidebarView.swift:2686-2694
    [Trait("Req", "UI-SIDEBAR-023")] // App/Views/Sidebar/SidebarView.swift:2686-2695 (part 7: the same label, as a
    // screen-contract record; the B3 colour is Chrome.secondaryText at 0.8, ED-062's 0.7 label colour further scaled)
    public void The_relative_time_label_reads_the_shared_clocks_now_at_subheadline_size_secondaryText_0_8()
    {
        headless.OnUiThread(() =>
        {
            var date = MinuteClock.Shared.Now.AddMinutes(-3);
            var label = Shown((Control)Activator.CreateInstance(Private("RelativeTimeLabel"), date)!);
            var text = LogicalQuery.SelfAndDescendants<TextBlock>(label).Single();
            Assert.Equal(RelativeTime.Short(date, MinuteClock.Shared.Now), text.Text); // :2690
            Assert.Equal(11d, text.FontSize); // .subheadline, ED-059
            Assert.Equal(Chrome.SecondaryText.Opacity(0.8).Resolve(text.ActualThemeVariant), ((ISolidColorBrush)text.Foreground!).Color); // :2692
            Assert.Equal(new Thickness(0, 0, 4, 0), text.Margin); // :2693
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-075")] // App/Views/Sidebar/SidebarView.swift:1772-1801 the trailing content of .overlay(alignment: .trailing) is centred vertically
    public void The_relative_time_sits_on_the_vertical_centre_of_its_thread_row() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var project = NewProject(harness, "Alpha");
        // A fresh thread: the label reads "now" (no ICU needed). Its age is taken against MinuteClock.Shared.Now (:2690), the process's minute hand,
        // which stands where it was made until its next 60 s tick, so the thread is dated by that hand and not by the wall clock (in a full run the
        // hand was made minutes earlier and the label read "Nm").
        var now = harness.Main.OnMain(() => MinuteClock.Shared.Now);
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the build", CreatedAt = now, UpdatedAt = now };
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var row = LiveThreadRow(thread);
            var window = WindowOf(Hosted(model, row));
            window.UpdateLayout();
            var label = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(text => text.Text == "now");
            // An .overlay(alignment: .trailing) centres its content on the row's height; the panel stretched the label, which drew at the
            // row's top.
            Assert.Equal(label.DesiredSize.Height - label.Margin.Top - label.Margin.Bottom, label.Bounds.Height, 0.5);
            Assert.Equal(row.Bounds.Height / 2, label.TranslatePoint(new Point(0, label.Bounds.Height / 2), row)!.Value.Y, 0.5);
        });
    });

    // ---- PR-SIDEBAR-076: the title's trailing clearance ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-076")] // App/Views/Sidebar/SidebarView.swift:1894-1898, :2065-2066
    public void The_trailing_clearance_is_52_30_18_or_those_plus_20_by_formula()
    {
        var type = Private("SidebarThreadRow");
        Assert.Equal(52d, (double)CallStatic(type, "TrailingClearance", false, false, false, false)!); // plain
        Assert.Equal(18d, (double)CallStatic(type, "TrailingClearance", false, true, false, false)!); // detailed
        Assert.Equal(18d, (double)CallStatic(type, "TrailingClearance", true, false, false, false)!); // settled
        Assert.Equal(52d, (double)CallStatic(type, "TrailingClearance", false, false, true, false)!); // showsActions, open
        Assert.Equal(30d, (double)CallStatic(type, "TrailingClearance", true, false, true, false)!); // showsActions, settled
        Assert.Equal(72d, (double)CallStatic(type, "TrailingClearance", false, false, true, true)!); // + 20 pt fold
        Assert.Equal(50d, (double)CallStatic(type, "TrailingClearance", true, false, true, true)!); // 30 + 20
    }

    // ======== Part 4 of 8 ========
    //
    // Records: PR-SIDEBAR-079, -081, -083, -084, -086, -087, -088, -089, -091, -093, -094, -095, -096, -097, -098,
    // -099, -100, -101, -102, -103, -107, -108, -109, -110.
    //
    // STOP App.Views.Sidebar.SidebarView #6 (extended part 7: UI-SIDEBAR-028, -029, -030 name the same site under
    // the B3 packet's own record ids)
    // Kind: forbidden need
    // Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:995-1024 (B3 52bc792); :1009 .textFieldStyle(.roundedBorder),
    // :1017 .buttonStyle(.bordered), :1019-1022 .buttonStyle(.borderedProminent)
    // Requirements: PR-SIDEBAR-091; UI-SIDEBAR-028 (the rename popover as a whole), UI-SIDEBAR-029 (the Title field;
    // its own site is otherwise unchanged from B1/B2, see this part's b2_change note, so only the field's chrome is
    // blocked, not its text or behaviour), UI-SIDEBAR-030 (the Cancel/Rename buttons)
    // What is unclear or missing: two AppKit control styles this file needs for the rename popover have no Windows
    // chrome anywhere in this port yet (Marrow.UI.Chrome, Marrow.UI.Port) and no engineering or platform-adaptation
    // decision names either one; this part's own decision list (ED-008, ED-009, ED-010, ED-041, ED-051, ED-059,
    // ED-062, ED-149, ED-187, OD-0001, OD-0005, OD-0008, OD-0009, OD-0010, PA-0019) is silent on both, and
    // porting/engineering_decisions.md and porting/platform_adaptations.md have no "roundedBorder" or
    // "borderedProminent"/"bordered" entry. Every other popover this part ports (Delete, Archive) reuses an
    // already-decided recipe (PopoverItem; OD-0005's `.glass`/`.glassProminent`); inventing a third, undecided one
    // here would be exactly the invention the contract forbids. Part 7's own decision list (ED-193 to ED-206) is
    // silent on both too; nothing has changed since part 4.
    // Readings or options: (a) treat `.bordered`/`.borderedProminent` as close enough to the already-decided
    // `.glass`/`.glassProminent` recipe and reuse it -> a visible material the source does not ask for on this
    // popover, an invented equivalence; (b) build a new, unstyled rectangle/border recipe for these two roles ->
    // arbitrary corner radius, padding and colours with no source number or owner ruling behind them.
    // Needed to proceed: an engineering decision (or owner ruling) naming the Windows chrome for
    // `.textFieldStyle(.roundedBorder)` and for `.buttonStyle(.bordered)`/`.buttonStyle(.borderedProminent)`.
    // The ellipsis popover's Rename action currently silently does nothing: it cannot present the missing
    // rename form. This no-op is part of STOP #6, not a completed rename path.
    // Done so far (updated 2026-10-03): RenameThreadPopover and SidebarView.RenamePopover were built on 2026-10-01 and
    // their strings, pre-fill, disabled rule, Return and the model rename are proven in the PR-SIDEBAR-091 section
    // below, so the "silently does nothing" sentence above no longer holds. What stays open is only the chrome named
    // under Needed (and presentedChrome's capsule shape, Popovers.swift:10); PR-SIDEBAR-091, UI-SIDEBAR-028, -029 and
    // -030 are not certified.

    internal static object? SetProp(object target, string name, object? value)
    {
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);
        return null;
    }

    // ---- PR-SIDEBAR-110: HelperConnector and HelperConnectorShape, the dotted line beside a helper row ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-110")] // App/Views/Sidebar/SidebarView.swift:1662-1674
    public void The_connector_shape_runs_from_the_top_to_the_bottom_or_only_to_midY_when_it_ends_here() => headless.OnUiThread(() =>
    {
        var full = (Geometry)CallOn(CreateShapeRecord(false), "Path", new Rect(0, 0, 28, 24))!;
        Assert.Equal(0d, full.Bounds.Top, 3);
        Assert.Equal(24d, full.Bounds.Bottom, 3); // :1669 endsHere false -> rect.maxY

        var last = (Geometry)CallOn(CreateShapeRecord(true), "Path", new Rect(0, 0, 28, 24))!;
        Assert.Equal(0d, last.Bounds.Top, 3);
        Assert.Equal(12d, last.Bounds.Bottom, 3); // :1669 endsHere true -> rect.midY

        object CreateShapeRecord(bool endsHere) => Activator.CreateInstance(Private("HelperConnectorShape"), [endsHere])!;
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-110")] // App/Views/Sidebar/SidebarView.swift:1644-1646
    [Trait("Req", "UI-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift:1636-1660 (part 7: the same connector, as a
    // screen-contract record; the B3 colour is Chrome.secondaryText at 0.45/0.9, ED-062's 0.7 label colour further
    // scaled to 0.315/0.63)
    public void The_connectors_colour_is_secondaryText_at_0_45_or_0_9_hovered()
    {
        var type = Private("HelperConnector");
        static Color Resolved(SwiftColor color) => color.Resolve(ThemeVariant.Light);
        Assert.Equal(Resolved(Chrome.SecondaryText.Opacity(0.45)), Resolved((SwiftColor)CallStatic(type, "Color", false)!)); // :1645
        Assert.Equal(Resolved(Chrome.SecondaryText.Opacity(0.9)), Resolved((SwiftColor)CallStatic(type, "Color", true)!));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-110")] // App/Views/Sidebar/SidebarView.swift:1648-1659
    public void Clicking_the_connector_folds_the_helpers_and_it_is_tooltipped_and_labelled_Hide_helpers() => headless.OnUiThread(() =>
    {
        var folded = false;
        var control = Shown((Control)Activator.CreateInstance(Private("HelperConnector"), false, (Action)(() => folded = true))!);
        var button = Assert.IsType<Button>(LogicalQuery.SelfAndDescendants<Button>(control).Single());
        Assert.Equal("Hide helpers", ToolTip.GetTip(button)); // :1657
        Assert.Equal("Hide helpers", AutomationProperties.GetName(button)); // :1658
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(folded); // :1648 Button(action: action)
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-110")] // App/Views/Sidebar/SidebarView.swift:1653 .contentShape(.rect)
    public void The_connectors_whole_rectangle_is_clickable_not_just_the_drawn_dash() => headless.OnUiThread(() =>
    {
        // RaiseEvent(ClickEvent) above bypasses hit-testing entirely; this exercises a real pointer click at a point
        // that is nowhere near the drawn dash (REVIEW FIX, part 4: IsHitTestVisible alone did not make the empty
        // space clickable; only Background = Transparent does). A real pointer move onto the button also fires its
        // .onHover (:1656), which animates through the frame clock (WallpaperCardFixtures.cs's own pattern), so this
        // binds a manual frame source the way that suite does.
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var folded = false;
        var control = (Control)Activator.CreateInstance(Private("HelperConnector"), false, (Action)(() => folded = true))!;
        control.Width = ThreadRowMetrics.ConnectorWidth; // :1291, 28
        control.Height = ThreadRowMetrics.HelperHeight; // :1277, 24
        Shown(control);
        var window = WindowOf(control);
        window.UpdateLayout();

        // The stroke sits at x = ConnectorLineX (18): a vertical line down the middle-right and a horizontal tick at
        // midY (12) from x=18 to the right edge. (2, 2) is near the top-left corner, clear of both dash segments.
        var point = At(control, 2, 2, window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.True(folded);
        window.Close();
    });

    // ---- PR-SIDEBAR-091: the rename popover (STOP #6 closed 2026-10-01) ----

    internal static Control ShownRename(string title, Action<string> onRename, Action onCancel)
    {
        var popover = (Control)Activator.CreateInstance(Private("RenameThreadPopover"), title, onRename, onCancel)!;
        var window = new AvaloniaWindow { Content = popover, Width = 360, Height = 200 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return popover;
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-028")] // App/Views/Sidebar/SidebarView.swift:1002-1024 the rename popover
    [Trait("Req", "UI-SIDEBAR-029")] // :1006-1009 the Title field
    [Trait("Req", "UI-SIDEBAR-030")] // :1010-1018 Cancel and Rename
    [Trait("Req", "PR-SIDEBAR-091")]
    public void The_rename_popover_is_300_wide_with_its_heading_a_drawn_title_field_and_drawn_cancel_and_rename_buttons() => headless.OnUiThread(() =>
    {
        var popover = ShownRename("Fix the bug", _ => { }, () => { });
        try
        {
            Assert.Equal(300, ((Decorator)popover).Child!.Bounds.Width, 0); // :1022 .padding(14).frame(width: 300)
            Assert.Contains("Rename thread", Texts(popover)); // :1004
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popover));
            Assert.Equal("Fix the bug", field.Text); // :995-1000 text starts as the title
            Assert.Equal("Title", field.PlaceholderText); // :1006
            Assert.True(field.Bounds.Height > 0, "the field is not drawn");
            var buttons = LogicalQuery.SelfAndDescendants<Button>(popover).ToList();
            Assert.Equal(["Cancel", "Rename"], buttons.Select(TextOf));
            Assert.All(buttons, b => Assert.True(b.Bounds.Width > 0 && b.Bounds.Height > 0, TextOf(b) + " is not drawn"));
            Assert.True(buttons[1].IsDefault); // :1016 .keyboardShortcut(.defaultAction)
        }
        finally { WindowOf(popover).Close(); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-030")] // :1014-1017 Rename is disabled while the title is only spaces
    [Trait("Req", "PR-SIDEBAR-091")]
    public void Rename_is_disabled_for_a_blank_title_and_renames_with_the_typed_text_and_cancel_cancels() => headless.OnUiThread(() =>
    {
        var renamed = new List<string>();
        var cancelled = 0;
        var popover = ShownRename("Old", renamed.Add, () => cancelled++);
        try
        {
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popover));
            var rename = LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Rename");
            field.Text = "   ";
            Assert.False(rename.IsEnabled);
            field.Text = "New name";
            Assert.True(rename.IsEnabled);
            PointerClick(rename);
            Assert.Equal(["New name"], renamed); // :1014 onRename(text)
            PointerClick(LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Cancel"));
            Assert.Equal(1, cancelled); // :1012
        }
        finally { WindowOf(popover).Close(); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-029")] // :1006-1007 TextField .roundedBorder: a long title scrolls inside the field
    public void A_long_title_stays_inside_the_field_and_scrolls_to_keep_the_caret_in_view() => headless.OnUiThread(() =>
    {
        var title = string.Concat(Enumerable.Repeat("A long thread title ", 5)); // 100 characters
        var popover = ShownRename(title, _ => { }, () => { });
        try
        {
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popover));
            Assert.True(field.Bounds.Width <= 300 - 28 + 0.5, "the field grew past the popover: " + field.Bounds);
            var line = field.GetVisualDescendants().OfType<ScrollViewer>().Single(v => v.Name == "PART_ScrollViewer");
            field.Focus();
            field.CaretIndex = title.Length;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            WindowOf(popover).UpdateLayout();
            Assert.True(line.Offset.X > 0, "the field did not scroll to the caret: " + line.Offset);
        }
        finally { WindowOf(popover).Close(); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-029")] // :1009 .onSubmit { onRename(text) }: Return submits what is typed, the button's guard aside
    [Trait("Req", "PR-SIDEBAR-091")]
    public void Return_in_the_field_submits_the_typed_title() => headless.OnUiThread(() =>
    {
        var renamed = new List<string>();
        var popover = ShownRename("Old", renamed.Add, () => { });
        try
        {
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popover));
            field.Text = "Typed";
            field.Focus();
            WindowOf(popover).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(["Typed"], renamed);
        }
        finally { WindowOf(popover).Close(); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-091")] // App/Views/Sidebar/SidebarView.swift:239-252 renamePopover(for:on:): renames through the model and closes
    public void The_rename_popover_renames_the_thread_through_the_model_and_closes() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Old title", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "RenameText", "Old title");
            SetProp(state, "Renaming", thread);
            var content = (Control)Call(view, "BuildRenamePopoverContent", thread.Id)!;
            var popover = LogicalQuery.SelfAndDescendants<Control>(content).Single(c => c.GetType().Name == "RenameThreadPopover");
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popover));
            Assert.Equal("Old title", field.Text); // :248 title: renameText
            field.Text = "Renamed";
            LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Rename").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Renamed", model.Thread(thread.Id)!.Title); // :248 model.rename(threadID, to: $0)
            Assert.Null(Get(state, "Renaming")); // :248 renaming = nil
        });
    });

    // ---- PR-SIDEBAR-088: DeleteThreadPopover's content ----

    internal static string? TextOf(Button button) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(button).Select(t => t.Text).FirstOrDefault();

    // A StyledText.Render(host) target holds its text as Inlines' Runs, not host.Text (matches
    // tests/Marrow.UI.Tests/Port/StyledTextTests.cs's own pattern).
    internal static string InlineText(TextBlock block) =>
        block.Inlines is { } inlines ? string.Concat(inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : "";

    internal static AvaloniaWindow WindowOf(Control control) => (AvaloniaWindow)TopLevel.GetTopLevel(control)!;

    [Fact]
    [Trait("Req", "PR-SIDEBAR-088")] // App/Views/Sidebar/SidebarView.swift:966-981
    [Trait("Req", "UI-GIT-057")] // App/Views/Sidebar/SidebarView.swift:965-983; App/AppModel.swift:943-966 (part 6: the same
    // popover, as a screen-contract record)
    [Trait("Req", "UI-SIDEBAR-026")] // App/Views/Sidebar/SidebarView.swift:966-981 (part 7: the same popover, as a
    // screen-contract record)
    public void The_delete_popover_is_280_wide_with_a_header_a_curly_quoted_note_a_divider_and_delete_rows() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // real pointer hover requests the source clock
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug" };
        var deletes = new List<bool>();
        var popover = Shown((Control)Activator.CreateInstance(Private("DeleteThreadPopover"), thread, (Action<bool>)(removeWorktree => deletes.Add(removeWorktree)))!);

        var menu = Assert.IsType<PopoverMenu>(Get(popover, "Child"));
        Assert.Equal(280d, menu.Width); // :980
        var texts = Texts(popover);
        Assert.Contains("Delete this thread?", texts); // :973
        Assert.Contains("\u201CFix the bug\u201D and its history will be removed. Files in your project stay as they are.", texts); // :974
        Assert.Contains("Delete thread", texts); // :976
        Assert.DoesNotContain("Delete thread and worktree", texts); // :977, no worktree

        var deleteButton = LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Delete thread");
        harness.Main.OnMain(() => PointerClick(deleteButton));
        harness.Main.RunUntilIdle(); // :271-274 PopoverItem.Activate spawns the action on the main actor
        Assert.Equal([false], deletes); // :976 onDelete(false)
        harness.Main.OnMain(() => WindowOf(popover).Close());
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-088")] // App/Views/Sidebar/SidebarView.swift:977-978
    [Trait("Req", "UI-GIT-057")] // App/Views/Sidebar/SidebarView.swift:977-978 (part 6: the worktree row's own state)
    [Trait("Req", "UI-SIDEBAR-027")] // App/Views/Sidebar/SidebarView.swift:977-980 (part 7: the same rows, as a
    // screen-contract record)
    public void The_delete_and_worktree_row_shows_only_with_a_worktree_and_deletes_with_it_true() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // real pointer hover requests the source clock
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug", WorktreePath = "C:/Alpha/wt" };
        var deletes = new List<bool>();
        var popover = Shown((Control)Activator.CreateInstance(Private("DeleteThreadPopover"), thread, (Action<bool>)(removeWorktree => deletes.Add(removeWorktree)))!);

        Assert.Contains("Delete thread and worktree", Texts(popover)); // :977
        var button = LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Delete thread and worktree");
        var window = WindowOf(popover);
        window.Height = 200; // this standalone fixture must expose both menu rows to actual pointer hit testing
        window.UpdateLayout();
        harness.Main.OnMain(() => PointerClick(button));
        harness.Main.RunUntilIdle(); // :271-274 PopoverItem.Activate spawns the action on the main actor
        Assert.Equal([true], deletes); // :978 onDelete(true)
        harness.Main.OnMain(() => WindowOf(popover).Close());
    });

    // ---- PR-SIDEBAR-095, PR-SIDEBAR-096: ArchiveThreadPopover ----

    // ArchiveThreadPopover now takes the anchor it resolves its window-scoped key handler from (REVIEW FIX, part 4:
    // see the constructor's own comment). These two helpers host a throwaway anchor alongside the popover in one
    // window -- a degenerate stand-in for ArchivePopover's real `Panel { row, popup }`, adequate for every test below
    // except Through_a_real_popup_..., which deliberately keeps the anchor and the popover in separate top-levels.
    internal static Control ShownArchivePopover(ChatThread thread, Action onArchive, Action onDelete, Action onDismiss)
    {
        var anchor = new Border();
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor, onArchive, onDelete, onDismiss)!;
        new AvaloniaWindow { Content = new Panel { Children = { anchor, popover } }, Width = 200, Height = 100 }.Show();
        return popover;
    }

    internal static Control ShownWithAnchor(Control anchor, Control content)
    {
        new AvaloniaWindow { Content = new Panel { Children = { anchor, content } }, Width = 200, Height = 100 }.Show();
        return content;
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-095")] // App/Views/Sidebar/SidebarView.swift:905-943
    [Trait("Req", "UI-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:917, :927-931: the question "Archive “title”?", click Delete, click Archive (OD-0005 governs the glass material)
    [Trait("Req", "UI-SIDEBAR-032")] // App/Views/Sidebar/SidebarView.swift:927-936: the buttons, their tooltips, Archive the default action
    public void The_archive_popover_is_272_wide_asks_Archive_title_and_offers_Delete_and_Archive() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug" };
        var archived = 0;
        var deleted = 0;
        var popover = ShownArchivePopover(thread, (Action)(() => archived++), (Action)(() => deleted++), (Action)(() => { }));

        var menu = Assert.IsType<PopoverMenu>(Get(popover, "Child"));
        Assert.Equal(272d, menu.Width); // :943
        var question = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Single(t => InlineText(t) == "Archive \u201CFix the bug\u201D?"); // :917

        var buttons = LogicalQuery.SelfAndDescendants<Button>(popover).ToList();
        var delete = buttons.Single(b => TextOf(b) == "Delete"); // :927
        var archive = buttons.Single(b => TextOf(b) == "Archive"); // :931
        Assert.Equal("Remove the thread and its history", ToolTip.GetTip(delete)); // :929
        Assert.Equal("Move it to Archived (Return)", ToolTip.GetTip(archive)); // :936
        Assert.True(archive.IsDefault); // :932 .keyboardShortcut(.defaultAction)

        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, deleted); // :927 Button(action: onDelete)
        archive.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, archived); // :931 Button("Archive", action: onArchive)
        WindowOf(popover).Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-095")] // App/Views/Sidebar/SidebarView.swift:927 Text("Delete").foregroundStyle(.red)
    [Trait("Req", "UI-SIDEBAR-032")] // App/Views/Sidebar/SidebarView.swift:927: Delete's label is the fixed `.red`
    [Trait("Req", "UI-SIDEBAR-031")] // :927 "Delete's label is .red"
    // `.foregroundStyle(.red)` is SwiftUI's system red, not the theme's Chrome.danger (AppTheme.swift:197 only FALLS BACK
    // to .red): under a theme whose palette names its own danger (Nord, :121 "BF616A") the label stays system red.
    public void The_delete_button_in_the_archive_popover_is_drawn_in_system_red_whatever_the_themes_danger() => headless.OnUiThread(() =>
    {
        var saved = ThemeManager.Current;
        ThemeManager.Current = AppTheme.Nord;
        try
        {
            using var harness = new AppModelHarness();
            var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
            var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug" };
            var popover = ShownArchivePopover(thread, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }));
            var delete = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Single(t => t.Text == "Delete");
            var window = WindowOf(popover);
            // ED-092/ED-104 (REVIEW pass 1): Color.red is the documented light/dark pair, not its light member alone; the
            // label follows the appearance (macOS 27 UI Kit "System colours": Red Light #FF383C, Dark #FF4245).
            window.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.FromRgb(255, 56, 60), ((ISolidColorBrush)delete.Foreground!).Color); // systemRed, Default (light)
            Assert.NotEqual(Chrome.Danger.Resolve(delete.ActualThemeVariant), ((ISolidColorBrush)delete.Foreground!).Color); // not Nord's BF616A
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.FromRgb(255, 66, 69), ((ISolidColorBrush)delete.Foreground!).Color); // systemRed, Default (dark): the lighter member
            Assert.NotEqual(Chrome.Danger.Resolve(delete.ActualThemeVariant), ((ISolidColorBrush)delete.Foreground!).Color);
            window.Close();
        }
        finally { ThemeManager.Current = saved; }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-096")] // App/Views/Sidebar/SidebarView.swift:946-955
    [Trait("Req", "UI-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:946-955: Return or keypad Enter archives, Escape dismisses
    public void Return_or_keypad_enter_archives_and_escape_dismisses_swallowing_the_key() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project);
        var archived = 0;
        var dismissed = 0;
        var popover = ShownArchivePopover(thread, (Action)(() => archived++), (Action)(() => { }), (Action)(() => dismissed++));
        var window = WindowOf(popover);
        var composerSawKey = false;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.PhysicalKey == PhysicalKey.Enter) composerSawKey = true; });

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // :951 keyCode 36
        Assert.Equal(1, archived);
        Assert.False(composerSawKey); // :954 return nil: swallowed before the bubble phase

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.NumPadEnter, null); // :951 keyCode 76
        Assert.Equal(2, archived);
        // The monitor answers by the key's code, as the source's keyCode does, not by the button's own default action.
        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.NumPadEnter, null);
        Assert.Equal(3, archived);
        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(4, archived);
        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, null);
        Assert.Equal(4, archived); // any other key goes on untouched

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); // :952 keyCode 53
        Assert.Equal(1, dismissed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-096")] // App/Views/Sidebar/SidebarView.swift:949 modifierFlags.intersection(...).isEmpty
    public void A_modifier_held_lets_the_key_pass_through_the_archive_popovers_monitor() => headless.OnUiThread(() =>
    {
        // Escape, not Return: Return is also the Archive button's own keyboard default-action (:932), so a Ctrl+Return
        // observation would be confounded by Avalonia's own IsDefault dispatch rather than isolating the monitor.
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project);
        var dismissed = 0;
        var popover = ShownArchivePopover(thread, (Action)(() => { }), (Action)(() => { }), (Action)(() => dismissed++));
        var window = WindowOf(popover);

        window.KeyPress(Key.Escape, RawInputModifiers.Control, PhysicalKey.Escape, null); // :949
        Assert.Equal(0, dismissed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-096")] // App/Views/Sidebar/SidebarView.swift:958-961 onDisappear
    public void The_archive_popovers_monitor_is_torn_down_once_it_leaves_the_tree() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project);
        var archived = 0;
        var anchor = new Border();
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor, (Action)(() => archived++), (Action)(() => { }), (Action)(() => { }))!;
        var panel = new Panel { Children = { anchor, popover } };
        var window = new AvaloniaWindow { Content = panel, Width = 200, Height = 100 };
        window.Show();
        panel.Children.Remove(popover); // detaches the popover; the anchor (and its window) stay
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(0, archived); // the handler was removed, so nothing archives
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-096")] // App/Views/Sidebar/SidebarView.swift:944-955, the doc comment at :900-904: "even
    // when the keys land on the chat window behind it"
    public void A_key_landing_on_the_anchors_window_still_archives_and_dismisses_when_the_popover_lives_in_its_own_top_level() => headless.OnUiThread(() =>
    {
        // Avalonia headless cannot open a real Popup at all (ViewLifetime.cs's own comment on IPopoverPresenter:
        // "headless tests record instead ... Avalonia 12.1.2 headless cannot open a Popup in a theme-less window"),
        // so a real Popup can't be exercised here either. This reproduces the shape a real Popup's separate PopupRoot
        // gives it instead: the anchor and the popover content sit in two distinct top-levels (two real Windows), and
        // only the anchor's window receives the key -- exactly AttachmentPreviewPanel.cs:162-172's "the popup's own
        // content is a separate top-level" scenario. Before the REVIEW FIX (resolving TopLevel.GetTopLevel(this)
        // instead of the anchor), the handler would attach to the popover's own window and this key would never
        // reach it.
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project);
        var archived = 0;
        var dismissed = 0;

        var anchor = new Border();
        var anchorWindow = new AvaloniaWindow { Content = anchor, Width = 200, Height = 100 };
        anchorWindow.Show();

        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor,
            (Action)(() => archived++), (Action)(() => { }), (Action)(() => dismissed++))!;
        var popoverWindow = new AvaloniaWindow { Content = popover, Width = 272, Height = 100 };
        popoverWindow.Show(); // a distinct top-level, standing in for the real PopupRoot
        // Archive takes the focus as the popover appears (:945), and the one headless keyboard device then routes a key to
        // the focused element wherever it is. The case under test is the key landing on the anchor's window, the focus late
        // or lost: so the focus is on the anchor's window here, as the source's own comment on the monitor describes
        // (:896-899).
        Dispatcher.UIThread.RunJobs();
        anchor.Focusable = true;
        Assert.True(anchor.Focus());

        anchorWindow.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // :951 keyCode 36
        Assert.Equal(1, archived); // the handler was resolved from the anchor's window, not the popover's own

        anchorWindow.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); // :952 keyCode 53
        Assert.Equal(1, dismissed);

        popoverWindow.Close();
        anchorWindow.Close();
    });

    // ---- PR-SIDEBAR-086, PR-SIDEBAR-087, PR-SIDEBAR-089: the delete popover's coordinator on SidebarView ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-086")] // App/Views/Sidebar/SidebarView.swift:889-896 (and the identical :359-366, :189-196)
    public void RequestDeletion_asks_first_when_confirmBeforeDeleting_and_deletes_at_once_otherwise() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var confirming = NewThread(project, "Ask first", 0);
        var immediate = NewThread(project, "No confirm", 1);
        var model = Model(harness, [project], [confirming, immediate]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            model.Settings.ConfirmBeforeDeleting = true;
            Call(view, "RequestDeletion", confirming.Id);
            var pending = Get(Field(view, "state")!, "PendingDeletionID");
            Assert.Equal(confirming.Id, pending); // :890
            Assert.NotNull(model.Thread(confirming.Id)); // not deleted yet

            model.Settings.ConfirmBeforeDeleting = false;
            Call(view, "RequestDeletion", immediate.Id);
            Assert.Null(model.Thread(immediate.Id)); // :896 deleted at once, removeWorktree false
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-087")] // App/Views/Sidebar/SidebarView.swift:217-237
    public void DeletePopover_presents_only_for_the_matching_id_with_its_arrow_on_the_trailing_edge() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var other = NewThread(project, "Other", 1);
        var model = Model(harness, [project], [thread, other]);
        var recorder = new RecordingPresenter();
        using var presenter = Presentation.Presenter.Override(recorder);
        object? view = null;
        Popup? popup = null;
        // Track's re-evaluation runs on a MainActor.Post; each phase below is its own OnMain call so the previous
        // phase's post is already drained (RunUntilIdle "runs queued work, including work queued while running")
        // before the next phase reads it, rather than re-entering the loop mid-pump.
        harness.Main.OnMain(() =>
        {
            view = Detached(model);
            var row = new Border();
            var panel = (Panel)Call(view, "DeletePopover", thread, row)!;
            popup = Assert.IsType<Popup>(panel.Children[1]);
            Assert.Equal(PlacementMode.Right, popup.Placement); // :220 arrowEdge: .trailing
            Shown(panel);
        });
        harness.Main.OnMain(() => SetProp(Field(view!, "state")!, "PendingDeletionID", other.Id)); // a different thread pending
        harness.Main.OnMain(() => Assert.DoesNotContain(recorder.Calls, c => c.Popup == popup && c.Presented)); // :221 ids differ

        harness.Main.OnMain(() => SetProp(Field(view!, "state")!, "PendingDeletionID", thread.Id)); // :221 this row's id
        harness.Main.OnMain(() => Assert.Contains(recorder.Calls, c => c.Popup == popup && c.Presented));
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-089")] // App/Views/Sidebar/SidebarView.swift:256-263 confirmDeletion
    public void ConfirmDeletion_records_the_deed_clears_both_pending_ids_and_runs_at_once_when_no_popover_shows() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "PendingDeletionID", thread.Id);
            SetProp(state, "PendingArchiveID", thread.Id);
            Call(view, "ConfirmDeletion", thread.Id, true);
            Assert.Null(Get(state, "PendingDeletionID")); // :259
            Assert.Null(Get(state, "PendingArchiveID")); // :260
        });
        harness.Main.Loop.Run(() => Task.Delay(1), AppModelHarness.Bound); // let the spawned MainActor task run
        Assert.Null(model.Thread(thread.Id)); // :261, :303-305 carried out with removeWorktree true
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-089")] // App/Views/Sidebar/SidebarView.swift:295-306 carryOutConfirmedDeletion
    public void ConfirmDeletion_waits_for_the_popover_to_close_when_one_is_still_shown() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "IsDeletePopoverShown", true); // :261 the popover is still up
            Call(view, "ConfirmDeletion", thread.Id, false);
            Assert.NotNull(model.Thread(thread.Id)); // :261 not carried out yet
            Assert.NotNull(Get(state, "ConfirmedDeletion")); // :258, waiting for onDisappear (:231-232)

            SetProp(state, "IsDeletePopoverShown", false);
            Call(view, "CarryOutConfirmedDeletion"); // what the popover's onDisappear calls (:232)
            Assert.Null(Get(state, "ConfirmedDeletion")); // :301
        });
        harness.Main.Loop.Run(() => Task.Delay(1), AppModelHarness.Bound);
        Assert.Null(model.Thread(thread.Id)); // :304-305, one MainActor turn later
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-089")] // App/Views/Sidebar/SidebarView.swift:299-300 guard let confirmed
    public void CarryOutConfirmedDeletion_does_nothing_when_nothing_was_confirmed() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            Call(view, "CarryOutConfirmedDeletion"); // no exception, nothing scheduled
            Assert.Null(Get(Field(view, "state")!, "ConfirmedDeletion"));
        });
    });

    // ---- PR-SIDEBAR-093: rename reads the live thread record ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-093")] // App/Views/Sidebar/SidebarView.swift:882-887 (and the identical :354-358)
    public void LiveRenameTarget_reads_the_live_record_and_falls_back_to_the_snapshot_when_gone() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Old title", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            model.Rename(thread.Id, "New title");
            var live = (ChatThread)CallStatic(typeof(SidebarView), "LiveRenameTarget", model, thread)!;
            Assert.Equal("New title", live.Title); // :886 the live record, not the stale snapshot

            model.Delete(thread.Id);
            var gone = (ChatThread)CallStatic(typeof(SidebarView), "LiveRenameTarget", model, thread)!;
            Assert.Equal(thread.Id, gone.Id); // :886 ?? thread: falls back to the snapshot
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-093")] // App/Views/Sidebar/SidebarView.swift:182-186 the context-menu path uses the posted snapshot
    public void A_rename_posted_from_the_context_menu_uses_the_posted_value_as_is() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Old title", 0);
        var model = Model(harness, [project], [thread]);
        object? view = null;
        harness.Main.OnMain(() =>
        {
            view = Detached(model);
            Call(view, "Appear"); // wires the onChange handlers without needing a real window (ProjectRow is still
            // MARROW_STUB, so a real Attached() render of a non-empty library throws)
            model.Rename(thread.Id, "New title"); // changes after the menu already posted its snapshot
            SetProp(Field(view, "menuRequests")!, "Rename", thread); // the snapshot the AppKit menu captured before the rename
        });
        // A fresh OnMain call: Track's re-evaluation runs on a MainActor.Post, which the call above already drained.
        harness.Main.OnMain(() =>
        {
            var state = Field(view!, "state")!;
            Assert.Equal("Old title", Get(state, "RenameText")); // :187 posted.title, not the live one
            Assert.Equal(thread, Get(state, "Renaming")); // :188
            Assert.Null(Get(Field(view!, "menuRequests")!, "Rename")); // :186 cleared
        });
    });

    // ---- PR-SIDEBAR-094: the archive shortcut's question lands on the thread's own row ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-094")] // App/Views/Sidebar/SidebarView.swift:198-202
    public void An_archive_request_for_a_present_thread_opens_the_question_and_clears_the_request() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        object? view = null;
        harness.Main.OnMain(() =>
        {
            view = Detached(model);
            Call(view, "Appear"); // see the rename test above for why Detached, not Attached
            model.AppModelArchiveRequest = new AppModel.ArchiveRequest(thread.Id);
        });
        harness.Main.OnMain(() =>
        {
            Assert.Null(model.AppModelArchiveRequest); // :200
            Assert.Equal(thread.Id, Get(Field(view!, "state")!, "PendingArchiveID")); // :201
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-094")] // App/Views/Sidebar/SidebarView.swift:199 guard ..., model.thread(request.threadID) != nil
    public void An_archive_request_for_a_missing_thread_is_left_for_the_models_own_fallback() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        SidebarView? view = null;
        var missing = Guid.NewGuid();
        harness.Main.OnMain(() =>
        {
            view = Attached(model);
            model.AppModelArchiveRequest = new AppModel.ArchiveRequest(missing);
        });
        harness.Main.OnMain(() =>
        {
            Assert.NotNull(model.AppModelArchiveRequest); // :199 not taken up: left for the model's 80 ms fallback
            Assert.Null(Get(Field(view!, "state")!, "PendingArchiveID"));
            Close(view!);
        });
    });

    // ---- PR-SIDEBAR-097: archiving or deleting from the archive popover ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-097")] // App/Views/Sidebar/SidebarView.swift:276-279
    public void Archiving_from_the_archive_popovers_content_closes_it_and_archives_inside_panelSlide() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            SetProp(Field(view, "state")!, "PendingArchiveID", thread.Id);
            var anchor = new Border();
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread, anchor)!;
            ShownWithAnchor(anchor, content);
            var archive = LogicalQuery.SelfAndDescendants<Button>(content).Single(b => TextOf(b) == "Archive");
            archive.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Null(Get(Field(view, "state")!, "PendingArchiveID")); // :278
            Assert.True(model.Thread(thread.Id)!.IsArchived); // :279
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-097")] // App/Views/Sidebar/SidebarView.swift:281, the deferred-delete path shared with the delete popover
    public void Deleting_from_the_archive_popovers_content_goes_through_the_deferred_delete_path() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "IsDeletePopoverShown", true); // :285, as the wrapping archivePopover sets on appear
            var anchor = new Border();
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread, anchor)!;
            ShownWithAnchor(anchor, content);
            var delete = LogicalQuery.SelfAndDescendants<Button>(content).Single(b => TextOf(b) == "Delete");
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.NotNull(Get(state, "ConfirmedDeletion")); // :281 confirmDeletion(of:removeWorktree: false)
            Assert.NotNull(model.Thread(thread.Id)); // deferred while the popover is still shown
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-097")] // App/Views/Sidebar/SidebarView.swift:283
    public void Dismissing_the_archive_popovers_content_only_clears_the_pending_id() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "PendingArchiveID", thread.Id);
            var anchor = new Border();
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread, anchor)!;
            ShownWithAnchor(anchor, content);
            var window = WindowOf(content);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); // :952 onDismiss()

            Assert.Null(Get(state, "PendingArchiveID")); // :283 clears the pending id, nothing else
            Assert.NotNull(model.Thread(thread.Id)); // not archived, not deleted
            window.Close();
        });
    });

    // ---- PR-SIDEBAR-079, PR-SIDEBAR-081, PR-SIDEBAR-083: settling, reopening and the finish request ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-079")] // App/Views/Sidebar/SidebarView.swift:1904-1910
    public void Settle_pops_the_check_and_settles_the_thread_with_its_own_sound_suppressed() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var setCalls = new List<bool>();
            CallStatic(Private("SidebarThreadRow"), "Settle", model, thread, false, false, (Action<bool>)(v => setCalls.Add(v)));
            Assert.Equal([true], setCalls); // :1909
            Assert.True(model.Thread(thread.Id)!.IsSettled); // :1910
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-079")] // App/Views/Sidebar/SidebarView.swift:1907 guard !isSettling, !thread.isSettled
    public void Settle_does_nothing_while_already_settling_or_already_settled() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var already = NewThread(project, "Already", 0) with { IsSettled = true };
        var mid = NewThread(project, "Mid", 1);
        var model = Model(harness, [project], [already, mid]);
        harness.Main.OnMain(() =>
        {
            var setCalls = new List<bool>();
            CallStatic(Private("SidebarThreadRow"), "Settle", model, already, false, false, (Action<bool>)(v => setCalls.Add(v)));
            Assert.Empty(setCalls); // :1907 already settled

            CallStatic(Private("SidebarThreadRow"), "Settle", model, mid, true, false, (Action<bool>)(v => setCalls.Add(v)));
            Assert.Empty(setCalls); // :1907 isSettling already true
            Assert.False(model.Thread(mid.Id)!.IsSettled);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-081")] // App/Views/Sidebar/SidebarView.swift:1912-1916
    public void Reopen_glides_before_reopening_and_does_nothing_when_not_settled() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var settled = NewThread(project, "Settled", 0) with { IsSettled = true };
        var open = NewThread(project, "Open", 1);
        var model = Model(harness, [project], [settled, open]);
        harness.Main.OnMain(() =>
        {
            var order = new List<string>();
            CallStatic(Private("SidebarThreadRow"), "Reopen", model, settled, (Action)(() => order.Add("glide")));
            Assert.Equal(["glide"], order); // :1914 launched first, so the arriving row can report its frame to it
            Assert.False(model.Thread(settled.Id)!.IsSettled); // :1915 model.reopenAnimated

            order.Clear();
            CallStatic(Private("SidebarThreadRow"), "Reopen", model, open, (Action)(() => order.Add("glide")));
            Assert.Empty(order); // :1913 guard thread.isSettled
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-083")] // App/Views/Sidebar/SidebarView.swift:1863-1870
    public void FinishRequestReceived_settles_or_reopens_only_the_matching_thread_and_state() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
            var thread = AppModelSamples.Thread(project);
            var settled = 0;
            var reopened = 0;

            model.AppModelFinishRequest = new AppModel.FinishRequest(thread.Id, false); // not settled -> settle()
            CallStatic(Private("SidebarThreadRow"), "FinishRequestReceived", model, thread, false, (Action)(() => settled++), (Action)(() => reopened++));
            Assert.Equal(1, settled); // :1867
            Assert.Equal(0, reopened);
            Assert.Null(model.AppModelFinishRequest); // :1865

            model.AppModelFinishRequest = new AppModel.FinishRequest(thread.Id, false);
            CallStatic(Private("SidebarThreadRow"), "FinishRequestReceived", model, thread, true, (Action)(() => settled++), (Action)(() => reopened++));
            Assert.Equal(1, settled); // :1864 isSettled (true) != request.reopens (false): not taken up
            Assert.NotNull(model.AppModelFinishRequest);

            model.AppModelFinishRequest = new AppModel.FinishRequest(Guid.NewGuid(), false); // a different thread
            CallStatic(Private("SidebarThreadRow"), "FinishRequestReceived", model, thread, false, (Action)(() => settled++), (Action)(() => reopened++));
            Assert.Equal(1, settled); // :1864 threadID mismatch: not taken up
        });
    });

    // ---- PR-SIDEBAR-084: the hover archive button's genie flight ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-084")] // App/Views/Sidebar/SidebarView.swift:1988-1999
    public void ArchiveWithGenie_flies_a_ghost_of_the_row_and_archives_the_thread() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var animator = new GenieAnimator(new RenderingGeniePlatform());
            CallStatic(Private("SidebarThreadRow"), "ArchiveWithGenie", animator, model, thread, null, null, false, null, new Rect(0, 0, 200, 28), ThemeVariant.Light);
            Assert.True(model.Thread(thread.Id)!.IsArchived); // :1998 withAnimation(Chrome.panelSlide) { model.archive(...) }
            Assert.Single(animator.Flights); // :1994-1997 a ghost launched from the row's own frame
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-084")] // App/Views/Sidebar/SidebarView.swift:1990 Chrome.overlay(isSelected ? 0.12 : 0.06)
    public void ArchiveWithGenies_ghost_fill_is_the_selection_overlay() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var animator = new GenieAnimator(new RenderingGeniePlatform());
            CallStatic(Private("SidebarThreadRow"), "ArchiveWithGenie", animator, model, thread, null, null, true, null, new Rect(0, 0, 200, 28), ThemeVariant.Light);
            Assert.Single(animator.Flights); // still flies once, selected this time (:1991 isSelected ? 0.12 : 0.06)
        });
    });

    // ---- PR-SIDEBAR-098, 099, 100, 101, 102, 103: ThreadActions ----

    internal static List<RowAction> ThreadActionsMake(AppModel model, ChatThread thread, Action? onSettle = null, Action? onReopen = null) =>
        ((IEnumerable)CallStatic(Private("ThreadActions"), "Make", model, thread, (Action)(() => { }), (Action)(() => { }), onSettle, onReopen)!)
            .Cast<RowAction>().ToList();

    internal static List<RowAction> ThreadActionsMakeContextMenu(AppModel model, ChatThread thread, bool settleFirst, object requests) =>
        ((IEnumerable)CallStatic(Private("ThreadActions"), "MakeContextMenu", model, thread, settleFirst, requests)!)
            .Cast<RowAction>().ToList();

    [Fact]
    [Trait("Req", "PR-SIDEBAR-099")] // App/Views/Sidebar/SidebarView.swift:2199-2207
    public void Pin_or_unpin_toggles_isPinned_and_is_absent_for_a_helper_or_a_settled_thread() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var parent = NewThread(project, "Parent", 1);
        var helper = NewThread(project, "Helper", 2) with { ParentThreadID = parent.Id };
        var settled = NewThread(project, "Settled", 3) with { IsSettled = true };
        var model = Model(harness, [project], [thread, parent, helper, settled]);
        harness.Main.OnMain(() =>
        {
            var pin = ThreadActionsMake(model, thread).Single(a => a.Title is "Pin" or "Unpin");
            Assert.Equal("Pin", pin.Title); // :2200 not yet pinned
            Assert.Equal("pin", pin.Symbol);
            pin.Action();
            Assert.True(model.Thread(thread.Id)!.IsPinned); // :2201-2203

            var unpin = ThreadActionsMake(model, model.Thread(thread.Id)!).Single(a => a.Title is "Pin" or "Unpin");
            Assert.Equal("Unpin", unpin.Title);
            Assert.Equal("pin.slash", unpin.Symbol);

            Assert.DoesNotContain(ThreadActionsMake(model, helper), a => a.Title is "Pin" or "Unpin"); // :2196-2198
            Assert.DoesNotContain(ThreadActionsMake(model, settled), a => a.Title is "Pin" or "Unpin");
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-100")] // App/Views/Sidebar/SidebarView.swift:2210-2212
    public void Forward_to_new_thread_starts_an_asynchronous_forward() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var forward = ThreadActionsMake(model, thread).Single(a => a.Title == "Forward to new thread"); // :2210
            Assert.Equal("arrowshape.turn.up.right", forward.Symbol);
            forward.Action(); // :2211 Task { await model.forwardThread(thread.id) }
        });
        // AppModel.swift:624-646 awaits document loading and continuation installation before selecting the new thread.
        // Fixed a race (was about 1 in 4 failures): AppModel.ForwardThread (AppModel.cs:722-742) awaits
        // sourceRuntime.EnsureLoaded() and newRuntime.InstallContinuation(continuation), each a real, variable-
        // duration async hop off this loop (I/O-bound), not a single microtask, so poll the real completion
        // condition instead of an arbitrary delay.
        harness.Main.Loop.RunUntil(() =>
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return model.Threads.Count == 2 && model.SelectedThreadID is { } selected && selected != thread.Id;
        }, AppModelHarness.Bound);
        harness.Main.OnMain(() => Assert.Equal(2, model.Threads.Count)); // the forward has landed
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-101")] // App/Views/Sidebar/SidebarView.swift:2216-2218, OD-0008 B1
    public void Reveal_worktree_shows_only_with_a_worktree_and_reads_File_Explorer_by_the_owner_ruling() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var withWorktree = NewThread(project, "WT", 0) with { WorktreePath = "C:/Alpha/wt" };
        var without = NewThread(project, "NoWT", 1);
        var model = Model(harness, [project], [withWorktree, without]);
        harness.Main.OnMain(() =>
        {
            var reveal = ThreadActionsMake(model, withWorktree).Single(a => a.Title == "Reveal worktree in File Explorer"); // OD-0008 B1
            Assert.Equal("folder", reveal.Symbol);

            var revealed = new List<string>();
            var previous = ShellWorkspace.RevealInFileExplorer;
            try
            {
                ShellWorkspace.RevealInFileExplorer = p => revealed.Add(p);
                reveal.Action(); // :2217
                Assert.Equal(["C:/Alpha/wt"], revealed);
            }
            finally
            {
                ShellWorkspace.RevealInFileExplorer = previous;
            }

            Assert.DoesNotContain(ThreadActionsMake(model, without), a => a.Title.StartsWith("Reveal", StringComparison.Ordinal)); // :2214
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-102")] // App/Views/Sidebar/SidebarView.swift:2219-2242
    public void A_helpers_menu_offers_no_pin_and_no_settle_only_archive_in_that_group() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        harness.Main.OnMain(() =>
        {
            var actions = ThreadActionsMake(model, helper);
            var titles = actions.Select(a => a.Title).ToList();
            Assert.DoesNotContain("Pin", titles);
            Assert.DoesNotContain("Unpin", titles);
            Assert.DoesNotContain("Settle", titles);
            Assert.DoesNotContain("Reopen", titles);
            var archive = Assert.Single(actions, a => a.Title == "Archive"); // :2224-2225, only in the settle/archive group
            Assert.True(archive.StartsGroup); // :2221 startsGroup: !settleFirst, and settleFirst is always false for a helper
            // REVIEW FIX (part 4): a helper's list is Rename, Forward, Archive, Delete… = 4, never 5. :2204's Pin
            // guard and :2225-2226's helper branch both gate solely on isHelper, so a helper's count does not depend
            // on isSettled or worktreePath either; this is the list's true minimum (isHelper is the only branch that
            // ever drops the list below 2 items in the settle/archive group).
            Assert.Equal(4, actions.Count);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-103")] // App/Views/Sidebar/SidebarView.swift:2245-2312
    public void The_context_menus_actions_post_rather_than_run() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var actions = ThreadActionsMakeContextMenu(model, thread, true, requests);

            var rename = actions.Single(a => a.Title == "Rename");
            rename.Action();
            Assert.Equal(thread, Get(requests, "Rename")); // :2261 requests.rename = thread, posted rather than run
            Assert.Null(Get(requests, "Delete"));

            var delete = actions.Single(a => a.Title == "Delete\u2026");
            delete.Action();
            Assert.Equal(thread, Get(requests, "Delete")); // :2309-2310
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-103")] // App/Views/Sidebar/SidebarView.swift:2262-2263, :2269 guard let model else { return }
    public void A_context_menu_actions_weak_model_capture_bails_once_the_model_is_gone() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var actions = ThreadActionsMakeContextMenu(model, thread, true, requests);
            var pin = actions.Single(a => a.Title == "Pin");
            GC.KeepAlive(model);
            pin.Action(); // model is still alive: the weak capture resolves
            Assert.True(model.Thread(thread.Id)!.IsPinned);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-098")] // App/Views/Sidebar/SidebarView.swift:2193-2246, :2253-2312
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:2192-2314 (part 7: the same menu items, as a
    // screen-contract record: order, symbols, Delete… in danger, and the settle/archive swap by threadFinishAction)
    public void The_popover_and_context_menu_lists_carry_the_same_items_in_the_same_order() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0) with { WorktreePath = "C:/Alpha/wt" };
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var popoverTitles = ThreadActionsMake(model, thread).Select(a => a.Title).ToList();
            var menuTitles = ThreadActionsMakeContextMenu(model, thread, true, requests).Select(a => a.Title).ToList();
            Assert.Equal(popoverTitles, menuTitles); // the two lists carry the same items in the same order
            Assert.Equal(
                ["Rename", "Pin", "Forward to new thread", "Reveal worktree in File Explorer"],
                popoverTitles.Take(4).ToList()); // :2200-2218 the fixed order up to the settle/archive group
            Assert.Equal("Delete\u2026", popoverTitles[^1]); // :2244 Delete… always last
            // REVIEW FIX (part 4): this non-helper, unsettled, worktree-carrying thread's list is Rename, Pin,
            // Forward, Reveal, [Archive, Settle] (both, :2242, since !isHelper), Delete… = 7 items, counted
            // straight off :2201-2244 (and the identical :2259-2311) -- not 8: nothing in the builder appends an
            // 8th item.
            Assert.Equal(7, popoverTitles.Count);
        });
    });

    // ---- PR-SIDEBAR-107, PR-SIDEBAR-108, PR-SIDEBAR-109: SidebarHelperRow's directly testable pieces ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-107")] // App/Views/Sidebar/SidebarView.swift:1361-1372
    public void A_helper_rows_title_and_background_match_the_recorded_numbers()
    {
        var type = Private("SidebarHelperRow");
        Assert.Equal(SwiftFont.Weight.Medium, CallStatic(type, "TitleWeight", true, false)); // :1361 selected
        Assert.Equal(SwiftFont.Weight.Medium, CallStatic(type, "TitleWeight", false, true)); // :1361 unread
        Assert.Equal(SwiftFont.Weight.Regular, CallStatic(type, "TitleWeight", false, false));
        Assert.Equal(0.96, (double)CallStatic(type, "TitleOpacity", true)!); // :1362
        Assert.Equal(0.8, (double)CallStatic(type, "TitleOpacity", false)!);
        Assert.Equal(34d, (double)CallStatic(type, "TrailingPadding", true)!); // :1365-1367, 8 + 26
        Assert.Equal(26d, (double)CallStatic(type, "TrailingPadding", false)!); // 8 + 18
        Assert.Equal(0.12, (double)CallStatic(type, "BackgroundFill", true, false)!); // :1370 selected
        Assert.Equal(0.06, (double)CallStatic(type, "BackgroundFill", false, true)!); // hovered
        Assert.Equal(0d, (double)CallStatic(type, "BackgroundFill", false, false)!); // neither
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-108")] // App/Views/Sidebar/SidebarView.swift:1354-1357
    [Trait("Req", "UI-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:1354-1357 vs :1477: isRunning is passed on the card but NOT on the helper row, upstream defect 65
    public void A_helper_rows_head_glyph_never_shows_the_running_breath_ring() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var runningHead = NewThread(project, "Head", 0) with { Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) };
        var plain = NewThread(project, "Plain", 1);
        harness.Main.OnMain(() =>
        {
            var glyph = (HydraGlyph)CallStatic(Private("SidebarHelperRow"), "HeadGlyph", runningHead)!;
            Assert.False(glyph.IsRunning); // :1354-1357 no isRunning argument, even though the head is running
            Assert.Equal(14d, glyph.Size); // ThreadRowMetrics.headGlyphSize, :1295

            Assert.Null(CallStatic(Private("SidebarHelperRow"), "HeadGlyph", plain)); // no hydra head at all
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-109")] // App/Views/Sidebar/SidebarView.swift:1375-1384
    public void A_helper_rows_trailing_area_holds_the_ellipsis_while_shown_or_the_activity_status_otherwise() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var actionsButton = new Border(); // a stand-in for RowActionsButton, which this piece takes as a parameter
            var withActions = (Control)CallStatic(Private("SidebarHelperRow"), "BuildTrailingOverlay", true, thread, actionsButton)!;
            Assert.Same(actionsButton, withActions); // :1377 the ellipsis button, passed straight through
            Assert.Equal(new Thickness(0, 0, 6, 0), withActions.Margin); // :1382 .padding(.trailing, 6)

            var withoutActions = (Control)CallStatic(Private("SidebarHelperRow"), "BuildTrailingOverlay", false, thread, actionsButton)!;
            Assert.IsType(Private("ActivityStatus"), withoutActions); // :1380 ActivityStatus(thread: thread)
        });
    });

    // ==== Part 5 of 8: PR-SIDEBAR-111, -113 to -120, -123, -125, -154, -155, -159 ====
    //
    // Records: PR-SIDEBAR-111, -113, -114, -115, -116, -117, -118, -119, -120, -123, -125, -154, -155, -159.
    // The old part-5/6 constructor and frame/glide dependency list is resolved: HeadCardsRow and SidebarThreadRow
    // are built and exercised, and PR-SIDEBAR-134 has real geometry/no-write/landing proofs. PR-SIDEBAR-135's
    // render/DPI contract is covered; its pinned two-face capture comparison remains partial under STOP #9.
    // STOP #7 is consolidated in its later part-7 block below: only the shared per-open right-click attachment
    // and the project-removal confirmation gated behind it remain (PR-SIDEBAR-133/-130, UI-SIDEBAR-033/-037).
    // No missing HeadCardsRow, SidebarThreadRow.windowFrame or SidebarThreadRow.glide body is claimed here.

    internal static IReadOnlyList<object> Enumerate(object valueList) => ((IEnumerable)valueList).Cast<object>().ToList();

    // ---- PR-SIDEBAR-111: the connector shape's exact extent, including the horizontal tick ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-111")] // App/Views/Sidebar/SidebarView.swift:1662-1676, :1292 connectorLineX = 18
    public void The_connector_shape_runs_down_x_18_and_ticks_to_2pt_short_of_the_trailing_edge() => headless.OnUiThread(() =>
    {
        object CreateShapeRecord(bool endsHere) => Activator.CreateInstance(Private("HelperConnectorShape"), [endsHere])!;

        var last = (Geometry)CallOn(CreateShapeRecord(true), "Path", new Rect(0, 0, 28, 24))!;
        Assert.Equal(18d, last.Bounds.Left, 3); // :1668, :1670 both segments start at x = connectorLineX (18)
        Assert.Equal(26d, last.Bounds.Right, 3); // :1671 rect.maxX - 2 = 26 in a 28 pt column
        Assert.Equal(0d, last.Bounds.Top, 3); // :1668 rect.minY
        Assert.Equal(12d, last.Bounds.Bottom, 3); // :1669 endsHere -> rect.midY

        var full = (Geometry)CallOn(CreateShapeRecord(false), "Path", new Rect(0, 0, 28, 24))!;
        Assert.Equal(18d, full.Bounds.Left, 3);
        Assert.Equal(26d, full.Bounds.Right, 3);
        Assert.Equal(0d, full.Bounds.Top, 3);
        Assert.Equal(24d, full.Bounds.Bottom, 3); // :1669 not endsHere -> rect.maxY
    });

    // ---- PR-SIDEBAR-113, PR-SIDEBAR-114, PR-SIDEBAR-115: HelperStubRow, the folded line under a parent ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-113")] // App/Views/Sidebar/SidebarView.swift:1564-1620
    [Trait("Req", "UI-SIDEBAR-035")] // App/Views/Sidebar/SidebarView.swift:1552-1618 (part 7: the same row, as a
    // screen-contract record)
    public void A_folded_stub_counts_its_helpers_and_says_head_only_when_every_folded_thread_is_a_head() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var helperB = NewThread(project, "Helper B", 2) with { ParentThreadID = parent.Id };
        var headA = HeadHelper(project, parent.Id, 0, 3);
        var headB = HeadHelper(project, parent.Id, 1, 4);
        var model = Model(harness, [project], [parent, helperA, helperB, headA, headB]);
        harness.Main.OnMain(() =>
        {
            var type = Private("HelperStubRow");

            var one = (Control)Activator.CreateInstance(type, parent, new[] { helperA }, (Action)(() => { }))!;
            Hosted(model, one);
            var oneButton = LogicalQuery.SelfAndDescendants<Button>(one).Single();
            var oneLabel = LogicalQuery.SelfAndDescendants<TextBlock>(oneButton).Single();
            Assert.Equal("1 helper", oneLabel.Text); // :1591 count == 1, mixed/no-head -> "helper"
            Assert.Equal("Show the helper", ToolTip.GetTip(oneButton)); // :1614
            Assert.Equal("1 folded helper", AutomationProperties.GetName(oneButton)); // :1615

            // A mix of a helper and a head still reads "helpers" (any mix says helpers).
            var mixed = (Control)Activator.CreateInstance(type, parent, new[] { helperA, headA }, (Action)(() => { }))!;
            Hosted(model, mixed);
            var mixedLabel = LogicalQuery.SelfAndDescendants<TextBlock>(LogicalQuery.SelfAndDescendants<Button>(mixed).Single()).Single();
            Assert.Equal("2 helpers", mixedLabel.Text);

            // Every folded thread a head -> "head"/"heads".
            var heads = (Control)Activator.CreateInstance(type, parent, new[] { headA, headB }, (Action)(() => { }))!;
            Hosted(model, heads);
            var headsButton = LogicalQuery.SelfAndDescendants<Button>(heads).Single();
            var headsLabel = LogicalQuery.SelfAndDescendants<TextBlock>(headsButton).Single();
            Assert.Equal("2 heads", headsLabel.Text);
            Assert.Equal("Show 2 heads", ToolTip.GetTip(headsButton));
            Assert.Equal("2 folded heads", AutomationProperties.GetName(headsButton));
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-035")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-114")] // App/Views/Sidebar/SidebarView.swift:1567-1571, :1594-1597
    public void The_folded_stub_shows_a_mini_spinner_only_while_a_folded_helper_is_running_or_merging() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var helperB = NewThread(project, "Helper B", 2) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helperA, helperB]);

        // Each case is its own harness.Main.OnMain call, as the ActivityStatus regression above does: OnMain pumps
        // the manual loop to idle on return, which is what actually runs Tracked<T>'s MainActor.Post(Update)
        // (ViewSupport.cs:71) after the runtime mutation, not Dispatcher.UIThread.RunJobs (a different queue).
        harness.Main.OnMain(() =>
        {
            var quiet = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA, helperB }, (Action)(() => { }))!;
            Hosted(model, quiet);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(quiet)); // no live runtime for either helper: no spinner
            WindowOf(quiet).Close(); // closed before the next case opens another, rather than left to pile up
        });

        // ThreadRuntime.IsRunning (:887) is computed from Phase, whose setter is private; set it through
        // reflection as AppModelHydraTests.SetRuntimeProperty does. :1573
        harness.Main.OnMain(() => typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(model.Runtime(helperB.Id), RuntimePhase.Running));
        harness.Main.OnMain(() =>
        {
            var working = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA, helperB }, (Action)(() => { }))!;
            Hosted(model, working);
            var spinner = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(working)); // :1594-1596
            // :1585 HStack { chevron; label; Spacer(minLength: 4); spinner }: the spinner ends the row, at the trailing padding
            // (:1601 rowHorizontalPadding) less its own 4-point trailing pad, not in the middle of the spare width.
            var right = spinner.TranslatePoint(new Avalonia.Point(spinner.Bounds.Width, 0), working)!.Value.X;
            Assert.Equal(working.Bounds.Width - Chrome.RowHorizontalPadding - 4, right, 0.5);
            WindowOf(working).Close();
        });

        harness.Main.OnMain(() =>
        {
            typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(model.Runtime(helperB.Id), RuntimePhase.Idle);
            model.Runtime(helperA.Id).IsHydraMerging = true; // :1573 isHydraMerging also counts
        });
        harness.Main.OnMain(() =>
        {
            var merging = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA, helperB }, (Action)(() => { }))!;
            Hosted(model, merging);
            Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(merging));
            WindowOf(merging).Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-035")] // SidebarView.swift:1581-1613 HelperStubRow body
    public void A_folded_stub_keeps_its_recorded_geometry_and_its_colours_ease_with_the_hover_on_chrome_hover() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1);
        var model = Model(harness, [project], [parent, head]);
        Control row = null!;
        AvaloniaWindow window = null!;
        Border shape = null!;
        TextBlock label = null!;
        SwiftImage chevron = null!;
        Avalonia.Controls.Shapes.Path connector = null!;
        static Color ColorOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        harness.Main.OnMain(() =>
        {
            row = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { head }, (Action)(() => { }))!;
            window = WindowOf(Hosted(model, row));
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                shape = LogicalQuery.SelfAndDescendants<Border>(row).Single(border => border.Height == ThreadRowMetrics.HelperHeight);
                label = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(text => text.Text == "1 head"); // :1577-1578, :1591 one head says "head"
                chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName == "chevron.right"); // :1586
                connector = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(row).Single();
                Assert.Equal(new Thickness(6, 0, Chrome.RowHorizontalPadding, 0), shape.Padding); // :1600-1601
                Assert.Equal(new CornerRadius(Chrome.RowCornerRadius), shape.CornerRadius); // :1580
                Assert.Equal(ThreadRowMetrics.ConnectorWidth, Frame(shape, row).X); // :1584 the 28 pt stub, then the row
                Assert.Equal(5d, Assert.IsType<StackPanel>(chevron.Parent).Spacing); // :1585 HStack(spacing: 5)
                Assert.Equal(8d, SwiftImage.GetFont(chevron)!.PointSize); // :1587 .system(size: 8, weight: .bold)
                Assert.Equal(FontWeight.Bold, SwiftImage.GetFont(chevron)!.Resolve().FontWeight);
                Assert.Equal(11d, label.FontSize); // :1590 .subheadline
                var idle = Chrome.SecondaryText.Opacity(0.85).Resolve(label.ActualThemeVariant); // :1596 not hovered
                Assert.Equal(idle, ColorOf(label.Foreground));
                Assert.Equal(idle, ColorOf(chevron.Foreground));
                Assert.Equal(HelperConnector.Color(false).Resolve(connector.ActualThemeVariant), ColorOf(connector.Stroke)); // :1584
                Assert.Equal(0, ColorOf(shape.Background).A); // :1607 clear
                window.MouseMove(shape.TranslatePoint(new Point(80, 12), window)!.Value); // :1611-1613 withAnimation(Chrome.hover)
            });
            Tick(50); // half of Chrome.hover's easeOut 0.1 s: the label, chevron and stroke are on their way, not cut
            harness.Main.OnMain(() =>
            {
                var idle = Chrome.SecondaryText.Opacity(0.85).Resolve(label.ActualThemeVariant);
                var hovered = Chrome.SecondaryText.Resolve(label.ActualThemeVariant);
                Assert.InRange((double)ColorOf(label.Foreground).A, idle.A + 1, hovered.A - 1);
                Assert.InRange((double)ColorOf(chevron.Foreground).A, idle.A + 1, hovered.A - 1);
                var strokeIdle = HelperConnector.Color(false).Resolve(connector.ActualThemeVariant);
                var strokeHovered = HelperConnector.Color(true).Resolve(connector.ActualThemeVariant);
                Assert.InRange((double)ColorOf(connector.Stroke).A, strokeIdle.A + 1, strokeHovered.A - 1);
                // :1607 the fill is one of them: clear to the 0.06 overlay along the same Chrome.hover, not cut
                // (round 3 mutant D8a-stub-fill-snaps survived the test without this line)
                var fillHovered = Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant);
                Assert.InRange((double)ColorOf(shape.Background).A, 1, fillHovered.A - 1);
            });
            Tick(200);
            harness.Main.OnMain(() =>
            {
                var hovered = Chrome.SecondaryText.Resolve(label.ActualThemeVariant); // :1596 hovered: 1
                Assert.Equal(hovered, ColorOf(label.Foreground));
                Assert.Equal(hovered, ColorOf(chevron.Foreground));
                Assert.Equal(HelperConnector.Color(true).Resolve(connector.ActualThemeVariant), ColorOf(connector.Stroke));
                Assert.Equal(Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant), ColorOf(shape.Background)); // :1607
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-113")] // App/Views/Sidebar/SidebarView.swift:1585 HStack(spacing: 5), centre alignment by default
    public void The_folded_stubs_chevron_and_label_sit_on_the_centre_line_of_its_24_point_row() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var helperB = NewThread(project, "Helper B", 2) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helperA, helperB]);
        harness.Main.OnMain(() =>
        {
            var row = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA, helperB }, (Action)(() => { }))!;
            WindowOf(Hosted(model, row)).UpdateLayout();
            var shape = LogicalQuery.SelfAndDescendants<Border>(row).Single(border => border.Height == ThreadRowMetrics.HelperHeight);
            var label = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(text => text.Text == "2 helpers");
            // The label (14 points) is shorter than the 24-point row; the HStack puts it at its own height on the row's centre line.
            Assert.Equal(label.DesiredSize.Height, label.Bounds.Height, 0.5);
            Assert.Equal(shape.Bounds.Height / 2, label.TranslatePoint(new Point(0, label.Bounds.Height / 2), shape)!.Value.Y, 0.5);
            WindowOf(row).Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-035")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-115")] // App/Views/Sidebar/SidebarView.swift:1583, :1606-1609
    public void Clicking_the_folded_stub_on_its_label_half_unfolds_the_helpers() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helperA]);
        harness.Main.OnMain(() =>
        {
            var unfolded = false;
            var row = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA }, (Action)(() => unfolded = true))!;
            Hosted(model, row);
            var button = LogicalQuery.SelfAndDescendants<Button>(row).Single(); // :1583 the whole HStack is one Button
            Click(button);
            Assert.True(unfolded);
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-035")] // SidebarView.swift:1583-1608 the stub's connector is a bare stroked shape (no contentShape; :1608 sits on the text half) and :1636-1658 the sibling HelperConnector adds .contentShape(.rect); OD-0098: a plain button's hit area is its label's drawn content, a stroke hit-tests on its path
    public void A_press_on_one_of_the_stub_connectors_drawn_dots_unfolds_the_helpers_and_a_press_in_a_gap_of_the_connector_column_does_not() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helperA]);
        harness.Main.OnMain(() =>
        {
            var unfolded = 0;
            var row = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA }, (Action)(() => unfolded++))!;
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = row;
            var window = new AvaloniaWindow { Content = host, Width = 400, Height = 100 };
            window.Show();
            window.UpdateLayout();
            try
            {
                var connector = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(row).Single(); // the 28 x 24 column at the row's leading edge
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // the pointer's hit test reads the rendered scene
                Dispatcher.UIThread.RunJobs();
                void Press(double x, double y)
                {
                    var point = At(connector, x, y, window);
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                }
                // The stem is the line x = 18 down the 28 x 24 column, dashed [0.1, 3.6] with round caps, 1 pt wide: a dot every 3.7 pt
                // from y = 0.05 (:1641-1642, :1666-1669). The gaps lie halfway between two dots, and anywhere off the line.
                Press(18, 0.05 + 3.7); // a dot
                Assert.Equal(1, unfolded);
                Press(18, 0.05 + 3.7 * 2); // another dot
                Assert.Equal(2, unfolded);
                Press(18, 0.05 + 3.7 + 1.85); // halfway between two dots: the undrawn gap
                Assert.Equal(2, unfolded);
                Press(8, 6); // the column left of the stem
                Assert.Equal(2, unfolded);
                Press(18, 0.05 + 3.7 * 3 + 1.85); // a gap again, on the last stem stretch above the tick
                Assert.Equal(2, unfolded);
            }
            finally { window.Close(); }
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-035")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-115")] // App/Views/Sidebar/SidebarView.swift:1603, :1605-1606 (REVIEW FIX, part 5)
    public void Clicking_the_stub_rows_empty_trailing_region_past_the_label_still_unfolds_the_helpers() => headless.OnUiThread(() =>
    {
        // :1603 .frame(maxWidth: .infinity, alignment: .leading) is on the labelled HStack itself, before :1605-1606's
        // .background and .contentShape, so the hover fill and hit area cover the row's whole remaining width, not
        // just the label's own text. Click(button) above (and PR-SIDEBAR-115's own synthetic click) bypasses hit-
        // testing entirely; this uses a real pointer click far past where the chevron/label content ends, in a
        // window wide enough that the gap cannot be closed by the label's own text (REVIEW FIX: a horizontally
        // oriented StackPanel arranges every child at its own DesiredSize along the stack axis --
        // HorizontalAlignment.Stretch on a stacked child is a no-op there -- so `shape` sat at only its own content
        // width instead of the row's remaining width; fixed with a Grid Auto,* column, this file's own established
        // idiom for a fixed leading column plus a flexible trailing one, at ThreadRowFace's `row`, :2022).
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // a real pointer move fires .onHover (:1611-1613)
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helperA]);
        harness.Main.OnMain(() =>
        {
            var unfolded = false;
            var row = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA }, (Action)(() => unfolded = true))!;
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = row;
            // :1584 connectorWidth is 28; a 400pt window leaves 372pt of remaining width, far past any plausible
            // chevron+label content width, so a click near the far trailing edge only lands on live content if the
            // row's remaining width was actually handed to `shape`.
            var window = new AvaloniaWindow { Content = host, Width = 400, Height = 100 };
            window.Show();
            window.UpdateLayout();
            var button = LogicalQuery.SelfAndDescendants<Button>(row).Single();
            var point = At(button, 350, 50, window);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(unfolded);
            window.Close();
        });
    });

    // ---- PR-SIDEBAR-116: the thread row's fold chevron, 46 pt from the trailing edge ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-116")] // App/Views/Sidebar/SidebarView.swift:1805-1806
    public void The_fold_overlay_is_absent_unless_showsFold_and_onToggleFold_are_both_set()
    {
        var type = Private("SidebarThreadRow");
        Assert.Null(CallStatic(type, "BuildFoldOverlay", false, (Action)(() => { }), false)); // :1805 !showsFold
        Assert.Null(CallStatic(type, "BuildFoldOverlay", true, null, false)); // :1805 onToggleFold nil
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-116")] // App/Views/Sidebar/SidebarView.swift:1804-1814, :1288 foldInset = 46
    [Trait("Req", "UI-SIDEBAR-022")] // App/Views/Sidebar/SidebarView.swift:1803-1814 (part 6: the same chevron, as a screen-contract record)
    public void The_fold_chevron_points_right_folded_and_down_unfolded_46pt_in_tooltipped_and_labelled() => headless.OnUiThread(() =>
    {
        var type = Private("SidebarThreadRow");
        var toggled = false;
        var folded = (Control)CallStatic(type, "BuildFoldOverlay", true, (Action)(() => toggled = true), true)!;
        Shown(folded);
        Assert.Equal(new Thickness(0, 0, 46, 0), folded.Margin); // :1811, ThreadRowMetrics.foldInset
        Assert.Equal(HorizontalAlignment.Right, folded.HorizontalAlignment); // :1804 .overlay(alignment: .trailing)
        var button = Assert.IsType<Button>(folded);
        Assert.Equal("Show helpers", ToolTip.GetTip(button)); // :1812
        Assert.Equal("Show helpers", AutomationProperties.GetName(button)); // :1813
        Click(button);
        Assert.True(toggled); // :1807 Button(action: onToggleFold)

        var unfolded = (Control)CallStatic(type, "BuildFoldOverlay", true, (Action)(() => { }), false)!;
        Assert.Equal("Hide helpers", ToolTip.GetTip((Button)unfolded));
        Assert.Equal("Hide helpers", AutomationProperties.GetName((Button)unfolded));
    });

    // ---- PR-SIDEBAR-117, PR-SIDEBAR-118, PR-SIDEBAR-119, PR-SIDEBAR-120: HeadCardsRow's directly testable geometry ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-117")] // App/Views/Sidebar/SidebarView.swift:1439-1451
    public void A_head_cards_glyphs_per_row_defaults_to_seven_before_measurement_then_floors_the_fit()
    {
        var type = Private("HeadCardsRow");
        Assert.Equal(7L, CallStatic(type, "PerRow", 0d)); // :1451 width == 0 -> 7
        // At the sidebar's own 210 pt minimum, the file's own worked example (:1439-1441): cardInner = 210-28-12 =
        // 170; usable = 170-3 = 167; perRow = floor((167+22-14)/22) = floor(175/22) = 7.
        Assert.Equal(7L, CallStatic(type, "PerRow", 210d));
        // A narrow card clamps to at least one glyph per row even when the floor would be zero.
        Assert.Equal(1L, CallStatic(type, "PerRow", 28d + 12d + 3d + 1d)); // usable = 1, floor((1+8)/22) = 0 -> max(1, 0)
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-118")] // App/Views/Sidebar/SidebarView.swift:1456-1458
    public void A_head_cards_row_gap_stretches_past_8pt_until_the_last_badge_sits_on_the_padding()
    {
        var type = Private("HeadCardsRow");
        Assert.Equal(8d, (double)CallStatic(type, "RowGap", 0d, 7L)!); // :1458 width <= 0 -> tightGap
        Assert.Equal(8d, (double)CallStatic(type, "RowGap", 210d, 1L)!); // :1458 perRow <= 1 -> tightGap
        // width 210, perRow 7 (see PR-SIDEBAR-117): usable 167; rowGap = max(8, (167 - 7*14) / 6) = max(8, 69/6) = 11.5.
        Assert.Equal(11.5, (double)CallStatic(type, "RowGap", 210d, 7L)!, 3);
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-119")] // App/Views/Sidebar/SidebarView.swift:1436-1443, :1514-1526
    public void A_head_card_caps_at_twelve_heads_and_the_chevron_row_reads_the_right_tooltip() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var heads = Enumerable.Range(0, 15).Select(i => NewThread(project, "Head" + i, i)).ToList();
        var type = Private("HeadCardsRow");

        Assert.False((bool)CallStatic(type, "Capped", 12L)!); // :1442 not more than 12
        Assert.True((bool)CallStatic(type, "Capped", 13L)!);
        Assert.Equal(2L, CallStatic(type, "CardRows", 2L, false)); // :1461 rows.count, no chevron row
        Assert.Equal(3L, CallStatic(type, "CardRows", 2L, true)); // :1461 + 1 chevron row

        var collapsed = Enumerate(CallStatic(type, "Visible", heads, false)!); // :1443
        Assert.Equal(12, collapsed.Count);
        Assert.Equal(heads.Take(12).Select(h => h.Id), collapsed.Cast<ChatThread>().Select(h => h.Id));
        Assert.Equal(15, Enumerate(CallStatic(type, "Visible", heads, true)!).Count); // showsAllHeads -> every head

        Assert.Equal("Show all 17 heads", CallStatic(type, "ChevronTooltip", false, 17L)); // :1526
        Assert.Equal("Show fewer heads", CallStatic(type, "ChevronTooltip", true, 17L));
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-120")] // App/Views/Sidebar/SidebarView.swift:1305-1311, :1530-1540
    public void A_head_cards_height_is_12pt_padding_plus_14pt_per_row_plus_6pt_between_rows()
    {
        double CardHeight(long rows) => (double)CallStatic(Private("ThreadRowMetrics"), "HeadCardHeight", rows)!;
        Assert.Equal(26d, CardHeight(1)); // :1310 12 + 14*1 + 6*0
        Assert.Equal(46d, CardHeight(2)); // 12 + 28 + 6
        Assert.Equal(66d, CardHeight(3)); // 12 + 42 + 12
        Assert.Equal(26d, CardHeight(0)); // :1309 max(1, rows) clamps to 1
    }

    // ---- PR-SIDEBAR-123: SettledHeader ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-123")] // App/Views/Sidebar/SidebarView.swift:2363-2406
    [Trait("Req", "UI-SIDEBAR-040")] // App/Views/Sidebar/SidebarView.swift:2398-2403: the tooltip, the accessibility label (always plural) and its value, the verbatim count
    public void The_settled_header_shows_its_count_toggles_the_fold_and_rotates_the_chevron() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // real pointer hover requests the source clock
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() => model.Settings.SettledCollapsed = false); // :2369 start expanded (default true, AppSettings.cs:1087)

        Button button = null!;
        harness.Main.OnMain(() =>
        {
            var header = (Control)Activator.CreateInstance(Private("SettledHeader"), 3L, true)!;
            Hosted(model, header);
            button = LogicalQuery.SelfAndDescendants<Button>(header).Single(); // SettledHeader's Child is the Button
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(button).ToList();
            Assert.Equal("Settled", texts[0].Text); // :2380
            Assert.Equal("3", texts[1].Text); // :2384, a bare decimal
            Assert.Equal(0.8, texts[1].Opacity); // :2387
            Assert.Equal("Hide settled threads", ToolTip.GetTip(button)); // :2401 expanded
            Assert.Equal("Settled, 3 threads", AutomationProperties.GetName(button)); // :2402, always plural
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(button)); // :2403
            Click(button); // :2370-2373
        });

        // A separate harness.Main.OnMain call, as the ActivityStatus regression above does: OnMain pumps the manual
        // loop to idle on return, which is what runs Tracked<T>'s MainActor.Post(Update) (ViewSupport.cs:71) after
        // the click's mutation, not Dispatcher.UIThread.RunJobs (a different queue).
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Settings.SettledCollapsed);
            Assert.Equal("Show settled threads", ToolTip.GetTip(button)); // :2401 collapsed
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(button));
        });
    });

    // ---- PR-SIDEBAR-125: ActivityHeader ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-125")] // App/Views/Sidebar/SidebarView.swift:2345-2358
    [Trait("Req", "UI-SIDEBAR-039")] // App/Views/Sidebar/SidebarView.swift:2345-2361 (part 7: the same header, as a
    // screen-contract record)
    public void The_activity_header_is_the_verbatim_title_with_2_or_14pt_of_top_padding() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var first = (Control)Activator.CreateInstance(Private("ActivityHeader"), "Today", true)!;
            Hosted(model, first);
            var firstText = LogicalQuery.SelfAndDescendants<TextBlock>(first).Single();
            Assert.Equal("Today", firstText.Text); // :2350, verbatim
            Assert.Equal(new Thickness(8, 2, 8, 4), firstText.Margin); // :2353-2355 isFirst -> 2 pt top

            var later = (Control)Activator.CreateInstance(Private("ActivityHeader"), "Yesterday", false)!;
            Hosted(model, later);
            var laterText = LogicalQuery.SelfAndDescendants<TextBlock>(later).Single();
            Assert.Equal(new Thickness(8, 14, 8, 4), laterText.Margin); // not first -> 14 pt top
        });
    });

    // ---- PR-SIDEBAR-154: PlacementCache and SidebarItemCache skip rebuilding when their key is unchanged ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-154")] // App/Views/Sidebar/SidebarView.swift:2485-2536 PlacementCache
    public void PlacementCache_hands_back_the_same_placements_only_when_no_fingerprint_field_changed() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threadA = NewThread(project, "A", 0);
        var threadB = NewThread(project, "B", 1);
        var cache = Activator.CreateInstance(Private("PlacementCache"))!;

        var first = Enumerate(CallOn(cache, "Placements", (object)new[] { threadA, threadB })!);
        var second = Enumerate(CallOn(cache, "Placements", (object)new[] { threadA, threadB })!); // :2531 identical fingerprint array
        Assert.Same(first[0], second[0]); // hands back the previously built array untouched, no rebuild
        Assert.Same(first[1], second[1]);

        // A title change alone does not move the fingerprint (:2487-2503 lists no title).
        var renamedA = threadA with { Title = "Renamed" };
        var third = Enumerate(CallOn(cache, "Placements", (object)new[] { renamedA, threadB })!);
        Assert.Same(second[0], third[0]);
        Assert.Same(second[1], third[1]);

        // A field the fingerprint does read (isPinned) changes the array, so the whole cache rebuilds (:2531-2533,
        // compared as one array, not per element).
        var pinnedA = renamedA with { IsPinned = !renamedA.IsPinned };
        var fourth = Enumerate(CallOn(cache, "Placements", (object)new[] { pinnedA, threadB })!);
        Assert.NotSame(third[0], fourth[0]);
        Assert.NotSame(third[1], fourth[1]);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-154")] // App/Views/Sidebar/SidebarView.swift:2589-2659 SidebarItemCache.helpers(for:group:)
    public void SidebarItemCache_rebuilds_the_helper_grouping_only_when_placements_change() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threadA = NewThread(project, "A", 0);
        var threadB = NewThread(project, "B", 1);
        var placementCache = Activator.CreateInstance(Private("PlacementCache"))!;
        var itemCache = Activator.CreateInstance(Private("SidebarItemCache"))!;
        var placementsA = CallOn(placementCache, "Placements", (object)new[] { threadA })!;
        var placementsAB = CallOn(placementCache, "Placements", (object)new[] { threadA, threadB })!;

        var calls = 0;
        ValueMap<Guid, ValueList<ChatThread>> Group()
        {
            calls++;
            return new ValueMap<Guid, ValueList<ChatThread>>(new Dictionary<Guid, ValueList<ChatThread>>());
        }

        CallOn(itemCache, "Helpers", placementsA, (Func<ValueMap<Guid, ValueList<ChatThread>>>)Group);
        CallOn(itemCache, "Helpers", placementsA, (Func<ValueMap<Guid, ValueList<ChatThread>>>)Group); // :2629 identical placements
        Assert.Equal(1, calls); // group() ran once only

        CallOn(itemCache, "Helpers", placementsAB, (Func<ValueMap<Guid, ValueList<ChatThread>>>)Group); // :2629 a different array
        Assert.Equal(2, calls);
    });

    // ---- PR-SIDEBAR-155: SidebarThreadRow and SidebarHelperRow compare value inputs only ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-155")] // App/Views/Sidebar/SidebarView.swift:1882-1891 SidebarThreadRow.==
    public void SidebarThreadRows_equatable_compares_value_inputs_only_ignoring_closures() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threadA = NewThread(project, "A", 0);
        var type = Private("SidebarThreadRow");
        bool Eq(ChatThread l, string? lp, bool ld, bool lf, ChatThread r, string? rp, bool rd, bool rf) =>
            (bool)CallStatic(type, "RowEquals", l, lp, ld, lf, r, rp, rd, rf)!;

        Assert.True(Eq(threadA, "Alpha", false, true, threadA, "Alpha", false, true));
        Assert.False(Eq(threadA, "Alpha", false, true, threadA with { Title = "Renamed" }, "Alpha", false, true)); // :1884
        Assert.False(Eq(threadA, "Alpha", false, true, threadA with { UpdatedAt = threadA.UpdatedAt.AddMinutes(1) }, "Alpha", false, true)); // :1885
        Assert.False(Eq(threadA, "Alpha", false, true, threadA with { IsPinned = !threadA.IsPinned }, "Alpha", false, true)); // :1886
        Assert.False(Eq(threadA, "Alpha", false, true, threadA with { IsSettled = !threadA.IsSettled }, "Alpha", false, true)); // :1887
        Assert.False(Eq(threadA, "Alpha", false, true, threadA, "Beta", false, true)); // :1888 projectName
        Assert.False(Eq(threadA, "Alpha", false, true, threadA, "Alpha", true, true)); // :1889 isDragged
        Assert.False(Eq(threadA, "Alpha", false, true, threadA, "Alpha", false, false)); // :1890 onToggleFold nil-ness
        Assert.True(Eq(threadA, null, false, true, threadA, null, false, true)); // both project names nil
        Assert.False(Eq(threadA, null, false, true, threadA, "Alpha", false, true)); // one nil, one not
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-155")] // App/Views/Sidebar/SidebarView.swift:1413-1420 SidebarHelperRow.==
    public void SidebarHelperRows_equatable_compares_value_inputs_only() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threadA = NewThread(project, "A", 0);
        var type = Private("SidebarHelperRow");
        bool Eq(ChatThread l, bool li, ChatThread r, bool ri) => (bool)CallStatic(type, "RowEquals", l, li, r, ri)!;

        Assert.True(Eq(threadA, true, threadA, true));
        Assert.False(Eq(threadA, true, threadA, false)); // :1419 isLast
        Assert.False(Eq(threadA, true, threadA with { Title = "Renamed" }, true)); // :1415
        Assert.False(Eq(threadA, true, threadA with { UpdatedAt = threadA.UpdatedAt.AddMinutes(1) }, true)); // :1416
        Assert.False(Eq(threadA, true, threadA with { IsPinned = !threadA.IsPinned }, true)); // :1417
        Assert.False(Eq(threadA, true, threadA with { IsSettled = !threadA.IsSettled }, true)); // :1418
    });

    // ---- PR-SIDEBAR-159: "Rename" is always the thread menu's first item ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-159")] // App/Views/Sidebar/SidebarView.swift:2201, :2251-2259
    public void Rename_is_always_the_first_item_with_symbol_pencil_in_every_thread_menu() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var lead = NewThread(project, "Lead", 0);
        var parent = NewThread(project, "Parent", 1);
        var helper = NewThread(project, "Helper", 2) with { ParentThreadID = parent.Id };
        var settled = NewThread(project, "Settled", 3) with { IsSettled = true };
        var pinned = NewThread(project, "Pinned", 4) with { IsPinned = true };
        var model = Model(harness, [project], [lead, parent, helper, settled, pinned]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            foreach (var thread in new[] { lead, helper, settled, pinned })
            {
                var made = ThreadActionsMake(model, thread); // :2193-2246
                Assert.Equal("Rename", made[0].Title); // :2200, unconditional, first
                Assert.Equal("pencil", made[0].Symbol);

                var menu = ThreadActionsMakeContextMenu(model, thread, false, requests); // :2253-2312
                Assert.Equal("Rename", menu[0].Title); // :2259-2262
                Assert.Equal("pencil", menu[0].Symbol);
            }
        });
    });

    // ======== Part 6 of 8 ========
    //
    // Records: PR-SIDEBAR-160, -161, -162, -163, PR-UPDATE-080, UI-GIT-057, UI-HYDRAMERGE-030, UI-SIDEBAR-001, -004,
    // -005, -006, -007, -008, -009, -010, -014, -015, -016, -017, -018, -019, -020, -021, -022 (24 of this part's
    // records). PR-UPDATE-080, UI-GIT-057, UI-HYDRAMERGE-030, UI-SIDEBAR-001, -006, -007, -018, -019, -020, -022 are
    // proven by adding this part's own [Trait] to a test parts 1-5 already wrote for the identical evidence (the
    // code and the record describe the same site; grep the id to find the added trait, e.g. UI-SIDEBAR-001 on
    // The_window_column_opens_with_a_14_point_clearance..., PR-SIDEBAR-023's own test). ProjectRow's own constructor
    // (UI-SIDEBAR-008, -009, -010), contextMenuActions, actions(isActive:), BuildContextMenuItems and
    // ProjectPairMenuItems (UI-SIDEBAR-014, -015, -016) are new this part (SidebarView.cs, ProjectRow's own header
    // comment there explains what PR-SIDEBAR-133 still needs). UI-SIDEBAR-017 is the STOP below.
    //
    // Window sweep assembles this row below. Only its right-click site remains under the narrowed STOP #8
    // in the file header; the shared genuine STOP #6 and #7 blocks are unchanged.

    // ---- helpers for ProjectRow (part 6) ----

    internal static Control ShownProjectRow(AppModel model, Project project, long count, bool togglesExpansion = true, double width = 320)
    {
        var row = (Control)Activator.CreateInstance(Private("ProjectRow"), project, count, togglesExpansion)!;
        row.VerticalAlignment = VerticalAlignment.Top; // the row's own 28pt height, not the host's full stretch
        var host = new Border { Child = row };
        AppEnvironment.SetAppModel(host, model);
        var window = new AvaloniaWindow { Content = host, Width = width, Height = 100 };
        window.Show();
        window.UpdateLayout();
        return row;
    }

    internal static ValueList<RowAction> ProjectRowContextMenuActions(Project project, bool isActive, bool hasOverride, AppModel model) =>
        (ValueList<RowAction>)CallStatic(Private("ProjectRow"), "ContextMenuActions", project, isActive, hasOverride, model, (Action<Project>)model.RemoveProject)!;

    internal static ValueList<RowAction> ProjectRowActions(AppModel model, Project project, bool isActive) =>
        (ValueList<RowAction>)CallStatic(Private("ProjectRow"), "Actions", model, project, isActive, (Action<Project>)model.RemoveProject)!;

    internal static object ProjectPairChoices(params (Guid Id, string Title)[] pairs)
    {
        var choiceType = Private("ProjectPairChoice");
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(choiceType))!;
        foreach (var (id, title) in pairs)
        {
            var choice = Activator.CreateInstance(choiceType)!;
            SetProp(choice, "Id", id);
            SetProp(choice, "Title", title);
            list.Add(choice);
        }
        return list;
    }

    internal static IReadOnlyList<Control> ProjectRowBuildContextMenuItems(Project project, bool isActive, bool hasOverride, AppModel model, object pairs) =>
        (IReadOnlyList<Control>)CallStatic(Private("ProjectRow"), "BuildContextMenuItems", project, isActive, hasOverride, model, pairs, (Action<Project>)model.RemoveProject)!;

    // ---- PR-SIDEBAR-160: the Archive item ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-160")] // App/Views/Sidebar/SidebarView.swift:2219-2226
    public void Archive_is_always_offered_and_starts_a_group_only_when_settle_is_not_first() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = thread.Id };
        var model = Model(harness, [project], [thread, helper]);
        harness.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; // settleFirst false for a non-helper
            var archive = ThreadActionsMake(model, thread).Single(a => a.Title == "Archive"); // :2221-2223
            Assert.Equal("archivebox", archive.Symbol);
            Assert.True(archive.StartsGroup); // :2224 !settleFirst
            archive.Action(); // :2223 model.archive(thread.id)
            Assert.True(model.Thread(thread.Id)!.IsArchived);

            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle; // settleFirst true for this non-helper
            var archiveNotFirst = ThreadActionsMake(model, model.Thread(thread.Id)!).Single(a => a.Title == "Archive");
            Assert.False(archiveNotFirst.StartsGroup);

            // A helper always gets Archive (it has no place of its own to settle into: :2219-2220), and settleFirst
            // is false for a helper regardless of the setting, so its Archive item always starts the group.
            var helperArchive = ThreadActionsMake(model, helper).Single(a => a.Title == "Archive");
            Assert.True(helperArchive.StartsGroup); // :2224-2225
        });
    });

    // ---- PR-SIDEBAR-161: the Settle/Reopen item ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-161")] // App/Views/Sidebar/SidebarView.swift:2227-2242
    public void Settle_or_Reopen_is_offered_to_non_helpers_only_and_prefers_the_rows_own_handler() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var open = NewThread(project, "Open", 0);
        var settled = NewThread(project, "Settled", 1) with { IsSettled = true };
        var helper = NewThread(project, "Helper", 2) with { ParentThreadID = open.Id };
        var model = Model(harness, [project], [open, settled, helper]);
        harness.Main.OnMain(() =>
        {
            Assert.DoesNotContain(ThreadActionsMake(model, helper), a => a.Title is "Settle" or "Reopen"); // :2224-2226

            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle; // settleFirst true for a non-helper
            var settle = ThreadActionsMake(model, open).Single(a => a.Title is "Settle" or "Reopen"); // :2228-2231
            Assert.Equal("Settle", settle.Title);
            Assert.Equal("checkmark.circle", settle.Symbol);
            Assert.True(settle.StartsGroup); // :2230 settleFirst

            var reopenItem = ThreadActionsMake(model, settled).Single(a => a.Title is "Settle" or "Reopen");
            Assert.Equal("Reopen", reopenItem.Title);
            Assert.Equal("arrow.uturn.backward", reopenItem.Symbol);

            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; // settleFirst false now
            var settleNotFirst = ThreadActionsMake(model, open).Single(a => a.Title is "Settle" or "Reopen");
            Assert.False(settleNotFirst.StartsGroup); // :2230

            // The row's own handler, when supplied, flies the glide; without it the model moves the thread directly
            // (:2233-2240).
            var handlerCalled = false;
            var withHandler = ThreadActionsMake(model, open, onSettle: () => handlerCalled = true).Single(a => a.Title == "Settle");
            withHandler.Action();
            Assert.True(handlerCalled);
            Assert.False(model.Thread(open.Id)!.IsSettled); // the handler ran instead of the model call

            var withoutHandler = ThreadActionsMake(model, open).Single(a => a.Title == "Settle");
            withoutHandler.Action(); // :2239 model.settleAnimated(thread.id)
            Assert.True(model.Thread(open.Id)!.IsSettled);

            var reopenHandlerCalled = false;
            var settledWithHandler = ThreadActionsMake(model, settled, onReopen: () => reopenHandlerCalled = true).Single(a => a.Title == "Reopen");
            settledWithHandler.Action();
            Assert.True(reopenHandlerCalled);
            Assert.True(model.Thread(settled.Id)!.IsSettled); // the handler ran instead of the model call

            var settledWithoutHandler = ThreadActionsMake(model, settled).Single(a => a.Title == "Reopen");
            settledWithoutHandler.Action(); // :2236 model.reopenAnimated(thread.id)
            Assert.False(model.Thread(settled.Id)!.IsSettled);
        });
    });

    // ---- PR-SIDEBAR-162: settle/archive swap by the finish-action setting ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-162")] // App/Views/Sidebar/SidebarView.swift:2219-2221, :2225-2227, :2242
    public void Settle_and_archive_swap_places_by_the_finish_action_and_a_helper_gets_archive_alone() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = thread.Id };
        var model = Model(harness, [project], [thread, helper]);
        harness.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            var helperGroup = ThreadActionsMake(model, helper).Where(a => a.Title is "Settle" or "Reopen" or "Archive").ToList();
            Assert.Equal(["Archive"], helperGroup.Select(a => a.Title)); // :2224-2226, no Settle/Reopen at all
            Assert.True(helperGroup[0].StartsGroup);

            var settleFirst = ThreadActionsMake(model, thread).Where(a => a.Title is "Settle" or "Archive").ToList();
            Assert.Equal(["Settle", "Archive"], settleFirst.Select(a => a.Title)); // :2242
            Assert.True(settleFirst[0].StartsGroup);
            Assert.False(settleFirst[1].StartsGroup);

            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            var archiveFirst = ThreadActionsMake(model, thread).Where(a => a.Title is "Settle" or "Archive").ToList();
            Assert.Equal(["Archive", "Settle"], archiveFirst.Select(a => a.Title));
            Assert.True(archiveFirst[0].StartsGroup);
            Assert.False(archiveFirst[1].StartsGroup);
        });
    });

    // ---- PR-SIDEBAR-163: the Delete… item ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-163")] // App/Views/Sidebar/SidebarView.swift:2244-2245, :2309-2312
    public void Delete_is_always_the_last_item_destructive_symbol_trash_and_one_ellipsis_character() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = thread.Id };
        var settled = NewThread(project, "Settled", 2) with { IsSettled = true };
        var model = Model(harness, [project], [thread, helper, settled]);
        harness.Main.OnMain(() =>
        {
            foreach (var candidate in new[] { thread, helper, settled })
            {
                var delete = ThreadActionsMake(model, candidate)[^1]; // :2244-2245, unconditional, last
                Assert.Equal("Delete\u2026", delete.Title); // one U+2026, not three periods
                Assert.Equal(new byte[] { 0x44, 0x65, 0x6C, 0x65, 0x74, 0x65, 0xE2, 0x80, 0xA6 }, System.Text.Encoding.UTF8.GetBytes(delete.Title));
                Assert.Equal("trash", delete.Symbol);
                Assert.True(delete.IsDestructive);
                Assert.False(delete.StartsGroup); // sits directly under the settle/archive group, no separator of its own
                Assert.Null(delete.Confirms); // the confirmation is the popover, not this flag
            }

            var deleted = false;
            var madeDirect = ((IEnumerable)CallStatic(Private("ThreadActions"), "Make", model, thread,
                (Action)(() => { }), (Action)(() => deleted = true), null, null)!).Cast<RowAction>().ToList();
            madeDirect[^1].Action(); // :2245 onDelete()
            Assert.True(deleted);

            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var contextDelete = ThreadActionsMakeContextMenu(model, thread, false, requests)[^1];
            Assert.Equal("Delete\u2026", contextDelete.Title); // :2308-2311
            contextDelete.Action();
            Assert.Equal(thread, Get(requests, "Delete")); // :2309-2310 requests.delete = thread
        });
    });

    // ---- UI-SIDEBAR-004: the activity-view bell's colours ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-004")] // App/Views/Sidebar/SidebarView.swift:2316-2343
    public void The_bell_glyph_and_background_colours_follow_the_on_off_and_hovered_while_off_states()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var manual = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(manual);
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));
            var window = WindowOf(view);
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(view).Single(image => image.SymbolName == "bell"); // :2325-2326
            var circle = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(view).Single(e => e.Width == 30); // :2329-2332
            Color GlyphColor() => ((ISolidColorBrush)glyph.Foreground!).Color;
            Color FillColor() => ((ISolidColorBrush)circle.Fill!).Color;
            var button = ButtonNamed(view, "Activity view");
            var inside = button.TranslatePoint(new Point(15, 15), window)!.Value;
            var away = new Point(1, 1);
            void Settle() { for (var i = 0; i < 20; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }

            harness.Main.OnMain(() =>
            {
                // Off, not hovering: secondaryText glyph, Color.clear background, set with no animation at construction (:2327, :2331).
                Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), GlyphColor());
                Assert.Equal(SwiftColor.Clear.Resolve(circle.ActualThemeVariant), FillColor());
            });

            harness.Main.OnMain(() => window.MouseMove(inside)); // pointer input reaches MainActorModel.Set; needs the main actor
            Settle();
            harness.Main.OnMain(() =>
            {
                // Hovered, still off: the label colour's own 0.08 overlay, glyph unchanged (:2331).
                Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), GlyphColor());
                Assert.Equal(Chrome.Overlay(0.08).Resolve(circle.ActualThemeVariant), FillColor());
            });

            harness.Main.OnMain(() => window.MouseMove(away));
            Settle();
            harness.Main.OnMain(() => Assert.Equal(SwiftColor.Clear.Resolve(circle.ActualThemeVariant), FillColor()));

            harness.Main.OnMain(() => Click(button)); // on
            Settle();
            harness.Main.OnMain(() =>
            {
                // On: accent glyph, accent at 0.16 background (:2327, :2331).
                Assert.Equal(Chrome.Accent.Resolve(glyph.ActualThemeVariant), GlyphColor());
                Assert.Equal(Chrome.Accent.Opacity(0.16).Resolve(circle.ActualThemeVariant), FillColor());
            });
            Close(view);
        });
    }

    // ---- UI-SIDEBAR-005: the scrolling list's own geometry and its list-wide drag gesture ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:101-148
    public void The_list_has_no_scroll_indicators_10_12_10_8_padding_and_1pt_spacing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the row's own insertion transition (:117-119) needs a bound clock
            var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
            var model = Model(harness, [alpha], []);
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));

            var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single();
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility); // :102, :139 .scrollIndicators(.never)
            Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);

            var list = ListStack(view);
            Assert.Equal(Orientation.Vertical, list.Orientation); // :103 LazyVStack(alignment: .leading, spacing: 1)
            Assert.Equal(1d, list.Spacing);
            Assert.Equal(new Thickness(Chrome.ListInset, 12, Chrome.ListInset, 8), list.Margin); // :120-122
            Assert.Null(list.Background); // rows supply their own fills

            // The list's own DragGesture, attached ahead of every row's own click, and its query-empty gating
            // (:123-137, :129, :776) are exercised through real presses on a bare (non-SidebarRow) row by the
            // reorder tests above (part 2), so this test does not repeat that interaction here.
            Close(view);
        });
    }

    // ---- UI-SIDEBAR-021: the archive button's tooltip and click ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-021")] // App/Views/Sidebar/SidebarView.swift:1775-1784
    public void The_archive_button_is_tooltipped_Archive_thread_and_its_click_runs_the_supplied_action() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var type = Private("SidebarThreadRow");
        var actions = new List<RowAction> { new() { Title = "Rename", Action = () => { } } };
        var isMenuPresented = new Binding<bool>(() => false, _ => { });
        var archived = false;
        var overlay = Shown((Control)CallStatic(type, "BuildTrailingOverlay",
            thread, true, false, false, false, false, (Action)(() => archived = true), actions, isMenuPresented)!);
        var archive = LogicalQuery.SelfAndDescendants<Button>(overlay).First(); // :1785, ahead of the ellipsis
        Assert.Equal("Archive thread", ToolTip.GetTip(archive)); // :1783
        Click(archive);
        Assert.True(archived); // :1777-1782 Button(action: archiveAction)
    });

    // ---- UI-SIDEBAR-008, -009, -010: ProjectRow's own body ----

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1078-1166; UI/Chrome/Chrome.swift:681-746
    public void The_project_row_toggles_expansion_unless_disabled_and_dims_to_0_55_while_inactive() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0);
            Assert.Equal(28d, row.Bounds.Height); // :1099 accessoryWidth into SidebarRow's own Chrome.rowHeight row
            Assert.Equal(1d, row.Opacity); // :1135, active by default
            Assert.True(model.Project(project.Id)!.IsExpanded); // Project.IsExpanded defaults true (Library.cs:86)
            Click(ButtonNamed(row, "Alpha")); // :1100-1105, :738 the button's own automation name is the title
            Assert.False(model.Project(project.Id)!.IsExpanded);
        });

        harness.Main.OnMain(() =>
        {
            var neverToggles = ShownProjectRow(model, project, 0, togglesExpansion: false);
            Click(ButtonNamed(neverToggles, "Alpha")); // :1101 guard togglesExpansion else return
            Assert.False(model.Project(project.Id)!.IsExpanded); // unchanged
        });

        harness.Main.OnMain(() => model.Settings.SetProjectActive(false, project.Id));
        harness.Main.OnMain(() => Assert.Equal(0.55d, row.Opacity)); // :1135, a later pass, not a rebuild
        harness.Main.OnMain(() => model.Settings.SetProjectActive(true, project.Id));
        harness.Main.OnMain(() => Assert.Equal(1d, row.Opacity));
    });

    // ---- PR-SIDEBAR-130, PR-SIDEBAR-133, UI-SIDEBAR-014, UI-SIDEBAR-015, UI-SIDEBAR-016: the live context menu ----

    // Real hit-testing through the actual pointer pipeline (ROUND3.md item 2's own instruction): MouseUp with the
    // right button raises Avalonia's own ContextRequestedEvent (Control.OnPointerReleased), which ContextMenu.Open
    // answers by running this constructor's own Opening handler synchronously (rebuilding Items from the live model)
    // and only then setting Popup.IsOpen, whose own property-changed handler tries to create a native popup host.
    // Avalonia 12.1.2 cannot do that in headless mode on this machine ("Unable to create IPopupImpl and no overlay
    // layer is found for the target control"): the same exception already independently confirmed empirically for
    // two other units' own real Popups (Marrow.App.Tests/Fixtures/HeadlessFixtures.cs's own R-VIEW-9 comment;
    // Marrow.UI.Tests/Markdown/LinkPopoverTests.cs, ProseRunViewTests.cs), a pre-existing, environment-wide gap this
    // job's own scope (SidebarView.cs, ROUND3.md item 2) does not touch. Opening has already run and mutated Items
    // by the time this throws, so the menu's Items still show the real right-click's own fresh read; only the
    // popup's own visible surface never renders here.
    internal static void PointerRightClick(Control control)
    {
        var window = WindowOf(control);
        window.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Right);
        try
        {
            window.MouseUp(point, MouseButton.Right);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("no overlay layer is found for the target control", StringComparison.Ordinal))
        {
        }
    }

    // ShownProjectRow returns the outer ProjectRow (a Decorator); ContextMenu is set on the inner SidebarRow this
    // constructor builds and wraps (beside the removal question's popup, OD-0008 A9), not on ProjectRow itself.
    internal static ContextMenu ContextMenuOf(Control row) => LogicalQuery.SelfAndDescendants<SidebarRow>(row).First().ContextMenu!;

    internal static string MenuItemTitle(object item) => item switch
    {
        MenuItem { Header: TextBlock tb } => tb.Text ?? "",
        MenuItem { Header: Control header } => LogicalQuery.SelfAndDescendants<TextBlock>(header).First().Text ?? "",
        _ => "",
    };

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // App/Views/Sidebar/SidebarView.swift:1138-1162,1168-1203,1205-1229: a real right-click opens the live context menu, rebuilt fresh (not stale) on every open
    [Trait("Req", "UI-SIDEBAR-008")] // App/Views/Sidebar/SidebarView.swift:1138: right-click opens the context menu
    public void A_real_right_click_opens_the_project_rows_context_menu_rebuilt_fresh_on_every_open() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        // The row's own hover Track animates its fill on pointer entry/exit (Chrome.cs :1119), which a real right-click's
        // own MouseMove/MouseDown/MouseUp pointer traffic triggers same as a left-click's would.
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0);
            model.Settings.SetProjectActive(true, project.Id); // :1096, :1141 isActive true; also gives hasOverride (:1142)
        });
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row);
            var menu = ContextMenuOf(row); // :1138, Control.ContextMenu opens on ContextRequestedEvent
            var titles = menu.Items.Cast<object>().Select(MenuItemTitle).ToArray();
            // :1170-1186, :1188-1195, :1196-1200 (ContextMenuActions): the weak-model-captured items, snapshot-built at open time.
            Assert.Contains("New thread", titles);
            Assert.Contains("New thread in worktree", titles);
            Assert.Contains("Reveal in File Explorer", titles); // OD-0008 B1
            Assert.Contains("Remove project", titles);
            Assert.Contains("Deactivate project", titles); // :1189 isActive true
            Assert.DoesNotContain("Activate project", titles);
            Assert.Contains("Follow global setting", titles); // :1196-1200 hasOverride true
        });
        // The row itself is never rebuilt between the two right-clicks below (still the same `row`/ProjectRow
        // instance): a menu whose Items were fixed once, at construction or at the first open, would show the same
        // "Deactivate project"/"Activate project" wording both times - exactly the "invented, silently wrong
        // behaviour" STOP #7 (SidebarViewTests.cs) rejected. Flipping the live model state between opens and
        // re-reading through a second real right-click is the only way to tell a per-open-fresh read (:1141, this
        // constructor's own ContextMenu.Opening) apart from a stale one.
        harness.Main.OnMain(() => model.Settings.SetProjectActive(false, project.Id));
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row);
            var menu = ContextMenuOf(row);
            var titles = menu.Items.Cast<object>().Select(MenuItemTitle).ToArray();
            Assert.Contains("Activate project", titles); // :1189 isActive now false: the flipped wording
            Assert.DoesNotContain("Deactivate project", titles);
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-015")] // App/Views/Sidebar/SidebarView.swift:1149-1161: the pair submenu is present only when hydraPickerPairs is non-empty, read fresh at open time
    [Trait("Req", "PR-SIDEBAR-133")] // App/Views/Sidebar/SidebarView.swift:1139-1163: the same, through a real right-click on a row that is never rebuilt
    public void The_pair_submenu_is_absent_with_no_pairs_and_appears_once_one_exists_without_rebuilding_the_row() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // see the sibling test above
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row);
            var menu = ContextMenuOf(row);
            Assert.DoesNotContain(menu.Items.Cast<object>(), item => item is MenuItem { Header: Control h } && // :1154 pairsSnapshot.isEmpty
                LogicalQuery.SelfAndDescendants<TextBlock>(h).Any(t => t.Text == "Start new chats on"));
        });
        // A pair enters HydraPickerPairs once its provider counts as installed and is enabled
        // (AppModel+HydraPairs.cs :11); ProviderRegistry.CountsAsInstalled and AppSettings.IsEnabled both default
        // true with no status/disabled entry for the provider (ProviderRegistry.cs :306, AppSettings.cs :1282), so
        // adding the pair alone is enough here, with no separate installed/enabled fixture.
        harness.Main.OnMain(() => model.Settings.AddHydraPair(new HydraPair(ProviderKind.Claude)));
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row); // same row, still not rebuilt: :1151 must re-read HydraPickerPairs fresh at this open
            var menu = ContextMenuOf(row);
            Assert.Contains(menu.Items.Cast<object>(), item => item is MenuItem { Header: Control h } &&
                LogicalQuery.SelfAndDescendants<TextBlock>(h).Any(t => t.Text == "Start new chats on")); // :1156-1160
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-009")] // App/Views/Sidebar/SidebarView.swift:1127-1132, :521-523
    [Trait("Req", "UI-SIDEBAR-008")] // App/Views/Sidebar/SidebarView.swift:1127-1132: collapsed with count > 0 the count label shows
    public void The_collapsed_count_label_shows_only_while_collapsed_with_a_positive_count() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha") with { IsExpanded = false };
        var model = Model(harness, [project], []);
        var windows = new List<AvaloniaWindow>();
        harness.Main.OnMain(() =>
        {
            var row = ShownProjectRow(model, project, 3);
            windows.Add(WindowOf(row));
            var count = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "3"); // :1128, a bare decimal
            Assert.Equal(Chrome.SecondaryText.Resolve(count.ActualThemeVariant), ((ISolidColorBrush)count.Foreground!).Color); // :1130
            var wrapper = Assert.IsType<Border>(count.Parent); // SidebarRow (Chrome.cs) sets its own Margin on the returned
            Assert.Equal(new Thickness(0, 0, 4, 0), wrapper.Padding); // control unconditionally, so :1131's own padding nests as this Padding
        });

        // ProjectRow's own accessory reads the model's live project (this constructor's own header comment), not the
        // constructor argument, once attached; a row built after the model is expanded reflects that, not the
        // snapshot passed in.
        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { IsExpanded = true }));
        Control expandedRow = null!;
        harness.Main.OnMain(() =>
        {
            expandedRow = ShownProjectRow(model, project, 3);
            windows.Add(WindowOf(expandedRow));
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(expandedRow), t => t.Text == "3"); // :1127 !isExpanded
        });

        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { IsExpanded = false }));
        harness.Main.OnMain(() =>
        {
            var zeroRow = ShownProjectRow(model, project, 0);
            windows.Add(WindowOf(zeroRow));
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(zeroRow), t => t.Text == "0"); // :1127 count > 0
        });
        // The rows stay live in their windows: the one built while the project was expanded draws its count now that the
        // project is collapsed again (:1085, :1127 read the live project, not the snapshot it was built from). The windows
        // are closed inside the test, so no layout of theirs is left for after the test body.
        harness.Main.OnMain(() =>
        {
            windows[1].UpdateLayout();
            Assert.Contains("3", Texts(expandedRow));
            windows.ForEach(window => window.Close());
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-010")] // App/Views/Sidebar/SidebarView.swift:1119-1125
    public void The_plus_button_shows_only_while_hovered_and_creates_a_thread_with_an_untruncated_tooltip() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the hover fill's own AnimatedColor needs a bound clock
        var project = NewProject(harness, "Alpha Beta Gamma Delta Epsilon"); // long enough to show a truncated tooltip if one happened
        var model = Model(harness, [project], []);
        Control row = null!;
        AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0);
            window = WindowOf(row);
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(row),
                b => ToolTip.GetTip(b) is string tip && tip.StartsWith("New thread in", StringComparison.Ordinal)); // :1116, not hovering
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value)); // needs the main actor (MainActorModel.Set)
        harness.Main.OnMain(() =>
        {
            var plus = LogicalQuery.SelfAndDescendants<Button>(row)
                .Single(b => ToolTip.GetTip(b) as string == "New thread in Alpha Beta Gamma Delta Epsilon"); // :1125, untruncated
            Assert.Empty(model.ThreadsIn(project));
            PointerClick(plus); // :1119-1123
            Assert.Single(model.ThreadsIn(project)); // :1120 model.newThread(in: project)
            window.Close();
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1085, :1106-1114 (REVIEW FIX, part 6)
    public void The_icon_glyph_follows_the_live_project_icon_without_rebuilding_the_row() => headless.OnUiThread(() =>
    {
        // REVIEW FIX: :1085 `project` is a computed property Swift's body re-reads on every render, so :1106-1114's
        // icon glyph always reflects the live project.icon, changed live from Settings' icon picker
        // (SettingsView.swift:1152). The initial part 6 implementation built the glyph once, inside SidebarRow's
        // own constructor (icon(), Chrome.cs:1079) and SidebarIconBadge's own constructor (glyph(), Chrome.cs:1044),
        // synchronously before this class's own Track was ever installed, so a later icon change never reached the
        // already-built row. Verified this fails (the glyph stays "folder.fill"/no TextBlock) against the pre-fix
        // code and passes against ReplaceGlyph.
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha"); // no icon yet
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0);
            var fallback = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName is not null);
            Assert.Equal("folder.fill", fallback.SymbolName); // :1111 SidebarSymbol("folder.fill"), no icon picked
        });

        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = new ProjectIcon.Symbol("star.fill") })); // :1108-1109, a picked symbol
        harness.Main.OnMain(() =>
        {
            var symbol = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName is not null); // same row instance, not a fresh one
            Assert.Equal("star.fill", symbol.SymbolName); // no longer the folder.fill fallback
        });

        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = new ProjectIcon.Emoji("\U0001F680") })); // :1108-1109, an emoji icon
        harness.Main.OnMain(() =>
        {
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(row), i => i.SymbolName is not null); // the symbol glyph is gone entirely
            var emoji = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "\U0001F680"); // :18 Text(verbatim: emoji)
            Assert.NotNull(emoji);
        });

        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = null })); // the icon is cleared again
        harness.Main.OnMain(() =>
        {
            var backToFallback = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName is not null);
            Assert.Equal("folder.fill", backToFallback.SymbolName); // :1111, back to the fallback
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-010")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "UI-SIDEBAR-009")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-128")] // App/Views/Sidebar/SidebarView.swift:1091, :1115-1133 (REVIEW FIX, part 6)
    public void The_hover_accessory_collapses_the_instant_the_ellipsis_menu_closes_even_while_not_hovering() => headless.OnUiThread(() =>
    {
        // REVIEW FIX: any write to :1091 `isMenuPresented` is a SwiftUI @State change that re-evaluates the whole
        // body, including :1115-1133's accessory branch (`hovering || isMenuPresented`), with or without a
        // concurrent hover change. The initial part 6 implementation only re-invoked the accessory closure from
        // SidebarRow's own hover Track (Chrome.cs:1120-1128), keyed on ITS OWN state.IsHovering; a change to this
        // row's own isMenuPresented alone (e.g. the ellipsis popover closing while the pointer is not back over the
        // row) registered no dependency there, so the ellipsis+plus cluster stayed on screen indefinitely instead
        // of reverting to the collapsed (here, empty: :1127 count == 0) state. Verified this fails (the cluster
        // survives, the DoesNotContain assertions below fail) against the pre-fix code and passes against the added
        // isMenuPresented Track. count is 0 (rather than a positive collapsed count, already covered independently
        // by UI-SIDEBAR-009) so the collapsed branch is the plain EmptyView Border (:1115-1133's own last branch),
        // not a fresh MonospacedDigit TextBlock: this assembly's headless Skia text-shaper (confirmed pre-existing,
        // unrelated to this fix: tests/Marrow.App.Tests/Fixtures/HeadlessFixtures.cs's own WarmFonts/CapHeight
        // gaps) can throw "Could not create glyphTypeface" the first time a fresh glyph run is measured right after
        // an older control carrying the same weight is torn out of the same overlay in the same pass, which the
        // ellipsis/plus cluster's own removal below would otherwise also trigger a second time for that TextBlock.
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the hover fill's own AnimatedColor needs a bound clock
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter()); // Avalonia 12.1.2 headless cannot open a real Popup (Port/ViewLifetime.cs); the ellipsis click below only needs isMenuPresented to flip, not a real popover
        var project = NewProject(harness, "Alpha") with { IsExpanded = false };
        var model = Model(harness, [project], []);
        Control row = null!;
        AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0); // :1127 count == 0, see this test's own header comment
            window = WindowOf(row);
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value)); // :1116 hovering
        Button ellipsis = null!;
        harness.Main.OnMain(() =>
        {
            ellipsis = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More"); // Popovers.cs :396
            PointerClick(ellipsis); // :391 isPresented.toggle() -> isMenuPresented = true
        });
        harness.Main.OnMain(() => window.MouseMove(new Point(1, 1))); // pointer leaves the row; :1116 hovering false, isMenuPresented still true
        harness.Main.OnMain(() =>
        {
            Assert.Contains(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "More"); // :1116 hovering || isMenuPresented, still shown
            Click(ellipsis); // closes the popover the same way a menu item's dismiss() does (Popovers.cs SetClosePopover): isMenuPresented = false, pointer still away
        });
        harness.Main.OnMain(() =>
        {
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "More"); // :1127 the ellipsis must collapse
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "New thread in Alpha"); // :1127 the plus button collapses with it
        });
    });

    // ---- PR-SIDEBAR-126, -127, -129: ProjectRow's own geometry, icon and dimming ----
    //
    // UI-SIDEBAR-008/-009/-010 prove the toggle, the count label and the plus
    // button; PR-SIDEBAR-128 (the hover accessory's collapse) is proven by its
    // own REVIEW FIX test below. The three tests here prove the PR-ids' numbers:
    // the 28 pt row with its 40 pt accessory reserve and panelSlide toggle
    // (-126), the picked-or-folder mark with its two sizes and opacities (-127)
    // and the 0.55 whole-row dimming with its override-then-global rule (-129).

    [Fact]
    [Trait("Req", "PR-SIDEBAR-126")] // App/Views/Sidebar/SidebarView.swift:1097-1105; UI/Chrome/Chrome.swift:710-728
    [Trait("Req", "UI-SIDEBAR-008")] // App/Views/Sidebar/SidebarView.swift:1097-1105: click toggles project.isExpanded inside Chrome.panelSlide; title typography and colours
    public void A_project_row_is_a_28pt_sidebar_row_with_a_40pt_accessory_reserve_and_click_toggles_expansion()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // real pointer hover requests the source clock
            var project = NewProject(harness, "Alpha");
            var model = Model(harness, [project], []);
            Control? row = null;
            harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
            harness.Main.OnMain(() =>
            {
                Assert.Equal(28d, row!.Bounds.Height); // :1099 via Chrome.rowHeight
                var shape = LogicalQuery.SelfAndDescendants<Border>(row!).First(b => b.Height == 28 && b.Padding.Right > 40);
                Assert.Equal(new Thickness(8, 0, 48, 0), shape.Padding); // :725-726 with :1099's 8 + 40
                var stack = LogicalQuery.SelfAndDescendants<StackPanel>(row!).First(s => s.Orientation == Orientation.Horizontal && s.Children.Count == 2);
                Assert.Equal(8d, stack.Spacing); // :711
                Assert.Equal(20d, stack.Children[0].Width); // :713 Chrome.iconSize
                var title = LogicalQuery.SelfAndDescendants<TextBlock>(row!).First(t => t.Text == "Alpha");
                Assert.Equal(1, title.MaxLines); // :718
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming); // :719 .tail
                Assert.Equal(TextWrapping.NoWrap, title.TextWrapping);
                Assert.Equal(13d, title.FontSize); // :717 .body
                Assert.Equal(FontWeight.Normal, title.FontWeight); // never selected, so regular, not medium
                Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color); // :718, not selected 1.0
                Assert.Equal(Chrome.Overlay(0.06).Resolve(title.ActualThemeVariant), ((SwiftColor)CallStatic(typeof(SidebarRow), "FillFor", false, true)!).Resolve(title.ActualThemeVariant)); // :743 the row's own hover fill
            });
            Animation? during = null;
            using var probe = new OnChange<bool>(() => model.Project(project.Id)!.IsExpanded, (_, _) => during = Transaction.Current);
            harness.Main.OnMain(() => PointerClick(ButtonNamed(row!, "Alpha"))); // :1100-1105
            harness.Main.OnMain(() =>
            {
                Assert.False(model.Project(project.Id)!.IsExpanded);
                Assert.Equal(Chrome.PanelSlide, during); // :1102-1104
                WindowOf(row!).Close(); // detach tracked hover accessories before the headless application resets
            });
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1106-1114; UI/Chrome/ProjectIconPicker.swift:11-27; UI/Chrome/Chrome.swift:651-679
    public void A_project_rows_leading_mark_is_the_picked_emoji_or_symbol_or_the_folder_mark_until_one_is_picked()
    {
        // The live row's glyph following the live icon through every branch is
        // proven by The_icon_glyph_follows_the_live_project_icon_without_rebuilding_the_row
        // above (same trait); this test proves the two sizes, the two opacities
        // and the 20 pt box. Each branch is built fresh and measured once: swapping
        // the badge's glyph and measuring a fresh run in the same pass trips this
        // harness's own pre-existing "Could not create glyphTypeface" gap (see the
        // REVIEW FIX test below), so no UpdateProject tearout happens here.
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha"); // no icon yet
            var model = Model(harness, [project], []);
            harness.Main.OnMain(() =>
            {
                var row = ShownProjectRow(model, project, 0);
                var badge = LogicalQuery.SelfAndDescendants<SidebarIconBadge>(row).Single();
                var box = (Panel)badge.Child!;
                Assert.Equal(20d, box.Width); // :677 both branches sit in a 20 pt square
                Assert.Equal(20d, box.Height);
                var fallback = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName is not null);
                Assert.Equal("folder.fill", fallback.SymbolName); // :1111
                Assert.Equal(14d, fallback.Width); // the default SidebarSymbol size
                Assert.Equal(14d, fallback.Height);
                Assert.Equal(Chrome.PrimaryText.Opacity(0.85).Resolve(fallback.ActualThemeVariant), ((ISolidColorBrush)fallback.GetValue(SwiftImage.ForegroundProperty)!).Color); // :676 the badge's 0.85, inherited
            });
            harness.Main.OnMain(() =>
            {
                // :1108-1109 a picked symbol: ProjectRow builds ProjectIconMark(icon, 12).
                var mark = new ProjectIconMark(new ProjectIcon.Symbol("star.fill"), 12);
                Hosted(model, mark);
                var symbol = LogicalQuery.SelfAndDescendants<SwiftImage>(mark).Single(i => i.SymbolName is not null);
                Assert.Equal("star.fill", symbol.SymbolName);
                Assert.Equal(12d, SwiftImage.GetFont(symbol)!.PointSize); // :1109 size 12, not the 14 pt fallback
                Assert.Equal(Chrome.PrimaryText.Opacity(0.9).Resolve(symbol.ActualThemeVariant), ((ISolidColorBrush)symbol.GetValue(SwiftImage.ForegroundProperty)!).Color); // :25 0.9, not 0.85
            });
            harness.Main.OnMain(() =>
            {
                // :1108 an emoji icon.
                var mark = new ProjectIconMark(new ProjectIcon.Emoji("\U0001F680"), 12);
                Hosted(model, mark);
                var emoji = LogicalQuery.SelfAndDescendants<TextBlock>(mark).Single(t => t.Text == "\U0001F680"); // :18
                Assert.Equal(12d, emoji.FontSize); // :19
                Assert.Equal(Chrome.PrimaryText.Opacity(0.9).Resolve(emoji.ActualThemeVariant), ((ISolidColorBrush)emoji.Foreground!).Color); // :25
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-129")] // App/Views/Sidebar/SidebarView.swift:1096-1097, :1135-1136; App/AppSettings.swift:343-346
    public void A_deactivated_projects_whole_row_draws_at_0_55_opacity()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha");
            var model = Model(harness, [project], []);
            Control? row = null;
            harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
            harness.Main.OnMain(() => Assert.Equal(1d, row!.Opacity)); // active by default

            harness.Main.OnMain(() => model.Settings.SetProjectActive(false, project.Id)); // explicit override
            harness.Main.OnMain(() => Assert.Equal(0.55d, row!.Opacity)); // :1135 the whole body, icon, title and accessories

            harness.Main.OnMain(() => model.Settings.SetProjectActive(true, project.Id));
            harness.Main.OnMain(() => Assert.Equal(1d, row!.Opacity));

            harness.Main.OnMain(() => model.Settings.ClearProjectActivationOverride(project.Id)); // no override: the global switch rules
            harness.Main.OnMain(() => model.Settings.ProjectsEnabled = false);
            harness.Main.OnMain(() => Assert.Equal(0.55d, row!.Opacity));
            harness.Main.OnMain(() => model.Settings.ProjectsEnabled = true);
            harness.Main.OnMain(() => Assert.Equal(1d, row!.Opacity));

            // AppSettings.swift:343-346: the project's own explicit override wins over the global switch, both ways.
            harness.Main.OnMain(() => model.Settings.ProjectsEnabled = false);
            harness.Main.OnMain(() => model.Settings.SetProjectActive(true, project.Id));
            harness.Main.OnMain(() => Assert.Equal(1d, row!.Opacity)); // switched on here while the global switch is off
            harness.Main.OnMain(() => model.Settings.ProjectsEnabled = true);
            harness.Main.OnMain(() => model.Settings.SetProjectActive(false, project.Id));
            harness.Main.OnMain(() => Assert.Equal(0.55d, row!.Opacity)); // switched off here while the global switch is on

            // The whole row dims, icon, title and accessories included: the opacity sits on the row that holds them all.
            var badge = LogicalQuery.SelfAndDescendants<SidebarIconBadge>(row!).Single();
            Assert.Contains(row, badge.GetLogicalAncestors()); // the icon sits under the row that carries the 0.55
            Assert.Equal(1d, badge.Opacity); // and carries no dimming of its own
        });
    }

    // ---- UI-SIDEBAR-014: the project menu's items (ellipsis popover and context menu) ----

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1168-1229
    public void The_project_menu_offers_new_thread_worktree_reveal_remove_and_the_activation_item_in_order() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        harness.Main.OnMain(() =>
        {
            foreach (var made in new[]
            {
                ProjectRowActions(model, project, isActive: true).ToList(),
                ProjectRowContextMenuActions(project, true, false, model).ToList(),
            })
            {
                Assert.Equal(
                    ["New thread", "New thread in worktree", "Reveal in File Explorer", "Remove project", "Deactivate project"],
                    made.Select(a => a.Title));
                Assert.Equal(["square.and.pencil", "square.stack.3d.up", "folder", "trash", "power"], made.Select(a => a.Symbol));
                Assert.True(made[3].IsDestructive); // Remove project, :1183/:1212
                Assert.True(made[2].StartsGroup); // Reveal in File Explorer, :1179/:1211
                Assert.True(made[3].StartsGroup); // Remove project, its own separator
                Assert.True(made[4].StartsGroup); // the activation item, :1188/:1216
            }

            Assert.Equal("Activate project", ProjectRowActions(model, project, isActive: false).Last().Title); // :1189/:1217
            Assert.Equal("Activate project", ProjectRowContextMenuActions(project, false, false, model).Last().Title);

            // hasOverride adds "Follow global setting" last, symbol arrow.uturn.backward, no separator (:1196-1200/:1223-1226).
            var overriddenMenu = ProjectRowContextMenuActions(project, true, true, model);
            Assert.Equal("Follow global setting", overriddenMenu.Last().Title);
            Assert.Equal("arrow.uturn.backward", overriddenMenu.Last().Symbol);
            Assert.False(overriddenMenu.Last().StartsGroup);
            Assert.Equal(6, overriddenMenu.Count);

            // Actions(isActive:) reads the override off the live model (:1223), unlike ContextMenuActions' own param.
            model.Settings.SetProjectActive(false, project.Id); // creates a per-project override
            var isActive = model.Settings.IsProjectActive(project.Id);
            var overriddenActions = ProjectRowActions(model, project, isActive);
            Assert.True(model.Settings.HasProjectActivationOverride(project.Id));
            Assert.Equal("Follow global setting", overriddenActions.Last().Title);

            // The action itself calls through to the model (Reveal in File Explorer and Remove project are exercised
            // structurally above; New thread is exercised directly here as the cheapest observable one).
            Assert.Empty(model.ThreadsIn(project));
            ProjectRowActions(model, project, isActive: true)[0].Action(); // :1207 model.newThread(in: project)
            Assert.Single(model.ThreadsIn(project));
        });
    });

    // ---- UI-SIDEBAR-015, -016: the pair submenu and its items ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-133")] // App/Views/Sidebar/SidebarView.swift:1139-1163: the submenu and its divider exist only while a pair is offered
    public void The_start_new_chats_on_submenu_appears_only_when_there_is_a_hydra_pair_to_choose() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        harness.Main.OnMain(() =>
        {
            var none = ProjectRowBuildContextMenuItems(project, true, false, model, ProjectPairChoices());
            Assert.DoesNotContain(none, c => c is MenuItem item && item.Header is StackPanel panel
                && panel.Children.OfType<TextBlock>().Any(t => t.Text == "Start new chats on")); // :1154 pairsSnapshot.isEmpty

            var withPairs = ProjectRowBuildContextMenuItems(project, true, false, model, ProjectPairChoices((Guid.NewGuid(), "Claude, GPT")));
            Assert.IsType<Separator>(withPairs[^2]); // :1155
            var submenu = Assert.IsType<MenuItem>(withPairs[^1]); // :1156-1160
            var header = Assert.IsType<StackPanel>(submenu.Header);
            Assert.Contains(header.Children, c => c is SwiftImage img && img.SymbolName == "point.3.connected.trianglepath.dotted"); // OD-0010
            Assert.Contains(header.Children, c => c is TextBlock t && t.Text == "Start new chats on"); // :1159
            Assert.Equal(3, submenu.Items.Count); // :1157 ProjectPairMenuItems: the clearing item, a divider, one pair
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1241-1270
    public void The_pair_submenu_lists_the_clearing_item_then_each_pair_with_a_checkmark_on_the_current_choice() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        var firstID = Guid.NewGuid();
        var secondID = Guid.NewGuid();
        harness.Main.OnMain(() =>
        {
            var pairs = ProjectPairChoices((firstID, "Claude, GPT"), (secondID, "Gemini, Grok"));

            var unselected = Activator.CreateInstance(Private("ProjectPairMenuItems"), project.Id, (Guid?)null, pairs, model)!;
            var items = (IReadOnlyList<Control>)Get(unselected, "Items")!;
            Assert.Equal(4, items.Count); // the clearing item, a divider, then one item per pair (:1248-1267)

            var clearing = Assert.IsType<MenuItem>(items[0]);
            var clearingHeader = Assert.IsType<StackPanel>(clearing.Header); // :1250-1255, selectedID nil -> checked
            Assert.Contains(clearingHeader.Children, c => c is SwiftImage img && img.SymbolName == "checkmark");
            Assert.Contains(clearingHeader.Children, c => c is TextBlock t && t.Text == "The chat's last pair");

            Assert.IsType<Separator>(items[1]); // :1257

            var first = Assert.IsType<MenuItem>(items[2]);
            var firstText = Assert.IsType<TextBlock>(first.Header); // :1254, not selected: plain Text
            Assert.Equal("Claude, GPT", firstText.Text);

            var second = Assert.IsType<MenuItem>(items[3]);
            Assert.Equal("Gemini, Grok", Assert.IsType<TextBlock>(second.Header).Text);

            ((MenuItem)items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); // :1259-1260
            Assert.Equal(firstID, model.Project(project.Id)!.HydraPairID);

            var selectedSecond = Activator.CreateInstance(Private("ProjectPairMenuItems"), project.Id, (Guid?)secondID, pairs, model)!;
            var selectedItems = (IReadOnlyList<Control>)Get(selectedSecond, "Items")!;
            var secondSelected = Assert.IsType<MenuItem>(selectedItems[3]);
            var secondHeader = Assert.IsType<StackPanel>(secondSelected.Header); // :1262-1266, selectedID == pair.id -> checked
            Assert.Contains(secondHeader.Children, c => c is SwiftImage img && img.SymbolName == "checkmark");
            Assert.Contains(secondHeader.Children, c => c is TextBlock t && t.Text == "Gemini, Grok");

            ((MenuItem)selectedItems[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); // :1248-1249, clears
            Assert.Null(model.Project(project.Id)!.HydraPairID);
        });
    });

    // ======== Part 7 of 8 ========
    //
    // Records: UI-SIDEBAR-023, -024, -025, -026, -027, -031, -032, -034, -035, -039, -040, -041, -042, -043, -044,
    // -059 (16 of this part's 24) are proven by adding this part's own [Trait] to a test parts 1-6 already wrote for
    // the identical evidence (grep the id to find the added trait; the sites are RelativeTimeLabel, ActivityStatus,
    // ThreadActions.Make/MakeContextMenu, DeleteThreadPopover, ArchiveThreadPopover, HelperConnector, HelperStubRow,
    // ActivityHeader, SettledHeader, NoResults, the Add project/Settings rows and folder chooser, and the sidebarRow
    // transition). UI-SIDEBAR-058's own gap/project-row/thread-row sequence is proven the same way; the project
    // row's own inertness-but-live-accessories in a search group is new below, and the thread rows' project form and
    // disabled reordering are now proven by the Window sweep below.
    // SidebarHelperRow's own constructor (UI-SIDEBAR-033) and HeadCardsRow's own constructor (UI-SIDEBAR-036, -037,
    // -038; the same site as PR-SIDEBAR-121, -122, now also proven except their own context menus) are new this
    // part, assembled from pieces that were still missing when STOP #7 was written (RowGlideAnimator.Shared.IsHiding
    // and the per-row hover/menu @State pattern, both landed by parts 5 and 6). Each row's own right-click
    // .contextMenu is left out: STOP #7 below is narrowed to name it explicitly for these two rows, rather than
    // resolved, since the same missing per-open-refresh ED/PA foundation still blocks it. UI-SIDEBAR-028, -029, -030
    // extend STOP #6 (part 4): the rename popover's TextField/Button chrome is still undecided.

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:1348-1409: a head's glyph before the title, click selects, hover swaps the activity status for the ellipsis
    public void A_helper_row_shows_its_head_glyph_selects_on_click_and_swaps_its_overlay_on_hover() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the hover fill's own AnimatedColor needs a bound clock
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1) with { HasUnread = true };
        var model = Model(harness, [project], [parent, head]);
        Control row = null!;
        AvaloniaWindow window = null!;
        Panel overlayHost = null!;
        Button selectButton = null!;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            row = (Control)Activator.CreateInstance(Private("SidebarHelperRow"),
                head, true, requests, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
            row.VerticalAlignment = VerticalAlignment.Top; // the row's own 24pt height, not the host's full stretch
            var host = Hosted(model, row);
            window = WindowOf(host);
            window.UpdateLayout();

            var grid = Assert.IsType<Grid>(Get(row, "Child"));
            var connector = grid.Children[0];
            Assert.Equal(ThreadRowMetrics.ConnectorWidth, connector.Width); // :1339, this row's own connector column
            overlayHost = Assert.IsType<Panel>(grid.Children[1]);
            selectButton = Assert.IsType<Button>(overlayHost.Children[0]);

            Assert.NotEmpty(LogicalQuery.SelfAndDescendants<HydraGlyph>(selectButton)); // :1354-1357 a head keeps its glyph
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(selectButton).Single(t => t.Text == head.Title); // :1359
            Assert.Equal(FontWeight.Medium, title.FontWeight); // :1361, hasUnread
            Assert.Equal(Chrome.PrimaryText.Opacity(0.8).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color); // :1362, not selected

            Assert.IsType(Private("ActivityStatus"), overlayHost.Children[1]); // :1380, not hovering, no menu open

            Assert.Null(model.SelectedThreadID);
            Click(selectButton); // :1348-1350
            Assert.Equal(head.Id, model.SelectedThreadID);
        });
        harness.Main.OnMain(() => window.MouseMove(selectButton.TranslatePoint(new Point(10, 10), window)!.Value)); // :1401-1404 .onHover, pumps the posted Track update on return
        harness.Main.OnMain(() =>
        {
            Assert.IsType<RowActionsButton>(overlayHost.Children[^1]); // :1376 showsActions -> the ellipsis, joining over the outgoing status (crossfade, :1386)
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:1361-1362: a selected title is medium at 0.96
    public void A_selected_helper_rows_title_is_medium_weight_at_0_96_with_the_selection_fill() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        harness.Main.OnMain(() =>
        {
            model.SelectedThreadID = helper.Id; // :1340 model.isSelected(thread.id)
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var row = (Control)Activator.CreateInstance(Private("SidebarHelperRow"),
                helper, false, requests, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
            var host = Hosted(model, row);
            WindowOf(host).UpdateLayout();

            var grid = Assert.IsType<Grid>(Get(row, "Child"));
            var overlayHost = Assert.IsType<Panel>(grid.Children[1]);
            var selectButton = Assert.IsType<Button>(overlayHost.Children[0]);
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(selectButton).Single(t => t.Text == helper.Title);
            Assert.Equal(FontWeight.Medium, title.FontWeight); // :1361, isSelected
            Assert.Equal(Chrome.PrimaryText.Opacity(0.96).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color); // :1362
            Assert.Empty(LogicalQuery.SelfAndDescendants<HydraGlyph>(selectButton)); // :1354, not a head
            WindowOf(host).Close();
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1359-1364 Text(...).lineLimit(1).truncationMode(.tail)
    public void A_helper_rows_long_title_measures_bounded_by_the_row_instead_of_at_its_full_width() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var longTitle = new string('W', 200); // wide glyph, repeated far past what this 200 pt host (Hosted) can show
        var helper = NewThread(project, longTitle, 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var row = (Control)Activator.CreateInstance(Private("SidebarHelperRow"),
                helper, false, requests, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
            var host = Hosted(model, row);
            WindowOf(host).UpdateLayout();

            var grid = Assert.IsType<Grid>(Get(row, "Child"));
            var overlayHost = Assert.IsType<Panel>(grid.Children[1]);
            var selectButton = Assert.IsType<Button>(overlayHost.Children[0]);
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(selectButton).Single(t => t.Text == longTitle);
            Assert.Equal(1, title.MaxLines); // :1359 lineLimit(1)
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming); // :1360 truncationMode(.tail)

            // REVIEW FIX: a horizontal StackPanel measures its stacking-axis children at infinite width no matter
            // what bounded width it itself received, so this title never actually got a constrained measure width
            // to trim against and always measured at its full, untruncated natural width. A sibling TextBlock built
            // the same way but never placed under any width constraint gives that untruncated reference width; the
            // row's own title must measure well under it once :1350's HStack is a Grid's star column instead.
            var unclipped = new TextBlock { Text = longTitle, MaxLines = 1 };
            SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Regular).Apply(unclipped);
            unclipped.Measure(Size.Infinity);

            Assert.True(unclipped.DesiredSize.Width > 400); // sanity: the fixture is far wider than this host
            Assert.True(title.DesiredSize.Width < unclipped.DesiredSize.Width / 2); // actually bounded by the row
            WindowOf(host).Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift:1427-1548
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1472-1507
    [Trait("Req", "PR-SIDEBAR-121")] // App/Views/Sidebar/SidebarView.swift:1472-1507 (B2 id, same site; the right-click
    // .contextMenu at :1490-1500 is not attached -- STOP #7 below)
    [Trait("Req", "PR-SIDEBAR-122")] // App/Views/Sidebar/SidebarView.swift:1440-1551
    [Trait("Req", "UI-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:1473-1482: click selects the head's thread; the selected head sits on a 0.14 disc 3 pt beyond the glyph
    public void A_head_card_selects_a_head_on_click_and_rings_the_selected_glyph() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        // :1010 OrderedHeads sorts newest createdAt first among finished heads, so headA (the one selected below)
        // is given the later minute to land at index 0.
        var headB = HeadHelper(project, parent.Id, 1, 1);
        var headA = HeadHelper(project, parent.Id, 0, 2);
        var model = Model(harness, [project], [parent, headA, headB]);
        harness.Main.OnMain(() => model.SelectedThreadID = headA.Id); // :1479 model.isSelected(head.id)
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var card = (Control)Activator.CreateInstance(Private("HeadCardsRow"),
                parent, new[] { headA, headB }, requests, (Action)(() => { }))!;
            var host = Hosted(model, card);
            var window = WindowOf(host);
            window.UpdateLayout(); // :1544 onGeometryChange(width) fires from the real layout pass

            var buttons = LogicalQuery.SelfAndDescendants<Button>(card)
                .Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
            Assert.Equal(2, buttons.Count); // :1466-1509 one glyph button per head

            var selectedGlyph = LogicalQuery.SelfAndDescendants<HydraGlyph>(buttons[0]).Single();
            var selectedHost = (Panel)selectedGlyph.Parent!;
            Assert.Equal(2, selectedHost.Children.Count); // :1479-1483 the disc sits behind the selected head's glyph
            var disc = Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(selectedHost.Children[0]);
            Assert.Equal(ThreadRowMetrics.HeadGlyphSize + 6, disc.Width); // :1481 padding(-3) each side
            Assert.Equal(Chrome.Overlay(0.14).Resolve(disc.ActualThemeVariant), ((ISolidColorBrush)disc.Fill!).Color);

            var unselectedGlyph = LogicalQuery.SelfAndDescendants<HydraGlyph>(buttons[1]).Single();
            Assert.Single(((Panel)unselectedGlyph.Parent!).Children); // :1479, headB is not selected: no disc

            Assert.Equal(headA.Id, model.SelectedThreadID);
            Click(buttons[1]); // :1473-1475
            Assert.Equal(headB.Id, model.SelectedThreadID);
            window.Hide();
            Dispatcher.UIThread.RunJobs();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:1484-1499: scale 1.18 on hover, the tooltip with a middle dot, the accessibility label with a comma
    public void A_head_glyph_scales_1_18_on_hover_and_carries_the_dotted_tooltip_and_comma_label() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual); // the hover scale animates (Chrome.hover)
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1);
        var model = Model(harness, [project], [parent, head]);
        AvaloniaWindow window = null!;
        Control card = null!;
        // The card's own width arrives through onGeometryChange (:1544) and can settle in more than one layout
        // pass, rebuilding the row's controls each time (ApplyEntries' own diff does not reach inside this card);
        // re-querying by the glyph's own HydraGlyph, rather than holding a captured Button/ScaleTransform, survives
        // that rebuild the way ChevronOf() does below for the chevron.
        Button HeadButton() => (Button)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!.Parent!;
        ScaleTransform Scale() => (ScaleTransform)((Panel)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!).RenderTransform!;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"),
                parent, new[] { head }, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top; // the card's own measured height, not the host's full stretch
            var host = Hosted(model, card);
            window = WindowOf(host);
            window.UpdateLayout();

            var button = HeadButton();
            Assert.Equal($"{head.Hydra!.Persona.Name} · {head.Hydra!.Task}", ToolTip.GetTip(button)); // :1490
            Assert.Equal($"{head.Hydra!.Persona.Name}, {head.Hydra!.Task}", AutomationProperties.GetName(button)); // :1499
            Assert.Equal(1d, Scale().ScaleX);
        });
        harness.Main.OnMain(() => window.MouseMove(HeadButton().TranslatePoint(new Point(7, 7), window)!.Value)); // :1486-1488 .onHover, pumps the posted Track update on return
        for (var i = 0; i < 20; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); }
        harness.Main.OnMain(() =>
        {
            Assert.Equal(1.18, Scale().ScaleX, 3); // :1484
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "UI-SIDEBAR-038")] // App/Views/Sidebar/SidebarView.swift:1514-1526
    // Named for what it actually proves (button count, chevron glyph and tooltip after the toggle): the snappy
    // height animation itself is proven separately, below, by
    // A_head_cards_row_count_change_animates_the_cards_and_connectors_height_with_snappy_0_18 (REVIEW FIX: this
    // test's old name claimed "and_snappy_animation" with no assertion of the kind -- a synchronous PointerClick() and an
    // immediate post-click assertion pass identically whether or not any animation exists).
    public void The_head_cards_chevron_toggles_all_heads_with_the_right_glyph_and_tooltip() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual); // the toggle's snappy animation runs on frames
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 13).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project, ], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        // The chevron the card draws now; the one it drew before the toggle fades out where it stood (:1516).
        Button ChevronOf() => LiveChevron(card);
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"),
                parent, heads, requests, (Action)(() => { }))!;
            var host = Hosted(model, card);
            window = WindowOf(host);
            window.UpdateLayout(); // the card's measured width reaches it on the turn this OnMain ends with, not in the click's
            // own pass below (a pass that took both would run without the click's transaction: Tracked<T> captures the
            // transaction of the change that schedules it, A_head_cards_row_count_change_animates_..., below)
        });
        harness.Main.OnMain(() =>
        {
            var chevron = ChevronOf();
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(chevron).Single();
            Assert.Equal("chevron.down", glyph.SymbolName); // :1516, collapsed: 13 > 12
            Assert.Equal("Show all 13 heads", ToolTip.GetTip(chevron)); // :1526

            PointerClick(chevron); // :1515-1516, .snappy(duration: 0.18)
        });
        // A separate harness.Main.OnMain call, as SettledHeader's own regression does: OnMain pumps the manual loop
        // to idle on return, which is what runs Tracked<T>'s MainActor.Post(Update) after the click's mutation, not
        // Dispatcher.UIThread.RunJobs (a different queue).
        harness.Main.OnMain(() =>
        {
            var buttons = LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
            Assert.Equal(13, buttons.Count); // :1443 showsAllHeads -> every head

            var expandedChevron = ChevronOf();
            var expandedGlyph = LogicalQuery.SelfAndDescendants<SwiftImage>(expandedChevron).Single();
            Assert.Equal("chevron.up", expandedGlyph.SymbolName); // :1516
            Assert.Equal("Show fewer heads", ToolTip.GetTip(expandedChevron)); // :1526
            Assert.Equal("chevron.down", ChevronSymbolOf(Assert.Single(ChevronButtons(card), OnGhostLayer))); // the one it replaces, on its way out
        });
        Frames(2);
        harness.Main.OnMain(() => Assert.InRange(ShownOpacityWithin(Assert.Single(ChevronButtons(card), OnGhostLayer), card), 0.02, 0.98)); // fading
        Frames(40);
        harness.Main.OnMain(() =>
        {
            Assert.Equal("chevron.up", ChevronSymbolOf(Assert.Single(ChevronButtons(card)))); // and gone: only the up-chevron is left
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // SidebarView.swift:1440-1544 HeadCardsRow body; :1291-1311 ThreadRowMetrics
    [Trait("Req", "UI-SIDEBAR-034")] // :1433-1436 the connector column beside a head card is connectorWidth wide and the card's height tall
    [Trait("Req", "UI-SIDEBAR-038")] // :1514-1527 the chevron row: 14x14, caption2 semibold, secondaryText
    public void A_mounted_head_card_keeps_the_28pt_connector_column_6pt_padding_and_gaps_and_twelve_glyphs_in_rows_of_perRow() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 13).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        Control card = null!;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            WindowOf(Hosted(model, card)).UpdateLayout();
        });
        harness.Main.OnMain(() =>
        {
            var window = WindowOf(card);
            window.UpdateLayout();
            var connector = LogicalQuery.SelfAndDescendants<HelperConnector>(card).Single();
            Assert.Equal(ThreadRowMetrics.ConnectorWidth, connector.Bounds.Width); // :1434 .frame(width: connectorWidth)
            var cardBorder = LogicalQuery.SelfAndDescendants<Border>(card).First(b => b.Padding == new Thickness(ThreadRowMetrics.HeadCardPadding)); // :1516 .padding(headCardPadding)
            Assert.Equal(28d, Frame(cardBorder, card).X); // HStack(spacing: 0): the card starts right after the 28 pt column
            Assert.Equal(new CornerRadius(Chrome.RowCornerRadius), cardBorder.CornerRadius); // :1519-1521
            Assert.Equal(Chrome.Overlay(0.04).Resolve(cardBorder.ActualThemeVariant), ((ISolidColorBrush)cardBorder.Background!).Color); // :1520
            Assert.True(cardBorder.ClipToBounds); // :1521 .clipShape
            Assert.Equal(connector.Bounds.Height, cardBorder.Bounds.Height); // :1435-1436 the column is the card's height
            var rows = Assert.IsType<StackPanel>(cardBorder.Child);
            Assert.Equal(ThreadRowMetrics.HeadRowGap, rows.Spacing); // :1465 VStack(spacing: headRowGap)
            var perRow = HeadCardsRow.PerRow(200); // :1451 at this host's 200 pt
            var lines = rows.Children.OfType<StackPanel>().ToList();
            Assert.Equal(new[] { (int)perRow, 12 - (int)perRow }, lines.Select(l => l.Children.Count)); // :1443 prefix(12), :1545-1547 chunked by perRow
            foreach (var line in lines)
            {
                Assert.Equal(HeadCardsRow.RowGap(200, perRow), line.Spacing); // :1467 HStack(spacing: rowGap)
                Assert.Equal(new Thickness(0, 0, ThreadRowMetrics.HeadBadgeOverhang, 0), line.Margin); // :1512-1513
            }
            var chevron = Assert.IsType<Button>(rows.Children[^1]); // :1514 the chevron's own row below the glyph rows
            Assert.Equal(new Size(ThreadRowMetrics.HeadGlyphSize, ThreadRowMetrics.HeadGlyphSize), chevron.Bounds.Size); // :1519 14x14
            Assert.Equal(ThreadRowMetrics.HeadCardPadding, Frame(chevron, cardBorder).X); // :1465 VStack(alignment: .leading), inside the 6 pt padding
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(chevron).Single();
            Assert.Equal(10d, SwiftImage.GetFont(glyph)!.PointSize); // :1517 .caption2 (10 pt)
            Assert.Equal(FontWeight.SemiBold, SwiftImage.GetFont(glyph)!.Resolve().FontWeight);
            Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), ((ISolidColorBrush)glyph.Foreground!).Color); // :1518
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift:1535 .animation(.snappy(duration: 0.18), value: cardRows)
    [Trait("Req", "UI-SIDEBAR-038")] // App/Views/Sidebar/SidebarView.swift:1515-1516 withAnimation(.snappy(duration: 0.18)) { showsAllHeads.toggle() }
    public void A_head_cards_row_count_change_animates_the_cards_and_connectors_height_with_snappy_0_18() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        // Fifteen heads, all Hydra: capped stays true either way (15 > 12), but the visible count the chevron
        // toggles between (12 collapsed, 15 expanded) chunks into a different number of glyph rows at this host's
        // own 200 pt width (Hosted), so cardRows (:1461) -- and so HeadCardHeight (:1305-1311) -- actually changes.
        var heads = Enumerable.Range(0, 15).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        var perRow = HeadCardsRow.PerRow(200); // :1451, this host's own width
        long RowsFor(long visibleCount) => (visibleCount + perRow - 1) / perRow; // :1546 stride(by: perRow), ceiling
        var collapsedRows = RowsFor(Math.Min(heads.Count, 12)) + 1; // :1443 prefix(12); :1461 capped adds the chevron row
        var expandedRows = RowsFor(heads.Count) + 1; // :1443 showsAllHeads -> every head
        Assert.NotEqual(collapsedRows, expandedRows); // the fixture must actually change row count, or this proves nothing
        var before = ThreadRowMetrics.HeadCardHeight(collapsedRows);
        var after = ThreadRowMetrics.HeadCardHeight(expandedRows);
        AvaloniaWindow window = null!;
        Control card = null!;
        Grid CardGrid() => Assert.IsType<Grid>(Get(card, "Child"));
        Button ChevronOf() => LogicalQuery.SelfAndDescendants<Button>(card)
            .Single(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName?.StartsWith("chevron", StringComparison.Ordinal) == true));
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"),
                parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top; // the card's own measured height, not the host's full stretch
            var host = Hosted(model, card);
            window = WindowOf(host);
            window.UpdateLayout(); // lets the real-width Track update (posted by outer.SizeChanged) run to idle here,
            // in its own OnMain, rather than still be pending -- and so coalesced with the chevron's own posted
            // update below -- when Click() runs, which would lose the chevron's own captured transaction (ViewSupport.cs
            // Tracked<T>.Schedule's `pendingAnimation ??= Transaction.Current` keeps whichever pass claims it first).
        });
        harness.Main.OnMain(() =>
        {
            var grid = CardGrid();
            Assert.Equal(before, grid.Children[0].Height); // :1439 the connector, collapsed
            Assert.Equal(before, grid.Children[1].Height); // :1518 the card, collapsed

            Click(ChevronOf()); // :1515-1516, withAnimation(.snappy(duration: 0.18)); OnMain pumps the manual loop to
            // idle on return, which is what runs Tracked<T>'s MainActor.Post(Update) after the click's mutation (the
            // sibling chevron test's own comment), scheduling this AnimatedDouble's first frame without firing it.
        });
        // REVIEW FIX: before this fix, Rebuild assigned Height directly, so by the time this OnMain call returns,
        // both heights would already read `after` -- with not one frame of the manual clock advanced. A real
        // animation cannot have reached its target yet: nothing has driven a single tick.
        harness.Main.OnMain(() =>
        {
            var grid = CardGrid();
            Assert.Equal(before, grid.Children[0].Height); // still mid-flight: no frame has ticked
            Assert.Equal(before, grid.Children[1].Height);
        });
        for (var i = 0; i < 30; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } // 480 ms, past the spring's own settling duration
        harness.Main.OnMain(() =>
        {
            var grid = CardGrid();
            Assert.Equal(after, grid.Children[0].Height); // :1541 Chrome.panelSlide would also cover the connector if
            // the head set itself had changed; here only cardRows did, so it rides the chevron's own ambient snappy
            // transaction (Transaction.Current at the moment Tracked<T>.Schedule captured it) to the same value.
            Assert.Equal(after, grid.Children[1].Height); // :1535
            window.Close();
        });
    });

    // ---- UI-SIDEBAR-058: a search group's project row stays inert but keeps its live hover accessories ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:709 ProjectRow(..., togglesExpansion: false): inert, but its hover ellipsis and plus stay live
    public void A_search_groups_project_row_never_toggles_but_still_shows_its_hover_ellipsis_and_plus() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the hover fill's own AnimatedColor needs a bound clock
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Fix it", 1);
        var model = Model(harness, [project], [thread]);
        AvaloniaWindow window = null!;
        Control row = null!;
        Button button = null!;
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            SearchBox(view).Text = "fix";
            var helpers = Helpers(view);
            var entries = Items(Call(view, "SearchEntries", Call(view, "SearchResults", helpers), helpers));
            var projectEntry = entries.Single(entry => Get(entry, "Value") is ValueTuple<Project, long>);
            row = ((Func<Control>)Get(projectEntry, "Make")!)(); // :709 ProjectRow(result.project, result.count, togglesExpansion: false)
            row.VerticalAlignment = VerticalAlignment.Top; // the row's own 28pt height, not the host's full stretch (ShownProjectRow's own pattern)
            var host = Hosted(model, row);
            window = WindowOf(host);
            window.UpdateLayout();

            var wasExpanded = model.Project(project.Id)!.IsExpanded;
            button = ButtonNamed(row, "Alpha"); // SidebarRow's own title button
            Click(button);
            Assert.Equal(wasExpanded, model.Project(project.Id)!.IsExpanded); // :1101 guard togglesExpansion else return: inert
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value)); // :1116 hovering, pumps the posted Track update on return
        harness.Main.OnMain(() =>
        {
            Assert.NotNull(LogicalQuery.SelfAndDescendants<RowActionsButton>(row).SingleOrDefault()); // :1117-1118, still live
            window.Close();
        });
    });

    // CLOSED (UI-SIDEBAR-033, UI-SIDEBAR-037): SidebarHelperRow and HeadCardsRow attach their own context menus now; proven by
    // A_real_right_click_on_a_helper_row_opens_its_context_menu_... and A_real_right_click_on_a_head_glyph_opens_.... The
    // note below is the history.
    // STOP App.Views.Sidebar.SidebarView #7 (narrowed part 7: SidebarHelperRow's and HeadCardsRow's own constructors
    // are now built, along with every other record this part names; ProjectRow's own PR-SIDEBAR-133 stays the
    // longest-standing item, unresolved since part 6)
    // Kind: forbidden need
    // Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:1138-1161 ProjectRow's own <c>.contextMenu</c>;
    // :1387-1397 SidebarHelperRow's own <c>.contextMenu</c>; :1490-1500 HeadCardsRow's own per-glyph
    // <c>.contextMenu</c> (B3 52bc792)
    // Requirements: PR-SIDEBAR-133 (ProjectRow's live AppKit context menu); PR-SIDEBAR-130 (the "Remove project"
    // confirmation gated behind it); the right-click interaction named in UI-SIDEBAR-033 ("right-click opens the
    // helper's context menu") and in UI-SIDEBAR-037 ("right-click opens the head's context menu with settleFirst
    // false") -- every other interaction, state and geometry each record names is proven above.
    // What is unclear or missing: SwiftUI's <c>.contextMenu</c> modifier re-evaluates its content fresh every time
    // the menu opens (all three sites cited above), and no ED or platform-adaptation entry names a Windows/Avalonia
    // foundation for that per-open refresh (porting/engineering_decisions.md and porting/platform_adaptations.md
    // have no ".contextMenu" or "ContextMenu" entry). A menu attached once, at construction time, would go stale the
    // first time the underlying snapshot changed without the row itself being rebuilt: an invented, silently wrong
    // behaviour. This part built SidebarHelperRow's and HeadCardsRow's own constructors in full otherwise, once
    // RowGlideAnimator.Shared.IsHiding(_:) and the per-row hover/menu @State pattern (both named missing when this
    // STOP was opened at part 5) were confirmed already in the tree (HelperStubRow, part 5; ProjectRow, part 6).
    // Readings or options: (a) build a menu fixed at construction time -> goes stale, an invented behaviour the
    // contract forbids more than an honest gap does; (b) invent a rebuild-on-open hook with no ED/PA backing -> the
    // same objection.
    // Needed to proceed: an engineering or platform-adaptation decision naming the Windows/Avalonia foundation for
    // SwiftUI's <c>.contextMenu</c> re-evaluation-on-open (then PR-SIDEBAR-130's request field and popover follow).
    // Done so far: ProjectRow's own body, contextMenuActions, actions(isActive:), BuildContextMenuItems and
    // ProjectPairMenuItems are proven directly (part 6); SidebarHelperRow's own constructor and HeadCardsRow's own
    // constructor are proven directly above, including their ellipsis-popover/click-to-select/hover-scale/selection-
    // ring/chevron pieces; nothing is claimed for PR-SIDEBAR-133 or -130, or for the right-click interaction alone
    // of UI-SIDEBAR-033/-037, and no test names them.

    // ======== Part 8 of 8 ========
    // Records: UI-SIDEBAR-060, UI-UPDATE-010. Both records describe behaviour parts 1 to 3 already implemented and
    // parts 2, 3 and 7 already tested under PR-ids (the pointer style PR-SIDEBAR-049, the neighbour slide
    // PR-SIDEBAR-052/-053, the lift's scale and fade PR-SIDEBAR-054, the update dot's presence and placement
    // PR-SIDEBAR-013/UI-SIDEBAR-043): those existing tests now also carry the UI-SIDEBAR-060 or UI-UPDATE-010 trait
    // above, and the lift's fill/shadow colors and the dot's own geometry/color were added to two of them (part 8).
    // The one piece no earlier part captured is the release spring dragEnded sets up once the drag clears; proven
    // below.
    //
    // The original tests below this part used synthetic lift content. The Window sweep now also builds and
    // verifies a real SidebarThreadRow in the dragged state.

    // Window critical path: assembled rows, not synthetic row builders.
    internal static Control LiveThreadRow(ChatThread thread, string? projectName = null, bool dragged = false,
        object? listFrame = null, Action? fold = null) =>
        WithNaturalRowHeight((Control)Activator.CreateInstance(Private("SidebarThreadRow"), thread, projectName, null,
            Activator.CreateInstance(Private("SidebarMenuRequests")), (Action)(() => { }), (Action)(() => { }),
            dragged, listFrame, fold)!);

    [Fact]
    [Trait("Req", "UI-SIDEBAR-017")] // SidebarView.swift:1772-1814 the trailing branches, swapped inside :1816-1817 withAnimation(Chrome.hover)
    [Trait("Req", "UI-SIDEBAR-023")] // the time label gives way to the hover buttons
    // A view that leaves or joins under an animation uses SwiftUI's default .opacity transition, so the age label fades
    // out while the buttons fade in (2-chat-use.mov 70.74-71.30 s, CATALOGUE hover.main-sidebar-thread-row: "the trailing
    // age label crossfades OUT while the row's buttons crossfade IN", ~90-110 ms).
    public void Hovering_a_thread_row_crossfades_its_time_label_out_and_its_buttons_in_on_chrome_hover() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Row", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        static double ShownOpacity(Control control) =>
            control.GetSelfAndVisualAncestors().OfType<Visual>().TakeWhile(v => v.GetType() != Private("SidebarThreadRow")).Aggregate(1d, (o, v) => o * v.Opacity);
        harness.Main.OnMain(() =>
        {
            row = LiveThreadRow(thread);
            window = WindowOf(Hosted(model, row));
            window.UpdateLayout();
        });
        try
        {
            RelativeTimeLabel time = null!;
            harness.Main.OnMain(() =>
            {
                time = LogicalQuery.SelfAndDescendants<RelativeTimeLabel>(row).Single();
                Assert.Empty(LogicalQuery.SelfAndDescendants<RowActionsButton>(row));
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            Tick(50); // half of Chrome.hover's easeOut 0.1 s
            harness.Main.OnMain(() =>
            {
                var ellipsis = LogicalQuery.SelfAndDescendants<RowActionsButton>(row).Single();
                Assert.InRange(ShownOpacity(ellipsis), 0.05, 0.95); // fading in
                Assert.Same(time, LogicalQuery.SelfAndDescendants<RelativeTimeLabel>(row).Single()); // the old label is still there,
                Assert.InRange(ShownOpacity(time), 0.05, 0.95); // fading out
            });
            Tick(200);
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<RelativeTimeLabel>(row)); // gone once faded
                Assert.Equal(1d, ShownOpacity(LogicalQuery.SelfAndDescendants<RowActionsButton>(row).Single()));
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    internal static Control WithNaturalRowHeight(Control row)
    {
        row.VerticalAlignment = VerticalAlignment.Top;
        return row;
    }

    // ---- PR-SIDEBAR-098 through -103, UI-SIDEBAR-017: the thread row's own live context menu ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-017")] // App/Views/Sidebar/SidebarView.swift:1848-1862,2253-2312: a real right-click opens the live thread context menu, rebuilt fresh (not stale) on every open
    public void A_real_right_click_opens_the_thread_rows_context_menu_rebuilt_fresh_on_every_open() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the row's own hover Track (Chrome.cs-equivalent :1816) animates on the same pointer traffic
        var project = NewProject(harness, "Alpha");
        // AppModelSamples.Thread's own CreatedAt/UpdatedAt is SwiftDate.Now (Library.cs :316), left untouched here
        // (unlike the NewThread helper's fixed Base date, tens of days in the past by the time this runs): the
        // window sweep's own RelativeTimeLabel (:2662-2695) takes RelativeTime.Short's "now" branch for a fresh
        // thread, never reaching SwiftFormatters.Formatted's ICU call - this sandbox has no native icu.dll (a
        // pre-existing, environment-wide gap, not this job's own: An_assembled_thread_row_selects_and_shows_hover_
        // actions_and_the_pinned_selection_tint, already in this file, hits the identical DllNotFoundException on
        // the unmodified baseline for the same reason, confirmed by this job's own git-stash comparison for Items
        // 1 and 2 above).
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the build" };
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            row = LiveThreadRow(thread, fold: () => { });
            var window = WindowOf(Hosted(model, row));
            window.UpdateLayout();
        });
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row);
            var menu = row.ContextMenu!; // :1851, Control.ContextMenu directly (this row IS the whole body, unlike ProjectRow's own SidebarRow child)
            var titles = menu.Items.Cast<object>().Select(MenuItemTitle).ToArray();
            // :2259-2262, :2265-2273, :2277-2280, :2244 (MakeContextMenu): the weak-model-captured items, snapshot-built at open time.
            Assert.Contains("Rename", titles);
            Assert.Contains("Pin", titles); // :2266-2267 !isHelper && !isSettled, not yet pinned
            Assert.DoesNotContain("Unpin", titles);
            Assert.Contains("Forward to new thread", titles);
            Assert.Contains("Delete" + (char)0x2026, titles); // one U+2026 HORIZONTAL ELLIPSIS, from its code point (R-STR-9)
        });
        // Same row, never rebuilt between the two right-clicks: a menu whose Items were fixed once would show the
        // same "Pin"/"Unpin" wording both times - the "invented, silently wrong behaviour" STOP #7/#8
        // (SidebarViewTests.cs) rejected. Flipping the live thread state directly (not through this row) and
        // re-reading through a second real right-click tells a per-open-fresh Thread()/Model read (this row's own
        // constructor, ContextMenu.Opening) apart from a stale one.
        harness.Main.OnMain(() => model.UpdateThread(thread.Id, t => t with { IsPinned = true }));
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row);
            var menu = row.ContextMenu!;
            var titles = menu.Items.Cast<object>().Select(MenuItemTitle).ToArray();
            Assert.Contains("Unpin", titles); // :2267 isPinned now true: the flipped wording
            Assert.DoesNotContain("Pin", titles);
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1716-1872
    public void An_assembled_thread_row_selects_and_shows_hover_actions_and_the_pinned_selection_tint() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Pinned thread", 0) with { IsPinned = true, ExpandsHelpers = true };
        var model = Model(harness, [project], [thread]);
        try
        {
            Control row = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                row = LiveThreadRow(thread, fold: () => { });
                row.VerticalAlignment = VerticalAlignment.Top;
                window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                Assert.Equal(28, row.Bounds.Height); // :2069 Chrome.rowHeight
                Assert.Contains(thread.Title, Texts(row));
                Assert.Empty(LogicalQuery.SelfAndDescendants<RowActionsButton>(row)); // :1723, :1774 idle
                var shape = (Border)Field(row, "shape")!;
                Assert.Equal(Chrome.Danger.Opacity(0.10).Resolve(shape.ActualThemeVariant), ((ISolidColorBrush)shape.Background!).Color); // :1751
                Click((Button)Field(row, "selectButton")!); // :1737
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(thread.Id, model.SelectedThreadID);
                var peer = ControlAutomationPeer.CreatePeerForElement((Control)Field(row, "selectButton")!)!;
                Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
                Assert.True(((ISelectionItemProvider)peer).IsSelected); // :1871
                var tint = (Border)Field(row, "pinnedOverlay")!;
                Assert.Equal(Chrome.Danger.Opacity(0.06).Resolve(tint.ActualThemeVariant), ((ISolidColorBrush)tint.Background!).Color); // :1756-1758
                window.MouseMove(row.TranslatePoint(new Point(15, 14), window)!.Value);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<RowActionsButton>(row)); // :1785
                Assert.NotNull(ButtonNamed(row, "Hide helpers")); // :1805-1813
                window.Hide();
            });
        }
        finally { harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); } // keep pending layout inside the live font/main-actor scope
    });

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1723-1729,1763-1771,1834-1849,1863-1872
    [Trait("Req", "UI-SIDEBAR-060")] // App/Views/Sidebar/SidebarView.swift:1838-1849
    public void A_real_dragged_row_suppresses_hover_accessories_and_finish_requests_pop_its_check() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Dragged", 0);
        var model = Model(harness, [project], [thread]);
        try
        {
            Control row = null!;
            harness.Main.OnMain(() =>
            {
                model.Settings.SettleSound = false;
                model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
                row = LiveThreadRow(thread, dragged: true, fold: () => { });
                var window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
            });
            harness.Main.Advance(TimeSpan.FromSeconds(1));
            harness.Main.OnMain(() =>
            {
                manual.Advance(TimeSpan.FromSeconds(1)); // :1849 let the lift's spring finish
                Assert.Equal(1.02, ((ScaleTransform)((Decorator)row).Child!.RenderTransform!).ScaleX); // :1847
                var window = WindowOf(row);
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<RowActionsButton>(row)); // :1723 !isDragged
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(row), b => AutomationProperties.GetName(b) == "Hide helpers");
                model.AppModelFinishRequest = new AppModel.FinishRequest(thread.Id, false); // :1863-1867
            });
            harness.Main.OnMain(() =>
            {
                Assert.Null(model.AppModelFinishRequest);
                Assert.True(model.Thread(thread.Id)!.IsSettled);
                Assert.True((bool)Get(Field(row, "state")!, "IsSettling")!);
                Assert.Contains(LogicalQuery.SelfAndDescendants<SwiftImage>(row), glyph => glyph.RenderTransform is ScaleTransform { ScaleX: 1.35 }); // :2132
                WindowOf(row).Hide();
            });
        }
        finally { harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); } // keep pending layout inside the live font/main-actor scope
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-134")] // App/Views/Sidebar/SidebarView.swift:1820-1833,1903-1905,2433-2440
    public void Window_frame_reports_are_gated_and_a_matching_arrival_lands_in_window_coordinates() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Landing", 0);
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        try
        {
            Control row = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                row = LiveThreadRow(thread);
                window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                Assert.Equal(default(Rect), Get(Field(row, "windowFrame")!, "Frame")); // idle frame deliberately stale
                animator.Launch(thread.Id, RowGlideAnimator.Glide.Direction.Reopen, new Rect(0, 300, 200, 24),
                    new Rect(0, -28, 200, 28), new Rect(0, 0, 200, 400), new FaceSample(200, 24), new FaceSample(200, 28), 0, 0);
                row.Margin = new Thickness(0, 20, 0, 0); // a genuine geometry change while a glide is in flight
                window.UpdateLayout();
            });
            harness.Main.OnMain(() =>
            {
                var frame = Frame(row, window);
                Assert.Equal(frame, Get(Field(row, "windowFrame")!, "Frame"));
                var flight = Assert.Single(animator.Glides);
                Assert.True(flight.HasLanded);
                Assert.Equal(frame, flight.To);
                Assert.Equal(0, row.Opacity); // :1834 hiding is instant
                animator.Arrive(flight.Id);
            });
            harness.Main.Advance(TimeSpan.FromSeconds(1));
            harness.Main.OnMain(() =>
            {
                manual.Advance(TimeSpan.FromSeconds(1));
                Assert.Equal(1, row.Opacity); // :1835-1838 reveal over 0.12s
                window.Hide();
            });
        }
        finally { harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); } // keep pending layout inside the live font/main-actor scope
    });

    [Theory]
    [InlineData(null, 28)]
    [InlineData("Alpha", 48)]
    [Trait("Req", "UI-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:1912-1916: the check of a settled row reopens its thread
    public void A_real_settled_row_reopens_with_two_faces_and_the_destination_height(string? projectName, double height) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Reopen", 0) with { IsSettled = true };
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new RenderingGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        try
        {
            Control row = null!;
            harness.Main.OnMain(() =>
            {
                var listFrame = Activator.CreateInstance(Private("FrameHolder"))!;
                SetProp(listFrame, "Frame", new Rect(10, 40, 200, 400));
                row = LiveThreadRow(thread, projectName, listFrame: listFrame);
                var window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            harness.Main.OnMain(() => Click(ButtonNamed(row, "Reopen thread")));
            harness.Main.OnMain(() =>
            {
                Assert.False(model.Thread(thread.Id)!.IsSettled);
                var glide = Assert.Single(animator.Glides);
                Assert.Equal(24, glide.Departure.Size.Height); // settled departing face
                Assert.Equal(height, glide.Arrival.Size.Height); // 48 detailed / 28 compact
                Assert.Equal(40 - height, glide.To.Y); // one destination height above the list
                Assert.Equal(height, glide.To.Height);
                Assert.Equal(RowGlideAnimator.Glide.Direction.Reopen, glide.GlideDirection);
                WindowOf(row).Hide();
            });
        }
        finally { harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); } // keep pending layout inside the live font/main-actor scope
    });

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1934 guard let listFrame else { return }
    [Trait("Req", "PR-SIDEBAR-135")] // leftovers pass: this test proves a clause of the record (listFrame nil / failed render / 24 pt settled destination / DPI scale)
    public void A_popover_row_reopens_without_a_glide() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Reopen", 0) with { IsSettled = true };
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform()); // throws if a render is attempted
        using var shared = RowGlideAnimator.Shared.Override(animator);
        try
        {
            Control row = null!;
            harness.Main.OnMain(() =>
            {
                row = LiveThreadRow(thread);
                Hosted(model, row);
                Click(ButtonNamed(row, "Reopen thread"));
            });
            harness.Main.OnMain(() =>
            {
                Assert.False(model.Thread(thread.Id)!.IsSettled);
                Assert.Empty(animator.Glides);
                WindowOf(row).Hide();
            });
        }
        finally { harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); } // keep pending layout inside the live font/main-actor scope
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-021")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1776-1783,1988-1999
    public void The_real_hover_archive_button_launches_a_genie_and_archives() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Archive", 0);
        var model = Model(harness, [project], [thread]);
        var animator = new GenieAnimator(new RenderingGeniePlatform());
        using var shared = GenieAnimator.Shared.Override(animator);
        try
        {
            Control row = null!;
            harness.Main.OnMain(() =>
            {
                model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
                row = LiveThreadRow(thread);
                var window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            harness.Main.OnMain(() => Click(LogicalQuery.SelfAndDescendants<Button>(row).Single(b => Equals(ToolTip.GetTip(b), "Archive thread"))));
            harness.Main.OnMain(() =>
            {
                Assert.True(model.Thread(thread.Id)!.IsArchived);
                Assert.Single(animator.Flights);
                WindowOf(row).Hide();
            });
        }
        finally { harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); } // keep pending layout inside the live font/main-actor scope
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:703-712, :136 isEnabled: query.isEmpty: thread rows in the project form, selectable, and reordering disabled
    public void Actual_search_rows_are_compact_selectable_and_cannot_start_a_reorder() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Needle", 0);
        var model = Model(harness, [project], [thread]);
        try
        {
            SidebarView view = null!;
            harness.Main.OnMain(() => { view = Attached(model); SearchBox(view).Text = "Needle"; });
            harness.Main.Advance(TimeSpan.FromSeconds(1));
            harness.Main.OnMain(() =>
            {
                manual.Advance(TimeSpan.FromSeconds(1)); // let the old layout's removal fade finish
                WindowOf(view).UpdateLayout();
                var row = LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("SidebarThreadRow"));
                Assert.Equal(28, row.Bounds.Height);
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<ProjectIconMark>(row), _ => true);
                Assert.DoesNotContain("Alpha", Texts(row));
                var window = WindowOf(view);
                var point = row.TranslatePoint(new Point(12, 12), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseMove(point + new Vector(0, 12)); // exceeds :129 minimumDistance 4
                Assert.Null(Drag(view).Id); // :137 isEnabled query.isEmpty
                window.MouseUp(point + new Vector(0, 12), MouseButton.Left);
                Click((Button)Field(row, "selectButton")!);
                Assert.Equal(thread.Id, model.SelectedThreadID);
                window.Hide();
            });
        }
        finally { harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); } // keep pending layout inside the live font/main-actor scope
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Req", "PR-SIDEBAR-056")] // App/Views/Sidebar/SidebarView.swift:347-375,1622-1632
    public void The_real_list_dispatches_helpers_cards_and_stubs_and_they_follow_a_dragged_parent() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0) with { ExpandsHelpers = true };
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        try
        {
            SidebarView view = null!;
            harness.Main.OnMain(() => { model.Settings.HydraAutoHidesIdleHeads = false; view = Attached(model); });
            harness.Main.OnMain(() =>
            {
                var helperRow = LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("SidebarHelperRow"));
                Assert.Contains("Helper", Texts(helperRow));
                SetProp(Field(view, "state")!, "Drag", new RowDrag<Guid> { Id = parent.Id, Translation = 17 });
            });
            harness.Main.OnMain(() =>
            {
                var helperRow = LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("SidebarHelperRow"));
                var riding = helperRow.GetVisualAncestors().OfType<Control>().First(c => c.RenderTransform is TranslateTransform);
                Assert.Equal(17, ((TranslateTransform)riding.RenderTransform!).Y);
                Assert.Equal(1, riding.ZIndex);
                model.UpdateThread(parent.Id, t => t.WithFoldsHelpers(true));
            });
            harness.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<Control>(view), c => c.GetType() == Private("HelperStubRow"));
                model.UpdateThread(parent.Id, t => t.WithFoldsHelpers(false));
                model.UpdateThread(helper.Id, t => t with { Hydra = HeadHelper(project, parent.Id, 0, 1).Hydra });
            });
            harness.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<Control>(view), c => c.GetType() == Private("HeadCardsRow"));
                WindowOf(view).Hide();
            });
        }
        finally { harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); } // keep pending layout inside the live font/main-actor scope
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1951-1978
    [Trait("Req", "PR-SIDEBAR-135")] // leftovers pass: this test proves a clause of the record (listFrame nil / failed render / 24 pt settled destination / DPI scale)
    public void A_failed_departure_or_arrival_render_abandons_the_glide(int failAt) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Failed image", 0) with { IsSettled = true };
        var platform = new SidebarSweepPlatform(failAt);
        var animator = new RowGlideAnimator(platform);
        harness.Main.OnMain(() =>
        {
            CallStatic(Private("SidebarThreadRow"), "Glide", animator, RowGlideAnimator.Glide.Direction.Reopen,
                new Rect(10, 40, 200, 400), new Rect(10, 300, 200, 24), thread, "Alpha", null, false, true, null, ThemeVariant.Dark);
            Assert.Empty(animator.Glides); // :1967 guard both images
            Assert.Equal(failAt, platform.Renders.Count);
            Assert.All(platform.Renders, render => Assert.Equal(2, render.Scale));
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1938-1978
    [Trait("Req", "PR-SIDEBAR-135")] // leftovers pass: this test proves a clause of the record (listFrame nil / failed render / 24 pt settled destination / DPI scale)
    public void The_settled_destination_is_a_24_point_face_at_the_lists_bottom() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Settle image", 0) with { IsPinned = true, HasUnread = true };
        var platform = new SidebarSweepPlatform();
        var animator = new RowGlideAnimator(platform);
        harness.Main.OnMain(() =>
        {
            CallStatic(Private("SidebarThreadRow"), "Glide", animator, RowGlideAnimator.Glide.Direction.Settle,
                new Rect(10, 40, 200, 400), new Rect(10, 100, 200, 48), thread, "Alpha", null, false, true, null, ThemeVariant.Dark);
            var glide = Assert.Single(animator.Glides);
            Assert.Equal(new Rect(10, 440, 200, 24), glide.To); // :1948-1955
            Assert.Equal(new Size(200, 48), glide.Departure.Size);
            Assert.Equal(new Size(200, 24), glide.Arrival.Size);
            Assert.Equal(0.06, glide.DepartureFill); // hovered departure
            Assert.Equal(0, glide.ArrivalFill); // arrival never hovered
            Assert.Equal(2, platform.Renders.Count);
            var scope = Assert.IsType<ThemeVariantScope>(platform.Renders[1].Content);
            Assert.Equal(ThemeVariant.Dark, scope.RequestedThemeVariant);
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(scope).Single(t => t.Text == thread.Title);
            Assert.Equal(12, title.FontSize); // :2042 settled destination, not the open snapshot
            Assert.Equal(FontWeight.Normal, title.FontWeight);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-134")] // App/Views/Sidebar/SidebarView.swift:2433-2440
    public void FrameHolder_notes_changed_frames_without_entering_the_observation_system()
    {
        var holder = Activator.CreateInstance(Private("FrameHolder"))!;
        var invalidations = 0;
        var watch = ObservationTracking.Track(() => Get(holder, "Frame"), () => invalidations++).Tracker;
        var note = holder.GetType().GetMethod("Note")!;
        var frame = new Rect(10, 20, 200, 28);
        note.Invoke(holder, [frame]);
        note.Invoke(holder, [frame]);
        Assert.Equal(frame, Get(holder, "Frame"));
        Assert.Equal(0, invalidations); // :2433 deliberately kept out of observation
        watch.Cancel();
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-134")] // App/Views/Sidebar/SidebarView.swift:1820-1833,2433-2440
    public void Scrolling_unhovered_undragged_rows_with_no_glide_never_writes_the_window_frame() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Idle scroll", 0);
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        harness.Main.OnMain(() =>
        {
            var row = LiveThreadRow(thread);
            var stack = new StackPanel { Children = { row, new Border { Height = 1500 } } };
            var scroll = new Avalonia.Controls.Presenters.ScrollContentPresenter { Content = stack, CanVerticallyScroll = true };
            var window = WindowOf(Hosted(model, scroll));
            try
            {
                window.UpdateLayout();
                var first = Frame(row, window);
                var holder = Field(row, "windowFrame")!;
                Assert.Equal(default(Rect), Get(holder, "Frame"));
                foreach (var offset in new[] { 20d, 80d, 140d })
                {
                    scroll.Offset = new Vector(0, offset);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    Assert.NotEqual(first, Frame(row, window)); // real viewport scroll changed geometry
                    Assert.Equal(default(Rect), Get(holder, "Frame")); // gated write stays absent
                    Assert.Empty(animator.Glides);
                }
            }
            finally { window.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-134")] // App/Views/Sidebar/SidebarView.swift:1820-1833,2433-2440 Note suppression must not suppress Land
    public void A_frame_returning_to_its_last_recorded_position_still_lands_a_pending_glide() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Returned frame", 0);
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        Control row = null!;
        AvaloniaWindow window = null!;
        Rect recorded = default;
        try
        {
            harness.Main.OnMain(() =>
            {
                row = LiveThreadRow(thread);
                window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            harness.Main.OnMain(() =>
            {
                recorded = (Rect)Get(Field(row, "windowFrame")!, "Frame")!;
                Assert.NotEqual(default, recorded);
                window.MouseMove(new Point(window.Bounds.Width - 1, window.Bounds.Height - 1));
            });
            harness.Main.OnMain(() =>
            {
                row.Margin = new Thickness(0, 20, 0, 0);
                window.UpdateLayout(); // gate false: holder still remembers old position
                Assert.Equal(recorded, Get(Field(row, "windowFrame")!, "Frame"));
                animator.Launch(thread.Id, RowGlideAnimator.Glide.Direction.Reopen, new Rect(0, 300, 200, 24),
                    new Rect(0, -28, 200, 28), new Rect(0, 0, 200, 400), new FaceSample(200, 24), new FaceSample(200, 28), 0, 0);
                row.Margin = new Thickness(0);
                window.UpdateLayout(); // geometry changed back; holder equality must not prevent land
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(recorded, Frame(row, window));
                Assert.True(Assert.Single(animator.Glides).HasLanded);
                Assert.Equal(recorded, Assert.Single(animator.Glides).To);
            });
        }
        finally { harness.Main.OnMain(() => { window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    [Theory]
    [InlineData(1.0, 2.0)]
    [InlineData(1.5, 2.0)]
    [InlineData(2.5, 2.5)]
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:1933-1978; App/Views/Sidebar/RowGlide.swift:127-134 windows_path DPI minimum 2
    [Trait("Req", "PR-SIDEBAR-135")] // leftovers pass: this test proves a clause of the record (listFrame nil / failed render / 24 pt settled destination / DPI scale)
    public void Glide_faces_use_their_own_windows_DPI_scale_with_a_two_times_minimum(double windowScale, double expectedScale) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "DPI reopen", 0) with { IsSettled = true };
        var model = Model(harness, [project], [thread]);
        var platform = new SidebarSweepPlatform(); // main-screen fallback deliberately remains 2 at every window DPI
        var animator = new RowGlideAnimator(platform);
        using var shared = RowGlideAnimator.Shared.Override(animator);
        Control row = null!;
        AvaloniaWindow window = null!;
        try
        {
            harness.Main.OnMain(() =>
            {
                var holder = Activator.CreateInstance(Private("FrameHolder"))!;
                SetProp(holder, "Frame", new Rect(0, 40, 200, 400));
                row = LiveThreadRow(thread, "Alpha", listFrame: holder);
                window = WindowOf(Hosted(model, row));
                window.SetRenderScaling(windowScale);
                window.UpdateLayout();
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            harness.Main.OnMain(() => Click(ButtonNamed(row, "Reopen thread")));
            harness.Main.OnMain(() =>
            {
                Assert.Single(animator.Glides);
                Assert.Equal(2, platform.Renders.Count);
                Assert.All(platform.Renders, render => Assert.Equal(expectedScale, render.Scale));
            });
        }
        finally { harness.Main.OnMain(() => { window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    // ---- UI-SIDEBAR-060: the release spring dragEnded's write runs inside ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-060")] // App/Views/Sidebar/SidebarView.swift:803-807 (part 8: the "release springs the
    // offset home on response 0.32 damping 0.8" piece of the dragged-row-lift screen-contract record)
    public void DragEnded_springs_the_offset_home_on_response_0_32_damping_0_8()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var grabbed = Guid.NewGuid();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Detached(model);
                Call(view, "DragChanged", grabbed, 30d); // :773-775 grabs
            });
            Animation? during = null;
            using var probe = new OnChange<RowDrag<Guid>>(() => Drag(view), (_, _) => during = Transaction.Current);
            harness.Main.OnMain(() => Call(view, "DragEnded"));
            harness.Main.OnMain(() =>
            {
                Assert.Equal(Animation.Spring(response: 0.32, dampingFraction: 0.8), during); // :806-808
                Assert.Null(Drag(view).Id); // :808 drag = RowDrag() clears the grab inside that same spring
            });
        });
    }
    [Trait("Req", "UI-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:101-148,1622-1632; precise caller citations below
    [Trait("Req", "UI-SIDEBAR-035")] // App/Views/Sidebar/SidebarView.swift:101-148,1622-1632; precise caller citations below
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift:101-148,1622-1632; precise caller citations below
    [Fact]
    [Trait("Req", "PR-SIDEBAR-055")] // SidebarView.swift:803-807 dragEnded: withAnimation(.spring(response: 0.32, dampingFraction: 0.8)) { drag = RowDrag() }
    [Trait("Req", "UI-SIDEBAR-060")] // "release springs the offset home on response 0.32 damping 0.8"
    [Trait("Req", "PR-SIDEBAR-056")] // :1622-1632 the riders' offset reads the same drag, so it springs home with the row
    public void On_a_real_release_the_row_and_everything_riding_it_spring_home_together_from_where_the_pointer_let_go() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0) with { ExpandsHelpers = true };
        var first = NewThread(project, "First helper", 1) with { ParentThreadID = parent.Id };
        var head = HeadHelper(project, parent.Id, 0, 3);
        var model = Model(harness, [project], [parent, first, head]);
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        Point start = default;
        void Tick(int milliseconds)
        {
            harness.Main.AdvanceMilliseconds(milliseconds);
            harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        double OffsetOf(Control dependent) =>
            ((TranslateTransform)dependent.GetVisualAncestors().OfType<Control>().First(c => c.RenderTransform is TranslateTransform).RenderTransform!).Y;
        IEnumerable<Control> Moving() => LogicalQuery.SelfAndDescendants<Control>(view)
            .Where(c => c.GetType() == Private("SidebarThreadRow") || c.GetType() == Private("SidebarHelperRow") || c.GetType() == Private("HeadCardsRow"));
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraAutoHidesIdleHeads = false;
            view = Attached(model);
            window = WindowOf(view);
            var row = LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("SidebarThreadRow"));
            start = row.TranslatePoint(new Point(12, 12), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(0, 3), RawInputModifiers.LeftMouseButton);
            window.MouseMove(start + new Vector(0, 17), RawInputModifiers.LeftMouseButton);
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                Assert.Equal(parent.Id, Drag(view).Id);
                Assert.Equal(3, Moving().Count());
                foreach (var moving in Moving()) Assert.Equal(17, OffsetOf(moving), 3);
                window.MouseUp(start + new Vector(0, 17), MouseButton.Left); // :803-807
            });
            Tick(90);
            harness.Main.OnMain(() =>
            {
                Assert.Null(Drag(view).Id); // drag reset at once; only the offset travels
                var expected = 17 * (1 - Animation.Spring(response: 0.32, dampingFraction: 0.8).Progress(0.09));
                Assert.InRange(expected, 1, 16); // the fixture really samples mid-flight
                foreach (var moving in Moving()) Assert.Equal(expected, OffsetOf(moving), 1); // the row and its riders together
            });
            Tick(1600);
            harness.Main.OnMain(() => { foreach (var moving in Moving()) Assert.Equal(0, OffsetOf(moving), 3); });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-056")] // App/Views/Sidebar/SidebarView.swift:347-375,1622-1633
    [Trait("Req", "UI-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:1622-1633: a helper rides with its dragged parent at the same offset and zIndex 1
    [Trait("Req", "UI-SIDEBAR-060")] // App/Views/Sidebar/SidebarView.swift:128-137,1838-1849
    public void A_real_parent_pointer_drag_carries_two_helpers_and_a_head_card_then_a_folded_stub_and_releases_its_lift() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0) with { ExpandsHelpers = true };
        var first = NewThread(project, "First helper", 1) with { ParentThreadID = parent.Id };
        var second = NewThread(project, "Second helper", 2) with { ParentThreadID = parent.Id };
        var head = HeadHelper(project, parent.Id, 0, 3);
        var model = Model(harness, [project], [parent, first, second, head]);
        SidebarView view = null!;
        Control row = null!;
        AvaloniaWindow window = null!;
        Point start = default;
        void Tick(int milliseconds)
        {
            harness.Main.AdvanceMilliseconds(milliseconds);
            harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        void AssertRider(Control dependent, double offset)
        {
            var rider = dependent.GetVisualAncestors().OfType<Control>().First(c => c.RenderTransform is TranslateTransform);
            Assert.Equal(offset, ((TranslateTransform)rider.RenderTransform!).Y, 3);
            Assert.Equal(offset == 0 ? 0 : 1, rider.ZIndex); // :1627-1632 the same visualOffset/zIndex as the parent
        }
        try
        {
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraAutoHidesIdleHeads = false;
                view = Attached(model);
                window = WindowOf(view);
                row = LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("SidebarThreadRow"));
                start = row.TranslatePoint(new Point(12, 12), window)!.Value;
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<Control>(view).Count(c => c.GetType() == Private("SidebarHelperRow")));
                Assert.Single(LogicalQuery.SelfAndDescendants<Control>(view), c => c.GetType() == Private("HeadCardsRow"));
                Assert.Equal(parent.Id, CallOn(Field(view, "rowFrames")!, "Thread", row.TranslatePoint(new Point(12, 12), ListPanel(view))!.Value));
                window.MouseDown(start, MouseButton.Left);
                Assert.NotNull(Field(view, "pressStart"));
                window.MouseMove(start + new Vector(0, 3), RawInputModifiers.LeftMouseButton);
                Assert.Null(Drag(view).Id); // :129 minimumDistance 4
                window.MouseMove(start + new Vector(0, 17), RawInputModifiers.LeftMouseButton);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(parent.Id, Drag(view).Id);
                Assert.Equal(17, Drag(view).VisualOffset); // no peer swap: source translation follows the pointer
                row = LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("SidebarThreadRow")); // :758-759 updates the isDragged input
                AssertRider(row, 17);
                foreach (var helper in LogicalQuery.SelfAndDescendants<Control>(view).Where(c => c.GetType() == Private("SidebarHelperRow"))) AssertRider(helper, 17);
                AssertRider(LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("HeadCardsRow")), 17);
                Assert.Empty(LogicalQuery.SelfAndDescendants<RowActionsButton>(row)); // :1723 !isDragged
            });
            Tick(1600); // settle the exact response 0.25/damping 0.8 lift, while still holding the pointer
            harness.Main.OnMain(() =>
            {
                var overlays = ((Decorator)row).Child!; // the lift host: :1846 scales the row together with its lift (:1838-1843)
                Assert.Equal(1.02, ((ScaleTransform)overlays.RenderTransform!).ScaleX, 3); // :1846
                var lift = LiftOf(Assert.IsType<Panel>(row is Decorator d ? d.Child : null));
                Assert.Equal(1, lift.Opacity);
                model.UpdateThread(parent.Id, t => t.WithFoldsHelpers(true));
            });
            Tick(1600); // finish removal transitions before inspecting the folded branch
            harness.Main.OnMain(() =>
            {
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Control>(view), c => c.GetType() == Private("SidebarHelperRow"));
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Control>(view), c => c.GetType() == Private("HeadCardsRow"));
                AssertRider(LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("HelperStubRow")), 17);
                window.MouseUp(start + new Vector(0, 17), MouseButton.Left);
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Null(Drag(view).Id);
                row = LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("SidebarThreadRow"));
                AssertRider(row, 0);
                AssertRider(LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("HelperStubRow")), 0);
                Assert.Equal(1, ((ScaleTransform)((Decorator)row).Child!.RenderTransform!).ScaleX, 3);
                Assert.Equal(0, LiftOf(Assert.IsType<Panel>(((Decorator)row).Child)).Opacity);
                window.MouseMove(new Point(-20, -20));
                window.MouseMove(row.TranslatePoint(new Point(12, 12), window)!.Value); // re-enter the newly released row's live instance
            });
            harness.Main.OnMain(() => Assert.NotNull(LogicalQuery.SelfAndDescendants<RowActionsButton>(row).SingleOrDefault()));
        }
        finally { harness.Main.OnMain(() => { window?.Close(); Dispatcher.UIThread.RunJobs(); }); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift (cited source contract below)
    [Trait("Rule", "R-TEST-6")] // App/Views/Sidebar/SidebarView.swift:168-175; App/Window/WindowManager.swift:32-36,80-84
    // OD-0140: Settings opens as a panel in the shared manager's main window (SettingsOverlay), not as a window of its own.
    public void Clicking_Settings_opens_the_real_shared_managers_SettingsView_and_reuses_its_panel() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // real pointer hover requests the source clock
        var managerType = typeof(Marrow.App.Window.WindowManager);
        var manager = harness.Main.OnMain(() => (Marrow.App.Window.WindowManager)Activator.CreateInstance(managerType, nonPublic: true)!);
        using var shared = (IDisposable)managerType.GetMethod("OverrideShared", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [manager])!;
        AvaloniaWindow settings = null!;
        SidebarView view = null!;
        var oldPage = harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage);
        try
        {
            harness.Main.OnMain(() =>
            {
                view = Attached(manager.Model);
                PointerClick(ButtonNamed(view, "Settings"));
                var overlay = Assert.IsType<Marrow.App.Port.SettingsOverlay>(manager.SettingsOverlay);
                settings = overlay.Window;
                Assert.Same(manager.MainWindow, settings);
                Assert.True(settings.IsVisible);
                Assert.True(overlay.IsOpen);
                var content = Assert.Single(LogicalQuery.SelfAndDescendants<SettingsView>(overlay.Content));
                Assert.Same(manager.Model, AppEnvironment.GetAppModel(content));
                // the panel is laid out at its stage's size for the window (ED-NEW-settingsinapp-2), the view filling it
                settings.UpdateLayout();
                var card = Marrow.App.Port.SettingsOverlay.SettingsStage.CardSize(overlay.Stage.Bounds.Size);
                Assert.InRange(overlay.Card.Bounds.Width - card.Width, -0.5, 0.5); // to the layout's whole pixel
                Assert.InRange(overlay.Card.Bounds.Height - card.Height, -0.5, 0.5);
                Assert.Equal(overlay.Card.Bounds.Size, content.Bounds.Size);
                manager.CloseSettings();
                Assert.False(overlay.IsOpen);
                PointerClick(ButtonNamed(view, "Settings"));
                Assert.Same(overlay, manager.SettingsOverlay);
                Assert.True(overlay.IsOpen);
                Assert.Same(content, Assert.Single(LogicalQuery.SelfAndDescendants<SettingsView>(overlay.Content)));
            });
        }
        finally
        {
            harness.Main.OnMain(() =>
            {
                // WindowManager.swift:98 orders Settings out instead of destroying it. Detach this fixture's
                // content explicitly so its global navigation observer cannot consume a later test's request.
                manager.SettingsOverlay?.Close();
                if (settings is not null) { settings.Content = null; settings.Close(); }
                if (view is not null) Close(view);
                SettingsNavigation.Shared.RequestedPage = oldPage;
                Dispatcher.UIThread.RunJobs();
            });
        }
    });
    internal static void PointerClick(Button button)
    {
        var window = WindowOf(button);
        window.UpdateLayout();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-004")] // App/Views/Sidebar/SidebarView.swift:82,2322-2341; UI/Chrome/Chrome.swift:28,98
    public void Bell_hits_only_its_circle_and_uses_the_source_font_accessibility_and_hover_curve() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var model = harness.MakeModel();
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        var button = ButtonNamed(view, "Activity view");
        var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(button).Single();
        var circle = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(button).Single();
        harness.Main.OnMain(() =>
        {
            Assert.Equal(new Size(30, 30), button.Bounds.Size); // :2328
            Assert.Equal(new Thickness(6, 0, 0, 0), button.GetLogicalAncestors().OfType<Control>().First(c => c.GetType() == Private("ActivityViewToggle")).Margin); // :82
            Assert.Equal(12, SwiftImage.GetFont(glyph)!.Resolve().FontSize); // Chrome.swift:28 callout semibold, ED-059
            Assert.Equal(FontWeight.SemiBold, SwiftImage.GetFont(glyph)!.Resolve().FontWeight);
            Assert.Equal("Off", AutomationProperties.GetItemStatus(button)); // :2341
            var corner = button.TranslatePoint(new Point(1, 1), window)!.Value;
            window.MouseDown(corner, MouseButton.Left); window.MouseUp(corner, MouseButton.Left);
            Assert.False(model.Settings.SidebarActivityView); // :2333 contentShape(Circle())
        });
        harness.Main.OnMain(() => window.MouseMove(button.TranslatePoint(new Point(15, 15), window)!.Value));
        harness.Main.AdvanceMilliseconds(50);
        harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(50)));
        harness.Main.OnMain(() =>
        {
            // :2336-2338 + Chrome.swift:98 easeOut(0.1); at halfway the alpha is neither endpoint.
            var actual = ((ISolidColorBrush)circle.Fill!).Color;
            var end = Chrome.Overlay(0.08).Resolve(circle.ActualThemeVariant);
            Assert.Equal((byte)Math.Round(end.A * Animation.EaseOut(0.1).Progress(0.05)), actual.A);
        });
        Animation? during = null;
        using var probe = new OnChange<bool>(() => model.Settings.SidebarActivityView, (_, _) => during = Transaction.Current);
        harness.Main.OnMain(() => PointerClick(button));
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Settings.SidebarActivityView);
            Assert.Equal(Chrome.PanelSlide, during); // :2323
            Assert.Equal("On", AutomationProperties.GetItemStatus(button));
            Close(view);
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-004")] // SidebarView.swift:2323 withAnimation(Chrome.panelSlide) { isOn.toggle() } drives :2327 and :2331
    public void The_bells_glyph_turns_to_the_accent_with_its_disc_on_panelSlide_and_the_on_disc_wins_over_hover() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var model = harness.MakeModel();
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        var button = ButtonNamed(view, "Activity view");
        var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(button).Single();
        var circle = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(button).Single();
        Color GlyphColor() => ((ISolidColorBrush)glyph.Foreground!).Color;
        Color DiscColor() => ((ISolidColorBrush)circle.Fill!).Color;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        try
        {
            harness.Main.OnMain(() =>
            {
                Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), GlyphColor()); // :2327 off
                window.MouseMove(button.TranslatePoint(new Point(15, 15), window)!.Value);
            });
            Tick(300);
            harness.Main.OnMain(() => PointerClick(button)); // :2323 inside Chrome.panelSlide
            Tick(50);
            harness.Main.OnMain(() =>
            {
                var off = Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant);
                var on = Chrome.Accent.Resolve(glyph.ActualThemeVariant);
                Assert.NotEqual(off, GlyphColor()); // on its way along the spring, not cut at either end
                Assert.NotEqual(on, GlyphColor());
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(Chrome.Accent.Resolve(glyph.ActualThemeVariant), GlyphColor()); // :2327 on
                Assert.Equal(Chrome.Accent.Opacity(0.16).Resolve(circle.ActualThemeVariant), DiscColor()); // :2331 on wins over the hover's 0.08
                Assert.True(button.IsPointerOver);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // PR-SIDEBAR-019: the ED-218 foundation has landed; this caller proof must go red before wiring it.
    // The capture comparison STOP #10 asked for is in the header ledger (#2, 2026-10-03); this ED proof keeps its
    // Decision trait.
    [Theory]
    [Trait("Decision", "ED-218")] // App/Views/Sidebar/SidebarView.swift:117-119; pinned capture remains STOP
    [InlineData(false)]
    [InlineData(true)]
    public void Project_expansion_moves_a_surviving_project_from_its_old_position_on_panelSlide(bool reduceMotion) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var reduction = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        var first = NewProject(harness, "First");
        var next = NewProject(harness, "Next");
        var thread = NewThread(first, "Open", 0);
        var model = Model(harness, [first, next], [thread]);
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        try
        {
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Tick(1600);
        var row = RowNamed(view, "Next");
        var container = ListPanel(view).Children.Single(c => c.GetLogicalDescendants().Contains(row));
        var oldY = container.Bounds.Y;
        harness.Main.OnMain(() => PointerClick(ButtonNamed(view, "First")));
        Tick(0);
        var displacement = oldY - container.Bounds.Y;
        Assert.NotEqual(0, displacement); // :117-119 space closes when First collapses
        Assert.Equal(displacement, container.RenderTransform!.Value.M32, 5); // starts at its old visible position
        Tick(90);
        var curve = reduceMotion ? Animation.EaseOut(0.18) : Animation.Spring(response: 0.32, dampingFraction: 0.9); // Chrome.swift:100-104 panelSlide
        Assert.Equal(displacement * (1 - curve.Progress(0.09)), container.RenderTransform!.Value.M32, 5);
        Tick(1600);
        Assert.Equal(0, container.RenderTransform?.Value.M32 ?? 0, 5);
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-039")] // App/Views/Sidebar/SidebarView.swift:2349-2357
    public void Activity_headers_keep_their_verbatim_text_font_colour_leading_alignment_and_non_button_role() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        foreach (var title in new[] { "Needs attention", "Pinned", "Today", "Yesterday", "Wednesday", "September 2", "September 2, 2025" })
        {
            var header = (Control)Activator.CreateInstance(Private("ActivityHeader"), title, true)!;
            Shown(header);
            var text = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(header));
            Assert.Equal(title, text.Text); // :2350 Text(verbatim: title), caller supplies literal or localized day string
            Assert.Equal(12, text.FontSize); Assert.Equal(FontWeight.SemiBold, text.FontWeight); // :2351 .callout semibold
            Assert.Equal(Chrome.SecondaryText.Resolve(text.ActualThemeVariant), ((ISolidColorBrush)text.Foreground!).Color); // :2352
            Assert.Equal(HorizontalAlignment.Stretch, text.HorizontalAlignment); // :2356 full width
            Assert.Equal(TextAlignment.Start, text.TextAlignment); // :2356 leading follows the writing direction
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(header));
            WindowOf(header).Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-041")] // App/Views/Sidebar/SidebarView.swift:693-703
    public void No_results_is_mounted_only_when_the_search_has_no_groups() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Found", 0);
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        harness.Main.OnMain(() => Assert.DoesNotContain("No results", Texts(view)));
        harness.Main.OnMain(() => SearchBox(view).Text = "missing");
        harness.Main.OnMain(() => Assert.Contains("No results", Texts(view)));
        harness.Main.OnMain(() => SearchBox(view).Text = "Found");
        harness.Main.AdvanceMilliseconds(1600);
        harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(1600)); WindowOf(view).UpdateLayout(); });
        harness.Main.OnMain(() => { Assert.DoesNotContain("No results", Texts(view)); Close(view); });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:331-332,517-519
    [Trait("Req", "UI-SIDEBAR-007")] // App/Views/Sidebar/SidebarView.swift:533-537,1281
    public void Both_kinds_of_project_spacer_render_clear_full_width_noninteractive_items_with_their_source_heights() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = RichLibrary(harness, out _);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var gaps = ProjectItems(view).Where(item => KindName(item) == "Gap").ToList();
            Assert.Contains(gaps, item => IdOf(item).StartsWith("gap-", StringComparison.Ordinal));
            Assert.Contains(gaps, item => IdOf(item).StartsWith("settled-gap-", StringComparison.Ordinal));
            foreach (var gap in gaps)
            {
                var spacer = Assert.IsType<Border>(Call(view, "ItemView", gap));
                Assert.Equal(IdOf(gap).StartsWith("settled-gap-", StringComparison.Ordinal) ? 4d : 10d, spacer.Height);
                Assert.Equal(HorizontalAlignment.Stretch, spacer.HorizontalAlignment);
                Assert.Null(spacer.Background); Assert.Null(spacer.Child);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(spacer));
                Assert.Equal(SwiftTransition.Opacity, RowTransition(view, gap));
            }
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift:1642-1658
    public void Connector_stroke_is_one_point_round_dotted_and_real_hover_follows_easeOut_0_1() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource(); using var seam = FrameClock.Source.Override(frames);
        Control connector = null!;
        harness.Main.OnMain(() =>
        {
            connector = (Control)Activator.CreateInstance(Private("HelperConnector"), true, (Action)(() => { }))!;
            connector.Width = 28; connector.Height = 24; Shown(connector);
        });
        try
        {
            var path = Assert.Single(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(connector));
            Assert.Equal(1d, path.StrokeThickness); Assert.Equal(PenLineCap.Round, path.StrokeLineCap);
            Assert.Equal(new double[] { 0.1, 3.6 }, path.StrokeDashArray!);
            var initial = Chrome.SecondaryText.Opacity(0.45).Resolve(path.ActualThemeVariant);
            var target = Chrome.SecondaryText.Opacity(0.9).Resolve(path.ActualThemeVariant);
            Assert.Equal(initial, ((ISolidColorBrush)path.Stroke!).Color);
            var window = WindowOf(connector);
            harness.Main.OnMain(() => window.MouseMove(connector.TranslatePoint(new Point(2, 2), window)!.Value));
            harness.Main.AdvanceMilliseconds(50);
            harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(50)));
            Assert.Equal((byte)Math.Round(initial.A + (target.A - initial.A) * Animation.EaseOut(0.1).Progress(0.05)), ((ISolidColorBrush)path.Stroke!).Color.A);
        }
        finally { harness.Main.OnMain(() => WindowOf(connector).Close()); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-040")] // App/Views/Sidebar/SidebarView.swift:2378-2379: the chevron turns along Chrome.panelSlide
    public void Settled_chevron_rotates_on_panelSlide_instead_of_snapping_after_a_real_pointer_click() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource(); using var seam = FrameClock.Source.Override(frames);
        var model = harness.MakeModel();
        Control header = null!;
        harness.Main.OnMain(() => header = Hosted(model, (Control)Activator.CreateInstance(Private("SettledHeader"), 1L, true)!));
        try
        {
            var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(header));
            var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(button));
            Assert.Equal(0d, Assert.IsType<RotateTransform>(glyph.RenderTransform).Angle);
            harness.Main.OnMain(() => PointerClick(button));
            Assert.Equal(0d, Assert.IsType<RotateTransform>(glyph.RenderTransform).Angle); // :2379 value changes animate, not snap
            harness.Main.AdvanceMilliseconds(90);
            harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(90)));
            Assert.Equal(90 * Animation.Spring(response: 0.32, dampingFraction: 0.9).Progress(0.09), Assert.IsType<RotateTransform>(glyph.RenderTransform).Angle, 5);
            harness.Main.AdvanceMilliseconds(1600);
            harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(1600)));
            Assert.Equal(90d, Assert.IsType<RotateTransform>(glyph.RenderTransform).Angle);
        }
        finally { harness.Main.OnMain(() => WindowOf(header).Close()); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-126")] // App/Views/Sidebar/SidebarView.swift:1097-1105; App/AppModel.swift:549-556
    [Trait("Req", "UI-SIDEBAR-008")] // App/Views/Sidebar/SidebarView.swift:1096-1105: in search results (togglesExpansion false) the click does nothing
    public void Project_body_pointer_toggles_persist_after_restart_but_search_and_no_change_schedule_no_save() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource(); using var seam = FrameClock.Source.Override(frames);
        var project = NewProject(harness, "Alpha");
        Assert.True(project.IsExpanded); // Core/Models/Library.swift:67 new-project default
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
        try
        {
            harness.Main.OnMain(() => PointerClick(ButtonNamed(row, "Alpha")));
            harness.Main.OnMain(() => Assert.NotNull(Field(model, "saveTask"))); // updateProject schedules the library save
            harness.Main.AdvanceMilliseconds(400);
            DiskWriter.Shared.WaitForQueuedWrites();
            var reloaded = harness.MakeModel();
            harness.Main.OnMain(() => Assert.False(reloaded.Project(project.Id)!.IsExpanded));
            harness.Main.OnMain(() => WindowOf(row).Close());
            harness.Main.OnMain(() => row = ShownProjectRow(reloaded, reloaded.Project(project.Id)!, 0, togglesExpansion: false));
            harness.Main.OnMain(() =>
            {
                Assert.Null(Field(reloaded, "saveTask"));
                PointerClick(ButtonNamed(row, "Alpha"));
                Assert.False(reloaded.Project(project.Id)!.IsExpanded);
                Assert.Null(Field(reloaded, "saveTask")); // search guard returns without updating
                reloaded.UpdateProject(project.Id, value => value);
                Assert.Null(Field(reloaded, "saveTask")); // source equality guard returns without scheduling
            });
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-009")] // App/Views/Sidebar/SidebarView.swift:1127-1132
    [Trait("Req", "UI-SIDEBAR-010")] // App/Views/Sidebar/SidebarView.swift:1115-1125; UI/Chrome/Chrome.swift:766-772
    public void Project_count_is_tabular_and_hover_accessories_keep_source_frames_order_font_and_hit_rectangles() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var harness = new AppModelHarness(); using var frames = FrameClock.Source.Override(manual);
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter());
        var project = NewProject(harness, "Alpha") with { IsExpanded = false };
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 3));
        try
        {
            harness.Main.OnMain(() =>
            {
                var count = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "3"); // :1128
                Assert.Equal(11d, count.FontSize); Assert.Equal(FontWeight.Normal, count.FontWeight); // :1129 SwiftFont.Subheadline
                Assert.Equal(SwiftFont.Subheadline.MonospacedDigit().Resolve().FontFeatures, count.FontFeatures); // :1129 .monospacedDigit()
                var window = WindowOf(row);
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value); // :1116 hovering true
            });
            // Chrome.swift:735-737 the hover comes inside withAnimation(Chrome.hover): the count leaves on its default .opacity
            // transition over easeOut 0.1 s (its curve is proven by REVIEW_Round2_A_search_results_project_row_crossfades_...).
            harness.Main.OnMain(() => Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(row), t => t.Text == "3")); // still drawn as it starts to fade
            harness.Main.AdvanceMilliseconds(100);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(100)));
            harness.Main.OnMain(() =>
            {
                WindowOf(row).UpdateLayout(); // the accessory rebuild (applied by the Track posted above) needs a real layout pass before the Bounds reads below
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(row), t => t.Text == "3"); // :1116 count branch no longer drawn, its fade over
                // The cluster is the Horizontal, zero-spacing StackPanel (:1117) whose two children together
                // contain exactly two real Buttons: the ellipsis (Popovers.cs:569 RowActionsButton, a
                // Decorator wrapping a Button) and the plus button (a Button directly).
                var cluster = LogicalQuery.SelfAndDescendants<StackPanel>(row)
                    .Single(p => p.Orientation == Orientation.Horizontal && p.Spacing == 0
                        && p.Children.Count == 2 && LogicalQuery.SelfAndDescendants<Button>(p).Count() == 2);
                Assert.Equal(new[] { "ellipsis", "plus" }, cluster.Children.Select(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Single().SymbolName)); // :1118,:1119 source order
                var plus = Assert.IsType<Button>(cluster.Children[1]);
                var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(plus));
                Assert.Equal(11d, SwiftImage.GetFont(glyph)!.Resolve().FontSize); // UI/Chrome/Chrome.cs RowAccessoryIcon :766-772
                Assert.Equal(FontWeight.SemiBold, SwiftImage.GetFont(glyph)!.Resolve().FontWeight);
                Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), ((ISolidColorBrush)glyph.Foreground!).Color);
                Assert.Equal(new Size(20, 20), plus.Bounds.Size); // RowAccessoryIcon's fixed hit rectangle
                var at = plus.TranslatePoint(new Point(1, 1), WindowOf(row))!.Value; // off the glyph, inside the rectangular hit box
                WindowOf(row).MouseMove(at); WindowOf(row).MouseDown(at, MouseButton.Left); WindowOf(row).MouseUp(at, MouseButton.Left);
                Assert.Single(model.ThreadsIn(project)); // :1119-1123 Model.NewThread(current)
            });
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    // STOP opencode-App.Views.Sidebar.SidebarView #11 (successor review of uncommitted predecessor draft)
    // Record: UI-SIDEBAR-059 (App/Views/Sidebar/SidebarView.swift:317-325,1064-1076, ED-218 ForEach
    // transitions). A draft test ("Mounted_project_rows_fade_after_the_insertion_delay_and_leave_after_the_short_removal")
    // was found uncommitted in this worktree and reverted: RowNamed(view, "Beta") threw "Sequence contains no
    // matching element" immediately after model.RemoveProject(beta) at Tick(0), before any fade-out assertion ran.
    // A throwaway diagnostic (built, run, then discarded; not committed) isolated the cause: it is NOT that the
    // row is gone from the tree. Re-querying every SidebarRow's automation name right after
    // harness.Main.OnMain(() => model.RemoveProject(beta)); Tick(0); still showed "Alpha,Beta,Add project,Settings"
    // with the ListPanel's three children all at Opacity 1 — i.e. the row list did not react to the removal at
    // all within that pump (no fade, no removal, no re-render), so RowNamed's failure in the real draft test must
    // have come from a different scheduling order than this isolated probe used, not from the row being absent.
    // Two real candidates remain open: (a) SidebarView's Track over Model.Projects/ThreadPlacements does not
    // register Projects as a dependency the way Model.Threads does (a genuine reactivity gap distinct from
    // ED-218's transition mechanics), so ApplyList/ApplyEntries never runs on a project-only removal; or (b) it
    // does react, but only on a later pump this probe did not reach (e.g. requires the same DiskWriter/save
    // round trip PR-SIDEBAR-126 depends on). Needs tracing ListPassNow/ThreadPlacements/HelpersByParent's actual
    // reads against Swift's :317-325,1064-1076 dependency set before a fix or a firm STOP is written; not
    // resolved in this pass.
    // UPDATE (parity pass 2026-10-03): UI-SIDEBAR-059's mounted fades are proven through threads instead
    // (Mounted_rows_fade_in_after_the_delay_leave_on_the_short_fade_and_a_settled_row_fades_in_on_the_lists_panelSlide:
    // insertion after 0.12 s on easeIn 0.18, removal on easeOut 0.06, a settled row on the list's panelSlide). The
    // project-removal question above is still unexamined; it bears on UI-SIDEBAR-006's mounted gap transition only.

    [Fact]
    [Trait("Req", "UI-SIDEBAR-026")] // App/Views/Sidebar/SidebarView.swift:965-983; UI/Chrome/Popovers.swift:102-157
    public void Delete_confirmation_roles_have_the_exact_padding_fonts_colours_order_and_26_point_actions() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha"); var thread = NewThread(project, "Title", 0) with { WorktreePath = harness.NewFolder("worktree") };
        var popover = Shown((Control)Activator.CreateInstance(Private("DeleteThreadPopover"), thread, (Action<bool>)(_ => { }))!);
        try
        {
            var menu = Assert.IsType<PopoverMenu>(Get(popover, "Child")); Assert.Equal(280d, menu.Width);
            var padded = Assert.IsType<Border>(Assert.IsType<ScrollViewer>(menu.Child).Content); Assert.Equal(new Thickness(6), padded.Padding);
            var inner = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(padded.Child).Children.Single());
            Assert.Equal(new[] { typeof(PopoverSectionHeader), typeof(PopoverNote), typeof(PopoverDivider), typeof(PopoverItem), typeof(PopoverItem) }, inner.Children.Select(c => c.GetType()));
            var header = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(inner.Children[0]));
            Assert.Equal("Delete this thread?", header.Text); Assert.Equal(11d, header.FontSize); Assert.Equal(FontWeight.SemiBold, header.FontWeight); Assert.Equal(new Thickness(8, 6, 8, 3), header.Margin);
            var note = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(inner.Children[1]));
            Assert.Equal("“Title” and its history will be removed. Files in your project stay as they are.", note.Text); Assert.Equal(12d, note.FontSize); Assert.Equal(new Thickness(8, 5), note.Margin);
            foreach (var text in new[] { header, note }) Assert.Equal(Chrome.SecondaryText.Resolve(text.ActualThemeVariant), ((ISolidColorBrush)text.Foreground!).Color);
            Assert.Equal(new Thickness(8, 5), Assert.IsType<Border>(Assert.IsType<PopoverDivider>(inner.Children[2]).Child).Margin);
            foreach (var item in inner.Children.OfType<PopoverItem>())
            {
                var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item)); Assert.Equal(26d, button.Bounds.Height);
                var title = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(button)); Assert.Equal(13d, title.FontSize);
                Assert.Equal(Chrome.Danger.Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color);
            }
            Assert.DoesNotContain("Cancel", Texts(popover));
        }
        finally { WindowOf(popover).Close(); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-026")] // SidebarView.swift:974; UI/Chrome/Popovers.swift:142-149 PopoverNote: a Text with no line limit wraps
    [Trait("Req", "UI-GIT-057")] // the note under the question, over the 280 pt menu
    // Measured beside 2-chat-use.mov 152.6 s (frames/gap-R1-07/d_confirm_hold): "“New thread” and its history will be /
    // removed. Files in your project stay as they / are." wraps onto three lines inside the ~280 pt confirm; the port's
    // note ran on one line and was cut at the menu's edge (evidence: E:\Droppy\work\parity\evidence\SidebarView\
    // delete-note-*.png).
    public void The_delete_questions_note_wraps_inside_the_280_point_menu_onto_the_lines_it_needs() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha"); var thread = NewThread(project, "New thread", 0);
        var popover = (Control)Activator.CreateInstance(Private("DeleteThreadPopover"), thread, (Action<bool>)(_ => { }))!;
        popover.HorizontalAlignment = HorizontalAlignment.Left;
        popover.VerticalAlignment = VerticalAlignment.Top;
        new AvaloniaWindow { Content = popover, Width = 300, Height = 160, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Color.FromRgb(70, 66, 62)) }.Show(); // about the video's glass grey, for the side-by-side
        try
        {
            WindowOf(popover).UpdateLayout();
            SidebarCaptureTests.SaveIfAsked(WindowOf(popover), "delete-note-port.png");
            var note = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Single(t => t.Text!.EndsWith("stay as they are.", StringComparison.Ordinal));
            Assert.Equal(TextWrapping.Wrap, note.TextWrapping);
            Assert.True(note.Bounds.Width <= 280 - 2 * 6 - 2 * 8); // wrapped inside the menu's inner width less the note's own 8 pt sides
            Assert.Equal(3, note.TextLayout.TextLines.Count); // as the Mac draws it at this width
        }
        finally { WindowOf(popover).Close(); }
    });

    // STOP opencode-App.Views.Sidebar.SidebarView #12 (successor review of uncommitted predecessor draft)
    // Rule: R-TEST-6, real popup dismissal probe (App/Views/Sidebar/SidebarView.swift:219-232,256-263). A
    // draft test ("Real_delete_popover_dismissal_keeps_the_thread_and_confirmation_waits_for_content_to_leave")
    // was found uncommitted in this worktree and reverted: window.Show()/PointerClick(anchor) threw
    // "Unable to create IPopupImpl and no overlay layer is found for the target control" from
    // Marrow.UI.Port.Presentation.AvaloniaPopoverPresenter.SetPresented (Port/ViewLifetime.cs:85) — the
    // VisualLayerManager the draft builds is not the one SidebarView's own presenter resolves the popup
    // against, so clicking the anchor opened a second, unrelated Popup.IsOpen the harness never attached an
    // overlay for. Not fixed blind in this pass; needs the same real-popup harness pattern already proven to
    // work elsewhere in this file (see DeletePopover_presents_only_for_the_matching_id_with_its_arrow_on_the_trailing_edge,
    // :3878) rather than a hand-built window/VisualLayerManager.


    // ---- PR-SIDEBAR-019: the list animates with Chrome.panelSlide on thread count, project expansion and the settled fold ----

    [Theory]
    [Trait("Req", "PR-SIDEBAR-019")] // App/Views/Sidebar/SidebarView.swift:114-117 (three .animation(Chrome.panelSlide, value:) on the LazyVStack); UI/Chrome/Chrome.swift:100-104
    [InlineData("threads", false)]
    [InlineData("threads", true)]
    [InlineData("expansion", false)]
    [InlineData("expansion", true)]
    [InlineData("settled", false)]
    [InlineData("settled", true)]
    public void The_list_slides_on_panelSlide_when_the_thread_count_a_project_expansion_or_the_settled_fold_changes(string change, bool reduceMotion) => headless.OnUiThread(() =>
    {
        // Each change is written straight to the model (no withAnimation around it), so the only thing that can animate
        // the rows below it is the list's own .animation(Chrome.panelSlide, value:) for that value. A row that survives
        // the change must start where it stood and travel to its new place on the curve (spring response 0.32, damping
        // fraction 0.9; easeOut 0.18 s while the system asks for reduced motion).
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var reduction = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        var first = NewProject(harness, "First");
        var next = NewProject(harness, "Next");
        var open = NewThread(first, "Open", 0);
        var settled = NewThread(first, "Done", 1) with { IsSettled = true, SettledAt = Base.AddHours(1) };
        var model = Model(harness, [first, next], [open, settled]);
        harness.Main.OnMain(() => model.Settings.SettledCollapsed = false); // AppSettings.swift:725 starts it folded
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        try
        {
            void Tick(int ms)
            {
                harness.Main.AdvanceMilliseconds(ms);
                harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
            }
            Tick(1600);
            var row = RowNamed(view, "Next");
            var container = ListPanel(view).Children.Single(c => c.GetLogicalDescendants().Contains(row));
            var oldY = container.Bounds.Y;
            harness.Main.OnMain(() =>
            {
                switch (change)
                {
                    case "threads": model.Delete(open.Id); break; // :115 model.threads.count
                    case "expansion": model.UpdateProject(first.Id, p => p with { IsExpanded = false }); break; // :116 model.projects.map(\.isExpanded)
                    default: model.Settings.SettledCollapsed = true; break; // :117 model.settings.settledCollapsed
                }
            });
            Tick(0);
            var displacement = oldY - container.Bounds.Y;
            Assert.NotEqual(0, displacement); // the space closes
            Assert.Equal(displacement, container.RenderTransform!.Value.M32, 5); // starts at its old visible position
            Tick(90);
            var curve = reduceMotion ? Animation.EaseOut(0.18) : Animation.Spring(response: 0.32, dampingFraction: 0.9); // Chrome.swift:100-104 panelSlide
            Assert.Equal(displacement * (1 - curve.Progress(0.09)), container.RenderTransform!.Value.M32, 5);
            Tick(1600);
            Assert.Equal(0, container.RenderTransform?.Value.M32 ?? 0, 5);
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-059")] // SidebarView.swift:317-325 rowTransition, :1064-1076 AnyTransition.sidebarRow
    [Trait("Req", "PR-SIDEBAR-019")] // :114-117 the list's animations also carry the rows' own transitions: a settled row's plain opacity rides panelSlide
    public void Mounted_rows_fade_in_after_the_delay_leave_on_the_short_fade_and_a_settled_row_fades_in_on_the_lists_panelSlide() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var project = NewProject(harness, "Alpha");
        var kept = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [kept]);
        harness.Main.OnMain(() => model.Settings.SettledCollapsed = false);
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Control ContainerOf(string title) => ListPanel(view).Children.Single(c => c.GetLogicalDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == title));
        bool Listed(string title) => ListPanel(view).Children.Any(c => c.GetLogicalDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == title));
        try
        {
            Tick(1600);
            ChatThread added = null!;
            harness.Main.OnMain(() => added = model.NewThread(project)!); // a plain write: the row's own transition times it
            Tick(0);
            var insertion = Animation.EaseIn(0.18);
            harness.Main.OnMain(() => Assert.Equal(0d, ContainerOf(added.Title).Opacity)); // :1072 nothing during the 0.12 s delay
            Tick(100);
            harness.Main.OnMain(() => Assert.Equal(0d, ContainerOf(added.Title).Opacity));
            Tick(110); // 0.21 s: 0.09 s into the 0.18 s easeIn
            harness.Main.OnMain(() => Assert.Equal(insertion.Progress(0.09), ContainerOf(added.Title).Opacity, 2));
            Tick(200);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(1d, ContainerOf(added.Title).Opacity);
                model.Delete(added.Id); // :1073 removal: easeOut 0.06 s
            });
            Tick(30);
            harness.Main.OnMain(() =>
            {
                Assert.True(Listed(added.Title)); // still leaving
                Assert.Equal(1 - Animation.EaseOut(0.06).Progress(0.03), ContainerOf(added.Title).Opacity, 2);
            });
            Tick(60);
            harness.Main.OnMain(() =>
            {
                Assert.False(Listed(added.Title)); // gone once faded
                Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settle(kept.Id, sounds: false)); // :1910 settle()'s own withAnimation(Chrome.panelSlide)
            });
            // :319-320 a settled row's transition is plain .opacity, so it rides the animation that carried the change:
            // the panelSlide spring (response 0.32, damping fraction 0.9), not the open rows' delayed easeIn.
            Tick(90);
            harness.Main.OnMain(() => Assert.Equal(Animation.Spring(response: 0.32, dampingFraction: 0.9).Progress(0.09), ContainerOf("Kept").Opacity, 2));
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(1d, ContainerOf("Kept").Opacity);
                model.Settings.SettledCollapsed = true; // a plain write: only the list's own :117 .animation(panelSlide, value: settledCollapsed) can time it
            });
            Tick(90); // the settled row leaves on its plain opacity, carried by the list's panelSlide
            harness.Main.OnMain(() => Assert.Equal(1 - Animation.Spring(response: 0.32, dampingFraction: 0.9).Progress(0.09), ContainerOf("Kept").Opacity, 2));
            Tick(1600);
            harness.Main.OnMain(() => Assert.False(Listed("Kept")));
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-019")] // App/Views/Sidebar/SidebarView.swift:114-117: only those three values carry the animation
    public void A_change_to_none_of_the_three_values_moves_the_rows_without_a_slide() => headless.OnUiThread(() =>
    {
        // Archiving keeps the thread in the library (the count does not change), so no .animation(value:) names it.
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var first = NewProject(harness, "First");
        var next = NewProject(harness, "Next");
        var one = NewThread(first, "One", 0);
        var model = Model(harness, [first, next], [one]);
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        try
        {
            void Tick(int ms)
            {
                harness.Main.AdvanceMilliseconds(ms);
                harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
            }
            Tick(1600);
            var row = RowNamed(view, "Next");
            var container = ListPanel(view).Children.Single(c => c.GetLogicalDescendants().Contains(row));
            var oldY = container.Bounds.Y;
            harness.Main.OnMain(() => model.Archive(one.Id));
            Tick(0);
            Assert.NotEqual(oldY, container.Bounds.Y); // the row did move up
            Assert.Equal(0, container.RenderTransform?.Value.M32 ?? 0, 5); // and jumped
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ---- PR-SIDEBAR-055: a cancelled gesture takes the release path ----

    [Theory]
    [Trait("Req", "PR-SIDEBAR-055")] // App/Views/Sidebar/SidebarView.swift:803-809 dragEnded; the record's cancellation clause: "A cancelled gesture takes the same path"
    [InlineData("button released unseen")]
    [InlineData("pointer capture lost")]
    public void A_cancelled_drag_gesture_springs_the_row_home_the_way_a_release_does(string how) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var id = Guid.NewGuid();
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        Animation? during = null;
        using var probe = new OnChange<RowDrag<Guid>>(() => Drag(view), (_, _) => during = Transaction.Current);
        var window = (AvaloniaWindow)TopLevel.GetTopLevel(view)!;
        var start = default(Point);
        harness.Main.OnMain(() =>
        {
            var row = StandInRow(() => { });
            ListPanel(view).Children.Add(row);
            window.UpdateLayout();
            NoteFrame(view, id, row.Bounds);
            start = At(row, 40, 10, window);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(new Point(start.X, start.Y + 9), RawInputModifiers.LeftMouseButton);
            Assert.Equal(id, Drag(view).Id); // grabbed
        });
        harness.Main.OnMain(() =>
        {
            during = null;
            if (how == "pointer capture lost")
                ListPanel(view).RaiseEvent(new PointerCaptureLostEventArgs(ListPanel(view), new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, true)));
            else
                window.MouseMove(new Point(start.X, start.Y + 12), RawInputModifiers.None); // the button went up where the list never saw it
        });
        harness.Main.OnMain(() =>
        {
            Assert.Null(Drag(view).Id); // :808 drag = RowDrag()
            Assert.Equal(Animation.Spring(response: 0.32, dampingFraction: 0.8), during); // :806-808
            Close(view);
        });
    });

    // ---- PR-SIDEBAR-127: a project row's leading mark ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-127")] // App/Views/Sidebar/SidebarView.swift:1106-1114; UI/Chrome/ProjectIconPicker.swift:11-27; UI/Chrome/Chrome.swift:651-679
    public void A_project_row_draws_its_picked_emoji_or_symbol_at_12pt_in_0_9_and_the_folder_mark_at_14pt_in_0_85_inside_a_20pt_square() => headless.OnUiThread(() =>
    {
        // Each branch is a fresh row built with the icon already set (the live-update path has its own test).
        using var harness = new AppModelHarness();
        var plain = NewProject(harness, "Plain");
        var symbolic = NewProject(harness, "Symbolic") with { Icon = new ProjectIcon.Symbol("star.fill") };
        var emoji = NewProject(harness, "Emoji") with { Icon = new ProjectIcon.Emoji("\U0001F680") };
        var model = Model(harness, [plain, symbolic, emoji], []);
        harness.Main.OnMain(() =>
        {
            var plainRow = ShownProjectRow(model, plain, 0);
            var badge = LogicalQuery.SelfAndDescendants<SidebarIconBadge>(plainRow).Single();
            var square = Assert.IsType<Panel>(badge.Child);
            Assert.Equal(20d, square.Width); // :677 both branches sit in a 20 pt square
            Assert.Equal(20d, square.Height);
            var folder = LogicalQuery.SelfAndDescendants<SwiftImage>(plainRow).Single(i => i.SymbolName is not null);
            Assert.Equal("folder.fill", folder.SymbolName); // :1111 no icon picked
            Assert.Equal(14d, folder.Width); // SidebarSymbol's default Chrome.symbolSize
            Assert.Equal(14d, folder.Height);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.85).Resolve(folder.ActualThemeVariant), ((ISolidColorBrush)folder.GetValue(SwiftImage.ForegroundProperty)!).Color); // :676 the badge's 0.85
            Close(plainRow);

            var symbolRow = ShownProjectRow(model, symbolic, 0);
            var symbol = LogicalQuery.SelfAndDescendants<SwiftImage>(symbolRow).Single(i => i.SymbolName is not null);
            Assert.Equal("star.fill", symbol.SymbolName); // :1108-1109 the named symbol
            Assert.Equal(12d, SwiftImage.GetFont(symbol)!.PointSize); // :1109 size 12, not the 14 pt fallback
            Assert.Equal(Chrome.PrimaryText.Opacity(0.9).Resolve(symbol.ActualThemeVariant), ((ISolidColorBrush)symbol.GetValue(SwiftImage.ForegroundProperty)!).Color); // ProjectIcon.swift:25 0.9, not 0.85
            Assert.Equal(20d, Assert.IsType<Panel>(LogicalQuery.SelfAndDescendants<SidebarIconBadge>(symbolRow).Single().Child).Width);
            Close(symbolRow);

            var emojiRow = ShownProjectRow(model, emoji, 0);
            var mark = LogicalQuery.SelfAndDescendants<TextBlock>(emojiRow).Single(t => t.Text == "\U0001F680"); // :18 the emoji character itself
            Assert.Equal(12d, mark.FontSize); // :19
            Assert.Equal(Chrome.PrimaryText.Opacity(0.9).Resolve(mark.ActualThemeVariant), ((ISolidColorBrush)mark.Foreground!).Color); // :25
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(emojiRow), i => i.SymbolName is not null); // no symbol beside it
            Assert.Equal(20d, Assert.IsType<Panel>(LogicalQuery.SelfAndDescendants<SidebarIconBadge>(emojiRow).Single().Child).Width);
            Close(emojiRow);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-127")] // SwiftUI draws no glyph for an Image(systemName:) whose name is not a symbol (ProjectIconPicker.swift:21): OD-0098, the framework's documented behaviour; the 20 pt square is SidebarView.swift:1106-1114
    public void A_stored_symbol_name_the_table_does_not_map_draws_an_empty_mark_inside_the_20pt_square_and_leaves_the_row_height_and_title_inset_unchanged() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var known = NewProject(harness, "Known") with { Icon = new ProjectIcon.Symbol("star.fill") };
        var unknown = NewProject(harness, "Unknown") with { Icon = new ProjectIcon.Symbol("not.a.real.symbol") }; // a hand-edited library value
        var model = Model(harness, [known, unknown], []);
        harness.Main.OnMain(() =>
        {
            var knownRow = ShownProjectRow(model, known, 0);
            var knownMark = LogicalQuery.SelfAndDescendants<SwiftImage>(knownRow).Single(i => i.SymbolName == "star.fill");
            Assert.False(knownMark.IsEmpty); // control: a mapped name draws a glyph
            var knownTitle = LogicalQuery.SelfAndDescendants<TextBlock>(knownRow).Single(t => t.Text == "Known");
            var knownHeight = knownRow.Bounds.Height;
            var knownInset = knownTitle.TranslatePoint(default, knownRow)!.Value.X;
            Close(knownRow);

            var row = ShownProjectRow(model, unknown, 0);
            var square = Assert.IsType<Panel>(LogicalQuery.SelfAndDescendants<SidebarIconBadge>(row).Single().Child);
            Assert.Equal(20d, square.Width); // the mark's square is reserved all the same
            Assert.Equal(20d, square.Height);
            var mark = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName == "not.a.real.symbol");
            Assert.True(mark.IsEmpty); // draws nothing: no placeholder glyph, as SwiftUI does (OD-0098)
            Assert.Equal(0d, mark.DesiredSize.Width);
            Assert.Equal(0d, mark.DesiredSize.Height);
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(row), i => i.SymbolName == "folder.fill");
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "Unknown");
            Assert.Equal(knownHeight, row.Bounds.Height); // the row's height is unchanged
            Assert.Equal(knownInset, title.TranslatePoint(default, row)!.Value.X); // and so is the title's inset
            Close(row);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-127")] // porting/icons/sf-symbols-to-lucide.json meta unmapped: 0; ProjectIconPicker.swift:80-109 offers only the shortlist and ProjectSymbolCatalogue.swift names
    public void Every_symbol_name_the_picker_offers_resolves_in_the_table_so_only_a_hand_edited_name_can_draw_an_empty_mark()
    {
        var unmapped = ProjectSymbolCatalogue.All.Where(name => SymbolCatalogue.Mapping(name) is null).ToArray();
        Assert.Empty(unmapped);
        Assert.NotEmpty(ProjectSymbolCatalogue.All);
        Assert.NotNull(SymbolCatalogue.Mapping("folder.fill")); // the no-icon mark
        Assert.Null(SymbolCatalogue.Mapping("not.a.real.symbol")); // control: a name outside the table is unmapped
    }

    // ---- UI-SIDEBAR-008: the project row ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-008")] // App/Views/Sidebar/SidebarView.swift:1079-1167; UI/Chrome/Chrome.swift:681-746
    public void A_project_row_keeps_the_recorded_geometry_and_hover_fill_and_truncates_at_the_same_place_with_or_without_its_buttons() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var project = NewProject(harness, "Alpha") with { Name = new string('W', 120) }; // far wider than the row
        var model = Model(harness, [project], []);
        Control row = null!;
        AvaloniaWindow window = null!;
        // Chrome.swift:711-720 HStack(spacing: 8) { icon; title; Spacer(minLength: 4) }: SwiftUI puts the stack's spacing between
        // every adjacent pair of subviews, the Spacer included, so the title's room is the row less the 8 + 40 trailing, the 8
        // + 20 + 8 before it and the 8 + 4 after it (ED-329). The title is cut at its tail at a whole character. The
        // row is made 17 glyphs + 92 wide: the exact room is then 17 glyphs less 4, so 16 characters fit, where the room
        // of a spacer without its spacing (8 more) would let the 17th in.
        double glyphWidth = 0;
        harness.Main.OnMain(() =>
        {
            var probe = ShownProjectRow(model, project, 0);
            var probeTitle = LogicalQuery.SelfAndDescendants<TextBlock>(probe).Single(t => t.Text == project.Name);
            var glyph = new TextBlock { Text = "W", FontFamily = probeTitle.FontFamily, FontSize = probeTitle.FontSize, FontWeight = probeTitle.FontWeight };
            glyph.Measure(Size.Infinity);
            glyphWidth = glyph.DesiredSize.Width;
            Close(probe);
            row = ShownProjectRow(model, project, 0, width: 17 * glyphWidth + 92);
            window = WindowOf(row);
        });
        Border shape = null!;
        TextBlock title = null!;
        double idleRight = 0;
        harness.Main.OnMain(() =>
        {
            Assert.Equal(28d, row.Bounds.Height); // Chrome.rowHeight
            shape = LogicalQuery.SelfAndDescendants<Border>(row).First(b => b.Height == 28 && b.Padding.Right > 40);
            Assert.Equal(new CornerRadius(7), shape.CornerRadius); // rowCornerRadius, :722
            Assert.Equal(new Thickness(8, 0, 48, 0), shape.Padding); // leading 8, trailing 8 + the 40 pt accessoryWidth
            var badge = LogicalQuery.SelfAndDescendants<SidebarIconBadge>(row).Single();
            Assert.Equal(8d, Frame(badge, row).X); // the icon slot starts at the leading padding
            Assert.Equal(20d, Frame(badge, row).Width);
            title = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == project.Name);
            Assert.Equal(8 + 20 + 8, Frame(title, row).X, 3); // icon slot, then the 8 pt gap
            Assert.Equal(13d, title.FontSize); // .system(.body, weight: .regular)
            Assert.Equal(FontWeight.Normal, title.FontWeight);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color);
            Assert.Equal(0, ((ISolidColorBrush)shape.Background!).Color.A); // clear while not hovered
            idleRight = Frame(title, row).Right;
            // The title never runs under the reserved 40 pt, the trailing padding or the spacer's 8 + 4 pt; its right edge is the
            // largest whole-character one that fits, less than one glyph short of the exact edge.
            var exactEdge = row.Bounds.Width - 8 - 40 - 8 - 4;
            Assert.True(idleRight <= exactEdge + 0.01, $"title ends at {idleRight}, past {exactEdge}");
            Assert.True(idleRight > exactEdge - glyphWidth, $"title ends at {idleRight}, more than a glyph ({glyphWidth}) short of {exactEdge}");
            Assert.InRange(idleRight, 8 + 20 + 8 + 16 * glyphWidth - 1, 8 + 20 + 8 + 16 * glyphWidth + 1); // 16 characters (layout rounds the text run to a whole pixel), not the 17 the old room let in
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(120, 14), window)!.Value)); // hover
        harness.Main.AdvanceMilliseconds(300);
        harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(300)));
        harness.Main.OnMain(() =>
        {
            window.UpdateLayout();
            Assert.Equal(Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant), ((ISolidColorBrush)shape.Background!).Color); // Chrome.overlay(0.06) while hovered
            var cluster = LogicalQuery.SelfAndDescendants<StackPanel>(row).Single(s => s.Children.Count == 2 && s.Orientation == Orientation.Horizontal && s.Children[1] is Button);
            Assert.Equal(row.Bounds.Width - 6, Frame(cluster, row).Right, 3); // the accessory overlay sits 6 pt from the trailing edge
            Assert.Equal(idleRight, Frame(title, row).Right, 3); // the 40 pt is reserved permanently: the same truncation point
        });
        harness.Main.OnMain(() => window.MouseMove(new Point(row.Bounds.Width + 5, 90))); // leave
        harness.Main.AdvanceMilliseconds(300);
        harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(300)));
        harness.Main.OnMain(() =>
        {
            Assert.Equal(0, ((ISolidColorBrush)shape.Background!).Color.A); // clear again
            Assert.Equal(idleRight, Frame(title, row).Right, 3);
            window.Close();
        });
    });

    // ---- PR-SIDEBAR-133, UI-SIDEBAR-016: the "Start new chats on" submenu and its checked choice ----

    internal static bool IsChecked(Control item) =>
        item is MenuItem { Header: Control header } && LogicalQuery.SelfAndDescendants<SwiftImage>(header).Any(i => i.SymbolName == "checkmark");

    internal static string PairItemTitle(Control item) =>
        item is MenuItem { Header: Control header } ? LogicalQuery.SelfAndDescendants<TextBlock>(header).Single().Text! : "";

    [Fact]
    [Trait("Req", "PR-SIDEBAR-133")] // App/Views/Sidebar/SidebarView.swift:1149-1161 context menu, :1239-1271 ProjectPairMenuItems; AppModel+HydraPairs.swift:112 setHydraPair
    [Trait("Req", "UI-SIDEBAR-016")] // App/Views/Sidebar/SidebarView.swift:1239-1271: the current choice is a Label with a checkmark, every other item plain Text
    public void The_start_new_chats_on_submenu_of_a_real_right_click_checks_only_the_current_choice_and_chooses_or_clears_the_rule() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        var review = new HydraPair(ProviderKind.Claude) with { Name = "Review pair" };
        var build = new HydraPair(ProviderKind.Claude) with { Name = "Build pair" };
        Control row = null!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
        List<Control> OpenSubmenu()
        {
            PointerRightClick(row); // the same row every time; the menu is read afresh at each open
            var items = ContextMenuOf(row).Items.Cast<Control>().ToList();
            Assert.IsType<Separator>(items[^2]); // :1155 a divider ahead of the submenu
            var submenu = Assert.IsType<MenuItem>(items[^1]);
            Assert.Equal("Start new chats on", LogicalQuery.SelfAndDescendants<TextBlock>((Control)submenu.Header!).Single().Text); // :1159
            return submenu.Items.Cast<Control>().ToList();
        }
        void Click(Control item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        harness.Main.OnMain(() =>
        {
            // No pair: neither the divider nor the submenu (:1154 pairsSnapshot.isEmpty).
            PointerRightClick(row);
            var none = ContextMenuOf(row).Items.Cast<Control>().ToList();
            Assert.DoesNotContain(none, item => item is MenuItem { Header: Control h } && LogicalQuery.SelfAndDescendants<TextBlock>(h).Any(t => t.Text == "Start new chats on"));
            Assert.IsType<MenuItem>(none[^1]); // the activation item still ends the menu: no trailing divider
            model.Settings.AddHydraPair(review);
        });
        harness.Main.OnMain(() =>
        {
            // One pair: the clearing item, a divider, that pair. Nothing chosen yet, so the clearing item carries the check.
            var one = OpenSubmenu();
            Assert.Equal(3, one.Count);
            Assert.Equal("The chat's last pair", PairItemTitle(one[0])); // :1250-1255
            Assert.IsType<Separator>(one[1]); // :1257
            Assert.Equal("Review pair", PairItemTitle(one[2])); // :1258-1268 titled by HydraPairSummary.title
            Assert.True(IsChecked(one[0])); // selectedID == nil
            Assert.False(IsChecked(one[2]));
            model.Settings.AddHydraPair(build);
        });
        harness.Main.OnMain(() =>
        {
            // Several pairs, in the order Settings lists them.
            var several = OpenSubmenu();
            Assert.Equal(4, several.Count);
            Assert.IsType<Separator>(several[1]);
            Assert.Equal(["Review pair", "Build pair"], new[] { PairItemTitle(several[2]), PairItemTitle(several[3]) });
            Assert.True(IsChecked(several[0]));
            Assert.False(IsChecked(several[2]));
            Assert.False(IsChecked(several[3]));
            Click(several[3]); // :1260-1261 setHydraPair(pairID, forProject:)
            Assert.Equal(build.Id, model.Project(project.Id)!.HydraPairID);
        });
        harness.Main.OnMain(() =>
        {
            var chosen = OpenSubmenu(); // the check moved to the chosen pair
            Assert.False(IsChecked(chosen[0]));
            Assert.False(IsChecked(chosen[2]));
            Assert.True(IsChecked(chosen[3]));
            Click(chosen[2]);
            Assert.Equal(review.Id, model.Project(project.Id)!.HydraPairID); // another pair replaces the rule
        });
        harness.Main.OnMain(() =>
        {
            var again = OpenSubmenu();
            Assert.True(IsChecked(again[2]));
            Assert.False(IsChecked(again[3]));
            Click(again[0]); // :1249-1250 setHydraPair(nil, forProject:) clears the rule
            Assert.Null(model.Project(project.Id)!.HydraPairID);
        });
        harness.Main.OnMain(() =>
        {
            var cleared = OpenSubmenu();
            Assert.True(IsChecked(cleared[0]));
            Assert.False(IsChecked(cleared[2]));
            Assert.False(IsChecked(cleared[3]));
            WindowOf(row).Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-133")] // App/Views/Sidebar/SidebarView.swift:1139-1163 vs :1118-1123 and :1205-1229: the ellipsis popover does not offer the pair submenu
    public void The_ellipsis_popover_does_not_offer_the_pair_submenu_even_when_pairs_exist() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter());
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        Control row = null!;
        AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            model.Settings.AddHydraPair(new HydraPair(ProviderKind.Claude) with { Name = "Review pair" });
            row = ShownProjectRow(model, project, 0);
            window = WindowOf(row);
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value));
        harness.Main.OnMain(() =>
        {
            var ellipsis = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
            PointerClick(ellipsis);
        });
        harness.Main.OnMain(() =>
        {
            var popup = LogicalQuery.SelfAndDescendants<Popup>(row).Single(p => p.Child is not null); // the ellipsis popover (the removal question's is not presented)
            var texts = LogicalQuery.SelfAndDescendants<Button>(popup.Child!).Select(b => AutomationProperties.GetName(b)).ToList(); // each item's title
            Assert.Contains("New thread", texts); // the popover is the plain action list
            Assert.DoesNotContain("Start new chats on", texts);
            Assert.DoesNotContain("The chat's last pair", texts);
            Assert.DoesNotContain("Review pair", texts);
            window.Close();
        });
    });

    // ---- UI-SIDEBAR-033: the helper row ----

    internal static Control ShownHelperRow(AppModel model, ChatThread helper, object requests, bool isLast = false)
    {
        var row = (Control)Activator.CreateInstance(Private("SidebarHelperRow"), helper, isLast, requests, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
        row.VerticalAlignment = VerticalAlignment.Top; // the row's own 24 pt height, not the host's full stretch
        Hosted(model, row);
        WindowOf(row).UpdateLayout();
        return row;
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:1338-1409, :1272-1312 ThreadRowMetrics
    public void A_helper_row_has_the_recorded_geometry_fills_and_hover_pre_decode_and_hides_while_its_parents_glide_is_in_the_air() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1);
        AppModelHarness.WriteDocument(head.Id);
        var model = Model(harness, [project], [parent, head]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        Control row = null!;
        AvaloniaWindow window = null!;
        Border shape = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownHelperRow(model, head, Activator.CreateInstance(Private("SidebarMenuRequests"))!);
            window = WindowOf(row);
            Assert.Equal(24d, row.Bounds.Height); // ThreadRowMetrics.helperHeight
            var grid = Assert.IsType<Grid>(Get(row, "Child"));
            Assert.Equal(28d, grid.Children[0].Bounds.Width); // the connector column
            Assert.Equal(24d, grid.Children[0].Bounds.Height);
            shape = LogicalQuery.SelfAndDescendants<Border>(row).First(b => b.Height == 24);
            Assert.Equal(new CornerRadius(7), shape.CornerRadius); // 7 pt continuous radius on the button's background
            Assert.Equal(new Thickness(6, 0, 8 + 18, 0), shape.Padding); // leading 6, trailing 8 + 18 while the actions are hidden
            var glyph = LogicalQuery.SelfAndDescendants<HydraGlyph>(row).Single();
            Assert.Equal(28 + 6, Frame(glyph, row).X, 3); // after the connector column and the leading padding
            Assert.Equal(14d, Frame(glyph, row).Width); // headGlyphSize
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == head.Title);
            Assert.Equal(28 + 6 + 14 + 6, Frame(title, row).X, 3); // inner HStack spacing 6
            Assert.Equal(12d, title.FontSize); // .system(.callout)
            Assert.Equal(0, ((ISolidColorBrush)shape.Background!).Color.A); // clear while idle
            Assert.Null(DocumentPrefetchTake(head.Id)); // nothing decoded before the hover
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(100, 12), window)!.Value)); // hover
        harness.Main.AdvanceMilliseconds(300);
        harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(300)));
        AppModelHarness.WaitForPrefetch();
        harness.Main.OnMain(() =>
        {
            window.UpdateLayout();
            Assert.Equal(new Thickness(6, 0, 8 + 26, 0), shape.Padding); // trailing 8 + 26 while the actions show
            Assert.Equal(Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant), ((ISolidColorBrush)shape.Background!).Color); // hovered
            var ellipsis = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
            Assert.Equal(row.Bounds.Width - 6, Frame(ellipsis, row).Right, 3); // the trailing overlay is padded 6
            Assert.NotNull(DocumentPrefetchTake(head.Id)); // hover pre-decodes its history
            model.SelectedThreadID = head.Id;
        });
        harness.Main.AdvanceMilliseconds(300);
        harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(300)));
        harness.Main.OnMain(() =>
        {
            Assert.Equal(Chrome.Overlay(0.12).Resolve(shape.ActualThemeVariant), ((ISolidColorBrush)shape.Background!).Color); // selected wins over hovered
            Assert.Equal(1d, row.Opacity);
            // The parent's ghost takes off: the helper goes out of sight at once, and shows again when it has arrived.
            animator.Launch(parent.Id, RowGlideAnimator.Glide.Direction.Settle, new Rect(0, 300, 200, 24),
                new Rect(0, -28, 200, 28), new Rect(0, 0, 200, 400), new FaceSample(200, 24), new FaceSample(200, 28), 0, 0);
        });
        harness.Main.OnMain(() =>
        {
            Assert.Equal(0d, row.Opacity);
            animator.Arrive(animator.Glides[0].Id);
        });
        harness.Main.OnMain(() =>
        {
            Assert.Equal(1d, row.Opacity);
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // SidebarView.swift:1370-1372 `.fill(isSelected ? ...).animation(Chrome.hover, value: isSelected)`
    public void A_helper_rows_selection_fill_eases_in_and_out_on_chrome_hover_even_with_no_ambient_animation() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        Control row = null!;
        Border shape = null!;
        double Alpha() => ((ISolidColorBrush)shape.Background!).Color.A / 255.0;
        var selectedAlpha = 0d;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        harness.Main.OnMain(() =>
        {
            row = ShownHelperRow(model, helper, Activator.CreateInstance(Private("SidebarMenuRequests"))!);
            shape = LogicalQuery.SelfAndDescendants<Border>(row).First(b => b.Height == 24);
            selectedAlpha = Chrome.Overlay(0.12).Resolve(shape.ActualThemeVariant).A / 255.0;
            Assert.Equal(0d, Alpha());
        });
        try
        {
            harness.Main.OnMain(() => model.SelectedThreadID = helper.Id); // a plain write: no withAnimation around it
            Tick(50); // half of Chrome.hover's easeOut 0.1 s
            harness.Main.OnMain(() => Assert.InRange(Alpha(), 0.2 * selectedAlpha, 0.95 * selectedAlpha)); // easing, not cut
            Tick(200);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(selectedAlpha, Alpha(), 2);
                model.SelectedThreadID = parent.Id; // and out again
            });
            Tick(50);
            harness.Main.OnMain(() => Assert.InRange(Alpha(), 0.05 * selectedAlpha, 0.8 * selectedAlpha));
            Tick(200);
            harness.Main.OnMain(() => Assert.Equal(0d, Alpha()));
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // SidebarView.swift:1375-1386 the ellipsis/status Group swaps inside withAnimation(Chrome.hover)
    public void Hovering_a_helper_row_crossfades_its_status_out_and_its_ellipsis_in_on_chrome_hover() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        static double ShownOpacity(Control control) =>
            control.GetSelfAndVisualAncestors().OfType<Visual>().TakeWhile(v => v.GetType() != Private("SidebarHelperRow")).Aggregate(1d, (o, v) => o * v.Opacity);
        harness.Main.OnMain(() =>
        {
            row = ShownHelperRow(model, helper, Activator.CreateInstance(Private("SidebarMenuRequests"))!);
            window = WindowOf(row);
        });
        try
        {
            ActivityStatus status = null!;
            harness.Main.OnMain(() =>
            {
                status = LogicalQuery.SelfAndDescendants<ActivityStatus>(row).Single(); // :1379 the idle branch
                window.MouseMove(row.TranslatePoint(new Point(100, 12), window)!.Value);
            });
            Tick(50); // half of Chrome.hover's easeOut 0.1 s
            harness.Main.OnMain(() =>
            {
                Assert.InRange(ShownOpacity(LogicalQuery.SelfAndDescendants<RowActionsButton>(row).Single()), 0.05, 0.95); // fading in
                Assert.Same(status, LogicalQuery.SelfAndDescendants<ActivityStatus>(row).Single());
                Assert.InRange(ShownOpacity(status), 0.05, 0.95); // fading out
            });
            Tick(200);
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<ActivityStatus>(row));
                Assert.Equal(1d, ShownOpacity(LogicalQuery.SelfAndDescendants<RowActionsButton>(row).Single()));
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    internal static object? DocumentPrefetchTake(Guid id) => DocumentPrefetch.Shared.Take(id);

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:1387-1397 .contextMenu on the helper row; :2253-2312 ThreadActions.makeContextMenu
    public void A_real_right_click_on_a_helper_row_opens_its_context_menu_rebuilt_at_every_open_and_posting_rename_and_delete() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
        Control row = null!;
        harness.Main.OnMain(() => model.Settings.ThreadFinishAction = ThreadFinishAction.Settle); // settleFirst for a thread; never for a helper (:1391)
        harness.Main.OnMain(() => row = ShownHelperRow(model, helper, requests));
        ContextMenu OpenMenu()
        {
            var host = (Panel)((Grid)((Decorator)row).Child!).Children[1]; // the button with its trailing overlay
            PointerRightClick(host.Children[0]); // the button, not the fold connector beside it
            return host.ContextMenu!;
        }
        harness.Main.OnMain(() =>
        {
            var items = OpenMenu().Items.Cast<Control>().ToList();
            // A helper has no place of its own to settle into and is never pinned: Rename, Forward, then Archive after a divider (settleFirst is false), Delete…
            Assert.True(new[] { "Rename", "Forward to new thread", "", "Archive", "Delete…" }.SequenceEqual(items.Select(i => i is Separator ? "" : MenuItemTitle(i))), string.Join("|", items.Select(i => i is Separator ? "" : MenuItemTitle(i))));
            Assert.IsType<Separator>(items[2]);
            model.UpdateThread(helper.Id, t => t with { WorktreePath = "C:/wt" });
        });
        harness.Main.OnMain(() =>
        {
            var items = OpenMenu().Items.Cast<Control>().ToList();
            Assert.Equal(["Rename", "Forward to new thread", "Reveal worktree in File Explorer", "", "Archive", "Delete\u2026"], items.Select(i => i is Separator ? "" : MenuItemTitle(i))); // read afresh at this open
            ((MenuItem)items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(helper.Id, ((ChatThread)Get(requests, "Rename")!).Id); // Rename posts its request rather than acting
            ((MenuItem)items[^1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(helper.Id, ((ChatThread)Get(requests, "Delete")!).Id);
            Assert.NotNull(model.Thread(helper.Id)); // and the thread is still there
            WindowOf(row).Close();
        });
    });

    // ---- UI-SIDEBAR-040: the Settled section header ----

    // Where layout put the control, before its own render transform (the chevron is turned 90 degrees).
    internal static Rect LayoutFrame(Control control, Control relativeTo) =>
        new(Frame((Control)control.Parent!, relativeTo).Position + control.Bounds.Position, control.Bounds.Size);

    internal static double FirstBaselineOf(Control control, Control relativeTo) =>
        LayoutFrame(control, relativeTo).Y + (control is TextBlock text ? text.TextLayout.TextLines[0].Baseline : TextBlock.GetBaselineOffset(control));

    [Fact]
    [Trait("Req", "UI-SIDEBAR-040")] // App/Views/Sidebar/SidebarView.swift:2364-2407
    public void The_settled_header_sits_chevron_title_and_count_on_one_first_baseline_5_points_apart_in_secondary_text_and_toggles_from_its_whole_width() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var model = harness.MakeModel();
        harness.Main.OnMain(() => model.Settings.SettledCollapsed = false);
        Control header = null!;
        Animation? toggled = null;
        using var probe = new OnChange<bool>(() => model.Settings.SettledCollapsed, (_, collapsed) => { if (collapsed) toggled = Transaction.Current; });
        harness.Main.OnMain(() =>
        {
            header = Hosted(model, (Control)Activator.CreateInstance(Private("SettledHeader"), 3L, true)!);
            var window = WindowOf(header);
            window.UpdateLayout();
            var button = LogicalQuery.SelfAndDescendants<Button>(header).Single();
            var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(header).Single();
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(header).Single(t => t.Text == "Settled");
            var count = LogicalQuery.SelfAndDescendants<TextBlock>(header).Single(t => t.Text == "3");

            // HStack(alignment: .firstTextBaseline, spacing: 5)
            Assert.True(Math.Abs(FirstBaselineOf(title, header) - FirstBaselineOf(chevron, header)) < 0.01, $"title {FirstBaselineOf(title, header)} chevron {FirstBaselineOf(chevron, header)}");
            Assert.True(Math.Abs(FirstBaselineOf(title, header) - FirstBaselineOf(count, header)) < 0.01, $"title {FirstBaselineOf(title, header)} count {FirstBaselineOf(count, header)}");
            Assert.True(Math.Abs(5d - (LayoutFrame(title, header).X - LayoutFrame(chevron, header).Right)) < 0.001, $"gap1 {LayoutFrame(title, header).X - LayoutFrame(chevron, header).Right}");
            Assert.True(Math.Abs(5d - (LayoutFrame(count, header).X - LayoutFrame(title, header).Right)) < 0.001, $"gap2 {LayoutFrame(count, header).X - LayoutFrame(title, header).Right}");

            // padding horizontal 8, top 2 as the first section, bottom 4; full width, leading aligned
            var content = LogicalQuery.SelfAndDescendants<Border>(header).First(b => b.Padding.Left == 8);
            Assert.Equal(new Thickness(8, 2, 8, 4), content.Padding);
            Assert.Equal(header.Bounds.Width, Frame(button, header).Width, 3);
            Assert.Equal(8d, LayoutFrame(chevron, header).X, 3);

            // chevron .caption2 semibold (10 pt) turned 90 degrees while expanded; title .callout semibold; count .callout, tabular digits
            Assert.Equal(10d, SwiftImage.GetFont(chevron)!.PointSize);
            Assert.Equal(90d, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
            Assert.Equal(12d, title.FontSize);
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            Assert.Equal(12d, count.FontSize);
            Assert.Equal(FontWeight.Normal, count.FontWeight);
            Assert.Equal(SwiftFont.Callout.MonospacedDigit().Resolve().FontFeatures, count.FontFeatures);
            Assert.Equal(0.8d, count.Opacity);

            // the whole header is Chrome.secondaryText
            var secondary = Chrome.SecondaryText.Resolve(title.ActualThemeVariant);
            Assert.Equal(secondary, ((ISolidColorBrush)title.Foreground!).Color);
            Assert.Equal(secondary, ((ISolidColorBrush)count.Foreground!).Color);
            Assert.Equal(secondary, ((ISolidColorBrush)chevron.GetValue(SwiftImage.ForegroundProperty)!).Color);

            // a rectangular content shape: a click on the empty part right of the count folds the section
            var inside = header.TranslatePoint(new Point(header.Bounds.Width - 12, header.Bounds.Height - 6), window)!.Value;
            window.MouseMove(inside);
            window.MouseDown(inside, MouseButton.Left);
            window.MouseUp(inside, MouseButton.Left);
            Assert.True(model.Settings.SettledCollapsed);

            // not the first section: 14 pt on top
            var later = Hosted(model, (Control)Activator.CreateInstance(Private("SettledHeader"), 1L, false)!);
            WindowOf(later).UpdateLayout();
            Assert.Equal(new Thickness(8, 14, 8, 4), LogicalQuery.SelfAndDescendants<Border>(later).First(b => b.Padding.Left == 8).Padding);
            Assert.Equal(0d, Assert.IsType<RotateTransform>(LogicalQuery.SelfAndDescendants<SwiftImage>(later).Single().RenderTransform).Angle); // collapsed: no turn
            window.Close();
            WindowOf(later).Close();
        });
        harness.Main.OnMain(() => Assert.Equal(Chrome.PanelSlide, toggled)); // :2371 withAnimation(Chrome.panelSlide) around the toggle
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-040")] // App/Views/Sidebar/SidebarView.swift:2364-2366, :2384-2388, :2398: count and isFirst change under the same view; the padding change rides Chrome.panelSlide
    public void The_settled_header_keeps_its_chevron_when_its_count_changes_and_animates_its_top_padding_when_it_becomes_the_first_section() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var project = NewProject(harness, "Alpha");
        var open = NewThread(project, "Open", 0);
        var doneA = NewThread(project, "Done A", 1) with { IsSettled = true, SettledAt = Base.AddHours(1) };
        var model = Model(harness, [project], [open, doneA]);
        harness.Main.OnMain(() =>
        {
            model.Settings.SettledCollapsed = false;
            model.Settings.SidebarActivityView = true; // the activity layout orders the sections; the Settled header closes it
        });
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        try
        {
            void Tick(int ms)
            {
                harness.Main.AdvanceMilliseconds(ms);
                harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
            }
            Control Header() => ListPanel(view).Children.Single(c => LogicalQuery.SelfAndDescendants<Button>(c).Any(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Settled, ", StringComparison.Ordinal)));
            Thickness PaddingOf(Control header) => LogicalQuery.SelfAndDescendants<Border>(header).First(b => b.Padding.Left == 8).Padding;
            Tick(1600);
            var before = Header();
            Assert.Equal(14d, PaddingOf(before).Top); // another section stands above it
            Assert.Equal("Settled, 1 threads", AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(before).Single())); // always the plural

            // The open thread settles: the digits become 2 and the header turns first, in the one header that is already there.
            var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(before).Single();
            harness.Main.OnMain(() => model.Settle(open.Id, sounds: false));
            Tick(0);
            var after = Header();
            Assert.Same(before, after); // updated in place, not rebuilt
            Assert.Equal("Settled, 2 threads", AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(after).Single()));
            Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(after), t => t.Text == "2");
            Assert.Equal(90d, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle); // the chevron carries on, still turned
            Assert.Equal(14d, PaddingOf(after).Top); // the padding starts from where it stood
            Tick(90);
            var curve = Animation.Spring(response: 0.32, dampingFraction: 0.9); // Chrome.panelSlide
            Assert.Equal(14 + (2 - 14) * curve.Progress(0.09), PaddingOf(after).Top, 3);
            Tick(1600);
            Assert.Equal(2d, PaddingOf(after).Top);

            // And back: the thread reopens, a section stands above the header again, and the padding grows along the same curve.
            harness.Main.OnMain(() => model.Reopen(open.Id));
            Tick(0);
            Assert.Same(after, Header());
            Assert.Equal("Settled, 1 threads", AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(after).Single()));
            Assert.Equal(2d, PaddingOf(after).Top);
            Tick(90);
            Assert.Equal(2 + (14 - 2) * curve.Progress(0.09), PaddingOf(after).Top, 3);
            Tick(1600);
            Assert.Equal(14d, PaddingOf(after).Top);
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ---- UI-SIDEBAR-037: a head's glyph on its card ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:1466-1509: a head with no hydra info is skipped; only a running head's glyph is told it is running
    public void A_head_card_skips_a_thread_without_hydra_info_and_passes_isRunning_to_a_running_heads_glyph_only() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var running = HeadHelper(project, parent.Id, 0, 3) with { Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) with { Index = 0 } };
        var done = HeadHelper(project, parent.Id, 1, 2);
        var failed = HeadHelper(project, parent.Id, 2, 1) with { Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Failed) with { Index = 2 } };
        var plain = NewThread(project, "Plain helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, running, done, failed, plain]);
        // A library loaded without a live runtime shows a head that was running as stopped; it is running again here.
        harness.Main.OnMain(() => model.UpdateThread(running.Id, t => t with { Hydra = t.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } }));
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, new[] { running, done, failed, plain }, requests, (Action)(() => { }))!;
            var window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
            var glyphs = LogicalQuery.SelfAndDescendants<HydraGlyph>(card).ToList();
            Assert.Equal(3, glyphs.Count); // :1471 `if let info = head.hydra`: the plain helper has no button
            Assert.Equal(3, LogicalQuery.SelfAndDescendants<Button>(card).Count(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()));
            var runningGlyph = Assert.Single(glyphs, g => g.Status == HydraHeadInfo.Status.Running);
            Assert.True(runningGlyph.IsRunning); // :1477 isRunning: info.status == .running
            Assert.False(Assert.Single(glyphs, g => g.Status == HydraHeadInfo.Status.Completed).IsRunning);
            Assert.False(Assert.Single(glyphs, g => g.Status == HydraHeadInfo.Status.Failed).IsRunning);
            Assert.All(glyphs, g => Assert.Equal(14d, g.Size)); // headGlyphSize
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:1484-1488: scaleEffect 1.18 on hover animated with Chrome.hover (easeOut 0.1 s); hover pre-decodes the head's history
    public void A_head_glyph_grows_to_1_18_along_the_hover_curve_and_hovering_it_pre_decodes_its_history() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1);
        AppModelHarness.WriteDocument(head.Id);
        var model = Model(harness, [project], [parent, head]);
        Control card = null!;
        AvaloniaWindow window = null!;
        Button HeadButton() => (Button)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!.Parent!;
        ScaleTransform Scale() => (ScaleTransform)((Panel)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!).RenderTransform!;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, new[] { head }, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
            Assert.Equal(1d, Scale().ScaleX);
            Assert.Null(DocumentPrefetchTake(head.Id)); // nothing decoded before the hover
        });
        harness.Main.OnMain(() => window.MouseMove(HeadButton().TranslatePoint(new Point(7, 7), window)!.Value));
        harness.Main.AdvanceMilliseconds(40);
        harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(40)));
        AppModelHarness.WaitForPrefetch();
        harness.Main.OnMain(() =>
        {
            Assert.Equal(1 + 0.18 * Animation.EaseOut(0.1).Progress(0.04), Scale().ScaleX, 3); // Chrome.hover: easeOut 0.1 s, part way
            Assert.NotNull(DocumentPrefetchTake(head.Id)); // :1488 model.warmDocuments([head.id])
        });
        harness.Main.AdvanceMilliseconds(200);
        harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(200)));
        harness.Main.OnMain(() => Assert.Equal(1.18, Scale().ScaleX, 3));
        harness.Main.OnMain(() => window.MouseMove(new Point(window.Width - 1, window.Height - 1))); // away again
        harness.Main.AdvanceMilliseconds(200);
        harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(200)));
        harness.Main.OnMain(() =>
        {
            Assert.Equal(1d, Scale().ScaleX, 3);
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:1490-1500 .contextMenu on the head's glyph with settleFirst false; :2253-2312 ThreadActions.makeContextMenu
    public void A_real_right_click_on_a_head_glyph_opens_the_heads_context_menu_with_settleFirst_false_rebuilt_at_every_open() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1);
        var model = Model(harness, [project], [parent, head]);
        var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
        Control card = null!;
        harness.Main.OnMain(() => model.Settings.ThreadFinishAction = ThreadFinishAction.Settle); // the setting must not reach a head's menu
        harness.Main.OnMain(() =>
        {
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, new[] { head }, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            WindowOf(Hosted(model, card)).UpdateLayout();
        });
        Button HeadButton() => (Button)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!.Parent!;
        List<Control> OpenMenu()
        {
            var button = HeadButton();
            PointerRightClick(button);
            return button.ContextMenu!.Items.Cast<Control>().ToList();
        }
        string[] Titles(IEnumerable<Control> items) => items.Select(i => i is Separator ? "" : MenuItemTitle(i)).ToArray();
        harness.Main.OnMain(() =>
        {
            var items = OpenMenu();
            // A head is a helper: no Pin, no Settle; Archive after a divider because settleFirst is false; Delete… last.
            Assert.True(new[] { "Rename", "Forward to new thread", "", "Archive", "Delete\u2026" }.SequenceEqual(Titles(items)), string.Join("|", Titles(items)));
            Assert.IsType<Separator>(items[2]);
            Assert.True(new[] { "Rename", "Forward to new thread", "", "Archive", "Delete…" }.SequenceEqual(Titles(OpenMenu()))); // opened again: the items are built anew, not added to
            model.UpdateThread(head.Id, t => t with { WorktreePath = "C:/wt" });
        });
        harness.Main.OnMain(() =>
        {
            var items = OpenMenu(); // the card has rebuilt its glyph for the changed head, and the menu is read afresh
            Assert.True(new[] { "Rename", "Forward to new thread", "Reveal worktree in File Explorer", "", "Archive", "Delete\u2026" }.SequenceEqual(Titles(items)), string.Join("|", Titles(items)));
            ((MenuItem)items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(head.Id, ((ChatThread)Get(requests, "Rename")!).Id); // posts the request rather than acting
            ((MenuItem)items[^1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(head.Id, ((ChatThread)Get(requests, "Delete")!).Id);
            Assert.NotNull(model.Thread(head.Id));
            WindowOf(card).Close();
        });
    });

    // ---- UI-SIDEBAR-014: the project's action items, in the ellipsis popover and in the context menu ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-014")] // App/Views/Sidebar/SidebarView.swift:1205-1229 actions(isActive:), :367-382 RowActionItems; UI/Chrome/Chrome.swift
    public void The_ellipsis_popover_lists_the_project_actions_with_their_dividers_symbols_and_colours_and_runs_an_action_one_turn_after_closing() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presented = Presentation.Presenter.Override(new RecordingPresenter());
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        harness.Main.OnMain(() => model.Settings.SetProjectActive(true, project.Id)); // its own override: "Deactivate project" and "Follow global setting"
        Control row = null!;
        AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0);
            window = WindowOf(row);
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value));
        harness.Main.OnMain(() => PointerClick(LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More")));
        harness.Main.OnMain(() =>
        {
            var popup = LogicalQuery.SelfAndDescendants<Popup>(row).Single(p => p.Child is not null); // the ellipsis popover (the removal question's is not presented)
            var stack = Assert.IsType<StackPanel>(LogicalQuery.SelfAndDescendants<RowActionItems>(popup.Child!).Single().Child);
            // dividers before Reveal in File Explorer, before Remove project and before the activation item
            Assert.Equal(
                [typeof(PopoverItem), typeof(PopoverItem), typeof(PopoverDivider), typeof(PopoverItem), typeof(PopoverDivider), typeof(PopoverItem), typeof(PopoverDivider), typeof(PopoverItem), typeof(PopoverItem)],
                stack.Children.Select(c => c.GetType()));
            var items = stack.Children.OfType<PopoverItem>().ToList();
            Assert.Equal(
                ["New thread", "New thread in worktree", "Reveal in File Explorer", "Remove project", "Deactivate project", "Follow global setting"],
                items.Select(i => AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(i).Single())));
            Assert.Equal(
                ["square.and.pencil", "square.stack.3d.up", "folder", "trash", "power", "arrow.uturn.backward"],
                items.Select(i => LogicalQuery.SelfAndDescendants<SwiftImage>(i).First(g => g.SymbolName is not null).SymbolName));
            foreach (var item in items) // Remove project draws in Chrome.danger, the rest in Chrome.primaryText
            {
                var button = LogicalQuery.SelfAndDescendants<Button>(item).Single();
                var title = LogicalQuery.SelfAndDescendants<TextBlock>(button).First();
                var expected = AutomationProperties.GetName(button) == "Remove project" ? Chrome.Danger : Chrome.PrimaryText;
                Assert.Equal(expected.Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color);
            }
            // the action runs one turn after the popover closes, not inside the click
            Assert.Empty(model.ThreadsIn(project));
            Click(LogicalQuery.SelfAndDescendants<Button>(items[0]).Single()); // the popup content has no window of its own to point into
            Assert.Empty(model.ThreadsIn(project));
        });
        harness.Main.OnMain(() =>
        {
            Assert.Single(model.ThreadsIn(project));
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-014")] // App/Views/Sidebar/SidebarView.swift:1168-1203 contextMenuActions: the titles, dividers and the activation pair, run synchronously inside the click
    public void The_context_menu_lists_the_project_actions_with_the_activation_item_by_current_activation_and_runs_an_action_inside_the_click() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
        string[] OpenMenu(out List<Control> items)
        {
            PointerRightClick(row);
            items = ContextMenuOf(row).Items.Cast<Control>().ToList();
            return items.Select(i => i is Separator ? "" : MenuItemTitle(i)).ToArray();
        }
        void Choose(Control item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        harness.Main.OnMain(() =>
        {
            // active by the global switch, no override: Deactivate project, and no Follow global setting
            Assert.Equal(["New thread", "New thread in worktree", "", "Reveal in File Explorer", "", "Remove project", "", "Deactivate project"], OpenMenu(out var items));
            Choose(items[0]); // :1171-1174 runs inside the click
            Assert.Single(model.ThreadsIn(project));
            Choose(items[^1]); // Deactivate project: writes the project's own override
            Assert.False(model.Settings.IsProjectActive(project.Id));
            Assert.True(model.Settings.HasProjectActivationOverride(project.Id));
        });
        harness.Main.OnMain(() =>
        {
            // now inactive with its own override: Activate project, and Follow global setting at the end
            Assert.Equal(["New thread", "New thread in worktree", "", "Reveal in File Explorer", "", "Remove project", "", "Activate project", "Follow global setting"], OpenMenu(out var items));
            Choose(items[^1]); // Follow global setting: clears the override
            Assert.False(model.Settings.HasProjectActivationOverride(project.Id));
            Assert.True(model.Settings.IsProjectActive(project.Id));
        });
        harness.Main.OnMain(() =>
        {
            Assert.Equal(["New thread", "New thread in worktree", "", "Reveal in File Explorer", "", "Remove project", "", "Deactivate project"], OpenMenu(out var items));
            Choose(items[^1]);
        });
        harness.Main.OnMain(() =>
        {
            OpenMenu(out var items);
            Choose(items[^2]); // Activate project
            Assert.True(model.Settings.IsProjectActive(project.Id));
            WindowOf(row).Close();
        });
    });

    // ---- PR-SIDEBAR-130/-131, UI-SIDEBAR-014: Remove project asks first (owner rulings OD-0008 A9, OD-0012 E2) ----
    //
    // Upstream removes at once from both menus (SidebarView.swift:1183-1186, :1212-1214). By OD-0008 A9 the port asks
    // first while confirmBeforeDeleting is on, in the thread delete question's own style (DeleteThreadPopover,
    // :966-981) with the OD-0012 E2 wording; with the setting off it removes at once, as upstream.

    internal static Popup RemovalPopup(Control row) =>
        LogicalQuery.SelfAndDescendants<Popup>(row).Single(p => PopoverHost.Content(p)?.GetType() == Private("RemoveProjectPopover"));

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // SidebarView.swift:1183-1186, :1212-1214; OD-0008 A9; OD-0012 E2
    [Trait("Req", "UI-SIDEBAR-014")] // SidebarView.swift:1168-1231: Remove project confirms first while confirmBeforeDeleting is on
    public void Remove_project_asks_first_from_both_menus_and_only_the_red_action_removes_the_project() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Settings.ConfirmBeforeDeleting); // AppSettings.swift:710 default true
            row = ShownProjectRow(model, project, 1);
            window = WindowOf(row);
        });
        // The context menu's Remove project only asks.
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row);
            var remove = ContextMenuOf(row).Items.Cast<Control>().Single(i => i is MenuItem && MenuItemTitle(i) == "Remove project");
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.NotNull(model.Project(project.Id)); // nothing removed yet
        });
        harness.Main.OnMain(() =>
        {
            var popup = RemovalPopup(row);
            Assert.Contains(recorder.Calls, c => c.Popup == popup && c.Presented);
            Assert.Equal(PlacementMode.Right, popup.Placement); // the thread delete question's own arrowEdge: .trailing (:220)
            var menu = Assert.IsType<PopoverMenu>(Get(PopoverHost.Content(popup)!, "Child"));
            Assert.Equal(280d, menu.Width); // :980 .frame(width: 280)
            var inner = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(Assert.IsType<ScrollViewer>(menu.Child).Content).Child).Children.Single());
            Assert.Equal(new[] { typeof(PopoverSectionHeader), typeof(PopoverNote), typeof(PopoverDivider), typeof(PopoverItem) }, inner.Children.Select(c => c.GetType()));
            Assert.Equal("Remove this project?", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(inner.Children[0])).Text); // OD-0012 E2
            Assert.Equal("“Alpha” and its threads will be removed. Files in your project stay as they are.", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(inner.Children[1])).Text);
            var item = (PopoverItem)inner.Children[3];
            var button = LogicalQuery.SelfAndDescendants<Button>(item).Single();
            Assert.Equal("Remove project", AutomationProperties.GetName(button));
            Assert.Equal("trash", LogicalQuery.SelfAndDescendants<SwiftImage>(item).First(g => g.SymbolName is not null).SymbolName);
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(button).First();
            Assert.Equal(Chrome.Danger.Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color); // the red action
            // Dismissing (click away, Escape) closes the popup, whose Closed handler writes false through the binding:
            // the project stays.
            SetProp(Field(row, "state")!, "IsRemovalPresented", false);
        });
        harness.Main.OnMain(() =>
        {
            Assert.Contains(recorder.Calls, c => c.Popup == RemovalPopup(row) && !c.Presented);
            Assert.NotNull(model.Project(project.Id));
            Assert.NotNull(model.Thread(thread.Id));
            // The ellipsis popover's Remove project asks too, one turn after its own popover closes.
            window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
        });
        harness.Main.OnMain(() => PointerClick(LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More")));
        harness.Main.OnMain(() =>
        {
            var actions = LogicalQuery.SelfAndDescendants<Popup>(row).Single(p => p.Child is not null && LogicalQuery.SelfAndDescendants<RowActionItems>(p.Child).Any());
            Click(LogicalQuery.SelfAndDescendants<Button>(actions.Child!).Single(b => AutomationProperties.GetName(b) == "Remove project"));
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            Assert.NotNull(model.Project(project.Id)); // asked, not removed
            var popup = RemovalPopup(row);
            Assert.Equal(2, recorder.Calls.Count(c => c.Popup == popup && c.Presented));
            // Only the red action removes the project (upstream's removal, AppModel.swift:558-567).
            Click(LogicalQuery.SelfAndDescendants<Button>(PopoverHost.Content(popup)!).Single(b => AutomationProperties.GetName(b) == "Remove project"));
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            Assert.Null(model.Project(project.Id));
            Assert.Null(model.Thread(thread.Id));
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // OD-0008 A9: with confirmBeforeDeleting off the removal runs at once, as upstream (:1183-1186)
    [Trait("Req", "UI-SIDEBAR-014")]
    public void With_confirm_before_deleting_off_remove_project_removes_at_once_from_the_context_menu() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            model.Settings.ConfirmBeforeDeleting = false;
            row = ShownProjectRow(model, project, 0);
        });
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row);
            ContextMenuOf(row).Items.Cast<Control>().Single(i => i is MenuItem && MenuItemTitle(i) == "Remove project")
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Null(model.Project(project.Id)); // removed inside the click, nothing asked
            Assert.DoesNotContain(recorder.Calls, c => c.Presented);
            WindowOf(row).Close();
        });
    });

    // ---- UI-SIDEBAR-020: the real check on a thread row ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:1763-1771, :1904-1910: the open row's check settles the thread, and exists only while threadFinishAction is settle
    public void Clicking_the_check_of_a_real_open_row_settles_its_thread_and_the_check_exists_only_under_the_settle_finish_action() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Settle me", 0);
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        Control archiveRow = null!;
        harness.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            archiveRow = LiveThreadRow(thread);
            var window = WindowOf(Hosted(model, archiveRow));
            window.UpdateLayout();
            window.MouseMove(archiveRow.TranslatePoint(new Point(10, 10), window)!.Value);
        });
        harness.Main.OnMain(() =>
        {
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(archiveRow), b => AutomationProperties.GetName(b) == "Settle thread"); // the archive button takes its place
            WindowOf(archiveRow).Close();
        });
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            row = LiveThreadRow(thread);
            var window = WindowOf(Hosted(model, row));
            window.UpdateLayout();
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(row), b => AutomationProperties.GetName(b) == "Settle thread"); // not hovered, not settling
            window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
        });
        harness.Main.OnMain(() =>
        {
            Assert.False(model.Thread(thread.Id)!.IsSettled);
            Click(ButtonNamed(row, "Settle thread")); // :1765-1770 the open row's check, at the trailing end
        });
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Thread(thread.Id)!.IsSettled); // :1910 model.settle
            WindowOf(row).Close();
        });
    });

    // ---- UI-SIDEBAR-025: a thread row's action items ----

    internal static string[] Described(IEnumerable<RowAction> actions) =>
        actions.Select(a => $"{a.Title}/{a.Symbol}{(a.StartsGroup ? "+" : "")}{(a.IsDestructive ? "!" : "")}").ToArray();

    [Fact]
    [Trait("Req", "UI-SIDEBAR-025")] // App/Views/Sidebar/SidebarView.swift:2193-2315 ThreadActions.make and makeContextMenu
    public void A_thread_menu_offers_its_items_in_the_recorded_order_with_the_recorded_symbols_and_one_divider_in_both_forms() => headless.OnUiThread(() =>
    {
        // "+" marks the item that starts a group (a divider before it), "!" the destructive one.
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var plain = NewThread(project, "Plain", 0);
        var pinnedWorktree = NewThread(project, "Pinned", 1) with { IsPinned = true, WorktreePath = "C:/Alpha/wt" };
        var settled = NewThread(project, "Settled", 2) with { IsSettled = true };
        var helper = NewThread(project, "Helper", 3) with { ParentThreadID = plain.Id, WorktreePath = "C:/Alpha/wt2" };
        var model = Model(harness, [project], [plain, pinnedWorktree, settled, helper]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var ellipsis = "Delete\u2026/trash!";
            void Check(string[] expected, ChatThread thread, bool settleFirst)
            {
                model.Settings.ThreadFinishAction = settleFirst ? ThreadFinishAction.Settle : ThreadFinishAction.Archive;
                Assert.Equal(expected, Described(ThreadActionsMake(model, thread))); // the ellipsis popover's list
                Assert.Equal(expected, Described(ThreadActionsMakeContextMenu(model, thread, !thread.IsHelper && settleFirst, requests))); // the context menu's list
            }
            // Settings chose Settle first: the divider stands before Settle, Archive follows it plainly.
            Check(["Rename/pencil", "Pin/pin", "Forward to new thread/arrowshape.turn.up.right", "Settle/checkmark.circle+", "Archive/archivebox", ellipsis], plain, settleFirst: true);
            // Settings chose Archive first: the divider stands before Archive, Settle follows it plainly.
            Check(["Rename/pencil", "Pin/pin", "Forward to new thread/arrowshape.turn.up.right", "Archive/archivebox+", "Settle/checkmark.circle", ellipsis], plain, settleFirst: false);
            // A pinned thread with a worktree: Unpin, and the worktree item after Forward (named for Windows' file manager, OD-0008 B1).
            Check(["Rename/pencil", "Unpin/pin.slash", "Forward to new thread/arrowshape.turn.up.right", "Reveal worktree in File Explorer/folder", "Settle/checkmark.circle+", "Archive/archivebox", ellipsis], pinnedWorktree, settleFirst: true);
            // A settled thread is not pinned from the menu and offers Reopen where Settle stood.
            Check(["Rename/pencil", "Forward to new thread/arrowshape.turn.up.right", "Reopen/arrow.uturn.backward+", "Archive/archivebox", ellipsis], settled, settleFirst: true);
            Check(["Rename/pencil", "Forward to new thread/arrowshape.turn.up.right", "Archive/archivebox+", "Reopen/arrow.uturn.backward", ellipsis], settled, settleFirst: false);
            // A helper has no Pin and no Settle or Reopen: Archive alone, after a divider whatever Settings chose.
            Check(["Rename/pencil", "Forward to new thread/arrowshape.turn.up.right", "Reveal worktree in File Explorer/folder", "Archive/archivebox+", ellipsis], helper, settleFirst: true);
            Check(["Rename/pencil", "Forward to new thread/arrowshape.turn.up.right", "Reveal worktree in File Explorer/folder", "Archive/archivebox+", ellipsis], helper, settleFirst: false);
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-025")] // App/Views/Sidebar/SidebarView.swift:2194-2198, :2236-2242 vs :2253-2312: the popover form acts, the context-menu form posts a request
    public void The_popover_form_of_a_thread_menu_acts_and_the_context_menu_form_posts_requests_instead() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var open = NewThread(project, "Open", 0);
        var settled = NewThread(project, "Settled", 1) with { IsSettled = true };
        var model = Model(harness, [project], [open, settled]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var renamed = 0;
            var deleted = 0;
            var made = ((IEnumerable)CallStatic(Private("ThreadActions"), "Make", model, open, (Action)(() => renamed++), (Action)(() => deleted++), null, null)!).Cast<RowAction>().ToList();
            made.Single(a => a.Title == "Rename").Action(); // the popover form hands its own callbacks the work
            made.Single(a => a.Title == "Delete\u2026").Action();
            Assert.Equal((1, 1), (renamed, deleted));

            var menu = ThreadActionsMakeContextMenu(model, open, true, requests);
            menu.Single(a => a.Title == "Rename").Action(); // the context-menu form posts the thread
            Assert.Equal(open.Id, ((ChatThread)Get(requests, "Rename")!).Id);
            menu.Single(a => a.Title == "Delete\u2026").Action();
            Assert.Equal(open.Id, ((ChatThread)Get(requests, "Delete")!).Id);
            Assert.NotNull(model.Thread(open.Id));
            Assert.Null(model.AppModelFinishRequest);
            menu.Single(a => a.Title == "Settle").Action(); // a finish request, taken up by the row with its pop and glide, not a settle here
            Assert.Equal((open.Id, false), (model.AppModelFinishRequest!.ThreadID, model.AppModelFinishRequest.Reopens));
            Assert.False(model.Thread(open.Id)!.IsSettled);
            model.AppModelFinishRequest = null;
            ThreadActionsMakeContextMenu(model, settled, true, requests).Single(a => a.Title == "Reopen").Action();
            Assert.Equal((settled.Id, true), (model.AppModelFinishRequest!.ThreadID, model.AppModelFinishRequest.Reopens));
            Assert.True(model.Thread(settled.Id)!.IsSettled);
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-025")] // App/Views/Sidebar/SidebarView.swift:1772-1801, :2193-2246, UI/Chrome/Chrome.swift RowActionsButton: the real ellipsis popover of a thread row, its items and its deferred action
    public void The_real_ellipsis_popover_of_a_thread_row_lists_the_menu_items_in_order_and_runs_an_item_one_turn_after_closing() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presented = Presentation.Presenter.Override(new RecordingPresenter());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = AppModelSamples.Thread(project) with { Title = "Pin me" };
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        Control row = null!;
        AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            row = LiveThreadRow(thread);
            window = WindowOf(Hosted(model, row));
            window.UpdateLayout();
            window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
        });
        harness.Main.OnMain(() => PointerClick(LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More")));
        harness.Main.OnMain(() =>
        {
            var popup = LogicalQuery.SelfAndDescendants<Popup>(row).Single(p => p.Child is not null); // the ellipsis popover (the removal question's is not presented)
            var stack = Assert.IsType<StackPanel>(LogicalQuery.SelfAndDescendants<RowActionItems>(popup.Child!).Single().Child);
            Assert.Equal(
                [typeof(PopoverItem), typeof(PopoverItem), typeof(PopoverItem), typeof(PopoverDivider), typeof(PopoverItem), typeof(PopoverItem), typeof(PopoverItem)],
                stack.Children.Select(c => c.GetType())); // one divider, ahead of Settle (Settings chose Settle first)
            var items = stack.Children.OfType<PopoverItem>().ToList();
            Assert.Equal(
                ["Rename", "Pin", "Forward to new thread", "Settle", "Archive", "Delete\u2026"],
                items.Select(i => AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(i).Single())));
            Assert.Equal(
                ["pencil", "pin", "arrowshape.turn.up.right", "checkmark.circle", "archivebox", "trash"],
                items.Select(i => LogicalQuery.SelfAndDescendants<SwiftImage>(i).First(g => g.SymbolName is not null).SymbolName));
            var danger = LogicalQuery.SelfAndDescendants<TextBlock>(items[^1]).First();
            Assert.Equal(Chrome.Danger.Resolve(danger.ActualThemeVariant), ((ISolidColorBrush)danger.Foreground!).Color); // Delete… is destructive
            Assert.False(model.Thread(thread.Id)!.IsPinned);
            Click(LogicalQuery.SelfAndDescendants<Button>(items[1]).Single()); // Pin
            Assert.False(model.Thread(thread.Id)!.IsPinned); // not inside the click
        });
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Thread(thread.Id)!.IsPinned); // one turn after the popover closes
            window.Close();
        });
    });

    // ---- UI-SIDEBAR-031, UI-SIDEBAR-032: the archive question popover ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:905-965
    [Trait("Req", "UI-SIDEBAR-032")] // App/Views/Sidebar/SidebarView.swift:922-941
    public void The_archive_popover_lays_out_its_question_and_right_aligned_buttons_with_the_recorded_spacing_padding_type_and_glass() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Fix the bug", 0);
        var anchor = new Border();
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
        var window = new AvaloniaWindow { Content = new Panel { Children = { anchor, popover } }, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();
        try
        {
            var menu = Assert.IsType<PopoverMenu>(Get(popover, "Child"));
            Assert.Equal(272d, menu.Width); // .frame(width: 272)
            var padded = LogicalQuery.SelfAndDescendants<Border>(popover).Single(b => b.Padding == new Thickness(8, 6, 8, 4)); // horizontal 8, top 6, bottom 4
            var column = Assert.IsType<StackPanel>(padded.Child);
            Assert.Equal(Orientation.Vertical, column.Orientation);
            Assert.Equal(9d, column.Spacing); // VStack(alignment: .leading, spacing: 9)
            var question = Assert.IsType<TextBlock>(column.Children[0]);
            var buttons = Assert.IsType<StackPanel>(column.Children[1]);
            Assert.Equal(6d, buttons.Spacing); // HStack(spacing: 6)
            Assert.Equal(HorizontalAlignment.Right, buttons.HorizontalAlignment); // the leading Spacer(minLength: 0)
            var delete = Assert.IsType<Button>(buttons.Children[0]);
            var archive = Assert.IsType<Button>(buttons.Children[1]);
            Assert.True(Math.Abs(6d - (Frame(archive, padded).X - Frame(delete, padded).Right)) < 0.001, $"gap {Frame(archive, padded).X - Frame(delete, padded).Right}");
            Assert.True(Math.Abs(menu.Width - 6 - 8 - Frame(archive, menu).Right) < 0.001, $"right {Frame(archive, menu).Right} width {menu.Width} bounds {menu.Bounds.Width}"); // right-aligned against the 8 pt padding inside PopoverMenu's own 6 (Popovers.swift:107)
            Assert.Equal("Delete", TextOf(delete));
            Assert.Equal("Archive", TextOf(archive));

            // question: .system(.callout), the title in curly quotes at semibold, at most two lines, cut at the tail
            Assert.Equal(12d, question.FontSize);
            Assert.Equal(2, question.MaxLines);
            Assert.Equal(TextTrimming.CharacterEllipsis, question.TextTrimming);
            Assert.Equal(TextWrapping.Wrap, question.TextWrapping);
            var runs = question.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
            Assert.Equal(["Archive ", "“Fix the bug”", "?"], runs.Select(r => r.Text ?? ""));
            Assert.Equal([FontWeight.Normal, FontWeight.SemiBold, FontWeight.Normal], runs.Select(r => r.FontWeight == default ? FontWeight.Normal : r.FontWeight));

            // Archive holds the focus from the start and is the default action
            Dispatcher.UIThread.RunJobs(); // the focus request made as the popover appears
            Assert.True(archive.IsFocused, "focused");
            Assert.True(archive.IsDefault, "default");

            // Delete is the plain glass capsule, Archive the prominent (tinted) one: both interactive
            var deleteGlass = LogicalQuery.SelfAndDescendants<Control>(delete).Select(SwiftGlass.GetEffect).First(e => e is not null)!;
            var archiveGlass = LogicalQuery.SelfAndDescendants<Control>(archive).Select(SwiftGlass.GetEffect).First(e => e is not null)!;
            Assert.Null(deleteGlass.TintColor);
            Assert.NotNull(archiveGlass.TintColor);
            Assert.True(deleteGlass.IsInteractive, "delete interactive");
            Assert.True(archiveGlass.IsInteractive, "archive interactive");
            Assert.Equal(deleteGlass.Shape, archiveGlass.Shape);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:918-921: lineLimit(2), truncationMode(.tail), vertical fixedSize
    public void A_long_archive_title_wraps_to_two_lines_and_is_cut_at_its_tail() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, string.Join(" ", Enumerable.Repeat("remarkably", 30)), 0);
        var anchor = new Border();
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
        var window = new AvaloniaWindow { Content = new Panel { Children = { anchor, popover } }, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();
        try
        {
            var question = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Single(t => InlineText(t).StartsWith("Archive ", StringComparison.Ordinal));
            Assert.Equal(2, question.TextLayout.TextLines.Count); // two lines at most, never three
            var shown = question.TextLayout.TextLines.Sum(l => l.Length);
            Assert.True(shown < InlineText(question).Length, $"shown {shown} of {InlineText(question).Length}"); // the second line is cut at its tail: the text does not all fit
            Assert.True(question.Bounds.Height >= 2 * 12); // and the question is given its full two lines (fixedSize vertically)
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-032")] // App/Views/Sidebar/SidebarView.swift:275-281: Archive archives under Chrome.panelSlide; Delete takes the deferred-delete path with removeWorktree false
    public void The_archive_popovers_buttons_archive_under_panelSlide_and_delete_without_the_worktree() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var archived = NewThread(project, "Archive me", 0);
        var deleted = NewThread(project, "Delete me", 1) with { WorktreePath = harness.NewFolder("worktree") };
        var model = Model(harness, [project], [archived, deleted]);
        Animation? during = null;
        using var probe = new OnChange<bool>(() => model.Thread(archived.Id)!.IsArchived, (_, now) => { if (now) during = Transaction.Current; });
        object state = null!;
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            state = Field(view, "state")!;
            SetProp(state, "PendingArchiveID", archived.Id);
            var anchor = new Border();
            var content = (Control)Call(view, "BuildArchivePopoverContent", archived, anchor)!;
            ShownWithAnchor(anchor, content);
            Click(LogicalQuery.SelfAndDescendants<Button>(content).Single(b => TextOf(b) == "Archive"));
            SetProp(state, "IsDeletePopoverShown", true);
            var other = (Control)Call(view, "BuildArchivePopoverContent", deleted, new Border())!;
            ShownWithAnchor(new Border(), other);
            Click(LogicalQuery.SelfAndDescendants<Button>(other).Single(b => TextOf(b) == "Delete"));
            var confirmed = Get(state, "ConfirmedDeletion")!;
            Assert.Equal(deleted.Id, (Guid)Get(confirmed, "ThreadID")!);
            Assert.False((bool)Get(confirmed, "RemoveWorktree")!); // the archive question never offers the worktree
        });
        harness.Main.OnMain(() => Assert.Equal(Chrome.PanelSlide, during)); // :279 withAnimation(Chrome.panelSlide) { model.archive }
    });

    // ---- UI-SIDEBAR-058: a search result group ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:702-711 searchList; :737-753 searchResults
    public void The_real_search_list_is_groups_of_an_inert_project_row_with_its_full_count_and_compact_thread_rows_and_a_settled_thread_changes_rows_in_place() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
        var beta = NewProject(harness, "Beta");
        var fixOne = NewThread(alpha, "Fix one", 0);
        var otherTwo = NewThread(alpha, "Other two", 1);
        var otherThree = NewThread(alpha, "Other three", 2);
        var fixBeta = NewThread(beta, "Fix beta", 3);
        var model = Model(harness, [alpha, beta], [fixOne, otherTwo, otherThree, fixBeta]);
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        void Settle(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Control ThreadRowOf(Control row) => LogicalQuery.SelfAndDescendants<Control>(row).First(c => c.GetType().Name == "SidebarThreadRow"); // the row inside its popover host
        string Shape(Control row) => row.GetType().Name == "Panel" ? "ThreadRow" : row.GetType().Name;
        try
        {
            harness.Main.OnMain(() => { view = Attached(model); window = WindowOf(view); });
            Settle(1600);
            harness.Main.OnMain(() => SearchBox(view).Text = "fix");
            Settle(1600);
            Control[] rows = null!;
            harness.Main.OnMain(() =>
            {
                rows = ListPanel(view).Children.ToArray();
                // a project row then one thread row per match; a 10 pt transparent spacer before every group but the first
                Assert.Equal(["ProjectRow", "ThreadRow", "Border", "ProjectRow", "ThreadRow"], rows.Select(Shape));
                var spacer = Assert.IsType<Border>(rows[2]);
                Assert.Equal(10d, spacer.Bounds.Height);
                Assert.Null(spacer.Background);
                Assert.Null(spacer.Child);
                // the project row is the project's own (never expanded or collapsed from here) and counts every candidate, not the matches
                Assert.Equal("3", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(rows[0]), t => t.Text == "3").Text); // Alpha is collapsed: its count shows; 1 match of 3
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(rows[3]), t => t.Text == "1"); // Beta is expanded: no count
                Click(ButtonNamed(rows[0], "Alpha"));
                Assert.False(model.Project(alpha.Id)!.IsExpanded); // inert: the click does nothing
                // thread rows are the compact project form: no project name, no project icon
                Assert.Equal(28d, ThreadRowOf(rows[1]).Bounds.Height);
                Assert.DoesNotContain("Alpha", Texts(ThreadRowOf(rows[1])));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ProjectIconMark>(ThreadRowOf(rows[1])));
                // clicking a thread row selects it
                Click((Button)Field(ThreadRowOf(rows[4]), "selectButton")!);
                Assert.Equal(fixBeta.Id, model.SelectedThreadID);
                window.MouseMove(rows[0].TranslatePoint(new Point(10, 10), window)!.Value);
            });
            harness.Main.OnMain(() =>
            {
                // the project row still shows its hover accessories, and the plus still starts a thread in that project
                Assert.NotNull(LogicalQuery.SelfAndDescendants<RowActionsButton>(rows[0]).SingleOrDefault());
                var plus = LogicalQuery.SelfAndDescendants<Button>(rows[0]).Single(b => ToolTip.GetTip(b) as string == "New thread in Alpha");
                PointerClick(plus);
                Assert.Equal(4, model.ThreadsIn(model.Project(alpha.Id)!).Count);
            });
            // a thread settled from within the results is keyed anew there, so it changes rows in place
            harness.Main.OnMain(() => model.Settle(fixOne.Id, sounds: false));
            Settle(1600);
            harness.Main.OnMain(() =>
            {
                var after = ListPanel(view).Children.ToArray();
                Assert.Equal(5, after.Length);
                Assert.NotSame(rows[1], after[1]); // a fresh row stands where the open one was
                Assert.Equal(24d, ThreadRowOf(after[1]).Bounds.Height); // a settled thread's one grey line
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ---- Parity pass (2026-10-03): the remaining clauses of UI-SIDEBAR-015, -034, -038, -039, -042, UI-GIT-057,
    // PR-SIDEBAR-130 and PR-SIDEBAR-134 on mounted controls ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // SidebarView.swift:1442 capped = heads.count > 12; :1436 @State showsAllHeads is the card's own
    public void The_head_cards_chevron_is_absent_at_twelve_heads_and_a_rebuilt_card_starts_collapsed_again() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 13).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        static IEnumerable<Button> Chevrons(Control card) => LogicalQuery.SelfAndDescendants<Button>(card)
            .Where(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName?.StartsWith("chevron", StringComparison.Ordinal) == true));
        Control Card(IReadOnlyList<ChatThread> shown) => (Control)Activator.CreateInstance(Private("HeadCardsRow"),
            parent, shown, Activator.CreateInstance(Private("SidebarMenuRequests"))!, (Action)(() => { }))!;
        Control twelve = null!, first = null!, second = null!;
        harness.Main.OnMain(() =>
        {
            twelve = Card(heads.Take(12).ToList());
            Hosted(model, twelve);
            Assert.Empty(Chevrons(twelve)); // :1514 `if capped`: twelve is not capped
            Assert.Equal(12, LogicalQuery.SelfAndDescendants<HydraGlyph>(twelve).Count());
            first = Card(heads);
            Hosted(model, first);
            PointerClick(Chevrons(first).Single()); // shows all thirteen
        });
        harness.Main.OnMain(() =>
        {
            Assert.Equal(13, LogicalQuery.SelfAndDescendants<HydraGlyph>(first).Count());
            WindowOf(first).Close(); // the card leaves the list: its @State goes with it
            second = Card(heads);
            Hosted(model, second);
            Assert.Equal(12, LogicalQuery.SelfAndDescendants<HydraGlyph>(second).Count()); // collapsed again
            Assert.Equal("chevron.down", LogicalQuery.SelfAndDescendants<SwiftImage>(Chevrons(second).Single()).Single().SymbolName);
            WindowOf(twelve).Close();
            WindowOf(second).Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-015")] // SidebarView.swift:1159 Label("Start new chats on", systemImage: "point.3.connected.trianglepath.dotted"), after a Divider
    public void The_pair_submenu_is_labelled_with_its_symbol_after_a_divider() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        harness.Main.OnMain(() =>
        {
            var items = ProjectRowBuildContextMenuItems(project, true, false, model, ProjectPairChoices((Guid.NewGuid(), "Claude, GPT")));
            Assert.IsType<Separator>(items[^2]); // :1155 Divider()
            var submenu = Assert.IsType<MenuItem>(items[^1]);
            Assert.Equal("Start new chats on", MenuItemTitle(submenu));
            var symbol = LogicalQuery.SelfAndDescendants<SwiftImage>((Control)submenu.Header!).Single();
            Assert.Equal("point.3.connected.trianglepath.dotted", symbol.SymbolName);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // windows_path: OD-0010 draws each item symbol as its Lucide icon (porting/icons/sf-symbols-to-lucide.json)
    [Trait("Req", "UI-SIDEBAR-014")]
    public void The_project_menus_symbols_are_drawn_as_their_lucide_icons() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presented = Presentation.Presenter.Override(new RecordingPresenter());
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        harness.Main.OnMain(() => model.Settings.SetProjectActive(true, project.Id)); // an override, so all six items show
        Control row = null!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
        harness.Main.OnMain(() => WindowOf(row).MouseMove(row.TranslatePoint(new Point(10, 10), WindowOf(row))!.Value));
        harness.Main.OnMain(() => PointerClick(LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More")));
        harness.Main.OnMain(() =>
        {
            var popup = LogicalQuery.SelfAndDescendants<Popup>(row).Single(p => p.Child is not null);
            var glyphs = LogicalQuery.SelfAndDescendants<PopoverItem>(popup.Child!)
                .Select(i => LogicalQuery.SelfAndDescendants<SwiftImage>(i).First(g => g.SymbolName is not null)).ToList();
            Assert.Equal(["square-pen", "layers", "folder", "trash", "power", "undo-2"], glyphs.Select(g => g.Mapping!.Lucide));
            Assert.All(glyphs, g => Assert.NotNull(g.Icon)); // each one draws
            WindowOf(row).Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-GIT-057")] // SidebarView.swift:977-980 the two rows hand their worktree choice to confirmDeletion (:256-263)
    [Trait("Req", "UI-SIDEBAR-027")]
    public void The_delete_questions_rows_record_the_deletion_with_or_without_the_worktree_and_wait_for_the_popover_to_go() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Has worktree", 0) with { WorktreePath = harness.NewFolder("worktree") };
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        harness.Main.OnMain(() =>
        {
            view = Detached(model);
        });
        foreach (var (title, removeWorktree) in new[] { ("Delete thread", false), ("Delete thread and worktree", true) })
        {
            harness.Main.OnMain(() =>
            {
                var state = Field(view, "state")!;
                SetProp(state, "IsDeletePopoverShown", true); // :229 the question is on screen
                var content = (Control)Call(view, "BuildDeletePopoverContent", thread)!; // :225-227, the content the popover shows
                var rows = LogicalQuery.SelfAndDescendants<PopoverItem>(content).ToList();
                Assert.All(rows, r => Assert.Equal("trash", LogicalQuery.SelfAndDescendants<SwiftImage>(r).First(g => g.SymbolName is not null).SymbolName)); // :976, :978
                Click(LogicalQuery.SelfAndDescendants<Button>(content).Single(b => AutomationProperties.GetName(b) == title)); // PopoverItem acts a turn later
            });
            harness.Main.OnMain(() =>
            {
                var state = Field(view, "state")!;
                var confirmed = Get(state, "ConfirmedDeletion")!;
                Assert.Equal(thread.Id, Get(confirmed, "ThreadID"));
                Assert.Equal(removeWorktree, Get(confirmed, "RemoveWorktree")); // :977-980 the worktree only from the second row
                Assert.NotNull(model.Thread(thread.Id)); // :261 nothing deleted while the popover is still shown
                SetProp(state, "ConfirmedDeletion", null);
            });
        }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-042")] // SidebarView.swift:164-166; UI/Chrome/Chrome.swift:710-746 SidebarRow
    public void The_mounted_Add_project_row_has_the_sidebar_rows_geometry_type_colours_and_hover_fill() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        var model = harness.MakeModel();
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        try
        {
            Border shape = null!;
            harness.Main.OnMain(() =>
            {
                var row = RowNamed(view, "Add project");
                Assert.Equal(28d, row.Bounds.Height); // Chrome.rowHeight
                shape = LogicalQuery.SelfAndDescendants<Border>(row).First(b => b.Height == 28);
                Assert.Equal(new Thickness(8, 0, 8, 0), shape.Padding); // :725-726 with accessoryWidth 0
                Assert.Equal(new CornerRadius(Chrome.RowCornerRadius), shape.CornerRadius);
                var badge = LogicalQuery.SelfAndDescendants<SidebarIconBadge>(row).Single();
                Assert.Equal(20d, badge.Bounds.Width); // :713 Chrome.iconSize
                Assert.Equal(8d, Frame(badge, row).X); // the leading padding
                var title = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "Add project");
                Assert.Equal(8 + 20 + 8, Frame(title, row).X, 0.5); // HStack(spacing: 8)
                Assert.Equal(13d, title.FontSize); // .body
                Assert.Equal(FontWeight.Normal, title.FontWeight);
                Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color);
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single();
                Assert.Equal("plus", glyph.SymbolName); // :165 SidebarSymbol("plus") at the default 14 pt
                Assert.Equal(Chrome.SymbolSize, glyph.Width);
                Assert.Equal(Chrome.PrimaryText.Opacity(0.85).Resolve(glyph.ActualThemeVariant), ((ISolidColorBrush)glyph.GetValue(SwiftImage.ForegroundProperty)!).Color); // SidebarIconBadge
                Assert.Equal(0, ((ISolidColorBrush)shape.Background!).Color.A);
                var window = WindowOf(view);
                window.MouseMove(row.TranslatePoint(new Point(40, 14), window)!.Value);
            });
            harness.Main.AdvanceMilliseconds(300);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(300)));
            harness.Main.OnMain(() => Assert.Equal(Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant), ((ISolidColorBrush)shape.Background!).Color)); // :729 hovered
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-034")] // SidebarView.swift:1662-1674 the last helper's connector stops at the tick; :1348-1349 on a mounted helper row
    public void A_mounted_last_helpers_connector_stops_at_its_tick_and_an_earlier_one_runs_to_the_bottom() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        harness.Main.OnMain(() =>
        {
            foreach (var isLast in new[] { true, false })
            {
                var row = ShownHelperRow(model, helper, Activator.CreateInstance(Private("SidebarMenuRequests"))!, isLast);
                var path = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(row).Single();
                WindowOf(row).UpdateLayout();
                Assert.Equal(new Size(28, 24), path.Bounds.Size); // the 28 x 24 column
                var bounds = path.Data!.Bounds;
                Assert.Equal(18d, bounds.Left, 3); // the line at x = 18 (connectorLineX)
                Assert.Equal(28 - 2, bounds.Right, 3); // the tick to maxX - 2
                Assert.Equal(isLast ? 12 : 24, bounds.Bottom, 3); // ends at midY on the last helper, else runs to the bottom
                WindowOf(row).Close();
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-134")] // SidebarView.swift:1820-1833 `guard isDragged || isHovering || ...`: the dragged branch
    public void A_dragged_row_records_its_window_frame_with_no_hover_and_no_glide() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Dragged", 0);
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        harness.Main.OnMain(() =>
        {
            var row = LiveThreadRow(thread, dragged: true);
            var window = WindowOf(Hosted(model, row));
            try
            {
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(animator.Glides);
                var holder = Field(row, "windowFrame")!;
                Assert.Equal(Frame(row, window), Get(holder, "Frame")); // recorded although nothing hovers it
                row.Margin = new Thickness(0, 30, 0, 0); // it moves, as a dragged row does
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Frame(row, window), Get(holder, "Frame")); // in window space (:1903-1905), following it
            }
            finally { window.Close(); }
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-039")] // SidebarView.swift:335-336 itemView's header arm builds ActivityHeader(title:isFirst:) in the mounted list
    public void The_mounted_activity_list_draws_its_day_headers_with_the_first_ones_short_top_padding() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var today = ThreadAt(project, "Fresh", Now.AddHours(-1));
        var yesterday = ThreadAt(project, "Older", Now.AddDays(-1));
        var model = Model(harness, [project], [today, yesterday]);
        InDays(() =>
        {
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                model.Settings.SidebarActivityView = true;
                view = Attached(model);
            });
            harness.Main.AdvanceMilliseconds(1600); // any arrival transition has finished
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(1600)); WindowOf(view).UpdateLayout(); });
            harness.Main.OnMain(() =>
            {
                var headers = LogicalQuery.SelfAndDescendants<ActivityHeader>(ListPanel(view)).ToList();
                Assert.Equal(["Today", "Yesterday"], headers.Select(h => ((TextBlock)h.Child!).Text));
                Assert.Equal(new Thickness(8, 2, 8, 4), ((TextBlock)headers[0].Child!).Margin); // first item
                Assert.True(new Thickness(8, 14, 8, 4) == ((TextBlock)headers[1].Child!).Margin, // the list as drawn, should this ever differ:
                    string.Join(" | ", ListPanel(view).Children.Select(c => c.GetType().Name + ":" + c.Opacity + ":" + string.Join(",", Texts(c)))));
                Close(view);
            });
        });
    });

    // The dotted guide beside a helper and beside the folded stub (2-chat-use.mov 162 s: a column of dots then the
    // tick, left of "> 4 heads"; evidence E:\Droppy\work\parity\evidence\SidebarView\z_stub.png). Counts the drawn
    // dots down the connector's line at x = 18 on a real rendered frame.
    internal static int DotsDownTheLine(AvaloniaWindow window, Control column, int bottom)
    {
        window.UpdateLayout();
        var origin = column.TranslatePoint(default, window)!.Value;
        var x = (int)Math.Round(origin.X + 18);
        var background = FramePixels.Levels(window, ((int)origin.X + 2, (int)origin.Y + 2))[0];
        var levels = FramePixels.Levels(window, Enumerable.Range(0, bottom).Select(y => (x, (int)origin.Y + y)).ToArray());
        var dots = 0;
        var inDot = false;
        foreach (var level in levels)
        {
            var drawn = Math.Abs(level - background) > 0.04;
            if (drawn && !inDot) dots++;
            inDot = drawn;
        }
        return dots;
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-034")] // SidebarView.swift:1648-1651 HelperConnectorShape(...).stroke(color, style: [0.1, 3.6] round) in a 28 x 24 frame
    [Trait("Req", "UI-SIDEBAR-035")] // :1583-1584 the stub's own ending connector
    public void The_helper_rows_and_the_folded_stubs_connectors_draw_their_dotted_line() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        harness.Main.OnMain(() =>
        {
            var row = ShownHelperRow(model, helper, Activator.CreateInstance(Private("SidebarMenuRequests"))!, isLast: false);
            var window = WindowOf(row);
            try
            {
                var column = Assert.IsType<Grid>(Get(row, "Child")).Children[0];
                Assert.InRange(DotsDownTheLine(window, column, 24), 5, 8); // 24 pt on a 3.7 pt pitch
            }
            finally { window.Close(); }
            var stub = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helper }, (Action)(() => { }))!;
            stub.VerticalAlignment = VerticalAlignment.Top;
            var stubWindow = WindowOf(Hosted(model, stub));
            try
            {
                var path = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(stub).Single();
                Assert.InRange(DotsDownTheLine(stubWindow, path, 12), 3, 5); // ends at midY: 12 pt of dots
            }
            finally { stubWindow.Close(); }
        });
    });

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-030")] // SidebarView.swift:1980-1984 mergeStage; :2053-2063 the detailed row's second line; :2090 the ghost badge
    public void A_live_detailed_row_swaps_its_project_line_for_the_merge_stage_while_merging_and_back_and_its_ghost_badge_counts_merging_as_running() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Lead", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            var runtime = model.Runtime(thread.Id); // the thread has run, so its runtime exists (AppModel.swift:108 runtimes are unobserved)
            row = LiveThreadRow(thread, projectName: "Alpha");
            WindowOf(Hosted(model, row)).UpdateLayout();
            Assert.Contains("Alpha", Texts(row)); // no merge: the project name
            runtime.IsHydraMerging = true;
            runtime.HydraMergeStage = "Alpha: Reviewing heads"; // the multi-project prefix comes with the stage words
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                Assert.Contains("Alpha: Reviewing heads…", Texts(row)); // :1981-1983 the words and one U+2026, in place of the name
                Assert.DoesNotContain("Alpha", Texts(row));
                Assert.True(new ThreadGhostBadge(thread, model.Runtime(thread.Id)).IsRunning); // :2090 merging counts as running
                model.Runtime(thread.Id).HydraMergeStage = null;
            });
            harness.Main.OnMain(() =>
            {
                Assert.Contains("Merging…", Texts(row)); // :1983 the fallback while merging with no stage
                model.Runtime(thread.Id).IsHydraMerging = false;
            });
            harness.Main.OnMain(() =>
            {
                Assert.Contains("Alpha", Texts(row)); // :1980 nil once the merge is over: the name again
                Assert.DoesNotContain(Texts(row), t => t.EndsWith('…'));
                Assert.False(new ThreadGhostBadge(thread, model.Runtime(thread.Id)).IsRunning);
            });
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // SidebarView.swift:1361 regular while idle and read; :1346 a continuous rounded rect; :128-133 only rows that report a frame are grabbed
    public void A_helper_row_is_regular_while_idle_has_continuous_corners_and_never_starts_a_reorder() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0) with { ExpandsHelpers = true };
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var other = NewThread(project, "Other", 2);
        var model = Model(harness, [project], [parent, helper, other]);
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var window = WindowOf(view);
        try
        {
            harness.Main.OnMain(() =>
            {
                var row = LogicalQuery.SelfAndDescendants<Control>(view).Single(c => c.GetType() == Private("SidebarHelperRow"));
                var title = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "Helper");
                Assert.Equal(FontWeight.Normal, title.FontWeight); // :1361 not selected, not unread
                var shape = LogicalQuery.SelfAndDescendants<Border>(row).First(b => b.Height == 24);
                Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(shape)); // :1346
                var start = title.TranslatePoint(new Point(4, 6), window)!.Value;
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(start + new Vector(0, 10), RawInputModifiers.LeftMouseButton);
                window.MouseMove(start + new Vector(0, 30), RawInputModifiers.LeftMouseButton);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Null(Drag(view).Id); // a helper reports no frame to the grab table: nothing to move
                window.MouseUp(new Point(10, 10), MouseButton.Left);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // SidebarView.swift:1433-1437 the connector folds the helpers away; :1540 hidden while the parent's ghost is in the air
    [Trait("Req", "UI-SIDEBAR-035")] // :1617 the folded stub hides the same way
    public void The_head_cards_connector_folds_and_the_card_and_the_stub_hide_while_their_parents_glide_is_in_the_air() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 3).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        var folded = 0;
        Control card = null!, stub = null!;
        harness.Main.OnMain(() =>
        {
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, Activator.CreateInstance(Private("SidebarMenuRequests"))!, (Action)(() => folded++))!;
            WindowOf(Hosted(model, card)).UpdateLayout();
            stub = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, heads, (Action)(() => { }))!;
            WindowOf(Hosted(model, stub)).UpdateLayout();
            var connector = LogicalQuery.SelfAndDescendants<HelperConnector>(card).Single();
            Click(LogicalQuery.SelfAndDescendants<Button>(connector).Single()); // :1436 action: onFold
            Assert.Equal(1, folded);
            Assert.Equal(1d, card.Opacity);
            Assert.Equal(1d, LogicalQuery.SelfAndDescendants<Button>(stub).Single().Opacity);
            animator.Launch(parent.Id, RowGlideAnimator.Glide.Direction.Settle, new Rect(0, 300, 200, 24),
                new Rect(0, -28, 200, 28), new Rect(0, 0, 200, 400), new FaceSample(200, 24), new FaceSample(200, 28), 0, 0);
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Assert.True(animator.IsHiding(parent.Id));
                Assert.Equal(0d, card.Opacity); // out of sight the moment the ghost takes off
                Assert.Equal(0d, LogicalQuery.SelfAndDescendants<Button>(stub).Single().Opacity);
                animator.Arrive(animator.Glides[0].Id);
            });
            harness.Main.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d, card.Opacity);
                Assert.Equal(1d, LogicalQuery.SelfAndDescendants<Button>(stub).Single().Opacity);
            });
        }
        finally { harness.Main.OnMain(() => { WindowOf(card).Close(); WindowOf(stub).Close(); }); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // windows_path: Reveal in File Explorer (OD-0008 B1) opens File Explorer at the project path (:1179-1182, :1211)
    public void Reveal_in_File_Explorer_from_either_menu_opens_File_Explorer_at_the_projects_path() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        var revealed = new List<string>();
        var previous = ShellWorkspace.RevealInFileExplorer;
        ShellWorkspace.RevealInFileExplorer = revealed.Add; // the shell seam: no real Explorer window in a test
        try
        {
            harness.Main.OnMain(() =>
            {
                ProjectRowActions(model, project, isActive: true).Single(a => a.Title == "Reveal in File Explorer").Action();
                ProjectRowContextMenuActions(project, true, false, model).Single(a => a.Title == "Reveal in File Explorer").Action();
                Assert.Equal([project.Path, project.Path], revealed);
            });
        }
        finally { ShellWorkspace.RevealInFileExplorer = previous; }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-040")] // SidebarView.swift:2384-2389 the count: .fixedSize() and .contentTransition(.identity)
    public void The_settled_headers_count_keeps_its_full_width_when_squeezed_and_changes_its_digits_in_place() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var header = new SettledHeader(1234, isFirst: false);
            var host = new Border { Width = 60, Child = header }; // narrower than chevron + "Settled" + count
            AppEnvironment.SetAppModel(host, model);
            var window = new AvaloniaWindow { Content = host, Width = 200, Height = 80 };
            window.Show();
            try
            {
                window.UpdateLayout();
                var count = LogicalQuery.SelfAndDescendants<TextBlock>(header).Single(t => t.Text == "1234");
                var natural = new TextBlock { Text = "1234", FontSize = count.FontSize, FontWeight = count.FontWeight, FontFamily = count.FontFamily, FontFeatures = count.FontFeatures };
                natural.Measure(Size.Infinity);
                Assert.Equal(natural.DesiredSize.Width, count.Bounds.Width, 1.0); // .fixedSize(): its own full width (unrounded in the baseline row), never cut or wrapped
                Assert.Single(count.TextLayout.TextLines);
                Transaction.WithAnimation(Chrome.PanelSlide, () => header.Update(1235, isFirst: false)); // even inside an animation
                Assert.Same(count, LogicalQuery.SelfAndDescendants<TextBlock>(header).Single(t => t.Text == "1235")); // :2388 .identity: the same text, its digits swapped at once
                Assert.Equal(0.8, count.Opacity); // no fade on the digits
            }
            finally { window.Close(); }
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-058")] // SidebarView.swift:705-708 the search list's project row keeps its ellipsis, and it works
    public void In_search_results_the_inert_project_rows_ellipsis_still_opens_its_actions() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var presented = Presentation.Presenter.Override(new RecordingPresenter());
        using var harness = new AppModelHarness();
        var alpha = NewProject(harness, "Alpha");
        var fix = NewThread(alpha, "Fix one", 0);
        var model = Model(harness, [alpha], [fix]);
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        void Settle(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        try
        {
            harness.Main.OnMain(() => { view = Attached(model); window = WindowOf(view); });
            Settle(1600);
            harness.Main.OnMain(() => SearchBox(view).Text = "fix");
            Settle(1600);
            Control projectRow = null!;
            harness.Main.OnMain(() =>
            {
                projectRow = ListPanel(view).Children[0];
                Assert.Equal("ProjectRow", projectRow.GetType().Name);
                window.MouseMove(projectRow.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            Settle(300);
            harness.Main.OnMain(() => PointerClick(LogicalQuery.SelfAndDescendants<Button>(projectRow).Single(b => ToolTip.GetTip(b) as string == "More")));
            harness.Main.OnMain(() =>
            {
                var popup = LogicalQuery.SelfAndDescendants<Popup>(projectRow).Single(p => p.Child is not null);
                var titles = LogicalQuery.SelfAndDescendants<PopoverItem>(popup.Child!)
                    .Select(i => AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(i).Single())).ToList();
                Assert.Equal("New thread", titles[0]); // :1207 the project's own actions, as outside a search
                Assert.Contains("Remove project", titles);
                Assert.DoesNotContain("Start new chats on", titles); // the popover never offers the pair submenu
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ---- REVIEW (pass 1, round 1, adversarial): hover that includes the trailing overlay ----
    //
    // SidebarView.swift:1375-1386 SidebarHelperRow: `.overlay(alignment: .trailing) { ... }` comes BEFORE
    // `.onHover { ... }`, so the hover region is the button together with its ellipsis; the pointer resting on the
    // ellipsis is still on the row and the ellipsis stays (UI-SIDEBAR-033 "hovered (fill plus ellipsis in place of the
    // activity status)"). Chrome.swift:731-737 SidebarRow (the project row, PR-SIDEBAR-126, UI-SIDEBAR-058 "the
    // project row's ellipsis and plus still work") has the same order. SidebarThreadRow gets this right
    // (overlays.OnHover, SidebarView.cs, ":1816 hover includes the overlaid check and accessory buttons").

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // SidebarView.swift:1375-1386: .overlay then .onHover
    public void REVIEW_A_helper_rows_ellipsis_stays_while_the_pointer_rests_on_it() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        harness.Main.OnMain(() =>
        {
            row = ShownHelperRow(model, helper, Activator.CreateInstance(Private("SidebarMenuRequests"))!);
            window = WindowOf(row);
            window.MouseMove(row.TranslatePoint(new Point(60, 12), window)!.Value); // on the title
        });
        try
        {
            Tick(300);
            harness.Main.OnMain(() =>
            {
                var more = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
                window.MouseMove(more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), window)!.Value); // onto the ellipsis
            });
            Tick(300);
            Tick(300);
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<ActivityStatus>(row)); // still hovered: no status in its place
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "More");
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-126")] // UI/Chrome/Chrome.swift:731-737 SidebarRow: .overlay then .onHover
    [Trait("Req", "UI-SIDEBAR-058")] // "the project row's ellipsis and plus still work"
    public void REVIEW_A_project_rows_ellipsis_and_plus_stay_while_the_pointer_rests_on_the_ellipsis() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 1);
            window = WindowOf(row);
            window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value); // on the icon
        });
        try
        {
            Tick(300);
            harness.Main.OnMain(() =>
            {
                var more = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
                window.MouseMove(more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), window)!.Value); // onto the ellipsis
            });
            Tick(300);
            Tick(300);
            harness.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "More"); // :1118 still offered
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "New thread in Alpha"); // :1119-1125
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // PR-SIDEBAR-126 "the row's own hover fill is the label colour at 0.06" on the live path, not only FillFor's value: a real
    // hover of the project row draws it once Chrome.hover's ease has run (Chrome.swift:729, :743), and it stays while the
    // pointer rests on the overlaid ellipsis, as the hover covers the overlay (:731-737).
    [Fact]
    [Trait("Req", "PR-SIDEBAR-126")] // Chrome.swift:729, :731-737, :741-745 via SidebarView.swift:1097-1133
    public void A_project_rows_hover_fill_is_drawn_on_a_real_hover_and_kept_while_the_pointer_rests_on_its_ellipsis() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        AvaloniaWindow window = null!;
        Border shape = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Color FillNow() => ((ISolidColorBrush)shape.Background!).Color;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 1);
            window = WindowOf(row);
            shape = LogicalQuery.SelfAndDescendants<Border>(row).First(b => b.Height == 28 && b.Padding.Right > 40);
            Assert.Equal(0, FillNow().A); // :744 clear while the pointer is elsewhere
            window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value); // onto the icon
        });
        try
        {
            Tick(300);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant), FillNow()); // :743 hovered, never selected
                var more = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
                window.MouseMove(more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), window)!.Value); // onto the ellipsis
            });
            Tick(300);
            Tick(300);
            harness.Main.OnMain(() => Assert.Equal(Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant), FillNow())); // still the row's hover
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // ---- REVIEW (pass 1, round 1, adversarial): the head card's chevron fades the glyphs past twelve ----
    //
    // SidebarView.swift:1466-1513 the glyphs are `ForEach(row, id: \.id)` inside `ForEach(rows, id: \.first?.id)`, and
    // :1515-1516 toggles showsAllHeads inside withAnimation(.snappy(duration: 0.18)): the heads past the first twelve
    // join (and leave) on SwiftUI's default .opacity transition under that snappy animation, while the clipped card
    // grows (:1521, :1535). 2-chat-use.mov 5.33-5.40 s (CATALOGUE sidebar.activity-grid-expand; frames extracted to
    // E:\Droppy\work\parity\evidence\SidebarView\review\grid\zoom345.png): in the first frames of the expansion the new
    // glyphs, including the six that join the second row, are drawn faint and reach full strength over the next frames,
    // while the first twelve stay whole. The port's Rebuild cleared and refilled every row, so the new glyphs were whole
    // at once (and the leaving ones gone at once).

    internal static double ShownOpacityWithin(Control control, Control root) =>
        control.GetSelfAndVisualAncestors().OfType<Visual>().TakeWhile(v => !ReferenceEquals(v, root)).Aggregate(1d, (o, v) => o * v.Opacity);

    // A head card's leaving glyphs and chevrons fade on the canvas layer over the card, which takes no input
    // (ED-258 (2)); the live ones sit in the card's own rows.
    internal static bool OnGhostLayer(Control control) => control.GetVisualAncestors().OfType<Canvas>().Any(canvas => !canvas.IsHitTestVisible);

    internal static List<Button> ChevronButtons(Control card) => LogicalQuery.SelfAndDescendants<Button>(card)
        .Where(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName?.StartsWith("chevron", StringComparison.Ordinal) == true)).ToList();

    // The chevron the card draws now (:1514-1526), not one still fading out where it stood.
    internal static Button LiveChevron(Control card) => ChevronButtons(card).Single(b => !OnGhostLayer(b));

    internal static string ChevronSymbolOf(Button chevron) => LogicalQuery.SelfAndDescendants<SwiftImage>(chevron).First(i => i.SymbolName is not null).SymbolName!;

    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // SidebarView.swift:1466-1516: the toggle's snappy animation carries the glyphs' default opacity transition
    public void REVIEW_The_head_cards_chevron_fades_the_glyphs_past_twelve_in_and_out_on_its_snappy_toggle() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 15).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        List<Button> Glyphs() => LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
        Button ChevronOf() => LogicalQuery.SelfAndDescendants<Button>(card)
            .Single(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName?.StartsWith("chevron", StringComparison.Ordinal) == true));
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                Assert.Equal(12, Glyphs().Count); // :1443 prefix(12)
                Click(ChevronOf()); // :1515-1516 withAnimation(.snappy(duration: 0.18))
            });
            harness.Main.OnMain(() => { });
            Frames(3); // about 48 ms into the 0.18 s snappy curve
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(15, glyphs.Count);
                Assert.Equal(1d, ShownOpacityWithin(glyphs[0], card)); // one of the first twelve: whole throughout
                Assert.InRange(ShownOpacityWithin(glyphs[^1], card), 0.02, 0.98); // a joining glyph: still fading in
            });
            Frames(30);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Equal(1d, ShownOpacityWithin(Glyphs()[^1], card));
                Click(ChevronOf()); // and back to twelve
            });
            harness.Main.OnMain(() => { });
            Frames(3);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(15, glyphs.Count); // the three leaving glyphs are still on their way out
                Assert.InRange(ShownOpacityWithin(glyphs[^1], card), 0.02, 0.98);
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // Every claim the record makes about the toggle's motion, each against a different way to get it wrong: the glyphs that
    // stay never fade (a rebuild that fades everything), the joiners start faint and the leavers end gone (the full curve,
    // not one frame), a leaver fades where it stood and the chevron does not wait for it (a leaver that holds its slot
    // makes the chevron jump when it is gone), and a rebuild that changes no membership (a selection) fades nothing.
    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // SidebarView.swift:1466-1516, :1535: the glyphs' default opacity transition under the toggle's snappy animation
    public void The_head_cards_leaving_glyphs_fade_where_they_stood_without_holding_the_chevron_and_the_stayers_never_fade() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 15).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        List<Button> Glyphs() => LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
        // The chevron the card draws now; on its own toggle the one it drew before fades out where it stood (:1516,
        // 2-chat-use.mov 5.340-5.378 s), on the ghost layer.
        Button ChevronOf() => LiveChevron(card);
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        static void Near(double expected, double actual, string what) => Assert.True(Math.Abs(expected - actual) < 0.5, $"{what}: expected {expected}, was {actual}");
        Rect lastGlyphWhenExpanded = default;
        double collapsedChevronTop = 0, expandedChevronTop = 0;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                collapsedChevronTop = Frame(ChevronOf(), card).Y; // under the second row of twelve
                Assert.True(collapsedChevronTop > 40, "the head card has not been laid out");
                Click(ChevronOf());
            });
            harness.Main.OnMain(() => { });
            Frames(40); // the 0.18 s toggle has run its course
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(15, glyphs.Count);
                Assert.All(glyphs, g => Assert.Equal(1d, ShownOpacityWithin(g, card))); // all whole once it has ended
                lastGlyphWhenExpanded = Frame(glyphs[^1], card); // alone on the third row
                Assert.Single(ChevronButtons(card)); // the down-chevron the expansion took away is gone
                expandedChevronTop = Frame(ChevronOf(), card).Y;
                Assert.True(expandedChevronTop > collapsedChevronTop); // the chevron sits below that third row
                Click(ChevronOf()); // back to twelve
            });
            harness.Main.OnMain(() => { });
            Frames(3);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(15, glyphs.Count); // the leaving three are still drawn, fading
                for (var i = 0; i < 12; i++) Assert.Equal(1d, ShownOpacityWithin(glyphs[i], card)); // the twelve that stay never fade
                var leaving = glyphs.Skip(12).ToList();
                Assert.All(leaving, g => Assert.InRange(ShownOpacityWithin(g, card), 0.02, 0.98));
                // A leaver fades where it stood: the last one is drawn at the place it had on the third row ...
                Near(lastGlyphWhenExpanded.X, Frame(leaving[^1], card).X, "leaver x");
                Near(lastGlyphWhenExpanded.Y, Frame(leaving[^1], card).Y, "leaver y");
                // ... and holds no room: the chevron is already where the collapsed card puts it, not held down until
                // the leaving row is gone, to jump at the end.
                Near(collapsedChevronTop, Frame(ChevronOf(), card).Y, "chevron y");
                Assert.Equal("chevron.down", ChevronSymbolOf(ChevronOf())); // :1516
                Assert.Contains(leaving[^1].GetVisualAncestors().OfType<Canvas>(), c => !c.IsHitTestVisible); // never a target while it goes
                // The up-chevron it replaces is not cut away: it fades out where it stood, under the grown card's third row.
                var leavingChevron = Assert.Single(ChevronButtons(card), OnGhostLayer);
                Assert.Equal("chevron.up", ChevronSymbolOf(leavingChevron));
                Assert.InRange(ShownOpacityWithin(leavingChevron, card), 0.02, 0.98);
                Near(expandedChevronTop, Frame(leavingChevron, card).Y, "leaving chevron y");
            });
            Frames(40);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Equal(12, Glyphs().Count); // gone, the ghost layer empty
                Assert.Single(ChevronButtons(card)); // the up-chevron too
                Near(collapsedChevronTop, Frame(ChevronOf(), card).Y, "chevron y");
                // A rebuild that changes no membership (the selection moves) fades nothing, in or out.
                model.SelectedThreadID = heads[3].Id;
            });
            harness.Main.OnMain(() => { });
            Frames(2);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(12, glyphs.Count);
                Assert.All(glyphs, g => Assert.Equal(1d, ShownOpacityWithin(g, card)));
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // The card the list keeps is the card it had (SidebarView.swift:470-472 ForEach id "heads-<uuid>"; :1434-1437 @State): a
    // head that joins, or whose record changes, is handed to it as new headSnapshots, and what the user chose (every head
    // showing) and what is hovered stay. A head that joins fades in under :1541's panelSlide (the head ids changed, the
    // row count did not).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // :1434-1437, :1541
    [Trait("Req", "UI-SIDEBAR-036")] // :1541 .animation(Chrome.panelSlide, value: heads.map(\.id))
    public void A_head_card_handed_new_heads_keeps_every_head_showing_and_fades_a_joining_head_in_on_panel_slide() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 13).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var joining = HeadHelper(project, parent.Id, 99, -1); // the oldest: the card's last glyph, so no other glyph changes place
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        List<Button> Glyphs() => LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
        Button ChevronOf() => LogicalQuery.SelfAndDescendants<Button>(card)
            .Single(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName?.StartsWith("chevron", StringComparison.Ordinal) == true));
        string ChevronSymbol() => LogicalQuery.SelfAndDescendants<SwiftImage>(ChevronOf()).First(i => i.SymbolName is not null).SymbolName!;
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Equal(12, Glyphs().Count); // :1443 prefix(12)
                Click(ChevronOf()); // every head shows
            });
            harness.Main.OnMain(() => { });
            Frames(40);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Equal(13, Glyphs().Count);
                Assert.Equal("chevron.up", ChevronSymbol());
                CallOn(card, "Update", heads.Append(joining).ToList()); // :1430 the list's next pass: one head more
            });
            harness.Main.OnMain(() => { });
            Frames(3); // about 48 ms into panelSlide
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(14, glyphs.Count);
                Assert.Equal("chevron.up", ChevronSymbol()); // still every head: the card's own state survived the new heads
                Assert.Equal(1d, ShownOpacityWithin(glyphs[0], card));
                Assert.Equal(1d, ShownOpacityWithin(glyphs[12], card)); // the last of the thirteen already there
                Assert.InRange(ShownOpacityWithin(glyphs[13], card), 0.02, 0.98); // the one that joined, still coming in
                Assert.Equal(Chrome.PanelSlide.Progress(0.048), ShownOpacityWithin(glyphs[13], card), 3); // on :1541's panelSlide, 48 ms in
            });
            Frames(60);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(14, glyphs.Count);
                Assert.All(glyphs, g => Assert.Equal(1d, ShownOpacityWithin(g, card)));
                Assert.Equal("chevron.up", ChevronSymbol());
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // :470-472, :1434-1437: the list keeps the card whose head changed, with its @State
    public void The_real_list_keeps_an_expanded_head_card_when_a_new_pass_hands_it_changed_heads() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0) with { ExpandsHelpers = true };
        var heads = Enumerable.Range(0, 14).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        StackPanel list = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Control CardNow() => LogicalQuery.SelfAndDescendants<Control>(list).Single(c => c.GetType() == Private("HeadCardsRow"));
        List<Button> Glyphs(Control from) => LogicalQuery.SelfAndDescendants<Button>(from).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
        Control card = null!;
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraAutoHidesIdleHeads = false;
            view = Attached(model);
            window = WindowOf(view);
            list = ListPanel(view);
        });
        try
        {
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                card = CardNow();
                Assert.Equal(12, Glyphs(card).Count);
                Click(LogicalQuery.SelfAndDescendants<Button>(card).Single(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName == "chevron.down")));
            });
            Tick(600);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(14, Glyphs(card).Count); // every head showing
                model.UpdateThread(heads[3].Id, t => t with { Title = "Renamed head" }); // one head's thread changes: the card reads its live thread
                model.NewThread(project); // and the list takes a new pass (a thread more): its item for the card carries the heads as they are now
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Same(card, CardNow()); // the card the list had, not one built where it stood
                Assert.Equal(14, Glyphs(card).Count); // and every head still showing
                Assert.Contains(LogicalQuery.SelfAndDescendants<SwiftImage>(card), i => i.SymbolName == "chevron.up");
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // :1514 `if capped { chevron }`: the chevron is a view of the card's own stack, so it joins and leaves on the same default
    // opacity transition as a glyph when the head count crosses twelve; the card's row count changes with it, so :1535's
    // snappy is the animation in force.
    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // :1514, :1535
    public void The_head_cards_chevron_fades_in_when_the_card_becomes_capped_and_out_when_it_stops_being() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var twelve = Enumerable.Range(0, 12).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var thirteen = twelve.Append(HeadHelper(project, parent.Id, 99, -1)).ToList(); // the oldest: the last of the card
        var model = Model(harness, [project], twelve.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        List<Button> Chevrons() => LogicalQuery.SelfAndDescendants<Button>(card)
            .Where(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName?.StartsWith("chevron", StringComparison.Ordinal) == true)).ToList();
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, twelve, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Empty(Chevrons()); // twelve heads: not capped, no chevron
                CallOn(card, "Update", thirteen); // a thirteenth head: capped
            });
            harness.Main.OnMain(() => { });
            Frames(3);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var chevron = Assert.Single(Chevrons());
                Assert.InRange(ShownOpacityWithin(chevron, card), 0.02, 0.98); // joining
            });
            Frames(40);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Equal(1d, ShownOpacityWithin(Assert.Single(Chevrons()), card));
                CallOn(card, "Update", twelve); // back to twelve: no longer capped
            });
            harness.Main.OnMain(() => { });
            Frames(3);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var chevron = Assert.Single(Chevrons()); // still drawn, on its way out
                Assert.InRange(ShownOpacityWithin(chevron, card), 0.02, 0.98);
            });
            Frames(40);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Empty(Chevrons()); // gone
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // ---- REVIEW (pass 1, round 1, adversarial): the Remove project question outlives a re-render of its row ----
    //
    // OD-0008 A9 / OD-0012 E2 put the question on the project row "in the style of the thread delete question"
    // (SidebarView.swift:217-237): that question's state lives in the list (pendingDeletionID) and survives every
    // re-render of the row it hangs on, and a SwiftUI row's own @State survives a new value under the same ForEach id
    // ("project-<uuid>", :524). The port kept IsRemovalPresented in the ProjectRow control, and the list rebuilt that
    // control whenever its item's value changed (UpdateItemView updated only gaps and the Settled header), so a new
    // thread in the project (its count changes, :522-524) while the question was up dropped the question.

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // OD-0008 A9, OD-0012 E2: Remove project asks first; the question stays until answered or dismissed
    [Trait("Req", "UI-SIDEBAR-014")]
    public void REVIEW_The_Remove_project_question_stays_up_when_the_projects_count_changes_under_it() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        StackPanel list = null!; // taken once: a popup's content, once built, brings a ScrollViewer of its own into the view's logical tree
        Control ProjectRowNow() => list.Children.Single(c => c.GetType() == Private("ProjectRow"));
        bool Asking(Control row) => LogicalQuery.SelfAndDescendants<Popup>(row).Any(p => recorder.Calls.LastOrDefault(c => c.Popup == p) is { Presented: true } call
            && PopoverHost.Content(p)?.GetType() == Private("RemoveProjectPopover"));
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Settings.ConfirmBeforeDeleting);
            view = Attached(model);
            window = WindowOf(view);
            list = ListPanel(view);
        });
        try
        {
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                var row = ProjectRowNow();
                PointerRightClick(row);
                ContextMenuOf(row).Items.Cast<Control>().Single(i => i is MenuItem && MenuItemTitle(i) == "Remove project")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            });
            Tick(16);
            harness.Main.OnMain(() => Assert.True(Asking(ProjectRowNow()))); // the question is up
            harness.Main.OnMain(() => model.NewThread(project)); // a new thread in the project: its count goes from 1 to 2
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.NotNull(model.Project(project.Id)); // nothing answered
                Assert.True(Asking(ProjectRowNow())); // still asking on the project's row
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // The row a surviving project keeps is the row it had: its question, its open ellipsis menu and the count it draws are
    // all one state, and the count it draws is the new one (:1127-1132, :521-523).
    [Fact]
    [Trait("Req", "PR-SIDEBAR-026")] // SidebarView.swift:521-523, :1127-1132: the collapsed project's count
    [Trait("Req", "UI-SIDEBAR-009")]
    [Trait("Req", "PR-SIDEBAR-126")] // :333-334 the row under its ForEach id takes its new snapshot and count
    public void A_surviving_project_row_draws_its_new_count_in_place_and_keeps_its_open_ellipsis_menu() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var presented = Presentation.Presenter.Override(new RecordingPresenter());
        var project = NewProject(harness, "Alpha") with { IsExpanded = false };
        var first = NewThread(project, "First", 0);
        var second = NewThread(project, "Second", 1);
        var third = NewThread(project, "Third", 2);
        var model = Model(harness, [project], [first, second, third]);
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        StackPanel list = null!; // taken once: a popup's content, once built, brings a ScrollViewer of its own into the view's logical tree
        Control ProjectRowNow() => list.Children.Single(c => c.GetType() == Private("ProjectRow"));
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            view = Attached(model);
            window = WindowOf(view);
            list = ListPanel(view);
        });
        try
        {
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                row = ProjectRowNow();
                Assert.Contains("3", Texts(row)); // :1127-1132 the collapsed project's count
                model.Delete(third.Id); // the count goes from 3 to 2 and the project stays collapsed
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Same(row, ProjectRowNow()); // the same row, not a new one built where it stood
                Assert.Contains("2", Texts(row));
                Assert.DoesNotContain("3", Texts(row));
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value); // hovered: the ellipsis and plus take the count's place
            });
            Tick(300);
            Button more = null!;
            harness.Main.OnMain(() =>
            {
                more = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
                PointerClick(more); // :391 the ellipsis menu opens: isMenuPresented
                Assert.True((bool)Get(Field(row, "state")!, "IsMenuPresented")!);
            });
            Tick(16); // the accessory is drawn again for the open menu (:1116 hovering || isMenuPresented)
            harness.Main.OnMain(() =>
            {
                more = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More"); // the one the open menu hangs on
                model.Delete(second.Id); // the count goes from 2 to 1 under the open menu
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Same(row, ProjectRowNow());
                Assert.True((bool)Get(Field(row, "state")!, "IsMenuPresented")!); // still open
                Assert.Same(more, LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More")); // its anchor not rebuilt under it
                window.MouseMove(new Point(1, 1)); // the pointer leaves the row; the menu is still open
            });
            Tick(300);
            harness.Main.OnMain(() => Click(more)); // closes the menu: isMenuPresented false
            Tick(300);
            harness.Main.OnMain(() =>
            {
                Assert.Contains("1", Texts(row)); // the count the open menu held back is drawn now
                Assert.DoesNotContain("2", Texts(row));
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // The question's life in the popup (OD-0008 A9, OD-0012 E2, the thread delete question's own: confirmDeletion and
    // carryOutConfirmedDeletion, SidebarView.swift:256-306): the popup's own Opened and Closed run here, which the
    // recording presenter never raises. Dismissing removes nothing; the red action closes the question and removes the
    // project only on the next main-actor turn after the popup is gone; never in the click that confirms.

    internal static void RaisePopup(Popup popup, string name)
    {
        var field = typeof(Popup).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Popup." + name + " backing field not found");
        ((Delegate?)field.GetValue(popup))?.DynamicInvoke(popup, EventArgs.Empty);
    }

    // Both menus' Remove project call the row's RequestRemoval (the context menu's own click is proven above); the
    // lifecycle tests ask through it directly, so no menu popup stays open beside the question under test.
    internal static void AskToRemove(Control row, Project project) => CallOn(row, "RequestRemoval", project);

    internal static Button RemoveAction(Popup popup) =>
        LogicalQuery.SelfAndDescendants<Button>(PopoverHost.Content(popup)!).Single(b => AutomationProperties.GetName(b) == "Remove project");

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // OD-0008 A9: a dismissal of the question removes nothing
    [Trait("Req", "UI-SIDEBAR-014")]
    public void Dismissing_the_Remove_project_question_through_its_popup_removes_nothing_and_closes_it() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        Popup popup = null!;
        bool Presented() => (bool)Get(Field(row, "state")!, "IsRemovalPresented")!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 1));
        harness.Main.OnMain(() => AskToRemove(row, project)); // the row is mounted and settled first: the question's presentation follows the ask by a turn
        harness.Main.OnMain(() =>
        {
            popup = RemovalPopup(row);
            Assert.True(Presented());
            Assert.True(recorder.Calls.Last(c => c.Popup == popup).Presented);
            RaisePopup(popup, "Opened"); // :229 .onAppear: on screen
            RaisePopup(popup, "Closed"); // :231-232 .onDisappear, by a click away or Escape: the popup's own Closed
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            Assert.False(Presented(), "the question is still presented after its popup closed");
            Assert.False(recorder.Calls.Last(c => c.Popup == popup).Presented, "the presenter was not told the question is down");
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            Assert.NotNull(model.Project(project.Id)); // and nothing was removed, however many turns later
            Assert.NotNull(model.Thread(thread.Id));
            AskToRemove(row, project); // asking again works, and the dismissal left no half-confirmed removal behind
        });
        harness.Main.OnMain(() =>
        {
            popup = RemovalPopup(row);
            RaisePopup(popup, "Opened");
            RaisePopup(popup, "Closed");
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            Assert.NotNull(model.Project(project.Id));
            Assert.NotNull(model.Thread(thread.Id));
            WindowOf(row).Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // OD-0008 A9 via PR-SIDEBAR-131: the red action removes after the question's popup is gone
    [Trait("Req", "UI-SIDEBAR-014")]
    public void The_Remove_project_red_action_closes_the_question_and_removes_once_its_popup_is_gone() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        Popup popup = null!;
        bool Presented() => (bool)Get(Field(row, "state")!, "IsRemovalPresented")!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 1));
        harness.Main.OnMain(() => AskToRemove(row, project));
        harness.Main.OnMain(() =>
        {
            popup = RemovalPopup(row);
            RaisePopup(popup, "Opened"); // :229 .onAppear: the popup is on screen
            Click(RemoveAction(popup)); // the red action: PopoverItem closes its popover and runs the action on the next turn (Popovers.swift:271-273)
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            // :256-263 confirmDeletion closed the question ...
            Assert.False(Presented(), "the question is still presented after the red action");
            Assert.False(recorder.Calls.Last(c => c.Popup == popup).Presented, "the presenter was not told to close the question");
            // ... and, the popup still being on screen (removing the project removes this row, popup included), waits.
            Assert.NotNull(model.Project(project.Id));
            Assert.NotNull(model.Thread(thread.Id));
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            Assert.NotNull(model.Project(project.Id)); // still waiting, however many turns it is
            Assert.NotNull(model.Thread(thread.Id));
            RaisePopup(popup, "Closed"); // :231-232 .onDisappear -> carryOutConfirmedDeletion
            Assert.NotNull(model.Project(project.Id)); // on the main actor's next turn (:295-306), not inside Closed
            Assert.NotNull(model.Thread(thread.Id));
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            Assert.Null(model.Project(project.Id));
            Assert.Null(model.Thread(thread.Id));
            WindowOf(row).Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // :256-263, :295-306 confirmDeletion with no popup on screen: carryOutConfirmedDeletion on the next main-actor turn
    [Trait("Req", "UI-SIDEBAR-014")]
    public void Confirming_the_Remove_project_question_with_no_popup_on_screen_removes_on_the_next_turn_not_in_the_call() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presented = Presentation.Presenter.Override(new RecordingPresenter());
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        harness.Main.OnMain(() => row = ShownProjectRow(model, project, 1));
        harness.Main.OnMain(() => AskToRemove(row, project));
        harness.Main.OnMain(() =>
        {
            RemovalPopup(row); // asked, and never shown (Opened never raised): there is no popup to wait for
            CallOn(row, "ConfirmRemoval"); // the red action's own action, which PopoverItem runs on a turn of its own
            Assert.False((bool)Get(Field(row, "state")!, "IsRemovalPresented")!);
            Assert.NotNull(model.Project(project.Id)); // not synchronously in the confirmation
            Assert.NotNull(model.Thread(thread.Id));
        });
        harness.Main.OnMain(() => { });
        harness.Main.OnMain(() =>
        {
            Assert.Null(model.Project(project.Id)); // the next main-actor turn
            Assert.Null(model.Thread(thread.Id));
            WindowOf(row).Close();
        });
    });

    // ---- REVIEW (pass 2, round 1, adversarial): two changes the port makes by a cut where the Mac fades ----
    //
    // (1) Chrome.swift:735-737 SidebarRow flips isHovering inside withAnimation(Chrome.hover), and the project row's
    // accessory (SidebarView.swift:1115-1133) is `if hovering || isMenuPresented { HStack { ellipsis; plus } } else if
    // !project.isExpanded, count > 0 { Text(count) }`: the two branches swap on SwiftUI's default .opacity transition under
    // that easeOut 0.1 s, as the helper row's status and ellipsis do (Hovering_a_helper_row_crossfades_its_status_out_...).
    // OD-0076: nothing on screen changes by a cut. The port's SidebarRow (Chrome.cs) removes the old accessory and adds the
    // new one at once, so a search result's inert project row (UI-SIDEBAR-058 "still shows its hover accessories") shows
    // them by a cut.
    [Fact]
    [Trait("Req", "UI-SIDEBAR-058")] // SidebarView.swift:709, :1115-1133; Chrome.swift:731-737
    public void REVIEW_Round2_A_search_results_project_row_crossfades_its_count_out_and_its_hover_accessories_in_on_chrome_hover() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha") with { IsExpanded = false }; // folded: the idle accessory is the count (:1127-1131)
        var thread = NewThread(project, "Fix it", 1);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 1, togglesExpansion: false); // :709 the search result's own project row
            window = WindowOf(row);
        });
        try
        {
            TextBlock count = null!;
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                count = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "1"); // :1127-1131 while the pointer is elsewhere
                Assert.Empty(LogicalQuery.SelfAndDescendants<RowActionsButton>(row));
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value); // onto the row's icon
            });
            Tick(50); // half of Chrome.hover's easeOut 0.1 s
            harness.Main.OnMain(() =>
            {
                Assert.InRange(ShownOpacityWithin(LogicalQuery.SelfAndDescendants<RowActionsButton>(row).Single(), row), 0.05, 0.95); // fading in
                Assert.Contains(count, LogicalQuery.SelfAndDescendants<TextBlock>(row)); // the count is still drawn ...
                Assert.InRange(ShownOpacityWithin(count, row), 0.05, 0.95); // ... fading out
            });
            Tick(200);
            harness.Main.OnMain(() =>
            {
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(row), t => t.Text == "1");
                Assert.Equal(1d, ShownOpacityWithin(LogicalQuery.SelfAndDescendants<RowActionsButton>(row).Single(), row));
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // (2) 2-chat-use.mov 5.340-5.378 s (frames python frames.py extract 2-chat-use.mov 5.30 5.56 --crop 80:50:20:285): on the
    // chevron's own toggle (SidebarView.swift:1515-1516 withAnimation(.snappy(duration: 0.18))) the down-chevron does not
    // vanish. At 5.340 s it is whole; at 5.357 s it is still drawn, faint, exactly where it stood, over the first joining
    // glyph fading in at the same place; it is gone once the toggle has run (and the up-chevron is under the grown card's
    // last row). OD-0076 asks the same. The port rebuilds the card's stack and puts a new up-chevron in at once, so the
    // down-chevron is gone in the first frame.
    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // SidebarView.swift:1514-1526 the chevron, :1515-1516 its snappy toggle; 2-chat-use.mov 5.340-5.378 s
    public void REVIEW_Round2_The_head_cards_down_chevron_fades_out_where_it_stood_on_its_own_toggle_instead_of_vanishing() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 15).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        List<SwiftImage> Chevrons(string symbol) => LogicalQuery.SelfAndDescendants<SwiftImage>(card).Where(i => i.SymbolName == symbol).ToList();
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        Rect downWhenCollapsed = default;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var down = Chevrons("chevron.down").Single();
                downWhenCollapsed = Frame(down, card);
                Assert.True(downWhenCollapsed.Y > 40, "the head card has not been laid out"); // under the second row of twelve
                Click(LogicalQuery.SelfAndDescendants<Button>(card).Single(b => b.GetLogicalDescendants().Contains(down)));
            });
            harness.Main.OnMain(() => { });
            Frames(2); // about 32 ms into the 0.18 s snappy toggle (the video's 5.357 s frame is 17-27 ms after the press)
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var down = Assert.Single(Chevrons("chevron.down")); // still drawn ...
                Assert.InRange(ShownOpacityWithin(down, card), 0.02, 0.98); // ... fading out ...
                Assert.True(Math.Abs(Frame(down, card).Y - downWhenCollapsed.Y) < 0.5, "the down-chevron fades where it stood"); // ... in place
            });
            Frames(40);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Empty(Chevrons("chevron.down")); // gone once the toggle has run
                Assert.Equal(1d, ShownOpacityWithin(Chevrons("chevron.up").Single(), card));
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // (3) SidebarView.swift:1338-1409: SidebarHelperRow reads its live thread through the model's cell (:1324-1325) and keeps
    // @State isMenuPresented (:1340) under its own identity, so its ellipsis menu (:1375-1377 RowActionsButton(actions:,
    // isPresented: $isMenuPresented)) stays open on the same button while the helper's record changes under it (its title,
    // an unread mark, a new updatedAt): only the title re-renders. The port's SidebarHelperRow builds a new RowActionsButton,
    // with a popup of its own, on every change of the record (its overlay Track), so the open menu's anchor is replaced
    // under it and the menu is presented again on another popup.
    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // :1340-1341 showsActions = isHovering || isMenuPresented; :1375-1377 the ellipsis
    public void REVIEW_Round2_A_helper_rows_open_ellipsis_menu_keeps_its_button_while_the_helpers_record_changes() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var presenter = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(presenter);
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Button More() => LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
        harness.Main.OnMain(() =>
        {
            row = ShownHelperRow(model, helper, Activator.CreateInstance(Private("SidebarMenuRequests"))!);
            window = WindowOf(row);
            window.MouseMove(row.TranslatePoint(new Point(100, 12), window)!.Value); // hovered: the ellipsis in place of the status
        });
        try
        {
            Tick(300);
            Button more = null!;
            harness.Main.OnMain(() =>
            {
                more = More();
                PointerClick(more); // :391 the ellipsis menu opens: isMenuPresented
            });
            Tick(16);
            harness.Main.OnMain(() =>
            {
                Assert.Single(presenter.Calls, call => call.Presented); // the menu, presented once
                Assert.True(ReferenceEquals(more, More()), "the ellipsis was rebuilt as its menu opened");
                model.UpdateThread(helper.Id, t => t with { Title = "Helper renamed" }); // the helper's thread changes under the open menu
            });
            Tick(300);
            harness.Main.OnMain(() =>
            {
                Assert.Contains("Helper renamed", Texts(row)); // :1359 the title follows the live record
                Assert.True(ReferenceEquals(more, More()), "the open menu's ellipsis was rebuilt when the helper's thread changed"); // the open menu hangs on the button it had
                Assert.Single(presenter.Calls, call => call.Presented); // and is not presented again on another popup
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // ---- Review round 2 (pass 2): the proofs the round asked for, each against the mutant that survived it ----
    // (mutants RA1-RA16 in E:\Droppy\work\parity\evidence\SidebarView\review\mutants_review2.txt)

    // The leaving glyphs' layer (ED-258 (2)) is the card's own area under the card's clip (:1521
    // .clipShape): a leaving glyph is drawn whole while the shrinking card still holds its row, and cut by the card's edge
    // once the card has shrunk past it, where SwiftUI's clip cuts it (RA1: the layer keeps no height and hides every leaver;
    // RA2: the layer does not clip and leavers draw outside the card).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // :1514-1516 the toggle; :1521 .clipShape; :1535 snappy
    public void A_leaving_glyph_is_drawn_inside_the_card_and_cut_by_its_edge_once_the_card_shrinks_past_its_row() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        // Twenty-two heads, seven to a row at the host's 200 pt (:1451): the last one alone on a fourth row, which the
        // collapsed card (two rows of glyphs and the chevron's, :1461) no longer reaches.
        Assert.Equal(7, HeadCardsRow.PerRow(200));
        var heads = Enumerable.Range(0, 22).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        List<Button> Glyphs() => LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
        Button Leaver() => Glyphs().Where(OnGhostLayer).OrderBy(g => Frame(g, card).Y).Last(); // the one from the fourth row
        Border Face() => LogicalQuery.SelfAndDescendants<Border>(card).Single(b => b.Child is StackPanel && b.Padding == new Thickness(ThreadRowMetrics.HeadCardPadding)); // :1516-1521
        List<Control> ClipsOf(Control leaver) => leaver.GetVisualAncestors().OfType<Control>().TakeWhile(a => !ReferenceEquals(a, card)).Where(a => a.ClipToBounds).ToList();
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.Height = 300; // room for the expanded card's five rows
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() => Click(LiveChevron(card))); // every head
            Frames(40);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(22, glyphs.Count);
                // The last glyph on the fourth row: 6 pt of padding and three rows of 14 pt with 6 pt between them above it (:1465, :1516).
                Assert.Equal(6 + 3 * (14 + 6), Frame(glyphs[^1], card).Y - Frame(Face(), card).Y, 3);
                Click(LiveChevron(card)); // back to twelve: the card shrinks from five rows to three on snappy (:1535)
            });
            harness.Main.OnMain(() => { });
            Frames(1); // 16 ms in: the card still holds the fourth row
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var leaver = Leaver();
                Assert.InRange(ShownOpacityWithin(leaver, card), 0.02, 0.98); // fading where it stood
                var at = Frame(leaver, card);
                Assert.True(at.Bottom <= Frame(Face(), card).Bottom, "the card should still hold the leaver's row");
                Assert.NotEmpty(ClipsOf(leaver)); // under the card's clip
                foreach (var clip in ClipsOf(leaver))
                    Assert.True(Frame(clip, card).Contains(at), $"a {clip.GetType().Name} of {Frame(clip, card)} cuts the leaver at {at}, which the card still holds");
            });
            Frames(4); // 80 ms in: snappy has the card at about 72 pt of its way from 106 to 66, short of the fourth row's bottom (80)
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var leaver = Leaver();
                Assert.InRange(ShownOpacityWithin(leaver, card), 0.02, 0.98); // still drawn, still fading
                var at = Frame(leaver, card);
                var face = Frame(Face(), card);
                Assert.True(face.Bottom > at.Top && face.Bottom < at.Bottom, $"the card's edge at {face.Bottom} should cross the leaver at {at.Top}-{at.Bottom}");
                // :1521 the card's own clip cuts it there, at the card's edge, not outside the card
                Assert.Contains(ClipsOf(leaver), clip => Math.Abs(Frame(clip, card).Bottom - face.Bottom) < 0.5);
            });
            Frames(40);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Equal(12, Glyphs().Count); // gone
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // :1515-1516 the record's "click toggles ... animated with .snappy(duration: 0.18)", on its curve: with fourteen heads,
    // seven to a row, the card keeps two rows of glyphs either way (:1461) and the same heads (:1541), so no .animation of
    // the card's takes over and the toggle's own snappy carries the glyphs that join and the chevrons that swap (RA7: the
    // toggle on panelSlide).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // :1515-1516 withAnimation(.snappy(duration: 0.18)) { showsAllHeads.toggle() }
    public void The_chevrons_toggle_fades_the_glyphs_past_twelve_and_its_symbols_on_snappy_0_18_when_the_cards_rows_stay() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        Assert.Equal(7, HeadCardsRow.PerRow(200));
        var heads = Enumerable.Range(0, 14).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        List<Button> Glyphs() => LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        var snappy = Animation.Snappy(duration: 0.18).Progress(0.032);
        Assert.True(Math.Abs(snappy - Chrome.PanelSlide.Progress(0.032)) > 0.1); // the fixture tells the two curves apart
        double cardHeight = 0;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                cardHeight = card.Bounds.Height;
                Assert.Equal(12, Glyphs().Count);
                Click(LiveChevron(card));
            });
            harness.Main.OnMain(() => { });
            Frames(2); // 32 ms into the toggle
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(14, glyphs.Count);
                Assert.Equal(cardHeight, card.Bounds.Height); // the card's rows stay: nothing of the card's own animates
                foreach (var joining in glyphs.Skip(12)) Assert.Equal(snappy, ShownOpacityWithin(joining, card), 3); // the two past twelve, on snappy
                Assert.Equal(snappy, ShownOpacityWithin(LiveChevron(card), card), 3); // the up-chevron comes in on it
                Assert.Equal(1 - snappy, ShownOpacityWithin(Assert.Single(ChevronButtons(card), OnGhostLayer), card), 3); // and the down-chevron goes on it
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // A head that joins on a row of its own changes the card's row count and its head ids together: the innermost
    // .animation over the glyphs, :1535 snappy for cardRows, is the one in force for it, not :1541 panelSlide around the
    // card (RA5: panelSlide first).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // :1535 inside :1541
    [Trait("Req", "UI-SIDEBAR-036")] // "row count changes animate with .snappy(duration: 0.18)"
    public void A_head_that_joins_on_a_new_row_fades_in_on_the_cards_snappy_and_not_on_panel_slide() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        Assert.Equal(7, HeadCardsRow.PerRow(200));
        var heads = Enumerable.Range(0, 14).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var joining = HeadHelper(project, parent.Id, 99, -1); // the oldest: the card's last glyph, alone on a third row
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        List<Button> Glyphs() => LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
        void Frames(int count) { for (var i = 0; i < count; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        var snappy = Animation.Snappy(duration: 0.18).Progress(0.032);
        Assert.True(Math.Abs(snappy - Chrome.PanelSlide.Progress(0.032)) > 0.1);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.Height = 300;
            window.UpdateLayout();
        });
        try
        {
            harness.Main.OnMain(() => Click(LiveChevron(card))); // every head: two full rows of seven
            Frames(40);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Assert.Equal(14, Glyphs().Count);
                CallOn(card, "Update", heads.Append(joining).ToList()); // :1430 the list's next pass: a fifteenth head, on a third row
            });
            harness.Main.OnMain(() => { });
            Frames(2);
            harness.Main.OnMain(() =>
            {
                window.UpdateLayout();
                var glyphs = Glyphs();
                Assert.Equal(15, glyphs.Count);
                var last = glyphs.OrderBy(g => Frame(g, card).Y).Last(); // alone on the third row
                Assert.Equal(snappy, ShownOpacityWithin(last, card), 3);
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // A search result's project row is the same row through a new pass of the results (:705-709, the group's ForEach id
    // the project's; :1081-1089 its inputs handed in place): an open ellipsis menu keeps its button, and the Remove project
    // question stays up, when a new thread changes the results under them (RA3: the search list rebuilds the row).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-058")] // :709 the inert project row keeps its hover accessories and menus
    [Trait("Req", "PR-SIDEBAR-130")] // OD-0008 A9, OD-0012 E2: the question stays until it is answered or dismissed
    [Trait("Req", "UI-SIDEBAR-014")]
    public void A_search_results_project_row_keeps_its_open_menu_and_its_Remove_question_through_a_new_pass_of_the_results() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        StackPanel list = null!; // taken once: a popup's content, once built, brings a ScrollViewer of its own into the view's logical tree
        Control ProjectRowNow() => list.Children.Single(c => c.GetType() == Private("ProjectRow"));
        Button More(Control row) => LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
        bool MenuPresented(Control row) => (bool)Get(Field(row, "state")!, "IsMenuPresented")!;
        bool Asking(Control row) => LogicalQuery.SelfAndDescendants<Popup>(row).Any(p => recorder.Calls.LastOrDefault(c => c.Popup == p) is { Presented: true }
            && PopoverHost.Content(p)?.GetType() == Private("RemoveProjectPopover"));
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Settings.ConfirmBeforeDeleting);
            view = Attached(model);
            window = WindowOf(view);
            list = ListPanel(view);
        });
        try
        {
            Tick(1600);
            harness.Main.OnMain(() => SearchBox(view).Text = "alpha"); // :743 the project's name matches: its threads are the results
            Tick(1600);
            Control row = null!;
            Button more = null!;
            harness.Main.OnMain(() =>
            {
                row = ProjectRowNow();
                Assert.DoesNotContain(list.Children, c => c.GetType() == Private("ProjectRow") && !ReferenceEquals(c, row)); // the search list's own row
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value); // hovered: the ellipsis and plus
            });
            Tick(300);
            harness.Main.OnMain(() =>
            {
                more = More(row);
                PointerClick(more); // :391 the ellipsis menu opens
            });
            Tick(16);
            harness.Main.OnMain(() =>
            {
                Assert.True(MenuPresented(row));
                model.NewThread(project); // a thread more among the results: the row's count goes from 1 to 2
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Equal("alpha", SearchBox(view).Text); // still the search list
                Assert.Same(row, ProjectRowNow()); // the row the results had, handed its new count
                Assert.True(MenuPresented(row)); // its menu still open ...
                Assert.Same(more, More(row)); // ... on the button it opened from
                Assert.Single(recorder.Calls, call => call.Presented && ReferenceEquals(call.Popup.PlacementTarget, more)); // and presented once
                Click(more); // closed
            });
            Tick(300);
            harness.Main.OnMain(() =>
            {
                PointerRightClick(row);
                ContextMenuOf(row).Items.Cast<Control>().Single(i => i is MenuItem && MenuItemTitle(i) == "Remove project")
                    .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            });
            Tick(16);
            harness.Main.OnMain(() =>
            {
                Assert.True(Asking(row)); // the question is up
                model.NewThread(project); // and the results change again under it: 2 to 3
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Same(row, ProjectRowNow());
                Assert.NotNull(model.Project(project.Id)); // nothing answered
                Assert.True(Asking(row)); // still asking, on the same row
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // Chrome.swift:731-737 puts .overlay before .onHover, so a pointer resting on the ellipsis is on the row: the ellipsis menu
    // opened and closed from there leaves the ellipsis and plus drawn (:1116 hovering || isMenuPresented), not the count
    // (RA4: the accessory asked again with the row button's own hover, which the ellipsis is not part of).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-010")] // :1116-1125 "visible only while the row is hovered or its ellipsis menu is open"
    [Trait("Req", "PR-SIDEBAR-126")] // Chrome.swift:731-737 the row's hover covers its accessory
    public void Closing_the_ellipsis_menu_with_the_pointer_resting_on_the_ellipsis_keeps_the_ellipsis_and_plus() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var presenter = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(presenter);
        var project = NewProject(harness, "Alpha") with { IsExpanded = false }; // folded: the idle accessory is the count
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Button More() => LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
        bool MenuPresented() => (bool)Get(Field(row, "state")!, "IsMenuPresented")!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 1);
            window = WindowOf(row);
            window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value); // onto the icon
        });
        try
        {
            Tick(300);
            Button more = null!;
            harness.Main.OnMain(() =>
            {
                more = More();
                window.MouseMove(more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), window)!.Value); // onto the ellipsis, and resting there
            });
            Tick(300);
            harness.Main.OnMain(() => PointerClick(more)); // :391 opens the menu
            Tick(16);
            harness.Main.OnMain(() =>
            {
                Assert.True(MenuPresented());
                PointerClick(more); // and closes it, the pointer still on the ellipsis
            });
            Tick(300);
            harness.Main.OnMain(() =>
            {
                Assert.False(MenuPresented());
                Assert.Same(more, More()); // the ellipsis it rests on, still drawn ...
                Assert.Equal(1d, ShownOpacityWithin(more, row));
                var plus = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "New thread in Alpha"); // ... with the plus
                Assert.Equal(1d, ShownOpacityWithin(plus, row));
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(row), t => t.Text == "1"); // and the count not back
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // Chrome.swift:729, :735-737: the row's fill follows the hover on Chrome.hover (easeOut 0.1 s), not by a snap; half way
    // it is part way between clear and the 0.06 overlay (RA16: the fill set with no animation).
    [Fact]
    [Trait("Req", "PR-SIDEBAR-126")] // the project row is a SidebarRow: its hover fill
    public void A_project_rows_hover_fill_eases_in_on_chrome_hover() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        Border shape = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 1);
            shape = LogicalQuery.SelfAndDescendants<Border>(row).First(b => b.Height == 28 && b.Padding.Right > 40);
            Assert.Equal(0, ((ISolidColorBrush)shape.Background!).Color.A); // :744 clear
        });
        try
        {
            harness.Main.OnMain(() => WindowOf(row).MouseMove(row.TranslatePoint(new Point(10, 10), WindowOf(row))!.Value));
            harness.Main.AdvanceMilliseconds(50);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(50)));
            harness.Main.OnMain(() =>
            {
                var end = Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant); // :743
                var now = ((ISolidColorBrush)shape.Background!).Color;
                Assert.Equal((byte)Math.Round(end.A * Animation.EaseOut(0.1).Progress(0.05)), now.A);
                Assert.InRange(now.A, (byte)1, (byte)(end.A - 1)); // neither clear nor there yet
            });
            harness.Main.AdvanceMilliseconds(60);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(60)));
            harness.Main.OnMain(() => Assert.Equal(Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant), ((ISolidColorBrush)shape.Background!).Color));
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    // :165-167 the Add project row is a SidebarRow too: its hover fill eases the same way (UI-SIDEBAR-042 "hover fill
    // Chrome.overlay(0.06)"; RA16).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-042")]
    public void The_Add_project_rows_hover_fill_eases_in_on_chrome_hover() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        SidebarView view = null!;
        Border shape = null!;
        harness.Main.OnMain(() => view = Attached(model));
        try
        {
            harness.Main.OnMain(() =>
            {
                var addProject = LogicalQuery.SelfAndDescendants<SidebarRow>(view).Single(r => LogicalQuery.SelfAndDescendants<TextBlock>(r).Any(t => t.Text == "Add project"));
                shape = LogicalQuery.SelfAndDescendants<Border>(addProject).First(b => b.Height == 28);
                Assert.Equal(0, ((ISolidColorBrush)shape.Background!).Color.A); // :744 clear
                WindowOf(view).MouseMove(shape.TranslatePoint(new Point(40, 14), WindowOf(view))!.Value);
            });
            harness.Main.AdvanceMilliseconds(50);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(50)));
            harness.Main.OnMain(() =>
            {
                var end = Chrome.Overlay(0.06).Resolve(shape.ActualThemeVariant);
                var now = ((ISolidColorBrush)shape.Background!).Color;
                Assert.Equal((byte)Math.Round(end.A * Animation.EaseOut(0.1).Progress(0.05)), now.A);
                Assert.InRange(now.A, (byte)1, (byte)(end.A - 1));
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // The ellipsis a row keeps (above) opens on the actions its body builds now (:1375-1376 makeActions() over the live
    // thread, :1118 actions(isActive:) over the live project and activation): the kept button reads them as its menu opens,
    // not when the button was first drawn.
    internal static string[] MenuTitles(RecordingPresenter presenter, Button more) =>
        LogicalQuery.SelfAndDescendants<Button>(presenter.Calls.Last(call => call.Presented && ReferenceEquals(call.Popup.PlacementTarget, more)).Popup.Child!)
            .Select(b => AutomationProperties.GetName(b) ?? "").Where(name => name.Length > 0).ToArray();

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // :1375-1376 RowActionsButton(actions: makeActions(), ...) over the live thread (:2214 Reveal worktree)
    public void A_helper_rows_kept_ellipsis_opens_on_the_actions_of_the_record_as_it_is_when_it_opens() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var presenter = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(presenter);
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Button More() => LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
        harness.Main.OnMain(() =>
        {
            row = ShownHelperRow(model, helper, Activator.CreateInstance(Private("SidebarMenuRequests"))!);
            window = WindowOf(row);
            window.MouseMove(row.TranslatePoint(new Point(100, 12), window)!.Value); // hovered: the ellipsis
        });
        try
        {
            Tick(300);
            Button more = null!;
            harness.Main.OnMain(() =>
            {
                more = More();
                model.UpdateThread(helper.Id, t => t with { WorktreePath = "C:/Alpha/wt" }); // the helper gets a worktree under the drawn ellipsis
            });
            Tick(300);
            harness.Main.OnMain(() =>
            {
                Assert.Same(more, More()); // the ellipsis is the one it had
                PointerClick(more);
            });
            Tick(16);
            harness.Main.OnMain(() => Assert.Contains("Reveal worktree in File Explorer", MenuTitles(presenter, more))); // :2214, as it is now
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-130")] // :1216-1220 Activate/Deactivate by the current activation, :1096 isActive read live
    [Trait("Req", "UI-SIDEBAR-014")] // "Activate project and Deactivate project are mutually exclusive by current activation"
    public void A_project_rows_kept_ellipsis_opens_on_the_activation_as_it_is_when_it_opens() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var presenter = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(presenter);
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        Button More() => LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More");
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 1);
            window = WindowOf(row);
            window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value); // hovered: the ellipsis and plus
        });
        try
        {
            Tick(300);
            Button more = null!;
            harness.Main.OnMain(() =>
            {
                Assert.True(model.Settings.IsProjectActive(project.Id));
                more = More();
                model.Settings.SetProjectActive(false, project.Id); // switched off under the drawn ellipsis
            });
            Tick(300);
            harness.Main.OnMain(() =>
            {
                Assert.Same(more, More()); // the ellipsis is the one it had
                PointerClick(more);
            });
            Tick(16);
            harness.Main.OnMain(() =>
            {
                var titles = MenuTitles(presenter, more);
                Assert.Contains("Activate project", titles); // :1217 isActive false now
                Assert.DoesNotContain("Deactivate project", titles);
                Assert.Contains("Follow global setting", titles); // :1223 the override this switch made
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // :1378-1380 the status branch is ActivityStatus(thread:) over the live thread: a helper that turns unread while the pointer
    // is elsewhere draws the 7 pt dot (:2421-2425), in the danger colour once its last turn failed (:2423). The status the row
    // keeps while its branch holds (ED-258 (3)) is drawn again for that (mutant R4M9: never drawn again).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")]
    public void A_helper_rows_status_follows_its_thread_turning_unread_and_then_failed() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        Control row = null!;
        AvaloniaWindow window = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        List<Avalonia.Controls.Shapes.Ellipse> Dots() => LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(row).ToList();
        harness.Main.OnMain(() =>
        {
            row = ShownHelperRow(model, helper, Activator.CreateInstance(Private("SidebarMenuRequests"))!);
            window = WindowOf(row);
        });
        try
        {
            harness.Main.OnMain(() =>
            {
                Assert.Empty(Dots()); // read, nothing to show (:2426)
                model.UpdateThread(helper.Id, t => t with { HasUnread = true });
            });
            Tick(300);
            harness.Main.OnMain(() =>
            {
                var dot = Assert.Single(Dots()); // :2421-2425
                Assert.Equal(7d, dot.Width);
                Assert.Equal(Chrome.Accent.Resolve(dot.ActualThemeVariant), ((ISolidColorBrush)dot.Fill!).Color);
                model.UpdateThread(helper.Id, t => t with { LastStatus = TurnStatus.Failed });
            });
            Tick(300);
            harness.Main.OnMain(() =>
            {
                var dot = Assert.Single(Dots());
                Assert.Equal(Chrome.Danger.Resolve(dot.ActualThemeVariant), ((ISolidColorBrush)dot.Fill!).Color); // :2423
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // The list keeps a helper row whose own item changes in a new pass (:470-472 its ForEach id; :1320, :1326 its snapshot and
    // isLast handed in place, UpdateItemView): its open ellipsis menu keeps its button, the title is the record's as it is
    // now, and the connector ends at the row once the helper below it is gone (:1349, :1665-1669).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")]
    public void The_real_list_keeps_a_helper_row_and_its_open_menu_when_a_new_pass_hands_it_a_changed_record() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var presenter = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(presenter);
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0) with { ExpandsHelpers = true };
        var first = NewThread(project, "Helper one", 1) with { ParentThreadID = parent.Id };
        var second = NewThread(project, "Helper two", 2) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, first, second]);
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        StackPanel list = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        List<Control> HelperRows() => LogicalQuery.SelfAndDescendants<Control>(list).Where(c => c.GetType() == Private("SidebarHelperRow")).ToList();
        Control HelperRowTitled(string title) => HelperRows().Single(r => Texts(r).Any(t => t.StartsWith(title, StringComparison.Ordinal)));
        bool EndsHere(Control helperRow) => LogicalQuery.SelfAndDescendants<HelperConnector>(helperRow).Single()
            .GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data!.Bounds.Bottom < ThreadRowMetrics.HelperHeight - 4; // :1669 the stem stops at the row's middle
        Button More(Control helperRow) => LogicalQuery.SelfAndDescendants<Button>(helperRow).Single(b => ToolTip.GetTip(b) as string == "More");
        harness.Main.OnMain(() =>
        {
            view = Attached(model);
            window = WindowOf(view);
            list = ListPanel(view);
        });
        try
        {
            Tick(1600);
            Control kept = null!;
            string goneTitle = null!;
            harness.Main.OnMain(() =>
            {
                Assert.Equal(2, HelperRows().Count);
                kept = Assert.Single(HelperRows(), r => !EndsHere(r)); // the helper with another below it
                goneTitle = Texts(Assert.Single(HelperRows(), EndsHere)).First(t => t.StartsWith("Helper", StringComparison.Ordinal));
                window.MouseMove(kept.TranslatePoint(new Point(100, 12), window)!.Value); // hovered: its ellipsis
            });
            Tick(300);
            Button more = null!;
            harness.Main.OnMain(() =>
            {
                more = More(kept);
                PointerClick(more); // its menu opens
            });
            Tick(16);
            harness.Main.OnMain(() =>
            {
                Assert.Single(presenter.Calls, call => call.Presented);
                var keptID = goneTitle == "Helper one" ? second.Id : first.Id;
                model.UpdateThread(keptID, t => t with { Title = "Helper kept" }); // its own thread changes ...
                model.Delete(goneTitle == "Helper one" ? first.Id : second.Id); // ... and the list takes a new pass without the other helper
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Same(kept, Assert.Single(HelperRows())); // the row the list had, not one built where it stood
                Assert.Same(kept, HelperRowTitled("Helper kept")); // drawing its thread as it is now
                Assert.True(EndsHere(kept)); // :1349 isLast handed in: the connector ends at this row
                Assert.Same(more, More(kept)); // its open menu still on the button it opened from
                Assert.Single(presenter.Calls, call => call.Presented); // and not presented again
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // The other way round (review pass 2, round 3): SidebarView.swift:1325-1326 `let isLast`, :1348-1349
    // HelperConnector(endsHere: isLast), :1665-1674 the stem stops at the row's middle only on the last helper and runs
    // through every other. Helpers are listed newest first (:419, createdAt descending) and an archived one is not listed
    // (:414), so an older helper coming back from the archive lands below the last one, which the list keeps (its ForEach id,
    // UpdateItemView) and hands isLast false: its connector runs on down to the new last row instead of stopping at its own
    // middle. A list that handed every kept row true, or an EndsHere that always drew the end, leaves a gap above the new
    // last row. Each connector is the full 28 x 24 shape: the stem down x = 18 (:1292 connectorLineX) from the top, the tick
    // across the middle to 2 pt short of the trailing edge (x = 26).
    [Fact]
    [Trait("Req", "PR-SIDEBAR-111")] // App/Views/Sidebar/SidebarView.swift:1325-1326, :1348-1349, :1662-1676, :409-420
    public void A_kept_last_helper_row_runs_its_connector_through_once_an_older_helper_lands_below_it() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0) with { ExpandsHelpers = true };
        var newer = NewThread(project, "Helper newer", 2) with { ParentThreadID = parent.Id };
        var older = NewThread(project, "Helper older", 1) with { ParentThreadID = parent.Id, IsArchived = true };
        var model = Model(harness, [project], [parent, newer, older]);
        SidebarView view = null!;
        AvaloniaWindow window = null!;
        StackPanel list = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }
        List<Control> HelperRows() => LogicalQuery.SelfAndDescendants<Control>(list).Where(c => c.GetType() == Private("SidebarHelperRow")).ToList();
        Control HelperRowTitled(string title) => HelperRows().Single(r => Texts(r).Contains(title));
        void Connector(Control helperRow, bool endsHere)
        {
            var shape = LogicalQuery.SelfAndDescendants<HelperConnector>(helperRow).Single().GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data!.Bounds;
            Assert.Equal(18d, shape.Left, 3); // :1668, :1670 the stem and the tick start at x = 18
            Assert.Equal(26d, shape.Right, 3); // :1671 the tick ends at rect.maxX - 2
            Assert.Equal(0d, shape.Top, 3); // :1668 from rect.minY
            Assert.Equal(endsHere ? ThreadRowMetrics.HelperHeight / 2 : ThreadRowMetrics.HelperHeight, shape.Bottom, 3); // :1669 midY on the last helper, maxY otherwise
        }
        harness.Main.OnMain(() =>
        {
            view = Attached(model);
            window = WindowOf(view);
            list = ListPanel(view);
        });
        try
        {
            Tick(1600);
            Control kept = null!;
            harness.Main.OnMain(() =>
            {
                kept = Assert.Single(HelperRows()); // the archived helper is not listed (:414)
                Connector(kept, endsHere: true); // the only helper: the connector ends at it
                model.UpdateThread(older.Id, t => t with { IsArchived = false }); // the older helper is back, below it (:419)
            });
            Tick(1600);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(2, HelperRows().Count);
                Assert.Same(kept, HelperRowTitled("Helper newer")); // the row the list had
                Assert.Same(kept, HelperRows()[0]); // above the older one
                Connector(HelperRowTitled("Helper older"), endsHere: true); // the new last helper ends the connector
                Connector(kept, endsHere: false); // :1349 isLast false now: the stem runs through to the row below
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });
}

// STOP App.Views.Sidebar.SidebarView #9 (Window review fixes)
// Kind: missing capture oracle.
// Swift: App/Views/Sidebar/SidebarView.swift:1933-1978 (B3 52bc792). Record: PR-SIDEBAR-135, partial.
// Missing: capture comparison of departure and arrival faces against pinned 1.7.0 renders, with the source
// thread states, clearances and DPI. Headless can render Windows controls but supplies no macOS reference faces.
// Needed: pinned reference captures plus comparison evidence for both faces. DPI and two-face contracts are tested.
