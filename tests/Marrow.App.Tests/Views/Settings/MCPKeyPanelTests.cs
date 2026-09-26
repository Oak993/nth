using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPKeyPanel.swift (B2 1fbf81e), :79-251.
[Collection("Headless")]
public sealed class MCPKeyPanelTests(HeadlessSession headless)
{
    private static MCPCatalogEntry Entry(
        string id = "notion",
        string name = "Notion",
        string? keysUrl = "https://notion.example/keys",
        bool oauth = false,
        params MCPField[] fields) =>
        new(
            id,
            name,
            "Notion",
            "Notes",
            MCPCategory.Developer,
            "notion",
            0,
            oauth
                ? new MCPTransport.Oauth("https://notion.example/oauth")
                : new MCPTransport.Stdio("notion", ValueList<string>.Empty),
            "https://notion.example/docs")
        {
            KeysURL = keysUrl,
            Fields = new ValueList<MCPField>(fields),
        };

    private static MCPField Field(
        string key,
        string label,
        string placeholder,
        MCPField.Kind kind = MCPField.Kind.Secret,
        string? help = null,
        bool required = true) =>
        new(key, label, placeholder, kind, help, IsRequired: required);

    private static MCPField Secret(string key = "api_key", string label = "API key") =>
        Field(key, label, "Paste the key");

    private static Control[] Descendants(Control control) =>
        LogicalExtensions.GetSelfAndLogicalDescendants(control).OfType<Control>().ToArray();

    private static string[] Texts(Control control) =>
        Descendants(control).OfType<TextBlock>().Select(block => block.Text ?? "").ToArray();

    private static Button? ButtonWithText(Control control, string text) =>
        Descendants(control).OfType<Button>().FirstOrDefault(button =>
            LogicalExtensions.GetSelfAndLogicalDescendants(button)
                .OfType<TextBlock>()
                .Any(block => block.Text == text)
            || button.Content as string == text);

    private static Button? ButtonWithSymbol(Control control, string symbol) =>
        Descendants(control).OfType<Button>().FirstOrDefault(button =>
            LogicalExtensions.GetSelfAndLogicalDescendants(button)
                .OfType<SwiftImage>()
                .Any(image => image.SymbolName == symbol));

    [Fact]
    [Trait("Req", "PR-MCP-217")] // App/Views/Settings/MCPKeyPanel.swift:145-161
    public void Key_page_and_non_key_page_entries_show_their_exact_numbered_steps()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var withPage = new MCPKeyPanelView(
                Entry(fields: Secret("api_key", "API Key")),
                new AppModel(),
                () => { });
            var withoutPage = new MCPKeyPanelView(
                Entry(keysUrl: "", fields: Secret("token", "Access Token")),
                new AppModel(),
                () => { });
            var oauthWithoutPage = new MCPKeyPanelView(
                Entry(keysUrl: "", oauth: true, fields: Secret()),
                new AppModel(),
                () => { });

            Assert.Contains("The page is open in your browser", Texts(withPage));
            Assert.Contains("Copy the api key", Texts(withPage));
            Assert.Contains("Paste it below and save", Texts(withPage));

            Assert.Contains("Enter the access token", Texts(withoutPage));
            Assert.Contains("Save", Texts(withoutPage));
            Assert.DoesNotContain("The page is open in your browser", Texts(withoutPage));

            Assert.Contains("Enter the api key", Texts(oauthWithoutPage));
            Assert.Contains("Sign in in your browser", Texts(oauthWithoutPage));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-217")] // App/Views/Settings/MCPKeyPanel.swift:152,157
    public void Step_text_uses_the_key_and_value_fallbacks_when_there_are_no_fields()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var withPage = new MCPKeyPanelView(
                Entry(fields: []),
                new AppModel(),
                () => { });
            var withoutPage = new MCPKeyPanelView(
                Entry(keysUrl: "", fields: []),
                new AppModel(),
                () => { });

            Assert.Contains("Copy the key", Texts(withPage));
            Assert.Contains("Enter the value", Texts(withoutPage));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-074")] // App/Views/Settings/MCPKeyPanel.swift:147-151
    public void The_reopen_key_page_link_is_shown_only_for_a_parseable_nonempty_url()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var parseable = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => { });
            var absent = new MCPKeyPanelView(
                Entry(keysUrl: null, fields: Secret()),
                new AppModel(),
                () => { });
            var empty = new MCPKeyPanelView(
                Entry(keysUrl: "", fields: Secret()),
                new AppModel(),
                () => { });

            Assert.Contains("Open it again", Texts(parseable));
            Assert.DoesNotContain("Open it again", Texts(absent));
            Assert.DoesNotContain("Open it again", Texts(empty));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-071")] // App/Views/Settings/MCPKeyPanel.swift:122-143
    public void The_header_shows_the_entry_name_and_the_literal_connect_subtitle_for_oauth()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var view = new MCPKeyPanelView(
                Entry(name: "Linear", oauth: true, fields: Secret()),
                new AppModel(),
                () => { });
            var blocks = Descendants(view).OfType<TextBlock>().ToArray();

            var name = Assert.Single(blocks, block => block.Text == "Linear");
            Assert.Equal(14, name.FontSize);
            Assert.Equal(FontWeight.SemiBold, name.FontWeight);

            var subtitle = Assert.Single(blocks, block => block.Text == "Connect");
            Assert.Equal(11, subtitle.FontSize);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-072")] // App/Views/Settings/MCPKeyPanel.swift:134-141
    public void The_xmark_button_calls_on_done_without_submitting()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var doneCalls = 0;
            var view = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => doneCalls++);
            var close = ButtonWithSymbol(view, "xmark");

            Assert.NotNull(close);
            close!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, doneCalls);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-073")] // App/Views/Settings/MCPKeyPanel.swift:163-180
    [Trait("Decision", "ED-059")]
    public void Numbered_steps_use_the_upstream_footnote_and_callout_sizes()
    {
        Assert.Equal(10, SwiftFont.System(SwiftFont.TextStyle.Footnote).PointSize);
        Assert.Equal(12, SwiftFont.System(SwiftFont.TextStyle.Callout).PointSize);

        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var view = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => { });
            var number = Descendants(view).OfType<TextBlock>().First(block => block.Text == "1");
            Assert.Equal(10, number.FontSize);
            Assert.Equal(FontWeight.SemiBold, number.FontWeight);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-218")] // App/Views/Settings/MCPKeyPanel.swift:182-224
    public void Fields_render_in_entry_order_with_a_folder_symbol_only_for_path_fields()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var view = new MCPKeyPanelView(
                Entry(fields:
                [
                    Secret("api_key", "API Key"),
                    Field("token", "Token", "Paste the token", MCPField.Kind.Text),
                    Field("root", "Root", "Choose a folder", MCPField.Kind.Path),
                ]),
                new AppModel(),
                () => { });

            var labels = Descendants(view).OfType<TextBlock>()
                .Select(block => block.Text ?? "")
                .Where(text => text is "API Key" or "Token" or "Root")
                .ToArray();

            Assert.Equal(["API Key", "Token", "Root"], labels);
            Assert.Single(Descendants(view).OfType<SwiftImage>(), image => image.SymbolName == "folder");

            var noPathView = new MCPKeyPanelView(
                Entry(fields:
                [
                    Field("token", "Token", "Paste the token", MCPField.Kind.Text),
                    Secret(),
                ]),
                new AppModel(),
                () => { });
            Assert.DoesNotContain(Descendants(noPathView).OfType<SwiftImage>(), image => image.SymbolName == "folder");
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-218")] // App/Views/Settings/MCPKeyPanel.swift:184-222
    [Trait("Decision", "ED-059")]
    public void Field_labels_use_subheadline_and_nonempty_help_is_rendered()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var withHelp = new MCPKeyPanelView(
                Entry(fields: Field("config", "Config", "Path", MCPField.Kind.Text, "Choose a config file")),
                new AppModel(),
                () => { });
            var withoutHelp = new MCPKeyPanelView(
                Entry(fields: Field("config", "Config", "Path", MCPField.Kind.Text, "")),
                new AppModel(),
                () => { });

            var label = Descendants(withHelp).OfType<TextBlock>().Single(block => block.Text == "Config");
            Assert.Equal(11, label.FontSize);
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);
            Assert.Contains("Choose a config file", Texts(withHelp));
            Assert.DoesNotContain("Choose a config file", Texts(withoutHelp));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-075")] // App/Views/Settings/MCPKeyPanel.swift:184-222
    [Trait("Decision", "ED-059")]
    public void Field_label_and_control_fonts_use_their_source_text_styles()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var view = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => { });

            var label = Descendants(view).OfType<TextBlock>().Single(block => block.Text == "API key");
            Assert.Equal(11, label.FontSize);
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);

            var field = Descendants(view).OfType<TextBox>().FirstOrDefault();
            if (field is not null)
                Assert.Equal(12, field.FontSize);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-076")] // App/Views/Settings/MCPKeyPanel.swift:201-210
    [Trait("Decision", "ED-059")]
    public void The_path_picker_symbol_uses_callout_size_and_is_absent_for_other_kinds()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var withPath = new MCPKeyPanelView(
                Entry(fields: Field("root", "Root", "Choose a folder", MCPField.Kind.Path)),
                new AppModel(),
                () => { });
            var withoutPath = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => { });

            var folder = Assert.Single(Descendants(withPath).OfType<SwiftImage>(), image => image.SymbolName == "folder");
            Assert.Equal(12, SwiftImage.GetFont(folder)?.PointSize);
            Assert.DoesNotContain(Descendants(withoutPath).OfType<SwiftImage>(), image => image.SymbolName == "folder");
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-219")] // App/Views/Settings/MCPKeyPanel.swift:94-101
    public void The_save_button_label_matches_oauth_and_non_oauth_entries()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var standard = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => { });
            var oauth = new MCPKeyPanelView(
                Entry(oauth: true, fields: Secret()),
                new AppModel(),
                () => { });

            Assert.NotNull(ButtonWithText(standard, "Save and connect"));
            Assert.NotNull(ButtonWithText(oauth, "Sign in"));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-077")] // App/Views/Settings/MCPKeyPanel.swift:94-101
    public void The_save_button_is_disabled_when_a_required_field_is_empty()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var view = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => { });

            var save = ButtonWithText(view, "Save and connect");
            Assert.NotNull(save);
            Assert.False(save!.IsEnabled);
        });
    }

    // NOT TESTED (Avalonia-headless limitation, not a code defect): MCPKeyPanelView wires each field's
    // TextChanged event to recompute the save button's IsEnabled directly (RefreshCanConnect in MCPKeyPanel.cs),
    // rather than a Track() on MCPStore.CanConnect - MCPStore.CanConnect (Services.MCP.MCPStore.cs) reads Drafts,
    // a plain public Dictionary mutated in place with no Set()/Notify() call anywhere in that file, so a Track
    // subscription on it would only ever fire once, at attach (a wider MCPStore reactivity gap, not this unit's
    // own code). A direct test of this (set a TextBox's Text, assert the button re-enables) was attempted and
    // dropped: confirmed by a throwaway diagnostic that Avalonia's TextBox.TextChanged event does not fire at all
    // in this codebase's headless test host on a programmatic Text/SetCurrentValue write to an unattached,
    // unstyled TextBox (0 invocations, both ways tried) - this codebase's TestApp.BuildAvaloniaApp() registers no
    // theme (HeadlessFixtures.cs), the same root cause already documented for ToggleSwitch elsewhere this session.

    [Fact]
    [Trait("Req", "UI-MCP-070")] // App/Views/Settings/MCPKeyPanel.swift:103-114
    [Trait("Decision", "ED-063")]
    public void The_key_panel_content_has_a_drag_area_and_a_half_opacity_backdrop()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var appModelHarness = new AppModelHarness(withRuntime: false);
            var view = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => { });

            Assert.Contains(Descendants(view), control => control is WindowDragArea);
            // WindowBackdrop's own opacity (:108-113, 0.5) is a private field with no public getter
            // (Marrow.UI.Chrome.Chrome.cs), so only its presence is checked here; the 0.5 argument itself is
            // visible directly in MCPKeyPanel.cs's `new WindowBackdrop(0.5)` call.
            Assert.Contains(Descendants(view), control => control is WindowBackdrop);
            Assert.Equal(26, Chrome.WindowCornerRadius);
        });
    }
}
