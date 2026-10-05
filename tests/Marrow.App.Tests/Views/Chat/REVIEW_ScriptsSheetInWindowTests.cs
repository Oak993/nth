using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (rec-composer 4a608bb54): the in-window scripts sheet (WindowSheetLayer) as a sheet: Escape, keyboard focus, hit-testing.
[Collection("Headless")]
public sealed class REVIEW_ScriptsSheetInWindowTests(HeadlessSession headless)
{
    private static (AvaloniaWindow window, TextBox box, Border anchor) Open(AppModelHarness h, Marrow.App.AppModel model, ManualFrameSource frames, bool focusBox)
    {
        var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
        var anchor = new Border();
        var box = new TextBox { Width = 200, Height = 30, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        var window = new Marrow.App.Window.ThreadWindow { Content = new Panel { Children = { box, anchor } }, Width = 900, Height = 700 };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        if (focusBox) box.Focus();
        ChatGitFixtures.InvokeStatic(typeof(ScriptsMenu), "PresentScriptsEditor", model, project.Id, anchor);
        return (window, box, anchor);
    }

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

    private static bool SheetShown(AvaloniaWindow window) => window.GetVisualDescendants().OfType<Marrow.App.Port.WindowSheetLayer>().Any();

    [Theory]
    [Trait("Req", "PR-TERM-074")] // ChatView.swift:1456-1463 the scripts sheet, an attached sheet of its window
    [InlineData(true)]
    [InlineData(false)]
    public void REVIEW_Escape_closes_the_in_window_scripts_sheet_whatever_holds_the_keyboard(bool focusBox)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            AvaloniaWindow window = null!; TextBox box = null!;
            h.Main.OnMain(() => { (window, box, _) = Open(h, model, frames, focusBox); });
            h.Main.RunUntilIdle(); Settle(h, frames);
            h.Main.OnMain(() => Assert.True(SheetShown(window), "sheet not shown"));
            h.Main.OnMain(() => { window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Escape, RawInputModifiers.None); });
            Settle(h, frames);
            h.Main.OnMain(() => Assert.False(SheetShown(window), "Escape did not close the sheet (focusBox=" + focusBox + ", focus=" + window.FocusManager?.GetFocusedElement()?.GetType().Name + ")"));
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-074")] // ChatView.swift:1456-1463 the scripts sheet, an attached sheet of its window
    public void REVIEW_Typing_while_the_sheet_is_open_does_not_reach_a_field_under_it()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            AvaloniaWindow window = null!; TextBox box = null!;
            h.Main.OnMain(() => { (window, box, _) = Open(h, model, frames, true); });
            h.Main.RunUntilIdle(); Settle(h, frames);
            h.Main.OnMain(() => window.KeyTextInput("zzz"));
            h.Main.OnMain(() => Assert.True(string.IsNullOrEmpty(box.Text), "typed text reached the field under the sheet: '" + box.Text + "'"));
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-074")] // ChatView.swift:1456-1463 the scripts sheet, an attached sheet of its window
    public void REVIEW_Hit_test_beside_and_above_the_sheet_reaches_the_layer_only()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            AvaloniaWindow window = null!; TextBox box = null!;
            h.Main.OnMain(() => { (window, box, _) = Open(h, model, frames, false); });
            h.Main.RunUntilIdle(); Settle(h, frames);
            h.Main.OnMain(() =>
            {
                foreach (var p in new[] { new Point(5, 5), new Point(100, 685) })
                {
                    var hit = window.InputHitTest(p) as Visual;
                    Assert.True(hit is not null && hit.GetSelfAndVisualAncestors().Any(v => v is Marrow.App.Port.WindowSheetLayer), $"{p} reaches {hit?.GetType().Name}, not the sheet layer");
                }
            });
        });
    }

    // The composer takes Escape for itself (ComposerView.cs: a running turn is taken back, a panel closes) and marks it handled. A popover or sheet over the window
    // closes first (PopoverHost.Listen: a tunnelling handler on the owner window); the in-window sheet has no such hook, so the field under it eats the key.
    [Theory]
    [Trait("Req", "PR-TERM-074")] // ChatView.swift:1456-1463 the scripts sheet, an attached sheet of its window
    [InlineData(true)]
    [InlineData(false)]
    public void REVIEW_Escape_closes_the_sheet_before_the_field_under_it_acts_on_it(bool inWindow)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            AvaloniaWindow window = null!; var fieldGotEscape = false; var shown = false;
            h.Main.OnMain(() =>
            {
                var box = new TextBox { Width = 200, Height = 30 };
                box.KeyDown += (_, e) => { if (e.Key == Key.Escape) { fieldGotEscape = true; e.Handled = true; } };
                var anchor = new Border();
                window = new Marrow.App.Window.ThreadWindow { Content = new Panel { Children = { box, anchor } }, Width = 900, Height = 700 };
                ChatHeadlessFixtures.Track(window);
                window.Show();
                box.Focus();
                Marrow.App.Port.ChatSheetPresentation.Show(anchor, dismiss => new Border { Width = 300, Height = 200, Background = Avalonia.Media.Brushes.Gray }, null, inWindow);
            });
            h.Main.RunUntilIdle(); Settle(h, frames);
            h.Main.OnMain(() => shown = window.GetVisualDescendants().OfType<Marrow.App.Port.WindowSheetLayer>().Any() || inWindow == false);
            h.Main.OnMain(() => { window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Escape, RawInputModifiers.None); });
            Settle(h, frames);
            h.Main.OnMain(() =>
            {
                Assert.True(shown);
                Assert.False(fieldGotEscape, "the field under the sheet took the Escape (inWindow=" + inWindow + ")");
                Assert.False(window.GetVisualDescendants().OfType<Marrow.App.Port.WindowSheetLayer>().Any(), "the sheet is still open");
            });
        });
    }
}
