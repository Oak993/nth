using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Window;
using AvaloniaWindow = Avalonia.Controls.Window;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;
using Rig = Marrow.App.Tests.Window.Native.TimelineRowsNativeRig;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for PR-CHAT-110 (OD-0055): the revert confirmation popover of the turn's file card, TimelineRows.swift:2777-2798, in a real Win32 window with a
// real popup window. Revert there is `.keyboardShortcut(.defaultAction)` (:2791), so Return activates it though the pointer never went into the popover. Headless
// cannot open a real Popup (the reviews of close:timelinerows-ab), so whether Return reaches the Revert button in the app was unresolved until this run.
// A failure where the window could not take the foreground or is covered is environmental (NATIVE_PRECONDITION) and is retried alone.
[Collection("WindowManagerNative")]
public sealed class TimelineRowsNativePopoverTests(WindowManagerNativeSession native)
{
    private const string RevertTip = "Revert this turn's files and conversation";

    private void Revert(Action<AvaloniaWindow, AppModel, Popup, (string Root, string Start, string Before, string After), Guid, Guid> body, string fact)
    {
        native.Run(subject =>
        {
            var checkout = Rig.MakeCheckout("revert");
            try
            {
                var (thread, first, second) = Rig.Seed(native, checkout);
                var (window, model) = Rig.Open(native, subject, thread);
                // The timeline loads the thread document and draws its two turn cards; the second card's disc is the one clicked.
                N.Until(() => Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == RevertTip).Count == 2, "The thread's two revert discs never appeared.", 30000);
                var disc = Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == RevertTip).Last();
                var popup = native.OnUi(() => ((Panel)disc.Parent!).Children.OfType<Popup>().Single());
                Rig.Watch(native, window);
                var opened = 0; var closed = 0;
                var events = new List<string>();
                native.OnUi(() =>
                {
                    popup.Opened += (_, _) => opened++; popup.Closed += (_, _) => closed++;
                    disc.Click += (_, _) => events.Add("Click");
                    disc.PointerEntered += (_, _) => events.Add("Entered");
                    disc.PointerPressed += (_, e) => events.Add("Pressed " + e.GetCurrentPoint(disc).Properties.PointerUpdateKind);
                    disc.PointerReleased += (_, e) => events.Add("Released " + e.InitialPressMouseButton);
                    window.AddHandler(InputElement.PointerPressedEvent, (object? _, PointerPressedEventArgs e) => events.Add("window down on " + e.Source?.GetType().Name + " handled " + e.Handled), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
                });
                Rig.Tap(native, window, disc);
                N.Until(() => native.OnUi(() => popup.IsOpen), $"Clicking the revert disc did not open the confirmation popover (opened {native.OnUi(() => opened)} times, closed {native.OnUi(() => closed)}; {string.Join(", ", native.OnUi(() => events.ToList()))}). " + Where(window, disc), 5000);
                body(window, model, popup, checkout, thread, second);
            }
            finally { Rig.Delete(checkout.Root); }
        }, fact, typeof(TimelineRowsNativePopoverTests));
    }

    private static IEnumerable<Button> HostedButtons(Popup popup) => PopoverHost.Content(popup) is { } content ? content.GetSelfAndVisualDescendants().OfType<Button>() : [];

    // What the runtime shows of the revert: the turns left and the notices it posted.
    private static string Outcome(AppModel model, Guid thread)
    {
        var runtime = model.Runtime(thread);
        var notices = runtime.Entries.Select(entry => entry.Item.TimelineItemContent).OfType<Marrow.Core.Models.TimelineItem.Content.Notice>().Select(notice => notice.Value.Message);
        return $"{runtime.Turns.Count} turns, phase {runtime.Phase}, notices [{string.Join(" | ", notices)}]";
    }

    // What a failed click found: the disc's place in the window, and what the window hit-tests at the point that was clicked.
    private string Where(AvaloniaWindow window, Button disc) => native.OnUi(() =>
    {
        var local = disc.TranslatePoint(new Point(disc.Bounds.Width / 2, disc.Bounds.Height / 2), window) ?? default;
        var hit = window.InputHitTest(local) as Visual;
        var chain = string.Join(" < ", (hit?.GetSelfAndVisualAncestors() ?? []).Take(8).Select(visual => visual.GetType().Name));
        var sv = disc.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        var diag = sv is null ? "no scroller" : $"scroller bounds {sv.Bounds} at {sv.TranslatePoint(default, window)} viewport {sv.Viewport} extent {sv.Extent} offset {sv.Offset}; ";
        return diag + $"disc bounds {disc.Bounds} at {disc.TranslatePoint(default, window)}, window {window.ClientSize}, its middle {local}, hit {chain}, pointer over disc {disc.IsPointerOver}, enabled {disc.IsEnabled}, hit-testable {disc.IsHitTestVisible}";
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // TimelineRows.swift:2777-2798 the confirmation, :2791 `.keyboardShortcut(.defaultAction)` on Revert, :2695 onRevert restoreFiles: true
    public void Return_in_the_revert_confirmation_reverts_the_turn_with_files_restored() => Revert((window, model, popup, checkout, thread, second) =>
    {
        // Return, with the pointer never having gone into the popover: the focused element is wherever the window left it.
        var clicks = new List<string>();
        native.OnUi(() =>
        {
            foreach (var button in HostedButtons(popup)) { var label = button.IsDefault ? "default" : "other"; button.Click += (_, _) => clicks.Add(label); }
        });
        Rig.Press(Rig.Return);
        var changed = Path.Combine(checkout.Root, "changed.txt");
        N.Until(() => File.ReadAllText(changed) == "before\n",
            "Return did not revert the turn: changed.txt reads '" + File.ReadAllText(changed).TrimEnd() + "' and the popover is " + (native.OnUi(() => popup.IsOpen) ? "still open" : "closed") + "; clicks [" + string.Join(",", native.OnUi(() => clicks.ToList())) + "]; " + native.OnUi(() => Outcome(model, thread)), 20000);
        Assert.Equal("before\n", File.ReadAllText(changed)); // the files went back to how they were before the turn
        N.Until(() => native.OnUi(() => model.Runtime(thread).Turns.Count) == 1, "The reverted turn did not leave the conversation.", 20000);
        Assert.DoesNotContain(native.OnUi(() => model.Runtime(thread).Turns.Select(turn => turn.Id).ToList()), id => id == second);
        N.Until(() => !native.OnUi(() => popup.IsOpen), "The popover stayed open after Revert.", 5000);
    }, nameof(Return_in_the_revert_confirmation_reverts_the_turn_with_files_restored));

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // TimelineRows.swift:2777-2798 "Dismissing the popover cancels"
    public void Escape_in_the_revert_confirmation_closes_it_and_reverts_nothing() => Revert((window, model, popup, checkout, thread, second) =>
    {
        Rig.Press(Rig.Escape);
        N.Until(() => !native.OnUi(() => popup.IsOpen), "Escape did not close the confirmation popover.", 5000);
        Thread.Sleep(1500); // a revert, had Escape reverted, would have gone through by now
        Assert.Equal("after\n", File.ReadAllText(Path.Combine(checkout.Root, "changed.txt")));
        Assert.Equal(2, native.OnUi(() => model.Runtime(thread).Turns.Count));
    }, nameof(Escape_in_the_revert_confirmation_closes_it_and_reverts_nothing));
}
