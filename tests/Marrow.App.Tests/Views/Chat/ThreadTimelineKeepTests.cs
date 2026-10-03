using Avalonia;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineMount;

namespace Marrow.App.Tests.Views.Chat;

// What a view whose id stands keeps when the block's value or context changes under it (DroppyCode/App/Views/Chat/ThreadTimeline.swift, B3 52bc792: :332-334 `.id(block.id)` on an
// `.equatable()` row, :1896-1936 DisplayBlockView's body, :1980-1989 the turn's ForEach and its `if let heads`; TimelineRows.swift:1569-1576 HydraHeadsList's ForEach(entries),
// :2499-2509 TurnFinishedBlock's @State). Every kind of block is handed its new value in place; only a block whose branch changed is a different view. The fixer's tests of the
// first adversarial round (ThreadTimelineReviewTests) cover the finished turn's fold, the pills' row and the running turn's heads; these cover what else a pass hands again.
// ED-NEW-parity-threadtimeline (3) (the placeholder the lead numbers at the merge).
[Collection("Headless")]
public sealed class ThreadTimelineKeepTests(HeadlessSession headless)
{
    private static readonly Guid TurnA = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid TurnB = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    private static void Inputs(TimelineMount mount, string? project, bool supportsRewind) =>
        typeof(ThreadTimeline).GetMethod("UpdateInputs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(mount.View, [project, null, supportsRewind, 480.0, true]);

    private static void SetTurns(TimelineMount mount, params TurnRecord[] turns) =>
        typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Turns))!.SetValue(mount.Runtime, new ValueList<TurnRecord>(turns));

    private static TimelineEntry EndWith(string id, Guid turn, double duration, int files) => Entry(id, new TimelineItem.Content.TurnEnd(new TurnSummary
    {
        TurnID = turn, Status = TurnStatus.Completed, Duration = duration, FilesChanged = files, Additions = 40, Deletions = 7,
        Changes = files == 0 ? null : new FileChangeSummary(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, files).Select(i => new FileChangeSummary.File("src/file" + i + ".cs", i, 1, false))), 40, 7),
    }), turn);

    private static bool Offers(Control row, string tip) => LogicalQuery.SelfAndDescendants<Button>(row).Any(button => ToolTip.GetTip(button) as string == tip);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-035 and PR-CHAT-155 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:167-172,332-334; TimelineRows.swift:2599-2602 ForEach(userEntries), :2688-2699 the file card's `canUndo`: the rewind flag flips at every turn start and end, and the rows it reaches are the same views
    public void A_finished_turns_message_and_file_card_stay_the_same_views_while_the_rewind_flag_flips_and_show_what_it_decides() => ThreadTimelineRig.Run(headless, mount =>
    {
        Inputs(mount, "Project", true);
        SetTurns(mount, new TurnRecord { Id = TurnA, Index = 0, Status = TurnStatus.Completed });
        var entries = new List<TimelineEntry> { User("p1", TurnA, "first question"), Reply("final", TurnA, "Done."), EndWith("e1", TurnA, 2, 2) };
        mount.SetEntries(entries); mount.Settle();
        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        var user = Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(folded.Row)); var card = Assert.Single(LogicalQuery.SelfAndDescendants<TurnFileCard>(folded.Row));
        Assert.True(Offers(user, "Edit message")); Assert.True(Offers(card, "Revert this turn's files and conversation")); // nothing runs: the turn may be reverted
        // The reader sends the next message: while a turn runs no row offers to revert.
        SetTurns(mount, new TurnRecord { Id = TurnA, Index = 0, Status = TurnStatus.Completed }, new TurnRecord { Id = TurnB, Index = 1 });
        entries.Add(User("p2", TurnB, "next question")); mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Advance(600);
        var now = mount.Blocks().Single(block => block.Id == folded.Id);
        Assert.Same(folded.Row, now.Row);
        Assert.Same(user, Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(now.Row))); Assert.Same(card, Assert.Single(LogicalQuery.SelfAndDescendants<TurnFileCard>(now.Row)));
        Assert.False(Offers(user, "Edit message"), "the message offers to edit while a turn runs"); Assert.False(Offers(card, "Revert this turn's files and conversation"), "the card offers to revert while a turn runs");
        // The turn ends: they may again.
        entries.Add(Reply("r2", TurnB, "answer")); entries.Add(EndWith("e2", TurnB, 3, 0)); mount.SetEntries(entries); mount.SetPhase(RuntimePhase.Idle); mount.Advance(600);
        Assert.Same(folded.Row, mount.Blocks().Single(block => block.Id == folded.Id).Row);
        Assert.Same(user, Assert.Single(LogicalQuery.SelfAndDescendants<UserMessageRow>(folded.Row))); Assert.Same(card, Assert.Single(LogicalQuery.SelfAndDescendants<TurnFileCard>(folded.Row)));
        Assert.True(Offers(user, "Edit message")); Assert.True(Offers(card, "Revert this turn's files and conversation"));
    });

    // A real right click on the row (RightClickCatcher's tunnel handler) and the titles of the menu it presents.
    private static string[] MenuTitles(RecordingPresenter presenter, Avalonia.Controls.Window window, Control on)
    {
        var point = on.TranslatePoint(new Avalonia.Point(4, 4), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        Avalonia.Headless.HeadlessWindowExtensions.MouseDown(window, point, Avalonia.Input.MouseButton.Right, Avalonia.Input.RawInputModifiers.None);
        Avalonia.Headless.HeadlessWindowExtensions.MouseUp(window, point, Avalonia.Input.MouseButton.Right, Avalonia.Input.RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var popup = presenter.Calls.Last(call => call.Presented).Popup;
        var items = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Chrome.RowActionItems>(popup.Child!));
        var titles = LogicalQuery.SelfAndDescendants<Marrow.UI.Chrome.PopoverItem>(items).Select(item => LogicalQuery.SelfAndDescendants<Marrow.UI.Chrome.MiddleTruncatingText>(item).First().FullText).ToArray();
        popup.IsOpen = false; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return titles;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-035 (ED-NEW-parity-threadtimeline (3)): TimelineRows.swift:131 `if canRevert` (the pencil) and :168-178 the menu's `canRevert`, the only two places of the body the flag reaches; the bubble (:118-129) stands whatever it says
    public void A_message_rows_menu_offers_to_edit_from_here_as_its_rewind_flag_is_when_the_menu_opens_and_its_bubble_stays_the_same_view() => headless.OnUiThread(() =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var h = new ThreadRuntimePart12Harness();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        h.Main.OnMain(() =>
        {
            var row = new UserMessageRow(new TimelineEntry(new TimelineItem(TurnA, new TimelineItem.Content.User(new UserMessage { Text = "hello there" }), "p1")), h.Runtime, canRevert: true);
            var window = new Avalonia.Controls.Window { Content = new StackPanel { Children = { row } }, Width = 800, Height = 600 };
            window.Show(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            try
            {
                var bubble = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.InlineText>(row));
                Assert.Equal(["Copy message", "Copy as markdown", "Edit from here"], MenuTitles(presenter, window, bubble)); Assert.True(Offers(row, "Edit message"));
                // A turn starts: the row is handed false. The menu opened now has no entry to edit from here, the pencil is gone, and the bubble is the one that was there.
                row.SetCanRevert(false); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Same(bubble, Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.InlineText>(row)));
                Assert.Equal(["Copy message", "Copy as markdown"], MenuTitles(presenter, window, bubble)); Assert.False(Offers(row, "Edit message"));
                // The turn ends: true again, and the menu and the pencil offer it again.
                row.SetCanRevert(true); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Same(bubble, Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.UI.Markdown.InlineText>(row)));
                Assert.Equal(["Copy message", "Copy as markdown", "Edit from here"], MenuTitles(presenter, window, bubble)); Assert.True(Offers(row, "Edit message"));
            }
            finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-104 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:332-334, TimelineRows.swift:2610 the header's line: a turn's summary handed again changes what its header says, and the fold stays as the reader left it
    public void A_finished_turns_summary_handed_again_changes_its_header_line_and_not_its_views_or_its_fold() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("p1", TurnA, "question"), Reply("said", TurnA, "I will run it"), ToolEntry("run", TurnA), Reply("final", TurnA, "Done."), EndWith("e1", TurnA, 2, 0) };
        mount.SetEntries(entries); mount.Settle();
        var folded = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        var header = LogicalQuery.SelfAndDescendants<Button>(folded.Row).Single(button => ToolTip.GetTip(button) as string == "Show this turn's steps");
        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
        var steps = Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(folded.Row));
        Assert.Contains("Worked for 2s", string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(folded.Row).Select(text => text.Text)), StringComparison.Ordinal);
        // The turn's end marker is replaced by one that says another duration.
        entries[^1] = EndWith("e1b", TurnA, 5, 0); mount.SetEntries(entries); mount.Advance(400);
        var now = mount.Blocks().Single(block => block.Id == folded.Id);
        Assert.Equal(5, Assert.IsType<DisplayBlock.Turn>(now.Block).Summary!.Duration); // the block says what the new marker says
        Assert.Same(folded.Row, now.Row);
        Assert.Contains("Worked for 5s",string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(now.Row).Select(text => text.Text)), StringComparison.Ordinal);
        Assert.Same(steps, Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(now.Row))); // still opened, the steps the same views
        Assert.NotNull(LogicalQuery.SelfAndDescendants<Button>(now.Row).SingleOrDefault(button => ToolTip.GetTip(button) as string == "Hide this turn's steps"));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-155 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:2039-2071 TimelineGroupView, TimelineRows.swift:1572 ForEach(entries): a group of a run with no turn that grows under its id is the same row
    public void A_work_group_that_grows_under_its_id_is_the_same_row_and_the_same_group_with_its_steps_following() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { ToolEntry("t1"), ToolEntry("t2") };
        mount.SetEntries(entries); mount.Settle();
        var group = mount.Blocks().Single(); var work = Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(group.Row));
        var first = LogicalQuery.SelfAndDescendants<ToolRow>(work).ToList(); Assert.Equal(2, first.Count);
        // A third step: the group shows the last two and says how many it holds back (TimelineRows.swift:1524-1545), and the row that stays is the same view.
        entries.Add(ToolEntry("t3")); mount.SetEntries(entries); mount.Advance(400);
        var now = mount.Blocks().Single(); Assert.Equal(group.Id, now.Id);
        Assert.Same(group.Row, now.Row); Assert.Same(work, Assert.Single(LogicalQuery.SelfAndDescendants<WorkGroup>(now.Row)));
        var rows = LogicalQuery.SelfAndDescendants<ToolRow>(work).ToList(); Assert.Equal(2, rows.Count);
        Assert.Same(first[1], rows[0]); // the step that was there and stays is the same view
        Assert.Contains("1 earlier step", string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(work).Select(text => text.Text)), StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-155 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:1985-1989 `if let heads` (the branch is the view's identity, not the first head's id), TimelineRows.swift:1569-1576 `ForEach(entries)`: the list is made of the rows of the heads it holds, one that leaves takes its row, the rest stay
    public void A_head_leaving_the_heads_list_takes_its_row_and_the_others_stay() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("prompt", TurnA, "send out heads"), ToolEntry("hank", TurnA, head: true), ToolEntry("walter", TurnA, head: true) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var row = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn).Row; var list = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(row));
        var rows = LogicalQuery.SelfAndDescendants<ToolRow>(list).ToList(); Assert.Equal(2, rows.Count);
        entries.RemoveAt(1); mount.SetEntries(entries); mount.Advance(400);
        Assert.Same(list, Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadsList>(row)));
        Assert.Same(rows[1], Assert.Single(LogicalQuery.SelfAndDescendants<ToolRow>(list)));
    });

    private static HydraHeadInfo FinishedHead(long index, DateTimeOffset started, DateTimeOffset finished) =>
        new(index, "task " + index, HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) { HydraHeadInfoStatus = HydraHeadInfo.Status.Completed, StartedAt = started, FinishedAt = finished };

    private static void SetHeadStatus(TimelineMount mount, long index, HydraHeadInfo.Status status, DateTimeOffset? finished)
    {
        var head = mount.Model.Children(mount.Runtime.ThreadID).First(thread => thread.Hydra!.Index == index);
        mount.Model.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = status, FinishedAt = finished } });
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-035 (ED-NEW-parity-threadtimeline (3)): TimelineRows.swift:408-415 ForEach(finished, id: \.index) { FinishedHeadPill(head: head) }, :441 @State isShowingReport: a head that changed under its index is the same pill with the new value
    public void A_finished_heads_pill_stays_the_same_view_when_its_head_changes_and_says_what_became_of_it() => ThreadTimelineRig.RunWithTeam(headless, mount =>
    {
        var s = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        SetHeadStatus(mount, 1, HydraHeadInfo.Status.Running, null); SetHeadStatus(mount, 3, HydraHeadInfo.Status.Running, null);
        var turn = Guid.NewGuid();
        mount.SetEntries(new[] { User("p1", turn), Reply("a1", turn), End("e1", turn) }); mount.Settle();
        var row = mount.Blocks().Single(block => block.Id == "hydra-heads-working").Row;
        var pills = LogicalQuery.SelfAndDescendants<FinishedHeadPill>(row).ToList(); Assert.Equal(2, pills.Count);
        Assert.EndsWith(" finished working", Avalonia.Automation.AutomationProperties.GetName(pills[0]), StringComparison.Ordinal);
        // The first of them turns out to have failed: the pill is the same view, and says so.
        SetHeadStatus(mount, 2, HydraHeadInfo.Status.Failed, s.AddSeconds(100)); mount.Advance(20);
        var after = LogicalQuery.SelfAndDescendants<FinishedHeadPill>(mount.Blocks().Single(block => block.Id == "hydra-heads-working").Row).ToList(); Assert.Equal(2, after.Count);
        Assert.Same(pills[0], after[0]); Assert.Same(pills[1], after[1]);
        Assert.EndsWith(" failed", Avalonia.Automation.AutomationProperties.GetName(pills[0]), StringComparison.Ordinal);
        Assert.Contains(" failed", string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(pills[0]).SelectMany(text => text.Inlines ?? []).OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text)), StringComparison.Ordinal);
    }, new[] { FinishedHead(1, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), DateTimeOffset.FromUnixTimeSeconds(1_800_001_000)), FinishedHead(2, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), DateTimeOffset.FromUnixTimeSeconds(1_800_000_100)),
        FinishedHead(3, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), DateTimeOffset.FromUnixTimeSeconds(1_800_000_400)), FinishedHead(4, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), DateTimeOffset.FromUnixTimeSeconds(1_800_000_200)) });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof UI-HYDRAMERGE-031 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:1902-1905 (the block keeps its id from the merge's first stage to its outcome note, so the one branch keeps the row's identity and the pill morphs rather than being swapped)
    public void The_merge_pill_is_one_view_from_the_merges_first_stage_to_its_outcome_note() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("p1", TurnA), Reply("a1", TurnA), End("e1", TurnA) };
        mount.SetEntries(entries); mount.Settle();
        mount.Runtime.HydraMergeNoteID = "note-1"; mount.Runtime.IsHydraMerging = true; mount.Advance(400);
        var merging = mount.Blocks().Single(block => block.Block is DisplayBlock.Merging); Assert.Equal("note-1", merging.Id);
        var pill = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMergeRow>(merging.Row));
        // The merge ends: its outcome note is in the thread under the id the block had.
        entries.Add(Entry("note-1", new TimelineItem.Content.User(new UserMessage { Text = "Hydra merged the heads", HydraHeads = new ValueList<long>([]) })));
        mount.SetEntries(entries); mount.Runtime.IsHydraMerging = false; mount.Advance(800);
        var outcome = mount.Blocks().Single(block => block.Id == "note-1");
        Assert.IsType<DisplayBlock.Group>(outcome.Block); Assert.Same(merging.Row, outcome.Row); Assert.Same(pill, Assert.Single(LogicalQuery.SelfAndDescendants<HydraMergeRow>(outcome.Row)));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-121 and UI-CHAT-043 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:2261-2274 (WorkingBlockView hands WorkingIndicator its startedAt and finishedAt), :2075-2092,2107-2119 (@State isExpanded, which no change of a `let` disturbs)
    public void An_opened_running_line_that_becomes_an_earlier_segment_stays_the_same_line_and_stays_open() => ThreadTimelineRig.Run(headless, mount =>
    {
        var entries = new List<TimelineEntry> { User("prompt", TurnA, "a question"), ToolEntry("ran", TurnA) };
        mount.SetEntries(entries); mount.SetTurnStartedAt(SwiftDate.Now); mount.SetPhase(RuntimePhase.Running); mount.Settle();
        var first = mount.Blocks().Single(block => block.Block is DisplayBlock.Turn);
        var line = Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(first.Row));
        LogicalQuery.SelfAndDescendants<Button>(line).First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); mount.Advance(600);
        Assert.Equal("Hide these steps", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(line).First())); // opened
        // A Hydra note lands in the thread and the turn goes on after it: the run so far is an earlier segment, its line finished (a stopped clock, no live bar).
        entries.Add(Entry("note", new TimelineItem.Content.Notice(new Marrow.Core.Models.Notice { NoticeLevel = Marrow.Core.Models.Notice.Level.Info, Message = "a note between the runs" })));
        entries.Add(Reply("after", TurnA, "and on it goes")); mount.SetEntries(entries); mount.Advance(600);
        var now = mount.Blocks().Single(block => block.Id == first.Id);
        Assert.Same(first.Row, now.Row); Assert.False(Assert.IsType<DisplayBlock.Turn>(now.Block).ShowsWorking);
        Assert.Same(line, Assert.Single(LogicalQuery.SelfAndDescendants<WorkingIndicator>(now.Row))); // the same line ...
        Assert.Equal("Hide these steps", ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(line).First())); // ... still open ...
        Assert.Equal(entries[0].Item.Date, line.StartedAt); // ... and now counting from the segment's own first entry, an earlier segment's line
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-026 (ED-NEW-parity-threadtimeline (3)): ThreadTimeline.swift:136-141, NewThreadPrompt.swift:15-55: the line's other branch (a name, no name) is other words in the same prompt, which does not appear again
    public void The_empty_threads_prompt_goes_between_asking_in_a_project_and_asking_alone_without_appearing_again() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)); mount.Advance(500);
        string Words() => string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(prompt).Select(text => text.Text));
        Assert.Contains("What should we build in ", Words(), StringComparison.Ordinal);
        // Each change crossfades over the move's motion (OD-0076; NewThreadPrompt.swift:77-78 Chrome.panelSlide, about 0.39 s): the words are read once it is over.
        Inputs(mount, null, false); mount.Advance(20);
        Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity); // the prompt is not faded in again
        mount.Advance(800);
        Assert.Same(prompt, Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)));
        Assert.Equal("What should we build?", Words()); Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity);
        Inputs(mount, "Another", false); mount.Advance(20);
        Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity);
        mount.Advance(800);
        Assert.Same(prompt, Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View)));
        Assert.Contains("Another", Words(), StringComparison.Ordinal); Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity);
        Inputs(mount, "A third", false); mount.Advance(800); // a name for a name: the same button, its words changed
        Assert.Contains("A third", Words(), StringComparison.Ordinal); Assert.DoesNotContain("Another", Words(), StringComparison.Ordinal);
    });

    private static double Shown(Control control, Control upTo)
    {
        var opacity = 1.0;
        for (Avalonia.Visual? at = control; at is not null; at = Avalonia.VisualTree.VisualExtensions.GetVisualParent(at)) { opacity *= at.Opacity; if (ReferenceEquals(at, upTo)) break; }
        return opacity;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-026 (ED-NEW-parity-threadtimeline (3), OD-0076 content that swaps crossfades and a label whose width changes eases its container's width): NewThreadPrompt.swift:22-46 the line, :25-37 the name's button, :74-80 the move under withAnimation(Chrome.panelSlide)
    public void Another_project_on_the_empty_threads_prompt_crossfades_its_words_in_the_same_button_and_eases_the_line_to_its_new_width() => ThreadTimelineRig.Run(headless, mount =>
    {
        mount.Settle();
        var prompt = Assert.Single(LogicalQuery.SelfAndDescendants<NewThreadPrompt>(mount.View));
        var slots = LogicalQuery.SelfAndDescendants<Panel>(prompt).Where(panel => panel.GetType().Name == "SwapSlot").ToList();
        var line = Assert.Single(slots, slot => slot.Parent is StackPanel { Spacing: 26 }); var name = Assert.Single(slots, slot => slot != line);
        var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(prompt), b => ToolTip.GetTip(b) as string == "Choose another project");
        TextBlock Text(string words) => LogicalQuery.SelfAndDescendants<TextBlock>(prompt).Single(text => text.Text == words);
        var before = name.Bounds.Width; var lineBefore = line.Bounds.Width;
        // A longer name: the same button (its picker, its hover); the old name at full opacity and the new one not yet showing when the change lands ...
        Inputs(mount, "A much longer project name", false); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        Assert.Same(button, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(prompt), b => ToolTip.GetTip(b) as string == "Choose another project"));
        Assert.Equal(1, Shown(Text("Project"), prompt), 3); Assert.Equal(0, Shown(Text("A much longer project name"), prompt), 3);
        // ... part way, both partly showing, the button between its widths and the line between its widths ...
        mount.Advance(100);
        var old = Shown(Text("Project"), prompt); var fresh = Shown(Text("A much longer project name"), prompt);
        Assert.InRange(old, 0.01, 0.99); Assert.InRange(fresh, 0.01, 0.99);
        var after = Text("A much longer project name").DesiredSize.Width; var fontSize = Text("A much longer project name").FontSize;
        Assert.True(name.Bounds.Width > before + 1 && name.Bounds.Width < after - 1, $"the name's slot glides: {before} -> {name.Bounds.Width} -> {after}");
        Assert.True(line.Bounds.Width > lineBefore + 1, "the line widens with it");
        // ... and at the end only the new name, at full opacity and at the size it was drawn at all along, the slot at its width.
        mount.Advance(800);
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(prompt), text => text.Text == "Project");
        Assert.Equal(1, Shown(Text("A much longer project name"), prompt), 3); Assert.Equal(after, name.Bounds.Width, 1); Assert.Equal(fontSize, Text("A much longer project name").FontSize);
        var lineWide = line.Bounds.Width;
        // No project: the line's other branch crossfades in over the old one, and the line eases to the shorter question.
        Inputs(mount, null, false); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); mount.Window.UpdateLayout();
        Assert.Equal(1, Shown(Text("A much longer project name"), prompt), 3); Assert.Equal(0, Shown(Text("What should we build?"), prompt), 3);
        mount.Advance(100);
        Assert.InRange(Shown(Text("What should we build?"), prompt), 0.01, 0.99);
        var shortWidth = Text("What should we build?").DesiredSize.Width;
        Assert.True(line.Bounds.Width < lineWide - 1 && line.Bounds.Width > shortWidth + 1, $"the line glides: {lineWide} -> {line.Bounds.Width} -> {shortWidth}");
        mount.Advance(800);
        Assert.Equal("What should we build?", string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(prompt).Select(text => text.Text)));
        Assert.Equal(1, Shown(Text("What should we build?"), prompt), 3); Assert.Equal(shortWidth, line.Bounds.Width, 1);
        Assert.Equal(1, Assert.IsAssignableFrom<Control>(prompt.Child).Opacity); // and the prompt never appeared again
    });
}
