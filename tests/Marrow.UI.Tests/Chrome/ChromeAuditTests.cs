using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Audit fixes for UI/Chrome/Chrome.swift (B3 52bc792): ChromeSection header :1037-1052, ChromeRow title :1060-1069,
// SidebarRow :710-739. Expected values are read off the Swift, not the C#.
[Collection("Headless")]
public sealed class ChromeAuditTests(HeadlessSession headless)
{
    private static Window Show(Control content, double width)
    {
        var host = new Border { Width = width, Child = content, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Window { Width = width + 40, Height = 400, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TextBlock TextOf(Control root, string text) =>
        Assert.Single(VisualQuery.Descendants<TextBlock>(root), t => t.Text == text);

    [Fact]
    [Trait("Req", "PR-WIN-203")] // Chrome.swift:1043-1047: Spacer(minLength: 8) then trailing, the trailing control sits at the header's far right
    public void ChromeSection_trailing_control_sits_at_the_far_trailing_edge() => headless.OnUiThread(() =>
    {
        var trailing = new Border { Width = 30, Height = 20 };
        var section = new ChromeSection("Servers", () => new Border { Height = 10 }, trailing: trailing);
        var window = Show(section, 400);
        var right = trailing.TranslatePoint(new Point(trailing.Bounds.Width, 0), section)!.Value.X;
        Assert.Equal(400, right, 0.5);
        window.Close();
    });
}
