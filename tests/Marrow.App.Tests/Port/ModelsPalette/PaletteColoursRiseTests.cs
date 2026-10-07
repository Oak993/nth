using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port.ModelsPalette;
using Marrow.App.Port.Settings;
using Marrow.App.Views.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.UI.Chrome;
using Xunit;
namespace Marrow.App.Tests.Port.ModelsPalette;
// PORT-ONLY: OD-0127 and lead placement ruling; original rise guards remain unchanged.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-modelspalette-9")]
public sealed class PaletteColoursRiseTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    public void Colours_is_the_last_card_and_joins_the_same_rise_curve()=>WithManager((_,manager)=>
    {
        using var rig=new SettingsRiseRig();manager.SettingsCloak=rig.Cloak;
        manager.ShowSettings(SettingsPage.Appearance);Dispatcher.UIThread.RunJobs();
        var view=View(manager);var cards=Nodes<ChromeCard>(view);
        var colours=Assert.Single(Nodes<PaletteColoursSection>(view));
        var card=Assert.Single(Nodes<ChromeCard>(colours));Assert.Same(card,cards[^1]);
        Assert.Equal(new[]{"Transparency","Sidebar","Activity list","Open the sidebar at launch"},Nodes<ChromeRow>(cards[1]).Select(row=>Nodes<TextBlock>(row).First().Text));
        rig.Frame(1d/60);Assert.Equal(0,card.Opacity);
        rig.Frame(1d/60);Assert.NotNull(rig.Reveal);
        // Look blocks 2..5; existing row card 6, picture 7, zoom 8; Colours heading 9 and card 10.
        rig.At(.180);var expected=SpecRise.Item(10,.180);
        Assert.InRange(card.Opacity,expected-1e-4,expected+1e-4);
        var transform=Assert.IsType<TranslateTransform>(card.RenderTransform);
        Assert.InRange(transform.Y,10*(1-expected)-1e-3,10*(1-expected)+1e-3);
        rig.At(.260);Assert.Equal(1,card.Opacity);Assert.Equal(0,(card.RenderTransform as TranslateTransform)?.Y??0);
    });
}
