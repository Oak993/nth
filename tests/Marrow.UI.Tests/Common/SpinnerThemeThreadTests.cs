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

    // A spinner still in a window whose UI dispatcher is no longer the running one (a headless session's earlier dispatch; in the
    // app, a window left behind on a dispatcher that has shut down) is never retinted from the thread that changes the theme: on a
    // thread with no running Avalonia application that thread's own Dispatcher.UIThread answers CheckAccess() with true, and the
    // retint read the system accent there and threw into whoever set the theme (AppSettings' init, on a test's main loop).
    [Fact]
    [Trait("Req", "PR-THEME-169")]
    public async Task A_theme_change_from_a_thread_that_is_not_the_spinners_dispatcher_never_retints_it_there()
    {
        var prior = ThemeManager.Current;
        Window window = null!;
        SpinnerLayerView spinner = null!;
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            spinner = new SpinnerLayerView(WorkingSpinner.Cells, 3, 3.5);
            window = new Window { Width = 64, Height = 64, Content = spinner };
            window.Show();
        });
        try
        {
            var change = Task.Run(() =>
            {
                ThemeManager.Current = AppTheme.CatppuccinMocha;
                ThemeManager.Current = AppTheme.System;
            });
            await change.WaitAsync(TimeSpan.FromSeconds(5)); // the posting thread is not the spinner's: nothing of the spinner ran on it
        }
        finally
        {
            ThemeManager.Current = prior;
            headless.OnUiThread(() => { try { window.Close(); } catch (InvalidOperationException) { } });
        }
    }

    private static Color[] Tints(SpinnerLayerView spinner) => (Color[])typeof(SpinnerLayerView)
        .GetField("tints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(spinner)!;
}
