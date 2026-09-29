using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// An integer twin of <see cref="ObservableBoolBox"/>: EffortSlider's index is bound to the thread's effort through the
/// model in the app, so a test that needs the slider to react to a write backs it the same way.
/// </summary>
public sealed class ObservableIntBox : MainActorModel
{
    private int held;

    public int Value { get => Read(held); set => Set(ref held, value); }

    public Binding<int> AsBinding() => new(() => Value, v => Value = v);
}
