using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

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

    [Fact]
    [Trait("Req", "PR-TERM-001")]
    public void The_drawer_is_white_8_percent_in_dark_and_98_point_5_percent_in_light()
    {
        // TerminalPanel.swift:107 .fill(colorScheme == .dark ? Color(white: 0.08) : Color(white: 0.985))
        var colour = typeof(TerminalPanel).GetMethod("PanelColor", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(Color.FromRgb(20, 20, 20), (Color)colour.Invoke(null, [true])!);   // 0.08 x 255 = 20.4
        Assert.Equal(Color.FromRgb(251, 251, 251), (Color)colour.Invoke(null, [false])!); // 0.985 x 255 = 251.2
    }

    // Live emptying of the drawer: Closing every tab through the store while the panel is shown (the user closing the
    // last tab). The sentence and button are looked up in the live panel's own tree.
    private static TextBlock Sentence(Control root) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(root).Single(text => text.Text == "No shell open in this thread");

    private static Button NewShell(Control root) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "New shell");

    [Fact]
    [Trait("Req", "PR-TERM-010")] // App/Views/Terminal/TerminalPanel.swift:64-78
    [Trait("Req", "UI-TERM-013")] // App/Views/Terminal/TerminalPanel.swift:67-70
    public void Closing_every_tab_keeps_the_drawer_open_at_its_height_and_shows_the_sentence_centred_ten_points_above_the_button_without_reopening_a_shell() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("empty live");
        rig.Step(() => rig.Runtime.IsTerminalVisible = true);
        var (host, panel, window) = rig.Show(directory, 500, 400);
        var height = panel.Height;
        Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(host), text => text.Text == "No shell open in this thread");

        rig.Step(() => rig.App.Terminals.CloseAll(rig.Thread.Id));
        rig.Step(() => TerminalPanelRig.Settle(window));

        // :65-66 the drawer stays open and does NOT reopen a shell by itself: onAppear fired once, when mounted.
        Assert.Empty(rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.True(rig.Runtime.IsTerminalVisible);
        Assert.Equal(height, panel.Height); // keeps its height
        Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalTab>(host));

        var sentence = Sentence(host);
        var button = NewShell(host);
        Assert.Equal(13, sentence.FontSize); // :69 .font(.system(.body)), ED-059
        Assert.Equal(Chrome.SecondaryText.Resolve(sentence.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(sentence.Foreground).Color); // :70
        var sentenceBox = new Rect(sentence.TranslatePoint(new Point(), panel)!.Value, sentence.Bounds.Size);
        var buttonBox = new Rect(button.TranslatePoint(new Point(), panel)!.Value, button.Bounds.Size);
        Assert.Equal(10, buttonBox.Top - sentenceBox.Bottom, 3); // :67 VStack(spacing: 10)
        // :77 centred in the area below the 38-point row, across the full width of the drawer.
        Assert.InRange(sentenceBox.Center.X, panel.Bounds.Width / 2 - 1, panel.Bounds.Width / 2 + 1); // layout rounding of odd widths
        Assert.InRange(buttonBox.Center.X, panel.Bounds.Width / 2 - 1, panel.Bounds.Width / 2 + 1);
        var contentCentre = (sentenceBox.Top + buttonBox.Bottom) / 2;
        Assert.InRange(contentCentre, 38 + (panel.Bounds.Height - 38) / 2 - 1, 38 + (panel.Bounds.Height - 38) / 2 + 1);
    });

    [Fact]
    [Trait("Req", "PR-TERM-010")] // App/Views/Terminal/TerminalPanel.swift:71-75
    [Trait("Req", "UI-TERM-014")] // App/Views/Terminal/TerminalPanel.swift:71-75
    public void The_New_shell_button_opens_a_session_exactly_as_the_plus_button_does_and_the_empty_state_goes_away() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("empty new shell");
        var (host, _, _) = rig.Show(directory);
        rig.Step(() => rig.App.Terminals.CloseAll(rig.Thread.Id));
        var button = NewShell(host);
        Assert.True(button.IsEnabled); // always enabled while the empty state is shown
        Assert.Null(ToolTip.GetTip(button)); // no .help, unlike the plus button's "New terminal"

        rig.Step(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

        // Terminal N with N = existing count + 1 (none), in the panel's directory, selected, running: what the plus does.
        var opened = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.Equal("Terminal 1", opened.Title);
        Assert.Equal(directory, opened.Directory);
        Assert.True(opened.IsRunning);
        Assert.Equal(opened.Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.Single(LogicalQuery.SelfAndDescendants<TerminalTab>(host));
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(host), text => text.Text == "No shell open in this thread"); // shown only with zero sessions
        Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(host), candidate => AutomationProperties.GetName(candidate) == "New shell");

        // And a second time, after emptying again: the same path, the number restarts from the count.
        rig.Step(() => rig.App.Terminals.CloseAll(rig.Thread.Id));
        rig.Step(() => NewShell(host).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.Equal("Terminal 1", Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id)).Title);
    });
}
