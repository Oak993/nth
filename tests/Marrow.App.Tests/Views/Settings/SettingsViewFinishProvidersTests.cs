using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// SettingsView.swift:254-256 (the chrome row's refresh button), :566-580 (ProvidersRefreshButton), :585-615 and :644-801 (what else the
// page does); UI/Chrome/Chrome.swift:308-322 (ChromeCapsule), :333-383 (ChromeIconLabel and ChromeIconButton); ProviderRegistry.swift:315-333.
// Every probe is counted at the executable lookup seam (ED-211), so no test here can reach a real CLI.
[Collection("Headless")]
public sealed class SettingsViewFinishProvidersTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    // A real press and release at one point, then one pass over the dispatcher only (Click's five passes could finish a quick refresh).
    private static void ClickOnce(Avalonia.Controls.Window w, Control control, Point local)
    {
        var point = control.TranslatePoint(local, w)!.Value; w.MouseMove(point); w.MouseDown(point, MouseButton.Left); w.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
    }

    // The click and everything the refresh hands back to the main actor run on the harness's manual loop, which only moves when the test pumps it,
    // and the executable lookups (off the main actor since T44, on the thread pool) wait at the seam until the test releases them
    // (ED-NEW-ignis-integration): the refresh stays in flight for as long as the test wants to look at it, however fast the pool is.
    private static void Start(AppModelHarness h, Avalonia.Controls.Window w, Control control, Point local)
    {
        using (MainActor.Executor.Override(h.Main.Loop)) { h.Main.Loop.Enqueue(() => ClickOnce(w, control, local)); h.Main.RunUntilIdle(); }
    }

    private static void Finish(AppModelHarness h, AppModel model)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (model.Providers.IsRefreshing && elapsed.Elapsed < AppModelHarness.Bound) { h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Dispatcher.UIThread.RunJobs(); Assert.False(model.Providers.IsRefreshing, "the refresh did not settle");
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-011")] // App/Views/Settings/SettingsView.swift:254-256,566-580; UI/Chrome/Chrome.swift:28,308-322,333-383
    public void Refresh_button_is_a_glass_capsule_around_a_28pt_icon_that_dims_while_checking_and_starts_one_full_refresh_from_its_whole_square()
    {
        var lookups = 0; using var probes = new ManualResetEventSlim(true); using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => { Interlocked.Increment(ref lookups); probes.Wait(TimeSpan.FromSeconds(30)); return null; }); // the probes run off the main actor (T44): held while the test looks at the refresh in flight
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            None<ProvidersRefreshButton>(view, "on the General page"); Click(w, RowButton(view, "Providers")); var refresh = Assert.Single(Nodes<ProvidersRefreshButton>(view)); var detail = Field<Control>(view, "detail"); var row = Nodes<Grid>(detail).Single(g => g.MinHeight == 32 && Nodes<ChromeCompactTitle>(g).Length == 1);
            // :569-570 a ChromeCapsule around a ChromeIconButton: the 28pt icon square inside 3pt (iconCapsuleInnerPadding) either side and 2pt above and below, so 34 x 32, last in the chrome row.
            Assert.Single(Nodes<ChromeCapsule>(refresh)); Assert.Single(Nodes<ChromeIconButton>(refresh)); var button = Assert.Single(Nodes<Button>(refresh)); Assert.Equal(34, refresh.Bounds.Width, 3); Assert.Equal(32, refresh.Bounds.Height, 3); Assert.Equal(new Size(28, 28), button.Bounds.Size); Assert.Equal(new Point(3, 2), TopLeft(button, refresh));
            Assert.Single(Nodes<Border>(refresh), b => b.Padding == new Thickness(3, 2)); Assert.Equal(row.Bounds.Width, TopLeft(refresh, row).X + 34, 3); Assert.Equal(0, TopLeft(refresh, row).Y, 3); Assert.Empty(Text(refresh));
            // Chrome.swift:317-320, :381 glass capsule washed 0.3 with the theme and answering presses; presses on it and on its button never drag the window.
            var effect = Glass(refresh); Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style); Assert.Equal(Chrome.GlassTint, effect.TintColor); Assert.Equal(0.3, effect.TintOpacity); Assert.True(effect.IsInteractive); Assert.Equal(2, Nodes<NoWindowDragArea>(refresh).Length);
            // :572 the symbol "arrow.clockwise" at Chrome.iconFont (.callout semibold), primary at 0.92 at rest, 1 hovering (Chrome.swift:354-358), the help text also the accessibility label (:379-380).
            var glyph = Assert.Single(Nodes<SwiftImage>(refresh)); Assert.Equal("arrow.clockwise", glyph.SymbolName); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(glyph)); Assert.Equal(Chrome.IconFont, SwiftImage.GetFont(glyph));
            Assert.Equal("Check providers again", ToolTip.GetTip(button)); Assert.Equal("Check providers again", AutomationProperties.GetName(button)); Assert.True(button.IsEnabled); Ink(glyph, Avalonia.Controls.Documents.TextElement.GetForeground(glyph), Chrome.PrimaryText.Opacity(0.92));
            // The whole 28pt square is the control (:351 contentShape(.rect)): the pointer in its very corner hovers it, and Chrome.hover (easeOut 0.1) lifts the symbol to full.
            var corner = TopLeft(button, w) + new Point(1, 1); w.MouseMove(corner); Drain(w); Assert.True(button.IsPointerOver); Tick(h, 50); Ink(glyph, Avalonia.Controls.Documents.TextElement.GetForeground(glyph), Chrome.PrimaryText.Opacity(0.92 + 0.08 * Animation.EaseOut(0.1).Progress(0.05))); Tick(h, 51); Ink(glyph, Avalonia.Controls.Documents.TextElement.GetForeground(glyph), Chrome.PrimaryText);
            w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 101); Ink(glyph, Avalonia.Controls.Documents.TextElement.GetForeground(glyph), Chrome.PrimaryText.Opacity(0.92));
            // A click in that corner starts the refresh (:576): checking, the button disabled and dimmed to 0.32, the help text "Checking providers…" with one ellipsis character.
            Assert.Equal(0, lookups); Assert.False(model.Providers.IsRefreshing); probes.Reset(); Start(h, w, button, new Point(2, 2)); Assert.True(model.Providers.IsRefreshing);
            button = Assert.Single(Nodes<Button>(refresh)); glyph = Assert.Single(Nodes<SwiftImage>(refresh)); Assert.False(button.IsEnabled); Assert.Equal("Checking providers…", ToolTip.GetTip(button)); Assert.Equal("Checking providers…", AutomationProperties.GetName(button)); Assert.Equal(0x2026, (int)"Checking providers…"[^1]); Assert.Equal(1, ToolTip.GetTip(button)!.ToString()!.Count(c => c == '…'));
            Ink(glyph, Avalonia.Controls.Documents.TextElement.GetForeground(glyph), Chrome.PrimaryText.Opacity(0.32)); Assert.Equal(new Size(34, 32), refresh.Bounds.Size);
            probes.Set(); Finish(h, model); Drain(w); button = Assert.Single(Nodes<Button>(refresh)); glyph = Assert.Single(Nodes<SwiftImage>(refresh)); Assert.True(button.IsEnabled); Assert.Equal("Check providers again", ToolTip.GetTip(button)); Ink(glyph, Avalonia.Controls.Documents.TextElement.GetForeground(glyph), Chrome.PrimaryText.Opacity(0.92));
            Assert.True(lookups > 0); Assert.Equal(16, model.Providers.Statuses.Count); // the refresh checked every provider again
        }, height: 1100);
    }

    [Fact]
    // UI-PROVREG-002 is WITHDRAWN (STOP: the record says the button is the ONLY control on the page that probes providers, which the pinned Swift contradicts:
    // SettingsView.swift:784-795 .onChange(of: apiKey) refreshes the provider and reloads its catalogue 600 ms after a key edit, and the port does the same, proven in
    // Key_edit_really_probes_after_six_hundred_ms_without_clicking_any_refresh_control). The record needs a spec_fix before it can close; this test proves the rest.
    [Trait("Rule", "R-TEST-4")] // partial UI-PROVREG-002 (SettingsView.swift:254-256,566-580,582-584,780-795; ProviderRegistry.swift:315-333)
    public void Refresh_button_shows_only_on_the_providers_page_and_is_the_control_that_starts_a_full_single_flight_refresh_while_opening_scrolling_and_editing_paths_probe_nothing()
    {
        var lookups = 0; using var probes = new ManualResetEventSlim(true); using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => { Interlocked.Increment(ref lookups); probes.Wait(TimeSpan.FromSeconds(30)); return null; }); // the probes run off the main actor (T44): held while the test looks at the refresh in flight
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            None<ProvidersRefreshButton>(view, "on the General page");
            Click(w, RowButton(view, "Providers")); var refresh = Assert.Single(Nodes<ProvidersRefreshButton>(view));
            // :582-584 opening the page checks no provider, and neither does scrolling it: nothing was looked up and no status exists yet.
            Assert.Equal(0, lookups); Assert.Empty(model.Providers.Statuses); var scroll = Nodes<ScrollViewer>(Field<Control>(view, "detail")).First(); /* the page's own; each rounded-border field carries its PART_ScrollViewer */ var at = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), w)!.Value;
            w.MouseMove(at); w.MouseWheel(at, new Vector(0, -3)); Drain(w); Assert.Equal(0, scroll.Offset.Y); /* ED-NEW-softscroll-1: the page glides (OD-0071); three notches are 300 points at the default 3 lines (ED-NEW-softscroll-2) */ Tick(h, 1000 / 60.0); Drain(w); Assert.InRange(scroll.Offset.Y, 0.01, 299.99); Tick(h, 600); Drain(w); Assert.Equal(300, scroll.Offset.Y, 3); w.MouseWheel(at, new Vector(0, 3)); Drain(w); Tick(h, 600); Drain(w); Assert.Equal(0, scroll.Offset.Y, 3); Tick(h, 200); /* the rows thaw 150ms after the last movement (SettingsScrollTracker) */ Assert.Equal(0, lookups); Assert.Empty(model.Providers.Statuses);
            // :644-783 the page's other controls only edit settings: opening a card, typing a binary path and flipping a provider's switch probe nothing.
            var codex = Nodes<ProviderSettingsSection>(view).First(x => Text(x).Contains("Codex")); Click(w, Assert.Single(Nodes<Button>(Assert.Single(Nodes<CollapsibleProviderHeader>(codex))))); Tick(h, Chrome.PanelSlide.Duration * 1000 + 1);
            var path = Assert.Single(Nodes<TextBox>(codex)); Type(w, path, "codex-fixture.exe"); Assert.Equal("codex-fixture.exe", model.Settings.BinaryPath(ProviderKind.Codex)); Click(w, Nodes<SmallSwitch>(codex).First()); Tick(h, 700);
            Assert.Equal(0, lookups); Assert.Empty(model.Providers.Statuses); Assert.False(model.Providers.IsRefreshing);
            // :571-576 pressing the button is what starts refreshEverything: every provider is checked once and the page is busy meanwhile (:316 single flight).
            var button = Assert.Single(Nodes<Button>(refresh)); Assert.Equal("Check providers again", ToolTip.GetTip(button)); probes.Reset(); Start(h, w, button, new Point(14, 14)); Assert.True(model.Providers.IsRefreshing);
            // While it runs the button is disabled, a second call returns at once without starting another, and a press on the dimmed button changes nothing.
            Task? again = null; using (MainActor.Executor.Override(h.Main.Loop)) { h.Main.Loop.Enqueue(() => again = model.Providers.RefreshEverything()); h.Main.RunUntilIdle(); } Assert.True(again!.IsCompletedSuccessfully); button = Assert.Single(Nodes<Button>(refresh)); Assert.False(button.IsEnabled); Assert.Equal("Checking providers…", ToolTip.GetTip(button)); ClickOnce(w, button, new Point(14, 14));
            probes.Set(); Finish(h, model); Drain(w); var first = lookups; Assert.True(first > 0); Assert.Equal(16, model.Providers.Statuses.Count); Assert.Equal("Check providers again", ToolTip.GetTip(Assert.Single(Nodes<Button>(refresh))));
            // A plain second run costs exactly what the first did, so nothing the first run's extra calls did started a second run.
            probes.Reset(); Start(h, w, Assert.Single(Nodes<Button>(refresh)), new Point(14, 14)); Assert.True(model.Providers.IsRefreshing); probes.Set(); Finish(h, model); Drain(w); Assert.Equal(first * 2, lookups);
            // :254 the button is in the chrome row only while the Providers page is showing, never on another page (other pages may look providers up for their own reasons).
            foreach (var title in new[] { "General", "Hydra", "Appearance", "MCP", "Models", "Source control", "Shortcuts", "Archive", "About" }) { var row = RowButton(view, title); Reveal(w, h, row); Click(w, row); None<ProvidersRefreshButton>(view, "on the " + title + " page"); }
            var providers = RowButton(view, "Providers"); Reveal(w, h, providers); Click(w, providers); Assert.Single(Nodes<ProvidersRefreshButton>(view)); // OD-0134: with Ignis listed the sidebar scrolls to its last rows, as a person scrolls it (Reveal)
        }, height: 1100);
    }
}
