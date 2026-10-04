using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// PR-CHAT-110, re-review round: a key a popover owns acts exactly once at every site that had its own handler for it before PopoverHost.ForwardKey: the sidebar's archive popover (its
// own window monitor, :946-955), LinkPopover's Escape monitor, the badge popovers' Escape monitor (QuestionSheet, the merge popover), the rename popover, the revert confirmation and
// the Hydra intro. THE RULE (PopoverHost.Listen): the host marks the key it forwards handled, and a site's own window-level monitor ignores a handled key; whichever handler the
// window offers the key to first acts, the other stands aside. Headless cannot open a Popup, so the popup is registered the way Presentation.SetPresented registers it and the host
// is told it is open. The archive popover is also read natively with real keys (TimelineRowsNativeReviewKeysTests).
[Collection("Headless")]
public sealed class PopoverHostOnceOnlyKeyTests(HeadlessSession headless)
{
    private static void Press(AvaloniaWindow window, Key key, PhysicalKey physical) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, PhysicalKey = physical, KeyModifiers = KeyModifiers.None });

    private static void Return(AvaloniaWindow window) => Press(window, Key.Enter, PhysicalKey.Enter);

    private static void Escape(AvaloniaWindow window) => Press(window, Key.Escape, PhysicalKey.Escape);

    // A popup as a site presents it (Presentation.SetPresented: registered as a transient over the window, dismissed by `dismiss`), told to the host as open.
    private static (Popup Popup, Action Stop) Present(AvaloniaWindow window, Control anchor, Control content, Action dismiss)
    {
        var popup = new Popup { PlacementTarget = anchor, IsLightDismissEnabled = true, Child = content };
        Presentation.SetPresented(popup, true, anchor, dismiss);
        return (popup, PopoverHost.Listen(popup, window, () => true));
    }

    [Theory]
    [InlineData(true)]  // the host listens before the popover's own monitor is installed
    [InlineData(false)] // the popover's monitor first
    [Trait("Req", "PR-CHAT-110")] // SidebarView.swift:932 `.keyboardShortcut(.defaultAction)`, :946-955 the local monitor: Return archives, Escape dismisses
    public void The_archive_popover_archives_once_on_Return_and_dismisses_once_on_Escape(bool hostFirst) => headless.OnUiThread(() =>
    {
        using var scope = Presentation.Presenter.Override(new RecordingPresenter());
        using var defaults = UserDefaults.Standard.Override(new Marrow.Testing.InMemorySettingsStore());
        var anchor = new Button { Content = "row" };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        var thread = AppModelSamples.Thread(AppModelSamples.Project("P", @"C:rchive-once"));
        int archived = 0, dismissed = 0;
        var content = new ArchiveThreadPopover(thread, anchor, () => archived++, () => { }, () => dismissed++);
        // Headless cannot attach a popup's content to a tree, so the popover's onAppear (which installs its window monitor, :944-955) is run as the tree would run it.
        (Popup Popup, Action Stop) presented = (null!, () => { });
        if (hostFirst) presented = Present(window, anchor, content, () => dismissed++);
        Invoke(content, "Appear");
        if (!hostFirst) presented = Present(window, anchor, content, () => dismissed++);
        try
        {
            Return(window);
            Assert.Equal(1, archived);
            Escape(window);
            Assert.Equal(1, dismissed);
        }
        finally { presented.Stop(); WindowTransients.Hide(presented.Popup); window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // SidebarView.swift:1016 `.keyboardShortcut(.defaultAction)` on Rename; :1009 onSubmit
    public void The_rename_popover_submits_once_on_Return() => headless.OnUiThread(() =>
    {
        using var scope = Presentation.Presenter.Override(new RecordingPresenter());
        using var defaults = UserDefaults.Standard.Override(new Marrow.Testing.InMemorySettingsStore());
        var anchor = new Button { Content = "row" };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        var renamed = new List<string>();
        var content = new RenameThreadPopover("Original", title => renamed.Add(title), () => { });
        var (popup, stop) = Present(window, anchor, content, () => { });
        try
        {
            Return(window);
            Assert.Equal(["Original"], renamed);
        }
        finally { stop(); WindowTransients.Hide(popup); window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // TimelineRows.swift:2791 `.keyboardShortcut(.defaultAction)` on Revert
    public void The_revert_confirmation_reverts_once_on_Return_and_Escape_dismisses_it_once() => headless.OnUiThread(() =>
    {
        using var scope = Presentation.Presenter.Override(new RecordingPresenter());
        var anchor = new Button { Content = "disc" };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        var reverted = 0; var dismissed = 0;
        var card = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("a.txt", 1, 0, false)]), 1, 0), canUndo: true, onRevert: () => reverted++, onReview: _ => { });
        var (popup, stop) = Present(window, anchor, Assert.IsAssignableFrom<Control>(Invoke(card, "RevertConfirmation")), () => dismissed++);
        try
        {
            Return(window);
            Assert.Equal(1, reverted);
            Escape(window);
            Assert.Equal(1, dismissed);
        }
        finally { stop(); WindowTransients.Hide(popup); window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // HydraIntroPopover.swift:70 `.keyboardShortcut(.defaultAction)` on Got it
    public void The_Hydra_intro_is_dismissed_once_on_Return() => headless.OnUiThread(() =>
    {
        using var scope = Presentation.Presenter.Override(new RecordingPresenter());
        var anchor = new Button { Content = "mark" };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        var gotIt = 0;
        var (popup, stop) = Present(window, anchor, new HydraIntroPopover(() => gotIt++), () => { });
        try
        {
            Return(window);
            Assert.Equal(1, gotIt);
        }
        finally { stop(); WindowTransients.Hide(popup); window.Close(); }
    });

    // The badge popovers open through BadgePopoverCoordinator: QuestionSheet's Send / Next (QuestionTab.swift:148,158) and the merge popover's Open (HydraMergePopover.swift:214) are
    // `.keyboardShortcut(.defaultAction)`, and Escape closes them (ChatBadge.swift:169-179, the coordinator's own monitor).
    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void A_badge_popover_is_a_registered_transient_so_Return_presses_its_default_once_and_Escape_closes_it_once() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var scope = Presentation.Presenter.Override(presenter);
        var window = new AvaloniaWindow { Content = new Border(), Width = 400, Height = 300 };
        window.Show();
        _ = AppCurrentEvent.CurrentWindow;
        window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
        var coordinator = new BadgePopoverCoordinator();
        coordinator.SetAnchor(new Rect(10, 20, 30, 40));
        var closes = 0;
        coordinator.OnClose = () => closes++;
        var opened = 0;
        var open = new Button { Content = "Open", IsDefault = true };
        open.Click += (_, _) => opened++;
        coordinator.Show(new StackPanel { Children = { new Button { Content = "Copy link" }, open } });
        var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
        Assert.True(WindowTransients.IsShown(popup)); // registered the way Presentation.SetPresented registers a popover
        var stop = PopoverHost.Listen(popup, window, () => true);
        try
        {
            Return(window);
            Assert.Equal(1, opened);
            Assert.True(coordinator.IsShown);
            Escape(window);
            Assert.False(coordinator.IsShown);
            Assert.Single(presenter.Calls, call => !call.Presented); // closed once
            coordinator.PopoverDidClose();
            Assert.Equal(1, closes);
        }
        finally { stop(); window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")] // LinkPopover.swift:54-58 the Escape monitor
    public void The_link_popover_closes_once_on_Escape() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var scope = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 100, Height = 40 };
        var window = new AvaloniaWindow { Content = anchor };
        window.Show();
        LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/page")!, null, new Rect(0, 0, 50, 20), anchor);
        var popup = LinkPopover.Shared.PopoverForTests;
        var stop = PopoverHost.Listen(popup, window, () => true);
        try
        {
            Escape(window);
            Assert.Single(presenter.Calls, call => !call.Presented);
        }
        finally { stop(); LinkPopover.Shared.PopoverDidClose(); window.Close(); }
    });
}
