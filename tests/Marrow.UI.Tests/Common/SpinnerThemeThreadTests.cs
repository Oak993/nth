using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Common;

[Collection("Headless")]
public sealed class SpinnerThemeThreadTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-THEME-169")]
    public void A_theme_change_from_another_actor_retints_a_mounted_spinner_on_its_UI_thread()
    {
        headless.OnUiThread(() =>
        {
            var prior = ThemeManager.Current;
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var spinner = new SpinnerLayerView(WorkingSpinner.Cells, 3, 3.5);
            var window = new Window { Width = 64, Height = 64, Content = spinner };
            try
            {
                ThemeManager.Current = AppTheme.System;
                window.Show();
                var before = Tints(spinner);
                var change = Task.Run(() => ThemeManager.Current = AppTheme.CatppuccinMocha);
                Assert.True(change.Wait(TimeSpan.FromSeconds(5)));
                change.GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(before, Tints(spinner));
                Assert.Equal(GradientSpin.RowTints(Marrow.UI.Chrome.Chrome.AccentNSColor.Resolve(spinner.ActualThemeVariant)), Tints(spinner));
            }
            finally
            {
                window.Close();
                ThemeManager.Current = prior;
            }
        });
    }

    private static Color[] Tints(SpinnerLayerView spinner) => (Color[])typeof(SpinnerLayerView)
        .GetField("tints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(spinner)!;
}
