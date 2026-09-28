using System.Reflection;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :204-235,271,364-419,521-534,626-638.
[Collection("Headless")]
public sealed class EffortSliderVerificationTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content, AppModel? model = null)
    {
        var host = new Border { Child = content };
        if (model is not null) AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 400, Height = 350 };
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); return window;
    }

    private static Point Centre(Control control, Avalonia.Controls.Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Avalonia.Controls.Window window, Control control)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // pointer hit testing consumes the rendered scene, not just layout
        var point = Centre(control, window);
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Effort(ModelEffortPanel panel, int index)
    {
        var slider = Assert.Single(panel.GetVisualDescendants().OfType<EffortSlider>());
        Assert.IsAssignableFrom<IRangeValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider)).SetValue(index);
        Dispatcher.UIThread.RunJobs();
    }

    private static AppModel Model(AppModelHarness harness, Project project, ChatThread thread)
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        harness.WriteLibrary([project], [thread]);
        var model = new AppModel();
        model.Settings.DefaultProvider = thread.Provider; // new-chat scenario stays on the fixture's model/provider without Choose
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        return model;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-122")] // App/Views/Composer/EffortSlider.swift:204-208,626-638
    [Trait("Req", "PR-COMPOSER-124")] // App/Views/Composer/EffortSlider.swift:230-235; App/AppSettings.swift:859-862,899-902
    public void Slider_choice_alone_remembers_the_effort_and_restarts_on_a_new_chat()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var project = new Project("Fixture", harness.NewFolder("project"));
            var thread = new ChatThread(project.Id, ProviderKind.Claude, "opus", "low", RuntimeMode.Supervised);
            var model = Model(harness, project, thread);
            var panel = new ModelEffortPanel(thread.Id, false);
            var window = Show(panel, model);
            Animation? transaction = null;
            model.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(model.Threads) && model.Thread(thread.Id)!.Effort == "high") transaction = Transaction.Current;
            };
            Effort(panel, 2); // actual range action -> production card binding -> thread write -> Remember
            Assert.Equal(Animation.Snappy(0.2), transaction); // Swift :633 snappy duration 0.2, ED-092(2)
            Assert.Equal("high", model.Thread(thread.Id)!.Effort);
            window.Close();
            var restarted = new AppModel(); // no fast toggle and no Choose can repair a missing effort Remember
            Assert.Equal(new ModelPreference { Effort = "high" }, restarted.Settings.Preference(ProviderKind.Claude, "opus"));
            Assert.Equal("opus", restarted.Settings.LastModel(ProviderKind.Claude));
            Assert.Equal("high", restarted.Settings.LastEffort(ProviderKind.Claude));
            Assert.Equal("opus", harness.Defaults.Dictionary("lastModels")!["claude"]);
            Assert.Equal("high", harness.Defaults.Dictionary("lastEfforts")!["claude"]);
            restarted.SelectedThreadID = null;
            var fresh = Assert.IsType<ChatThread>(restarted.NewThread(project, WorkspaceMode.Local)); // AppModel.swift:668-680
            Assert.Equal("opus", fresh.Model); Assert.Equal("high", fresh.Effort); Assert.False(fresh.FastMode);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-124")] // App/Views/Composer/EffortSlider.swift:210-217,230-235
    public void A_fast_toggle_alone_is_remembered_before_any_other_action()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var project = new Project("Fixture", harness.NewFolder("project"));
            var thread = new ChatThread(project.Id, ProviderKind.Claude, "opus", "low", RuntimeMode.Supervised);
            var model = Model(harness, project, thread);
            var panel = new ModelEffortPanel(thread.Id, false);
            var window = Show(panel, model);
            Click(window, Assert.Single(Assert.Single(panel.GetVisualDescendants().OfType<FastModeButton>()).GetVisualDescendants().OfType<Button>()));
            Assert.True(model.Thread(thread.Id)!.FastMode);
            window.Close();
            var restarted = new AppModel();
            Assert.Equal(new ModelPreference { Effort = "low", FastMode = true }, restarted.Settings.Preference(ProviderKind.Claude, "opus"));
            Assert.Equal("opus", restarted.Settings.LastModel(ProviderKind.Claude));
            Assert.Equal("low", restarted.Settings.LastEffort(ProviderKind.Claude));
            restarted.SelectedThreadID = null;
            var fresh = Assert.IsType<ChatThread>(restarted.NewThread(project, WorkspaceMode.Local));
            Assert.Equal("opus", fresh.Model); Assert.Equal("low", fresh.Effort); Assert.True(fresh.FastMode);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-122")] // App/Views/Composer/EffortSlider.swift:802-818,204-208,626-638
    public void A_real_pointer_drag_writes_and_remembers_the_selected_effort()
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
            using var clock = SwiftClock.Provider.Override(time); using var source = FrameClock.Source.Override(frames);
            using var harness = new AppModelHarness(withRuntime: false);
            var project = new Project("Fixture", harness.NewFolder("project"));
            var thread = new ChatThread(project.Id, ProviderKind.Claude, "opus", "low", RuntimeMode.Supervised);
            var model = Model(harness, project, thread); var panel = new ModelEffortPanel(thread.Id, false);
            var window = Show(panel, model); var slider = Assert.Single(panel.GetVisualDescendants().OfType<EffortSlider>());
            var start = slider.TranslatePoint(new Point(21, 21), window)!.Value; // Swift :740-754 thumb width 42, inset 21
            var end = slider.TranslatePoint(new Point(21 + (slider.Bounds.Width - 42) * 3 / 4, 21), window)!.Value; // Claude's five levels, stop 3 = xhigh
            window.MouseMove(start); window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(end); window.MouseUp(end, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
            Assert.Equal("xhigh", model.Thread(thread.Id)!.Effort);
            window.Close();
            var restarted = new AppModel();
            Assert.Equal(new ModelPreference { Effort = "xhigh" }, restarted.Settings.Preference(ProviderKind.Claude, "opus"));
            Assert.Equal("opus", restarted.Settings.LastModel(ProviderKind.Claude)); Assert.Equal("xhigh", restarted.Settings.LastEffort(ProviderKind.Claude));
        });
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-121")] // App/Views/Composer/EffortSlider.swift:219-226,521-525
    [InlineData("high", true, "High")]
    [InlineData("invalid", true, "Medium")]
    [InlineData("invalid", false, "Low")]
    public void Reset_resolves_default_then_medium_then_first_through_the_real_panel(string defaultEffort, bool offersMedium, string expectedTitle)
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
            using var clock = SwiftClock.Provider.Override(time); using var source = FrameClock.Source.Override(frames);
            using var harness = new AppModelHarness(withRuntime: false);
            var project = new Project("Fixture", harness.NewFolder("project"));
            var thread = new ChatThread(project.Id, ProviderKind.Claude, "fixture", "high", RuntimeMode.Supervised, true);
            var model = Model(harness, project, thread);
            Marrow.App.Tests.App.Fixtures.HydraCookbookResolveFixture.SetModels(model.Providers, ProviderKind.Claude,
                new ModelOption("fixture", "Fixture", efforts: new ValueList<string>(offersMedium ? ["low", "medium", "high"] : ["low", "high"]), defaultEffort: defaultEffort, fastTier: "fast"));
            var panel = new ModelEffortPanel(thread.Id, false); var window = Show(panel, model);
            Click(window, Assert.Single(panel.GetVisualDescendants().OfType<Button>(), b => Equals(ToolTip.GetTip(b), "Reset to default")));
            Assert.Null(model.Thread(thread.Id)!.Effort); Assert.False(model.Thread(thread.Id)!.FastMode);
            time.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs();
            var card = Assert.Single(panel.GetVisualDescendants().OfType<EffortSliderCard>());
            var title = card.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal(expectedTitle, title.Text);
            window.Close();
        });
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-147")] // App/Views/Composer/EffortSlider.swift:384-419; ED-059 and ED-182(Q2) font mapping
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Pair_row_renders_source_geometry_order_fonts_colours_and_pointer_states(bool enabled, bool selected)
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
            using var clock = SwiftClock.Provider.Override(time); using var source = FrameClock.Source.Override(frames);
            var calls = 0;
            var row = new HydraPairListRow(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex }, "Opus + Heads", "Claude lead, Codex heads", 46, selected, enabled, () => calls++);
            var window = Show(new StackPanel { Children = { row } }); // production ModelList puts rows in a vertical stack, not a stretching Border
            var button = Assert.IsType<Button>(row.Child); var plate = Assert.IsType<Border>(button.Content);
            var grid = Assert.IsType<Grid>(plate.Child);
            var icon = Assert.Single(grid.Children.OfType<HydraFusedPairIcon>());
            Assert.Same(icon, grid.Children[0]); Assert.Equal(18, icon.Width);
            var artwork = Assert.IsType<Grid>(icon.Child); Assert.Equal(16, artwork.Width); Assert.Equal(16, artwork.Height);
            var leadMask = Assert.IsType<VisualBrush>(Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(Assert.IsType<Border>(artwork.Children[0]).Child).OpacityMask);
            var headsMask = Assert.IsType<VisualBrush>(Assert.IsType<Avalonia.Controls.Shapes.Rectangle>(Assert.IsType<Border>(artwork.Children[1]).Child).OpacityMask);
            Assert.Equal(ProviderKind.Claude.IconName, Assert.IsType<SwiftImage>(leadMask.Visual).AssetName);
            Assert.Equal(ProviderKind.Codex.IconName, Assert.IsType<SwiftImage>(headsMask.Visual).AssetName);
            Assert.True((bool)typeof(HydraFusedPairIcon).GetField("isFused", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(icon)!); // animates:false, already fused
            var texts = Assert.IsType<StackPanel>(grid.Children[1]); Assert.Equal(1, texts.Spacing);
            var title = Assert.IsType<TextBlock>(texts.Children[0]); var detail = Assert.IsType<TextBlock>(texts.Children[1]);
            Assert.Equal("Opus + Heads", title.Text); Assert.Equal("Claude lead, Codex heads", detail.Text);
            Assert.Equal(14, title.FontSize); Assert.Equal(12, detail.FontSize);
            Assert.Equal(FontWeight.Normal, title.FontWeight); Assert.Equal(FontWeight.Normal, detail.FontWeight);
            Assert.Equal(1, title.MaxLines); Assert.Equal(1, detail.MaxLines);
            Assert.Equal(TextAlignment.Start, title.TextAlignment); Assert.Equal(TextAlignment.Start, detail.TextAlignment); // :389 .leading is semantic Start
            Assert.Equal(Chrome.PrimaryText.Resolve(title.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
            Assert.Equal(Chrome.SecondaryText.Resolve(detail.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(detail.Foreground).Color);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(detail.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(detail.Foreground).Color); // Chrome.swift:79, ED-062
            Assert.True(title.TranslatePoint(default, window)!.Value.Y < detail.TranslatePoint(default, window)!.Value.Y);
            Assert.Equal(title.TranslatePoint(default, window)!.Value.X, detail.TranslatePoint(default, window)!.Value.X);
            var check = Assert.IsType<SwiftImage>(grid.Children[2]); Assert.Equal("checkmark", check.SymbolName);
            var checkFont = SwiftImage.GetFont(check)!.Resolve(); Assert.Equal(13, checkFont.FontSize); Assert.Equal(FontWeight.SemiBold, checkFont.FontWeight);
            Assert.Equal(Chrome.PrimaryText.Resolve(check.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(check.Foreground).Color);
            Assert.Equal(selected ? 1 : 0, check.Opacity);
            Assert.Equal(7, grid.ColumnDefinitions.Count); // :386 HStack's spacing 10 also surrounds the :400 Spacer(minLength:12)
            Assert.Equal(18, grid.ColumnDefinitions[0].ActualWidth); Assert.Equal(10, grid.ColumnDefinitions[1].ActualWidth);
            Assert.Equal(10, grid.ColumnDefinitions[3].ActualWidth); Assert.Equal(12, grid.ColumnDefinitions[4].ActualWidth); Assert.Equal(10, grid.ColumnDefinitions[5].ActualWidth);
            Assert.Equal(2, Grid.GetColumn(texts)); Assert.Equal(6, Grid.GetColumn(check));
            Assert.True(icon.TranslatePoint(default, window)!.Value.X < title.TranslatePoint(default, window)!.Value.X);
            Assert.True(title.TranslatePoint(default, window)!.Value.X < check.TranslatePoint(default, window)!.Value.X);
            Assert.Equal(new Thickness(10, 0), plate.Padding); Assert.Equal(46, plate.Height); Assert.Equal(46, row.Bounds.Height);
            Assert.Equal(new CornerRadius(9), plate.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(plate));
            // ED-181 approves the continuous request/radius with circular arcs pending the curve oracle.
            // ED-182(Q2) approves Lucide symbol mapping and recorded semibold; native stroke weight is not claimed.
            var presenter = Assert.IsType<Avalonia.Controls.Presenters.ContentPresenter>(Assert.Single(button.GetVisualChildren()));
            Assert.Same(plate, presenter.Child); // :416 .plain renders only the label, no extra bezel
            Assert.Equal(enabled, button.IsEnabled); Assert.Equal(enabled ? 1 : 0.45, button.Opacity);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color.A);
            var point = Centre(button, window);
            var hit = window.InputHitTest(point) as Visual;
            if (enabled) Assert.True(ReferenceEquals(hit, button) || hit!.GetVisualAncestors().Contains(button));
            window.MouseMove(point); Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromMilliseconds(50)); frames.Advance(TimeSpan.FromMilliseconds(50)); Dispatcher.UIThread.RunJobs();
            var midpoint = Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color;
            var alpha = enabled ? (int)Math.Round(Chrome.PrimaryText.Opacity(0.1).Resolve(plate.ActualThemeVariant).A * Animation.EaseOut(0.1).Progress(0.05)) : 0; // Swift :419 Chrome.hover = easeOut(0.1), ED-092(2)
            Assert.InRange((int)midpoint.A, alpha - 1, alpha + 1);
            time.Advance(TimeSpan.FromMilliseconds(60)); frames.Advance(TimeSpan.FromMilliseconds(60)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(enabled ? Chrome.PrimaryText.Opacity(0.1).Resolve(plate.ActualThemeVariant) : SwiftColor.Clear.Resolve(plate.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color); // Swift :414 Color.clear = transparent black, not Avalonia's named Transparent
            Click(window, button); Assert.Equal(enabled ? 1 : 0, calls);
            var corner = plate.TranslatePoint(new Point(1, 1), window)!.Value; // :415 .contentShape(.rect), including unpainted rounded-plate corners
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); var cornerHit = window.InputHitTest(corner) as Visual;
            if (enabled) Assert.True(ReferenceEquals(cornerHit, button) || cornerHit!.GetVisualAncestors().Contains(button));
            window.MouseMove(corner); window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None); window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs(); Assert.Equal(enabled ? 2 : 0, calls);
            window.MouseMove(new Point(399, 349)); Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color.A);
            window.Close();
        });
    }

    [Theory]
    [Trait("Req", "PR-COMPOSER-147")] // App/Views/Composer/EffortSlider.swift:312-323,364-367; HydraButton.swift:697-710
    [InlineData(false)]
    [InlineData(true)]
    public void Model_list_builds_the_unnamed_title_and_locked_pair_detail(bool locked)
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var harness = new AppModelHarness(withRuntime: false);
            var project = new Project("Fixture", harness.NewFolder("project"));
            var thread = new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised) { HydraEnabled = true };
            var model = Model(harness, project, thread); model.Settings.HydraEnabled = true;
            model.Settings.AddHydraPair(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "opus", WorkerModel = "sonnet", OrchestratorEffort = "high", WorkerEffort = "medium" });
            var list = new ModelList(thread, locked, _ => { }, () => { }); var window = Show(list, model);
            var row = Assert.Single(list.GetVisualDescendants().OfType<HydraPairListRow>());
            var text = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Equal("Opus + Sonnet", text[0].Text);
            Assert.Equal(locked ? "New chats only" : "Claude · Lead high, heads medium effort", text[1].Text);
            Assert.Equal(!locked, Assert.Single(row.GetVisualDescendants().OfType<Button>()).IsEnabled);
            var plate = Assert.IsType<Border>(Assert.Single(row.GetVisualDescendants().OfType<Button>()).Content);
            Assert.Equal(46, plate.Height); Assert.Equal(46, row.Bounds.Height); // :271 actual ModelList row height
            window.Close();
        });
    }
}
