using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-2.2, CZ-3.2 (the chrome row's Preview state control), CZ-3.3 (what the page holds, in order), CZ-4.6 (the control and the preview's phase are one pick).
[Collection("Headless")]
public sealed class CustomizationPageTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T5, CZ-3.2: the chrome row has the Preview state control, Rest | Typing | Running a turn, at Rest
    [Trait("Spec", "CZ-T5")]
    public void The_chrome_row_carries_the_Preview_state_control_at_Rest() => headless.OnUiThread(() =>
    {
        var page = new CustomizationSettingsPage();
        Assert.True(page.HasChromeControls);
        Assert.True(SettingsView.HasChromeControls(SettingsPage.Customization, page));
        var pick = Assert.IsType<CzSegmentedControl>(Assert.Single(SettingsView.ChromeAccessoriesOf(page)));
        Assert.Same(page.PreviewStatePick, pick);
        Assert.Equal("Preview state", AutomationProperties.GetName(pick));
        Assert.Equal(["Rest", "Typing", "Running a turn"], pick.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.Equal("rest", pick.SelectedId);
        Assert.True(pick.Small);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T2 / SA-1: the page is ready, so the sidebar lists it and a search finds it
    [Trait("Spec", "CZ-T2")]
    public void The_page_is_ready_and_the_sidebar_lists_it()
    {
        Assert.True(CustomizationSettingsPage.IsReady);
        Assert.True(SettingsPage.Customization.IsReady);
        Assert.Contains(SettingsPage.Customization, SettingsSidebar.Pages(""));
        Assert.Equal([SettingsPage.Customization], SettingsSidebar.Pages("glow"));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-2.2, CZ-3.2: the Settings window lists the page, opens it when asked, builds it from the app's model and puts the Preview state control in its chrome row
    public void The_settings_window_opens_the_page_and_shows_the_control_in_the_chrome_row() => SettingsViewRig.Run(headless, (view, model, sidebar) =>
    {
        Assert.Contains("Customization", SettingsViewRig.RowTitles(sidebar));
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Customization;
        SettingsViewRig.Sync();
        Assert.Equal(SettingsPage.Customization, SettingsViewRig.CurrentPage(view));
        var page = Assert.Single(LogicalQuery.SelfAndDescendants<CustomizationSettingsPage>(view));
        Assert.NotNull(page.Stage);
        var pick = Assert.Single(LogicalQuery.SelfAndDescendants<CzSegmentedControl>(view), c => AutomationProperties.GetName(c) == "Preview state");
        Assert.Same(page.PreviewStatePick, pick);
        Assert.True(pick.IsVisible);
        // CZ-3.4: the heading sits 60 below the sheet's top, once (the pinned block carries the inset the window gives a page with chrome controls)
        ((Avalonia.Controls.Window)Avalonia.Controls.TopLevel.GetTopLevel(page)!).UpdateLayout();
        var scroll = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<ScrollViewer>(page)!;
        Assert.Equal(60, Avalonia.VisualExtensions.TranslatePoint(page.Stage!.HeadingBox, default, scroll)!.Value.Y);
        Assert.Equal(60, Marrow.UI.Chrome.Chrome.ChromeTopPadding + Marrow.UI.Chrome.Chrome.CapsuleHeight + 16);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-3.2, CZ-4.6, CZ-T35: the chrome control is the session's pick, both ways (touching Working dots moves it)
    [Trait("Spec", "CZ-T35")]
    public void The_chrome_control_and_the_previews_phase_are_one_pick() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var session = rig.Page.Session!;
        Assert.Equal(PreviewState.Rest, session.State);
        rig.Do(() => rig.Page.PreviewStatePick.Select("typing"));
        Assert.Equal(PreviewState.Typing, session.State);
        Assert.Equal(ComposerLightPhase.Typing, rig.Page.Stage!.Preview.Light.Phase);
        rig.Do(() => rig.Page.PreviewStatePick.Select("running"));
        Assert.Equal(PreviewState.Running, session.State);
        Assert.Equal(ComposerLightPhase.Running, rig.Page.Stage!.Preview.Light.Phase);
        rig.Do(() => rig.Page.PreviewStatePick.Select("rest"));
        Assert.Equal(PreviewState.Rest, session.State);
        // the session can move the pick: touching a Working dots control switches to Running a turn (CZ-4.6, CZ-5's hook)
        rig.Do(session.WorkingDotsTouched);
        Assert.Equal("running", rig.Page.PreviewStatePick.SelectedId);
        Assert.Equal(PreviewState.Running, session.State);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-3.3, CZ-T39: the pinned block first, then Style, then the sections of each style in order; the headings are the design's
    [Trait("Spec", "CZ-T39")]
    public void The_page_holds_the_stage_then_Style_then_the_style_sections_in_order() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var page = rig.Page;
        var kids = page.Sticky.Children;
        Assert.Same(page.Stage, kids[0]);
        Assert.Equal("Composer light", page.Stage!.Heading.Text);
        Assert.Equal(372, page.Stage.Bounds.Height);
        Assert.Equal(520, page.Stage.Bounds.Width);
        Assert.Contains(page.StyleCards!, Descendants(kids[1]));
        Assert.Equal(["Style"], Texts(kids[1]).Where(t => t == "Style"));
        var sections = kids.OfType<CzSection>().ToList();
        Assert.Equal(
            ["Look", "Pace and strength", null, null, "Look", "Motion", "Brightness by moment", null, null, "Look", "Text and light", "Wallpaper", null, null, "Voice orb", "Working dots"],
            sections.Select(s => s.Title));
        Assert.Equal(sections, page.Beam!.Sections.Concat(page.Aura!.Sections).Concat(page.Shader!.Sections).Append(page.Off!).Append(page.Voice!.Section).Append(page.Dots!.Section)); // CZ-5 adds the Working dots section at the end of the sticky container; OD-0139 the shader lights' sections after the Uiverse light's
        // the rest after the sticky container holds the Speech section and then the capsule section (CZ-8)
        Assert.NotNull(page.Rest);
        Assert.Collection(page.Rest.Children, // CZ-8: the Speech section is first after the sticky container, then "Pill in the composer" (multi-account step 3a)
            child => Assert.Same(page.Speech!.Section, child),
            child => Assert.IsType<Marrow.App.Port.MultiAccount.Capsule.PillCapsuleSection>(child));
        // the stage sits 2 above the Style heading (CZ-3.4)
        Assert.Equal(2, page.Stage.Margin.Bottom);
        Assert.True(page.Stage.Bounds.Bottom + 2 <= kids[1].Bounds.Top + 0.5);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-3.4 / CZ-13.1: the stage wears the app's own settings (a change in the app's store shows in the preview's light)
    public void The_stage_follows_the_apps_settings() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var changed = ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura };
        rig.SetLight(changed);
        Assert.Equal(changed, rig.Page.Stage!.Preview.Light.Settings);
        Assert.Equal("Uiverse light · Original · 12 s a turn", rig.Page.Stage.Caption.Text);
    });

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual root)
    {
        yield return root;
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root).OfType<Avalonia.Visual>())
            foreach (var inner in Descendants(child)) yield return inner;
    }

    private static IEnumerable<string> Texts(Avalonia.Visual root) => Descendants(root).OfType<TextBlock>().Select(t => t.Text ?? "");
}
