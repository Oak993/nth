using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Port;

// PORT-ONLY: the presentation seam behind NSAlert.runModal() (AppUpdater.swift:274-280 is its only site so
// far). Mirrors UI.Port.ViewLifetime's Presentation.Presenter seam for popovers (R-VIEW-9): production shows
// a real modal window and the returned Task does not complete until it closes, matching runModal's block;
// a test overrides the Ambient and records instead, because Avalonia 12.1.2 headless cannot open a real
// Window any more than it can a Popup ("Unable to create IPopupImpl and no overlay layer is found").

/// <summary>One alert button: its title and the action a click performs before the alert closes.</summary>
public sealed record AlertButton(string Title, Action Perform)
{
    public AlertButtonRole Role { get; init; }
}

// ED-216: declarations for tests-first implementation of the shared dialog contract.
public enum AlertButtonRole { None, Cancel, Destructive }
public enum AlertKind { Alert, ConfirmationDialog }
public sealed record AlertTextField(string Label, Binding<string> Value);
public sealed record AlertRequest(string Title, string? Message, IReadOnlyList<AlertButton> Buttons,
    AlertKind Kind = AlertKind.Alert, IReadOnlyList<AlertTextField>? TextFields = null, Avalonia.Controls.Window? Owner = null)
{
    public AlertButton? ReturnDefault => throw new NotImplementedException("ED-216 declaration only");
}

internal sealed class AlertSession(AlertRequest request, Action close)
{
    public Task Completion => throw new NotImplementedException("ED-216 declaration only");
    public void Press(string title) { _ = request; _ = close; _ = title; throw new NotImplementedException("ED-216 declaration only"); }
    public void Cancel() => throw new NotImplementedException("ED-216 declaration only");
    public void Closed() => throw new NotImplementedException("ED-216 declaration only");
}

/// <summary>NSAlert.Style's one case this app ever sets (AppUpdater.swift:278).</summary>
public enum AlertStyle
{
    Warning,
}

public interface IAlertPresenter
{
    /// <summary>Presents a modal alert; the returned Task completes when the alert closes.</summary>
    Task Show(string title, string informativeText, AlertStyle style, IReadOnlyList<AlertButton> buttons);
}

public static class AlertPresentation
{
    public static Ambient<IAlertPresenter> Presenter { get; } = new(() => new WindowsAlertPresenter());
    internal static AlertRequest Prepare(AlertRequest request) { _ = request; throw new NotImplementedException("ED-216 declaration only"); }
}

/// <summary>The production presenter: a plain modal Avalonia window with the message and its buttons.</summary>
public sealed class WindowsAlertPresenter : IAlertPresenter
{
    internal static Avalonia.Controls.Window BuildWindow(AlertRequest request, AlertSession session)
    { _ = request; _ = session; throw new NotImplementedException("ED-216 declaration only"); }

    public Task Show(string title, string informativeText, AlertStyle style, IReadOnlyList<AlertButton> buttons)
    {
        _ = style; // The one style this app shows (Warning) carries no distinct visual treatment yet.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MainActor.Post(() =>
        {
            var window = new Avalonia.Controls.Window
            {
                Title = title,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            foreach (var button in buttons)
            {
                var control = new Button { Content = button.Title };
                // A lone button is both the alert's default (Return) and its cancel default (Escape): the shape
                // AppUpdater.cs's own single "OK" button (:410-414, App/AppUpdater.swift:274-280's implicit,
                // unadorned NSAlert default button) and RootView.cs's single "OK" button (PR-WIN-066,
                // `Button("OK", role: .cancel)`, "so Escape and Enter both dismiss") both already need.
                if (buttons.Count == 1) { control.IsDefault = true; control.IsCancel = true; }
                control.Click += (_, _) =>
                {
                    button.Perform();
                    window.Close();
                };
                buttonRow.Children.Add(control);
            }
            var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };
            content.Children.Add(new TextBlock
            {
                Text = informativeText,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 360,
            });
            content.Children.Add(buttonRow);
            window.Content = content;
            window.Closed += (_, _) => completion.TrySetResult();
            window.Show();
        });
        return completion.Task;
    }
}
