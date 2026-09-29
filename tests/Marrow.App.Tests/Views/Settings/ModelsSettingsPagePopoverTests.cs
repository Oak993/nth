using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// PinnedModelRow's effort popover (App/Views/Settings/ModelsSettingsPage.swift:192-194, :217-243): the row's block
// opens an EffortSliderCard whose effort, fast-mode and reset write through the settings' per-model preference.
// Before Job STUBS this was "left unwired" (a BLOCKED comment in ModelsSettingsPage.cs) because EffortSliderCard
// was still a stub; it is real now (Views/Composer/EffortSlider.cs).
[Collection("Headless")]
public sealed class ModelsSettingsPagePopoverTests(HeadlessSession headless)
{
    private static readonly ModelPin Pin = new(ProviderKind.Claude, "fixture");

    private void Run(bool supportsFast, Action<ModelsSettingsPopoverRig> test) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            model.Providers.UpdateCatalog(new ValueList<ModelOption>([
                new ModelOption("fixture", "Fixture", efforts: new ValueList<string>(["low", "medium", "high"]),
                    defaultEffort: "high", fastTier: supportsFast ? "fast" : null)]), ProviderKind.Claude);
            model.Settings.AddToModelList(Pin);
        });
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var page = new ModelsSettingsPage("");
        host.Child = page;
        var window = new AvaloniaWindow { Content = host, Width = 500, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { test(new ModelsSettingsPopoverRig(harness.Main, model, page, presenter)); }
        finally { window.Close(); }
    });

    private static Button BlockButton(ModelsSettingsPage page) =>
        LogicalQuery.SelfAndDescendants<Button>(page).Single(button =>
            LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == "Fixture"));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // The recorded popup's content is the card, at the source's 330 pt width (:239).
    private static EffortSliderCard OpenCard(ModelsSettingsPopoverRig rig)
    {
        rig.Main.OnMain(() => Click(BlockButton(rig.Page))); // :192-194 Button { isPresented = true }; presented on the next main turn
        var (popup, presented) = Assert.Single(rig.Presenter.Calls);
        Assert.True(presented);
        Assert.Equal(PlacementMode.Right, popup.Placement); // :217 arrowEdge: .trailing, as SidebarView.cs and HydraSettingsPage.cs read it
        var card = Assert.Single(LogicalQuery.SelfAndDescendants<EffortSliderCard>(popup.Child!));
        Assert.Equal(330, card.Width); // :242 .frame(width: 330)
        return card;
    }

    // The popup cannot open headless (Avalonia 12.1.2, no overlay layer), so the card it would host is shown in a
    // window of its own, which is also what makes the card build its effort title.
    private static AvaloniaWindow Mount(ModelsSettingsPopoverRig rig, EffortSliderCard card)
    {
        var popup = rig.Presenter.Calls.Single().Popup;
        AvaloniaWindow window = null!;
        rig.Main.OnMain(() =>
        {
            popup.Child = null;
            window = new AvaloniaWindow { Content = card, Width = 330, Height = 260 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
        });
        return window;
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // App/Views/Settings/ModelsSettingsPage.swift:192-194,:217-243
    public void Clicking_the_block_presents_the_effort_card_for_that_model_with_its_catalog_facts() => Run(supportsFast: true, rig =>
    {
        var card = OpenCard(rig);
        var window = Mount(rig, card);
        try
        {
            var titles = card.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
            Assert.Contains("Fixture", titles); // :219 modelName: option?.shortName ?? pin.modelID
            Assert.Contains("High", titles); // :221-222 the catalog's efforts and defaultEffort "high": no chosen effort, so the default is the stop (the fallback would read Medium)
            Assert.Contains(card.GetVisualDescendants().OfType<Button>(), b => ToolTip.GetTip(b) is "Fast mode"); // :223 supportsFast
        }
        finally { rig.Main.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // App/Views/Settings/ModelsSettingsPage.swift:217-243
    public void A_model_without_a_fast_tier_offers_no_fast_toggle() => Run(supportsFast: false, rig =>
    {
        var card = OpenCard(rig);
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(card), b => ToolTip.GetTip(b) is "Fast mode" or "Fast mode is on"); // :223 option?.supportsFast ?? false
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // App/Views/Settings/ModelsSettingsPage.swift:232-238
    public void The_fast_toggle_writes_the_models_preference_and_keeps_its_effort() => Run(supportsFast: true, rig =>
    {
        rig.Main.OnMain(() => rig.Model.Settings.SetPreference(new ModelPreference { Effort = "high" }, Pin.Provider, Pin.ModelID));
        var card = OpenCard(rig);
        var fast = LogicalQuery.SelfAndDescendants<Button>(card).Single(b => ToolTip.GetTip(b) is "Fast mode");
        rig.Main.OnMain(() => Click(fast));
        var updated = rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID);
        Assert.True(updated.FastMode); // :234-238 set { updated.fastMode = isOn }
        Assert.Equal("high", updated.Effort); // the read-modify-write leaves the other field alone
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // App/Views/Settings/ModelsSettingsPage.swift:224-230
    public void Dragging_the_slider_writes_the_models_effort_and_keeps_fast_mode() => Run(supportsFast: true, rig =>
    {
        rig.Main.OnMain(() => rig.Model.Settings.SetPreference(new ModelPreference { FastMode = true }, Pin.Provider, Pin.ModelID));
        var card = OpenCard(rig);
        // The popup cannot open headless (Avalonia 12.1.2, no overlay layer), so the card it would host is shown in a window of its own.
        var window = Mount(rig, card);
        try
        {
            var slider = LogicalQuery.SelfAndDescendants<EffortSlider>(card).Single();
            var bounds = new Rect(slider.TranslatePoint(default, window)!.Value, slider.Bounds.Size);
            rig.Main.OnMain(() =>
            {
                window.MouseDown(new Point(bounds.X + 5, bounds.Center.Y), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(new Point(bounds.X + 5, bounds.Center.Y), MouseButton.Left, RawInputModifiers.None);
            });
            var updated = rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID);
            Assert.Equal("low", updated.Effort); // :226-230 set { updated.effort = effort }
            Assert.True(updated.FastMode);
        }
        finally { rig.Main.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // App/Views/Settings/ModelsSettingsPage.swift:240
    public void Reset_clears_the_models_preference_to_its_defaults() => Run(supportsFast: true, rig =>
    {
        rig.Main.OnMain(() => rig.Model.Settings.SetPreference(new ModelPreference { Effort = "low", FastMode = true }, Pin.Provider, Pin.ModelID));
        var card = OpenCard(rig);
        var reset = LogicalQuery.SelfAndDescendants<Button>(card).Single(b => ToolTip.GetTip(b) is "Reset to default");
        rig.Main.OnMain(() => Click(reset));
        Assert.Equal(new ModelPreference(), rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID)); // :240 setPreference(ModelPreference(), ...)
    });
}
