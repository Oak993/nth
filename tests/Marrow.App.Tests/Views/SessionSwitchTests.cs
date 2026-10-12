using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Sidebar;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views;

// PORT-ONLY (ED-NEW-session-hotkeys; owner 2026-10-11): Ctrl+1..9 (number row and number pad) opens the Nth session the sidebar
// shows, ready to type in; hold Ctrl and the rows show their numbers; and every chat switch, a click as much as the shortcut,
// moves the same quick, premium way: the selection glides, the chat opened comes in over the chat left, which fades out under
// it. Sampled frame by frame on the manual frame clock, reading what is drawn: the real opacity of each part down the tree,
// the rows' places on screen, the control that holds the keyboard.
[Collection("Headless")]
public sealed class SessionSwitchTests(HeadlessSession headless)
{
    private const double Frame = 1000.0 / 60;

    private static readonly (Key Key, PhysicalKey Physical)[] Digits =
    [
        (Key.D1, PhysicalKey.Digit1), (Key.D2, PhysicalKey.Digit2), (Key.D3, PhysicalKey.Digit3),
        (Key.D4, PhysicalKey.Digit4), (Key.D5, PhysicalKey.Digit5), (Key.D6, PhysicalKey.Digit6),
        (Key.D7, PhysicalKey.Digit7), (Key.D8, PhysicalKey.Digit8), (Key.D9, PhysicalKey.Digit9),
    ];

    private sealed class Rig(AppModelHarness h, AppModel model, ThreadWindow window, SidebarView sidebar, SidebarView? floating, DetailView detail, ManualFrameSource frames, Guid[] ids, Guid[] second)
    {
        public AppModelHarness H { get; } = h;
        public AppModel Model { get; } = model;
        public ThreadWindow Window { get; } = window;
        public SidebarView Sidebar { get; } = sidebar;
        public SidebarView? Floating { get; } = floating;
        public DetailView Detail { get; } = detail;
        public ManualFrameSource Frames { get; } = frames;
        public Guid[] Ids { get; } = ids;
        public Guid[] Second { get; } = second;
        public bool ControlDown { get; set; }

        /// <summary>The numbered order the sidebar published (a convenience: the tests that prove the order read the rows).</summary>
        public IReadOnlyList<Guid> Order => Model.SessionNumbers.Order ?? throw new Xunit.Sdk.XunitException("no published order");

        public void Step(double milliseconds = Frame) => Step(TimeSpan.FromMilliseconds(milliseconds));

        public void Step(TimeSpan by)
        {
            // The real dispatcher runs work posted by input (a spawned hold, a posted refresh) before time moves on.
            Dispatcher.UIThread.RunJobs();
            H.Main.Time.Advance(by, () => Dispatcher.UIThread.RunJobs());
            Frames.Advance(by);
        }

        public void Settle(int frames = 40)
        {
            for (var i = 0; i < frames; i++)
            {
                Step();
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
            }
        }

        public void Press(Key key, PhysicalKey physical)
        {
            Window.KeyPress(key, RawInputModifiers.Control, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        public void Release(Key key, PhysicalKey physical) => Window.KeyRelease(key, RawInputModifiers.Control, physical, null);

        public void Key(Key key, PhysicalKey physical)
        {
            Window.KeyPress(key, RawInputModifiers.None, physical, null);
            Window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        public ThreadColumns Columns => Assert.IsType<ThreadColumns>(Detail.Child);

        public Dictionary<Guid, FrameHostingView> ColumnMap
        {
            get
            {
                var coordinator = typeof(ThreadColumns).GetField("coordinator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Columns)!;
                return (Dictionary<Guid, FrameHostingView>)coordinator.GetType().GetField("columns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
            }
        }

        public FrameHostingView Column(Guid id) => ColumnMap[id];

        public ChatView Chat(Guid id) => ChatSwitchMotion.ChatOf(Column(id))!;

        public SidebarThreadRow Row(Guid id) =>
            LogicalQuery.SelfAndDescendants<SidebarThreadRow>(Sidebar).Single(row => ((IGlideRow)row).ThreadID == id);

        /// <summary>The row's fill shape in the sidebar's space.</summary>
        public Rect RowFrame(Guid id)
        {
            var shape = ((IGlideRow)Row(id)).FillShape;
            return new Rect(shape.TranslatePoint(default, Sidebar.Glide)!.Value, shape.Bounds.Size);
        }

        /// <summary>The pointer arrives over the row (its hover look settles in its own 0.1 s, before any click).</summary>
        public void Hover(Guid id)
        {
            Window.MouseMove(Center(id));
            Dispatcher.UIThread.RunJobs();
        }

        private Point Center(Guid id)
        {
            var shape = ((IGlideRow)Row(id)).FillShape;
            return shape.TranslatePoint(new Point(shape.Bounds.Width / 2, shape.Bounds.Height / 2), Window)!.Value;
        }

        public void Click(Guid id)
        {
            var center = Center(id);
            Window.MouseMove(center);
            Window.MouseDown(center, MouseButton.Left);
            Window.MouseUp(center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        public TextBox SearchBox => LogicalQuery.SelfAndDescendants<TextBox>(Sidebar).First();

        public object? Focused => Window.FocusManager?.GetFocusedElement();
    }

    private void With(Action<Rig> check, int threads = 4, bool reduceMotion = false, ThemeVariant? theme = null, bool collapsed = false,
        bool floating = false, int secondProjectThreads = 0, int? zoom = null) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var motion = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        Rig? rig = null;
        var controlDown = false;
        using var keyState = ThreadWindow.ControlKeyState.Override(() => rig?.ControlDown ?? controlDown);
        var model = h.MakeModel();
        Guid[] ids = [];
        Guid[] second = [];
        ThreadWindow window = null!;
        SidebarView sidebar = null!;
        SidebarView? popover = null;
        DetailView detail = null!;
        IDisposable? actor = null;
        try
        {
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = false;
                model.Settings.HydraEnabled = false;
                var project = model.AddProject(new Project("Project", h.NewFolder("project")).Url);
                ids = Enumerable.Range(0, threads).Select(_ => model.NewThread(project)!.Id).ToArray();
                if (secondProjectThreads > 0)
                {
                    var other = model.AddProject(new Project("Other", h.NewFolder("other")).Url);
                    second = Enumerable.Range(0, secondProjectThreads).Select(_ => model.NewThread(other)!.Id).ToArray();
                }
            });
            h.Main.Loop.Run(async () =>
            {
                foreach (var id in ids.Concat(second)) await model.Runtime(id).EnsureLoaded();
            }, AppModelHarness.Bound);
            // The window lives on the Avalonia main executor until it has closed (finally), so its teardown runs on main too.
            actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            window = new ThreadWindow { Width = 900, Height = 600 };
            if (theme is not null) window.RequestedThemeVariant = theme;
            var resize = new WindowLiveResize(window);
            sidebar = new SidebarView();
            detail = new DetailView();
            Control sidebarSlot = collapsed || floating
                ? new Border { Width = 0, ClipToBounds = true, Child = new Border { Width = 260, Child = sidebar } }
                : sidebar;
            sidebarSlot.HorizontalAlignment = HorizontalAlignment.Left;
            var layout = new Grid { ColumnDefinitions = new ColumnDefinitions(collapsed || floating ? "Auto,*" : "260,*") };
            Grid.SetColumn(detail, 1);
            layout.Children.Add(sidebarSlot);
            layout.Children.Add(detail);
            if (floating)
            {
                // The floating sidebar over the detail (the column one collapsed): the numbers follow the one on screen.
                popover = new SidebarView(inPopover: true);
                var panel = new Border { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Child = popover };
                Grid.SetColumnSpan(panel, 2);
                layout.Children.Add(panel);
            }
            var host = new Border
            {
                Background = new SolidColorBrush(theme == ThemeVariant.Light ? Color.FromRgb(233, 233, 229) : Color.FromRgb(46, 47, 44)),
                Child = zoom is { } percent ? new AppZoomModifier(percent).Body(layout) : layout,
            };
            AppEnvironment.SetAppModel(host, model);
            AppEnvironment.SetWindowLiveResize(host, resize);
            window.Content = host;
            window.SelectThreadNumber = model.SelectThreadNumber;
            window.ShowSessionNumbers = shown => model.SessionNumbers.SetShown(shown, window);
            window.Show();
            rig = new Rig(h, model, window, sidebar, popover, detail, frames, ids, second);
            rig.Settle(30);
            check(rig);
        }
        finally
        {
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            actor?.Dispose();
        }
    });

    // ---- what is drawn ---------------------------------------------------------------------------------------------

    /// <summary>The opacity a visual is drawn with: its own times every ancestor's, 0 if any of them is hidden.</summary>
    private static double Drawn(Visual visual)
    {
        var opacity = 1.0;
        for (Visual? node = visual; node is not null; node = Avalonia.VisualTree.VisualExtensions.GetVisualParent(node))
        {
            if (!node.IsVisible) return 0;
            opacity *= node.Opacity;
        }
        return opacity;
    }

    private static Control Part(ChatView chat, string field) =>
        (Control)typeof(ChatView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chat)!;

    private static Control Timeline(ChatView chat) => Part(chat, "timelineHost");

    private static Control Composer(ChatView chat) => Part(chat, "composerHost");

    private static Control Header(ChatView chat) => Part(chat, "chrome");

    private static Control[] Veils(ChatView chat) => LogicalQuery.SelfAndDescendants<PaneTopVeil>(chat).Cast<Control>().ToArray();

    private static Control[] Sheet(ChatView chat) => ((Panel)chat.Child!).Children.OfType<Avalonia.Controls.Shapes.Shape>().Cast<Control>().ToArray();

    private static ComposerTextColumn Draft(ChatView chat) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(Composer(chat)).OfType<ComposerTextColumn>().Single();

    /// <summary>Every chat column the pane draws: a column dropped (a chat deleted) stays in the pane while it fades out.</summary>
    private static FrameHostingView[] PaneColumns(Rig rig) =>
        ((Panel)rig.ColumnMap.Values.First(column => column.Parent is Panel).Parent!).Children.OfType<FrameHostingView>().ToArray();

    private static ChatView[] VisibleChats(Rig rig) =>
        PaneColumns(rig).Where(column => column.IsVisible).Select(column => ChatSwitchMotion.ChatOf(column)!).ToArray();

    /// <summary>
    /// One frame of a switch, as drawn: the glass (header capsules, composer) of exactly one column, that column's top veil
    /// alone, the sheet's wash of exactly one column, and the column opened over the one left.
    /// </summary>
    private static void AssertOneOfEachDrawn(Rig rig, Guid opened, string frame)
    {
        var chats = VisibleChats(rig);
        var shown = rig.Chat(opened);
        Assert.Equal(1, Drawn(Header(shown)), 3);
        Assert.Equal(1, Drawn(Composer(shown)), 3);
        foreach (var chat in chats.Where(chat => chat != shown))
        {
            Assert.True(Drawn(Header(chat)) == 0, $"{frame}: a second header's glass is drawn ({Drawn(Header(chat))})");
            Assert.True(Drawn(Composer(chat)) == 0, $"{frame}: a second composer is drawn ({Drawn(Composer(chat))})");
            Assert.True(rig.Column(opened).ZIndex > ChatSwitchColumn(rig, chat).ZIndex, $"{frame}: the chat opened is not over the one left");
        }
        Assert.Equal(1, chats.Count(chat => Veils(chat).Any(veil => Drawn(veil) > 0)));
        Assert.True(Veils(shown).All(veil => Drawn(veil) == 1), $"{frame}: the opened chat's veil is not drawn");
        Assert.Equal(1, chats.Count(chat => Sheet(chat).Any(shape => Drawn(shape) > 0)));
        Assert.True(chats.Single(chat => Sheet(chat).Any(shape => Drawn(shape) > 0)) is var drawsSheet && Sheet(drawsSheet).All(shape => Drawn(shape) == 1));
    }

    private static FrameHostingView ChatSwitchColumn(Rig rig, ChatView chat) => PaneColumns(rig).Single(column => ChatSwitchMotion.ChatOf(column) == chat);

    private static double RiseOf(ChatView chat) => Timeline(chat).RenderTransform is TranslateTransform translate ? translate.Y : 0;

    /// <summary>
    /// The switch from <paramref name="from"/> to <paramref name="to"/>, drawn frame by frame from the call that made it: the
    /// transcripts cross (the opened one readable from the first frame, settled in 140 ms, rising 4 points; the left one gone in
    /// 90 ms, then hidden), and every frame draws one glass, one veil, one wash.
    /// </summary>
    private static Guid AssertCrosses(Rig rig, Guid from, Func<Guid> switchNow, bool titlesDiffer = false)
    {
        var leaving = rig.Column(from);
        var left = rig.Chat(from);
        var to = switchNow();
        Assert.Equal(to, rig.Model.SelectedThreadID);
        Assert.True(leaving.IsLeaving); // gated at once: no chord or focus reaches the chat left
        Assert.False(leaving.IsEffectivelyEnabled);
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout(); // the switch's one layout: the chat shown is measured before its first animated frame
        var opened = rig.Chat(to);
        AssertOneOfEachDrawn(rig, to, "on the switch");
        var inOpacity = new List<double>();
        var outOpacity = new List<double>();
        var rises = new List<double>();
        var wordsLeft = new List<double>();
        var wordsCome = new List<double>();
        Assert.Equal(titlesDiffer, opened.TitleCross is not null);
        for (var frame = 0; frame < 14; frame++)
        {
            rig.Step();
            Assert.True(LaidOut(rig.Column(to)), $"frame {frame}: the chat opened asked for layout ({Unlaid(rig.Column(to))})");
            Assert.True(!leaving.IsVisible || LaidOut(leaving), $"frame {frame}: the chat left asked for layout ({Unlaid(leaving)})");
            AssertOneOfEachDrawn(rig, to, $"frame {frame}");
            inOpacity.Add(Drawn(Timeline(opened)));
            rises.Add(RiseOf(opened));
            outOpacity.Add(Drawn(Timeline(left)));
            if (opened.TitleCross is { } cross)
            {
                // The header's words cross in step with the transcript, in the one header drawn.
                wordsLeft.Add(Drawn(cross.Leaving));
                wordsCome.Add(Drawn(cross.Arriving));
                Assert.Equal(inOpacity[^1], wordsCome[^1], 3);
                Assert.Equal(1 - inOpacity[^1], wordsLeft[^1], 3);
            }
        }
        Assert.Null(opened.TitleCross); // over: this chat's words alone
        if (titlesDiffer)
        {
            Assert.True(wordsLeft.Count >= 6, $"the words crossed for {wordsLeft.Count} frames");
            Assert.True(wordsLeft.Zip(wordsLeft.Skip(1)).All(pair => pair.Second <= pair.First), string.Join(", ", wordsLeft));
        }
        Assert.True(inOpacity[0] > 0.25 && inOpacity[0] < 1, $"first frame {inOpacity[0]}");
        Assert.True(inOpacity.Zip(inOpacity.Skip(1)).All(pair => pair.Second >= pair.First), string.Join(", ", inOpacity));
        Assert.True(inOpacity.Zip(inOpacity.Skip(1)).All(pair => pair.Second - pair.First <= 3 * Frame / 140 + 0.01), string.Join(", ", inOpacity));
        Assert.Equal(1, inOpacity[^1]);
        Assert.True(rises.Zip(rises.Skip(1)).All(pair => pair.Second <= pair.First), string.Join(", ", rises));
        Assert.True(rises[0] > 0 && rises[0] <= ChatSwitchMotion.Rise && rises[^1] == 0, string.Join(", ", rises));
        Assert.True(outOpacity[0] > 0 && outOpacity[0] < 1, $"first frame out {outOpacity[0]}");
        Assert.True(outOpacity.Zip(outOpacity.Skip(1)).All(pair => pair.Second <= pair.First), string.Join(", ", outOpacity));
        Assert.Equal(0, outOpacity[6]); // 90 ms is under six frames
        Assert.False(leaving.IsVisible);
        Assert.False(leaving.IsLeaving);
        // At rest: one column, every part drawn whole, the hidden one back at rest for a later show.
        Assert.Equal([opened], VisibleChats(rig));
        Assert.All(new[] { Timeline(opened), Header(opened), Composer(opened), Draft(opened) }.Concat(Veils(opened)).Concat(Sheet(opened)),
            part => Assert.Equal(1, Drawn(part)));
        Assert.All(new[] { Timeline(left), Header(left), Composer(left), Draft(left) }.Concat(Veils(left)).Concat(Sheet(left)),
            part => Assert.Equal(1, part.Opacity));
        Assert.Equal(0, RiseOf(opened));
        return to;
    }

    // ---- Ctrl+1..9 -----------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void The_number_row_and_the_number_pad_open_the_first_session_ready_to_type_with_the_caret_at_the_end() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        Assert.Equal(rig.Ids.Length, order.Length);
        var first = order[0];
        // The first session has a draft, its caret left mid-word, and the keyboard is elsewhere (a third session, clicked).
        rig.Model.SelectedThreadID = first;
        rig.Settle();
        var box = rig.Chat(first).ComposerBox!;
        box.Text = "draft to finish";
        box.CaretIndex = 3;
        rig.Click(order[2]);
        rig.Settle();
        Assert.Equal(order[2], rig.Model.SelectedThreadID);
        Assert.False(box.IsFocused);

        rig.Press(Key.D1, PhysicalKey.Digit1);
        Assert.Equal(first, rig.Model.SelectedThreadID);
        rig.Settle(2);
        Assert.True(box.IsFocused); // typed keys land in it from the first frames
        Assert.Equal(box.Text!.Length, box.CaretIndex);
        Assert.Equal(box.Text.Length, box.SelectionStart);
        Assert.Equal(box.Text.Length, box.SelectionEnd);
        rig.Release(Key.D1, PhysicalKey.Digit1);

        rig.Click(order[1]);
        rig.Settle();
        rig.Press(Key.NumPad1, PhysicalKey.NumPad1);
        Assert.Equal(first, rig.Model.SelectedThreadID);
        rig.Settle(2);
        Assert.True(box.IsFocused);
        Assert.Equal(box.Text.Length, box.CaretIndex);
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void A_number_past_the_last_row_does_nothing() => With(rig =>
    {
        var selected = rig.Model.SelectedThreadID;
        rig.Press(Key.D9, PhysicalKey.Digit9);
        rig.Press(Key.NumPad5, PhysicalKey.NumPad5);
        rig.Settle(3);
        Assert.Equal(selected, rig.Model.SelectedThreadID);
    }, threads: 4);

    // ---- the numbers follow the rows as drawn ----------------------------------------------------------------------

    /// <summary>The session rows a sidebar draws, top to bottom by their place on screen (threads and helper rows).</summary>
    private static List<(double Y, Guid Id, SessionKeycap Keycap)> RowsOnScreen(SidebarView sidebar)
    {
        var rows = new List<(double Y, Guid Id, SessionKeycap Keycap)>();
        foreach (var control in LogicalQuery.SelfAndDescendants<Control>(sidebar))
        {
            var keycap = control switch { SidebarThreadRow row => row.Keycap, SidebarHelperRow helper => helper.Keycap, _ => null };
            if (keycap is null || !control.IsEffectivelyVisible || control.Bounds.Height < 1) continue;
            if (control.TranslatePoint(default, sidebar) is not { } at) continue;
            rows.Add((at.Y, ((IGlideRow)control).ThreadID, keycap));
        }
        rows.Sort((a, b) => a.Y.CompareTo(b.Y));
        return rows;
    }

    /// <summary>
    /// The numbers are the rows' places: the Nth row from the top (as drawn) carries badge N, the ninth is the last numbered,
    /// and Ctrl+N opens exactly that row's session.
    /// </summary>
    private static void AssertNumbersFollowTheRows(Rig rig, SidebarView sidebar, int expectedRows)
    {
        var rows = RowsOnScreen(sidebar);
        Assert.Equal(expectedRows, rows.Count);
        Assert.True(rows.Zip(rows.Skip(1)).All(pair => pair.Second.Y > pair.First.Y + 1), "two rows share a place");
        for (var index = 0; index < rows.Count; index++)
            Assert.Equal(index < SessionNumbers.Count ? index + 1 : null, rows[index].Keycap.Number);
        for (var number = 1; number <= Math.Min(rows.Count, SessionNumbers.Count); number++)
        {
            var (key, physical) = Digits[number - 1];
            rig.Press(key, physical);
            rig.Release(key, physical);
            Assert.Equal(rows[number - 1].Id, rig.Model.SelectedThreadID);
            rig.Settle(12);
        }
    }

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void In_the_project_layout_the_numbers_follow_the_rows_drawn_pinned_first_and_helpers_under_their_thread() => With(rig =>
    {
        // Pinned and helper rows move rows about: the numbers must follow them as drawn, not the model's own list.
        rig.Model.UpdateThread(rig.Ids[3], thread => thread with { IsPinned = true });
        rig.Model.UpdateThread(rig.Ids[4], thread => thread with { ParentThreadID = rig.Ids[0] });
        rig.Model.UpdateThread(rig.Ids[0], thread => thread with { ExpandsHelpers = true }); // drawn as rows, not folded into a stub
        rig.Model.SelectedThreadID = rig.Ids[1];
        rig.Settle();
        var rows = RowsOnScreen(rig.Sidebar);
        Assert.Equal(rig.Ids[3], rows[0].Id); // pinned first, as drawn
        Assert.Contains(LogicalQuery.SelfAndDescendants<SidebarHelperRow>(rig.Sidebar), helper => ((IGlideRow)helper).ThreadID == rig.Ids[4]);
        var parentAt = rows.FindIndex(row => row.Id == rig.Ids[0]);
        Assert.Equal(rig.Ids[4], rows[parentAt + 1].Id); // the helper row right under its thread
        AssertNumbersFollowTheRows(rig, rig.Sidebar, 6);
    }, threads: 6);

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void Past_nine_rows_the_tenth_has_no_number_and_a_collapsed_group_is_left_out() => With(rig =>
    {
        AssertNumbersFollowTheRows(rig, rig.Sidebar, 12);
        // The first project folded: its rows leave the list, and the numbers close up over the rows still drawn.
        var project = rig.Model.Thread(rig.Ids[0])!.ProjectID;
        rig.Model.UpdateProject(project, value => value with { IsExpanded = false });
        rig.Settle();
        var drawn = RowsOnScreen(rig.Sidebar).Count;
        Assert.True(drawn < 12, $"{drawn} rows still drawn");
        Assert.All(rig.Second, id => Assert.Contains(RowsOnScreen(rig.Sidebar), row => row.Id == id));
        AssertNumbersFollowTheRows(rig, rig.Sidebar, drawn);
    }, threads: 7, secondProjectThreads: 5);

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void In_the_activity_layout_the_numbers_follow_its_rows() => With(rig =>
    {
        rig.Model.UpdateThread(rig.Ids[2], thread => thread with { IsPinned = true });
        rig.Model.Settings.SidebarActivityView = true;
        rig.Settle();
        AssertNumbersFollowTheRows(rig, rig.Sidebar, 5);
    }, threads: 5);

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void While_searching_the_numbers_follow_the_results_drawn() => With(rig =>
    {
        string[] titles = ["Alpha one", "Beta two", "Alpha three", "Gamma four", "Alpha five"];
        for (var index = 0; index < titles.Length; index++)
            rig.Model.UpdateThread(rig.Ids[index], thread => thread with { Title = titles[index], HasCustomTitle = true });
        // A helper of an Alpha thread: the search list draws threads only, so only drawn rows are numbered.
        rig.Model.UpdateThread(rig.Ids[1], thread => thread with { ParentThreadID = rig.Ids[0] });
        rig.Settle();
        rig.SearchBox.Text = "Alpha";
        rig.Settle();
        var rows = RowsOnScreen(rig.Sidebar);
        Assert.All(rows, row => Assert.StartsWith("Alpha", rig.Model.Thread(row.Id)!.Title, StringComparison.Ordinal));
        AssertNumbersFollowTheRows(rig, rig.Sidebar, 3);
    }, threads: 5);

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void With_the_sidebar_collapsed_the_shortcut_still_opens_the_session_it_would_number() => With(rig =>
    {
        // Nothing of the column sidebar is on screen; its rows, laid out at their width, still say what it would number.
        var rows = RowsOnScreen(rig.Sidebar);
        Assert.Equal(rig.Ids.Length, rows.Count);
        rig.Press(Key.D2, PhysicalKey.Digit2);
        Assert.Equal(rows[1].Id, rig.Model.SelectedThreadID);
    }, collapsed: true);

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void With_the_floating_sidebar_on_screen_the_numbers_follow_its_rows() => With(rig =>
    {
        rig.Model.UpdateThread(rig.Ids[1], thread => thread with { IsPinned = true });
        rig.Settle();
        AssertNumbersFollowTheRows(rig, rig.Floating!, 4);
    }, floating: true);

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void The_order_is_the_floating_sidebars_while_on_screen_else_the_columns_on_screen_or_not()
    {
        var numbers = new SessionNumbers();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var floatingOnScreen = false;
        var columnOnScreen = false;
        Assert.Null(numbers.Order);
        numbers.Publish("column", inPopover: false, () => columnOnScreen, [a, b, a]);
        Assert.Equal([a, b], numbers.Order); // collapsed: still the column's (one row once)
        numbers.Publish("floating", inPopover: true, () => floatingOnScreen, [c, a]);
        Assert.Equal([a, b], numbers.Order); // a floating sidebar off screen does not count
        floatingOnScreen = true;
        Assert.Equal([c, a], numbers.Order);
        Assert.Equal(1, numbers.NumberOf(c));
        Assert.Null(numbers.NumberOf(b));
        columnOnScreen = true;
        Assert.Equal([c, a], numbers.Order); // the floating one on screen wins over the column
        numbers.Withdraw("floating");
        Assert.Equal([a, b], numbers.Order);
        numbers.Withdraw("column");
        Assert.Null(numbers.Order);
    }

    // ---- the switch: what is drawn ----------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void A_click_glides_the_selection_and_crosses_the_chats_with_one_glass_one_veil_and_one_wash_drawn_every_frame() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.UpdateThread(order[0], thread => thread with { Title = "Ship the login fix", HasCustomTitle = true });
        rig.Model.UpdateThread(order[3], thread => thread with { Title = "Draft the release notes", HasCustomTitle = true });
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        var from = rig.RowFrame(order[0]);
        var to = rig.RowFrame(order[3]);
        // The pointer reaches the row first, as a hand does; the hover's own look (source behaviour) settles before the press.
        rig.Hover(order[3]);
        rig.Settle();
        AssertCrosses(rig, order[0], () =>
        {
            rig.Click(order[3]);
            Assert.True(rig.Sidebar.Glide.IsGliding);
            return order[3];
        }, titlesDiffer: true);
        // The title's words crossed (the opened header started on the words left, then its own words came in).
        Assert.Equal("Draft the release notes", ((ChatChromeRow)Header(rig.Chat(order[3]))).TitleWords);
        Assert.True(rig.Chat(order[3]).ComposerBox!.IsFocused);

        // And back to a column kept from before (no build): the same move, the same one-of-each.
        rig.Hover(order[0]);
        rig.Settle();
        AssertCrosses(rig, order[3], () =>
        {
            rig.Click(order[0]);
            return order[0];
        }, titlesDiffer: true);
        Assert.True(rig.Chat(order[0]).ComposerBox!.IsFocused);
        Assert.True(from.Y < to.Y);
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void The_selection_glides_down_without_overshoot_and_lands_on_the_row() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        var from = rig.RowFrame(order[0]);
        var to = rig.RowFrame(order[3]);
        rig.Hover(order[3]);
        rig.Settle();
        rig.Click(order[3]);
        var glideY = new List<double>();
        for (var frame = 0; frame < 14; frame++)
        {
            rig.Step();
            Assert.True(LaidOut(rig.Sidebar), $"frame {frame}: the sidebar asked for layout ({Unlaid(rig.Sidebar)})");
            if (rig.Sidebar.Glide.IsGliding) glideY.Add(rig.Sidebar.Glide.PillFrame.Y);
        }
        Assert.NotEmpty(glideY);
        Assert.True(glideY.Zip(glideY.Skip(1)).All(pair => pair.Second >= pair.First - 0.01), string.Join(", ", glideY));
        Assert.True(glideY.All(y => y >= from.Y - 0.5 && y <= to.Y + 0.5));
        Assert.False(rig.Sidebar.Glide.IsGliding);
        Assert.True(glideY.Count <= 12, $"the glide took {glideY.Count} frames"); // about 160 ms
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void A_chat_never_shown_before_is_built_and_crosses_in_the_same_way_ready_to_type() => With(rig =>
    {
        var before = rig.Model.SelectedThreadID!.Value;
        var project = rig.Model.Thread(before)!.ProjectID;
        // A new chat: no column of it exists, so the switch builds one (Make) and moves it in that same frame.
        var made = AssertCrosses(rig, before, () =>
        {
            var built = rig.ColumnMap.Keys.ToHashSet();
            var id = rig.Model.NewThread(rig.Model.Project(project)!)!.Id;
            Dispatcher.UIThread.RunJobs(); // the detail follows the model's selection on its next turn, as for any selection
            Assert.DoesNotContain(id, built);
            Assert.True(rig.ColumnMap.ContainsKey(id)); // built by the switch itself
            return id;
        });
        rig.Settle(3);
        Assert.True(rig.Chat(made).ComposerBox!.IsFocused);
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void The_draft_crosses_only_when_the_two_drafts_differ() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        // Both empty: the composer reads the same, so it is drawn whole from the first frame (nothing blinks).
        rig.Click(order[1]);
        rig.Step();
        Assert.Equal(1, Drawn(Draft(rig.Chat(order[1]))));
        rig.Settle();
        // The chat left holds a draft; the one opened is empty: its draft (the placeholder) comes in with the transcript.
        rig.Chat(order[1]).ComposerBox!.Text = "half a thought";
        rig.Settle(2);
        rig.Click(order[2]);
        rig.Step();
        var first = Drawn(Draft(rig.Chat(order[2])));
        Assert.True(first > 0.25 && first < 1, $"draft first frame {first}");
        Assert.Equal(Drawn(Timeline(rig.Chat(order[2]))), first, 3);
        Assert.Equal(0, Drawn(Composer(rig.Chat(order[1])))); // the draft left goes with its composer's glass, at once
        rig.Settle();
        Assert.Equal(1, Drawn(Draft(rig.Chat(order[2]))));
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void The_shortcut_moves_exactly_like_the_click_and_a_quick_second_press_turns_it_without_a_jump() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        AssertCrosses(rig, order[0], () =>
        {
            rig.Press(Key.D2, PhysicalKey.Digit2);
            Assert.True(rig.Sidebar.Glide.IsGliding);
            return order[1];
        });
        rig.Release(Key.D2, PhysicalKey.Digit2);
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();

        rig.Press(Key.D2, PhysicalKey.Digit2);
        rig.Step();
        rig.Step();
        var second = rig.Column(order[1]);
        var midOpacity = Drawn(Timeline(rig.Chat(order[1])));
        var midY = rig.Sidebar.Glide.PillFrame.Y;
        Assert.InRange(midOpacity, 0.05, 0.99);
        rig.Release(Key.D2, PhysicalKey.Digit2);
        rig.Press(Key.D4, PhysicalKey.Digit4);
        Assert.Equal(order[3], rig.Model.SelectedThreadID);
        // The glide goes on from where it is (no snap back to a row), and the half-come chat leaves from its own opacity.
        Assert.Equal(midY, rig.Sidebar.Glide.PillFrame.Y, 3);
        Assert.Equal(midOpacity, Drawn(Timeline(rig.Chat(order[1]))), 3);
        AssertOneOfEachDrawn(rig, order[3], "on the turn");
        var y = new List<double> { midY };
        var leaving = new List<double> { midOpacity };
        for (var frame = 0; frame < 20; frame++)
        {
            rig.Step();
            AssertOneOfEachDrawn(rig, order[3], $"frame {frame}");
            if (rig.Sidebar.Glide.IsGliding) y.Add(rig.Sidebar.Glide.PillFrame.Y);
            if (second.IsVisible) leaving.Add(Drawn(Timeline(rig.Chat(order[1]))));
        }
        var target = rig.RowFrame(order[3]).Y;
        Assert.True(y.Zip(y.Skip(1)).All(pair => Math.Abs(pair.Second - pair.First) < Math.Abs(target - midY) * 0.6 + 0.01), string.Join(", ", y));
        Assert.True(y.All(value => value <= target + 0.5), string.Join(", ", y)); // no overshoot past the row
        Assert.True(leaving.Zip(leaving.Skip(1)).All(pair => pair.Second <= pair.First), string.Join(", ", leaving));
        Assert.False(second.IsVisible);
        Assert.False(rig.Sidebar.Glide.IsGliding);
        Assert.Equal(1, Drawn(Timeline(rig.Chat(order[3]))));
    });

    public static TheoryData<string> OtherWaysIn => new() { "selection", "new chat", "delete", "search Return" };

    [Theory]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    [MemberData(nameof(OtherWaysIn))]
    public void Every_other_way_into_a_chat_moves_the_same_way_and_leaves_it_ready_to_type(string way) => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        var project = rig.Model.Thread(order[0])!.ProjectID;
        var leaving = rig.Column(order[0]);
        switch (way)
        {
            case "selection": rig.Model.SelectedThreadID = order[2]; break; // the command palette, a notification, a Hydra head
            case "new chat": rig.Model.NewThread(rig.Model.Project(project)!); break;
            case "delete": rig.Model.Delete(order[0]); break; // the neighbour is opened
            case "search Return":
                rig.Model.UpdateThread(order[2], thread => thread with { Title = "Zebra crossing", HasCustomTitle = true });
                rig.Settle(2);
                rig.SearchBox.Focus();
                rig.SearchBox.Text = "Zebra";
                rig.Settle(2);
                rig.Key(Key.Enter, PhysicalKey.Enter);
                break;
        }
        Dispatcher.UIThread.RunJobs();
        var opened = rig.Model.SelectedThreadID!.Value;
        Assert.NotEqual(order[0], opened);
        Assert.True(leaving.IsLeaving, $"{way}: the chat left is not fading out");
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
        rig.Step();
        var first = Drawn(Timeline(rig.Chat(opened)));
        Assert.True(first > 0.25 && first < 1, $"{way}: first frame {first}");
        AssertOneOfEachDrawn(rig, opened, way);
        rig.Settle();
        Assert.Equal(1, Drawn(Timeline(rig.Chat(opened))));
        Assert.True(rig.Chat(opened).ComposerBox!.IsFocused, $"{way}: the opened chat is not ready to type ({rig.Focused})");
    }, threads: 4);

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void A_chat_picked_over_an_empty_detail_comes_in_the_same_way() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.SelectedThreadID = null; // the no-thread view
        rig.Settle();
        Assert.IsNotType<ThreadColumns>(rig.Detail.Child);
        rig.Click(order[1]);
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout();
        rig.Step();
        var first = Drawn(Timeline(rig.Chat(order[1])));
        Assert.True(first > 0.25 && first < 1, $"first frame {first}");
        Assert.True(Sheet(rig.Chat(order[1])).All(shape => Drawn(shape) == 1)); // over the sheet, which never blinks
        rig.Settle();
        Assert.Equal(1, Drawn(Timeline(rig.Chat(order[1]))));
        Assert.True(rig.Chat(order[1]).ComposerBox!.IsFocused);
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void With_Reduce_Motion_the_switch_is_at_once() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        var leaving = rig.Column(order[0]);
        rig.Click(order[2]);
        Assert.False(rig.Sidebar.Glide.IsGliding);
        Assert.False(leaving.IsVisible);
        var opened = rig.Chat(order[2]);
        Assert.All(new[] { Timeline(opened), Header(opened), Composer(opened), Draft(opened) }.Concat(Veils(opened)).Concat(Sheet(opened)),
            part => Assert.Equal(1, Drawn(part)));
        Assert.Equal(0, RiseOf(opened));
        Assert.True(opened.ComposerBox!.IsFocused);
    }, reduceMotion: true);

    // ---- the chat left: gated, its look kept ---------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void A_column_leaving_is_gated_at_once_but_its_controls_keep_the_look_they_drew() => With(rig =>
    {
        var enabled = new Button { Content = "Allow" };
        var disabled = new Button { Content = "Deny", IsEnabled = false };
        SwiftGlass.StyleAccentGlassButton(enabled);
        SwiftGlass.StyleAccentGlassButton(disabled);
        var host = new FrameHostingView(new StackPanel { Children = { enabled, disabled } });
        var pane = (Panel)rig.Column(rig.Model.SelectedThreadID!.Value).Parent!;
        pane.Children.Add(host);
        rig.Settle(4);
        static Control Surface(Button button) => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(button).OfType<Border>().First(border => SwiftGlass.GetEffect(border) is not null);
        Assert.Equal(1, Surface(enabled).Opacity);
        Assert.Equal(0.4, Surface(disabled).Opacity, 3);

        host.IsLeaving = true;
        Assert.False(enabled.IsEffectivelyEnabled); // no hot key, default button or click reaches it
        for (var frame = 0; frame < 12; frame++)
        {
            rig.Step();
            Assert.Equal(1, Surface(enabled).Opacity); // it does not dim mid-fade
            Assert.Equal(0.4, Surface(disabled).Opacity, 3); // and one disabled of its own stays so
        }
        host.IsVisible = false;
        host.IsLeaving = false; // hidden: the real look again, out of sight
        host.IsVisible = true;
        Assert.True(enabled.IsEffectivelyEnabled);
        rig.Settle(12);
        Assert.Equal(1, Surface(enabled).Opacity);
        pane.Children.Remove(host);
    });

    // ---- ready to type ---------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void Picking_a_search_result_by_click_Return_or_arrows_leaves_the_chat_ready_to_type() => With(rig =>
    {
        string[] titles = ["Alpha one", "Alpha two", "Alpha three", "Beta"];
        for (var index = 0; index < titles.Length; index++)
            rig.Model.UpdateThread(rig.Ids[index], thread => thread with { Title = titles[index], HasCustomTitle = true });
        rig.Model.SelectedThreadID = rig.Ids[3];
        rig.Settle();
        rig.SearchBox.Focus();
        rig.SearchBox.Text = "Alpha";
        rig.Settle();
        var results = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        Assert.Equal(3, results.Length);

        // An arrow from the field opens the next result, and that chat's composer takes the keyboard (the source's own
        // ComposerView.swift:201-203: a column shown takes it).
        rig.Key(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(results[0], rig.Model.SelectedThreadID);
        rig.Settle(12);
        Assert.True(rig.Chat(results[0]).ComposerBox!.IsFocused, $"focused: {rig.Focused}");

        // A click on a result picks it: its composer takes the keyboard.
        rig.Click(results[2]);
        rig.Settle(12);
        Assert.Equal(results[2], rig.Model.SelectedThreadID);
        Assert.True(rig.Chat(results[2]).ComposerBox!.IsFocused, $"focused: {rig.Focused}");

        // Return picks the first result from the field: the same.
        rig.SearchBox.Focus();
        rig.Key(Key.Enter, PhysicalKey.Enter);
        rig.Settle(12);
        Assert.Equal(results[0], rig.Model.SelectedThreadID);
        Assert.True(rig.Chat(results[0]).ComposerBox!.IsFocused, $"focused: {rig.Focused}");
    }, threads: 4);

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void When_the_composer_cannot_take_the_keyboard_the_chat_shown_holds_it_never_the_chat_left() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.SelectedThreadID = order[2];
        rig.Settle();
        rig.Click(order[1]);
        rig.Settle();
        Assert.True(rig.Chat(order[1]).ComposerBox!.IsFocused); // the keyboard is in the chat about to be left
        var target = rig.Chat(order[2]).ComposerBox ?? throw new Xunit.Sdk.XunitException("no composer");
        target.Focusable = false; // its box refuses the keyboard (not ready)
        rig.Press(Key.D3, PhysicalKey.Digit3);
        rig.Release(Key.D3, PhysicalKey.Digit3);
        Assert.Equal(order[2], rig.Model.SelectedThreadID);
        rig.Settle(12);
        // Tried again after layout, then the column shown takes the keyboard: it never stays with the chat left.
        Assert.Same(rig.Column(order[2]), rig.Focused);
        Assert.False(rig.Column(order[1]).IsVisible);
        target.Focusable = true;
    });

    // ---- hold Ctrl: the numbers ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void Holding_Control_fades_the_numbers_in_together_and_letting_go_starts_the_fade_out_at_once() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        var keycaps = order.Select(id => rig.Row(id).Keycap).ToArray();
        Assert.Equal(Enumerable.Range(1, order.Length).Select(n => (long?)n), keycaps.Select(k => k.Number));
        var titleBefore = order.Select(id => TitleFrame(rig, id)).ToArray();
        Assert.All(keycaps, keycap => Assert.Equal(0, keycap.Opacity));
        Assert.Equal("Ctrl+1", Avalonia.Automation.AutomationProperties.GetAcceleratorKey(Row1Button(rig, order[0])));

        rig.ControlDown = true;
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        for (var frame = 0; frame < 29; frame++) rig.Step();
        Assert.All(keycaps, keycap => Assert.Equal(0, keycap.Opacity)); // nothing before 500 ms
        rig.Step(TimeSpan.FromMilliseconds(500) - TimeSpan.FromMilliseconds(Frame) * 29); // exactly 500 ms in ticks
        Assert.True(rig.Window.AreSessionNumbersShown);
        var fadeIn = new List<double>();
        for (var frame = 0; frame < 20; frame++)
        {
            rig.Step();
            Assert.Single(keycaps.Select(k => k.Opacity).Distinct()); // all of them as one
            Assert.True(LaidOut(rig.Sidebar));
            fadeIn.Add(keycaps[0].Opacity);
        }
        AssertSprings(fadeIn, 1, firstStepAtLeast: 0.1, frames: 15);
        // The badges never move the row's text.
        Assert.Equal(titleBefore, order.Select(id => TitleFrame(rig, id)).ToArray());
        // The mark under a badge (the row's leading icon) is hidden under it, through a mask: its own opacity is the row's.
        var mark = LogicalQuery.SelfAndDescendants<ThreadRowFace>(rig.Row(order[0])).Single().LeadingMark!;
        Assert.Equal(0, SessionKeycap.CoverOf(mark));
        Assert.Equal(1, mark.Opacity);

        rig.ControlDown = false;
        rig.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        var fadeOut = new List<double> { 1 };
        for (var frame = 0; frame < 16; frame++)
        {
            rig.Step();
            fadeOut.Add(keycaps[0].Opacity);
        }
        Assert.True(fadeOut[1] < 0.85, $"the first frame after the key-up moved only {1 - fadeOut[1]:0.000}"); // under way at once
        AssertSprings(fadeOut, 0, firstStepAtLeast: 0.15, frames: 12);
        Assert.All(keycaps, keycap => Assert.Equal(0, keycap.Opacity));
        Assert.Equal(1, SessionKeycap.CoverOf(mark));
        Assert.Null(mark.OpacityMask);
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void Letting_go_mid_fade_bends_the_numbers_round_from_where_they_are_with_their_speed() => With(rig =>
    {
        var keycap = rig.Row(rig.Order[0]).Keycap;
        rig.ControlDown = true;
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.Step(500);
        rig.Step();
        rig.Step();
        var reached = keycap.Opacity;
        Assert.InRange(reached, 0.05, 0.95);
        rig.ControlDown = false;
        rig.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        Assert.Equal(reached, keycap.Opacity); // no jump on the key-up itself
        var values = new List<double> { reached };
        for (var frame = 0; frame < 16; frame++)
        {
            rig.Step();
            values.Add(keycap.Opacity);
        }
        // It carries on a little with the speed it had, turns, and goes out: never a dead stop, never a jump.
        var steps = values.Zip(values.Skip(1)).Select(pair => pair.Second - pair.First).ToList();
        var peak = values.IndexOf(values.Max());
        Assert.True(values.Max() <= reached + 0.15, string.Join(", ", values));
        Assert.True(values.Skip(peak).Zip(values.Skip(peak + 1)).All(pair => pair.Second <= pair.First), string.Join(", ", values));
        Assert.True(steps.All(step => Math.Abs(step) <= 0.3), string.Join(", ", values));
        Assert.Equal(0, values[^1]);
        // Pressed again mid-fade-out, they come back from where they are.
        rig.ControlDown = true;
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.Step(500);
        for (var frame = 0; frame < 20; frame++) rig.Step();
        Assert.Equal(1, keycap.Opacity);
        rig.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        rig.Step();
        var partial = keycap.Opacity;
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.Step(500); // the hold comes back after its delay; until then the fade-out goes on, smoothly
        Assert.True(keycap.Opacity <= partial);
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void A_row_changing_its_mark_under_a_badge_keeps_that_change_when_the_badge_goes() => With(rig =>
    {
        var row = rig.Row(rig.Order[0]);
        var mark = LogicalQuery.SelfAndDescendants<ThreadRowFace>(row).Single().LeadingMark!;
        rig.ControlDown = true;
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.Step(500);
        rig.Settle(20);
        Assert.Equal(0, SessionKeycap.CoverOf(mark));
        mark.Opacity = 0.5; // the row fades its mark while the badge is up (a hover, an activity change)
        rig.Settle(2);
        Assert.Equal(0.5, mark.Opacity); // never overwritten
        Assert.Equal(0, SessionKeycap.CoverOf(mark)); // still hidden under the badge
        rig.ControlDown = false;
        rig.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        rig.Settle(20);
        Assert.Equal(0.5, mark.Opacity); // the row's value, not one remembered from before
        Assert.Equal(1, SessionKeycap.CoverOf(mark));
        mark.Opacity = 1;
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void With_Reduce_Motion_the_numbers_show_and_hide_at_once() => With(rig =>
    {
        var keycap = rig.Row(rig.Order[0]).Keycap;
        rig.ControlDown = true;
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.Step(500);
        Assert.Equal(1, keycap.Opacity);
        rig.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        Assert.Equal(0, keycap.Opacity);
    }, reduceMotion: true);

    // ---- the owner's look -----------------------------------------------------------------------------------------------

    [Theory]
    [Trait("Rule", "R-TEST-1")] // capture rig: the owner's look at the badges (MARROW_SESSION_HOTKEYS_CAPTURE)
    [InlineData("dark", false, null)]
    [InlineData("light", false, null)]
    [InlineData("light-pinned-helper-selected", false, null)]
    [InlineData("dark-activity", true, null)]
    [InlineData("light-zoom-175", false, 175)]
    public void The_badges_render_legibly(string name, bool activity, int? zoom) => With(rig =>
    {
        if (name.Contains("pinned", StringComparison.Ordinal))
        {
            rig.Model.UpdateThread(rig.Ids[2], thread => thread with { IsPinned = true });
            rig.Model.UpdateThread(rig.Ids[5], thread => thread with { ParentThreadID = rig.Ids[0] });
            rig.Model.UpdateThread(rig.Ids[0], thread => thread with { ExpandsHelpers = true });
            rig.Model.SelectedThreadID = rig.Ids[0];
        }
        if (activity) rig.Model.Settings.SidebarActivityView = true;
        rig.Settle();
        rig.ControlDown = true;
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.Step(500);
        rig.Settle(20);
        var rows = RowsOnScreen(rig.Sidebar);
        Assert.All(rows.Take(SessionNumbers.Count), row =>
        {
            Assert.Equal(1, row.Keycap.Opacity);
            Assert.True(row.Keycap.Bounds.Height >= 12 && row.Keycap.Bounds.Height <= 30, $"keycap {row.Keycap.Bounds}");
        });
        Capture(rig, $"sidebar-numbers-{name}.png");
    }, threads: 7, theme: name.StartsWith("dark", StringComparison.Ordinal) ? ThemeVariant.Dark : ThemeVariant.Light, zoom: zoom);

    [Fact]
    [Trait("Rule", "R-TEST-1")] // capture rig: one frame mid-switch, to see the cross (MARROW_SESSION_HOTKEYS_CAPTURE)
    public void A_switch_mid_move_renders_one_glass_and_the_words_crossing() => With(rig =>
    {
        var order = RowsOnScreen(rig.Sidebar).Select(row => row.Id).ToArray();
        rig.Model.UpdateThread(order[0], thread => thread with { Title = "Ship the login fix", HasCustomTitle = true });
        rig.Model.UpdateThread(order[2], thread => thread with { Title = "Draft the release notes", HasCustomTitle = true });
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        rig.Chat(order[0]).ComposerBox!.Text = "half a thought";
        rig.Settle(2);
        rig.Click(order[2]);
        rig.Step();
        rig.Step();
        AssertOneOfEachDrawn(rig, order[2], "mid-move");
        Capture(rig, "switch-mid-move-dark.png");
        rig.Settle();
        Capture(rig, "switch-settled-dark.png");
    }, theme: ThemeVariant.Dark);

    private static void Capture(Rig rig, string file)
    {
        var folder = Environment.GetEnvironmentVariable("MARROW_SESSION_HOTKEYS_CAPTURE");
        if (string.IsNullOrEmpty(folder)) return;
        Dispatcher.UIThread.RunJobs();
        using var frame = rig.Window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
#pragma warning disable CS0618
        frame.Save(Path.Combine(folder, file));
#pragma warning restore CS0618
    }

    // No control under the root has a pending measure or arrange: the frame changed only what is drawn.
    private static bool LaidOut(Control root) => Unlaid(root) is null;

    /// <summary>The first control under <paramref name="root"/> waiting for a layout pass, named for the failure message.</summary>
    private static string? Unlaid(Control root) =>
        Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualDescendants(root).OfType<Layoutable>()
            // A hidden control (a composer tab slot holding no tab) is never laid out, so its flags say nothing.
            .FirstOrDefault(control => control.IsEffectivelyVisible && (!control.IsMeasureValid || !control.IsArrangeValid)) is { } waiting
            ? $"{waiting.GetType().Name} {(waiting.IsMeasureValid ? "arrange" : "measure")} under {string.Join("<", Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(waiting).Take(6).Select(a => a.GetType().Name))}"
            : null;

    private static Button Row1Button(Rig rig, Guid id) =>
        LogicalQuery.SelfAndDescendants<Button>(rig.Row(id)).First();

    private static Rect TitleFrame(Rig rig, Guid id)
    {
        var row = rig.Row(id);
        var title = LogicalQuery.SelfAndDescendants<TextBlock>(row).First(block => !string.IsNullOrEmpty(block.Text) && block.Text!.Length > 2);
        return new Rect(title.TranslatePoint(default, row)!.Value, title.Bounds.Size);
    }

    // A critically damped spring from rest: under way on the first frame, monotonic toward the end with no overshoot, every
    // step smaller than the one before after the first two (it eases in to land), and landing exactly within the frames given.
    private static void AssertSprings(List<double> values, double end, double firstStepAtLeast, int frames)
    {
        var steps = values.Zip(values.Skip(1)).Select(pair => pair.Second - pair.First).ToList();
        Assert.True(steps.All(step => end > 0 ? step >= 0 : step <= 0), string.Join(", ", values));
        Assert.True(Math.Abs(steps[0]) >= firstStepAtLeast, $"first step {steps[0]:0.000}: " + string.Join(", ", values));
        Assert.True(values.All(value => value >= 0 && value <= 1), string.Join(", ", values));
        // (the last step, its landing on the end, may be up to half a percent: under any visible change)
        Assert.True(steps.Skip(2).Zip(steps.Skip(3)).All(pair => Math.Abs(pair.Second) <= Math.Abs(pair.First) + 0.005), string.Join(", ", values));
        Assert.Equal(end, values[Math.Min(frames, values.Count - 1)]);
        Assert.Equal(end, values[^1]);
    }
}
