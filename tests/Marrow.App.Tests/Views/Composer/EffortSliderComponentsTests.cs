using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :73-75,114,436-439,606-608.
[Collection("Headless")]
public sealed class EffortSliderComponentsTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control view, AppModel? model = null)
    {
        var host = new Border { Child = view };
        if (model is not null) AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 220 };
        window.Show(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private static void AssertArtwork(ProviderIcon icon, double size)
    {
        Assert.Equal(size, icon.Size);
        var image = Assert.IsType<SwiftImage>(icon.Child);
        Assert.Equal(icon.Provider.IconName, image.AssetName);
        Assert.False(image.IsEmpty);
        Assert.Equal(size, image.Width); Assert.Equal(size, image.Height);
        Assert.True(image.IsResizable); Assert.Equal(ContentMode.Fit, image.AspectContentMode);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)size, (int)size));
        using (var context = bitmap.CreateDrawingContext()) image.Render(context);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions());
        using var pixels = SKBitmap.Decode(stream.ToArray());
        Assert.Contains(pixels.Pixels, pixel => pixel.Alpha > 0);
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:73-75
    [Trait("Req", "PR-COMPOSER-107")] // App/Views/Composer/EffortSlider.swift:82-103
    [Trait("Req", "UI-COMPOSER-012")] // App/Views/Composer/EffortSlider.swift:73-75,114
    [InlineData(false)]
    [InlineData(true)]
    public void Both_model_chip_forms_draw_the_real_14_point_provider_mark(bool compact) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "test-model", null, RuntimeMode.Supervised);
        harness.WriteLibrary([], [thread]); var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        var chip = new ModelEffortButton(thread, false, compact); var window = Show(chip, model);
        try { AssertArtwork(Assert.Single(chip.GetVisualDescendants().OfType<ProviderIcon>()), 14); }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-106")] // App/Views/Composer/EffortSlider.swift:114; UI/Common/Components.swift:75-80,89-91
    [Trait("Req", "UI-COMPOSER-012")] // App/Views/Composer/EffortSlider.swift:114
    public void Opening_and_closing_the_model_popover_updates_its_real_chip_plate() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised);
        harness.WriteLibrary([], [thread]); var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        var chip = new ModelEffortButton(thread, false);
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        var window = Show(chip, model);
        try
        {
            var button = Assert.Single(chip.GetVisualDescendants().OfType<Button>());
            Border Surface() => Assert.IsType<Border>(button.Content);
            Color Fill() => Assert.IsAssignableFrom<ISolidColorBrush>(Surface().Background).Color;
            Assert.Equal(new Thickness(9, 5), Surface().Padding);
            Assert.Equal(RoundedCornerStyle.Circular, SwiftShapes.GetCornerStyle(Surface()));
            Assert.Equal(0, Fill().A);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.True(presenter.Calls[^1].Presented);
            Assert.Equal(Chrome.Accent.Opacity(0.16).Resolve(Surface().ActualThemeVariant), Fill());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Assert.False(presenter.Calls[^1].Presented); Assert.Equal(0, Fill().A);
            Assert.False(button.Focusable);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-143")] // App/Views/Composer/EffortSlider.swift:436-439
    [Trait("Req", "UI-COMPOSER-043")] // App/Views/Composer/EffortSlider.swift:436-439
    public void The_model_row_draws_14_point_artwork_in_an_18_point_column_with_secondary_tint() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var row = new ModelListRow(new ModelCatalog.Entry(ProviderKind.Codex,
            new ModelOption("test", "Test", null, ValueList<string>.Empty, null)), null, true, 34, false, true, () => { });
        var window = Show(row);
        try
        {
            var icon = Assert.Single(row.GetVisualDescendants().OfType<ProviderIcon>());
            Assert.Equal(18, icon.Width); AssertArtwork(icon, 14);
            var image = Assert.IsType<SwiftImage>(icon.Child);
            Assert.Equal(ImageRenderingMode.Template, image.EffectiveRenderingMode); // pinned provider-codex imageset intent
            Assert.Equal(Chrome.SecondaryText.Resolve(icon.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-114")] // App/Views/Composer/EffortSlider.swift:606-608
    [Trait("Req", "UI-COMPOSER-032")] // App/Views/Composer/EffortSlider.swift:606-608
    public void The_unpaired_effort_subtitle_draws_the_real_12_point_provider_mark() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var card = new EffortSliderCard("Test", ProviderKind.Codex, new ValueList<string>(["low", "high"]), "low", false,
            Binding<string?>.Constant("low"), Binding<bool>.Constant(false), () => { });
        var window = Show(card);
        try { AssertArtwork(Assert.Single(card.GetVisualDescendants().OfType<ProviderIcon>()), 12); }
        finally { window.Close(); }
    });
}
