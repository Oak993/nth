using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Settings;

// The About > Credits card against SettingsView.swift:946-964 (the section), :1104-1121 (CreditLink), with the Terminal row settled by ED-272.
//
// SOURCE FACT (SettingsView.swift:954-955): ChromeRow(title: "Terminal", detail: "SwiftTerm by Miguel de Icaza, MIT License.") { CreditLink(title: "SwiftTerm", url: https://github.com/migueldeicaza/SwiftTerm) }.
// DEVIATION (OD-0008 B15, OD-0012 H "name the terminal component the Windows build actually ships", the licence line settled in OD-0072, ED-272): the port ships no SwiftTerm, so
// the row reads "Marrow's own terminal, built on Windows ConPTY." and has no link (ConPTY is a Windows API, not shipped code that needs a third-party credit). Every other row is the Swift's.
[Collection("Headless")]
public sealed class AboutCreditsTerminalTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    private const string TerminalDetail = "Nth's own terminal, built on Windows ConPTY."; // ED-272, the same words as the licence line (OD-0072)

    private static StackPanel CreditRows(Control page)
    {
        var root = Assert.IsType<StackPanel>(Assert.IsType<AboutSettingsPage>(page).Child);
        var section = Assert.IsType<ChromeSection>(root.Children[2]); // :946 ChromeSection(title: "Credits"), the page's third child after the update section and the app card
        var card = Assert.IsType<ChromeCard>(Assert.IsType<StackPanel>(section.Child).Children[1]); // :947 ChromeCard
        return Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(card.Child).Child).Children[0]);
    }

    [Fact]
    [Trait("Decision", "ED-272")] // the Terminal row's text and absent link
    [Trait("Req", "PR-UPDATE-085")] // SettingsView.swift:946-964: the Credits section lists Droppy, three attributions and the Licenses row, in that order
    [Trait("Req", "PR-SETTINGS-248")] // SettingsView.swift:946-964: five rows separated by dividers
    [Trait("Req", "UI-UPDATE-029")] // SettingsView.swift:954-956: the Terminal attribution row
    [Trait("Req", "UI-SETTINGS-072")] // SettingsView.swift:950-960: the credit links (the middle one, the terminal's, is absent by ED-272)
    public void Credits_hold_the_five_entries_in_the_sources_order_and_the_Terminal_row_is_ED_272_s_own_words_with_no_link()
    {
        With(() => new AboutSettingsPage(), (view, _, _, w) =>
        {
            var rows = CreditRows(view);
            // :948-962 five entries, a divider between each pair (:949, :953, :957, :961 ChromeRowDivider(), inset 16, Chrome.swift:1082-1087), in this order.
            Assert.Equal(9, rows.Children.Count);
            Assert.IsType<MadeByDroppyRow>(rows.Children[0]); Assert.IsType<LicensesRow>(rows.Children[8]);
            foreach (var index in new[] { 1, 3, 5, 7 }) Assert.Equal(new Thickness(16, 0, 0, 0), Assert.IsType<ChromeRowDivider>(rows.Children[index]).Margin);
            var working = Assert.IsType<ChromeRow>(rows.Children[2]); var terminal = Assert.IsType<ChromeRow>(rows.Children[4]); var tour = Assert.IsType<ChromeRow>(rows.Children[6]);
            // The title and detail of each, .body 13 over .subheadline 11 (Chrome.swift:1064-1069); the first and third verbatim from the Swift (:950, :958), the second ED-272's.
            CheckRow(working, "Working indicators", "Ported from Zeron by Wing, MIT License.");
            CheckRow(terminal, "Terminal", TerminalDetail);
            CheckRow(tour, "Welcome tour", "Ported from TourKit by Ram Patra, MIT License.");
            // The links: :951 zeronsh/zeron and :959 rampatra/TourKit stay (their clauses are Repository_credit_rows_match_exact_labels_fonts_colours_tooltips_and_real_clicks);
            // the Swift's third, :955 CreditLink "SwiftTerm", is not in the page: ED-272, no link in the Terminal row, and no CreditLink anywhere but those two.
            var links = Nodes<CreditLink>(view);
            Assert.Equal(2, links.Length); Assert.Same(Assert.Single(Nodes<CreditLink>(working)), links[0]); Assert.Same(Assert.Single(Nodes<CreditLink>(tour)), links[1]);
            Assert.Equal(new[] { "zeronsh/zeron", "rampatra/TourKit" }, links.Select(link => Text(link).Single()));
            Assert.Empty(Nodes<CreditLink>(terminal)); Assert.Empty(Nodes<Button>(terminal)); // no link, and nothing in the row presses
            // UI-UPDATE-029 states "Always enabled" (the Swift row has no .disabled): the row and its text draw enabled, at full opacity.
            Assert.True(terminal.IsEffectivelyEnabled); Assert.Equal(1, terminal.Opacity);
            foreach (var text in Nodes<TextBlock>(terminal)) { Assert.True(text.IsEffectivelyEnabled); Assert.Equal(1, text.Opacity); }
            // Nothing in the row is a link whatever its tooltip or accessibility says: the one text it draws besides its title is the detail.
            Assert.Equal(new[] { "Terminal", TerminalDetail }, Text(terminal));
            foreach (var control in Nodes<Control>(terminal)) { Assert.Null(ToolTip.GetTip(control)); Assert.False(control is Button); }
            // The row sits on the same card geometry as its neighbours: its two text lines set its height (a link is shorter than them, :1104-1121), so the row is as tall as the others.
            Assert.Equal(working.Bounds.Height, terminal.Bounds.Height, 0.5); Assert.Equal(tour.Bounds.Height, terminal.Bounds.Height, 0.5);
            // A click on the row, its middle and its trailing end where the Swift's link would be, opens nothing.
            var opened = new List<string>(); using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
            foreach (var x in new[] { 0.3, 0.5, 0.97 }) Click(w, terminal, new Point(terminal.Bounds.Width * x, terminal.Bounds.Height / 2));
            Assert.Empty(opened);
        });
    }

    [Fact]
    [Trait("Decision", "ED-272")] // no SwiftTerm credit anywhere on the page: the row and the licence line both name the Windows terminal
    [Trait("Req", "PR-UPDATE-085")] // the terminal credit is not SwiftTerm (OD-0012 H: name the component the Windows build ships)
    [Trait("Req", "PR-SETTINGS-248")]
    [Trait("Req", "UI-UPDATE-029")] // the row's text: neither "SwiftTerm" nor its author, licence or URL (SettingsView.swift:954-955)
    [Trait("Req", "UI-SETTINGS-072")]
    public void No_text_or_link_of_the_About_page_credits_SwiftTerm_and_the_row_and_the_licence_line_say_the_same_words()
    {
        With(() => new AboutSettingsPage(), (view, _, _, _) =>
        {
            var all = string.Join("\n", Text(view));
            foreach (var swiftTerm in new[] { "SwiftTerm", "Miguel de Icaza", "migueldeicaza", "Miguel" }) Assert.DoesNotContain(swiftTerm, all, StringComparison.OrdinalIgnoreCase);
            foreach (var tooltip in Nodes<Control>(view).Select(c => ToolTip.GetTip(c)).OfType<string>()) Assert.DoesNotContain("SwiftTerm", tooltip, StringComparison.OrdinalIgnoreCase);
            // :997-1002 the licence line, whose "SwiftTerm is MIT licensed" OD-0072 settled as "the terminal is Marrow's own, built on Windows ConPTY"; the Terminal row's detail is the same clause.
            var licence = Assert.Single(Nodes<TextBlock>(view), t => t.Text is { } text && text.StartsWith("Nth is licensed under", StringComparison.Ordinal));
            Assert.Equal("Nth is licensed under the GNU AGPL v3.0 with attribution terms; the terminal is Nth's own, built on Windows ConPTY.", licence.Text);
            Assert.EndsWith("built on Windows ConPTY.", licence.Text, StringComparison.Ordinal); Assert.EndsWith("built on Windows ConPTY.", TerminalDetail, StringComparison.Ordinal);
            Assert.Equal(2, all.Split("built on Windows ConPTY.", StringSplitOptions.None).Length - 1); // the Terminal row's detail and the licence line, once each
        });
    }
}
