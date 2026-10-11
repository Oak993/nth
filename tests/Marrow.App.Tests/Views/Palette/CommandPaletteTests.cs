using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :1-211.
[Collection("Headless")]
public sealed class CommandPaletteTests(HeadlessSession headless)
{
    private static (Border Host, AvaloniaWindow Window, CommandPalette Palette) Build(AppModel model, ThemeVariant? theme = null)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var palette = new CommandPalette();
        host.Child = palette;
        var window = new AvaloniaWindow { Content = host, Width = 900, Height = 700 };
        if (theme is not null) window.RequestedThemeVariant = theme;
        window.Show();
        Settle(window);
        return (host, window, palette);
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            // Forces any pending render (and so any pending text/glyph measurement) to complete now, inside
            // this OnMain call, rather than staying queued on the real Avalonia dispatcher until the headless
            // session's own next-dispatch reset flushes it later, attributed to whichever test runs next.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>A row's fixed children, in the layout order CommandPaletteRow builds them: an icon/text/chord
    /// Grid ("Auto,*,Auto", review fix), not the flat HStack StackPanel it used before review.</summary>
    private static (Panel Icon, TextBlock Title, MiddleTruncatingTextBlock Subtitle, TextBlock Chord, Border Background) RowParts(Control rowControl)
    {
        var decorator = Assert.IsAssignableFrom<Decorator>(rowControl);
        var background = Assert.IsType<Border>(decorator.Child);
        var row = Assert.IsType<Grid>(background.Child);
        var icon = Assert.IsType<Panel>(row.Children[0]);
        var textStack = Assert.IsType<StackPanel>(row.Children[1]);
        var title = Assert.IsType<TextBlock>(textStack.Children[0]);
        var subtitle = Assert.IsType<MiddleTruncatingTextBlock>(textStack.Children[1]);
        var chord = Assert.IsType<TextBlock>(row.Children[2]);
        return (icon, title, subtitle, chord, background);
    }

    private static string[] Titles(CommandPalette palette) =>
        palette.RowsPanel.Children.Select(row => RowParts(row).Title.Text ?? "").ToArray();

    private static Point Centre(Visual control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    // ---- Base action rows, PR-WIN-171, PR-WIN-173 ----

    [Fact]
    [Trait("Req", "PR-WIN-171")] // App/Views/Palette/CommandPalette.swift:151-163
    public void Base_actions_are_new_thread_new_worktree_thread_and_add_project_in_order()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                var titles = Titles(palette);
                Assert.Equal("New thread", titles[0]);
                Assert.Equal("New thread in worktree", titles[1]);
                Assert.Equal("Add project", titles[2]);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-173")] // App/Views/Palette/CommandPalette.swift:176-178
    public void Settings_action_reads_Settings_with_no_ellipsis_after_the_base_actions() // OD-0053: the tour row follows it
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                var titles = Titles(palette);
                Assert.Equal("Toggle sidebar", titles[3]); // ED-NEW-sidebarmodes-2: the View menu's command (AppCommands.swift:38), in Column by default
                Assert.Equal("Settings", titles[4]); // fifth row: no thread selected
            });
        });
    }

    // ---- The Welcome tour row, OD-0012 G5 placed by OD-0053 ----

    [Fact]
    [Trait("Req", "PR-WIN-115")] // App/AppCommands.swift:111-113; DEVIATION OD-0012 G5 / OD-0053: a palette row after Settings
    [Trait("Req", "UI-UPDATE-055")] // App/AppCommands.swift:111-113: titled "Welcome tour", no chord, never disabled
    [Trait("Req", "UI-WIN-027")] // App/AppCommands.swift:111-113
    public void The_welcome_tour_row_follows_settings_with_sparkles_and_no_chord_and_is_found_by_its_title()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            CommandPalette palette = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                var titles = Titles(palette);
                Assert.Equal("Welcome tour", titles[5]); // right after Settings (titles[4]; Toggle sidebar is titles[3], ED-NEW-sidebarmodes-2)
                var row = RowParts(palette.RowsPanel.Children[5]);
                Assert.Equal("sparkles", Assert.IsType<SwiftImage>(Assert.Single(row.Icon.Children)).SymbolName);
                Assert.Equal("welcome-tour", CommandPaletteRig.RowId(palette.RowsPanel.Children[5])); // OD-0053: place, symbol and id
                Assert.True(string.IsNullOrEmpty(row.Chord.Text)); // the source gives the command no shortcut
                Assert.True(palette.RowsPanel.Children[5].IsEnabled);
                // with a thread selected the row still follows Settings
                var project = model.AddProject(new Project("P", directory).Url);
                model.SelectedThreadID = model.NewThread(project)!.Id;
                window.Close();
                (_, window, palette) = Build(model);
                titles = Titles(palette);
                Assert.Equal(Array.IndexOf(titles, "Settings") + 1, Array.IndexOf(titles, "Welcome tour"));
                palette.QueryField.Text = " TOUR ";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(["Welcome tour"], Titles(palette).Where(title => title is "Welcome tour" or "Settings").ToArray());
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-092")] // App/AppCommands.swift:111-113; TourPages.swift:26-28: presents the tour whatever hasSeenTour says
    [Trait("Req", "PR-WIN-115")] // the tour is presented from the palette
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-002: the re-open path from the command palette (OD-0012 G5)
    [Trait("Req", "UI-LAUNCH-002")] // App/Views/Tour/TourPages.swift:26-28; owner_decisions.md OD-0012 G5
    public void Choosing_the_welcome_tour_row_closes_the_palette_and_presents_the_tour_even_after_it_was_seen()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            var tourWindow = typeof(Marrow.App.Views.Tour.TourWindowController).GetField("window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            harness.Main.OnMain(() =>
            {
                model.Settings.HasSeenTour = true;
                Marrow.App.Views.Tour.TourWindowController.Shared.Close();
                Assert.Null(tourWindow.GetValue(Marrow.App.Views.Tour.TourWindowController.Shared));
                CommandPalette palette;
                (_, window, palette) = Build(model);
                model.IsCommandPalettePresented = true;
                var index = Array.IndexOf(Titles(palette), "Welcome tour");
                var point = Centre(palette.RowsPanel.Children[index], window);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            });
            harness.Main.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                Assert.False(model.IsCommandPalettePresented); // :204 close() runs before the action
                Assert.NotNull(tourWindow.GetValue(Marrow.App.Views.Tour.TourWindowController.Shared)); // the tour is up
                Marrow.App.Views.Tour.TourWindowController.Shared.Close();
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-092")] // App/AppCommands.swift:111-113; App/Views/Tour/TourPages.swift:26-28; TourWindowController.swift:27-31: the palette row, the open tour only refocused
    [Trait("Req", "PR-WIN-115")] // App/AppCommands.swift:111-113; OD-0008 C3 (no menu bar), OD-0012 G5, OD-0053 (the row's place, symbol and id)
    [Trait("Req", "UI-UPDATE-055")] // App/AppCommands.swift:111-113: one item, no chord, never disabled; port: no menu bar, the palette row instead
    [Trait("Req", "UI-WIN-027")] // App/AppCommands.swift:111-113
    public void Choosing_the_welcome_tour_row_with_the_tour_open_only_refocuses_it_and_no_window_draws_a_help_menu()
    {
        // The About page's Show button presenting the tour is SettingsViewTests.About_Show_replays_all_eight_tour_pages_even_when_the_tour_was_already_seen.
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            var controller = Marrow.App.Views.Tour.TourWindowController.Shared;
            var tourWindow = typeof(Marrow.App.Views.Tour.TourWindowController).GetField("window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            AvaloniaWindow Tour() => (AvaloniaWindow)tourWindow.GetValue(controller)!;
            void ChooseTourRow()
            {
                (_, window, palette) = Build(model);
                model.IsCommandPalettePresented = true;
                var row = palette.RowsPanel.Children[Array.IndexOf(Titles(palette), "Welcome tour")];
                Assert.True(string.IsNullOrEmpty(RowParts(row).Chord.Text)); // AppCommands.swift:112: no .keyboardShortcut
                var point = Centre(row, window);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                window.Close();
            }
            harness.Main.OnMain(() =>
            {
                model.Settings.HasSeenTour = false;
                controller.Close();
                Assert.Null(tourWindow.GetValue(controller));
                try
                {
                    // The help group holds exactly one item, last, titled "Welcome tour", with no chord, never disabled (:111-113);
                    // the port draws no Help menu and no menu at all (OD-0008 C3).
                    var body = new AppCommands(model).Body;
                    Assert.Single(body, command => command.Title == "Welcome tour");
                    Assert.Equal("Welcome tour", body[^1].Title);
                    Assert.Null(body[^1].Shortcut);
                    Assert.False(body[^1].IsDisabled);
                    Assert.DoesNotContain(body, command => command.Title is "Help" or "Help…");

                    ChooseTourRow();
                    Assert.False(model.IsCommandPalettePresented); // CommandPalette.swift:204 close() runs before the action
                    var first = Tour();
                    Assert.True(first.IsVisible);
                    Assert.Null(NativeMenu.GetMenu(first));
                    Assert.Empty(first.GetVisualDescendants().OfType<Menu>());
                    Assert.False(model.Settings.HasSeenTour); // hasSeenTour is written by the tour's own close, not by presenting it

                    // With the tour open and not the active window, the row only brings that window forward (TourWindowController.swift:27-31).
                    AppCommandsRig.Deactivate(first);
                    Assert.False(first.IsActive);
                    ChooseTourRow();
                    Assert.Same(first, Tour());
                    Assert.True(first.IsActive);
                    Assert.False(model.IsCommandPalettePresented);
                    Assert.False(model.Settings.HasSeenTour);

                    // Closing it from its own checkmark marks the tour seen (TourPages.swift:26-28), as before.
                    var view = Assert.IsType<Marrow.App.Views.Tour.TourView>(first.Content);
                    view.GetVisualDescendants().OfType<Button>()
                        .Single(button => button.GetVisualDescendants().OfType<SwiftImage>().Any(image => image.SymbolName == "checkmark"))
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Assert.True(model.Settings.HasSeenTour);
                }
                finally { controller.Close(); Dispatcher.UIThread.RunJobs(); }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-112")] // App/AppCommands.swift:100-102; App/AppModel.swift:855-882; App/AppSettings.swift:30-34; Views/Palette/CommandPalette.swift:172-174
    [Trait("Req", "PR-WIN-114")] // App/AppCommands.swift:106-108: the settle command is given no palette row
    [Trait("Req", "UI-WIN-024")] // App/AppCommands.swift:99-102; Core/Models/Shortcuts.swift:177, :225
    public void The_finish_row_reads_Settle_Reopen_or_Archive_thread_with_its_symbol_and_chord_follows_the_setting_live_and_the_settle_item_has_no_row()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var shortcuts = ShortcutStore.Shared.Override(harness.Main.OnMain(AppCommandsRig.FreshStore));
            var project = AppModelSamples.Project("P", harness.NewFolder("p"));
            var open = AppModelSamples.Thread(project);
            var settled = AppModelSamples.Thread(project) with { IsSettled = true };
            harness.WriteLibrary([project], [open, settled]);
            AppModelHarness.WriteDocument(open.Id);
            AppModelHarness.WriteDocument(settled.Id);
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            // The finish row sits after "Toggle changes" and before "Settings" (CommandPalette.swift:164-178), whatever thread rows follow.
            (string Title, string Symbol, string Chord) FinishRow()
            {
                var titles = Titles(palette);
                var index = Array.IndexOf(titles, "Toggle changes") + 1;
                Assert.Equal("Settings", titles[index + 1]);
                var row = RowParts(palette.RowsPanel.Children[index]);
                return (titles[index], Assert.IsType<SwiftImage>(Assert.Single(row.Icon.Children)).SymbolName ?? "", row.Chord.Text ?? "");
            }
            harness.Main.OnMain(() =>
            {
                model.Settings.SettleSound = false; // no chime from a test
                // The three titles and their symbols, by setting and selection (AppModel.swift:855-863).
                (ThreadFinishAction Action, Guid Selected, string Title, string Symbol)[] cases =
                [
                    (ThreadFinishAction.Settle, open.Id, "Settle thread", "checkmark.circle"),
                    (ThreadFinishAction.Settle, settled.Id, "Reopen thread", "arrow.uturn.backward"),
                    (ThreadFinishAction.Archive, open.Id, "Archive thread", "archivebox"),
                    (ThreadFinishAction.Archive, settled.Id, "Archive thread", "archivebox"),
                ];
                foreach (var (action, selected, title, symbol) in cases)
                {
                    model.Settings.ThreadFinishAction = action;
                    model.SelectedThreadID = selected;
                    (_, window, palette) = Build(model);
                    var row = FinishRow();
                    Assert.Equal((title, symbol, "Ctrl+Shift+Delete"), row); // Shortcuts.swift:225: Shift-Command-Delete, OD-0008 B7
                    // PR-WIN-114: the settle command has no row of its own, so with Archive chosen neither title is listed.
                    if (action == ThreadFinishAction.Archive) Assert.DoesNotContain(Titles(palette), t => t is "Settle thread" or "Reopen thread");
                    window.Close();
                }

                // The row follows the setting and the selection while the palette stays open (ResultsKey, :102-107).
                model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
                model.SelectedThreadID = open.Id;
                (_, window, palette) = Build(model);
                Assert.Equal("Settle thread", FinishRow().Title);
                model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(("Archive thread", "archivebox"), (FinishRow().Title, FinishRow().Symbol));
                model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
                model.SelectedThreadID = settled.Id;
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(("Reopen thread", "arrow.uturn.backward"), (FinishRow().Title, FinishRow().Symbol));

                // Choosing it with Settle and a settled thread asks to reopen it (AppModel.swift:872-880 reopens: thread.isSettled).
                model.IsCommandPalettePresented = true;
                var rowControl = palette.RowsPanel.Children[Array.IndexOf(Titles(palette), "Reopen thread")];
                var point = Centre(rowControl, window);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.False(model.IsCommandPalettePresented);
                var request = Assert.IsType<AppModel.FinishRequest>(model.AppModelFinishRequest);
                Assert.Equal((settled.Id, true), (request.ThreadID, request.Reopens));
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-171")] // App/Views/Palette/CommandPalette.swift:154-162 exact ids and shortcuts
    public void Base_action_rows_carry_their_exact_ids_symbols_and_live_shortcut_labels()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                var newThread = RowParts(palette.RowsPanel.Children[0]);
                var newWorktree = RowParts(palette.RowsPanel.Children[1]);
                var addProject = RowParts(palette.RowsPanel.Children[2]);

                Assert.Equal(ShortcutStore.Label(AppShortcut.NewThread), newThread.Chord.Text);
                Assert.Equal(ShortcutStore.Label(AppShortcut.NewWorktreeThread), newWorktree.Chord.Text);
                Assert.Equal(ShortcutStore.Label(AppShortcut.AddProject), addProject.Chord.Text);
            });
        });
    }

    // ---- Selected-thread actions, PR-WIN-172, PR-TERM-071, UI-GIT-025, UI-TERM-033 ----

    [Fact]
    [Trait("Req", "PR-TERM-071")] // App/Views/Palette/CommandPalette.swift:164-168
    public void Toggle_terminal_action_is_absent_with_no_thread_selected()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                Assert.DoesNotContain("Toggle terminal", Titles(palette));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-172")] // App/Views/Palette/CommandPalette.swift:164-175
    [Trait("Req", "PR-TERM-071")] // App/Views/Palette/CommandPalette.swift:166-168
    [Trait("Req", "UI-GIT-025")] // App/Views/Palette/CommandPalette.swift:169
    [Trait("Req", "UI-TERM-033")] // App/Views/Palette/CommandPalette.swift:164-168
    public void With_a_thread_selected_terminal_changes_and_finish_actions_are_inserted_before_settings()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                var thread = model.NewThread(project)!;
                model.SelectedThreadID = thread.Id;

                var (_, _, palette) = Build(model);
                var titles = Titles(palette);
                // The new thread itself is also a recent-thread row (:181-189), so its own position among the
                // four base actions moves with the fixture; find the three inserted rows and Settings by name
                // rather than assume a fixed index (PR-WIN-172's own point is their RELATIVE order).
                var terminalIndex = Array.IndexOf(titles, "Toggle terminal");
                var changesIndex = Array.IndexOf(titles, "Toggle changes");
                var finishIndex = Array.IndexOf(titles, model.FinishActionTitle);
                var settingsIndex = Array.IndexOf(titles, "Settings");
                Assert.True(terminalIndex >= 0 && changesIndex >= 0 && finishIndex >= 0 && settingsIndex >= 0);
                Assert.Equal(terminalIndex + 1, changesIndex);
                Assert.Equal(changesIndex + 1, finishIndex);
                Assert.Equal(finishIndex + 1, settingsIndex);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-071")] // App/Views/Palette/CommandPalette.swift:166-168 runtime.isTerminalVisible.toggle()
    public void Running_the_toggle_terminal_action_flips_the_selected_threads_drawer()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                var thread = model.NewThread(project)!;
                model.SelectedThreadID = thread.Id;
                var runtime = model.Runtime(thread.Id);
                Assert.False(runtime.IsTerminalVisible);

                CommandPalette palette;
                (_, window, palette) = Build(model);
                model.IsCommandPalettePresented = true;
                var terminalIndex = Array.IndexOf(Titles(palette), "Toggle terminal");
                Assert.True(terminalIndex >= 0);
                var point = Centre(palette.RowsPanel.Children[terminalIndex], window);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            });
            harness.Main.OnMain(() =>
            {
                Assert.True(model.Runtime(model.SelectedThreadID!.Value).IsTerminalVisible);
                Assert.False(model.IsCommandPalettePresented); // :204 close() runs before the action
            });
        });
    }

    // ---- Query matching, PR-WIN-174 ----

    [Fact]
    [Trait("Req", "PR-WIN-174")] // App/Views/Palette/CommandPalette.swift:150,180 trimmed, lowercased, substring
    public void Query_matching_is_trimmed_lowercased_and_a_plain_substring_test()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            CommandPalette palette = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                palette.QueryField.Text = "  ADD  "; // padded, upper-cased, still matches "Add project"
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var titles = Titles(palette);
                Assert.Contains("Add project", titles);
                Assert.DoesNotContain("New thread", titles); // "new thread" does not contain "add"
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-174")] // App/Views/Palette/CommandPalette.swift:150,180 trimmed, lowercased, substring
    public void A_query_of_only_spaces_behaves_as_an_empty_query()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            CommandPalette palette = null!;
            AvaloniaWindow window = null!;
            string[] emptyQueryTitles = [];
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                emptyQueryTitles = Titles(palette);
                palette.QueryField.Text = "   ";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(emptyQueryTitles, Titles(palette));
            });
        });
    }

    // ---- Recent threads, PR-WIN-175 ----

    [Fact]
    [Trait("Req", "PR-WIN-175")] // App/Views/Palette/CommandPalette.swift:181-189
    public void Recent_threads_exclude_archived_and_panel_threads_newest_first_six_by_default()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                var baseTime = DateTimeOffset.UtcNow;
                for (var i = 0; i < 8; i++)
                {
                    var thread = AppModelSamples.Thread(project) with { Title = "T" + i, UpdatedAt = baseTime.AddMinutes(i) };
                    model.InsertThread(thread);
                }
                model.InsertThread(AppModelSamples.Thread(project) with { Title = "Archived", IsArchived = true, UpdatedAt = baseTime.AddMinutes(100) });
                model.InsertThread(AppModelSamples.Thread(project) with { Title = "Panelled", IsInPanel = true, UpdatedAt = baseTime.AddMinutes(100) });

                var (_, _, palette) = Build(model);
                var titles = Titles(palette).Take(6).ToArray();
                Assert.Equal(["T7", "T6", "T5", "T4", "T3", "T2"], titles); // newest first, six with an empty query
                Assert.DoesNotContain("Archived", Titles(palette));
                Assert.DoesNotContain("Panelled", Titles(palette));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-175")] // App/Views/Palette/CommandPalette.swift:184 prefix(needle.isEmpty ? 6 : 10)
    public void Searching_widens_the_thread_cutoff_to_ten()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                var baseTime = DateTimeOffset.UtcNow;
                for (var i = 0; i < 12; i++)
                    model.InsertThread(AppModelSamples.Thread(project) with { Title = "Match" + i, UpdatedAt = baseTime.AddMinutes(i) });

                (_, window, palette) = Build(model);
                palette.QueryField.Text = "match";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var matches = Titles(palette).Count(title => title.StartsWith("Match", StringComparison.Ordinal));
                Assert.Equal(10, matches);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-175")] // App/Views/Palette/CommandPalette.swift:186-188 title, project name subtitle, bubble.left
    public void A_thread_row_shows_its_title_and_its_projects_name_as_subtitle()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            harness.Main.OnMain(() =>
            {
                // AppModel.AddProject derives Name from the folder's own basename (NewThreadPromptTests's own
                // documented convention): read it back rather than assume the throwaway Project's name survives.
                var project = model.AddProject(new Project("Alpha", directory).Url);
                var thread = AppModelSamples.Thread(project) with { Title = "My thread" };
                model.InsertThread(thread);

                var (_, _, palette) = Build(model);
                var index = Array.IndexOf(Titles(palette), "My thread");
                Assert.True(index >= 0);
                var parts = RowParts(palette.RowsPanel.Children[index]);
                Assert.True(parts.Subtitle.IsVisible);
                Assert.Equal(project.Name, parts.Subtitle.FullText);
                var symbol = Assert.IsType<SwiftImage>(Assert.Single(parts.Icon.Children));
                Assert.Equal("bubble.left", symbol.SymbolName);
            });
        });
    }

    // ---- Project rows, PR-WIN-176 ----

    [Fact]
    [Trait("Req", "PR-WIN-176")] // App/Views/Palette/CommandPalette.swift:190-196
    public void Project_rows_appear_only_while_searching_and_read_New_thread_in_the_project_name()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("marrow");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            string projectName = "";
            string projectPath = "";
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("Marrow", directory).Url);
                projectName = project.Name;
                projectPath = project.Path; // read back what AddProject actually stored, not the raw input
                (_, window, palette) = Build(model);
                Assert.DoesNotContain("New thread in " + projectName, Titles(palette)); // :190 empty query: no project rows

                palette.QueryField.Text = projectName.ToUpperInvariant(); // still matches: case-insensitive
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var index = Array.IndexOf(Titles(palette), "New thread in " + projectName);
                Assert.True(index >= 0);
                var parts = RowParts(palette.RowsPanel.Children[index]);
                Assert.Equal(projectPath, parts.Subtitle.FullText); // :193 subtitle: project.path
            });
        });
    }

    // ---- Ordering and the twelve-row cap, PR-WIN-177 ----

    [Fact]
    [Trait("Req", "PR-WIN-177")] // App/Views/Palette/CommandPalette.swift:197 empty query: threads then actions, no projects
    public void With_an_empty_query_threads_come_before_actions_and_no_project_rows_appear()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                model.InsertThread(AppModelSamples.Thread(project) with { Title = "Recent" });

                var (_, _, palette) = Build(model);
                var titles = Titles(palette);
                Assert.Equal("Recent", titles[0]);
                Assert.Equal("New thread", titles[1]);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-177")] // App/Views/Palette/CommandPalette.swift:197 searching: actions, then threads, then projects
    public void While_searching_actions_come_before_threads_before_projects()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("addison");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            string projectName = "";
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("Addison", directory).Url);
                projectName = project.Name;
                model.InsertThread(AppModelSamples.Thread(project) with { Title = "Add feature" });

                (_, window, palette) = Build(model);
                palette.QueryField.Text = "add";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var titles = Titles(palette);
                var actionIndex = Array.IndexOf(titles, "Add project");
                var threadIndex = Array.IndexOf(titles, "Add feature");
                var projectIndex = Array.IndexOf(titles, "New thread in " + projectName);
                Assert.True(actionIndex >= 0 && threadIndex >= 0 && projectIndex >= 0);
                Assert.True(actionIndex < threadIndex);
                Assert.True(threadIndex < projectIndex);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-177")] // App/Views/Palette/CommandPalette.swift:198 Array(ordered.prefix(12))
    public void Results_are_capped_at_twelve_rows()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                var baseTime = DateTimeOffset.UtcNow;
                ChatThread? first = null;
                // Empty query: at most 6 recent threads (:184) plus, with a thread selected, seven actions
                // (:154-178) = 13 candidate rows, one more than the cap (:198).
                for (var i = 0; i < 7; i++)
                {
                    var thread = AppModelSamples.Thread(project) with { Title = "T" + i, UpdatedAt = baseTime.AddMinutes(i) };
                    model.InsertThread(thread);
                    first ??= thread;
                }
                model.SelectedThreadID = first!.Id;
                (_, window, palette) = Build(model);
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(12, palette.ResultCount);
                Assert.Equal(12, palette.RowsPanel.Children.Count);
            });
        });
    }

    // ---- Cached results, PR-WIN-178 ----

    [Fact]
    [Trait("Req", "PR-WIN-178")] // App/Views/Palette/CommandPalette.swift:102-107 rebuilt only on the ResultsKey
    public void Moving_the_selection_does_not_rebuild_the_row_controls()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            Control[] before = [];
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                before = palette.RowsPanel.Children.ToArray();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(1, palette.Selection);
                Assert.Equal(before, palette.RowsPanel.Children.ToArray()); // same Control instances, never rebuilt
            });
        });
    }

    // ---- Keyboard, PR-WIN-163, PR-WIN-164, PR-WIN-165 ----

    [Fact]
    [Trait("Req", "PR-WIN-163")] // App/Views/Palette/CommandPalette.swift:51-58 clamped both ends, no wrap
    public void Up_and_down_arrows_move_the_selection_one_row_clamped_at_both_ends()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
                window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, "");
            });
            harness.Main.OnMain(() => Assert.Equal(0, palette.Selection)); // clamped at zero, never negative

            harness.Main.OnMain(() =>
            {
                for (var i = 0; i < palette.ResultCount + 3; i++)
                {
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                    window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                }
            });
            harness.Main.OnMain(() => Assert.Equal(palette.ResultCount - 1, palette.Selection)); // clamped at the end
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-164")] // App/Views/Palette/CommandPalette.swift:59-62
    public void Escape_closes_the_palette_even_with_a_non_empty_query()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                model.IsCommandPalettePresented = true;
                palette.QueryField.Text = "still here";
            });
            harness.Main.OnMain(() =>
            {
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "\u001b");
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "\u001b");
            });
            harness.Main.OnMain(() => Assert.False(model.IsCommandPalettePresented));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-165")] // App/Views/Palette/CommandPalette.swift:201-206 dismiss, then act
    public void Return_closes_the_palette_before_running_the_selected_row()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                model.AddProject(new Project("P", directory).Url); // NewThread() needs a current project to land in
                (_, window, _) = Build(model);
                model.IsCommandPalettePresented = true;
            });
            harness.Main.OnMain(() =>
            {
                var before = model.Threads.Count;
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Assert.Equal(before + 1, model.Threads.Count); // row 0 is "New thread" with no thread selected
            });
            harness.Main.OnMain(() => Settle(window)); // the new thread is itself a fresh row now (:181-189)
            harness.Main.OnMain(() => Assert.False(model.IsCommandPalettePresented));
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-165")] // App/Views/Palette/CommandPalette.swift:202 guard items.indices.contains(selection)
    public void Return_on_an_out_of_range_selection_does_nothing_and_leaves_the_palette_open()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                var thread = model.NewThread(project)!;
                model.SelectedThreadID = thread.Id;
                (_, window, palette) = Build(model);
                model.IsCommandPalettePresented = true;

                // With a thread selected the row list has three more rows than without one (:164-175); walk to
                // the last one (Settings, still in range here), matching PR-WIN-172's own fixture shape.
                for (var i = 0; i < palette.ResultCount; i++)
                {
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                    window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, "");
                }
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(palette.ResultCount - 1, palette.Selection); // sanity: really did land on the last row
                // Deselecting the thread drops the three thread-only rows (:164-175) without touching the query,
                // so the cached selection index is now stale: PR-WIN-178 never resets it for this kind of change.
                model.SelectedThreadID = null;
            });
            harness.Main.OnMain(() =>
            {
                Assert.True(palette.Selection >= palette.ResultCount); // now genuinely out of range
                var before = model.Threads.Count;
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                Assert.Equal(before, model.Threads.Count); // the guard caught it: no action ran
            });
            harness.Main.OnMain(() => Assert.True(model.IsCommandPalettePresented));
        });
    }

    // ---- Mouse, PR-WIN-169, PR-WIN-170 ----

    [Fact]
    [Trait("Req", "PR-WIN-169")] // App/Views/Palette/CommandPalette.swift:81-85 entering only, never cleared on leave
    public void Hovering_a_row_moves_the_selection_to_it_and_leaving_does_not_clear_it()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            Control target = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                Assert.True(palette.RowsPanel.Children.Count > 2);
                target = palette.RowsPanel.Children[2];
                var point = Centre(target, window);
                window.MouseMove(point, RawInputModifiers.None);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(2, palette.Selection);
                window.MouseMove(new Point(1, 1), RawInputModifiers.None); // leaves every row
            });
            harness.Main.OnMain(() => Assert.Equal(2, palette.Selection)); // unchanged: leaving never clears it
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-170")] // App/Views/Palette/CommandPalette.swift:86-89 selection = index; run(items)
    public void Clicking_a_row_selects_and_runs_it_closing_the_palette_first()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                model.AddProject(new Project("P", directory).Url);
                (_, window, palette) = Build(model);
                model.IsCommandPalettePresented = true;

                var addProjectIndex = Array.IndexOf(Titles(palette), "Add project");
                var target = palette.RowsPanel.Children[addProjectIndex];
                var point = Centre(target, window);
                window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            });
            harness.Main.OnMain(() => Assert.False(model.IsCommandPalettePresented));
        });
    }

    // ---- Focus on appear, PR-WIN-162 ----

    [Fact]
    [Trait("Req", "PR-WIN-162")] // App/Views/Palette/CommandPalette.swift:100 .onAppear { isFocused = true }
    public void The_search_field_is_focused_as_soon_as_the_palette_appears()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                Assert.True(palette.QueryField.IsFocused);
            });
        });
    }

    // ---- Visual records: scrim (PR-WIN-159, UI-WIN-028) ----

    [Fact]
    [Trait("Req", "PR-WIN-159")] // App/Views/Palette/CommandPalette.swift:34-41
    [Trait("Req", "UI-WIN-028")] // App/Views/Palette/CommandPalette.swift:34-41
    public void The_scrim_is_darker_in_dark_mode_than_light_mode_and_a_click_on_it_closes_the_palette()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, window, palette) = Build(model, ThemeVariant.Light);
                model.IsCommandPalettePresented = true;
                var scrim = Assert.IsType<Panel>(palette.Child).Children[0];
                var brush = Assert.IsType<Border>(scrim).Background;
                var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
                Assert.Equal(0.08, Math.Round(solid.Color.A / 255.0, 2));

                window.MouseDown(new Point(5, 5), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(new Point(5, 5), MouseButton.Left, RawInputModifiers.None);
            });
            harness.Main.OnMain(() => Assert.False(model.IsCommandPalettePresented));
        });
    }

    // ---- Panel geometry (PR-WIN-160, UI-WIN-029) ----

    [Fact]
    [Trait("Req", "PR-WIN-160")] // App/Views/Palette/CommandPalette.swift:96-98
    [Trait("Req", "UI-WIN-029")] // App/Views/Palette/CommandPalette.swift:96-98
    public void The_panel_is_600_wide_72_from_the_top_with_a_24_point_glass_radius()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                var outer = Assert.IsType<Panel>(palette.Child);
                var panel = Assert.IsType<Border>(outer.Children[1]);
                Assert.Equal(600, panel.Width);
                Assert.Equal(new Thickness(0, 72, 0, 0), panel.Margin);
                var effect = SwiftGlass.GetEffect(panel);
                Assert.NotNull(effect);
                Assert.Equal(GlassShapeKind.RoundedRectangle, effect!.Shape.Kind);
                Assert.Equal(24, effect.Shape.CornerRadius);
            });
        });
    }

    // ---- Search row (PR-WIN-161, UI-WIN-030) ----

    [Fact]
    [Trait("Req", "PR-WIN-161")] // App/Views/Palette/CommandPalette.swift:43-49,64-65
    [Trait("Req", "UI-WIN-030")] // App/Views/Palette/CommandPalette.swift:43-49,64-65
    public void The_search_field_has_the_exact_placeholder_and_no_clear_button()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            CommandPalette palette = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                Assert.Equal("Search threads, projects and actions", palette.QueryField.PlaceholderText);
                palette.QueryField.Text = "anything";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                // No clear-button chrome exists in the palette's own field template (unlike the sidebar/chrome fields).
                Assert.DoesNotContain("Clear search", LogicalQuery.SelfAndDescendants<Button>(palette.QueryField)
                    .Select(Avalonia.Automation.AutomationProperties.GetName));
            });
        });
    }

    // ---- No matches (PR-WIN-166, UI-WIN-031) ----

    [Fact]
    [Trait("Req", "PR-WIN-166")] // App/Views/Palette/CommandPalette.swift:66-76
    [Trait("Req", "UI-WIN-031")] // App/Views/Palette/CommandPalette.swift:66-76
    public void A_query_matching_nothing_shows_the_exact_message_with_curly_quotes()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            CommandPalette palette = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                palette.QueryField.Text = "zzzznomatch";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal(0, palette.ResultCount);
                Assert.Same(palette.NoMatchesText, palette.ResultsSlot.Children[0]);
                var expected = "Nothing matches " + (char)0x201C + "zzzznomatch" + (char)0x201D;
                Assert.Equal(expected, palette.NoMatchesText.FullText);
                Assert.Equal(1, palette.NoMatchesText.MaxLines);
            });
        });
    }

    // ---- Row layout (PR-WIN-167, PR-WIN-168, UI-WIN-032) ----

    [Fact]
    [Trait("Req", "PR-WIN-167")] // App/Views/Palette/CommandPalette.swift:77-94
    public void The_row_list_is_inset_six_points_with_rows_two_points_apart()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                Assert.Equal(new Thickness(6, 0, 6, 6), palette.RowsPanel.Margin);
                Assert.Equal(2, palette.RowsPanel.Spacing);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-168")] // App/Views/Palette/CommandPalette.swift:110-141
    // UI-WIN-032 is proven in parts by CommandPaletteRowTests and CommandPaletteChordTests; this test proves only the tint, so it no longer claims the record.
    public void A_selected_row_shows_the_accent_tint_and_an_unselected_row_the_secondary_symbol()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                var first = RowParts(palette.RowsPanel.Children[0]);
                var second = RowParts(palette.RowsPanel.Children[1]);

                var selectedFill = Assert.IsAssignableFrom<ISolidColorBrush>(first.Background.Background);
                var unselectedFill = Assert.IsAssignableFrom<ISolidColorBrush>(second.Background.Background);
                Assert.NotEqual(0, selectedFill.Color.A); // :141 accent at 0.16 for the selected row (index 0)
                Assert.Equal(0, unselectedFill.Color.A); // Color.clear otherwise
            });
        });
    }

    // ---- Row title truncation and chord position, review fixes for PR-WIN-168 ----

    [Fact]
    [Trait("Req", "PR-WIN-168")] // App/Views/Palette/CommandPalette.swift:122-123 lineLimit(1), default .tail truncation
    public void A_row_title_truncates_with_a_trailing_character_ellipsis()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                var parts = RowParts(palette.RowsPanel.Children[0]);
                Assert.Equal(1, parts.Title.MaxLines);
                Assert.Equal(TextTrimming.CharacterEllipsis, parts.Title.TextTrimming);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-168")] // App/Views/Palette/CommandPalette.swift:111,132 Spacer(minLength: 8) actually expands
    public void A_rows_chord_sits_at_the_trailing_edge_regardless_of_the_titles_length()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                // Rows 0-2 are the three base actions (:154-162): "New thread", "New thread in worktree" and
                // "Add project", all carrying a shortcut, with titles of deliberately different lengths. A
                // Spacer(minLength: 8) that only ever expands its fixed 8px minimum, rather than filling the
                // row, would leave the chord's distance from the row's trailing edge grow with the title's
                // width instead of staying constant.
                for (var index = 0; index < 3; index++)
                {
                    var parts = RowParts(palette.RowsPanel.Children[index]);
                    Assert.True(parts.Chord.IsVisible);
                    var topRight = parts.Chord.TranslatePoint(new Point(parts.Chord.Bounds.Width, 0), parts.Background)!.Value;
                    var expectedRight = parts.Background.Bounds.Width - parts.Background.Padding.Right;
                    Assert.True(Math.Abs(topRight.X - expectedRight) < 1.0,
                        $"row {index} (\"{parts.Title.Text}\"): chord right edge {topRight.X}, expected {expectedRight}");
                }
            });
        });
    }

    // ---- Query matching is canonical, review fix for PR-WIN-174 ----

    [Fact]
    [Trait("Req", "PR-WIN-174")] // App/Views/Palette/CommandPalette.swift:150,182 needle.lowercased().contains: canonical, not ordinal (R-STR-2)
    public void Query_matching_finds_a_title_written_with_a_differently_normalized_but_canonically_equal_needle()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            CommandPalette palette = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                // Title has NFC "é"; the query is typed with the NFD form "é" of the same visible
                // character. Swift's contains(_:) compares Characters canonically (R-STR-2), so it matches;
                // an ordinal substring search would not, even though the two forms render identically.
                model.InsertThread(AppModelSamples.Thread(project) with { Title = "Café project" });
                (_, window, palette) = Build(model);
                palette.QueryField.Text = "café";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Contains("Café project", Titles(palette));
            });
        });
    }

    // ---- Cleared chord, PR-WIN-179, UI-WIN-033 ----

    [Fact]
    [Trait("Req", "PR-WIN-179")] // App/Views/Palette/CommandPalette.swift:133-137
    // UI-WIN-033 is proven in parts by CommandPaletteChordTests; this test proves only the thread row, so it no longer claims the record.
    public void A_thread_row_never_carries_a_chord()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                model.InsertThread(AppModelSamples.Thread(project) with { Title = "Only thread" });

                var (_, _, palette) = Build(model);
                var index = Array.IndexOf(Titles(palette), "Only thread");
                var parts = RowParts(palette.RowsPanel.Children[index]);
                Assert.False(parts.Chord.IsVisible); // omitted entirely, never an empty label
            });
        });
    }
}
