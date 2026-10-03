using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageTestFirst;

namespace Marrow.App.Tests.Views.Settings;

// UI-MCP-051: the editor's Kind picker (MCPSettingsPage.swift:635-653) pressed through its real button and its real popover items.
// The two options, their symbols, the check on the current one, and the switch of kind through the picker itself (not through the
// private method) keeping the typed variable rows and moving only the fields and the height base (:642-653, :683).
[Collection("Headless")]
public sealed class MCPCustomEditorKindPickerTests(HeadlessSession headless)
{
    private const string Stdio = "stdio \U00002014 a command on this PC", Http = "http \U00002014 a remote server"; // U+2014 EM DASH

    [Fact]
    [Trait("Req", "UI-MCP-051")] // App/Views/Settings/MCPSettingsPage.swift:635-653; the picker's glass material is the shared control (ED-180, OD-0005), tracked with its own unit
    public void Kind_picker_opens_two_options_with_symbols_and_selecting_http_swaps_the_fields_and_keeps_the_typed_rows()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
            var model = runtime.OnMain(() => new AppModel());
            MCPCustomEditor editor = null!; IDisposable window = null!; GlassPickerButton<MCPCustomServer.Kind> picker = null!; Button button = null!; Popup popup = null!; TextBox key = null!, value = null!; IDisposable menuWindow = null!; PopoverItem[] items = null!;
            runtime.OnMain(() =>
            {
                editor = new MCPCustomEditor(null); window = Attach(editor, model); Layout(editor);
                picker = Assert.Single(Nodes<GlassPickerButton<MCPCustomServer.Kind>>(editor)); button = Assert.Single(Nodes<Button>(picker)); popup = Assert.Single(Nodes<Popup>(picker));
                // :636 the field is labelled Kind and shows the current option with its symbol.
                Assert.Contains("Kind", Texts(editor)); Assert.Contains(Stdio, Texts(picker)); Assert.Contains(Nodes<SwiftImage>(picker), i => i.SymbolName == "terminal");
                // Typed rows before the switch: Name, Command and one environment row with a key and a value.
                Field(editor, "My server").Text = "Mine"; Field(editor, "npx").Text = "tool"; Click(Button(editor, "Add environment variable"));
                key = Field(editor, "KEY"); value = Field(editor, "value"); key.Text = "MODE"; value.Text = "fast"; Dispatcher.UIThread.RunJobs(); Layout(editor);
                Assert.Equal(new Size(380, 484), editor.Bounds.Size); Assert.Contains("Environment", Texts(editor)); Assert.Contains("Arguments", Texts(editor));
                // Pressing the picker opens its popover with the two options, a symbol each, and the check on the current one only.
                Click(button);
            });
            runtime.OnMain(() =>
            {
                Assert.Same(popup, ((RecordingPresenter)Presentation.Presenter.Current).Calls.Last(c => c.Presented).Popup);
                var menu = Assert.IsAssignableFrom<Control>(PopoverHost.Content(popup)); menuWindow = Attach(menu, model); items = Nodes<PopoverItem>(menu);
                Assert.True(new[] { Stdio, Http }.SequenceEqual(items.Select(i => Texts(i).FirstOrDefault())), "items: " + string.Join(" | ", items.Select(i => string.Join("/", Texts(i)))) + " menu=" + menu.GetType().Name);
                Assert.Equal(new[] { "terminal", "network" }, items.Select(i => Nodes<SwiftImage>(i).Single(image => image.SymbolName != "checkmark").SymbolName));
                Assert.Equal(new[] { 1.0, 0.0 }, items.Select(i => Nodes<SwiftImage>(i).Single(image => image.SymbolName == "checkmark").Opacity));
                // Choosing http: Command and Arguments go, the URL field and "Headers" come, the typed row keeps its very controls, the base is 320.
                Click(Assert.Single(Nodes<Button>(items[1])));
            });
            runtime.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs(); Layout(editor);
                Assert.DoesNotContain("Command", Texts(editor)); Assert.DoesNotContain("Arguments", Texts(editor)); Assert.DoesNotContain("Environment", Texts(editor));
                Assert.Contains("URL", Texts(editor)); Assert.Contains("Headers", Texts(editor)); Assert.Contains("Add header", Texts(editor)); Field(editor, "https://\U00002026");
                Assert.Same(key, Field(editor, "KEY")); Assert.Same(value, Field(editor, "value")); Assert.Equal("MODE", key.Text); Assert.Equal("fast", value.Text);
                Assert.Equal("Mine", Field(editor, "My server").Text); Assert.Equal(new Size(380, 354), editor.Bounds.Size); Assert.Contains(Http, Texts(picker)); Assert.Contains(Nodes<SwiftImage>(picker), i => i.SymbolName == "network");
                Assert.Contains("Custom \U000000B7 http", Texts(editor)); // U+00B7 MIDDLE DOT
                // Choosing stdio again (the same popover) brings the command fields back with the row still there.
                Click(Assert.Single(Nodes<Button>(items[0])));
            });
            runtime.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs(); Layout(editor);
                Assert.Contains("Command", Texts(editor)); Assert.Contains("Arguments", Texts(editor)); Assert.Contains("Environment", Texts(editor)); Assert.Same(key, Field(editor, "KEY")); Assert.Equal(new Size(380, 484), editor.Bounds.Size);
                Assert.Equal("tool", Field(editor, "npx").Text); // the command field is built afresh from the draft's command
                menuWindow.Dispose(); window.Dispose();
            });
        });
    }
}
