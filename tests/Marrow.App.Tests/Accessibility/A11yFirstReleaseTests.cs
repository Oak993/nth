using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port;
using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Accessibility;

[Collection("Headless")]
public sealed class A11yFirstReleaseTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "CZ-4")]
    public void A11Y_Customization_switch_has_visible_keyboard_focus() => headless.OnUiThread(() =>
    {
        using var preference = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        using var rig = new CustomizationPageRig(reduceMotion: true);
        rig.Do(() =>
        {
            // Production Settings scrolls this long page. The unscrolled rig puts its first switch at y=944,
            // below the 900 px capture: use a real scroll viewport before asserting rendered focus.
            rig.Host.Child = null;
            rig.Host.Child = new ScrollViewer { Content = rig.Page };
            rig.Window.UpdateLayout();
            var toggle = A11yReview.Tree(rig.Page).OfType<SmallSwitch>().First(c => c.IsEffectivelyEnabled && c.IsEffectivelyVisible);
            toggle.BringIntoView();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            rig.Window.UpdateLayout();
            Assert.True(new Avalonia.Rect(rig.Window.Bounds.Size).Contains(new Avalonia.Rect(toggle.TranslatePoint(default, rig.Window)!.Value, toggle.Bounds.Size)));
            A11yReview.VisibleFocus(rig.Window, toggle);
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "VT-14")]
    public void A11Y_Voice_model_download_progress_is_announced() => headless.OnUiThread(() =>
    {
        var column = new VoiceStateColumn();
        var window = new Avalonia.Controls.Window { Content = column, Width = 220, Height = 100 };
        try
        {
            window.Show();
            column.SetState(VoiceModelState.Downloading, .37);
            window.UpdateLayout();
            Assert.Equal("37%", column.PercentText);
            A11yReview.HasLiveText(column, "37%");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "UE-5")]
    public void A11Y_Update_pill_has_visible_keyboard_focus() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var item = new FakeUpdateItem("c", "Claude Code", UpdateItemKind.ProviderCli, "1.0.0", "1.1.0");
        var service = harness.Main.OnMain(() => new UpdateEverythingService([item], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        harness.Main.OnMain(() => service.CheckNow());
        harness.Main.RunUntilIdle();
        harness.Main.OnMain(() =>
        {
            var pill = new UpdatePill();
            var host = new Border { Child = pill };
            AppEnvironment.SetAppModel(host, model);
            EnvironmentValues.SetAccessibilityReduceMotion(host, true);
            var window = new Avalonia.Controls.Window { Content = host, Width = 280, Height = 120 };
            try
            {
                window.Show();
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(pill.IsShown);
                Assert.Equal(pill.LabelText, AutomationProperties.GetName(pill.Button));
                A11yReview.VisibleFocus(window, pill.Button);
            }
            finally { window.Close(); }
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "UE-4")]
    public void A11Y_Updates_status_changes_are_announced() => headless.OnUiThread(() =>
    {
        var status = new UpdatesStatusCell(RowStatus.Current, "Up to date");
        var window = new Avalonia.Controls.Window { Content = status, Width = 240, Height = 100 };
        try
        {
            window.Show();
            status.SetStatus(RowStatus.Downloading, "Downloading 37%");
            window.UpdateLayout();
            Assert.Equal("Downloading 37%", status.Text);
            A11yReview.HasLiveText(status, "Downloading 37%");
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "GR-2")]
    public void A11Y_Getting_ready_exposes_step_progress_to_a_live_region() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig(reduceMotion: true);
        rig.Sheet.Report(GettingReadyRig.Event(rig.Plan[0], 0, rig.Plan.Count, FirstRunStepState.Running, 0));
        rig.Frame(1.0 / 60);
        Assert.Equal(FirstRunStepState.Running, rig.Sheet.Rows[0].State);
        A11yReview.HasLiveText(rig.Sheet, rig.Plan[0].Step.Name);
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "WL-1")]
    public void A11Y_Wallpaper_choose_controls_have_explicit_purpose_names() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var buttons = A11yReview.Tree(scope.Card).OfType<Button>().ToArray();
        Assert.Equal(2, buttons.Length); // the upstream preview and Choose… action
        Assert.All(buttons, button => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)),
            "The wallpaper preview/chooser has no AutomationProperties.Name."));
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "WL-1")]
    public void A11Y_Wallpaper_chooser_has_visible_keyboard_focus() => headless.OnUiThread(() =>
    {
        using var scope = new WallpaperCardScope();
        var choose = A11yReview.Tree(scope.Card).OfType<Button>().Last();
        A11yReview.VisibleFocus(scope.Window, choose);
    });

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Review", "UE-6")]
    public void A11Y_Whats_new_cards_have_visible_keyboard_focus() => headless.OnUiThread(() =>
    {
        var cards = new UpdateReleaseSectionCards(UpdateReleaseNotesDigest.Parse("## New features\n- Keyboard test\n"), "1.2.3");
        var window = new Avalonia.Controls.Window { Content = cards, Width = 560, Height = 300 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var card = A11yReview.Tree(cards).OfType<Button>().First(c => c.IsEnabled);
            Assert.Equal("New features", AutomationProperties.GetName(card));
            A11yReview.VisibleFocus(window, card);
        }
        finally { window.Close(); }
    });
}
