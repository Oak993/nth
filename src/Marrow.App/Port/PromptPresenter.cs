using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Port;

namespace Marrow.App.Port;

// PORT-ONLY: the presentation seam behind a text-field NSAlert, first needed by ChatView's "Rename thread"
// alert (ChatView.swift:1231-1236) and reused by BranchMenu's "New branch" alert (ChatView.swift:1381-1385).
// Mirrors Port/AlertPresenter.cs's ambient seam (its own comment explains why): production shows a real modal
// window and the returned Task does not complete until it closes; a test overrides the Ambient, because
// Avalonia 12.1.2 headless cannot open a real Window.

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

/// <summary>The production presenter: a plain modal Avalonia window with a text field and its buttons.</summary>
public sealed class WindowsPromptPresenter : IPromptPresenter
{
    public Task Show(string title, string placeholder, string initialText, IReadOnlyList<PromptButton> buttons)
    {
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
            var field = new TextBox { Text = initialText, PlaceholderText = placeholder, Width = 280 };
            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            foreach (var button in buttons)
            {
                var control = new Button { Content = button.Title };
                control.Click += (_, _) =>
                {
                    if (!button.IsCancelRole) button.Perform(field.Text ?? "");
                    window.Close();
                };
                buttonRow.Children.Add(control);
            }
            var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };
            content.Children.Add(field);
            content.Children.Add(buttonRow);
            window.Content = content;
            window.Closed += (_, _) => completion.TrySetResult();
            window.Show();
        });
        return completion.Task;
    }
}
