using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.App.Tests.Port.Skills;

[Collection("Headless")]
public sealed class SkillsProgressPillTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-326")] // OD-0099 Minimize A; sd-page.css:456-493, sd-page.js:326,364-370
    public void Hide_keeps_the_run_visible_in_both_pills_and_enlarge_reopens_the_same_dialog() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        h.Install.HoldOn("S1");
        var model = h.Page.Model!;
        LibrarySection? opened = null;
        var main = new SkillsProgressPill(model, page => opened = page);
        var settings = new SkillsProgressPill(model, page => opened = page);
        h.Root.Children.Add(main);
        h.Root.Children.Add(settings);
        h.Controller.OpenDownloadAll();
        var run = h.Controller.Continue();
        h.Wait(h.Install.Started("S1"));
        Assert.True(h.Press("dialog-hide"));
        Assert.True(main.IsShown);
        Assert.True(settings.IsShown);
        Assert.Equal("Downloading 1 of 3", main.LabelText);
        Assert.Equal(34, main.Button.Height);
        var compact = main.BuildPopover();
        Assert.Equal(360, compact.Width);
        Assert.Equal(h.Controller.Dialog.ProgressText, compact.ProgressText);
        compact.Enlarge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        h.Settle();
        Assert.Equal(LibrarySection.Skills, opened);
        Assert.False(model.DialogHidden);
        Assert.NotNull(h.Page.Sheet);
        Assert.False(main.IsShown);
        Assert.Single(h.Install.Calls);
        h.Controller.StopRun();
        h.Wait(run);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_hidden_run_keeps_its_completion_and_failure_visible_until_close() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, h =>
    {
        h.Install.HoldOn("P1");
        var model = h.Page.Model!;
        var pill = new SkillsProgressPill(model, _ => { });
        h.Root.Children.Add(pill);
        h.Controller.OpenDownloadAll();
        var run = h.Controller.Continue();
        h.Wait(h.Install.Started("P1"));
        h.Press("dialog-hide");
        h.Install.Release("P1");
        h.Wait(run);
        Assert.True(pill.IsShown);
        Assert.Equal("Downloaded", pill.LabelText);
        Assert.Null(h.Page.Sheet);
        var compact = pill.BuildPopover();
        Assert.Equal("done", compact.PhaseKey);
        compact.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        h.Settle();
        Assert.False(pill.IsShown);
    });

    [Fact]
    [Trait("Decision", "ED-261")]
    public void The_pill_arrival_eases_and_reduced_motion_still_fades() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        h.Install.HoldOn("S1");
        var pill = new SkillsProgressPill(h.Page.Model!, _ => { });
        h.Root.Children.Add(pill);
        h.Settle();
        h.Controller.OpenDownloadAll();
        var run = h.Controller.Continue();
        h.Wait(h.Install.Started("S1"));
        h.Page.Model!.DialogHidden = true;
        h.Settle();
        Assert.Equal(0, pill.Revealed);
        h.Step(100);
        Assert.Equal(0, pill.Revealed); // Approved Round 2 correction: CSS:459 holds the arrival for .12s.
        h.Step(20);
        Assert.Equal(0, pill.Revealed);
        h.Step(100);
        Assert.Equal(.7791312484017134, pill.Revealed, 6); // --ez(.25): .1s into the locked .4s movement.
        h.Step(500);
        Assert.Equal(1, pill.Revealed);
        h.Controller.StopRun();
        h.Wait(run);
    }, motion: true);
}
