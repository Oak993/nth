using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :926-1162 the merge pill once the note has landed:
// its title button, the Open link beside it and the report popover; :607-640 the generic report.
[Collection("Headless")]
public sealed class TimelineRowsFinMergeOutcomeTests(HeadlessSession headless)
{
    private static readonly string[] Titles =
    [
        "Hydra merged !95", "Hydra did not merge: no remote", "Hydra did not merge: a rebase or merge is underway",
        "Hydra did not merge: conflict markers in a file", "Hydra did not merge: the checkout moved while it worked",
        "Hydra did not merge: the team's files are outside every project", "Hydra left a file outside every project",
        "Hydra pushed feature/x but could not open a merge request", "Hydra opened !95 but could not merge it",
        "Hydra could not merge the team's work", "Hydra is waiting for another chat's files", "Hydra has no files left to merge",
    ];

    private static UserMessage Note(string text) => new() { Text = text };

    // The pill draws a still copy of its contents at opacity 0 to size its slot (:978-987); what a reader sees is the rest.
    private static IEnumerable<T> Live<T>(Control row) where T : Control =>
        Mounted<T>(row).Where(control => !control.GetVisualAncestors().OfType<Control>().Prepend(control).Any(ancestor => ancestor.Opacity == 0));

    private static double Top(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.Y;

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-005")] // TimelineRows.swift:1099-1120, :1142-1155, :802-808
    public void An_outcome_title_is_the_notes_first_line_less_one_stop_in_callout_medium_with_a_chevron_when_there_is_a_report() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.OnMain(() =>
        {
            var rows = Titles.Select(title => new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note(title + ".\nWhat the merge did, in detail.")))).ToList();
            var bare = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Hydra left a file outside every project.")));
            var twoStops = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Hydra stopped..\nmore")));
            var panel = new StackPanel { Spacing = 10 };
            foreach (var row in rows) panel.Children.Add(row);
            panel.Children.Add(bare);
            panel.Children.Add(twoStops);
            var window = Show(panel);
            try
            {
                for (var i = 0; i < Titles.Length; i++)
                {
                    // :802-808 the title is the first line with exactly one trailing full stop removed.
                    Assert.Contains(Titles[i], Texts(rows[i]));
                    Assert.Equal(Titles[i], AutomationProperties.GetName(Mounted<Border>(rows[i]).First(border => AutomationProperties.GetName(border) is not null)));
                }
                Assert.Contains("Hydra stopped.", Texts(twoStops)); // only one stop goes
                var row0 = rows[0];
                var title = Assert.Single(Live<TextBlock>(row0), text => text.Text == Titles[0]);
                // :1144-1148 callout medium, Chrome.primaryText at 0.9, one line, tail-truncated.
                Face(title, 12, FontWeight.Medium, ".chat(.callout, weight: .medium)");
                Paints(title.Foreground, ChromeTokens.PrimaryText.Opacity(0.9), title, "title");
                Assert.Equal(1, title.MaxLines);
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
                // :1149-1153 the chevron (caption2 semibold, Chrome.secondaryText) only when there is a report behind the title.
                var chevron = Assert.Single(Live<SwiftImage>(row0), image => image.SymbolName == "chevron.right");
                var font = SwiftImage.GetFont(chevron)!.Resolve();
                Assert.Equal(10, font.FontSize);
                Assert.Equal(FontWeight.SemiBold, font.FontWeight);
                Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "chevron");
                // :1143 HStack(spacing: 8) between the title and the chevron, on one centre line.
                Near("nMergeOutcome:1", 8, Left(chevron, window) - (Left(title, window) + title.Bounds.Width));
                OnOneCentreLine(window, title, chevron);
                // :1109-1110 enabled with the tooltip "Show the message".
                var button = Assert.Single(Mounted<Button>(row0));
                Assert.True(button.IsEnabled);
                Assert.Equal("Show the message", ToolTip.GetTip(button));
                // Nothing behind it: the button is disabled, its tooltip is empty and there is no chevron.
                var bareButton = Assert.Single(Mounted<Button>(bare));
                Assert.False(bareButton.IsEnabled);
                Assert.Equal("", ToolTip.GetTip(bareButton));
                Assert.DoesNotContain(Live<SwiftImage>(bare), image => image.SymbolName == "chevron.right");
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-006")] // TimelineRows.swift:1121-1126, :1157-1161 the Open link, :376-381 the leading address
    public void The_Open_link_is_a_caption_semibold_sibling_8_points_after_the_title_button_only_for_a_leading_address() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var opened = new List<string>();
        using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
        h.Main.OnMain(() =>
        {
            var withLink = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Hydra merged !7.\nhttps://gitlab.com/g/p/-/merge_requests/7\n1 file (+2 −1) landed on `main` from `head`.")));
            var noLink = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Hydra merged !7.\n1 file (+2 −1) landed on `main` from `head`.")));
            var midLine = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Hydra merged !7.\nsee https://gitlab.com/g/p/-/merge_requests/7")));
            var panel = new StackPanel { Spacing = 20 };
            foreach (var row in new Control[] { withLink, noLink, midLine }) panel.Children.Add(row);
            var window = Show(panel);
            try
            {
                var buttons = Mounted<Button>(withLink).ToList();
                Assert.Equal(2, buttons.Count);
                var open = buttons.Single(button => Texts(button).Contains("Open"));
                var title = buttons.Single(button => !ReferenceEquals(button, open));
                // It stays a sibling of the title button, never inside its label.
                Assert.DoesNotContain(open, Mounted<Button>(title));
                Assert.Equal("Open in the browser", ToolTip.GetTip(open));
                var word = TextNamed(open, "Open");
                Face(word, 10, FontWeight.SemiBold, ".chat(.caption, weight: .semibold)");
                Paints(word.Foreground, ChromeTokens.SecondaryText, word, "Open");
                // :1100 HStack(spacing: 8): 8 points after the title button, on its centre line.
                Near("nMergeOutcome:2", 8, Left(open, window) - (Left(title, window) + title.Bounds.Width));
                OnOneCentreLine(window, title, word);
                // Only a body that leads with a bare http(s) address on its own line gets the link.
                Assert.Single(Mounted<Button>(noLink));
                Assert.Single(Mounted<Button>(midLine));
                Click(window, open);
                Assert.Equal(["https://gitlab.com/g/p/-/merge_requests/7"], opened);
            }
            finally { Close(window); }
        });
    });

    [Theory]
    [InlineData("https://gitlab.com/g/p/-/merge_requests/7\nNo files line.", false)]
    [InlineData("https://gitlab.com/g/p/-/merge_requests/7\n1 file (+2 −1) landed on `main` from `head`.", true)]
    [InlineData("1 file (+2 −1) landed on `main` from `head`.", false)]
    [InlineData("LONG", false)]
    [InlineData("https://gitlab.com/g/p/-/merge_requests/7\nThe pipeline failed: see [the lint job](https://gitlab.com/g/p/-/jobs/12).", false)]
    [Trait("Req", "PR-HYDRAMERGE-090")] // TimelineRows.swift:1111-1120 the merge card only when all three conditions hold
    [Trait("Req", "UI-HYDRAMERGE-007")] // TimelineRows.swift:607-640 the generic report popover
    [Trait("Req", "UI-CHAT-050")] // TimelineRows.swift:607-639 the report panel
    public void The_report_opens_the_merge_card_for_a_leading_address_that_parses_with_a_files_line_and_the_460_point_report_otherwise(string body, bool merge) => headless.OnUiThread(() =>
    {
        // No network (R-TEST-10; ED-256 (10)). The merge card and the merge-failed row's link paragraph both ask
        // gitlab.com for its favicon, and a report is measured in a hidden window first (PopoverScroll.MeasuredHeight: attached,
        // detached, its ask cancelled), so the host is named here for every row: the rig fails the test if anything else is asked.
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraMergeRow row = null!;
        AvaloniaWindow window = null!;
        if (body == "LONG") body = string.Join("\n\n", Enumerable.Range(1, 80).Select(line => "Paragraph " + line + " of a long report."));
        h.Main.OnMain(() =>
        {
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Hydra opened !7 but could not merge it.\n" + body)));
            window = Show(new StackPanel { Children = { row } });
            Click(window, Assert.Single(Mounted<Button>(row), button => ToolTip.GetTip(button) as string == "Show the message"));
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
                // :1111 .popover(isPresented:, arrowEdge: .bottom): the popover hangs from the bottom edge.
                Assert.Equal(PlacementMode.Bottom, popup.Placement);
                var opened = Assert.IsAssignableFrom<System.Delegate>(typeof(Popup).GetField("Opened", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(popup));
                opened.DynamicInvoke(popup, EventArgs.Empty);
                var content = Assert.IsAssignableFrom<Control>(PopoverHost.Content(popup)); // the site's view, not the card the presenter hangs it on (OD-0095)
                Assert.True(Popovers.GetIsCapsuleButtonBorderShape(content)); // :1115, :1118 presentedChrome()
                if (merge)
                {
                    Assert.IsType<HydraMergePopover>(content);
                    return;
                }
                // :607-640 the generic report: 460 wide, at most 460 tall, 12-point callout markdown, 16/14 padding, blocks 10 apart.
                var report = Assert.IsType<HydraReportPopover>(content);
                Assert.Equal(460, report.Width);
                var scroll = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverScroll>(report));
                Assert.True(Assert.IsType<ScrollViewer>(scroll.Child).Height <= 460);
                if (body.StartsWith("Paragraph", StringComparison.Ordinal)) Assert.Equal(460, Assert.IsType<ScrollViewer>(scroll.Child).Height);
                var blocks = LogicalQuery.SelfAndDescendants<MarkdownBlockView>(report).ToList();
                Assert.NotEmpty(blocks);
                // :1117 the whole body past the title, a leading address and all, not only what follows the address.
                Assert.True(MarkdownView.Blocks(body).SequenceEqual(blocks.Select(block => block.Block)), "the report is not the note's body");
                var stack = Assert.IsType<StackPanel>(blocks[0].Parent);
                Assert.Equal(10, stack.Spacing);
                Assert.Equal(12, MarkdownView.GetMarkdownPointSize(blocks[0]));
                Assert.False(MarkdownView.GetMarkdownDimmed(blocks[0]));
                Assert.Equal(12, Avalonia.Controls.Documents.TextElement.GetFontSize(stack));
                // What those mean on screen (review round 5): every text of the report is drawn at 12 points, the callout text style
                // (:630) and the markdown point size (:631) alike. A paragraph with a link (the markdown link of the last row; the bare
                // https address line is veiled text in this port's parser, not asserted here) is InlineText's link branch, LinkParagraphView(pointSize: scaled)
                // (MarkdownView.swift:1094, :1114 at B3), drawn at the markdown point size, which the report sets on blocks already built
                // (InlineText runs body again: MarkdownView.cs InlineText).
                var drawn = LogicalQuery.SelfAndDescendants<SelectableTextBlock>(report).ToArray();
                Assert.NotEmpty(drawn);
                if (body.Contains("](", StringComparison.Ordinal)) Assert.NotEmpty(LogicalQuery.SelfAndDescendants<LinkParagraphView>(report));
                foreach (var text in drawn)
                    Assert.True(DrawnProseSizes(text).SequenceEqual([12.0]),
                        $"the report's \"{text.Inlines?.Text ?? text.Text}\" is drawn at {string.Join(", ", DrawnProseSizes(text))} points");
                Assert.Equal(new Thickness(16, 14), Assert.IsAssignableFrom<Decorator>(stack.Parent).Padding);
                Assert.True(MarkdownView.GetMarkdownBlockSelection(stack)); // :633 textSelection(.enabled)
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-089")] // TimelineRows.swift:937-952 the merging pill vanishes when the flag goes down without a note
    [Trait("Req", "UI-HYDRAMERGE-002")] // TimelineRows.swift:951 animated with Chrome.glideSpring (response 0.6, damping 0.86)
    public void The_merging_pill_goes_when_the_flag_comes_down_with_no_note_under_the_glide_spring_and_an_outcome_pill_stays() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraMergeRow merging = null!, outcome = null!;
        AvaloniaWindow window = null!;
        Assert.Equal(0.6, ChromeTokens.GlideSpring.Response);
        Assert.Equal(0.86, ChromeTokens.GlideSpring.DampingRatio);
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStage = "Pushing the branch";
            merging = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
            outcome = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Hydra merge held.\nwhy")));
            var panel = new StackPanel { Spacing = 20 };
            panel.Children.Add(merging);
            panel.Children.Add(outcome);
            window = Show(panel);
            Assert.NotEmpty(Mounted<HydraMergeTrack>(merging));
            h.Runtime.IsHydraMerging = false; // no outcome note takes the pill's place
        });
        // :951 the removal runs under the glide spring: still on screen a moment later, gone once it has settled.
        frames.Advance(TimeSpan.Zero);
        h.Main.AdvanceMilliseconds(80);
        frames.Advance(TimeSpan.FromMilliseconds(80));
        h.Main.OnMain(() => Assert.NotEmpty(Mounted<HydraMergeTrack>(merging)));
        h.Main.AdvanceMilliseconds(3000);
        frames.Advance(TimeSpan.FromMilliseconds(3000));
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Empty(Mounted<HydraMergeTrack>(merging));
                Assert.Empty(Texts(merging));
                // :939-944 an outcome pill is shown whatever the flag says.
                Assert.NotEmpty(Mounted<HydraMergeTrack>(outcome));
                Assert.Contains("Hydra merge held", Texts(outcome));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-108")] // TimelineRows.swift:1028-1033 the report is parsed off the main thread as the pill appears
    public void The_report_body_is_warmed_as_the_outcome_pill_appears_keyed_to_the_body_and_a_pill_with_no_details_warms_nothing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var nonce = Guid.NewGuid().ToString("N");
        var body = "```\nfirst " + nonce + "\n```";
        var nextBody = "```\nnext " + nonce + "\n```";
        var linkOnly = "```\nlink only " + nonce + "\n```";
        HydraMergeRow row = null!;
        AvaloniaWindow window = null!;
        var panel = new StackPanel();
        h.Main.OnMain(() =>
        {
            // Nothing is cached yet.
            Assert.Equal([body, nextBody, linkOnly], MarkdownView.WarmTexts([body, nextBody, linkOnly]));
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Held back.\n" + body)));
            panel.Children.Add(row);
            window = Show(panel);
        });
        h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([body]).Count == 0, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            // The body is cached; the next one is not until the pill's body changes.
            Assert.Equal([nextBody], MarkdownView.WarmTexts([nextBody]));
            row.Update(new HydraMergePhase.Outcome(Note("Held back again.\n" + nextBody)));
        });
        h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([nextBody]).Count == 0, AppModelHarness.Bound);
        var address = "https://gitlab.com/g/p/-/merge_requests/9";
        var control = "```\ncontrol " + nonce + "\n```";
        HydraMergeRow other = null!;
        h.Main.OnMain(() =>
        {
            // A pill whose body is only its address has no details: nothing is warmed. A second pill queued after it, with details,
            // is the control: once that one is warm, the first has had its turn.
            row.Update(new HydraMergePhase.Outcome(Note("Merged.\n" + address)));
            other = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(Note("Held back.\n" + control)));
            panel.Children.Add(other);
        });
        h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([control]).Count == 0, AppModelHarness.Bound);
        h.Main.RunUntilIdle();
        h.Main.OnMain(() =>
        {
            Assert.Equal([address], MarkdownView.WarmTexts([address]));
            // With an address and details, what is warmed is the whole body, the address line included: that is what the popover shows.
            row.Update(new HydraMergePhase.Outcome(Note("Merged.\n" + address + "\n" + linkOnly)));
        });
        h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([address + "\n" + linkOnly]).Count == 0, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.Equal([linkOnly], MarkdownView.WarmTexts([linkOnly]));
            Close(window);
        });
    });
}
