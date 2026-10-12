using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY WL-2 (OD-0110 and its amendments; ED-NEW-wltwo-1): Settings > Appearance's wallpaper library. Expected values come from
// the approved design snapshot (porting/first-release/design-src/wallpaper-library): 90 static and 72 animated wallpapers in three
// tiers (30 and 24 each), the pages' headings ("Static wallpapers", "Animated wallpapers"), Colour pack, Speed 0.25x to 2.5x,
// Motion (Calm to Lively), Focus (Sharp to Dreamy), Dynamic and Perspective, and Match accent as a sixth pack.
[Collection("Headless")]
[Trait("Feature", "WL-2")]
public sealed class WallpaperLibrarySectionTests(HeadlessSession headless)
{
    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    private static string[] Text(Control root) => Nodes<TextBlock>(root).Select(x => x.Text ?? "").ToArray();
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Drain(Avalonia.Controls.Window window) { for (var i = 0; i < 5; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } }

    private static ManualFrameSource? frames;
    private static ManualTimeProvider? time;

    // the frame source and SwiftClock move together, or a transition never completes (as WallpaperCardScope does)
    private static void Tick(Avalonia.Controls.Window window, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 1.0 / 60) { time!.Advance(TimeSpan.FromSeconds(1.0 / 60)); frames!.Advance(TimeSpan.FromSeconds(1.0 / 60)); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        Drain(window);
    }

    private void With(Action<WallpaperLibrarySection, WallpaperLibraryStore, InMemorySettingsStore, Avalonia.Controls.Window> check) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var picture = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
        frames = new ManualFrameSource(); time = new ManualTimeProvider();
        using var frameScope = FrameClock.Source.Override(frames);
        using var timeScope = SwiftClock.Provider.Override(time);
        var defaults = new InMemorySettingsStore(); var store = new WallpaperLibraryStore(defaults);
        using var library = WallpaperLibraryStore.Shared.Override(store);
        var section = new WallpaperLibrarySection();
        var window = new Avalonia.Controls.Window { Width = 760, Height = 2400, Content = section };
        try { window.Show(); Drain(window); check(section, store, defaults, window); Drain(window); }
        finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_library_shows_static_and_animated_sub_sections_with_all_162_wallpapers_in_their_tiers()
    {
        With((section, store, _, window) =>
        {
            Assert.Equal(new[] { "Static wallpapers", "Animated wallpapers" }, new[] { section.Static.Title, section.Animated.Title });
            Assert.Equal(90, section.Static.Wallpapers.Count); Assert.Equal(72, section.Animated.Wallpapers.Count);
            Assert.All(section.Static.Wallpapers, w => Assert.Equal(WallpaperKind.Static, w.Kind)); Assert.All(section.Animated.Wallpapers, w => Assert.Equal(WallpaperKind.Animated, w.Kind));
            Assert.Equal(new[] { "vibrant", "balanced", "minimal" }, section.Static.TierGroups.Select(g => g.Tier)); Assert.Equal(new[] { "vibrant", "balanced", "minimal" }, section.Animated.TierGroups.Select(g => g.Tier));
            Assert.All(section.Static.TierGroups, g => Assert.Equal(30, g.Grid.Wallpapers.Count)); Assert.All(section.Animated.TierGroups, g => Assert.Equal(24, g.Grid.Wallpapers.Count));
            Assert.Contains("90", Text(section.Static)); Assert.Contains("72", Text(section.Animated)); Assert.Contains("Show all 30", Text(section.Static)); Assert.Contains("Show all 24", Text(section.Animated));
            Assert.Contains("Calm, slow and low in contrast, yet still colourful: soft hue drifts, quiet gradients, gentle light.", Text(section.Animated)); // the animated page's Minimal blurb
            // collapsed: one row per tier is made, with live thumbnails, the rest only when opened
            foreach (var g in section.Static.TierGroups.Concat(section.Animated.TierGroups))
            {
                Assert.Equal(g.Grid.Columns, g.Grid.Realized.Count); Assert.All(g.Grid.Realized.Keys, i => Assert.True(i < g.Grid.Columns));
                Assert.All(g.Grid.Realized.Values, t => Assert.Same(g.Grid.Wallpapers[Array.IndexOf(g.Grid.Wallpapers.ToArray(), t.Wallpaper)], t.Wallpaper));
            }
            var first = section.Static.TierGroups[0];
            Click(first.ShowAll); Drain(window); Tick(window, 1.5);
            Assert.True(first.Grid.Expanded); Assert.Contains("Show fewer", Text(first));
            Assert.True(first.Grid.Realized.Count > first.Grid.Columns); // the opened tier makes the rows now in view
            Assert.Equal(WallpaperKind.None, store.Selection.Kind); // OD-0014: browsing chooses nothing; the default stays no wallpaper
            Assert.Contains("None: the desktop shows through the glass", Text(section.Summary)); Assert.False(section.Summary.None.IsEnabled);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Choosing_a_thumbnail_sets_the_library_store_persists_it_and_opens_its_options_under_its_grid()
    {
        With((section, store, defaults, window) =>
        {
            var tile = section.Static.TierGroups[0].Grid.Realized[1];
            Assert.Equal($"{tile.Wallpaper.Name}, Vibrant static wallpaper", AutomationProperties.GetName(tile.Button));
            Click(tile.Button); Drain(window);
            Assert.Equal(WallpaperKind.Static, store.Selection.Kind); Assert.Equal(tile.Wallpaper.Id, store.Selection.Id);
            var reload = new WallpaperLibraryStore(defaults).Selection; Assert.Equal(WallpaperKind.Static, reload.Kind); Assert.Equal(tile.Wallpaper.Id, reload.Id); // restored at the next launch
            Assert.True(tile.Chosen); Assert.True(tile.Thumbnail.IsChosen); Assert.Equal("Chosen", AutomationProperties.GetItemStatus(tile.Button));
            Assert.Equal(1, section.Static.TierGroups.SelectMany(g => g.Grid.Realized.Values).Count(t => t.Chosen));
            Assert.NotNull(section.Static.Options); Assert.Null(section.Animated.Options); Assert.Equal(tile.Wallpaper.Id, section.Static.Options!.Wallpaper.Id);
            Assert.Contains($"{tile.Wallpaper.Name} · Vibrant · Static", Text(section.Summary)); Assert.True(section.Summary.None.IsEnabled);
            // a static wallpaper's rows: no Speed; its grain and own option, Focus, Dynamic, Perspective
            var rows = Nodes<ChromeRow>(section.Static.Options).Select(r => Text(r).First()).ToArray();
            Assert.Equal(new[] { tile.Wallpaper.Name, "Grain", tile.Wallpaper.Options[1].Label, "Focus", "Dynamic", "Perspective" }, rows);
            Assert.Equal(tile.Wallpaper.Packs.Length + 1, section.Static.Options.Packs.Swatches.Count); Assert.Equal("Match accent", section.Static.Options.Packs.Swatches[^1].Pack.Name);
            // another wallpaper: the store follows, the first tile lets go of its ring
            var other = section.Animated.TierGroups[1].Grid.Realized[0];
            Click(other.Button); Drain(window);
            Assert.Equal((WallpaperKind.Animated, other.Wallpaper.Id), (store.Selection.Kind, store.Selection.Id)); Assert.False(tile.Chosen); Assert.True(other.Chosen);
            Assert.NotNull(section.Animated.Options);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Pack_speed_option_focus_dynamic_and_perspective_changes_reach_the_selection_and_the_thumbnail()
    {
        With((section, store, defaults, window) =>
        {
            var tile = section.Animated.TierGroups[0].Grid.Realized[0]; var wall = tile.Wallpaper;
            Click(tile.Button); Drain(window);
            var card = section.Animated.Options!;
            var rows = Nodes<ChromeRow>(card).Select(r => Text(r).First()).ToArray();
            Assert.Equal(new[] { wall.Name, "Speed", "Motion", wall.Options[1].Label, "Focus", "Dynamic", "Perspective" }, rows);
            Assert.Contains("Calm", Text(card)); Assert.Contains("Lively", Text(card)); Assert.Contains("0.25x", Text(card)); Assert.Contains("2.5x", Text(card)); Assert.Contains("Sharp", Text(card)); Assert.Contains("Dreamy", Text(card));
            Assert.Contains($"{wall.Packs.Single(p => p.Id == wall.DefaultPack).Name} · default", Text(card.Packs));
            var pack = card.Packs.Swatches.First(s => s.Pack.Id != wall.DefaultPack && s.Pack.Id != WallpaperMatchPack.Id);
            Click(pack.Button); Drain(window);
            Assert.Equal(pack.Pack.Id, store.Selection.Pack); Assert.True(pack.Chosen); Assert.Equal(pack.Pack.Id, tile.Thumbnail.Selection.Pack); Assert.Contains(pack.Pack.Name, Text(card.Packs));
            Click(card.Packs.Swatches.Single(s => s.Pack.Id == WallpaperMatchPack.Id).Button); Drain(window); Assert.Equal(WallpaperMatchPack.Id, store.Selection.Pack);
            Slider Slider(string title) => Assert.Single(Nodes<Slider>(Nodes<ChromeRow>(card).Single(r => Text(r).First() == title)));
            var speed = Slider("Speed"); Assert.Equal(.25, speed.Minimum); Assert.Equal(2.5, speed.Maximum);
            speed.Value = 1.75; Drain(window); Assert.Equal(1.75, store.Selection.Speed); Assert.Equal(1.75, tile.Thumbnail.Selection.Speed); Assert.Contains("1.75x", Text(card));
            Slider("Motion").Value = .8; Drain(window); Assert.Equal(.8, store.Selection.Options!["motion"], 6);
            Slider(wall.Options[1].Label).Value = .2; Drain(window); Assert.Equal(.2, store.Selection.Options![wall.Options[1].Id], 6);
            Slider("Focus").Value = .4; Drain(window); Assert.Equal(.4, store.Selection.Focus!.Value, 6); Assert.Contains("40%", Text(card));
            IToggleProvider Toggle(string title) => (IToggleProvider)ControlAutomationPeer.CreatePeerForElement(Nodes<SmallSwitch>(Nodes<ChromeRow>(card).Single(r => Text(r).First() == title)).Single());
            var persp = store.Selection.Persp == true;
            Toggle("Dynamic").Toggle(); Drain(window); Assert.True(store.Selection.Dynamic);
            Toggle("Perspective").Toggle(); Drain(window); Assert.Equal(!persp, store.Selection.Persp);
            var reload = new WallpaperLibraryStore(defaults).Selection; // every change is kept for the next launch
            Assert.Equal((WallpaperMatchPack.Id, 1.75, true, !persp), (reload.Pack, reload.Speed, reload.Dynamic == true, reload.Persp == true)); Assert.Equal(.8, reload.Options!["motion"], 6);
            Assert.True(card.Reset.IsEnabled);
            Click(card.Reset); Drain(window);
            var stock = WallpaperLibraryStore.Normalize(new(WallpaperKind.Animated, wall.Id));
            Assert.Equal((stock.Pack, stock.Speed, stock.Dynamic, stock.Persp), (store.Selection.Pack, store.Selection.Speed, store.Selection.Dynamic, store.Selection.Persp)); Assert.False(card.Reset.IsEnabled);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void None_restores_no_wallpaper_and_a_wallpaper_chosen_again_recalls_its_own_choices()
    {
        With((section, store, defaults, window) =>
        {
            var tile = section.Animated.TierGroups[2].Grid.Realized[0];
            Click(tile.Button); Drain(window);
            Slider speed = Assert.Single(Nodes<Slider>(Nodes<ChromeRow>(section.Animated.Options!).Single(r => Text(r).First() == "Speed")));
            speed.Value = .5; Drain(window);
            Click(section.Summary.None); Drain(window);
            Assert.Equal(WallpaperKind.None, store.Selection.Kind); Assert.Equal(WallpaperKind.None, new WallpaperLibraryStore(defaults).Selection.Kind);
            Assert.False(tile.Chosen); Assert.False(section.Summary.None.IsEnabled); Assert.Contains("None: the desktop shows through the glass", Text(section.Summary));
            Click(tile.Button); Drain(window);
            Assert.Equal(.5, store.Selection.Speed); // its own last speed (the store remembers each wallpaper's choices)
        });
    }
}
