using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Panes;

namespace Marrow.App.Tests.Views.Browser;

// PORT-ONLY: web surfaces W10, "Show search suggestions" in the address field (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2, 9; ED-NEW-web-w10-7). With the switch off nothing is asked and the list is the reader's own tabs and history. With it
// on, a search phrase is asked of the chosen engine a moment after the last key; the answer joins the list under the typed row; an address, a private tab or a text that moved on asks or shows nothing; closing the list
// cancels the ask. A manual clock decides "a moment"; no network is used.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-7")]
public sealed class WebW10SuggestionsFieldTests(HeadlessSession headless)
{
    private static Func<PaneKind, PaneContent?> Contents(VisusRig visus) => kind => kind == PaneKind.Browser
        ? VisusPane.Create(visus.Session, null)
        : new PaneContent(new Border { Child = new TextBox { Text = PaneKinds.Title(kind) } }, PaneKinds.Title(kind));

    private static (PaneWorkspaceRig Rig, VisusPaneBody Body) Open(VisusRig visus)
    {
        var rig = new PaneWorkspaceRig(width: 1400, contents: Contents(visus), popupLayers: true);
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        return (rig, Assert.IsType<VisusPaneBody>(rig.Card(PaneKind.Browser).Content.Body));
    }

    private static void Elapse(VisusRig visus, int milliseconds)
    {
        visus.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void With_the_switch_off_nothing_is_asked_however_the_reader_types() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        var asked = new List<string>();
        VisusHost.SuggestionSource = (engine, typed, token) => { asked.Add(typed); return Task.FromResult<IReadOnlyList<string>>(["x"]); };
        var (rig, body) = Open(visus);
        using var _ = rig;
        visus.Page("https://example.com/");
        body.Toolbar.Address.Box.Focus();
        rig.Layout();
        body.Toolbar.Address.Box.Text = "cook rice";
        Elapse(visus, 1000);
        Assert.Empty(asked);
        Assert.Equal([VisusSuggestionKind.Search], body.Toolbar.Address.Suggestions.Select(s => s.Kind));
    });

    [Fact]
    public void With_it_on_a_search_phrase_is_asked_once_a_moment_after_the_last_key_and_the_answer_joins_the_list() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.SearchSuggestions = () => true;
        VisusHost.SearchEngine = () => SearchEngine.Brave;
        var asks = new List<(SearchEngine Engine, string Typed)>();
        VisusHost.SuggestionSource = (engine, typed, token) => { asks.Add((engine, typed)); return Task.FromResult<IReadOnlyList<string>>(["cook rice fast", "cook rice noodles"]); };
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        visus.Page("https://example.com/");
        field.Box.Focus();
        rig.Layout();

        field.Box.Text = "co";
        Elapse(visus, 100);
        field.Box.Text = "coo";
        Elapse(visus, 100);
        field.Box.Text = "cook rice";
        Elapse(visus, 179);
        Assert.Empty(asks); // each key starts the wait again
        Elapse(visus, 2);
        var ask = Assert.Single(asks);
        Assert.Equal((SearchEngine.Brave, "cook rice"), ask);
        Assert.Equal(VisusSuggestionKind.Search, field.Suggestions[0].Kind);
        Assert.Equal(["cook rice", "cook rice fast", "cook rice noodles"], field.Suggestions.Take(3).Select(s => s.Title));
        Assert.Equal("Search Brave", field.Suggestions[1].Detail);
        Assert.True(field.AreSuggestionsOpen);
    });

    [Fact]
    public void An_address_an_app_link_and_a_private_tab_ask_nothing() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.SearchSuggestions = () => true;
        var asked = new List<string>();
        VisusHost.SuggestionSource = (engine, typed, token) => { asked.Add(typed); return Task.FromResult<IReadOnlyList<string>>(["x"]); };
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        visus.Page("https://example.com/");
        field.Box.Focus();
        rig.Layout();
        foreach (var text in new[] { "example.org", "https://example.org/a", "localhost:5173", "mailto:dana@example.org", "dana@example.org", "4111 1111 1111 1111" })
        {
            field.Box.Text = text;
            Elapse(visus, 1000);
        }
        Assert.Empty(asked);

        visus.Page("https://private.example.org/", VisusTabKind.Private);
        field.Box.Focus();
        rig.Layout();
        field.Box.Text = "private search phrase";
        Elapse(visus, 1000);
        Assert.Empty(asked);
    });

    [Fact]
    public void An_answer_for_text_the_reader_has_since_changed_is_dropped() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.SearchSuggestions = () => true;
        var release = new TaskCompletionSource<IReadOnlyList<string>>();
        VisusHost.SuggestionSource = (engine, typed, token) => release.Task;
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        visus.Page("https://example.com/");
        field.Box.Focus();
        rig.Layout();
        field.Box.Text = "cook rice";
        Elapse(visus, 200);
        field.Box.Text = "cook rice fast";
        release.SetResult(["cook rice pudding"]);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(field.Suggestions, s => s.Title == "cook rice pudding");
    });

    [Fact]
    public void Closing_the_list_cancels_the_ask_in_flight() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.SearchSuggestions = () => true;
        CancellationToken seen = default;
        var release = new TaskCompletionSource<IReadOnlyList<string>>();
        VisusHost.SuggestionSource = (engine, typed, token) => { seen = token; return release.Task; };
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        visus.Page("https://example.com/");
        field.Box.Focus();
        rig.Layout();
        field.Box.Text = "cook rice";
        Elapse(visus, 200);
        Assert.False(seen.IsCancellationRequested);
        Assert.True(field.Box.IsFocused);
        field.Box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, PhysicalKey = PhysicalKey.Escape });
        Assert.True(seen.CanBeCanceled, "the ask was made");
        Assert.True(seen.IsCancellationRequested, "Esc closed the list but the ask went on");
        release.SetResult(["too late"]);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(field.Suggestions, s => s.Title == "too late");
    });

    [Fact]
    public void A_newer_key_cancels_the_earlier_ask() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        VisusHost.SearchSuggestions = () => true;
        var tokens = new List<CancellationToken>();
        var release = new TaskCompletionSource<IReadOnlyList<string>>();
        VisusHost.SuggestionSource = (engine, typed, token) => { tokens.Add(token); return release.Task; };
        var (rig, body) = Open(visus);
        using var _ = rig;
        var field = body.Toolbar.Address;
        visus.Page("https://example.com/");
        field.Box.Focus();
        rig.Layout();
        field.Box.Text = "cook";
        Elapse(visus, 200);
        field.Box.Text = "cook rice";
        Assert.True(tokens[0].IsCancellationRequested);
        Elapse(visus, 200);
        Assert.Equal(2, tokens.Count);
        Assert.False(tokens[1].IsCancellationRequested);
        release.SetResult([]);
    });

    [Fact]
    public void The_delay_is_the_designed_180_ms_and_the_default_source_is_the_real_one() => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        Assert.Equal(TimeSpan.FromMilliseconds(180), VisusHost.SuggestionDelay);
        Assert.False(VisusHost.SearchSuggestions());
    });
}
