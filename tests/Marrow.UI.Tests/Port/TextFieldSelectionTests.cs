using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// PlainControls.TextField (`.textFieldStyle(.plain)`, PORT-ONLY template): a selection in the field is drawn. The presenter
// draws the highlight from its own SelectionStart/End, which a template must bind to the TextBox's.
[Collection("Headless")]
public sealed class TextFieldSelectionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-107")] // the port-owned field templates (no theme package)
    public void Selecting_the_text_of_a_plain_field_highlights_it_and_the_caret_follows_typing() => headless.OnUiThread(() =>
    {
        var text = "";
        var box = PlainControls.TextField("Prompt", new Binding<string>(() => text, v => text = v));
        var window = new Window { Content = box, Width = 300, Height = 100 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            box.Focus();
            window.KeyTextInput("hello");
            Dispatcher.UIThread.RunJobs();
            var presenter = Assert.Single(box.GetVisualDescendants().OfType<TextPresenter>());
            Assert.Equal(5, presenter.CaretIndex);
            box.SelectAll();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, presenter.SelectionStart);
            Assert.Equal(5, presenter.SelectionEnd);
        }
        finally { window.Close(); }
    });
}
