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
        Animation? seen = null;
        var (_,tracker) = ObservationTracking.Track(() => c.Model.Thread(c.First.Id)!.IsInPanel,()=>seen=Transaction.Current);
        var item = c.Find<PopoverItem>().Single(i => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(i).Any(t=>t.FullText=="Clear finished heads"));
        var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); c.Drain();
        Assert.False(c.Model.Thread(c.First.Id)!.IsInPanel);
        Assert.Equal(Chrome.PanelSlide,seen);
        tracker.Cancel();
    });

    private void Run(Action<HydraPanelContext> test) => headless.OnUiThread(() =>
    {
        using var context = new HydraPanelContext();
        test(context);
    });

    // STOP App.Views.Chat.HydraPanel #followup-transcript
    // Kind: prerequisite. Swift :419-435; PR-HYDRA-168. JumpToLatestButton (ChatView.cs:201)
    // still throws MARROW_STUB. Clock tests substitute only stage content via the internal factory;
    // panel observation, model mutations, clipping, transitions and DotFieldSweep are real.
    // Full transcript integration needs the ChatView unit to implement that dependency.

}
