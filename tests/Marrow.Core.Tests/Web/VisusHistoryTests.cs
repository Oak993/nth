using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2 "Suggestions come from local history and open
// tabs only (no network)", 8.1 "Private tabs leave no history", 8.3 "Remember for this site" for mailto and tel only, 3.11 the
// saved tabs). The visits, the rows under the address field, the remembered app answers and the saved tab list.
[Trait("Decision", "ED-NEW-web-w8a-1")]
public sealed class VisusHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Browse_and_preview_visits_are_kept_newest_first_with_their_count()
    {
        var history = new VisusHistory();
        var changes = 0;
        history.Changed += () => changes++;
        history.Record(VisusTabKind.Browse, "https://example.com/", "Example", Now.AddMinutes(-5));
        history.Record(VisusTabKind.Preview, "http://localhost:5173/", "Orbit", Now.AddMinutes(-3));
        history.Record(VisusTabKind.Browse, "https://example.com/", "", Now);
        Assert.Equal(3, changes);
        Assert.Equal(2, history.Entries.Count);
        var first = history.Entries[0];
        Assert.Equal("https://example.com/", first.Url);
        Assert.Equal("Example", first.Title); // an empty title keeps the one it had
        Assert.Equal(2, first.Visits);
        Assert.Equal(Now, first.LastVisit);
        Assert.Equal(WebProfileKind.Preview, history.Entries[1].Profile);
    }

    [Theory]
    [InlineData(VisusTabKind.Private, "https://example.com/")]
    [InlineData(VisusTabKind.Agent, "https://example.com/")]
    [InlineData(VisusTabKind.Browse, "about:blank")]
    [InlineData(VisusTabKind.Browse, "mailto:x@example.com")]
    [InlineData(VisusTabKind.Browse, null)]
    public void Private_and_agent_tabs_and_non_pages_leave_nothing(VisusTabKind kind, string? url)
    {
        var history = new VisusHistory();
        history.Record(kind, url, "x", Now);
        history.SetTitle(kind, url, "y");
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void A_late_title_reaches_its_visit()
    {
        var history = new VisusHistory();
        history.Record(VisusTabKind.Browse, "https://example.com/", "", Now);
        history.SetTitle(VisusTabKind.Browse, "https://example.com/", "  Example Domain ");
        Assert.Equal("Example Domain", history.Entries[0].Title);
        history.SetTitle(VisusTabKind.Private, "https://example.com/", "Leak");
        Assert.Equal("Example Domain", history.Entries[0].Title);
    }

    [Fact]
    public void The_history_keeps_at_most_its_capacity()
    {
        var history = new VisusHistory();
        for (var i = 0; i < VisusHistory.Capacity + 5; i++)
            history.Record(VisusTabKind.Browse, $"https://example.com/{i}", "", Now.AddSeconds(i));
        Assert.Equal(VisusHistory.Capacity, history.Entries.Count);
        Assert.Equal($"https://example.com/{VisusHistory.Capacity + 4}", history.Entries[0].Url);
    }

    [Fact]
    public void Clearing_a_profile_removes_only_its_visits()
    {
        var history = new VisusHistory();
        history.Record(VisusTabKind.Browse, "https://example.com/", "", Now);
        history.Record(VisusTabKind.Preview, "http://localhost:3000/", "", Now);
        history.Clear(WebProfileKind.Browse);
        Assert.Equal("http://localhost:3000/", Assert.Single(history.Entries).Url);
    }

    [Fact]
    public void The_history_round_trips_and_a_damaged_file_reads_as_empty()
    {
        var history = new VisusHistory();
        history.Record(VisusTabKind.Browse, "https://example.com/", "Example", Now);
        history.Record(VisusTabKind.Preview, "http://localhost:5173/", "Orbit", Now.AddMinutes(1));
        var back = VisusHistory.FromJson(history.ToJson());
        Assert.Equal(history.Entries, back.Entries);
        Assert.Empty(VisusHistory.FromJson("{ not json").Entries);
        Assert.Empty(VisusHistory.FromJson(null).Entries);
        // Edited files cannot plant private visits or non-pages.
        var planted = """[{"Url":"javascript:alert(1)","Title":"","LastVisit":"2026-10-12T09:00:00+00:00","Visits":1,"Profile":"Browse"},{"Url":"https://a.example/","Title":"","LastVisit":"2026-10-12T09:00:00+00:00","Visits":1,"Profile":"Private"}]""";
        Assert.Empty(VisusHistory.FromJson(planted).Entries);
    }

    [Fact]
    public void The_first_row_is_what_enter_does()
    {
        Assert.Equal(VisusSuggestionKind.Go, First("example.com").Kind);
        Assert.Equal("Go to this address", First("example.com").Detail);
        Assert.Equal(VisusSuggestionKind.Search, First("cook rice").Kind);
        Assert.Equal("Search DuckDuckGo", First("cook rice").Detail);
        Assert.Equal(VisusSuggestionKind.OpenApp, First("mailto:dana@example.com").Kind);
        Assert.Empty(VisusSuggestions.For("", SearchEngine.DuckDuckGo, [], [], null, VisusTabKind.Browse, Now));

        static VisusSuggestion First(string text) => VisusSuggestions.For(text, SearchEngine.DuckDuckGo, [], [], null, VisusTabKind.Browse, Now)[0];
    }

    [Fact]
    public void Open_tabs_then_history_follow_best_first()
    {
        var history = new VisusHistory();
        history.Record(VisusTabKind.Browse, "https://news.example.org/", "Example news", Now.AddDays(-30));
        history.Record(VisusTabKind.Browse, "https://example.com/docs", "Example docs", Now.AddHours(-1));
        history.Record(VisusTabKind.Browse, "https://other.dev/example", "Unrelated", Now);
        history.Record(VisusTabKind.Browse, "https://nothing.dev/", "Nothing", Now);
        var tabs = new List<VisusOpenTab>
        {
            new("t1", "https://example.com/pricing", "Pricing", VisusTabKind.Browse),
            new("t2", "https://example.com/secret", "Secret", VisusTabKind.Private),
            new("me", "https://example.com/here", "Here", VisusTabKind.Browse),
        };
        var rows = VisusSuggestions.For("example", SearchEngine.DuckDuckGo, tabs, history.Entries, "me", VisusTabKind.Browse, Now);
        Assert.Equal(VisusSuggestionKind.Search, rows[0].Kind);
        Assert.Equal(VisusSuggestionKind.SwitchToTab, rows[1].Kind);
        Assert.Equal("t1", rows[1].TabId); // never the tab typed in, never a private tab's page from a normal tab
        Assert.DoesNotContain(rows, row => row.TabId is "t2" or "me");
        var pages = rows.Where(row => row.Kind == VisusSuggestionKind.History).Select(row => row.Url).ToList();
        Assert.Equal(new[] { "https://example.com/docs", "https://news.example.org/", "https://other.dev/example" }, pages);
        Assert.DoesNotContain(rows, row => row.Url == "https://nothing.dev/");
        Assert.True(rows.Count <= VisusSuggestions.Limit);
    }

    [Fact]
    public void A_private_tab_is_offered_only_from_a_private_tab()
    {
        var tabs = new List<VisusOpenTab> { new("p", "https://example.com/secret", "Secret", VisusTabKind.Private) };
        var fromPrivate = VisusSuggestions.For("secret", SearchEngine.DuckDuckGo, tabs, [], "q", VisusTabKind.Private, Now);
        Assert.Contains(fromPrivate, row => row.TabId == "p");
        var fromBrowse = VisusSuggestions.For("secret", SearchEngine.DuckDuckGo, tabs, [], "q", VisusTabKind.Browse, Now);
        Assert.DoesNotContain(fromBrowse, row => row.TabId == "p");
    }

    [Fact]
    public void The_list_stops_at_six_rows_and_never_repeats_an_address()
    {
        var history = new VisusHistory();
        for (var i = 0; i < 20; i++) history.Record(VisusTabKind.Browse, $"https://example.com/{i}", "Example " + i, Now.AddMinutes(-i));
        history.Record(VisusTabKind.Browse, "https://example.com/", "Example", Now);
        var rows = VisusSuggestions.For("example.com", SearchEngine.DuckDuckGo, [], history.Entries, null, VisusTabKind.Browse, Now);
        Assert.Equal(VisusSuggestions.Limit, rows.Count);
        Assert.Equal(rows.Count, rows.Select(row => row.Url).Distinct().Count());
        Assert.Equal("https://example.com/", rows[0].Url); // the Go row; the same page is not listed again
    }

    [Fact]
    public void Saved_tabs_drop_private_tabs_and_non_pages_and_keep_a_valid_selection()
    {
        var state = new VisusTabsState
        {
            Tabs = new ValueList<VisusTabState>(
            [
                new("a", VisusTabKind.Browse, "https://example.com/", "Example"),
                new("b", VisusTabKind.Private, "https://secret.example/", "Secret"),
                new("c", VisusTabKind.Preview, "http://localhost:5173/", "Orbit", 1.25),
                new("a", VisusTabKind.Browse, "https://dup.example/", "Duplicate id"),
                new("d", VisusTabKind.Browse, "file:///C:/x.html", "File"),
                new("e", VisusTabKind.Browse, "about:blank", "", double.NaN),
            ]),
            SelectedId = "b",
        }.Normalized();
        Assert.Equal(new[] { "a", "c", "e" }, state.Tabs.Select(tab => tab.Id).ToArray());
        Assert.Equal("a", state.SelectedId); // the private tab was chosen; it is not saved
        Assert.Equal(1.25, state.Tabs[1].Zoom);
        Assert.Equal(1, state.Tabs[2].Zoom);
        Assert.Null(VisusTabsState.Empty.Normalized().SelectedId);
    }

    [Fact]
    public void Saved_tabs_round_trip_through_the_threads_json_and_bad_text_reads_as_none()
    {
        var state = new VisusTabsState
        {
            Tabs = new ValueList<VisusTabState>(
            [
                new("a", VisusTabKind.Browse, "https://example.com/", "Example \"quoted\""),
                new("c", VisusTabKind.Preview, "http://localhost:5173/", "Orbit", 1.25),
            ]),
            SelectedId = "c",
        };
        var json = state.ToJson();
        Assert.Equal(state.Normalized(), VisusTabsState.FromJson(json));
        Assert.Contains("\"kind\":\"Preview\"", json, StringComparison.Ordinal);
        Assert.Equal(VisusTabsState.Empty, VisusTabsState.FromJson("{ nope"));
        Assert.Equal(VisusTabsState.Empty, VisusTabsState.FromJson("[]"));
        Assert.Equal(VisusTabsState.Empty, VisusTabsState.FromJson(null));

        // A newer build's kind is dropped and the rest kept; a private tab or a non-page written by hand never comes back.
        var mixed = VisusTabsState.FromJson(
            """{"selected":"x","tabs":[{"id":"x","kind":"Hologram","url":"https://x.example/"},{"id":"p","kind":"Private","url":"https://secret.example/"},{"id":"a","kind":"Browse","url":"https://a.example/","title":"A","zoom":2},{"id":"f","kind":"Browse","url":"file:///C:/x"},null]}""");
        Assert.Equal(new[] { "a" }, mixed.Tabs.Select(tab => tab.Id).ToArray());
        Assert.Equal("a", mixed.SelectedId);
        Assert.Equal(2, mixed.Tabs[0].Zoom);
        Assert.Equal("A", mixed.Tabs[0].Title);
    }
}
