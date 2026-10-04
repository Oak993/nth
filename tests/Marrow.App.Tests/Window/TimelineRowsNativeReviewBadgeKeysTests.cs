using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;
using Rig = Marrow.App.Tests.Window.Native.TimelineRowsNativeRig;

namespace Marrow.App.Tests.Window.Native;

// REVIEW (rec-tlr, final check of round 3): the badge popover (BadgePopoverCoordinator over QuestionSheet) and LinkPopover with real keys in a real window, now that their own
// window monitors no longer see a key PopoverHost already handled and the badge popover is a registered transient. Read: Return and Escape with the sheet's answer field FOCUSED
// (QuestionTab.swift:239 `.onSubmit(onSubmit)`: Return in the field advances, it does not press the default button), Return and Escape with the focus left in the window, and the
// order of two popovers open together (Escape closes the newest first).
[Collection("WindowManagerNative")]
public sealed class TimelineRowsNativeReviewBadgeKeysTests(WindowManagerNativeSession native)
{
    private void EnsureForeground(AvaloniaWindow window)
    {
        var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
        if (N.GetForegroundWindow() != handle) Rig.Front(native, window);
        if (N.GetForegroundWindow() != handle) throw new InvalidOperationException("NATIVE_PRECONDITION: the main window is not in front; no key was sent.");
    }

    private static QuestionRequest Request(int count, bool recommended)
    {
        var questions = Enumerable.Range(0, count).Select(index => new QuestionRequest.Question("q" + index, "H" + index, "Which " + index + "?",
            new ValueList<QuestionRequest.Choice>([new(recommended ? "A (Recommended)" : "A"), new("B")]), false, true, false)).ToList();
        return new QuestionRequest("request", new ValueList<QuestionRequest.Question>(questions));
    }

    private string Page(Control sheet) => native.OnUi(() => sheet.GetSelfAndVisualDescendants().OfType<TextBlock>().Select(block => block.Text).FirstOrDefault(text => text is not null && System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9]+ of [0-9]+$")) ?? "?");

    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void REVIEW_Badge_and_link_popovers_take_Return_and_Escape_once_with_the_field_focused_or_not() => native.Run(subject =>
    {
        var checkout = Rig.MakeCheckout("review-badge-keys");
        try
        {
            var (thread, _, _) = Rig.Seed(native, checkout);
            var (window, model) = Rig.Open(native, subject, thread);
            N.Until(() => Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == "Revert this turn's files and conversation").Count == 2, "The thread never appeared.", 30000);
            EnsureForeground(window);
            Rig.Press(0x7C); // F13: an event in the window, so WindowRectAnchor finds it (NSApp.currentEvent)
            var findings = new List<string>();
            var runtime = native.OnUi(() => model.Runtime(thread));

            BadgePopoverCoordinator Badge(QuestionRequest request, out QuestionSheet sheet, out int[] closes)
            {
                EnsureForeground(window); // in front before it opens: taking the foreground afterwards (the rig's Alt tap) is not part of what is read
                native.OnUi(() => window.Focus()); // the focus back in the window, not in the closed popover's field
                Rig.Press(0x7C); // an event in the window again: WindowRectAnchor reads NSApp.currentEvent, which a key in a closed popup leaves pointing at that popup (the app falls back to its key window)
                var counts = new int[1];
                closes = counts;
                var made = native.OnUi(() =>
                {
                    var coordinator = new BadgePopoverCoordinator();
                    coordinator.SetAnchor(new Rect(200, 200, 40, 20));
                    coordinator.Appear(window, () => counts[0]++);
                    var content = new QuestionSheet(request, runtime);
                    coordinator.Show(content);
                    return (coordinator, content);
                });
                sheet = made.content;
                Thread.Sleep(900);
                return made.coordinator;
            }

            // 2. Nothing answered (Next is the default button), the focus left in the window: Return presses Next once, Escape closes it once.
            var coordinator = Badge(Request(3, false), out var sheet, out var closes);
            var pageBefore = "";
            var shownBefore = native.OnUi(() => coordinator.IsShown);
            EnsureForeground(window);
            var shownAfterFront = native.OnUi(() => coordinator.IsShown);
            pageBefore = Page(sheet);
            Rig.Press(Rig.Return);
            findings.Add($"incomplete, focus in the window (shown before front {shownBefore}, after {shownAfterFront}): Return {pageBefore} -> {Page(sheet)}, shown {native.OnUi(() => coordinator.IsShown)}");
            Rig.Press(Rig.Escape);
            findings.Add($"  Escape: shown {native.OnUi(() => coordinator.IsShown)}, closed callbacks {native.OnUi(() => closes[0])}");
            native.OnUi(() => coordinator.Close());
            Thread.Sleep(600);

            // 1. Every question has its recommended answer (the default button is Send), the field focused: Return advances (:239), it does not send.
            coordinator = Badge(Request(3, true), out sheet, out closes);
            var shown = native.OnUi(() => coordinator.IsShown);
            var field = native.OnUi(() => sheet.GetSelfAndVisualDescendants().OfType<TextBox>().First(box => box.IsEffectivelyVisible));
            native.OnUi(() => field.Focus());
            Thread.Sleep(300);
            var focus = native.OnUi(() => TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement()?.GetType().Name);
            EnsureForeground(window);
            pageBefore = Page(sheet);
            Rig.Press(Rig.Return);
            findings.Add($"complete+field focused ({focus}): Return {pageBefore} -> {Page(sheet)}, shown {native.OnUi(() => coordinator.IsShown)}, closed callbacks {native.OnUi(() => closes[0])}");
            // Escape in the field: closes once.
            Rig.Press(Rig.Escape);
            findings.Add($"  Escape: shown {native.OnUi(() => coordinator.IsShown)}, closed callbacks {native.OnUi(() => closes[0])}, transient {native.OnUi(() => WindowTransients.Newest(window) is not null)}");
            native.OnUi(() => coordinator.Close());
            Thread.Sleep(600);

            // 2b. Nothing answered, the field focused: Return advances once.
            coordinator = Badge(Request(3, false), out sheet, out closes);
            field = native.OnUi(() => sheet.GetSelfAndVisualDescendants().OfType<TextBox>().First(box => box.IsEffectivelyVisible));
            native.OnUi(() => field.Focus());
            Thread.Sleep(300);
            EnsureForeground(window);
            pageBefore = Page(sheet);
            Rig.Press(Rig.Return);
            findings.Add($"incomplete, field focused: Return {pageBefore} -> {Page(sheet)}");
            native.OnUi(() => coordinator.Close());
            Thread.Sleep(600);

            // 3. A link popover, then a badge popover over it: Escape closes the badge first, the second Escape the link.
            EnsureForeground(window);
            native.OnUi(() => LinkPopover.Shared.Show(Marrow.Core.Port.SwiftURL.Create("https://example.com")!, null, new Rect(300, 300, 10, 10), window));
            Thread.Sleep(900);
            var linkPopup = native.OnUi(() => WindowTransients.Newest(window) as Popup);
            coordinator = Badge(Request(1, false), out sheet, out closes);
            var newestIsBadge = native.OnUi(() => WindowTransients.Newest(window) is Popup p && !ReferenceEquals(p, linkPopup));
            EnsureForeground(window);
            Rig.Press(Rig.Escape);
            var afterFirst = (native.OnUi(() => coordinator.IsShown), native.OnUi(() => linkPopup?.IsOpen));
            Rig.Press(Rig.Escape);
            var afterSecond = (native.OnUi(() => coordinator.IsShown), native.OnUi(() => linkPopup?.IsOpen));
            findings.Add($"two popovers: newest is the badge {newestIsBadge}; Escape 1 (badge shown, link open) {afterFirst}; Escape 2 {afterSecond}");
            native.OnUi(() => { coordinator.Close(); LinkPopover.Shared.Close(); });

            Console.WriteLine("REVIEW-BADGE-KEYS " + string.Join(" || ", findings));
            Assert.True(shown, "The badge popover did not show. " + string.Join(" || ", findings));
        }
        finally { Rig.Delete(checkout.Root); }
    }, nameof(REVIEW_Badge_and_link_popovers_take_Return_and_Escape_once_with_the_field_focused_or_not), typeof(TimelineRowsNativeReviewBadgeKeysTests));
}
