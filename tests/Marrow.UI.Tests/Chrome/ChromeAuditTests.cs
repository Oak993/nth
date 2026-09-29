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

    [Fact]
    [Trait("Req", "PR-WIN-203")] // Chrome.swift:1039-1042: the heading Text has no lineLimit, so a long title wraps and grows the header
    public void ChromeSection_heading_wraps_when_the_pane_is_narrow() => headless.OnUiThread(() =>
    {
        var one = new ChromeSection("Servers", () => new Border { Height = 10 });
        var oneWindow = Show(one, 120);
        var lineHeight = TextOf(one, "Servers").Bounds.Height;
        oneWindow.Close();

        const string title = "Servers that this project connects to every session";
        var section = new ChromeSection(title, () => new Border { Height = 10 });
        var window = Show(section, 120);
        Assert.True(TextOf(section, title).Bounds.Height > lineHeight * 1.5, "the heading stayed on one line");
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-204")] // Chrome.swift:1061-1064: the row title Text has normal wrapping, so a long title takes more lines
    public void ChromeRow_title_wraps_when_the_control_leaves_little_room() => headless.OnUiThread(() =>
    {
        var one = new ChromeRow("Mode", () => new Border { Width = 60, Height = 20 });
        var oneWindow = Show(one, 200);
        var lineHeight = TextOf(one, "Mode").Bounds.Height;
        oneWindow.Close();

        const string title = "Allow every command without asking first";
        var row = new ChromeRow(title, () => new Border { Width = 60, Height = 20 });
        var window = Show(row, 200);
        Assert.True(TextOf(row, title).Bounds.Height > lineHeight * 1.5, "the title stayed on one line");
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:730: the row is a Button whose accessibility activation runs the row action
    public void SidebarRow_automation_invoke_runs_the_row_action() => headless.OnUiThread(() =>
    {
        var count = 0;
        var row = new SidebarRow("Thread", () => count++, () => new Border { Width = 14, Height = 14 });
        var window = Show(row, 200);
        var button = Assert.Single(VisualQuery.Descendants<Button>(row));
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(button));
        invoke.Invoke();
        Assert.Equal(1, count);
        window.Close();
    });
}
