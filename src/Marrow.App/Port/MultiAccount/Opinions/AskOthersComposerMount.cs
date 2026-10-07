// PORT-ONLY: MA-3b-6 one shared-file seam. Keeps T16 controls intact; W binds send and per-message note association.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Runtime;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal static class AskOthersComposerMount
{
    internal static AskOthersView? Install(Decorator textColumn, ThreadRuntime runtime)
    {
        if (AskOthersComposerSeams.Offer.Current(runtime.ThreadID) is not { } model) return null;
        var view = new AskOthersView(model) { VerticalAlignment = VerticalAlignment.Bottom };
        if (textColumn.Child is not StackPanel stack) return null;
        stack.Children.Insert(0, view.Chips);
        textColumn.Child = null;
        var field = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        field.Children.Add(view); Grid.SetColumn(stack, 1); field.Children.Add(stack);
        textColumn.Child = field;
        return view;
    }
}
