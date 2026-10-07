using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings.MultiAccount;

// MA-8 (step 2): "Usage details" and "Keys and order" lead to the right service: Settings > Providers with that service's section open and brought into view, and the request used up.
[Collection("Headless")]
public sealed class ProvidersRevealTests(HeadlessSession headless)
{
    private static void Sync() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private void WithPage(ProviderKind? requested, Action<ProvidersSettingsPage> check) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var refused = new InProcessHttpHandler();
        using var transport = URLSession.Transport.Override(refused);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
        var model = new AppModel();
        var host = new Border();
        Marrow.App.Port.AppEnvironment.SetAppModel(host, model);
        SettingsNavigation.Shared.RequestedProvider = requested;
        var page = new ProvidersSettingsPage();
        host.Child = page;
        var window = new Avalonia.Controls.Window { Content = host, Width = 780, Height = 580 };
        try
        {
            window.Show();
            for (var i = 0; i < 5; i++) { window.UpdateLayout(); Sync(); }
            check(page);
        }
        finally
        {
            SettingsNavigation.Shared.RequestedProvider = null;
            window.Close();
            Sync();
        }
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void A_requested_service_is_opened_and_brought_into_view_and_the_request_is_used_up() => WithPage(ProviderKind.Claude, page =>
    {
        Assert.Equal(ProviderKind.Claude, page.Revealed);
        Assert.True(page.IsOpen(ProviderKind.Claude));
        Assert.Null(SettingsNavigation.Shared.RequestedProvider);
    });

    [Fact]
    [Trait("Feature", "MA-8")]
    [Trait("Rule", "R-TEST-4")]
    public void Without_a_request_the_page_is_as_it_was() => WithPage(null, page => Assert.Null(page.Revealed));
}
