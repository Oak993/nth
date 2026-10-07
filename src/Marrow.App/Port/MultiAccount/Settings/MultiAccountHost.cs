// PORT-ONLY: T11 step 2. A page part that needs the multi-account context builds itself when it is first attached: the Settings window makes its pages before the window has the running AppModel (the pages find it from the tree), while a test
// supplies the context through the seam. If there is no context (no AppModel) the part stays empty.
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class MultiAccountHost<T> : Decorator where T : Control
{
    private readonly Func<MultiAccountContext, T> build;

    internal MultiAccountHost(Func<MultiAccountContext, T> build)
    {
        this.build = build;
        AttachedToVisualTree += (_, _) => Build();
    }

    /// <summary>The part, once built.</summary>
    internal T? View { get; private set; }

    private void Build()
    {
        if (View is not null) return;
        if (MultiAccountSettingsSeams.For(this) is not { } context) return;
        View = build(context);
        Child = View;
    }
}
