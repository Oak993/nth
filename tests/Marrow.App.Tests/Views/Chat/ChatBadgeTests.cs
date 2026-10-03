using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/ChatBadge.swift (B2 1fbf81e), :22-200.
[Collection("Headless")]
public sealed class ChatBadgeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-CHAT-140")] // App/Views/Chat/ChatBadge.swift:22-52,76-80
    public void The_badge_uses_its_title_as_its_accessibility_label_and_click_toggles_presentation()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presented = false;
            var badge = new ChatBadge(
                "Plan",
                new Binding<bool>(() => presented, value => presented = value),
                () => new Border { Width = 18, Height = 18 },
                () => new Border());

            Assert.Equal("Plan", AutomationProperties.GetName(badge));
            var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(badge));
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(presented);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(presented);
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-140")] // App/Views/Chat/ChatBadge.swift:13-14,52-53
    public void A_disabled_badge_does_not_enable_its_button()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presented = false;
            var badge = new ChatBadge(
                "Plan",
                new Binding<bool>(() => presented, value => presented = value),
                () => new Border { Width = 18, Height = 18 },
                () => new Border(),
                isEnabled: false);

            var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(badge));
            Assert.False(button.IsEnabled);
            Assert.False(presented);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-062")] // App/Views/Chat/ChatBadge.swift:54-65
    public void The_attention_dot_layer_is_present_only_when_attention_is_needed()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presented = false;
            ChatBadge MakeBadge(bool needsAttention) => new(
                "Plan",
                new Binding<bool>(() => presented, value => presented = value),
                () => new Border { Width = 18, Height = 18 },
                () => new Border(),
                needsAttention: needsAttention);

            var attentive = MakeBadge(true);
            var ordinary = MakeBadge(false);

            Assert.NotEmpty(LogicalQuery.SelfAndDescendants<DotFieldControl>(attentive));
            Assert.Empty(LogicalQuery.SelfAndDescendants<DotFieldControl>(ordinary));
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-141")] // App/Views/Chat/ChatBadge.swift:113-152
    public void A_badge_popover_is_fixed_at_its_measured_size_and_unmeasurable_content_uses_the_fallback()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var window = new AvaloniaWindow { Content = new Border() };
            window.Show();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);

            var coordinator = new BadgePopoverCoordinator();
            coordinator.SetAnchor(new Rect(10, 20, 30, 40));
            var content = new Border { Width = 123, Height = 77 };
            coordinator.Show(content);

            var popup = Assert.Single(presenter.Calls).Popup;
            var fixedFrame = Assert.IsType<Border>(PopoverHost.Content(popup)); // the site's frame, not the card the presenter hangs it on (OD-0095)
            Assert.Equal(123, fixedFrame.Width);
            Assert.Equal(77, fixedFrame.Height);

            content.Width = 300;
            content.Height = 200;
            Assert.Equal(123, fixedFrame.Width);
            Assert.Equal(77, fixedFrame.Height);

            coordinator.Close();

            var fallback = new BadgePopoverCoordinator();
            fallback.SetAnchor(new Rect(10, 20, 30, 40));
            fallback.Show(new Border { Width = 0, Height = 0 });

            var fallbackPopup = Assert.Single(presenter.Calls.Where(call => call.Presented).Skip(1)).Popup;
            var fallbackFrame = Assert.IsType<Border>(PopoverHost.Content(fallbackPopup));
            Assert.Equal(460, fallbackFrame.Width);
            Assert.Equal(320, fallbackFrame.Height);
            window.Close();
        });
    }

    // :145 NSHostingView(rootView: content).fittingSize, frozen at :147: the size of a hosted view, with its styles and templates
    // in effect. The question sheet's footer buttons are templated controls, which a detached sheet measured 16 pt short (246
    // for 262 with one question).
    [Fact]
    [Trait("Req", "PR-CHAT-141")] // App/Views/Chat/ChatBadge.swift:145-147
    public void A_badge_popover_is_exactly_as_big_as_its_content_measured_in_a_window()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var question = new QuestionRequest.Question("q", "Scope", "Which approach should I take for the migration?",
                new ValueList<QuestionRequest.Choice>([new("Rewrite it (Recommended)", "Cleaner but slower"), new("Patch it"), new("Skip")]),
                false, false, false);
            var request = new QuestionRequest("request", new ValueList<QuestionRequest.Question>([question]));

            var hosted = new QuestionSheet(request, null!);
            var hostWindow = new AvaloniaWindow { Width = 640, Height = 600, Content = hosted };
            hostWindow.Show();
            Dispatcher.UIThread.RunJobs();
            hosted.Measure(Size.Infinity);
            var expected = hosted.DesiredSize;
            hostWindow.Close();

            var window = new AvaloniaWindow { Content = new Border() };
            window.Show();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
            var coordinator = new BadgePopoverCoordinator();
            coordinator.SetAnchor(new Rect(10, 20, 30, 40));

            coordinator.Show(new QuestionSheet(request, null!));

            var frame = Assert.IsType<Border>(PopoverHost.Content(Assert.Single(presenter.Calls).Popup));
            Assert.Equal(expected.Width, frame.Width);
            Assert.Equal(expected.Height, frame.Height);
            coordinator.Close();
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-141")] // App/Views/Chat/ChatBadge.swift:138-152
    [Trait("Req", "PR-CHAT-142")] // App/Views/Chat/ChatBadge.swift:154-167
    public void Showing_without_an_anchor_does_nothing_and_close_callback_fires_once_after_close()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);

            var unanchored = new BadgePopoverCoordinator();
            unanchored.Show(new Border { Width = 100, Height = 50 });
            Assert.Empty(presenter.Calls);

            var window = new AvaloniaWindow { Content = new Border() };
            window.Show();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
            var coordinator = new BadgePopoverCoordinator();
            coordinator.SetAnchor(new Rect(10, 20, 30, 40));
            var closes = 0;
            coordinator.OnClose = () => closes++;
            coordinator.Show(new Border { Width = 100, Height = 50 });

            coordinator.Close();
            Assert.Equal(0, closes);
            coordinator.PopoverDidClose();
            coordinator.PopoverDidClose();
            Assert.Equal(1, closes);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-142")] // App/Views/Chat/ChatBadge.swift:189-194
    public void A_click_inside_the_badges_rect_leaves_the_popover_open_and_a_click_outside_it_closes_the_popover()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var window = new AvaloniaWindow { Content = new Border(), Width = 400, Height = 300 };
            window.Show();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);

            var coordinator = new BadgePopoverCoordinator();
            coordinator.SetAnchor(new Rect(10, 20, 30, 40)); // window coordinates, matching the badge's own rect
            coordinator.Show(new Border { Width = 100, Height = 50 });
            Assert.True(coordinator.IsShown);

            // :191 a click inside the badge's recorded rect passes through: the popover stays open.
            window.MouseDown(new Point(20, 30), MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(new Point(20, 30), MouseButton.Left, RawInputModifiers.None);
            Assert.True(coordinator.IsShown);

            // :192 anything else in that window closes the popover and still passes the event on.
            window.MouseDown(new Point(200, 200), MouseButton.Left, RawInputModifiers.None);
            Assert.False(coordinator.IsShown);
            window.MouseUp(new Point(200, 200), MouseButton.Left, RawInputModifiers.None);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-142")] // App/Views/Chat/ChatBadge.swift:169-194
    public void Escape_in_the_chat_window_closes_the_popover_but_escape_in_another_window_does_not()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var chatWindow = new AvaloniaWindow { Content = new Border() };
            var otherWindow = new AvaloniaWindow { Content = new Border() };
            chatWindow.Show();
            otherWindow.Show();
            _ = AppCurrentEvent.CurrentWindow;
            chatWindow.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);

            var coordinator = new BadgePopoverCoordinator();
            coordinator.SetAnchor(new Rect(10, 20, 30, 40));
            coordinator.Show(new Border { Width = 100, Height = 50 });
            var popup = Assert.Single(presenter.Calls).Popup;

            otherWindow.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });
            Assert.True(coordinator.IsShown);

            chatWindow.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });
            Assert.False(coordinator.IsShown);
            Assert.Contains(presenter.Calls, call => ReferenceEquals(call.Popup, popup) && !call.Presented);
            chatWindow.Close();
            otherWindow.Close();
        });
    }

    // Review fix (HIGH): ChatBadge.swift:66,76-77 applies `.windowRectAnchor` to the Button BEFORE the outer
    // `.frame(maxWidth: .infinity, alignment: .leading)` at :76, so the reported window rect is the button's own
    // natural/compact pill size (:45 "The whole pill takes the click, padding included"), never stretched to the
    // row's full width. `button.HorizontalAlignment` must stay Left (not Stretch) so both the anchor rect
    // BadgePopoverCoordinator uses for placement and outside-click detection, and the button's own arranged
    // bounds, match the compact pill instead of the full available width.
    [Fact]
    [Trait("Req", "PR-CHAT-140")] // App/Views/Chat/ChatBadge.swift:66,76-77
    [Trait("Req", "PR-CHAT-142")] // App/Views/Chat/ChatBadge.swift:189-194
    public void The_windowRectAnchor_rect_is_the_compact_pill_not_the_full_stretched_row()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presented = false;
            var badge = new ChatBadge(
                "Plan",
                new Binding<bool>(() => presented, value => presented = value),
                () => new Border { Width = 18, Height = 18 },
                () => new Border());

            var aligned = Assert.IsType<Border>(badge.Child); // :76 the outer frame
            var button = Assert.IsType<Button>(aligned.Child); // :23-65 the pill button, pre-:76
            var layers = (BadgeLayers)button.Content!;

            Rect? reported = null;
            button.WindowRectAnchor(rect => reported = rect); // the same call the constructor makes, observed independently

            // Plenty of window width so a wrongly-stretched button would report a much wider rect than the pill.
            var window = new AvaloniaWindow { Width = 400, Height = 200, Content = badge };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Assert.True(reported is { Width: > 0 });
            Assert.Equal(layers.DesiredSize.Width, reported!.Value.Width); // the pill's own natural width
            Assert.True(reported.Value.Width < 200, "the reported rect must not be stretched to the row's ~304pt available width");
            Assert.Equal(layers.DesiredSize.Width, button.Bounds.Width); // button itself stays compact, matching the anchor
            window.Close();
        });
    }

    // Review finding rejected (MEDIUM): the claim that setting AutomationProperties.Name/ItemStatus on both
    // `button` (ChatBadge.cs:125-126) and `this` (ChatBadge.cs:127-128) "typically forces" the outer Decorator to
    // become its own control element is not what Avalonia 12.1.2 actually does. ChatBadge's own automation peer
    // (the default NoneAutomationPeer for a plain Control/Decorator) reports IsControlElement() == false whether
    // or not AutomationProperties.Name is set on it, so it is never surfaced as a second accessible node; the one
    // control element a screen reader walks to is the button's own ButtonAutomationPeer, matching the single
    // collapsed element ChatBadge.swift:78-79 produces for the whole modifier chain.
    [Fact]
    [Trait("Req", "PR-CHAT-140")] // App/Views/Chat/ChatBadge.swift:78-79
    public void The_outer_badge_is_not_exposed_as_a_second_accessible_control_element()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presented = false;
            var badge = new ChatBadge(
                "Plan",
                new Binding<bool>(() => presented, value => presented = value),
                () => new Border { Width = 18, Height = 18 },
                () => new Border(),
                needsAttention: true);

            var aligned = Assert.IsType<Border>(badge.Child);
            var button = Assert.IsType<Button>(aligned.Child);

            var badgePeer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(badge);
            var buttonPeer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(button);

            Assert.Equal("Plan", AutomationProperties.GetName(badge)); // ChatBadge.cs:127, harmless metadata
            Assert.False(badgePeer.IsControlElement()); // ...but never surfaced as a control element itself
            Assert.True(buttonPeer.IsControlElement());
            Assert.Equal("Plan", buttonPeer.GetName()); // the one exposed element carries the label
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-062")] // App/Views/Chat/ChatBadge.swift:55-62 reduceMotion only sets animated: false; the field stays rendered
    public void With_reduce_motion_the_attention_dot_field_stays_visible_and_static()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presented = false;
            var badge = new ChatBadge(
                "Plan",
                new Binding<bool>(() => presented, value => presented = value),
                () => new Border { Width = 18, Height = 18 },
                () => new Border(),
                needsAttention: true);
            var host = new Border { Child = badge };
            EnvironmentValues.SetAccessibilityReduceMotion(host, true);
            var window = new AvaloniaWindow { Content = host, Width = 200, Height = 100 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<DotFieldControl>(badge));
            Assert.True(field.IsVisible);
            window.Close();
        });
    }

    // Review round 1 (2026-10-03): ChatBadge.Update is the one way a host (PlanCard, TimelineRows.swift:2263-2316, PR-CHAT-113)
    // hands a badge that stays in the tree the values its body has now. Everything the badge's body derives from them
    // follows: the label (:23-53), the pill's availability (:53 .disabled(!isEnabled)), the accessibility label and value
    // (:78-79), the attention dot field (:54-65): the clauses of PR-CHAT-140, for the values a host gave later.
    [Fact]
    [Trait("Req", "PR-CHAT-140")] // ChatBadge.swift:23-65, :53, :78-79; TimelineRows.swift:2263-2316
    public void A_badge_given_new_values_by_its_host_shows_them_in_its_label_availability_name_and_status()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presented = false;
            var badge = new ChatBadge("Writing a plan", new Binding<bool>(() => presented, value => presented = value),
                () => new Border { Width = 18, Height = 18 }, () => new Border(), isEnabled: false);
            var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(badge));
            string[] Words() => LogicalQuery.SelfAndDescendants<TextBlock>(badge).Select(text => text.Text ?? "").ToArray();
            Assert.False(button.IsEnabled);
            Assert.Equal("", AutomationProperties.GetItemStatus(button));
            Assert.Contains("Writing a plan", Words());
            Assert.Empty(LogicalQuery.SelfAndDescendants<DotFieldControl>(badge));

            badge.Update("Plan ready", () => new Border { Width = 18, Height = 18 }, () => new Border(), caption: "Implement or dismiss",
                needsAttention: true, isEnabled: true);
            Assert.Same(button, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(badge))); // the same pill, not a new one
            Assert.True(button.IsEnabled);
            Assert.Equal("Plan ready", AutomationProperties.GetName(button));
            Assert.Equal("Plan ready", AutomationProperties.GetName(badge));
            Assert.Equal("Needs your attention", AutomationProperties.GetItemStatus(button));
            Assert.Equal("Needs your attention", AutomationProperties.GetItemStatus(badge));
            Assert.Contains("Plan ready", Words());
            Assert.Contains("Implement or dismiss", Words());
            Assert.DoesNotContain("Writing a plan", Words());
            Assert.NotEmpty(LogicalQuery.SelfAndDescendants<DotFieldControl>(badge));

            badge.Update("Plan", () => new Border { Width = 18, Height = 18 }, () => new Border(), isEnabled: false);
            Assert.False(button.IsEnabled);
            Assert.Equal("Plan", AutomationProperties.GetName(button));
            Assert.Equal("", AutomationProperties.GetItemStatus(button));
            Assert.Equal("", AutomationProperties.GetItemStatus(badge));
            Assert.Empty(LogicalQuery.SelfAndDescendants<DotFieldControl>(badge));
        });
    }

    // ChatBadge.swift:75 .onDisappear { coordinator.close() }: SwiftUI fires it for a view that left the hierarchy, never for
    // one that stayed (PR-CHAT-142 cancellation: the popover goes "when the view disappears"). ThreadTimeline.Draw takes
    // each kept row out of its stack and puts it into a new one inside a single pass, so a badge is detached and attached
    // again with nothing having disappeared; only a badge that is still out of the tree after the pass has.
    [Fact]
    [Trait("Req", "PR-CHAT-142")] // ChatBadge.swift:74-75; ThreadTimeline.swift:384-391
    public void A_badge_that_is_moved_keeps_its_open_popover_and_one_that_has_left_the_tree_closes_it()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var flag = new PresentedFlag();
            var coordinator = new BadgePopoverCoordinator();
            var badge = new ChatBadge("Plan", new Binding<bool>(() => flag.Value, value => flag.Value = value),
                () => new Border { Width = 18, Height = 18 }, () => new Border { Width = 100, Height = 50 }, coordinator: coordinator);
            var before = new StackPanel { Children = { badge } };
            var after = new StackPanel();
            var window = new AvaloniaWindow { Content = new StackPanel { Children = { before, after } }, Width = 400, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);

            runtime.OnMain(() => Assert.Single(LogicalQuery.SelfAndDescendants<Button>(badge)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))); // the flag is main-actor state; its Track runs on the main loop too
            Dispatcher.UIThread.RunJobs();
            Assert.True(coordinator.IsShown);
            var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;

            // Taken out of one stack and put into another in the same pass: the badge never disappeared.
            before.Children.Remove(badge);
            after.Children.Add(badge);
            window.UpdateLayout();
            runtime.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.True(coordinator.IsShown, "the open popover closed when its badge was only moved");
            Assert.True(presenter.Calls.Last(call => ReferenceEquals(call.Popup, popup)).Presented);

            // Out of the tree for good: the popover goes with the badge.
            after.Children.Remove(badge);
            Dispatcher.UIThread.RunJobs();
            Assert.False(coordinator.IsShown);
            Assert.False(presenter.Calls.Last(call => ReferenceEquals(call.Popup, popup)).Presented);
            window.Close();
        });
    }

    // :20 the coordinator is the badge's @State: a host whose identity outlives the control (PlanCard) rebuilds the control
    // over the coordinator it keeps, and the popover that is open is still the one popover, with the new badge now the view
    // it hangs from. The old badge leaving does not close it; the new one leaving does.
    [Fact]
    [Trait("Req", "PR-CHAT-142")] // ChatBadge.swift:20, :74-75
    public void A_popover_open_over_a_coordinator_outlives_the_badge_that_opened_it_while_another_badge_is_over_that_coordinator()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var flag = new PresentedFlag();
            var coordinator = new BadgePopoverCoordinator();
            ChatBadge Make() => new("Plan", new Binding<bool>(() => flag.Value, value => flag.Value = value),
                () => new Border { Width = 18, Height = 18 }, () => new Border { Width = 100, Height = 50 }, coordinator: coordinator);
            var first = Make();
            var host = new StackPanel { Children = { first } };
            var window = new AvaloniaWindow { Content = host, Width = 400, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
            runtime.OnMain(() => Assert.Single(LogicalQuery.SelfAndDescendants<Button>(first)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))); // the flag is main-actor state; its Track runs on the main loop too
            Dispatcher.UIThread.RunJobs();
            Assert.True(coordinator.IsShown);
            var shown = presenter.Calls.Count(call => call.Presented);

            // The host builds the badge again (a new control for the same identity) and drops the old one.
            var second = Make();
            host.Children.Clear();
            host.Children.Add(second);
            window.UpdateLayout();
            runtime.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.True(coordinator.IsShown, "the open popover closed when the host replaced its badge");
            Assert.Equal(shown, presenter.Calls.Count(call => call.Presented)); // and was not shown a second time
            Assert.True(flag.Value);

            // The replacement leaves: now the popover goes.
            host.Children.Clear();
            Dispatcher.UIThread.RunJobs();
            Assert.False(coordinator.IsShown);
            window.Close();
        });
    }

    // ChatBadge.swift:93-111 badgePopover(isPresented:detail:), the modifier the Hydra report, heads and brief pills use
    // (TimelineRows.swift:293, :470, :583): its own `.onDisappear { coordinator.close() }` (:109) is the badge's (:75), so a
    // view that is only moved (ThreadTimeline.Draw takes each kept row out of its stack and puts it into a new one in one
    // pass) keeps its open popover, and one that has left the tree for good closes it.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatBadge.swift:93-111, :109; PR-CHAT-143's pill (the timeline half is TimelineRowsParityReview2Tests); ED-256
    public void A_badgePopover_pill_that_is_moved_keeps_its_open_popover_and_one_that_has_left_the_tree_closes_it()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var flag = new PresentedFlag();
            var pill = new Button { Content = "Report" }.BadgePopover(new Binding<bool>(() => flag.Value, value => flag.Value = value),
                () => new Border { Width = 100, Height = 50 });
            var before = new StackPanel { Children = { pill } };
            var after = new StackPanel();
            var window = new AvaloniaWindow { Content = new StackPanel { Children = { before, after } }, Width = 400, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);

            runtime.OnMain(() => flag.Value = true); // the reader opens the report
            Dispatcher.UIThread.RunJobs();
            var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
            var content = popup.Child;
            Assert.NotNull(content);

            // Taken out of one stack and put into another in the same pass: the pill never disappeared.
            before.Children.Remove(pill);
            after.Children.Add(pill);
            window.UpdateLayout();
            runtime.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Assert.True(presenter.Calls.Last(call => ReferenceEquals(call.Popup, popup)).Presented, "the open popover closed when its pill was only moved");
            Assert.Single(presenter.Calls, call => call.Presented); // and was not shown a second time
            Assert.Same(content, popup.Child);
            Assert.True(flag.Value);

            // Out of the tree for good: the popover goes with the pill.
            after.Children.Remove(pill);
            Dispatcher.UIThread.RunJobs();
            Assert.False(presenter.Calls.Last(call => ReferenceEquals(call.Popup, popup)).Presented);
            window.Close();
        });
    }

    // The popover stays whole while it closes (the closing animation draws it) and lets go only once it has closed; the next opening
    // sets both again.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatBadge.swift:156-167; ED-256
    public void A_popover_keeps_its_content_while_it_closes_lets_go_when_it_has_closed_and_opens_again()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var window = new AvaloniaWindow { Content = new Border(), Width = 400, Height = 300 };
            window.Show();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
            var coordinator = new BadgePopoverCoordinator();
            coordinator.SetAnchor(new Rect(10, 20, 30, 40));
            coordinator.Show(new Border { Width = 100, Height = 50 });
            var popup = Assert.Single(presenter.Calls).Popup;
            Assert.NotNull(popup.PlacementTarget);
            Assert.NotNull(popup.Child);

            coordinator.Close(); // performClose: the popover is going, not gone
            Assert.NotNull(popup.PlacementTarget);
            Assert.NotNull(popup.Child);
            coordinator.PopoverDidClose();
            Assert.Null(popup.PlacementTarget);
            Assert.Null(popup.Child);

            coordinator.Show(new Border { Width = 120, Height = 60 });
            Assert.True(coordinator.IsShown);
            Assert.NotNull(popup.PlacementTarget);
            Assert.Equal(120, Assert.IsType<Border>(PopoverHost.Content(popup)).Width);
            coordinator.Close();
            window.Close();
        });
    }

    // :119 `private weak var shownIn`: a coordinator a host keeps for longer than the badge (PlanCard's identity lives as long as the
    // thread keeps the plan's entry) must not keep the window it was shown in alive. A closed headless window cannot be observed
    // going (the platform keeps some of them reachable, measured 2026-10-03), so the held references are read instead: no field of the
    // coordinator is a window while the popover is open and none after it has closed.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatBadge.swift:119; ED-256
    public void A_coordinator_holds_the_window_it_was_shown_in_only_weakly()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var presenter = new RecordingPresenter();
            using var presenterScope = Presentation.Presenter.Override(presenter);
            var window = new AvaloniaWindow { Content = new Border(), Width = 400, Height = 300 };
            window.Show();
            _ = AppCurrentEvent.CurrentWindow;
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null);
            var coordinator = new BadgePopoverCoordinator();
            coordinator.SetAnchor(new Rect(10, 20, 30, 40));
            coordinator.Show(new Border { Width = 100, Height = 50 });
            Assert.True(coordinator.IsShown);

            IEnumerable<object?> Held() => typeof(BadgePopoverCoordinator)
                .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Select(field => field.GetValue(coordinator));
            Assert.DoesNotContain(Held(), held => held is TopLevel);
            coordinator.Close();
            coordinator.PopoverDidClose();
            Assert.DoesNotContain(Held(), held => held is TopLevel);
            window.Close();
        });
    }
}
