using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class HydraPanelFollowupTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(HydraHeadInfo.Status.Completed)]
    [InlineData(HydraHeadInfo.Status.Failed)]
    [InlineData(HydraHeadInfo.Status.Stopped)]
    [Trait("Req", "UI-HYDRA-026")] // App/Views/Chat/HydraPanel.swift:43-91
    [Trait("Req", "PR-HYDRA-168")] // App/Views/Chat/HydraPanel.swift:50-72
    [Trait("Req", "PR-HYDRA-169")] // App/Views/Chat/HydraPanel.swift:41, :72-91, :205-208
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-169 (the hold, the wave, the replay and the cancel are proven by the set of tests carrying it)
    public void The_same_running_head_holds_the_stage_for_exactly_two_seconds(HydraHeadInfo.Status status) => Run(c =>
    {
        var original = c.Stage(c.First.Id);
        c.Status(c.First, status);
        Assert.Single(c.Find<DotFieldSweep>());
        Assert.Same(original,c.Stage(c.First.Id));
        c.Select(c.Second);
        c.Tick(1.999);
        Assert.Same(original,Assert.Single(c.Stages()));
        c.Tick(.001);
        var next = c.Stage(c.Second.Id);
        Assert.Equal(0,next.Opacity);
        Assert.Contains(original,c.Stages());
        c.Tick(Chrome.PanelSlide.Duration);
        Assert.Same(next,Assert.Single(c.Stages()));
        Assert.Equal(1,next.Opacity);
        Assert.Empty(c.Find<DotFieldSweep>());
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-168")] // App/Views/Chat/HydraPanel.swift:57-78
    [Trait("Req", "UI-HYDRA-026")] // App/Views/Chat/HydraPanel.swift:74-91; App/Views/Composer/ComposerWorkingDots.swift:22-34,177-189
    [Trait("Req", "PR-HYDRA-169")] // App/Views/Chat/HydraPanel.swift:41, :72-91, :205-208
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-169 (the hold, the wave, the replay and the cancel are proven by the set of tests carrying it)
    public void The_finish_wave_has_persona_ink_round_clipping_and_a_real_opacity_transition() => Run(c =>
    {
        c.Status(c.First,HydraHeadInfo.Status.Completed);
        var sweep = Assert.Single(c.Find<DotFieldSweep>());
        var clip = Assert.IsType<Border>(sweep.Parent);
        Assert.Equal(new CornerRadius(22),clip.CornerRadius);
        Assert.True(clip.ClipToBounds);
        Assert.Equal(0,clip.Opacity);
        var field = Assert.IsType<DotFieldControl>(sweep.Child);
        field.Measure(new Size(40,24)); field.Arrange(new Rect(0,0,40,24));
        var firstDot = Assert.IsType<Ellipse>(Assert.IsType<Canvas>(field.Layer).Children[0]);
        Assert.Equal(Color.FromRgb(255,138,61),Assert.IsAssignableFrom<ISolidColorBrush>(firstDot.Fill).Color); // Core/Models/Hydra.swift:232 Hank FF8A3D
        c.Tick(.05);
        Assert.Equal(Chrome.PanelSlide.Progress(.05),clip.Opacity,6);
        c.Tick(.27);
        // Since 1f779c4c5 the animated replicas are drawn by the compositor (DotFieldCompositionFrame) and the static canvas
        // is hidden while animated, so the peak is read from a rendered frame (ComposerWorkingDots.swift:158-175). The context
        // keeps the panel hidden to avoid headless glyph shaping; it is shown for this one render pass only.
        c.Panel.IsVisible = true;
        Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var pixels = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(c.Window)) Assert.NotNull(pixels);
        c.Panel.IsVisible = false;
        var frame = typeof(DotFieldControl).GetProperty("LastRenderedFrame", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(field);
        Assert.NotNull(frame);
        Assert.Equal(.7, (double)frame!.GetType().GetMethod("OpacityAt")!.Invoke(frame, [0, 0])!, 6); // one sweep peak at 0.4*0.8s
        c.Tick(1.68);
        Assert.Contains(sweep,c.Find<DotFieldSweep>()); // removal itself fades rather than vanishing
        c.Tick(.05);
        Assert.Equal(1-Chrome.PanelSlide.Progress(.05),clip.Opacity,6);
        c.Tick(Chrome.PanelSlide.Duration);
        Assert.Empty(c.Find<DotFieldSweep>());
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-168")] // App/Views/Chat/HydraPanel.swift:50-68,79-82
    public void Selection_reacts_and_crossfades_but_another_heads_finish_does_not_create_a_hold() => Run(c =>
    {
        var old = c.Stage(c.First.Id);
        c.Select(c.Second);
        var next = c.Stage(c.Second.Id);
        Assert.Equal(0,next.Opacity); Assert.Equal(1,old.Opacity);
        c.Tick(.05);
        Assert.Equal(Chrome.PanelSlide.Progress(.05),next.Opacity,6);
        Assert.Equal(1-Chrome.PanelSlide.Progress(.05),old.Opacity,6);
        c.Tick(Chrome.PanelSlide.Duration);
        c.Status(c.First,HydraHeadInfo.Status.Completed);
        Assert.Empty(c.Find<DotFieldSweep>());
        Assert.Same(next,Assert.Single(c.Stages()));
        c.Select(c.First); c.Tick(Chrome.PanelSlide.Duration);
        Assert.Empty(c.Find<DotFieldSweep>()); // changing id to an already-finished head is not running->finished
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-026")] // App/Views/Chat/HydraPanel.swift:79-91,203-208
    [Trait("Req", "PR-HYDRA-169")] // App/Views/Chat/HydraPanel.swift:41, :72-91, :205-208
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-169 (the hold, the wave, the replay and the cancel are proven by the set of tests carrying it)
    public void A_replacement_hold_restarts_its_wave_and_cancels_the_previous_deadline() => Run(c =>
    {
        c.Status(c.First,HydraHeadInfo.Status.Completed);
        var firstWave = Assert.Single(c.Find<DotFieldSweep>());
        c.Tick(1);
        c.Status(c.First,HydraHeadInfo.Status.Running);
        c.Status(c.First,HydraHeadInfo.Status.Failed);
        Assert.Contains(c.Find<DotFieldSweep>(),wave => !ReferenceEquals(wave,firstWave));
        c.Select(c.Second); c.Tick(1);
        Assert.Single(c.Stages());
        Assert.NotEmpty(c.Find<DotFieldSweep>());
        c.Tick(1); c.Tick(Chrome.PanelSlide.Duration);
        Assert.Equal(c.Second.Id,c.StageId(Assert.Single(c.Stages())));
        Assert.Empty(c.Find<DotFieldSweep>());
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-026")] // App/Views/Chat/HydraPanel.swift:87-91
    [Trait("Req", "PR-HYDRA-169")] // App/Views/Chat/HydraPanel.swift:41, :72-91, :205-208
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-169 (the hold, the wave, the replay and the cancel are proven by the set of tests carrying it)
    public void Detaching_cancels_the_hold_task_and_stops_observing_selection() => Run(c =>
    {
        c.Status(c.First,HydraHeadInfo.Status.Completed);
        var wave = Assert.Single(c.Find<DotFieldSweep>());
        c.Window.Close();
        c.Select(c.Second); c.Tick(3);
        Assert.Contains(wave,c.Find<DotFieldSweep>());
        Assert.Equal(c.First.Id,c.StageId(Assert.Single(c.Stages())));
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-169")] // App/Views/Chat/HydraPanel.swift:164,182-188,198-199
    public void Head_count_updates_the_strip_and_soft_appears_the_popout_control() => Run(c =>
    {
        c.InPanel(c.Second,false); c.Tick(Chrome.PanelSlide.Duration);
        Assert.Empty(c.PopOutButtons());
        Assert.Contains(c.Find<Control>(),control => Avalonia.Automation.AutomationProperties.GetName(control)=="1 head");
        c.InPanel(c.Second,true);
        var button = Assert.Single(c.PopOutButtons());
        Assert.Equal(0,button.Opacity);
        Assert.Equal(4,Assert.IsType<TranslateTransform>(button.RenderTransform).Y);
        c.Tick(.05);
        var progress=Animation.SoftAppear.Progress(.05);
        Assert.Equal(progress,button.Opacity,6);
        Assert.Equal(4*(1-progress),Assert.IsType<TranslateTransform>(button.RenderTransform).Y,6);
        c.Tick(Animation.SoftAppear.Duration);
        Assert.Equal(1,button.Opacity);
        Assert.Equal(0,Assert.IsType<TranslateTransform>(button.RenderTransform).Y);
        Assert.Contains(c.Find<Control>(),control => Avalonia.Automation.AutomationProperties.GetName(control)=="2 heads");
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-169")] // App/Views/Chat/HydraPanel.swift:68,97,198-199
    public void Live_resize_disables_new_stage_and_capsule_animations_and_restores_them_afterward() => Run(c =>
    {
        c.Resize(true); c.Select(c.Second);
        var next = Assert.Single(c.Stages());
        Assert.Equal(c.Second.Id,c.StageId(next)); Assert.Equal(1,next.Opacity);
        Assert.Equal(1,Assert.Single(c.PopOutButtons()).Opacity);
        c.InPanel(c.First,false);
        Assert.Empty(c.PopOutButtons());
        c.InPanel(c.First,true);
        Assert.Equal(1,Assert.Single(c.PopOutButtons()).Opacity);
        c.Resize(false); c.Select(c.First);
        Assert.Equal(0,c.Stage(c.First.Id).Opacity);
        c.Tick(Chrome.PanelSlide.Duration);
        Assert.Equal(c.First.Id,c.StageId(Assert.Single(c.Stages())));
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-019")] // App/Views/Chat/HydraPanel.swift:278-282
    public void Clear_finished_heads_performs_its_real_model_mutation_inside_panel_slide() => Run(c =>
    {
        c.Status(c.First,HydraHeadInfo.Status.Completed);
        // The heads popover is a popup that builds its content when it is presented (HydraPanel.swift:258), so open it first.
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        c.Panel.IsVisible = true; c.Drain();
        c.Tap(c.WithTip("2 heads"));
        Animation? seen = null;
        var (_,tracker) = ObservationTracking.Track(() => c.Model.Thread(c.First.Id)!.IsInPanel,()=>seen=Transaction.Current);
        var item = c.Find<PopoverItem>().Single(i => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(i).Any(t=>t.FullText=="Clear finished heads"));
        var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); c.Drain();
        Assert.False(c.Model.Thread(c.First.Id)!.IsInPanel);
        Assert.Equal(Chrome.PanelSlide,seen);
        tracker.Cancel();
    });

    // PR-HYDRA-169 failure case: the head leaves the panel during its hold, so the overlay's lookup fails and no wave is drawn
    // (HydraPanel.swift:80-81 `if let hold, let head = heads.first(where: { $0.id == hold.id })`); the stage moves on to the head that is left.
    [Fact]
    [Trait("Req", "PR-HYDRA-169")] // App/Views/Chat/HydraPanel.swift:79-82
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-169: the failure case only
    public void Leaving_the_panel_during_the_hold_draws_no_wave_and_moves_the_stage_on() => Run(c =>
    {
        c.Status(c.First,HydraHeadInfo.Status.Completed);
        Assert.Single(c.Find<DotFieldSweep>());
        c.InPanel(c.First,false);
        c.Tick(Chrome.PanelSlide.Duration);
        Assert.Empty(c.Find<DotFieldSweep>());
        Assert.Equal(c.Second.Id,c.StageId(Assert.Single(c.Stages())));
    });

    // PR-HYDRA-168's fallback chain, HydraPanel.swift:50-56: `heads.first { selected } ?? heads.last { running } ?? heads.last`.
    // The selection is gone (nothing, or an id that is no head of the team) so the next two operands decide; each test step
    // changes which operand wins: the newest RUNNING head over a newer finished one, then the newest head when none runs.
    [Theory]
    [InlineData(false)] // no selected head at all
    [InlineData(true)] // a selected id that no head of this team has
    [Trait("Req", "PR-HYDRA-168")] // App/Views/Chat/HydraPanel.swift:50-56 (wanted), :58 (held ?? wanted)
    public void Without_a_selection_the_newest_running_head_is_on_stage_else_the_newest_head(bool strayId) => Run(c =>
    {
        Guid OnStage(string step) { var stages = c.Stages(); Assert.True(stages.Length == 1, step + ": " + stages.Length + " stages"); return c.StageId(stages[0]); }
        c.Tick(Chrome.PanelSlide.Duration); // the selected head finishes fading in
        c.Runtime.HydraSelectedHeadID = strayId ? Guid.NewGuid() : null; c.Drain(); c.Tick(Chrome.PanelSlide.Duration);
        Assert.Equal(c.Second.Id,OnStage("both running")); // the LAST running head
        // With no pick the head on stage IS the newest working one, so when it finishes the stage moves on at once: the stage's id changes with its
        // status, which is not "the same head running -> not running", so no hold and no wave start (PR-HYDRA-169).
        c.Status(c.Second,HydraHeadInfo.Status.Completed); c.Tick(Chrome.PanelSlide.Duration);
        Assert.Equal(c.First.Id,OnStage("second finished")); // the newest head is finished: the newest one still WORKING wins over it
        Assert.Empty(c.Find<DotFieldSweep>());
        c.Status(c.First,HydraHeadInfo.Status.Completed); c.Tick(Chrome.PanelSlide.Duration);
        Assert.Equal(c.Second.Id,OnStage("none working")); // nothing is working: the newest head
        Assert.Empty(c.Find<DotFieldSweep>());
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-168")] // App/Views/Chat/HydraPanel.swift:50-52 `isPoppedOut ? heads.first`
    public void A_popped_out_panel_shows_its_first_head_whatever_the_leads_selection_is()
    {
        headless.OnUiThread(() =>
        {
            // Both heads work and the lead has the SECOND selected: only the popped-out rule keeps the first on stage.
            using var c = new HydraPanelContext(poppedOut: true, poppedWithBoth: true, realStage: true);
            c.Runtime.HydraSelectedHeadID = c.Second.Id; c.Drain(); c.Tick(Chrome.PanelSlide.Duration);
            Assert.Same(c.Model.Runtime(c.First.Id),StageRuntime(c));
            c.Status(c.First,HydraHeadInfo.Status.Completed); c.Tick(HydraPanel.FinishHold); c.Tick(Chrome.PanelSlide.Duration);
            Assert.Same(c.Model.Runtime(c.First.Id),StageRuntime(c)); // still its first head: finishing changes nothing about who is on stage
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-168")] // App/Views/Chat/HydraPanel.swift:56-62 (no head: no transcript), :131-199 (the strip stays)
    public void With_no_heads_only_the_strip_shows() => Run(c =>
    {
        c.InPanel(c.First,false); c.InPanel(c.Second,false);
        c.Tick(Chrome.PanelSlide.Duration);
        Assert.Empty(c.Stages());
        Assert.NotNull(c.WithTip("Drag to move"));
        Assert.NotNull(c.WithTip("Dismiss the panel; heads keep working"));
        Assert.Empty(c.PopOutButtons());
    });

    // The stage hosts the real HydraHeadTranscript for the selected head at the panel's height (HydraPanel.swift:61-67), not only the
    // test seam's stand-in: a context without the StageFactory builds it, and selecting the other head swaps it (`.id(selected.id)`).
    [Fact]
    [Trait("Req", "PR-HYDRA-168")] // App/Views/Chat/HydraPanel.swift:59-71
    public void The_stage_hosts_the_real_head_transcript_for_the_selected_head_and_swaps_it_with_the_selection()
    {
        headless.OnUiThread(() =>
        {
            using var c = new HydraPanelContext(realStage: true);
            c.Drain(); c.Tick(Chrome.PanelSlide.Duration);
            var first = Assert.Single(c.Find<HydraHeadTranscript>());
            Assert.Same(c.Model.Runtime(c.First.Id),StageRuntime(c));
            c.Select(c.Second); c.Tick(Chrome.PanelSlide.Duration);
            var second = Assert.Single(c.Find<HydraHeadTranscript>());
            Assert.NotSame(first,second); // `.id(selected.id)`: a new transcript for the new head
            Assert.Same(c.Model.Runtime(c.Second.Id),StageRuntime(c));
        });
    }

    // The head runtime the one real transcript on stage was built for (HydraPanel.swift:61-66 HydraHeadTranscript(head:)).
    private static ThreadRuntime StageRuntime(HydraPanelContext c) =>
        (ThreadRuntime)typeof(HydraHeadTranscript).GetField("runtime", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Assert.Single(c.Find<HydraHeadTranscript>()))!;

    private void Run(Action<HydraPanelContext> test) => headless.OnUiThread(() =>
    {
        using var context = new HydraPanelContext();
        test(context);
    });

    // The earlier STOP App.Views.Chat.HydraPanel #followup-transcript is resolved: JumpToLatestButton is real (ChatView.cs:1155-1190) and
    // The_stage_hosts_the_real_head_transcript_for_the_selected_head_and_swaps_it_with_the_selection runs the panel without the seam.

}
