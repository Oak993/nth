using Marrow.UI.Port.Nth;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :271-359 ModelList, :364-477 the rows. One scenario per record: it proves every
// clause the record states, over the real list and panel.
[Collection("Headless")]
public sealed class EffortSliderModelListRecordsTests(HeadlessSession headless)
{
    private static Color Colour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    // ---- UI-COMPOSER-038 and UI-COMPOSER-039: the header ------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-COMPOSER-038")] // App/Views/Composer/EffortSlider.swift:288-306,198
    [Trait("Req", "UI-COMPOSER-039")] // App/Views/Composer/EffortSlider.swift:299-301
    public void The_header_is_a_back_chevron_in_a_20_point_frame_beside_Select_model_and_back_returns_to_the_slider() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Thread(ProviderKind.Codex, "c1"), model => model.Settings.AddToModelList(Pin(ProviderKind.Codex, "c1")), (ProviderKind.Codex, [Option("c1", "C1")]));
        var backs = 0;
        var list = new ModelList(env.Thread, false, _ => { }, () => backs++);
        var window = env.Show(list);
        var back = ButtonWithTip(list, "Back");
        var frame = Assert.IsType<Border>(back.Content);
        Assert.Equal((20, 20), (frame.Width, frame.Height));
        // :291-293 a chevron.left at .subheadline semibold (11 pt) in secondary text
        var chevron = Assert.IsType<SwiftImage>(frame.Child);
        Assert.Equal("chevron.left", chevron.SymbolName);
        var font = SwiftImage.GetFont(chevron)!.Resolve(); Assert.Equal(11, font.FontSize); Assert.Equal(FontWeight.SemiBold, font.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(chevron.ActualThemeVariant), Colour(chevron.Foreground));
        // the header row: spacing 6, padded 8 left and right, 8 above and 4 below, inside the list's 6
        var header = Assert.IsType<StackPanel>(back.Parent);
        Assert.Equal(Orientation.Horizontal, header.Orientation); Assert.Equal(6, header.Spacing); Assert.Equal(new Thickness(8, 8, 8, 4), header.Margin);
        Assert.Equal(6 + 8, back.TranslatePoint(default, window)!.Value.X, 0.5); Assert.Equal(6 + 8, back.TranslatePoint(default, window)!.Value.Y, 0.5);
        // :299-301 "Select model" beside the button, 6 points on: .body (13), regular, secondary text
        var text = Assert.IsType<TextBlock>(header.Children[1]); Assert.Equal(2, header.Children.Count);
        Assert.Equal("Select model", text.Text); Assert.Equal(13, text.FontSize); Assert.Equal(FontWeight.Normal, text.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(text.ActualThemeVariant), Colour(text.Foreground));
        Assert.Equal(back.TranslatePoint(default, window)!.Value.X + 20 + 6, text.TranslatePoint(default, window)!.Value.X, 0.5);
        Assert.Equal("Back", ToolTip.GetTip(back));
        // :294 .contentShape(.rect): the whole 20 point frame takes the press, its corners too, and nothing beside it
        var origin = back.TranslatePoint(default, window)!.Value;
        foreach (var inside in new[] { new Point(1, 1), new Point(18.5, 1), new Point(1, 18.5), new Point(18.5, 18.5), new Point(10, 10) })
        {
            var before = backs;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.MouseMove(origin + inside); window.MouseDown(origin + inside, MouseButton.Left, RawInputModifiers.None); window.MouseUp(origin + inside, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 1, backs);
        }
        var count = backs;
        var outside = origin + new Point(22.5, 10); // in the 6 points between the button and the text
        window.MouseMove(outside); window.MouseDown(outside, MouseButton.Left, RawInputModifiers.None); window.MouseUp(outside, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(count, backs);
        window.Close();

        // :198 inside the panel the press returns to the slider, over the cross-fade
        using var inPanel = Make(() => Thread(ProviderKind.Codex, "c1"), model => model.Settings.AddToModelList(Pin(ProviderKind.Codex, "c1")), (ProviderKind.Codex, [Option("c1", "C1")]));
        var panel = OpenPanelList(inPanel, false, out var panelWindow);
        Assert.Empty(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        Click(panelWindow, ButtonWithTip(panel, "Back"));
        inPanel.Step(130);
        Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        Assert.Empty(panel.GetVisualDescendants().OfType<ModelList>());
    });

    // ---- UI-COMPOSER-045: the scrolling area --------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-COMPOSER-045")] // App/Views/Composer/EffortSlider.swift:271-287,348-351; ED-262: the platform has no rubber band, the list scrolls only past its frame
    public void The_scrolling_area_is_the_fixed_row_arithmetic_padded_6_capped_at_440_and_scrolls_only_when_the_rows_exceed_it() => headless.OnUiThread(() =>
    {
        ProviderKind[] kinds = [ProviderKind.Codex, ProviderKind.Deepseek, ProviderKind.Meta];
        // (providers listed, models per provider, only a foreign provider listed, the chat has history, empty, pairs) -> (row height, scroll height)
        foreach (var (providers, per, foreignOnly, history, empty, pairCount, rowHeight, height) in new (int, int, bool, bool, bool, int, double, double)[]
        {
            (1, 3, false, false, false, 0, 34, 105), (3, 2, false, false, false, 0, 46, 282), (2, 1, false, false, false, 0, 46, 94),
            (1, 2, true, true, false, 0, 46, 94), // a locked cross-provider row: 46
            (1, 2, true, false, false, 0, 34, 70), // the same list without history: nothing is locked and one provider shows no icons, 34 (:286's `hasHistory &&`)
            (1, 2, false, false, false, 0, 34, 70),
            (2, 5, false, false, false, 0, 46, 440), (1, 1, false, false, true, 0, 34, 120), (1, 3, false, false, false, 2, 34, 44 + 94 + 105), (1, 1, false, false, false, 1, 34, 44 + 47 + 35), (2, 2, false, false, false, 5, 46, 440),
        })
        {
            var own = foreignOnly || empty ? "unlisted" : "c1";
            var listed = foreignOnly ? new[] { ProviderKind.Deepseek } : empty ? [] : kinds.Take(providers).ToArray();
            var catalogues = listed.Select(kind => (kind, Enumerable.Range(1, per).Select(i => Option($"{(kind == ProviderKind.Codex ? "c" : kind == ProviderKind.Deepseek ? "d" : "m")}{i}", $"{kind.Id}{i}")).ToArray())).ToArray();
            var pairs = Enumerable.Range(0, pairCount).Select(i => new HydraPair(ProviderKind.Codex) { Name = $"Pair {i}" }).ToArray();
            using var env = Make(() => Thread(ProviderKind.Codex, own, hydra: true), model =>
            {
                model.Settings.HydraEnabled = true;
                foreach (var pair in pairs) model.Settings.AddHydraPair(pair);
                if (empty) model.Settings.AddToModelList(Pin(ProviderKind.Codex, "nothing-resolves-this"));
                else foreach (var (kind, models) in catalogues) foreach (var option in models) model.Settings.AddToModelList(Pin(kind, option.Id));
            }, catalogues);
            var list = new ModelList(env.Thread, history, _ => { }, () => { });
            env.Show(list);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(height == scroll.Height, $"{providers}x{per} foreignOnly {foreignOnly} history {history} empty {empty} pairs {pairCount}: scroll {scroll.Height}, expected {height}");
            Assert.Equal(new Thickness(6), Assert.IsType<Border>(list.Child).Padding); // the whole list is padded 6
            var rows = Models(list);
            Assert.All(rows, row => Assert.Equal(rowHeight, row.Bounds.Height, 0.5));
            Assert.All(Pairs(list), row => Assert.Equal(46, row.Bounds.Height, 0.5));
            // fixed heights: the rows fit the frame in one pass (no jump) unless the cap cuts them, and then they scroll; the frame is what the arithmetic says
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            // An empty list is the one exception. nth step 7 (replacing the "Loading models…" line, :341-345): three skeleton rows of 40, so the frame
            // reserves those 3 * 40 = 120 points and the rows fill them exactly.
            if (empty) Assert.Equal(3 * 40, scroll.Extent.Height, 0.5);
            else if (height < 440) Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1.5, $"the rows ({scroll.Extent.Height}) fit the frame ({scroll.Viewport.Height}) with nothing to scroll");
            else Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "past the 440 cap the rows scroll");
            Assert.Equal(height, scroll.Viewport.Height, 0.5);
        }
    });

    // ---- UI-COMPOSER-043: the model row, in the list --------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-COMPOSER-043")] // App/Views/Composer/EffortSlider.swift:327-339,422-477
    public void A_model_row_is_the_name_the_detail_the_bolt_and_the_checkmark_and_a_press_sets_the_model_leaves_the_pair_and_may_stop_the_session() => headless.OnUiThread(() =>
    {
        // the row itself, in each of its forms: the icon, the bolt, the checkmark, locked
        foreach (var (icon, fast, selected, enabled) in new[] { (true, true, true, true), (true, false, false, true), (false, true, false, true), (false, false, true, true), (true, true, false, false) })
            EffortSliderModelRowTests.VerifyModelRow(icon, fast, selected, enabled);

        // in a list: 46 high rows with 1 point between them for two providers, the bolt on the model that has the fast tier, the tick on the thread's model
        // (and never while the chat leads a pair), a locked foreign row, and the press
        var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus", Name = "Led" };
        using var env = Make(() => new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", "high", RuntimeMode.Supervised) { HydraEnabled = true, HydraPairID = pair.Id, ProviderSessionID = "session", ProviderResumeAt = "resume" },
            model =>
            {
                model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair);
                foreach (var (kind, id) in new[] { (ProviderKind.Claude, "opus"), (ProviderKind.Claude, "sonnet"), (ProviderKind.Codex, "cx") }) model.Settings.AddToModelList(Pin(kind, id));
            },
            (ProviderKind.Claude, [Option("opus", "Opus", fast: true), Option("sonnet", "Sonnet")]), (ProviderKind.Codex, [Option("cx", "Cx", fast: true)]));
        AppModelHarness.WriteDocument(env.Thread.Id);
        var runtime = env.Model.Runtime(env.Thread.Id);
        var load = runtime.EnsureLoaded();
        for (var i = 0; i < 2000 && !load.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(5); }
        runtime.RehearseTurn("running");
        var panel = new ModelEffortPanel(env.Thread.Id, true); // the chat has history: the other provider's row is locked
        var window = env.Show(panel);
        OpenList(env, panel);
        var rows = Models(panel);
        Assert.Equal(["Opus", "Sonnet", "Cx"], rows.Select(Name));
        Assert.All(rows, row => Assert.Equal(46, row.Bounds.Height, 0.5));
        Assert.Equal(1, Assert.IsType<StackPanel>(rows[0].Parent!).Spacing);
        Assert.Equal([true, false, true], rows.Select(row => row.GetVisualDescendants().OfType<SwiftImage>().Any(image => image.SymbolName == "bolt"))); // the bolt only with a fast tier
        Assert.Equal([0d, 0d, 0d], rows.Select(Tick)); // the chat leads the pair: no model row is ticked, the pair is
        Assert.Equal(1, Tick(Pairs(panel).Single()));
        Assert.Equal([true, true, false], rows.Select(row => RowButton(row).IsEnabled));
        Assert.Equal([ProviderKind.Claude.DisplayName, ProviderKind.Claude.DisplayName, "New chats only"], rows.Select(Detail)); // two providers are listed: the provider's name is the detail; a locked row says it is for new chats
        Assert.Equal(0.45, RowButton(ModelRow(panel, "Cx")).Opacity);
        Click(window, ModelRow(panel, "Cx")); // locked: nothing happens
        Assert.Equal("opus", env.Model.Thread(env.Thread.Id)!.Model); Assert.Equal(pair.Id, env.Model.Thread(env.Thread.Id)!.HydraPairID);
        // a press on a row of the thread's own provider sets the model, leaves the pair, keeps the session and the running turn
        Click(window, ModelRow(panel, "Sonnet"));
        env.Step(130);
        var after = env.Model.Thread(env.Thread.Id)!;
        Assert.Equal("sonnet", after.Model); Assert.Null(after.HydraPairID); Assert.Equal("session", after.ProviderSessionID); Assert.True(runtime.IsRunning);
        OpenList(env, panel);
        Assert.Equal([0d, 1d, 0d], Models(panel).Select(Tick)); // now the model row carries the tick
        window.Close();

        // another provider (a chat with no history): the session is dropped and the running turn is stopped
        using var other = Make(() => new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "opus", "high", RuntimeMode.Supervised) { ProviderSessionID = "session" },
            model => { model.Settings.AddToModelList(Pin(ProviderKind.Claude, "opus")); model.Settings.AddToModelList(Pin(ProviderKind.Codex, "cx")); },
            (ProviderKind.Claude, [Option("opus", "Opus")]), (ProviderKind.Codex, [Option("cx", "Cx", fast: true)]));
        AppModelHarness.WriteDocument(other.Thread.Id);
        var otherRuntime = other.Model.Runtime(other.Thread.Id);
        var otherLoad = otherRuntime.EnsureLoaded();
        for (var i = 0; i < 2000 && !otherLoad.IsCompleted; i++) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(5); }
        otherRuntime.RehearseTurn("running");
        var otherPanel = new ModelEffortPanel(other.Thread.Id, false);
        var otherWindow = other.Show(otherPanel);
        OpenList(other, otherPanel);
        Click(otherWindow, ModelRow(otherPanel, "Cx"));
        other.Step(130);
        Assert.Equal(ProviderKind.Codex, other.Model.Thread(other.Thread.Id)!.Provider); Assert.Null(other.Model.Thread(other.Thread.Id)!.ProviderSessionID);
        Assert.False(otherRuntime.IsRunning);
    });

    // ---- UI-COMPOSER-042: the pair row, in the list -----------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-COMPOSER-042")] // App/Views/Composer/EffortSlider.swift:310-324,364-420
    public void A_pair_row_is_the_fused_mark_the_title_and_detail_and_the_checkmark_and_a_press_enters_the_pair_and_dismisses_the_popover() => headless.OnUiThread(() =>
    {
        foreach (var (enabled, selected) in new[] { (true, true), (true, false), (false, false) })
            EffortSliderModelRowTests.VerifyPairRow(enabled, selected);

        var led = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c1", OrchestratorEffort = "high", Name = "Named pair" };      // the user's own name: the detail names the models first
        var plain = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c2", WorkerProvider = ProviderKind.Claude };                  // no name: the title names the models
        var foreign = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus", Name = "Foreign" };
        using var env = Make(() => Thread(ProviderKind.Codex, "c1", hydra: true, pair: led.Id), model =>
        {
            model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(led); model.Settings.AddHydraPair(plain); model.Settings.AddHydraPair(foreign);
            model.Settings.AddToModelList(Pin(ProviderKind.Codex, "c1")); model.Settings.AddToModelList(Pin(ProviderKind.Codex, "c2"));
        }, (ProviderKind.Codex, [Option("c1", "Codex one"), Option("c2", "Codex two")]));
        var dismissed = 0;
        var panel = OpenPanelList(env, true, out var window, () => dismissed++);
        var rows = Pairs(panel);
        Assert.Equal(["Named pair", HydraPairSummary.Title(plain, env.Model.Providers), "Foreign"], rows.Select(Name));
        Assert.StartsWith("Codex two + ", Name(rows[1]), StringComparison.Ordinal); // the lead model, then the heads' (the title names the models when the pair has no name)
        Assert.Equal("Codex one + itself · Codex · Lead high effort", Detail(rows[0])); // the models named first, then the providers, then the efforts
        Assert.Equal("Codex lead, Claude heads", Detail(rows[1]));          // no models: the title has them
        Assert.Equal("New chats only", Detail(rows[2]));                    // locked: another provider, with history
        Assert.All(rows, row => Assert.Equal(46, row.Bounds.Height, 0.5));
        Assert.Equal(1, Assert.IsType<StackPanel>(rows[0].Parent!).Spacing); // 1 point of list spacing
        Assert.Equal([1d, 0d, 0d], rows.Select(Tick));
        Assert.Equal([true, true, false], rows.Select(row => RowButton(row).IsEnabled));
        Assert.Equal([1d, 1d, 0.45], rows.Select(row => RowButton(row).Opacity));
        Click(window, rows[2]); env.Step(130);
        Assert.Equal(0, dismissed);
        Click(window, rows[1]); env.Step(130);
        Assert.Equal(1, dismissed);
        Assert.Equal(plain.Id, env.Model.Thread(env.Thread.Id)!.HydraPairID); Assert.Equal("c2", env.Model.Thread(env.Thread.Id)!.Model);
    });
}
