using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Terminal;

// TerminalPanel.swift:64-78: with every tab closed the drawer shows "No shell open in this thread" (body, secondary
// text) over a glass "New shell" button (.buttonStyle(.glass), no .help). The panel is built but never attached, so
// no shell starts (EnsureTerminal runs on appear, :109); the empty state is hosted in a window of its own.
[Collection("Headless")]
public sealed class TerminalEmptyStateTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-TERM-001")]
    public void The_empty_state_shows_its_sentence_and_a_visible_glass_new_shell_button_with_no_tooltip() => headless.OnUiThread(() =>
    {
        var store = new AppModelHarness();
        using var _ = store;
        var project = AppModelSamples.Project("Terminal empty state", store.NewFolder("terminal-empty"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var app = store.MakeModel();
        store.Main.OnMain(() =>
        {
            var runtime = new ThreadRuntime(thread.Id, app);
            var panel = new TerminalPanel(runtime, store.NewFolder("terminal-empty-dir"));
            var make = typeof(TerminalPanel).GetMethod("MakeEmptyState", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var empty = (Control)make.Invoke(panel, [app])!;
            var window = new Avalonia.Controls.Window { Width = 400, Height = 200, Content = empty };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var sentence = empty.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "No shell open in this thread");
                Assert.Equal(Chrome.SecondaryText.Resolve(sentence.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(sentence.Foreground).Color);
                var button = empty.GetVisualDescendants().OfType<Button>().Single();
                // Its words, on a glass capsule (.buttonStyle(.glass)), not a plain button.
                Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "New shell");
                Assert.Contains(button.GetVisualDescendants().OfType<Control>().Prepend(button), control => SwiftGlass.GetEffect(control) is not null);
                Assert.Null(ToolTip.GetTip(button)); // :71-74 no .help
            }
            finally { window.Close(); }
        });
    });
}
