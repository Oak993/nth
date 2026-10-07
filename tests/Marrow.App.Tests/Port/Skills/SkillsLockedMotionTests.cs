using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class SkillsLockedMotionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")] // sd-page.css:456-459: .12 delay; .3 CSS ease opacity; .4 --ez movement.
    public void Arrival_holds_for_120ms_then_uses_independent_locked_opacity_and_movement() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        h.Install.HoldOn("S1");
        var pill = new SkillsProgressPill(h.Page.Model!, _ => { });
        h.Root.Children.Add(pill);
        h.Controller.OpenDownloadAll();
        var run = h.Controller.Continue();
        h.Wait(h.Install.Started("S1"));
        h.Page.Model!.DialogHidden = true;
        h.Settle();
        Assert.Equal(0, pill.Face.Opacity);
        h.Step(119);
        Assert.Equal(0, pill.Face.Opacity);
        Assert.Equal(0, pill.Revealed);
        h.Step(1);
        Assert.Equal(0, pill.Face.Opacity);
        h.Step(150);
        // Cubic Bezier values solved from the locked CSS, independent of the port's sampler.
        Assert.Equal(.802403387584857, pill.Face.Opacity, 6);
        Assert.Equal(.9052153090409583, pill.Revealed, 6);
        h.Step(150);
        Assert.Equal(1, pill.Face.Opacity);
        Assert.InRange(pill.Revealed, .99, .999999);
        h.Step(100);
        Assert.Equal(1, pill.Revealed);
        h.Controller.StopRun();
        h.Wait(run);
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-326")] // CSS:469-471: ring retained .45s, then .3s fade/shrink; check .25/.32 after .45s.
    public void Completion_retains_the_ring_for_450ms_then_crossfades_and_scales_the_check() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, h =>
    {
        h.Install.HoldOn("P1");
        var pill = new SkillsProgressPill(h.Page.Model!, _ => { });
        h.Root.Children.Add(pill);
        h.Controller.OpenDownloadAll();
        var run = h.Controller.Continue();
        h.Wait(h.Install.Started("P1"));
        h.Page.Model!.DialogHidden = true;
        h.Settle(); h.Step(1000);
        h.Install.Release("P1"); h.Wait(run);
        Assert.Equal(1, pill.RingLead.Opacity);
        Assert.Equal(0, pill.CheckLead.Opacity);
        h.Step(449);
        Assert.Equal(1, pill.RingLead.Opacity);
        Assert.Equal(0, pill.CheckLead.Opacity);
        h.Step(1);
        Assert.Equal(1, pill.RingLead.Opacity);
        Assert.Equal(0, pill.CheckLead.Opacity);
        h.Step(125);
        Assert.Equal(1 - .7056111253727029, pill.RingLead.Opacity, 6);
        Assert.Equal(.802403387584857, pill.CheckLead.Opacity, 6);
        Assert.Equal(.5 + .5 * .9137184425875109, ((ScaleTransform)pill.CheckLead.RenderTransform!).ScaleX, 6);
        h.Step(125);
        Assert.Equal(1, pill.CheckLead.Opacity);
        Assert.True(pill.RingLead.Opacity > 0);
        h.Step(50);
        Assert.Equal(0, pill.RingLead.Opacity);
        Assert.Equal(.6, ((ScaleTransform)pill.RingLead.RenderTransform!).ScaleX, 6);
        h.Step(20);
        Assert.Equal(1, ((ScaleTransform)pill.CheckLead.RenderTransform!).ScaleX);
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-326")] // CSS:473-476: .22s ease opacity; .32s --ez scale .92/translate8; origin30px bottom.
    public void Popup_opens_and_closes_with_its_locked_curves_instead_of_the_default_presenter() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var pill = new SkillsProgressPill(h.Page.Model!, _ => { });
        h.Root.Children.Add(pill);
        var compact = pill.BuildPopover();
        h.Root.Children.Add(compact);
        var motion = Assert.IsType<SkillsPopoverMotion>(pill.Popover.GetValue(Presentation.MotionProperty));
        motion.Open(compact);
        Assert.Equal(0, compact.Opacity);
        h.Step(110);
        Assert.Equal(.802403387584857, compact.Opacity, 6);
        var transform = (TransformGroup)compact.RenderTransform!;
        Assert.Equal(.92 + .08 * .885109728292122, ((ScaleTransform)transform.Children[0]).ScaleX, 6);
        Assert.Equal(8 * (1 - .885109728292122), ((TranslateTransform)transform.Children[1]).Y, 6);
        h.Step(110);
        Assert.Equal(1, compact.Opacity);
        h.Step(100);
        Assert.Equal(1, ((ScaleTransform)transform.Children[0]).ScaleX);
        var closed = false;
        motion.Close(compact, () => closed = true);
        h.Step(220);
        Assert.Equal(0, compact.Opacity);
        Assert.False(closed);
        h.Step(100);
        Assert.True(closed);
    }, motion: true);

    [Fact]
    [Trait("Decision", "ED-261")] // Locked reduced duration .01s; OD-0076 stills displacement and keeps a fade.
    public void Reduced_motion_keeps_the_arrival_delay_and_short_fade_without_displacement() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        h.Install.HoldOn("S1");
        var pill = new SkillsProgressPill(h.Page.Model!, _ => { });
        h.Root.Children.Add(pill);
        h.Controller.OpenDownloadAll(); var run = h.Controller.Continue(); h.Wait(h.Install.Started("S1"));
        h.Page.Model!.DialogHidden = true; h.Settle();
        h.Step(120);
        Assert.Equal(0, pill.Face.Opacity);
        h.Step(5);
        Assert.Equal(.802403387584857, pill.Face.Opacity, 6);
        Assert.Null(pill.Face.RenderTransform);
        h.Step(5);
        Assert.Equal(1, pill.Face.Opacity);
        var compact = pill.BuildPopover(); h.Root.Children.Add(compact);
        var motion = Assert.IsType<SkillsPopoverMotion>(pill.Popover.GetValue(Presentation.MotionProperty));
        motion.Open(compact); h.Step(5);
        Assert.Equal(.802403387584857, compact.Opacity, 6);
        Assert.Null(compact.RenderTransform);
        h.Step(5); Assert.Equal(1, compact.Opacity);
        h.Controller.StopRun(); h.Wait(run);
    }, motion: true, reduceMotion: true);
}
