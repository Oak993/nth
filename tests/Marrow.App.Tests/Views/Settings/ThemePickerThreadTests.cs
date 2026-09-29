using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class ThemePickerThreadTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-THEME-003")]
    public void A_theme_change_from_another_actor_updates_the_mounted_picker_on_its_UI_thread()
    {
        headless.OnUiThread(() =>
        {
            var prior = ThemeManager.Current;
            var picker = new ThemePickerButton(new Binding<AppTheme>(() => ThemeManager.Current, value => ThemeManager.Current = value));
            var window = new HostWindow { Width = 240, Height = 80, Content = picker };
            try
            {
                window.Show();
                var change = Task.Run(() => ThemeManager.Current = AppTheme.Nord);
                Assert.True(change.Wait(TimeSpan.FromSeconds(5)));
                change.GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(picker), label => label.Text == "Nord");
            }
            finally
            {
                window.Close();
                ThemeManager.Current = prior;
            }
        });
    }
}
