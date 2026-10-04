using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;
using Rig = Marrow.App.Tests.Window.Native.TimelineRowsNativeRig;

namespace Marrow.App.Tests.Window.Native;

// REVIEW (rec-tlr, second round): PopoverHost.ForwardKey hands Escape and Return from the window a popover hangs over to the popover. It is reached only when the key arrives at that window.
// A popover that takes the keyboard focus into itself (the rename field, the archive button) has the key delivered inside its own popup window instead. These are real keys in a real
// window on the popovers the unit names, the rename and archive popovers of the sidebar, presented the way PlainControls.Popover presents them (Presentation.SetPresented).
[Collection("WindowManagerNative")]
public sealed class TimelineRowsNativeReviewKeysTests(WindowManagerNativeSession native)
{
    private const string RevertTip = "Revert this turn's files and conversation";

    private void EnsureForeground(AvaloniaWindow window)
    {
        var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
        if (N.GetForegroundWindow() != handle) Rig.Front(native, window);
        if (N.GetForegroundWindow() != handle) throw new InvalidOperationException("NATIVE_PRECONDITION: the main window is not in front; no key was sent.");
    }

    private Popup Present(Panel panel, Control anchor, Control content, Action<Popup> onDismiss) => native.OnUi(() =>
    {
        var popup = new Popup { Placement = PlacementMode.Bottom, PlacementTarget = anchor, IsLightDismissEnabled = true, Child = content };
        panel.Children.Add(popup);
        Presentation.SetPresented(popup, true, anchor, () => { onDismiss(popup); Presentation.SetPresented(popup, false, anchor, () => { }); });
        return popup;
    });

    private void Close(Panel panel, Control anchor, Popup popup) => native.OnUi(() =>
    {
        if (popup.IsOpen) Presentation.SetPresented(popup, false, anchor, () => { });
        panel.Children.Remove(popup);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void REVIEW_Escape_and_Return_reach_the_rename_and_archive_popovers_once_each() => native.Run(subject =>
    {
        var checkout = Rig.MakeCheckout("review-keys");
        try
        {
            var (thread, _, _) = Rig.Seed(native, checkout);
            var (window, model) = Rig.Open(native, subject, thread);
            N.Until(() => Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == RevertTip).Count == 2, "The thread's two revert discs never appeared.", 30000);
            var anchor = Rig.Find<Button>(native, window, button => ToolTip.GetTip(button) as string == RevertTip).Last();
            var panel = native.OnUi(() => (Panel)anchor.Parent!);
            var findings = new List<string>();

            // 1. The rename popover with its field focused: Escape.
            var renamed = new List<string>(); var cancelled = 0; var dismissed = 0;
            var popup = Present(panel, anchor, native.OnUi(() => new Marrow.App.Views.Sidebar.RenameThreadPopover("Original", title => renamed.Add(title), () => cancelled++)), _ => dismissed++);
            N.Until(() => native.OnUi(() => popup.IsOpen), "The rename popover did not open.", 5000);
            Thread.Sleep(800);
            var focused = native.OnUi(() => TopLevel.GetTopLevel(anchor)?.FocusManager?.GetFocusedElement()?.GetType().Name);
            EnsureForeground(window);
            Rig.Press(Rig.Escape);
            var closedByEscape = !native.OnUi(() => popup.IsOpen);
            findings.Add($"rename: focus {focused}; Escape closed it {closedByEscape}; dismissed {native.OnUi(() => dismissed)}, cancelled {native.OnUi(() => cancelled)}");
            Close(panel, anchor, popup);

            // 2. The rename popover: Return submits the typed title once.
            popup = Present(panel, anchor, native.OnUi(() => new Marrow.App.Views.Sidebar.RenameThreadPopover("Original", title => renamed.Add(title), () => cancelled++)), _ => dismissed++);
            N.Until(() => native.OnUi(() => popup.IsOpen), "The rename popover did not open the second time.", 5000);
            Thread.Sleep(800);
            EnsureForeground(window);
            Rig.Press(Rig.Return);
            findings.Add($"rename: Return submitted {native.OnUi(() => renamed.Count)} times [{string.Join("|", native.OnUi(() => renamed.ToList()))}]");
            Close(panel, anchor, popup);

            // 3. The archive popover (its own window key handler, and the default button the forwarding now presses): Return, then Escape.
            var chat = native.OnUi(() => model.Thread(thread)!);
            var archived = 0; var archiveDismissed = 0; var who = new List<string>();
            static string Via(string what) { var trace = Environment.StackTrace; return what + ":" + (trace.Contains("ForwardKey", StringComparison.Ordinal) ? "ForwardKey" : trace.Contains("ArchiveThreadPopover.OnWindowKeyDown", StringComparison.Ordinal) ? "ArchiveOwnHandler" : "other"); }
            popup = Present(panel, anchor, native.OnUi(() => new Marrow.App.Views.Sidebar.ArchiveThreadPopover(chat, anchor, () => { archived++; who.Add(Via("archive")); }, () => { }, () => { archiveDismissed++; who.Add(Via("onDismiss")); })), _ => { archiveDismissed++; who.Add(Via("registeredDismiss")); });
            N.Until(() => native.OnUi(() => popup.IsOpen), "The archive popover did not open.", 5000);
            Thread.Sleep(800);
            EnsureForeground(window);
            Rig.Press(Rig.Return);
            findings.Add($"archive: Return archived {native.OnUi(() => archived)} times via [{string.Join(",", native.OnUi(() => who.ToList()))}]");
            Close(panel, anchor, popup);
            popup = Present(panel, anchor, native.OnUi(() => new Marrow.App.Views.Sidebar.ArchiveThreadPopover(chat, anchor, () => { archived++; who.Add(Via("archive")); }, () => { }, () => { archiveDismissed++; who.Add(Via("onDismiss")); })), _ => { archiveDismissed++; who.Add(Via("registeredDismiss")); });
            N.Until(() => native.OnUi(() => popup.IsOpen), "The archive popover did not open the second time.", 5000);
            Thread.Sleep(800);
            EnsureForeground(window);
            var before = native.OnUi(() => archiveDismissed);
            Rig.Press(Rig.Escape);
            findings.Add($"archive: Escape dismissed {native.OnUi(() => archiveDismissed) - before} times, popover still open {native.OnUi(() => popup.IsOpen)}, via [{string.Join(",", native.OnUi(() => who.ToList()))}]");
            Close(panel, anchor, popup);

            Console.WriteLine("REVIEW-KEYS " + string.Join(" || ", findings));
            Assert.True(closedByEscape, "Escape did not close the rename popover whose field has the focus. " + string.Join(" || ", findings));
            Assert.Equal(1, native.OnUi(() => renamed.Count));
            Assert.Equal(1, native.OnUi(() => archived));
        }
        finally { Rig.Delete(checkout.Root); }
    }, nameof(REVIEW_Escape_and_Return_reach_the_rename_and_archive_popovers_once_each), typeof(TimelineRowsNativeReviewKeysTests));
}
