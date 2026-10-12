using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.Media;
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
using Marrow.UI.Chrome;
using Marrow.UI.Common;
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

    private void Run(bool supportsFast, Action<ModelsSettingsPopoverRig> test, bool known = true, bool realPopup = false) => headless.OnUiThread(() =>
    {
        var manualFrames = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manualFrames);
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(realPopup ? Presentation.Presenter.Current : presenter);
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            model.Providers.UpdateCatalog(known ? new ValueList<ModelOption>([
                new ModelOption("fixture", "Fixture", efforts: new ValueList<string>(["low", "medium", "high"]),
                    defaultEffort: "high", fastTier: supportsFast ? "fast" : null)]) : ValueList<ModelOption>.Empty, ProviderKind.Claude);
            model.Settings.AddToModelList(Pin);
        });
        var host = new Border { Background = Brushes.Transparent };
        AppEnvironment.SetAppModel(host, model);
        var page = new ModelsSettingsPage("");
        host.Child = page;
        var window = new AvaloniaWindow { Content = host, Width = 500, Height = 400, Background = Brushes.Transparent };
        if (realPopup) SettingsProviderPopupLayers.Install(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        harness.Main.OnMain(() => { }); // the page tracks the model and rebuilds its rows once on the first main turn (batch 25); settle that before a row's block is found
        try { test(new ModelsSettingsPopoverRig(harness.Main, model, page, presenter) { AdvanceFrames = manualFrames.Advance }); }
        finally { harness.Main.OnMain(window.Close); }
    });

    private static Button BlockButton(ModelsSettingsPage page, string title = "Fixture") =>
        LogicalQuery.SelfAndDescendants<Button>(page).Single(button =>
            LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == title)
            && !LogicalQuery.SelfAndDescendants<EffortSliderCard>(page).Any(card => LogicalQuery.SelfAndDescendants<Button>(card).Contains(button))); // the open card's own model line (OD-0139) shows the name too, and is no block

    private static void Click(Button button, Control? target = null)
    {
        var window = Assert.IsType<AvaloniaWindow>(TopLevel.GetTopLevel(button));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var surface = target ?? button;
        var bounds = new Rect(surface.TranslatePoint(default, window)!.Value, surface.Bounds.Size);
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(bounds.Center));
        Assert.Contains(button, hit.GetSelfAndVisualAncestors());
        window.MouseMove(bounds.Center, RawInputModifiers.None);
        window.MouseDown(bounds.Center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(bounds.Center, MouseButton.Left, RawInputModifiers.None);
    }

    private static void ChooseLow(EffortSlider slider)
    {
        var window = Assert.IsType<AvaloniaWindow>(TopLevel.GetTopLevel(slider));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var bounds = new Rect(slider.TranslatePoint(default, window)!.Value, slider.Bounds.Size);
        var point = new Point(bounds.X + 5, bounds.Center.Y);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.Contains(slider, hit.GetSelfAndVisualAncestors());
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    // The recorded popup's content is the card, at the source's 330 pt width (:239).
    private static EffortSliderCard OpenCard(ModelsSettingsPopoverRig rig, string title = "Fixture")
    {
        var block = BlockButton(rig.Page, title);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(block.Background).Color);
        Assert.Equal(new Thickness(0), block.BorderThickness);
        Assert.Equal(new Thickness(0), block.Padding);
        rig.Main.OnMain(() => Click(block)); // :192-194 Button { isPresented = true }; presented on the next main turn
        var (popup, presented) = Assert.Single(rig.Presenter.Calls);
        Assert.True(presented);
        Assert.Same(BlockButton(rig.Page, title), popup.PlacementTarget);
        Assert.Equal(PlacementMode.Right, popup.Placement); // :217 arrowEdge: .trailing, as SidebarView.cs and HydraSettingsPage.cs read it
        var card = Assert.Single(LogicalQuery.SelfAndDescendants<EffortSliderCard>(popup.Child!));
        Assert.True(Popovers.GetIsCapsuleButtonBorderShape(card)); // :243 presentedChrome
        Assert.Equal(330, card.Width); // :242 .frame(width: 330)
        return card;
    }

    // For the recording-presenter cases, mount the production card in its own window so its view tracks attach.
    // The separate real-popup case below uses the production presenter and the existing headless overlay fixture.
    private static AvaloniaWindow Mount(ModelsSettingsPopoverRig rig, EffortSliderCard card)
    {
        var popup = rig.Presenter.Calls.Single().Popup;
        AvaloniaWindow window = null!;
        rig.Main.OnMain(() =>
        {
            popup.Child = null;
            PopoverCardContent.Lift(card); // the card the presenter hung the view on lets it go
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
            Assert.Equal(Pin.Provider, Assert.Single(LogicalQuery.SelfAndDescendants<ProviderIcon>(card)).Provider);
            Assert.Empty(LogicalQuery.SelfAndDescendants<HydraMarkImage>(card));
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(card), image => image.SymbolName == "chevron.right");
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(card), button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == "High")); // OD-0139: the effort title is no button
            Assert.False(LogicalQuery.SelfAndDescendants<Button>(card).Single(button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == "Fixture")).IsEnabled); // the model line is the disabled button, without a handler
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
        var window = Mount(rig, card);
        try
        {
            var fast = LogicalQuery.SelfAndDescendants<Button>(card).Single(b => ToolTip.GetTip(b) is "Fast mode");
            rig.Main.OnMain(() => Click(fast));
            var updated = rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID);
            Assert.True(updated.FastMode); // :234-238 set { updated.fastMode = isOn }
            Assert.Equal("high", updated.Effort); // the read-modify-write leaves the other field alone
        }
        finally { rig.Main.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // App/Views/Settings/ModelsSettingsPage.swift:224-230
    public void Dragging_the_slider_writes_the_models_effort_and_keeps_fast_mode() => Run(supportsFast: true, rig =>
    {
        rig.Main.OnMain(() => rig.Model.Settings.SetPreference(new ModelPreference { FastMode = true }, Pin.Provider, Pin.ModelID));
        var card = OpenCard(rig);
        // Mount the recorded production card for hit-tested slider interaction.
        var window = Mount(rig, card);
        try
        {
            var slider = LogicalQuery.SelfAndDescendants<EffortSlider>(card).Single();
            rig.Main.OnMain(() => ChooseLow(slider));
            var updated = rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID);
            Assert.Equal("low", updated.Effort); // :226-230 set { updated.effort = effort }
            Assert.True(updated.FastMode);
            Assert.Equal(updated, rig.Main.OnMain(() => new AppSettings()).Preference(Pin.Provider, Pin.ModelID));
        }
        finally { rig.Main.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // App/Views/Settings/ModelsSettingsPage.swift:240
    public void Reset_clears_the_models_preference_to_its_defaults() => Run(supportsFast: true, rig =>
    {
        rig.Main.OnMain(() => rig.Model.Settings.SetPreference(new ModelPreference { Effort = "low", FastMode = true }, Pin.Provider, Pin.ModelID));
        var card = OpenCard(rig);
        var window = Mount(rig, card);
        try
        {
            var reset = LogicalQuery.SelfAndDescendants<Button>(card).Single(b => ToolTip.GetTip(b) is "Reset to default");
            Assert.Equal("arrow.counterclockwise", Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(Assert.Single(LogicalQuery.SelfAndDescendants<RoundIconButton>(card)))).SymbolName);
            Assert.Single(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(Assert.Single(LogicalQuery.SelfAndDescendants<RoundIconButton>(card))));
            var fast = Assert.Single(LogicalQuery.SelfAndDescendants<FastModeButton>(card));
            Assert.Equal(0, Grid.GetColumn(fast));
            Assert.Equal(2, Grid.GetColumn(Assert.Single(LogicalQuery.SelfAndDescendants<RoundIconButton>(card))));
            Assert.True(reset.TranslatePoint(default, window)!.Value.X > fast.TranslatePoint(default, window)!.Value.X);
            rig.Main.OnMain(() => Click(reset));
            Assert.Equal(new ModelPreference(), rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID)); // :240 setPreference(ModelPreference(), ...)
            Assert.False(rig.Main.OnMain(() => new AppSettings()).ModelPreferences.ContainsKey("claude/fixture"));
            Assert.False(rig.Model.Settings.ModelPreferences.ContainsKey("claude/fixture")); // AppSettings.swift:859-862 removes the default entry
        }
        finally { rig.Main.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // ModelsSettingsPage.swift:219-223, EffortSlider.swift:510,516,575-578,620-624
    public void An_unknown_model_uses_its_raw_id_and_the_no_efforts_form_without_fast_or_pair_or_title_action() => Run(false, rig =>
    {
        var card = OpenCard(rig, "fixture");
        var window = Mount(rig, card);
        try
        {
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(card).Select(text => text.Text).ToArray();
            Assert.Contains("fixture", texts);
            Assert.Contains("Standard", texts);
            Assert.Contains("This model has one reasoning level.", texts);
            Assert.Empty(LogicalQuery.SelfAndDescendants<EffortSlider>(card));
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(card), b => ToolTip.GetTip(b) is "Fast mode" or "Fast mode is on" or "Choose a model");
            Assert.Empty(LogicalQuery.SelfAndDescendants<HydraMarkImage>(card));
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(card), image => image.SymbolName == "chevron.right");
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(card), button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == "Standard")); // OD-0139: the effort title is no button
            Assert.False(LogicalQuery.SelfAndDescendants<Button>(card).Single(button =>
                LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == "fixture")).IsEnabled); // the model line is the disabled button, without a handler
            Assert.Equal(Pin.Provider, Assert.Single(LogicalQuery.SelfAndDescendants<ProviderIcon>(card)).Provider);
        }
        finally { rig.Main.OnMain(window.Close); }
    }, known: false);

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // ModelsSettingsPage.swift:224-238; AppSettings.swift:859-867
    public void The_card_reads_fresh_preferences_persists_both_fields_and_keeps_other_model_keys() => Run(true, rig =>
    {
        var other = new ModelPreference { Effort = "medium", FastMode = true };
        rig.Main.OnMain(() => rig.Model.Settings.SetPreference(other, ProviderKind.Codex, Pin.ModelID));
        var card = OpenCard(rig);
        var window = Mount(rig, card);
        try
        {
            rig.Main.OnMain(() => rig.Model.Settings.SetPreference(new ModelPreference { Effort = "low" }, Pin.Provider, Pin.ModelID));
            Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(card), text => text.Text == "Low");
            rig.Main.OnMain(() => Click(LogicalQuery.SelfAndDescendants<Button>(card).Single(button => ToolTip.GetTip(button) is "Fast mode")));
            var stored = rig.Main.OnMain(() => new AppSettings()); // reload the actual settings store after the card's write
            Assert.Equal(new ModelPreference { Effort = "low", FastMode = true }, stored.Preference(Pin.Provider, Pin.ModelID));
            Assert.Equal(other, stored.Preference(ProviderKind.Codex, Pin.ModelID));
            using var dictionary = JsonDocument.Parse(Assert.IsType<byte[]>(UserDefaults.Standard.Current.Object("modelPreferences")));
            var entry = dictionary.RootElement.GetProperty("claude/fixture");
            Assert.Equal("low", entry.GetProperty("effort").GetString());
            Assert.True(entry.GetProperty("fastMode").GetBoolean());
            Assert.True(dictionary.RootElement.TryGetProperty("codex/fixture", out _));
        }
        finally { rig.Main.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-189")] // ModelsSettingsPage.swift:192-244, PlainControls implements SwiftUI light dismissal
    public void A_real_headless_popup_keeps_changes_on_outside_dismissal_and_reopens_from_the_same_row() => Run(true, rig =>
    {
        var block = BlockButton(rig.Page);
        var window = Assert.IsType<AvaloniaWindow>(TopLevel.GetTopLevel(block));
        rig.Main.OnMain(() => Click(block));
        rig.FinishAnimations(); // the real popover grows in from its arrow first (PopoverAppear)
        var popup = Assert.Single(LogicalQuery.SelfAndDescendants<Popup>(rig.Page));
        Assert.True(popup.IsOpen);
        Assert.Same(block, popup.PlacementTarget);
        Assert.Equal(PlacementMode.Right, popup.Placement);
        var card = Assert.IsType<EffortSliderCard>(Assert.IsType<Marrow.UI.Port.PopoverCard>(popup.Child).Child); // OD-0095: the presenter hangs it on the panel card
        Assert.Equal(330, card.Width);
        rig.Main.OnMain(() => ChooseLow(Assert.Single(LogicalQuery.SelfAndDescendants<EffortSlider>(card))));
        Assert.Equal("low", rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID).Effort);
        rig.Main.OnMain(() => Click(LogicalQuery.SelfAndDescendants<Button>(card).Single(button => ToolTip.GetTip(button) is "Fast mode")));
        Assert.True(rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID).FastMode);
        rig.Main.OnMain(() =>
        {
            Assert.NotNull(window.InputHitTest(new Point(2, 2))); // a real routed outside press, including without Avalonia's blocking overlay
            window.MouseDown(new Point(2, 2), MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(new Point(2, 2), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        });
        Assert.True(popup.IsOpen); // T45: the close transition runs before the endpoint assertions below
        rig.FinishAnimations();
        Assert.False(popup.IsOpen);
        Assert.Null(popup.Child);
        Assert.True(rig.Model.Settings.Preference(Pin.Provider, Pin.ModelID).FastMode);
        Assert.Equal(new ModelPreference { Effort = "low", FastMode = true }, rig.Main.OnMain(() => new AppSettings()).Preference(Pin.Provider, Pin.ModelID));
        rig.Main.OnMain(() => Click(block));
        rig.FinishAnimations();
        Assert.True(popup.IsOpen);
        Assert.Same(block, popup.PlacementTarget);
        var reopened = Assert.IsType<EffortSliderCard>(Assert.IsType<Marrow.UI.Port.PopoverCard>(popup.Child).Child);
        Assert.NotSame(card, reopened);
        Assert.Contains(LogicalQuery.SelfAndDescendants<Button>(reopened), button => ToolTip.GetTip(button) is "Fast mode is on");
    }, realPopup: true);


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-SETTINGS-189")] // ModelsSettingsPage.swift:192-217: icon and slider hint are inside the Button's rectangular label
    public void The_provider_icon_and_effort_hint_belong_to_the_hit_tested_model_button(bool icon) => Run(true, rig =>
    {
        var block = BlockButton(rig.Page);
        var target = icon
            ? (Control)Assert.Single(LogicalQuery.SelfAndDescendants<ProviderIcon>(block))
            : Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(block), image => image.SymbolName == "slider.horizontal.3");
        rig.Main.OnMain(() => Click(block, target));
        Assert.True(Assert.Single(rig.Presenter.Calls).Presented);
    });

}
