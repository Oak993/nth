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

// Source: DroppyCode/App/Views/Settings/MCPKeyPanel.swift (B3 52bc7926), :24-195.
[Collection("Headless")]
public sealed class MCPKeyPanelTests(HeadlessSession headless)
{
    // Follow-up: field construction writes MCPStore drafts, so run the UI body on TestRuntime's main actor.
    private void OnMain(Action body) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var harness = new AppModelHarness(withRuntime: false);
        runtime.OnMain(() =>
        {
            body();
            // TextChanged is deferred; drain it before the fake store and main actor are disposed.
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
    });

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
    [Trait("Req", "PR-MCP-217")] // App/Views/Settings/MCPKeyPanel.swift:89-105
    public void Key_page_and_non_key_page_entries_show_their_exact_numbered_steps()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "PR-MCP-217")] // App/Views/Settings/MCPKeyPanel.swift:96,101
    public void Step_text_uses_the_key_and_value_fallbacks_when_there_are_no_fields()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "UI-MCP-074")] // App/Views/Settings/MCPKeyPanel.swift:91-95
    public void The_reopen_key_page_link_is_shown_only_for_a_parseable_nonempty_url()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "UI-MCP-071")] // App/Views/Settings/MCPKeyPanel.swift:66-87
    public void The_header_shows_the_entry_name_and_the_literal_connect_subtitle_for_oauth()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "UI-MCP-072")] // App/Views/Settings/MCPKeyPanel.swift:78-85
    public void The_xmark_button_calls_on_done_without_submitting()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "UI-MCP-073")] // App/Views/Settings/MCPKeyPanel.swift:107-124
    [Trait("Decision", "ED-059")]
    public void Numbered_steps_use_the_upstream_footnote_and_callout_sizes()
    {
        Assert.Equal(10, SwiftFont.System(SwiftFont.TextStyle.Footnote).PointSize);
        Assert.Equal(12, SwiftFont.System(SwiftFont.TextStyle.Callout).PointSize);

        OnMain(() =>
        {
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
    [Trait("Req", "PR-MCP-218")] // App/Views/Settings/MCPKeyPanel.swift:126-168
    public void Fields_render_in_entry_order_with_a_folder_symbol_only_for_path_fields()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "PR-MCP-218")] // App/Views/Settings/MCPKeyPanel.swift:128-166
    [Trait("Decision", "ED-059")]
    public void Field_labels_use_subheadline_and_nonempty_help_is_rendered()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "UI-MCP-075")] // App/Views/Settings/MCPKeyPanel.swift:128-166
    [Trait("Decision", "ED-059")]
    public void Field_label_and_control_fonts_use_their_source_text_styles()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "UI-MCP-076")] // App/Views/Settings/MCPKeyPanel.swift:145-154
    [Trait("Decision", "ED-059")]
    public void The_path_picker_symbol_uses_callout_size_and_is_absent_for_other_kinds()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "PR-MCP-219")] // App/Views/Settings/MCPKeyPanel.swift:38-45
    public void The_save_button_label_matches_oauth_and_non_oauth_entries()
    {
        OnMain(() =>
        {
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
    [Trait("Req", "UI-MCP-077")] // App/Views/Settings/MCPKeyPanel.swift:38-45
    public void The_save_button_is_disabled_when_a_required_field_is_empty()
    {
        OnMain(() =>
        {
            var view = new MCPKeyPanelView(
                Entry(fields: Secret()),
                new AppModel(),
                () => { });

            var save = ButtonWithText(view, "Save and connect");
            Assert.NotNull(save);
            Assert.False(save!.IsEnabled);
            var field = Assert.Single(Descendants(view).OfType<TextBox>());
            field.Text = "offline-test-key";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(save.IsEnabled); // :43 .disabled(!model.mcp.canConnect(entry)): typing the required field enables Save
            field.Text = "";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(save.IsEnabled);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-217")] // App/Views/Settings/MCPKeyPanel.swift:108 HStack(spacing: 8), centre alignment by default
    public void Each_step_text_sits_on_the_centre_line_of_its_18_point_number_circle()
    {
        OnMain(() =>
        {
            var view = new MCPKeyPanelView(Entry(keysUrl: "", fields: Secret("token", "Access Token")), new AppModel(), () => { });
            var window = new Avalonia.Controls.Window { Content = view, Width = 520, Height = 600 };
            window.Show();
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var text = Descendants(view).OfType<TextBlock>().Single(block => block.Text == "Enter the access token");
                var row = Assert.IsType<StackPanel>(text.Parent);
                var circle = Assert.IsType<Border>(row.Children[0]);
                // The 15-point callout line is shorter than the 18-point circle: the HStack puts it on the circle's centre line.
                Assert.Equal(text.DesiredSize.Height, text.Bounds.Height, 0.5);
                Assert.Equal(circle.Bounds.Center.Y, text.Bounds.Center.Y, 0.5);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-077")] // App/Views/Settings/MCPKeyPanel.swift:134-141 TextField/SecureField(field.placeholder, text:) .textFieldStyle(.plain)
    public void The_key_field_is_drawn_with_its_prompt_and_then_the_typed_text()
    {
        // The app carries no theme package, so a TextBox shows nothing unless it is given a template: the field must be
        // the port's plain text field (the prompt over a TextPresenter), not a bare TextBox.
        OnMain(() =>
        {
            var view = new MCPKeyPanelView(Entry(fields: [Field("token", "Token", "Paste the token", MCPField.Kind.Text)]), new AppModel(), () => { });
            var window = new Avalonia.Controls.Window { Content = view, Width = 520, Height = 600 };
            window.Show();
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var field = Assert.Single(Descendants(view).OfType<TextBox>());
                Assert.True(field.Bounds.Height > 0, "the field has no height: " + field.Bounds);
                var prompt = field.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Paste the token");
                Assert.True(prompt.IsEffectivelyVisible && prompt.Bounds.Height > 0, "the prompt is not drawn");
                field.Text = "secret-value";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.False(prompt.IsVisible);
                var presenter = Assert.Single(field.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>());
                Assert.Equal("secret-value", presenter.Text);
                Assert.True(presenter.Bounds.Width > 0, "the typed text is not drawn");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-077")] // App/Views/Settings/MCPKeyPanel.swift:134-136 a secret field is a SecureField: what is typed is drawn masked
    public void A_secret_key_is_drawn_masked()
    {
        OnMain(() =>
        {
            var view = new MCPKeyPanelView(Entry(fields: Secret()), new AppModel(), () => { });
            var window = new Avalonia.Controls.Window { Content = view, Width = 520, Height = 600 };
            window.Show();
            try
            {
                var field = Assert.Single(Descendants(view).OfType<TextBox>());
                field.Text = "sk-live-secret";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var presenter = Assert.Single(field.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>());
                Assert.Equal('•', presenter.PasswordChar);
            }
            finally { window.Close(); }
        });
    }

    // Follow-up: MCPStore drafts are now observed. Deferred TextChanged callbacks are drained above
    // while the fake store and TestRuntime main actor remain active; field assertions are unchanged.

    [Fact]
    [Trait("Req", "UI-MCP-070")] // App/Views/Settings/MCPKeyPanel.swift:47-58
    [Trait("Decision", "ED-063")]
    public void The_key_panel_content_has_a_drag_area_and_a_half_opacity_backdrop()
    {
        OnMain(() =>
        {
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

    [Fact]
    [Trait("Req", "UI-MCP-076")] // App/Views/Settings/MCPKeyPanel.swift:126-168 HStack(spacing: 6): six points between the path field and its folder button
    public void The_folder_button_sits_six_points_after_the_path_field()
    {
        OnMain(() =>
        {
            var view = new MCPKeyPanelView(Entry(fields: [Field("root", "Root", "Choose a folder", MCPField.Kind.Path)]), new AppModel(), () => { });
            var button = ButtonWithSymbol(view, "folder")!;
            var grid = (Grid)button.Parent!;
            var field = grid.Children.OfType<TextBox>().Single();
            grid.Measure(new Avalonia.Size(300, double.PositiveInfinity));
            grid.Arrange(new Avalonia.Rect(0, 0, 300, grid.DesiredSize.Height));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(6, button.Bounds.Left - field.Bounds.Right, 0.5);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-071")] // App/Views/Settings/MCPKeyPanel.swift:66-87 HStack(spacing: 10) { icon; texts; Spacer(minLength: 8); close }: 10 + 8 + 10 points at least between the texts and the close button
    public void The_header_keeps_twenty_eight_points_between_the_title_and_the_close_button()
    {
        OnMain(() =>
        {
            var view = new MCPKeyPanelView(Entry(name: "A very long connector name that has to wrap inside the narrow panel header", fields: [Secret()]), new AppModel(), () => { });
            var close = ButtonWithSymbol(view, "xmark")!;
            var header = (Grid)close.Parent!;
            var title = header.Children.OfType<StackPanel>().Single();
            header.Measure(new Avalonia.Size(344, double.PositiveInfinity));
            header.Arrange(new Avalonia.Rect(0, 0, 344, header.DesiredSize.Height));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(28, close.Bounds.Left - title.Bounds.Right, 0.5);
            Assert.True(header.DesiredSize.Width <= 344.5, "the title did not wrap: " + header.DesiredSize);
        });
    }
}
