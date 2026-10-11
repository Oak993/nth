using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Parity closure for ChatView.swift (1.7.0): the chrome row's buttons, title, popovers and clipboard writes, and the
// usage panel's height ladder and reset.
[Collection("Headless")]
public sealed class ChatViewParityChromeTests(HeadlessSession headless)
{
    private static readonly FieldInfo LifetimeField = typeof(Application).GetField("_applicationLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static AvaloniaWindow Host(Control control, double width = 1400)
    {
        var window = new AvaloniaWindow { Content = control, Width = width, Height = 300 };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static ThreadRuntime NewRuntime(AppModelHarness h, AppModel model) =>
        model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("project")).Url))!.Id);

    private static string? Tip(Control control) => ToolTip.GetTip(control) as string;

    private static string[] Symbols(Control root) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(root).Select(image => image.SymbolName ?? "").ToArray();

    private static PopoverItem Row(Control menu, string title) => LogicalQuery.SelfAndDescendants<PopoverItem>(menu)
        .Single(item => Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item)).FullText == title);

    private static void Click(PopoverItem item) => ChatGitFixtures.Click(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(item)));

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static WriteableBitmap Pixel()
    {
        var bitmap = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        return bitmap;
    }

    // A prior clipboard flavour (an image) the clear-then-set write must not leave behind (ChatView.swift:1222-1223,1427-1428).
    private static void SeedClipboard(IClipboard clipboard)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText("previous text"));
        data.Add(DataTransferItem.Create(DataFormat.Bitmap, (Bitmap)Pixel()));
        clipboard.SetDataAsync(data).GetAwaiter().GetResult();
    }

    private static (string? Text, bool HasBitmap) ReadClipboard(IClipboard clipboard)
    {
        var data = clipboard.TryGetDataAsync().GetAwaiter().GetResult();
        var formats = data?.Formats ?? [];
        return (clipboard.TryGetTextAsync().GetAwaiter().GetResult(), formats.Contains(DataFormat.Bitmap));
    }

    // ---- PR-CHAT-173 / UI-CHAT-074 / PR-CHAT-174: the toolbar's sidebar button (ED-NEW-sidebarmodes-1) -----------------------
    // The Mac drew it only while the column was away and, in Column mode, hung a popover of the sidebar from it; the port has no
    // menu bar to hide an open column from, so the button stays in Column and Floating, open or not, always shows or hides the
    // column, and is absent only in Panel only ("No column and no toolbar button").

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, false, true, false, true)] // sidebarOnlyFloats means nothing without sidebarFloats.
    [InlineData(false, true, true, false, false)] // Panel only: the list floats as the only sidebar, no button.
    [InlineData(true, false, false, true, true)] // the column is open and holds the window buttons: the button stays, to hide it.
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, true, false)]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1125-1131,1249-1254; ED-NEW-sidebarmodes-1
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1125-1131,1244-1265
    public void The_sidebar_button_leads_the_row_in_Column_and_Floating_open_or_not_and_is_absent_only_in_Panel_only(bool holds, bool floats, bool onlyFloats, bool open, bool present)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = floats;
                model.Settings.SidebarOnlyFloats = onlyFloats;
                if (open) model.Sidebar.Toggle();
                Assert.Equal(open, model.Sidebar.IsVisible);
                if (holds) model.Sidebar.NoteLaidOutWidth(400);
                Assert.Equal(holds, model.Sidebar.HoldsTrafficLights);
                var row = new ChatChromeRow(model, NewRuntime(h, model), new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                var button = LogicalQuery.SelfAndDescendants<ThreadsButton>(row).SingleOrDefault();
                Assert.Equal(present, button is not null);
                if (button is null) return;
                Assert.Same(LogicalQuery.SelfAndDescendants<StackPanel>(row).First().Children[0], SwiftTransition.Container(button)); // the first slot.
                var words = (open ? "Hide the sidebar" : "Show the sidebar") + ShortcutStore.Hint(AppShortcut.ToggleSidebar);
                var press = LogicalQuery.SelfAndDescendants<Button>(button).Single();
                Assert.Equal(words, Tip(press));
                Assert.Equal(words, Avalonia.Automation.AutomationProperties.GetName(press));
                Assert.Equal(["list.bullet"], Symbols(button));
            });
        });
    }

    [Theory]
    [InlineData(false)] // Column: "the list button in the toolbar shows and hides it" (AppSettings.swift:63)
    [InlineData(true)] // Floating: the Mac's own press (ChatView.swift:1253), and now the way back to the panel
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1251-1254; ED-NEW-sidebarmodes-1
    [Trait("Req", "PR-CHAT-174")] // App/Views/Chat/ChatView.swift:1255-1259, removed: no popover of the sidebar
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1244-1265
    public void The_press_shows_and_hides_the_column_and_never_presents_a_popover_its_words_following_in_place(bool floats)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var recorder = new RecordingPresenter();
            using var presenter = Presentation.Presenter.Override(recorder);
            using var store = ShortcutStore.Shared.Override(h.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!));
            Button button = null!;
            ThreadsButton threads = null!;
            void Drain() { h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); }
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = floats;
                Assert.False(model.Sidebar.IsVisible);
                threads = new ThreadsButton(model);
                Host(threads, 400);
                button = LogicalQuery.SelfAndDescendants<Button>(threads).Single();
                Assert.Equal("Show the sidebar (Ctrl+B)", Tip(button)); // the default chord, in Windows notation
                ChatGitFixtures.Click(button);
                Assert.True(model.Sidebar.IsVisible); // the column, at once
            });
            Drain();
            h.Main.OnMain(() =>
            {
                Assert.Empty(recorder.Calls); // nothing hung from the button
                Assert.Same(button, LogicalQuery.SelfAndDescendants<Button>(threads).Single()); // the same button, its words changed in place
                Assert.Equal("Hide the sidebar (Ctrl+B)", Tip(button));
                Assert.Equal("Hide the sidebar (Ctrl+B)", Avalonia.Automation.AutomationProperties.GetName(button));
                ChatGitFixtures.Click(button); // the same press hides it again
                Assert.False(model.Sidebar.IsVisible);
            });
            Drain();
            h.Main.OnMain(() =>
            {
                Assert.Empty(recorder.Calls);
                Assert.Equal("Show the sidebar (Ctrl+B)", Tip(button));
                Assert.Empty(LogicalQuery.SelfAndDescendants<SidebarView>(threads)); // no popover form of the sidebar anywhere in it
            });
            Assert.Null(typeof(ThreadsButton).GetMethod("BuildSidebarPopover", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)); // the dropdown is gone
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1129-1131 `.transition(.softAppear)`; ED-NEW-sidebarmodes-1
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1130
    public void The_sidebar_button_stays_when_the_column_takes_the_window_buttons_and_leaves_for_Panel_only_through_soft_appear_rather_than_a_cut()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            ChatChromeRow row = null!;
            ThreadsButton before = null!;
            void Run() { for (var i = 0; i < 40; i++) { time.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); } }
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                row = new ChatChromeRow(model, NewRuntime(h, model), new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                Host(row);
                before = Assert.Single(LogicalQuery.SelfAndDescendants<ThreadsButton>(row));
                model.Sidebar.NoteLaidOutWidth(400); // the window buttons move to the sidebar: the button stays.
            });
            Run();
            Assert.Same(before, Assert.Single(h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadsButton>(row))));
            h.Main.OnMain(() => model.Settings.SidebarFloats = true); // Column to Floating: the same button, no exit and no arrival
            Run();
            Assert.Same(before, Assert.Single(h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadsButton>(row))));
            h.Main.OnMain(() => model.Settings.SidebarOnlyFloats = true); // Panel only: it leaves
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadsButton>(row))); // still on screen mid-fade, not cut.
            Run();
            Assert.Empty(h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadsButton>(row)));
        });
    }

    // ---- PR-CHAT-176 / UI-CHAT-076 / PR-CHAT-177 / UI-CHAT-077: the title button and its popover ------------------------

    [Fact]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1199-1214,1238
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1189-1240
    [Trait("Req", "PR-CHAT-177")] // App/Views/Chat/ChatView.swift:1215-1218 the popover hangs off the title's own width
    [Trait("Req", "UI-CHAT-077")] // App/Views/Chat/ChatView.swift:1215-1230
    public void The_title_button_is_its_own_width_inside_the_leftover_frame_and_its_press_toggles_a_popover_under_the_words()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var recorder = new RecordingPresenter();
            using var presenter = Presentation.Presenter.Override(recorder);
            Button button = null!;
            AvaloniaWindow window = null!;
            ChatChromeRow row = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.HydraEnabled = false; // no Hydra intro popover in the recorder.
                row = new ChatChromeRow(model, NewRuntime(h, model), new ChromeScrollModel(), "Fix it", null, h.Scratch, new GitStatusModel());
                window = Host(row, 1400);
                var title = LogicalQuery.SelfAndDescendants<ChatChromeTitle>(row).Single();
                button = LogicalQuery.SelfAndDescendants<Button>(title).Single();
                var words = LogicalQuery.SelfAndDescendants<TextBlock>(button).Single();
                Assert.True(title.Bounds.Width > 600, $"the title frame takes the leftover width ({title.Bounds.Width})"); // :1238
                Assert.Equal(words.DesiredSize.Width, button.Bounds.Width, 1); // the button is the words' width (:1215-1217).
                Assert.Equal(0, button.TranslatePoint(default, title)!.Value.X); // leading-aligned.
                // The trailing capsules sit at the row's trailing end, after the title's leftover frame.
                var last = LogicalQuery.SelfAndDescendants<ChromeCapsule>(row).Last();
                var rowEnd = row.TranslatePoint(new Point(row.Bounds.Width, 0), window)!.Value.X;
                Assert.Equal(rowEnd - WindowChrome.CaptionTrailingReserve, last.TranslatePoint(new Point(last.Bounds.Width, 0), window)!.Value.X, 1);
                // The press region is the button alone: the gap after it is the row's, which drags the window (ED-063).
                var grab = LogicalQuery.SelfAndDescendants<NoWindowDragView>(title).Single();
                Assert.Equal(button.Bounds.Width, grab.Bounds.Width, 1);
                Assert.Equal(Brushes.Transparent, Assert.IsType<Border>(button.Content).Background); // :1209 .contentShape(.rect)
                ChatGitFixtures.Click(button);
            });
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            var (popup, presented) = Assert.Single(recorder.Calls);
            Assert.True(presented);
            Assert.Same(button, popup.PlacementTarget); // anchored to the words, not the leftover width.
            Assert.Equal(PlacementMode.Bottom, popup.Placement); // :1218 arrowEdge: .bottom
            var menu = popup.Child!;
            Assert.Equal(["doc.on.doc"], Symbols(Row(menu, "Copy title")));
            Assert.Equal(["pencil"], Symbols(Row(menu, "Rename\u2026")));
            h.Main.OnMain(() => ChatGitFixtures.Click(button)); // :1201 isPresented.toggle()
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.False(recorder.Calls[^1].Presented);
        });
    }

    // ---- The row against the reference still (2-chat-use.mov at C 11 s, stills/chat-11.png, 1 px = 1 pt) --------------
    // Measured on the Mac with the sidebar holding the window buttons, a project, no repository, Hydra on: from the content
    // area's leading edge the three circles span 14-46, 56-88 and 98-130, the title starts at 140, the circles' tops sit 12
    // below the window top; the trailing capsules are 63 wide, 8 apart and 32 high. Side by side:
    // E:\Droppy\work\parity\evidence\ChatView\chrome_row_port_vs_mac.png (and _left/_right zooms), and with the terminal open
    // (the "on" circle behind the terminal glyph, 27 grey levels over its capsule on the Mac, 22 on the port's darker glass)
    // terminal_toggle_on_port_vs_mac.png. The trailing end differs by OD-0044 (the capsules end clear of the Windows caption
    // buttons); glyph shapes by OD-0010 (Lucide); the glass by OD-0005 (UNVERIFIED, ED-180); the title face by OD-0009 (Inter).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/ChatView.swift:1123-1174 (geometry; the single glass pass stays STOP #3)
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1209 (the capture of the non-empty title)
    [Trait("Req", "PR-TERM-062")] // App/Views/Chat/ChatView.swift:1153-1158 (the capsule [Open in | Run] as the video shows it)
    [Trait("Req", "PR-TERM-072")] // App/Views/Chat/ChatView.swift:1164-1167 (both states, the "on" circle)
    public void The_row_lays_out_at_the_positions_measured_on_the_Mac()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.HydraEnabled = true;
                var project = model.AddProject(new Project("LifeOS-Migration", h.NewFolder("project")).Url);
                var runtime = model.Runtime(model.NewThread(project)!.Id);
                model.Sidebar.NoteLaidOutWidth(400);
                // The Mac was measured with its column holding the window buttons, where it draws no list button. The port keeps the list
                // button in that state (ED-NEW-sidebarmodes-1), so the same row without it is Panel only's; the list button's own slot
                // in Column is checked after.
                model.Settings.SidebarMode = SidebarMode.PanelOnly;
                var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "Establish LifeOS repo baseline framework", project, project.Path, new GitStatusModel())
                { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                var window = Host(row, 2290);
                Rect Box(Control c) => new(c.TranslatePoint(default, window)!.Value, c.Bounds.Size);
                var circles = new Control[]
                {
                    LogicalQuery.SelfAndDescendants<ChromeCircleButton>(row).First(),
                    LogicalQuery.SelfAndDescendants<HydraButton>(row).Single(),
                    LogicalQuery.SelfAndDescendants<ChromeCircleMenu>(row).Single(),
                };
                var expected = new[] { 14d, 56, 98 };
                for (var i = 0; i < 3; i++)
                {
                    var surface = LogicalQuery.SelfAndDescendants<Button>(circles[i]).First();
                    Assert.Equal(expected[i], Box(surface).X, 0.5);
                    Assert.Equal(32, surface.Bounds.Width, 0.5);
                    Assert.Equal(12, Box(surface).Y, 0.5);
                }
                var words = LogicalQuery.SelfAndDescendants<TextBlock>(LogicalQuery.SelfAndDescendants<ChatChromeTitle>(row).Single()).Single();
                Assert.Equal(140, Box(words).X, 0.5);
                var capsules = LogicalQuery.SelfAndDescendants<ChromeCapsule>(row).ToArray();
                Assert.Equal(2, capsules.Length);
                Assert.All(capsules, capsule => { Assert.Equal(63, capsule.Bounds.Width, 0.5); Assert.Equal(32, capsule.Bounds.Height, 0.5); });
                Assert.Equal(8, Box(capsules[1]).X - Box(capsules[0]).Right, 0.5);
                Assert.Equal(12, Box(capsules[0]).Y, 0.5);
                Assert.Equal(2290 - WindowChrome.CaptionTrailingReserve, Box(capsules[1]).Right, 0.5); // OD-0044 (the Mac: 2290 - 14).
                window.Close();

                // Column, the column open and holding the window buttons: the list button takes the first 32-point slot and every control
                // after it moves along by one slot (32 + the row's 10-point spacing); the trailing capsules stay put.
                model.Settings.SidebarMode = SidebarMode.Column;
                var withList = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "Establish LifeOS repo baseline framework", project, project.Path, new GitStatusModel())
                { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                window = Host(withList, 2290);
                var list = LogicalQuery.SelfAndDescendants<Button>(LogicalQuery.SelfAndDescendants<ThreadsButton>(withList).Single()).Single();
                Assert.Equal(14, Box(list).X, 0.5);
                Assert.Equal(12, Box(list).Y, 0.5);
                Assert.Equal(32, list.Bounds.Width, 0.5);
                var moved = new Control[]
                {
                    LogicalQuery.SelfAndDescendants<ChromeCircleButton>(withList).Single(c => (Tip(LogicalQuery.SelfAndDescendants<Button>(c).First()) ?? "").StartsWith("New thread", StringComparison.Ordinal)),
                    LogicalQuery.SelfAndDescendants<HydraButton>(withList).Single(),
                    LogicalQuery.SelfAndDescendants<ChromeCircleMenu>(withList).Single(),
                };
                for (var i = 0; i < 3; i++) Assert.Equal(expected[i] + 42, Box(LogicalQuery.SelfAndDescendants<Button>(moved[i]).First()).X, 0.5);
                Assert.Equal(140 + 42, Box(LogicalQuery.SelfAndDescendants<TextBlock>(LogicalQuery.SelfAndDescendants<ChatChromeTitle>(withList).Single()).Single()).X, 0.5);
                Assert.Equal(2290 - WindowChrome.CaptionTrailingReserve, Box(LogicalQuery.SelfAndDescendants<ChromeCapsule>(withList).Last()).Right, 0.5);
            });
        });
    }

    [Theory]
    [InlineData("", "New thread")]
    [InlineData("Fix the build", "Fix the build")]
    [Trait("Req", "PR-CHAT-176")] // App/Views/Chat/ChatView.swift:1203-1211
    [Trait("Req", "UI-CHAT-076")] // App/Views/Chat/ChatView.swift:1203-1205 colours
    public void The_title_reads_New_thread_in_the_secondary_colour_when_empty_and_the_title_in_the_primary_colour(string title, string shown)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var view = new ChatChromeTitle(model, NewRuntime(h, model), title);
                Host(view);
                var text = LogicalQuery.SelfAndDescendants<TextBlock>(view).Single();
                Assert.Equal(shown, text.Text);
                var expected = (title.Length == 0 ? Chrome.SecondaryText : Chrome.PrimaryText).Resolve(text.ActualThemeVariant);
                Assert.Equal(expected, BrushColor(text.Foreground));
                Assert.NotEqual((title.Length == 0 ? Chrome.PrimaryText : Chrome.SecondaryText).Resolve(text.ActualThemeVariant), BrushColor(text.Foreground));
                // :1211 .buttonStyle(.plain): the shared plain template, no bezel.
                var button = LogicalQuery.SelfAndDescendants<Button>(view).Single();
                Assert.Same(PlainControls.Button(new Border(), () => { }).Template, button.Template);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-177")] // App/Views/Chat/ChatView.swift:1221-1224
    [Trait("Req", "UI-CHAT-077")] // App/Views/Chat/ChatView.swift:1221-1224
    public void Copy_title_replaces_every_clipboard_flavour_with_the_title()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                var anchor = new Button();
                window = Host(anchor);
                SeedClipboard(window.Clipboard!);
                Assert.True(ReadClipboard(window.Clipboard!).HasBitmap);
                Click(Row(ChatChromeTitle.BuildMenu(model, NewRuntime(h, model), anchor, "Copied title"), "Copy title"));
            });
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            h.Main.RunUntilIdle();
            var (text, bitmap) = ReadClipboard(window.Clipboard!);
            Assert.Equal("Copied title", text);
            Assert.False(bitmap); // cleared first: the image does not survive.
        });
    }

    // ---- PR-CHAT-180 / UI-CHAT-080: the Open in menu's button and Copy path ------------------------------------------

    [Fact]
    [Trait("Req", "PR-CHAT-180")] // App/Views/Chat/ChatView.swift:1411-1432
    [Trait("Req", "UI-CHAT-080")] // App/Views/Chat/ChatView.swift:1411-1432
    public void Open_in_is_an_arrow_up_forward_app_menu_button_first_in_its_capsule_and_Copy_path_replaces_the_clipboard()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var workspace = new ChatWorkspaceFixture();
            var model = h.MakeModel();
            AvaloniaWindow window = null!;
            var previous = LifetimeField.GetValue(Application.Current);
            try
            {
                h.Main.OnMain(() =>
                {
                    var row = new ChatChromeRow(model, NewRuntime(h, model), new ChromeScrollModel(), "T", null, @"C:\work dir", new GitStatusModel());
                    window = Host(row);
                    LifetimeField.SetValue(Application.Current, new ClassicDesktopStyleApplicationLifetime { MainWindow = window });
                    var openIn = LogicalQuery.SelfAndDescendants<OpenInMenu>(row).Single();
                    var capsule = LogicalQuery.SelfAndDescendants<ChromeCapsule>(row).First();
                    Assert.Same(openIn, LogicalQuery.SelfAndDescendants<StackPanel>(capsule).Single(panel => panel.Children.Contains(openIn)).Children[0]);
                    Assert.Single(LogicalQuery.SelfAndDescendants<ChromeMenuButton>(openIn));
                    Assert.Equal("Open in another app", Tip(LogicalQuery.SelfAndDescendants<Button>(openIn).Single()));
                    Assert.Equal(["arrow.up.forward.app"], Symbols(openIn));
                    SeedClipboard(window.Clipboard!);
                    var menu = OpenInMenu.BuildMenu(@"C:\work dir");
                    Assert.Equal(["doc.on.doc"], Symbols(Row(menu, "Copy path")));
                    Click(Row(menu, "Copy path"));
                });
                h.Main.RunUntilIdle();
                Dispatcher.UIThread.RunJobs();
                h.Main.RunUntilIdle();
                var (text, bitmap) = ReadClipboard(window.Clipboard!);
                Assert.Equal(@"C:\work dir", text);
                Assert.False(bitmap);
            }
            finally { LifetimeField.SetValue(Application.Current, previous); }
        });
    }

    // ---- PR-USAGE-072: the lift a tall usage panel gives the panels stacked beyond it -----------------------------------

    [Fact]
    [Trait("Req", "PR-USAGE-072")] // App/Views/Chat/ChatView.swift:952-954,997-1006
    public void A_usage_panel_taller_than_a_slot_lifts_the_panel_stacked_beyond_it_by_the_excess()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var runtime = NewRuntime(h, model);
                model.Settings.ShowsUsagePanel = true;
                model.Settings.UsagePanelDock = PanelDockCorner.TopLeading;
                runtime.HydraPanelDock = PanelDockCorner.TopLeading;
                runtime.PanelStackOrder = new ValueList<FloatingPanelID>([new FloatingPanelID.Usage(), new FloatingPanelID.Hydra()]);
                model.Settings.UsagePanelHeight = 420;
                var head = runtime.Thread! with { Id = Guid.NewGuid() };
                var members = new PanelMembers(runtime, model) with { Heads = new ValueList<ChatThread>([head]) };
                var pane = new Size(1600, 1400);
                var scene = new PanelScene(members, runtime, model, pane, 120, null);
                var single = new SubagentPanelLayout { Pane = pane, ComposerAreaHeight = 120, StackDepth = 1, Preferred = model.Settings.PanelSize, Compact = !model.Settings.HydraShowsHeadDetails };
                Assert.Equal(420, scene.UsageHeight);
                var usage = scene.Slots[new FloatingPanelID.Usage()];
                var hydra = scene.Slots[new FloatingPanelID.Hydra()];
                Assert.Equal(0, usage.Below);
                Assert.Equal(1, hydra.Below);
                Assert.Equal(0, usage.Lift);
                Assert.Equal(Math.Max(0, 420 - single.PanelHeight), hydra.Lift, 6); // :954 usageLift, :1000-1001.
                Assert.True(hydra.Lift > 0);
                // Below the usage panel in the stack: no lift.
                runtime.PanelStackOrder = new ValueList<FloatingPanelID>([new FloatingPanelID.Hydra(), new FloatingPanelID.Usage()]);
                var flipped = new PanelScene(members, runtime, model, pane, 120, null);
                Assert.Equal(0, flipped.Slots[new FloatingPanelID.Hydra()].Lift);
            });
        });
    }

    // ---- PR-USAGE-074: the reset removes the stored key, animated -----------------------------------------------------

    [Fact]
    [Trait("Req", "PR-USAGE-074")] // App/Views/Chat/ChatView.swift:574-577; AppSettings.swift:692-700
    public void Resetting_the_usage_height_removes_the_key_inside_the_panel_slide_and_survives_a_restart()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            Animation? during = null;
            h.Main.OnMain(() =>
            {
                var runtime = NewRuntime(h, model);
                model.Settings.ShowsUsagePanel = true;
                model.Settings.UsagePanelHeight = 300;
                Assert.Equal(300d, Convert.ToDouble(h.Defaults.Object("usagePanelHeight"), System.Globalization.CultureInfo.InvariantCulture));
                var scene = new PanelScene(new PanelMembers(runtime, model), runtime, model, new Size(1600, 2000), 100, 200);
                var grip = ChatViewReviewFixtures.Invoke<PanelResize>("UsageResizer", model, new PanelResizeState(), PanelDockCorner.BottomLeading, scene, new Size(350, 300), 94d);
                var tracker = ObservationTracking.Track(() => model.Settings.UsagePanelHeight, () => during = Transaction.Current);
                grip.OnReset();
            });
            Assert.Equal<Animation?>(Chrome.PanelSlide, during); // :576 withAnimation(Chrome.panelSlide)
            Assert.Null(model.Settings.UsagePanelHeight);
            Assert.Null(h.Defaults.Object("usagePanelHeight")); // removed, never a sentinel.
            var restarted = h.MakeModel();
            Assert.Null(restarted.Settings.UsagePanelHeight);
        });
    }

    // ---- PR-USAGE-069: the heads provider joins the usage panel only when another provider runs the heads ---------------

    [Theory]
    [InlineData(true, ProviderKind.Claude, ProviderKind.Claude)]
    [InlineData(true, null, null)]
    [InlineData(false, ProviderKind.Claude, null)]
    [Trait("Req", "PR-USAGE-069")] // App/Views/Chat/ChatView.swift:797-806
    public void The_usage_panel_adds_the_pairs_heads_provider_only_when_Hydra_is_on_and_it_differs(bool hydraOn, ProviderKind? worker, ProviderKind? expected)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var runtime = NewRuntime(h, model);
                var thread = runtime.Thread!;
                Assert.Equal(ProviderKind.Codex, thread.Provider);
                var pair = new HydraPair(thread.Provider) { WorkerProvider = worker ?? thread.Provider };
                model.Settings.AddHydraPair(pair);
                model.Settings.HydraEnabled = hydraOn;
                model.UpdateThread(thread.Id, t => t with { HydraEnabled = true, HydraPairID = pair.Id });
                model.Settings.ShowsUsagePanel = true;
                var usage = new PanelMembers(runtime, model).Usage!;
                Assert.Equal(ProviderKind.Codex, usage.Provider);
                Assert.Equal(expected, usage.HeadsProvider);
            });
        });
    }
}
