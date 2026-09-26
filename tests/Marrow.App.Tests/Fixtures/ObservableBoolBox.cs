using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A plain closure-backed <see cref="Binding{T}"/> (<c>new Binding&lt;bool&gt;(() => local, v => local = v)</c>)
/// never notifies ObservationTracking on write, so a control's <c>Track(() => binding.Value, ...)</c> would never
/// re-run in a test; in the app every such binding is backed by a MainActorModel property (thread.fastMode
/// through AppModel, a settings flag, etc.), so tests that need a bound value to react to a write, rather than
/// just read it once, back it the same way here instead of a raw local variable.
/// </summary>
public sealed class ObservableBoolBox : MainActorModel
{
    private bool held;

    public bool Value { get => Read(held); set => Set(ref held, value); }

    public Binding<bool> AsBinding() => new(() => Value, v => Value = v);
}
