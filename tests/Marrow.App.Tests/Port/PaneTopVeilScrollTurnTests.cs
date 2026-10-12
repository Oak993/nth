using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views.Settings;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// OD-0088, OD-0076 "nothing cuts; everything is transitioned" (ED-253, round 4): the pane's top veil (PaneTopVeil,
// Chrome.swift:579-628) turns over with the Settings window, and a scroll during that 0.32 s keeps the turn running, retargeted
// at the colours of the new scroll position, instead of cutting both fades to their end.
[Collection("Headless")]
public sealed class PaneTopVeilScrollTurnTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    private static int Distance(Color a, Color b) => new[] { Math.Abs(a.R - b.R), Math.Abs(a.G - b.G), Math.Abs(a.B - b.B), Math.Abs(a.A - b.A) }.Max();

    private void Scrolled(Action<SettingsRiseRig, HostWindow, ScrollViewer, Func<Color[]>, AppModel> check) => WithManager((h, manager) =>
    {
        using var rig = new SettingsRiseRig { Accepts = false };
        using var accent = SwiftColor.AccentSource.Override(() => Color.Parse("#0067C0"));
        manager.Model.Settings.Theme = AppTheme.Dark;
        manager.ShowSettings(SettingsPage.Appearance);
        Dispatcher.UIThread.RunJobs();
        var window = Settings(manager);
        Drain(window);
        for (var i = 0; i < 60; i++) { rig.Frame(1.0 / 60); Drain(window); }
        var card = Nodes<LookCard>(View(manager)).Single();
        var page = Nodes<ScrollViewer>((Control)window.Content!).Single(s => s.GetVisualDescendants().Contains(card));
        page.Offset = new Vector(0, 30);
        Drain(window);
        for (var i = 0; i < 60; i++) { rig.Frame(1.0 / 60); Drain(window); }
        var veil = Nodes<PaneTopVeil>((Control)window.Content!).Single();
        Color[] Fades() => veil.GetVisualDescendants().OfType<Border>().Where(b => b.Background is LinearGradientBrush)
            .Select(b => ((LinearGradientBrush)b.Background!).GradientStops[0].Color).ToArray();
        check(rig, window, page, Fades, manager.Model);
    });

    private static void Frames(SettingsRiseRig rig, HostWindow window, int count)
    {
        for (var i = 0; i < count; i++) { rig.Frame(1.0 / 60); Drain(window); }
    }

    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_scroll_during_the_turnover_keeps_the_turn_running_to_the_new_positions_colours() => Scrolled((rig, window, page, fades, model) =>
    {
        var from = fades();
        Assert.Equal(2, from.Length);
        model.Settings.Theme = AppTheme.Light;
        Drain(window);
        Frames(rig, window, 4);
        var before = fades();
        page.Offset = new Vector(0, 20); // progress 0.6 to 0.28
        Drain(window);
        var after = fades();
        Frames(rig, window, 1);
        var next = fades();
        Frames(rig, window, 40);
        var end = fades();
        for (var f = 0; f < 2; f++)
        {
            var whole = Distance(from[f], end[f]);
            Assert.True(whole > 20, $"fade {f} turns over ({from[f]} to {end[f]})");
            Assert.True(Distance(before[f], after[f]) <= whole / 4, $"fade {f}: the scroll at frame 4 moved it {before[f]} -> {after[f]} (the turn {from[f]} -> {end[f]})");
            Assert.True(Distance(next[f], end[f]) > 4, $"fade {f}: the turn runs on after the scroll ({next[f]}, the end {end[f]})");
        }
        // The turn ends where a fresh drawing at the new position is (a scroll after the turn redraws at once).
        page.Offset = new Vector(0, 21);
        Drain(window);
        Assert.NotEqual(end, fades()); // the turn is over: a scroll redraws at once
        page.Offset = new Vector(0, 20);
        Drain(window);
        Assert.Equal(fades(), end);
    });

    // A scroll back to the top during the turnover takes the veil away; one down again after it draws the new appearance's veil,
    // with no turn left over.
    [Fact]
    [Trait("Decision", "ED-253")]
    public void A_scroll_to_the_top_during_the_turnover_leaves_no_turn_behind() => Scrolled((rig, window, page, fades, model) =>
    {
        model.Settings.Theme = AppTheme.Light;
        Drain(window);
        Frames(rig, window, 4);
        page.Offset = new Vector(0, 0);
        Drain(window);
        Assert.Empty(fades());
        Frames(rig, window, 40);
        page.Offset = new Vector(0, 30);
        Drain(window);
        var drawn = fades();
        Frames(rig, window, 10);
        Assert.Equal(drawn, fades());
        page.Offset = new Vector(0, 31);
        Drain(window);
        page.Offset = new Vector(0, 30);
        Drain(window);
        Assert.Equal(drawn, fades());
    });
}
