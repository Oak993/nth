using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The heads popover (App/Views/Chat/HydraPanel.swift:258-286): a header counting the heads that work and wait, one row per
/// head, and, below a divider that shows only when it has something to divide, "Stop all heads" and "Clear finished heads".
/// Every action closes it except clearing (upstream defect 31, porting/upstream_defects.md:75 entry 31).
/// </summary>
[Collection("Headless")]
public sealed class HydraHeadsPopoverTests(HeadlessSession headless)
{
    private void Run(Action<HydraPanelContext, ContentRecordingPresenter> test) => ChatHeadlessFixtures.Run(headless, () =>
    {
        var presenter = new ContentRecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        using var context = new HydraPanelContext(visible: true);
        context.Tick(1);
        test(context, presenter);
    });

    private static Popup Open(HydraPanelContext c, ContentRecordingPresenter presenter)
    {
        c.Tap(c.WithTip("2 heads"));
        return Assert.Single(presenter.Shown()).Popup;
    }

    private static string Header(Popup popup) =>
        Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(Assert.Single(LogicalQuery.SelfAndDescendants<PopoverSectionHeader>(popup.Child!)))).Text!;

    private static PopoverItem[] Items(Popup popup) => LogicalQuery.SelfAndDescendants<PopoverItem>(popup.Child!).ToArray();

    private static string Title(PopoverItem item) => Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item)).FullText;

    private static string[] Titles(Popup popup) => Items(popup).Select(Title).ToArray();

    private static PopoverItem Item(Popup popup, string title) => Items(popup).Single(item => Title(item) == title);

    private static string? Symbol(PopoverItem item) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(item).Select(image => image.SymbolName).FirstOrDefault(name => name is not null);

    private static void Press(PopoverItem item) =>
        Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string RowName(HydraHeadRow row) =>
        LogicalQuery.SelfAndDescendants<Control>(row).Select(Avalonia.Automation.AutomationProperties.GetName).OfType<string>().First(name => name.Length > 0);

    private static int Dividers(Popup popup) => LogicalQuery.SelfAndDescendants<PopoverDivider>(popup.Child!).Count();

    // ED-119: nothing public sets the waiting count; only the lead's own queue of heads that cannot start does.
    private static void SetWaiting(HydraPanelContext c, long count)
    {
        typeof(ThreadRuntime).GetProperty("HydraWaitingCount")!.SetValue(c.Runtime, count);
        c.Drain();
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-174")] // HydraPanel.swift:260-262 "Heads", "1 head working" or "N heads working", then ", 1 waiting" or ", N waiting"
    [Trait("Req", "UI-HYDRA-014")] // HydraPanel.swift:260-262 the header counts working and waiting heads
    public void The_header_counts_the_heads_that_work_and_the_heads_that_wait_and_follows_them_while_open() => Run((c, presenter) =>
    {
        var popup = Open(c, presenter);
        Assert.Equal("2 heads working", Header(popup));

        c.Status(c.First, HydraHeadInfo.Status.Completed);
        Assert.Equal("1 head working", Header(popup));
        c.Status(c.Second, HydraHeadInfo.Status.Failed);
        Assert.Equal("Heads", Header(popup));

        SetWaiting(c, 1);
        Assert.Equal("Heads, 1 waiting", Header(popup));
        c.Status(c.Second, HydraHeadInfo.Status.Running);
        Assert.Equal("1 head working, 1 waiting", Header(popup));
        SetWaiting(c, 5);
        Assert.Equal("1 head working, 5 waiting", Header(popup));
        c.Status(c.First, HydraHeadInfo.Status.Running);
        Assert.Equal("2 heads working, 5 waiting", Header(popup));
        SetWaiting(c, 0);
        Assert.Equal("2 heads working", Header(popup));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-174")] // HydraPanel.swift:269-271 if running > 1 || heads.contains(where: { $0.hydra?.isFinished == true }) { PopoverDivider() }
    public void The_divider_shows_only_when_more_than_one_head_runs_or_one_is_finished() => Run((c, presenter) =>
    {
        var popup = Open(c, presenter);
        Assert.Equal(1, Dividers(popup)); // two run

        c.Status(c.First, HydraHeadInfo.Status.Completed);
        Assert.Equal(1, Dividers(popup)); // one is finished
        c.Status(c.Second, HydraHeadInfo.Status.Stopped);
        Assert.Equal(1, Dividers(popup));
        c.Status(c.Second, HydraHeadInfo.Status.Running);
        c.InPanel(c.First, false);
        Assert.Equal("1 head working", Header(popup));
        Assert.Equal(0, Dividers(popup)); // one head, running: nothing to divide
        Assert.Empty(Items(popup));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-174")] // HydraPanel.swift:272-277 PopoverItem("Stop all heads", symbol: "stop.circle") only when running > 1; stopAllHydraHeads(of:), then isPresented = false
    [Trait("Req", "UI-HYDRA-017")] // UI-HYDRA-017: present only when MORE THAN ONE head is running; click stops every running head of the lead, then CLOSES the popover
    public void Stop_all_heads_stops_every_running_head_and_closes_the_popover() => Run((c, presenter) =>
    {
        c.MakeStoppable(c.First);
        c.MakeStoppable(c.Second);
        var popup = Open(c, presenter);
        var stopAll = Item(popup, "Stop all heads");
        Assert.Equal("stop.circle", Symbol(stopAll));
        Assert.Equal(["Stop all heads"], Titles(popup));
        // The lead has no provider session, so each native head's stop is refused and its stop affordance goes (AppModel+Hydra.swift:926-931).
        Assert.True(c.Model.Thread(c.First.Id)!.Hydra!.CanStop);
        Press(stopAll);
        c.Drain();
        Assert.False(c.Model.Thread(c.First.Id)!.Hydra!.CanStop);
        Assert.False(c.Model.Thread(c.Second.Id)!.Hydra!.CanStop);
        Assert.Empty(presenter.Shown());
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-017")] // HydraPanel.swift:272 hidden when exactly one head runs (its own row carries a stop button) or none
    public void Stop_all_heads_is_hidden_unless_more_than_one_head_runs() => Run((c, presenter) =>
    {
        var popup = Open(c, presenter);
        Assert.Contains("Stop all heads", Titles(popup));
        c.Status(c.First, HydraHeadInfo.Status.Completed);
        Assert.DoesNotContain("Stop all heads", Titles(popup));
        c.Status(c.Second, HydraHeadInfo.Status.Failed);
        Assert.DoesNotContain("Stop all heads", Titles(popup));
        c.Status(c.First, HydraHeadInfo.Status.Running);
        Assert.DoesNotContain("Stop all heads", Titles(popup));
        c.Status(c.Second, HydraHeadInfo.Status.Running);
        Assert.Contains("Stop all heads", Titles(popup));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-174")] // HydraPanel.swift:278-284 PopoverItem("Clear finished heads", symbol: "checkmark.circle") only when a head is finished; clears inside withAnimation(Chrome.panelSlide); does NOT close
    [Trait("Req", "UI-HYDRA-018")] // UI-HYDRA-018: present only when a head is finished; click clears every finished head with Chrome.panelSlide and LEAVES THE POPOVER OPEN (upstream defect 31)
    public void Clear_finished_heads_clears_them_inside_the_panel_slide_and_leaves_the_popover_open() => Run((c, presenter) =>
    {
        var popup = Open(c, presenter);
        Assert.DoesNotContain("Clear finished heads", Titles(popup));
        c.Status(c.First, HydraHeadInfo.Status.Completed);
        var clear = Item(popup, "Clear finished heads");
        Assert.Equal("checkmark.circle", Symbol(clear));
        Assert.Equal(["Clear finished heads"], Titles(popup)); // one head still runs: no stop-all

        Animation? seen = null;
        var (_, tracker) = ObservationTracking.Track(() => c.Model.Thread(c.First.Id)!.IsInPanel, () => seen = Transaction.Current);
        Press(clear);
        c.Drain();
        Assert.False(c.Model.Thread(c.First.Id)!.IsInPanel);
        Assert.True(c.Model.Thread(c.Second.Id)!.IsInPanel); // only the finished head went
        Assert.Equal(Chrome.PanelSlide, seen);
        tracker.Cancel();
        // It only clears: the panel is not dismissed and the selection is left alone (clearFinishedHydraHeads, not dismissHydraHeads).
        Assert.False(c.Runtime.IsHydraPanelHidden);
        Assert.Equal(c.First.Id, c.Runtime.HydraSelectedHeadID);

        // The one action that does not close the popover; it now shows what is left.
        Assert.Same(popup, Assert.Single(presenter.Shown()).Popup);
        Assert.Equal("1 head working", Header(popup));
        Assert.DoesNotContain("Clear finished heads", Titles(popup));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-174")] // HydraPanel.swift:263-268 one HydraHeadRow per head; a tap sets runtime.hydraSelectedHeadID and sets isPresented = false
    public void Choosing_a_head_puts_it_on_stage_and_closes_the_popover() => Run((c, presenter) =>
    {
        var popup = Open(c, presenter);
        var rows = LogicalQuery.SelfAndDescendants<HydraHeadRow>(popup.Child!).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.EndsWith(", on stage", RowName(rows[0]), StringComparison.Ordinal);
        Assert.DoesNotContain(", on stage", RowName(rows[1]), StringComparison.Ordinal);

        c.Drain();
        LogicalQuery.SelfAndDescendants<Button>(rows[1]).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        c.Drain();
        Assert.Equal(c.Second.Id, c.Runtime.HydraSelectedHeadID);
        Assert.Empty(presenter.Shown());

        // And back again.
        var again = Open(c, presenter);
        LogicalQuery.SelfAndDescendants<Button>(LogicalQuery.SelfAndDescendants<HydraHeadRow>(again.Child!).First()).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        c.Drain();
        Assert.Equal(c.First.Id, c.Runtime.HydraSelectedHeadID);
        Assert.Empty(presenter.Shown());
    });
}
