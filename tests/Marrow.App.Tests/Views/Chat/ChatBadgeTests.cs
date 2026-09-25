using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
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
            var fixedFrame = Assert.IsType<Border>(popup.Child);
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
            var fallbackFrame = Assert.IsType<Border>(fallbackPopup.Child);
            Assert.Equal(460, fallbackFrame.Width);
            Assert.Equal(320, fallbackFrame.Height);
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
}
