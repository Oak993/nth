using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :70 `NSApp.isActive`. The port's one reading of it,
// NSApplication.IsActive (Port/ApplicationLifecycle.cs), over real headless windows: AppDelegateTests overrides it per
// async flow and so never reaches the default. Headless (R-TEST-7), so these tests are a class of their own. Avalonia
// 12 does not let user code implement a desktop lifetime, and its one desktop lifetime is process-wide, so the tests
// hand the windows a desktop lifetime would list (IClassicDesktopStyleApplicationLifetime.Windows, "the list of all
// open windows in the application") to the reading directly.
[Collection("Headless")]
public sealed class AppDelegateSecondTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-WIN-075")] // App/AppDelegate.swift:70 NSApp.isActive (windows_path: "the app is foreground")
    public void The_app_is_active_while_any_one_of_its_windows_is_active_and_not_only_the_main_window()
    {
        headless.OnUiThread(() =>
        {
            var main = new AvaloniaWindow { ShowActivated = false };
            var other = new AvaloniaWindow { ShowActivated = false };
            main.Show();
            other.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.False(main.IsActive);
                Assert.False(other.IsActive);
                Assert.False(NSApplication.AnyWindowIsActive([main, other])); // no window of the app is active

                other.Activate(); // another window of the app (the tour window, say; OD-0151: Settings is no window of its own) comes to the front; the main window stays behind it
                Dispatcher.UIThread.RunJobs();

                Assert.False(main.IsActive);
                Assert.True(other.IsActive);
                Assert.True(NSApplication.AnyWindowIsActive([main, other])); // through a window not the main one
            }
            finally
            {
                other.Close();
                main.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-075")] // App/AppDelegate.swift:70 NSApp.isActive
    public void With_only_the_main_window_active_the_app_is_active()
    {
        headless.OnUiThread(() =>
        {
            var main = new AvaloniaWindow { ShowActivated = false };
            var other = new AvaloniaWindow { ShowActivated = false };
            main.Show();
            other.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                main.Activate();
                Dispatcher.UIThread.RunJobs();

                Assert.True(main.IsActive);
                Assert.False(other.IsActive);
                Assert.True(NSApplication.AnyWindowIsActive([main, other]));
            }
            finally
            {
                other.Close();
                main.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-075")] // App/AppDelegate.swift:70 NSApp.isActive
    public void With_no_desktop_lifetime_listing_windows_the_default_reading_is_not_active()
    {
        headless.OnUiThread(() =>
        {
            // The headless test application runs with no desktop lifetime, so no window is listed as the app's.
            Assert.False(Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime);
            Assert.False(NSApplication.IsActive.Current());
        });
    }
}
