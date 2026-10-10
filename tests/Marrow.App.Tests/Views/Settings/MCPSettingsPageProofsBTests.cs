using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Services.Models;
using System.Text.Json;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using UiWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :521-1141.
// Supplements the existing Third/Fourth tests; every assertion reads the production control.
// ED-181 is closed (2026-10-04): the continuous corner is Apple's curve; these tests assert the radius and style parameters.
[Collection("Headless")]
public sealed class MCPSettingsPageProofsBTests(HeadlessSession headless)
{
    private void Ui(Func<AppModel, TestRuntime, RecordingPresenter, Task> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        runtime.Loop.Run(async () => await test(new AppModel(), runtime, presenter));
        Dispatcher.UIThread.RunJobs();
    });

    private static UiWindow Show(Control control, AppModel model, ThemeVariant? theme = null)
    {
        PopoverCardContent.Lift(control); // a popover's view comes off the card the presenter hung it on
        AppEnvironment.SetAppModel(control, model);
        var window = new UiWindow { Content = control, Width = 800, Height = 800,
            RequestedThemeVariant = theme ?? ThemeVariant.Light };
        window.Show(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private static void Press(Button button)
    {
        // Real headless pointer dispatch, including the plain button's whole rectangular target.
        var window = Assert.IsType<UiWindow>(TopLevel.GetTopLevel(button));
        window.UpdateLayout(); button.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var clicked = false; EventHandler<Avalonia.Interactivity.RoutedEventArgs> mark = (_, _) => clicked = true;
        button.Click += mark;
        window.MouseMove(at, RawInputModifiers.None);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        button.Click -= mark;
        Assert.True(clicked, $"Pointer did not activate {AutomationProperties.GetName(button)} / {string.Join(',', Texts(button))} at {at}; bounds {button.Bounds}; hit path {string.Join('/', (window.InputHitTest(at) as Control)?.GetSelfAndVisualAncestors().OfType<Control>().Select(c => c.GetType().Name + c.Bounds) ?? Array.Empty<string>())}; scroll {string.Join('/', button.GetVisualAncestors().OfType<ScrollViewer>().Select(s => $"{s.Bounds} offset {s.Offset} extent {s.Extent} viewport {s.Viewport}"))}");
        Dispatcher.UIThread.RunJobs();
    }

    private static void Type(TextBox field, string text)
    {
        Assert.True(field.Focus());
        var window = Assert.IsType<UiWindow>(TopLevel.GetTopLevel(field));
        field.SelectAll(); window.KeyTextInput(text); Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock TextAt(Control control, string text) => Nodes<TextBlock>(control).Single(t => t.Text == text);
    private static void ColorIs(Control control, IBrush? brush, SwiftColor expected) =>
        Assert.Equal(expected.Resolve(control.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
    private static void TextStyle(TextBlock text, double size, FontWeight weight, SwiftColor color)
    {
        Assert.Equal(size, text.FontSize); Assert.Equal(weight, text.FontWeight);
        Assert.Equal(SwiftFont.System(size).Resolve().FontFamily, text.FontFamily);
        ColorIs(text, text.Foreground, color);
    }
    private static void Glyph(SwiftImage image, string symbol, double size, FontWeight weight, SwiftColor color)
    {
        Assert.Equal(symbol, image.SymbolName); var font = SwiftImage.GetFont(image)!.Resolve();
        Assert.Equal(size, font.FontSize); Assert.Equal(weight, font.FontWeight);
        Assert.Equal(SwiftFont.System(size).Resolve().FontFamily, font.FontFamily);
        ColorIs(image, image.Foreground, color);
    }
    private static void Surface(TextBox field, bool name = false, bool args = false)
    {
        // :732-742,756-791,894-898: padding, plain/focus-free field, 9 continuous, overlay(.06).
        var surface = Assert.IsType<Border>(field.Parent);
        Assert.Equal(new CornerRadius(9), surface.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface));
        ColorIs(surface, surface.Background, Chrome.PrimaryText.Opacity(0.06));
        Assert.Equal(args ? new Thickness(11, 4) : new Thickness(10, 7), field.Padding); // OD-0032: the source's 6 plus the TextEditor's own 5
        Assert.Null(field.FocusAdorner); Assert.Equal(default, field.BorderThickness);
        Assert.Equal(name ? 15 : 12, field.FontSize);
        Assert.Equal(name ? FontWeight.SemiBold : FontWeight.Normal, field.FontWeight);
        Assert.Equal(SwiftFont.System(12, design: name ? SwiftFont.Design.Default : SwiftFont.Design.Monospaced).Resolve().FontFamily, field.FontFamily);
        ColorIs(field, field.Foreground, Chrome.PrimaryText);
    }

    [Fact]
    [Trait("Req", "UI-MCP-042")] // App/Views/Settings/MCPSettingsPage.swift:542-554
    public void Options_have_secondary_body_glyph_and_toggle_the_bottom_anchor_in_every_state() => Ui(async (model, _, presenter) =>
    {
        await Task.Yield();
        var custom = Custom();
        foreach (var state in new MCPConnectionState[] { new MCPConnectionState.NotConnected(), new MCPConnectionState.Connecting(),
            new MCPConnectionState.Connected(2), new MCPConnectionState.Failed("Rejected"),
            new MCPConnectionState.NeedsSetup(new MCPSetupGuide("Setup", "Reason", new[] { "Step" }, "Open", "", null)) })
        {
            State(model, custom.Id, state); var row = new MCPCustomRow(custom); var window = Show(row, model);
            try
            {
                var options = Nodes<Button>(row).Single(b => ToolTip.GetTip(b) as string == "Server options");
                Assert.Equal(22, options.Width); Assert.Equal(22, options.Height); Assert.Null(options.Background);
                Glyph(Assert.Single(Nodes<SwiftImage>(options)), "ellipsis.circle", 13, FontWeight.Normal, Chrome.SecondaryText);
                var popups = Nodes<Popup>(row); Assert.Equal(3, popups.Length);
                Assert.All(popups, p => { Assert.Same(options, p.PlacementTarget); Assert.Equal(PlacementMode.Bottom, p.Placement); Assert.True(p.IsLightDismissEnabled); });
                presenter.Calls.Clear(); Press(options); await Task.Yield(); Assert.Contains(presenter.Calls, c => c.Popup == popups[0] && c.Presented);
                Press(options); await Task.Yield(); Assert.False(presenter.Calls.Last().Presented);
            }
            finally { window.Close(); }
        }
    });

    private static void MenuStyle(Control menu, string title, string symbol, bool destructive, bool enabled)
    {
        // Popovers.swift:275-335 shared row: 8 spacing/padding, height 26, body 13, symbol callout 12,
        // continuous 6, clear idle fill, danger/primary foreground, disabled opacity .4.
        var button = Button(menu, title); Assert.Equal(enabled, button.IsEnabled); Assert.Equal(enabled ? 1 : 0.4, button.Opacity);
        var border = Assert.IsType<Border>(button.Content); Assert.Equal(new CornerRadius(6), border.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(border));
        ColorIs(border, border.Background, SwiftColor.Clear);
        var row = Assert.IsAssignableFrom<StackPanel>(border.Child); Assert.Equal(8, row.Spacing); Assert.Equal(26, row.Height); Assert.Equal(new Thickness(8, 0), row.Margin);
        var text = Assert.Single(Nodes<MiddleTruncatingText>(button));
        TextStyle(text, 13, FontWeight.Normal, destructive ? Chrome.Danger : Chrome.PrimaryText);
        Glyph(Assert.Single(Nodes<SwiftImage>(button)), symbol, 12, FontWeight.Normal, destructive ? Chrome.Danger : Chrome.PrimaryText);
    }

    [Fact]
    [Trait("Req", "UI-MCP-043")] // App/Views/Settings/MCPSettingsPage.swift:556-559
    [Trait("Req", "UI-MCP-045")] // App/Views/Settings/MCPSettingsPage.swift:563; UI/Chrome/Popovers.swift:152-158
    public void Edit_has_normal_shared_style_closes_before_opening_edit_mode_and_divider_is_exact() => Ui(async (model, runtime, presenter) =>
    {
        await Task.Yield();
        var row = new MCPCustomRow(Custom()); var window = Show(row, model);
        try
        {
            Press(Nodes<Button>(row).Single(b => ToolTip.GetTip(b) as string == "Server options")); await Task.Yield();
            var menu = Invoke<Control>(row, "BuildMenu"); var menus = Nodes<Popup>(row);
            Popovers.SetClosePopover(menu, () => Read<object>(row, "view").GetType().GetProperty("Menu")!.SetValue(Read<object>(row, "view"), false));
            var menuWindow = Show(menu, model);
            try
            {
                MenuStyle(menu, "Edit", "pencil", false, true);
                var divider = Assert.IsType<Border>(Assert.Single(Nodes<PopoverDivider>(menu)).Child);
                Assert.Equal(1, divider.Height); Assert.Equal(new Thickness(8, 5), divider.Margin);
                // PopoverDivider's documented shared separator decision: primary.opacity(.22).
                ColorIs(divider, divider.Background, Chrome.PrimaryText.Opacity(0.22));
                presenter.Calls.Clear(); Press(Button(menu, "Edit")); await Task.Yield();
                Assert.Contains(presenter.Calls, c => c.Popup == menus[0] && !c.Presented);
                await Task.Yield(); Dispatcher.UIThread.RunJobs();
                Assert.Contains(presenter.Calls, c => c.Popup == menus[1] && c.Presented);
                Assert.True(presenter.Calls.FindIndex(c => c.Popup == menus[0] && !c.Presented) < presenter.Calls.FindIndex(c => c.Popup == menus[1] && c.Presented));
                var editor = Assert.IsType<MCPCustomEditor>(MCPSettingsPage.PopoverContent(menus[1]));
                Assert.Contains("Example", Texts(editor)); Assert.Equal("custom-example", Read<MCPCustomServer>(editor, "Draft").Id);
                Assert.True(Popovers.GetIsCapsuleButtonBorderShape(editor)); // real presentedChrome factory, not a test copy
            }
            finally { menuWindow.Close(); }
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-044")] // App/Views/Settings/MCPSettingsPage.swift:560-562; Services/MCP/MCPStore.swift:115-128; OD-0012 E6
    public void Disconnect_has_disabled_destructive_style_and_deletes_or_reports_stored_secrets() => Ui(async (model, runtime, _) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false };
        using var secrets = SecretStore.Shared.Override(new SecretStore(backend)); var custom = Custom(1);
        Assert.Null(model.Mcp.SaveCustom(custom));
        foreach (var state in new MCPConnectionState[] { new MCPConnectionState.NotConnected(), new MCPConnectionState.Connecting(), new MCPConnectionState.Failed("Rejected"),
            new MCPConnectionState.NeedsSetup(new MCPSetupGuide("Setup", "Reason", new[] { "Step" }, "Open", "", null)) })
        {
            State(model, custom.Id, state); var row = new MCPCustomRow(custom); var window = Show(row, model);
            try { var menu = Invoke<Control>(row, "BuildMenu"); var mw = Show(menu, model); try { MenuStyle(menu, "Disconnect", "xmark.circle", true, false); } finally { mw.Close(); } }
            finally { window.Close(); }
        }
        foreach (var refuse in new[] { false, true })
        {
            backend.RefuseDeletes = false; Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "protected-value", true) }, custom.Id, null));
            backend.RefuseDeletes = refuse; State(model, custom.Id, new MCPConnectionState.Connected(1)); Connection(model, new MCPConnection(custom.Id));
            var row = new MCPCustomRow(custom); var window = Show(row, model); var menu = Invoke<Control>(row, "BuildMenu"); var mw = Show(menu, model);
            try
            {
                MenuStyle(menu, "Disconnect", "xmark.circle", true, true); Press(Button(menu, "Disconnect")); await Task.Yield(); await Task.Yield();
                Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State(custom.Id)); Assert.Null(model.Mcp.Connection(custom.Id));
                if (refuse) Assert.Equal("Disconnected, but Credential Manager did not remove this server’s stored values.", model.Mcp.CredentialDeletionFailure);
                else { Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", true)); Assert.Null(model.Mcp.CredentialDeletionFailure); }
            }
            finally { mw.Close(); window.Close(); }
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-046")] // App/Views/Settings/MCPSettingsPage.swift:564-566; Services/MCP/MCPStore.swift:187-193; OD-0012 E1
    public void Delete_has_destructive_style_and_obeys_both_confirmation_settings() => Ui(async (model, runtime, _) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        foreach (var confirm in new[] { true, false })
        {
            model.Settings.ConfirmBeforeDeleting = confirm; var custom = Custom(1); Assert.Null(model.Mcp.SaveCustom(custom));
            var definitionBefore = File.ReadAllBytes(MCPPaths.CustomsURL); // MCPStore.swift:187-193 deletes and saves the definition
            Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "protected-value", true) }, custom.Id, null));
            State(model, custom.Id, new MCPConnectionState.Connected(1)); Connection(model, new MCPConnection(custom.Id));
            var row = new MCPCustomRow(custom); var w = Show(row, model); var menu = Invoke<Control>(row, "BuildMenu"); var mw = Show(menu, model);
            try
            {
                MenuStyle(menu, "Delete", "trash", true, true); Press(Button(menu, "Delete")); await Task.Yield(); await Task.Yield();
                if (confirm)
                {
                    Assert.Single(model.Mcp.Customs); Assert.Equal("protected-value", model.Mcp.StoredValue(custom.Id, "KEY0", true));
                    var confirmation = Read<Control>(row, "DeleteConfirmation"); var cw = Show(confirmation, model);
                    try
                    {
                        Assert.Contains("Delete this server?", Texts(confirmation));
                        Assert.Contains("“Example” and the values it keeps in Credential Manager will be removed.", Texts(confirmation));
                        MenuStyle(confirmation, "Delete server", "trash", true, true);
                        Popovers.GetClosePopover(confirmation)!.Invoke(); // dismissal cancels without changing persisted state
                        Assert.Single(model.Mcp.Customs); Assert.NotNull(model.Mcp.Connection(custom.Id));
                        Assert.Equal(definitionBefore, File.ReadAllBytes(MCPPaths.CustomsURL));
                        Assert.Equal("protected-value", model.Mcp.StoredValue(custom.Id, "KEY0", true));
                        Press(Button(menu, "Delete")); await Task.Yield(); await Task.Yield();
                        var second = Read<Control>(row, "DeleteConfirmation"); var sw = Show(second, model);
                        try { Press(Button(second, "Delete server")); await Task.Yield(); await Task.Yield(); } finally { sw.Close(); }
                    }
                    finally { cw.Close(); }
                }
                Assert.Empty(model.Mcp.Customs); Assert.Null(model.Mcp.Connection(custom.Id)); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", true));
                using var definitions = JsonDocument.Parse(File.ReadAllBytes(MCPPaths.CustomsURL)); Assert.Equal(0, definitions.RootElement.GetArrayLength());
            }
            finally { mw.Close(); w.Close(); }
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-048")] // App/Views/Settings/MCPSettingsPage.swift:706-730
    [Trait("Req", "UI-MCP-049")] // App/Views/Settings/MCPSettingsPage.swift:718-728
    public void Header_has_source_alignment_spacer_colours_and_close_square_discards_the_draft() => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        foreach (var edit in new[] { false, true })
        {
            var editor = new MCPCustomEditor(edit ? Custom() : null); var w = Show(editor, model);
            try
            {
                var title = TextAt(editor, edit ? "Example" : "New server"); TextStyle(title, 14, FontWeight.SemiBold, Chrome.PrimaryText);
                var subtitle = TextAt(editor, "Custom · stdio"); TextStyle(subtitle, 11, FontWeight.Normal, Chrome.SecondaryText);
                var copy = Assert.IsType<StackPanel>(title.Parent); Assert.Equal(1, copy.Spacing); Assert.Equal(VerticalAlignment.Top, copy.VerticalAlignment);
                var header = Assert.IsType<Grid>(copy.Parent); Assert.Equal(10, header.ColumnSpacing);
                Assert.Contains(header.Children, c => c.MinWidth == 8); Assert.Equal(26, Assert.Single(Nodes<MCPIcon>(header)).Child!.Width);
                var close = Button(editor, "Close"); Assert.Equal(24, close.Width); Assert.Equal(24, close.Height); Assert.Null(close.Background);
                Glyph(Assert.Single(Nodes<SwiftImage>(close)), "xmark", 11, FontWeight.SemiBold, Chrome.SecondaryText);
                var closed = false; Popovers.SetClosePopover(editor, () => closed = true); Type(Field(editor, "My server"), "Discard me");
                // :724-725 rectangular contentShape: a corner inside the square is a live target.
                var corner = close.TranslatePoint(new Point(1, 1), w)!.Value;
                w.MouseDown(corner, MouseButton.Left, RawInputModifiers.None); w.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);
                Assert.True(closed); Assert.Empty(model.Mcp.Customs);
                Call(editor, "SetKind", MCPCustomServer.Kind.Http); TextStyle(TextAt(editor, "Custom · http"), 11, FontWeight.Normal, Chrome.SecondaryText);
            }
            finally { w.Close(); }
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-050")] // App/Views/Settings/MCPSettingsPage.swift:732-753,663-665
    [Trait("Req", "UI-MCP-052")] // App/Views/Settings/MCPSettingsPage.swift:643-645,756-764,850-854
    [Trait("Req", "UI-MCP-053")] // App/Views/Settings/MCPSettingsPage.swift:777-791,851-858
    [Trait("Req", "UI-MCP-054")] // App/Views/Settings/MCPSettingsPage.swift:649-651,756-764,854
    [Trait("Req", "UI-MCP-096")] // App/Views/Settings/MCPSettingsPage.swift:892-898
    public void Fields_have_source_colours_continuous_surfaces_and_real_typing_updates_the_draft() => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        var editor = new MCPCustomEditor(null); var w = Show(editor, model);
        try
        {
            var name = Field(editor, "My server"); Assert.True(name.IsFocused); Surface(name, name: true);
            var label = TextAt(editor, "Name"); TextStyle(label, 11, FontWeight.SemiBold, Chrome.SecondaryText); Assert.Equal(5, Assert.IsType<StackPanel>(label.Parent).Spacing);
            Type(name, "A typed server"); Assert.Equal(MCPCustomServer.MakeID("A typed server"), Read<MCPCustomServer>(editor, "Draft").Id);
            var command = Field(editor, "npx"); Surface(command); Type(command, "  offline-fixture  ");
            Assert.Equal("offline-fixture", Read<MCPCustomServer>(editor, "Draft").Command);
            var args = Nodes<TextBox>(editor).Single(f => f.AcceptsReturn); Surface(args, args: true); Assert.Equal(66, args.Height);
            Type(args, "--flag\n\n argument with spaces \n  ");
            Assert.Equal(new[] { "--flag", " argument with spaces " }, Read<MCPCustomServer>(editor, "Draft").Args);
            TextStyle(TextAt(editor, "One argument per line; empty lines are dropped."), 10.5, FontWeight.Normal, Chrome.SecondaryText);
            Call(editor, "SetKind", MCPCustomServer.Kind.Http); Assert.DoesNotContain(Nodes<TextBox>(editor), f => f.PlaceholderText == "npx" || f.AcceptsReturn);
            var url = Field(editor, "https://…"); Surface(url); Type(url, "  https://example.invalid/mcp  ");
            Assert.Equal("https://example.invalid/mcp", Read<MCPCustomServer>(editor, "Draft").Url);
            Call(editor, "SetKind", MCPCustomServer.Kind.Stdio); Assert.DoesNotContain(Nodes<TextBox>(editor), f => f.PlaceholderText == "https://…");
            Assert.Equal("  offline-fixture  ", Field(editor, "npx").Text);
        }
        finally { w.Close(); }
        var edit = new MCPCustomEditor(Custom()); var second = Show(edit, model);
        try { Assert.False(Field(edit, "My server").IsFocused); } finally { second.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-055")] // App/Views/Settings/MCPSettingsPage.swift:794-840
    [Trait("Req", "UI-MCP-056")] // App/Views/Settings/MCPSettingsPage.swift:798,846-849
    [Trait("Req", "UI-MCP-057")] // App/Views/Settings/MCPSettingsPage.swift:799-803,767-774
    [Trait("Req", "UI-MCP-058")] // App/Views/Settings/MCPSettingsPage.swift:804-814,864-881
    [Trait("Req", "UI-MCP-059")] // App/Views/Settings/MCPSettingsPage.swift:815-825
    [Trait("Req", "UI-MCP-060")] // App/Views/Settings/MCPSettingsPage.swift:828-835
    [Trait("Req", "UI-MCP-061")] // App/Views/Settings/MCPSettingsPage.swift:836-838
    public void Variable_colours_surfaces_glyphs_and_store_changes_follow_real_add_lock_save_remove_input() => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        var custom = Custom(1); Assert.Null(model.Mcp.SaveCustom(custom));
        Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "old-plain", false) }, custom.Id, null));
        var editor = new MCPCustomEditor(custom); var w = Show(editor, model);
        try
        {
            TextStyle(TextAt(editor, "Environment"), 11, FontWeight.SemiBold, Chrome.SecondaryText);
            var key = Field(editor, "KEY"); var value = Field(editor, "value"); Surface(key); Surface(value);
            Assert.Equal(6, Assert.IsType<Grid>(key.Parent!.Parent).ColumnSpacing);
            Type(value, "new-protected"); Assert.Equal("old-plain", model.Mcp.StoredValue(custom.Id, "KEY0", false));
            var toggle = Nodes<Button>(editor).Single(b => ToolTip.GetTip(b) as string == "Stored in mcp.json; click to keep it in the Credential Manager instead");
            Assert.Equal(24, toggle.Width); Assert.Equal(24, toggle.Height);
            Glyph(Assert.Single(Nodes<SwiftImage>(toggle)), "lock.open", 12, FontWeight.Medium, Chrome.SecondaryText);
            Press(toggle); await Task.Yield(); Surface(Field(editor, "value"));
            Glyph(Assert.Single(Nodes<SwiftImage>(toggle)), "lock.fill", 12, FontWeight.Medium, Chrome.Accent);
            Assert.Equal("Kept in the Credential Manager", ToolTip.GetTip(toggle));
            var presenter = Field(editor, "value").GetVisualDescendants().OfType<TextPresenter>().Single();
            Assert.Equal('\u2022', presenter.PasswordChar); Assert.False(presenter.RevealPassword);
            Assert.Equal("old-plain", model.Mcp.StoredValue(custom.Id, "KEY0", false)); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", true));
            Press(Button(editor, "Save")); await Task.Yield(); Assert.Equal("new-protected", model.Mcp.StoredValue(custom.Id, "KEY0", true)); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", false));
            Press(toggle); await Task.Yield(); Type(Field(editor, "value"), "new-plain");
            Assert.Equal(default(char), Field(editor, "value").PasswordChar); Assert.Equal("new-protected", model.Mcp.StoredValue(custom.Id, "KEY0", true));
            Press(Button(editor, "Save")); await Task.Yield(); Assert.Equal("new-plain", model.Mcp.StoredValue(custom.Id, "KEY0", false)); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", true));
            var add = Button(editor, "Add environment variable"); Assert.Null(add.Background); Assert.True(double.IsNaN(add.Width));
            Glyph(Assert.Single(Nodes<SwiftImage>(add)), "plus", 12, FontWeight.Medium, Chrome.Accent);
            TextStyle(TextAt(add, "Add environment variable"), 12, FontWeight.Medium, Chrome.Accent);
            Press(add); await Task.Yield(); Assert.Equal(2, Nodes<TextBox>(editor).Count(f => f.PlaceholderText == "KEY"));
            var blankKey = Nodes<TextBox>(editor).Last(f => f.PlaceholderText == "KEY"); Type(blankKey, "  ");
            Assert.Single(Read<MCPCustomServer>(editor, "Draft").Env);
            var blankValue = Nodes<TextBox>(editor).Last(f => f.PlaceholderText == "value"); Assert.Equal("", blankValue.Text); Assert.Equal(default(char), blankValue.PasswordChar);
            var remove = Nodes<Button>(editor).Last(b => ToolTip.GetTip(b) as string == "Remove this variable");
            Assert.True(remove.IsEnabled); Assert.Equal(24, remove.Width); Assert.Equal(24, remove.Height);
            Glyph(Assert.Single(Nodes<SwiftImage>(remove)), "minus.circle", 12, FontWeight.Medium, Chrome.SecondaryText);
            Press(remove); await Task.Yield(); Assert.Single(Nodes<TextBox>(editor), f => f.PlaceholderText == "KEY");
            Call(editor, "SetKind", MCPCustomServer.Kind.Http); Assert.Equal("KEY0", Field(editor, "KEY").Text); Assert.Equal("new-plain", Field(editor, "value").Text);
            TextStyle(TextAt(editor, "Headers"), 11, FontWeight.SemiBold, Chrome.SecondaryText);
            var addHeader = Button(editor, "Add header"); TextStyle(TextAt(addHeader, "Add header"), 12, FontWeight.Medium, Chrome.Accent);
            Press(addHeader); await Task.Yield(); Assert.Equal(2, Nodes<TextBox>(editor).Count(f => f.PlaceholderText == "KEY"));
            var hint = TextAt(editor, "Locked values are kept in the Credential Manager."); TextStyle(hint, 10.5, FontWeight.Normal, Chrome.SecondaryText);
            Assert.Same(addHeader.Parent, hint.Parent);
        }
        finally { w.Close(); }
        var empty = new MCPCustomEditor(null); var ew = Show(empty, model);
        try { Assert.DoesNotContain(Nodes<TextBox>(empty), f => f.PlaceholderText == "KEY"); Assert.NotNull(Button(empty, "Add environment variable")); Assert.Contains("Locked values are kept in the Credential Manager.", Texts(empty)); }
        finally { ew.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-062")] // App/Views/Settings/MCPSettingsPage.swift:687-696
    [Trait("Req", "UI-MCP-095")] // App/Views/Settings/MCPSettingsPage.swift:629-658,687-704
    public void Footer_has_danger_error_and_ten_spacing_and_stays_fixed_when_the_real_form_scrolls() => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        var editor = new MCPCustomEditor(null); var w = Show(editor, model);
        try
        {
            var save = Button(editor, "Add server"); var footer = Assert.IsType<StackPanel>(save.Parent); Assert.Single(footer.Children); Assert.Equal(10, footer.Spacing);
            var root = Assert.IsType<Grid>(footer.Parent); Assert.Equal(12, root.RowSpacing); Assert.Equal(2, Grid.GetRow(footer));
            var scroll = Assert.Single(Nodes<ScrollViewer>(editor), s => s.Content is StackPanel); Assert.Equal(1, Grid.GetRow(scroll));
            Assert.Equal(12, Assert.IsType<StackPanel>(scroll.Content).Spacing); Assert.Equal(HorizontalAlignment.Right, save.HorizontalAlignment);
            for (var i = 0; i < 5; i++) Press(Button(editor, "Add environment variable")); await Task.Yield();
            w.UpdateLayout(); var fixedPosition = save.TranslatePoint(default, w)!.Value;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            scroll.Offset = new Vector(0, scroll.Extent.Height); w.UpdateLayout(); Assert.True(scroll.Offset.Y > 0);
            Assert.Equal(fixedPosition, save.TranslatePoint(default, w)!.Value);
            scroll.Offset = default; w.UpdateLayout(); Assert.Equal(fixedPosition, save.TranslatePoint(default, w)!.Value);
            Press(save); await Task.Yield(); var error = TextAt(editor, "The server needs a name."); TextStyle(error, 11, FontWeight.Normal, Chrome.Danger);
            Assert.Equal(3, error.MaxLines); Assert.Equal(TextWrapping.Wrap, error.TextWrapping); Assert.Same(error, footer.Children[0]);
            Assert.DoesNotContain(error, Nodes<TextBlock>(Assert.IsAssignableFrom<Control>(scroll.Content)));
            w.UpdateLayout(); var errorSave = Button(editor, "Add server"); var afterError = errorSave.TranslatePoint(default, w)!.Value;
            scroll.Offset = new Vector(0, scroll.Extent.Height); w.UpdateLayout(); Assert.Equal(afterError, errorSave.TranslatePoint(default, w)!.Value);
            Assert.True(errorSave.Bounds.Height > 0); Assert.True(error.Bounds.Height > 0);
        }
        finally { w.Close(); }
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "UI-MCP-065")] // App/Views/Settings/MCPSettingsPage.swift:975-985
    [Trait("Req", "UI-MCP-066")] // App/Views/Settings/MCPSettingsPage.swift:990-993,1026-1036
    [Trait("Req", "UI-MCP-067")] // App/Views/Settings/MCPSettingsPage.swift:994-1000,1073-1121
    public void Info_header_details_and_tools_have_source_colours_typography_and_baselines(bool dark) => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        var entry = Entry() with { SampleTools = new[] { "read", "write" } }; var info = new MCPInfoPopover(entry); var w = Show(info, model, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        try
        {
            var title = TextAt(info, "Example"); TextStyle(title, 14, FontWeight.SemiBold, Chrome.PrimaryText);
            TextStyle(TextAt(info, "Vendor · Developer"), 11, FontWeight.Normal, Chrome.SecondaryText);
            var copy = Assert.IsType<StackPanel>(title.Parent); Assert.Equal(1, copy.Spacing); Assert.Equal(VerticalAlignment.Center, copy.VerticalAlignment);
            Assert.Equal(10, Assert.IsType<StackPanel>(copy.Parent).Spacing); Assert.Equal(28, Assert.Single(Nodes<MCPIcon>(info)).Child!.Width);
            TextStyle(TextAt(info, "Summary"), 12, FontWeight.Normal, Chrome.PrimaryText.Opacity(0.9));
            foreach (var label in new[] { "Connects", "Needs" })
            {
                var key = TextAt(info, label); TextStyle(key, 11, FontWeight.SemiBold, Chrome.SecondaryText); Assert.Equal(58, key.Width);
                var row = Assert.IsAssignableFrom<Panel>(key.Parent); var value = row.Children.OfType<TextBlock>().Single(t => t != key);
                TextStyle(value, 11.5, FontWeight.Normal, Chrome.PrimaryText.Opacity(0.9)); Assert.Equal(TextWrapping.Wrap, value.TextWrapping);
                // :1027 HStack(alignment: .firstTextBaseline, spacing: 8): compare actual arranged first baselines.
                Assert.Equal(key.Bounds.Right + 8, value.Bounds.Left, 5);
                Assert.Equal(key.Bounds.Top + key.TextLayout.TextLines[0].Baseline, value.Bounds.Top + value.TextLayout.TextLines[0].Baseline, 5);
                Assert.Equal(8, Assert.IsType<StackPanel>(row.Parent).Spacing);
            }
            var heading = TextAt(info, "Tools"); TextStyle(heading, 11, FontWeight.SemiBold, Chrome.SecondaryText); Assert.Equal(6, Assert.IsType<StackPanel>(heading.Parent).Spacing);
            var flow = Assert.IsType<FlowLayout>(Assert.Single(Nodes<FlowChips>(info)).Child); Assert.Equal(6, flow.Spacing);
            Assert.All(flow.Children.OfType<Border>(), b =>
            {
                Assert.Equal(new Thickness(7, 3), b.Padding); Assert.Equal(new CornerRadius(6), b.CornerRadius);
                Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(b)); ColorIs(b, b.Background, Chrome.PrimaryText.Opacity(0.08));
                var text = Assert.IsType<TextBlock>(b.Child); Assert.Equal(10.5, text.FontSize);
                Assert.Equal(SwiftFont.System(10.5, design: SwiftFont.Design.Monospaced).Resolve().FontFamily, text.FontFamily);
                ColorIs(text, text.Foreground, Chrome.PrimaryText.Opacity(0.85)); Assert.Empty(Nodes<Button>(b));
            });
        }
        finally { w.Close(); }
        var empty = new MCPInfoPopover(Custom().CatalogEntry()); Assert.Empty(Nodes<FlowChips>(empty)); Assert.DoesNotContain("Tools", Texts(empty));
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "UI-MCP-089")] // App/Views/Settings/MCPSettingsPage.swift:60-62
    [Trait("Req", "UI-MCP-093")] // App/Views/Settings/MCPSettingsPage.swift:49-59; UI/Chrome/Chrome.swift:1037-1052
    [Trait("Req", "UI-MCP-094")] // App/Views/Settings/MCPSettingsPage.swift:95-98
    public void Page_footer_categories_and_lead_title_have_exact_colours_and_layout_in_both_appearances(bool dark) => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        var page = new MCPSettingsPage(""); var w = Show(page, model, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        try
        {
            var stack = Assert.IsType<StackPanel>(page.Child); Assert.Equal(20, stack.Spacing);
            var footer = Assert.IsType<TextBlock>(stack.Children[^1]); TextStyle(footer, 11, FontWeight.Normal, Chrome.SecondaryText);
            Assert.Equal("Connected servers reach every provider when a thread starts: the CLIs at launch, the API models through Nth itself.", footer.Text);
            var title = TextAt(page, "Connected servers lead"); TextStyle(title, 13, FontWeight.SemiBold, Chrome.PrimaryText);
            var titleRow = Assert.IsType<StackPanel>(title.Parent); Assert.Equal(4, titleRow.Spacing); Assert.Same(title, titleRow.Children[0]); Assert.IsType<MCPHoverInfoButton>(titleRow.Children[1]);
            var sections = Nodes<ChromeSection>(page).Skip(1).ToArray();
            Assert.Equal(new[] { "Developer", "Browser", "Search", "Work", "Data", "Cloud", "Knowledge" }, sections.Select(s => Texts(s)[0]));
            Assert.All(sections, s =>
            {
                var section = Assert.IsType<StackPanel>(s.Child); Assert.Equal(10, section.Spacing);
                var header = Assert.IsAssignableFrom<StackPanel>(section.Children[0]); Assert.Equal(4, header.Spacing);
                TextStyle(Assert.IsType<TextBlock>(header.Children[0]), 13, FontWeight.SemiBold, Chrome.SecondaryText);
            });
            page.SetQuery("absent in every catalog"); Assert.Empty(Nodes<ChromeSection>(page)); Assert.Same(footer, stack.Children[^1]);
            var developer = MCPCatalog.ByCategory.Single(g => g.Category == MCPCategory.Developer);
            foreach (var entry in developer.Entries) State(model, entry.Id, new MCPConnectionState.Connected(1));
            page.SetQuery(""); Assert.DoesNotContain(Nodes<ChromeSection>(page), s => Texts(s)[0] == "Developer");
        }
        finally { w.Close(); }
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "UI-MCP-091")] // App/Views/Settings/MCPSettingsPage.swift:1124-1141; Core/Models/MCP.swift:255-263
    public void Icons_use_primary_for_fallback_and_monochrome_and_exact_rgb_for_brand_assets(bool dark) => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        foreach (var size in new[] { 22d, 26d, 28d })
        foreach (var mode in new[] { 0, 1, 2 })
        {
            // Supplied 0x123456 is test input; Swift :1132's entry.color defines the expected RGB mapping.
            var entry = Entry() with { Asset = mode == 0 ? "" : "mcp-github", IsMonochrome = mode == 1, ColorValue = 0x123456 };
            var icon = new MCPIcon(entry, size); var w = Show(icon, model, dark ? ThemeVariant.Dark : ThemeVariant.Light);
            try
            {
                var image = Assert.IsType<SwiftImage>(icon.Child); Assert.Equal(size, image.Width); Assert.Equal(size, image.Height);
                Assert.True(image.IsResizable); Assert.Equal(ContentMode.Fit, image.AspectContentMode); Assert.Equal(ImageRenderingMode.Template, image.EffectiveRenderingMode);
                ColorIs(image, image.Foreground, mode == 2 ? new SwiftColor(0x12 / 255d, 0x34 / 255d, 0x56 / 255d) : Chrome.PrimaryText);
                if (mode == 0) Assert.Equal("server.rack", image.SymbolName); else Assert.Equal("mcp-github", image.AssetName);
            }
            finally { w.Close(); }
        }
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:521-528,698-702; partial source-defined glass data
    public void Custom_setup_retry_and_save_exercise_source_defined_glass_data_and_setup_presentation() => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        // The pill's size and tint are proven in MCPSettingsPageActionPillTests (UI-MCP-024, 041, and the size of 040, 063);
        // this keeps the untinted-window data and the setup presentation.
        var custom = Custom(); var guide = new MCPSetupGuide("Setup", "Reason", new[] { "Step" }, "Open", "", null);
        foreach (var failed in new[] { false, true })
        {
            State(model, custom.Id, failed ? new MCPConnectionState.Failed("Rejected") : new MCPConnectionState.NeedsSetup(guide));
            var row = new MCPCustomRow(custom); var w = Show(row, model);
            try
            {
                var button = Button(row, failed ? "Try again" : "Fix it");
                TextStyle(TextAt(button, failed ? "Try again" : "Fix it"), 11, FontWeight.Normal, failed ? Chrome.PrimaryText : SwiftColor.White); // an untinted window: neutral glass for .glass, the accent fill under white for .glassProminent
                var effect = Nodes<Border>(button).Select(SwiftGlass.GetEffect).Single(e => e is not null)!;
                if (failed)
                {
                    Assert.Null(effect.TintColor); Assert.True(effect.IsInteractive);
                    Press(button); Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State(custom.Id));
                    // Cancel the queued probe before yielding; no external process is launched (ED-211).
                    model.Mcp.Disconnect(custom.CatalogEntry()); await Task.Yield();
                }
                else
                {
                    Assert.NotNull(effect.TintColor); Assert.False(MCPSetupPanel.Shared.IsPresenting(custom.CatalogEntry()));
                    Press(button); await Task.Yield(); Assert.True(MCPSetupPanel.Shared.IsPresenting(custom.CatalogEntry())); MCPSetupPanel.Shared.Close();
                }
            }
            finally { w.Close(); }
        }
        foreach (var edit in new[] { false, true })
        {
            var editor = new MCPCustomEditor(edit ? custom : null); var w = Show(editor, model);
            try
            {
                var button = Button(editor, edit ? "Save" : "Add server"); Assert.True(button.IsEnabled);
                Assert.Contains(Nodes<Border>(button), b => SwiftGlass.GetEffect(b)?.TintColor is not null);
            }
            finally { w.Close(); }
        }
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:635-640,624-685,973-1024
    public void Editor_container_picker_and_info_container_keep_source_geometry_and_foregrounds() => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        // STOP opencode-proofs-mcp-b #2: UI-MCP-051. Shared GlassPickerButton's F.GlassMaterial
        // is deferred under ED-180. Geometry/state proofs exist; complete shared style cannot be claimed.
        // STOP opencode-proofs-mcp-b #3: UI-MCP-064 (and the rest of UI-MCP-047, below). UI-MCP-047's container, height formula, focus rule and presented chrome
        // (capsule shape and tint, Popovers.swift:8-12; the record's "background" is the native popover's own) are proven from the Swift (OD-0098) by
        // MCPCustomEditorSwiftProofTests; the editor through the REAL presenter (overlay host: shown, Close, press outside) is MCPCustomEditorRealPresenterTests and the
        // info popover's hover onto the popover is MCPSettingsPageInfoLinksTests. Escape dismissal is MCPCustomEditorRealPresenterTests (the overlay host), SettingsEscapeTests (the Settings window)
        // and MCPCustomEditorNativeTests (the editor in its own native popup window over the real Settings window): PopoverHost.Listen hands the owner window's Escape to the popover.
        var editor = new MCPCustomEditor(null); var w = Show(editor, model);
        try
        {
            Assert.Equal(380, editor.Width); Assert.Equal(450, editor.Height); Assert.Equal(new Thickness(16), Assert.IsType<Border>(editor.Child).Padding);
            TextStyle(TextAt(editor, "Kind"), 11, FontWeight.SemiBold, Chrome.SecondaryText);
            var picker = Assert.Single(Nodes<GlassPickerButton<MCPCustomServer.Kind>>(editor)); Assert.NotNull(picker);
            Call(editor, "SetKind", MCPCustomServer.Kind.Http); Assert.Equal(320, editor.Height);
            for (var i = 0; i < 8; i++) { Press(Button(editor, "Add header")); await Task.Yield(); Assert.Equal(Math.Min(560, 320 + (i + 1) * 34), editor.Height); }
            Call(editor, "SetKind", MCPCustomServer.Kind.Stdio); Assert.Equal(560, editor.Height);
        }
        finally { w.Close(); }
        var info = new MCPInfoPopover(Entry()); var iw = Show(info, model);
        try
        {
            var border = Assert.IsType<Border>(info.Child); Assert.Equal(340, border.Width); Assert.Equal(new Thickness(16), border.Padding);
            Assert.Equal(14, Assert.IsType<StackPanel>(border.Child).Spacing);
            TextStyle(TextAt(info, "Summary"), 12, FontWeight.Normal, Chrome.PrimaryText.Opacity(0.9));
        }
        finally { iw.Close(); }
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:1002-1019
    public void Optional_and_docs_links_have_accent_styles_and_source_presence_conditions() => Ui(async (model, _, _) =>
    {
        await Task.Yield();
        // (Former STOP #4, UI-MCP-068 UI-MCP-069: CLOSED. MCPSettingsPageInfoLinksTests records both links' destinations through MCPSetupLauncherScope;
        // no browser is ever opened. This test keeps the style and presence subset.)
        var entry = Entry() with { DocsURL = "https://example.invalid/docs", Fields = new[] { new MCPField("key", "API Key", "", IsRequired: false) } };
        var info = new MCPInfoPopover(entry); var w = Show(info, model);
        try
        {
            var optional = Button(info, "Change the api key…"); Assert.Null(optional.Background); Assert.True(double.IsNaN(optional.Width));
            TextStyle(TextAt(optional, "Change the api key…"), 12, FontWeight.Medium, Chrome.Accent);
            var docs = Button(info, "Read the docs"); TextStyle(TextAt(docs, "Read the docs"), 12, FontWeight.Medium, Chrome.Accent);
            Assert.Equal(5, Assert.IsType<StackPanel>(docs.Content).Spacing);
            Glyph(Assert.Single(Nodes<SwiftImage>(docs)), "arrow.up.right", 10, FontWeight.Bold, Chrome.Accent);
            Press(optional); await Task.Yield();
            var keyWindow = (UiWindow)typeof(FloatingGuidePanel).GetField("panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(MCPKeyPanel.Shared)!;
            Assert.True(keyWindow.IsVisible); Assert.IsType<MCPKeyPanelView>(keyWindow.Content); MCPKeyPanel.Shared.Close();
        }
        finally { w.Close(); }
        var custom = new MCPInfoPopover(Custom().CatalogEntry()); Assert.DoesNotContain(Nodes<Button>(custom), b => Texts(b).Contains("Read the docs") || Texts(b).Any(t => t.StartsWith("Change the", StringComparison.Ordinal)));
    });

    [Fact]
    [Trait("Req", "UI-MCP-059")] // App/Views/Settings/MCPSettingsPage.swift:815-825; Services/MCP/MCPStore.swift:216-219
    public void Removing_a_stored_variable_only_deletes_its_plain_or_protected_value_when_saved() => Ui(async (model, _, _) =>
    {
        await Task.Yield(); var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        foreach (var secret in new[] { false, true })
        {
            var custom = Custom(1) with { Env = new[] { new MCPCustomServer.Variable("KEY0", secret) } }; Assert.Null(model.Mcp.SaveCustom(custom, custom.Id));
            Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "kept-until-save", secret) }, custom.Id, null));
            var editor = new MCPCustomEditor(custom); var w = Show(editor, model);
            try
            {
                Press(Nodes<Button>(editor).Single(b => ToolTip.GetTip(b) as string == "Remove this variable")); await Task.Yield();
                Assert.DoesNotContain(Nodes<TextBox>(editor), f => f.PlaceholderText == "KEY"); Assert.Empty(Read<MCPCustomServer>(editor, "Draft").Env);
                Assert.Equal("kept-until-save", model.Mcp.StoredValue(custom.Id, "KEY0", secret));
                Press(Button(editor, "Save")); await Task.Yield(); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", false)); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", true));
            }
            finally { w.Close(); }
        }
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:864-889; UI-MCP-063 partial, material blocker #1
    public void Saving_a_connected_custom_writes_values_then_requests_refresh_and_dismisses_before_the_probe_runs() => Ui(async (model, _, _) =>
    {
        await Task.Yield(); var custom = Custom(1); Assert.Null(model.Mcp.SaveCustom(custom));
        Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "before", false) }, custom.Id, null)); State(model, custom.Id, new MCPConnectionState.Connected(1));
        var editor = new MCPCustomEditor(custom); var w = Show(editor, model); var closed = false; Popovers.SetClosePopover(editor, () => closed = true);
        try
        {
            Type(Field(editor, "value"), "after"); Press(Button(editor, "Save"));
            Assert.True(closed); Assert.Equal("after", model.Mcp.StoredValue(custom.Id, "KEY0", false));
            Assert.IsType<MCPConnectionState.Connecting>(model.Mcp.State(custom.Id)); // saveCustom drops to notConnected; only refresh restores Connecting
            model.Mcp.Disconnect(custom.CatalogEntry()); await Task.Yield(); // ED-211: cancel queued process before it can start
        }
        finally { w.Close(); }
    });

    // Independent mutation witnesses: only a subset assertion, hence no Req trait. Each case reads its own
    // production element; a failure in another record's full proof cannot masquerade as this case's failure.
    [Theory]
    [InlineData("UI-MCP-040")] // App/Views/Settings/MCPSettingsPage.swift:521-524 "Fix it"
    [InlineData("UI-MCP-041")] // App/Views/Settings/MCPSettingsPage.swift:525-528 "Try again"
    [InlineData("UI-MCP-042")] // App/Views/Settings/MCPSettingsPage.swift:548 secondaryText
    [InlineData("UI-MCP-043")] // :556-559 normal PopoverItem; Popovers.swift:320 primaryText
    [InlineData("UI-MCP-044")] // :560-562 destructive PopoverItem; Popovers.swift:320 danger
    [InlineData("UI-MCP-045")] // :563 shared Divider; Popovers.swift:155 height 1
    [InlineData("UI-MCP-046")] // :564-566 destructive PopoverItem; Popovers.swift:320 danger
    [InlineData("UI-MCP-047")] // :624 width 380
    [InlineData("UI-MCP-048")] // :712 primaryText
    [InlineData("UI-MCP-049")] // :723 secondaryText
    [InlineData("UI-MCP-050")] // :742 mcpFieldSurface; :896 radius 9
    [InlineData("UI-MCP-051")] // :635 field("Kind")
    [InlineData("UI-MCP-052")] // :643-645 editorField; :896 radius 9
    [InlineData("UI-MCP-053")] // :787 height 66
    [InlineData("UI-MCP-054")] // :649-651 editorField; :896 radius 9
    [InlineData("UI-MCP-055")] // :794 field label; :751 secondaryText
    [InlineData("UI-MCP-056")] // :798 editorField; :896 radius 9
    [InlineData("UI-MCP-057")] // :799-803 editorField; :896 radius 9
    [InlineData("UI-MCP-058")] // :809 unlocked secondaryText
    [InlineData("UI-MCP-059")] // :821 secondaryText
    [InlineData("UI-MCP-060")] // :833 accent
    [InlineData("UI-MCP-061")] // :838 secondaryText
    [InlineData("UI-MCP-062")] // :692 subheadline 11 (ED-059)
    [InlineData("UI-MCP-063")] // :700 "Add server" in add mode
    [InlineData("UI-MCP-064")] // :971 width 340
    [InlineData("UI-MCP-065")] // :981 primaryText
    [InlineData("UI-MCP-066")] // :1031 label width 58
    [InlineData("UI-MCP-067")] // :1074 flow spacing 6
    [InlineData("UI-MCP-068")] // :1007 accent
    [InlineData("UI-MCP-069")] // :1018 accent
    [InlineData("UI-MCP-089")] // :62 secondaryText
    [InlineData("UI-MCP-091")] // :1132 fallback primaryText
    [InlineData("UI-MCP-093")] // :49-59 category.title; Core/Models/MCP.swift:52 "Developer"
    [InlineData("UI-MCP-094")] // :98 primaryText
    [InlineData("UI-MCP-095")] // :690 footer spacing 10
    [InlineData("UI-MCP-096")] // :896 field surface radius 9
    [Trait("Rule", "R-TEST-2")]
    public void Mutation_witness_for_each_assigned_record(string id) => Ui(async (model, _, _) =>
    {
        await Task.Yield(); var custom = Custom(1); Control control;
        if (string.CompareOrdinal(id, "UI-MCP-047") < 0)
        {
            State(model, custom.Id, id == "UI-MCP-040" ? new MCPConnectionState.NeedsSetup(new MCPSetupGuide("Setup", "Reason", new[] { "Step" }, "Open", "", null))
                : id == "UI-MCP-041" ? new MCPConnectionState.Failed("Rejected") : new MCPConnectionState.Connected(1));
            var row = new MCPCustomRow(custom); control = row;
            if (id is "UI-MCP-043" or "UI-MCP-044" or "UI-MCP-045" or "UI-MCP-046")
            {
                AppEnvironment.SetAppModel(row, model); control = Invoke<Control>(row, "BuildMenu");
            }
        }
        else if (id is "UI-MCP-089" or "UI-MCP-093" or "UI-MCP-094") control = new MCPSettingsPage("");
        else if (id == "UI-MCP-091") control = new MCPIcon(Entry(), 22);
        else if (string.CompareOrdinal(id, "UI-MCP-064") >= 0 && string.CompareOrdinal(id, "UI-MCP-069") <= 0)
            control = new MCPInfoPopover(Entry() with { SampleTools = new[] { "read" }, DocsURL = "https://example.invalid/docs", Fields = new[] { new MCPField("key", "API Key", "", IsRequired: false) } });
        else control = new MCPCustomEditor(id == "UI-MCP-062" || id == "UI-MCP-063" ? null : custom);
        var w = Show(control, model);
        try
        {
            switch (id)
            {
                case "UI-MCP-040": Assert.Contains("Fix it", Texts(control)); break;
                case "UI-MCP-041": Assert.Contains("Try again", Texts(control)); break;
                case "UI-MCP-042": { var glyph = Nodes<SwiftImage>(control).Single(g => g.SymbolName == "ellipsis.circle"); ColorIs(glyph, glyph.Foreground, Chrome.SecondaryText); break; }
                case "UI-MCP-043": case "UI-MCP-044": case "UI-MCP-046":
                {
                    var button = Button(control, id == "UI-MCP-043" ? "Edit" : id == "UI-MCP-044" ? "Disconnect" : "Delete");
                    var text = Assert.Single(Nodes<MiddleTruncatingText>(button)); ColorIs(text, text.Foreground, id == "UI-MCP-043" ? Chrome.PrimaryText : Chrome.Danger); break;
                }
                case "UI-MCP-045": Assert.Equal(1, Assert.IsType<Border>(Assert.Single(Nodes<PopoverDivider>(control)).Child).Height); break;
                case "UI-MCP-047": Assert.Equal(380, control.Width); break;
                case "UI-MCP-048": { var title = TextAt(control, "Example"); ColorIs(title, title.Foreground, Chrome.PrimaryText); break; }
                case "UI-MCP-049": { var glyph = Nodes<SwiftImage>(control).Single(g => g.SymbolName == "xmark"); ColorIs(glyph, glyph.Foreground, Chrome.SecondaryText); break; }
                case "UI-MCP-050": case "UI-MCP-052": case "UI-MCP-054": case "UI-MCP-056": case "UI-MCP-057": case "UI-MCP-096":
                {
                    if (id == "UI-MCP-054") { Call(control, "SetKind", MCPCustomServer.Kind.Http); await Task.Yield(); }
                    var prompt = id == "UI-MCP-052" ? "npx" : id == "UI-MCP-054" ? "https://…" : id == "UI-MCP-056" ? "KEY" : id == "UI-MCP-057" ? "value" : "My server";
                    Assert.Equal(new CornerRadius(9), Assert.IsType<Border>(Field(control, prompt).Parent).CornerRadius); break;
                }
                case "UI-MCP-051": Assert.Contains("Kind", Texts(control)); break;
                case "UI-MCP-053": Assert.Equal(66, Nodes<TextBox>(control).Single(f => f.AcceptsReturn).Height); break;
                case "UI-MCP-055": { var label = TextAt(control, "Environment"); ColorIs(label, label.Foreground, Chrome.SecondaryText); break; }
                case "UI-MCP-058": case "UI-MCP-059":
                { var glyph = Nodes<SwiftImage>(control).Single(g => g.SymbolName == (id == "UI-MCP-058" ? "lock.open" : "minus.circle")); ColorIs(glyph, glyph.Foreground, Chrome.SecondaryText); break; }
                case "UI-MCP-060": { var label = TextAt(control, "Add environment variable"); ColorIs(label, label.Foreground, Chrome.Accent); break; }
                case "UI-MCP-061": { var label = TextAt(control, "Locked values are kept in the Credential Manager."); ColorIs(label, label.Foreground, Chrome.SecondaryText); break; }
                case "UI-MCP-062":
                {
                    var root = Assert.IsType<Grid>(Assert.IsType<Border>(((MCPCustomEditor)control).Child).Child);
                    var footer = root.Children.OfType<StackPanel>().Single(); Press(Assert.IsType<Button>(footer.Children[^1])); await Task.Yield();
                    Assert.Equal(11, TextAt(control, "The server needs a name.").FontSize); break;
                }
                case "UI-MCP-063": Assert.Contains("Add server", Texts(control)); break;
                case "UI-MCP-064": Assert.Equal(340, Assert.IsType<Border>(((MCPInfoPopover)control).Child).Width); break;
                case "UI-MCP-065": { var title = TextAt(control, "Example"); ColorIs(title, title.Foreground, Chrome.PrimaryText); break; }
                case "UI-MCP-066": Assert.Equal(58, TextAt(control, "Connects").Width); break;
                case "UI-MCP-067": Assert.Equal(6, Assert.Single(Nodes<FlowLayout>(control)).Spacing); break;
                case "UI-MCP-068": { var label = TextAt(control, "Change the api key…"); ColorIs(label, label.Foreground, Chrome.Accent); break; }
                case "UI-MCP-069": { var glyph = Nodes<SwiftImage>(control).Single(g => g.SymbolName == "arrow.up.right"); ColorIs(glyph, glyph.Foreground, Chrome.Accent); break; }
                case "UI-MCP-089": { var footer = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(((MCPSettingsPage)control).Child).Children[^1]); ColorIs(footer, footer.Foreground, Chrome.SecondaryText); break; }
                case "UI-MCP-091": { var image = Assert.IsType<SwiftImage>(((MCPIcon)control).Child); ColorIs(image, image.Foreground, Chrome.PrimaryText); break; }
                case "UI-MCP-093": Assert.Contains("Developer", Texts(control)); break;
                case "UI-MCP-094": { var title = TextAt(control, "Connected servers lead"); ColorIs(title, title.Foreground, Chrome.PrimaryText); break; }
                case "UI-MCP-095": Assert.Equal(10, Assert.IsType<StackPanel>(Button(control, "Save").Parent).Spacing); break;
                default: throw new InvalidOperationException("Unassigned mutation witness " + id);
            }
        }
        finally { w.Close(); }
    });
}
