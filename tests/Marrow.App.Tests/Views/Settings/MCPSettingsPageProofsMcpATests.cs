using static Marrow.App.Tests.Fixtures.MCPProofsMcpARound2Input;
using BindingFlags = System.Reflection.BindingFlags;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :35-889.
// This job owns this file; existing unit tests supply the previously reviewed clauses.
[Collection("Headless")]
public sealed class MCPSettingsPageProofsMcpATests(HeadlessSession headless)
{
    private void Ui(Action<AppModel, TestRuntime, AppModelHarness, RecordingPresenter> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        runtime.OnMain(() =>
        {
            test(new AppModel(), runtime, harness, presenter);
            Dispatcher.UIThread.RunJobs(); // drain deferred TextChanged before the actor/store scopes end
        });
    });

    private void Flow(Action<AppModel, TestRuntime, AppModelHarness, RecordingPresenter> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        var model = runtime.OnMain(() => new AppModel());
        test(model, runtime, harness, presenter);
        runtime.OnMain(() => Dispatcher.UIThread.RunJobs());
    });

    // Swift colour tokens, not sampled output: Chrome.swift:76-79; ED-062 secondary = primary * .7.
    private static void Colour(IBrush? actual, SwiftColor expected, Control owner)
    {
        var window = TopLevel.GetTopLevel(owner) as Avalonia.Controls.Window;
        var previous = window?.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                if (window is not null) { window.RequestedThemeVariant=theme; Dispatcher.UIThread.RunJobs(); }
                var colour = ((ISolidColorBrush)expected.Brush(owner)).Color;
                var drawn = Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color;
                if (colour.A == 0) Assert.Equal(0, drawn.A);
                else Assert.Equal(colour, drawn);
            }
        }
        finally { if(window is not null) { window.RequestedThemeVariant=previous; Dispatcher.UIThread.RunJobs(); } }
    }
    // ED-059/091 + OD-0009: fixed nominal sizes, Inter and Cascadia Mono; no app zoom in the font.
    private static void Font(TextBlock block, double size, FontWeight weight = FontWeight.Normal, bool mono = false)
    {
        Assert.Equal(size, block.FontSize); Assert.Equal(weight, block.FontWeight); Assert.Equal(FontStyle.Normal,block.FontStyle); Assert.Equal(TextAlignment.Start,block.TextAlignment); // Swift leading is logical text start.
        Assert.Equal(SwiftFont.System(size, design: mono ? SwiftFont.Design.Monospaced : SwiftFont.Design.Default).Resolve().FontFamily, block.FontFamily);
    }
    private static TextBlock Text(Control root, string text) => Nodes<TextBlock>(root).Single(t => t.Text == text);
    // Simulate the platform Opened notification; invoke the REAL PlainControls.Popover factory handler.
    private static Control OpenContent(Popup popup)
    {
        ((EventHandler)typeof(Popup).GetField("Opened", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!).Invoke(popup, EventArgs.Empty);
        return Assert.IsAssignableFrom<Control>(PopoverHost.Content(popup)); // the site's view, not the card the presenter hangs it on (OD-0095)
    }

    [Theory]
    [InlineData("Connect", false, false, false)]
    [InlineData("Sign in", true, false, false)]
    [InlineData("Try again", false, true, false)]
    [InlineData("Sign in again", true, true, false)]
    [InlineData("Fix it", false, false, false)]
    [InlineData("Connect", false, false, true)]
    [Trait("Rule", "R-TEST-2")] // MCPSettingsPage.swift:328-348,505-507; ED-180 source-defined surroundings only
    // The small control's metrics and the tinted look are measured on the reference video and proven in MCPSettingsPageActionPillTests
    // (UI-MCP-024 and the records that share its button); this theory keeps the untinted-window data: the neutral glass, the primary label.
    public void Actions_keep_source_labels_primary_colour_and_glass_effect_data_in_each_branch(string label, bool oauth, bool failed, bool custom)
    {
        Ui((model, _, _, _) =>
        {
            var setup = new MCPSetupGuide("Setup", "Reason", new[] { "Step" }, "Open", "", null);
            State(model, custom ? "custom-example" : "offline", label == "Fix it" ? new MCPConnectionState.NeedsSetup(setup) : failed ? new MCPConnectionState.Failed("Rejected") : new MCPConnectionState.NotConnected());
            Control owner = custom ? new MCPCustomRow(Custom()) : new MCPServerCard(Entry(transport: oauth ? new MCPTransport.Oauth("https://example.invalid") : null)); using var window = Attach(owner, model);
            var button = Button(owner, label); Colour(Text(button, label).Foreground, label == "Fix it" ? SwiftColor.White : Chrome.PrimaryText, button); // the prominent glass is always accent-filled under a white label
            Assert.Contains(Nodes<Border>(button), b => SwiftGlass.GetEffect(b) is { IsInteractive: true } effect && (effect.TintColor is not null) == (label == "Fix it"));
            if (label == "Fix it")
            {
                PointerClick(button); Assert.True(MCPSetupPanel.Shared.IsPresenting(Entry())); var panel = typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(MCPSetupPanel.Shared); PointerClick(button); Assert.Same(panel, typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(MCPSetupPanel.Shared)); MCPSetupPanel.Shared.Close();
            }
        });
    }
    private static void ObservableState(AppModel model, string id, MCPConnectionState value)
    {
        var next = new Dictionary<string, MCPConnectionState>(model.Mcp.States) { [id] = value };
        typeof(MCPStore).GetProperty(nameof(MCPStore.States))!.SetValue(model.Mcp, next);
        Dispatcher.UIThread.RunJobs();
    }
    private static string[] Configs() => [MCPPaths.ClaudeConfigURL, MCPPaths.CodexConfigURL, MCPPaths.CopilotConfigURL, Path.Combine(MCPPaths.Directory, "gemini-settings.json")];
    private static void Exported(string id, bool present)
    {
        // MCPStore.swift:137-143 and MCPProviderConfig.swift:148-159: all FOUR generated files.
        Assert.All(Configs(), path =>
        {
            if (present) { Assert.True(File.Exists(path)); Assert.Contains(id, File.ReadAllText(path), StringComparison.Ordinal); }
            else Assert.False(File.Exists(path)); // fixture has only this server; WriteAll([]) deletes each file
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-204")] // App/Views/Settings/MCPSettingsPage.swift:352-358,534-540; MCPKeyPanel.swift:12-19
    public void Card_and_custom_actions_take_the_real_no_input_keyed_and_browser_branches()
    {
        Ui((model, _, _, _) =>
        {
            var launcher = new MCPProofsMcpALauncher();
            var desktop = new ClassicDesktopStyleApplicationLifetime(); var previous = Application.Current!.ApplicationLifetime;
            var mainControl = new Border(); using var main = Attach(mainControl, model); var mainWindow = Assert.IsType<Avalonia.Controls.Window>(Avalonia.Controls.TopLevel.GetTopLevel(mainControl));
            var platformField = typeof(Avalonia.Controls.TopLevel).GetField("<PlatformImpl>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var original = (IWindowImpl)platformField.GetValue(mainWindow)!; var proxy = System.Reflection.DispatchProxy.Create<IWindowImpl, MCPProofsMcpAWindowProxy>();
            ((MCPProofsMcpAWindowProxy)proxy).Original = original; ((MCPProofsMcpAWindowProxy)proxy).Launcher = launcher; platformField.SetValue(mainWindow, proxy);
            var lifetimeField = typeof(Application).GetField("_applicationLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;
            desktop.MainWindow = mainWindow; lifetimeField.SetValue(Application.Current, desktop); var inputWindows = new List<IDisposable>();
            try
            {
                void Show(Control content) => inputWindows.Add(Attach(content, model));
                var plain = Entry(); var card = new MCPServerCard(plain); Show(card); PointerClick(Button(card, "Connect")); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State(plain.Id)); Assert.True(((System.Collections.IDictionary)Read<object>(model.Mcp,"probes")).Contains(plain.Id)); Assert.False((typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(MCPKeyPanel.Shared) as Avalonia.Controls.Window)?.IsVisible ?? false); model.Mcp.Disconnect(plain);
                var keyed = Entry("keyed") with { KeysURL = "https://example.invalid/keys", Fields = new[] { new MCPField("TOKEN", "API Key", "Paste", MCPField.Kind.Secret) } };
                Assert.True(keyed.NeedsInput); var keyCard = new MCPServerCard(keyed); Show(keyCard); PointerClick(Button(keyCard, "Connect"));
                Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State(keyed.Id)); Assert.Equal(new Uri("https://example.invalid/keys"), Assert.Single(launcher.Requests));
                var panel = (Avalonia.Controls.Window)typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(MCPKeyPanel.Shared)!;
                Assert.True(panel.IsVisible); Assert.Single(Nodes<MCPKeyPanelView>(Assert.IsAssignableFrom<Control>(panel.Content))); MCPKeyPanel.Shared.Close();
                var custom = Custom(); var row = new MCPCustomRow(custom); Show(row); PointerClick(Button(row, "Connect")); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State(custom.Id)); Assert.True(((System.Collections.IDictionary)Read<object>(model.Mcp,"probes")).Contains(custom.Id)); Assert.False(panel.IsVisible); model.Mcp.Disconnect(custom.CatalogEntry());
                var keyedCustom = Custom(1); Assert.True(keyedCustom.CatalogEntry().NeedsInput); var keyedRow = new MCPCustomRow(keyedCustom); Show(keyedRow); PointerClick(Button(keyedRow, "Connect"));
                Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State(keyedCustom.Id)); Assert.True(panel.IsVisible); Assert.Single(launcher.Requests); MCPKeyPanel.Shared.Close();
                Dispatcher.UIThread.RunJobs();
            }
            finally { MCPKeyPanel.Shared.Close(); foreach (var inputWindow in inputWindows) inputWindow.Dispose(); lifetimeField.SetValue(Application.Current, previous); platformField.SetValue(mainWindow, original); }
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-073")] // App/Views/Settings/MCPSettingsPage.swift:564-566; MCPStore.swift:186-193; OD-0008 A1/B3; OD-0012 E1
    public void Delete_menu_preserves_disk_and_secrets_on_dismiss_and_removes_them_on_confirm_or_setting_off()
    {
        Flow((model, runtime, _, _) =>
        {
            // Declared protected field, not an undeclared plaintext key: exercises real Credential Manager boundary.
            var server = Custom(1) with { Env = new[] { new MCPCustomServer.Variable("TOKEN", IsSecret: true) } };
            runtime.OnMain(() => { Assert.True(model.Settings.ConfirmBeforeDeleting); Assert.Null(model.Mcp.SaveCustom(server)); Connection(model, new MCPConnection(server.Id)); Assert.True(model.Mcp.SaveCustomValues(new[] { ("TOKEN", "fixture-secret", true) }, server.Id, null)); });
            var row = new MCPCustomRow(server); using var window = runtime.OnMain(() => Attach(row, model));
            var beforeCustom = File.ReadAllBytes(MCPPaths.CustomsURL); var beforeConnection = File.ReadAllBytes(MCPPaths.ConnectionsURL);
            runtime.OnMain(() => { PointerClick(Button(Invoke<Control>(row, "BuildMenu"), "Delete")); });
            Assert.Single(model.Mcp.Customs); var confirmation = Read<Control>(row, "DeleteConfirmation");
            using var confirmationWindow = runtime.OnMain(() => Attach(confirmation, model)); // measure the real middle-truncating label
            Assert.Equal(new[] { "Delete this server?", "“Example” and the values it keeps in Credential Manager will be removed.", "Delete server" }, Texts(confirmation));
            Colour(Assert.Single(Nodes<MiddleTruncatingText>(Button(confirmation, "Delete server"))).Foreground, Chrome.Danger, confirmation);
            runtime.OnMain(() => Popovers.GetClosePopover(confirmation)!.Invoke());
            Assert.Equal(beforeCustom, File.ReadAllBytes(MCPPaths.CustomsURL)); Assert.Equal(beforeConnection, File.ReadAllBytes(MCPPaths.ConnectionsURL)); Assert.Equal("fixture-secret", MCPKeychain.Value(server.Id, "TOKEN"));
            runtime.OnMain(() => PointerClick(Button(Invoke<Control>(row, "BuildMenu"), "Delete")));
            confirmation = Read<Control>(row, "DeleteConfirmation"); using var confirmationWindow2 = runtime.OnMain(() => Attach(confirmation, model)); runtime.OnMain(() => PointerClick(Button(confirmation, "Delete server")));
            Assert.Empty(model.Mcp.Customs); Assert.Null(MCPKeychain.Value(server.Id, "TOKEN")); Assert.DoesNotContain(server.Id, File.ReadAllText(MCPPaths.CustomsURL), StringComparison.Ordinal); Assert.DoesNotContain(server.Id, File.ReadAllText(MCPPaths.ConnectionsURL), StringComparison.Ordinal);
            runtime.OnMain(() => { Assert.Null(model.Mcp.SaveCustom(server)); Assert.True(model.Mcp.SaveCustomValues(new[] { ("TOKEN", "fixture-secret", true) }, server.Id, null)); model.Settings.ConfirmBeforeDeleting = false; PointerClick(Button(Invoke<Control>(row, "BuildMenu"), "Delete")); });
            Assert.Empty(model.Mcp.Customs); Assert.Null(MCPKeychain.Value(server.Id, "TOKEN")); Assert.DoesNotContain(server.Id, File.ReadAllText(MCPPaths.CustomsURL), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-208")] // App/Views/Settings/MCPSettingsPage.swift:864-889
    public void Save_validation_credentials_retry_and_connected_edit_preserve_id_and_reprobe_after_values()
    {
        Ui((model, _, _, _) =>
        {
            var backend = new MCPSettingsRefusingCredentialBackend { Refuse = true };
            using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
            var editor = new MCPCustomEditor(null); using var window = Attach(editor, model); var closes = 0; Popovers.SetClosePopover(editor, () => closes++);
            PointerClick(Button(editor, "Add server")); Assert.Equal(0, closes); Assert.Empty(model.Mcp.Customs); Assert.Contains("The server needs a name.", Texts(editor));
            Field(editor, "My server").Text = "Example"; Field(editor, "npx").Text = "offline-fixture"; PointerClick(Button(editor, "Add environment variable"));
            Field(editor, "KEY").Text = " TOKEN "; Field(editor, "value").Text = "fixture-secret";
            PointerClick(Nodes<Button>(editor).Single(b => ToolTip.GetTip(b) as string == "Stored in mcp.json; click to keep it in the Credential Manager instead"));
            PointerClick(Button(editor, "Add environment variable")); // blank key must be dropped, Swift :877-878
            Nodes<TextBox>(editor).Last(b=>b.PlaceholderText=="value").Text="must-not-persist";
            PointerClick(Button(editor, "Add server")); Assert.Equal(0, closes); Assert.Contains("The Credential Manager could not save a value. Keep this editor open and try again.", Texts(editor));
            var saved = Assert.Single(model.Mcp.Customs); Assert.Equal("TOKEN", Assert.Single(saved.Env).Key); Assert.Equal(saved.Id, Read<string>(editor, "savedID"));
            backend.Refuse = false; Field(editor, "My server").Text = "Renamed"; PointerClick(Button(editor, "Add server"));
            Assert.Equal(1, closes); saved = Assert.Single(model.Mcp.Customs); Assert.Equal("custom-example", saved.Id); Assert.Equal("Renamed", saved.Name); Assert.Equal("fixture-secret", MCPKeychain.Value(saved.Id, "TOKEN")); Assert.False(model.Mcp.Connection(saved.Id)?.Values.ContainsKey("") ?? false);
            State(model, saved.Id, new MCPConnectionState.Connected(2));
            var edit = new MCPCustomEditor(saved); using var editWindow = Attach(edit, model); var closedAfterValues = false;
            Popovers.SetClosePopover(edit, () => { closedAfterValues = true; Assert.Equal("after-edit", model.Mcp.StoredValue(saved.Id, "MODE", false)); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State(saved.Id)); });
            Field(edit, "KEY").Text = " MODE "; Field(edit, "value").Text = "after-edit";
            PointerClick(Nodes<Button>(edit).Single(b => ToolTip.GetTip(b) as string == "Kept in the Credential Manager"));
            Field(edit, "npx").Text = "changed-offline-fixture"; PointerClick(Button(edit, "Save"));
            Assert.True(closedAfterValues); Assert.Equal("changed-offline-fixture", Assert.Single(model.Mcp.Customs).Command); Assert.Null(MCPKeychain.Value(saved.Id, "TOKEN"));
            Assert.NotEmpty(Read<object>(model.Mcp, "probes") as System.Collections.IDictionary ?? throw new InvalidOperationException("Probe dictionary missing"));
            model.Mcp.Disconnect(model.Mcp.Entry(saved.Id)!); // cancel before the queued fake command can start
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-210")] // App/Views/Settings/MCPSettingsPage.swift:794-840; MCPStore.swift:207-225
    public void Lock_keeps_row_identity_and_unsaved_value_then_moves_both_persisted_stores_in_both_directions()
    {
        Ui((model, _, _, _) =>
        {
            var server = Custom(1); Assert.Null(model.Mcp.SaveCustom(server)); Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "fixture-value", false) }, server.Id, null));
            var editor = new MCPCustomEditor(server); using var window = Attach(editor, model); var key = Field(editor, "KEY"); var row = key.Parent!.Parent;
            key.Text = " RENAMED "; Assert.Same(row, Field(editor, "KEY").Parent!.Parent); // :796 ForEach identity is UUID, never the edited key
            var toggle = Nodes<Button>(editor).Single(b => ToolTip.GetTip(b) as string == "Stored in mcp.json; click to keep it in the Credential Manager instead");
            var glyph = Assert.Single(Nodes<SwiftImage>(toggle)); Assert.Equal("lock.open", glyph.SymbolName); Colour(glyph.Foreground, Chrome.SecondaryText, glyph);
            var before = File.ReadAllBytes(MCPPaths.ConnectionsURL); PointerClick(toggle);
            Assert.Same(row, Field(editor, "KEY").Parent!.Parent); Field(editor,"value").ApplyTemplate(); var masked = Field(editor,"value").GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single(); Assert.NotEqual(default(char),masked.PasswordChar); Assert.False(masked.RevealPassword); Assert.NotEqual(default(char), Field(editor, "value").PasswordChar); Assert.Equal("fixture-value", Field(editor, "value").Text);
            Assert.Equal(before, File.ReadAllBytes(MCPPaths.ConnectionsURL)); Assert.Null(MCPKeychain.Value(server.Id, "RENAMED"));
            glyph = Assert.Single(Nodes<SwiftImage>(toggle)); Assert.Equal("lock.fill", glyph.SymbolName); Colour(glyph.Foreground, Chrome.Accent, glyph); Assert.Equal("Kept in the Credential Manager", ToolTip.GetTip(toggle));
            PointerClick(Button(editor, "Save")); var saved = Assert.Single(model.Mcp.Customs); Assert.Equal("fixture-value", MCPKeychain.Value(saved.Id, "RENAMED")); Assert.Null(model.Mcp.StoredValue(saved.Id, "RENAMED", false)); Assert.Null(model.Mcp.StoredValue(saved.Id, "KEY0", false)); Assert.DoesNotContain("fixture-value", File.ReadAllText(MCPPaths.ConnectionsURL), StringComparison.Ordinal);
            var edit = new MCPCustomEditor(saved); using var editWindow = Attach(edit, model); var unlock = Nodes<Button>(edit).Single(b => ToolTip.GetTip(b) as string == "Kept in the Credential Manager"); PointerClick(unlock); Assert.Equal(default(char), Field(edit, "value").PasswordChar); Assert.Equal("fixture-value", Field(edit, "value").Text); Assert.Equal("fixture-value", MCPKeychain.Value(saved.Id, "RENAMED"));
            PointerClick(Button(edit, "Save")); Assert.Null(MCPKeychain.Value(saved.Id, "RENAMED")); Assert.Equal("fixture-value", model.Mcp.StoredValue(saved.Id, "RENAMED", false)); Assert.Contains("fixture-value", File.ReadAllText(MCPPaths.ConnectionsURL), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-002")] // App/Views/Settings/MCPSettingsPage.swift:89-122
    [Trait("Req", "UI-MCP-003")] // App/Views/Settings/MCPSettingsPage.swift:91-93; ED-182 symbol method
    public void Lead_has_source_alignment_continuous_shape_fonts_and_resolved_colours()
    {
        Ui((model, _, _, _) =>
        {
            var lead = new MCPLeadCard(); using var window = Attach(lead, model);
            var surface = Assert.IsType<Border>(lead.Child); var row = Assert.IsType<Grid>(surface.Child);
            Assert.Equal(new Thickness(16), surface.Padding); Assert.Equal(new CornerRadius(14), surface.CornerRadius);
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface)); // :119 style continuous
            Assert.Equal(HorizontalAlignment.Stretch, surface.HorizontalAlignment); Assert.Equal(12, row.ColumnSpacing);
            var shield = Nodes<SwiftImage>(lead).Single(i => i.SymbolName == "checkmark.shield.fill");
            Assert.Equal(VerticalAlignment.Top, shield.VerticalAlignment); // :90 HStack(alignment: .top)
            var copy = Assert.IsType<StackPanel>(row.Children[1]); Assert.Equal(6, copy.Spacing); Assert.Equal(VerticalAlignment.Top,copy.VerticalAlignment); Assert.Equal(0,Grid.GetColumn(shield)); Assert.Equal(1,Grid.GetColumn(copy)); Assert.True(copy.Bounds.X>=shield.Bounds.Right+12);
            Assert.Equal(4, Assert.IsType<StackPanel>(copy.Children[0]).Spacing);
            var title = Text(lead, "Connected servers lead"); Font(title, 13, FontWeight.SemiBold);
            var body = Assert.IsType<TextBlock>(copy.Children[1]); Font(body, 12);
            Assert.Equal("Connect a server here and every thread can use it. Anything set up in a terminal or another app stays just as it is.", body.Text);
            Assert.Equal(TextWrapping.Wrap, body.TextWrapping); Assert.Equal(0, body.MaxLines); Assert.Equal(TextTrimming.None, body.TextTrimming);
            Assert.Equal(22, SwiftImage.GetFont(shield)!.Resolve().FontSize);
            Assert.Equal(FontWeight.Normal, SwiftImage.GetFont(shield)!.Resolve().FontWeight);
            Assert.Equal(SwiftFont.System(22).Resolve().FontFamily, SwiftImage.GetFont(shield)!.Resolve().FontFamily);
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                ((Avalonia.Controls.Window)Avalonia.Controls.TopLevel.GetTopLevel(lead)!).RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs();
                Colour(surface.Background, Chrome.PrimaryText.Opacity(0.05), surface);
                Colour(title.Foreground, Chrome.PrimaryText, title); Colour(body.Foreground, Chrome.SecondaryText, body);
                Colour(shield.Foreground, Chrome.Accent, shield);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-004")] // App/Views/Settings/MCPSettingsPage.swift:101,923-961
    [Trait("Req", "UI-MCP-005")] // App/Views/Settings/MCPSettingsPage.swift:124-133
    [Trait("Req", "UI-MCP-031")] // App/Views/Settings/MCPSettingsPage.swift:297-301,905-909
    public void Real_lead_and_corner_information_drive_hover_click_timers_and_content_colours()
    {
        Flow((model, runtime, _, presenter) =>
        {
            var lead = new MCPLeadCard(); using var leadWindow = runtime.OnMain(() => Attach(lead, model));
            var card = new MCPServerCard(Entry()); using var cardWindow = runtime.OnMain(() => Attach(card, model));
            foreach (var owner in new Control[] { lead, card })
            {
                var info = Assert.Single(Nodes<MCPHoverInfoButton>(owner)); var button = Assert.Single(Nodes<Button>(info));
                var glyph = Assert.Single(Nodes<SwiftImage>(button)); var popup = Assert.Single(Nodes<Popup>(info));
                Assert.Equal(owner == lead ? "About connected servers" : "About Example", AutomationProperties.GetName(button));
                Assert.Equal(22, button.Width); Assert.Equal(22, button.Height); Assert.False(button.Focusable);
                runtime.OnMain(() => Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick()); // hit testing reads the composed scene, as ChromeTests.Show documents
                Assert.NotNull(button.InputHitTest(new Point(1, 1))); Assert.NotNull(button.InputHitTest(new Point(21, 21))); // :932 .contentShape(.rect), including corners outside the glyph
                Assert.Equal("info.circle", glyph.SymbolName); Assert.Equal(12, SwiftImage.GetFont(glyph)!.Resolve().FontSize);
                Assert.Equal(FontWeight.Medium, SwiftImage.GetFont(glyph)!.Resolve().FontWeight); Assert.Equal(SwiftFont.System(12,SwiftFont.Weight.Medium).Resolve().FontFamily,SwiftImage.GetFont(glyph)!.Resolve().FontFamily);
                Colour(glyph.Foreground, Chrome.SecondaryText.Opacity(0.55), glyph);
                Assert.False(Read<bool>(info, "IsPresented"));
                runtime.OnMain(() => PointerHover(button, true)); Colour(glyph.Foreground, Chrome.SecondaryText, glyph);
                runtime.AdvanceMilliseconds(219); Assert.False(Read<bool>(info, "IsPresented"));
                runtime.AdvanceMilliseconds(1); Assert.True(Read<bool>(info, "IsPresented")); Dispatcher.UIThread.RunJobs();
                Assert.Contains(presenter.Calls, c => c.Popup == popup && c.Presented);
                var content = runtime.OnMain(() => OpenContent(popup));
                if (owner == lead)
                {
                    var facts = Nodes<Border>(content).Single(b => b.Padding == new Thickness(14));
                    var stack = Assert.IsType<StackPanel>(facts.Child); Assert.Equal(8, stack.Spacing);
                    Assert.Equal(new[] { "Keys stay in your Credential Manager", "Disconnect to go back to your own setup", "Every provider, whatever the model" }, Texts(facts)); // :102-107, OD-0008 B3
                    Assert.Equal(new[] { "lock.fill", "arrow.uturn.backward", "sparkles" }, Nodes<SwiftImage>(facts).Select(i => i.SymbolName));
                    Assert.All(stack.Children.Cast<StackPanel>(), r => { Assert.Equal(Orientation.Horizontal, r.Orientation); Assert.Equal(8, r.Spacing); });
                    Assert.All(Nodes<SwiftImage>(facts), i => { Assert.Equal(14, i.Width); Assert.Equal(12, SwiftImage.GetFont(i)!.Resolve().FontSize); Colour(i.Foreground, Chrome.Accent, i); });
                    Assert.All(Nodes<TextBlock>(facts), t => { Font(t, 12); Colour(t.Foreground, Chrome.PrimaryText.Opacity(0.9), t); });
                }
                runtime.OnMain(() => PointerHover(button, false)); runtime.AdvanceMilliseconds(319); Assert.True(Read<bool>(info, "IsPresented"));
                runtime.OnMain(() => PointerHover(content, true)); runtime.AdvanceMilliseconds(1); Assert.True(Read<bool>(info, "IsPresented"));
                runtime.OnMain(() => PointerHover(content, false)); runtime.AdvanceMilliseconds(319); Assert.True(Read<bool>(info, "IsPresented"));
                runtime.AdvanceMilliseconds(1); Assert.False(Read<bool>(info, "IsPresented")); Dispatcher.UIThread.RunJobs();
                Colour(glyph.Foreground, Chrome.SecondaryText.Opacity(0.55), glyph);
                runtime.OnMain(() => PointerClick(button)); Assert.True(Read<bool>(info, "IsPresented")); Colour(glyph.Foreground, Chrome.SecondaryText, glyph);
                runtime.OnMain(() => PointerClick(button)); Assert.False(Read<bool>(info, "IsPresented"));
            }
            var corner = Assert.Single(Nodes<MCPInfoButton>(card));
            Assert.Equal(new Thickness(10), corner.Margin); Assert.Equal(HorizontalAlignment.Right, corner.HorizontalAlignment); Assert.Equal(VerticalAlignment.Bottom, corner.VerticalAlignment);
            runtime.OnMain(() => card.UpdateCelebrating(true)); Assert.Empty(Nodes<MCPInfoButton>(card));
        });
    }

    private static void Laid(Control page)
    {
        for (var pass = 0; pass < 4; pass++) { ((Avalonia.Controls.Window)TopLevel.GetTopLevel(page)!).UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    }

    // Chrome.swift:1021-1025 `.background(RoundedRectangle(cornerRadius: Chrome.cardCornerRadius, style: .continuous).fill(Chrome.overlay(0.03)))` with cardCornerRadius 16
    // (:58): the Connected card is a fill, so SwiftShapes cuts it to Apple's continuous corner once it is 2 * 1.528665 * 16 = 48.9 pt tall, which one connected row
    // (11 + two lines of copy + 11) already is. OD-0098: the Swift source is the proof where no frame shows the corner.
    [Fact]
    [Trait("Req", "UI-MCP-007")] // MCPSettingsPage.swift:40-45; Chrome.swift:1021-1025 RoundedRectangle(cornerRadius: Chrome.cardCornerRadius, style: .continuous)
    [Trait("Rule", "R-TEST-4")] // the corner clause only: the lazy row creation of STOP opencode-proofs-mcp-a #4 is still open
    public void The_Connected_card_is_cut_to_Apples_continuous_corner_of_radius_16_with_one_row_and_with_two() => Flow((model, runtime, _, _) =>
    {
        var page = new MCPSettingsPage(""); EnvironmentValues.SetAccessibilityReduceMotion(page, true); using var window = runtime.OnMain(() => Attach(page, model));
        runtime.OnMain(() => { ObservableState(model, "github", new MCPConnectionState.Connected(3)); });
        runtime.RunUntilIdle(); runtime.OnMain(() => Laid(page));
        var surface = Assert.IsType<Border>(Assert.Single(Nodes<ChromeCard>(Assert.IsType<ChromeSection>(Assert.IsType<StackPanel>(page.Child).Children[1]))).Child);
        Assert.Equal(new CornerRadius(16), surface.CornerRadius);
        Assert.Single(Nodes<MCPConnectedRow>(page));
        Assert.True(surface.Bounds.Height >= 2 * 1.528665 * 16, $"one connected row makes a {surface.Bounds.Height} pt card");
        Assert.True(ContinuousCornerChecks.Drawn(surface, expectApple: true));
        runtime.OnMain(() => { ObservableState(model, "gitlab", new MCPConnectionState.Connected(2)); });
        runtime.RunUntilIdle(); runtime.OnMain(() => Laid(page));
        Assert.Equal(2, Nodes<MCPConnectedRow>(page).Count());
        Assert.True(ContinuousCornerChecks.Drawn(surface, expectApple: true));
    });

    [Fact]
    [Trait("Req", "UI-MCP-006")] // App/Views/Settings/MCPSettingsPage.swift:38-47; UI/Chrome/Chrome.swift:1038-1051
    [Trait("Req", "UI-MCP-008")] // App/Views/Settings/MCPSettingsPage.swift:152-203
    [Trait("Rule", "R-TEST-2")] // PR-MCP-199 and UI-MCP-007 structural subset; blockers are recorded below
    // STOP opencode-proofs-mcp-a #1 (PR-MCP-199): CLOSED 2026-10-04 (unit rec-linebox). The reference video 1-settings-tour.mov shows this page (S 180.5-187.5):
    // the grid cards are 255 wide with 10 between them, the Connected card 520 wide with its divider inset 16, all equal to the stills; the card heights that
    // differed (118.5 and 171 in the port) followed from the text-style line boxes (ED-290 (1)) and now match the frames (MCPSettingsPageFrameTests: a catalog
    // card 126, the Connected card 192; the video's 125.5 / 188 here were rough reads of the same cards).
    // STOP opencode-proofs-mcp-a #4: UI-MCP-007 needs lazy row creation on scroll, not only ChromeCard.Lazy data.
    // ChromeCard currently builds an eager subtree (Chrome.cs:1565-1587); no docs/b3 ruling approves that substitution.
    public void Page_sections_rows_and_grids_follow_source_order_geometry_colours_and_live_presence()
    {
        Flow((model, runtime, _, _) =>
        {
            var page = new MCPSettingsPage(""); EnvironmentValues.SetAccessibilityReduceMotion(page, true); using var window = runtime.OnMain(() => Attach(page, model));
            var root = Assert.IsType<StackPanel>(page.Child); Assert.Equal(20, root.Spacing);
            Assert.DoesNotContain("Connected", Texts(page));
            var footer = Assert.IsType<TextBlock>(root.Children[^1]); Font(footer, 11); Colour(footer.Foreground, Chrome.SecondaryText, footer);
            Assert.Equal("Connected servers reach every provider when a thread starts: the CLIs at launch, the API models through Nth itself.", footer.Text);
            Assert.All(Nodes<Grid>(page).Where(g => g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions.All(c => c.Width.IsStar)), g =>
            { Assert.Equal(10, g.ColumnSpacing); Assert.Equal(10, g.RowSpacing); Assert.All(g.ColumnDefinitions, c => Assert.Equal(new GridLength(1, GridUnitType.Star), c.Width)); });
            runtime.OnMain(() => { ObservableState(model, "github", new MCPConnectionState.Connected(3)); ObservableState(model, "gitlab", new MCPConnectionState.Connected(2)); });
            Assert.IsType<MCPLeadCard>(root.Children[0]); var section = Assert.IsType<ChromeSection>(root.Children[1]); Assert.IsType<MCPCustomSection>(root.Children[2]);
            var heading = Text(section, "Connected"); Font(heading, 13, FontWeight.SemiBold); Colour(heading.Foreground, Chrome.SecondaryText, heading);
            var sectionStack = Assert.IsType<StackPanel>(section.Child); Assert.Equal(10, sectionStack.Spacing); Assert.Equal(4, Assert.IsAssignableFrom<StackPanel>(sectionStack.Children[0]).Spacing);
            var card = Assert.Single(Nodes<ChromeCard>(section)); Assert.True(card.Lazy); var surface = Assert.IsType<Border>(card.Child);
            Assert.Equal(new CornerRadius(16), surface.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface)); Colour(surface.Background, Chrome.PrimaryText.Opacity(0.03), surface);
            Assert.False(surface.ClipToBounds); Assert.Equal(HorizontalAlignment.Stretch, surface.HorizontalAlignment);
            Assert.All(Nodes<StackPanel>(card), s => Assert.True(s.Spacing == 0 || s.Spacing == 2));
            var divider = Assert.Single(Nodes<ChromeRowDivider>(section)); Assert.Equal(new Thickness(16, 0, 0, 0), divider.Margin);
            Assert.Equal(new[] { "GitHub", "GitLab" }, Nodes<MCPConnectedRow>(section).Select(r => Texts(r)[0]));
            foreach (var row in Nodes<MCPConnectedRow>(section))
            {
                var border = Assert.IsType<Border>(row.Child); Assert.Equal(new Thickness(16, 11, 10, 11), border.Padding);
                var grid = Assert.IsType<Grid>(border.Child); Assert.Equal(12, grid.ColumnSpacing);
                Assert.Contains(grid.Children.OfType<Border>(), b => b.MinWidth == 12); runtime.OnMain(() => SpacerOrder(grid, grid.Children[1], grid.Children[3], 12)); // :164 Spacer(minLength: 12)
                var icon = Assert.Single(Nodes<MCPIcon>(row)); Assert.Equal(22, icon.Child!.Width); Assert.Equal(26, Assert.IsType<Border>(icon.Parent).Width); Assert.Equal(HorizontalAlignment.Center, icon.HorizontalAlignment);
                var copy = Assert.Single(grid.Children.OfType<StackPanel>()); Assert.Equal(2, copy.Spacing);
                var name = Assert.IsType<TextBlock>(copy.Children[0]); var detail = Assert.IsType<TextBlock>(copy.Children[1]);
                Font(name, 13, FontWeight.SemiBold); Font(detail, 11); Colour(name.Foreground, Chrome.PrimaryText, name); Colour(detail.Foreground, Chrome.SecondaryText, detail);
                Assert.Equal(name.Text == "GitHub" ? "3 tools · GitHub" : "2 tools · GitLab", detail.Text); // :160 vendor fallback
            }
            runtime.OnMain(() => { ObservableState(model, "github", new MCPConnectionState.NotConnected()); ObservableState(model, "gitlab", new MCPConnectionState.NotConnected()); });
            Assert.DoesNotContain("Connected", Texts(page)); Assert.Empty(Nodes<MCPConnectedRow>(page));
            runtime.OnMain(() => page.SetQuery("no-such-server-in-any-field")); Assert.Empty(Nodes<ChromeSection>(page));
            Assert.Null(Assert.Single(Nodes<MCPCustomSection>(page)).Child); Assert.Same(footer, root.Children[^1]);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-010")] // App/Views/Settings/MCPSettingsPage.swift:169-180
    [Trait("Req", "UI-MCP-011")] // App/Views/Settings/MCPSettingsPage.swift:182-184; Popovers.swift:258-273
    [Trait("Req", "UI-MCP-012")] // App/Views/Settings/MCPSettingsPage.swift:185-188
    [Trait("Req", "UI-MCP-013")] // App/Views/Settings/MCPSettingsPage.swift:189; Popovers.swift:152-158
    public void Options_menu_has_shared_style_and_closes_before_refresh_or_tools()
    {
        Flow((model, runtime, _, presenter) =>
        {
            var entry = Entry(); State(model, entry.Id, new MCPConnectionState.Connected(1));
            Connection(model, new MCPConnection(entry.Id) { Values = new ValueMap<string, string>(new Dictionary<string, string> { ["MODE"] = "keep" }) });
            var row = new MCPConnectedRow(entry); using var window = runtime.OnMain(() => Attach(row, model));
            var options = Nodes<Button>(row).Single(b => ToolTip.GetTip(b) as string == "Server options");
            Assert.Equal(22, options.Width); Assert.Equal(22, options.Height); Assert.Equal(0, options.BorderThickness.Left); Assert.Null(options.Background);
            var glyph = Assert.Single(Nodes<SwiftImage>(options)); Assert.Equal("ellipsis.circle", glyph.SymbolName); Assert.Equal(13, SwiftImage.GetFont(glyph)!.Resolve().FontSize); Colour(glyph.Foreground, Chrome.SecondaryText, glyph);
            runtime.OnMain(() => PointerClick(options));
            var menuPopup = Nodes<Popup>(row)[0]; var toolsPopup = Nodes<Popup>(row)[1];
            Assert.Same(options, menuPopup.PlacementTarget); Assert.Equal(PlacementMode.Bottom, menuPopup.Placement);
            Assert.True(presenter.Calls.Last(c => c.Popup == menuPopup).Presented);
            var menu = runtime.OnMain(() => OpenContent(menuPopup));
            Assert.Equal(new[] { "Refresh", "Show tools", "Disconnect" }, Nodes<Button>(menu).Select(AutomationProperties.GetName));
            foreach (var label in new[] { "Refresh", "Show tools" })
            {
                var item = Button(menu, label); Assert.True(item.IsEnabled); var title = Assert.Single(Nodes<MiddleTruncatingText>(item)); Font(title, 13); Colour(title.Foreground, Chrome.PrimaryText, title);
                Assert.Equal(26, Assert.Single(Nodes<StackPanel>(item)).Height); Assert.Equal(new Thickness(8, 0), Assert.Single(Nodes<StackPanel>(item)).Margin);
                Colour(Assert.Single(Nodes<SwiftImage>(item)).Foreground, Chrome.PrimaryText, item);
            }
            var divider = Assert.IsType<Border>(Assert.Single(Nodes<PopoverDivider>(menu)).Child); Assert.Equal(1, divider.Height); Assert.Equal(new Thickness(8, 5), divider.Margin); Colour(divider.Background, Chrome.PrimaryText.Opacity(0.22), divider); // shared port separator method
            // Run only the click work item; PopoverItem.swift:271 closes synchronously, :273 queues action.
            var refreshedClick = false; runtime.Loop.Enqueue(() => { }); // a predecessor deterministically exposes the old head-of-queue flake
            runtime.Loop.Enqueue(() => { PointerClick(Button(menu, "Refresh")); refreshedClick = true; }); runtime.Loop.RunUntil(() => refreshedClick);
            Assert.False(Read<bool>(Read<object>(row, "view"), "Menu"));
            runtime.Loop.RunUntil(() => !presenter.Calls.Last(c => c.Popup == menuPopup).Presented);
            Assert.False(presenter.Calls.Last(c => c.Popup == menuPopup).Presented);
            Assert.IsType<MCPConnectionState.Connected>(model.Mcp.State(entry.Id)); // action is a queued main-actor task
            runtime.Loop.RunUntil(() => model.Mcp.State(entry.Id) is MCPConnectionState.Connecting);
            Assert.Equal("keep", model.Mcp.Connection(entry.Id)!.Values["MODE"]); runtime.OnMain(() => model.Mcp.Disconnect(entry));
            options = Nodes<Button>(row).Single(b => ToolTip.GetTip(b) as string == "Server options"); menuPopup = Nodes<Popup>(row)[0]; toolsPopup = Nodes<Popup>(row)[1];
            runtime.OnMain(() => PointerClick(options)); menu = runtime.OnMain(() => OpenContent(menuPopup));
            var toolsClick = false; runtime.Loop.Enqueue(() => { });
            runtime.Loop.Enqueue(() => { PointerClick(Button(menu, "Show tools")); toolsClick = true; }); runtime.Loop.RunUntil(() => toolsClick);
            Assert.False(Read<bool>(Read<object>(row, "view"), "Menu"));
            runtime.Loop.RunUntil(() => !presenter.Calls.Last(c => c.Popup == menuPopup).Presented);
            Assert.False(presenter.Calls.Last(c => c.Popup == menuPopup).Presented);
            runtime.Loop.RunUntil(() => presenter.Calls.Any(c => c.Popup == toolsPopup && c.Presented)); Dispatcher.UIThread.RunJobs();
            Assert.True(presenter.Calls.Last(c => c.Popup == toolsPopup).Presented); Assert.Same(options, toolsPopup.PlacementTarget); Assert.Equal(PlacementMode.Bottom, toolsPopup.Placement);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-014")] // App/Views/Settings/MCPSettingsPage.swift:190-192; MCPStore.swift:115-128; OD-0012 E6
    public void Destructive_menu_disconnect_deletes_declared_secrets_and_reports_refusal()
    {
        Flow((model, runtime, _, _) =>
        {
            var entry = MCPCatalog.Entry("github")!; var field = entry.Fields.First(f => f.FieldKind == MCPField.Kind.Secret);
            foreach (var refuse in new[] { false, true })
            {
                var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false, RefuseDeletes = refuse };
                using var secrets = SecretStore.Shared.Override(new SecretStore(backend)); runtime.OnMain(() => Assert.True(MCPKeychain.Set("fixture-secret", entry.Id, field.Key)));
                State(model, entry.Id, new MCPConnectionState.Connected(1)); Connection(model, new MCPConnection(entry.Id) { ConnectedAt = DateTimeOffset.UtcNow });
                runtime.OnMain(() => model.Mcp.SetEnabled(true, entry)); Exported(entry.Id, true);
                var row = new MCPConnectedRow(entry); using var window = runtime.OnMain(() => Attach(row, model));
                var menu = Invoke<Control>(row, "BuildMenu"); var button = Button(menu, "Disconnect");
                Assert.True(button.IsEnabled); var title = Assert.Single(Nodes<MiddleTruncatingText>(button)); Font(title, 13); Colour(title.Foreground, Chrome.Danger, title);
                Colour(Assert.Single(Nodes<SwiftImage>(button)).Foreground, Chrome.Danger, button);
                runtime.OnMain(() => PointerClick(button));
                Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State(entry.Id)); Assert.False(model.Mcp.Drafts.ContainsKey(entry.Id)); Exported(entry.Id, false);
                Assert.DoesNotContain(entry.Id, File.ReadAllText(MCPPaths.ConnectionsURL), StringComparison.Ordinal);
                Assert.Equal(refuse ? "fixture-secret" : null, MCPKeychain.Value(entry.Id, field.Key));
                Assert.Equal(refuse ? "Disconnected, but Credential Manager did not remove this server’s stored values." : null, model.Mcp.CredentialDeletionFailure);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-015")] // App/Views/Settings/MCPSettingsPage.swift:207; Popovers.swift:117-133
    [Trait("Req", "UI-MCP-016")] // App/Views/Settings/MCPSettingsPage.swift:208-211; Popovers.swift:135-150
    [Trait("Req", "UI-MCP-017")] // App/Views/Settings/MCPSettingsPage.swift:212-227
    public void Tools_content_has_source_header_note_optional_summaries_and_first_baselines()
    {
        Ui((model, _, _, _) =>
        {
            var row = new MCPConnectedRow(Entry()); using var window = Attach(row, model);
            var empty = Invoke<Control>(row, "BuildTools"); using var emptyWindow = Attach(empty, model);
            var heading = Assert.IsType<TextBlock>(Assert.Single(Nodes<PopoverSectionHeader>(empty)).Child);
            Assert.Equal("Example tools", heading.Text); Font(heading, 11, FontWeight.SemiBold); Colour(heading.Foreground, Chrome.SecondaryText, heading); Assert.Equal(new Thickness(8, 6, 8, 3), heading.Margin);
            var note = Assert.IsType<TextBlock>(Assert.Single(Nodes<PopoverNote>(empty)).Child); Assert.Equal("No tools listed yet.", note.Text); Font(note, 12); Colour(note.Foreground, Chrome.SecondaryText, note); Assert.Equal(new Thickness(8, 5), note.Margin);
            var scroll = Assert.Single(Nodes<ScrollViewer>(empty)); Assert.Equal(460, scroll.MaxHeight); var padded = Assert.IsType<Border>(scroll.Content); Assert.Equal(new Thickness(6), padded.Padding); Assert.Equal(210, padded.MinWidth); Assert.Equal(360, padded.MaxWidth); // Popovers.swift:106-112
            Connection(model, new MCPConnection("offline") { Tools = new[] { new MCPToolSummary("read", "Reads a document\nwithin the fixture"), new MCPToolSummary("list", ""), new MCPToolSummary("write") } });
            var tools = Invoke<Control>(row, "BuildTools"); using var toolsWindow = Attach(tools, model);
            Assert.Empty(Nodes<PopoverNote>(tools)); Assert.Equal(new[] { "Example tools", "read", "Reads a document\nwithin the fixture", "list", "write" }, Texts(tools));
            foreach (var border in Nodes<Border>(tools).Where(b => b.Child is Grid))
            {
                Assert.Equal(new Thickness(8, 3), border.Padding); var grid = (Grid)border.Child!; Assert.Equal(8, grid.ColumnSpacing); Assert.Equal(HorizontalAlignment.Stretch, grid.HorizontalAlignment);
                var name = Assert.IsType<TextBlock>(grid.Children[0]); Font(name, 11, mono: true); Colour(name.Foreground, Chrome.PrimaryText, name);
                if (grid.Children.Count > 1)
                {
                    var summary = Assert.IsType<TextBlock>(grid.Children[1]); Font(summary, 11); Colour(summary.Foreground, Chrome.SecondaryText, summary); Assert.Equal(2, summary.MaxLines);
                    Assert.Equal(name.Bounds.Y + name.TextLayout.TextLines[0].Baseline, summary.Bounds.Y + summary.TextLayout.TextLines[0].Baseline, 6); // :213 .firstTextBaseline
                }
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-018")] // App/Views/Settings/MCPSettingsPage.swift:249-303; existing TransitionTests cover :53-58
    [Trait("Req", "UI-MCP-019")] // App/Views/Settings/MCPSettingsPage.swift:305-319
    [Trait("Req", "UI-MCP-020")] // App/Views/Settings/MCPSettingsPage.swift:257-261
    [Trait("Req", "UI-MCP-021")] // App/Views/Settings/MCPSettingsPage.swift:262-268
    [Trait("Req", "UI-MCP-022")] // App/Views/Settings/MCPSettingsPage.swift:269-274
    [Trait("Req", "UI-MCP-023")] // App/Views/Settings/MCPSettingsPage.swift:275-280
    public void Cards_prove_surface_header_summary_tools_and_state_message_styles()
    {
        Ui((model, _, _, _) =>
        {
            var guide = new MCPSetupGuide("Fixture setup", "Reason", new[] { "Step" }, "Open", "https://example.invalid", null);
            foreach (var state in new MCPConnectionState[] { new MCPConnectionState.NotConnected(), new MCPConnectionState.Connecting(), new MCPConnectionState.Failed("Fixture failure"), new MCPConnectionState.NeedsSetup(guide) })
            {
                State(model, "offline", state); var card = new MCPServerCard(Entry() with { SampleTools = new[] { "read", "list" } }); using var window = Attach(card, model);
                var layers = Assert.IsType<Panel>(card.Child); var surface = Assert.IsType<Border>(layers.Children[0]); Assert.Equal(new Thickness(14), surface.Padding); Assert.Equal(new CornerRadius(14), surface.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface)); Colour(surface.Background, Chrome.PrimaryText.Opacity(0.05), surface);
                Assert.Equal(HorizontalAlignment.Stretch, card.HorizontalAlignment); Assert.Equal(VerticalAlignment.Stretch, card.VerticalAlignment);
                var stack = Assert.IsType<StackPanel>(surface.Child); Assert.Equal(8, stack.Spacing); Assert.Equal(VerticalAlignment.Top, stack.VerticalAlignment);
                var header = Assert.IsType<Grid>(stack.Children[0]); Assert.Equal(10, header.ColumnSpacing); Assert.Contains(header.Children.OfType<Border>(), b => b.MinWidth == 8); SpacerOrder(header, header.Children[1], header.Children[3], 8);
                Assert.All(header.Children.Where(c => c is not Border), c => Assert.Equal(VerticalAlignment.Top, c.VerticalAlignment));
                var name = Text(card, "Example"); var vendor = Text(card, "Vendor"); Font(name, 13, FontWeight.SemiBold); Font(vendor, 11); Colour(name.Foreground, Chrome.PrimaryText, name); Colour(vendor.Foreground, Chrome.SecondaryText, vendor); Assert.Equal(1, Assert.IsType<StackPanel>(name.Parent).Spacing);
                var summary = Text(card, "Summary"); Font(summary, 12); Colour(summary.Foreground, Chrome.SecondaryText, summary); Assert.Equal(2, summary.MaxLines); Assert.Equal(TextWrapping.Wrap, summary.TextWrapping); Assert.Equal(TextTrimming.None, summary.TextTrimming);
                var tools = Text(card, "read · list"); Font(tools, 10.5, mono: true); Colour(tools.Foreground, Chrome.SecondaryText.Opacity(0.8), tools); Assert.Equal(1, tools.MaxLines); Assert.Equal(TextWrapping.NoWrap, tools.TextWrapping); Assert.Equal(TextTrimming.CharacterEllipsis, tools.TextTrimming); Assert.Equal(new Thickness(0, 0, 24, 0), tools.Margin);
                Assert.Equal(state is MCPConnectionState.Failed, Texts(card).Contains("Fixture failure")); Assert.Equal(state is MCPConnectionState.NeedsSetup, Texts(card).Contains("Fixture setup"));
                if (state is MCPConnectionState.Failed) { var t = Text(card, "Fixture failure"); Font(t, 11); Colour(t.Foreground, Chrome.Danger, t); Assert.Equal(3, t.MaxLines); Assert.Same(t, stack.Children[^1]); }
                if (state is MCPConnectionState.NeedsSetup) { var t = Text(card, "Fixture setup"); Font(t, 11); Colour(t.Foreground, Chrome.Warning, t); Assert.Equal(2, t.MaxLines); Assert.Same(t, stack.Children[^1]); }
                card.UpdateCelebrating(true); Assert.DoesNotContain("Example", Texts(card)); Assert.DoesNotContain("Summary", Texts(card)); Assert.Empty(Nodes<MCPInfoButton>(card));
            }
            State(model, "offline", new MCPConnectionState.NotConnected()); var noTools = new MCPServerCard(Entry()); using var emptyWindow = Attach(noTools, model); Assert.NotNull(Text(noTools, " "));
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [Trait("Req", "UI-MCP-026")] // App/Views/Settings/MCPSettingsPage.swift:331-338
    [Trait("Req", "UI-MCP-027")] // App/Views/Settings/MCPSettingsPage.swift:334-335
    [Trait("Req", "UI-MCP-038")] // App/Views/Settings/MCPSettingsPage.swift:508-515
    public void All_connecting_indicators_have_six_spacing_three_cell_spinner_and_secondary_fixed_type(bool oauth, bool custom)
    {
        Ui((model, _, _, _) =>
        {
            State(model, "offline", new MCPConnectionState.Connecting()); State(model, "custom-example", new MCPConnectionState.Connecting());
            Control control = custom ? new MCPCustomRow(Custom()) : new MCPServerCard(oauth ? Entry(transport: new MCPTransport.Oauth("https://example.invalid")) : Entry());
            {
                using var window = Attach(control, model); var spinner = Assert.Single(Nodes<WorkingSpinner>(control));
                var stack = Assert.IsType<StackPanel>(spinner.Parent); Assert.Equal(Orientation.Horizontal, stack.Orientation); Assert.Equal(6, stack.Spacing);
                var text = Assert.IsType<TextBlock>(stack.Children[1]); Assert.Equal(oauth ? "Waiting" : "Connecting", text.Text); Font(text, 11); Colour(text.Foreground, Chrome.SecondaryText, text); Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
                Assert.Equal(3, Read<double>(spinner.Child!, "cellSize")); Assert.DoesNotContain(Nodes<Button>(control), b => AutomationProperties.GetName(b) == "Cancel");
            }
            State(model, "offline", new MCPConnectionState.NotConnected()); var idle = new MCPServerCard(Entry()); using var idleWindow = Attach(idle, model); Assert.Empty(Nodes<WorkingSpinner>(idle));
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-032")] // App/Views/Settings/MCPSettingsPage.swift:360-375; existing Third/FollowupTests cover spring and 900 ms
    public void Celebration_has_source_success_colour_caption_style_and_reduce_motion_half_scale()
    {
        Ui((model, _, _, _) =>
        {
            State(model, "offline", new MCPConnectionState.Connected(3)); var card = new MCPServerCard(Entry(), true); EnvironmentValues.SetAccessibilityReduceMotion(card, true); using var window = Attach(card, model);
            var success = Nodes<StackPanel>(card).Single(s => s.MinHeight == 120); Assert.Equal(8, success.Spacing); Assert.Equal(HorizontalAlignment.Stretch, success.HorizontalAlignment); Assert.Equal(VerticalAlignment.Center, success.VerticalAlignment);
            var glyph = Assert.Single(Nodes<SwiftImage>(card)); Colour(glyph.Foreground, Chrome.Success, glyph); Assert.Equal("checkmark.circle.fill", glyph.SymbolName); Assert.Equal(34, SwiftImage.GetFont(glyph)!.Resolve().FontSize); Assert.Equal(0.5, Assert.IsType<ScaleTransform>(glyph.RenderTransform).ScaleX);
            var text = Text(card, "Connected · 3 tools"); Font(text, 12, FontWeight.Medium); Colour(text.Foreground, Chrome.PrimaryText, text); Assert.Equal(HorizontalAlignment.Center, text.HorizontalAlignment);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-033")] // App/Views/Settings/MCPSettingsPage.swift:394; Chrome.swift:1038-1051
    [Trait("Req", "UI-MCP-034")] // App/Views/Settings/MCPSettingsPage.swift:396-403; OD-0008 B4
    [Trait("Req", "UI-MCP-035")] // App/Views/Settings/MCPSettingsPage.swift:417-440
    public void Custom_section_proves_header_note_and_last_add_row_then_opens_the_real_editor()
    {
        Flow((model, runtime, _, presenter) =>
        {
            var section = new MCPCustomSection(""); using var window = runtime.OnMain(() => Attach(section, model));
            var header = Text(section, "Custom"); Font(header, 13, FontWeight.SemiBold); Colour(header.Foreground, Chrome.SecondaryText, header);
            var shell = Assert.IsType<StackPanel>(Assert.IsType<ChromeSection>(section.Child).Child); Assert.Equal(10, shell.Spacing); Assert.Equal(4, Assert.IsAssignableFrom<StackPanel>(shell.Children[0]).Spacing);
            var note = Text(section, "Servers of your own: a command that runs on this PC, or a remote address, with the variables they need."); Font(note, 12); Colour(note.Foreground, Chrome.SecondaryText, note); Assert.Equal(new Thickness(16, 11), note.Margin); Assert.Equal(TextWrapping.Wrap, note.TextWrapping); Assert.Equal(TextTrimming.None, note.TextTrimming);
            Assert.Equal(new Thickness(16, 0, 0, 0), Assert.Single(Nodes<ChromeRowDivider>(section)).Margin);
            var add = Button(section, "Add server"); var surface = Assert.IsType<Border>(add.Content); Assert.Equal(new Thickness(16, 11, 10, 11), surface.Padding); Colour(surface.Background, SwiftColor.Clear, surface);
            var row = Assert.IsType<Grid>(surface.Child); Assert.Equal(12, row.ColumnSpacing); Assert.Contains(row.Children.OfType<Border>(), b => b.MinWidth == 12); var lastSpacer = row.Children.OfType<Border>().Single(b => b.MinWidth == 12); Assert.True(Grid.GetColumn(row.Children[1]) < Grid.GetColumn(lastSpacer)); Assert.True(lastSpacer.Bounds.X >= row.Children[1].Bounds.Right); Assert.True(lastSpacer.Bounds.Width >= 12);
            var label = Text(add, "Add server"); Font(label, 13, FontWeight.Medium); Colour(label.Foreground, Chrome.PrimaryText, label);
            var glyph = Assert.Single(Nodes<SwiftImage>(add)); Assert.Equal("plus.circle", glyph.SymbolName); Assert.Equal(13, SwiftImage.GetFont(glyph)!.Resolve().FontSize); Colour(glyph.Foreground, Chrome.Accent, glyph); Assert.Equal(26, Assert.IsType<Border>(glyph.Parent).Width); Assert.Equal(HorizontalAlignment.Center, glyph.HorizontalAlignment);
            var rows = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(Assert.Single(Nodes<ChromeCard>(section)).Child).Child).Children[0]); Assert.Same(add.Parent, rows.Children[^1]);
            runtime.OnMain(() => PointerClick(add)); var popup = Assert.Single(Nodes<Popup>(section)); Assert.Same(add, popup.PlacementTarget); Assert.Equal(PlacementMode.Bottom, popup.Placement); Assert.True(presenter.Calls.Last(c => c.Popup == popup).Presented); runtime.OnMain(() => { Assert.Single(Nodes<MCPCustomEditor>(OpenContent(popup))); Dispatcher.UIThread.RunJobs(); });
            runtime.OnMain(() => Assert.Null(model.Mcp.SaveCustom(Custom()))); Assert.DoesNotContain(note.Text, Texts(section)); Assert.Contains(add, Nodes<Button>(section)); /* the same add row stays (MCPSettingsPage.swift:417-440); the open editor also has an "Add server" button, so a title lookup is ambiguous */
            var search = new MCPCustomSection("not-a-match"); using var second = runtime.OnMain(() => Attach(search, model)); Assert.Null(search.Child);
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-036")] // App/Views/Settings/MCPSettingsPage.swift:463-499
    public void Custom_rows_prove_colours_type_spacing_and_every_status_branch()
    {
        Ui((model, _, _, _) =>
        {
            var server = Custom();
            foreach (var state in new MCPConnectionState[] { new MCPConnectionState.NotConnected(), new MCPConnectionState.Connecting(), new MCPConnectionState.Connected(4), new MCPConnectionState.Failed("Fixture failure"), new MCPConnectionState.NeedsSetup(new MCPSetupGuide("Setup", "Reason", new[] { "Step" }, "Open", "https://example.invalid", null)) })
            {
                State(model, server.Id, state); var row = new MCPCustomRow(server); using var window = Attach(row, model);
                var border = Assert.IsType<Border>(row.Child); Assert.Equal(new Thickness(16, 11, 10, 11), border.Padding); var grid = Assert.IsType<Grid>(border.Child); Assert.Equal(12, grid.ColumnSpacing); Assert.Contains(grid.Children.OfType<Border>(), b => b.MinWidth == 12); SpacerOrder(grid, grid.Children[1], grid.Children[3], 12);
                var copy = Assert.Single(grid.Children.OfType<StackPanel>(), s => s.Spacing == 2); var name = Assert.IsType<TextBlock>(copy.Children[0]); var status = Assert.IsType<TextBlock>(copy.Children[1]); Font(name, 13, FontWeight.SemiBold); Colour(name.Foreground, Chrome.PrimaryText, name); Font(status, 11); Colour(status.Foreground, state is MCPConnectionState.Failed ? Chrome.Danger : Chrome.SecondaryText, status);
                Assert.Equal(state switch { MCPConnectionState.Connected => "4 tools · Custom", MCPConnectionState.Failed => "Fixture failure", _ => server.Summary }, status.Text);
                Assert.Equal(state is MCPConnectionState.Failed ? 3 : 0, status.MaxLines);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-MCP-009")] // App/Views/Settings/MCPSettingsPage.swift:165-168; MCPStore.swift:137-143
    [Trait("Req", "UI-MCP-039")] // App/Views/Settings/MCPSettingsPage.swift:516-520
    public void Catalog_and_custom_switches_persist_both_directions_and_rewrite_all_four_configs()
    {
        Flow((model, runtime, _, _) =>
        {
            foreach (var custom in new[] { false, true })
            {
                var server = Custom(); if (custom) runtime.OnMain(() => Assert.Null(model.Mcp.SaveCustom(server)));
                var entry = custom ? server.CatalogEntry() : MCPCatalog.Entry("github")!;
                // Fixture state from an offline successful probe; this test never launches a catalog command.
                runtime.OnMain(() => { if (!custom) foreach (var field in entry.Fields) Assert.True(MCPKeychain.Set("fixture-value", entry.Id, field.Key)); });
                Connection(model, new MCPConnection(entry.Id) { ConnectedAt = DateTimeOffset.UtcNow }); State(model, entry.Id, new MCPConnectionState.Connected(1));
                Control owner = custom ? new MCPCustomRow(server) : new MCPConnectedRow(entry); using var window = runtime.OnMain(() => Attach(owner, model));
                var toggle = Assert.Single(Nodes<SmallSwitch>(owner)); var slot = Assert.IsType<SettingsSwitch>(toggle.Parent);
                var options = Nodes<Button>(owner).Single(b => ToolTip.GetTip(b) as string == "Server options");
                Assert.True(Grid.GetColumn(slot) < Grid.GetColumn(Assert.IsType<Panel>(options.Parent))); // :165-169,516-520 switch before options / in the action slot
                runtime.OnMain(() => model.Mcp.SetEnabled(true, entry)); Exported(entry.Id, true); toggle = Assert.Single(Nodes<SmallSwitch>(owner));
                runtime.AdvanceMilliseconds(300); ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromMilliseconds(300)); Dispatcher.UIThread.RunJobs(); // the switch's track crossfades over 0.25 s (UI-SETTINGS-012, ED-280); the accent is drawn once it has settled
                var window0 = (Avalonia.Controls.Window)TopLevel.GetTopLevel(toggle)!; var theme0 = window0.RequestedThemeVariant;
                try { foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark }) { window0.RequestedThemeVariant = theme; Dispatcher.UIThread.RunJobs(); Assert.Equal(((ISolidColorBrush)Chrome.AccentNSColor.Brush(toggle)).Color, toggle.TrackNow()); } } finally { window0.RequestedThemeVariant = theme0; Dispatcher.UIThread.RunJobs(); }
                var drawing = new DrawingGroup(); using (var context = drawing.Open()) toggle.Render(context); Assert.Equal(toggle.TrackNow(), Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<GeometryDrawing>(drawing.Children[0]).Brush).Color); // shared switch's theme accent, Chrome.swift:90
                runtime.OnMain(() => KeyboardSpace(toggle));
                Assert.False(model.Mcp.Connection(entry.Id)!.IsEnabled); Assert.IsType<MCPConnectionState.Connected>(model.Mcp.State(entry.Id)); Exported(entry.Id, false);
                using (var persisted = JsonDocument.Parse(File.ReadAllText(MCPPaths.ConnectionsURL))) Assert.False(persisted.RootElement.EnumerateArray().Single().GetProperty("isEnabled").GetBoolean());
                Dispatcher.UIThread.RunJobs(); toggle = Assert.Single(Nodes<SmallSwitch>(owner));
                runtime.OnMain(() => PointerAt(toggle, new Point(toggle.Bounds.Width/2,toggle.Bounds.Height/2)));
                Assert.True(model.Mcp.Connection(entry.Id)!.IsEnabled); Exported(entry.Id, true);
                using (var persisted = JsonDocument.Parse(File.ReadAllText(MCPPaths.ConnectionsURL))) Assert.True(persisted.RootElement.EnumerateArray().Single().GetProperty("isEnabled").GetBoolean());
                if (custom) { runtime.OnMain(() => ObservableState(model, server.Id, new MCPConnectionState.NotConnected())); Assert.Empty(Nodes<SettingsSwitch>(owner)); Assert.NotNull(Button(owner, "Connect")); }
                runtime.OnMain(() => model.Mcp.Disconnect(entry));
            }
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:624-627,679-685; PR-MCP-229 structural subset
    // (Former STOP #2, PR-MCP-229: no frame of the editor exists in the videos; OD-0098 makes the Swift formula the proof, asserted on laid-out bounds in
    // MCPSettingsPageTestFirstTests and through the real presenter in MCPCustomEditorRealPresenterTests.)
    public void Real_editor_resizes_through_add_remove_and_kind_switch_including_empty_key_rows()
    {
        Ui((model, _, _, _) =>
        {
            var editor = new MCPCustomEditor(null); using var window = Attach(editor, model);
            Assert.Equal(380, editor.Width); Assert.Equal(450, editor.Height); // :624 width 380; :683 stdio base 450
            for (var i = 0; i < 3; i++) Click(Button(editor, "Add environment variable")); Assert.Equal(552, editor.Height); // :684 rows * 34
            Click(Button(editor, "Add environment variable")); Assert.Equal(560, editor.Height);
            Call(editor, "SetKind", MCPCustomServer.Kind.Http); Assert.Equal(456, editor.Height); // :683 http base 320, four rows kept
            for (var i = 0; i < 3; i++) Click(Button(editor, "Add header")); Assert.Equal(558, editor.Height);
            Click(Button(editor, "Add header")); Assert.Equal(560, editor.Height);
            Click(Nodes<Button>(editor).First(b => ToolTip.GetTip(b) as string == "Remove this variable")); Assert.Equal(558, editor.Height);
            Assert.Equal(7, Nodes<TextBox>(editor).Count(b => b.PlaceholderText == "KEY")); Assert.Empty(Read<MCPCustomServer>(editor, "Draft").Headers);
            Call(editor, "SetKind", MCPCustomServer.Kind.Stdio); Assert.Equal(560, editor.Height);
        });
    }
}
