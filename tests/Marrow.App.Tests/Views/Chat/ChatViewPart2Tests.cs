using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Expectations for DroppyCode/App/Views/Chat/ChatView.swift (B3 52bc792), part 2 of 4:
// PR-TERM-064/072/074-080, PR-USAGE-069-074, UI-CHAT-006/073-080.
[Collection("Headless")]
public sealed class ChatViewPart2Tests(HeadlessSession headless)
{
    // ---- helpers (ChatViewPart1Tests precedent) --------------------------------------------------------------

    private static T Show<T>(T control) where T : Control
    {
        // These editor tests inspect state, source dimensions/fonts and input actions, not measured glyph bounds.
        // Attach without measuring the shared headless glyph cache (its fresh-run font failure is documented in SidebarViewTests).
        if (control is ScriptsEditor) control.IsVisible = false;
        var window = new AvaloniaWindow { Content = control };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return control;
    }

    private static string[] Texts(Control control) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button ButtonWithTip(Control root, string tip) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Single(b => Equals(ToolTip.GetTip(b), tip));

    private static Button ButtonWithContent(Control root, string content) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Single(b => Equals(b.Content, content));

    // ChromeIconLabel keeps a private Ellipse in its frame only while isActive (Chrome.cs:596-607): its presence
    // is the only externally visible witness of the active state, since isActive itself is not a public property.
    private static bool HasActiveCircle(Button button) => LogicalQuery.SelfAndDescendants<Ellipse>(button).Any();

    private static ChatThread MakeThread(AppModelHarness harness, AppModel model) =>
        model.NewThread(model.AddProject(new Project("P", harness.NewFolder("proj")).Url))!;

    // Precedent: AppCommandsTests.cs:59 - ShortcutStore's constructor is private; a fresh one reads the harness's
    // own empty defaults, so every chord starts at its documented default.
    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;

    // ---- ScriptsMenu: PR-TERM-064 ----------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-064")] // App/Views/Chat/ChatView.swift:1452-1454,1456-1463
    [Trait("Req", "UI-TERM-019")] // App/Views/Chat/ChatView.swift:1452-1454,1456-1463
    public void The_last_row_switches_between_Add_and_Edit_and_clicking_either_presents_the_editor_without_throwing()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            using var presentation = Presentation.Presenter.Override(new RecordingPresenter()); // R-VIEW-9: real Popup unavailable in headless.
            var model = harness.MakeModel();
            Control emptyMenu = null!, filledMenu = null!;
            harness.Main.OnMain(() =>
            {
                var emptyProject = model.AddProject(new Project("Empty", harness.NewFolder("empty")).Url);
                var filledProject = model.AddProject(new Project("Filled", harness.NewFolder("filled")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make build")]) };
                model.UpdateProject(filledProject.Id, _ => filledProject);
                var thread = model.NewThread(emptyProject)!;

                emptyMenu = Show(ScriptsMenu.BuildMenu(model, emptyProject, model.Runtime(thread.Id)));
                filledMenu = Show(ScriptsMenu.BuildMenu(model, model.Project(filledProject.Id)!, model.Runtime(thread.Id)));
            });

            Assert.Equal(["Add a script…"], Texts(emptyMenu));
            Assert.Equal(["Scripts", "Build", "Edit scripts…"], Texts(filledMenu));

            // PopoverItem.Activate closes the popover then spawns the click action (Popovers.cs), which itself
            // posts ShowScriptsEditor to the main actor (ChatView.cs): draining after the click proves the whole
            // chain runs end to end without throwing. The sheet's own real appearance has no window-observation
            // seam in headless (same precedent as GitActionsMenu.ShowCommitSheet, part 1); its content is instead
            // verified directly by constructing ScriptsEditor below (PR-TERM-074 through PR-TERM-080).
            var addItem = LogicalQuery.SelfAndDescendants<PopoverItem>(emptyMenu).Single();
            Click(LogicalQuery.SelfAndDescendants<Button>(addItem).Single());
            harness.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var editItem = LogicalQuery.SelfAndDescendants<PopoverItem>(filledMenu).Last();
            Click(LogicalQuery.SelfAndDescendants<Button>(editItem).Single());
            harness.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
    }

    // ---- Terminal button: PR-TERM-072 ------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-072")] // App/Views/Chat/ChatView.swift:1164-1167
    [Trait("Req", "UI-CHAT-081")] // App/Views/Chat/ChatView.swift:1164-1167
    [Trait("Req", "UI-TERM-015")] // App/Views/Chat/ChatView.swift:1164-1167
    public void The_terminal_button_reflects_the_drawers_visibility_live_and_flips_it_on_click()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the toggle capsule's circle fades on the frame clock (OD-0076).
            var model = harness.MakeModel();
            ChatChromeRow row = null!;
            ThreadRuntime runtime = null!;
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                runtime = model.Runtime(thread.Id);
                var git = new GitStatusModel();
                row = Show(new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, harness.Scratch, git));

                var terminalButton = ButtonWithTip(row, "Terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal));
                Assert.False(HasActiveCircle(terminalButton));

                Click(terminalButton);
                Assert.True(runtime.IsTerminalVisible); // :1166 direct flag flip
            });

            // Tracked<T>.Schedule (ViewSupport.cs:65-71) posts the rebuild through MainActor.Post rather than
            // running it inline (only ObservationTracker.Fire itself is synchronous, at the write): drain once,
            // as a separate top-level call, before a freshly-built button can show the new isActive state.
            harness.Main.RunUntilIdle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var rebuilt = ButtonWithTip(row, "Terminal" + ShortcutStore.Hint(AppShortcut.ToggleTerminal));
            Assert.True(HasActiveCircle(rebuilt));
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-072")] // App/Views/Chat/ChatView.swift:1165 help: "Terminal" + ShortcutStore.hint(for: .toggleTerminal)
    [Trait("Req", "UI-CHAT-081")] // App/Views/Chat/ChatView.swift:1165 help: "Terminal" + ShortcutStore.hint(for: .toggleTerminal)
    [Trait("Req", "UI-TERM-015")] // App/Views/Chat/ChatView.swift:1165 help: "Terminal" + ShortcutStore.hint(for: .toggleTerminal)
    public void The_terminal_tooltip_carries_the_default_chord_in_Windows_notation_and_is_bare_once_cleared()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            using var shared = ShortcutStore.Shared.Override(harness.Main.OnMain(FreshStore));
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var git = new GitStatusModel();
                var row = new ChatChromeRow(model, model.Runtime(thread.Id), new ChromeScrollModel(), "T", null, harness.Scratch, git);

                // OD-0008 B7: the default chord renders in Windows notation, "Terminal (Ctrl+J)".
                Assert.Equal("Terminal (Ctrl+J)", ToolTip.GetTip(ButtonWithTip(row, "Terminal (Ctrl+J)")));

                ShortcutStore.Shared.Current.Set(null, AppShortcut.ToggleTerminal);
                var clearedRow = new ChatChromeRow(model, model.Runtime(thread.Id), new ChromeScrollModel(), "T", null, harness.Scratch, git);
                Assert.Equal("Terminal", ToolTip.GetTip(ButtonWithTip(clearedRow, "Terminal")));
            });
        });
    }

    // ---- ScriptsEditor: PR-TERM-074 --------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1660-1665,1721-1722
    [Trait("Req", "UI-TERM-020")] // App/Views/Chat/ChatView.swift:1660-1665,1721-1722
    [Trait("Req", "UI-TERM-021")] // App/Views/Chat/ChatView.swift:1660-1665,1721-1722
    [Trait("Req", "UI-TERM-022")] // App/Views/Chat/ChatView.swift:1660-1665,1721-1722
    public void The_editor_is_a_fixed_580_by_460_sheet_titled_Project_scripts_with_the_two_sentence_explanation()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));

                Assert.Equal(580, editor.Width);
                Assert.Equal(460, editor.Height);

                var heading = LogicalQuery.SelfAndDescendants<TextBlock>(editor).Single(t => t.Text == "Project scripts");
                Assert.Equal(17, heading.FontSize); // .title2, ED-059 (B1:ChatZoom.swift:49)
                Assert.Equal(FontWeight.SemiBold, heading.FontWeight);

                // DEVIATION OD-0012 F1: "Droppy Code" -> "Marrow"; the file name droppy-code.json is kept verbatim.
                var explanation = LogicalQuery.SelfAndDescendants<TextBlock>(editor)
                    .Single(t => t.Text == "Scripts run in a terminal at the project root. nth also picks up scripts from droppy-code.json.");
                Assert.Equal(12, explanation.FontSize); // .callout, ED-059 (B1:ChatZoom.swift:52)
            });
        });
    }

    // ---- ScriptsEditor: PR-TERM-075 --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-075")] // App/Views/Chat/ChatView.swift:1657,1723
    public void The_editor_takes_a_copy_of_the_projects_scripts_on_appear_and_a_missing_project_yields_empty()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make build")]) };
                model.UpdateProject(project.Id, _ => project);

                var editor = new ScriptsEditor(model, project.Id, () => { });
                Assert.Single(editor.Scripts);
                Assert.Equal("Build", editor.Scripts[0].Name);

                var missing = new ScriptsEditor(model, Guid.NewGuid(), () => { });
                Assert.Empty(missing.Scripts);
            });
        });
    }

    // ---- ScriptsEditor: PR-TERM-076 --------------------------------------------------------------------------

    [Fact]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1666-1699
    [Trait("Req", "UI-TERM-023")] // App/Views/Chat/ChatView.swift:1666-1699
    [Trait("Req", "UI-TERM-024")] // App/Views/Chat/ChatView.swift:1666-1699
    [Trait("Req", "UI-TERM-027")] // App/Views/Chat/ChatView.swift:1666-1699
    public void Each_card_holds_a_Name_field_a_trash_button_a_monospaced_Command_field_and_a_worktree_checkbox()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make build", runOnWorktreeCreate: true)]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));

                var nameField = LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Name");
                Assert.Equal("Build", nameField.Text);

                var commandField = LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Command");
                Assert.Equal("make build", commandField.Text);
                Assert.Equal(12, commandField.FontSize); // .callout, ED-059
                Assert.Contains("Cascadia", commandField.FontFamily.Name, StringComparison.Ordinal); // OD-0009

                var checkbox = LogicalQuery.SelfAndDescendants<CheckBox>(editor).Single();
                Assert.Equal("Run when a worktree is created", checkbox.Content);
                Assert.True(checkbox.IsChecked);

                // No icon picker, so a trash button is the card's only non-text-labelled button.
                var trash = LogicalQuery.SelfAndDescendants<Button>(editor).Single(b => b.Content is not string);
                Assert.NotNull(trash);
            });
        });
    }

    // ---- ScriptsEditor: PR-TERM-077 --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-077")] // App/Views/Chat/ChatView.swift:1674-1679
    [Trait("Req", "UI-TERM-025")] // App/Views/Chat/ChatView.swift:1674-1679
    public void The_trash_button_removes_the_card_immediately_with_no_confirmation_and_Cancel_still_restores_it()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make build"), new ProjectScript("Test", "make test")]) };
                model.UpdateProject(project.Id, _ => project);

                var cancelled = false;
                var editor = Show(new ScriptsEditor(model, project.Id, () => cancelled = true));
                Assert.Equal(2, editor.Scripts.Count);

                var trash = LogicalQuery.SelfAndDescendants<Button>(editor).Where(b => b.Content is not string).ToList();
                Assert.Equal(2, trash.Count); // one per card, no confirmation dialog anywhere in the tree
                Click(trash[0]);
                Assert.Single(editor.Scripts); // :1675 removed at once, from the local copy

                Click(ButtonWithContent(editor, "Cancel"));
                Assert.True(cancelled);
                // Nothing was written until Save: the project's own scripts are untouched by the removal.
                Assert.Equal(2, model.Project(project.Id)!.Scripts.Count);
            });
        });
    }

    // ---- ScriptsEditor: PR-TERM-078 ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-078")] // App/Views/Chat/ChatView.swift:1684-1691,1711-1718,1726-1729
    [Trait("Req", "UI-TERM-026")] // App/Views/Chat/ChatView.swift:1684-1691,1711-1718,1726-1729
    [Trait("Rule", "R-STR-7")] // App/Views/Chat/ChatView.swift:1726-1729, Foundation .whitespaces includes the tab.
    [Trait("Req", "UI-TERM-030")] // App/Views/Chat/ChatView.swift:1684-1691,1711-1718,1726-1729
    [Trait("Decision", "ED-NEW-web-w9-8")]
    public void A_blank_command_is_outlined_in_danger_and_blocks_saving_while_a_real_command_is_not()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));

                var commandField = LogicalQuery.SelfAndDescendants<TextBox>(editor).Single(t => t.PlaceholderText == "Command");
                // :1686-1691 `.overlay { if incomplete { RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Chrome.danger.opacity(0.65), lineWidth: 1) } }`:
                // the outline is a shape beside the field in a Panel (an overlay, no layout contribution), not a border around it.
                var frame = LogicalQuery.SelfAndDescendants<SwiftShapeBorder>(editor).Single(b => b.Parent is Panel p && p.Children.Contains(commandField));
                Assert.Null(frame.Child);
                Assert.False(frame.IsHitTestVisible);
                Assert.Equal(new Thickness(1), frame.BorderThickness);
                Assert.Equal(new CornerRadius(6), frame.CornerRadius);
                Assert.Equal(RoundedCornerStyle.Continuous, frame.CornerStyle);
                // :1670-1693 the field is the second item of the card's VStack(spacing: 8): the name row above it, the checkbox below.
                var fieldFrame = Assert.IsType<Panel>(frame.Parent);
                var column = Assert.IsType<StackPanel>(fieldFrame.Parent);
                Assert.Equal(8, column.Spacing);
                // DESIGN.md:705 adds optional PreviewUrl after the unchanged Name/Command/worktree controls.
                Assert.Equal(4, column.Children.Count);
                Assert.Same(fieldFrame, column.Children[1]);
                Assert.IsType<Grid>(column.Children[0]); // the name row: an HStack whose field is flexible (a grid with a star column, ED-290-2)
                Assert.IsType<CheckBox>(column.Children[2]);
                var previewField = Assert.IsType<TextBox>(column.Children[3]);
                Assert.Equal("Preview URL (optional)", previewField.PlaceholderText);
                Assert.Null(editor.Scripts[0].PreviewUrl);
                Assert.Equal(Chrome.Danger.Opacity(0.65).Resolve(frame.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color); // :1687-1689 danger at 0.65 while blank
                Assert.False(editor.SaveButton.IsEnabled); // :1711,1717
                Assert.Equal("Every script needs a command.", ToolTip.GetTip(editor.SaveButton)); // :1718

                commandField.Text = "make build";
                Assert.Null(frame.BorderBrush);
                Assert.True(editor.SaveButton.IsEnabled);
                Assert.Equal("Save these scripts", ToolTip.GetTip(editor.SaveButton));
                previewField.Text = "http://localhost:5173/";
                Assert.True(editor.SaveButton.IsEnabled);
                previewField.Text = "";
                Assert.True(editor.SaveButton.IsEnabled); // optional PreviewUrl never substitutes for or blocks command validation

                // A tab is U+0009 CHARACTER TABULATION. Apple documents CharacterSet.whitespaces as Unicode general
                // category Zs plus that exact code point, so trimmingCharacters(in: .whitespaces) reduces a
                // tab-only command to empty: it counts as blank, the same as a run of plain spaces. This record's
                // own failure_cases prose claims the opposite ("a command consisting only of a tab passes the
                // check"); that paraphrase contradicts Apple's documented definition of .whitespaces and the
                // already-verified FoundationText.IsWhitespace/Trim helper (T-M2-023, used everywhere else in this
                // port, which includes tab). The verified behavior is kept here; the packet's prose is not trusted
                // without a source citation contradicting Apple's own documentation. Flagged in the CLOUD REPORT.
                commandField.Text = "\t";
                Assert.NotNull(frame.BorderBrush);
                Assert.False(editor.SaveButton.IsEnabled);

                commandField.Text = "   ";
                Assert.NotNull(frame.BorderBrush);
                Assert.False(editor.SaveButton.IsEnabled);

                // :1726-1729 `.whitespaces` is spaces, tabs and the other Zs characters, NOT newlines: a command holding only a line feed
                // is complete (string.IsNullOrWhiteSpace would call it blank), so there is no outline and Save is held no longer.
                foreach (var spaces in new[] { "\u00A0", "\u2003", " \t " })
                {
                    commandField.Text = spaces;
                    Assert.Equal(spaces, commandField.Text);
                    Assert.NotNull(frame.BorderBrush);
                    Assert.False(editor.SaveButton.IsEnabled, $"{spaces.Length} Zs/tab characters are blank");
                }
                foreach (var withNewline in new[] { "\n", " \n ", "make\n", "\r\n" })
                {
                    commandField.Text = withNewline;
                    Assert.Equal(withNewline, commandField.Text); // the field kept the line break, so the check below is about the command
                    Assert.Null(frame.BorderBrush);
                    Assert.True(editor.SaveButton.IsEnabled, $"a command of {withNewline.Length} characters holding a line break is complete");
                    Assert.Equal("Save these scripts", ToolTip.GetTip(editor.SaveButton));
                }

                // :1681-1682 `.textFieldStyle(.roundedBorder)` (OD-0042 / OD-0045: the Mac look from Apple's macOS UI Kit, "Rounded-border text
                // field", size 3 Rg, layer style Content Area / Input Fields / 01 - Idle; E:/Droppy/work/apple-kit/macos27-uikit-controls.md
                // lines 27-31 and 77-83): 24 high, radius 6, a 1 px border, dark fill #1E1E1E with border white at 0.04, light fill #FFFFFF with
                // border black at 0.05, the text left-aligned and centred vertically. The default bezel is the field's applied template.
                Assert.Equal("RoundedBorder", commandField.GetValue(Marrow.App.Port.ChatDialogControls.InputStyleProperty));
                commandField.ApplyTemplate();
                var bezel = Assert.IsType<Border>(commandField.GetVisualChildren().Single());
                Assert.Equal(new CornerRadius(6), bezel.CornerRadius);
                Assert.Equal(24, bezel.MinHeight);
                Assert.Equal(new Thickness(1), bezel.BorderThickness);
                Assert.Equal(Color.Parse("#1E1E1E"), Marrow.App.Port.ChatDialogControls.FieldFill.Resolve(ThemeVariant.Dark));
                Assert.Equal(Color.Parse("#FFFFFF"), Marrow.App.Port.ChatDialogControls.FieldFill.Resolve(ThemeVariant.Light));
                Assert.Equal(new SwiftColor(white: 1, opacity: 0.04).Resolve(ThemeVariant.Dark), Marrow.App.Port.ChatDialogControls.FieldStroke.Resolve(ThemeVariant.Dark));
                Assert.Equal(new SwiftColor(white: 0, opacity: 0.05).Resolve(ThemeVariant.Light), Marrow.App.Port.ChatDialogControls.FieldStroke.Resolve(ThemeVariant.Light));
                var variant = bezel.ActualThemeVariant;
                Assert.Equal(Marrow.App.Port.ChatDialogControls.FieldFill.Resolve(variant), Assert.IsAssignableFrom<ISolidColorBrush>(bezel.Background).Color);
                Assert.Equal(Marrow.App.Port.ChatDialogControls.FieldStroke.Resolve(variant), Assert.IsAssignableFrom<ISolidColorBrush>(bezel.BorderBrush).Color);
                Assert.Equal(VerticalAlignment.Center, Assert.IsType<ScrollViewer>(bezel.Child).VerticalAlignment);
            });
        });
    }

    // ---- ScriptsEditor: PR-TERM-079 --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-079")] // App/Views/Chat/ChatView.swift:1701-1707; Library.swift:102-108
    [Trait("Req", "UI-TERM-028")] // App/Views/Chat/ChatView.swift:1701-1707; Library.swift:102-108
    public void Add_script_appends_a_New_script_row_with_an_empty_command_and_library_defaults()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));
                Assert.True(editor.SaveButton.IsEnabled); // vacuously true: no scripts yet

                Click(ButtonWithContent(editor, "Add script"));

                var added = Assert.Single(editor.Scripts);
                Assert.Equal("New script", added.Name);
                Assert.Equal("", added.Command);
                Assert.Equal("play", added.Symbol); // Library.swift's memberwise default
                Assert.False(added.RunOnWorktreeCreate);
                Assert.False(editor.SaveButton.IsEnabled); // its own empty command is immediately outlined
            });
        });
    }

    // ---- ScriptsEditor: PR-TERM-080 --------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-TERM-080")] // App/Views/Chat/ChatView.swift:1708-1717
    [Trait("Req", "UI-TERM-029")] // App/Views/Chat/ChatView.swift:1708-1717
    [Trait("Req", "UI-TERM-030")] // App/Views/Chat/ChatView.swift:1708-1717
    public void Cancel_discards_every_change_while_Save_writes_the_whole_list_back_and_dismisses()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "make build")]) };
                model.UpdateProject(project.Id, _ => project);

                var cancelled = false;
                var editorForCancel = Show(new ScriptsEditor(model, project.Id, () => cancelled = true));
                LogicalQuery.SelfAndDescendants<TextBox>(editorForCancel).Single(t => t.PlaceholderText == "Name").Text = "Renamed";
                Click(ButtonWithContent(editorForCancel, "Cancel"));
                Assert.True(cancelled);
                Assert.Equal("Build", model.Project(project.Id)!.Scripts[0].Name); // untouched

                var dismissed = false;
                var editorForSave = Show(new ScriptsEditor(model, project.Id, () => dismissed = true));
                LogicalQuery.SelfAndDescendants<TextBox>(editorForSave).Single(t => t.PlaceholderText == "Name").Text = "Renamed";
                Click(ButtonWithContent(editorForSave, "Add script")); // a second script, to prove the whole list is replaced
                Click(ButtonWithContent(editorForSave, "Save"));
                Assert.True(dismissed);

                var saved = model.Project(project.Id)!.Scripts;
                Assert.Equal(2, saved.Count); // wholesale replacement, not a merge
                Assert.Equal("Renamed", saved[0].Name);
                Assert.Equal("New script", saved[1].Name);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-080")] // App/Views/Chat/ChatView.swift:1717 .disabled(incomplete)
    [Trait("Req", "UI-TERM-029")] // App/Views/Chat/ChatView.swift:1717 .disabled(incomplete)
    [Trait("Req", "UI-TERM-030")] // App/Views/Chat/ChatView.swift:1717 .disabled(incomplete)
    public void Save_is_unreachable_while_any_script_is_incomplete()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("proj")).Url)
                    with
                    { Scripts = new ValueList<ProjectScript>([new ProjectScript("Build", "")]) };
                model.UpdateProject(project.Id, _ => project);
                var editor = Show(new ScriptsEditor(model, project.Id, () => { }));

                Assert.False(editor.SaveButton.IsEnabled);
                Click(editor.SaveButton); // a disabled Button never raises Click in a real UI; belt and braces here
                Assert.Equal("", model.Project(project.Id)!.Scripts[0].Command); // still unsaved either way
            });
        });
    }

    // ---- PanelMembers / PanelScene: PR-USAGE-069 ----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-USAGE-069")] // App/Views/Chat/ChatView.swift:797-806,930,234-241
    public void The_usage_panel_exists_only_when_the_setting_is_on_the_pane_is_measured_and_a_thread_is_loaded()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);

                model.Settings.ShowsUsagePanel = false;
                Assert.Null(new PanelMembers(runtime, model).Usage); // :797 gate

                model.Settings.ShowsUsagePanel = true;
                var members = new PanelMembers(runtime, model);
                Assert.NotNull(members.Usage);
                Assert.Equal(thread.Provider, members.Usage!.Provider); // :803
                Assert.Null(members.Usage.HeadsProvider); // no Hydra pair configured on this thread

                // :926 paneSize != .zero: before the pane reports a size, no panel is placed at all.
                var unmeasured = new PanelScene(members, runtime, model, default, composerAreaHeight: 120, usageContentHeight: null);
                Assert.False(unmeasured.IsMeasured);
                Assert.False(unmeasured.ShowsUsage);

                var measured = new PanelScene(members, runtime, model, new Size(1200, 900), composerAreaHeight: 120, usageContentHeight: null);
                Assert.True(measured.IsMeasured);
                Assert.True(measured.ShowsUsage); // :930 usage != nil && isMeasured
                Assert.True(measured.Slots.ContainsKey(new FloatingPanelID.Usage()));
            });
        });
    }

    // ---- PanelScene: PR-USAGE-070 (settings-level default; the drag/reorder mechanics live in ChatView's own -----
    // ---- un-built body, dock(_:_:nearest:scene:)/dockUsage/reorder, deferred with UI-CHAT-006 below) -------------

    [Fact]
    [Trait("Req", "PR-USAGE-070")] // App/Views/Chat/ChatView.swift:492-494; AppSettings.swift:175,787
    public void The_usage_panel_docks_at_the_setting_which_defaults_to_bottom_leading()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                model.Settings.ShowsUsagePanel = true;
                Assert.Equal(PanelDockCorner.BottomLeading, model.Settings.UsagePanelDock); // documented default

                var members = new PanelMembers(runtime, model);
                var scene = new PanelScene(members, runtime, model, new Size(1200, 900), composerAreaHeight: 120, usageContentHeight: null);
                Assert.Equal(PanelDockCorner.BottomLeading, scene.Slots[new FloatingPanelID.Usage()].Corner);

                model.Settings.UsagePanelDock = PanelDockCorner.TopTrailing;
                var moved = new PanelScene(members, runtime, model, new Size(1200, 900), composerAreaHeight: 120, usageContentHeight: null);
                Assert.Equal(PanelDockCorner.TopTrailing, moved.Slots[new FloatingPanelID.Usage()].Corner);
            });
        });
    }

    // ---- PanelScene.geometry: PR-USAGE-072 --------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-USAGE-072")] // App/Views/Chat/ChatView.swift:945-951,920
    public void The_usage_height_follows_override_then_stored_then_content_then_one_slot_with_a_floor_at_both_ends()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model);
                var runtime = model.Runtime(thread.Id);
                model.Settings.ShowsUsagePanel = true;
                var members = new PanelMembers(runtime, model);
                var roomyPane = new Size(1200, 2000); // room stays far above every wanted value tried below

                // Nothing set at all: falls back to one slot's own height.
                var single = new SubagentPanelLayout { Pane = roomyPane, ComposerAreaHeight = 120, StackDepth = 1, Preferred = model.Settings.PanelSize };
                var baseline = new PanelScene(members, runtime, model, roomyPane, composerAreaHeight: 120, usageContentHeight: null);
                Assert.Equal(single.PanelHeight, baseline.UsageHeight);

                // The content-reported height wins over the slot height.
                var withContent = new PanelScene(members, runtime, model, roomyPane, composerAreaHeight: 120, usageContentHeight: 260);
                Assert.Equal(260, withContent.UsageHeight);

                // A stored setting wins over the content height.
                model.Settings.UsagePanelHeight = 300;
                var withStored = new PanelScene(members, runtime, model, roomyPane, composerAreaHeight: 120, usageContentHeight: 260);
                Assert.Equal(300, withStored.UsageHeight);

                // A live resize override wins over the stored setting.
                var withOverride = new PanelScene(members, runtime, model, roomyPane, composerAreaHeight: 120, usageContentHeight: 260, usageHeightOverride: 350);
                Assert.Equal(350, withOverride.UsageHeight);

                // A tiny wanted value still floors at PanelScene.UsageMinimumHeight (94 = strip height 50 + 44).
                var tinyWanted = new PanelScene(members, runtime, model, roomyPane, composerAreaHeight: 120, usageContentHeight: 260, usageHeightOverride: 1);
                Assert.Equal(PanelScene.UsageMinimumHeight, tinyWanted.UsageHeight);

                // A tiny room floors the same way, regardless of how large the wanted value is.
                model.Settings.UsagePanelHeight = null;
                var tinyPane = new Size(1200, 10);
                var tinyRoom = new PanelScene(members, runtime, model, tinyPane, composerAreaHeight: 0, usageContentHeight: 500);
                Assert.Equal(PanelScene.UsageMinimumHeight, tinyRoom.UsageHeight);
            });
        });
    }

    // ---- UI-CHAT-006, PR-USAGE-071/073/074 (updated 2026-10-03): the column is built and these are proven elsewhere -----
    // UI-CHAT-006: ChatViewFollowupTests.Jump_to_latest_has_the_source_action_zero_height_and_asymmetric_transitions.
    // PR-USAGE-071/073/074 and the drag half of PR-USAGE-070: ChatViewPart2ReviewTests, ChatViewReviewFixTests (a real
    // grip drag), ChatViewParityChromeTests and ChatViewParityMountedTests (a real drag of the panel by its strip).

    // ---- ChatChromeRow states not covered by part 1: UI-CHAT-073 -----------------------------------------------

    [Fact]
    // UI-CHAT-073 stays open for two clauses no headless test reaches: "the gaps between controls drag the window" (the native title-bar hit test, ED-063) and the leading inset of the Mac's window buttons (replaced by OD-0044's trailing caption reserve). The one-glass-pass clause is proven in ChatViewParityAppearTests.The_chrome_row_is_one_glass_pass_holding_its_slots_in_the_fixed_order_in_every_project_and_repository_state.
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1106-1182 (states: no project, not a repository)
    public void With_no_project_the_Scripts_item_is_absent_and_with_no_repository_the_branch_and_git_items_are_absent()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var thread = MakeThread(harness, model); // has a project, but git.Refresh is never awaited below
                var git = new GitStatusModel();
                Assert.False(git.IsRepository);

                var row = Show(new ChatChromeRow(model, model.Runtime(thread.Id), new ChromeScrollModel(), "T", null, harness.Scratch, git));

                var tooltips = LogicalQuery.SelfAndDescendants<Button>(row)
                    .Select(b => ToolTip.GetTip(b) as string).Where(t => t is not null).ToArray()!;
                Assert.DoesNotContain("Run a project script", tooltips); // :1155 project == nil
                Assert.DoesNotContain(tooltips, t => t!.Contains("changed file", StringComparison.Ordinal) ||
                    t!.StartsWith("Commit, push", StringComparison.Ordinal)); // :1159 GitActionsMenu absent
                Assert.DoesNotContain("New branch…", Texts(row)); // :1144-1145 BranchMenu absent
            });
        });
    }

    // ---- UI-CHAT-074 through UI-CHAT-080: covered structurally by part 1's tests --------------------------------
    // These records describe the same controls PR-CHAT-173 (Threads button, UI-CHAT-074), PR-CHAT-175 (New thread,
    // UI-CHAT-075), PR-CHAT-176 (thread title button, UI-CHAT-076), PR-CHAT-177 (title popover menu, UI-CHAT-077),
    // PR-CHAT-178 (rename alert, UI-CHAT-078), PR-CHAT-179 (Permissions menu, UI-CHAT-079) and PR-CHAT-180 (Open in
    // menu, UI-CHAT-080) already exercise in ChatViewPart1Tests.cs: same exact strings, same click behaviour, same
    // absence states. No new behaviour is left for this part to test beyond UI-CHAT-073's own states above; the
    // typography/geometry detail each UI-* record also carries (fonts, paddings) stays UNVERIFIED without macOS
    // per OD-0001, same as every other visual-parity claim in this unit.
}
