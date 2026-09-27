using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Marrow.UI.Port;
using Marrow.Core.Port;

namespace Marrow.App.Port;

// PORT-ONLY: the presentation seam behind a text-field NSAlert, first needed by ChatView's "Rename thread"
// alert (ChatView.swift:1231-1236) and reused by BranchMenu's "New branch" alert (ChatView.swift:1381-1385).
// The production presentation also goes through R-VIEW-9's common Popup seam. The task completes only on
// dismissal. Native alert bezel/layout metrics are not specified by the Swift; do not invent them (STOP #5).

/// <summary>One alert button: its title, whether it is the cancel-role button (SwiftUI's <c>role: .cancel</c>,
/// which never reads the field), and the action a non-cancel click performs with the field's current text.</summary>
public sealed record PromptButton(string Title, bool IsCancelRole, Action<string> Perform);

public interface IPromptPresenter
{
    /// <summary>Presents a modal alert holding one text field seeded with <paramref name="initialText"/>; the
    /// returned Task completes when the alert closes.</summary>
    Task Show(string title, string placeholder, string initialText, IReadOnlyList<PromptButton> buttons);
}

public static class PromptPresentation
{
    public static Ambient<IPromptPresenter> Presenter { get; } = new(() => new WindowsPromptPresenter());
}

/// <summary>The production presenter: the title, text field and role-bearing buttons through the common seam.</summary>
public sealed class WindowsPromptPresenter : IPromptPresenter
{
    public Task Show(string title, string placeholder, string initialText, IReadOnlyList<PromptButton> buttons)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MainActor.Post(() =>
        {
            ChatSheetPresentation.Show(null, dismiss =>
            {
                var field = new TextBox { Text = initialText, PlaceholderText = placeholder };
                ChatDialogControls.TextEditor(field, style: "NativeAlertTextField");
                var buttonRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                foreach (var button in buttons)
                {
                    var control = new Button { Content = button.Title, IsCancel = button.IsCancelRole };
                    ChatDialogControls.Plain(control);
                    control.Click += (_, _) =>
                    {
                        if (!button.IsCancelRole) button.Perform(field.Text ?? "");
                        dismiss();
                    };
                    buttonRow.Children.Add(control);
                }
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = title });
                content.Children.Add(field);
                content.Children.Add(buttonRow);
                content.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape && buttons.Any(button => button.IsCancelRole)) { dismiss(); e.Handled = true; }
                };
                return content;
            }, onDismiss: () => completion.TrySetResult());
        });
        return completion.Task;
    }
}
