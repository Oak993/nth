using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Avalonia.LogicalTree;
using static Marrow.App.Tests.Fixtures.MCPProofsMcpARound2Input;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageTestFirst;

namespace Marrow.App.Tests.Views.Settings;

// Test-first completion of the MCP settings page records: each test is derived from DroppyCode/App/Views/Settings/MCPSettingsPage.swift
// (line numbers on each test). STOP cloud-verify #1 and STOP blitz-frames (2026-10-04): CLOSED 2026-10-04 (unit rec-linebox). PR-MCP-199's capture
// comparison against 1-settings-tour.mov (the page is on screen at S 180.5-188.2) is MCPSettingsPageFrameTests: the lead card 94, the Connected
// rows 58 / 74 / 58 in a 192 card and the Custom card 97 (S 183.5), a catalog card 126 at a 136 row pitch (S 186.0) and a two-line-title card 144 at
// 154 (S 187.5), all matched to the pixel. The cause the earlier STOP left UNRESOLVED is settled by ED-290 (1) (measured 2026-10-04 on both videos and
// six screens): the explicit text styles draw in 18 / 17 / 16 line boxes (body, callout, subheadline) with their glyphs at their true size (the
// larger-glyph reading was an ink-measure bias), keyed by the TEXT STYLE, not by the point size (fixed .system(size:) fonts keep the face's box);
// the page's own 13 / 12 / 11 type is now drawn as those styles, as MCPSettingsPage.swift writes it.
[Collection("Headless")]
public sealed class MCPSettingsPageTestFirstTests(HeadlessSession headless)
{
    // A rig that may pump the main loop: only for tests that never press Connect (a pressed Connect queues a probe).
    private void Flow(Action<AppModel, TestRuntime> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        using var audio = SoundOutput.Current.Override(new MCPSettingsSilentSound());
        var model = runtime.OnMain(() => new AppModel());
        test(model, runtime);
        runtime.OnMain(() => Dispatcher.UIThread.RunJobs());
    });

    // The whole body is ONE main-loop step: whatever a press queues (the probe) is never run, and the runtime is dropped with it.
    private void OneStep(Action<AppModel> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        var ran = false;
        Step(runtime, () => { try { test(new AppModel()); Dispatcher.UIThread.RunJobs(); ran = true; } finally { MCPKeyPanel.Shared.Close(); MCPSetupPanel.Shared.Close(); } });
        Assert.True(ran, "the test body did not run");
    });

    private static string Kind(Control child) => child switch
    {
        MCPLeadCard => "lead",
        ChromeSection section => "section:" + Texts(section)[0],
        MCPCustomSection { Child: null } => null!,
        MCPCustomSection => "custom",
        TextBlock => "footer",
        _ => child.GetType().Name,
    };

    private static string[] Order(MCPSettingsPage page) => Assert.IsType<StackPanel>(page.Child).Children.Select(Kind).Where(kind => kind is not null).ToArray();
    private static void AssertOrder(IEnumerable<string> expected, MCPSettingsPage page) => Assert.True(string.Join(" > ", expected) == string.Join(" > ", Order(page)), "expected [" + string.Join(" > ", expected) + "] but the page is [" + string.Join(" > ", Order(page)) + "]");

    private static readonly string[] Categories = { "section:Developer", "section:Browser", "section:Search", "section:Work", "section:Data", "section:Cloud", "section:Knowledge" };

    // Chrome.sectionSpacing = 20 between neighbours of the page's VStack (MCPSettingsPage.swift:36; Chrome.swift:59).
    private static void AssertTwentyBetween(Control stack)
    {
        var kids = Assert.IsType<StackPanel>(stack).Children.Where(c => c.Bounds.Height > 0).ToArray();
        Assert.True(kids.Length >= 2);
        for (var i = 1; i < kids.Length; i++) { var gap = kids[i].Bounds.Top - kids[i - 1].Bounds.Bottom; Assert.True(Math.Abs(gap - 20) <= 0.5, "the gap between " + Kind(kids[i - 1]) + " and " + Kind(kids[i]) + " is " + gap); }
    }

    [Fact]
    [Trait("Req", "PR-MCP-199")] // App/Views/Settings/MCPSettingsPage.swift:35-63; UI/Chrome/Chrome.swift:59,79
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-MCP-199: the source order and spacings; the capture comparison against S 183.5-187.5 is MCPSettingsPageFrameTests (STOP cloud-verify #1 closed 2026-10-04)
    public void Page_stacks_lead_connected_custom_categories_and_footer_with_twenty_points_between()
    {
        Flow((model, runtime) =>
        {
            MCPSettingsPage page = null!; IDisposable window = null!;
            runtime.OnMain(() => { page = new MCPSettingsPage(""); window = Attach(page, model); });
            try
            {
                var root = Assert.IsType<StackPanel>(page.Child); Assert.Equal(20, root.Spacing);
                // :37-62 nothing connected: lead card, Custom, one grid section per non-empty category, footer last.
                AssertOrder(new[] { "lead", "custom" }.Concat(Categories).Append("footer"), page); Layout(page); AssertTwentyBetween(root);
                var footer = Assert.IsType<TextBlock>(root.Children[^1]);
                Assert.Equal("Connected servers reach every provider when a thread starts: the CLIs at launch, the API models through nth itself.", footer.Text);
                // :61 .subheadline = 11; :62 Chrome.secondaryText = primaryText at opacity 0.7 (Chrome.swift:79, ED-062).
                Assert.Equal(11, footer.FontSize); Colour(footer.Foreground, Chrome.PrimaryText.Opacity(0.7), footer);
                // :51-52,17-19 each category is a two-column grid with 10 between the columns and 10 between the rows.
                var grid = Nodes<Grid>(Assert.IsType<ChromeSection>(root.Children[2])).Single(g => g.ColumnDefinitions.Count == 2);
                var cards = grid.Children.OrderBy(c => Grid.GetRow(c) * 2 + Grid.GetColumn(c)).ToArray(); Assert.True(cards.Length >= 3);
                Assert.Equal(10, cards[1].Bounds.Left - cards[0].Bounds.Right, 0.5); Assert.Equal(cards[0].Bounds.Width, cards[1].Bounds.Width, 0.5);
                Assert.Equal(10, cards[2].Bounds.Top - cards[0].Bounds.Bottom, 0.5); Assert.Equal(cards[0].Bounds.Left, cards[2].Bounds.Left, 0.5);
                // :48,62 a search matching nothing leaves the lead card and the footer, 20 apart (Custom steps aside while searching).
                runtime.OnMain(() => page.SetQuery("zzz-no-match-anywhere"));
                runtime.OnMain(() => { AssertOrder(new[] { "lead", "footer" }, page); Layout(page); AssertTwentyBetween(root); });
                runtime.OnMain(() => page.SetQuery(""));
                // :38-47 a connected server that is celebrating stays in its grid; only afterwards the Connected section opens
                // between the lead card and Custom.
                runtime.OnMain(() => Publish(model, "github", new MCPConnectionState.Connected(4)));
                runtime.OnMain(() => { AssertOrder(new[] { "lead", "custom" }.Concat(Categories).Append("footer"), page); Assert.Contains("Connected · 4 tools", Texts(page)); }); // U+00B7 MIDDLE DOT
                runtime.AdvanceMilliseconds(900);
                runtime.OnMain(() => { AssertOrder(new[] { "lead", "section:Connected", "custom" }.Concat(Categories).Append("footer"), page); Layout(page); AssertTwentyBetween(root); });
                // :19-23 the Connected list is not filtered by the search (only the grids and Custom are).
                runtime.OnMain(() => page.SetQuery("zzz-no-match-anywhere"));
                runtime.OnMain(() => { Assert.Equal("lead", Order(page)[0]); Assert.Equal("section:Connected", Order(page)[1]); Assert.Equal("footer", Order(page)[^1]); });
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }

    private static MCPCustomServer Stdio(string name, string command, params string[] args) => new(MCPCustomServer.MakeID(name), name, MCPCustomServer.Kind.Stdio) { Command = command, Args = new ValueList<string>(args) };

    // MCPSettingsPage.swift:77-85 over a catalog entry whose five searchable fields hold five different words.
    [Fact]
    [Trait("Req", "PR-MCP-202")] // App/Views/Settings/MCPSettingsPage.swift:77-85
    public void Search_trims_and_matches_name_vendor_summary_id_or_any_sample_tool_ignoring_case()
    {
        var entry = Entry("deltaid") with { Name = "Alphaname", Vendor = "Betavendor", Summary = "Gammasummary line", SampleTools = new[] { "epsilon_tool", "zeta_tool" } };
        // :78-79 the query is trimmed of whitespace and newlines; an empty query matches everything.
        foreach (var query in new[] { "", "   ", " \n\t " }) Assert.True(MCPSettingsPage.Matches(entry, query));
        // :80-84 each of the five fields on its own, any case, with the padding trimmed first; every sample tool is searched.
        foreach (var query in new[] { "ALPHANAME", " \n betavendor\t", "gammaSUMMARY", "  DELTAID ", "Epsilon_Tool", "zeta_tool" }) Assert.True(MCPSettingsPage.Matches(entry, query), query);
        // :80 containment, not equality or prefix; text that is in no field, or spans two fields, matches nothing.
        foreach (var query in new[] { "pha", "nam", "tool" }) Assert.True(MCPSettingsPage.Matches(entry, query), query);
        foreach (var query in new[] { "absent", "alphanamebetavendor", "toolzeta", "-alphaname", "alphaname-" }) Assert.False(MCPSettingsPage.Matches(entry, query), query);
        // :80 localizedCaseInsensitiveContains folds case but not accents (ED-009, R-STR-6).
        var accented = entry with { Name = "Caf\u00E9" }; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        Assert.True(MCPSettingsPage.Matches(accented, "CAF\u00C9")); Assert.False(MCPSettingsPage.Matches(accented, "cafe")); // U+00C9 LATIN CAPITAL LETTER E WITH ACUTE
    }

    // MCPSettingsPage.swift:393-396 the same predicate over customs' converted catalog entries (MCPCustom.swift:79-117).
    [Fact]
    [Trait("Req", "PR-MCP-202")] // App/Views/Settings/MCPSettingsPage.swift:77-85,393-396; Core/Models/MCPCustom.swift:79-117
    public void Search_applies_to_customs_through_name_the_literal_vendor_custom_the_summary_line_and_the_id()
    {
        Flow((model, runtime) =>
        {
            var stdio = Stdio("Zebra Gadget", "quokka-runner", "--flag-xyz");
            var http = new MCPCustomServer(MCPCustomServer.MakeID("Http Thing"), "Http Thing", MCPCustomServer.Kind.Http) { Url = "https://remote-host.invalid/mcp" };
            runtime.OnMain(() => { Assert.Null(model.Mcp.SaveCustom(stdio)); Assert.Null(model.Mcp.SaveCustom(http)); });
            string[] Rows(string query)
            {
                var section = runtime.OnMain(() => new MCPCustomSection(query)); using var window = runtime.OnMain(() => Attach(section, model));
                runtime.OnMain(() => { }); return Nodes<MCPCustomRow>(section).Select(row => Texts(row)[0]).ToArray();
            }
            Assert.Equal(new[] { "Zebra Gadget", "Http Thing" }, Rows(""));
            Assert.Equal(new[] { "Zebra Gadget" }, Rows("  ZEBRA gadget\n"));          // :80 name
            Assert.Equal(new[] { "Zebra Gadget" }, Rows("zebra-gadget"));              // :83 id (custom- plus the slugged name; the name has a space)
            Assert.Equal(new[] { "Zebra Gadget" }, Rows("quokka-RUNNER --flag-xyz"));  // :82 summary: the command and its arguments
            Assert.Equal(new[] { "Http Thing" }, Rows("remote-host.invalid"));         // :82 summary: the host of an http server
            Assert.Equal(new[] { "Zebra Gadget", "Http Thing" }, Rows("CUSTOM"));      // :81 vendor "Custom" (and the id prefix)
            Assert.Empty(Rows("no-such-server"));                                      // :392-396 nothing matches: the section steps aside
            // :81 the vendor of a converted custom entry is the literal "Custom", whatever its id.
            Assert.True(MCPSettingsPage.Matches(stdio.CatalogEntry() with { Id = "zz" }, "cUSTOM"));
        });
    }

    private static void AddRows(MCPCustomEditor editor, string label, int count) { for (var i = 0; i < count; i++) Click(Button(editor, label)); Layout(editor); }

    // MCPSettingsPage.swift:624-627,679-685, observed on the laid out editor rather than on the Height property alone.
    [Fact]
    [Trait("Req", "PR-MCP-229")] // App/Views/Settings/MCPSettingsPage.swift:624-627,642-653,662,679-685,815-816,828-829
    public void Editor_is_380_wide_and_min_560_of_base_plus_34_a_row_with_the_overflow_scrolling_inside()
    {
        Flow((model, runtime) =>
        {
            runtime.OnMain(() =>
            {
                // :683 an empty stdio form is 450 tall; :662 and 380 wide whatever the content.
                var editor = new MCPCustomEditor(null); using var window = Attach(editor, model); Layout(editor);
                Assert.Equal(new Size(380, 450), editor.Bounds.Size);
                // :828-829 Add appends a row, an empty key still counts: 450 + 3 * 34 = 552, then the 560 cap holds.
                AddRows(editor, "Add environment variable", 3); Assert.Equal(new Size(380, 552), editor.Bounds.Size);
                AddRows(editor, "Add environment variable", 1); Assert.Equal(new Size(380, 560), editor.Bounds.Size);
                AddRows(editor, "Add environment variable", 2); Assert.Equal(new Size(380, 560), editor.Bounds.Size);
                // :684 the form scrolls inside the capped popover: its content is taller than its viewport.
                var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Content is StackPanel); Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1);
                // :642-653 switching kind keeps the six rows and moves only the base: 320 + 6 * 34 = 524.
                Call(editor, "SetKind", MCPCustomServer.Kind.Http); Layout(editor); Assert.Equal(new Size(380, 524), editor.Bounds.Size);
                Assert.Equal(6, Nodes<TextBox>(editor).Count(box => box.PlaceholderText == "KEY"));
                // :815-816 Remove deletes one row; the height follows it.
                Click(Nodes<Button>(editor).First(b => ToolTip.GetTip(b) as string == "Remove this variable")); Layout(editor); Assert.Equal(new Size(380, 490), editor.Bounds.Size);
                Call(editor, "SetKind", MCPCustomServer.Kind.Stdio); Layout(editor); Assert.Equal(new Size(380, 560), editor.Bounds.Size);
            });
            // :683 an empty http form is 320 tall; :684 seven rows is 558 and eight are capped at 560; stdio with 3 rows is 552, 4 rows 560.
            foreach (var (kind, rows, height) in new[] { (MCPCustomServer.Kind.Http, 0, 320.0), (MCPCustomServer.Kind.Http, 7, 558.0), (MCPCustomServer.Kind.Http, 8, 560.0), (MCPCustomServer.Kind.Stdio, 3, 552.0), (MCPCustomServer.Kind.Stdio, 4, 560.0) })
                runtime.OnMain(() => { var editor = new MCPCustomEditor(Custom(rows, kind)); using var window = Attach(editor, model); Layout(editor); Assert.Equal(new Size(380, height), editor.Bounds.Size); });
        });
    }

    // MCPSettingsPage.swift:629-658,687-704: the header and the footer are outside the ScrollView.
    [Fact]
    [Trait("Req", "PR-MCP-230")] // App/Views/Settings/MCPSettingsPage.swift:629-658,687-704
    public void Error_line_and_save_button_stay_below_the_scrolling_form_and_a_failed_save_shows_its_message_above_the_button()
    {
        Flow((model, runtime) => runtime.OnMain(() =>
        {
            var editor = new MCPCustomEditor(null); using var window = Attach(editor, model); AddRows(editor, "Add environment variable", 5);
            var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First(v => v.Content is StackPanel);
            var save = Button(editor, "Add server"); var close = Nodes<Button>(editor).Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Close");
            var footer = Assert.IsType<StackPanel>(save.Parent); var header = Assert.IsAssignableFrom<Control>(close.Parent);
            // :630-656 header, scroll view, footer one under the other with 12 between them; :690 the footer's own items are 10 apart.
            Assert.Equal(10, footer.Spacing); Assert.Equal(12, scroll.Bounds.Top - header.Bounds.Bottom, 0.5); Assert.Equal(12, footer.Bounds.Top - scroll.Bounds.Bottom, 0.5);
            // :699-701 the button is trailing in its own row; :700 "Add server" when adding.
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, save.HorizontalAlignment); Assert.Equal(editor.Bounds.Width - 32, footer.Bounds.Width, 0.5);
            // :632 five variable rows overflow the 560 popover, so the form scrolls.
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 1); var name = Field(editor, "My server");
            var saveAt = Origin(save); var closeAt = Origin(close); var nameAt = Origin(name);
            // :688 scrolling the form to the bottom moves the form, never the header or the footer.
            scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height); Layout(editor);
            Assert.True(scroll.Offset.Y > 0); Assert.True(Origin(name).Y < nameAt.Y - 1); Assert.Equal(saveAt, Origin(save)); Assert.Equal(closeAt, Origin(close));
            scroll.Offset = default; Layout(editor); Assert.Equal(nameAt, Origin(name)); Assert.Equal(saveAt, Origin(save));
            // :691-698 a failed save puts its message in the footer, 10 above the button, subheadline in Chrome.danger.
            var bottomBefore = Origin(save).Y + save.Bounds.Height; Click(save); Layout(editor); save = Button(editor, "Add server"); // a failed save rebuilds the footer
            var error = Nodes<TextBlock>(footer).Single(t => t.Text == "The server needs a name.");
            Assert.Equal(11, error.FontSize); Colour(error.Foreground, Chrome.Danger, error); Assert.Equal(TextWrapping.Wrap, error.TextWrapping);
            Assert.Equal(10, save.Bounds.Top - error.Bounds.Bottom, 0.5); Assert.True(Origin(error).Y < Origin(save).Y);
            // the button did not move out of view: the footer grew upwards and took the room from the form.
            Assert.Equal(bottomBefore, Origin(save).Y + save.Bounds.Height, 0.5); Assert.True(save.TranslatePoint(new Point(0, save.Bounds.Height), editor)!.Value.Y <= editor.Bounds.Height + 0.5);
            // :695 at most three lines, wrapping: a long message is cut at three.
            Field(editor, "My server").Text = "Example"; Field(editor, "npx").Text = "offline-fixture"; Field(editor, "KEY").Text = "bad key " + string.Join(" ", Enumerable.Repeat("word", 90)); Dispatcher.UIThread.RunJobs();
            Click(save); Layout(editor); save = Button(editor, "Add server"); error = Nodes<TextBlock>(footer).Single(t => t.Text!.EndsWith("is not a valid variable name: letters, digits, `_`, `-` and `.`, starting with a letter or `_`.", StringComparison.Ordinal));
            Assert.Equal(3, error.MaxLines); Assert.Equal(3, error.TextLayout.TextLines.Count); Assert.Equal(10, save.Bounds.Top - error.Bounds.Bottom, 0.5); Assert.Empty(model.Mcp.Customs);
            // :700 and "Save" when editing.
            var edit = new MCPCustomEditor(Custom(1)); using var second = Attach(edit, model); Assert.NotNull(Button(edit, "Save"));
        }));
    }

    // MCPSettingsPage.swift:718-728.
    [Fact]
    [Trait("Req", "PR-MCP-231")] // App/Views/Settings/MCPSettingsPage.swift:718-728
    public void Close_is_a_24_square_whole_target_named_Close_whose_press_discards_the_draft_and_saves_nothing()
    {
        Flow((model, runtime) => runtime.OnMain(() =>
        {
            Assert.Null(model.Mcp.SaveCustom(Stdio("Existing", "offline-fixture"))); var stored = File.ReadAllBytes(MCPPaths.CustomsURL);
            var editor = new MCPCustomEditor(null); var closed = 0; Popovers.SetClosePopover(editor, () => closed++); using var window = Attach(editor, model);
            Field(editor, "My server").Text = "Draft server"; Field(editor, "npx").Text = "offline-fixture"; Dispatcher.UIThread.RunJobs(); Layout(editor);
            var close = Nodes<Button>(editor).Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Close");
            // :724 a 24 by 24 frame; :721-723 the xmark glyph, subheadline (11) semibold, Chrome.secondaryText = primaryText at 0.7.
            Assert.Equal(new Size(24, 24), close.Bounds.Size); var glyph = Assert.Single(Nodes<SwiftImage>(close)); Assert.Equal("xmark", glyph.SymbolName);
            var font = SwiftImage.GetFont(glyph)!.Resolve(); Assert.Equal(11, font.FontSize); Assert.Equal(FontWeight.SemiBold, font.FontWeight); Colour(glyph.Foreground, Chrome.PrimaryText.Opacity(0.7), glyph);
            // :725 the whole rectangle is the hit area, down to its four corners.
            foreach (var corner in new[] { new Point(0.5, 0.5), new Point(23.5, 0.5), new Point(0.5, 23.5), new Point(23.5, 23.5) }) PointerAt(close, corner, press: false);
            Assert.Equal(0, closed);
            // :719 a press near a corner dismisses and nothing is saved: the draft is discarded.
            PointerAt(close, new Point(23, 1)); Assert.Equal(1, closed);
            Assert.Single(model.Mcp.Customs); Assert.Equal(stored, File.ReadAllBytes(MCPPaths.CustomsURL));
        }));
    }

    // MCPSettingsPage.swift:40-45 over Chrome.swift:1006-1021,1082-1087.
    [Fact]
    [Trait("Req", "UI-MCP-007")] // App/Views/Settings/MCPSettingsPage.swift:40-45; UI/Chrome/Chrome.swift:1006-1021,1076-1081
    public void Connected_card_is_a_full_width_lazy_stack_with_no_spacing_on_a_rounded_16_fill_and_a_divider_inset_16_between_rows()
    {
        Flow((model, runtime) =>
        {
            MCPSettingsPage page = null!; IDisposable window = null!;
            runtime.OnMain(() =>
            {
                foreach (var id in new[] { "gitlab", "deepwiki", "github" }) State(model, id, new MCPConnectionState.Connected(2));
                page = new MCPSettingsPage(""); EnvironmentValues.SetAccessibilityReduceMotion(page, true); window = Attach(page, model); Layout(page);
            });
            try
            {
                var section = Nodes<ChromeSection>(page).Single(x => Texts(x)[0] == "Connected"); var card = Assert.Single(Nodes<ChromeCard>(section));
                // :1008-1009 the card is declared lazy at this call site (:40 ChromeCard(lazy: true)).
                Assert.True(card.Lazy);
                // :1021-1025 a filled continuous rounded rectangle of Chrome.cardCornerRadius = 16 at Chrome.overlay(0.03), a fill and not a clip.
                var surface = Assert.IsType<Border>(card.Child); Assert.Equal(new CornerRadius(16), surface.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface));
                Colour(surface.Background, Chrome.PrimaryText.Opacity(0.03), surface); Assert.False(surface.ClipToBounds);
                // :1015 LazyVStack(spacing: 0): every stack between the card and its rows has no spacing.
                var rows = Nodes<StackPanel>(surface).First(stack => stack.Children.Any(child => child is MCPConnectedRow)); Assert.Equal(0, rows.Spacing);
                Assert.All(rows.GetLogicalAncestors().TakeWhile(a => a != card).OfType<StackPanel>(), stack => Assert.Equal(0, stack.Spacing));
                // :41-45 rows in name order with a divider before every row after the first, and none before the first or after the last.
                Assert.Equal(new[] { "MCPConnectedRow", "ChromeRowDivider", "MCPConnectedRow", "ChromeRowDivider", "MCPConnectedRow" }, rows.Children.Select(c => c.GetType().Name));
                Assert.Equal(new[] { "DeepWiki", "GitHub", "GitLab" }, rows.Children.OfType<MCPConnectedRow>().Select(r => Texts(r)[0]));
                // :1006-1019 full width: the surface, the stack and every row span the card; the divider starts 16 in from the leading edge and runs to the trailing one.
                Assert.Equal(section.Bounds.Width, card.Bounds.Width, 0.5); Assert.Equal(card.Bounds.Width, surface.Bounds.Width, 0.5);
                foreach (var child in rows.Children)
                {
                    var divider = child as ChromeRowDivider; Assert.Equal(divider is null ? 0 : 16, child.Bounds.Left, 0.5);
                    Assert.Equal(divider is null ? rows.Bounds.Width : rows.Bounds.Width - 16, child.Bounds.Width, 0.5);
                }
                for (var i = 1; i < rows.Children.Count; i++) Assert.Equal(rows.Children[i - 1].Bounds.Bottom, rows.Children[i].Bounds.Top, 0.5);
                // :1082-1087 the divider's inset is a leading padding of 16.
                Assert.All(rows.Children.OfType<ChromeRowDivider>(), divider => Assert.Equal(new Thickness(16, 0, 0, 0), divider.Margin));
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }

    private static readonly string[] ActionLabels = { "Connect", "Sign in", "Fix it", "Try again", "Sign in again" };

    // The action buttons a card or row shows, by their label (the info and options buttons are named otherwise).
    private static string[] Actions(Control owner) => Nodes<Button>(owner)
        .Select(b => Avalonia.Automation.AutomationProperties.GetName(b) ?? b.Content as string ?? (b.Content is Control c ? Texts(c).FirstOrDefault() : null))
        .Where(title => title is not null && ActionLabels.Contains(title)).Select(title => title!).ToArray();

    private static string[] ActionsOf(AppModel model, string id, MCPConnectionState state, Func<Control> make)
    {
        State(model, id, state); var owner = make(); using var window = Attach(owner, model); return Actions(owner);
    }

    private static readonly MCPSetupGuide Guide = new("Turn it on", "The server is off.", new[] { "Step one" }, "Open page", "", null);
    private static readonly MCPCatalogEntry Plain = Entry("plain-action");
    private static MCPCatalogEntry Keyed(string id) => Entry(id) with { Fields = new[] { new MCPField("TOKEN", "API Key", "Paste", MCPField.Kind.Secret) } };
    private static MCPCatalogEntry OAuth(string id) => Entry(id, new MCPTransport.Oauth("https://example.invalid"));

    // The states a card moves through, paired with the labels each of the five buttons is shown in (MCPSettingsPage.swift:322-349).
    private static IEnumerable<(MCPConnectionState State, string[] Plain, string[] Oauth)> Matrix() => new (MCPConnectionState, string[], string[])[]
    {
        (new MCPConnectionState.NotConnected(), new[] { "Connect" }, new[] { "Sign in" }),
        (new MCPConnectionState.Connecting(), Array.Empty<string>(), Array.Empty<string>()),
        (new MCPConnectionState.Connected(1), Array.Empty<string>(), Array.Empty<string>()),
        (new MCPConnectionState.NeedsSetup(Guide), new[] { "Fix it" }, new[] { "Fix it" }),
        (new MCPConnectionState.Failed("Rejected"), new[] { "Try again" }, new[] { "Sign in again" }),
    };

    [Fact]
    [Trait("Req", "UI-MCP-024")] // App/Views/Settings/MCPSettingsPage.swift:324-330,352-358
    public void Connect_shows_only_while_not_connected_and_connects_at_once_or_opens_the_key_panel()
    {
        OneStep(model =>
        {
            // :324-330 "Connect" only in notConnected and only for a server that is not an OAuth one.
            foreach (var (state, plain, oauth) in Matrix())
            {
                Assert.Equal(plain.Where(label => label == "Connect"), ActionsOf(model, "plain-024", state, () => new MCPServerCard(Entry("plain-024"))).Where(label => label == "Connect"));
                Assert.DoesNotContain("Connect", ActionsOf(model, "oauth-024", state, () => new MCPServerCard(OAuth("oauth-024"))));
            }
            // :352-354 no required input: the press connects at once and the key panel stays shut.
            State(model, "plain-024", new MCPConnectionState.NotConnected()); var card = new MCPServerCard(Entry("plain-024")); using var window = Attach(card, model);
            Click(Button(card, "Connect")); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State("plain-024")); Assert.True(ProbeQueued(model.Mcp, "plain-024")); Assert.False(KeyPanelVisible());
            // :355-357 a required field: the press opens the key panel for this server and connects nothing yet.
            var keyed = Keyed("keyed-024"); Assert.True(keyed.NeedsInput); var keyedCard = new MCPServerCard(keyed); using var second = Attach(keyedCard, model);
            Click(Button(keyedCard, "Connect")); Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State("keyed-024")); Assert.False(ProbeQueued(model.Mcp, "keyed-024"));
            Assert.True(KeyPanelVisible()); Assert.Contains("API Key", Nodes<TextBlock>(Assert.IsAssignableFrom<Control>(PanelWindow(MCPKeyPanel.Shared)!.Content)).Select(t => t.Text));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-025")] // App/Views/Settings/MCPSettingsPage.swift:328,352-358
    public void Sign_in_shows_only_for_an_oauth_server_not_connected_and_signs_in_at_once_unless_a_field_is_required()
    {
        OneStep(model =>
        {
            // The catalog has fourteen OAuth servers (MCPCatalog.swift :30,165,177,241,253,265,277,289,302,314,326,338,350,363); each shows "Sign in".
            Assert.Equal(14, MCPCatalog.Entries.Count(entry => entry.IsOAuth));
            foreach (var (state, plain, oauth) in Matrix())
            {
                Assert.Equal(oauth.Where(label => label == "Sign in"), ActionsOf(model, "oauth-025", state, () => new MCPServerCard(OAuth("oauth-025"))).Where(label => label == "Sign in"));
                Assert.DoesNotContain("Sign in", ActionsOf(model, "plain-025", state, () => new MCPServerCard(Entry("plain-025"))));
            }
            // :335 while it signs in the card says Waiting, not Connecting.
            State(model, "oauth-025", new MCPConnectionState.Connecting()); var waiting = new MCPServerCard(OAuth("oauth-025")); using var wait = Attach(waiting, model); Assert.Contains("Waiting", Texts(waiting)); Assert.DoesNotContain("Connecting", Texts(waiting));
            // :352-354 no required field: the press starts the browser sign-in at once (a queued probe), the key panel stays shut.
            State(model, "oauth-025", new MCPConnectionState.NotConnected()); var card = new MCPServerCard(OAuth("oauth-025")); using var window = Attach(card, model);
            Click(Button(card, "Sign in")); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State("oauth-025")); Assert.True(ProbeQueued(model.Mcp, "oauth-025")); Assert.False(KeyPanelVisible());
            // gitlab's only field is optional, so it too goes straight to the sign-in.
            var gitlab = MCPCatalog.Entry("gitlab")!; Assert.False(gitlab.NeedsInput); Assert.NotEmpty(gitlab.Fields);
            var gitlabCard = new MCPServerCard(gitlab); using var third = Attach(gitlabCard, model); Click(Button(gitlabCard, "Sign in"));
            Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State("gitlab")); Assert.True(ProbeQueued(model.Mcp, "gitlab")); Assert.False(KeyPanelVisible());
            // :355-357 a required field opens the key panel first and signs nothing in.
            var keyed = OAuth("oauth-keyed-025") with { Fields = new[] { new MCPField("TOKEN", "API Key", "Paste", MCPField.Kind.Secret) } }; var keyedCard = new MCPServerCard(keyed); using var fourth = Attach(keyedCard, model);
            Click(Button(keyedCard, "Sign in")); Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State(keyed.Id)); Assert.False(ProbeQueued(model.Mcp, keyed.Id)); Assert.True(KeyPanelVisible());
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-028")] // App/Views/Settings/MCPSettingsPage.swift:341-344
    public void Fix_it_shows_only_while_setup_is_needed_and_presents_the_setup_panel_once_for_the_entry_and_guide()
    {
        OneStep(model =>
        {
            foreach (var (state, plain, oauth) in Matrix())
            {
                Assert.Equal(plain.Where(label => label == "Fix it"), ActionsOf(model, "plain-028", state, () => new MCPServerCard(Entry("plain-028"))).Where(label => label == "Fix it"));
                Assert.Equal(oauth.Where(label => label == "Fix it"), ActionsOf(model, "oauth-028", state, () => new MCPServerCard(OAuth("oauth-028"))).Where(label => label == "Fix it"));
            }
            State(model, "plain-028", new MCPConnectionState.NeedsSetup(Guide)); var entry = Entry("plain-028"); var card = new MCPServerCard(entry); using var window = Attach(card, model);
            // :341-343 the prominent glass button tinted with the theme accent, where the other buttons are untinted glass.
            var fix = Button(card, "Fix it"); var effect = Nodes<Border>(fix).Select(SwiftGlass.GetEffect).First(e => e is not null)!; Assert.True(effect.IsInteractive); Assert.Equal(Chrome.Accent, effect.TintColor);
            // :343 a press presents the panel for this entry and this guide ...
            Assert.False(MCPSetupPanel.Shared.IsPresenting(entry)); Click(fix); Assert.True(MCPSetupPanel.Shared.IsPresenting(entry));
            var panel = PanelWindow(MCPSetupPanel.Shared); Assert.NotNull(panel); Assert.Contains("Turn it on", Nodes<TextBlock>(Assert.IsAssignableFrom<Control>(panel!.Content)).Select(t => t.Text));
            // ... and the panel is idempotent: a second press refocuses the same window.
            Click(fix); Assert.True(MCPSetupPanel.Shared.IsPresenting(entry)); Assert.Same(panel, PanelWindow(MCPSetupPanel.Shared));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-029")] // App/Views/Settings/MCPSettingsPage.swift:345-348,352-358
    public void Try_again_shows_only_for_a_failed_plain_server_and_reconnects_with_the_stored_values()
    {
        OneStep(model =>
        {
            foreach (var (state, plain, oauth) in Matrix())
            {
                Assert.Equal(plain.Where(label => label == "Try again"), ActionsOf(model, "plain-029", state, () => new MCPServerCard(Entry("plain-029"))).Where(label => label == "Try again"));
                Assert.DoesNotContain("Try again", ActionsOf(model, "oauth-029", state, () => new MCPServerCard(OAuth("oauth-029"))));
            }
            // :348 a press connects again, keeping the values the server was set up with.
            Connection(model, new MCPConnection("plain-029") { Values = new ValueMap<string, string>(new Dictionary<string, string> { ["MODE"] = "keep" }), IsEnabled = false }); Assert.True(MCPKeychain.Set("secret-token", "plain-029", "TOKEN"));
            State(model, "plain-029", new MCPConnectionState.Failed("Rejected")); var card = new MCPServerCard(Entry("plain-029")); using var window = Attach(card, model);
            Click(Button(card, "Try again"));
            Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State("plain-029")); Assert.True(ProbeQueued(model.Mcp, "plain-029")); Assert.False(KeyPanelVisible());
            Assert.Equal("keep", model.Mcp.Connection("plain-029")!.Values["MODE"]); Assert.True(model.Mcp.Connection("plain-029")!.IsEnabled); Assert.Equal("secret-token", MCPKeychain.Value("plain-029", "TOKEN"));
            // :352-357 a server that has a required field goes back through the key panel, which keeps what was stored.
            var keyed = Keyed("keyed-029"); State(model, keyed.Id, new MCPConnectionState.Failed("Rejected")); var keyedCard = new MCPServerCard(keyed); using var second = Attach(keyedCard, model);
            Click(Button(keyedCard, "Try again")); Assert.True(KeyPanelVisible()); Assert.IsType<MCPConnectionState.Failed>(model.Mcp.State(keyed.Id)); Assert.False(ProbeQueued(model.Mcp, keyed.Id));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-030")] // App/Views/Settings/MCPSettingsPage.swift:346; Services/MCP/MCPStore.swift:91-96
    public void Sign_in_again_shows_only_for_a_failed_oauth_server_and_signs_it_out_before_the_browser_flow()
    {
        OneStep(model =>
        {
            foreach (var (state, plain, oauth) in Matrix())
            {
                Assert.Equal(oauth.Where(label => label == "Sign in again"), ActionsOf(model, "oauth-030", state, () => new MCPServerCard(OAuth("oauth-030"))).Where(label => label == "Sign in again"));
                Assert.DoesNotContain("Sign in again", ActionsOf(model, "plain-030", state, () => new MCPServerCard(Entry("plain-030"))));
            }
            // The rejected token and the registered client are what a sign-out deletes (MCPOAuth.swift :189-192).
            void Seed(string id) { Assert.True(MCPKeychain.Set("rejected-session", id, "oauth")); Assert.True(MCPKeychain.Set("registered-client", id, "oauth-client")); }
            // Sign in (never failed) keeps whatever session exists: only the retry after a rejection signs out.
            Seed("oauth-030"); State(model, "oauth-030", new MCPConnectionState.NotConnected()); var fresh = new MCPServerCard(OAuth("oauth-030")); using var first = Attach(fresh, model);
            Click(Button(fresh, "Sign in")); Assert.Equal("rejected-session", MCPKeychain.Value("oauth-030", "oauth")); Assert.Equal("registered-client", MCPKeychain.Value("oauth-030", "oauth-client"));
            // Sign in again signs out first: both items are gone the moment it is pressed, and the flow it queues starts from nothing.
            Seed("oauth-030b"); State(model, "oauth-030b", new MCPConnectionState.Failed("The token was rejected")); var card = new MCPServerCard(OAuth("oauth-030b")); using var window = Attach(card, model);
            Click(Button(card, "Sign in again"));
            Assert.Null(MCPKeychain.Value("oauth-030b", "oauth")); Assert.Null(MCPKeychain.Value("oauth-030b", "oauth-client"));
            Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State("oauth-030b")); Assert.True(ProbeQueued(model.Mcp, "oauth-030b")); Assert.False(KeyPanelVisible());
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-037")] // App/Views/Settings/MCPSettingsPage.swift:503-507,534-540
    public void Custom_row_connect_shows_only_while_not_connected_and_connects_at_once_without_variables_or_opens_the_key_panel()
    {
        OneStep(model =>
        {
            var plain = Custom(); var keyed = Custom(1);
            // :503-507 "Connect" only in notConnected; a custom is never an OAuth entry, so there is no "Sign in" in any state.
            foreach (var (state, expected, _) in Matrix())
            {
                Assert.Equal(expected.Where(label => label == "Connect"), ActionsOf(model, plain.Id, state, () => new MCPCustomRow(plain)).Where(label => label == "Connect"));
                Assert.DoesNotContain("Sign in", ActionsOf(model, plain.Id, state, () => new MCPCustomRow(plain)));
            }
            // :534-536 a server that declares no variables connects at once.
            State(model, plain.Id, new MCPConnectionState.NotConnected()); var row = new MCPCustomRow(plain); using var window = Attach(row, model);
            Click(Button(row, "Connect")); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State(plain.Id)); Assert.True(ProbeQueued(model.Mcp, plain.Id)); Assert.False(KeyPanelVisible());
            // :537-539 one with variables opens the key panel and connects nothing yet.
            var keyedEntry = keyed.CatalogEntry(); Assert.True(keyedEntry.NeedsInput); var keyedRow = new MCPCustomRow(keyed with { Id = "custom-keyed-037" }); using var second = Attach(keyedRow, model);
            Click(Button(keyedRow, "Connect")); Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State("custom-keyed-037")); Assert.False(ProbeQueued(model.Mcp, "custom-keyed-037")); Assert.True(KeyPanelVisible());
        });
    }
}
