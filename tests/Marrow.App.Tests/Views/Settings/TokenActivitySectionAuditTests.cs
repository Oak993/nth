using System;
using System.Linq;
using System.Reflection;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Settings;

// Audit-fix tests for DroppyCode/App/Views/Settings/TokenActivitySection.swift (B3 52bc792).
[Collection("Headless")]
public sealed class TokenActivitySectionAuditTests(HeadlessSession headless)
{
    private static Button Chip(TokenActivitySection section, string name) => section.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 600, Height = 400, Content = content };
        window.RequestedThemeVariant = ThemeVariant.Light;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-132")] // TokenActivitySection.swift:59-72 a chip is a Button: its action runs when invoked
    public void Invoking_a_mode_chip_through_automation_switches_the_mode() => headless.OnUiThread(() =>
    {
        using var scope = TokenActivityTestScope.Create();
        using var clock = SwiftClock.Provider.Override(new ManualTimeProvider());
        using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
        var section = new TokenActivitySection(); var window = Show(section);
        try
        {
            var state = section.GetType().GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(section)!;
            var mode = state.GetType().GetProperty("Mode", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal("Daily", mode.GetValue(state)!.ToString());
            ControlAutomationPeer.CreatePeerForElement(Chip(section, "Weekly")).GetProvider<IInvokeProvider>()!.Invoke();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Weekly", mode.GetValue(state)!.ToString());
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-131")] // TokenActivitySection.swift:84 with Components.swift:72-73 the label colour rides the header's 0.25s animation
    public void The_active_chip_label_colour_follows_the_quarter_second_curve() => headless.OnUiThread(() =>
    {
        using var scope = TokenActivityTestScope.Create();
        var time = new ManualTimeProvider();
        using var clock = SwiftClock.Provider.Override(time);
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var section = new TokenActivitySection(); var window = Show(section);
        try
        {
            var weekly = Chip(section, "Weekly");
            var label = Assert.IsType<TextBlock>(Assert.IsType<Border>(weekly.Content).Child);
            weekly.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromTicks(1_250_000)); frames.Advance(TimeSpan.FromTicks(1_250_000));
            var expected = TokenActivityHeatmapCanvas.Mix(Chrome.SecondaryText.Resolve(ThemeVariant.Light),
                Chrome.Accent.Resolve(ThemeVariant.Light), Marrow.UI.Port.Animation.Smooth(.25).Progress(.125));
            Assert.Equal(expected, Assert.IsType<SolidColorBrush>(label.Foreground).Color);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

}
