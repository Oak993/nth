// PORT-ONLY: MA-3b-6 one shared-file seam. Keeps T16 controls intact; W binds send and per-message note association.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Runtime;
using Marrow.App.Port.MultiAccount.Thread;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Core.Models;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal static class AskOthersComposerMount
{
    internal static AskOthersView? Install(Decorator textColumn, ThreadRuntime runtime)
    {
        if (textColumn.Child is not StackPanel stack) return null;
        var model = AskOthersComposerSeams.Offer.Current(runtime.ThreadID);
        if (model is null)
        {
            var ports = ThreadPortSeams.Offer.Current;
            if (runtime.Thread is not { } thread || ports.Opinions is not { } opinions || ports.Account(runtime.ThreadID, thread.Provider) is not { } current) return null;
            var accounts = SwiftEnum<ProviderKind>.AllCases.Select(service => ports.Account(runtime.ThreadID, service)).OfType<Marrow.Core.Port.MultiAccount.Thread.IThreadAccountPort>().Distinct().ToArray();
            model = new AskOthersModel(runtime.ThreadID, current, accounts, opinions, new OtherServiceConsentStore(UserDefaults.Standard.Current), service => runtime.App is { } app ? app.Providers.Models(service) : Array.Empty<ModelOption>());
        }
        var view = new AskOthersView(model) { VerticalAlignment = VerticalAlignment.Bottom };
        var lifetime = model; view.AttachedToVisualTree += (_, _) => lifetime.Resume(); view.DetachedFromVisualTree += (_, _) => lifetime.Dispose();
        stack.Children.Insert(0, view.Chips);
        textColumn.Child = null;
        var field = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        field.Children.Add(view); Grid.SetColumn(stack, 1); field.Children.Add(stack);
        textColumn.Child = field;
        return view;
    }
}
