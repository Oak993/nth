using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Views.Settings;
using Marrow.App.Views;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// OD-0015, OD-0002, OD-0033: the places upstream draws NSApp.applicationIconImage (RootView.swift:307, NewThreadPrompt.swift:17) draw the packaged app icon (Assets/icon.png) like the About identity pill; only the "Made by" row uses Image("droppy-logo") and draws the Marrow tile render
// (src/Marrow.App/Assets/icon.svg artwork), at the source's size: the Welcome page (104 pt, RootView.swift:307-309),
// the new-thread prompt (72 pt, NewThreadPrompt.swift:17-21) and Settings > About "Made by" row (40 pt, SettingsView.swift:1090-1093).
// The Droppy Code logo must be in none of them: its tile is blue-violet; the Marrow tile is dark navy with a white core.
[Collection("Headless")]
public sealed class MarrowLogoPlacesTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-215")] // OD-0015: the owner-ruled Marrow icon replaces the Droppy logo
    public void The_Welcome_page_icon_is_the_packaged_app_icon_at_104_points() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var host = RootViewRig.Show(model, new WelcomeView(), 900);
            try
            {
                Assert.DoesNotContain(RootViewRig.All<SwiftImage>(host), image => image.AssetName == "droppy-logo");
                MarrowLogoCheck.AssertIsTheAppIconImage(Assert.Single(RootViewRig.All<Image>(host), image => image.Width == 104), 104);
            }
            finally { RootViewRig.Close(host); }
        });
    });

    [Fact]
    [Trait("Decision", "ED-215")] // OD-0015: the owner-ruled Marrow icon replaces the Droppy logo
    [Trait("Decision", "OD-0136")] // the empty chat's icon is the 44 point nth tile B
    public void The_new_thread_prompt_icon_is_the_nth_tile_at_44_points() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = new NewThreadPrompt(System.Guid.NewGuid(), null);
            var window = new AvaloniaWindow { Content = host };
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            window.Show();
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(host), image => image.AssetName == "droppy-logo");
            Assert.Empty(LogicalQuery.SelfAndDescendants<Image>(host));
            var icon = Assert.Single(LogicalQuery.SelfAndDescendants<NthTile>(host));
            MarrowLogoCheck.AssertIsTheNthTile(icon);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(icon));
        });
    });
}
