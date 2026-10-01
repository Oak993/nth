using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;
using UiWorkspace = Marrow.UI.Common.Workspace;
using UrlWorkspace = Marrow.Services.Port.Workspace;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :365-410 private struct ProviderStatusRow, with the
// provider facts it reads from DroppyCode/Core/Models/Provider.swift:62-101 and ProviderStatus
// (Services/Providers/ProviderRegistry.swift:3-27). Records PR-WIN-083, PR-WIN-085, PR-WIN-086, UI-WIN-064 to UI-WIN-068.
[Collection("Headless")]
public sealed class RootViewProviderRowTests(HeadlessSession headless)
{
    private static ProviderStatus Status(ProviderKind kind, string state) => state switch
    {
        "checking" => new ProviderStatus { IsChecking = true },
        "checkingSignedOut" => RootViewRig.Installed(kind, new ProviderStatus.Auth.SignedOut()) with { IsChecking = true },
        "missing" => new ProviderStatus(),
        "missingSignedOut" => new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() },
        "signedOut" => RootViewRig.Installed(kind, new ProviderStatus.Auth.SignedOut()),
        "signedIn" => RootViewRig.Installed(kind, new ProviderStatus.Auth.SignedIn("fixture@example.invalid")),
        "signedInNoAccount" => RootViewRig.Installed(kind, new ProviderStatus.Auth.SignedIn(null)),
        "unknown" => RootViewRig.Installed(kind, new ProviderStatus.Auth.Unknown()),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    // `.buttonStyle(.glass)`: the row's trailing control sits on one capsule of glass.
    private static void AssertGlassCapsule(Control row)
    {
        var glass = Assert.Single(RootViewRig.All<Control>(row), control => SwiftGlass.GetEffect(control) is not null);
        Assert.Equal(GlassShapeKind.Capsule, SwiftGlass.GetEffect(glass)!.Shape.Kind);
    }

    private void OnRow(ProviderKind kind, ProviderStatus status, Action<ProviderStatusRow, Control> check)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                RootViewRig.Seed(model, kind, status); // before the row exists: a change afterwards is posted to a later turn
                var row = new ProviderStatusRow(kind);
                var host = RootViewRig.ShowRow(model, row);
                try { check(row, host); }
                finally { RootViewRig.Close(host); }
            });
        });
    }

    [Theory]
    [Trait("Req", "UI-WIN-064")] // App/Views/RootView.swift:383-405 the four mutually exclusive states, tested in that order
    [InlineData(ProviderKind.Claude, "checking", "spinner")] // checking beats "not installed"
    [InlineData(ProviderKind.Claude, "checkingSignedOut", "spinner")] // and "signed out"
    [InlineData(ProviderKind.Claude, "missing", "Install")]
    [InlineData(ProviderKind.Claude, "missingSignedOut", "Install")] // not installed is tested before signed out
    [InlineData(ProviderKind.Claude, "signedOut", "Login")]
    [InlineData(ProviderKind.Claude, "signedIn", "check")]
    [InlineData(ProviderKind.Claude, "unknown", "check")] // installed and not signed out
    [InlineData(ProviderKind.Deepseek, "checking", "spinner")]
    [InlineData(ProviderKind.Deepseek, "missing", "Add key")] // an API-key provider has nothing to install
    [InlineData(ProviderKind.Deepseek, "missingSignedOut", "Add key")]
    [InlineData(ProviderKind.Deepseek, "signedOut", "Add key")]
    [InlineData(ProviderKind.Deepseek, "signedIn", "check")]
    [InlineData(ProviderKind.Zai, "signedOut", "Add key")]
    [InlineData(ProviderKind.Meta, "unknown", "check")]
    public void The_trailing_control_follows_the_four_states_in_order(ProviderKind kind, string state, string expected)
    {
        OnRow(kind, Status(kind, state), (row, _) => Assert.Equal(expected, RootViewRig.Trailing(row)));
    }

    [Fact]
    [Trait("Req", "PR-WIN-083")] // App/Views/RootView.swift:369-385, :407-408
    [Trait("Req", "UI-WIN-064")] // App/Views/RootView.swift:365-410 the row's geometry
    public void A_checking_row_is_padded_16_and_11_with_an_18_point_icon_in_a_22_point_box_name_status_and_a_spinner()
    {
        OnRow(ProviderKind.Codex, Status(ProviderKind.Codex, "checking"), (row, host) =>
        {
            var icon = Assert.Single(RootViewRig.All<SwiftImage>(row));
            Assert.Equal("provider-codex", icon.AssetName); // Provider.swift:55 iconName
            var name = RootViewRig.Text(row, "Codex"); // :376 provider.displayName
            var status = RootViewRig.Text(row, "Checking\u2026"); // :378, one horizontal ellipsis character
            var spinner = Assert.Single(RootViewRig.All<ProgressBar>(row)); // :383-385
            Assert.Empty(RootViewRig.All<Button>(row)); // no button while checking

            var rowBox = RootViewRig.In(row, host);
            var iconBounds = RootViewRig.In(icon, host);
            Assert.Equal(new Size(18, 18), iconBounds.Size); // :372 ProviderIcon(size: 18)
            // :374 .frame(width: 22): the 18-point icon is centred in a box that starts 16 points in (:407 padding).
            Assert.Equal(rowBox.Left + 16 + 11, iconBounds.Center.X, 0.5);
            Assert.Equal(rowBox.Left + 16 + 22 + 12, RootViewRig.In(name, host).Left, 0.5); // :371 HStack(spacing: 12)
            Assert.Equal(2, status.Bounds.Top - name.Bounds.Bottom, 0.5); // :375 VStack(spacing: 2)
            Assert.Equal(name.Bounds.Left, status.Bounds.Left, 0.5); // alignment: .leading
            Assert.Equal(rowBox.Right - 16, RootViewRig.In(spinner, host).Right, 0.5); // :382 Spacer(), :407 trailing 16
            var text = Assert.IsType<StackPanel>(name.Parent);
            Assert.Equal(rowBox.Top + 11, RootViewRig.In(text, host).Top, 0.5); // :408 .padding(.vertical, 11)
            Assert.Equal(text.Bounds.Height + 22, rowBox.Height, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-083")] // App/Views/RootView.swift:373,376-380; Chrome.swift:76-79
    [Trait("Req", "UI-WIN-064")] // App/Views/RootView.swift:373-380 the row's typography and colours
    public void The_icon_is_in_the_primary_label_colour_the_name_at_body_the_status_at_subheadline_in_secondary_text()
    {
        OnRow(ProviderKind.Codex, Status(ProviderKind.Codex, "checking"), (row, _) =>
        {
            RootViewRig.AssertForeground(Assert.Single(RootViewRig.All<SwiftImage>(row)), RootViewRig.Primary); // :373 .foregroundStyle(Chrome.primaryText)
            var name = RootViewRig.Text(row, "Codex");
            RootViewRig.AssertStyle(name, 13, FontWeight.Normal); // :377 .font(.system(.body))
            Assert.False(name.IsSet(TextBlock.ForegroundProperty)); // no style: the default text colour
            var status = RootViewRig.Text(row, "Checking\u2026");
            RootViewRig.AssertStyle(status, 11, FontWeight.Normal); // :379 .font(.system(.subheadline))
            RootViewRig.AssertForeground(status, RootViewRig.Secondary); // :380 Chrome.secondaryText
        });
    }

    [Theory]
    [Trait("Req", "PR-WIN-083")] // App/Views/RootView.swift:378 `status.isChecking ? "Checking…" : status.summary`; ProviderRegistry.swift:19-26
    [InlineData(ProviderKind.Claude, "missing", "Not installed")]
    [InlineData(ProviderKind.Claude, "signedOut", "Not signed in")]
    [InlineData(ProviderKind.Claude, "signedIn", "Signed in as fixture@example.invalid")]
    [InlineData(ProviderKind.Claude, "signedInNoAccount", "Signed in")]
    [InlineData(ProviderKind.Claude, "unknown", "Installed")]
    [InlineData(ProviderKind.Deepseek, "missing", "Not installed")]
    public void Outside_the_check_the_status_line_is_the_summary(ProviderKind kind, string state, string summary)
    {
        OnRow(kind, Status(kind, state), (row, _) =>
        {
            Assert.Contains(summary, RootViewRig.All<TextBlock>(row).Select(block => block.Text));
            Assert.DoesNotContain("Checking\u2026", RootViewRig.AllText(row), StringComparison.Ordinal);
            Assert.NotEqual("spinner", RootViewRig.Trailing(row));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-083")] // App/Views/RootView.swift:378 `.unknown: version.map { "Installed · \($0)" }` (ProviderRegistry.swift:24)
    public void An_installed_provider_with_a_known_version_says_so_after_a_middle_dot()
    {
        var status = Status(ProviderKind.Claude, "unknown") with { Version = "2.1.7" };
        OnRow(ProviderKind.Claude, status, (row, _) => Assert.Contains("Installed \u00B7 2.1.7", RootViewRig.All<TextBlock>(row).Select(block => block.Text)));
    }

    [Theory]
    [Trait("Req", "UI-WIN-065")] // App/Views/RootView.swift:386-393; Provider.swift:81-96 installURL
    [InlineData(ProviderKind.Claude, "https://claude.com/product/claude-code")]
    [InlineData(ProviderKind.Codex, "https://developers.openai.com/codex/cli")]
    [InlineData(ProviderKind.Pi, "https://pi.dev/docs/latest")]
    public void Install_is_a_glass_button_at_the_trailing_edge_that_opens_the_providers_install_address(ProviderKind kind, string address)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var opened = new List<string>();
            using var browser = UrlWorkspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
            harness.Main.OnMain(() =>
            {
                RootViewRig.Seed(model, kind, Status(kind, "missing"));
                var row = new ProviderStatusRow(kind);
                var host = RootViewRig.ShowRow(model, row);
                try
                {
                    var button = Assert.Single(RootViewRig.All<Button>(row));
                    Assert.Equal("Install", RootViewRig.AllText(button)); // :392
                    Assert.Equal(RootViewRig.In(row, host).Right - 16, RootViewRig.In(button, host).Right, 0.5);
                    AssertGlassCapsule(row); // :393 .buttonStyle(.glass)
                    Assert.Empty(opened);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal([address], opened); // the default browser, the provider's own address
                }
                finally { RootViewRig.Close(host); }
            });
        });
    }

    [Theory]
    [Trait("Req", "PR-WIN-085")] // App/Views/RootView.swift:395-401; Provider.swift:62-79 loginCommand
    [Trait("Req", "UI-WIN-066")] // App/Views/RootView.swift:395-401
    [InlineData(ProviderKind.Claude, "claude auth login")]
    [InlineData(ProviderKind.Codex, "codex login")]
    [InlineData(ProviderKind.Cursor, "cursor-agent login")]
    public void Login_is_a_glass_button_that_opens_a_terminal_running_the_providers_login_command(ProviderKind kind, string command)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var launches = new List<(string Executable, string[] Arguments, bool Console)>();
            var backend = new UiWorkspace.Backend
            {
                Which = name => name is "wt.exe" or "pwsh.exe" ? @"C:\fixture\" + name : null,
                Launch = (executable, arguments, _, console) => launches.Add((executable, [.. arguments], console)),
                Home = @"C:\fixture-home",
            };
            using var platform = UiWorkspace.Platform.Override(backend);
            harness.Main.OnMain(() =>
            {
                RootViewRig.Seed(model, kind, Status(kind, "signedOut"));
                var row = new ProviderStatusRow(kind);
                var host = RootViewRig.ShowRow(model, row);
                try
                {
                    var button = Assert.Single(RootViewRig.All<Button>(row));
                    Assert.Equal("Login", RootViewRig.AllText(button)); // one word, never "Log in"
                    AssertGlassCapsule(row);
                    Assert.Equal(RootViewRig.In(row, host).Right - 16, RootViewRig.In(button, host).Right, 0.5);
                    Assert.Empty(launches);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var launch = Assert.Single(launches);
                    Assert.EndsWith("wt.exe", launch.Executable, StringComparison.Ordinal); // Windows Terminal when present
                    Assert.Equal(command, launch.Arguments[^1]); // running that provider's login command
                }
                finally { RootViewRig.Close(host); }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-085")] // App/Views/RootView.swift:399 Workspace.openTerminal(running:); the console host when Windows Terminal is absent
    public void Login_falls_back_to_the_console_host_when_Windows_Terminal_is_absent()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var launches = new List<(string Executable, string[] Arguments, bool Console)>();
            var backend = new UiWorkspace.Backend
            {
                Which = name => name == "pwsh.exe" ? @"C:\fixture\pwsh.exe" : null,
                Launch = (executable, arguments, _, console) => launches.Add((executable, [.. arguments], console)),
                Home = @"C:\fixture-home",
            };
            using var platform = UiWorkspace.Platform.Override(backend);
            harness.Main.OnMain(() =>
            {
                RootViewRig.Seed(model, ProviderKind.Claude, Status(ProviderKind.Claude, "signedOut"));
                var row = new ProviderStatusRow(ProviderKind.Claude);
                var host = RootViewRig.ShowRow(model, row);
                try
                {
                    Assert.Single(RootViewRig.All<Button>(row)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var launch = Assert.Single(launches);
                    Assert.Equal(@"C:\fixture\pwsh.exe", launch.Executable);
                    Assert.True(launch.Console);
                    Assert.Equal("claude auth login", launch.Arguments[^1]);
                }
                finally { RootViewRig.Close(host); }
            });
        });
    }

    [Theory]
    [Trait("Req", "PR-WIN-086")] // App/Views/RootView.swift:402-405
    [Trait("Req", "UI-WIN-068")] // App/Views/RootView.swift:402-405
    [InlineData(ProviderKind.Claude, "signedIn")]
    [InlineData(ProviderKind.Claude, "unknown")]
    [InlineData(ProviderKind.Deepseek, "signedIn")]
    public void An_installed_provider_that_is_not_signed_out_shows_a_filled_check_in_the_success_colour_and_no_control(ProviderKind kind, string state)
    {
        OnRow(kind, Status(kind, state), (row, host) =>
        {
            Assert.Empty(RootViewRig.All<Button>(row)); // no button or link at all
            Assert.Empty(RootViewRig.All<ProgressBar>(row));
            var check = Assert.Single(RootViewRig.All<SwiftImage>(row), image => image.SymbolName == "checkmark.circle.fill"); // the filled variant
            RootViewRig.AssertForeground(check, ThemeManager.Spec.Success); // :404 Chrome.success
            Assert.Equal(RootViewRig.In(row, host).Right - 16, RootViewRig.In(check, host).Right, 0.5); // trailing edge
        });
    }

    [Theory]
    [Trait("Req", "UI-WIN-067")] // App/Views/RootView.swift:412-422
    [InlineData(ProviderKind.Deepseek, "missing")]
    [InlineData(ProviderKind.Meta, "signedOut")]
    [InlineData(ProviderKind.Zai, "missingSignedOut")]
    public void Add_key_is_a_glass_button_with_the_settings_tooltip_only_for_an_API_key_provider_that_is_missing_or_signed_out(ProviderKind kind, string state)
    {
        OnRow(kind, Status(kind, state), (row, host) =>
        {
            var button = Assert.Single(RootViewRig.All<Button>(row));
            Assert.Equal("Add key", RootViewRig.AllText(button)); // :415
            Assert.Equal("Open Settings > Providers", ToolTip.GetTip(button)); // :420, a plain greater-than sign
            AssertGlassCapsule(row);
            Assert.Equal(RootViewRig.In(row, host).Right - 16, RootViewRig.In(button, host).Right, 0.5);
            Assert.DoesNotContain("Install", RootViewRig.AllText(row), StringComparison.Ordinal);
            Assert.DoesNotContain("Login", RootViewRig.AllText(row), StringComparison.Ordinal);
        });
    }
}
