using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :153-228 ModelEffortPanel (PR-COMPOSER-109: 330 wide,
// the 0.12 s cross-fade, presentedChrome), :261-359 ModelList (PR-COMPOSER-141: the header, the back chevron;
// PR-COMPOSER-142: the fixed row heights). Each scenario mounts the real panel or list over a real AppModel.
[Collection("Headless")]
public sealed class EffortSliderPanelTests(HeadlessSession headless)
{

    // A Codex chat; the provider CLIs cannot resolve, so nothing real is launched (fail closed).
    private static EffortPanelEnv Make(Func<ChatThread>? thread = null, Action<AppModel>? configure = null, params (ProviderKind Provider, ModelOption[] Models)[] catalogues)
    {
        var owned = new List<IDisposable>();
        var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
        owned.Add(FrameClock.Source.Override(frames)); owned.Add(SwiftClock.Provider.Override(time));
        var harness = new AppModelHarness(withRuntime: false); owned.Add(harness);
        var chat = thread?.Invoke() ?? new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "c1", "medium", RuntimeMode.Supervised);
        harness.WriteLibrary([], [chat]);
        var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        foreach (var (provider, models) in catalogues) HydraCookbookResolveFixture.SetModels(model.Providers, provider, models);
        configure?.Invoke(model);
        var env = new EffortPanelEnv { Model = model, Thread = chat, Time = time, Frames = frames };
        foreach (var value in owned) env.Own(value);
        return env;
    }

    private static ModelOption Option(string id, string name, bool efforts = true, bool fast = false) =>
        new(id, name, efforts: efforts ? new ValueList<string>(["low", "medium", "high"]) : null, defaultEffort: efforts ? "medium" : null, fastTier: fast ? "fast" : null);

    private static ModelOption[] Options(string prefix, int count) =>
        Enumerable.Range(1, count).Select(i => Option($"{prefix}{i}", $"{prefix.ToUpperInvariant()} model {i}")).ToArray();

    private static Button ButtonWithTip(Control root, string tip) =>
        root.GetVisualDescendants().OfType<Button>().Single(button => Equals(ToolTip.GetTip(button), tip));

    private static void Click(Avalonia.Controls.Window window, Control control)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // pointer hit testing consumes the rendered scene, not just layout
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Invoke(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    // ---- PR-COMPOSER-109: the panel -------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-109")] // App/Views/Composer/EffortSlider.swift:161-188 .frame(width: 330) .animation(.easeOut(duration: 0.12), value: showsModels), both branches .transition(.opacity)
    [Trait("Req", "UI-COMPOSER-030")] // App/Views/Composer/EffortSlider.swift:161-188 the card is 330 wide and cross-fades to the model list over 0.12 s ease-out
    public void The_panel_is_330_wide_and_cross_fades_the_slider_and_the_list_in_point_one_two_seconds_both_ways() => headless.OnUiThread(() =>
    {
        using var env = Make(catalogues: [(ProviderKind.Codex, Options("c", 3))]);
        var panel = new ModelEffortPanel(env.Thread.Id, false);
        var window = env.Show(panel);
        var ease = Animation.EaseOut(0.12);
        void Width() { Assert.Equal(330, panel.Width); Assert.Equal(330, panel.Bounds.Width, 0.5); } // :186 .frame(width: 330) at every step
        Width();
        var card = Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        Assert.Equal(1, card.Opacity);
        Assert.Empty(panel.GetVisualDescendants().OfType<ModelList>());

        // the title is tapped: the list comes in as the slider goes
        Invoke(ButtonWithTip(card, "Choose a model"));
        var list = Assert.Single(panel.GetVisualDescendants().OfType<ModelList>());
        Assert.Equal(0, list.Opacity, 3); // the incoming page starts clear
        Assert.Equal(1, card.Opacity, 3); // the outgoing one starts whole, and is still there
        Width();
        foreach (var milliseconds in new[] { 30, 60, 90 })
        {
            env.Step(30);
            var p = ease.Progress(milliseconds / 1000.0);
            Assert.Equal(p, list.Opacity, 3);
            Assert.Equal(1 - p, card.Opacity, 3);
            Assert.True(panel.IsVisualAncestorOf(card), "the outgoing slider stays until it has faded");
            Width();
        }
        Assert.True(list.Opacity > 0.2 && list.Opacity < 1, $"ease-out is under way at 90 ms: {list.Opacity}");
        env.Step(30); // 120 ms
        Assert.Equal(1, list.Opacity, 3);
        Assert.False(panel.IsVisualAncestorOf(card)); // the slider is gone
        Width();

        // the list's back chevron: the slider comes back as the list goes, over the same 0.12 s
        Invoke(ButtonWithTip(list, "Back"));
        var back = Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        Assert.Equal(0, back.Opacity, 3);
        Assert.Equal(1, list.Opacity, 3);
        foreach (var milliseconds in new[] { 30, 60, 90 })
        {
            env.Step(30);
            var p = ease.Progress(milliseconds / 1000.0);
            Assert.Equal(p, back.Opacity, 3);
            Assert.Equal(1 - p, list.Opacity, 3);
            Width();
        }
        env.Step(30);
        Assert.Equal(1, back.Opacity, 3);
        Assert.False(panel.IsVisualAncestorOf(list));
        Assert.Empty(panel.GetVisualDescendants().OfType<ModelList>());
        Width();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-109")] // App/Views/Composer/EffortSlider.swift:162 `if let thread = model.thread(threadID)`: the thread disappears and the panel renders nothing
    public void A_vanished_thread_renders_nothing_and_a_present_one_renders_the_card() => headless.OnUiThread(() =>
    {
        using var env = Make(catalogues: [(ProviderKind.Codex, Options("c", 3))]);
        var panel = new ModelEffortPanel(Guid.NewGuid(), false); // no such thread from the start
        var window = env.Show(panel);
        Assert.Empty(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        Assert.Empty(panel.GetVisualDescendants().OfType<ModelList>());
        panel.Child = null; window.Close();

        var live = new ModelEffortPanel(env.Thread.Id, false);
        env.Show(live);
        Assert.Single(live.GetVisualDescendants().OfType<EffortSliderCard>());
        env.Model.Delete(env.Thread.Id); // the chat goes while its popover is open
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(live.GetVisualDescendants().OfType<EffortSliderCard>());
        Assert.Empty(live.GetVisualDescendants().OfType<ModelList>());
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-109")] // App/Views/Composer/EffortSlider.swift:120-123 the popover's content is .presentedChrome(): capsule buttons and the accent tint
    [Trait("Req", "UI-COMPOSER-030")] // App/Views/Composer/EffortSlider.swift:120-123 the card's colours are the popover chrome
    public void The_chips_popover_content_is_the_panel_wrapped_in_presented_chrome() => headless.OnUiThread(() =>
    {
        using var env = Make(catalogues: [(ProviderKind.Codex, Options("c", 3))]);
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        var chip = new ModelEffortButton(env.Thread, false);
        env.Show(chip);
        Invoke(chip.GetVisualDescendants().OfType<Button>().Single());
        var (popup, presented) = presenter.Calls[^1];
        Assert.True(presented);
        var content = Assert.IsType<ModelEffortPanel>(PopoverHost.Content(popup));
        Assert.Equal(330, content.Width);
        Assert.True(Popovers.GetIsCapsuleButtonBorderShape(content)); // Popovers.swift:8-12 presentedChrome
        Assert.Equal(ThemeManager.Spec.Accent, Popovers.GetTint(content));
        Assert.Same(env.Model, AppEnvironment.GetAppModel(content)); // and the panel reads the chat from the same model
    });

    // ---- PR-COMPOSER-141: the list's header and its back chevron ---------------------------------------------------------

    private static ModelList MakeList(EffortPanelEnv env, Action? onBack = null) =>
        new(env.Thread, false, _ => { }, onBack ?? (() => { }));

    [Fact]
    [Trait("Req", "PR-COMPOSER-141")] // App/Views/Composer/EffortSlider.swift:289-306 `.frame(width: 20, height: 20).contentShape(.rect)`, :351 .padding(6)
    public void Back_is_hit_anywhere_in_its_20_point_box_and_calls_back_once() => headless.OnUiThread(() =>
    {
        using var env = Make(catalogues: [(ProviderKind.Codex, Options("c", 3))]);
        var backs = 0;
        var list = MakeList(env, onBack: () => backs++);
        var window = env.Show(list);
        var button = ButtonWithTip(list, "Back");
        var box = button.GetVisualDescendants().OfType<Border>().First(border => border.Width == 20);
        Assert.Equal((20, 20), (box.Width, box.Height));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        // every corner and the middle of the frame, the glyph's own pixels or not
        foreach (var (x, y) in new[] { (1.0, 1.0), (19.0, 1.0), (1.0, 19.0), (19.0, 19.0), (10.0, 10.0) })
        {
            var at = box.TranslatePoint(new Point(x, y), window)!.Value;
            var hit = window.InputHitTest(at) as Visual;
            Assert.True(hit is not null && (hit == button || button.IsVisualAncestorOf(hit)), $"({x}, {y}) of the 20 point box hits {hit?.GetType().Name ?? "nothing"}, not the back button");
        }
        var corner = box.TranslatePoint(new Point(1, 1), window)!.Value;
        window.MouseMove(corner);
        window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, backs);
        // outside the box nothing is hit as the button
        var outside = box.TranslatePoint(new Point(-3, 10), window)!.Value;
        Assert.False(window.InputHitTest(outside) is Visual miss && (miss == button || button.IsVisualAncestorOf(miss)));
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-141")] // App/Views/Composer/EffortSlider.swift:289-306 the header: chevron.left at .subheadline semibold, "Select model" at .body in secondary, spacing 6, padding 8 / 8 / 4
    public void The_header_is_a_back_chevron_and_select_model_with_the_source_fonts_colours_and_spacing() => headless.OnUiThread(() =>
    {
        using var env = Make(catalogues: [(ProviderKind.Codex, Options("c", 3))]);
        var list = MakeList(env);
        var window = env.Show(list);
        var button = ButtonWithTip(list, "Back");
        var chevron = button.GetVisualDescendants().OfType<SwiftImage>().Single();
        Assert.Equal("chevron.left", chevron.SymbolName);
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(chevron)); // :290-292 11 point semibold
        var ink = Assert.IsAssignableFrom<ISolidColorBrush>(chevron.Foreground).Color;
        Assert.Equal(Chrome.SecondaryText.Resolve(chevron.ActualThemeVariant), ink); // :297 .foregroundStyle(Chrome.secondaryText)
        var label = list.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Select model");
        Assert.Equal(13, label.FontSize); // :300 .font(.system(.body)), regular
        Assert.Equal(FontWeight.Normal, label.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(label.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color); // :301
        // :348-353 the list is padded 6 round; the header (:303-305) 8 / 8 / 4 with 6 between the chevron's frame and the label
        var box = button.GetVisualDescendants().OfType<Border>().First(border => border.Width == 20);
        var origin = box.TranslatePoint(default, list)!.Value;
        Assert.Equal(6 + 8, origin.X, 0.5);
        Assert.Equal(6 + 8, origin.Y, 0.5);
        Assert.Equal(origin.X + 20 + 6, label.TranslatePoint(default, list)!.Value.X, 0.5);
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        // ED-NEW-gate-fixes-1 (OD-0126): owner task T46 put the model search field between the header and the rows, so the rows no longer begin 4 under the header.
        // The pin is at least as tight: the field is exactly 28 high with margins 4 / 4 / 4 / 8 (ModelListSearch), 4 below the header, and the rows begin exactly 8 under it.
        var searchField = list.GetVisualDescendants().OfType<TextBox>().Single(field => Avalonia.Automation.AutomationProperties.GetName(field) == "Search models");
        var searchBox = searchField.GetVisualAncestors().OfType<Border>().First(border => border.Height == 28);
        var searchOrigin = searchBox.TranslatePoint(default, list)!.Value;
        Assert.Equal(6 + 4, searchOrigin.X, 0.5);                                   // list padding 6 + field margin 4
        Assert.Equal(6 + 8 + 20 + 4 + 4, searchOrigin.Y, 0.5);                      // 4 under the header (its bottom margin) plus the field's own 4 top margin = 42
        Assert.Equal(list.Bounds.Width - 2 * (6 + 4), searchBox.Bounds.Width, 0.5); // 4 margin each side inside the 6 padding
        Assert.Equal(28, searchBox.Bounds.Height, 0.5);
        Assert.Equal(78, scroll.TranslatePoint(default, list)!.Value.Y, 0.5);       // the rows begin 78 down
        Assert.Equal(searchOrigin.Y + 28 + 8, scroll.TranslatePoint(default, list)!.Value.Y, 0.5); // exactly the field's bottom margin (8) below the field
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-141")] // App/Views/Composer/EffortSlider.swift:198 `onBack: { showsModels = false }`: Back returns to the slider
    public void Pressing_back_in_the_panel_returns_to_the_effort_slider() => headless.OnUiThread(() =>
    {
        using var env = Make(catalogues: [(ProviderKind.Codex, Options("c", 3))]);
        var panel = new ModelEffortPanel(env.Thread.Id, false);
        var window = env.Show(panel);
        Invoke(ButtonWithTip(panel, "Choose a model"));
        env.Step(130);
        Assert.Single(panel.GetVisualDescendants().OfType<ModelList>());
        Assert.Empty(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        var box = ButtonWithTip(panel, "Back").GetVisualDescendants().OfType<Border>().First(border => border.Width == 20);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var corner = box.TranslatePoint(new Point(1, 1), window)!.Value; // the frame's own corner, away from the glyph
        window.MouseMove(corner);
        window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        env.Step(130);
        Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        Assert.Empty(panel.GetVisualDescendants().OfType<ModelList>());
        Assert.Equal(env.Thread.Model, env.Model.Thread(env.Thread.Id)!.Model); // going back chose nothing
    });

    // ---- PR-COMPOSER-142: the fixed row heights ---------------------------------------------------------------------------

    private static ModelPin Pin(ProviderKind provider, string id) => new(provider, id);

    private static (ScrollViewer Scroll, ModelListRow[] Rows, HydraPairListRow[] PairRows, PopoverSectionHeader[] Headers) Measure(ModelList list)
    {
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        return (scroll, list.GetVisualDescendants().OfType<ModelListRow>().ToArray(), list.GetVisualDescendants().OfType<HydraPairListRow>().ToArray(),
            list.GetVisualDescendants().OfType<PopoverSectionHeader>().ToArray());
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-142")] // App/Views/Composer/EffortSlider.swift:271-287,348-349 rowHeight 34 or 46, pairsHeight 22 * 2 + n * 47, min(.., 440)
    [InlineData(1, 3, false, false, false, 34, 105)]   // one provider, three models: 3 * (34 + 1)
    [InlineData(3, 2, false, false, false, 46, 282)]   // three providers show their icons: 6 * (46 + 1)
    [InlineData(2, 1, false, false, false, 46, 94)]    // two providers
    [InlineData(1, 2, true, true, false, 46, 94)]      // only a foreign provider's models, and the chat has history: they are locked, so 46 (:286 hasHistory && contains)
    [InlineData(1, 2, true, false, false, 34, 70)]     // the same foreign-only list without history: nothing is locked and one provider shows no icons, so 34 (the `hasHistory &&` term)
    [InlineData(1, 2, false, false, false, 34, 70)]    // the thread's own provider, no history: 2 * 35
    [InlineData(2, 5, false, false, false, 46, 440)]   // ten rows would be 470: the popover stops at 440
    [InlineData(1, 1, false, false, true, 34, 35)]     // no model at all: the "Loading models…" line takes one reserved row
    public void Model_list_heights_follow_the_fixed_row_arithmetic(int providers, int perProvider, bool foreignOnly, bool history, bool empty, double rowHeight, double scrollHeight) =>
        headless.OnUiThread(() =>
        {
            ProviderKind[] kinds = [ProviderKind.Codex, ProviderKind.Deepseek, ProviderKind.Meta];
            // the thread's own model is "c1": a Codex model when the scenario lists Codex, an unlisted id when only foreign providers are listed
            var own = foreignOnly || empty ? "unlisted" : "c1";
            var listed = foreignOnly ? new[] { ProviderKind.Deepseek } : empty ? [] : kinds.Take(providers).ToArray();
            var catalogues = listed.Select(kind => (kind, Options(kind == ProviderKind.Codex ? "c" : kind == ProviderKind.Deepseek ? "d" : "m", perProvider))).ToArray();
            using var env = Make(() => new ChatThread(Guid.NewGuid(), ProviderKind.Codex, own, "medium", RuntimeMode.Supervised),
                model =>
                {
                    if (empty) model.Settings.AddToModelList(Pin(ProviderKind.Codex, "nothing-resolves-this")); // the list is chosen, and its one model is not in any catalogue
                    else foreach (var (kind, models) in catalogues) foreach (var option in models) model.Settings.AddToModelList(Pin(kind, option.Id));
                },
                catalogues);
            var list = new ModelList(env.Thread, history, _ => { }, () => { });
            env.Show(list);
            var (scroll, rows, _, _) = Measure(list);
            Assert.True(scrollHeight == scroll.Height, $"scroll {scroll.Height}, expected {scrollHeight}; rows {rows.Length}");
            if (empty) { Assert.Empty(rows); return; }
            Assert.Equal(providers * perProvider, rows.Length);
            Assert.All(rows, row => Assert.Equal(rowHeight, row.Bounds.Height, 0.5)); // each row is exactly 34 or 46 tall
            var content = Assert.IsType<StackPanel>(rows[0].Parent!);
            Assert.Equal(1, content.Spacing); // :288 LazyVStack(spacing: 1)
            if (scrollHeight < 440) Assert.True(content.Bounds.Height <= scroll.Height + 0.5, $"the rows ({content.Bounds.Height}) fit the frame ({scroll.Height})");
            else Assert.True(content.Bounds.Height > scroll.Height, "the rows past the 440 cap scroll");
        });

    [Fact]
    [Trait("Req", "PR-COMPOSER-142")] // App/Views/Composer/EffortSlider.swift:273,280,286-287,310-325 the pairs section: two headers of 22 and n rows of 46 + 1
    public void The_pairs_section_adds_two_headers_of_22_and_a_47_point_row_per_pair() => headless.OnUiThread(() =>
    {
        var first = new HydraPair(ProviderKind.Codex) { Name = "Alpha" };
        var second = new HydraPair(ProviderKind.Codex) { Name = "Beta" };
        using var env = Make(() => new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "c1", "medium", RuntimeMode.Supervised) { HydraEnabled = true },
            model =>
            {
                model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(first); model.Settings.AddHydraPair(second);
                foreach (var option in Options("c", 3)) model.Settings.AddToModelList(Pin(ProviderKind.Codex, option.Id));
            }, (ProviderKind.Codex, Options("c", 3)));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        env.Show(list);
        var (scroll, rows, pairRows, headers) = Measure(list);
        Assert.Equal(2, pairRows.Length);
        Assert.Equal(3, rows.Length);
        Assert.Equal(22 * 2 + 2 * (46 + 1) + 3 * (34 + 1), scroll.Height); // 44 + 94 + 105
        Assert.All(pairRows, row => Assert.True(Math.Abs(row.Bounds.Height - 46) <= 0.5, $"pair row {row.Bounds.Height}")); // :271 pairRowHeight
        Assert.All(rows, row => Assert.True(Math.Abs(row.Bounds.Height - 34) <= 0.5, $"model row {row.Bounds.Height}")); // one provider: 34
        Assert.Equal(["Pairs", "Models"], headers.Select(header => header.GetVisualDescendants().OfType<TextBlock>().Single().Text));
        Assert.All(headers, header => Assert.True(Math.Abs(header.Bounds.Height - 22) <= 0.5, $"section header {header.Bounds.Height}")); // :273 sectionHeaderHeight, the line and its padding
        var content = Assert.IsType<StackPanel>(rows[0].Parent!);
        // the laid-out rows are the reserved frame plus the source's own one point: the arithmetic (:287) counts a 1 point gap for
        // every row and none for the two headers, the stack has one more gap than rows (7 children, 6 gaps against the 5 reserved)
        Assert.True(Math.Abs(content.Bounds.Height - (scroll.Height + 1)) <= 0.5, $"{content.Bounds.Height} against the frame {scroll.Height}");
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-142")] // App/Views/Composer/EffortSlider.swift:286 a locked cross-provider row makes every row 46 even with one listed provider
    public void Rows_stay_34_tall_when_the_chat_has_history_but_every_row_is_its_own_provider() => headless.OnUiThread(() =>
    {
        using var env = Make(() => new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "c1", "medium", RuntimeMode.Supervised),
            model => { foreach (var option in Options("c", 2)) model.Settings.AddToModelList(Pin(ProviderKind.Codex, option.Id)); },
            (ProviderKind.Codex, Options("c", 2)));
        var plain = new ModelList(env.Thread, true, _ => { }, () => { }); // history, but every row is the chat's own provider: nothing locked
        env.Show(plain);
        Assert.All(Measure(plain).Rows, row => Assert.Equal(34, row.Bounds.Height, 0.5));
        Assert.Equal(2 * 35, Measure(plain).Scroll.Height);
    });
}
