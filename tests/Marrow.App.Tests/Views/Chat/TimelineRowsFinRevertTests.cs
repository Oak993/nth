using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinRig;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :2770-2799 the revert confirmation, :2695 what it runs,
// :2707-2714 the legacy changes worked out once.
[Collection("Headless")]
public sealed class TimelineRowsFinRevertTests(HeadlessSession headless)
{
    private static FileChangeSummary Files(int count) =>
        new(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);

    private static Button DiscWith(Control card, string symbol) =>
        Mounted<Button>(card).Single(button => Mounted<SwiftImage>(button).Any(image => image.SymbolName == symbol));

    [Fact]
    [Trait("Req", "UI-CHAT-039")] // TimelineRows.swift:2777-2798 the confirmation popover
    [Trait("Req", "UI-GIT-047")] // TimelineRows.swift:2777-2798
    [Trait("Req", "PR-GIT-122")] // TimelineRows.swift:2770-2799 Cancel and Revert, the popover closing first
    public void The_revert_confirmation_is_a_300_point_panel_with_a_14_point_title_a_secondary_body_and_Cancel_and_Revert_that_close_it() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var reverts = 0;
        TurnFileCard card = null!;
        AvaloniaWindow window = null!, menu = null!;
        Popup popup = null!;
        runtime.OnMain(() =>
        {
            card = new TurnFileCard(Files(2), canUndo: true, onRevert: () => reverts++, onReview: _ => { });
            window = Show(new StackPanel { Margin = new Thickness(20), Children = { card } });
            Click(window, DiscWith(card, "arrow.uturn.backward"));
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
                // :2777 arrowEdge: .bottom.
                Assert.Equal(PlacementMode.Bottom, popup.Placement);
                menu = HostMenu(popup);
                var sheet = Mounted<Control>(menu).First(control => control.Width == 300);
                // :2795-2796 padded 16 and 300 wide; :2778 the content is 10 points apart, leading-aligned.
                Assert.Equal(new Thickness(16), Assert.IsType<Border>(sheet).Padding);
                var column = Mounted<StackPanel>(sheet).First(stack => stack.Spacing == 10);
                var title = TextNamed(column, "Revert up to this point?");
                // :2780 .system(size: 14, weight: .semibold); :2782 the body .system(.callout) in Chrome.secondaryText, wrapping.
                Face(title, 14, FontWeight.SemiBold, "title");
                var body = TextNamed(column, "Files go back to how they were before this turn, and the turn leaves the conversation.");
                Face(body, 12, FontWeight.Normal, ".system(.callout)");
                Paints(body.Foreground, ChromeTokens.SecondaryText, body, "body");
                Assert.Equal(TextWrapping.Wrap, body.TextWrapping);
                Assert.Equal(10, Top(body) - (Top(title) + title.Bounds.Height), 0.5);
                // :2785-2793 the button row: 8 apart, pushed to the trailing edge; Cancel then Revert; Revert is the default action.
                var buttons = Mounted<StackPanel>(column).Single(stack => stack.Orientation == Orientation.Horizontal && stack.Spacing == 8);
                Assert.Equal(HorizontalAlignment.Right, buttons.HorizontalAlignment);
                var cancel = Assert.Single(Mounted<Button>(buttons), button => Texts(button).Contains("Cancel"));
                var go = Assert.Single(Mounted<Button>(buttons), button => Texts(button).Contains("Revert"));
                Assert.True(Left(cancel, menu) < Left(go, menu));
                Assert.Equal(8, Left(go, menu) - (Left(cancel, menu) + cancel.Bounds.Width), 0.5);
                Assert.True(go.IsDefault);
                Assert.False(cancel.IsDefault);
                OnOneCentreLine(menu, cancel, go);
                // Cancel closes and does nothing.
                ClickButton(menu, cancel);
                Assert.Equal(0, reverts);
            }
            catch { Close(window); throw; }
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Contains(presenter.Calls, call => !call.Presented);
            }
            finally { if (menu is not null) Close(menu); Close(window); }
        });

        double Top(Control control) => control.TranslatePoint(default, menu)!.Value.Y;
        double Left(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.X;
    });

    [Fact]
    [Trait("Req", "UI-CHAT-039")] // TimelineRows.swift:2789 Revert closes the popover and runs the revert
    [Trait("Req", "UI-GIT-047")] // TimelineRows.swift:2789-2791
    [Trait("Req", "PR-GIT-122")] // TimelineRows.swift:2789, :2695 Revert closes, then reverts with files restored
    public void Revert_closes_the_popover_and_runs_the_revert_once() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var reverts = 0;
        TurnFileCard card = null!;
        AvaloniaWindow window = null!, menu = null!;
        runtime.OnMain(() =>
        {
            card = new TurnFileCard(Files(2), canUndo: true, onRevert: () => reverts++, onReview: _ => { });
            window = Show(new StackPanel { Margin = new Thickness(20), Children = { card } });
            Click(window, DiscWith(card, "arrow.uturn.backward"));
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                menu = HostMenu(Assert.Single(presenter.Calls, call => call.Presented).Popup);
                var go = Mounted<Button>(menu).Single(button => Texts(button).Contains("Revert"));
                ClickButton(menu, go);
                Assert.Equal(1, reverts);
            }
            catch { Close(window); throw; }
        });
        runtime.OnMain(() =>
        {
            try
            {
                Settle(window);
                Assert.Contains(presenter.Calls, call => !call.Presented);
                Assert.Equal(1, reverts);
            }
            finally { if (menu is not null) Close(menu); Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-GIT-122")] // TimelineRows.swift:2695 onRevert: Task { _ = try? await runtime.revert(to: turnID, restoreFiles: true) }
    public void The_cards_revert_always_asks_for_the_files_back_so_a_turn_without_a_checkpoint_is_refused_and_kept_without_a_second_question() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var dialogs = new RecordingConfirmationPresenter();
        using var dialogSeam = AlertPresentation.Presenter.Override(dialogs);
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("prompt");
            h.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Failed, null));
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return h.Runtime.Phase == RuntimePhase.Idle; }, AppModelHarness.Bound);
        AvaloniaWindow window = null!, menu = null!;
        TurnFinishedBlock block = null!;
        h.Main.OnMain(() =>
        {
            var turn = h.Runtime.Turns[0].Id;
            block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn) with { Duration = 64, Changes = Files(1) }, ValueList<TimelineEntry>.Empty,
                new ValueList<TimelineEntry>([Reply("```\nanswer\n```")]), null, canUndo: true, chronological: false);
            window = Show(new StackPanel { Margin = new Thickness(20), Children = { block } });
            Click(window, DiscWith(block, "arrow.uturn.backward"));
        });
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                menu = HostMenu(Assert.Single(presenter.Calls, call => call.Presented).Popup);
                Assert.Single(h.Runtime.Turns);
                ClickButton(menu, Mounted<Button>(menu).Single(button => Texts(button).Contains("Revert")));
            }
            catch { Close(window); throw; }
        });
        h.Main.Loop.RunUntilIdle();
        h.Main.OnMain(() =>
        {
            try
            {
                Settle(window);
                // No second question: nothing asked again. Files were asked for (restoreFiles: true), and with no checkpoint to
                // restore from the revert is refused: the turn is still there, where keeping the files would have trimmed it.
                Assert.Empty(dialogs.Shown);
                Assert.Single(h.Runtime.Turns);
                Assert.Contains(presenter.Calls, call => !call.Presented);
            }
            finally { if (menu is not null) Close(menu); Close(window); }
        });
    });
}
