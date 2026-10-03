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

    // ---- PR-CHAT-173 / UI-CHAT-074 / PR-CHAT-174: the Threads button -------------------------------------------------

    [Theory]
    [InlineData(false, false, false, "Threads")]
    [InlineData(false, true, false, "Show the sidebar")]
    [InlineData(false, false, true, "Threads")] // sidebarOnlyFloats means nothing without sidebarFloats.
    [InlineData(false, true, true, null)] // the list floats as the only sidebar: nothing to open.
    [InlineData(true, false, false, null)] // the sidebar holds the window buttons.
    [InlineData(true, true, true, null)]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1125-1131,1249-1254
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1125-1131,1244-1265
    public void The_Threads_button_exists_only_while_the_sidebar_is_away_and_not_the_only_floating_list(bool holds, bool floats, bool onlyFloats, string? tip)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = floats;
                model.Settings.SidebarOnlyFloats = onlyFloats;
                if (holds) model.Sidebar.NoteLaidOutWidth(400);
                Assert.Equal(holds, model.Sidebar.HoldsTrafficLights);
                var row = new ChatChromeRow(model, NewRuntime(h, model), new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                var button = LogicalQuery.SelfAndDescendants<ThreadsButton>(row).SingleOrDefault();
                Assert.Equal(tip is null, button is null);
                if (button is null) return;
                Assert.Same(LogicalQuery.SelfAndDescendants<StackPanel>(row).First().Children[0], SwiftTransition.Container(button)); // the first slot.
                Assert.Equal(tip, Tip(LogicalQuery.SelfAndDescendants<Button>(button).Single()));
                Assert.Equal(["list.bullet"], Symbols(button));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1253 `isPresented.toggle()`
    [Trait("Req", "PR-CHAT-174")] // App/Views/Chat/ChatView.swift:1255-1259
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1244-1265
    public void Without_floating_the_press_toggles_a_bottom_popover_of_the_sidebar_at_its_live_width_whose_dismiss_closes_it()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var recorder = new RecordingPresenter();
            using var presenter = Presentation.Presenter.Override(recorder);
            Button button = null!;
            var wasVisible = false;
            void Drain() { h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); }
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                var threads = new ThreadsButton(model);
                Host(threads, 400);
                button = LogicalQuery.SelfAndDescendants<Button>(threads).Single();
                wasVisible = model.Sidebar.IsVisible;
                ChatGitFixtures.Click(button);
            });
            Drain();
            h.Main.OnMain(() =>
            {
                var (popup, presented) = Assert.Single(recorder.Calls);
                Assert.True(presented);
                Assert.Equal(wasVisible, model.Sidebar.IsVisible); // the popover, not the sidebar.
                var circle = Assert.IsType<ChromeCircleButton>(popup.PlacementTarget); // hung from the button itself (:1255).
                Assert.Contains(button, LogicalQuery.SelfAndDescendants<Button>(circle));
                Assert.Equal(PlacementMode.Bottom, popup.Placement); // :1255 arrowEdge: .bottom
                var sidebar = Assert.Single(LogicalQuery.SelfAndDescendants<SidebarView>(popup.Child!));
                Assert.Equal(model.Sidebar.Width, sidebar.Width);
                Assert.Equal(560, sidebar.Height);
                Assert.True(Popovers.GetIsCapsuleButtonBorderShape(popup.Child!)); // .presentedChrome()
                ((Action)typeof(SidebarView).GetField("dismiss", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sidebar)!)();
            });
            Drain();
            Assert.False(recorder.Calls[^1].Presented); // dismiss: { isPresented = false }
            h.Main.OnMain(() => ChatGitFixtures.Click(button));
            Drain();
            Assert.True(recorder.Calls[^1].Presented);
            h.Main.OnMain(() => ChatGitFixtures.Click(button)); // the same press closes it (:1253 isPresented.toggle()).
            Drain();
            Assert.False(recorder.Calls[^1].Presented);
            // The content is built from the sidebar's own width each time it is made (:1257 model.sidebar.width).
            h.Main.OnMain(() =>
            {
                typeof(SidebarLayout).GetProperty(nameof(SidebarLayout.Width))!.SetValue(model.Sidebar, 312d);
                Assert.Equal(312, LogicalQuery.SelfAndDescendants<SidebarView>(ThreadsButton.BuildSidebarPopover(model, () => { })).Single().Width);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1129-1131 `.transition(.softAppear)`
    [Trait("Req", "UI-CHAT-074")] // App/Views/Chat/ChatView.swift:1130
    public void The_Threads_button_leaves_and_arrives_through_soft_appear_rather_than_a_cut()
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
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                row = new ChatChromeRow(model, NewRuntime(h, model), new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                Host(row);
                Assert.Single(LogicalQuery.SelfAndDescendants<ThreadsButton>(row));
                model.Sidebar.NoteLaidOutWidth(400); // the window buttons move to the sidebar: the button leaves with them.
            });
            h.Main.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ThreadsButton>(row))); // still on screen mid-fade, not cut.
            for (var i = 0; i < 40; i++) { time.Advance(TimeSpan.FromMilliseconds(16)); frames.Advance(TimeSpan.FromMilliseconds(16)); h.Main.RunUntilIdle(); Dispatcher.UIThread.RunJobs(); }
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
