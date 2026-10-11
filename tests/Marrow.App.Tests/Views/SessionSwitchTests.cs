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
// moves the same quick, premium way: the selection glides, the chat left fades out, the chat opened settles in. Sampled frame
// by frame on the manual frame clock.
[Collection("Headless")]
public sealed class SessionSwitchTests(HeadlessSession headless)
{
    private const double Frame = 1000.0 / 60;

    private sealed class Rig(AppModelHarness h, AppModel model, ThreadWindow window, SidebarView sidebar, DetailView detail, ManualFrameSource frames, Guid[] ids)
    {
        public AppModelHarness H { get; } = h;
        public AppModel Model { get; } = model;
        public ThreadWindow Window { get; } = window;
        public SidebarView Sidebar { get; } = sidebar;
        public DetailView Detail { get; } = detail;
        public ManualFrameSource Frames { get; } = frames;
        public Guid[] Ids { get; } = ids;
        public bool ControlDown { get; set; }

        /// <summary>The numbered order the sidebar published.</summary>
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

        public ThreadColumns Columns => Assert.IsType<ThreadColumns>(Detail.Child);

        public FrameHostingView Column(Guid id)
        {
            var coordinator = typeof(ThreadColumns).GetField("coordinator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Columns)!;
            var columns = (Dictionary<Guid, FrameHostingView>)coordinator.GetType().GetField("columns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
            return columns[id];
        }

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
    }

    private void With(Action<Rig> check, int threads = 4, bool reduceMotion = false, ThemeVariant? theme = null, bool collapsed = false) => headless.OnUiThread(() =>
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
        ThreadWindow window = null!;
        SidebarView sidebar = null!;
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
            });
            h.Main.Loop.Run(async () =>
            {
                foreach (var id in ids) await model.Runtime(id).EnsureLoaded();
            }, AppModelHarness.Bound);
            // The window lives on the Avalonia main executor until it has closed (finally), so its teardown runs on main too.
            actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            window = new ThreadWindow { Width = 900, Height = 600 };
            if (theme is not null) window.RequestedThemeVariant = theme;
            var resize = new WindowLiveResize(window);
            sidebar = new SidebarView();
            detail = new DetailView();
            Control sidebarSlot = collapsed
                ? new Border { Width = 0, ClipToBounds = true, Child = new Border { Width = 260, Child = sidebar } }
                : sidebar;
            sidebarSlot.HorizontalAlignment = HorizontalAlignment.Left;
            var layout = new Grid { ColumnDefinitions = new ColumnDefinitions(collapsed ? "Auto,*" : "260,*") };
            Grid.SetColumn(detail, 1);
            layout.Children.Add(sidebarSlot);
            layout.Children.Add(detail);
            var host = new Border
            {
                Background = new SolidColorBrush(theme == ThemeVariant.Light ? Color.FromRgb(233, 233, 229) : Color.FromRgb(46, 47, 44)),
                Child = layout,
            };
            AppEnvironment.SetAppModel(host, model);
            AppEnvironment.SetWindowLiveResize(host, resize);
            window.Content = host;
            window.SelectThreadNumber = model.SelectThreadNumber;
            window.ShowSessionNumbers = shown => model.SessionNumbers.SetShown(shown, window);
            window.Show();
            rig = new Rig(h, model, window, sidebar, detail, frames, ids);
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

    // ---- Ctrl+1..9 -----------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void The_number_row_and_the_number_pad_open_the_first_session_ready_to_type_with_the_caret_at_the_end() => With(rig =>
    {
        var order = rig.Order;
        Assert.Equal(rig.Ids.Length, order.Count);
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

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void The_numbers_follow_the_rows_the_sidebar_shows_pinned_first() => With(rig =>
    {
        var last = rig.Order[^1];
        rig.Model.UpdateThread(last, thread => thread with { IsPinned = true });
        rig.Settle();
        Assert.Equal(last, rig.Order[0]); // the sidebar draws pinned threads first, and so the numbers do
        rig.Press(Key.D1, PhysicalKey.Digit1);
        Assert.Equal(last, rig.Model.SelectedThreadID);
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void With_the_sidebar_collapsed_the_shortcut_still_opens_the_session_it_would_number() => With(rig =>
    {
        var second = rig.Order[1];
        rig.Press(Key.D2, PhysicalKey.Digit2);
        Assert.Equal(second, rig.Model.SelectedThreadID);
    }, collapsed: true);

    // ---- the switch's motion ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void A_click_on_another_row_glides_the_selection_and_crosses_the_chats_smoothly_with_no_layout_pass() => With(rig =>
    {
        var order = rig.Order;
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        var from = rig.RowFrame(order[0]);
        var to = rig.RowFrame(order[3]);
        var leaving = rig.Column(order[0]);
        // The pointer reaches the row first, as a hand does; the hover's own look (source behaviour) settles before the press,
        // so every frame below belongs to the switch.
        rig.Hover(order[3]);
        rig.Settle();
        rig.Click(order[3]);
        Assert.Equal(order[3], rig.Model.SelectedThreadID);
        var entering = rig.Column(order[3]);
        Assert.True(rig.Sidebar.Glide.IsGliding);
        Assert.True(leaving.IsLeaving); // gated at once: no chord or focus reaches the chat left
        Assert.False(leaving.IsEffectivelyEnabled);
        Assert.True(rig.Chat(order[3]).ComposerBox!.IsFocused);
        Dispatcher.UIThread.RunJobs();
        rig.Window.UpdateLayout(); // the switch's one layout: the chat shown is measured before its first animated frame

        var glideY = new List<double>();
        var inOpacity = new List<double>();
        var outOpacity = new List<double>();
        var rises = new List<double>();
        for (var frame = 0; frame < 14; frame++)
        {
            rig.Step();
            // Every frame of the move is drawn without a layout pass: the columns and the list keep their measure.
            Assert.True(LaidOut(entering), $"frame {frame}: the chat opened asked for layout ({Unlaid(entering)})");
            // The chat left keeps its measure while it is drawn; once hidden it is out of layout altogether.
            Assert.True(!leaving.IsVisible || LaidOut(leaving), $"frame {frame}: the chat left asked for layout ({Unlaid(leaving)})");
            Assert.True(LaidOut(rig.Sidebar), $"frame {frame}: the sidebar asked for layout ({Unlaid(rig.Sidebar)})");
            if (rig.Sidebar.Glide.IsGliding) glideY.Add(rig.Sidebar.Glide.PillFrame.Y);
            inOpacity.Add(rig.Columns.Motion.PoseOf(entering).Opacity);
            rises.Add(rig.Columns.Motion.PoseOf(entering).Rise);
            outOpacity.Add(leaving.IsVisible ? rig.Columns.Motion.PoseOf(leaving).Opacity : 0);
        }
        // The selection glides down without overshoot and lands exactly on the row (then the row draws it again).
        Assert.NotEmpty(glideY);
        Assert.True(glideY.Zip(glideY.Skip(1)).All(pair => pair.Second >= pair.First - 0.01), string.Join(", ", glideY));
        Assert.True(glideY.All(y => y >= from.Y - 0.5 && y <= to.Y + 0.5));
        Assert.False(rig.Sidebar.Glide.IsGliding);
        Assert.True(glideY.Count <= 12, $"the glide took {glideY.Count} frames"); // about 160 ms
        // The chat opened is readable from the first frame and settles in 140 ms, rising 4 points; the one left is gone in 90 ms.
        Assert.True(inOpacity[0] > 0.25, $"first frame {inOpacity[0]}");
        Assert.True(inOpacity.Zip(inOpacity.Skip(1)).All(pair => pair.Second >= pair.First), string.Join(", ", inOpacity));
        Assert.True(inOpacity.Zip(inOpacity.Skip(1)).All(pair => pair.Second - pair.First <= 3 * Frame / 140 + 0.01), string.Join(", ", inOpacity));
        Assert.Equal(1, inOpacity[^1]);
        Assert.True(rises.Zip(rises.Skip(1)).All(pair => pair.Second <= pair.First), string.Join(", ", rises));
        Assert.True(rises[0] <= ChatSwitchMotion.Rise && rises[^1] == 0);
        Assert.True(outOpacity.Zip(outOpacity.Skip(1)).All(pair => pair.Second <= pair.First), string.Join(", ", outOpacity));
        Assert.Equal(0, outOpacity[6]); // 90 ms is under six frames
        Assert.False(leaving.IsVisible);
        Assert.False(leaving.IsLeaving);
        Assert.Equal(1, rig.Columns.Motion.PoseOf(leaving).Opacity); // hidden at rest, ready for a later show
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void The_shortcut_moves_exactly_like_the_click_and_a_quick_second_press_turns_it_without_a_jump() => With(rig =>
    {
        var order = rig.Order;
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        rig.Press(Key.D2, PhysicalKey.Digit2);
        Assert.True(rig.Sidebar.Glide.IsGliding);
        rig.Step();
        rig.Step();
        var second = rig.Column(order[1]);
        var midOpacity = rig.Columns.Motion.PoseOf(second).Opacity;
        var midY = rig.Sidebar.Glide.PillFrame.Y;
        Assert.InRange(midOpacity, 0.05, 0.99);
        rig.Release(Key.D2, PhysicalKey.Digit2);
        rig.Press(Key.D4, PhysicalKey.Digit4);
        Assert.Equal(order[3], rig.Model.SelectedThreadID);
        // The glide goes on from where it is (no snap back to a row), and the half-come chat leaves from its own opacity.
        Assert.Equal(midY, rig.Sidebar.Glide.PillFrame.Y, 3);
        Assert.Equal(midOpacity, rig.Columns.Motion.PoseOf(second).Opacity, 3);
        var y = new List<double> { midY };
        var leaving = new List<double> { midOpacity };
        for (var frame = 0; frame < 20; frame++)
        {
            rig.Step();
            if (rig.Sidebar.Glide.IsGliding) y.Add(rig.Sidebar.Glide.PillFrame.Y);
            if (second.IsVisible) leaving.Add(rig.Columns.Motion.PoseOf(second).Opacity);
        }
        var target = rig.RowFrame(order[3]).Y;
        Assert.True(y.Zip(y.Skip(1)).All(pair => Math.Abs(pair.Second - pair.First) < Math.Abs(target - midY) * 0.6 + 0.01), string.Join(", ", y));
        Assert.True(y.All(value => value <= target + 0.5), string.Join(", ", y)); // no overshoot past the row
        Assert.True(leaving.Zip(leaving.Skip(1)).All(pair => pair.Second <= pair.First), string.Join(", ", leaving));
        Assert.False(second.IsVisible);
        Assert.False(rig.Sidebar.Glide.IsGliding);
        Assert.Equal(1, rig.Columns.Motion.PoseOf(rig.Column(order[3])).Opacity);
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void With_Reduce_Motion_the_switch_is_at_once() => With(rig =>
    {
        var order = rig.Order;
        rig.Model.SelectedThreadID = order[0];
        rig.Settle();
        var leaving = rig.Column(order[0]);
        rig.Click(order[2]);
        Assert.False(rig.Sidebar.Glide.IsGliding);
        Assert.False(leaving.IsVisible);
        Assert.Equal(1, rig.Columns.Motion.PoseOf(rig.Column(order[2])).Opacity);
        Assert.Equal(0, rig.Columns.Motion.PoseOf(rig.Column(order[2])).Rise);
        Assert.True(rig.Chat(order[2]).ComposerBox!.IsFocused);
    }, reduceMotion: true);

    // ---- hold Ctrl: the numbers ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void Holding_Control_fades_the_numbers_in_together_and_letting_go_fades_them_out_with_no_jump() => With(rig =>
    {
        var order = rig.Order;
        var keycaps = order.Select(id => rig.Row(id).Keycap).ToArray();
        Assert.Equal(Enumerable.Range(1, order.Count).Select(n => (long?)n), keycaps.Select(k => k.Number));
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
        for (var frame = 0; frame < 16; frame++)
        {
            rig.Step();
            Assert.Single(keycaps.Select(k => k.Opacity).Distinct()); // all of them as one
            Assert.True(LaidOut(rig.Sidebar));
            fadeIn.Add(keycaps[0].Opacity);
        }
        AssertEases(fadeIn, 1, SessionNumbers.FadeInSeconds);
        // The badges never move the row's text.
        Assert.Equal(titleBefore, order.Select(id => TitleFrame(rig, id)).ToArray());
        // The mark under a badge (the row's leading icon) has faded out under it by the same amount.
        Assert.Equal(0, LogicalQuery.SelfAndDescendants<ThreadRowFace>(rig.Row(order[0])).Single().LeadingMark!.Opacity);

        rig.ControlDown = false;
        rig.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        var fadeOut = new List<double>();
        for (var frame = 0; frame < 14; frame++)
        {
            rig.Step();
            fadeOut.Add(keycaps[0].Opacity);
        }
        Assert.True(fadeOut[0] < 1); // under way on the first frame after the key-up
        AssertEases(fadeOut, 0, SessionNumbers.FadeOutSeconds);
        Assert.All(keycaps, keycap => Assert.Equal(0, keycap.Opacity));
    });

    [Fact]
    [Trait("Req", "ED-NEW-session-hotkeys")]
    public void Letting_go_mid_fade_turns_the_numbers_around_from_where_they_are() => With(rig =>
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
        for (var frame = 0; frame < 12; frame++)
        {
            rig.Step();
            values.Add(keycap.Opacity);
        }
        Assert.True(values.Zip(values.Skip(1)).All(pair => pair.Second <= pair.First), string.Join(", ", values));
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

    [Theory]
    [Trait("Rule", "R-TEST-1")] // capture rig: the owner's look at the badges, light and dark (MARROW_SESSION_HOTKEYS_CAPTURE)
    [InlineData("dark")]
    [InlineData("light")]
    public void The_badges_render_legibly_in_both_themes(string name) => With(rig =>
    {
        rig.ControlDown = true;
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.Step(500);
        rig.Settle(20);
        var keycap = rig.Row(rig.Order[0]).Keycap;
        Assert.Equal(1, keycap.Opacity);
        Assert.True(keycap.Bounds.Height >= 12 && keycap.Bounds.Height <= rig.Row(rig.Order[0]).Bounds.Height);
        var folder = Environment.GetEnvironmentVariable("MARROW_SESSION_HOTKEYS_CAPTURE");
        if (string.IsNullOrEmpty(folder)) return;
        Dispatcher.UIThread.RunJobs();
        using var frame = rig.Window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
#pragma warning disable CS0618
        frame.Save(Path.Combine(folder, $"sidebar-numbers-{name}.png"));
#pragma warning restore CS0618
    }, threads: 7, theme: name == "dark" ? ThemeVariant.Dark : ThemeVariant.Light);

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

    // Monotonic toward the end, no step past twice the even share (quadratic easing), and landing exactly on it.
    private static void AssertEases(List<double> values, double end, double seconds)
    {
        var share = Frame / 1000 / seconds;
        var steps = values.Zip(values.Skip(1)).Select(pair => pair.Second - pair.First).ToList();
        Assert.True(steps.All(step => end > 0 ? step >= 0 : step <= 0), string.Join(", ", values));
        Assert.True(steps.All(step => Math.Abs(step) <= 2 * share + 0.01), string.Join(", ", values));
        Assert.Equal(end, values[^1]);
    }
}
