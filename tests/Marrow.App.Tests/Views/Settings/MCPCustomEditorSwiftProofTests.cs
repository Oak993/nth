using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc7926), :624-704 MCPCustomEditor's container.
// UI-MCP-047 asks for the editor container compared with the reference; the reference videos hold no frame of it, so under OD-0098 the values the
// Swift states are the proof: the 380 width, the height min(560, base + variables x 34), the 16 point padding, the VStack at spacing 12 holding the header,
// the form's ScrollView (a VStack at spacing 12) and the footer (a VStack at spacing 10) outside the scroll, add mode taking focus and edit mode not,
// and `.presentedChrome()` (capsule button shape and the theme's tint, Popovers.swift:8-12) on the presented tree (:438-439, :571-572).
// NOT proven here, still open on the record: Escape closes the popover (ED-257 (5): the shared popover host, another unit's, does not handle it)
// and the editor in its own native popup window (NATIVE-ONLY).
[Collection("Headless")]
public sealed class MCPCustomEditorSwiftProofTests(HeadlessSession headless)
{
    private void Ui(Action<AppModel, TestRuntime> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        var model = runtime.OnMain(() => new AppModel());
        runtime.OnMain(() => test(model, runtime));
        runtime.OnMain(() => Dispatcher.UIThread.RunJobs());
    });

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Req", "UI-MCP-047")] // MCPSettingsPage.swift:630-658,662: VStack(leading, 12) { header; ScrollView { VStack(leading, 12) }; footer }, padding 16, width 380; the header and footer never scroll
    public void The_container_is_padded_16_with_the_header_the_scrolling_form_and_the_footer_in_a_12_point_stack(bool editing) => Ui((model, _) =>
    {
        var editor = new MCPCustomEditor(editing ? Custom() : null); using var window = Attach(editor, model);
        Assert.Equal(380, editor.Width); // :662 .frame(width: Self.width, ...)
        var padded = Assert.IsType<Border>(editor.Child); Assert.Equal(new Thickness(16), padded.Padding); // :658 .padding(Self.outerPadding)
        var root = Assert.IsType<Grid>(padded.Child);
        Assert.Equal(3, root.Children.Count); Assert.Equal(12, root.RowSpacing); // :630 spacing: Self.sectionSpacing
        var byRow = root.Children.OrderBy(Grid.GetRow).ToArray();
        var scroll = Assert.IsType<ScrollViewer>(byRow[1]); Assert.Equal(1, Grid.GetRow(scroll)); // :632 the middle row scrolls
        var form = Assert.IsType<StackPanel>(scroll.Content); Assert.Equal(12, form.Spacing); // :633 the fields' VStack at spacing 12
        var footer = Assert.IsType<StackPanel>(byRow[2]); Assert.Equal(10, footer.Spacing); // :690 VStack(alignment: .leading, spacing: 10)
        Assert.DoesNotContain(footer, scroll.GetLogicalDescendants()); Assert.DoesNotContain(byRow[0], scroll.GetLogicalDescendants()); // the header and footer sit outside the ScrollView
        Assert.Contains(editing ? "Example" : "New server", Texts(byRow[0])); // :706-729 the header
        Assert.Contains(editing ? "Save" : "Add server", Texts(footer)); // :698 Button(server == nil ? "Add server" : "Save")
    });

    [Theory]
    [InlineData(MCPCustomServer.Kind.Stdio, 450)] [InlineData(MCPCustomServer.Kind.Http, 320)]
    [Trait("Req", "UI-MCP-047")] // MCPSettingsPage.swift:679-685: base 450 for stdio and 320 for http, plus 34 per variable row, capped at 560
    public void The_height_is_the_base_for_the_kind_plus_34_per_variable_row_up_to_560(MCPCustomServer.Kind kind, int baseHeight) => Ui((model, _) =>
    {
        foreach (var rows in new[] { 0, 1, 2, 3, 4, 5, 8, 20 })
        {
            var editor = new MCPCustomEditor(Custom(rows, kind) with { ServerKind = kind }); using var window = Attach(editor, model);
            var count = kind == MCPCustomServer.Kind.Stdio ? Custom(rows, kind).Env.Count : Custom(rows, kind).Headers.Count;
            Assert.Equal(Math.Min(560, baseHeight + count * 34), editor.Height); // :684
        }
    });

    [Fact]
    [Trait("Req", "UI-MCP-047")] // MCPSettingsPage.swift:663-675 onAppear: add mode focuses the name field, edit mode moves no focus
    public void Add_mode_takes_focus_into_the_name_field_and_edit_mode_moves_none() => Ui((model, runtime) =>
    {
        var add = new MCPCustomEditor(null); using var addWindow = Attach(add, model); Dispatcher.UIThread.RunJobs();
        Assert.True(Field(add, "My server").IsFocused, "add mode focuses the Name field"); // :664
        var edit = new MCPCustomEditor(Custom()); using var editWindow = Attach(edit, model); Dispatcher.UIThread.RunJobs();
        Assert.All(Nodes<TextBox>(edit), box => Assert.False(box.IsFocused)); // :665-671 no focus is moved
    });

    [Fact]
    [Trait("Req", "UI-MCP-047")] // MCPSettingsPage.swift:438-439,571-572 .presentedChrome() on the presented editor; Popovers.swift:8-12 capsule button shape and the theme's tint
    public void The_presented_editor_wears_presented_chrome_so_its_buttons_inherit_the_capsule_shape_and_the_tint() => headless.OnUiThread(() =>
    {
        using var rig = new MCPCustomEditorRig();
        rig.OpenAdd();
        var editor = rig.Editor; Assert.NotNull(editor);
        Assert.True(Popovers.GetIsCapsuleButtonBorderShape(editor!)); // :9 .buttonBorderShape(.capsule)
        Assert.Equal(ThemeManager.Spec.Accent, Popovers.GetTint(editor!)); // :10 .tint(ThemeManager.spec.accent)
        var save = Button(editor!, "Add server");
        Assert.True(Popovers.GetIsCapsuleButtonBorderShape(save)); Assert.Equal(ThemeManager.Spec.Accent, Popovers.GetTint(save)); // inherited down the tree
    });
}
