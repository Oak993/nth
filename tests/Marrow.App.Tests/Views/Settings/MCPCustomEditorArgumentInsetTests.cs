using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageTestFirst;

namespace Marrow.App.Tests.Views.Settings;

// UI-MCP-053 with OD-0032 ("Windows argument-editor inset compensation"): the Mac TextEditor's text sits 5 pt inside its padding
// (SettingsView.swift:829 pads a placeholder by 5 to line up with a TextEditor's text; the reference video, S 199.50, measures that
// placeholder 16.7 px in from the card edge = 12 + 5), so the argument text is 6 + 5 = 11 pt from the surface edge, 1 pt in from the
// Command field's 10 (MCPSettingsPage.swift:777-792). The port's presenter has no such inset, so the box carries it as padding.
[Collection("Headless")]
public sealed class MCPCustomEditorArgumentInsetTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-MCP-053: the inset is proven against the source's own numbers; no Mac frame of this editor exists
    public void Argument_text_starts_eleven_points_in_from_the_surface_edge()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
            runtime.OnMain(() =>
            {
                var editor = new MCPCustomEditor(null); using var window = Attach(editor, new AppModel()); Layout(editor);
                var args = Nodes<TextBox>(editor).Single(box => box.AcceptsReturn); var command = Field(editor, "npx");
                args.Text = "a"; command.Text = "a"; Dispatcher.UIThread.RunJobs(); Layout(editor);
                double Inset(TextBox box)
                {
                    var surface = Assert.IsType<Border>(box.Parent); var presenter = box.GetVisualDescendants().OfType<TextPresenter>().Single();
                    return presenter.TranslatePoint(new Point(0, 0), surface)!.Value.X;
                }
                Assert.Equal(10, Inset(command), 0.5); Assert.Equal(11, Inset(args), 0.5);
                // The vertical inset stays the source's 4 (the TextEditor's own top inset is 0 there: the placeholder of :829 has no vertical padding).
                var top = args.GetVisualDescendants().OfType<TextPresenter>().Single().TranslatePoint(new Point(0, 0), Assert.IsType<Border>(args.Parent))!.Value.Y; Assert.Equal(4, top, 0.5);
            });
        });
    }
}
