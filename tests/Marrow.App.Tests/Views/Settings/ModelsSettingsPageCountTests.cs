using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class ModelsSettingsPageCountTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, true)]
    [InlineData(15, true)]
    [Trait("Req", "PR-SETTINGS-183")] // ModelsSettingsPage.swift:66-71, including pins.isEmpty
    [Trait("Req", "UI-SETTINGS-041")] // ModelsSettingsPage.swift:66-71
    public void The_count_line_includes_zero_uses_source_geometry_and_tracks_trimmed_search_and_list_changes(int count, bool dark) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        for (var index = 0; index < count; index++)
            model.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, "fixture" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var page = new ModelsSettingsPage(" \r\n\t");
        var host = new Border { Child = page };
        AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 500, Height = 400, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Check(count);
            page.SetQuery("  fixture\n");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(CountLabels());
            page.SetQuery("\n ");
            Dispatcher.UIThread.RunJobs();
            Check(count);
            model.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, "extra"));
            Dispatcher.UIThread.RunJobs();
            Check(Math.Min(count + 1, 15));
            model.Settings.RemoveFromModelList(new ModelPin(ProviderKind.Claude, "extra"));
            Dispatcher.UIThread.RunJobs();
            Check(count);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }

        TextBlock[] CountLabels() => LogicalQuery.SelfAndDescendants<TextBlock>(page)
            .Where(text => text.Text?.Contains("Click a model", StringComparison.Ordinal) == true).ToArray();
        void Check(int expected)
        {
            var line = Assert.Single(CountLabels());
            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of 15. Click a model to set its reasoning effort and fast mode for every new chat.", line.Text);
            Assert.Equal(11, line.FontSize);
            Assert.Equal(FontWeight.Normal, line.FontWeight);
            Assert.Equal(new Thickness(4, 0, 4, 0), line.Margin);
            Assert.Equal(Chrome.SecondaryText.Resolve(line.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(line.Foreground).Color);
            var parent = Assert.IsType<StackPanel>(line.Parent);
            Assert.Same(line, parent.Children.Last()); // below the model card
            Assert.IsType<ChromeCard>(parent.Children.First());
        }
    });
}
