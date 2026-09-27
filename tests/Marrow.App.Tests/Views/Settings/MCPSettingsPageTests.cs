using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :1-1141.
// STOP App.Views.Settings.MCPSettingsPage #1
// Kind: missing dependency
// Swift: App/Views/Settings/MCPSettingsPage.swift:64-74,284-290; records PR-MCP-203, PR-THEME-088, PR-MCP-205.
// Missing: MCPStore mutates statesField/connectionsField/customsField in place without Set/Notify; State(id)
// does not Read either. Track/OnChange cannot observe a connect, refresh, enabled change, save or deletion.
// Needed: MCPStore owner must publish mutations through observation. The automatic effects are left unwritten.
// Static snapshots, action dispatch and local view state can be tested independently below.
// STOP App.Views.Settings.MCPSettingsPage #2
// Kind: missing dependency
// Swift: MCPStore.swift:118,186-193; records PR-MCP-073 (credential deletion failure only).
// Missing: Disconnect ignores MCPKeychain.DeleteAll's deletion failures; no error is returned for this view to show.
// Needed: service owner must implement OD-0008 A12 / PR-MCP-228. Confirmation and successful deletion are tested.

// STOP App.Views.Settings.MCPSettingsPage #3
// Kind: conflict; Swift: MCPSettingsPage.swift:77-85; record PR-MCP-202.
// Packet requires accent-insensitive search, while R-STR-6 / ED-009 requires accent-sensitive secondary-strength search.
// Needed: orchestrator must reconcile PR-MCP-202 with ED-009; the accent expectation stays red, unchanged.
[Collection("Headless")]
public sealed class MCPSettingsPageTests(HeadlessSession headless)
{
    private void Ui(Action<AppModel, TestRuntime> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var harness = new AppModelHarness(withRuntime: false);
        var model = new AppModel();
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        runtime.OnMain(() => test(model, runtime));
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "PR-MCP-202")] // App/Views/Settings/MCPSettingsPage.swift:77-85
    public void Search_trims_and_matches_each_of_the_five_fields_ignoring_case_and_accents()
    {
        var entry = Entry() with { Name = "Caf\U000000E9", Vendor = "Vendor", Summary = "Summary", SampleTools = new[] { "query_tool" } }; // U+00E9 LATIN SMALL LETTER E WITH ACUTE
        foreach (var query in new[] { " \n ", " CAFE\n", "VENDOR", "SUMMARY", "OFFLINE", "QUERY_TOOL" }) Assert.True(MCPSettingsPage.Matches(entry, query));
        Assert.False(MCPSettingsPage.Matches(entry, "absent"));
    }

    [Fact]
    [Trait("Req", "PR-MCP-199")] // App/Views/Settings/MCPSettingsPage.swift:35-63
    [Trait("Req", "UI-MCP-001")] // App/Views/Settings/MCPSettingsPage.swift:14-17,35-63
    public void Page_has_lead_custom_categories_footer_in_order_with_twenty_spacing()
    {
        Ui((model, _) => { var page = new MCPSettingsPage(""); using var window = Attach(page, model);
            var stack = Assert.IsType<StackPanel>(page.Child); Assert.Equal(20, stack.Spacing);
            Assert.IsType<MCPLeadCard>(stack.Children[0]); Assert.IsType<MCPCustomSection>(stack.Children[1]);
            Assert.Equal("Connected servers reach every provider when a thread starts: the CLIs at launch, the API models through Marrow itself.", Assert.IsType<TextBlock>(stack.Children[^1]).Text);
            Assert.Equal(new[] { "Developer", "Browser", "Search", "Work", "Data", "Cloud", "Knowledge" }, Nodes<ChromeSection>(page).Skip(1).Select(x => Texts(x)[0]));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-200")] // App/Views/Settings/MCPSettingsPage.swift:19-23,38-47
    public void Connected_uses_catalog_only_in_name_order_and_rows_have_intervening_dividers()
    {
        Ui((model, _) => { State(model, "gitlab", new MCPConnectionState.Connected(2)); State(model, "github", new MCPConnectionState.Connected(3)); State(model, "custom-example", new MCPConnectionState.Connected(1));
            var page = new MCPSettingsPage("absent"); using var window = Attach(page, model);
            var rows = Nodes<MCPConnectedRow>(page); Assert.Equal(2, rows.Length);
            Assert.Equal(new[] { "GitHub", "GitLab" }, rows.Select(x => Texts(x)[0])); Assert.Single(Nodes<ChromeRowDivider>(page));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-201")] // App/Views/Settings/MCPSettingsPage.swift:25-33
    public void Category_search_omits_connected_entries_and_drops_empty_groups()
    {
        Ui((model, _) => { State(model, "github", new MCPConnectionState.Connected(1)); var page = new MCPSettingsPage("github"); using var window = Attach(page, model); Assert.Empty(Nodes<MCPServerCard>(page));
            State(model, "github", new MCPConnectionState.NotConnected()); var empty = new MCPSettingsPage("no match anywhere"); using var second = Attach(empty, model); Assert.Empty(Nodes<ChromeSection>(empty));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-206")] // App/Views/Settings/MCPSettingsPage.swift:387-415
    public void Custom_shows_empty_note_and_add_row_without_search_and_hides_on_no_match()
    {
        Ui((model, _) => { var section = new MCPCustomSection(""); using var window = Attach(section, model);
            Assert.Contains("Servers of your own: a command that runs on this PC, or a remote address, with the variables they need.", Texts(section)); Assert.NotNull(Button(section, "Add server"));
            var absent = new MCPCustomSection("absent"); using var second = Attach(absent, model); Assert.Null(absent.Child);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-204")] // App/Views/Settings/MCPSettingsPage.swift:352-358,534-540
    public void No_input_action_connects_immediately_without_running_the_queued_probe()
    {
        Ui((model, _) => { var entry = Entry(); var card = new MCPServerCard(entry); using var window = Attach(card, model);
            Click(Button(card, "Connect")); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State(entry.Id)); model.Mcp.Disconnect(entry);
            var custom = Custom(); var row = new MCPCustomRow(custom); using var second = Attach(row, model);
            Click(Button(row, "Connect")); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State(custom.Id)); model.Mcp.Disconnect(custom.CatalogEntry());
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-073")] // App/Views/Settings/MCPSettingsPage.swift:564-566
    public void Delete_confirmation_preserves_server_until_the_destructive_button_is_invoked()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
            using var scope = Presentation.Presenter.Override(new RecordingPresenter()); var model = runtime.OnMain(() => new AppModel()); var server = Custom();
            runtime.OnMain(() => Assert.Null(model.Mcp.SaveCustom(server))); var row = new MCPCustomRow(server); using var window = runtime.OnMain(() => Attach(row, model));
            runtime.OnMain(() => Call(row, "Delete")); Assert.Single(model.Mcp.Customs); var confirmation = Read<Control>(row, "DeleteConfirmation");
            Assert.Contains("Delete this server?", Texts(confirmation));
            Assert.Contains("\U0000201CExample\U0000201D and the values it keeps in Credential Manager will be removed.", Texts(confirmation)); // U+201C/201D DOUBLE QUOTATION MARKS
            runtime.OnMain(() => Click(Button(confirmation, "Delete server"))); Assert.Empty(model.Mcp.Customs);
            runtime.OnMain(() => { model.Mcp.SaveCustom(server); model.Settings.ConfirmBeforeDeleting = false; Call(row, "Delete"); }); Assert.Empty(model.Mcp.Customs);
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-207")] // App/Views/Settings/MCPSettingsPage.swift:845-862
    public void Draft_trims_keys_command_url_drops_blank_keys_and_preserves_argument_lines_and_rows()
    {
        Ui((model, _) => { var editor = new MCPCustomEditor(Custom(2)); using var window = Attach(editor, model);
            Field(editor, "KEY").Text = "  TOKEN  "; Nodes<TextBox>(editor).Where(x => x.PlaceholderText == "KEY").Last().Text = " \t ";
            Field(editor, "npx").Text = " \n node \n "; Nodes<TextBox>(editor).Single(x => x.AcceptsReturn).Text = " -y \n \t \nargument with spaces\n\"quoted\" ";
            var draft = Read<MCPCustomServer>(editor, "Draft"); Assert.Equal("node", draft.Command); Assert.Equal(new[] { " -y ", "argument with spaces", "\"quoted\" " }, draft.Args); Assert.Equal("TOKEN", Assert.Single(draft.Env).Key);
            Call(editor, "SetKind", MCPCustomServer.Kind.Http); Field(editor, "https://\U00002026").Text = "\n https://example.invalid/mcp \n"; // U+2026 HORIZONTAL ELLIPSIS
            draft = Read<MCPCustomServer>(editor, "Draft"); Assert.Equal("https://example.invalid/mcp", draft.Url); Assert.Equal("TOKEN", Assert.Single(draft.Headers).Key); Assert.Empty(draft.Env);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-208")] // App/Views/Settings/MCPSettingsPage.swift:864-889
    public void Save_keeps_editor_on_validation_failure_then_saves_definition_and_values_and_dismisses()
    {
        Ui((model, _) => { var editor = new MCPCustomEditor(null); using var window = Attach(editor, model); var closed = false; Popovers.SetClosePopover(editor, () => closed = true);
            Click(Button(editor, "Add server")); Assert.False(closed); Assert.Empty(model.Mcp.Customs); Assert.Contains("The server needs a name.", Texts(editor));
            Field(editor, "My server").Text = "Example"; Field(editor, "npx").Text = "offline-fixture"; Click(Button(editor, "Add environment variable"));
            Field(editor, "KEY").Text = "MODE"; Field(editor, "value").Text = "sample-mode"; Click(Button(editor, "Add server"));
            Assert.True(closed); Assert.Equal("Example", Assert.Single(model.Mcp.Customs).Name); Assert.Equal("sample-mode", model.Mcp.StoredValue("custom-example", "MODE", false));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-209")] // App/Views/Settings/MCPSettingsPage.swift:614-622,663-676
    public void Edit_prefills_values_on_appear_and_add_focuses_name()
    {
        Ui((model, _) => { var server = Custom(1); model.Mcp.SaveCustom(server); model.Mcp.SaveCustomValues(new[] { ("KEY0", "sample-mode", false) }, server.Id, null);
            var edit = new MCPCustomEditor(server); Assert.Equal("", Field(edit, "value").Text); using var window = Attach(edit, model); Assert.Equal("sample-mode", Field(edit, "value").Text); Assert.False(Field(edit, "My server").IsFocused);
            var add = new MCPCustomEditor(null); using var second = Attach(add, model); Assert.True(Field(add, "My server").IsFocused);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-210")] // App/Views/Settings/MCPSettingsPage.swift:794-840
    public void Lock_switches_secure_field_and_keeps_the_typed_value_until_save()
    {
        Ui((model, _) => { var editor = new MCPCustomEditor(Custom(1)); using var window = Attach(editor, model); var value = Field(editor, "value"); value.Text = "sample-mode";
            var toggle = Nodes<Button>(editor).Single(x => ToolTip.GetTip(x) as string == "Stored in mcp.json; click to keep it in the Credential Manager instead"); Click(toggle);
            Assert.NotEqual(default(char), Field(editor, "value").PasswordChar); Assert.Equal("sample-mode", Field(editor, "value").Text);
            Assert.Contains(Nodes<Button>(editor), x => ToolTip.GetTip(x) as string == "Kept in the Credential Manager"); Assert.Null(model.Mcp.StoredValue("custom-example", "KEY0", true));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-211")] // App/Views/Settings/MCPSettingsPage.swift:914-963
    public void Hover_waits_220_to_open_and_320_to_close_and_popover_hover_cancels_close()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
            using var scope = Presentation.Presenter.Override(new RecordingPresenter());
            var model = runtime.OnMain(() => new AppModel()); var info = new MCPHoverInfoButton("About", () => new Border());
            using var window = runtime.OnMain(() => Attach(info, model)); var button = Assert.Single(Nodes<Button>(info));
            runtime.OnMain(() => button.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, button, new Pointer(1, PointerType.Mouse, true), button, default, 0, default, default)));
            runtime.AdvanceMilliseconds(219); Assert.False(Read<bool>(info, "IsPresented")); runtime.AdvanceMilliseconds(1); Assert.True(Read<bool>(info, "IsPresented"));
            runtime.OnMain(() => button.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, button, new Pointer(1, PointerType.Mouse, true), button, default, 0, default, default)));
            runtime.AdvanceMilliseconds(319); Assert.True(Read<bool>(info, "IsPresented")); runtime.AdvanceMilliseconds(1); Assert.False(Read<bool>(info, "IsPresented"));
            runtime.OnMain(() => Click(button)); Assert.True(Read<bool>(info, "IsPresented")); runtime.OnMain(() => Click(button)); Assert.False(Read<bool>(info, "IsPresented"));
            Dispatcher.UIThread.RunJobs();
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-212")] // App/Views/Settings/MCPSettingsPage.swift:1039-1064
    public void Information_describes_all_transports_and_required_optional_and_empty_needs()
    {
        Ui((model, _) => { var stdio = new MCPInfoPopover(Entry(transport: new MCPTransport.Stdio("npx", new[] { "-y", "package" }))); using var window = Attach(stdio, model);
            Assert.Contains("Runs on this PC through npx (package).", Texts(stdio)); Assert.Contains("Nothing \U00002014 press Connect.", Texts(stdio)); // U+2014 EM DASH
            var http = new MCPInfoPopover(Entry(transport: new MCPTransport.Http("https://example.invalid/mcp", ValueMap<string, string>.Empty))); Assert.Contains("Remote server at example.invalid.", Texts(http));
            var oauth = new MCPInfoPopover(Entry(transport: new MCPTransport.Oauth("https://{host}/mcp")) with { Fields = new[] { new MCPField("host", "Server Host", "", MCPField.Kind.Text, DefaultValue: "example.invalid", IsRequired: false), new MCPField("token", "Access Token", "") } });
            Assert.Contains("Remote server at example.invalid. You sign in once in your browser.", Texts(oauth)); Assert.Contains("Access Token; optional: server host.", Texts(oauth));
            Assert.Contains("Only your sign-in.", Texts(new MCPInfoPopover(Entry(transport: new MCPTransport.Oauth("https://example.invalid")))));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-213")] // App/Views/Settings/MCPSettingsPage.swift:1002-1020
    public void Optional_link_uses_first_optional_label_and_empty_docs_has_no_link()
    {
        Ui((model, _) => { var info = new MCPInfoPopover(Entry() with { Fields = new[] { new MCPField("key", "API Key", "", IsRequired: false), new MCPField("second", "Other", "", IsRequired: false) } });
            Assert.NotNull(Button(info, "Change the api key\U00002026")); Assert.DoesNotContain("Read the docs", Texts(info)); // U+2026 HORIZONTAL ELLIPSIS
            Assert.Contains("Read the docs", Texts(new MCPInfoPopover(Entry() with { DocsURL = "https://example.invalid/docs" })));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-224")] // App/Views/Settings/MCPSettingsPage.swift:249-303,360-375
    public void Celebration_replaces_normal_content_with_check_and_tool_count()
    {
        Ui((model, _) => { State(model, "offline", new MCPConnectionState.Connected(7)); var card = new MCPServerCard(Entry(), true); EnvironmentValues.SetAccessibilityReduceMotion(card, true); using var window = Attach(card, model);
            Assert.Contains("Connected \U000000B7 7 tools", Texts(card)); Assert.DoesNotContain("Summary", Texts(card)); Assert.Empty(Nodes<MCPInfoButton>(card)); // U+00B7 MIDDLE DOT
            var glyph = Assert.Single(Nodes<SwiftImage>(card)); Assert.Equal("checkmark.circle.fill", glyph.SymbolName); Assert.Equal(34, SwiftImage.GetFont(glyph)!.Resolve().FontSize); Assert.Equal(0.5, Assert.IsType<ScaleTransform>(glyph.RenderTransform).ScaleX);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-229")] // App/Views/Settings/MCPSettingsPage.swift:624-627,679-685
    public void Editor_height_counts_empty_rows_changes_base_and_caps_at_560()
    {
        Ui((model, _) => { foreach (var (kind, rows, height) in new[] { (MCPCustomServer.Kind.Stdio, 0, 450), (MCPCustomServer.Kind.Stdio, 3, 552), (MCPCustomServer.Kind.Stdio, 4, 560), (MCPCustomServer.Kind.Http, 0, 320), (MCPCustomServer.Kind.Http, 7, 558), (MCPCustomServer.Kind.Http, 8, 560) }) { var editor = new MCPCustomEditor(Custom(rows, kind)); Assert.Equal(380, editor.Width); Assert.Equal(height, editor.Height); }
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-230")] // App/Views/Settings/MCPSettingsPage.swift:629-658,687-704
    public void Header_and_footer_are_outside_the_scrollable_form()
    {
        Ui((model, _) => { var editor = new MCPCustomEditor(null); var scroll = Assert.Single(Nodes<ScrollViewer>(editor)); Assert.DoesNotContain(Button(editor, "Add server"), Nodes<Button>(Assert.IsAssignableFrom<Control>(scroll.Content))); Assert.Equal(12, Assert.Single(Nodes<Grid>(editor), x => x.RowDefinitions.Count == 3).RowSpacing); });
    }

    [Fact]
    [Trait("Req", "PR-MCP-231")] // App/Views/Settings/MCPSettingsPage.swift:718-728
    public void Close_has_24_square_hit_target_and_discards_unsaved_draft()
    {
        Ui((model, _) => { var editor = new MCPCustomEditor(null); var closed = false; Popovers.SetClosePopover(editor, () => closed = true); var close = Nodes<Button>(editor).Single(x => AutomationProperties.GetName(x) == "Close"); Assert.Equal(24, close.Width); Assert.Equal(24, close.Height); Click(close); Assert.True(closed); Assert.Empty(model.Mcp.Customs); });
    }

    [Fact]
    [Trait("Req", "UI-MCP-002")] // App/Views/Settings/MCPSettingsPage.swift:88-122
    [Trait("Req", "UI-MCP-003")] // App/Views/Settings/MCPSettingsPage.swift:91-93
    public void Lead_card_uses_exact_copy_padding_radius_and_shield_size()
    {
        Ui((model, _) => { var lead = new MCPLeadCard(); var border = Assert.IsType<Border>(lead.Child); Assert.Equal(new Thickness(16), border.Padding); Assert.Equal(new CornerRadius(14), border.CornerRadius); Assert.Contains("Connected servers lead", Texts(lead)); Assert.Contains("Connect a server here and every thread can use it. Anything set up in a terminal or another app stays just as it is.", Texts(lead)); var shield = Nodes<SwiftImage>(lead).Single(x => x.SymbolName == "checkmark.shield.fill"); Assert.Equal(22, SwiftImage.GetFont(shield)!.Resolve().FontSize); });
    }
}





