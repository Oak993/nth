using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Audit-fix tests for DroppyCode/App/Views/Settings/HydraSettingsPage.swift (B3 52bc7926).
[Collection("Headless")]
public sealed class HydraSettingsPageAuditTests(HeadlessSession headless)
{
    private void WithPair(string type, Func<HydraPair, HydraPair> shape, double width, Action<Control, AppModel, HydraPair> check)
    {
        var pair = shape(new HydraPair(ProviderKind.Claude));
        WithView(m =>
        {
            m.Settings.AddHydraPair(pair);
            var unit = typeof(HydraSettingsPage).Assembly.GetType("Marrow.App.Views.Settings." + type)!;
            return (Control)Activator.CreateInstance(unit, type == "HydraPairRow" ? new object[] { pair } : new object[] { pair.Id })!;
        }, width, (view, m) => check(view, m, pair));
    }

    private void WithView(Func<AppModel, Control> create, double width, Action<Control, AppModel> check) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(); var model = h.MakeModel();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var time = SwiftClock.Provider.Override(new ManualTimeProvider());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var attempted = (HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(model.Providers)!;
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases) attempted.Add(provider);
        var view = create(model); var host = new Border { Child = view }; AppEnvironment.SetAppModel(host, model);
        var w = new Avalonia.Controls.Window { Width = width, Height = 1800, Content = host };
        try { w.Show(); Sync(); check(view, model); }
        finally { w.Close(); Sync(); }
    });

    private static IEnumerable<T> Of<T>(Control c) where T : class => LogicalQuery.SelfAndDescendants<T>(c);
    private static void Sync() => Dispatcher.UIThread.RunJobs();
    private static double Right(Control c, Visual to) => c.TranslatePoint(new Point(c.Bounds.Width, 0), to)!.Value.X;
    private static void Advance(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        ((ManualTimeProvider)SwiftClock.Provider.Current).Advance(span); ((ManualFrameSource)FrameClock.Source.Current).Advance(span); Sync();
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-146")] // HydraSettingsPage.swift:12-35 the Spacer pushes the switch to the card's trailing edge
    public void The_hydra_switch_sits_at_the_trailing_edge_of_its_card() => WithView(_ => new HydraSettingsPage(), 900, (view, _) =>
    {
        var toggle = Of<SettingsSwitch>(view).First();
        var card = Of<ChromeCard>(view).First(c => Of<SettingsSwitch>(c).Contains(toggle));
        Assert.InRange(Math.Abs(Right(card, view) - Chrome.RowControlTrailingPadding - Right(toggle, view)), 0, 1.5);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-163")] // HydraSettingsPage.swift:246-263 the row's button fills the width and the remove control trails it
    public void A_pair_rows_remove_control_sits_at_the_trailing_edge() => WithPair("HydraPairRow", p => p, 900, (view, _, _) =>
    {
        var remove = Of<Button>(view).Last();
        Assert.InRange(Math.Abs(Right(view, view) - Chrome.RowControlTrailingPadding - Right(remove, view)), 0, 1.5);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-160")] // HydraSettingsPage.swift:118-141 the pairs blurb takes the width left beside the roster and wraps
    public void The_pairs_blurb_wraps_inside_a_narrow_page() => WithView(_ => new HydraSettingsPage(), 420, (view, _) =>
    {
        var blurb = Of<TextBlock>(view).First(t => t.Text?.StartsWith("A pair says which model leads", StringComparison.Ordinal) == true);
        Assert.True(Right(blurb, view) <= view.Bounds.Width + 0.5);
        Assert.True(blurb.Bounds.Height > 2 * blurb.FontSize);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-163")] // HydraSettingsPage.swift:247-262 a long pair name and summary wrap and grow the row
    public void A_long_pair_title_wraps_inside_the_row() => WithPair("HydraPairRow", p => p with { Name = string.Join(' ', Enumerable.Repeat("verylongword", 20)) }, 500, (view, _, _) =>
    {
        var title = Of<TextBlock>(view).First(t => t.Text?.StartsWith("verylongword", StringComparison.Ordinal) == true);
        Assert.True(Right(title, view) <= view.Bounds.Width + 0.5);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-166")] // HydraSettingsPage.swift:345-379 the binding setter keeps the field's identity and focus while typing
    public void Typing_in_the_pair_name_field_keeps_the_same_focused_field() => WithPair("HydraPairEditor", p => p, 900, (view, m, pair) =>
    {
        var field = Of<TextBox>(view).Single();
        field.Focus(); Sync();
        Assert.True(field.IsFocused);
        field.Text = "a"; Sync();
        field.Text = "ab"; Sync();
        Assert.Equal("ab", m.Settings.HydraPair(pair.Id)!.Name);
        Assert.Same(field, Of<TextBox>(view).Single());
        Assert.True(field.IsFocused);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-166")] // HydraSettingsPage.swift:360-379 plain field, 10x7 padding, overlay(0.06) in a 9-point rounded shape, 2-point subtitle inset
    public void The_pair_name_field_has_the_sources_visual_treatment() => WithPair("HydraPairEditor", p => p, 900, (view, _, _) =>
    {
        var field = Of<TextBox>(view).Single();
        var frame = Of<Border>(view).First(b => Of<TextBox>(b).Contains(field) && b.Background is not null);
        Assert.Equal(new Thickness(10, 7), frame.Padding);
        Assert.Equal(new CornerRadius(9), frame.CornerRadius);
        Assert.Equal(Chrome.Overlay(0.06).Resolve(frame.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(frame.Background).Color);
        Assert.Equal(Chrome.PrimaryText.Resolve(field.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(field.Foreground).Color);
        var subtitle = Of<TextBlock>(view).First(t => t.Text == "Shown in the picker and the composer");
        Assert.Equal(new Thickness(2, 0), subtitle.Margin);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-149")] // HydraSettingsPage.swift:164-166 opacity 0.5 / 1 rides .smooth(duration: 0.2) on hydraEnabled
    public void The_dimmed_group_fades_along_the_smooth_point_two_curve() => WithView(_ => new HydraSettingsPage(), 900, (view, m) =>
    {
        m.Settings.HydraEnabled = true; Sync();
        var root = (StackPanel)typeof(HydraSettingsPage).GetField("root", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        Assert.Equal(1, GroupOf(view, root).Opacity);
        m.Settings.HydraEnabled = false; Sync();
        Advance(0.05);
        var mid = GroupOf(view, root).Opacity;
        Assert.InRange(mid, 0.5001, 0.9999);
        Advance(1);
        Assert.Equal(0.5, GroupOf(view, root).Opacity);
    });

    private static Control GroupOf(Control view, StackPanel root)
    {
        var heads = Of<ChromeRow>(view).First(r => Of<TextBlock>(r).Any(t => t.Text == "Checkout"));
        return (Control)heads.GetLogicalAncestors().First(a => ReferenceEquals(a.GetLogicalParent(), root));
    }
}
