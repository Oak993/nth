// PORT-ONLY: OD-0120. Disclosure verbatim from page :4056; unchecked checkbox. Cancel/Ask others/Link action words are ED-480.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.Skills.Pages;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OtherServiceConfirmation : Decorator
{
    internal ConsentCheckBox DontAskAgain { get; }
    internal OtherServiceConfirmation(string text, string accept, Action<bool> remember, Action<bool> answer)
    {
        Width = 300;
        DontAskAgain = new("Don't ask again");
        DontAskAgain.IsCheckedChanged += (_, _) => remember(DontAskAgain.IsChecked == true);
        var cancel = PlainControls.Button(LibraryUi.Text("Cancel", 12), () => answer(false)); cancel.Focusable = true; cancel.Named("Cancel", "other-service-cancel");
        var send = PlainControls.Button(LibraryUi.Text(accept, 12), () => answer(true)); send.Focusable = true; send.Named(accept, "other-service-accept");
        Child = new Border { Padding = new Thickness(12), Child = new StackPanel { Spacing = 10, Children =
        {
            LibraryUi.Text(text, 12.5, wrap: true), DontAskAgain,
            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { cancel, send } },
        } } };
        KeyDown += (_, e) => { if (e.Key != Key.Escape) return; answer(false); e.Handled = true; };
    }
}
