using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for PR-COMPOSER-053 (DroppyCode/App/Views/Composer/ComposerView.swift:228-237; ComposerTextView.swift:92-96, :130-134), STOP
// App.Views.Composer.ComposerView #5 (a): a focus loss whose click lands inside the open suggestion panel keeps the list, because
// isClickInsideSuggestions (the port: suggestionPopover.IsOpen && popup.IsPointerOverPopup, ComposerTextView.cs:131-133) reads where the real pointer
// is over the panel's own window, which the headless platform has no HWND for. A real ComposerView with an open @ list on a real Win32 window, the
// panel a real Popup HWND, the pointer and its button the system's (SendInput), never RaiseEvent:
//   - with the pointer over a row of the panel the check answers inside; a focus loss then (the text view resigning while the pointer is on the
//     panel, as a click in an NSPopover makes the text view resign) keeps the list; a real press and release on the row keeps the list while
//     pressed and its pick lands in the draft;
//   - with the list open again, a real press on the transcript's selectable text (the chat's own selectable text, VeiledText.SelectableText) takes
//     the focus from the text view while the pointer is outside the panel: the list clears, the panel closes and the refresh the list had is cancelled.
// Needs a free interactive desktop; the runner takes native.lock around the run.
[Collection("WindowManagerNative")]
public sealed class ComposerSuggestionsNativeTests(WindowManagerNativeSession native)
{
    private static void Move(PixelPoint point)
    {
        N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
        N.Mouse(N.MOUSE_MOVE);
    }

    private static Popup Panel(ComposerView view) =>
        (Popup?)typeof(ComposerController).GetField("suggestionPopover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view.Controller)
            ?? throw new InvalidOperationException("the suggestion panel was never made");

    [Fact]
    [Trait("Req", "PR-COMPOSER-053")] // ComposerView.swift:228-237; ComposerTextView.swift:92-96, :130-134
    public void A_press_inside_the_open_panel_keeps_the_list_and_a_press_on_the_transcript_clears_it() => native.Run(subject =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        try
        {
            var directory = Directory.CreateTempSubdirectory("rec-composer-suggestions-").FullName;
            foreach (var name in new[] { "main.swift", "mail.txt", "map.json" }) File.WriteAllBytes(Path.Combine(directory, name), []);
            // the shared index of the directory is built before a composer asks for it (ComposerView.swift:1121-1127), as the headless tests do
            var indexing = native.OnUi(() => new FileIndex().Prepare(directory));
            N.Until(() => indexing.IsCompleted, "the file index was never built", 10000);
            Task? loading = null;
            var (window, view, box, transcript) = native.OnUi(() =>
            {
                subject.Main.Hide();
                subject.Settings.Hide();
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Main)); // the frame clock the app binds at start-up
                var model = new Marrow.App.AppModel();
                // a runtime for a thread the model does not hold: no provider, no session, the pill's text only (ComposerView.swift:160)
                var runtime = new ThreadRuntime(Guid.NewGuid(), model);
                loading = runtime.EnsureLoaded();
                var view = new ComposerView(runtime, directory);
                var transcript = VeiledText.SelectableText(wraps: true);
                transcript.Text = "The transcript: a message the reader can select, above the composer.";
                transcript.Margin = new Thickness(20);
                var column = new DockPanel { LastChildFill = false };
                DockPanel.SetDock(view, Dock.Bottom);
                DockPanel.SetDock(transcript, Dock.Top);
                column.Children.Add(view);
                column.Children.Add(transcript);
                var host = new Border { Child = column, Background = Brushes.DimGray, Padding = new Thickness(20, 0, 20, 14) };
                AppEnvironment.SetAppModel(host, model);
                var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
                var window = new AvaloniaWindow
                {
                    Width = 700, Height = 600, ShowInTaskbar = false, Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = new PixelPoint(screen.WorkingArea.X + 120, screen.WorkingArea.Y + 80), Content = host,
                };
                window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
                native.Register(window);
                window.Show();
                window.Activate();
                return (window, view, view.GetVisualDescendants().OfType<ComposerNSTextView>().Single(), transcript);
            });
            N.Until(() => loading!.IsCompleted, "the thread runtime never loaded", 5000);
            N.Until(() => native.OnUi(() => window.IsActive && box.IsFocused), "the composer's text view never took the keyboard", 5000);
            // what each loss of focus saw: whether the pointer was over the open panel at that moment
            var losses = new List<bool>();
            native.OnUi(() => box.LostFocus += (_, _) => losses.Add(view.Controller.IsClickInsideSuggestions()));

            void TypeMention() => native.OnUi(() =>
            {
                box.Text = "@ma";
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                box.SelectionStart = box.SelectionEnd = 3;
            });
            bool Listed() => native.OnUi(() => view.Suggestions.IsVisible && Panel(view).IsOpen);
            string State() => native.OnUi(() => $"draft '{box.Text}', list visible {view.Suggestions.IsVisible} with {view.Suggestions.Items.Count} items, panel {(typeof(ComposerController).GetField("suggestionPopover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view.Controller) is Popup p ? (p.IsOpen ? "open" : "closed") : "never made")}, focused {box.IsFocused}");
            TypeMention();
            if (!N.Wait(Listed, 5000)) throw new InvalidOperationException("the @ list never opened: " + State());
            Thread.Sleep(500); // the panel has appeared

            // 1. The pointer over a row of the real panel: the check answers inside, and a focus loss then keeps the list.
            var row = native.OnUi(() => Panel(view).Child!.GetVisualDescendants().OfType<SuggestionPopoverRow>().First());
            var onRow = native.OnUi(() => row.PointToScreen(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2)));
            Move(onRow);
            N.Until(() => native.OnUi(() => view.Controller.IsClickInsideSuggestions()), "the pointer over the panel's row was never seen inside the panel", 3000);
            native.OnUi(() => transcript.Focus()); // the text view resigns while the pointer is on the panel
            N.Until(() => native.OnUi(() => losses.Count == 1), "the text view never lost the keyboard");
            Thread.Sleep(300); // the resign is reported on a later main-actor turn (ComposerTextView.swift:280-282)
            Assert.True(losses[0], "the pointer was over the panel's row when the text view resigned");
            Assert.True(Listed(), "a focus loss with the pointer inside the panel closed the list");
            native.OnUi(() => box.Focus());
            N.Until(() => native.OnUi(() => box.IsFocused), "the text view did not take the keyboard back");
            N.Until(Listed, "the list was gone after the text view took the keyboard back", 3000);

            // 2. A real press on the row keeps the list while it is held, and the release picks it.
            var picked = native.OnUi(() => view.Suggestions.Items[(int)view.Suggestions.Selected].Value);
            Move(onRow);
            Thread.Sleep(150);
            N.Mouse(N.MOUSE_LEFTDOWN);
            Thread.Sleep(400);
            var listedWhilePressed = Listed();
            var lossesWhilePressed = native.OnUi(() => losses.ToArray());
            N.Mouse(N.MOUSE_LEFTUP);
            Assert.True(listedWhilePressed, "a press on a row of the open panel closed the list");
            Assert.All(lossesWhilePressed.Skip(1), inside => Assert.True(inside, "the text view resigned with the pointer outside the panel during the press"));
            N.Until(() => native.OnUi(() => box.Text == picked), $"the row's pick never landed: the draft reads '{native.OnUi(() => box.Text)}', the row was '{picked}'", 3000);
            Console.WriteLine($"ComposerSuggestionsNativeTests: focus losses during the press on the row: {lossesWhilePressed.Length - 1}");

            // 3. With the list open again, a real press on the transcript's text, outside the panel: the list clears and its refresh is cancelled.
            TypeMention();
            N.Until(Listed, "the @ list never opened again", 5000);
            Thread.Sleep(300);
            var refresh = native.OnUi(() => view.Controller.SuggestionRefresh) ?? throw new InvalidOperationException("no refresh behind the list");
            var onTranscript = native.OnUi(() => transcript.PointToScreen(new Point(30, transcript.Bounds.Height / 2)));
            Move(onTranscript);
            Thread.Sleep(150);
            Assert.False(native.OnUi(() => view.Controller.IsClickInsideSuggestions()));
            var before = native.OnUi(() => losses.Count);
            N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP);
            N.Until(() => native.OnUi(() => losses.Count > before), "a press on the transcript's text did not take the keyboard from the composer", 3000);
            Assert.False(losses[^1], "the pointer was over the panel when the press on the transcript took the keyboard");
            N.Until(() => !native.OnUi(() => view.Suggestions.IsVisible), "the list stayed after the press on the transcript", 3000);
            N.Until(() => !native.OnUi(() => Panel(view).IsOpen), "the panel stayed open after the press on the transcript", 3000);
            Assert.True(refresh.IsCancelled, "the refresh behind the list was not cancelled");
            Thread.Sleep(500);
            Assert.False(native.OnUi(() => view.Suggestions.IsVisible)); // nothing put the list back
        }
        finally
        {
            N.SetCursorPos(original.X, original.Y);
        }
    }, factType: typeof(ComposerSuggestionsNativeTests));
}
