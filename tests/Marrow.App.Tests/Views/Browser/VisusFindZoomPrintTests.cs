using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.App.Views.Artifacts;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w8c-1")]
public sealed class VisusFindZoomPrintTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(0x21, ChordModifiers.Control, "Find")]
    [InlineData(0x19, ChordModifiers.Control, "Print")]
    [InlineData(0x0D, ChordModifiers.Control, "ZoomIn")]
    [InlineData(0x0D, ChordModifiers.Control | ChordModifiers.Shift, "ZoomIn")]
    [InlineData(0x4E, ChordModifiers.Control, "ZoomIn")]
    [InlineData(0x0C, ChordModifiers.Control, "ZoomOut")]
    [InlineData(0x4A, ChordModifiers.Control, "ZoomOut")]
    [InlineData(0x0B, ChordModifiers.Control, "ResetZoom")]
    public void Browser_chords_reach_the_new_commands(int scan, ChordModifiers modifiers, string command)
    {
        Assert.Equal(command, VisusKeys.CommandFor(new KeyChord(scan, modifiers))?.ToString());
    }

    [Fact]
    public void The_menu_offers_find_zoom_print_and_save_as_pdf() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        visus.Page("https://example.test/page");
        var menu = new VisusToolbar(visus.Session).MenuEntries().OfType<VisusMenuEntry>().Select(row => row.Title).ToList();
        Assert.Contains("Find in page", menu);
        Assert.Contains("Print", menu);
        Assert.Contains("Save as PDF", menu);
        Assert.Contains(menu, title => title.StartsWith("Zoom ", StringComparison.Ordinal));
    });

    [Fact]
    public void Zoom_follows_the_site_and_profile_and_not_the_tab() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var first = visus.Page("https://example.test/one");
        first.SetZoom(1.25);
        Assert.Equal(1.25, visus.Page("https://example.test/two").Zoom);
        Assert.Equal(1, visus.Page("https://other.test/").Zoom);
        Assert.Equal(1, visus.Page("https://example.test/", Marrow.Core.Web.VisusTabKind.Private).Zoom);
        first.Open("https://other.test/");
        VisusRig.Live(first).RaiseNavigationCompleted(first.Url);
        Assert.Equal(1, first.Zoom);
        first.Open("https://example.test/three");
        VisusRig.Live(first).RaiseNavigationCompleted(first.Url);
        Assert.Equal(1.25, first.Zoom);
    });

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_zoom_is_refused_without_changing_the_engine(double value) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var page = VisusRig.Live(tab);
        Assert.Throws<ArgumentOutOfRangeException>(() => tab.SetZoom(value));
        Assert.Equal(1, page.Zoom);
        Assert.Equal(1, tab.Zoom);
    });

    [Fact]
    public void Print_command_calls_the_selected_pages_print_dialog() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var first = visus.Page("https://one.test/");
        var second = visus.Page("https://two.test/");
        Assert.True(Enum.TryParse<VisusCommand>("Print", out var print));
        Assert.True(visus.Session.Perform(print));
        Assert.Contains("Print", VisusRig.Live(second).Log);
        Assert.DoesNotContain("Print", VisusRig.Live(first).Log);
    });

    [Fact]
    public void Changing_zoom_updates_an_already_open_same_site_tab_and_its_engine() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var first = visus.Page("https://example.test/one");
        var second = visus.Page("https://example.test/two");
        first.SetZoom(1.5);
        Assert.Equal(1.5, second.Zoom);
        Assert.Equal(1.5, VisusRig.Live(second).Zoom);
        Assert.Equal(1.5, VisusRig.Live(first).Zoom);
    });

    [Fact]
    public void Zoom_steps_and_reset_reach_the_engine() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        Assert.True(Enum.TryParse<VisusCommand>("ZoomIn", out var increase));
        Assert.True(Enum.TryParse<VisusCommand>("ZoomOut", out var decrease));
        Assert.True(Enum.TryParse<VisusCommand>("ResetZoom", out var reset));
        visus.Session.Perform(increase);
        Assert.Equal(1.1, VisusRig.Live(tab).Zoom);
        visus.Session.Perform(decrease);
        Assert.Equal(1, VisusRig.Live(tab).Zoom);
        tab.SetZoom(1.5);
        visus.Session.Perform(reset);
        Assert.Equal(1, VisusRig.Live(tab).Zoom);
    });

    [Fact]
    public void Closing_the_last_private_tab_forgets_private_zoom() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var first = visus.Page("https://example.test/", Marrow.Core.Web.VisusTabKind.Private);
        first.SetZoom(1.5);
        var second = visus.Page("https://example.test/other", Marrow.Core.Web.VisusTabKind.Private);
        Assert.Equal(1.5, second.Zoom);
        visus.Session.Close(first);
        Assert.Equal(1.5, second.Zoom);
        visus.Session.Close(second);
        Assert.Equal(1, visus.Page("https://example.test/", Marrow.Core.Web.VisusTabKind.Private).Zoom);
    });

    [Theory]
    [InlineData("http://example.test/")]
    [InlineData("https://example.test:444/")]
    public void Scheme_and_effective_port_separate_zoom(string other) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        visus.Page("https://example.test/").SetZoom(1.5);
        Assert.Equal(1, visus.Page(other).Zoom);
        Assert.Equal(1.5, visus.Page("https://EXAMPLE.test:443/path").Zoom);
    });

    private static Task SavePdf(VisusPaneBody body, ITabulaFiles files)
    {
        var property = typeof(VisusPaneBody).GetProperty("Files");
        Assert.NotNull(property);
        property.SetValue(body, files);
        var method = typeof(VisusPaneBody).GetMethod("SavePdfAsync");
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Task>(method.Invoke(body, null));
    }

    [Fact]
    public void Save_as_pdf_uses_the_pdf_picker_and_does_nothing_on_cancel() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/", title: "Fixture");
        var body = new VisusPaneBody(visus.Session);
        var files = new FakeTabulaFiles { SaveAnswer = _ => null };
        var save = SavePdf(body, files);
        HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2));
        save.GetAwaiter().GetResult();
        var request = Assert.Single(files.Saves);
        Assert.Equal("Save as PDF", request.Title);
        Assert.Equal("pdf", request.Extension);
        Assert.Equal("Fixture.pdf", request.FileName);
        Assert.DoesNotContain(VisusRig.Live(tab).Log, line => line.StartsWith("PrintToPdf ", StringComparison.Ordinal));
    });

    [Fact]
    public void Save_as_pdf_exports_the_selected_document_to_the_chosen_path() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var first = visus.Page("https://one.test/");
        var selected = visus.Page("https://two.test/");
        var files = new FakeTabulaFiles { SaveAnswer = _ => @"C:\nth-test\chosen.pdf" };
        var save = SavePdf(new VisusPaneBody(visus.Session), files);
        HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2));
        save.GetAwaiter().GetResult();
        Assert.Contains(@"PrintToPdf C:\nth-test\chosen.pdf", VisusRig.Live(selected).Log);
        Assert.DoesNotContain(VisusRig.Live(first).Log, line => line.StartsWith("PrintToPdf ", StringComparison.Ordinal));
    });

    [Fact]
    public void Save_as_pdf_does_not_export_a_replaced_document() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/old");
        var files = new FakeTabulaFiles
        {
            SaveAnswer = _ =>
            {
                tab.Open("https://example.test/new");
                return @"C:\nth-test\chosen.pdf";
            },
        };
        var save = SavePdf(new VisusPaneBody(visus.Session), files);
        HeadlessSession.RunDispatcherUntil(save, TimeSpan.FromSeconds(2));
        save.GetAwaiter().GetResult();
        Assert.DoesNotContain(VisusRig.Live(tab).Log, line => line.StartsWith("PrintToPdf ", StringComparison.Ordinal));
    });

    [Fact]
    public void Page_zoom_chord_is_handled_before_the_page_and_unrelated_keys_are_left_alone() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var page = VisusRig.Live(tab);
        Assert.True(page.RaiseKey(new KeyChord(0x0D, ChordModifiers.Control)).Handled);
        Assert.Equal(1.1, page.Zoom);
        Assert.False(page.RaiseKey(new KeyChord(0x0D, ChordModifiers.Control | ChordModifiers.Alt)).Handled);
        Assert.Equal(1.1, page.Zoom);
    });

    private static dynamic Find(VisusSession session)
    {
        // Bootstrap the red test against W8a; use the concrete type once W8c supplies it.
        var type = typeof(VisusSession).Assembly.GetType("Marrow.App.Views.Browser.VisusFindController");
        Assert.NotNull(type);
        return Activator.CreateInstance(type, session)!;
    }

    private static void AnswerFind(FakeWebView page, Func<string, bool, CancellationToken, Task<WebFindResult>> answer)
    {
        var handler = typeof(FakeWebView).GetProperty("FindHandler");
        Assert.NotNull(handler);
        handler.SetValue(page, answer);
    }

    [Fact]
    public void Find_reports_count_case_and_no_matches_and_clears_on_close() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var page = VisusRig.Live(tab);
        var requests = new List<(string Query, bool Case)>();
        AnswerFind(page, (query, matchCase, _) =>
        {
            requests.Add((query, matchCase));
            return Task.FromResult(query == "missing" ? new WebFindResult(0, -1) : new WebFindResult(matchCase ? 2 : 3, 0));
        });
        dynamic find = Find(visus.Session);
        try
        {
            Assert.True((bool)find.Open());
            Task search = find.SearchAsync("price", false);
            HeadlessSession.RunDispatcherUntil(search, TimeSpan.FromSeconds(2));
            search.GetAwaiter().GetResult();
            Assert.Equal("1 of 3", (string)find.CountText);
            search = find.SearchAsync("price", true);
            HeadlessSession.RunDispatcherUntil(search, TimeSpan.FromSeconds(2));
            search.GetAwaiter().GetResult();
            Assert.Equal("1 of 2", (string)find.CountText);
            search = find.SearchAsync("missing", true);
            HeadlessSession.RunDispatcherUntil(search, TimeSpan.FromSeconds(2));
            search.GetAwaiter().GetResult();
            Assert.Equal("No matches", (string)find.CountText);
            Assert.Equal(new[] { ("price", false), ("price", true), ("missing", true) }, requests);
            find.Close();
            Assert.False((bool)find.IsOpen);
            Assert.Contains("StopFind", page.Log);
        }
        finally { ((IDisposable)find).Dispose(); }
    });

    [Fact]
    public void A_late_find_result_cannot_reopen_or_update_a_closed_bar() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        var pending = new TaskCompletionSource<WebFindResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        AnswerFind(VisusRig.Live(tab), (_, _, _) => pending.Task);
        dynamic find = Find(visus.Session);
        try
        {
            Assert.True((bool)find.Open());
            Task search = find.SearchAsync("price", false);
            find.Close();
            pending.SetResult(new WebFindResult(99, 0));
            HeadlessSession.RunDispatcherUntil(search, TimeSpan.FromSeconds(2));
            search.GetAwaiter().GetResult();
            Assert.False((bool)find.IsOpen);
            Assert.Equal("", (string)find.CountText);
            Assert.Contains("StopFind", VisusRig.Live(tab).Log);
        }
        finally { ((IDisposable)find).Dispose(); }
    });

    [Fact]
    public void Switching_tabs_ends_find_in_the_original_document() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        dynamic find = Find(visus.Session);
        try
        {
            Assert.True((bool)find.Open());
            visus.Page("https://other.test/");
            Assert.False((bool)find.IsOpen);
            Assert.Contains("StopFind", VisusRig.Live(tab).Log);
        }
        finally { ((IDisposable)find).Dispose(); }
    });

    [Fact]
    public void Navigation_ends_find_before_the_document_changes() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var tab = visus.Page("https://example.test/");
        dynamic find = Find(visus.Session);
        try
        {
            Assert.True((bool)find.Open());
            VisusRig.Live(tab).RaiseNavigationStarting("https://example.test/new");
            Assert.False((bool)find.IsOpen);
            Assert.Contains("StopFind", VisusRig.Live(tab).Log);
        }
        finally { ((IDisposable)find).Dispose(); }
    });
}
