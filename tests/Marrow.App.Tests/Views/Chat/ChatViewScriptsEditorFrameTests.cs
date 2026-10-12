using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// PR-TERM-074 and PR-TERM-076 (DroppyCode/App/Views/Chat/ChatView.swift:1652-1730, :1456-1463). The reference videos hold no scripts editor (CATALOG: only the Run
// menu's "Add a script..." row, C 131.0-132.1, which opens it), so for the comparison the records ask OD-0098 makes the Swift the proof: the sheet is laid out here at the
// size and in the order the code states and every dimension, string, type style and colour is read from that layout. Type sizes are the text styles' (ED-059: title2 17,
// callout 12).
[Collection("Headless")]
public sealed class ChatViewScriptsEditorFrameTests(HeadlessSession headless)
{
    private static (ScriptsEditor Editor, AvaloniaWindow Window, Func<Control, Rect> Box) Mount(AppModel model, Guid projectID, double width = 700, double height = 600)
    {
        var editor = new ScriptsEditor(model, projectID, () => { });
        var window = new AvaloniaWindow { Content = new Border { Child = editor, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top }, Width = width, Height = height };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        return (editor, window, control => new Rect(control.TranslatePoint(default, editor)!.Value, control.Bounds.Size));
    }

    private static Project WithScripts(AppModel model, string folder, params ProjectScript[] scripts)
    {
        var project = model.AddProject(new Project("P", folder).Url) with { Scripts = new ValueList<ProjectScript>(scripts) };
        model.UpdateProject(project.Id, _ => project);
        return project;
    }

    [Fact]
    [Trait("Req", "PR-TERM-074")] // App/Views/Chat/ChatView.swift:1660-1665, :1721-1722, :1456-1463; OD-0098 (no frame of the sheet), OD-0005 and OD-0012 F1 deviations as the ledger names them
    public void The_scripts_editor_is_a_580_by_460_sheet_padded_20_with_its_children_14_apart_titled_Project_scripts_over_the_two_sentence_explanation_on_the_window_backdrop()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = WithScripts(model, h.NewFolder("proj"), new ProjectScript("Build", "make build"));
                var (editor, _, box) = Mount(model, project.Id);
                // :1721-1722 a 580 by 460 point sheet, padded 20.
                Assert.Equal(new Size(580, 460), editor.Bounds.Size);
                var content = Assert.IsType<Border>(editor.Child);
                Assert.Equal(new Thickness(20), content.Padding);
                var stack = Assert.IsType<StackPanel>(content.Child);
                Assert.Equal(14, stack.Spacing); // :1660 VStack(alignment: .leading, spacing: 14)
                // The heading: exactly "Project scripts", the .title2 style at semibold (17 points, ED-059).
                var heading = Assert.IsType<TextBlock>(stack.Children[0]);
                Assert.Equal("Project scripts", heading.Text);
                Assert.Equal(17, heading.FontSize);
                Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
                Assert.Equal(20, box(heading).X);
                Assert.Equal(20, box(heading).Y);
                // Under it the explanation, exactly, in the .callout style (12 points, ED-059) in Chrome.secondaryText, 14 below the heading.
                var explanation = Assert.IsType<TextBlock>(stack.Children[1]);
                Assert.Equal("Scripts run in a terminal at the project root. nth also picks up scripts from droppy-code.json.", explanation.Text); // OD-0012 F1: "Marrow", the file name kept
                Assert.Equal(12, explanation.FontSize);
                Assert.Equal(Chrome.SecondaryText.Resolve(explanation.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(explanation.Foreground).Color);
                Assert.Equal(box(heading).Bottom + 14, box(explanation).Y, 0.5);
                Assert.Equal(20, box(explanation).X);
                // The scroller of the cards follows, again 14 below, at least 220 tall (:1700), and the footer after it.
                var scroll = Assert.IsType<ScrollViewer>(stack.Children[2]);
                Assert.Equal(box(explanation).Bottom + 14, box(scroll).Y, 0.5);
                Assert.Equal(220, scroll.MinHeight);
            });
        });
        // :1456-1463 the sheet paints the app's own window backdrop at the user's backdrop opacity and is clipped to the window's corner radius.
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var recorder = new RecordingPresenter();
            using var presenter = Presentation.Presenter.Override(recorder);
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                ChatGitFixtures.InvokeStatic(typeof(ScriptsMenu), "ShowScriptsEditor", model, project.Id);
            });
            h.Main.RunUntilIdle();
            var shown = Assert.Single(recorder.Calls);
            Control? sheetRoot = null;
            h.Main.OnMain(() =>
            {
                var sheet = Assert.IsType<Border>(shown.Popup.Child);
                shown.Popup.Child = null; // on a window, as the popup's top level would hold it
                var hostWindow = new AvaloniaWindow { Content = sheet, Width = 700, Height = 600 };
                ChatHeadlessFixtures.Track(hostWindow);
                hostWindow.Show();
                Dispatcher.UIThread.RunJobs();
                sheetRoot = sheet;
                Assert.True(sheet.ClipToBounds);
                Assert.Equal(new CornerRadius(Chrome.WindowCornerRadius), sheet.CornerRadius); // clipped to the window corner radius, so it reads as one window's worth of chrome
                var backdrop = Assert.Single(LogicalQuery.SelfAndDescendants<WindowBackdrop>(sheet));
                // ED-338: the sheet is clipped to its own 26 pt shape, so its backdrop's hairline follows that curve (the window backdrops' is the rectangle's)
                Assert.Equal(new CornerRadius(26), Assert.Single(LogicalQuery.SelfAndDescendants<SwiftShapeBorder>(backdrop), layer => layer.BorderThickness != default).CornerRadius);
                double Scrim(WindowBackdrop layer) => Assert.IsAssignableFrom<Control>(typeof(WindowBackdrop).GetField("scrim", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(layer)).Opacity;
                var isDark = PlainControls.IsDark(backdrop.ActualThemeVariant);
                Assert.Equal(WindowBackdrop.Scrim(model.Settings.BackdropOpacity, isDark), Scrim(backdrop), 6); // the glass base under the scrim of the user's backdrop opacity (:258)
                model.Settings.BackdropOpacity = 0.2;
            });
            h.Main.RunUntilIdle();
            h.Main.OnMain(() =>
            {
                var layer = Assert.Single(LogicalQuery.SelfAndDescendants<WindowBackdrop>(sheetRoot!));
                var shownScrim = Assert.IsAssignableFrom<Control>(typeof(WindowBackdrop).GetField("scrim", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(layer)).Opacity;
                Assert.Equal(WindowBackdrop.Scrim(0.2, PlainControls.IsDark(layer.ActualThemeVariant)), shownScrim, 6); // follows the setting
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-076")] // App/Views/Chat/ChatView.swift:1666-1699, :1700; OD-0098 (no frame of the sheet), OD-0009 (Cascadia Mono) as the ledger names it
    [Trait("Decision", "ED-NEW-web-w9-8")]
    public void Each_script_is_a_14_padded_card_of_a_Name_field_filling_the_row_beside_a_trash_button_a_monospaced_Command_field_and_a_worktree_checkbox_8_apart_in_cards_10_apart()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var project = WithScripts(model, h.NewFolder("proj"), new ProjectScript("Build", "make build", runOnWorktreeCreate: true), new ProjectScript("Test", "make test"));
                var (editor, _, box) = Mount(model, project.Id);
                var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(editor).First();
                Assert.Equal(220, scroll.MinHeight); // :1700 .frame(minHeight: 220)
                var cards = LogicalQuery.SelfAndDescendants<ChromeCard>(editor).ToArray();
                Assert.Equal(2, cards.Length);
                Assert.Equal(10, box(cards[1]).Y - box(cards[0]).Bottom, 0.5); // :1667 VStack(spacing: 10)
                var card = cards[0];
                var inner = LogicalQuery.SelfAndDescendants<StackPanel>(card).First(panel => panel.Spacing == 8);
                Assert.Equal(new Thickness(14), inner.Margin); // :1695 .padding(14)
                // DESIGN.md:705 adds optional PreviewUrl. Preserve the original three controls and their spacing, then append its field.
                Assert.Equal(4, inner.Children.Count);
                var nameField = LogicalQuery.SelfAndDescendants<TextBox>(inner).Single(box => box.PlaceholderText == "Name");
                var trash = LogicalQuery.SelfAndDescendants<Button>(inner).Single(button => button.Content is not string);
                // :1674-1678 Button(role: .destructive) { Image(systemName: "trash") }.buttonStyle(.borderless): the SF Symbol "trash", in the destructive colour, with no bezel, border or
                // fill of its own (the label alone: a borderless button draws nothing but its label).
                var symbol = Assert.IsType<SwiftImage>(trash.Content);
                Assert.Equal("trash", symbol.SymbolName);
                Assert.Equal(Chrome.Danger.Resolve(symbol.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(symbol.Foreground).Color);
                var trashParts = trash.GetVisualDescendants().ToArray();
                Assert.Empty(trashParts.OfType<Border>());
                Assert.Empty(trashParts.OfType<Avalonia.Controls.Shapes.Shape>());
                Assert.Null(trash.Background); Assert.Equal(default, trash.BorderThickness); Assert.Equal(default, trash.Padding);
                Assert.Same(symbol, Assert.Single(trashParts.OfType<SwiftImage>()));
                var commandField = LogicalQuery.SelfAndDescendants<TextBox>(inner).Single(box => box.PlaceholderText == "Command");
                var checkbox = LogicalQuery.SelfAndDescendants<CheckBox>(inner).Single();
                var previewField = Assert.IsType<TextBox>(inner.Children[3]);
                Assert.Equal("Preview URL (optional)", previewField.PlaceholderText);
                Assert.Null(editor.Scripts[0].PreviewUrl);
                Assert.Equal("RoundedBorder", previewField.GetValue(ChatDialogControls.InputStyleProperty));
                Assert.Equal("Build", nameField.Text);
                Assert.Equal("make build", commandField.Text);
                Assert.Equal("Run when a worktree is created", checkbox.Content);
                Assert.True(checkbox.IsChecked);
                var row = box(inner.Children[0]); var command = box(inner.Children[1]); var toggle = box(inner.Children[2]);
                Assert.Equal(8, command.Y - row.Bottom, 0.5);
                Assert.Equal(8, toggle.Y - command.Bottom, 0.5);
                Assert.Equal(8, box(previewField).Y - toggle.Bottom, 0.5);
                // The Name field is a flexible rounded-border field filling what the borderless trash button leaves of the card's inner width (512): the HStack's field takes the room.
                var cardInner = box(card).Width - 28;
                Assert.Equal(512, cardInner, 0.5);
                Assert.Equal(cardInner, box(nameField).Width + 8 + box(trash).Width, 1); // 8: the HStack's default spacing between a field and a button (UNVERIFIED, ED-290-2)
                Assert.Equal(box(trash).X + box(trash).Width, box(card).X + 14 + cardInner, 1); // the trash ends at the card's inner edge
                Assert.Equal(box(nameField).Y + box(nameField).Height / 2, box(trash).Y + box(trash).Height / 2, 1); // centred on the same line
                // The Command field fills the card's inner width too, in the .callout style in the monospaced design (12 points, Cascadia Mono, OD-0009).
                Assert.Equal(cardInner, box(commandField).Width, 1);
                Assert.Equal(cardInner, box(previewField).Width, 1);
                Assert.Equal(12, commandField.FontSize);
                Assert.Contains("Cascadia", commandField.FontFamily.Name, StringComparison.Ordinal);
                // Both fields are rounded-border text fields; the card holds no icon picker (the trash is its only button without a text label).
                Assert.Equal("RoundedBorder", nameField.GetValue(ChatDialogControls.InputStyleProperty));
                Assert.Equal("RoundedBorder", commandField.GetValue(ChatDialogControls.InputStyleProperty));
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(card), button => button.Content is not string);
                previewField.Text = "  http://localhost:5173/  ";
                Assert.Equal("http://localhost:5173/", editor.Scripts[0].PreviewUrl);
                previewField.Text = " \t ";
                Assert.Null(editor.Scripts[0].PreviewUrl);
            });
        });
    }
}
