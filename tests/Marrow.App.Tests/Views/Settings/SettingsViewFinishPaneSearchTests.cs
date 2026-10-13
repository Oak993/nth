using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Settings;

// SettingsView.swift:211-215 (a page change empties both queries), :245-253 (the chrome row), :276-282 (the queries reach their
// pages); UI/Chrome/Chrome.swift:825-869 ChromeSearchField; the pages' own matching: ModelsSettingsPage.swift:22-27,66-71,87-93,
// :153-157 and MCPSettingsPage.swift:77-85,381-399.
[Collection("Headless")]
public sealed class SettingsViewFinishPaneSearchTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-009")] // App/Views/Settings/SettingsView.swift:211-215,248-250,282; UI/Chrome/Chrome.swift:827-868
    public void Models_search_capsule_is_the_fixed_glass_field_in_the_chrome_row_and_filters_the_models_page_live()
    {
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            None<ChromeSearchField>(view, "pane search #1"); // :248 only the Models page has it
            Reveal(w, h, RowButton(view, "Models")); /* ED-NEW-final-settings-1: Models lies below the 580 pt window's fold, so the sidebar is scrolled to it first */ Click(w, RowButton(view, "Models")); var page = Assert.Single(Nodes<ModelsSettingsPage>(view));
            CheckPaneSearch(view, w, h, "Search models");
            var field = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<ChromeSearchField>(view))));
            // ModelsSettingsPage.swift:66-71 the "N of 15" line shows only while the search is empty; :34-40 the pinned model is a row while it matches.
            Assert.Contains(Text(page), t => t.StartsWith("1 of 15.", StringComparison.Ordinal)); Assert.Contains("Your models", Text(page));
            // :153-157 the search matches the model's id (and name, detail, provider); the query is trimmed first (:23).
            Type(w, field, "  claude-mod "); Assert.Equal("  claude-mod ", field.Text); Assert.DoesNotContain(Text(page), t => t.StartsWith("1 of 15.", StringComparison.Ordinal)); Assert.Contains("Your models", Text(page)); Assert.DoesNotContain(Text(page), t => t.StartsWith("No models match", StringComparison.Ordinal));
            // :87-93 nothing matches: the section steps aside and the sentence names the trimmed query in curly quotes.
            field.Text = ""; Drain(w); Type(w, field, "  zzz "); Assert.Contains("No models match “zzz”.", Text(page)); Assert.DoesNotContain("Your models", Text(page));
            Press(w, Avalonia.Input.Key.Escape); Assert.Equal("", field.Text ?? ""); Assert.Contains("Your models", Text(page)); Assert.Contains(Text(page), t => t.StartsWith("1 of 15.", StringComparison.Ordinal)); Assert.DoesNotContain(Text(page), t => t.StartsWith("No models match", StringComparison.Ordinal));
            // :211-215 changing the page empties the query: away and back, the field is empty and the page unfiltered.
            Type(w, field, "zzz"); Assert.Contains("No models match “zzz”.", Text(page)); Reveal(w, h, RowButton(view, "General")); Click(w, RowButton(view, "General")); None<ChromeSearchField>(view, "pane search #2"); Reveal(w, h, RowButton(view, "Models")); Click(w, RowButton(view, "Models"));
            var fresh = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<ChromeSearchField>(view)))); Assert.Equal("", fresh.Text ?? ""); Assert.Contains("Your models", Text(Assert.Single(Nodes<ModelsSettingsPage>(view))));
            Click(w, RowButton(view, "MCP")); Assert.Equal("Search servers", Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<ChromeSearchField>(view)))).PlaceholderText); // :251 the prompt differs only in its text
        }, model => model.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, "claude-model")));
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-010")] // App/Views/Settings/SettingsView.swift:211-215,251-253,280; UI/Chrome/Chrome.swift:827-868
    [Trait("Req", "UI-MCP-090")] // App/Views/Settings/SettingsView.swift:214,252,280; App/Views/Settings/MCPSettingsPage.swift:22-31,77-85,381-399
    [Trait("Req", "PR-MCP-202")] // App/Views/Settings/SettingsView.swift:214,252; the field half: prompt "Search servers", filtering as typed, query emptied when the page changes
    public void Server_search_capsule_is_the_header_field_that_filters_the_category_grids_and_the_custom_section_and_empties_with_the_page()
    {
        With(() => new SettingsView(), (view, model, h, w) =>
        {
            None<ChromeSearchField>(view, "pane search #3"); // :251 only the MCP page has it, and the page itself holds no field
            Click(w, RowButton(view, "MCP")); var page = Assert.Single(Nodes<MCPSettingsPage>(view)); None<ChromeSearchField>(page, "pane search #4"); None<TextBox>(page, "pane search #5");
            CheckPaneSearch(view, w, h, "Search servers");
            var field = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<ChromeSearchField>(view))));
            // Unfiltered: every category grid, the Custom section with the user's server and its "Add server" row.
            Assert.Contains("Custom", Text(page)); Assert.Contains("Example", Text(page)); Assert.Contains("Add server", Text(page)); Assert.Contains("Developer", Text(page)); Assert.Contains("Browser", Text(page)); Assert.Contains("GitHub", Text(page)); Assert.Contains("Playwright", Text(page));
            // MCPSettingsPage.swift:77-85 a name match narrows the grids to the one category and, matching no custom server, the Custom section steps aside (:392,:396).
            Type(w, field, "playwright"); Assert.Contains("Playwright", Text(page)); Assert.Contains("Browser", Text(page)); Assert.DoesNotContain("GitHub", Text(page)); Assert.DoesNotContain("Developer", Text(page)); Assert.DoesNotContain("Custom", Text(page)); Assert.DoesNotContain("Add server", Text(page));
            // A match inside the custom server keeps that section with only that server, and the catalog entry whose summary says "examples" keeps its category.
            field.Text = ""; Drain(w); Type(w, field, "EXAMPLE"); Assert.Contains("Custom", Text(page)); Assert.Contains("Example", Text(page)); Assert.Contains("Add server", Text(page)); Assert.Contains("Context7", Text(page)); Assert.Contains("Knowledge", Text(page)); Assert.DoesNotContain("Playwright", Text(page)); Assert.DoesNotContain("GitHub", Text(page));
            // A query matching nothing leaves no category and no Custom section; whitespace alone is not a search at all (:392 `.trimmed`).
            field.Text = ""; Drain(w); Type(w, field, " zzzz "); Assert.DoesNotContain("Custom", Text(page)); Assert.DoesNotContain("Developer", Text(page)); Assert.DoesNotContain("Browser", Text(page)); Assert.DoesNotContain("GitHub", Text(page));
            field.Text = ""; Drain(w); Type(w, field, "   "); Assert.Contains("Custom", Text(page)); Assert.Contains("Example", Text(page)); Assert.Contains("GitHub", Text(page)); Assert.Contains("Playwright", Text(page));
            // :214 the query is reset to the empty string when the settings page changes; Escape empties it on the same page.
            field.Text = ""; Drain(w); Type(w, field, "github"); Assert.Contains("GitHub", Text(page)); Assert.DoesNotContain("GitLab", Text(page)); Press(w, Avalonia.Input.Key.Escape); Assert.Contains("GitLab", Text(page));
            Type(w, field, "github"); Click(w, RowButton(view, "General")); None<ChromeSearchField>(view, "pane search #6"); Click(w, RowButton(view, "MCP"));
            var fresh = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<ChromeSearchField>(view)))); Assert.Equal("", fresh.Text ?? ""); var again = Assert.Single(Nodes<MCPSettingsPage>(view)); Assert.Contains("GitLab", Text(again)); Assert.Contains("Playwright", Text(again));
        }, model => Assert.Null(model.Mcp.SaveCustom(MCPSettingsPageFixture.Custom())));
    }
}
