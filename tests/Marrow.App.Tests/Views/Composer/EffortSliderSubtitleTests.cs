using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :599-617 the line under the effort title, :206 the panel hands it
// option.shortName (the chip above takes chipName, :90).
[Collection("Headless")]
public sealed class EffortSliderSubtitleTests(HeadlessSession headless)
{
    private static Color Colour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static double Left(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.X;

    [Fact]
    [Trait("Req", "UI-COMPOSER-032")] // App/Views/Composer/EffortSlider.swift:599-617,206
    public void The_subtitle_is_the_providers_icon_and_the_models_short_name_or_the_pair_mark_and_the_pairs_title() => headless.OnUiThread(() =>
    {
        var pair = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "fancy", Name = "Duo" };
        // a name the chip and the card read differently: shortName keeps "Claude Fancy (preview)", chipName cuts at " (" and drops the vendor
        var fancy = new ModelOption("fancy", "Claude Fancy (preview)", efforts: new ValueList<string>(["low", "high"]), defaultEffort: "low");
        var longName = new ModelOption("long", string.Join(" ", Enumerable.Repeat("Extraordinarily-Lengthy-Model", 5)), efforts: new ValueList<string>(["low", "high"]), defaultEffort: "low");
        Assert.Equal("Claude Fancy (preview)", fancy.ShortName); Assert.Equal("Fancy", fancy.ChipName);
        using var env = Make(() => Thread(ProviderKind.Codex, "fancy", hydra: true), model =>
        {
            model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair);
        }, (ProviderKind.Codex, [fancy, longName]));
        var panel = new ModelEffortPanel(env.Thread.Id, false);
        var window = env.Show(panel);
        var card = Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        // the provider's icon at 12, 5 points before the model's short name (13 pt, one line, the tail cut) in primary text at 0.7
        var icon = Assert.Single(card.GetVisualDescendants().OfType<ProviderIcon>());
        Assert.Equal((12, 12, 12), (icon.Size, icon.Width, icon.Height)); Assert.Equal(ProviderKind.Codex, icon.Provider);
        var name = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Claude Fancy (preview)");
        Assert.Equal(13, name.FontSize); Assert.Equal(FontWeight.Normal, name.FontWeight); Assert.Equal(1, name.MaxLines); Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
        Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(name.ActualThemeVariant), Colour(name.Foreground));
        Assert.Equal(5, Left(name, card) - (Left(icon, card) + 12), 0.5);
        Assert.Empty(card.GetVisualDescendants().OfType<HydraMarkImage>());
        // the chip above names the same model by chipName
        var chip = new ModelEffortButton(env.Thread, false);
        var chipWindow = env.Show(chip);
        Assert.Contains(chip.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Fancy");
        Assert.DoesNotContain(chip.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Claude Fancy (preview)");
        chipWindow.Close();

        // the chat leads the pair: the Hydra mark at 12 (primary text at 0.75) and the pair's title in place of the icon and the model's name
        env.Model.UpdateThread(env.Thread.Id, chat => chat with { HydraPairID = pair.Id }); Dispatcher.UIThread.RunJobs();
        card = Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        var mark = Assert.Single(card.GetVisualDescendants().OfType<HydraMarkImage>());
        Assert.Equal((12, 12), (mark.Width, mark.Height));
        var markImage = Assert.IsType<SwiftImage>(mark.Child);
        Assert.Equal(Chrome.PrimaryText.Opacity(0.75).Resolve(markImage.ActualThemeVariant), Colour(markImage.Foreground));
        Assert.Empty(card.GetVisualDescendants().OfType<ProviderIcon>());
        var title = card.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Duo");
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Claude Fancy (preview)");
        Assert.Equal(13, title.FontSize); Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(title.ActualThemeVariant), Colour(title.Foreground));
        Assert.Equal(5, Left(title, card) - (Left(mark, card) + 12), 0.5);
        window.Close();

        // a name far wider than the block between the two buttons: shrunk as far as 0.85, then cut with an ellipsis
        using var wide = Make(() => Thread(ProviderKind.Codex, "long"), model => { }, (ProviderKind.Codex, [fancy, longName]));
        var widePanel = new ModelEffortPanel(wide.Thread.Id, false);
        var wideWindow = wide.Show(widePanel);
        var longText = widePanel.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == longName.ShortName);
        Assert.Equal(13 * 0.85, longText.FontSize, 5);
        Assert.Contains(longText.TextLayout.TextLines, line => line.HasCollapsed);
        wideWindow.Close();

        // a card with no provider has no icon, and then no 5 point gap: the name is centred alone
        var bare = new EffortSliderCard("Bare name", null, new ValueList<string>(["low", "high"]), "low", false,
            Binding<string?>.Constant("low"), Binding<bool>.Constant(false), () => { });
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var host = new Border { Child = bare, Width = 330, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var bareWindow = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 500 };
        bareWindow.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.Empty(bare.GetVisualDescendants().OfType<ProviderIcon>()); Assert.Empty(bare.GetVisualDescendants().OfType<HydraMarkImage>());
        var bareName = bare.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Bare name");
        Assert.True(Math.Abs(bare.Bounds.Width / 2 - bareName.TranslatePoint(new Point(bareName.Bounds.Width / 2, 0), bare)!.Value.X) <= 1);
        bareWindow.Close();
    });
}
