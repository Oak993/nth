using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.App.Port;
using Marrow.App.Runtime;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
public sealed class PopoverCoordinatorCloseRegressionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-063")]
    public void A_caption_press_dismisses_the_prompt_edit_and_its_popup_once() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current); using var presentation = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 40, Height = 30 };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = anchor };
        SettingsProviderPopupLayers.Install(window); window.Show(); Dispatcher.UIThread.RunJobs();
        var editor = new PromptEditorPopover<Border>(); editor.SetAnchor(anchor); var dismissed = 0;
        try
        {
            runtime.OnMain(() => editor.Sync(new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty), () => dismissed++, _ => new Border { Width = 200, Height = 100 }));
            runtime.RunUntilIdle(); Dispatcher.UIThread.RunJobs();
            var popup = Assert.Single(presenter.Popups); Assert.True(popup.IsOpen);
            runtime.Advance(TimeSpan.FromMilliseconds(300)); frames.Advance(TimeSpan.FromMilliseconds(300));
            runtime.OnMain(() => PopoverHost.DismissOutside(window));
            Assert.Equal(1, dismissed); Assert.True(popup.IsOpen);
            runtime.Advance(TimeSpan.FromMilliseconds(200)); frames.Advance(TimeSpan.FromMilliseconds(200)); Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen); Assert.Equal(1, dismissed);
        }
        finally { runtime.OnMain(editor.Close); window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_message_context_popover_closes_outside_through_its_animation() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current); using var presentation = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 100, Height = 40 }.RightClickPopover(() => MessageActionsSample.Make(false, []));
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = anchor };
        SettingsProviderPopupLayers.Install(window); window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            anchor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Apps });
            Dispatcher.UIThread.RunJobs();
            var popup = Assert.Single(presenter.Popups); Assert.True(popup.IsOpen);
            runtime.Advance(TimeSpan.FromMilliseconds(300)); frames.Advance(TimeSpan.FromMilliseconds(300));
            PopoverHost.DismissOutside(window);
            Assert.True(popup.IsOpen);
            runtime.Advance(TimeSpan.FromMilliseconds(200)); frames.Advance(TimeSpan.FromMilliseconds(200)); Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void The_popup_sheet_keeps_its_content_until_its_close_finishes() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current); using var presentation = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 40, Height = 30 };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = anchor };
        SettingsProviderPopupLayers.Install(window); window.Show(); Dispatcher.UIThread.RunJobs();
        Action dismiss = null!; var notifications = 0;
        try
        {
            ChatSheetPresentation.Show(anchor, close => { dismiss = close; return new Border { Width = 200, Height = 100 }; }, () => notifications++);
            var popup = Assert.Single(presenter.Popups); var content = popup.Child;
            runtime.Advance(TimeSpan.FromMilliseconds(300)); frames.Advance(TimeSpan.FromMilliseconds(300));
            dismiss(); Assert.True(popup.IsOpen); Assert.Same(content, popup.Child); Assert.Equal(0, notifications);
            runtime.Advance(TimeSpan.FromMilliseconds(200)); frames.Advance(TimeSpan.FromMilliseconds(200)); Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen); Assert.Null(popup.Child); Assert.Equal(1, notifications);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void Hiding_a_prompt_editor_owner_releases_the_application_escape_monitor() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var source = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current); using var presentation = Presentation.Presenter.Override(presenter);
        var anchor = new Border { Width = 40, Height = 30 };
        var window = new Avalonia.Controls.Window { Width = 800, Height = 600, Content = anchor };
        SettingsProviderPopupLayers.Install(window); window.Show(); Dispatcher.UIThread.RunJobs();
        var editor = new PromptEditorPopover<Border>(); editor.SetAnchor(anchor);
        try
        {
            runtime.OnMain(() => editor.Sync(new PromptEdit(Guid.NewGuid(), "hello", ValueList<Attachment>.Empty), () => { }, _ => new Border { Width = 200, Height = 100 }));
            runtime.RunUntilIdle(); Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.Single(presenter.Popups).IsOpen);
            window.Hide(); Dispatcher.UIThread.RunJobs();
            Assert.False(presenter.Popups.Single().IsOpen);
            foreach (var field in new[] { "monitorWindow", "applicationEscape" })
                Assert.Null(typeof(PromptEditorPopover<Border>).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor));
        }
        finally { runtime.OnMain(editor.Close); window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-107")]
    public void A_real_diff_popup_defers_close_notification_until_its_animation_finishes() => headless.OnUiThread(() =>
    {
        var real = Presentation.Presenter.Current;
        DiffPopoverRig.Run(rig =>
        {
            var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
            var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
            var presenter = new ReviewOverlayPresenter(real); using var presentation = Presentation.Presenter.Override(presenter);
            SettingsProviderPopupLayers.Install(rig.Window);
            rig.Window.ApplyTemplate(); rig.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); frames.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); }
            rig.OpenOnFirst(); Step(300);
            var popup = Assert.Single(presenter.Popups); var content = popup.Child; Assert.True(popup.IsOpen);
            rig.Coordinator.Close(); Assert.True(popup.IsOpen); Assert.Same(content, popup.Child);
            Step(80); Assert.True(popup.IsOpen); Assert.InRange(content!.Opacity, .1, .9);
            Step(100); Assert.False(popup.IsOpen); Assert.Null(popup.Child);
        });
    });
}
