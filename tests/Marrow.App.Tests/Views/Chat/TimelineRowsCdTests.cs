using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792). The clauses of the rows' UI records that the earlier
// TimelineRows tests did not pin, found by the clause-by-clause audit of 2026-10-03 (unit blitz-timelinerows-cd). Where a
// record asks for a comparison with a frame the reference videos do not contain, OD-0098 makes the pinned Swift the proof:
// each test below states the Swift lines it reads and the value the code gives.
[Collection("Headless")]
public sealed class TimelineRowsCdTests(HeadlessSession headless)
{
    private static ToolRow ToolRowFor(ThreadRuntimePart12Harness h, ToolCall call) => new(ToolEntry(call), h.Runtime, h.Project.Path);

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    private static Control PlanDetail(PlanCard card, ProposedPlan plan) =>
        Assert.IsAssignableFrom<Control>(typeof(PlanCard).GetMethod("Detail", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, [plan]));

    private static ProposedPlan PlanOf(TimelineEntry entry) => ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value;

    private static FileChangeSummary Files(int count) =>
        new(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);

    // ---------------------------------------------------------------- UI-CHAT-024

    // :1774 HStack(spacing: iconSpacing) { badge; Spacer(minLength: 8); outcome }: a long title cannot take the room of the
    // trailing "exit N" or "Declined": the badge's text is cut so that the spacer keeps its 8-point minimum between two
    // 8-point stack gaps, 24 points in all, and the outcome stays the last thing on the row, 12 points in (:1844).
    [Fact]
    [Trait("Req", "UI-CHAT-024")] // TimelineRows.swift:1774, :1832-1844 the minimum 8-point spacer before any trailing status text
    public void A_long_title_leaves_the_trailing_exit_code_and_Declined_text_the_minimum_spacer_and_the_two_stack_gaps() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var failed = ToolRowFor(h, Call(ToolCall.Kind.Command, new string('x', 400), ToolCall.Status.Failed) with { ExitCode = 127 });
            var declined = ToolRowFor(h, Call(ToolCall.Kind.Command, new string('y', 400), ToolCall.Status.Declined));
            var panel = new StackPanel { Spacing = 20, Width = 400 };
            panel.Children.Add(failed);
            panel.Children.Add(declined);
            var window = Show(panel);
            try
            {
                foreach (var (row, words) in new[] { (failed, "exit 127"), (declined, "Declined") })
                {
                    var label = Assert.Single(Mounted<MiddleTruncatingTextBlock>(row));
                    var outcome = TextNamed(row, words);
                    // The label gave way: it does not run to the row's 12-point trailing padding.
                    Assert.True(label.Bounds.Width < 400 - 12 - outcome.Bounds.Width - 24 + 0.5, $"{words}: the label is {label.Bounds.Width} wide");
                    // 8 (stack gap) + 8 (the spacer's minimum) + 8 (stack gap) between the label and the outcome; a middle-truncated
                    // label is as wide as the characters that fit, so the gap is 24 plus less than one more character (12-point Inter).
                    var gap = Left(outcome, window) - (Left(label, window) + label.Bounds.Width);
                    Assert.True(gap >= 24 - 0.5 && gap < 24 + 10, $"{words} gap: the Swift keeps at least 24 and the layout has {gap}");
                    Near(words + " trailing padding", 12, Left(row, window) + 400 - (Left(outcome, window) + outcome.Bounds.Width));
                }
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-026

    // :2158-2161 Button(...).buttonStyle(.link).font(.chat(.caption)): the link style draws its label in the view's tint, the
    // theme's accent (OD-0088 / ED-253: "the accent applies wherever the source applies the theme accent ... links"), with
    // the pointing hand over it (SwiftUI's link button style), and the same style and tint in both of its labels.
    [Fact]
    [Trait("Req", "UI-CHAT-026")] // TimelineRows.swift:2156-2162 the link style: the tint, the pointing hand
    public void The_Show_full_output_link_is_drawn_in_the_accent_with_the_pointing_hand_in_both_of_its_states() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        ToolDetailView view = null!;
        AvaloniaWindow window = null!;
        runtime.OnMain(() =>
        {
            view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = Lines(60) });
            window = Show(new StackPanel { Width = 500, Children = { view } });
            var link = Assert.Single(Mounted<Button>(view));
            var title = Assert.Single(Mounted<TextBlock>(link));
            Assert.Equal("Show full output (60 lines)", title.Text);
            Paints(title.Foreground, ChromeTokens.Accent, title, "the link's label is the accent (the tint)");
            Assert.Equal("Hand", link.Cursor?.ToString());
            Click(window, link);
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                var link = Assert.Single(Mounted<Button>(view));
                var title = Assert.Single(Mounted<TextBlock>(link));
                Assert.Equal("Show less", title.Text);
                Paints(title.Foreground, ChromeTokens.Accent, title, "the expanded label is the accent too");
                Assert.Equal("Hand", link.Cursor?.ToString());
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-029

    // ChatBadge.swift:55-61 the attention wave: DotFieldLayerView.Representable(color: Chrome.accentNSColor, ...) along the
    // badge's top quarter, for a plan that is proposed and for no other.
    [Fact]
    [Trait("Req", "UI-CHAT-029")] // TimelineRows.swift:2335; ChatBadge.swift:55-61 the attention wave is Chrome.accentNSColor
    public void The_attention_wave_of_a_proposed_plan_is_drawn_in_the_accent_dots() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var proposed = new PlanCard(PlanEntry(ProposedPlan.State.Proposed), h.Runtime);
            var accepted = new PlanCard(PlanEntry(ProposedPlan.State.Accepted), h.Runtime);
            var panel = new StackPanel { Width = 500, Spacing = 20, Children = { proposed, accepted } };
            var window = Show(panel);
            try
            {
                var wave = Assert.Single(Mounted<DotFieldControl>(proposed));
                Assert.Equal(ChromeTokens.AccentNSColor, Assert.IsType<SwiftColor>(typeof(DotFieldControl).GetField("color", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wave)));
                Assert.Empty(Mounted<DotFieldControl>(accepted));
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-030, UI-CHAT-032

    // :2281-2285 CopyButton(text: plan.markdown) in the header; :2301-2305 Button("Dismiss") with .buttonStyle(.glass), no role:
    // "Not marked destructive".
    [Fact]
    [Trait("Req", "UI-CHAT-030")] // TimelineRows.swift:2281-2285 the copy button copies the plan markdown
    [Trait("Req", "UI-CHAT-032")] // TimelineRows.swift:2301-2305 Dismiss is a plain glass button, not marked destructive
    public void The_plan_panels_copy_button_copies_the_plans_markdown_and_Dismiss_draws_like_any_plain_glass_button() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        const string markdown = "Add the cache, then wire it to the store";
        Control detail = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // warms the lazy event subscription before Show presses its key
            var entry = PlanEntry(ProposedPlan.State.Proposed, markdown);
            detail = PlanDetail(new PlanCard(entry, h.Runtime), PlanOf(entry));
            window = Show(detail);
            window.Clipboard!.SetTextAsync("before").GetAwaiter().GetResult();
            var copy = Assert.Single(Mounted<CopyButton>(detail));
            Assert.Single(Mounted<Button>(copy)).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() != "before"; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Equal(markdown, window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult());
                var actions = Assert.IsType<HStack>(Assert.IsType<StackPanel>(Assert.IsType<Border>(detail).Child).Children[2]);
                var implement = Assert.Single(Mounted<TextBlock>((Control)actions.Children[0]), text => text.Text == "Implement plan");
                var dismiss = Assert.Single(Mounted<TextBlock>((Control)actions.Children[1]), text => text.Text == "Dismiss");
                // No role: Dismiss's label takes whatever the glass button gives every label, never the danger colour.
                Assert.Equal(implement.Foreground, dismiss.Foreground);
                foreach (var brush in Mounted<Control>((Control)actions.Children[1]).OfType<TemplatedControl>().Select(control => control.Foreground).Append(dismiss.Foreground))
                    if (brush is ISolidColorBrush solid) Assert.NotEqual(Resolved(ChromeTokens.Danger, dismiss), solid.Color);
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-033

    // :2372-2376 .animation(.snappy(duration: 0.2), value: steps): checking an item off rewrites the same rows under that
    // animation. The text of the step that changes sees the transaction when its colour is rewritten.
    [Fact]
    [Trait("Req", "UI-CHAT-033")] // TimelineRows.swift:2366-2376 updates animate with .snappy(duration: 0.2)
    public void Checking_a_step_off_rewrites_the_same_rows_under_the_snappy_two_tenths_of_a_second_animation() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        TodoListRow row = null!;
        TextBlock one = null!;
        AvaloniaWindow window = null!;
        TimelineEntry entry = null!;
        var seen = new List<Animation?>();
        h.Main.OnMain(() =>
        {
            entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            window = Show(row);
            one = TextNamed(row, "One");
            one.PropertyChanged += (_, change) => { if (change.Property == TextBlock.ForegroundProperty) seen.Add(Transaction.Current); };
            entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Active)) };
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Same(one, TextNamed(row, "One")); // the same rows, rewritten
                Assert.NotEmpty(seen);
                Assert.All(seen, animation => Assert.Equal(Animation.Snappy(duration: 0.2), animation));
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-035

    // UI/Common/Components.swift:37-40 Duration.seconds(max(0, Int(interval.rounded()))).formatted(.units(allowed: [.seconds,
    // .minutes, .hours], width: .narrow, maximumUnitCount: 2, fractionalPart: .hide(rounded: .down))). SOURCE FACT: the
    // reference video's folded headers read "5s", "10s", "28s", "38s", "1m 23s" and "Failed after 1s" (2-chat-use.mov 9.0 s and
    // 168.0 s, catalogue static.timeline-rows). OD-0098: every other duration is what the formatter the code names gives, and
    // the port's formatter is swift-foundation's own algorithm (SwiftFormatters.DurationUnits, Oracle O-65, whose cases hold the
    // same boundaries: SwiftFormattersSecondTests). ENGINEERING DECISION: the en_US register, the locale the video was made in.
    [Theory]
    [InlineData(TurnStatus.Completed, 5d, "Worked for 5s")]
    [InlineData(TurnStatus.Completed, 10d, "Worked for 10s")]
    [InlineData(TurnStatus.Completed, 28d, "Worked for 28s")]
    [InlineData(TurnStatus.Completed, 38d, "Worked for 38s")]
    [InlineData(TurnStatus.Completed, 83d, "Worked for 1m 23s")]
    [InlineData(TurnStatus.Failed, 1d, "Failed after 1s")]
    [InlineData(TurnStatus.Completed, 60d, "Worked for 1m")]
    [InlineData(TurnStatus.Interrupted, 3599d, "Stopped after 59m 59s")]
    [InlineData(TurnStatus.Completed, 3600d, "Worked for 1h")]
    [InlineData(TurnStatus.Failed, 3725d, "Failed after 1h 2m")]
    [InlineData(TurnStatus.Completed, 3599.5d, "Worked for 1h")] // rounded to whole seconds first: 3600
    [InlineData(TurnStatus.Completed, 0.4d, "Worked for 0s")]
    [Trait("Req", "UI-CHAT-035")] // TimelineRows.swift:2448-2455; Components.swift:37-40; OD-0098
    public void The_turn_duration_reads_the_reference_runtimes_narrow_units_at_every_boundary_the_code_names(TurnStatus status, double seconds, string expected) => headless.OnUiThread(() =>
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        Assert.Equal(expected, TurnEndRow.Label(Summary(Guid.NewGuid()) with { Status = status, Duration = seconds }));
    });

    // ---------------------------------------------------------------- UI-CHAT-037

    // :2742-2747 Image("badge-edit").renderingMode(.template).resizable().frame(width: 18, height: 18): a bundled asset drawn as
    // a template, which Chrome.secondaryText tints.
    [Fact]
    [Trait("Req", "UI-CHAT-037")] // TimelineRows.swift:2744-2748 the badge-edit asset, a template at 18 by 18
    public void The_file_cards_badge_is_the_bundled_badge_edit_asset_drawn_as_a_template_at_18_by_18() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var card = new TurnFileCard(Files(3), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var window = Show(new StackPanel { Children = { card } });
            try
            {
                var badge = Assert.Single(Mounted<SwiftImage>(card), image => image.Width == 18);
                Assert.Equal("badge-edit", badge.AssetName);
                Assert.Null(badge.SymbolName);
                Assert.Equal(ImageRenderingMode.Template, badge.EffectiveRenderingMode);
                Assert.Equal(18, badge.Bounds.Width);
                Assert.Equal(18, badge.Bounds.Height);
                Paints(badge.Foreground, ChromeTokens.SecondaryText, badge, "the template is tinted with Chrome.secondaryText");
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-041, UI-GIT-045

    // :2840-2845 DiffStatLabel in a file row: Text("+n") in Chrome.success and Text("−n") in Chrome.danger (Components.swift:42-51).
    [Fact]
    [Trait("Req", "UI-CHAT-041")] // TimelineRows.swift:2845; Components.swift:43-51 the stat uses Chrome.success and Chrome.danger
    [Trait("Req", "UI-GIT-045")] // TimelineRows.swift:2845 the statistics use the shared success and danger colours
    public void A_file_rows_stat_is_the_plus_in_success_and_the_minus_in_danger() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var card = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>([new("a.swift", 3, 2, false)]), 3, 2), false, () => { }, _ => { });
            var popover = Assert.IsAssignableFrom<Control>(typeof(TurnFileCard).GetMethod("FilesPopover", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, null));
            var window = Show(new StackPanel { Children = { popover } });
            try
            {
                var stat = Assert.Single(Mounted<DiffStatLabel>(popover));
                Paints(TextNamed(stat, "+3").Foreground, ChromeTokens.Success, stat, "additions");
                Paints(TextNamed(stat, "−2").Foreground, ChromeTokens.Danger, stat, "deletions");
                Assert.Empty(Mounted<Button>(popover)); // "none; the rows are not interactive"
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-040, UI-GIT-048

    // :2695-2698 onReview: { [weak reviewRuntime] button in reviewRuntime?.showDiff(on: button, turn: turnID) }: the changes open
    // for this turn on the button itself (ThreadRuntime.showDiff, :383-390: the anchor, the default edge, the selection, the
    // panel shown, a request raised), once the pointer has been on the button so that it has an anchor.
    [Fact]
    [Trait("Req", "UI-CHAT-040")] // TimelineRows.swift:2696-2698, :2800-2816 the click opens this turn's changes on the button
    [Trait("Req", "UI-GIT-048")] // TimelineRows.swift:2696-2698 click opens the changes popover on this button showing this turn's changes
    public void The_review_disc_of_a_finished_turn_asks_the_runtime_for_this_turns_changes_on_the_disc_itself() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var turn = Guid.NewGuid();
        TurnFinishedBlock block = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn) with { Duration = 64, Changes = Files(2) }, ValueList<TimelineEntry>.Empty,
                new ValueList<TimelineEntry>([Reply("```\nanswer\n```")]), null, canUndo: false, chronological: false);
            window = Show(new StackPanel { Margin = new Thickness(20), Children = { block } });
            var review = Mounted<Button>(block).Single(button => Mounted<SwiftImage>(button).Any(image => image.SymbolName == "plusminus"));
            Click(window, review); // never hovered: nothing is asked
            Assert.Null(h.Runtime.DiffSelection);
            Assert.False(h.Runtime.IsDiffVisible);
            Assert.Equal(0, h.Runtime.DiffOpenRequest);
            Hover(window, review);
        });
        h.Main.OnMain(() =>
        {
            Settle(window);
            var review = Mounted<Button>(block).Single(button => Mounted<SwiftImage>(button).Any(image => image.SymbolName == "plusminus"));
            Click(window, review);
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                var review = Mounted<Button>(block).Single(button => Mounted<SwiftImage>(button).Any(image => image.SymbolName == "plusminus"));
                Assert.Equal(turn, h.Runtime.DiffSelection);
                Assert.True(h.Runtime.IsDiffVisible);
                Assert.Equal(1, h.Runtime.DiffOpenRequest);
                Assert.Same(Assert.Single(Mounted<AttachmentAnchorCapture>((Visual)review.Parent!)), h.Runtime.DiffAnchor!.Value);
                Assert.Equal(PreviewPanelEdge.MaxY, h.Runtime.DiffAnchorEdge); // showDiff's default edge: above the button
                Assert.Empty(h.Runtime.DiffFocusEdits); // the whole turn, not one tool's edits
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-055, UI-CHAT-070

    // :2010-2025 statusLine(for:): Working…, Done, Failed or Stopped; then 1 tool or N tools above zero; then the duration once
    // finished; joined with a space, U+00B7 and a space.
    [Theory]
    [InlineData(HydraHeadInfo.Status.Running, 0, -1, "Working…")]
    [InlineData(HydraHeadInfo.Status.Running, 1, -1, "Working… · 1 tool")]
    [InlineData(HydraHeadInfo.Status.Running, 4, -1, "Working… · 4 tools")]
    [InlineData(HydraHeadInfo.Status.Completed, 2, 64, "Done · 2 tools · 1m 4s")]
    [InlineData(HydraHeadInfo.Status.Completed, 0, 9, "Done · 9s")]
    [InlineData(HydraHeadInfo.Status.Failed, 1, 3, "Failed · 1 tool · 3s")]
    [InlineData(HydraHeadInfo.Status.Stopped, 12, 0, "Stopped · 12 tools · 0s")]
    [Trait("Req", "UI-CHAT-055")] // TimelineRows.swift:2010-2025 the status line, joined with a space, U+00B7 and a space
    public void The_head_panels_status_line_names_the_outcome_the_tool_count_and_once_finished_the_duration(HydraHeadInfo.Status status, int tools, int seconds, string expected) => headless.OnUiThread(() =>
    {
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var head = TimelineRowsFinTeam.Head(0, status) with { StartedAt = start, FinishedAt = seconds < 0 ? null : start.AddSeconds(seconds), ToolCalls = tools };
        Assert.Equal(expected, typeof(HydraHeadStepsPopover).GetMethod("StatusLine", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [head]));
    });

    // :2030-2048 steps(in:): every tool call, reply and notice, and any brief after the first; the first user entry is the task
    // itself, shown above; thinking, plans, todo lists and turn ends are the head's own bookkeeping. A blank reply or brief is
    // no step; with none worth a line the panel reads No steps yet. (:1906-1909).
    [Fact]
    [Trait("Req", "UI-CHAT-055")] // TimelineRows.swift:2030-2048 which entries are steps
    [Trait("Req", "UI-CHAT-070")] // TimelineRows.swift:1906-1909 No steps yet. while the head has produced no step worth a line
    public void The_head_panels_steps_are_the_tools_replies_notices_and_later_briefs_and_nothing_else_so_a_fresh_head_reads_No_steps_yet() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var task = UserEntry("the task itself", null);
            var tool = ToolEntry(Call(ToolCall.Kind.Read, "Read the popover"));
            var reply = Reply("what the head said");
            var blank = Reply("   ");
            var later = UserEntry("steer it on", null);
            var blankBrief = UserEntry("  \n ", null);
            var notice = NoticeEntry(Notice.Level.Warning, "careful");
            var plan = PlanEntry(ProposedPlan.State.Proposed);
            var todos = new TimelineEntry(new TimelineItem(null, Steps(("one", TodoStep.Status.Active))));
            var steps = Assert.IsAssignableFrom<IEnumerable<TimelineEntry>>(typeof(HydraHeadStepsPopover).GetMethod("Steps", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [new[] { task, tool, blank, reply, plan, todos, blankBrief, later, notice }]));
            Assert.Equal([tool.Id, reply.Id, later.Id, notice.Id], steps.Select(entry => entry.Id));
            var none = Assert.IsAssignableFrom<IEnumerable<TimelineEntry>>(typeof(HydraHeadStepsPopover).GetMethod("Steps", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [new[] { task, blank, plan, todos }]));
            Assert.Empty(none);
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-057

    // :1721-1735, :1746-1756 (the row): an MCP call is "the same as an ordinary tool row": a call with output of its own
    // unfolds in place, and one with nothing to show is a line of text, whatever the badge it wears.
    [Fact]
    [Trait("Req", "UI-CHAT-057")] // TimelineRows.swift:1672-1700 an MCP badge row expands its output like any tool row, or is a line of text
    public void An_MCP_badge_row_with_output_unfolds_it_in_place_and_one_with_nothing_to_show_is_a_line_of_text() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        ToolRow withOutput = null!, bare = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            withOutput = ToolRowFor(h, Call(ToolCall.Kind.Mcp, "Context7 · resolve-library-id") with { Output = "a\nb" });
            bare = ToolRowFor(h, Call(ToolCall.Kind.Mcp, "Context7 · resolve-library-id"));
            window = Show(new StackPanel { Width = 600, Spacing = 20, Children = { withOutput, bare } });
            Assert.Equal("Context7: resolve-library-id", Assert.Single(Mounted<MiddleTruncatingTextBlock>(withOutput)).FullText);
            Assert.Empty(Mounted<ToolDetailView>(withOutput));
            Assert.Empty(Mounted<Button>(bare));
            var button = Assert.Single(Mounted<Button>(withOutput));
            Assert.Equal("Show the output", ToolTip.GetTip(button));
            Click(window, button);
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Single(Mounted<ToolDetailView>(withOutput));
                Assert.Equal("Hide the output", ToolTip.GetTip(Assert.Single(Mounted<Button>(withOutput))));
                Assert.Empty(Mounted<ToolDetailView>(bare));
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-061

    // :1546-1553 MarkdownView(...) with no .textSelection of its own: the markdown's blocks enable it (MarkdownBlockSelection),
    // so the narration's words select, with the I-beam over them (ED-256 (5)).
    [Fact]
    [Trait("Req", "UI-CHAT-061")] // TimelineRows.swift:1546-1553 the narration enables text selection
    public void The_narration_between_steps_selects_like_any_markdown_text() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var steps = new WorkSteps(new ValueList<TimelineEntry>([
                ToolEntry(Call(ToolCall.Kind.Read, "Read a")), Reply("now the build"), ToolEntry(Call(ToolCall.Kind.Command, "make"))]), h.Runtime, Binding<bool>.Constant(true));
            var window = Show(steps);
            try
            {
                var narration = Assert.Single(Mounted<MarkdownView>(steps));
                // A markdown paragraph is a prose run (ProseRunView's ParagraphTextPresenter), the text the replies select in.
                var prose = Assert.Single(Mounted<ParagraphTextPresenter>(narration));
                prose.SelectAll();
                Assert.Equal("now the build", prose.SelectedText);
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-052

    // :411-415 ForEach(finished, id: \.index) { FinishedHeadPill(head: head).transition(.settleGlide) } under
    // .animation(.smooth(duration: 0.3), value: finished.map(\.index)): a head that finishes while its batch still works joins the
    // column, in the order of the list, in that animation; the pills already there stay the same views. (The settle glide's
    // leaving half is in TimelineRowsFinHeadRowsTests; its soft arrival is not claimed here: see the notes of unit
    // blitz-timelinerows-cd, SoftAppearModifier takes its identity at once under ED-098 (2).)
    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-CHAT-052 (a head joins in order under .smooth(duration: 0.3)): TimelineRows.swift:411-415
    public void A_finished_head_joins_the_column_in_list_order_under_the_smooth_animation_and_the_pills_already_there_stay() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        var (store, app, runtime) = TimelineRowsFinTeam.Make(
            TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Failed), TimelineRowsFinTeam.Head(1, HydraHeadInfo.Status.Completed));
        using (store)
        {
            HydraHeadsWorkingRow row = null!;
            AvaloniaWindow window = null!;
            FinishedHeadPill walter = null!;
            var seen = new List<(int Index, Animation? Animation)>();
            store.Main.OnMain(() =>
            {
                row = new HydraHeadsWorkingRow(new ValueList<int>([1, 0]), runtime);
                var host = new Border { Width = 500, Child = row };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                window = Show(new StackPanel { Children = { host } });
                row.ApplyTeam(app.Children(runtime.ThreadID).ToList());
                Settle(window);
                Assert.Equal(["Walter finished working", "Hank failed"], Mounted<FinishedHeadPill>(row).Select(pill => AutomationProperties.GetName(pill)));
                walter = Mounted<FinishedHeadPill>(row).First();
                row.ApplyTeam(app.Children(runtime.ThreadID).Where(thread => thread.Hydra?.Index == 1).ToList()); // Hank leaves
                Settle(window);
            });
            frames.Advance(TimeSpan.Zero);
            store.Main.AdvanceMilliseconds(3000);
            frames.Advance(TimeSpan.FromMilliseconds(3000));
            store.Main.OnMain(() =>
            {
                try
                {
                    Settle(window);
                    Assert.Equal(["Walter finished working"], Mounted<FinishedHeadPill>(row).Select(pill => AutomationProperties.GetName(pill)));
                    var column = Assert.IsType<StackPanel>(Field(row, "headsColumn"));
                    column.Children.CollectionChanged += (_, change) =>
                    {
                        if (change.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) seen.Add((change.NewStartingIndex, Transaction.Current));
                    };
                    row.ApplyTeam(app.Children(runtime.ThreadID).ToList()); // Hank is back, listed second
                    Settle(window);
                    Assert.Equal(["Walter finished working", "Hank failed"], Mounted<FinishedHeadPill>(row).Select(pill => AutomationProperties.GetName(pill)));
                    Assert.Same(walter, Mounted<FinishedHeadPill>(row).First());
                    Assert.Equal([1], seen.Select(entry => entry.Index));
                    Assert.All(seen, entry => Assert.Equal(Animation.Smooth(duration: 0.3), entry.Animation));
                }
                finally { Close(window); }
            });
        }
    });

    // ---------------------------------------------------------------- UI-CHAT-048

    // :241-300 HydraReportRow: the details are the body, or the body past its address line when the note leads with one; the
    // pill is disabled with no details; the popover is HydraMergePopover for a note whose address parses as a merge request and
    // whose details parse as a merge note, else HydraReportPopover(text: body) (:293-300), the whole body, address included.
    // R-TEST-10 / ED-256 (10): every test that shows link text takes the NoLinkNetwork rig.
    [Theory]
    [InlineData("Hydra merged !7.\nhttps://gitlab.com/g/p/-/merge_requests/7\n1 file (+2 −1) landed on `main` from `head`.", "merge")]
    [InlineData("Hydra opened !7 but could not merge it.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThe pipeline failed.", "report")]
    [InlineData("Held the heads back.\nThe lead is busy.", "report")]
    [InlineData("Hydra merged !7.\nhttps://gitlab.com/g/p/-/merge_requests/7", "none")]
    [Trait("Req", "UI-CHAT-048")] // TimelineRows.swift:241-300 which popover a Hydra note's pill opens, and when it opens none
    public void A_notes_pill_opens_the_merge_card_for_a_merge_request_the_report_for_any_other_note_and_nothing_without_details(string text, string expected) => headless.OnUiThread(() =>
    {
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraReportRow row = null!;
        AvaloniaWindow window = null!;
        runtime.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // warms the lazy event subscription before Show presses its key
            row = new HydraReportRow(new UserMessage { Text = text, HydraHeads = null });
            window = Show(new StackPanel { Children = { row } });
            var pill = Mounted<Button>(row).First(button => !Texts(button).Contains("Open"));
            if (expected == "none") { Assert.False(pill.IsEnabled); return; }
            Assert.True(pill.IsEnabled);
            Click(window, pill);
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                if (expected == "none") { Assert.DoesNotContain(presenter.Calls, call => call.Presented); return; }
                var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
                var content = Assert.IsAssignableFrom<Control>(PopoverHost.Content(popup)); // the site's view, in its badge card (OD-0095)
                if (expected == "merge") { Assert.Single(LogicalQuery.SelfAndDescendants<HydraMergePopover>(content)); Assert.Empty(LogicalQuery.SelfAndDescendants<HydraReportPopover>(content)); return; }
                var report = Assert.Single(LogicalQuery.SelfAndDescendants<HydraReportPopover>(content));
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraMergePopover>(content));
                var body = text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..].Trim();
                Assert.True(MarkdownView.Blocks(body).SequenceEqual(LogicalQuery.SelfAndDescendants<MarkdownBlockView>(report).Select(block => block.Block)),
                    "the report is not the note's whole body past its title");
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-028

    // :2135 DiffLinesView(file:, showsLineNumbers: false) under DiffInspector.swift:407 .textSelection(.enabled): the diff's
    // lines are selectable text, here as in the inspector.
    [Fact]
    [Trait("Req", "UI-CHAT-028")] // TimelineRows.swift:2135; DiffInspector.swift:407 text selection within the diff view
    public void The_diff_lines_of_an_edit_block_are_selectable_text() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() =>
        {
            var view = new ToolDetailView(Call(ToolCall.Kind.Edit, "e") with { Edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-old line\n+new line\n", 1, 1)]) });
            var lines = Assert.Single(LogicalQuery.SelfAndDescendants<DiffLinesView>(view));
            var line = Assert.Single(LogicalQuery.SelfAndDescendants<SelectableTextBlock>(lines), text => (text.Text ?? "").Contains("new line", StringComparison.Ordinal));
            line.SelectAll();
            Assert.Equal(line.Text, line.SelectedText);
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-059

    // :2640-2657 the steps card's switch: an assistant row, a work group, a plan, a checklist and a notice each draw; "case .user,
    // .reasoning, .turnEnd: EmptyView()". Entries of those kinds that reach the card draw nothing.
    [Fact]
    [Trait("Req", "UI-CHAT-059")] // TimelineRows.swift:2640-2657 user, reasoning and turn-end entries render nothing in the steps card
    public void The_steps_card_draws_no_row_for_a_stray_user_reasoning_or_turn_end_entry() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var content = new ValueList<TimelineEntry>(
            [
                UserEntry("stray user words", turn),
                new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "stray thoughts" }))),
                ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn),
                new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.TurnEnd(Summary(turn) with { Duration = 64 }))),
                PlanEntry(ProposedPlan.State.Accepted, turn: turn),
                Reply("```\nthe answer\n```"),
            ]);
            var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn) with { Duration = 64 }, ValueList<TimelineEntry>.Empty, content, null, canUndo: false, chronological: true);
            var window = Show(new StackPanel { Children = { block } });
            try
            {
                var card = Assert.Single(Mounted<Border>(block), border => border.Padding == new Thickness(14));
                Assert.Single(Mounted<WorkGroup>(card));
                Assert.Single(Mounted<PlanCard>(card));
                Assert.Single(Mounted<AssistantMessageRow>(card));
                Assert.Empty(Mounted<UserMessageRow>(card));
                Assert.Empty(Mounted<TurnEndRow>(card));
                var words = string.Join("|", Texts(card));
                Assert.DoesNotContain("stray", words, StringComparison.Ordinal);
                Assert.DoesNotContain("Worked for", words, StringComparison.Ordinal); // the header above the card carries it, not a row inside
            }
            finally { Close(window); }
        });
    });

    // ---------------------------------------------------------------- UI-CHAT-055, UI-CHAT-070

    // :1877-1893, :1906-1909 the panel reads the head's own runtime straight from the model, so "the list grows while the head still
    // works": a head with no step yet reads No steps yet. in the Steps section, and the line is gone once a tool call arrives, which
    // shows as a step in the icon column.
    [Fact]
    [Trait("Req", "UI-CHAT-055")] // TimelineRows.swift:1884-1931 the panel grows while the head still works; the Report section is appended once finished
    [Trait("Req", "UI-CHAT-070")] // TimelineRows.swift:1906-1909 No steps yet. is shown while the head has produced no step and goes with the first
    public void The_head_panel_grows_with_the_heads_own_runtime_and_No_steps_yet_goes_with_its_first_step() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var (store, app, _) = TimelineRowsFinTeam.Make(TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Running) with { Task = "Tidy the popover" });
        using (store)
        {
            ThreadRuntime head = null!;
            HydraHeadStepsPopover view = null!;
            AvaloniaWindow window = null!;
            store.Main.OnMain(() =>
            {
                var id = app.Threads.Single(thread => thread.IsHydraHead).Id;
                head = app.Runtime(id);
                view = new HydraHeadStepsPopover(id);
                var host = new Border { Child = view };
                Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                window = Show(host);
                var empty = TextNamed(view, "No steps yet.");
                Assert.Same(empty.Parent, TextNamed(view, "Steps").Parent); // in the Steps section
                head.Rehearse(new ProviderEvent.ToolStarted("head-read", Call(ToolCall.Kind.Read, "Read head", ToolCall.Status.Running)));
            });
            store.Main.OnMain(() =>
            {
                try
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Settle(window);
                    Assert.DoesNotContain("No steps yet.", Texts(view));
                    Assert.Contains("Reading head", Texts(view));
                }
                finally { Close(window); }
            });
        }
    });
}
