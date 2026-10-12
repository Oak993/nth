using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Web;
using Marrow.UI.Panes;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: DESIGN 5.3 find bar and 13.3 "Visus chrome: ... find bar"; LEAN-TESTS 10 (new controls: Escape returns focus to the opener, error
// state, long text and right-to-left); ED-NEW-web-w8c-9. The pane over the fake engine: Ctrl+F, typing, Enter, Shift+Enter and Esc end to end.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8c-9")]
public sealed class VisusFindBarKeysTests(HeadlessSession headless)
{
    private static Func<PaneKind, PaneContent?> Contents(VisusRig visus) => kind => kind == PaneKind.Browser
        ? VisusPane.Create(visus.Session, null)
        : new PaneContent(new Border { Child = new TextBox { Text = PaneKinds.Title(kind) } }, PaneKinds.Title(kind));

    private static (PaneWorkspaceRig Rig, VisusPaneBody Body, VisusTab Tab, FakeWebView Page) Open(VisusRig visus)
    {
        var rig = new PaneWorkspaceRig(width: 1400, contents: Contents(visus), popupLayers: true);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        var body = Assert.IsType<VisusPaneBody>(rig.Card(PaneKind.Browser).Content.Body);
        visus.Session.Go("https://example.test/");
        var tab = visus.Session.Selected!;
        var page = VisusRig.Live(tab);
        page.RaiseNavigationCompleted(tab.Url);
        page.FindHandler = (_, _, _) => Task.FromResult(new WebFindResult(3, 0));
        rig.Layout();
        return (rig, body, tab, page);
    }

    private static Popup FindPopup(VisusPaneBody body) =>
        Assert.Single(body.Children.OfType<Popup>(), popup => popup.Child is PopoverCard card && ReferenceEquals(card.Child, body.FindBar));

    private static void Pump(PaneWorkspaceRig rig)
    {
        Dispatcher.UIThread.RunJobs(); rig.Layout(); Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, PhysicalKey = key == Key.Escape ? PhysicalKey.Escape : PhysicalKey.Enter, KeyModifiers = modifiers });

    private static string CountText(VisusFindBar bar) =>
        bar.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").FirstOrDefault(text => text.EndsWith(" of 3", StringComparison.Ordinal) || text is "No matches" or "Find unavailable") ?? "";

    [Fact]
    public void Ctrl_F_opens_the_bar_with_the_query_focused_and_typing_searches_the_page() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, _, page) = Open(visus);
        using var _ = rig;
        Assert.True(visus.Session.Perform(VisusCommand.Find));
        Pump(rig);
        Assert.True(FindPopup(body).IsOpen);
        Assert.True(body.FindBar.Box.IsFocused);
        body.FindBar.Box.Text = "price";
        Pump(rig);
        Assert.Contains("Find price", page.Log);
        Assert.Equal("1 of 3", CountText(body.FindBar));
    });

    [Fact]
    public void Enter_steps_to_the_next_match_and_Shift_Enter_to_the_previous_one() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, _, page) = Open(visus);
        using var _ = rig;
        visus.Session.Perform(VisusCommand.Find);
        Pump(rig);
        body.FindBar.Box.Text = "price";
        Pump(rig);
        Press(body.FindBar.Box, Key.Enter);
        Assert.Equal(1, page.Log.Count(line => line == "FindNext"));
        Press(body.FindBar.Box, Key.Enter, KeyModifiers.Shift);
        Assert.Equal(1, page.Log.Count(line => line == "FindPrevious"));
        Assert.Equal(1, page.Log.Count(line => line == "FindNext"));
    });

    [Fact]
    public void Esc_in_the_bar_closes_it_clears_the_page_marks_and_returns_focus_to_the_page() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, _, page) = Open(visus);
        using var _ = rig;
        visus.Session.Perform(VisusCommand.Find);
        Pump(rig);
        Assert.True(body.FindBar.Box.IsFocused);
        var stops = page.Log.Count(line => line == "StopFind");
        Press(body.FindBar.Box, Key.Escape);
        Pump(rig);
        rig.Settle(); // the card shrinks back into its arrow before the popup goes
        Assert.False(FindPopup(body).IsOpen);
        Assert.False(body.FindBar.Box.IsFocused);
        Assert.True(page.Log.Count(line => line == "StopFind") > stops);
    });

    [Fact]
    public void Esc_from_inside_the_page_closes_the_bar_before_anything_else() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, _, page) = Open(visus);
        using var _ = rig;
        visus.Session.Perform(VisusCommand.Find);
        Pump(rig);
        Assert.True(page.RaiseKey(new KeyChord(0x01, ChordModifiers.None)).Handled);
        Pump(rig);
        rig.Settle();
        Assert.False(FindPopup(body).IsOpen);
        Assert.False(page.RaiseKey(new KeyChord(0x01, ChordModifiers.None)).Handled);
    });

    [Fact]
    public void A_failing_search_says_Find_unavailable_in_the_bar_and_a_later_one_recovers() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, _, page) = Open(visus);
        using var _ = rig;
        page.FindHandler = (_, _, _) => Task.FromException<WebFindResult>(new InvalidOperationException("fixture"));
        visus.Session.Perform(VisusCommand.Find);
        Pump(rig);
        body.FindBar.Box.Text = "price";
        Pump(rig);
        Assert.Equal("Find unavailable", CountText(body.FindBar));
        page.FindHandler = (_, _, _) => Task.FromResult(new WebFindResult(3, 0));
        body.FindBar.Box.Text = "prices";
        Pump(rig);
        Assert.Equal("1 of 3", CountText(body.FindBar));
    });

    [Fact]
    public void Find_is_refused_on_a_tab_with_no_page() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var (rig, body, _, _) = Open(visus);
        using var _ = rig;
        visus.Session.NewTab();
        Pump(rig);
        Assert.False(visus.Session.Perform(VisusCommand.Find));
        Assert.False(FindPopup(body).IsOpen);
    });

    [Theory]
    [InlineData(FlowDirection.LeftToRight)]
    [InlineData(FlowDirection.RightToLeft)]
    public void A_very_long_query_never_pushes_the_buttons_out_of_the_bar(FlowDirection direction) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        visus.Page("https://example.test/");
        using var find = new VisusFindController(visus.Session);
        var bar = new VisusFindBar(find, find.Close) { FlowDirection = direction };
        var window = new Avalonia.Controls.Window { Width = 330, Height = 60, Content = bar };
        window.Show();
        try
        {
            bar.Box.Text = new string('w', 400);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(328, bar.Bounds.Width);
            foreach (var button in bar.GetVisualDescendants().OfType<Button>())
            {
                var at = button.TranslatePoint(default, bar) ?? default;
                Assert.InRange(at.X, -0.5, bar.Bounds.Width - button.Bounds.Width + 0.5);
                Assert.Equal(button.Width, button.Bounds.Width); // a fixed-size control is never squeezed
            }
        }
        finally { window.Close(); }
    });
}
