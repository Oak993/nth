using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Palette;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views;

// The owner, 2026-10-11, on Settings > Appearance > Sidebar: "these options are broken, they don't do exactly what each option must do".
// ED-NEW-sidebarmodes-1..7. Each option draws what its words say, in the window as a whole (RootView: the column, the floating panel)
// and in the toolbar (the list button, ChatViewParityChromeTests), every switch between them in Settings lands there live and without a
// cut, Ctrl+B and the palette's Toggle sidebar do what the toolbar button does (nothing in Panel only), and the choice survives a restart.
//   Column     - "A column beside the chat; the list button in the toolbar shows and hides it"
//   Floating   - "With the column collapsed, the thread list floats as a panel over the chat and stays where you leave it"
//   Panel only - "No column and no toolbar button; the floating panel is the sidebar"
[Collection("Headless")]
public sealed class SidebarModesTests(HeadlessSession headless)
{
    private const string ColumnWords = "A column beside the chat; the list button in the toolbar shows and hides it";
    private const string FloatingWords = "With the column collapsed, the thread list floats as a panel over the chat and stays where you leave it";
    private const string PanelOnlyWords = "No column and no toolbar button; the floating panel is the sidebar";

    private sealed record Rig(LaunchHarness Launch, AppModel Model, RootView Root, AvaloniaWindow Window, ManualFrameSource Frames)
    {
        public double ColumnWidth => Launch.Harness.Main.OnMain(() => Root.SidebarColumn.Bounds.Width);
        public double DetailX => Launch.Harness.Main.OnMain(() => Root.DetailClip.TranslatePoint(default, Root)!.Value.X);
        public FloatingSidebarPanel? Panel => Launch.Harness.Main.OnMain(() => Root.FloatingPanel);

        // One frame: the clock and the frame source move on together, then the model's posted changes and the layout run.
        public void Step(int milliseconds)
        {
            Launch.Harness.Main.Advance(TimeSpan.FromMilliseconds(milliseconds));
            Launch.Harness.Main.OnMain(() => Frames.Advance(TimeSpan.FromMilliseconds(milliseconds)));
            Settle();
        }

        public void Settle()
        {
            Launch.Harness.Main.RunUntilIdle();
            Launch.Harness.Main.OnMain(() => { Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
            Launch.Harness.Main.RunUntilIdle();
        }

        public void Finish() { for (var i = 0; i < 60; i++) Step(16); }

        public void OnMain(Action action) => Launch.Harness.Main.OnMain(action);
    }

    private static Action<AppModelHarness> Saved(SidebarMode mode, bool startsOpen) => h =>
    {
        // What a previous run left behind (AppSettings.swift:647-661; WindowChrome.swift:245-248).
        h.Defaults.Set("sidebarFloats", mode != SidebarMode.Column);
        h.Defaults.Set("sidebarOnlyFloats", mode == SidebarMode.PanelOnly);
        h.Defaults.Set("sidebarStartsOpen", startsOpen);
    };

    private void WithRoot(Action<Rig> check, Action<AppModelHarness>? prepare = null, bool reduceMotion = false, double width = 1000, double height = 700) => headless.OnUiThread(() =>
    {
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var launch = new LaunchHarness(prepare);
        using var store = ShortcutStore.Shared.Override(launch.Harness.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!));
        var model = launch.Manager.Model;
        AvaloniaWindow window = null!;
        try
        {
            RootView root = null!;
            launch.Harness.Main.OnMain(() =>
            {
                var host = new Border();
                AppEnvironment.SetAppModel(host, model);
                root = new RootView();
                host.Child = root;
                window = new AvaloniaWindow { Content = host, Width = width, Height = height };
                window.Show();
            });
            var rig = new Rig(launch, model, root, window, frames);
            rig.Settle();
            check(rig);
        }
        finally { launch.Harness.Main.OnMain(() => { window?.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    // What an option draws, settled: the column's width, whether the list floats as a panel, and whether that panel can close.
    private static void AssertDraws(Rig rig, SidebarMode mode, bool columnOpen)
    {
        var model = rig.Model;
        Assert.Equal(mode, model.Settings.SidebarMode);
        Assert.Equal(columnOpen, model.Sidebar.IsVisible);
        var columnShown = mode != SidebarMode.PanelOnly && columnOpen;
        Assert.Equal(columnShown ? model.Sidebar.Width : 0, rig.ColumnWidth, 1);
        Assert.Equal(columnShown ? model.Sidebar.Width : 0, rig.DetailX, 1); // the chat starts where the column ends, no gap and no overlap
        var floats = mode == SidebarMode.PanelOnly || (mode == SidebarMode.Floating && !columnOpen);
        var panel = rig.Panel;
        Assert.Equal(floats, panel is not null);
        if (panel is null) return;
        Assert.Equal(mode == SidebarMode.Floating, panel.CanClose); // "Back into the column" only where there is a column
        Assert.Equal(mode == SidebarMode.Floating ? 1 : 0, rig.Launch.Harness.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ChromeCircleButton>(panel)
            .Count(button => ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(button).First()) as string == "Back into the column")));
    }

    public static IEnumerable<object[]> Options() =>
        from mode in SwiftEnum<SidebarMode>.AllCases from open in new[] { false, true } select new object[] { mode, open };

    public static IEnumerable<object[]> Switches() =>
        from start in SwiftEnum<SidebarMode>.AllCases
        from end in SwiftEnum<SidebarMode>.AllCases
        from open in new[] { false, true }
        select new object[] { start, end, open };

    [Fact]
    [Trait("Req", "PR-SETTINGS-255")] // App/AppSettings.swift:53-67
    [Trait("Req", "PR-SETTINGS-256")]
    [Trait("Req", "PR-SETTINGS-257")]
    public void Each_option_reads_its_own_words_and_Column_is_the_default()
    {
        Assert.Equal([SidebarMode.Column, SidebarMode.Floating, SidebarMode.PanelOnly], SwiftEnum<SidebarMode>.AllCases);
        Assert.Equal(["Column", "Floating", "Panel only"], SwiftEnum<SidebarMode>.AllCases.Select(mode => mode.Title));
        Assert.Equal([ColumnWords, FloatingWords, PanelOnlyWords], SwiftEnum<SidebarMode>.AllCases.Select(mode => mode.Detail));
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            Assert.Null(h.Defaults.Object("sidebarFloats"));
            Assert.Equal(SidebarMode.Column, h.Main.OnMain(() => model.Settings.SidebarMode));
            Assert.False(h.Main.OnMain(() => model.Sidebar.IsVisible)); // "Collapsed otherwise" (SettingsView.swift:445)
        });
    }

    // At launch each option, with the column left open or collapsed by "Open the sidebar at launch", draws what its words say: this is
    // also the restart restore, since the mode and the launch state are read back from what the last run stored.
    [Theory]
    [MemberData(nameof(Options))]
    [Trait("Req", "PR-WIN-040")] // App/Views/RootView.swift:9-21,44-51
    [Trait("Req", "PR-WIN-046")]
    public void At_launch_each_option_draws_what_its_words_say(SidebarMode mode, bool startsOpen) => WithRoot(rig =>
    {
        Assert.Equal(startsOpen, rig.Model.Sidebar.StartsOpen);
        AssertDraws(rig, mode, startsOpen);
        // The window's leading-edge strip pulls a collapsed column out in Column and Floating, never in Panel only (:53-63).
        Assert.Equal(mode != SidebarMode.PanelOnly && !startsOpen, rig.Launch.Harness.Main.OnMain(() =>
            ((DragHandleView)typeof(RootView).GetField("leadingHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Root)!).IsActive));
        // The column's own trailing strip resizes it only while it is drawn (:22-31).
        Assert.Equal(mode != SidebarMode.PanelOnly && startsOpen, rig.Launch.Harness.Main.OnMain(() =>
            ((DragHandleView)typeof(RootView).GetField("trailingHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Root)!).IsActive));
    }, Saved(mode, startsOpen));

    // Every switch in Settings, the six between options and the three that pick the option already chosen, from a column open or collapsed,
    // lands live on what the new option draws (ED-NEW-sidebarmodes-6: picking Column brings the column out), keeps a floating panel that
    // stays a floating panel the same panel (ED-NEW-sidebarmodes-4), stores the choice, and moves the column by sliding: never a cut, never
    // past either end, never back.
    [Theory]
    [MemberData(nameof(Switches))]
    [Trait("Req", "PR-SETTINGS-255")] // App/Views/Settings/SettingsView.swift:432-436
    [Trait("Req", "PR-WIN-040")] // App/Views/RootView.swift:9-21,44-51
    public void Every_switch_between_options_lands_live_on_what_the_new_option_draws_without_a_cut(SidebarMode from, SidebarMode to, bool open) => WithRoot(rig =>
    {
        var model = rig.Model;
        AssertDraws(rig, from, open);
        var panelBefore = rig.Panel;
        var widthBefore = rig.ColumnWidth;
        var full = model.Sidebar.Width;
        var openAfter = open || to == SidebarMode.Column;
        var widthAfter = to != SidebarMode.PanelOnly && openAfter ? full : 0;

        rig.OnMain(() => AppearanceSettingsPage.PickSidebarMode(model, to));
        rig.Settle();
        var seen = new List<double> { rig.ColumnWidth };
        Assert.Equal(widthBefore, seen[0], 1); // the first frame is where it was: a change slides, it is not cut
        for (var i = 0; i < 6; i++)
        {
            rig.Step(16);
            seen.Add(rig.ColumnWidth);
            Assert.Equal(rig.ColumnWidth, rig.DetailX, 1); // the chat's edge rides the column's on every frame
        }
        if (widthAfter != widthBefore)
        {
            Assert.Contains(seen, width => width > 1 && width < full - 1); // a frame part-way
            Assert.True(model.Sidebar.IsSliding, "the chat lays its text out once, at the width it is heading for");
            Assert.Equal(seen, widthAfter > widthBefore ? seen.OrderBy(width => width) : seen.OrderByDescending(width => width)); // never back
        }
        else Assert.All(seen, width => Assert.Equal(widthBefore, width, 1)); // nothing that stays may move
        Assert.All(seen, width => Assert.InRange(width, Math.Min(widthBefore, widthAfter) - 1, Math.Max(widthBefore, widthAfter) + 1));
        rig.Finish();

        AssertDraws(rig, to, openAfter);
        if (panelBefore is not null && rig.Panel is { } panelAfter) Assert.Same(panelBefore, panelAfter);
        Assert.Equal(to != SidebarMode.Column, rig.Launch.Harness.Defaults.Object("sidebarFloats"));
        Assert.Equal(to == SidebarMode.PanelOnly, rig.Launch.Harness.Defaults.Object("sidebarOnlyFloats"));
        Assert.False(model.Sidebar.IsSliding);
    }, Saved(from, open));

    // A tile pressed in Settings writes the mode on the picker's own Chrome.hover transaction (its tiles' 0.1 s selection); the column it
    // brings or takes away still slides on the 0.32 s panel spring, not on the hover curve (ED-NEW-sidebarmodes-5, -6).
    [Theory]
    [InlineData(SidebarMode.Column, SidebarMode.PanelOnly, true)]
    [InlineData(SidebarMode.PanelOnly, SidebarMode.Column, false)]
    [Trait("Req", "PR-WIN-046")] // UI/Chrome/WindowChrome.swift:347,352,362
    public void A_pick_on_the_pickers_hover_transaction_slides_the_column_on_the_panel_spring(SidebarMode start, SidebarMode end, bool open) => WithRoot(rig =>
    {
        var model = rig.Model;
        var full = model.Sidebar.Width;
        var before = rig.ColumnWidth;
        rig.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () => AppearanceSettingsPage.PickSidebarMode(model, end)));
        rig.Settle();
        Assert.Equal(before, rig.ColumnWidth, 1);
        for (var i = 0; i < 8; i++) rig.Step(16); // 128 ms: the hover curve would have ended
        var progress = Chrome.PanelSlide.Progress(0.128);
        Assert.InRange(progress, 0.2, 0.95);
        var expected = before + ((end == SidebarMode.PanelOnly ? 0 : full) - before) * progress;
        Assert.Equal(expected, rig.ColumnWidth, tolerance: 2.0); // the layout rounds the drawn width to whole pixels
        rig.Finish();
        Assert.Equal(end == SidebarMode.PanelOnly ? 0 : full, rig.ColumnWidth, 1);
    }, Saved(start, open));

    // The toolbar button (ThreadsButton), Ctrl+B and the palette's Toggle sidebar, in each option: in Column and Floating each shows the
    // column and hides it again, and the window draws it; in Panel only there is no button, Ctrl+B does nothing and the palette has no row.
    [Theory]
    [InlineData(SidebarMode.Column)]
    [InlineData(SidebarMode.Floating)]
    [InlineData(SidebarMode.PanelOnly)]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1125-1131,1249-1254
    [Trait("Req", "PR-WIN-092")] // App/AppCommands.swift:37-39
    public void The_button_Ctrl_B_and_the_palette_show_and_hide_the_column_in_Column_and_Floating_and_do_nothing_in_Panel_only(SidebarMode mode) => WithRoot(rig =>
    {
        var model = rig.Model;
        var hasColumn = mode != SidebarMode.PanelOnly;
        var ctrlB = new KeyboardShortcut(new KeyEquivalent("b"), ChordModifiers.Control);

        // The toolbar.
        ChatChromeRow row = null!;
        rig.OnMain(() =>
        {
            var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", rig.Launch.Harness.NewFolder("p")).Url))!.Id);
            row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, rig.Launch.Harness.Scratch, new GitStatusModel());
            model.SelectedThreadID = null; // the window keeps its empty page: this test is about the sidebar, not the chat
        });
        var button = rig.Launch.Harness.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadsButton>(row).SingleOrDefault());
        Assert.Equal(hasColumn, button is not null);
        void Press(Action press, string what)
        {
            for (var round = 0; round < 2; round++)
            {
                var before = model.Sidebar.IsVisible;
                rig.OnMain(press);
                rig.Finish();
                Assert.True(hasColumn ? model.Sidebar.IsVisible != before : model.Sidebar.IsVisible == before, $"{what}, round {round}");
                AssertDraws(rig, mode, model.Sidebar.IsVisible);
            }
        }
        if (button is not null)
            Press(() => LogicalQuery.SelfAndDescendants<Button>(button).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), "the toolbar button");

        // Ctrl+B (AppCommands.swift:38-39), disabled where there is no column (ED-NEW-sidebarmodes-3).
        Assert.Equal(!hasColumn, rig.Launch.Harness.Main.OnMain(() => new AppCommands(model).Body.Single(command => command.Title == "Toggle sidebar").IsDisabled));
        Press(() => new AppCommands(model).PerformKeyEquivalent(ctrlB), "Ctrl+B");

        // The palette's row (ED-NEW-sidebarmodes-2), right after Add project.
        var titles = rig.Launch.Harness.Main.OnMain(() => PaletteActions(model).Select(item => item.Title).ToList());
        Assert.Equal(hasColumn, titles.Contains("Toggle sidebar"));
        if (!hasColumn) return;
        Assert.Equal(titles.IndexOf("Add project") + 1, titles.IndexOf("Toggle sidebar"));
        var row2 = rig.Launch.Harness.Main.OnMain(() => PaletteActions(model).Single(item => item.Title == "Toggle sidebar"));
        Assert.Equal(("toggle-sidebar", "sidebar.left", "Ctrl+B"), (row2.Id, row2.Symbol, row2.Shortcut));
        Press(() => PaletteActions(model).Single(item => item.Title == "Toggle sidebar").Perform(), "the palette");
    }, Saved(mode, startsOpen: false));

    private static IReadOnlyList<CommandPalette.Item> PaletteActions(AppModel model) =>
        (IReadOnlyList<CommandPalette.Item>)typeof(CommandPalette).GetMethod("BuildResults", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [model, ""])!;

    // The Settings page itself: pressing a tile is the pick above (Column from a collapsed Floating brings the column out), and the words
    // under the picker follow the chosen option.
    [Fact]
    [Trait("Req", "PR-SETTINGS-255")] // App/Views/Settings/SettingsView.swift:432-436
    public void Pressing_a_tile_in_Settings_switches_live_and_the_words_under_the_picker_follow() => WithRoot(rig =>
    {
        var model = rig.Model;
        Border host = null!;
        AvaloniaWindow settings = null!;
        rig.OnMain(() =>
        {
            host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = new AppearanceSettingsPage();
            settings = new AvaloniaWindow { Content = host, Width = 780, Height = 580 };
            settings.Show();
            for (var i = 0; i < 5; i++) { settings.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        });
        try
        {
            void Pick(string title)
            {
                rig.OnMain(() =>
                {
                    var tile = LogicalQuery.SelfAndDescendants<ChromeVisualTile>(host).Single(tile => AutomationProperties.GetName(tile) == title);
                    LogicalQuery.SelfAndDescendants<Button>(tile).First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                rig.Finish();
                rig.OnMain(() => { settings.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
            }
            string[] Words() => rig.Launch.Harness.Main.OnMain(() => LogicalQuery.SelfAndDescendants<TextBlock>(host).Select(block => block.Text ?? "").ToArray());
            AssertDraws(rig, SidebarMode.Floating, columnOpen: false);
            Assert.Contains(FloatingWords, Words());
            Pick("Panel only");
            AssertDraws(rig, SidebarMode.PanelOnly, columnOpen: false);
            Assert.Contains(PanelOnlyWords, Words());
            Assert.DoesNotContain(FloatingWords, Words());
            Pick("Column");
            AssertDraws(rig, SidebarMode.Column, columnOpen: true); // a column beside the chat, at once
            Assert.Contains(ColumnWords, Words());
            Pick("Floating");
            AssertDraws(rig, SidebarMode.Floating, columnOpen: true); // Floating with the column open is the column
            Assert.Contains(FloatingWords, Words());
        }
        finally { rig.OnMain(() => { settings.Close(); Dispatcher.UIThread.RunJobs(); }); }
    }, Saved(SidebarMode.Floating, startsOpen: false));

    // A restart brings back the option chosen in Settings, and the column as "Open the sidebar at launch" says (WindowChrome.swift:292-295).
    [Theory]
    [InlineData(SidebarMode.Column, false)]
    [InlineData(SidebarMode.Column, true)]
    [InlineData(SidebarMode.Floating, false)]
    [InlineData(SidebarMode.Floating, true)]
    [InlineData(SidebarMode.PanelOnly, false)]
    [Trait("Req", "PR-SETTINGS-255")] // App/AppSettings.swift:647-661
    [Trait("Req", "PR-WIN-039")] // UI/Chrome/WindowChrome.swift:287-296
    public void A_restart_restores_the_chosen_option_and_opens_the_column_only_as_Open_at_launch_says(SidebarMode mode, bool startsOpen) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var first = h.MakeModel();
        h.Main.OnMain(() =>
        {
            AppearanceSettingsPage.PickSidebarMode(first, mode == SidebarMode.Column ? SidebarMode.Floating : SidebarMode.Column); // something else first
            AppearanceSettingsPage.PickSidebarMode(first, mode);
            first.Sidebar.StartsOpen = startsOpen;
        });
        h.Main.RunUntilIdle();
        var again = h.Main.OnMain(() => new AppModel()); // the next launch reads the same store
        Assert.Equal(mode, h.Main.OnMain(() => again.Settings.SidebarMode));
        Assert.Equal(startsOpen, h.Main.OnMain(() => again.Sidebar.IsVisible));
    });

    // The column slides on the panel spring as the Mac's does (RootView.swift:19; WindowChrome.swift:362), the chat moving with it on
    // every frame, and Reduce Motion takes it at once (ED-NEW-sidebarmodes-5).
    [Fact]
    [Trait("Req", "PR-WIN-046")] // App/Views/RootView.swift:14-21; UI/Chrome/WindowChrome.swift:298,358-364
    public void The_column_slides_in_from_the_left_edge_full_height_and_the_chat_moves_with_it_then_slides_back_out() => WithRoot(rig =>
    {
        var model = rig.Model;
        Assert.False(model.Sidebar.IsVisible);
        Assert.Equal(0, rig.ColumnWidth);
        var full = model.Sidebar.Width;
        rig.OnMain(() => model.Sidebar.Toggle());
        rig.Settle();
        Assert.True(rig.ColumnWidth < full, "the first frame is not the open column: it slides, it is not cut");
        var seen = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            rig.Step(16);
            seen.Add(rig.ColumnWidth);
            Assert.Equal(rig.ColumnWidth, rig.DetailX, 1);
        }
        Assert.Contains(seen, width => width > 1 && width < full - 1);
        Assert.Equal(seen.OrderBy(width => width), seen);
        rig.Finish();
        rig.OnMain(() =>
        {
            Assert.Equal(full, rig.Root.SidebarColumn.Bounds.Width, 1);
            Assert.Equal(new Point(0, 0), rig.Root.SidebarColumn.TranslatePoint(default, rig.Root)!.Value); // the window's leading edge, from the top
            Assert.Equal(rig.Root.Bounds.Height, rig.Root.SidebarColumn.Bounds.Height, 1); // to the bottom
            Assert.Equal(rig.Root.Bounds.Width - full, rig.Root.DetailClip.Bounds.Width, 1);
        });
        rig.OnMain(() => model.Sidebar.Toggle());
        rig.Settle();
        Assert.True(rig.ColumnWidth > 0, "closing slides too");
        var closing = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            rig.Step(16);
            closing.Add(rig.ColumnWidth);
            Assert.Equal(rig.ColumnWidth, rig.DetailX, 1);
        }
        Assert.Contains(closing, width => width > 1 && width < full - 1);
        Assert.Equal(closing.OrderByDescending(width => width), closing);
        rig.Finish();
        Assert.Equal(0, rig.ColumnWidth);
        Assert.Equal(0, rig.DetailX);
    }, Saved(SidebarMode.Column, startsOpen: false));

    [Theory]
    [InlineData(SidebarMode.Column)]
    [InlineData(SidebarMode.PanelOnly)]
    [Trait("Req", "PR-WIN-208")] // Reduce Motion stills the slide (OD-0076)
    public void With_Reduce_Motion_the_column_comes_and_goes_at_once(SidebarMode other) => WithRoot(rig =>
    {
        var model = rig.Model;
        var full = model.Sidebar.Width;
        rig.OnMain(() => model.Sidebar.Toggle());
        rig.Settle();
        Assert.Equal(full, rig.ColumnWidth, 1); // no frame needed
        Assert.Equal(full, rig.DetailX, 1);
        rig.OnMain(() => AppearanceSettingsPage.PickSidebarMode(model, other == SidebarMode.Column ? SidebarMode.PanelOnly : other));
        rig.Settle();
        Assert.Equal(0, rig.ColumnWidth);
        Assert.Equal(0, rig.DetailX);
        rig.OnMain(() => AppearanceSettingsPage.PickSidebarMode(model, SidebarMode.Column));
        rig.Settle();
        Assert.Equal(full, rig.ColumnWidth, 1);
        rig.OnMain(() => model.Sidebar.Toggle());
        rig.Settle();
        Assert.Equal(0, rig.ColumnWidth);
    }, Saved(SidebarMode.Column, startsOpen: false), reduceMotion: true);

    // "Open the sidebar at launch" shows the column already open on the first frame: nothing slides at launch.
    [Fact]
    [Trait("Req", "PR-WIN-039")] // App/Views/Settings/SettingsView.swift:444-448; UI/Chrome/WindowChrome.swift:292-295
    public void Open_the_sidebar_at_launch_shows_the_column_open_on_the_first_frame() => WithRoot(rig =>
    {
        Assert.True(rig.Model.Sidebar.IsVisible);
        Assert.Equal(rig.Model.Sidebar.Width, rig.ColumnWidth, 1);
        Assert.Equal(rig.Model.Sidebar.Width, rig.DetailX, 1);
    }, Saved(SidebarMode.Column, startsOpen: true));

    // At the window's smallest size (860 x 560, WindowManager.swift:20) every option still lays the column and the chat side by side with
    // nothing over anything, and a floating panel stays inside the chat.
    [Theory]
    [MemberData(nameof(Options))]
    [Trait("Req", "PR-WIN-040")] // App/Views/RootView.swift:9-51
    public void At_the_smallest_window_each_option_lays_out_without_overlap(SidebarMode mode, bool open) => WithRoot(rig =>
    {
        AssertDraws(rig, mode, open);
        rig.OnMain(() =>
        {
            Assert.Equal(860, rig.Root.Bounds.Width, 1);
            var column = rig.Root.SidebarColumn.Bounds.Width;
            var detail = rig.Root.DetailClip;
            Assert.Equal(860 - column - (column > 0 ? 0 : Chrome.SheetInset) - Chrome.SheetInset, detail.Bounds.Width, 1);
            if (rig.Root.FloatingPanel is { } panel)
            {
                var surface = LogicalQuery.SelfAndDescendants<Canvas>(panel).First().Children.OfType<Control>().First();
                var at = surface.TranslatePoint(default, detail)!.Value;
                Assert.InRange(at.X, 0, detail.Bounds.Width - surface.Bounds.Width);
                Assert.InRange(at.Y, 0, detail.Bounds.Height - surface.Bounds.Height);
            }
        });
    }, Saved(mode, open), width: 860, height: 560);
}
