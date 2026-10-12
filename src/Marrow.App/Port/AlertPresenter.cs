using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port;

// PORT-ONLY: ED-216's shared SwiftUI alert/confirmationDialog and NSAlert.runModal seam.
// Source sites: AppUpdater.swift:274-280, TimelineRows.swift:191-196, SettingsView.swift:920-934,
// RootView.swift:80-91, ChatView.swift:1231-1237, :1381-1385 (B3 52bc792).
// The Windows ordering/default/sheet appearance remain UNVERIFIED under OD-0001.

/// <summary>One alert button: its title and the action a click performs before the alert closes.</summary>
public sealed record AlertButton(string Title, Action Perform)
{
    public AlertButtonRole Role { get; init; }
}

// ED-216: preserve the updater's two-argument AlertButton constructor and old Show overload.
public enum AlertButtonRole { None, Cancel, Destructive }
public enum AlertKind { Alert, ConfirmationDialog }
public sealed record AlertTextField(string Label, Binding<string> Value);
public sealed record AlertRequest(string Title, string? Message, IReadOnlyList<AlertButton> Buttons,
    AlertKind Kind = AlertKind.Alert, IReadOnlyList<AlertTextField>? TextFields = null, Avalonia.Controls.Window? Owner = null)
{
    public AlertButton? ReturnDefault => Buttons.FirstOrDefault(button => button.Role == AlertButtonRole.None)
        ?? (Buttons.Count == 1 && Buttons[0].Role == AlertButtonRole.Cancel ? Buttons[0] : null);
}

internal sealed class AlertSession(AlertRequest request, Action close)
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool responded;
    private CancellationTokenRegistration lifetime;
    public Task Completion => completion.Task;
    public bool HasResponded => responded;
    public void BindLifetime(CancellationToken token) => lifetime = token.Register(() => MainActor.Post(() => Cancel()));
    public void Press(string title) => Press(request.Buttons.First(button => button.Title == title));
    public void Press(AlertButton button)
    {
        if (responded || completion.Task.IsCompleted) return;
        responded = true;
        try { button.Perform(); }
        finally { close(); }
    }
    public void Cancel(bool closeWindow = true)
    {
        if (responded || completion.Task.IsCompleted) return;
        responded = true;
        try { request.Buttons.FirstOrDefault(button => button.Role == AlertButtonRole.Cancel)?.Perform(); }
        finally { if (closeWindow) close(); }
    }
    public void Closed() { lifetime.Dispose(); completion.TrySetResult(); }
    public void Failed(Exception error) { lifetime.Dispose(); completion.TrySetException(error); }
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

    // A default bridge preserves existing presenter overrides implementing the updater's original API.
    Task Show(AlertRequest request) => Show(request.Title, request.Message ?? "", AlertStyle.Warning, request.Buttons);
}

public static class AlertPresentation
{
    public static Ambient<IAlertPresenter> Presenter { get; } = new(() => new WindowsAlertPresenter());
    public static Task Show(AlertRequest request) => Presenter.Current.Show(Prepare(request));

    internal static AlertRequest Prepare(AlertRequest request)
    {
        var buttons = request.Buttons.ToList();
        if (request.Kind == AlertKind.ConfirmationDialog && buttons.All(button => button.Role != AlertButtonRole.Cancel))
            buttons.Add(new AlertButton("Cancel", () => { }) { Role = AlertButtonRole.Cancel });
        else if (request.Kind == AlertKind.Alert && buttons.Count == 0)
            buttons.Add(new AlertButton("OK", () => { }));
        return request with { Buttons = buttons.OrderBy(button => button.Role == AlertButtonRole.Cancel).ToArray() };
    }
}

/// <summary>The production presenter: a plain modal Avalonia window with the message and its buttons.</summary>
public sealed class WindowsAlertPresenter : IAlertPresenter
{
    internal static Avalonia.Controls.Window BuildWindow(AlertRequest request, AlertSession session)
    {
        var window = new Avalonia.Controls.Window
        {
            Title = request.Title, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };
        content.Children.Add(new TextBlock { Text = request.Title, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        if (request.Message is { } message)
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        foreach (var field in request.TextFields ?? []) content.Children.Add(PlainControls.TextField(field.Label, field.Value));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in request.Buttons)
        {
            var label = new TextBlock { Text = button.Title };
            if (button.Role == AlertButtonRole.Destructive) label.Foreground = Chrome.Danger.Brush(label);
            var control = new Button { Content = label, IsDefault = ReferenceEquals(button, request.ReturnDefault), IsCancel = button.Role == AlertButtonRole.Cancel };
            control.Click += (_, _) => session.Press(button);
            row.Children.Add(control);
        }
        content.Children.Add(row);
        window.Content = content;
        window.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { session.Cancel(); args.Handled = true; }
            else if (args.Key == Key.Enter && request.ReturnDefault is { } button) { session.Press(button); args.Handled = true; }
        };
        window.Closing += (_, _) => session.Cancel(closeWindow: false);
        window.Closed += (_, _) => session.Closed();
        return window;
    }

    public Task Show(string title, string informativeText, AlertStyle style, IReadOnlyList<AlertButton> buttons)
    {
        _ = style; // The one style this app shows (Warning) carries no distinct visual treatment yet.
        return Show(new AlertRequest(title, informativeText, buttons));
    }

    public Task Show(AlertRequest request)
    {
        request = AlertPresentation.Prepare(request);
        Avalonia.Controls.Window? window = null;
        var session = new AlertSession(request, () => window?.Close());
        session.BindLifetime(SwiftTask.CancellationToken);
        MainActor.Post(() =>
        {
            try
            {
                if (session.HasResponded) { session.Closed(); return; }
                var desktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                var owner = request.Owner ?? desktop?.Windows.FirstOrDefault(candidate => candidate.IsActive) ?? desktop?.MainWindow
                    ?? throw new InvalidOperationException("A modal alert requires an owner window.");
                window = BuildWindow(request, session);
                // ED-NEW-textsize-1: drawn at the app zoom of the window it is asked over.
                if (window.Content is Control content) window.Content = Marrow.UI.Common.AppZoom.Zoomed(content, Marrow.UI.Common.AppZoom.ScaleOfWindow(owner));
                _ = window.ShowDialog(owner);
            }
            catch (Exception error) { session.Failed(error); }
        });
        return session.Completion;
    }
}
