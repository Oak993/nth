using Marrow.UI.Port.Nth;
using Avalonia;
using Avalonia.Automation;
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

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :153-359 ModelEffortPanel and ModelList, :364-477 the rows.
// Each scenario mounts the real list or panel over a real AppModel whose provider CLIs cannot resolve (fail closed: nothing is launched).
[Collection("Headless")]
public sealed class EffortSliderModelListTests(HeadlessSession headless)
{

    internal static EffortListEnv Make(Func<ChatThread>? thread = null, Action<AppModel>? configure = null, params (ProviderKind Provider, ModelOption[] Models)[] catalogues)
    {
        var owned = new List<IDisposable>();
        var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
        owned.Add(FrameClock.Source.Override(frames)); owned.Add(SwiftClock.Provider.Override(time));
        var harness = new AppModelHarness(withRuntime: false); owned.Add(harness);
        // ProviderRegistry.LoadCatalog awaits the process-global login environment on its first use; finish that prerequisite first
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var chat = thread?.Invoke() ?? new ChatThread(Guid.NewGuid(), ProviderKind.Codex, "c1", "medium", RuntimeMode.Supervised);
        harness.WriteLibrary([], [chat]);
        var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        foreach (var (provider, models) in catalogues) HydraCookbookResolveFixture.SetModels(model.Providers, provider, models);
        configure?.Invoke(model);
        var env = new EffortListEnv { Model = model, Harness = harness, Thread = chat, Time = time, Frames = frames };
        foreach (var value in owned) env.Own(value);
        return env;
    }

    internal static ModelOption Option(string id, string name, bool efforts = true, bool fast = false) =>
        new(id, name, efforts: efforts ? new ValueList<string>(["low", "medium", "high"]) : null, defaultEffort: efforts ? "medium" : null, fastTier: fast ? "fast" : null);

    internal static ModelPin Pin(ProviderKind provider, string id) => new(provider, id);

    internal static Button ButtonWithTip(Control root, string tip) =>
        root.GetVisualDescendants().OfType<Button>().Single(button => Equals(ToolTip.GetTip(button), tip));

    internal static void Click(Avalonia.Controls.Window window, Control control)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // pointer hit testing consumes the rendered scene, not just layout
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    internal static string Name(Control row) => row.GetVisualDescendants().OfType<TextBlock>().First().Text!;
    internal static string? Detail(Control row) => row.GetVisualDescendants().OfType<TextBlock>().Skip(1).FirstOrDefault()?.Text;
    internal static double Tick(Control row) => row.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == "checkmark").Opacity;
    internal static Button RowButton(Control row) => row.GetVisualDescendants().OfType<Button>().Single();
    internal static ModelListRow[] Models(Control root) => root.GetVisualDescendants().OfType<ModelListRow>().ToArray();
    internal static HydraPairListRow[] Pairs(Control root) => root.GetVisualDescendants().OfType<HydraPairListRow>().ToArray();
    internal static ModelListRow ModelRow(Control root, string name) => Models(root).Single(row => Name(row) == name);

    /// <summary>The panel with its list open: the title is tapped and the 0.12 s cross-fade has run.</summary>
    internal static ModelEffortPanel OpenPanelList(EffortListEnv env, bool hasHistory, out Avalonia.Controls.Window window, Action? dismissed = null)
    {
        var panel = new ModelEffortPanel(env.Thread.Id, hasHistory);
        if (dismissed is not null) Popovers.SetClosePopover(panel, dismissed);
        window = env.Show(panel);
        OpenList(env, panel);
        return panel;
    }

    internal static void OpenList(EffortListEnv env, ModelEffortPanel panel)
    {
        var card = Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
        ButtonWithTip(card, "Choose a model").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        env.Step(130);
        Assert.Single(panel.GetVisualDescendants().OfType<ModelList>());
    }

    internal static ChatThread Thread(ProviderKind provider, string? model, bool hydra = false, Guid? pair = null) =>
        new(Guid.NewGuid(), provider, model, "medium", RuntimeMode.Supervised) { HydraEnabled = hydra, HydraPairID = pair };

    // ---- PR-COMPOSER-144: the lock once there is history -------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-144")] // App/Views/Composer/EffortSlider.swift:313-320,328-336,688
    public void Cross_provider_rows_lock_once_the_thread_has_history_and_own_provider_rows_stay_pickable() => headless.OnUiThread(() =>
    {
        var ownPair = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c1", Name = "Own pair" };
        var foreignPair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus", Name = "Foreign pair" };
        foreach (var history in new[] { true, false })
        {
            using var env = Make(() => Thread(ProviderKind.Codex, "c1", hydra: true),
                model =>
                {
                    model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(ownPair); model.Settings.AddHydraPair(foreignPair);
                    foreach (var (kind, id) in new[] { (ProviderKind.Codex, "c1"), (ProviderKind.Codex, "c2"), (ProviderKind.Deepseek, "d1"), (ProviderKind.Deepseek, "d2") })
                        model.Settings.AddToModelList(Pin(kind, id));
                },
                (ProviderKind.Codex, [Option("c1", "C1"), Option("c2", "C2")]), (ProviderKind.Deepseek, [Option("d1", "D1"), Option("d2", "D2")]));
            var chosen = new List<string>(); var entered = new List<Guid>();
            var list = new ModelList(env.Thread, history, entry => chosen.Add(entry.Id), () => { }, pair => entered.Add(pair.Id));
            var window = env.Show(list);
            var rows = Models(list); var pairRows = Pairs(list);
            Assert.Equal(["C1", "C2", "D1", "D2"], rows.Select(Name));
            Assert.Equal(["Own pair", "Foreign pair"], pairRows.Select(Name));
            foreach (var row in rows)
            {
                var locked = history && Name(row).StartsWith('D');
                var button = RowButton(row);
                Assert.Equal(!locked, button.IsEnabled);
                Assert.Equal(locked ? 0.45 : 1, button.Opacity);
                // the detail reads exactly "New chats only" in place of the provider's name; an own-provider row keeps the provider's name
                Assert.Equal(locked ? "New chats only" : (Name(row).StartsWith('D') ? ProviderKind.Deepseek : ProviderKind.Codex).DisplayName, Detail(row));
            }
            foreach (var row in pairRows)
            {
                var locked = history && Name(row) == "Foreign pair";
                Assert.Equal(!locked, RowButton(row).IsEnabled);
                Assert.Equal(locked ? 0.45 : 1, RowButton(row).Opacity);
                if (locked) Assert.Equal("New chats only", Detail(row)); else Assert.NotEqual("New chats only", Detail(row));
            }
            // a press on a locked row does nothing; one on an own-provider row (and on every row without history) chooses
            foreach (var row in rows.Concat<Control>(pairRows)) Click(window, row);
            Assert.Equal(history ? ["codex/c1", "codex/c2"] : ["codex/c1", "codex/c2", "deepseek/d1", "deepseek/d2"], chosen);
            Assert.Equal(history ? [ownPair.Id] : [ownPair.Id, foreignPair.Id], entered);
        }
    });

    // ---- PR-COMPOSER-148: the checkmark ------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-148")] // App/Views/Composer/EffortSlider.swift:281,319,334; App/AppModel+HydraPairs.swift:21-23
    public void The_checkmark_sits_on_the_led_pair_and_never_on_a_model_row() => headless.OnUiThread(() =>
    {
        var pair = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c1", Name = "Led" };
        var elsewhere = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus", Name = "Elsewhere" };
        string Ticks(Func<ChatThread> thread, bool settingsHydra = true)
        {
            using var env = Make(thread, model =>
            {
                model.Settings.HydraEnabled = settingsHydra; model.Settings.AddHydraPair(pair); model.Settings.AddHydraPair(elsewhere);
                model.Settings.AddToModelList(Pin(ProviderKind.Codex, "c1")); model.Settings.AddToModelList(Pin(ProviderKind.Codex, "c2"));
            }, (ProviderKind.Codex, [Option("c1", "C1"), Option("c2", "C2")]));
            var list = new ModelList(env.Thread, false, _ => { }, () => { }, _ => { });
            env.Show(list);
            var pairRows = Pairs(list);
            return $"pairs[{(pairRows.Length == 0 ? "none" : $"Led {Tick(pairRows.Single(row => Name(row) == "Led"))}, Elsewhere {Tick(pairRows.Single(row => Name(row) == "Elsewhere"))}")}] models[{string.Join(", ", Models(list).Select(Tick))}]";
        }
        // leading the pair: the pair is ticked although its lead model c1 is a row of its own and matches the thread's provider and model
        Assert.Equal("pairs[Led 1, Elsewhere 0] models[0, 0]", Ticks(() => Thread(ProviderKind.Codex, "c1", hydra: true, pair: pair.Id)));
        // leading no pair: the model row is ticked, no pair row
        Assert.Equal("pairs[Led 0, Elsewhere 0] models[1, 0]", Ticks(() => Thread(ProviderKind.Codex, "c1", hydra: true)));
        // a pair on another provider than the chat's cannot be led (the provider must match): the model keeps the tick
        Assert.Equal("pairs[Led 0, Elsewhere 0] models[1, 0]", Ticks(() => Thread(ProviderKind.Codex, "c1", hydra: true, pair: elsewhere.Id)));
        // Hydra off for the chat (or in settings): no pairs section, and the model keeps the tick
        Assert.Equal("pairs[none] models[1, 0]", Ticks(() => Thread(ProviderKind.Codex, "c1", hydra: false, pair: pair.Id)));
        Assert.Equal("pairs[none] models[1, 0]", Ticks(() => Thread(ProviderKind.Codex, "c1", hydra: true, pair: pair.Id), settingsHydra: false));
        // leading the pair while on another model: still only the pair
        Assert.Equal("pairs[Led 1, Elsewhere 0] models[0, 0]", Ticks(() => Thread(ProviderKind.Codex, "c2", hydra: true, pair: pair.Id)));
    });

    // ---- PR-COMPOSER-146, UI-COMPOSER-040, UI-COMPOSER-041: the sections ---------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-146")] // App/Views/Composer/EffortSlider.swift:276-287,310-326,352-358
    [Trait("Req", "UI-COMPOSER-040")] // App/Views/Composer/EffortSlider.swift:310-311; App/UI/Chrome/Popovers.swift:117-132
    [Trait("Req", "UI-COMPOSER-041")] // App/Views/Composer/EffortSlider.swift:325
    public void The_Pairs_and_Models_headers_appear_only_with_hydra_on_and_pairs_in_the_source_order_and_the_pairs_providers_catalogues_load() => headless.OnUiThread(() =>
    {
        var claudeCodex = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus", WorkerProvider = ProviderKind.Codex, Name = "Mixed" };
        var codexSelf = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c1", Name = "Codex alone" };
        var ownProvider = new HydraPair(ProviderKind.Deepseek) { Name = "Chat's own" };
        (string Order, string Loaded) Scenario(bool settingsHydra, bool threadHydra, HydraPair[] pairs, Action<AppModel>? more = null)
        {
            using var env = Make(() => Thread(ProviderKind.Deepseek, "d1", hydra: threadHydra), model =>
            {
                model.Settings.HydraEnabled = settingsHydra;
                foreach (var pair in pairs) model.Settings.AddHydraPair(pair);
                model.Settings.AddToModelList(Pin(ProviderKind.Deepseek, "d1"));
                more?.Invoke(model);
            }, (ProviderKind.Deepseek, [Option("d1", "D1")]));
            var loading = new HashSet<ProviderKind>();
            env.Model.Providers.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(env.Model.Providers.LoadingCatalogs)) foreach (var kind in env.Model.Providers.LoadingCatalogs) loading.Add(kind);
            };
            var list = new ModelList(env.Thread, false, _ => { }, () => { }, _ => { });
            var window = env.Show(list);
            Dispatcher.UIThread.RunJobs();
            var content = Assert.IsType<StackPanel>(Models(list)[0].Parent!);
            var order = content.Children.Select(child => child switch
            {
                PopoverSectionHeader header => "H:" + header.GetVisualDescendants().OfType<TextBlock>().Single().Text,
                HydraPairListRow row => "P:" + Name(row),
                ModelListRow row => "M:" + Name(row),
                var other => other.GetType().Name,
            }).ToArray();
            if (order.Contains("H:Pairs")) // UI-040/041: the two headers, subheadline semibold (11) in secondary text, padded 8, 6, 8, 3, and 22 tall as the arithmetic counts them
                foreach (var header in content.Children.OfType<PopoverSectionHeader>())
                {
                    var text = header.GetVisualDescendants().OfType<TextBlock>().Single();
                    Assert.Equal(11, text.FontSize); Assert.Equal(FontWeight.SemiBold, text.FontWeight);
                    Assert.Equal(Chrome.SecondaryText.Resolve(text.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
                    Assert.Equal(new Thickness(8, 6, 8, 3), text.Margin); Assert.Equal(22, header.Bounds.Height, 0.5);
                    Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, text.HorizontalAlignment);
                }
            return (string.Join(" | ", order), string.Join(",", loading.OrderBy(kind => kind.Id).Select(kind => kind.Id)));
        }

        // Hydra on with pairs: "Pairs", the pair rows in settings order, "Models", the model rows; the catalogues of every provider a pair names
        // (lead and heads) other than the chat's own are loaded, so their models can be named
        var full = Scenario(true, true, [claudeCodex, codexSelf, ownProvider]);
        Assert.Equal("H:Pairs | P:Mixed | P:Codex alone | P:Chat's own | H:Models | M:D1", full.Order);
        Assert.Equal(string.Join(",", new[] { ProviderKind.Claude, ProviderKind.Codex }.OrderBy(kind => kind.Id).Select(kind => kind.Id)), full.Loaded);
        // Hydra off in the chat, or in settings: no header and no pair, the models stand alone, nothing is loaded for the pairs
        Assert.Equal(("M:D1", ""), Scenario(true, false, [claudeCodex, codexSelf]));
        Assert.Equal(("M:D1", ""), Scenario(false, true, [claudeCodex, codexSelf]));
        // Hydra on without any pair: the same
        Assert.Equal(("M:D1", ""), Scenario(true, true, []));
        // a pair whose provider is not installed is not offered (hydraPickerPairs), and its catalogue is not loaded
        var hidden = Scenario(true, true, [claudeCodex, codexSelf], model => HydraCookbookResolveFixture.SetInstalled(model.Providers, ProviderKind.Claude, false));
        Assert.Equal("H:Pairs | P:Codex alone | H:Models | M:D1", hidden.Order);
        Assert.Equal(ProviderKind.Codex.Id, hidden.Loaded);
    });

    // ---- PR-COMPOSER-145, UI-COMPOSER-044: the placeholder ------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-COMPOSER-145")] // App/Views/Composer/EffortSlider.swift:137-138,340-345
    [Trait("Req", "UI-COMPOSER-044")] // App/Views/Composer/EffortSlider.swift:340-345,286-287,348-351
    public void Loading_models_stands_in_for_an_empty_list_until_the_catalogue_arrives_and_each_chip_loads_its_providers_catalogue() => headless.OnUiThread(() =>
    {
        using var env = Make(() => Thread(ProviderKind.Codex, null), model => model.Settings.AddToModelList(Pin(ProviderKind.Codex, "late")));
        var list = new ModelList(env.Thread, false, _ => { }, () => { });
        var listWindow = env.Show(list);
        // nothing resolves yet: nth step 7's three skeleton rows (40 points each), padded 10 at the sides, stand in for the line of text; the
        // text "Loading models…" (with the one-character ellipsis, U+2026) stays as their accessible name
        Assert.DoesNotContain(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text != "Select model");
        var placeholder = Assert.Single(list.GetVisualDescendants().OfType<NthSkeleton>());
        var name = AutomationProperties.GetName(placeholder);
        Assert.Equal("Loading models…", name); Assert.Equal(0x2026, name![^1]); Assert.Equal(15, name.Length);
        Assert.Equal(NthSkeletonKind.List, placeholder.Kind); Assert.Equal(3, placeholder.Rows); Assert.Equal(120, placeholder.Height);
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(placeholder));
        Assert.Equal(new Thickness(10, 0), placeholder.Margin);
        Assert.Empty(Models(list));
        var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
        Assert.Equal(120, scroll.Height); // the list reserves the three rows
        // the catalogue arrives: the model takes the placeholder's place and the placeholder is gone; one row reserves the same 34 + 1
        HydraCookbookResolveFixture.SetModels(env.Model.Providers, ProviderKind.Codex, Option("late", "Late arrival"));
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.Equal(["Late arrival"], Models(list).Select(Name));
        Assert.DoesNotContain(list.GetVisualDescendants().OfType<NthSkeleton>(), _ => true);
        Assert.DoesNotContain(list.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Loading models…");
        Assert.Equal(35, list.GetVisualDescendants().OfType<ScrollViewer>().Single().Height);
        // the arriving rows rise in (NthArrive.Rows): an opacity transition of 0.36 s on the model row
        Assert.Contains(Models(list).Single().Transitions!, t => t is Avalonia.Animation.DoubleTransition { Duration: var d } && d == TimeSpan.FromSeconds(.36));

        // the chip itself starts the catalogue load of its thread's provider, and a chip made for another provider (the composer draws a new chip
        // when the thread's provider changes) loads that provider's: Claude and Codex are the seeded ones whose load runs and ends with no executable
        var loading = new List<ProviderKind>();
        env.Model.Providers.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(env.Model.Providers.LoadingCatalogs)) foreach (var kind in env.Model.Providers.LoadingCatalogs) if (!loading.Contains(kind)) loading.Add(kind);
        };
        listWindow.Close();
        var slot = new Border();
        var chipWindow = env.Show(slot);
        slot.Child = new ModelEffortButton(Thread(ProviderKind.Claude, "opus"), false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([ProviderKind.Claude], loading);
        slot.Child = new ModelEffortButton(Thread(ProviderKind.Codex, "c1"), false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([ProviderKind.Claude, ProviderKind.Codex], loading);
        Assert.Empty(env.Model.Providers.LoadingCatalogs); // every load cleared its mark
        chipWindow.Close();
    });
}
