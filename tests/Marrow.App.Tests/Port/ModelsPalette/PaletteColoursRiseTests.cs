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
    // OD-0140: Settings opens as a panel in the main window, which appears as a popover does (SettingsOverlay); the separate window's
    // card-by-card rise is gone with that window, so Colours is shown whole with the rest of the page once the panel has opened.
    public void Colours_is_the_last_card_and_shows_whole_with_the_page_once_the_panel_has_opened()=>WithManager((_,manager)=>
    {
        using var rig=new SettingsRiseRig();
        manager.ShowSettings(SettingsPage.Appearance);Dispatcher.UIThread.RunJobs();
        var view=View(manager);var cards=Nodes<ChromeCard>(view);
        var colours=Assert.Single(Nodes<PaletteColoursSection>(view));
        var card=Assert.Single(Nodes<ChromeCard>(colours));Assert.Same(card,cards[^1]);
        Assert.Equal(new[]{"Transparency","Sidebar","Activity list","Open the sidebar at launch"},Nodes<ChromeRow>(cards[1]).Select(row=>Nodes<TextBlock>(row).First().Text));
        var window=Settings(manager);
        for(var i=0;i<30;i++){rig.Frame(1d/60);Drain(window);}
        Assert.Equal(1,Overlay(manager).Shown);
        Assert.Equal(1,card.Opacity);Assert.Equal(0,(card.RenderTransform as TranslateTransform)?.Y??0);
        Assert.True(card.IsEffectivelyVisible);
    });
}
