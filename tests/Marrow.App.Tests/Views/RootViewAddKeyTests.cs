using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window;
using Marrow.App.Views;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :412-422 private struct OpenProvidersSettingsButton, through the
// real WindowManager (App/Window/WindowManager.swift:32-38) with a stand-in Settings page. DEVIATION OD-0140: Settings opens as the
// panel inside the main window, not as a window of its own.
[Collection("Headless")]
public sealed class RootViewAddKeyTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-WIN-067")] // App/Views/RootView.swift:415-418 showSettings() first, then requestedPage = .providers
    [Trait("Decision", "OD-0140")]
    public void Clicking_add_key_opens_the_Settings_panel_in_the_main_window_and_then_requests_the_Providers_page()
    {
        WithManager((_, manager) =>
        {
            SettingsPage? pageWhenTheWindowWasMade = SettingsPage.Archive; // anything but the null the store starts at
            manager.SettingsContent = () =>
            {
                pageWhenTheWindowWasMade = SettingsNavigation.Shared.RequestedPage; // the Settings panel is built by the first call
                return new Border();
            };
            var model = manager.Model;
            RootViewRig.Seed(model, ProviderKind.Deepseek, new ProviderStatus());
            var host = RootViewRig.Show(model, new ProviderStatusRow(ProviderKind.Deepseek));
            try
            {
                Assert.Null(SettingsOrNull(manager));
                Assert.False(manager.IsSettingsOpen);
                Assert.Null(SettingsNavigation.Shared.RequestedPage);

                var button = Assert.Single(RootViewRig.All<Button>(host));
                Assert.Equal("Add key", RootViewRig.AllText(button));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var settings = Settings(manager); // the panel is open in the main window (OD-0140)
                Assert.Same(manager.MainWindow, settings);
                Assert.True(manager.IsSettingsOpen);
                Assert.True(settings.IsVisible); // and that window is shown
                Assert.Equal(SettingsPage.Providers, SettingsNavigation.Shared.RequestedPage); // and the page requested
                Assert.Null(pageWhenTheWindowWasMade); // Settings came first: nothing had been requested yet (:416 before :417)
            }
            finally { RootViewRig.Close(host); }
        });
    }
}
