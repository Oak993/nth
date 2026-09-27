using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :637,688.
[Collection("Headless")]
public sealed class ComposerViewComponentsTests(HeadlessSession headless)
{
    private static ThreadRuntimePart12Harness Harness()
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var harness = new ThreadRuntimePart12Harness();
        harness.Main.Loop.Run(() => harness.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        return harness;
    }
    private static Avalonia.Controls.Window Show(Control view, AppModel model)
    {
        var host = new Border { Child = view }; AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 550, Height = 250 };
        window.Show(); Dispatcher.UIThread.RunJobs(); return window;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-077")] // App/Views/Composer/ComposerView.swift:637
    [Trait("Req", "UI-COMPOSER-011")] // App/Views/Composer/ComposerView.swift:637
    public void The_attachment_chip_follows_external_and_click_changes_to_the_recents_binding() => headless.OnUiThread(() =>
    {
        using var harness = Harness(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        ComposerControls controls = null!; Avalonia.Controls.Window window = null!;
        var state = new ObservableBoolBox();
        harness.Main.OnMain(() =>
        {
            controls = new ComposerControls(harness.Runtime, harness.Thread, true, true, false, false, null,
                new Binding<bool>(() => state.Value, value => state.Value = value), _ => { }, () => { }, () => { });
            window = Show(controls, harness.App);
        });
        try
        {
            void AssertPlate(bool active) => harness.Main.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                var button = Assert.Single(controls.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Attach files");
                var plate = Assert.IsType<Border>(button.Content);
                Assert.Equal(new Thickness(9, 5), plate.Padding);
                Assert.Equal(RoundedCornerStyle.Circular, SwiftShapes.GetCornerStyle(plate));
                Assert.Equal(active ? Chrome.Accent.Opacity(0.16).Resolve(plate.ActualThemeVariant) : Chrome.PrimaryText.Opacity(0).Resolve(plate.ActualThemeVariant),
                    Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color);
                Assert.False(button.Focusable);
            });
            AssertPlate(false);
            harness.Main.OnMain(() => state.Value = true); AssertPlate(true);
            harness.Main.OnMain(() =>
                Assert.Single(controls.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "Attach files")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            AssertPlate(false); Assert.False(harness.Main.OnMain(() => state.Value));
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Theory]
    [Trait("Req", "PR-COMPOSER-090")] // App/Views/Composer/ComposerView.swift:647,688
    [InlineData(false)]
    [InlineData(true)]
    public void The_model_slot_contains_the_actual_chip_in_full_and_compact_controls(bool compact) => headless.OnUiThread(() =>
    {
        using var harness = Harness(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        harness.Main.OnMain(() =>
        {
            var controls = new ComposerControls(harness.Runtime, harness.Thread, compact, false, false, false, null,
                Binding<bool>.Constant(false), _ => { }, () => { }, () => { });
            var window = Show(controls, harness.App);
            try
            {
                var slot = Assert.Single(controls.GetVisualDescendants().OfType<ModelEffortSlot>());
                var chip = Assert.IsType<ModelEffortButton>(slot.Child);
                Assert.Single(chip.GetVisualDescendants().OfType<Button>());
                Assert.Single(chip.GetVisualDescendants().OfType<Marrow.UI.Common.ProviderIcon>());
                if (compact) Assert.Empty(chip.GetVisualDescendants().OfType<TextBlock>());
                else Assert.NotEmpty(chip.GetVisualDescendants().OfType<TextBlock>());
            }
            finally { window.Close(); }
        });
    });

    [Theory]
    [Trait("Req", "PR-COMPOSER-090")] // App/Views/Composer/ComposerView.swift:688; EffortSlider.swift:327-339
    [InlineData(false)]
    [InlineData(true)]
    public void The_model_slot_passes_runtime_history_to_the_model_picker(bool hasHistory) => headless.OnUiThread(() =>
    {
        using var harness = Harness(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        harness.Main.OnMain(() =>
        {
            var option = harness.App.Providers.Models(ProviderKind.Claude).First();
            harness.App.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, option.Id));
            if (hasHistory) typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Turns))!.SetValue(harness.Runtime,
                new ValueList<TurnRecord>([new() { Index = 1, Status = TurnStatus.Interrupted }]));
            var slot = new ModelEffortSlot(harness.Runtime, harness.Thread, false);
            var window = Show(slot, harness.App);
            Avalonia.Controls.Window? pickerWindow = null;
            try
            {
                var button = Assert.Single(slot.GetVisualDescendants().OfType<Button>());
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
                var content = Assert.IsAssignableFrom<Control>(presenter.Calls[^1].Popup.Child);
                presenter.Calls[^1].Popup.Child = null; // The recording presenter does not host the panel; transfer it to the headless window.
                pickerWindow = Show(content, harness.App);
                Assert.Single(content.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), "Choose a model"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
                var row = Assert.Single(content.GetVisualDescendants().OfType<ModelListRow>());
                Assert.Equal(!hasHistory, Assert.Single(row.GetVisualDescendants().OfType<Button>()).IsEnabled);
                if (hasHistory) Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "New chats only");
            }
            finally { pickerWindow?.Close(); window.Close(); }
        });
    });
}
