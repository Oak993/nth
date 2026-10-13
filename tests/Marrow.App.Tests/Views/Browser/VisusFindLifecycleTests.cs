using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Panes;

namespace Marrow.App.Tests.Views.Browser;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8c-2")]
public sealed class VisusFindLifecycleTests(HeadlessSession headless)
{
    [Fact]
    public void Reopening_find_searches_the_retained_query_again() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var page = VisusRig.Live(tab);
        var searches = 0;
        page.FindHandler = (_, _, _) => { searches++; return Task.FromResult(new WebFindResult(3, 0)); };
        using var rig = new PaneWorkspaceRig(contents: kind => kind == PaneKind.Browser
            ? VisusPane.Create(visus.Session, null) : new PaneContent(new Border(), "Other"), popupLayers: true);
        rig.Workspace.Open(PaneKind.Browser); rig.Settle();
        var body = Assert.IsType<VisusPaneBody>(rig.Card(PaneKind.Browser).Content.Body);
        visus.Session.Perform(VisusCommand.Find); rig.Layout();
        body.FindBar.Box.Text = "price"; rig.Layout();
        Assert.Equal(1, searches);
        // Close with the actual bar button, then reopen through the command.
        body.FindBar.GetVisualDescendants().OfType<Button>().Last().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        visus.Session.Perform(VisusCommand.Find); rig.Layout();
        Assert.Equal("price", body.FindBar.Box.Text);
        Assert.Equal(2, searches);
        Assert.Contains(body.FindBar.GetVisualDescendants().OfType<TextBlock>(), c => c.Text == "1 of 3");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacing_the_document_at_the_same_url_aborts_a_pending_pdf(bool pageInitiated) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var page = VisusRig.Live(tab);
        var body = new VisusPaneBody(visus.Session)
        {
            Files = new FakeTabulaFiles { SaveAnswer = _ => { if (pageInitiated) page.RaiseNavigationStarting(tab.Url); else tab.Reload(); return @"C:\nth-test\chosen.pdf"; } },
        };
        var save = body.SavePdfAsync();
        HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2)); save.GetAwaiter().GetResult();
        Assert.DoesNotContain(page.Log, entry => entry.StartsWith("PrintToPdf ", StringComparison.Ordinal));
    });

    [Fact]
    public void Switching_away_and_back_aborts_the_pending_pdf() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var other = visus.Page("https://other.test/");
        var tab = visus.Page("https://example.test/");
        var body = new VisusPaneBody(visus.Session)
        {
            Files = new FakeTabulaFiles { SaveAnswer = _ => { visus.Session.Select(other); visus.Session.Select(tab); return @"C:\nth-test\chosen.pdf"; } },
        };
        var save = body.SavePdfAsync();
        HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2)); save.GetAwaiter().GetResult();
        Assert.DoesNotContain(VisusRig.Live(tab).Log, entry => entry.StartsWith("PrintToPdf ", StringComparison.Ordinal));
    });

    [Theory]
    [InlineData(0.1)]
    [InlineData(10)]
    public void Unsupported_legacy_zoom_restores_at_default_without_throwing(double factor) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = new VisusTab(visus.Session, VisusTabKind.Browse, url: "https://example.test/", zoom: factor);
        try
        {
            tab.Reopen();
            Assert.Equal(1, tab.Zoom);
            Assert.Equal(1, VisusRig.Live(tab).Zoom);
        }
        finally { tab.Release(); }
    });

    [Fact]
    public void Print_before_attachment_reports_unavailability_without_calling_the_engine() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var page = VisusRig.Live(tab); page.Detach();
        var notices = new List<string>();
        visus.Session.Notice += (_, text) => notices.Add(text);
        visus.Session.Perform(VisusCommand.Print);
        Assert.Contains("Could not print this page", notices);
        Assert.DoesNotContain("Print", page.Log);
    });

    [Fact]
    public void Detaching_a_pane_aborts_the_pending_pdf() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var body = new VisusPaneBody(visus.Session);
        var window = new Avalonia.Controls.Window { Content = body }; window.Show();
        try
        {
            body.Files = new FakeTabulaFiles { SaveAnswer = _ => { window.Content = null; return @"C:\nth-test\chosen.pdf"; } };
            var save = body.SavePdfAsync();
            HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2)); save.GetAwaiter().GetResult();
            Assert.DoesNotContain(VisusRig.Live(tab).Log, entry => entry.StartsWith("PrintToPdf ", StringComparison.Ordinal));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_body_removed_from_the_tree_releases_its_find_controller_subscription() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        visus.Page("https://example.test/");
        var body = new VisusPaneBody(visus.Session);
        var window = new Avalonia.Controls.Window { Content = body }; window.Show(); window.Content = null; window.Close();
        var handlers = (Delegate?)typeof(VisusSession).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(visus.Session);
        Assert.DoesNotContain(handlers?.GetInvocationList() ?? [], handler => handler.Target is VisusFindController);
    });
}
