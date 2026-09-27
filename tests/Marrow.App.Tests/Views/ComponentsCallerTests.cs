using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.App.Views.Usage;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using Chrome = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

[Collection("Headless")]
public sealed class ComponentsCallerTests(HeadlessSession headless)
{
    // With a harness, the content holds a UsagePanel: UsagePanel.swift:9 reads AppModel from @Environment and :71-78
    // refreshes plan limits and credits in .onAppear, on the main actor. Marrow.App binds the main actor to the
    // Avalonia UI thread (MainActor.cs:5); the harness runs it on its own loop instead, so the window is shown inside
    // harness.Main.OnMain, after every CLI provider is pointed at a missing no-cli.exe so no refresh can find a real
    // CLI: the UsagePanelTests.Show convention.
    private static AvaloniaWindow Show(Control content, AppModelHarness? harness = null)
    {
        var window = new AvaloniaWindow { Content = content, Width = 650, Height = 400 };
        if (harness is null) window.Show();
        else
        {
            var model = AppEnvironment.GetAppModel(content);
            harness.Main.OnMain(() =>
            {
                foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
                    if (!kind.IsAPIKeyBased) model.Settings.SetBinaryPath(System.IO.Path.Combine(harness.Scratch, "no-cli.exe"), kind);
            });
            harness.Main.OnMain(() => window.Show());
        }
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-USAGE-020")] // App/Views/Usage/UsagePanel.swift:91-97; UI/Common/Components.swift:4-12
    public void Actual_provider_headers_compose_the_role_and_display_name_with_exact_icon_font_spacing_and_padding()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var host = new Border();
            AppEnvironment.SetAppModel(host, harness.MakeModel());
            var panel = new UsagePanel(null, ProviderKind.Codex, headsProvider: ProviderKind.Deepseek);
            host.Child = panel;
            var window = Show(host, harness);
            try
            {
                var icons = LogicalQuery.SelfAndDescendants<ProviderIcon>(panel).ToArray();
                Assert.Equal(2, icons.Length);
                foreach (var (icon, expectedText, expectedAsset) in new[]
                {
                    (icons[0], "Lead · Codex", "provider-codex"),
                    (icons[1], "Heads · DeepSeek", "provider-deepseek"),
                })
                {
                    var header = Assert.IsType<StackPanel>(icon.Parent);
                    Assert.Equal(Orientation.Horizontal, header.Orientation);
                    Assert.Equal(6, header.Spacing);
                    Assert.Equal(2, header.Children.Count);
                    var label = Assert.IsType<TextBlock>(header.Children[1]);
                    Assert.Equal(expectedText, label.Text); // :93 literal spaces around U+00B7.
                    Assert.Equal(12, label.FontSize); // :94 .callout, ED-059.
                    Assert.Equal(FontWeight.SemiBold, label.FontWeight);
                    Assert.Equal(Chrome.PrimaryText.Resolve(label.ActualThemeVariant), Assert.IsType<SolidColorBrush>(label.Foreground).Color);
                    Assert.Equal(new Thickness(0, 0, 0, 8), Assert.IsType<Border>(header.Parent).Padding);
                    Assert.Equal(13, icon.Size);
                    var image = Assert.IsType<SwiftImage>(icon.Child);
                    Assert.Equal(expectedAsset, image.AssetName);
                    Assert.False(image.IsEmpty);
                    Assert.True(image.IsResizable);
                    Assert.Equal(ContentMode.Fit, image.AspectContentMode);
                    Assert.Equal(new Size(13, 13), image.Bounds.Size);
                }
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Theory]
    [Trait("Req", "PR-SIDEBAR-074")] // UI/Common/Components.swift:23-33; App/Views/Sidebar/SidebarView.swift:2686-2694
    [InlineData(-1, "now")]
    [InlineData(59, "now")]
    [InlineData(60, "1m")]
    [InlineData(3599, "59m")]
    [InlineData(3600, "1h")]
    [InlineData(86399, "23h")]
    [InlineData(86400, "1d")]
    [InlineData(604799, "6d")]
    [InlineData(604800, null)]
    public void Actual_sidebar_time_label_has_exact_boundary_text_and_all_source_label_modifiers(int seconds, string? expected)
    {
        headless.OnUiThread(() =>
        {
            using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
            var date = MinuteClock.Shared.Now.AddSeconds(-seconds);
            // :31 directly specifies the formatter expression for the final branch, independently of Short.
            expected ??= SwiftFormatters.Formatted(date, DateFormatStyle.DateTime.Month().Day());
            var label = new RelativeTimeLabel(date);
            var window = Show(label);
            try
            {
                var text = Assert.IsType<TextBlock>(label.Child);
                Assert.Equal(expected, text.Text);
                Assert.Equal(11, text.FontSize); // .subheadline, ED-059.
                Assert.Contains(text.FontFeatures!, feature => feature.Tag == "tnum");
                Assert.Equal(Chrome.SecondaryText.Opacity(0.8).Resolve(text.ActualThemeVariant), Assert.IsType<SolidColorBrush>(text.Foreground).Color);
                Assert.Equal(new Thickness(0, 0, 4, 0), text.Margin);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Fact]
    [Trait("Rule", "R-VIEW-2")] // App/Views/Settings/TokenActivitySection.swift:37, :62-84; partial caller proof, STOP below.
    public void Actual_mode_chips_select_every_mode_ignore_current_mode_clicks_and_reopen_on_Daily()
    {
        headless.OnUiThread(() =>
        {
            using var scope = TokenActivityTestScope.Create();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var section = new TokenActivitySection(TokenLedger.Shared.Current);
            var window = Show(section);
            try
            {
                var buttons = LogicalQuery.SelfAndDescendants<Button>(section).ToArray();
                Assert.Equal(new[] { "Daily", "Weekly", "Cumulative" }, buttons.Select(AutomationProperties.GetName));
                void CheckSelected(int index)
                {
                    for (var i = 0; i < buttons.Length; i++)
                    {
                        var peer = ControlAutomationPeer.CreatePeerForElement(buttons[i]);
                        Assert.Equal(AutomationControlType.Button, peer!.GetAutomationControlType());
                        Assert.Equal(i == index, Assert.IsAssignableFrom<ISelectionItemProvider>(peer).IsSelected);
                        var surface = Assert.IsType<Border>(buttons[i].Content);
                        var text = Assert.IsType<TextBlock>(surface.Child);
                        Assert.Equal(13, text.FontSize);
                        Assert.Equal(i == index ? FontWeight.Medium : FontWeight.Normal, text.FontWeight);
                        Assert.Equal(1, text.MaxLines);
                        Assert.Equal(new Thickness(9, 5), surface.Padding);
                    }
                }
                CheckSelected(0);
                var state = typeof(TokenActivitySection).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(section)!;
                object? State(string name) => state.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state);
                foreach (var index in new[] { 1, 2, 0 })
                {
                    buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    CheckSelected(index);
                    var count = State("SwitchCount");
                    var grid = State("Grid");
                    var previous = State("PreviousGrid");
                    var pending = frames.Pending;
                    buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(count, State("SwitchCount"));
                    Assert.Same(grid, State("Grid"));
                    Assert.Same(previous, State("PreviousGrid"));
                    Assert.Equal(pending, frames.Pending); // :63 prevents a fresh animation request.
                }
                buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                CheckSelected(2);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }

            var reopened = new TokenActivitySection(TokenLedger.Shared.Current);
            var reopenedWindow = Show(reopened);
            try
            {
                var buttons = LogicalQuery.SelfAndDescendants<Button>(reopened).ToArray();
                Assert.Equal(new[] { true, false, false }, buttons.Select(button =>
                    Assert.IsAssignableFrom<ISelectionItemProvider>(ControlAutomationPeer.CreatePeerForElement(button)).IsSelected));
            }
            finally { reopenedWindow.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    // STOP UI.Common.Components #2 (caller proof; also recorded in ComponentsSecondTests)
    // Kind: incomplete proof requiring another unit's production file.
    // Swift: App/Views/Settings/TokenActivitySection.swift:54-59, :79, :87.
    // Records: UI-SETTINGS-022, PR-SETTINGS-131.
    // Missing: exact first-baseline/heading behavior and the independent .smooth(0.25) header animation;
    // the owning section also uses its own copied ChipButton rather than the shared capsule hit geometry.
    // Needed: the settings unit must finish those source behaviors and their full caller proofs.
    // This test proves only the actual selection/fonts/padding/no-op/reopening behavior, so it carries no Req.
}
