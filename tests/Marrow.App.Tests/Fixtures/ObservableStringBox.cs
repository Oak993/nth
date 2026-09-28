using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A string-valued twin of <see cref="ObservableBoolBox"/>: a closure-backed
/// <c>Binding</c> over a raw local never notifies, so card tests that need an
/// effort change to re-run both the card's and the slider's Track back it here.
/// </summary>
public sealed class ObservableStringBox : MainActorModel
{
    private string? held;

    public string? Value { get => Read(held); set => Set(ref held, value); }

    public Binding<string?> AsBinding() => new(() => Value, v => Value = v);
}
