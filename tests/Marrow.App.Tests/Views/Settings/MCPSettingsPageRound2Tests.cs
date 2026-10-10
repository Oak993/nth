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

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :35-122, :542-1141.
// Round 2 of proofs-mcp-b: every clause of each tagged record is asserted on the rendered production tree
// (arranged bounds, resolved brushes, real pointer and keyboard input, the production popover builder).
// ED-181 is closed (2026-10-04): the continuous corner is Apple's curve; these tests assert the radius and style parameters.
[Collection("Headless")]
public sealed class MCPSettingsPageRound2Tests(HeadlessSession headless)
{
    private void Ui(Func<AppModel, RecordingPresenter, Task> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        runtime.Loop.Run(async () => await test(new AppModel(), presenter));
        Dispatcher.UIThread.RunJobs();
    });

    private static UiWindow Show(Control control, AppModel model, ThemeVariant? theme = null)
    {
        PopoverCardContent.Lift(control); // a popover's view comes off the card the presenter hung it on
        AppEnvironment.SetAppModel(control, model);
        var window = new UiWindow { Content = control, Width = 800, Height = 800, RequestedThemeVariant = theme ?? ThemeVariant.Light };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window;
    }

    // Real headless pointer input at a point inside the control, through the window's own hit testing.
    private static void PressAt(Control target, Point local, bool expectClick = true)
    {
        var window = Assert.IsType<UiWindow>(TopLevel.GetTopLevel(target));
        window.UpdateLayout(); target.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var at = target.TranslatePoint(local, window)!.Value;
        var clicked = false; EventHandler<Avalonia.Interactivity.RoutedEventArgs> mark = (_, _) => clicked = true;
        var button = target as Button ?? target.GetVisualAncestors().OfType<Button>().FirstOrDefault();
        if (button is not null) button.Click += mark;
        window.MouseMove(at, RawInputModifiers.None);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        if (button is not null) button.Click -= mark;
        if (expectClick) Assert.True(clicked, $"Pointer at {local} did not activate {AutomationProperties.GetName(target)}");
        Dispatcher.UIThread.RunJobs();
    }
    private static void Press(Control target) => PressAt(target, new Point(target.Bounds.Width / 2, target.Bounds.Height / 2));

    private static void Type(TextBox field, string text)
    {
        Assert.True(field.Focus());
        var window = Assert.IsType<UiWindow>(TopLevel.GetTopLevel(field));
        field.SelectAll(); window.KeyTextInput(text); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }

    private static Rect At(Control control, Visual relativeTo) => new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);
    private static TextBlock TextAt(Control control, string text) => Nodes<TextBlock>(control).Single(t => t.Text == text);
    private static void ColorIs(Control control, IBrush? brush, SwiftColor expected) =>
        Assert.Equal(expected.Resolve(control.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);
    private static void TextStyle(TextBlock text, double size, FontWeight weight, SwiftColor color, bool mono = false)
    {
        Assert.Equal(size, text.FontSize); Assert.Equal(weight, text.FontWeight);
        Assert.Equal(SwiftFont.System(size, design: mono ? SwiftFont.Design.Monospaced : SwiftFont.Design.Default).Resolve().FontFamily, text.FontFamily);
        ColorIs(text, text.Foreground, color);
    }
    private static void Glyph(SwiftImage image, string symbol, double size, FontWeight weight, SwiftColor color)
    {
        Assert.Equal(symbol, image.SymbolName); var font = SwiftImage.GetFont(image)!.Resolve();
        Assert.Equal(size, font.FontSize); Assert.Equal(weight, font.FontWeight);
        Assert.Equal(SwiftFont.System(size).Resolve().FontFamily, font.FontFamily);
        ColorIs(image, image.Foreground, color);
    }
    // :732-742,756-791,894-898: the field's own padding, plain focus-free field, 9 continuous, overlay(.06).
    private static Border Surface(TextBox field, bool name = false, bool args = false)
    {
        var surface = Assert.IsType<Border>(field.Parent);
        Assert.Equal(new CornerRadius(9), surface.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface));
        ColorIs(surface, surface.Background, Chrome.PrimaryText.Opacity(0.06));
        Assert.Equal(field.Bounds.Size, surface.Bounds.Size); // the surface is exactly the padded field
        Assert.Equal(args ? new Thickness(11, 4) : new Thickness(10, 7), field.Padding); // OD-0032: the source's 6 plus the TextEditor's own 5
        // Plain style: the rendered template draws no bezel, background or border of its own.
        var chrome = Assert.IsType<Border>(Assert.Single(field.GetVisualChildren()));
        Assert.Null(chrome.Background); Assert.Equal(default, chrome.BorderThickness); Assert.Equal(field.Padding, chrome.Padding);
        var text = field.GetVisualDescendants().OfType<TextPresenter>().Single();
        Assert.Equal(new Point(field.Padding.Left, field.Padding.Top), text.TranslatePoint(default, field)!.Value); // the text sits inside the field's own padding
        Assert.Null(field.FocusAdorner); // .focusEffectDisabled(): no focus ring is drawn on the surface
        Assert.Equal(name ? 15 : 12, field.FontSize);
        Assert.Equal(name ? FontWeight.SemiBold : FontWeight.Normal, field.FontWeight);
        Assert.Equal(SwiftFont.System(12, design: name ? SwiftFont.Design.Default : SwiftFont.Design.Monospaced).Resolve().FontFamily, field.FontFamily);
        ColorIs(field, field.Foreground, Chrome.PrimaryText);
        return surface;
    }
    // :747-753 field(): VStack(alignment: .leading, spacing: 5) { label 11 semibold secondary; control }.
    private static TextBlock Labelled(Control editor, string label, Control control)
    {
        var text = TextAt(editor, label); TextStyle(text, 11, FontWeight.SemiBold, Chrome.SecondaryText);
        var l = At(text, editor); var c = At(control, editor);
        Assert.Equal(16, l.Left); Assert.Equal(16, c.Left); // leading, inside the 16 outer padding (:657)
        Assert.Equal(l.Bottom + 5, c.Top, 6);
        return text;
    }
    private static MCPCustomServer Draft(MCPCustomEditor editor) => Read<MCPCustomServer>(editor, "Draft");
    private static MCPSetupGuide Guide() => new("Setup", "Reason", new[] { "Step" }, "Open", "", null);
    private static MCPConnectionState[] AllStates() => new MCPConnectionState[] { new MCPConnectionState.NotConnected(), new MCPConnectionState.Connecting(),
        new MCPConnectionState.Connected(2), new MCPConnectionState.Failed("Rejected"), new MCPConnectionState.NeedsSetup(Guide()) };

    [Fact]
    [Trait("Req", "UI-MCP-042")] // App/Views/Settings/MCPSettingsPage.swift:542-554
    public void Options_square_is_fully_clickable_and_toggles_the_bottom_anchored_popover_in_every_state() => Ui(async (model, presenter) =>
    {
        await Task.Yield(); var custom = Custom();
        foreach (var state in AllStates())
        {
            State(model, custom.Id, state); var row = new MCPCustomRow(custom); var w = Show(row, model);
            try
            {
                var options = Nodes<Button>(row).Single(b => ToolTip.GetTip(b) as string == "Server options"); // :553 .help
                Assert.True(options.IsEffectivelyVisible); Assert.Equal(new Size(22, 22), options.Bounds.Size); // :549
                Assert.Null(options.Background);
                Glyph(Assert.Single(Nodes<SwiftImage>(options)), "ellipsis.circle", 13, FontWeight.Normal, Chrome.SecondaryText); // :546-548 .body = 13 (ED-059)
                var popups = Nodes<Popup>(row); Assert.Equal(3, popups.Length);
                Assert.All(popups, p => { Assert.Same(options, p.PlacementTarget); Assert.Equal(PlacementMode.Bottom, p.Placement); }); // :554 arrowEdge .bottom
                presenter.Calls.Clear();
                PressAt(options, new Point(0.5, 0.5)); await Task.Yield(); // :550 .contentShape(.rect): a corner is a live target
                Assert.Equal(new[] { (popups[0], true) }, presenter.Calls);
                PressAt(options, new Point(21.5, 21.5)); await Task.Yield(); // the opposite corner toggles it closed
                Assert.Equal(new[] { (popups[0], true), (popups[0], false) }, presenter.Calls);
            }
            finally { w.Close(); }
        }
    });

    // Opens the options popover by real input and builds its content through the production presentation builder.
    private static async Task<(Popup[] Popups, Control Menu, UiWindow RowWindow, UiWindow MenuWindow)> OpenMenu(MCPCustomRow row, AppModel model, RecordingPresenter presenter)
    {
        var w = Show(row, model); var popups = Nodes<Popup>(row);
        presenter.Calls.Clear(); Press(Nodes<Button>(row).Single(b => ToolTip.GetTip(b) as string == "Server options")); await Task.Yield();
        Assert.Equal(new[] { (popups[0], true) }, presenter.Calls); presenter.Calls.Clear();
        var menu = MCPSettingsPage.PopoverContent(popups[0]); var mw = Show(menu, model);
        return (popups, menu, w, mw);
    }

    private static void MenuOrder(Control menu)
    {
        // :556-566 Edit, Disconnect, Divider, Delete, rendered top to bottom in that order.
        var entries = Nodes<Control>(menu).Where(c => c is PopoverItem or PopoverDivider).ToArray();
        Assert.Equal(new[] { "Edit", "Disconnect", "|", "Delete" }, entries.Select(c => c is PopoverDivider ? "|" : AutomationProperties.GetName(Assert.IsType<Button>(((PopoverItem)c).Child))));
        var tops = entries.Select(c => At(c, menu).Top).ToArray(); Assert.Equal(tops.OrderBy(t => t), tops); Assert.Equal(tops.Length, tops.Distinct().Count());
    }

    private static void MenuStyle(Control menu, string title, string symbol, bool destructive, bool enabled)
    {
        // UI/Chrome/Popovers.swift:275-335 shared row: 8 spacing and padding, height 26, body 13, symbol callout 12,
        // continuous 6, clear idle fill, danger/primary foreground, disabled opacity .4.
        var button = Button(menu, title); Assert.Equal(enabled, button.IsEnabled); Assert.Equal(enabled ? 1 : 0.4, button.Opacity);
        var border = Assert.IsType<Border>(button.Content); Assert.Equal(new CornerRadius(6), border.CornerRadius);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(border));
        ColorIs(border, border.Background, SwiftColor.Clear);
        var row = Assert.IsAssignableFrom<StackPanel>(border.Child); Assert.Equal(8, row.Spacing); Assert.Equal(26, row.Bounds.Height); Assert.Equal(new Thickness(8, 0), row.Margin);
        var text = Assert.Single(Nodes<MiddleTruncatingText>(button)); Assert.Equal(title, text.FullText);
        TextStyle(text, 13, FontWeight.Normal, destructive ? Chrome.Danger : Chrome.PrimaryText);
        Glyph(Assert.Single(Nodes<SwiftImage>(button)), symbol, 12, FontWeight.Normal, destructive ? Chrome.Danger : Chrome.PrimaryText);
        Assert.Equal(At(text, button).Left, At(Assert.Single(Nodes<SwiftImage>(button)), button).Left + 16 + 8, 6); // glyph column, then title
    }

    [Fact]
    [Trait("Req", "UI-MCP-043")] // App/Views/Settings/MCPSettingsPage.swift:556-559
    public void Edit_item_is_normal_and_enabled_and_real_click_closes_the_menu_then_presents_edit_mode() => Ui(async (model, presenter) =>
    {
        await Task.Yield();
        foreach (var state in AllStates())
        {
            State(model, "custom-example", state);
            var (popups, menu, w, mw) = await OpenMenu(new MCPCustomRow(Custom()), model, presenter);
            try
            {
                MenuOrder(menu); MenuStyle(menu, "Edit", "pencil", false, true);
                if (state is not MCPConnectionState.NotConnected) continue;
                Press(Button(menu, "Edit")); await Task.Yield(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
                // Closes this popover (never reopens it), then presents the editor from the same button.
                Assert.Equal(new[] { (popups[0], false), (popups[1], true) }, presenter.Calls);
                var editor = Assert.IsType<MCPCustomEditor>(Nodes<MCPCustomEditor>(MCPSettingsPage.PopoverContent(popups[1])).Single());
                Assert.Contains("Example", Texts(editor)); Assert.Equal("custom-example", Draft(editor).Id); Assert.Equal("offline-fixture", Field(editor, "npx").Text);
            }
            finally { mw.Close(); w.Close(); }
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-044")] // App/Views/Settings/MCPSettingsPage.swift:560-562; Services/MCP/MCPStore.swift:115-128; OD-0008 A12; OD-0012 E6
    public void Disconnect_is_destructive_disabled_until_connected_and_deletes_or_reports_stored_values() => Ui(async (model, presenter) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        var custom = Custom(1); Assert.Null(model.Mcp.SaveCustom(custom));
        foreach (var state in AllStates().Where(s => !s.IsConnected))
        {
            State(model, custom.Id, state); var (_, menu, w, mw) = await OpenMenu(new MCPCustomRow(custom), model, presenter);
            try { MenuOrder(menu); MenuStyle(menu, "Disconnect", "xmark.circle", true, false); }
            finally { mw.Close(); w.Close(); }
        }
        foreach (var refuse in new[] { false, true })
        {
            backend.RefuseDeletes = false; Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "protected-value", true) }, custom.Id, null));
            backend.RefuseDeletes = refuse; State(model, custom.Id, new MCPConnectionState.Connected(1)); Connection(model, new MCPConnection(custom.Id));
            var (popups, menu, w, mw) = await OpenMenu(new MCPCustomRow(custom), model, presenter);
            try
            {
                MenuStyle(menu, "Disconnect", "xmark.circle", true, true);
                Press(Button(menu, "Disconnect")); await Task.Yield(); await Task.Yield(); // immediately: no confirmation
                Assert.Equal(new[] { (popups[0], false) }, presenter.Calls);
                Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State(custom.Id)); Assert.Null(model.Mcp.Connection(custom.Id));
                if (refuse) Assert.Equal("Disconnected, but Credential Manager did not remove this server’s stored values.", model.Mcp.CredentialDeletionFailure); // OD-0012 E6
                else { Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", true)); Assert.Null(model.Mcp.CredentialDeletionFailure); }
            }
            finally { mw.Close(); w.Close(); }
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-045")] // App/Views/Settings/MCPSettingsPage.swift:563; UI/Chrome/Popovers.swift:152-158
    public void Divider_is_the_shared_hairline_between_disconnect_and_delete() => Ui(async (model, presenter) =>
    {
        await Task.Yield();
        foreach (var dark in new[] { false, true })
        {
            var (_, menu, w, mw) = await OpenMenu(new MCPCustomRow(Custom()), model, presenter);
            mw.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light; Dispatcher.UIThread.RunJobs(); mw.UpdateLayout();
            try
            {
                MenuOrder(menu);
                var divider = Assert.Single(Nodes<PopoverDivider>(menu)); var bar = Assert.IsType<Border>(divider.Child);
                Assert.Equal(1, bar.Bounds.Height); Assert.Equal(new Thickness(8, 5), bar.Margin); Assert.Equal(11, divider.Bounds.Height);
                ColorIs(bar, bar.Background, Chrome.PrimaryText.Opacity(0.22));
                var disconnect = At(Button(menu, "Disconnect"), menu); var delete = At(Button(menu, "Delete"), menu); var line = At(bar, menu);
                Assert.Equal(disconnect.Bottom + 5, line.Top, 6); Assert.Equal(line.Bottom + 5, delete.Top, 6);
                Assert.Equal(At(divider, menu).Left + 8, line.Left, 6);
            }
            finally { mw.Close(); w.Close(); }
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-046")] // App/Views/Settings/MCPSettingsPage.swift:564-566; Services/MCP/MCPStore.swift:187-193; OD-0008 A1; OD-0012 E1
    public void Delete_presents_the_confirmation_first_when_asked_and_deletes_at_once_otherwise() => Ui(async (model, presenter) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        foreach (var confirm in new[] { true, false })
        {
            model.Settings.ConfirmBeforeDeleting = confirm; var custom = Custom(1); Assert.Null(model.Mcp.SaveCustom(custom));
            var definitionBefore = File.ReadAllBytes(MCPPaths.CustomsURL);
            Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "protected-value", true) }, custom.Id, null));
            State(model, custom.Id, new MCPConnectionState.Connected(1)); Connection(model, new MCPConnection(custom.Id));
            var row = new MCPCustomRow(custom); var (popups, menu, w, mw) = await OpenMenu(row, model, presenter);
            try
            {
                MenuOrder(menu); MenuStyle(menu, "Delete", "trash", true, true);
                Press(Button(menu, "Delete")); await Task.Yield(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
                if (confirm)
                {
                    // The confirmation is presented from the options button before anything is removed.
                    Assert.Equal(new[] { (popups[0], false), (popups[2], true) }, presenter.Calls); presenter.Calls.Clear();
                    Assert.Single(model.Mcp.Customs); Assert.NotNull(model.Mcp.Connection(custom.Id)); Assert.Equal("protected-value", model.Mcp.StoredValue(custom.Id, "KEY0", true));
                    var confirmation = MCPSettingsPage.PopoverContent(popups[2]); var cw = Show(confirmation, model);
                    try
                    {
                        var question = Assert.Single(Nodes<PopoverSectionHeader>(confirmation)); Assert.Equal("Delete this server?", Assert.IsType<TextBlock>(question.Child).Text);
                        var note = Assert.Single(Nodes<PopoverNote>(confirmation)); Assert.Equal("“Example” and the values it keeps in Credential Manager will be removed.", Assert.IsType<TextBlock>(note.Child).Text);
                        MenuStyle(confirmation, "Delete server", "trash", true, true); // the red button
                        Assert.True(At(question, confirmation).Top < At(note, confirmation).Top && At(note, confirmation).Top < At(Button(confirmation, "Delete server"), confirmation).Top);
                        // Dismissing (light dismiss sets the presentation binding false) cancels: nothing changes.
                        Popovers.GetClosePopover(Nodes<PopoverMenu>(confirmation).First())!.Invoke(); await Task.Yield();
                        Assert.Equal(new[] { (popups[2], false) }, presenter.Calls); presenter.Calls.Clear();
                        Assert.Single(model.Mcp.Customs); Assert.NotNull(model.Mcp.Connection(custom.Id));
                        Assert.Equal(definitionBefore, File.ReadAllBytes(MCPPaths.CustomsURL)); Assert.Equal("protected-value", model.Mcp.StoredValue(custom.Id, "KEY0", true));
                    }
                    finally { cw.Close(); }
                    Press(Button(menu, "Delete")); await Task.Yield(); await Task.Yield(); Dispatcher.UIThread.RunJobs();
                    Assert.Contains((popups[2], true), presenter.Calls); Assert.Single(model.Mcp.Customs);
                    var second = MCPSettingsPage.PopoverContent(popups[2]); var sw = Show(second, model);
                    try { Press(Button(second, "Delete server")); await Task.Yield(); await Task.Yield(); Dispatcher.UIThread.RunJobs(); } finally { sw.Close(); }
                }
                else Assert.DoesNotContain(presenter.Calls, c => c.Popup == popups[2]);
                Assert.Empty(model.Mcp.Customs); Assert.Null(model.Mcp.Connection(custom.Id)); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", true));
                using var definitions = JsonDocument.Parse(File.ReadAllBytes(MCPPaths.CustomsURL)); Assert.Equal(0, definitions.RootElement.GetArrayLength());
            }
            finally { mw.Close(); w.Close(); }
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-048")] // App/Views/Settings/MCPSettingsPage.swift:706-730
    [Trait("Req", "UI-MCP-049")] // App/Views/Settings/MCPSettingsPage.swift:718-728
    public void Header_is_top_aligned_ten_spaced_and_its_close_square_dismisses_without_saving() => Ui(async (model, _) =>
    {
        await Task.Yield();
        foreach (var edit in new[] { false, true })
        {
            var editor = new MCPCustomEditor(edit ? Custom() with { Name = "Example" } : null); var w = Show(editor, model);
            try
            {
                var title = TextAt(editor, edit ? "Example" : "New server"); TextStyle(title, 14, FontWeight.SemiBold, Chrome.PrimaryText); // :710-712
                var subtitle = TextAt(editor, "Custom · stdio"); TextStyle(subtitle, 11, FontWeight.Normal, Chrome.SecondaryText); // :713-715
                var icon = Nodes<MCPIcon>(editor).Single(); var close = Button(editor, "Close"); // :728 accessibility label
                var i = At(icon, editor); var t = At(title, editor); var s = At(subtitle, editor); var c = At(close, editor);
                Assert.Equal(new Size(26, 26), icon.Bounds.Size); Assert.Equal(new Size(26, 26), icon.Child!.Bounds.Size); // :708
                Assert.Equal(16, i.Left); Assert.Equal(16, i.Top); Assert.Equal(16, t.Top); Assert.Equal(16, c.Top); // :707 alignment .top
                Assert.Equal(i.Right + 10, t.Left); Assert.Equal(t.Bottom + 1, s.Top, 6); Assert.Equal(t.Left, s.Left); // :707 spacing 10, :709 spacing 1 leading
                Assert.Equal(At(Assert.IsAssignableFrom<Control>(title.Parent), editor).Right + 10 + 8 + 10, c.Left); // :717 Spacer(minLength: 8) between spacing-10 gaps
                Assert.Equal(editor.Bounds.Width - 16, c.Right); // the spacer pushes the close button to the trailing edge
                Assert.Equal(new Size(24, 24), close.Bounds.Size); Assert.Null(close.Background); // :724, :727 plain
                Glyph(Assert.Single(Nodes<SwiftImage>(close)), "xmark", 11, FontWeight.SemiBold, Chrome.SecondaryText); // :721-723
                var closed = false; Popovers.SetClosePopover(editor, () => closed = true);
                Type(Field(editor, "My server"), "Discard me"); Type(Field(editor, "npx"), "offline-fixture");
                PressAt(close, new Point(0.5, 0.5)); // :725 .contentShape(.rect): the square's corner is a live target
                Assert.True(closed); Assert.Empty(model.Mcp.Customs); // :719 dismiss() discards the draft
                Call(editor, "SetKind", MCPCustomServer.Kind.Http); w.UpdateLayout(); // the subtitle follows the kind picker live
                TextStyle(TextAt(editor, "Custom · http"), 11, FontWeight.Normal, Chrome.SecondaryText); Assert.DoesNotContain("Custom · stdio", Texts(editor));
                Assert.Contains(edit ? "Example" : "New server", Texts(editor)); Assert.True(Button(editor, "Close").IsEffectivelyVisible);
            }
            finally { w.Close(); }
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-050")] // App/Views/Settings/MCPSettingsPage.swift:732-753,663-665
    [Trait("Req", "UI-MCP-096")] // App/Views/Settings/MCPSettingsPage.swift:892-898
    public void Name_field_is_labelled_boxed_semibold_and_focused_only_when_adding() => Ui(async (model, _) =>
    {
        await Task.Yield();
        var editor = new MCPCustomEditor(null); var w = Show(editor, model);
        try
        {
            var name = Field(editor, "My server"); Assert.True(name.IsFocused); // :663-665 focused on appear in add mode
            var surface = Surface(name, name: true); Labelled(editor, "Name", surface);
            Assert.True(name.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "My server").IsEffectivelyVisible); // :734 prompt
            Type(name, "A typed server"); Assert.Equal("A typed server", Draft(editor).Name);
            Assert.Equal(MCPCustomServer.MakeID("A typed server"), Draft(editor).Id); // :849 add mode mints the id from the name
            Assert.False(name.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "My server").IsVisible);
        }
        finally { w.Close(); }
        var edit = new MCPCustomEditor(Custom()); var second = Show(edit, model);
        try { var field = Field(edit, "My server"); Assert.False(field.IsFocused); Assert.Equal("Example", field.Text); Surface(field, name: true); Type(field, "Renamed"); Assert.Equal("custom-example", Draft(edit).Id); }
        finally { second.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-052")] // App/Views/Settings/MCPSettingsPage.swift:642-645,756-764,850
    [Trait("Req", "UI-MCP-053")] // App/Views/Settings/MCPSettingsPage.swift:777-792,851-856
    [Trait("Req", "UI-MCP-054")] // App/Views/Settings/MCPSettingsPage.swift:649-651,756-764,857
    [Trait("Req", "UI-MCP-096")] // App/Views/Settings/MCPSettingsPage.swift:892-898
    public void Command_arguments_and_url_are_labelled_boxed_per_kind_and_real_typing_sets_the_draft() => Ui(async (model, _) =>
    {
        await Task.Yield();
        var editor = new MCPCustomEditor(null); var w = Show(editor, model);
        try
        {
            var command = Field(editor, "npx"); Labelled(editor, "Command", Surface(command)); // :643-645
            Type(command, "  offline-fixture  "); Assert.Equal("offline-fixture", Draft(editor).Command); // :850 trimmed
            var args = Nodes<TextBox>(editor).Single(f => f.AcceptsReturn); var argsSurface = Surface(args, args: true);
            Labelled(editor, "Arguments", argsSurface); Assert.Equal(66, argsSurface.Bounds.Height); Assert.Equal(66, args.Bounds.Height); // :786
            Assert.Equal("", args.PlaceholderText ?? "");
            // :783 .scrollContentBackground(.hidden): nothing inside the surface paints a background of its own.
            var inner = args.GetVisualDescendants().ToArray(); Assert.Contains(inner, v => v is ScrollContentPresenter);
            Assert.All(inner.OfType<Border>(), b => Assert.Null(b.Background)); Assert.All(inner.OfType<Panel>(), p => Assert.Null(p.Background));
            Assert.All(inner.OfType<ContentPresenter>(), p => Assert.Null(p.Background)); Assert.All(inner.OfType<ScrollViewer>(), p => Assert.Null(p.Background));
            var hint = TextAt(editor, "One argument per line; empty lines are dropped."); TextStyle(hint, 10.5, FontWeight.Normal, Chrome.SecondaryText); // :788-790
            Assert.Equal(At(argsSurface, editor).Bottom + 5, At(hint, editor).Top, 6); Assert.Equal(16, At(hint, editor).Left); // :748 field VStack spacing 5: the hint sits below
            Type(args, "--flag\n\n argument with spaces \n  \n-x"); // :851-856 blank-after-trim lines dropped, survivors not trimmed
            Assert.Equal(new[] { "--flag", " argument with spaces ", "-x" }, Draft(editor).Args);
            Assert.Equal(At(TextAt(editor, "Arguments"), editor).Top, At(TextAt(editor, "Command"), editor).Bottom + 5 + command.Bounds.Height + 12, 6);
            Call(editor, "SetKind", MCPCustomServer.Kind.Http); w.UpdateLayout(); // :642-653 stdio fields leave, URL appears
            Assert.DoesNotContain(Nodes<TextBox>(editor), f => f.PlaceholderText == "npx" || f.AcceptsReturn);
            Assert.DoesNotContain("Command", Texts(editor)); Assert.DoesNotContain("Arguments", Texts(editor)); Assert.DoesNotContain("One argument per line; empty lines are dropped.", Texts(editor));
            var url = Field(editor, "https://…"); Labelled(editor, "URL", Surface(url)); // :649-651
            Type(url, "  https://example.invalid/mcp  "); Assert.Equal("https://example.invalid/mcp", Draft(editor).Url); // :857 trimmed
            Call(editor, "SetKind", MCPCustomServer.Kind.Stdio); w.UpdateLayout();
            Assert.DoesNotContain(Nodes<TextBox>(editor), f => f.PlaceholderText == "https://…"); Assert.DoesNotContain("URL", Texts(editor));
            Assert.Equal("  offline-fixture  ", Field(editor, "npx").Text);
        }
        finally { w.Close(); }
    });

    private static Grid[] VariableRows(MCPCustomEditor editor) => Nodes<TextBox>(editor).Where(f => f.PlaceholderText == "KEY").Select(k => Assert.IsType<Grid>(k.Parent!.Parent)).ToArray();
    private static Button Lock(Grid row) => row.Children.OfType<Button>().Single(b => Nodes<SwiftImage>(b).Single().SymbolName!.StartsWith("lock", StringComparison.Ordinal));
    private static Button Remove(Grid row) => row.Children.OfType<Button>().Single(b => ToolTip.GetTip(b) as string == "Remove this variable");

    private static void Section(MCPCustomEditor editor, string label, string add, int rows)
    {
        // :794-840 field(title) { rows; add button; hint } flattened into one VStack(spacing: 5).
        var addButton = Button(editor, add); var hint = TextAt(editor, "Locked values are kept in the Credential Manager.");
        var grids = VariableRows(editor); Assert.Equal(rows, grids.Length);
        var column = new List<Control>(grids) { addButton, hint };
        Labelled(editor, label, column[0]);
        for (var i = 1; i < column.Count; i++) { Assert.Equal(At(column[i - 1], editor).Bottom + 5, At(column[i], editor).Top, 6); Assert.Equal(16, At(column[i], editor).Left); }
        foreach (var row in grids)
        {
            // :797 HStack(spacing: 6) { key, value, lock, remove }, left to right.
            var key = Assert.IsType<Border>(row.Children[0]); var value = Assert.IsType<Border>(row.Children[1]);
            Assert.Equal("KEY", Assert.IsType<TextBox>(key.Child).PlaceholderText); Assert.Equal("value", Assert.IsType<TextBox>(value.Child).PlaceholderText);
            var cells = new Control[] { key, value, Lock(row), Remove(row) }.Select(c => At(c, editor)).ToArray();
            Assert.Equal(16, cells[0].Left);
            for (var i = 1; i < cells.Length; i++) Assert.Equal(cells[i - 1].Right + 6, cells[i].Left, 6);
            Assert.Equal(editor.Bounds.Width - 16, cells[^1].Right, 6);
        }
    }

    [Fact]
    [Trait("Req", "UI-MCP-055")] // App/Views/Settings/MCPSettingsPage.swift:794-840
    [Trait("Req", "UI-MCP-061")] // App/Views/Settings/MCPSettingsPage.swift:836-838; OD-0008 B3
    public void Variables_section_orders_rows_then_add_then_hint_in_both_kinds_and_when_empty() => Ui(async (model, _) =>
    {
        await Task.Yield();
        var empty = new MCPCustomEditor(null); var ew = Show(empty, model);
        try
        {
            Section(empty, "Environment", "Add environment variable", 0);
            var hint = TextAt(empty, "Locked values are kept in the Credential Manager."); TextStyle(hint, 10.5, FontWeight.Normal, Chrome.SecondaryText);
            Call(empty, "SetKind", MCPCustomServer.Kind.Http); ew.UpdateLayout(); Section(empty, "Headers", "Add header", 0);
        }
        finally { ew.Close(); }
        var editor = new MCPCustomEditor(Custom(2)); var w = Show(editor, model);
        try
        {
            Section(editor, "Environment", "Add environment variable", 2);
            Press(Button(editor, "Add environment variable")); await Task.Yield(); w.UpdateLayout(); Section(editor, "Environment", "Add environment variable", 3);
            TextStyle(TextAt(editor, "Locked values are kept in the Credential Manager."), 10.5, FontWeight.Normal, Chrome.SecondaryText);
            Call(editor, "SetKind", MCPCustomServer.Kind.Http); w.UpdateLayout(); Section(editor, "Headers", "Add header", 3);
            Assert.DoesNotContain("Environment", Texts(editor));
        }
        finally { w.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-056")] // App/Views/Settings/MCPSettingsPage.swift:798,845-849
    [Trait("Req", "UI-MCP-057")] // App/Views/Settings/MCPSettingsPage.swift:799-803,766-775,864-881
    [Trait("Req", "UI-MCP-096")] // App/Views/Settings/MCPSettingsPage.swift:892-898
    public void Key_and_value_fields_are_boxed_masked_when_locked_and_values_save_only_on_save() => Ui(async (model, _) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        var custom = Custom(1) with { Env = new[] { new MCPCustomServer.Variable("KEY0"), new MCPCustomServer.Variable("SECRET", true) } };
        Assert.Null(model.Mcp.SaveCustom(custom)); Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "old-plain", false), ("SECRET", "old-secret", true) }, custom.Id, null));
        var editor = new MCPCustomEditor(custom); var w = Show(editor, model);
        try
        {
            var rows = VariableRows(editor);
            var key = Assert.IsType<TextBox>(((Border)rows[0].Children[0]).Child); var plain = Assert.IsType<TextBox>(((Border)rows[0].Children[1]).Child);
            var secret = Assert.IsType<TextBox>(((Border)rows[1].Children[1]).Child);
            Surface(key); Surface(plain); Surface(secret); // secure and plain fields are dressed identically (:766-775)
            Assert.Equal("KEY", key.PlaceholderText); Assert.Equal("value", plain.PlaceholderText); Assert.Equal("value", secret.PlaceholderText);
            Assert.Equal("old-plain", plain.Text); Assert.Equal("old-secret", secret.Text);
            // Unlocked shows the text; locked is a secure field showing dots.
            var plainText = plain.GetVisualDescendants().OfType<TextPresenter>().Single(); Assert.Equal(default(char), plainText.PasswordChar); Assert.Equal("old-plain", plainText.Text);
            var secretText = secret.GetVisualDescendants().OfType<TextPresenter>().Single(); Assert.Equal('•', secretText.PasswordChar); Assert.False(secretText.RevealPassword); // U+2022 BULLET
            Type(key, "  RENAMED  "); Assert.Equal("RENAMED", Draft(editor).Env[0].Key); // :847 keys trim
            Type(plain, "typed-plain"); Type(secret, "typed-secret");
            Assert.Equal("old-plain", model.Mcp.StoredValue(custom.Id, "KEY0", false)); Assert.Equal("old-secret", model.Mcp.StoredValue(custom.Id, "SECRET", true)); // nothing saved while typing
            Press(Button(editor, "Add environment variable")); await Task.Yield(); w.UpdateLayout();
            var blank = VariableRows(editor)[2]; var blankKey = Assert.IsType<TextBox>(((Border)blank.Children[0]).Child); Surface(blankKey); Type(blankKey, "   ");
            Assert.Equal(new[] { "RENAMED", "SECRET" }, Draft(editor).Env.Select(v => v.Key)); // :848 a key empty after trimming drops the row
            var closed = false; Popovers.SetClosePopover(editor, () => closed = true);
            Press(Button(editor, "Save")); await Task.Yield(); Assert.True(closed);
            Assert.Equal("typed-plain", model.Mcp.StoredValue(custom.Id, "RENAMED", false)); Assert.Equal("typed-secret", model.Mcp.StoredValue(custom.Id, "SECRET", true));
            Assert.Null(model.Mcp.StoredValue(custom.Id, "SECRET", false)); Assert.Null(model.Mcp.StoredValue(custom.Id, "   ", false));
        }
        finally { w.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-058")] // App/Views/Settings/MCPSettingsPage.swift:804-814,864-881; OD-0008 B3
    public void Lock_square_toggles_glyph_colour_tip_and_mask_and_moves_the_value_only_on_save() => Ui(async (model, _) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        var custom = Custom(1) with { Env = new[] { new MCPCustomServer.Variable("KEY0"), new MCPCustomServer.Variable("SECRET", true) } };
        Assert.Null(model.Mcp.SaveCustom(custom)); Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "plain-value", false), ("SECRET", "secret-value", true) }, custom.Id, null));
        var editor = new MCPCustomEditor(custom); var w = Show(editor, model);
        const string lockedTip = "Kept in the Credential Manager", unlockedTip = "Stored in mcp.json; click to keep it in the Credential Manager instead";
        void Is(Grid row, bool locked)
        {
            var toggle = Lock(row); Assert.Equal(new Size(24, 24), toggle.Bounds.Size); Assert.Null(toggle.Background);
            Glyph(Assert.Single(Nodes<SwiftImage>(toggle)), locked ? "lock.fill" : "lock.open", 12, FontWeight.Medium, locked ? Chrome.Accent : Chrome.SecondaryText); // :807-809
            Assert.Equal(locked ? lockedTip : unlockedTip, ToolTip.GetTip(toggle)); // :814 OD-0008 B3
            var value = Assert.IsType<TextBox>(((Border)row.Children[1]).Child); Surface(value);
            Assert.Equal(locked ? '•' : default(char), value.GetVisualDescendants().OfType<TextPresenter>().Single().PasswordChar);
            Assert.Same(toggle, (w.InputHitTest(toggle.TranslatePoint(new Point(12, 12), w)!.Value) as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().First()); // hover reaches the tip
        }
        try
        {
            var rows = VariableRows(editor); Is(rows[0], false); Is(rows[1], true);
            PressAt(Lock(rows[0]), new Point(0.5, 0.5)); await Task.Yield(); w.UpdateLayout(); Is(rows[0], true); // :811 .contentShape(.rect)
            PressAt(Lock(rows[1]), new Point(23.5, 23.5)); await Task.Yield(); w.UpdateLayout(); Is(rows[1], false);
            Assert.Equal(new[] { true, false }, Draft(editor).Env.Select(v => v.IsSecret));
            // Only Save moves the values between the stores.
            Assert.Equal("plain-value", model.Mcp.StoredValue(custom.Id, "KEY0", false)); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", true));
            Assert.Equal("secret-value", model.Mcp.StoredValue(custom.Id, "SECRET", true)); Assert.Null(model.Mcp.StoredValue(custom.Id, "SECRET", false));
            Press(Lock(rows[0])); await Task.Yield(); w.UpdateLayout(); Is(rows[0], false); Press(Lock(rows[0])); await Task.Yield(); w.UpdateLayout(); Is(rows[0], true);
            Press(Button(editor, "Save")); await Task.Yield();
            Assert.Equal("plain-value", model.Mcp.StoredValue(custom.Id, "KEY0", true)); Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", false));
            Assert.Equal("secret-value", model.Mcp.StoredValue(custom.Id, "SECRET", false)); Assert.Null(model.Mcp.StoredValue(custom.Id, "SECRET", true));
        }
        finally { w.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-059")] // App/Views/Settings/MCPSettingsPage.swift:815-825; Services/MCP/MCPStore.swift:216-219
    public void Remove_square_drops_the_row_at_once_and_save_deletes_its_stored_values() => Ui(async (model, _) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = false }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        model.Settings.ConfirmBeforeDeleting = true; // no confirmation even when deletions are confirmed elsewhere
        var custom = Custom(1) with { Env = new[] { new MCPCustomServer.Variable("KEY0"), new MCPCustomServer.Variable("SECRET", true), new MCPCustomServer.Variable("KEEP") } };
        Assert.Null(model.Mcp.SaveCustom(custom));
        Assert.True(model.Mcp.SaveCustomValues(new[] { ("KEY0", "plain-value", false), ("SECRET", "secret-value", true), ("KEEP", "kept", false) }, custom.Id, null));
        var editor = new MCPCustomEditor(custom); var w = Show(editor, model);
        try
        {
            foreach (var row in VariableRows(editor))
            {
                var remove = Remove(row); Assert.True(remove.IsEnabled); Assert.Equal(new Size(24, 24), remove.Bounds.Size); Assert.Null(remove.Background);
                Glyph(Assert.Single(Nodes<SwiftImage>(remove)), "minus.circle", 12, FontWeight.Medium, Chrome.SecondaryText); // :818-820
                Assert.Same(remove, (w.InputHitTest(remove.TranslatePoint(new Point(12, 12), w)!.Value) as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().First()); // hover reaches the tip
            }
            PressAt(Remove(VariableRows(editor)[0]), new Point(0.5, 0.5)); await Task.Yield(); w.UpdateLayout(); // :822 .contentShape(.rect)
            PressAt(Remove(VariableRows(editor)[0]), new Point(23.5, 23.5)); await Task.Yield(); w.UpdateLayout();
            Assert.Equal(new[] { "KEEP" }, Draft(editor).Env.Select(v => v.Key)); Assert.Single(VariableRows(editor)); // immediately, no confirmation
            Assert.Equal("plain-value", model.Mcp.StoredValue(custom.Id, "KEY0", false)); Assert.Equal("secret-value", model.Mcp.StoredValue(custom.Id, "SECRET", true));
            Press(Button(editor, "Save")); await Task.Yield();
            foreach (var secret in new[] { false, true }) { Assert.Null(model.Mcp.StoredValue(custom.Id, "KEY0", secret)); Assert.Null(model.Mcp.StoredValue(custom.Id, "SECRET", secret)); }
            Assert.Equal("kept", model.Mcp.StoredValue(custom.Id, "KEEP", false));
        }
        finally { w.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-060")] // App/Views/Settings/MCPSettingsPage.swift:828-835,647,652
    public void Add_button_is_a_plain_leading_label_and_appends_an_empty_unlocked_row() => Ui(async (model, _) =>
    {
        await Task.Yield();
        var editor = new MCPCustomEditor(null); var w = Show(editor, model);
        try
        {
            foreach (var kind in new[] { MCPCustomServer.Kind.Stdio, MCPCustomServer.Kind.Http })
            {
                Call(editor, "SetKind", kind); w.UpdateLayout();
                var title = kind == MCPCustomServer.Kind.Stdio ? "Add environment variable" : "Add header";
                var add = Button(editor, title); Assert.Null(add.Background); Assert.Equal(16, At(add, editor).Left);
                var glyph = Assert.Single(Nodes<SwiftImage>(add)); var text = TextAt(add, title);
                Glyph(glyph, "plus", 12, FontWeight.Medium, Chrome.Accent); TextStyle(text, 12, FontWeight.Medium, Chrome.Accent); // :831-833
                Assert.True(At(glyph, add).Right <= At(text, add).Left); // the symbol leads the title
                Assert.Equal(At(text, editor).Right, At(add, editor).Right, 6); Assert.Equal(At(glyph, editor).Left, At(add, editor).Left, 6); // no frame: the label's own extent
                var before = VariableRows(editor).Length;
                Press(add); await Task.Yield(); w.UpdateLayout(); // :829 append
                var rows = VariableRows(editor); Assert.Equal(before + 1, rows.Length);
                var key = Assert.IsType<TextBox>(((Border)rows[^1].Children[0]).Child); var value = Assert.IsType<TextBox>(((Border)rows[^1].Children[1]).Child);
                Assert.Equal("", key.Text ?? ""); Assert.Equal("", value.Text ?? ""); Assert.Equal(default(char), value.PasswordChar);
                Assert.Equal("lock.open", Nodes<SwiftImage>(Lock(rows[^1])).Single().SymbolName);
                Type(key, "NEW" + before); Assert.False(Draft(editor).Env.Concat(Draft(editor).Headers).Single(v => v.Key == "NEW" + before).IsSecret);
            }
        }
        finally { w.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-MCP-062")] // App/Views/Settings/MCPSettingsPage.swift:689-697,880; OD-0008 B3
    [Trait("Req", "UI-MCP-095")] // App/Views/Settings/MCPSettingsPage.swift:629-658,687-704
    public void Footer_holds_the_error_above_the_trailing_save_and_never_scrolls() => Ui(async (model, _) =>
    {
        await Task.Yield();
        var backend = new MCPSettingsRefusingCredentialBackend { Refuse = true }; using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
        var editor = new MCPCustomEditor(null); var w = Show(editor, model);
        try
        {
            var save = Button(editor, "Add server"); var footer = Assert.IsType<StackPanel>(save.Parent);
            Assert.Single(footer.Children); Assert.DoesNotContain(Nodes<TextBlock>(footer), t => t.Text is { Length: > 0 } && t.Text != "Add server"); // no error yet
            var scroll = Assert.Single(Nodes<ScrollViewer>(editor), s => s.Content is StackPanel);
            var root = Assert.IsType<Grid>(footer.Parent); Assert.Equal(root.RowDefinitions.Count - 1, Grid.GetRow(footer)); // the editor VStack's last element
            Assert.Equal(At(scroll, editor).Bottom + 12, At(footer, editor).Top, 6); // :630 spacing 12, footer after the ScrollView
            Assert.Equal(At(scroll, editor).Top, At(Assert.IsAssignableFrom<Control>(Nodes<MCPIcon>(editor).Single().Parent), editor).Bottom + 12, 6); // header, then the ScrollView
            Assert.Equal(editor.Bounds.Width - 16, At(save, editor).Right, 6); Assert.Equal(editor.Bounds.Height - 16, At(save, editor).Bottom, 6); // Spacer + button: trailing, last
            for (var i = 0; i < 6; i++) { Press(Button(editor, "Add environment variable")); await Task.Yield(); }
            w.UpdateLayout(); var fixedPosition = At(save, editor);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            scroll.Offset = new Vector(0, scroll.Extent.Height); w.UpdateLayout(); Assert.True(scroll.Offset.Y > 0);
            Assert.Equal(fixedPosition, At(save, editor)); // does not scroll with the form
            scroll.Offset = default; w.UpdateLayout();
            Press(save); await Task.Yield(); w.UpdateLayout();
            void ErrorLine(string text)
            {
                var error = TextAt(editor, text); TextStyle(error, 11, FontWeight.Normal, Chrome.Danger); // :692-694
                Assert.Same(error, footer.Children[0]); Assert.Equal(2, footer.Children.Count);
                Assert.Equal(3, error.MaxLines); Assert.Equal(TextWrapping.Wrap, error.TextWrapping); Assert.Equal(TextTrimming.None, error.TextTrimming); // :695-696
                var e = At(error, editor); var s = At(Button(editor, "Add server"), editor);
                Assert.Equal(At(scroll, editor).Bottom + 12, e.Top, 6); Assert.Equal(e.Bottom + 10, s.Top, 6); Assert.Equal(16, e.Left);
                Assert.Equal(editor.Bounds.Width - 16, s.Right, 6); Assert.Equal(editor.Bounds.Height - 16, s.Bottom, 6);
                Assert.Equal(Math.Ceiling(error.TextLayout.TextLines.Count * error.TextLayout.TextLines[0].Height), error.Bounds.Height, 3); // every line shown (layout-rounded up)
                Assert.DoesNotContain(error, Nodes<TextBlock>(Assert.IsAssignableFrom<Control>(scroll.Content)));
                var before = At(Button(editor, "Add server"), editor); scroll.Offset = new Vector(0, scroll.Extent.Height); w.UpdateLayout();
                Assert.Equal(before, At(Button(editor, "Add server"), editor)); Assert.Equal(e, At(error, editor)); scroll.Offset = default; w.UpdateLayout();
            }
            ErrorLine("The server needs a name."); // MCPCustom.swift:132
            Type(Field(editor, "My server"), "Example"); Type(Field(editor, "npx"), "offline-fixture");
            var keys = Nodes<TextBox>(editor).Where(f => f.PlaceholderText == "KEY").ToArray(); Type(keys[0], "TOKEN"); Type(Nodes<TextBox>(editor).First(f => f.PlaceholderText == "value"), "sample");
            Press(Lock(VariableRows(editor)[0])); await Task.Yield();
            Press(Button(editor, "Add server")); await Task.Yield(); w.UpdateLayout();
            const string refused = "The Credential Manager could not save a value. Keep this editor open and try again."; // :880, OD-0008 B3
            ErrorLine(refused); Assert.True(TextAt(editor, refused).TextLayout.TextLines.Count > 1); // wraps rather than truncating
        }
        finally { w.Close(); }
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "UI-MCP-065")] // App/Views/Settings/MCPSettingsPage.swift:975-985
    [Trait("Req", "UI-MCP-066")] // App/Views/Settings/MCPSettingsPage.swift:990-993,1026-1036
    [Trait("Req", "UI-MCP-067")] // App/Views/Settings/MCPSettingsPage.swift:994-1001,1068-1122
    public void Info_header_details_and_tool_chips_render_in_source_geometry(bool dark) => Ui(async (model, _) =>
    {
        await Task.Yield();
        var entry = MCPCatalog.Entries.Single(e => e.Id == "github");
        var popover = new MCPInfoPopover(entry); var w = Show(popover, model, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        var info = Assert.IsType<Border>(popover.Child); // measure against the 340-wide card, not the stretched host
        try
        {
            Assert.Equal(340, info.Bounds.Width); Assert.Equal(new Thickness(16), info.Padding); // :971,1021-1022
            // UI-MCP-065 header: HStack(alignment: .center, spacing: 10) { icon 28; VStack(leading, spacing: 1) }.
            var title = TextAt(info, "GitHub"); TextStyle(title, 14, FontWeight.SemiBold, Chrome.PrimaryText);
            var subtitle = TextAt(info, "GitHub · Developer"); TextStyle(subtitle, 11, FontWeight.Normal, Chrome.SecondaryText); // U+00B7 with a space each side
            var icon = Nodes<MCPIcon>(info).Single(); var i = At(icon, info); var t = At(title, info); var s = At(subtitle, info);
            Assert.Equal(new Size(28, 28), icon.Bounds.Size); Assert.Equal(16, i.Left); Assert.Equal(i.Right + 10, t.Left); Assert.Equal(t.Left, s.Left);
            Assert.Equal(t.Bottom + 1, s.Top, 6);
            var copyCentre = (t.Top + s.Bottom) / 2; Assert.Equal(copyCentre, (i.Top + i.Bottom) / 2, 6); Assert.Equal(16, Math.Min(i.Top, t.Top), 6);
            // UI-MCP-066 details: two firstTextBaseline rows, label column 58 leading, spacing 8, rows 8 apart.
            var rows = new List<Rect>();
            foreach (var label in new[] { "Connects", "Needs" })
            {
                var key = TextAt(info, label); TextStyle(key, 11, FontWeight.SemiBold, Chrome.SecondaryText);
                var row = Assert.IsAssignableFrom<Panel>(key.Parent); var value = row.Children.OfType<TextBlock>().Single(x => x != key);
                TextStyle(value, 11.5, FontWeight.Normal, Chrome.PrimaryText.Opacity(0.9));
                var k = At(key, info); var v = At(value, info);
                Assert.Equal(58, k.Width); Assert.Equal(16, k.Left); Assert.Equal(0, key.TextLayout.TextLines[0].Start, 6); // .frame(width: 58, alignment: .leading)
                Assert.Equal(k.Left + 58 + 8, v.Left, 6);
                Assert.Equal(k.Top + key.TextLayout.TextLines[0].Baseline, v.Top + value.TextLayout.TextLines[0].Baseline, 6);
                Assert.Equal(TextTrimming.None, value.TextTrimming); Assert.Equal(value.TextLayout.TextLines.Count * value.TextLayout.TextLines[0].Height, value.Bounds.Height, 3);
                Assert.True(v.Right <= info.Bounds.Width - 16 + 0.001);
                rows.Add(At(row, info));
            }
            Assert.Equal(rows[0].Bottom + 8, rows[1].Top, 6);
            // UI-MCP-067 tools: VStack(leading, spacing: 6) { heading; FlowChips }.
            var heading = TextAt(info, "Tools"); TextStyle(heading, 11, FontWeight.SemiBold, Chrome.SecondaryText);
            var flow = Assert.IsType<FlowLayout>(Assert.Single(Nodes<FlowChips>(info)).Child); var f = At(flow, info);
            Assert.Equal(At(heading, info).Bottom + 6, f.Top, 6); Assert.Equal(16, f.Left); Assert.Equal(info.Bounds.Width - 32, f.Width, 6);
            var chips = flow.Children.OfType<Border>().ToArray(); Assert.Equal(entry.SampleTools, chips.Select(c => Assert.IsType<TextBlock>(c.Child).Text));
            foreach (var chip in chips)
            {
                Assert.Equal(new CornerRadius(6), chip.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(chip));
                ColorIs(chip, chip.Background, Chrome.PrimaryText.Opacity(0.08));
                var text = Assert.IsType<TextBlock>(chip.Child); TextStyle(text, 10.5, FontWeight.Normal, Chrome.PrimaryText.Opacity(0.85), mono: true);
                Assert.Equal(new Point(7, 3), text.TranslatePoint(default, chip)!.Value); Assert.Equal(chip.Bounds.Size, new Size(text.Bounds.Width + 14, text.Bounds.Height + 6));
                Assert.Empty(chip.GetSelfAndVisualAncestors().OfType<Button>()); Assert.Empty(Nodes<Button>(chip)); // not clickable
            }
            var lines = 1;
            for (var n = 1; n < chips.Length; n++)
            {
                var previous = At(chips[n - 1], flow); var current = At(chips[n], flow);
                if (current.Top == previous.Top) Assert.Equal(previous.Right + 6, current.Left, 6);
                else
                {
                    lines++; Assert.Equal(0, current.Left); Assert.Equal(previous.Bottom + 6, current.Top, 6);
                    Assert.True(previous.Right + 6 + current.Width > flow.Bounds.Width); // wraps only when the next chip would not fit
                }
                Assert.True(current.Right <= flow.Bounds.Width + 0.001);
            }
            Assert.True(lines > 1); Assert.Equal(At(chips[^1], flow).Bottom, flow.Bounds.Height, 6);
        }
        finally { w.Close(); }
        // Both rows are always present; custom servers have no sample tools, so the block is absent.
        var custom = new MCPInfoPopover(Custom().CatalogEntry()); var cw = Show(custom, model);
        try { Assert.Contains("Connects", Texts(custom)); Assert.Contains("Needs", Texts(custom)); Assert.Empty(Nodes<FlowChips>(custom)); Assert.DoesNotContain("Tools", Texts(custom)); }
        finally { cw.Close(); }
        // A value longer than its column wraps onto further lines (:1035 fixedSize vertical), never truncates.
        var longEntry = Entry(transport: new MCPTransport.Stdio("offline-fixture-with-a-deliberately-long-command", new[] { "offline-package-with-a-long-name" }));
        var lw = Show(new MCPInfoPopover(longEntry), model);
        try
        {
            var card = Assert.IsType<Border>(((Decorator)lw.Content!).Child); var key = TextAt(card, "Connects");
            var value = Assert.IsAssignableFrom<Panel>(key.Parent).Children.OfType<TextBlock>().Single(x => x != key);
            Assert.Equal("Runs on this PC through offline-fixture-with-a-deliberately-long-command (offline-package-with-a-long-name).", value.Text);
            Assert.True(value.TextLayout.TextLines.Count > 1); Assert.Equal(TextTrimming.None, value.TextTrimming);
            Assert.Equal(value.TextLayout.TextLines.Count * value.TextLayout.TextLines[0].Height, value.Bounds.Height, 3);
            Assert.True(At(value, card).Right <= card.Bounds.Width - 16 + 0.001);
            Assert.Equal(At(key, card).Top + key.TextLayout.TextLines[0].Baseline, At(value, card).Top + value.TextLayout.TextLines[0].Baseline, 6);
        }
        finally { lw.Close(); }
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "UI-MCP-089")] // App/Views/Settings/MCPSettingsPage.swift:36,60-62; OD-0012 F1
    [Trait("Req", "UI-MCP-093")] // App/Views/Settings/MCPSettingsPage.swift:49-59; Core/Models/MCP.swift:50-60; UI/Chrome/Chrome.swift:1037-1052
    [Trait("Req", "UI-MCP-094")] // App/Views/Settings/MCPSettingsPage.swift:95-100
    public void Page_lead_title_category_headers_and_footer_render_in_order_and_spacing(bool dark) => Ui(async (model, _) =>
    {
        await Task.Yield();
        var page = new MCPSettingsPage(""); var w = Show(page, model, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        try
        {
            var stack = Assert.IsType<StackPanel>(page.Child);
            // UI-MCP-094: first in an HStack(spacing: 4), the information button after it.
            var title = TextAt(page, "Connected servers lead"); TextStyle(title, 13, FontWeight.SemiBold, Chrome.PrimaryText);
            var infoButton = Assert.Single(Nodes<MCPHoverInfoButton>(page), b => b.Parent == title.Parent);
            Assert.Equal(At(title, page).Right + 4, At(infoButton, page).Left, 6); Assert.Equal(At(Assert.IsAssignableFrom<Control>(title.Parent), page).Center.Y, At(title, page).Center.Y, 6); /* an HStack centres the 16-point title on the 22-point info button: it is not at the stack's top */
            // UI-MCP-093: one header per non-empty category, declaration order, grid 10 below the header.
            var sections = Nodes<ChromeSection>(page).Where(s => s.Parent == stack).ToArray();
            Assert.Equal(new[] { "Developer", "Browser", "Search", "Work", "Data", "Cloud", "Knowledge" }, sections.Select(s => Texts(s)[0]));
            for (var n = 0; n < sections.Length; n++)
            {
                var heading = TextAt(sections[n], Texts(sections[n])[0]); TextStyle(heading, 13, FontWeight.SemiBold, Chrome.SecondaryText);
                var grid = Assert.Single(Nodes<Grid>(sections[n]), g => g.Parent is StackPanel p && p.Parent == sections[n]);
                Assert.Equal(At(heading, page).Bottom + 10, At(grid, page).Top, 6); Assert.Equal(At(heading, page).Left, At(grid, page).Left, 6);
                Assert.True(grid.Children.Count > 0);
                if (n > 0) Assert.True(At(sections[n - 1], page).Bottom < At(sections[n], page).Top);
            }
            // UI-MCP-089: the last element, 20 below the final section, even with nothing connected.
            var footer = Assert.IsType<TextBlock>(stack.Children[^1]); TextStyle(footer, 11, FontWeight.Normal, Chrome.SecondaryText);
            Assert.Equal("Connected servers reach every provider when a thread starts: the CLIs at launch, the API models through Nth itself.", footer.Text);
            Assert.Equal(At(sections[^1], page).Bottom + 20, At(footer, page).Top, 6); Assert.Equal(At(sections[^1], page).Left, At(footer, page).Left);
            // A category whose entries are all connected, or all filtered out, is omitted, header and all.
            foreach (var entry in MCPCatalog.ByCategory.Single(g => g.Category == MCPCategory.Developer).Entries) State(model, entry.Id, new MCPConnectionState.Connected(1));
            page.SetQuery(""); w.UpdateLayout();
            Assert.DoesNotContain(Nodes<ChromeSection>(page), s => Texts(s)[0] == "Developer"); Assert.Contains(Nodes<ChromeSection>(page), s => Texts(s)[0] == "Connected");
            Assert.Same(footer, stack.Children[^1]);
            page.SetQuery("absent in every catalog"); w.UpdateLayout();
            Assert.DoesNotContain(Nodes<ChromeSection>(page).Where(s => s.Parent == stack), s => Texts(s)[0] is not ("Connected" or "Custom"));
            Assert.Same(footer, stack.Children[^1]); Assert.Equal(At(stack.Children.Where(c => c.IsVisible).SkipLast(1).Last(), page).Bottom + 20, At(footer, page).Top, 6); // :36 an empty Custom body is no child of the VStack, so the footer follows the last visible section
        }
        finally { w.Close(); }
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "UI-MCP-091")] // App/Views/Settings/MCPSettingsPage.swift:1124-1141; Core/Models/MCP.swift:74-79,92; MCPKeyPanel.swift:68; MCPSetupPanel.swift:148
    public void Server_icon_is_sized_by_every_caller_and_tinted_by_the_source_rule(bool dark) => Ui(async (model, _) =>
    {
        await Task.Yield(); var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var github = MCPCatalog.Entries.Single(e => e.Id == "github"); var brand = MCPCatalog.Entries.First(e => !e.IsMonochrome && !e.IsOAuth);
        var keyed = MCPCatalog.Entries.First(e => e.NeedsInput && !e.IsOAuth); var custom = Custom();
        // Catalog data: every asset is the template "mcp-<id>"; exactly these five catalog entries are monochrome.
        Assert.All(MCPCatalog.Entries, e => Assert.Equal("mcp-" + e.Id, e.Asset));
        Assert.Equal(new[] { "deepwiki", "github", "notion", "resend", "vercel" }, MCPCatalog.Entries.Where(e => e.IsMonochrome).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal("", custom.CatalogEntry().Asset); Assert.True(custom.CatalogEntry().IsMonochrome);
        void Icon(Control host, double size, MCPCatalogEntry entry)
        {
            var w = Show(host, model, theme);
            try
            {
                var icon = Assert.Single(Nodes<MCPIcon>(host)); var image = Assert.IsType<SwiftImage>(icon.Child);
                Assert.Equal(new Size(size, size), image.Bounds.Size); // .frame(width: size, height: size)
                Assert.True(image.IsResizable); Assert.Equal(ContentMode.Fit, image.AspectContentMode); Assert.Equal(ImageRenderingMode.Template, image.EffectiveRenderingMode);
                ColorIs(image, image.Foreground, entry.Asset.Length == 0 || entry.IsMonochrome ? Chrome.PrimaryText : SwiftColor.Rgb(entry.ColorValue));
                if (entry.Asset.Length == 0) Assert.Equal("server.rack", image.SymbolName); else Assert.Equal(entry.Asset, image.AssetName);
            }
            finally { w.Close(); }
        }
        State(model, github.Id, new MCPConnectionState.Connected(1)); State(model, brand.Id, new MCPConnectionState.Connected(1));
        Icon(new MCPConnectedRow(github), 22, github); Icon(new MCPConnectedRow(brand), 22, brand); // rows: 22 (:153)
        Icon(new MCPCustomRow(custom), 22, custom.CatalogEntry());
        State(model, github.Id, new MCPConnectionState.NotConnected()); State(model, brand.Id, new MCPConnectionState.NotConnected());
        Icon(new MCPServerCard(brand), 26, brand); Icon(new MCPServerCard(github), 26, github); // cards: 26 (:251)
        Icon(new MCPCustomEditor(custom), 26, custom.CatalogEntry()); // editor header: 26 (:708)
        Icon(new MCPKeyPanelView(keyed, model, () => { }), 26, keyed); // panel headers: 26 (MCPKeyPanel.swift:68)
        Icon(new MCPSetupPanelView(brand, Guide(), model, () => { }), 26, brand); // (MCPSetupPanel.swift:148)
        Icon(new MCPInfoPopover(brand), 28, brand); Icon(new MCPInfoPopover(custom.CatalogEntry()), 28, custom.CatalogEntry()); // info popover: 28 (:976)
        // The rule itself (:1132): an empty asset alone selects the primary text colour and the rack symbol.
        Icon(new MCPIcon(brand with { Asset = "" }, 22), 22, brand with { Asset = "" });
    });
}
