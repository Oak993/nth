using System.Runtime.InteropServices;
using Avalonia;
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

// OD-0015, OD-0002, OD-0033: every place upstream shows its own app logo (Image("droppy-logo")) draws the Marrow icon
// (src/Marrow.App/Assets/icon.svg artwork), at the source's size: the Welcome page (104 pt, RootView.swift:307-309),
// the new-thread prompt (72 pt, NewThreadPrompt.swift:17-21) and Settings > About "Made by" row (40 pt, SettingsView.swift:1090-1093).
// The Droppy Code logo must be in none of them: its tile is blue-violet; the Marrow tile is dark navy with a white core.
[Collection("Headless")]
public sealed class MarrowLogoSettingsRowTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Decision", "ED-215")] // OD-0015: the owner-ruled Marrow icon replaces the Droppy logo
    public void The_Settings_About_row_logo_is_the_Marrow_icon_at_40_points()
    {
        With(() => new MadeByDroppyRow(SwiftURL.Create("https://getdroppy.app")!), (view, _, _, _) =>
        {
            var logo = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(view), image => image.AssetName == "droppy-logo");
            Assert.Equal(40, logo.Width); Assert.Equal(40, logo.Height);
            MarrowLogoCheck.AssertIsTheMarrowIcon(logo);
        }, height: 220);
    }
}
