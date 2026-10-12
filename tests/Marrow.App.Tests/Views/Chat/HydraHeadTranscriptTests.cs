using System.Reflection;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// A head's panel body (App/Views/Chat/HydraPanel.swift:385-437): its whole conversation or its task and progress bar, decided by
/// Settings, with a full chat box at the bottom for a Droppy-run head and a read-only footer for a native one, a jump-to-latest
/// button over whichever is at the bottom, and a veil over the top of the whole.
/// </summary>
[Collection("Headless")]
public sealed class HydraHeadTranscriptTests(HeadlessSession headless)
{
    private void Run(HydraHeadInfo.Kind kind, bool showsDetails, Action<HydraPanelContext, HydraHeadTranscript> test) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var context = new HydraPanelContext();
        context.Kind(context.First, kind);
        context.Model.Settings.HydraShowsHeadDetails = showsDetails;
        var transcript = new HydraHeadTranscript(context.Model.Thread(context.First.Id)!, 280, "/work/project", "Project");
        var host = new Border { Child = transcript, Width = 420, Height = 280 };
        AppEnvironment.SetAppModel(host, context.Model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 420, Height = 280 };
        AppEnvironment.SetWindowLiveResize(host, new WindowLiveResize(window));
        try
        {
            window.Show();
            context.Drain();
            test(context, transcript);
        }
        finally { window.Close(); }
    });

    private static T[] All<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();

    private static object? Field(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(owner);

    [Fact]
    [Trait("Req", "PR-HYDRA-177")] // HydraPanel.swift:399, :413-417 with settings.hydraShowsHeadDetails off, the default, HydraHeadProgress stands in for the steps
    public void With_show_what_heads_are_doing_off_the_panel_shows_the_task_and_progress_bar_instead_of_the_steps() =>
        Run(HydraHeadInfo.Kind.Native, false, (c, transcript) =>
        {
            Assert.Single(All<HydraHeadProgress>(transcript));
            Assert.Empty(All<ThreadTimeline>(transcript));
        });

    [Fact]
    [Trait("Req", "PR-HYDRA-177")] // HydraPanel.swift:399-412 with it on, ThreadTimeline(... supportsRewind: false, columnHeight: height, showsMinimap: false)
    public void With_it_on_the_whole_conversation_shows_without_a_rewind_affordance_and_without_the_timeline_rail() =>
        Run(HydraHeadInfo.Kind.Native, true, (c, transcript) =>
        {
            Assert.Empty(All<HydraHeadProgress>(transcript));
            var timeline = Assert.Single(All<ThreadTimeline>(transcript));
            // The timeline keeps its inputs; nothing public reads them back (ED-119).
            var inputs = Field(timeline, "inputs")!;
            var fields = inputs.GetType().GetFields();
            Assert.Equal(c.Model.Runtime(c.First.Id), fields[0].GetValue(inputs));
            Assert.Equal("Project", fields[3].GetValue(inputs));
            Assert.Equal("/work/project", fields[4].GetValue(inputs));
            Assert.Equal(false, fields[5].GetValue(inputs)); // supportsRewind
            Assert.Equal(280d, fields[6].GetValue(inputs)); // columnHeight: the panel's height
            var rest = fields[7].GetValue(inputs)!; // an 8-tuple keeps its eighth member in Rest
            Assert.Equal(false, rest.GetType().GetField("Item1")!.GetValue(rest)); // showsMinimap
        });

    [Fact]
    [Trait("Req", "PR-HYDRA-177")] // HydraPanel.swift:420-425 a Droppy-run head keeps a full ComposerArea(compactModelChip: true, takesFocusOnAppear: false, showsChanges: false)
    public void A_droppy_run_head_keeps_a_full_chat_box_with_the_compact_model_chip_no_focus_grab_and_no_changes_tab() =>
        Run(HydraHeadInfo.Kind.Droppy, false, (c, transcript) =>
        {
            var composer = Assert.Single(All<ComposerArea>(transcript));
            Assert.Empty(All<HydraHeadFooter>(transcript));
            var view = Assert.Single(All<ComposerView>(composer));
            Assert.Equal(true, Field(view, "compactModelChip"));
            Assert.Equal(false, Field(view, "takesFocusOnAppear"));
            Assert.Empty(All<ChangeStatsRefresh>(composer)); // showsChanges: false
            Assert.Empty(All<ThreadChangesTab>(composer));
        });

    [Fact]
    [Trait("Req", "PR-HYDRA-177")] // HydraPanel.swift:426-431 else if let stored = head.hydra { HydraHeadFooter(info: HydraLiveInfo.shown(stored, runtime)) }
    public void A_native_head_gets_a_read_only_footer_instead() =>
        Run(HydraHeadInfo.Kind.Native, false, (c, transcript) =>
        {
            Assert.Single(All<HydraHeadFooter>(transcript));
            Assert.Empty(All<ComposerArea>(transcript));
        });

    // ED-NEW-calculi-rv-1: the footer is built again when the head's record changes; a running head's beads go on, never re-enter
    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")]
    public void A_running_heads_footer_keeps_its_beads_through_a_rebuild_and_ends_them_when_the_head_stops() =>
        Run(HydraHeadInfo.Kind.Native, false, (c, transcript) =>
        {
            var footer = Assert.Single(All<HydraHeadFooter>(transcript));
            var slot = Assert.Single(All<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(footer));
            Assert.True(slot.IsRunning); Assert.Equal(1, slot.Beads.Current.Turn);
            // the head's real description arrives (HydraAgentStarted rewrites Task and Title): the stored record changes and the footer is rebuilt
            c.Model.UpdateThread(c.First.Id, t => t with { Hydra = t.Hydra! with { Task = "The real description" } });
            transcript.Update(c.Model.Thread(c.First.Id)!);
            c.Drain();
            var rebuilt = Assert.Single(All<HydraHeadFooter>(transcript));
            Assert.NotSame(footer, rebuilt);
            Assert.Same(slot, Assert.Single(All<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(rebuilt)));
            Assert.True(slot.IsRunning); Assert.Equal(1, slot.Beads.Current.Turn); // the same turn: no Ortus again
            Assert.True(Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(slot.Beads));
            // the head stops: its footer shows the status icon and the beads leave with their slot
            c.Status(c.First, HydraHeadInfo.Status.Completed);
            transcript.Update(c.Model.Thread(c.First.Id)!);
            c.Drain();
            Assert.Empty(All<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(transcript));
            Assert.False(slot.IsRunning); Assert.Equal(Marrow.UI.Port.Nth.Beads.NthBeadsExit.Finish, slot.Beads.Current.Exit);
        });

    [Theory]
    [InlineData(HydraHeadInfo.Kind.Droppy)]
    [InlineData(HydraHeadInfo.Kind.Native)]
    [Trait("Req", "PR-HYDRA-177")] // HydraPanel.swift:423-425, :428-430 a JumpToLatestButton overlaid at the top of whichever sits at the bottom
    public void A_jump_to_latest_button_floats_over_whichever_sits_at_the_bottom(HydraHeadInfo.Kind kind) =>
        Run(kind, false, (c, transcript) =>
        {
            var jump = Assert.Single(All<JumpToLatestButton>(transcript));
            var bottom = kind == HydraHeadInfo.Kind.Droppy ? (Control)Assert.Single(All<ComposerArea>(transcript)) : Assert.Single(All<HydraHeadFooter>(transcript));
            var host = Assert.IsAssignableFrom<Panel>(jump.Parent);
            var holder = Assert.Single(host.Children, child => child == bottom || LogicalQuery.SelfAndDescendants<Control>(child).Contains(bottom));
            Assert.True(host.Children.IndexOf(jump) > host.Children.IndexOf(holder)); // overlaid
            Assert.Equal(Avalonia.Controls.Dock.Bottom, DockPanel.GetDock(host)); // at the bottom of the body
        });

    [Fact]
    [Trait("Req", "PR-HYDRA-177")] // HydraPanel.swift:433-435 .overlay(alignment: .top) { PaneTopVeil(model: scrollChrome) } over the whole
    public void A_pane_top_veil_is_overlaid_at_the_top_of_the_whole() =>
        Run(HydraHeadInfo.Kind.Native, false, (c, transcript) =>
        {
            var veil = Assert.Single(All<PaneTopVeil>(transcript));
            var host = Assert.IsAssignableFrom<Panel>(veil.Parent);
            Assert.Same(host.Children.Last(), veil);
            // Over the body, which holds the bottom bar: the veil is a sibling after it, not inside it.
            Assert.Contains(host.Children, child => LogicalQuery.SelfAndDescendants<HydraHeadFooter>(child).Any());
        });
}
