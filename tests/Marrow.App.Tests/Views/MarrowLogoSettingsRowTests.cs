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

// OD-0015, OD-0002, OD-0033, OD-0135: the Settings > About credit row once drew Image("droppy-logo") (SettingsView.swift:1067, 40 pt); it draws no logo now.
// The Droppy Code logo must be in none of them: its tile is blue-violet; the Marrow tile is dark navy with a white core.
[Collection("Headless")]
public sealed class MarrowLogoSettingsRowTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Decision", "ED-215")] // OD-0015: the owner-ruled Marrow icon replaces the Droppy logo
    public void The_Settings_About_credit_row_draws_no_logo()
    {
        With(() => new MadeByDroppyRow(SwiftURL.Create("https://getdroppy.app")!), (view, _, _, _) =>
        {
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(view), image => image.AssetName is not null);
        }, height: 220);
    }
}
