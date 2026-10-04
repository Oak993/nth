using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// PR-TERM-074 (ChatView.swift:1456-1463 `.sheet`), the in-window scripts sheet as a sheet (ED-NEW-rec-composer 5, after the review of 4a608bb54): an attached
// sheet is its window's key window while it shows, so it takes the keyboard when it opens, its window reads it as its attached sheet (ThreadWindow.swift:12-14,
// :26: the thread chords stand still), the keyboard goes back to where it was once it is dismissed, and the window can still be dragged by its own drag region
// beside it (a Mac window with a sheet can be).
[Collection("Headless")]
public sealed class ChatViewScriptsSheetKeyboardTests(HeadlessSession headless)
{
    private static void Settle(AppModelHarness h, ManualFrameSource frames)
    {
        for (var pass = 0; pass < 40; pass++)
        {
            h.Main.Advance(TimeSpan.FromMilliseconds(16));
            frames.Advance(TimeSpan.FromMilliseconds(16));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static WindowSheetLayer? Layer(AvaloniaWindow window) => window.GetVisualDescendants().OfType<WindowSheetLayer>().SingleOrDefault();

    [Fact]
    [Trait("Req", "PR-TERM-074")] // ChatView.swift:1456-1463; the sheet is the window's key window while it shows
    public void The_sheet_takes_the_keyboard_when_it_opens_is_its_windows_attached_sheet_and_gives_the_keyboard_back_when_it_is_dismissed()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            Marrow.App.Window.ThreadWindow window = null!; TextBox box = null!;
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                var anchor = new Border();
                box = new TextBox { Width = 200, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
                window = new Marrow.App.Window.ThreadWindow { Content = new Panel { Children = { box, anchor } }, Width = 900, Height = 700 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                box.Focus();
                ChatGitFixtures.InvokeStatic(typeof(ScriptsMenu), "PresentScriptsEditor", model, project.Id, anchor);
            });
            h.Main.RunUntilIdle(); Settle(h, frames);
            h.Main.OnMain(() =>
            {
                var layer = Assert.IsType<WindowSheetLayer>(Layer(window));
                var focused = window.FocusManager?.GetFocusedElement() as Visual;
                Assert.True(focused is not null && (ReferenceEquals(focused, layer) || layer.IsVisualAncestorOf(focused)), "the sheet did not take the keyboard: " + focused?.GetType().Name);
                Assert.Same(layer.Sheet, window.AttachedSheet);
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            });
            Settle(h, frames);
            h.Main.OnMain(() =>
            {
                Assert.Null(Layer(window));
                Assert.Null(window.AttachedSheet);
                Assert.True(box.IsFocused, "the keyboard did not go back to the field it was in");
                window.KeyTextInput("ok"); // and the window's own content takes keys again
                Assert.Equal("ok", box.Text);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-074")] // ChatView.swift:1456-1463; Chrome.swift WindowDragArea (:1091-1099): the window drags by its own region beside a sheet
    public void Beside_the_sheet_a_press_on_the_windows_drag_region_reaches_it_and_anywhere_else_the_sheet_takes_it()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            AvaloniaWindow window = null!; WindowDragArea drag = null!; Button under = null!;
            h.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                var anchor = new Border();
                drag = new WindowDragArea { Height = 40, VerticalAlignment = VerticalAlignment.Top }; // a drag strip across the top
                under = new Button { Width = 120, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Content = "under" };
                window = new Marrow.App.Window.ThreadWindow { Content = new Panel { Children = { drag, under, anchor } }, Width = 900, Height = 700 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                ChatGitFixtures.InvokeStatic(typeof(ScriptsMenu), "PresentScriptsEditor", model, project.Id, anchor);
            });
            h.Main.RunUntilIdle(); Settle(h, frames);
            h.Main.OnMain(() =>
            {
                var layer = Assert.IsType<WindowSheetLayer>(Layer(window));
                Visual? At(Point point) => window.InputHitTest(point) as Visual;
                bool InLayer(Visual? hit) => hit is not null && hit.GetSelfAndVisualAncestors().Contains(layer);
                Assert.True(At(new Point(20, 20)) is { } top && top.GetSelfAndVisualAncestors().Contains(drag), "the drag strip beside the sheet does not take the press: " + At(new Point(20, 20))?.GetType().Name);
                Assert.True(InLayer(At(new Point(40, 680))), "a press on the button under the sheet's layer was not taken by the layer");
                Assert.True(InLayer(At(new Point(450, 350))), "the sheet itself does not take the press");
                Assert.True(InLayer(At(new Point(20, 300))), "a press beside the sheet, off the drag strip, reached the content under it");
            });
        });
    }
}
